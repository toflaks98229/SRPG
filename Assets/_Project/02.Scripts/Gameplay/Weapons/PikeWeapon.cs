using System.Collections.Generic;
using SRPG.Common;
using SRPG.Gameplay.Units;
using SRPG.Systems.Combat;
using UnityEngine;

namespace SRPG.Gameplay.Weapons
{
    /// <summary>
    /// 찌르는 근접 무기입니다. 창병이 씁니다.
    ///
    /// 검과 두 가지가 다릅니다.
    ///
    /// 1) <b>창이 별도의 오브젝트로 그려집니다.</b>
    ///    창의 사거리 우위는 스탯이 아니라 <b>자루 길이</b>에서 물리적으로 나옵니다.
    ///    적이 창끝에 닿기 전에 먼저 맞는 것이 눈에 보여야, 왜 창병 앞으로 걸어 들어가면 안 되는지가 전달됩니다.
    ///
    /// 2) <b>호가 아니라 선분으로 판정합니다.</b>
    ///    앞으로 뻗은 자루를 따라 캡슐을 훑으므로 정면의 좁은 각도만 위험합니다.
    ///    측면과 후방이 비는 것이 창병의 약점이 되고, 그래서 초크포인트가 필요해집니다.
    ///    조사에서 확인한 "이동하거나 측면을 잡히면 창병 대열이 무너진다"가 여기서 나옵니다.
    ///
    /// <b>창병을 창병답게 만드는 세 가지</b>
    ///
    /// 3) <b>이동 중에는 창을 세워 듭니다.</b>
    ///    긴 자루를 겨눈 채로는 걸을 수 없습니다. 자리를 잡기 전에는 싸울 수 없다는 규칙이
    ///    수치가 아니라 <b>눈에 보이는 자세</b>로 드러납니다.
    ///
    /// 4) <b>품 안으로 파고들면 찌를 수 없습니다.</b>
    ///    긴 무기의 위험한 부분은 창끝이지 손 근처의 자루가 아닙니다.
    ///    안쪽 사각지대를 두어, 파고드는 데 성공한 적에게는 창병이 무력해집니다.
    ///    이것이 창병을 단독으로 세우면 안 되는 이유이자, 보병이 앞에 서야 하는 이유입니다.
    ///
    /// 5) <b>적이 올 자리를 미리 겨눕니다.</b>
    ///    사거리 밖이어도 접근 중인 적의 도착 지점을 향해 창을 돌려 둡니다.
    ///    지금 있는 곳을 겨누면 항상 한 박자 늦습니다. 쫓아가지 않고 기다리는데도 먼저 닿는 것이
    ///    창병이 "버티는 병과"인 이유입니다.
    /// </summary>
    public sealed class PikeWeapon : WeaponBase
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>찌르기 판정이 시작되는 진행도입니다.</summary>
        private const float ActiveWindowStart = 0.28f;

        /// <summary>찌르기 판정이 끝나는 진행도입니다.</summary>
        private const float ActiveWindowEnd = 0.58f;

        /// <summary>찌를 때 창이 앞으로 나가는 거리입니다.</summary>
        private const float ThrustDistance = 0.75f;

        /// <summary>대기 시 창을 뒤로 당겨 두는 거리입니다.</summary>
        private const float RestPullback = 0.18f;

        /// <summary>정면 판정으로 인정하는 각도(도)입니다. 이 밖은 창이 닿지 않습니다.</summary>
        private const float FrontalConeDegrees = 42f;

        private const int MaxHitBuffer = 8;

        /// <summary>이동 중 창을 세우는 각도(도)입니다. 음수가 창끝을 위로 들어 올립니다.</summary>
        private const float CarryPitchDegrees = -62f;

        /// <summary>운반 자세와 겨눔 자세 사이를 오가는 속도입니다.</summary>
        private const float PoseBlendSpeed = 9f;

        /// <summary>튜닝이 없을 때 쓰는 안쪽 사각지대 비율입니다.</summary>
        private const float DefaultInnerDeadZoneRatio = 0.45f;

        /// <summary>튜닝이 없을 때 쓰는 준비 동작 보정(초)입니다.</summary>
        private const float DefaultAimLeadSeconds = 0.15f;

        /// <summary>튜닝이 없을 때 쓰는 예측 상한(초)입니다.</summary>
        private const float DefaultMaxAimLeadSeconds = 1.2f;

        /// <summary>튜닝이 없을 때 쓰는 표적 고정 시간(초)입니다.</summary>
        private const float DefaultTargetLockSeconds = 1.1f;

        /// <summary>튜닝이 없을 때 쓰는 회전 스프링 값입니다.</summary>
        private const float DefaultTurnSpringFrequency = 1.4f;
        private const float DefaultTurnSpringDamping = 0.5f;

