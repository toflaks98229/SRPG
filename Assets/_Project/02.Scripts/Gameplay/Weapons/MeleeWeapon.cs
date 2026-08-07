using System.Collections.Generic;
using SRPG.Common;
using SRPG.Gameplay.Units;
using UnityEngine;

namespace SRPG.Gameplay.Weapons
{
    /// <summary>
    /// 휘두르는 근접 무기입니다. 검·도끼류가 씁니다.
    ///
    /// 판정 방식: 칼날이 실제로 지나간 자리를 <c>Physics.OverlapCapsule</c>로 훑습니다.
    /// 사거리 안이면 무조건 맞는 방식과 달리, 다음이 자연히 성립합니다.
    ///   · 휘두르는 호(arc) 밖에 있으면 안 맞는다 — 등 뒤의 적은 안 맞는다
    ///   · 호 안에 여럿 있으면 한 번에 여럿 맞는다 — 밀집한 적을 쓸어 낸다
    ///   · 휘두르는 도중에 들어온 적도 맞는다 — 뛰어드는 적이 그냥 통과하지 않는다
    ///
    /// 판정 구간은 동작 전체가 아니라 중간의 짧은 구간입니다.
    /// 준비 동작에서 이미 맞으면 "휘두르기도 전에 맞았다"는 느낌이 나기 때문입니다.
    /// </summary>
    public sealed class MeleeWeapon : WeaponBase
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>판정이 시작되는 진행도입니다. 이 이전은 준비 동작입니다.</summary>
        private const float ActiveWindowStart = 0.30f;

        /// <summary>판정이 끝나는 진행도입니다. 이 이후는 회수 동작입니다.</summary>
        private const float ActiveWindowEnd = 0.68f;

        /// <summary>휘두르는 호의 시작 각도입니다(도). 몸 오른쪽 뒤에서 시작합니다.</summary>
        private const float SwingStartAngle = 110f;

        /// <summary>휘두르는 호의 끝 각도입니다(도). 몸 왼쪽 앞으로 지나갑니다.</summary>
        private const float SwingEndAngle = -70f;

        /// <summary>대기 자세의 각도입니다.</summary>
        private const float RestAngle = 55f;

        /// <summary>한 번에 감지할 수 있는 최대 대상 수입니다.</summary>
        private const int MaxHitBuffer = 12;

        /// <summary>튜닝이 없을 때 쓰는 대열 이탈 거리(칸)입니다.</summary>
        private const float DefaultBreakFormationTiles = 2f;

        /// <summary>튜닝이 없을 때 쓰는 도약 세기입니다.</summary>
        private const float DefaultLungeForce = 3.6f;

        /// <summary>튜닝이 없을 때 쓰는 최소 도약 거리입니다.</summary>
        private const float DefaultLungeMinDistance = 0.7f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly HashSet<Unit> _alreadyHit = new HashSet<Unit>();
        private readonly Collider[] _hitBuffer = new Collider[MaxHitBuffer];

        private Vector3 _previousBladeTip;
        private bool _hasPreviousTip;

        // ====================================================================================================
        // 2-1. Properties
        // ====================================================================================================

        /// <summary>
        /// 검을 든 병사는 <b>대열을 풀고</b> 눈앞의 적에게 다가가 벱니다.
        ///
        /// 사거리가 1.3m라 자리를 지키기만 하면 두 칸 옆의 적을 영영 못 칩니다.
        /// 적이 알아서 걸어 들어오기를 기다리는 그림은 근접병답지 않고,
        /// 무엇보다 <b>플레이어가 분대를 정확히 겹쳐 놓아야만</b> 전투가 벌어지게 됩니다.
        ///
        /// <b>행군 중에도 붙습니다.</b> 자리를 잡은 뒤에만 나서게 하면 행군로에 선 적을 그냥 지나쳐 갑니다.
        ///
        /// 대신 무한정 쫓지는 않습니다. 리시는 자기 자리(행군 중이면 앵커)에서 재므로,
        /// 분대가 지나가 버리면 거리가 벌어져 자연히 교전을 접고 따라붙습니다.
        /// 별도의 "복귀" 규칙 없이 이탈 시간이 스스로 제한됩니다.
        /// </summary>
        public override float FormationBreakTiles =>
            Tuning != null ? Tuning.MeleeBreakFormationTiles : DefaultBreakFormationTiles;

        // ====================================================================================================
        // 3. Overrides - Lifecycle
        // ====================================================================================================

        /// <summary>
        /// 검병은 <b>몸을 던지며</b> 벱니다. 제자리에서 휘두르면 판정이 아무리 정확해도 답답해 보입니다.
        /// </summary>
        public override float LungeForce =>
            Tuning != null ? Tuning.MeleeLungeForce : DefaultLungeForce;

        protected override void OnAttackBegan(Unit target)
        {
            _alreadyHit.Clear();
            _hasPreviousTip = false;

            Lunge(target);
        }

