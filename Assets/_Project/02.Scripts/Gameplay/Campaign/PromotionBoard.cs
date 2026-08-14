using System.Collections.Generic;
using SRPG.Data;

namespace SRPG.Gameplay.Campaign
{
    /// <summary>
    /// 승급한 분대 하나와, 그 분대가 고를 수 있는 특전입니다.
    /// </summary>
    public sealed class PendingPromotion
    {
        /// <summary>승급한 분대의 식별자입니다.</summary>
        public int SquadId { get; }

        /// <summary>승급 전 단련도입니다.</summary>
        public int FromRank { get; }

        /// <summary>승급 후 단련도입니다.</summary>
        public int ToRank { get; }

        /// <summary>고를 수 있는 특전입니다. 비어 있으면 더 고를 것이 없다는 뜻입니다.</summary>
        public IReadOnlyList<SquadPerkKind> Offer { get; }

        /// <param name="squadId">승급한 분대의 식별자입니다.</param>
        /// <param name="fromRank">승급 전 단련도입니다.</param>
        /// <param name="toRank">승급 후 단련도입니다.</param>
        /// <param name="offer">고를 수 있는 특전입니다.</param>
        public PendingPromotion(int squadId, int fromRank, int toRank, IReadOnlyList<SquadPerkKind> offer)
        {
            SquadId = squadId;
            FromRank = fromRank;
            ToRank = toRank;
            Offer = offer;
        }
    }

    /// <summary>
    /// 전투가 끝난 뒤 <b>아직 고르지 않은 승급</b>을 쌓아 두는 자리입니다.
    ///
    /// <b>왜 따로 두는가</b>
    ///
    /// 승급 자체는 장부가 즉시 처리합니다 — 단련도는 공적이 정하는 것이라 물어볼 것이 없습니다.
    /// 그런데 <b>특전은 플레이어가 골라야 합니다.</b> 고르는 동안 캠페인은 멈춰 있어야 하고,
    /// 화면은 무엇을 물어야 하는지 알아야 합니다.
    ///
    /// 그 "아직 답하지 않은 질문"을 진행 규칙 안에 두면, <see cref="CampaignDirector"/> 가
    /// 화면의 사정을 알게 됩니다. 여기 따로 두면 규칙은 질문을 <b>쌓기만</b> 하고,
    /// 화면은 쌓인 것을 <b>비우기만</b> 합니다. 서로를 몰라도 됩니다.
    ///
    /// <b>선택지는 한 번만 뽑습니다.</b> 씨앗을 승급마다 고정해 두므로 화면을 다시 그려도
    /// 목록이 바뀌지 않습니다 — 그러지 않으면 마음에 들 때까지 다시 여는 놀이가 됩니다.
    /// </summary>
    public sealed class PromotionBoard
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>아직 특전을 고르지 않은 승급입니다. 먼저 들어온 것부터 답합니다.</summary>
        private readonly List<PendingPromotion> _pending = new List<PendingPromotion>(4);

        /// <summary>선택지를 뽑을 때 쓰는 재사용 버퍼입니다.</summary>
        private readonly List<SquadPerkKind> _offerBuffer = new List<SquadPerkKind>(SquadPerks.OfferSize);

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>답을 기다리는 승급이 있는지입니다. 있으면 월드맵이 이동을 멈춥니다.</summary>
        public bool HasPending => _pending.Count > 0;

        /// <summary>지금 물어야 할 승급입니다. 없으면 null입니다.</summary>
        public PendingPromotion Current => _pending.Count > 0 ? _pending[0] : null;

        /// <summary>답을 기다리는 승급 수입니다.</summary>
        public int PendingCount => _pending.Count;

        /// <summary>
        /// 이 분대가 답을 기다리고 있는지입니다.
        ///
        /// 화면이 명부에서 그 분대를 짚어 주기 위한 것입니다 — 패널이 이름을 적어 주더라도
        /// <b>목록의 어느 줄인지</b>가 함께 보여야 승급이 그 부대의 일로 읽힙니다.
        /// </summary>
        /// <param name="squadId">볼 분대의 식별자입니다.</param>
        /// <returns>답을 기다리고 있으면 true입니다.</returns>
        public bool IsPending(int squadId)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].SquadId == squadId)
                {
                    return true;
                }
            }

            return false;
        }

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 승급 하나를 올려 둡니다.
        /// </summary>
        /// <param name="squad">승급한 분대입니다.</param>
        /// <param name="fromRank">승급 전 단련도입니다.</param>
        /// <param name="seed">선택지를 뽑을 씨앗입니다. 같은 값은 같은 목록을 냅니다.</param>
        public void Enqueue(CampaignSquad squad, int fromRank, int seed)
        {
            if (squad == null)
            {
                return;
            }

            SquadPerks.BuildOffer(squad.Perks, seed, _offerBuffer);

            // 고를 것이 없으면 물을 것도 없습니다. 특전을 전부 가진 분대가 여기로 옵니다.
            if (_offerBuffer.Count == 0)
            {
                return;
            }

            _pending.Add(new PendingPromotion(
                squad.Id, fromRank, squad.Rank, _offerBuffer.ToArray()));
        }

        /// <summary>
        /// 지금 묻고 있는 승급에 답합니다.
        /// </summary>
        /// <param name="roster">분대를 찾을 장부입니다.</param>
        /// <param name="perk">고른 특전입니다. 제안에 없는 것이면 거절합니다.</param>
        /// <returns>실제로 반영했으면 true입니다.</returns>
        public bool Choose(CampaignRoster roster, SquadPerkKind perk)
        {
            var pending = Current;

            if (pending == null || roster == null)
            {
                return false;
            }

            // 제안에 없던 것을 받으면 안 됩니다. 화면이 잘못 그려져도 규칙이 무너지지 않아야 합니다.
            bool offered = false;

            for (int i = 0; i < pending.Offer.Count; i++)
            {
                if (pending.Offer[i] == perk)
                {
                    offered = true;
                    break;
                }
            }

            if (!offered)
            {
                return false;
            }

            var squad = roster.Find(pending.SquadId);

            // 고르는 사이에 분대가 사라졌을 수 있습니다. 그때는 질문만 치웁니다.
            if (squad != null && squad.IsAlive)
            {
                squad.Perks.Add(perk);
            }

            _pending.RemoveAt(0);
            return true;
        }

        /// <summary>쌓인 질문을 전부 버립니다. 새 회차를 시작할 때 씁니다.</summary>
        public void Clear()
        {
            _pending.Clear();
        }
    }
}
