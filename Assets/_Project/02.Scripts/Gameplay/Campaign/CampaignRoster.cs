using System.Collections.Generic;
using SRPG.Data;
using UnityEngine;

namespace SRPG.Gameplay.Campaign
{
    /// <summary>
    /// 캠페인이 거느린 부대의 <b>장부</b>입니다. 전투보다 오래 삽니다.
    ///
    /// <b>여기가 부대의 유일한 출처입니다</b>
    ///
    /// 전투는 부대를 만들어 내지 않습니다. 이 장부에 적힌 것을 주문서로 옮겨 세우고,
    /// 판이 끝나면 보고서를 받아 이 장부를 고칩니다.
    /// 그래서 지난 전투에서 다친 부대가 다음 전장에 다친 채로 섭니다 —
    /// 그것이 없으면 매 전투가 언제나 처음부터입니다.
    ///
    /// <b>왜 전투가 이것을 직접 보지 않는가</b>
    ///
    /// 전투는 <see cref="BattleRequest"/> 만 받습니다. 장부를 통째로 넘기면
    /// 전투가 캠페인의 자료 구조를 알게 되고, 그 순간 전투를 캠페인 없이 열 수 없게 됩니다.
    /// 지금 자동 검사가 전투만 따로 여는 것이 가능한 이유가 그 경계입니다.
    /// </summary>
    public sealed class CampaignRoster
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>거느린 분대입니다. 순서가 곧 투입 순서입니다.</summary>
        private readonly List<CampaignSquad> _squads = new List<CampaignSquad>(8);

        /// <summary>다음 분대에 붙일 식별자입니다. 한 번 쓴 번호는 다시 쓰지 않습니다.</summary>
        private int _nextId = 1;

        /// <summary>전투 성과를 성장으로 옮기는 규칙입니다. 절대 null이 아닙니다.</summary>
        private readonly CampaignProgression _progression;

        // ====================================================================================================
        // 1-1. Constructor
        // ====================================================================================================

        /// <param name="progression">
        /// 성장 규칙입니다. 비우면 기본 곡선을 씁니다 —
        /// 규칙 없이 장부만 만드는 경로(검사)가 성장까지 신경 쓰지 않아도 되게 합니다.
        /// </param>
        public CampaignRoster(CampaignProgression progression = null)
        {
            _progression = progression ?? new CampaignProgression();
        }

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>거느린 분대입니다.</summary>
        public IReadOnlyList<CampaignSquad> Squads => _squads;

