using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Systems.Formation;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 슬롯 배정을 검증합니다.
    ///
    /// 이 배정이 하는 일은 두 가지입니다.
    ///   · 병사가 자기 자리까지 <b>대열을 가로지르지 않게</b> 한다
    ///   · 빈자리를 <b>가장 가까운 병사가 즉시 메우게</b> 한다 (진형 복구)
    ///
    /// 두 번째가 눈에 보이는 결과입니다. 앞줄이 맞고 밀려났을 때 뒷줄이 빨려 들어가듯 전진하는지,
    /// 아니면 밀려난 병사가 혼자 기어 돌아올 때까지 구멍이 남는지가 여기서 갈립니다.
    /// </summary>
    public sealed class SlotAssignerTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private static List<Vector3> Points(params float[] xs)
        {
            var list = new List<Vector3>();
            for (int i = 0; i < xs.Length; i++)
            {
                list.Add(new Vector3(xs[i], 0f, 0f));
            }

            return list;
        }

        // ====================================================================================================
        // 2. 가까운 자리 배정
        // ====================================================================================================

        [Test]
        public void 각자_가장_가까운_자리를_받는다()
        {
            // 병사는 0, 10 에 있고 슬롯도 0, 10 에 있습니다. 서로 엇갈리면 안 됩니다.
            var positions = Points(0f, 10f);
            var slots = Points(0f, 10f);
            var result = new List<int>();

            SlotAssigner.AssignNearest(positions, slots, result);

            Assert.AreEqual(0, result[0]);
            Assert.AreEqual(1, result[1]);
        }

        [Test]
        public void 목록_순서가_뒤집혀도_가까운_자리를_받는다()
        {
            // 순서대로 배분하는 구현이라면 여기서 둘이 서로를 가로질러 갑니다.
            var positions = Points(10f, 0f);
            var slots = Points(0f, 10f);
            var result = new List<int>();

            SlotAssigner.AssignNearest(positions, slots, result);

            Assert.AreEqual(1, result[0], "먼 쪽 병사가 가까운 슬롯을 가져갔습니다.");
            Assert.AreEqual(0, result[1]);
        }

        [Test]
        public void 모든_병사가_서로_다른_자리를_받는다()
        {
            var positions = Points(0f, 1f, 2f, 3f, 4f);
            var slots = Points(4f, 3f, 2f, 1f, 0f);
            var result = new List<int>();

            SlotAssigner.AssignNearest(positions, slots, result);

            var used = new HashSet<int>(result);
            Assert.AreEqual(positions.Count, used.Count, "같은 슬롯을 두 명이 받았습니다.");
        }

        // ====================================================================================================
        // 3. 진형 복구 — 이것이 핵심입니다
        // ====================================================================================================

        /// <summary>
        /// 앞줄 병사가 맞고 뒤로 밀려나면, 비어 버린 앞자리를 뒷줄이 받아야 합니다.
        /// 순서 배분이면 밀려난 병사가 여전히 그 자리의 주인이라 구멍이 남습니다.
        /// </summary>
        [Test]
        public void 밀려난_자리를_뒤에_있던_병사가_메운다()
        {
            var slots = Points(0f, 2f, 4f);

            // 원래 0번 자리에 있던 병사가 5까지 밀려났습니다.
            var positions = new List<Vector3>
            {
                new Vector3(5f, 0f, 0f),   // 밀려난 병사
                new Vector3(2f, 0f, 0f),   // 중간
                new Vector3(4f, 0f, 0f),   // 뒤
            };

            var result = new List<int>();
            SlotAssigner.AssignNearest(positions, slots, result);

            Assert.AreNotEqual(0, result[0], "밀려난 병사가 여전히 맨 앞자리를 붙잡고 있습니다.");

            // 맨 앞자리는 누군가가 받아야 합니다.
            CollectionAssert.Contains(result, 0, "빈 앞자리를 아무도 메우지 않았습니다.");
        }

        // ====================================================================================================
        // 4. 지휘관 고정
        // ====================================================================================================

        [Test]
        public void 지휘관은_중심_자리에_고정된다()
        {
            // 지휘관이 가장 먼 곳에 있어도 중심을 받아야 합니다.
            var positions = Points(9f, 0f, 1f);
            var slots = Points(0f, 3f, 6f);
            var result = new List<int>();

            SlotAssigner.AssignNearest(positions, slots, result, pinned: 0);

            Assert.AreEqual(0, result[0], "지휘관이 중심 자리를 받지 못했습니다.");
            Assert.AreNotEqual(0, result[1]);
            Assert.AreNotEqual(0, result[2]);
        }

        [Test]
        public void 지휘관이_없으면_전원이_가까운_자리를_받는다()
        {
            var positions = Points(0f, 3f);
            var slots = Points(0f, 3f);
            var result = new List<int>();

            SlotAssigner.AssignNearest(positions, slots, result, pinned: -1);

            Assert.AreEqual(0, result[0]);
            Assert.AreEqual(1, result[1]);
        }

        // ====================================================================================================
        // 5. 인원과 슬롯 수가 다를 때
        // ====================================================================================================

        [Test]
        public void 슬롯이_모자라면_남는_병사는_마지막_자리를_받는다()
        {
            var positions = Points(0f, 1f, 2f);
            var slots = Points(0f, 1f);
            var result = new List<int>();

            SlotAssigner.AssignNearest(positions, slots, result);

            Assert.AreEqual(3, result.Count);

            for (int i = 0; i < result.Count; i++)
            {
                Assert.GreaterOrEqual(result[i], 0);
                Assert.Less(result[i], slots.Count, "슬롯 범위를 벗어난 인덱스가 나왔습니다.");
            }
        }

        [Test]
        public void 슬롯이_남으면_남는_자리는_비워_둔다()
        {
            var positions = Points(0f, 5f);
            var slots = Points(0f, 5f, 10f, 15f);
            var result = new List<int>();

            SlotAssigner.AssignNearest(positions, slots, result);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(0, result[0]);
            Assert.AreEqual(1, result[1]);
        }

        // ====================================================================================================
        // 6. 방어적 동작
        // ====================================================================================================

        [Test]
        public void 빈_입력은_안전하다()
        {
            var result = new List<int>();

            Assert.DoesNotThrow(() => SlotAssigner.AssignNearest(null, Points(0f), result));
            Assert.AreEqual(0, result.Count);

            Assert.DoesNotThrow(() => SlotAssigner.AssignNearest(Points(0f), null, result));
            Assert.AreEqual(0, result.Count);

            Assert.DoesNotThrow(() => SlotAssigner.AssignNearest(new List<Vector3>(), new List<Vector3>(), result));
            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void 범위를_벗어난_지휘관_인덱스는_무시된다()
        {
            var positions = Points(0f, 3f);
            var slots = Points(0f, 3f);
            var result = new List<int>();

            Assert.DoesNotThrow(() => SlotAssigner.AssignNearest(positions, slots, result, pinned: 99));
            Assert.AreEqual(2, result.Count);
        }
    }
}
