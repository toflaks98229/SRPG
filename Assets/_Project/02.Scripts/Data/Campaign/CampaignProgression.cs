using SRPG.Common;
using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 전투가 남긴 보고를 부대의 성장으로 옮기는 규칙입니다.
    ///
    /// <b>왜 전투 튜닝과 나누는가</b>
    ///
    /// <see cref="BattleTuning"/> 은 <b>한 판이 어떻게 굴러가는가</b>를 정합니다 —
    /// 얼마나 세게 밀리고, 얼마나 빨리 지원군이 오는지.
    /// 여기는 <b>판과 판 사이에 무엇이 남는가</b>입니다.
    ///
    /// 둘을 한 에셋에 두면 전투 밸런스를 만질 때마다 캠페인 길이가 함께 흔들리고,
    /// 어느 쪽을 고치려던 것이었는지가 흐려집니다.
    ///
    /// <b>왜 스크립터블 오브젝트가 아닌가</b>
    ///
    /// 캠페인 스코프에 직접 직렬화되는 편이 낫습니다. 성장 곡선은 한 벌만 있으면 되고,
    /// 에셋으로 만들면 "연결하는 것을 잊어 성장이 멈춘" 상태가 생길 수 있습니다.
    /// 필드 초기값이 그대로 기본 곡선이 되므로 비워 두어도 캠페인이 굴러갑니다.
    /// </summary>
    [System.Serializable]
    public sealed class CampaignProgression
    {
        // ====================================================================================================
        // 1. Inspector
        // ====================================================================================================

        [Header("무기 숙련도")]
        [Range(0f, 2f)]
        [Tooltip("명중 한 번마다 오르는 숙련도입니다.\n" +
                 "분대 하나가 한 판에 수십 번 맞히므로 작게 잡습니다.")]
        public float ProficiencyPerHit = 0.15f;

        [Range(0, 20)]
        [Tooltip("전투에 나갔다 살아 돌아오기만 해도 오르는 숙련도입니다.\n" +
                 "명중만으로 세면 못 맞히는 부대가 영영 늘지 않는 역설이 생깁니다 — " +
                 "빗나가면서 배우는 몫이 이것입니다.")]
        public int ProficiencyForSurviving = 3;

        [Header("무기별 보정")]
        [Range(0.1f, 4f)]
        [Tooltip("보정의 기준이 되는 초당 명중 수입니다.\n" +
                 "이만큼 맞히는 무기가 보정 1배를 받습니다. 값 자체보다 무기 간 비율이 중요합니다.")]
        public float ReferenceHitsPerSecond = 0.8f;

        [Range(0.05f, 2f)]
        [Tooltip("명중률을 어림할 때 가정하는 표적의 반경(미터)입니다.\n" +
                 "병사 반경과 같게 두는 것이 자연스럽습니다.")]
        public float NominalTargetRadius = 0.35f;

        [Range(0.05f, 1f)]
        [Tooltip("보정치의 하한입니다. 지나치게 잘 맞히는 무기라도 이보다 덜 받지는 않습니다.")]
        public float MinExperienceScale = 0.25f;

        [Range(1f, 12f)]
        [Tooltip("보정치의 상한입니다. 수치를 잘못 적은 무기가 터무니없는 성장을 얻는 것을 막습니다.")]
        public float MaxExperienceScale = 6f;

        [Header("단련도")]
        [Range(1, 12)]
        [Tooltip("랭크 한 단계를 올리는 데 필요한 생존 전투 수입니다.\n" +
                 "숙련도와 달리 전과를 보지 않습니다 — 단련은 '살아남아 본 횟수'입니다.")]
        public int BattlesPerRank = 3;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 이 분대의 이번 전투 성과를 성장으로 옮깁니다.
        ///
        /// <b>무너진 분대는 성장하지 않습니다.</b>
        /// 장부에서 사라질 부대의 숙련도를 올리는 것은 의미가 없고,
        /// 무엇보다 그 값이 다음 전투로 이어질 자리가 없습니다.
        ///
        /// <b>어느 무기가 늘었는지는 분대의 병과가 말합니다.</b>
        /// 전투는 계열을 적어 보내지 않습니다 — 몇 번 맞혔는지만 돌려주고,
        /// 그것이 어떤 동작이었는지는 장부를 든 쪽이 압니다.
        /// </summary>
        /// <param name="squad">성장할 분대입니다.</param>
        /// <param name="report">이 분대에 대한 전황 보고입니다.</param>
        public void Apply(CampaignSquad squad, SquadReport report)
        {
            if (squad == null || squad.Definition == null || report.Destroyed)
            {
                return;
            }

            squad.BattlesSurvived++;

            float scaled = Mathf.Max(0, report.HitsLanded)
                           * ProficiencyPerHit
                           * GetExperienceScale(squad.Definition);

            int gain = Mathf.RoundToInt(scaled) + ProficiencyForSurviving;

            squad.Proficiency = squad.Proficiency.Gain(squad.Definition.Style, gain);

            squad.Rank = ResolveRank(squad.BattlesSurvived);
        }

        /// <summary>
        /// 이 무기가 명중 한 번으로 받을 숙련도의 보정치입니다.
        ///
        /// <b>왜 필요한가</b>
        ///
        /// 명중 수를 그대로 세면 <b>자주 때리고 잘 맞는 무기가 저절로 빨리 숙련됩니다.</b>
        /// 보병은 1초마다 휘두르고 근접이라 거의 다 닿습니다. 궁수는 1.4초마다 쏘고
        /// 그중 상당수가 빗나갑니다. 같은 시간을 똑같이 싸워도 명중 수는 몇 배 차이가 나고,
        /// 그 차이가 그대로 성장 속도가 됩니다. 무기를 고르는 이유가 성능이 아니라
        /// "빨리 크는 쪽"이 되어 버립니다.
        ///
        /// 그래서 <b>같은 시간을 싸우면 비슷하게 자라도록</b> 기대 명중 수로 나눕니다.
        ///
        /// <b>무기 자체의 값만 씁니다</b>
        ///
        /// 여기서 읽는 것은 <see cref="UnitDefinition"/> 뿐이고 <c>UnitStats</c> 는 보지 않습니다.
        /// 성장으로 오른 공격 속도와 명중을 보정에 넣으면, 숙련될수록 더 자주 맞히고
        /// 그래서 보정이 줄어드는 되먹임이 생깁니다. 이미 오른 능력으로 다시 벌을 받는 셈이고,
        /// 성장할수록 성장이 느려지는 이중 감속이 됩니다.
        ///
        /// 보정은 <b>무기의 성질</b>이지 그 무기를 든 부대의 상태가 아닙니다.
        /// 같은 무기라면 신병이든 고참이든 명중 한 번의 값은 같아야 합니다.
        /// </summary>
        /// <param name="definition">무기를 든 병과의 정의입니다.</param>
        /// <returns>명중 한 번에 곱할 보정치입니다. 상·하한 안으로 묶입니다.</returns>
        public float GetExperienceScale(UnitDefinition definition)
        {
            if (definition == null)
            {
                return 1f;
            }

            // 무기 자체의 공격 속도입니다. 랭크도 숙련도도 반영되지 않은 값입니다.
            float attacksPerSecond = 1f / Mathf.Max(0.05f, definition.AttackInterval);

            float expected = attacksPerSecond * EstimateHitRate(definition);

            if (expected <= 0.0001f)
            {
                return MaxExperienceScale;
            }

            return Mathf.Clamp(ReferenceHitsPerSecond / expected, MinExperienceScale, MaxExperienceScale);
        }

        /// <summary>
        /// 이 무기가 한 번 공격했을 때 닿을 확률을 어림합니다.
        ///
        /// <b>근접은 1로 봅니다.</b> 사거리 안에 들어가서 휘두르면 판정 형상이 상대를 훑습니다.
        /// 빗나가는 경우가 없지는 않지만 그것은 자리 잡기의 문제이지 무기의 성질이 아닙니다.
        ///
        /// <b>투사체는 산포로 어림합니다.</b> 표적이 차지하는 각이 산포 원뿔에서
        /// 얼마나 되는지를 봅니다 — 멀수록, 산포가 넓을수록 덜 맞습니다.
        /// 정확한 확률을 구하려는 것이 아니라 <b>무기 사이의 비율</b>을 잡는 것이 목적이므로
        /// 이 정도의 어림으로 충분합니다.
        ///
        /// 산포는 <see cref="UnitDefinition.MaxSpreadDegrees"/>(최하 숙련의 산포)를 씁니다.
        /// 성장한 뒤의 좁아진 산포를 쓰면 보정이 부대 상태를 따라 흔들립니다.
        /// </summary>
        /// <param name="definition">무기를 든 병과의 정의입니다.</param>
        /// <returns>0에서 1 사이의 어림 명중률입니다.</returns>
        public float EstimateHitRate(UnitDefinition definition)
        {
            if (definition.Style != AttackStyle.Projectile)
            {
                return 1f;
            }

            float spreadDegrees = Mathf.Max(0.01f, definition.MaxSpreadDegrees);
            float range = Mathf.Max(1f, definition.AttackRange);

            float targetDegrees = Mathf.Atan2(Mathf.Max(0.01f, NominalTargetRadius), range) * Mathf.Rad2Deg;

            return Mathf.Clamp01(targetDegrees / spreadDegrees);
        }

        /// <summary>
        /// 살아남은 전투 수에 해당하는 단련도를 구합니다.
        /// </summary>
        /// <param name="battlesSurvived">이 분대가 살아 돌아온 전투 수입니다.</param>
        /// <returns>허용 범위 안의 단련도입니다.</returns>
        public int ResolveRank(int battlesSurvived)
        {
            int steps = battlesSurvived / Mathf.Max(1, BattlesPerRank);

            return Mathf.Clamp(
                CombatConstants.MinRank + steps,
                CombatConstants.MinRank,
                CombatConstants.MaxRank);
        }
    }
}