        /// <summary>아직 남아 있는 분대 수입니다.</summary>
        public int LivingSquadCount
        {
            get
            {
                int count = 0;

                for (int i = 0; i < _squads.Count; i++)
                {
                    if (_squads[i].IsAlive)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        // ====================================================================================================
        // 3. Public Methods - Roster
        // ====================================================================================================

        /// <summary>분대를 장부에 올립니다. 식별자는 장부가 붙입니다.</summary>
        /// <param name="definition">병과입니다.</param>
        /// <param name="soldierCount">지휘관을 제외한 인원입니다.</param>
        /// <param name="displayName">표시할 이름입니다. 비우면 병과 이름을 씁니다.</param>
        /// <param name="proficiency">시작 숙련도입니다. 비우면 미숙 상태로 시작합니다.</param>
        /// <returns>장부에 오른 분대입니다.</returns>
        public CampaignSquad Enlist(
            UnitDefinition definition,
            int soldierCount,
            string displayName = null,
            WeaponProficiency proficiency = default)
        {
            var squad = new CampaignSquad
            {
                Id = _nextId++,
                Definition = definition,
                SoldierCount = soldierCount,
                MaxSoldiers = soldierCount,
                DisplayName = displayName,
                Proficiency = proficiency,
            };

            _squads.Add(squad);

            return squad;
        }

        /// <summary>식별자로 분대를 찾습니다.</summary>
        /// <param name="id">찾을 분대의 식별자입니다.</param>
        /// <returns>찾은 분대입니다. 없으면 null입니다.</returns>
        public CampaignSquad Find(int id)
        {
            for (int i = 0; i < _squads.Count; i++)
            {
                if (_squads[i].Id == id)
                {
                    return _squads[i];
                }
            }

            return null;
        }

        // ====================================================================================================
        // 4. Public Methods - Battle Boundary
        // ====================================================================================================

        /// <summary>
        /// 장부에서 <b>고른 부대만</b> 골라 이번 전투의 주문서를 씁니다.
        ///
        /// <b>고르는 일은 여기서 하지 않습니다.</b> 누구를 데려갈지는 이번 회차의 의도이고
        /// (<c>DeploymentPlan</c>), 장부는 그 결정을 받아 옮겨 적을 뿐입니다.
        /// 장부가 스스로 고르기 시작하면 "왜 이 분대가 나갔는가"의 답이 두 곳으로 갈립니다.
        ///
        /// <b>살아 있는 분대만 데려갑니다.</b> 무너진 분대를 인원 0으로 내보내면
        /// 전장에 지휘관만 선 유령 분대가 생깁니다.
        ///
        /// <b>순서는 고른 순서를 따릅니다.</b> 전개기가 앞에서부터 자리를 채우므로,
        /// 이 순서가 곧 전장에 먼저 서는 순서가 됩니다.
        /// </summary>
        /// <param name="deployedIds">
        /// 데리고 나갈 분대의 식별자입니다. 비어 있으면 아군 없이 주문서가 나갑니다 —
        /// 그것을 막는 것은 부르는 쪽의 일입니다(<c>CampaignDirector.MoveTo</c>).
        /// 여기서 전부 데려가는 것으로 대신하면, 편성이 비어 있다는 사실이 조용히 덮입니다.
        /// </param>
        /// <param name="enemySquads">상대가 데리고 나온 분대입니다.</param>
        /// <param name="battlefield">어디서 싸우는지입니다.</param>
        /// <param name="seed">지형과 배치를 재현하는 시드입니다.</param>
        /// <returns>전투에 넘길 주문서입니다.</returns>
        public BattleRequest BuildRequest(
            IReadOnlyList<int> deployedIds,
            IReadOnlyList<SquadOrder> enemySquads,
            BattlefieldSpec battlefield,
            int seed)
        {
            var request = new BattleRequest
            {
                Seed = seed,
                Battlefield = battlefield,
            };

            if (deployedIds != null)
            {
                for (int i = 0; i < deployedIds.Count; i++)
                {
                    var squad = Find(deployedIds[i]);

                    if (squad != null && squad.IsAlive)
                    {
                        request.PlayerSquads.Add(squad.ToOrder());
                    }
                }
            }

            if (enemySquads != null)
            {
                for (int i = 0; i < enemySquads.Count; i++)
                {
                    request.EnemySquads.Add(enemySquads[i]);
                }
            }

            return request;
        }

        /// <summary>
        /// 전투가 돌려준 보고를 장부에 반영합니다.
        ///
        /// 무너진 분대는 장부에서 지웁니다. 남은 분대는 인원만 줄어듭니다.
        ///
        /// <b>보고에 없는 분대는 건드리지 않습니다.</b>
        /// 이번 전투에 나가지 않은 부대가 있을 수 있고, 그들이 손실을 볼 이유는 없습니다.
        /// </summary>
        /// <param name="result">전투가 남긴 보고서입니다. null이면 아무것도 하지 않습니다.</param>
        /// <param name="board">
        /// 승급을 올려 둘 게시판입니다. null이면 승급은 그대로 일어나되 특전을 묻지 않습니다 —
        /// 진행 규칙만 확인하는 검사가 그 경로로 옵니다.
        /// </param>
        /// <param name="seed">
        /// 특전 선택지를 뽑을 씨앗입니다. 같은 전투는 같은 선택지를 냅니다.
        /// 분대 식별자와 승급 후 단련도를 함께 섞으므로, 같은 판에서 둘이 승급해도 서로 다른 목록이 나옵니다.
        /// </param>
        public void ApplyResult(BattleResult result, PromotionBoard board = null, int seed = 0)
        {
            if (result == null)
            {
                return;
            }

            for (int i = 0; i < result.Squads.Count; i++)
            {
                var report = result.Squads[i];
                var squad = Find(report.Id);

                if (squad == null)
                {
                    // 전투가 캠페인이 모르는 식별자를 돌려주었습니다. 조용히 넘기면
                    // "손실이 반영되지 않는다"는 증상만 남고 원인은 보이지 않습니다.
                    Debug.LogWarning($"[CampaignRoster] 보고서의 {report.Id}번 분대가 장부에 없습니다.");
                    continue;
                }

                squad.ApplyReport(report);

                // 승급을 알아보려면 <b>올리기 전의</b> 단련도를 들고 있어야 합니다.
                int rankBefore = squad.Rank;

                // 손실을 반영한 <b>뒤에</b> 성장을 얹습니다.
                // 순서가 뒤바뀌면 무너진 분대가 한 번 성장하고 사라집니다.
                _progression.Apply(squad, report);

                // 무너진 분대는 승급하지 않습니다 — 위에서 성장 자체가 걸러지므로
                // 여기 오는 것은 살아 돌아온 분대뿐이지만, 사라질 분대에게 특전을 묻지 않도록 한 번 더 봅니다.
                if (board != null && squad.IsAlive && squad.Rank > rankBefore)
                {
                    board.Enqueue(squad, rankBefore, seed * 7919 + squad.Id * 31 + squad.Rank);
                }
            }

            _squads.RemoveAll(squad => !squad.IsAlive);
        }
    }
}
