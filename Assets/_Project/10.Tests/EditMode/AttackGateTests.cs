using NUnit.Framework;
using SRPG.Systems.Combat;

namespace SRPG.Tests
{
    /// <summary>
    /// 공격을 시작해도 되는지의 판정을 검증합니다.
    ///
    /// <b>여기서 가장 중요한 것은 창병 규칙입니다.</b>
    /// "이동 중에는 찌르지 않는다"가 창병을 버티는 병과로 만들고,
    /// 그래서 진형이 흔들리면 창병이 아무것도 못 하게 됩니다.
    /// 규칙 하나가 병과의 성격을 통째로 결정하므로, 조용히 뒤집히면 안 됩니다.
    /// </summary>
    public sealed class AttackGateTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        /// <summary>아무것도 막지 않는 상태입니다. 여기서 하나씩 비틀어 봅니다.</summary>
        private static AttackGateInput Clear(
            bool weaponBusy = false,
            float staggerTimer = -1f,
            float cooldownTimer = -1f,
            bool hasLivingTarget = true,
            bool isMoving = false,
            bool canAttackWhileMoving = true,
            float distanceToTarget = 1f,
            float attackRange = 2f)
        {
            return new AttackGateInput(
                weaponBusy,
                staggerTimer,
                cooldownTimer,
                hasLivingTarget,
                isMoving,
                canAttackWhileMoving,
                distanceToTarget,
                attackRange);
        }

        // ====================================================================================================
        // 2. 통과
        // ====================================================================================================

        [Test]
        public void 조건이_모두_맞으면_막지_않는다()
        {
            Assert.AreEqual(AttackBlock.None, AttackGate.Evaluate(Clear()));
        }

        [Test]
        public void 사거리_경계는_통과시킨다()
        {
            Assert.AreEqual(
                AttackBlock.None,
                AttackGate.Evaluate(Clear(distanceToTarget: 2f, attackRange: 2f)));
        }

        // ====================================================================================================
        // 3. 개별 차단 사유
        // ====================================================================================================

        [Test]
        public void 무기가_동작_중이면_막는다()
        {
            Assert.AreEqual(AttackBlock.WeaponBusy, AttackGate.Evaluate(Clear(weaponBusy: true)));
        }

        [Test]
        public void 경직_중이면_막는다()
        {
            Assert.AreEqual(AttackBlock.Staggered, AttackGate.Evaluate(Clear(staggerTimer: 0.2f)));
        }

        [Test]
        public void 재사용_대기_중이면_막는다()
        {
            Assert.AreEqual(AttackBlock.OnCooldown, AttackGate.Evaluate(Clear(cooldownTimer: 0.4f)));
        }

        [Test]
        public void 표적이_없으면_막는다()
        {
            Assert.AreEqual(AttackBlock.NoTarget, AttackGate.Evaluate(Clear(hasLivingTarget: false)));
        }

        [Test]
        public void 사거리_밖이면_막는다()
        {
            Assert.AreEqual(
                AttackBlock.OutOfRange,
                AttackGate.Evaluate(Clear(distanceToTarget: 2.5f, attackRange: 2f)));
        }

        // ====================================================================================================
        // 4. 창병 규칙
        // ====================================================================================================

        [Test]
        public void 이동_중_공격이_금지된_병과는_걸으면_못_친다()
        {
            Assert.AreEqual(
                AttackBlock.MovingRestricted,
                AttackGate.Evaluate(Clear(isMoving: true, canAttackWhileMoving: false)));
        }

        [Test]
        public void 이동_중_공격이_허용된_병과는_걸으면서도_친다()
        {
            Assert.AreEqual(
                AttackBlock.None,
                AttackGate.Evaluate(Clear(isMoving: true, canAttackWhileMoving: true)));
        }

        /// <summary>
        /// 제자리에서 고개만 돌리는 것은 이동이 아닙니다.
        /// 창병이 위협 쪽으로 몸을 틀면서도 계속 찌를 수 있어야 방어선이 성립합니다.
        /// </summary>
        [Test]
        public void 멈춰_있으면_이동_금지_병과도_친다()
        {
            Assert.AreEqual(
                AttackBlock.None,
                AttackGate.Evaluate(Clear(isMoving: false, canAttackWhileMoving: false)));
        }

        // ====================================================================================================
        // 5. 우선순위
        // ====================================================================================================

        /// <summary>
        /// 여러 사유가 겹칠 때 표시되는 것은 "가장 먼저 해결해야 하는" 쪽이어야 합니다.
        /// 디버그 표시가 매번 다른 이유를 가리키면 원인 추적에 쓸 수 없습니다.
        /// </summary>
        [Test]
        public void 사유가_겹치면_더_근본적인_쪽을_돌려준다()
        {
            var everything = new AttackGateInput(
                weaponBusy: true,
                staggerTimer: 1f,
                cooldownTimer: 1f,
                hasLivingTarget: false,
                isMoving: true,
                canAttackWhileMoving: false,
                distanceToTarget: 99f,
                attackRange: 1f);

            Assert.AreEqual(AttackBlock.WeaponBusy, AttackGate.Evaluate(everything));
        }

        [Test]
        public void 경직이_재사용_대기보다_먼저다()
        {
            Assert.AreEqual(
                AttackBlock.Staggered,
                AttackGate.Evaluate(Clear(staggerTimer: 1f, cooldownTimer: 1f)));
        }

        /// <summary>
        /// 표적이 없는 것이 사거리 문제보다 먼저입니다.
        /// 대상이 없는데 "사거리 밖"이라고 말하면 엉뚱한 곳을 뒤지게 됩니다.
        /// </summary>
        [Test]
        public void 표적_없음이_사거리보다_먼저다()
        {
            Assert.AreEqual(
                AttackBlock.NoTarget,
                AttackGate.Evaluate(Clear(hasLivingTarget: false, distanceToTarget: 99f)));
        }
    }
}
