using System.Collections.Generic;
using SRPG.Data;

namespace SRPG.Gameplay.Campaign
{
    /// <summary>
    /// 다음 전투에 <b>데리고 나갈 분대</b>를 골라 둔 것입니다.
    ///
    /// <b>왜 이것이 생겼는가</b>
    ///
    /// 예전에는 장부에 남아 있는 분대를 전부 데리고 나갔습니다. 그러면 부대가 서로 바뀌어도
    /// 아무 차이가 없습니다 — 고를 것이 없으니 아낄 것도 없고, 아낄 것이 없으니
    /// 무기 숙련도를 쌓아 둔 궁수 분대가 특별할 이유가 없습니다.
    ///
    /// 여기서 <b>고르는 단계 하나</b>를 넣습니다. 그것만으로 이미 만들어 둔 성장 체계가
    /// 처음으로 의미를 얻습니다 — "이 분대를 두고 가도 되는가"를 물을 수 있게 되기 때문입니다.
    ///
    /// <b>왜 분대에 표시하지 않는가</b>
    ///
    /// <see cref="CampaignSquad"/> 에 <c>Selected</c> 를 두는 편이 짧습니다.
    /// 그러나 그것은 <b>분대의 성질이 아니라 이번 회차의 의도</b>입니다.
    /// 분대에 매달면 장부를 읽는 모든 곳이 "지금 고른 상태"까지 함께 보게 되고,
    /// 나중에 편성을 여럿 굴리거나(다음 전투와 그다음 전투) 되돌리는 일이 어려워집니다.
    ///
    /// <b>MonoBehaviour 가 아닙니다.</b> 상한과 최소치 규칙을 화면 없이 검사합니다.
    /// </summary>
    public sealed class DeploymentPlan
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>고른 분대의 식별자입니다. 고른 순서를 유지합니다.</summary>
        private readonly List<int> _selected = new List<int>(8);

        /// <summary>이번 회차의 출진 규칙입니다. 절대 null이 아닙니다.</summary>
        private readonly CampaignTuning _tuning;

        // ====================================================================================================
        // 2. Constructor
        // ====================================================================================================

        /// <param name="tuning">출진 규칙입니다. 비우면 기본 규칙을 씁니다.</param>
        public DeploymentPlan(CampaignTuning tuning = null)
        {
            _tuning = tuning ?? new CampaignTuning();
        }

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>고른 분대의 식별자입니다. 고른 순서 그대로입니다.</summary>
        public IReadOnlyList<int> Selected => _selected;

        /// <summary>지금 고른 분대 수입니다.</summary>
        public int Count => _selected.Count;

        /// <summary>한 번에 데리고 나갈 수 있는 최대 분대 수입니다.</summary>
        public int Cap => _tuning.ResolveMarchCap();

        /// <summary>출진에 최소한 필요한 분대 수입니다.</summary>
        public int Minimum => _tuning.ResolveMinimum();

        /// <summary>
        /// 지금 이대로 출진할 수 있는지 여부입니다.
        ///
        /// <b>비어 있으면 출진할 수 없습니다.</b> 아무도 없이 전장에 들어서면
        /// 시작하자마자 패배하고, 그것은 선택이 아니라 사고입니다.
        /// </summary>
        public bool IsReady => _selected.Count >= Minimum;

        /// <summary>더 고를 자리가 남았는지 여부입니다.</summary>
        public bool HasRoom => _selected.Count < Cap;

        // ====================================================================================================
        // 4. Public Methods - Selection
        // ====================================================================================================

        /// <summary>이 분대를 골라 두었는지 봅니다.</summary>
        /// <param name="squadId">볼 분대의 식별자입니다.</param>
        /// <returns>골라 두었으면 true입니다.</returns>
        public bool IsSelected(int squadId)
        {
            return _selected.Contains(squadId);
        }

