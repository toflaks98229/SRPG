namespace SRPG.Systems.Combat
{
    /// <summary>
    /// 지금 공격을 시작하지 못하는 이유입니다.
    ///
    /// <b>bool 대신 이유를 돌려주는 까닭</b>
    ///
    /// "왜 창병이 안 찌르는가"는 이 프로젝트에서 반복해서 물어야 했던 질문입니다.
    /// 참·거짓만 돌려주면 그 답을 얻으려고 조건문을 하나씩 주석 처리해 봐야 합니다.
    /// 이유를 값으로 들고 있으면 디버그 표시에 그대로 띄울 수 있습니다.
    /// </summary>
    public enum AttackBlock
    {
        /// <summary>막는 것이 없습니다. 공격을 시작해도 됩니다.</summary>
        None = 0,

        /// <summary>이전 공격 동작이 아직 끝나지 않았습니다.</summary>
        WeaponBusy,

        /// <summary>맞고 경직된 상태입니다.</summary>
        Staggered,

        /// <summary>공격 간격이 아직 지나지 않았습니다.</summary>
        OnCooldown,

        /// <summary>칠 대상이 없거나 이미 죽었습니다.</summary>
        NoTarget,

        /// <summary>이동 중이고, 이 병과는 이동 중에 공격할 수 없습니다.</summary>
        MovingRestricted,

        /// <summary>대상이 사거리 밖입니다.</summary>
        OutOfRange,
    }

    /// <summary>
    /// 공격을 시작해도 되는지 판단합니다.
    ///
    /// <b>무기 고유의 사정은 보지 않습니다.</b>
    /// 창병이 품 안을 내주어 무너진 상태 같은 것은 <c>WeaponBase.CanBeginAttack</c>이 봅니다.
    /// 여기서 보는 것은 병과를 가리지 않는 공통 조건뿐입니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 판단이라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public static class AttackGate
    {
        // ====================================================================================================
        // 1. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 공격 개시를 막는 첫 번째 이유를 반환합니다.
        ///
        /// <b>순서가 의미를 갖습니다.</b> 위쪽일수록 더 근본적인 사정이라, 여러 이유가 겹칠 때
        /// 표시되는 것은 "가장 먼저 해결해야 하는" 쪽입니다.
        /// </summary>
        /// <param name="input">공격 개시 판정에 필요한 관측값입니다.</param>
        /// <returns>
        /// 공격을 막는 첫 번째 이유입니다. 막는 것이 없으면 <see cref="AttackBlock.None"/>입니다.
        /// </returns>
        public static AttackBlock Evaluate(in AttackGateInput input)
        {
            if (input.WeaponBusy)
            {
                return AttackBlock.WeaponBusy;
            }

            if (input.StaggerTimer > 0f)
            {
                return AttackBlock.Staggered;
            }

            if (input.CooldownTimer > 0f)
            {
                return AttackBlock.OnCooldown;
            }

            if (!input.HasLivingTarget)
            {
                return AttackBlock.NoTarget;
            }

            // 창병 규칙: 이동 중에는 공격하지 않습니다. 자리를 먼저 잡도록 강제하는 장치입니다.
            if (input.IsMoving && !input.CanAttackWhileMoving)
            {
                return AttackBlock.MovingRestricted;
            }

            if (input.DistanceToTarget > input.AttackRange)
            {
                return AttackBlock.OutOfRange;
            }

            return AttackBlock.None;
        }
    }

    /// <summary>
    /// 공격 개시 판정에 필요한 관측값입니다.
    /// </summary>
    public readonly struct AttackGateInput
    {
        /// <summary>무기가 이전 동작을 진행 중인지 여부입니다.</summary>
        public readonly bool WeaponBusy;

        /// <summary>남은 경직 시간입니다.</summary>
        public readonly float StaggerTimer;

        /// <summary>남은 공격 재사용 대기 시간입니다.</summary>
        public readonly float CooldownTimer;

        /// <summary>살아 있는 교전 대상이 있는지 여부입니다.</summary>
        public readonly bool HasLivingTarget;

        /// <summary>스스로 이동 중인지 여부입니다. 넉백에 밀려나는 것은 포함하지 않습니다.</summary>
        public readonly bool IsMoving;

        /// <summary>이 병과가 이동 중에도 공격할 수 있는지 여부입니다.</summary>
        public readonly bool CanAttackWhileMoving;

        /// <summary>대상까지의 거리입니다.</summary>
        public readonly float DistanceToTarget;

        /// <summary>공격 사거리입니다.</summary>
        public readonly float AttackRange;

        /// <summary>관측값을 묶습니다.</summary>
        /// <param name="weaponBusy">무기가 이전 동작을 진행 중인지 여부입니다.</param>
        /// <param name="staggerTimer">남은 경직 시간입니다. 0보다 크면 막힙니다.</param>
        /// <param name="cooldownTimer">남은 공격 재사용 대기 시간입니다. 0보다 크면 막힙니다.</param>
        /// <param name="hasLivingTarget">살아 있는 교전 대상이 있는지 여부입니다.</param>
        /// <param name="isMoving">스스로 이동 중인지 여부입니다. 넉백에 밀려나는 것은 포함하지 않습니다.</param>
        /// <param name="canAttackWhileMoving">이 병과가 이동 중에도 공격할 수 있는지 여부입니다.</param>
        /// <param name="distanceToTarget">대상까지의 거리입니다.</param>
        /// <param name="attackRange">공격 사거리입니다.</param>
        public AttackGateInput(
            bool weaponBusy,
            float staggerTimer,
            float cooldownTimer,
            bool hasLivingTarget,
            bool isMoving,
            bool canAttackWhileMoving,
            float distanceToTarget,
            float attackRange)
        {
            WeaponBusy = weaponBusy;
            StaggerTimer = staggerTimer;
            CooldownTimer = cooldownTimer;
            HasLivingTarget = hasLivingTarget;
            IsMoving = isMoving;
            CanAttackWhileMoving = canAttackWhileMoving;
            DistanceToTarget = distanceToTarget;
            AttackRange = attackRange;
        }
    }
}
