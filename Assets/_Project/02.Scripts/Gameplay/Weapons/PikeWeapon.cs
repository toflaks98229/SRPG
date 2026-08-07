using System.Collections.Generic;
using SRPG.Common;
using SRPG.Gameplay.Units;
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

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly HashSet<Unit> _alreadyHit = new HashSet<Unit>();
        private readonly Collider[] _hitBuffer = new Collider[MaxHitBuffer];

        private Vector3 _restLocalPosition;

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
        /// 자루를 따라 캡슐을 훑습니다. 창끝뿐 아니라 자루 전체를 잡아야
        /// 이미 품 안으로 파고든 적도 찔립니다.
        /// </summary>
        private void ScanForHits()
        {
            if (Owner == null || WeaponPivot == null)
            {
                return;
            }

            Vector3 shaftBase = WeaponPivot.position;
            Vector3 shaftTip = shaftBase + WeaponPivot.forward * Definition.WeaponLength;

            int count = Physics.OverlapCapsuleNonAlloc(
                shaftBase,
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
            }
        }
    }
}
