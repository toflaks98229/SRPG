using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Gameplay.Units;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 공격 예약 장부를 검증합니다.
    ///
    /// <b>왜 이 규칙이 필요한가</b>
    ///
    /// 창병 여럿이 최전방의 한 명만 동시에 찌르면 그가 죽은 자리로 뒤따라오던 적들이 그대로 통과합니다.
    /// 방어선의 목적은 잘 죽이는 것이 아니라 <b>새지 않는 것</b>입니다.
    ///
    /// 그리고 정원이 체력에서 나오는 덕에, "덩치 큰 적에게는 여럿이 달라붙는다"가
    /// 별도 규칙 없이 성립합니다. 그 파생을 여기서 못 박아 둡니다.
    /// </summary>
    public sealed class AttackerSlotsTests
    {
        // ====================================================================================================
        // 1. Setup / Teardown
        // ====================================================================================================

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();
        }

        /// <summary>
        /// 예약자 역할만 하는 유닛입니다. 장부는 참조 동일성과 생존 여부만 보므로
        /// 전투 컨텍스트 없이도 충분합니다.
        /// </summary>
        private Unit CreateAttacker()
        {
            var go = new GameObject("Attacker");
            _spawned.Add(go);

            return go.AddComponent<Unit>();
        }

        // ====================================================================================================
        // 2. 정원 계산 — 체력에서 파생됩니다
        // ====================================================================================================

        [Test]
        public void 한_방에_죽는_적에게는_한_명만_붙는다()
        {
            Assert.AreEqual(1, AttackerSlots.ComputeCapacity(remainingHealth: 8f, damagePerHit: 10f, maxAttackers: 4));
        }

        [Test]
        public void 여러_대를_맞아야_하는_적에게는_여럿이_붙는다()
        {
            Assert.AreEqual(3, AttackerSlots.ComputeCapacity(remainingHealth: 30f, damagePerHit: 10f, maxAttackers: 4));
        }

        [Test]
        public void 정원은_상한을_넘지_않는다()
        {
            Assert.AreEqual(4, AttackerSlots.ComputeCapacity(remainingHealth: 500f, damagePerHit: 1f, maxAttackers: 4));
        }

        [Test]
        public void 정원은_최소_한_명이다()
        {
            Assert.AreEqual(1, AttackerSlots.ComputeCapacity(remainingHealth: 0.001f, damagePerHit: 10f, maxAttackers: 4));
            Assert.AreEqual(1, AttackerSlots.ComputeCapacity(remainingHealth: 10f, damagePerHit: 10f, maxAttackers: 0));
        }

        /// <summary>
        /// 피해량이 0에 가까우면 나눗셈이 무의미해집니다. 상한을 그대로 씁니다.
        /// </summary>
        [Test]
        public void 피해량이_0이면_상한을_그대로_쓴다()
        {
            Assert.AreEqual(4, AttackerSlots.ComputeCapacity(remainingHealth: 30f, damagePerHit: 0f, maxAttackers: 4));
        }

        // ====================================================================================================
        // 3. 예약
        // ====================================================================================================

        [Test]
        public void 자리가_있으면_예약된다()
        {
            var slots = new AttackerSlots();

            Assert.IsTrue(slots.TryCommit(CreateAttacker(), 30f, 10f, 4));
            Assert.AreEqual(1, slots.Count);
        }

        [Test]
        public void 정원이_차면_거절한다()
        {
            var slots = new AttackerSlots();

            // 체력 8, 한 방 10 → 정원 1명
            Assert.IsTrue(slots.TryCommit(CreateAttacker(), 8f, 10f, 4));
            Assert.IsFalse(slots.TryCommit(CreateAttacker(), 8f, 10f, 4), "정원을 넘겨 예약했습니다.");

            Assert.AreEqual(1, slots.Count);
        }

        [Test]
        public void 같은_예약자는_두_번_세지_않는다()
        {
            var slots = new AttackerSlots();
            var attacker = CreateAttacker();

            Assert.IsTrue(slots.TryCommit(attacker, 8f, 10f, 4));
            Assert.IsTrue(slots.TryCommit(attacker, 8f, 10f, 4), "이미 잡아 둔 자리를 잃었습니다.");

            Assert.AreEqual(1, slots.Count);
        }

        [Test]
        public void 예약을_놓으면_자리가_난다()
        {
            var slots = new AttackerSlots();
            var first = CreateAttacker();

            slots.TryCommit(first, 8f, 10f, 4);
            slots.Release(first);

            Assert.AreEqual(0, slots.Count);
            Assert.IsTrue(slots.TryCommit(CreateAttacker(), 8f, 10f, 4));
        }

        // ====================================================================================================
        // 4. 조회 — 예약을 잡지 않습니다
        // ====================================================================================================

        [Test]
        public void 자리_조회는_예약을_잡지_않는다()
        {
            var slots = new AttackerSlots();

            Assert.IsTrue(slots.HasRoom(CreateAttacker(), 8f, 10f, 4));
            Assert.AreEqual(0, slots.Count, "조회만 했는데 자리가 잡혔습니다.");
        }

        [Test]
        public void 이미_예약한_쪽에게는_언제나_자리가_있다()
        {
            var slots = new AttackerSlots();
            var attacker = CreateAttacker();

            slots.TryCommit(attacker, 8f, 10f, 4);

            Assert.IsTrue(slots.HasRoom(attacker, 8f, 10f, 4));
            Assert.IsFalse(slots.HasRoom(CreateAttacker(), 8f, 10f, 4), "남에게도 자리가 있다고 답했습니다.");
        }

        // ====================================================================================================
        // 5. 정리
        // ====================================================================================================

        /// <summary>
        /// 죽은 예약자가 자리를 붙잡고 있으면, 그 적을 아무도 치지 못하게 됩니다.
        /// </summary>
        [Test]
        public void 사라진_예약자는_자리를_비운다()
        {
            var slots = new AttackerSlots();
            var doomed = CreateAttacker();

            slots.TryCommit(doomed, 8f, 10f, 4);
            Assert.AreEqual(1, slots.Count);

            Object.DestroyImmediate(doomed.gameObject);

            slots.Prune();

            Assert.AreEqual(0, slots.Count, "파괴된 예약자가 자리를 붙잡고 있습니다.");
        }

        [Test]
        public void 사라진_예약자가_있으면_새_예약이_들어갈_수_있다()
        {
            var slots = new AttackerSlots();
            var doomed = CreateAttacker();

            // 정원 1명을 채웁니다.
            slots.TryCommit(doomed, 8f, 10f, 4);

            Object.DestroyImmediate(doomed.gameObject);

            Assert.IsTrue(
                slots.TryCommit(CreateAttacker(), 8f, 10f, 4),
                "죽은 예약자 때문에 아무도 이 적을 치지 못합니다.");
        }

        [Test]
        public void 통째로_비울_수_있다()
        {
            var slots = new AttackerSlots();

            slots.TryCommit(CreateAttacker(), 100f, 10f, 4);
            slots.TryCommit(CreateAttacker(), 100f, 10f, 4);

            slots.Clear();

            Assert.AreEqual(0, slots.Count);
        }
    }
}
