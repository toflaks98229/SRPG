using NUnit.Framework;
using SRPG.Systems.Pathfinding;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// "자리를 지킬 것인가, 대열을 풀고 나설 것인가"를 검증합니다.
    ///
    /// <b>이 판단이 병과를 병과답게 만듭니다.</b>
    /// 창병과 궁수는 리시가 0이라 자리를 지키고, 검병만 한두 걸음 나가 벱니다.
    /// 예전에는 이 규칙이 <c>Unit.Update</c> 한복판에서 조향·넉백과 뒤엉켜 있어
    /// "왜 저 병사가 안 나가는가"를 씬을 재생해 가며 찾아야 했습니다.
    /// </summary>
    public sealed class ApproachSolverTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private const float AttackRange = 1.3f;

        private static ApproachRequest Request(
            Vector3 position,
            Vector3 slot,
            Vector3? target = null,
            float leash = 0f)
        {
            return new ApproachRequest(
                position,
                slot,
                target.HasValue,
                target ?? Vector3.zero,
                AttackRange,
                leash);
        }

        // ====================================================================================================
        // 2. 기본 — 자기 슬롯으로 간다
        // ====================================================================================================

        [Test]
        public void 적이_없으면_자기_슬롯으로_간다()
        {
            var slot = new Vector3(5f, 0f, 0f);

            var plan = ApproachSolver.Resolve(Request(Vector3.zero, slot));

            Assert.AreEqual(slot, plan.Destination);
            Assert.IsFalse(plan.AtSlot);
            Assert.IsFalse(plan.Engaging);
        }

        [Test]
        public void 슬롯에_가까우면_도착으로_본다()
        {
            var slot = Vector3.zero;
            var position = new Vector3(ApproachSolver.SlotArrivalThreshold * 0.5f, 0f, 0f);

            Assert.IsTrue(ApproachSolver.Resolve(Request(position, slot)).AtSlot);
        }

        /// <summary>
        /// 도착 판정은 반드시 평면 거리여야 합니다.
        /// 비탈에 선 병사는 슬롯과 높이가 다르므로, 3차원으로 재면
        /// 제자리에 서 있는데도 영영 "도착하지 않은" 것이 됩니다.
        /// </summary>
        [Test]
        public void 도착_판정은_높이를_보지_않는다()
        {
            var slot = Vector3.zero;
            var onSlope = new Vector3(0f, 4f, 0f);

            Assert.IsTrue(ApproachSolver.Resolve(Request(onSlope, slot)).AtSlot);
        }

        // ====================================================================================================
        // 3. 자리를 지키는 병과 (창병·궁수, 리시 0)
        // ====================================================================================================

        [Test]
        public void 리시가_0이면_사거리_밖의_적을_쫓지_않는다()
        {
            var slot = Vector3.zero;
            var farEnemy = new Vector3(6f, 0f, 0f);

            var plan = ApproachSolver.Resolve(Request(slot, slot, farEnemy));

            Assert.AreEqual(slot, plan.Destination, "자리를 지켜야 할 병과가 적을 쫓아 나갔습니다.");
            Assert.IsFalse(plan.Engaging);
        }

        [Test]
        public void 리시가_0이어도_슬롯에서_사거리_안이면_멈춰_싸운다()
        {
            var slot = Vector3.zero;
            var closeEnemy = new Vector3(AttackRange * 0.8f, 0f, 0f);

            var plan = ApproachSolver.Resolve(Request(slot, slot, closeEnemy));

            Assert.AreEqual(slot, plan.Destination, "사거리 안인데 자리를 벗어났습니다.");
            Assert.IsTrue(plan.Engaging);
        }

        /// <summary>
        /// 행군 중(슬롯에서 멀리 떨어진 상태)인 창병은 눈앞에 적이 있어도 대열을 유지해야 합니다.
        /// </summary>
        [Test]
        public void 리시가_0이면_행군_중_눈앞의_적에게도_붙지_않는다()
        {
            var slot = new Vector3(20f, 0f, 0f);
            var position = Vector3.zero;
            var enemyRightHere = new Vector3(0.5f, 0f, 0f);

            var plan = ApproachSolver.Resolve(Request(position, slot, enemyRightHere));

            Assert.AreEqual(slot, plan.Destination);
            Assert.IsFalse(plan.Engaging);
        }

        // ====================================================================================================
        // 4. 대열을 풀 수 있는 병과 (검병)
        // ====================================================================================================

        [Test]
        public void 리시_안의_적에게는_다가간다()
        {
            var slot = Vector3.zero;
            var enemy = new Vector3(3f, 0f, 0f);

            var plan = ApproachSolver.Resolve(Request(slot, slot, enemy, leash: 4f));

            Assert.AreEqual(enemy, plan.Destination, "리시 안의 적에게 다가가지 않았습니다.");
            Assert.IsTrue(plan.Engaging);
        }

        /// <summary>
        /// 리시는 <b>자기 자리</b>에서 잽니다. 분대가 지나가 버리면 거리가 벌어져
        /// 자연히 교전을 접고 따라붙습니다 — 별도의 "복귀" 규칙이 필요 없습니다.
        /// </summary>
        [Test]
        public void 리시는_슬롯에서_재므로_분대가_지나가면_교전을_접는다()
        {
            var position = Vector3.zero;
            var enemy = new Vector3(1.5f, 0f, 0f);

            // 처음에는 슬롯이 병사 근처라 적이 리시 안입니다.
            var near = ApproachSolver.Resolve(
                new ApproachRequest(position, position, true, enemy, AttackRange, 4f));

            Assert.IsTrue(near.Engaging);

            // 분대가 멀리 지나가면 같은 적이 리시 밖이 됩니다.
            var slotMovedAway = new Vector3(30f, 0f, 0f);

            var far = ApproachSolver.Resolve(
                new ApproachRequest(position, slotMovedAway, true, enemy, AttackRange, 4f));

            Assert.AreEqual(slotMovedAway, far.Destination, "분대를 따라가지 않고 적에게 붙어 있습니다.");
            Assert.IsFalse(far.Engaging);
        }

        [Test]
        public void 행군_중에도_리시_안의_적에게는_붙는다()
        {
            var slot = new Vector3(10f, 0f, 0f);
            var position = new Vector3(6f, 0f, 0f);
            var enemyOnTheWay = new Vector3(8f, 0f, 0f);

            var plan = ApproachSolver.Resolve(Request(position, slot, enemyOnTheWay, leash: 4f));

            Assert.IsFalse(plan.AtSlot, "전제가 어긋났습니다. 아직 도착하지 않은 상태여야 합니다.");
            Assert.AreEqual(enemyOnTheWay, plan.Destination, "행군로에 선 적을 그냥 지나쳐 갔습니다.");
        }

        [Test]
        public void 사거리_안에_들면_멈춘다()
        {
            var slot = Vector3.zero;
            var position = new Vector3(1f, 0f, 0f);
            var enemy = new Vector3(1f + AttackRange * 0.5f, 0f, 0f);

            var plan = ApproachSolver.Resolve(Request(position, slot, enemy, leash: 4f));

            Assert.AreEqual(position, plan.Destination, "사거리 안인데 계속 파고들었습니다.");
            Assert.IsTrue(plan.Engaging);
        }
    }
}
