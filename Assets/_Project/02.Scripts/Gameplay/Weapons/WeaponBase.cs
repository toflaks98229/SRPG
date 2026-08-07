using SRPG.Data;
using SRPG.Gameplay.Units;
using UnityEngine;

namespace SRPG.Gameplay.Weapons
{
    /// <summary>
    /// 무기의 공통 골격입니다.
    ///
    /// 이 계층이 생긴 이유: 피해를 "사거리 안이면 즉시 적용"에서 "무기가 실제로 닿은 곳에서 적용"으로 바꿨습니다.
    /// 그러면 공격은 순간이 아니라 <b>시간을 갖는 동작</b>이 되고, 그 동작을 관리할 주체가 필요합니다.
    ///
    /// 한 번의 공격은 세 구간으로 나뉩니다.
    ///   준비(windup) → 판정(active) → 회수(recovery)
    /// 판정 구간에서만 물리 질의를 돌리므로, 휘두르는 도중에 들어온 적도 맞고
    /// 이미 맞은 적은 같은 동작에서 두 번 맞지 않습니다.
    /// </summary>
    public abstract class WeaponBase : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>이 무기를 든 유닛입니다.</summary>
        protected Unit Owner { get; private set; }

        /// <summary>소유자의 정의 데이터입니다.</summary>
        protected UnitDefinition Definition { get; private set; }

        /// <summary>
        /// 투사체 재사용 풀입니다. 투사체를 쓰지 않는 무기에는 null일 수 있습니다.
        ///
        /// 전투 컨텍스트 전체가 아니라 <b>풀만</b> 받습니다.
        /// 무기가 지형이나 유닛 레지스트리에 손댈 이유가 없고, 받을 수 있게 두면 언젠가 손댑니다.
        /// </summary>
        protected ProjectilePool ProjectilePool { get; private set; }

        /// <summary>무기 모델이 붙는 트랜스폼입니다. 프리팹에서 연결하거나 런타임에 만듭니다.</summary>
        [SerializeField]
        [Tooltip("무기 모델의 루트입니다. 공격 동작에서 이 트랜스폼을 움직입니다.")]
        protected Transform WeaponPivot;

        private float _actionTimer;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>공격 동작이 진행 중인지 여부입니다.</summary>
        public bool IsBusy => _actionTimer > 0f;

        /// <summary>동작 전체 길이 대비 현재 진행도입니다. 0이면 시작, 1이면 끝입니다.</summary>
        protected float Progress
        {
            get
            {
                float duration = Definition != null ? Definition.AttackDuration : 0.4f;
                return duration <= 0.0001f ? 1f : 1f - Mathf.Clamp01(_actionTimer / duration);
            }
        }

        /// <summary>공격을 시작할 때 소유자를 제자리에 붙잡는 시간입니다.</summary>
        public virtual float RootDuration => Definition != null ? Definition.AttackRootDuration : 0f;

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 무기를 초기화합니다. 유닛 초기화 직후 호출됩니다.
        /// </summary>
        /// <param name="owner">이 무기를 든 유닛입니다.</param>
        /// <param name="definition">소유자의 정의 데이터입니다.</param>
        /// <param name="projectilePool">투사체 재사용 풀입니다. 없어도 동작합니다.</param>
        public virtual void Initialize(Unit owner, UnitDefinition definition, ProjectilePool projectilePool = null)
        {
            Owner = owner;
            Definition = definition;
            ProjectilePool = projectilePool;

            if (WeaponPivot == null)
            {
                WeaponPivot = BuildDefaultPivot();
            }

            CacheRestPose();
        }

        /// <summary>
        /// 공격 동작을 시작합니다.
        /// </summary>
        /// <returns>실제로 시작했으면 true입니다. 이미 동작 중이면 false입니다.</returns>
        public bool TryBeginAttack(Unit target)
        {
            if (IsBusy || Owner == null || Definition == null || target == null || !target.IsAlive)
            {
                return false;
            }

            _actionTimer = Mathf.Max(0.05f, Definition.AttackDuration);
            OnAttackBegan(target);
            return true;
        }

        /// <summary>
        /// 매 프레임 호출됩니다. 동작 진행과 판정을 처리합니다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_actionTimer <= 0f)
            {
                OnIdleTick(deltaTime);
                return;
            }

            _actionTimer -= deltaTime;
            OnActionTick(deltaTime, Progress);

            if (_actionTimer <= 0f)
            {
                _actionTimer = 0f;
                OnAttackEnded();
            }
        }

        /// <summary>
        /// 진행 중인 동작을 즉시 취소합니다. 경직되거나 죽었을 때 호출됩니다.
        /// </summary>
        public void CancelAttack()
        {
            if (_actionTimer <= 0f)
            {
                return;
            }

            _actionTimer = 0f;
            OnAttackEnded();
        }

        // ====================================================================================================
        // 4. Template Methods
        // ====================================================================================================

        /// <summary>공격이 시작될 때 호출됩니다.</summary>
        protected virtual void OnAttackBegan(Unit target)
        {
        }

        /// <summary>공격이 진행되는 동안 매 프레임 호출됩니다.</summary>
        /// <param name="deltaTime">경과 시간입니다.</param>
        /// <param name="progress">동작 진행도(0~1)입니다.</param>
        protected virtual void OnActionTick(float deltaTime, float progress)
        {
        }

        /// <summary>공격이 끝날 때 호출됩니다. 무기를 대기 자세로 되돌리는 자리입니다.</summary>
        protected virtual void OnAttackEnded()
        {
            ReturnToRestPose();
        }

        /// <summary>공격하지 않는 동안 매 프레임 호출됩니다.</summary>
        protected virtual void OnIdleTick(float deltaTime)
        {
        }

        /// <summary>무기 모델이 없을 때 기본 형상을 만듭니다.</summary>
        protected abstract Transform BuildDefaultPivot();

        /// <summary>대기 자세를 기억합니다.</summary>
        protected abstract void CacheRestPose();

        /// <summary>대기 자세로 되돌립니다.</summary>
        protected abstract void ReturnToRestPose();
    }
}