        /// <summary>튜닝이 없을 때 쓰는 진형 붕괴 지속 시간(초)입니다.</summary>
        private const float DefaultBreakRecoverySeconds = 1.4f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly HashSet<Unit> _alreadyHit = new HashSet<Unit>();
        private readonly Collider[] _hitBuffer = new Collider[MaxHitBuffer];

        private Vector3 _restLocalPosition;

        /// <summary>진형이 무너진 뒤 회복까지 남은 시간입니다. 0보다 크면 창을 들고 물러납니다.</summary>
        private float _breakTimer;

        /// <summary>품 안으로 파고든 적의 위치입니다. 물러날 방향의 기준입니다.</summary>
        private Vector3 _intruderPosition;

        // ====================================================================================================
        // 2-1. Properties
        // ====================================================================================================

        /// <summary>
        /// 창끝이 위협적인 구간의 시작 거리입니다. 이 안쪽은 자루뿐이라 찌를 수 없습니다.
        /// 자루가 길수록 사각지대도 비례해 커집니다. 긴 창일수록 파고들렸을 때 더 무력합니다.
        /// </summary>
        private float InnerDeadZone
        {
            get
            {
                float ratio = Tuning != null ? Tuning.PikeInnerDeadZoneRatio : DefaultInnerDeadZoneRatio;
                float length = Definition != null ? Definition.WeaponLength : 1f;

                // 사각지대가 자루 전체를 먹으면 창이 아무도 못 찌릅니다.
                return Mathf.Clamp(length * ratio, 0f, length * 0.9f);
            }
        }

        /// <summary>
        /// 한 번 겨눈 적은 잠시 놓지 않습니다.
        ///
        /// 더 가까운 적이 나타날 때마다 창끝을 돌리면 방어선이 이리저리 흩어지고,
        /// 결국 아무도 막지 않는 구멍이 생깁니다. 굳건히 버티는 것이 창병의 일입니다.
        /// </summary>
        public override float TargetLockSeconds =>
            Tuning != null ? Tuning.PikeTargetLockSeconds : DefaultTargetLockSeconds;

        /// <summary>
        /// 창병은 공격 대기열을 씁니다.
        ///
        /// 여럿이 최전방의 한 명만 동시에 찌르면 그가 죽은 자리로 뒤따라오던 적들이 그대로 통과합니다.
        /// 방어선의 목적은 잘 죽이는 것이 아니라 <b>새지 않는 것</b>입니다.
        /// </summary>
        public override bool UsesAttackQueue => true;

        /// <summary>
        /// 창은 무게가 있습니다. 표적을 옮길 때 관성으로 잠깐 지나쳤다 되돌아옵니다.
        /// 보간으로 돌리면 로봇처럼 휙 꺾여 긴 무기의 느낌이 나지 않습니다.
        /// </summary>
        public override bool UsesSpringTurn => true;

        /// <summary>
        /// 창병은 <b>횡대</b>로 늘어섭니다.
        ///
        /// 창은 정면의 좁은 각도만 위험합니다. 동심원으로 뭉치면 대부분의 창이 안쪽을 향해
        /// 아무것도 막지 못합니다. 옆으로 늘어서야 창끝이 모두 같은 방향을 겨눠 방어선이 됩니다.
        /// </summary>
        public override bool PrefersLineFormation => true;

        public override float TurnSpringFrequency =>
            Tuning != null ? Tuning.PikeTurnSpringFrequency : DefaultTurnSpringFrequency;

        public override float TurnSpringDamping =>
            Tuning != null ? Tuning.PikeTurnSpringDamping : DefaultTurnSpringDamping;

        // ====================================================================================================
        // 3. Overrides - Lifecycle
        // ====================================================================================================

        protected override void OnAttackBegan(Unit target)
        {
            _alreadyHit.Clear();
        }

        protected override void OnActionTick(float deltaTime, float progress)
        {
            PoseShaft(progress);

            if (progress < ActiveWindowStart || progress > ActiveWindowEnd)
            {
                return;
            }

            ScanForHits();
        }

        protected override void OnAttackEnded()
        {
            _alreadyHit.Clear();
            ReturnToRestPose();
        }

