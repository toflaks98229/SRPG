using SRPG.Common;
using SRPG.Data;
using UnityEngine;

namespace SRPG.Systems.Combat
{
    /// <summary>
    /// <b>맞는 쪽</b>이 어떤 상태인지입니다. 한 번의 타격을 푸는 데 필요한 것만 담았습니다.
    ///
    /// <b>왜 <c>Unit</c> 을 받지 않는가</b>
    ///
    /// 병사를 통째로 받으면 이 계산이 <c>MonoBehaviour</c> 를 끌고 들어옵니다.
    /// 그러면 "판금 갑옷에 자돌이 얼마나 드는가"를 확인하려고 씬에 병사를 세워야 하고,
    /// 세운 김에 체력이 깎이고 넉백이 걸려 검사가 계산이 아니라 <b>연출</b>을 보게 됩니다.
    ///
    /// 여기 있는 넷이 결과를 결정하는 전부입니다. 그러므로 그 넷만 받습니다.
    /// </summary>
    public readonly struct DefenderProfile
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>맞는 쪽이 바라보는 방향입니다. 방패는 이쪽에서 오는 것만 막습니다.</summary>
        public readonly Vector3 Forward;

        /// <summary>투사체 피해 감소율(0~1)입니다. 사실상 방패를 들었는지입니다.</summary>
        public readonly float ProjectileResistance;

        /// <summary>몸에 걸친 상시 방어입니다. 타격 성질과 맞물립니다.</summary>
        public readonly ArmorType Armor;

        /// <summary>지금 움직이고 있는지입니다. 뛰는 동안에는 방패가 제 몫을 못 합니다.</summary>
        public readonly bool IsMoving;

        // ====================================================================================================
        // 2. Constructor
        // ====================================================================================================

