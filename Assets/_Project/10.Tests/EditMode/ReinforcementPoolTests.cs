using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Data;
using SRPG.Systems.Deployment;

namespace SRPG.Tests
{
    /// <summary>
    /// 지원군 투입 규칙을 검증합니다.
    ///
    /// <b>파도와 무엇이 다른가</b>
    ///
    /// 예전 웨이브는 시각에 맞춰 밀려왔습니다. 여기서는 전장에 <b>자리가 나야</b> 들어옵니다.
    /// 그 차이가 전술을 바꿉니다 — 적을 빨리 갈아 내면 그만큼 빨리 다음 부대를 마주하고,
    /// 내 부대를 아끼면 전장에 오래 남아 지원군이 늦게 들어옵니다.
    ///
    /// 이 규칙이 어긋나면 전장이 텅 비거나(투입이 막힘) 한꺼번에 쏟아집니다(상한이 안 먹힘).
    /// 둘 다 예외를 내지 않고 "전투가 이상하다"로만 보입니다.
    /// </summary>
    public sealed class ReinforcementPoolTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private static List<SquadOrder> Roster(int count)
        {
            var orders = new List<SquadOrder>(count);

            for (int i = 0; i < count; i++)
            {
                orders.Add(new SquadOrder { Id = i + 1, SoldierCount = 5 });
            }

            return orders;
        }

        // ====================================================================================================
        // 2. 초기 전개
        // ====================================================================================================

        /// <summary>
        /// 전투가 시작되면 양측이 <b>이미 서 있어야</b> 합니다.
        /// 상한만큼은 간격 없이 한 번에 채웁니다.
        /// </summary>
        [Test]
        public void 시작하면_상한까지_한_번에_채운다()
        {
            var pool = new ReinforcementPool(Roster(6), fieldCap: 3, interval: 5f);
            var deployed = new List<SquadOrder>();

            Assert.AreEqual(3, pool.FillField(0, deployed));
            Assert.AreEqual(3, deployed.Count);
            Assert.AreEqual(3, pool.Remaining);
        }

        [Test]
        public void 서열이_상한보다_적으면_있는_만큼만_나간다()
        {
            var pool = new ReinforcementPool(Roster(2), fieldCap: 5);
            var deployed = new List<SquadOrder>();

            Assert.AreEqual(2, pool.FillField(0, deployed));
            Assert.IsTrue(pool.IsExhausted);
        }

        [Test]
        public void 투입_순서는_서열_순서다()
        {
            var pool = new ReinforcementPool(Roster(4), fieldCap: 2);
            var deployed = new List<SquadOrder>();

            pool.FillField(0, deployed);

            Assert.AreEqual(1, deployed[0].Id);
            Assert.AreEqual(2, deployed[1].Id);

            Assert.IsTrue(pool.TryDeploy(1, out var third));
            Assert.AreEqual(3, third.Id, "서열을 건너뛰었습니다.");
        }

        // ====================================================================================================
        // 3. 상한
        // ====================================================================================================

        [Test]
        public void 전장이_가득_차면_내보내지_않는다()
        {
            var pool = new ReinforcementPool(Roster(6), fieldCap: 3);

            Assert.IsFalse(pool.TryDeploy(squadsOnField: 3, out _), "상한을 넘겨 내보냈습니다.");
            Assert.AreEqual(6, pool.Remaining);
        }

        /// <summary>
        /// <b>앞 부대가 무너지면 뒤가 올라옵니다.</b> 이것이 이 게임의 지원군 규칙입니다.
        /// </summary>
        [Test]
        public void 자리가_나면_다음_분대가_올라온다()
        {
            var pool = new ReinforcementPool(Roster(6), fieldCap: 3);
            var deployed = new List<SquadOrder>();

            pool.FillField(0, deployed);
            Assert.AreEqual(3, pool.Remaining);

            // 한 분대가 무너졌습니다.
            Assert.IsTrue(pool.TryDeploy(squadsOnField: 2, out var next));
            Assert.AreEqual(4, next.Id);
            Assert.AreEqual(2, pool.Remaining);
        }

        [Test]
        public void 상한은_최소_하나다()
        {
            var pool = new ReinforcementPool(Roster(3), fieldCap: 0);

            Assert.AreEqual(1, pool.FieldCap, "상한이 0이면 아무도 전장에 서지 못합니다.");
            Assert.IsTrue(pool.TryDeploy(0, out _));
        }