        /// <summary>
        /// 표적을 향해 몸을 던집니다.
        ///
        /// 이미 붙어 있으면 도약하지 않습니다. 밀고 들어갈 공간이 없는데 힘을 주면
        /// 서로 통과하거나 어색하게 미끄러집니다. 거리가 남아 있을 때만 파고듭니다.
        /// </summary>
        private void Lunge(Unit target)
        {
            if (Owner == null || target == null)
            {
                return;
            }

            float force = LungeForce;
            if (force <= 0f)
            {
                return;
            }

            Vector3 toTarget = target.Position - Owner.Position;
            toTarget.y = 0f;

            float distance = toTarget.magnitude;
            float minimum = Tuning != null ? Tuning.MeleeLungeMinDistance : DefaultLungeMinDistance;

            if (distance <= minimum)
            {
                return;
            }

            Owner.ApplyLunge(toTarget / distance * force);
        }

        protected override void OnActionTick(float deltaTime, float progress)
        {
            PoseBlade(progress);

            bool isActiveWindow = progress >= ActiveWindowStart && progress <= ActiveWindowEnd;
            if (!isActiveWindow)
            {
                _hasPreviousTip = false;
                return;
            }

            ScanForHits();
        }

        protected override void OnAttackEnded()
        {
            _alreadyHit.Clear();
            _hasPreviousTip = false;
            ReturnToRestPose();
        }

        // ====================================================================================================
        // 4. Private Methods - Motion
        // ====================================================================================================

        /// <summary>
        /// 진행도에 따라 칼날 각도를 정합니다.
        /// 준비 구간은 천천히, 판정 구간은 빠르게 지나가도록 이징을 넣어 "휘두르는" 느낌을 만듭니다.
        /// </summary>
        private void PoseBlade(float progress)
        {
            if (WeaponPivot == null)
            {
                return;
            }

            float eased = EaseSwing(progress);
            float angle = Mathf.Lerp(SwingStartAngle, SwingEndAngle, eased);

            WeaponPivot.localRotation = Quaternion.Euler(0f, angle, 0f);
        }

        /// <summary>
        /// 휘두르기 이징입니다. 앞뒤는 느리고 가운데(판정 구간)는 빠릅니다.
        /// </summary>
        private static float EaseSwing(float t)
        {
            // smoothstep 을 한 번 더 밟아 가운데 구간의 각속도를 키웁니다.
            float s = Mathf.SmoothStep(0f, 1f, t);
            return Mathf.SmoothStep(0f, 1f, s);
        }

        // ====================================================================================================
        // 5. Private Methods - Hit Detection
        // ====================================================================================================

        /// <summary>
        /// 칼날이 이번 프레임에 훑고 지나간 구간을 캡슐로 질의합니다.
        ///
        /// 점이 아니라 "직전 위치 → 현재 위치" 구간을 캡슐로 잡는 이유는 터널링 때문입니다.
        /// 프레임이 길거나 휘두르는 속도가 빠르면 칼날이 적을 건너뛰어 지나갈 수 있습니다.
        /// </summary>
        private void ScanForHits()
        {
            if (Owner == null || WeaponPivot == null)
            {
                return;
            }

            Vector3 bladeBase = WeaponPivot.position;
            Vector3 bladeTip = bladeBase + WeaponPivot.forward * Definition.WeaponLength;

            Vector3 sweepStart = _hasPreviousTip ? _previousBladeTip : bladeBase;
            _previousBladeTip = bladeTip;
            _hasPreviousTip = true;

            float radius = Definition.WeaponThickness;

            int count = Physics.OverlapCapsuleNonAlloc(
                sweepStart,
                bladeTip,
                radius,
                _hitBuffer,
                GameLayers.UnitMask,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                var victim = UnitLookup.FromCollider(_hitBuffer[i]);
                if (!IsValidVictim(victim))
                {
                    continue;
                }

                _alreadyHit.Add(victim);
                ApplyHit(victim, bladeBase);
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
        /// 타격을 전달합니다.
        /// 넉백 방향은 "공격자 → 피격자"의 수평 방향입니다. 밀려난 끝이 물이면 그대로 익사합니다.
        /// </summary>
        private void ApplyHit(Unit victim, Vector3 bladeBase)
        {
            Vector3 push = victim.Position - Owner.Position;
            push.y = 0f;

            // 정확히 겹쳐 서 있으면 방향이 나오지 않습니다. 칼이 향한 쪽으로 밀어냅니다.
            if (push.sqrMagnitude < 0.0001f)
            {
                push = WeaponPivot != null ? WeaponPivot.forward : Owner.transform.forward;
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
            var pivot = new GameObject("WeaponPivot").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = new Vector3(0f, Definition != null ? Definition.DebugHeight * 0.55f : 0.55f, 0f);
            return pivot;
        }

        protected override void CacheRestPose()
        {
            ReturnToRestPose();
        }

        protected override void ReturnToRestPose()
        {
            if (WeaponPivot != null)
            {
                WeaponPivot.localRotation = Quaternion.Euler(0f, RestAngle, 0f);
            }
        }
    }
}
