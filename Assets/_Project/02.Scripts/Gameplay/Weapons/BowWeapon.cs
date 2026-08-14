using SRPG.Common;
using SRPG.Gameplay.Units;
using SRPG.Systems.Combat;
using UnityEngine;

namespace SRPG.Gameplay.Weapons
{
    /// <summary>
    /// 활입니다. 궁수가 씁니다. <b>곡사</b>로 쏩니다.
    ///
    /// 곡사는 발사각을 고정하고 속력을 거리에 맞춰 계산합니다. 그래서 어떤 거리에서도 포물선의 모양이 같습니다.
    /// 반대로 속력을 고정하고 각을 풀면, 가까운 거리에서 발사각이 수직에 가까워져
    /// 화살이 하늘로 솟구쳤다 떨어지는 이상한 그림이 됩니다.
    ///
    /// 곡사를 쓰면 따라오는 결과가 있습니다. <b>비행 시간이 길어집니다.</b>
    /// 9m 거리에서도 1초 남짓 걸리고, 그동안 대상은 3m 넘게 이동합니다.
    /// 그래서 예측이 선택이 아니라 필수가 되고, 예측 자체도 실제 비행 시간을 반영해 반복 수렴시켜야 합니다.
    /// (직선 비행을 가정한 예측을 곡사에 쓰면 비행 시간을 크게 과소평가해 늘 뒤를 쏩니다)
    ///
    /// 그리고 마지막에 <b>산포</b>를 얹습니다.
    /// 산포는 분대 랭크에 반비례합니다. 낮은 랭크의 궁수는 계산은 맞게 하지만 손이 흔들립니다.
    /// 조사에서 확인한 "명중이 들쭉날쭉하다 / 개별 표적보다 무리에게 더 정확하다"가
    /// 별도 규칙 없이 이 산포 하나에서 나옵니다. 한 명을 빗나간 화살이 옆의 다른 적에게 맞기 때문입니다.
    /// </summary>
    public sealed class BowWeapon : WeaponBase
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>시위를 놓는 진행도입니다. 이 시점에 화살이 생성됩니다.</summary>
        private const float ReleaseProgress = 0.55f;

        /// <summary>활을 당길 때 뒤로 빼는 거리입니다.</summary>
        private const float DrawPullback = 0.16f;

        /// <summary>화살이 생성되는 높이 오프셋입니다. 발밑이 아니라 어깨 높이에서 나가야 합니다.</summary>
        private const float MuzzleHeightRatio = 0.72f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        /// <summary>이번 동작에서 겨눈 대상입니다.</summary>
        private Unit _currentTarget;
        /// <summary>이번 동작에서 화살을 이미 쏘았는지 여부입니다. 한 동작에 한 발만 나갑니다.</summary>
        private bool _hasReleased;
        /// <summary>활의 대기 자세입니다. 동작이 끝나면 여기로 돌아옵니다.</summary>
        private Vector3 _restLocalPosition;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>화살이 나가는 지점입니다.</summary>
        private Vector3 MuzzlePosition
        {
            get
            {
                float height = Definition != null ? Definition.DebugHeight * MuzzleHeightRatio : 0.72f;
                return Owner.Position + Vector3.up * height;
            }
        }

        // ====================================================================================================
        // 4. Overrides - Lifecycle
        // ====================================================================================================

        protected override void OnAttackBegan(Unit target)
        {
            _currentTarget = target;
            _hasReleased = false;
        }

        protected override void OnActionTick(float deltaTime, float progress)
        {
            PoseBow(progress);

            if (_hasReleased || progress < ReleaseProgress)
            {
                return;
            }

            _hasReleased = true;
            FireArrow();
        }

        protected override void OnAttackEnded()
        {
            _currentTarget = null;
            _hasReleased = false;
            ReturnToRestPose();
        }

        // ====================================================================================================
        // 5. Private Methods - Firing
        // ====================================================================================================