        /// <param name="forward">맞는 쪽이 바라보는 방향입니다.</param>
        /// <param name="projectileResistance">투사체 피해 감소율(0~1)입니다.</param>
        /// <param name="armor">몸에 걸친 방어입니다.</param>
        /// <param name="isMoving">지금 움직이고 있는지입니다.</param>
        public DefenderProfile(Vector3 forward, float projectileResistance, ArmorType armor, bool isMoving)
        {
            Forward = forward;
            ProjectileResistance = projectileResistance;
            Armor = armor;
            IsMoving = isMoving;
        }
    }

    /// <summary>
    /// 한 번의 타격이 실제로 무엇을 하는지입니다.
    ///
    /// <b>피해와 충격이 따로 나옵니다.</b> 둘은 서로 다른 것을 타기 때문입니다 —
    /// 자세한 이유는 <see cref="DamageResolver.Resolve"/> 에 적혀 있습니다.
    /// </summary>
    public readonly struct DamageOutcome
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>체력에서 실제로 깎일 양입니다. 방패와 갑옷이 모두 적용된 값입니다.</summary>
        public readonly float HealthLoss;

        /// <summary>밀어낼 속도입니다. 수평 성분만 남아 있고, 밀 것이 없으면 0입니다.</summary>
        public readonly Vector3 Impulse;

        /// <summary>경직 시간(초)입니다.</summary>
        public readonly float StaggerSeconds;

        /// <summary>
        /// 방패가 얼마나 막았는지입니다. 1이면 그대로 들어갔고 0에 가까우면 거의 막혔습니다.
        ///
        /// 결과 자체에는 이미 반영되어 있습니다. 진단과 검사를 위해 함께 실어 보냅니다 —
        /// "왜 이 화살이 안 아팠는가"를 되짚을 때 이 값 하나면 답이 납니다.
        /// </summary>
        public readonly float Mitigation;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>밀어낼 것이 있는지입니다.</summary>
        public bool HasImpulse => Impulse.sqrMagnitude > 0f;

        // ====================================================================================================
        // 3. Constructor
        // ====================================================================================================

        /// <param name="healthLoss">체력에서 깎일 양입니다.</param>
        /// <param name="impulse">밀어낼 속도입니다.</param>
        /// <param name="staggerSeconds">경직 시간입니다.</param>
        /// <param name="mitigation">방패 감쇠 배율입니다.</param>
        public DamageOutcome(float healthLoss, Vector3 impulse, float staggerSeconds, float mitigation)
        {
            HealthLoss = healthLoss;
            Impulse = impulse;
            StaggerSeconds = staggerSeconds;
            Mitigation = mitigation;
        }
    }

    /// <summary>
    /// 한 번의 타격이 얼마나 들어가고 얼마나 밀어내는지를 정합니다.
    ///
    /// <b>왜 병사 밖으로 꺼냈는가</b>
    ///
    /// 이 판정은 <c>Unit.ReceiveHit</c> 안에 있었습니다. 그 함수는 <b>세 가지를 겸하고</b> 있었습니다 —
    /// 전과를 세고 소리를 내고, 그리고 이 계산을 했습니다. 앞의 둘은 병사의 일이 맞지만
    /// 이 계산은 아닙니다. 방패 각도와 갑옷 상성은 <b>누가 맞았는지와 무관한 규칙</b>이고,
    /// 그런 규칙이 <c>MonoBehaviour</c> 안에 있으면 확인하는 데 씬이 필요해집니다.
    ///
    /// <see cref="ShieldSolver"/> 가 이미 같은 이유로 여기 나와 있습니다. 그때는 방패 각도만 꺼냈고,
    /// 갑옷 상성과 넉백 감쇠는 병사 안에 남았습니다. 남은 절반을 마저 옮깁니다.
    ///
    /// <b>규칙의 뼈대</b>
    ///
    /// 두 방어가 <b>다른 축</b>으로 작동합니다. 그것이 이 게임 전술의 핵심입니다.
    ///
    ///   · <b>방패</b> — 상황 방어. 투사체만, 그것도 앞이나 위에서 올 때만 막습니다.
    ///     그래서 옆으로 돌아가면 뚫립니다.
    ///   · <b>갑옷</b> — 상시 방어. 어디서 맞든 같지만 무엇으로 맞았는지에 따라 갈립니다.
    ///     그래서 자돌 무기를 붙이면 뚫립니다.
    ///
    /// 하나로 합치면 "측면을 잡는다"와 "맞는 무기를 고른다"라는 서로 다른 두 해법이 뭉개집니다.
    /// </summary>
    public static class DamageResolver
    {
        // ====================================================================================================
        // 1. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 타격 하나를 풉니다.
        ///
        /// <b>피해와 충격이 다른 것을 탑니다</b>
        ///
        /// 피해는 방패와 갑옷을 <b>모두</b> 지납니다. 충격은 <b>방패만</b> 지납니다.
        /// 갑옷은 살을 지킬 뿐 운동량을 없애지 못하기 때문입니다 —
        /// 판금에 막힌 철퇴도 사람을 밀어냅니다.
        ///
        /// 그리고 막아 낸 방패조차 충격을 전부 지우지는 못합니다
        /// (<c>BlockedKnockbackRetention</c>). 이 덕분에 큰 적의 일격은
        /// 피해가 막혀도 방패벽을 뒤로 밀어 틈을 벌립니다. 그 틈이 다음 수의 재료가 됩니다.
        ///
        /// <b>넉백은 수평 성분만 씁니다.</b> 위에서 내리꽂힌 화살이 사람을 땅에 박을 수는 없습니다.
        /// </summary>
        /// <param name="hit">무엇을 어느 방향으로 때렸는지입니다.</param>
        /// <param name="defender">맞는 쪽의 상태입니다.</param>
        /// <param name="tuning">전투 수치입니다. null이면 상성도 감쇠 보정도 없는 것으로 봅니다.</param>
        /// <returns>실제로 깎일 체력과 밀어낼 힘입니다.</returns>
        public static DamageOutcome Resolve(in DamageInfo hit, in DefenderProfile defender, BattleTuning tuning)
        {
            float mitigation = ComputeMitigation(hit, defender, tuning);
            float armor = ComputeArmorEffectiveness(hit, defender, tuning);

            float healthLoss = hit.Amount * mitigation * armor;

            // 갑옷 배율(armor)이 여기 없는 것이 핵심입니다. 위 문단의 이유입니다.
            float retention = tuning != null ? Mathf.Clamp01(tuning.Shield.BlockedKnockbackRetention) : 1f;
            float impulseScale = Mathf.Lerp(mitigation, 1f, retention);

            Vector3 push = hit.Direction;
            push.y = 0f;

            if (push.sqrMagnitude <= 0.0001f)
            {
                // 바로 위나 아래에서 온 타격입니다. 밀어낼 방향이 없습니다.
                return new DamageOutcome(healthLoss, Vector3.zero, 0f, mitigation);
            }

            return new DamageOutcome(
                healthLoss,
                push.normalized * (hit.KnockbackForce * impulseScale),
                hit.StaggerSeconds * impulseScale,
                mitigation);
        }

        /// <summary>
        /// 방패가 이 타격을 얼마나 막는지 구합니다. 1이면 그대로 들어가고 0에 가까우면 거의 막힙니다.
        ///
        /// 방패는 <b>투사체만</b> 막습니다. 근접 타격이 그냥 지나가는 것은 규칙이지 누락이 아닙니다 —
        /// 방패로 칼을 받아 내는 것은 이 게임이 다루는 층위가 아닙니다.
        ///
        /// <b>이동 중에는 제 몫을 못 합니다.</b> 뛰는 동안 방패가 위아래로 흔들려 빈틈이 생기기 때문입니다.
        /// 그래서 "궁수 앞에서 뛰어다니지 마라"가 규칙이 아니라 결과로 나옵니다.
        /// </summary>
        /// <param name="hit">받은 타격입니다.</param>
        /// <param name="defender">맞는 쪽의 상태입니다.</param>
        /// <param name="tuning">전투 수치입니다. null이면 코드 기본값을 씁니다.</param>
        /// <returns>피해와 충격에 곱할 배율입니다.</returns>
        public static float ComputeMitigation(
            in DamageInfo hit,
            in DefenderProfile defender,
            BattleTuning tuning)
        {
            if (hit.Kind != DamageKind.Projectile || defender.ProjectileResistance <= 0f)
            {
                return 1f;
            }

            float steepMargin = tuning != null
                ? tuning.Shield.SteepBlockMarginDegrees
                : BattleTuning.DefaultSteepBlockMarginDegrees;

            float resistance = defender.ProjectileResistance;

            if (defender.IsMoving && tuning != null)
            {
                resistance *= Mathf.Clamp01(tuning.Shield.MovingEffectiveness);
            }

            return ShieldSolver.ComputeBlockFactor(
                hit.Direction,
                defender.Forward,
                resistance,
                hit.ArcAngleDegrees,
                steepMargin);
        }

        /// <summary>
        /// 이 타격의 성질이 상대의 갑옷에 얼마나 잘 드는지 구합니다.
        ///
        /// 수치는 튜닝이 들고 있습니다. 여기서는 어느 축을 물어야 하는지만 압니다 —
        /// 아홉 칸짜리 표를 코드에 박아 두면 밸런스를 만질 때마다 컴파일하게 됩니다.
        ///
        /// <b>튜닝이 없으면 상성이 없는 것으로 봅니다.</b> 병사만 떼어 검사하는 경로가 그렇습니다.
        /// </summary>
        /// <param name="hit">받은 타격입니다.</param>
        /// <param name="defender">맞는 쪽의 상태입니다.</param>
        /// <param name="tuning">전투 수치입니다. null이면 1을 돌려줍니다.</param>
        /// <returns>피해량에 곱할 배율입니다.</returns>
        public static float ComputeArmorEffectiveness(
            in DamageInfo hit,
            in DefenderProfile defender,
            BattleTuning tuning)
        {
            return tuning != null ? tuning.GetArmorEffectiveness(hit.Type, defender.Armor) : 1f;
        }
    }
}