        // ====================================================================================================
        // 4. 소진
        // ====================================================================================================

        [Test]
        public void 다_내보내면_소진된다()
        {
            var pool = new ReinforcementPool(Roster(2), fieldCap: 1);

            Assert.IsTrue(pool.TryDeploy(0, out _));
            Assert.IsFalse(pool.IsExhausted);

            Assert.IsTrue(pool.TryDeploy(0, out _));
            Assert.IsTrue(pool.IsExhausted, "다 내보냈는데 소진으로 보지 않습니다.");

            Assert.IsFalse(pool.TryDeploy(0, out _));
        }

        [Test]
        public void 빈_서열은_처음부터_소진이다()
        {
            var pool = new ReinforcementPool(new List<SquadOrder>(), fieldCap: 3);

            Assert.IsTrue(pool.IsExhausted);
            Assert.AreEqual(0, pool.Remaining);
            Assert.IsFalse(pool.TryDeploy(0, out _));
        }

        [Test]
        public void 서열이_없어도_터지지_않는다()
        {
            var pool = new ReinforcementPool(null, fieldCap: 3);

            Assert.IsTrue(pool.IsExhausted);
            Assert.IsFalse(pool.TryDeploy(0, out _));
        }

        // ====================================================================================================
        // 5. 투입 간격
        // ====================================================================================================

        /// <summary>
        /// 자리만 보고 즉시 내보내면 앞 부대가 쓰러진 그 자리에 다음 부대가 튀어나옵니다.
        /// 눈으로 읽을 수 없고, 무엇보다 이겼다는 감각이 사라집니다.
        /// </summary>
        [Test]
        public void 간격이_지나기_전에는_내보내지_않는다()
        {
            var pool = new ReinforcementPool(Roster(4), fieldCap: 2, interval: 3f);
            var deployed = new List<SquadOrder>();

            pool.FillField(0, deployed);

            Assert.IsFalse(pool.TryDeploy(1, out _), "간격을 무시하고 곧바로 올라왔습니다.");

            pool.Tick(1f);
            Assert.IsFalse(pool.TryDeploy(1, out _));

            pool.Tick(2.5f);
            Assert.IsTrue(pool.TryDeploy(1, out _), "간격이 지났는데도 올라오지 않았습니다.");
        }

        [Test]
        public void 간격이_0이면_자리가_나는_대로_올라온다()
        {
            var pool = new ReinforcementPool(Roster(4), fieldCap: 2, interval: 0f);
            var deployed = new List<SquadOrder>();

            pool.FillField(0, deployed);

            Assert.IsTrue(pool.TryDeploy(1, out _));
            Assert.IsTrue(pool.TryDeploy(1, out _));
        }

        [Test]
        public void 남은_시간을_읽을_수_있다()
        {
            var pool = new ReinforcementPool(Roster(4), fieldCap: 2, interval: 4f);
            var deployed = new List<SquadOrder>();

            pool.FillField(0, deployed);
            Assert.AreEqual(4f, pool.TimeUntilNext, 0.001f);

            pool.Tick(1.5f);
            Assert.AreEqual(2.5f, pool.TimeUntilNext, 0.001f);
        }

        [Test]
        public void 남은_시간은_음수가_되지_않는다()
        {
            var pool = new ReinforcementPool(Roster(4), fieldCap: 2, interval: 1f);
            var deployed = new List<SquadOrder>();

            pool.FillField(0, deployed);
            pool.Tick(99f);

            Assert.AreEqual(0f, pool.TimeUntilNext, 0.001f);
        }

        /// <summary>
        /// 간격은 <b>내보낸 뒤</b>에 겁니다. 그래야 한 번에 여러 자리가 비어도
        /// 분대가 하나씩 시차를 두고 올라옵니다.
        /// </summary>
        [Test]
        public void 여러_자리가_비어도_한_번에_하나씩_올라온다()
        {
            var pool = new ReinforcementPool(Roster(5), fieldCap: 4, interval: 2f);
            var deployed = new List<SquadOrder>();

            pool.FillField(0, deployed);   // 4개 나가고 1개 남음
            Assert.AreEqual(1, pool.Remaining);

            pool.Tick(2f);

            // 전장이 통째로 비었지만 한 번 부르면 하나만 나옵니다.
            Assert.IsTrue(pool.TryDeploy(0, out _));
            Assert.IsFalse(pool.TryDeploy(0, out _), "간격 없이 연달아 나왔습니다.");
        }
    }
}