        /// <summary>
        /// 예측 조준 → 탄도 계산 → 산포 적용 순으로 화살을 쏩니다.
        /// </summary>
        private void FireArrow()
        {
            if (Owner == null || Definition == null || Definition.ProjectilePrefab == null)
            {
                return;
            }

            if (_currentTarget == null || !_currentTarget.IsAlive)
            {
                return;
            }

            Vector3 muzzle = MuzzlePosition;
            Vector3 targetCenter = ResolveTargetCenter();

            float gravity = Mathf.Abs(Physics.gravity.y) * Definition.ProjectileGravityScale;

            // 예측할 대상 속도입니다. 예측을 끄면 0을 넘겨 현재 위치를 그대로 겨눕니다.
            Vector3 targetVelocity = Definition.UsePredictiveAim ? _currentTarget.Velocity : Vector3.zero;

            if (!BallisticSolver.TrySolveArcingIntercept(
                    muzzle,
                    targetCenter,
                    targetVelocity,
                    Definition.ArcLaunchAngleDegrees,
                    gravity,
                    Definition.ProjectileSpeed,
                    out Vector3 launchVelocity,
                    out _))
            {
                // 이 각도와 속력 한계로는 닿지 않는 거리입니다. 쏘지 않고 넘깁니다.
                // 억지로 쏘면 화살이 목표 한참 앞에 꽂혀 오히려 고장처럼 보입니다.
                return;
            }

            float spread = BallisticSolver.GetSpreadForRank(
                Owner.Rank,
                CombatConstants.MaxRank,
                Owner.Stats.MaxSpreadDegrees,
                Owner.Stats.MinSpreadDegrees);

            launchVelocity = BallisticSolver.ApplySpread(launchVelocity, spread);

            SpawnArrow(muzzle, launchVelocity);
        }

        /// <summary>
        /// 대상의 몸통 중앙 좌표입니다. 발밑을 겨누면 화살이 항상 다리 앞 땅에 꽂힙니다.
        /// </summary>
        private Vector3 ResolveTargetCenter()
        {
            float torsoHeight = _currentTarget.Definition != null
                ? _currentTarget.Definition.DebugHeight * 0.5f
                : 0.5f;

            return _currentTarget.Position + Vector3.up * torsoHeight;
        }

        private void SpawnArrow(Vector3 muzzle, Vector3 launchVelocity)
        {
            var rotation = Quaternion.LookRotation(launchVelocity.normalized, Vector3.up);

            var arrow = ProjectilePool != null
                ? ProjectilePool.Rent(Definition.ProjectilePrefab, muzzle, rotation)
                : InstantiateArrow(muzzle, rotation);

            if (arrow == null)
            {
                return;
            }

            arrow.Launch(
                Owner,
                launchVelocity,
                Owner.Stats.AttackDamage,
                Owner.Stats.KnockbackForce,
                Owner.Stats.KnockbackStagger,
                Definition.ProjectileGravityScale,
                Definition.ArcLaunchAngleDegrees,
                Definition.Damage);

            // 화살이 실제로 나간 뒤에만 냅니다. 위에서 탄도 해가 없어 돌아가는 길이 있어서,
            // 동작 시작에 붙이면 <b>쏘지 않은 화살의 소리</b>가 납니다.
            Audio.PlayShot(muzzle);
        }

        /// <summary>
        /// 풀 없이 화살을 만듭니다. 무기 하나만 떼어 시험할 때의 경로입니다.
        /// </summary>
        private Arrow InstantiateArrow(Vector3 muzzle, Quaternion rotation)
        {
            var instance = Instantiate(Definition.ProjectilePrefab, muzzle, rotation);
            GameLayers.ApplyRecursively(instance, GameLayers.Projectile);

            var arrow = instance.GetComponent<Arrow>();
            if (arrow == null)
            {
                Debug.LogError($"[BowWeapon] 화살 프리팹 '{Definition.ProjectilePrefab.name}' 루트에 Arrow 컴포넌트가 없습니다.");
                Destroy(instance);
                return null;
            }

            return arrow;
        }

        // ====================================================================================================
        // 6. Private Methods - Motion
        // ====================================================================================================

        /// <summary>
        /// 활을 당겼다 놓는 동작입니다.
        /// </summary>
        private void PoseBow(float progress)
        {
            if (WeaponPivot == null)
            {
                return;
            }

            float draw;
            if (progress <= ReleaseProgress)
            {
                // 당기는 구간
                draw = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, ReleaseProgress, progress));
            }
            else
            {
                // 놓는 구간: 빠르게 튕겨 돌아옵니다.
                float t = Mathf.InverseLerp(ReleaseProgress, 1f, progress);
                draw = 1f - t * t;
            }

            WeaponPivot.localPosition = _restLocalPosition - Vector3.forward * (draw * DrawPullback);
        }

        // ====================================================================================================
        // 7. Overrides - Pose
        // ====================================================================================================

        protected override Transform BuildDefaultPivot()
        {
            var pivot = new GameObject("BowPivot").transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = new Vector3(0.16f, Definition != null ? Definition.DebugHeight * 0.6f : 0.6f, 0.1f);
            return pivot;
        }

        protected override void CacheRestPose()
        {
            if (WeaponPivot != null)
            {
                _restLocalPosition = WeaponPivot.localPosition;
            }
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