        /// <summary>
        /// 공격하지 않는 동안의 자세입니다. 걷는 중이면 창을 세워 들고, 멈춰 서면 겨눕니다.
        ///
        /// 이 자세가 곧 규칙입니다. 창을 세워 든 창병은 공격할 수 없고, 그것이 화면에 그대로 보입니다.
        /// 수치로만 막고 자세는 그대로 두면, 플레이어는 왜 창병이 안 싸우는지 알 수 없습니다.
        /// </summary>
        protected override void OnIdleTick(float deltaTime)
        {
            _breakTimer -= deltaTime;
            DetectIntruder();

            if (WeaponPivot == null)
            {
                return;
            }

            // 걷는 중이거나 진형이 무너졌으면 창을 세워 듭니다. 둘 다 싸울 수 없는 상태입니다.
            bool carrying = (Owner != null && Owner.IsMoving) || IsBroken;

            Quaternion targetRotation = carrying
                ? Quaternion.Euler(CarryPitchDegrees, 0f, 0f)
                : Quaternion.identity;

            // 뚝 끊기지 않게 섞습니다. 자리를 잡는 순간 창이 내려오는 것이 눈에 보여야 합니다.
            WeaponPivot.localRotation = Quaternion.Slerp(
                WeaponPivot.localRotation,
                targetRotation,
                PoseBlendSpeed * deltaTime);

            WeaponPivot.localPosition = Vector3.Lerp(
                WeaponPivot.localPosition,
                _restLocalPosition,
                PoseBlendSpeed * deltaTime);
        }

        // ====================================================================================================
        // 3-0. Line Breaking
        // ====================================================================================================

        /// <summary>진형이 무너져 물러나는 중인지 여부입니다.</summary>
        private bool IsBroken => _breakTimer > 0f;

        /// <summary>
        /// 품 안으로 파고든 적이 있는지 살핍니다.
        ///
        /// 사각지대 안까지 들어온 적이 있으면 창병은 싸울 방법이 없습니다.
        /// 그 자리에 서서 헛찌르는 것은 선택지가 아니고, 창을 세워 들고 물러나는 것이 유일한 반응입니다.
        ///
        /// <b>이 한 명이 열어 준 구멍으로 뒤따르던 적들이 쏟아져 들어옵니다.</b>
        /// 진형 붕괴가 연출이 아니라 물리적 결과로 일어나는 지점입니다.
        /// </summary>
        private void DetectIntruder()
        {
            if (Owner == null || Definition == null || Owner.IsMoving)
            {
                return;
            }

            var intruder = Owner.FindClosestEnemyWithin(InnerDeadZone);
            if (intruder == null)
            {
                return;
            }

            _intruderPosition = intruder.Position;
            _breakTimer = Tuning != null ? Tuning.PikeBreakRecoverySeconds : DefaultBreakRecoverySeconds;
        }

        /// <summary>무너진 동안에는 찌를 수 없습니다.</summary>
        public override bool CanBeginAttack(Unit target) => !IsBroken;

        /// <summary>무너진 동안에는 파고든 적에게서 물러납니다.</summary>
        public override bool TryGetRetreatFrom(out Vector3 threatPosition)
        {
            threatPosition = _intruderPosition;
            return IsBroken;
        }

        // ====================================================================================================
        // 3-1. Overrides - Aiming
        // ====================================================================================================

        /// <summary>
        /// 접근 중인 적이 <b>도착할 자리</b>를 겨눕니다.
        ///
        /// 조사에서 확인한 Bad North 창병의 행동입니다.
        /// 적이 아직 사거리 밖이어도 창은 이미 그가 올 자리를 향해 있습니다.
        /// 지금 있는 곳을 겨누면 항상 한 박자 늦어, 버티는 병과가 성립하지 않습니다.
        ///
        /// <b>이동 중에는 겨누지 않습니다.</b> 창을 세워 들고 걷는 중이라 겨눌 수가 없습니다.
        /// </summary>
        public override bool TryGetAimPoint(Unit target, out Vector3 aimPoint)
        {
            aimPoint = Vector3.zero;

            if (Owner == null || target == null || !target.IsAlive || Owner.IsMoving)
            {
                return false;
            }

            float lead = Tuning != null ? Tuning.PikeAimLeadSeconds : DefaultAimLeadSeconds;
            float maxLead = Tuning != null ? Tuning.PikeMaxAimLeadSeconds : DefaultMaxAimLeadSeconds;

            // 창끝이 닿기 시작하는 거리를 기준으로 도착 시점을 잡습니다.
            float effectiveRange = Definition != null ? Definition.AttackRange : 2f;

            aimPoint = AimPredictor.PredictApproachPoint(
                Owner.Position,
                target.Position,
                target.Velocity,
                effectiveRange,
                lead,
                maxLead);

            return true;
        }

        // ====================================================================================================
        // 4. Private Methods - Motion
        // ====================================================================================================