        /// <summary>
        /// 이 분대를 고릅니다. 이미 골랐거나 자리가 없으면 아무것도 하지 않습니다.
        /// </summary>
        /// <param name="squadId">고를 분대의 식별자입니다.</param>
        /// <returns>실제로 골랐으면 true입니다.</returns>
        public bool Select(int squadId)
        {
            if (IsSelected(squadId) || !HasRoom)
            {
                return false;
            }

            _selected.Add(squadId);

            return true;
        }

        /// <summary>이 분대를 뺍니다.</summary>
        /// <param name="squadId">뺄 분대의 식별자입니다.</param>
        /// <returns>실제로 뺐으면 true입니다.</returns>
        public bool Deselect(int squadId)
        {
            return _selected.Remove(squadId);
        }

        /// <summary>
        /// 골랐으면 빼고 안 골랐으면 고릅니다. 화면의 체크박스가 부르는 자리입니다.
        /// </summary>
        /// <param name="squadId">뒤집을 분대의 식별자입니다.</param>
        /// <returns>뒤집은 뒤 골라진 상태이면 true입니다.</returns>
        public bool Toggle(int squadId)
        {
            if (IsSelected(squadId))
            {
                Deselect(squadId);
                return false;
            }

            return Select(squadId);
        }

        /// <summary>고른 것을 전부 지웁니다.</summary>
        public void Clear()
        {
            _selected.Clear();
        }

        // ====================================================================================================
        // 5. Public Methods - Roster Sync
        // ====================================================================================================

        /// <summary>
        /// 장부에 없거나 무너진 분대를 골라 둔 목록에서 걷어냅니다.
        ///
        /// <b>전투가 끝날 때마다 불러야 합니다.</b> 무너진 분대는 장부에서 사라지는데
        /// 여기 식별자가 남아 있으면, 다음 출진에서 그 자리가 채워진 것으로 세어져
        /// <b>실제보다 적은 부대를 데리고 나가게</b> 됩니다. 오류는 나지 않습니다.
        /// </summary>
        /// <param name="roster">지금 장부에 남아 있는 분대입니다.</param>
        /// <returns>걷어낸 수입니다.</returns>
        public int Retain(IReadOnlyList<CampaignSquad> roster)
        {
            int removed = 0;

            for (int i = _selected.Count - 1; i >= 0; i--)
            {
                if (!IsAliveIn(roster, _selected[i]))
                {
                    _selected.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }

        /// <summary>
        /// 빈 자리를 장부 순서대로 채웁니다.
        ///
        /// <b>회차를 시작할 때만 부릅니다.</b> 전투가 끝날 때마다 채우면
        /// "이 분대는 쉬게 두겠다"는 플레이어의 결정이 매 판 되돌려집니다.
        /// 시작 시점에 채워 두는 것은 다른 이야기입니다 — 처음 열었을 때
        /// 아무도 고르지 않은 채 이동이 막혀 있으면 그것은 규칙이 아니라 고장으로 보입니다.
        /// </summary>
        /// <param name="roster">채울 후보가 담긴 장부입니다.</param>
        /// <returns>새로 채운 수입니다.</returns>
        public int Refill(IReadOnlyList<CampaignSquad> roster)
        {
            if (roster == null)
            {
                return 0;
            }

            int added = 0;

            for (int i = 0; i < roster.Count && HasRoom; i++)
            {
                var squad = roster[i];

                if (squad != null && squad.IsAlive && Select(squad.Id))
                {
                    added++;
                }
            }

            return added;
        }

        // ====================================================================================================
        // 6. Private Methods
        // ====================================================================================================

        /// <summary>이 식별자가 장부에 살아 있는지 봅니다.</summary>
        /// <param name="roster">볼 장부입니다.</param>
        /// <param name="squadId">찾을 식별자입니다.</param>
        /// <returns>살아 있으면 true입니다.</returns>
        private static bool IsAliveIn(IReadOnlyList<CampaignSquad> roster, int squadId)
        {
            if (roster == null)
            {
                return false;
            }

            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i] != null && roster[i].Id == squadId)
                {
                    return roster[i].IsAlive;
                }
            }

            return false;
        }
    }
}
