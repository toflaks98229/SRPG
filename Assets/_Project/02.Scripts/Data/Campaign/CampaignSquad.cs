using System.Collections.Generic;
using SRPG.Common;
using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 월드맵에 <b>기록되어 있는</b> 분대 하나입니다.
    ///
    /// <b>이것이 전장에 서는 부대의 유일한 출처입니다</b>
    ///
    /// 전투는 분대를 만들어 내지 않습니다. 여기 적힌 것을 <see cref="SquadOrder"/> 로 옮겨 세우고,
    /// 판이 끝나면 <see cref="SquadReport"/> 를 받아 여기 인원을 깎습니다.
    /// 그래서 지난 전투에서 다친 부대가 다음 전장에 그대로 다친 채로 섭니다.
    ///
    /// <b>병사는 세지만 이름은 없습니다</b>
    ///
    /// 여기에 병사 목록이 없고 <see cref="SoldierCount"/> 하나만 있는 것은
    /// <see cref="SquadOrder"/> 와 같은 이유입니다 — 식별과 숙련과 보고를 전부 분대가 집니다.
    /// 앞으로 붙을 병과·랭크·무기 숙련도도 이 분대의 것입니다.
    /// 병사 개개인에게 그것을 매달면 전투마다 수십 명의 상태를 오가며 저장해야 합니다.
    /// </summary>
    [System.Serializable]
    public sealed class CampaignSquad
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>
        /// 캠페인이 이 분대에 붙인 식별자입니다.
        ///
        /// 전투는 이 값을 해석하지 않고 보고서에 그대로 돌려줍니다.
        /// 그 덕분에 전투는 캠페인의 자료 구조를 전혀 몰라도 됩니다.
        /// </summary>
        public int Id;

        /// <summary>병과입니다.</summary>
        public UnitDefinition Definition;

        /// <summary>표시할 이름입니다. 비우면 병과 이름을 씁니다.</summary>
        public string DisplayName;

        /// <summary>지휘관을 제외한 현재 인원입니다.</summary>
        public int SoldierCount;

        /// <summary>이 분대가 채울 수 있는 최대 인원입니다. 보충의 상한입니다.</summary>
        public int MaxSoldiers = 5;

        /// <summary>분대 단련도입니다. 부대 전체의 체력·피해·공속·이동이 여기서 옵니다.</summary>
        public int Rank = CombatConstants.MinRank;

        /// <summary>
        /// 무기 계열별 숙련도입니다. 이 분대가 쓰는 동작만 실제로 걸립니다.
        ///
        /// 나머지 계열도 함께 들고 있는 이유는, 무기를 바꿔 들었을 때
        /// 예전에 쌓은 것이 사라지지 않게 하기 위해서입니다 —
        /// 활을 놓고 창을 잡았다가 다시 활을 들면 그 손이 기억하고 있어야 합니다.
        /// </summary>
        public WeaponProficiency Proficiency;

        /// <summary>
        /// 이 분대가 살아 돌아온 전투 수입니다. 단련도가 여기서 나옵니다.
        ///
        /// <b>전과가 아니라 횟수입니다.</b>
        /// 잘 싸웠는가는 무기 숙련도가 재고, 단련은 '얼마나 겪었는가'입니다.
        /// 그래서 후방에서 한 대도 못 때린 분대도 살아 돌아오면 조금씩 단단해집니다.
        /// </summary>
        public int BattlesSurvived;

        /// <summary>
        /// 지금까지 쌓은 공적입니다. 이것이 문턱을 넘을 때 승급합니다.
        ///
        /// 깎이지 않습니다 — 이유는 <see cref="CampaignProgression.ResolveRank"/> 에 적어 두었습니다.
        /// </summary>
        public float Merit;

        /// <summary>
        /// 승급할 때 고른 특전입니다.
        ///
        /// <b>장비는 여기 없습니다.</b> 무기와 방패는 사서 드는 것이고 그쪽은 상점의 축입니다
        /// (<see cref="SquadPerks"/>).
        /// </summary>
        public List<SquadPerkKind> Perks = new List<SquadPerkKind>();

        /// <summary>
        /// 이 분대가 무너졌는지 여부입니다.
        ///
        /// <b>인원 0과 같은 말이 아닙니다.</b>
        /// 이 게임에서는 지휘관이 쓰러지면 남은 병사가 있어도 분대가 무너집니다.
        /// 반대로 병사를 다 잃어도 지휘관이 살아 돌아왔으면 분대는 남고, 보충하면 다시 섭니다.
        /// 그래서 인원으로 생사를 유추하지 않고 따로 적습니다.
        /// </summary>
        public bool Disbanded;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>아직 장부에 남아 있는 분대인지 여부입니다.</summary>
        public bool IsAlive => Definition != null && !Disbanded;

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>이 분대를 이번 전투의 지시로 옮깁니다.</summary>
        /// <returns>전투가 그대로 세울 수 있는 지시입니다.</returns>
        public SquadOrder ToOrder()
        {
            return new SquadOrder
            {
                Id = Id,
                Definition = Definition,
                SoldierCount = Mathf.Max(0, SoldierCount),
                Rank = Mathf.Clamp(Rank, CombatConstants.MinRank, CombatConstants.MaxRank),
                Proficiency = Proficiency,
                DisplayName = ResolveName(),

                // 특전은 배율로 눌러 보냅니다. 전장은 "어떤 특전인가"를 알 이유가 없고,
                // 알게 두면 전투 코드에 특전별 분기가 생깁니다.
                Perks = SquadPerks.Combine(Perks),
            };
        }

        /// <summary>
        /// 전투가 돌려준 보고를 반영합니다.
        ///
        /// <b>생존자에서 지휘관을 뺍니다.</b>
        /// 보고서의 인원은 지휘관을 포함하지만 이 분대의 <see cref="SoldierCount"/> 는 그렇지 않습니다.
        /// 이 한 명을 빼먹으면 전투를 치를 때마다 부대가 한 명씩 불어납니다.
        /// </summary>
        /// <param name="report">이 분대에 대한 전황 보고입니다.</param>
        public void ApplyReport(SquadReport report)
        {
            if (report.Destroyed)
            {
                Disbanded = true;
                SoldierCount = 0;
                return;
            }

            SoldierCount = Mathf.Clamp(report.Survivors - 1, 0, MaxSoldiers);
        }

        /// <summary>표시할 이름을 정합니다.</summary>
        /// <returns>지정된 이름입니다. 비어 있으면 병과 이름이고, 그것도 없으면 "분대"입니다.</returns>
        public string ResolveName()
        {
            if (!string.IsNullOrEmpty(DisplayName))
            {
                return DisplayName;
            }

            return Definition != null ? Definition.DisplayName : "분대";
        }
    }
}