        /// <summary>
        /// 창을 앞으로 뻗었다가 되돌립니다. 뻗는 구간은 빠르고 회수는 느립니다.
        /// </summary>
        private void PoseShaft(float progress)
        {
            if (WeaponPivot == null)
            {
                return;
            }

            // 찌르는 동안에는 반드시 수평입니다. 운반 자세가 남아 있으면 판정 방향이 위를 향합니다.
            WeaponPivot.localRotation = Quaternion.identity;

            float extend;
            if (progress <= ActiveWindowEnd)
            {
                // 뻗는 구간: 빠르게 가속해 찌릅니다.
                float t = Mathf.InverseLerp(0f, ActiveWindowEnd, progress);
                extend = 1f - (1f - t) * (1f - t);
            }
            else
            {
                // 회수 구간: 천천히 당겨 다음 찌르기를 준비합니다.
                float t = Mathf.InverseLerp(ActiveWindowEnd, 1f, progress);
                extend = 1f - Mathf.SmoothStep(0f, 1f, t);
            }

            WeaponPivot.localPosition = _restLocalPosition + Vector3.forward * (extend * ThrustDistance);
        }

        // ====================================================================================================
        // 5. Private Methods - Hit Detection
        // ====================================================================================================

        /// <summary>
        /// 창끝 쪽 구간만 캡슐로 훑습니다.
        ///
        /// <b>자루 전체가 아니라 바깥 구간만 봅니다.</b>
        /// 긴 무기에서 위험한 것은 창끝이지 손 근처의 자루가 아닙니다.
        /// 안쪽으로 파고든 적은 이 구간에 들어오지 않으므로, 창병은 그를 지나쳐 헛찌릅니다.
        ///
        /// 예전에는 자루 전체를 훑어 품 안의 적도 찔렸습니다.
        /// 그러면 창병에게 약점이 없어지고, 보병을 앞에 세울 이유도 사라집니다.
        /// </summary>
        private void ScanForHits()
        {
            if (Owner == null || WeaponPivot == null)
            {
                return;
            }

            Vector3 forward = WeaponPivot.forward;
            Vector3 shaftBase = WeaponPivot.position;

            Vector3 lethalStart = shaftBase + forward * InnerDeadZone;
            Vector3 shaftTip = shaftBase + forward * Definition.WeaponLength;

            int count = Physics.OverlapCapsuleNonAlloc(
                lethalStart,
                shaftTip,
                Definition.WeaponThickness,
                _hitBuffer,
                GameLayers.UnitMask,
                QueryTriggerInteraction.Collide);

            float cosCone = Mathf.Cos(FrontalConeDegrees * Mathf.Deg2Rad);

            for (int i = 0; i < count; i++)
            {
                var victim = UnitLookup.FromCollider(_hitBuffer[i]);
                if (!IsValidVictim(victim))
                {
                    continue;
                }

                // 정면 원뿔 밖은 창이 닿지 않습니다. 측면과 후방이 창병의 약점입니다.
                Vector3 toVictim = victim.Position - Owner.Position;
                toVictim.y = 0f;

                if (toVictim.sqrMagnitude > 0.0001f)
                {
                    Vector3 facing = Owner.transform.forward;
                    facing.y = 0f;

                    if (Vector3.Dot(facing.normalized, toVictim.normalized) < cosCone)
                    {
                        continue;
                    }
                }

                _alreadyHit.Add(victim);
                ApplyHit(victim);
            }
        }

        private bool IsValidVictim(Unit victim)
        {
            return victim != null
                   && victim != Owner
                   && victim.IsAlive
                   && victim.Team != Owner.Team
                   && !_alreadyHit.Contains(victim);
        }

        /// <summary>
        /// 타격을 전달합니다. 찌르기라 넉백 방향은 창이 향한 쪽입니다.
        /// </summary>
        private void ApplyHit(Unit victim)
        {
            Vector3 push = WeaponPivot.forward;
            push.y = 0f;

            if (push.sqrMagnitude < 0.0001f)
            {
                push = victim.Position - Owner.Position;
                push.y = 0f;
            }

            victim.ReceiveHit(DamageInfo.Melee(
                Definition.AttackDamage,
                push,
                Definition.KnockbackForce,
                Definition.KnockbackStagger,
                Owner));
        }

        // ====================================================================================================
        // 6. Overrides - Pose
        // ====================================================================================================

        protected override Transform BuildDefaultPivot()
        {
            var pivot = new GameObject("SpearPivot").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = new Vector3(0.18f, Definition != null ? Definition.DebugHeight * 0.62f : 0.62f, 0f);
            return pivot;
        }

        protected override void CacheRestPose()
        {
            if (WeaponPivot != null)
            {
                // 프리팹이 배치해 둔 위치를 기준 자세로 삼습니다.
                _restLocalPosition = WeaponPivot.localPosition - Vector3.forward * RestPullback;
            }

            ReturnToRestPose();
        }

        protected override void ReturnToRestPose()
        {
            if (WeaponPivot != null)
            {
                WeaponPivot.localPosition = _restLocalPosition;
                WeaponPivot.localRotation = Quaternion.identity;
            }
        }
    }
}
