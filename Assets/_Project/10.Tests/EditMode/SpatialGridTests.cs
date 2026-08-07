using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Systems.Spatial;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 공간 격자를 검증합니다.
    ///
    /// 이 자료구조가 대체하는 것은 <b>전수 조사</b>입니다. 전수 조사는 느리지만 절대 틀리지 않습니다.
    /// 그래서 검증의 중심은 성능이 아니라 **"빠른 답이 느린 답과 같은가"** 입니다.
    /// 무작위 배치 여러 벌에 대해 두 방식의 결과를 직접 대조합니다.
    /// </summary>
    public sealed class SpatialGridTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        /// <summary>테스트용 표식입니다. 격자는 대상의 타입을 모르므로 아무 참조 타입이나 됩니다.</summary>
        private sealed class Marker
        {
            public readonly int Id;
            public Marker(int id) => Id = id;
            public override string ToString() => $"M{Id}";
        }

        private static SpatialGrid<Marker> CreateGrid(float cellSize = 2f)
        {
            // -30..30 범위를 덮는 격자입니다.
            return new SpatialGrid<Marker>(new Vector3(-30f, 0f, -30f), 60f, 60f, cellSize);
        }

        /// <summary>전수 조사로 구한 정답입니다.</summary>
        private static HashSet<Marker> BruteForce(
            IReadOnlyList<(Vector3 Position, Marker Item)> items,
            Vector3 center,
            float radius)
        {
            var result = new HashSet<Marker>();
            float sqr = radius * radius;

            for (int i = 0; i < items.Count; i++)
            {
                if ((items[i].Position - center).sqrMagnitude <= sqr)
                {
                    result.Add(items[i].Item);
                }
            }

            return result;
        }

        // ====================================================================================================
        // 2. 전수 조사와의 대조
        // ====================================================================================================

        /// <summary>
        /// <b>이 테스트가 이 파일의 핵심입니다.</b>
        /// 무작위 배치와 무작위 질의 여러 벌에 대해, 공간 격자의 답이 전수 조사와 정확히 일치해야 합니다.
        /// </summary>
        [TestCase(1f)]
        [TestCase(2f)]
        [TestCase(5f)]
        public void 질의_결과가_전수_조사와_정확히_같다(float cellSize)
        {
            var random = new System.Random(20260807);
            var grid = CreateGrid(cellSize);
            var items = new List<(Vector3, Marker)>();
            var buffer = new List<Marker>();

            for (int trial = 0; trial < 20; trial++)
            {
                items.Clear();
                grid.Clear();

                int count = 1 + random.Next(120);
                for (int i = 0; i < count; i++)
                {
                    var position = new Vector3(
                        (float)(random.NextDouble() * 60.0 - 30.0),
                        0f,
                        (float)(random.NextDouble() * 60.0 - 30.0));

                    var marker = new Marker(i);
                    items.Add((position, marker));
                    grid.Insert(position, marker);
                }

                Assert.AreEqual(count, grid.Count, "삽입한 수와 색인된 수가 다릅니다.");

                for (int q = 0; q < 10; q++)
                {
                    var center = new Vector3(
                        (float)(random.NextDouble() * 60.0 - 30.0),
                        0f,
                        (float)(random.NextDouble() * 60.0 - 30.0));

                    float radius = (float)(random.NextDouble() * 12.0 + 0.5);

                    grid.Query(center, radius, buffer);

                    var expected = BruteForce(items, center, radius);

                    Assert.AreEqual(
                        expected.Count,
                        buffer.Count,
                        $"trial={trial} q={q} cell={cellSize} 반경={radius:F2} 결과 개수가 다릅니다.");

                    for (int i = 0; i < buffer.Count; i++)
                    {
                        Assert.IsTrue(
                            expected.Contains(buffer[i]),
                            $"전수 조사에 없는 대상이 나왔습니다: {buffer[i]}");
                    }
                }
            }
        }

        // ====================================================================================================
        // 3. 기본 동작
        // ====================================================================================================

        [Test]
        public void 반경_밖의_대상은_나오지_않는다()
        {
            var grid = CreateGrid();
            var buffer = new List<Marker>();

            var near = new Marker(1);
            var far = new Marker(2);

            grid.Insert(new Vector3(0f, 0f, 0f), near);
            grid.Insert(new Vector3(20f, 0f, 0f), far);

            grid.Query(Vector3.zero, 5f, buffer);

            CollectionAssert.AreEquivalent(new[] { near }, buffer);
        }

        [Test]
        public void 반경_경계의_대상은_포함된다()
        {
            var grid = CreateGrid();
            var buffer = new List<Marker>();

            var onEdge = new Marker(1);
            grid.Insert(new Vector3(5f, 0f, 0f), onEdge);

            grid.Query(Vector3.zero, 5f, buffer);

            Assert.AreEqual(1, buffer.Count, "반경과 정확히 같은 거리의 대상이 누락되었습니다.");
        }

        [Test]
        public void 높이는_반경_판정에_포함된다()
        {
            // 셀 분할은 XZ만 쓰지만 거리 확인은 3차원입니다.
            // 상륙정 위의 적과 해변의 아군이 같은 XZ에 있어도 거리가 다릅니다.
            var grid = CreateGrid();
            var buffer = new List<Marker>();

            grid.Insert(new Vector3(0f, 10f, 0f), new Marker(1));

            grid.Query(Vector3.zero, 5f, buffer);

            Assert.AreEqual(0, buffer.Count, "높이 차가 반경 판정에서 무시되었습니다.");
        }

        [Test]
        public void 반경이_0이하면_아무것도_나오지_않는다()
        {
            var grid = CreateGrid();
            var buffer = new List<Marker>();

            grid.Insert(Vector3.zero, new Marker(1));

            grid.Query(Vector3.zero, 0f, buffer);
            Assert.AreEqual(0, buffer.Count);

            grid.Query(Vector3.zero, -1f, buffer);
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void 비우면_아무것도_남지_않는다()
        {
            var grid = CreateGrid();
            var buffer = new List<Marker>();

            for (int i = 0; i < 50; i++)
            {
                grid.Insert(new Vector3(i % 10, 0f, i / 10), new Marker(i));
            }

            grid.Clear();

            Assert.AreEqual(0, grid.Count);
            Assert.AreEqual(0, grid.Query(Vector3.zero, 100f, buffer));
        }

        [Test]
        public void 다시_만들어도_이전_내용이_섞이지_않는다()
        {
            // 매 프레임 Clear → Insert 를 반복하는 것이 실제 사용 방식입니다.
            var grid = CreateGrid();
            var buffer = new List<Marker>();

            for (int frame = 0; frame < 5; frame++)
            {
                grid.Clear();

                var current = new Marker(frame);
                grid.Insert(new Vector3(frame, 0f, 0f), current);

                grid.Query(Vector3.zero, 100f, buffer);

                Assert.AreEqual(1, buffer.Count, $"frame={frame} 이전 프레임의 대상이 남아 있습니다.");
                Assert.AreSame(current, buffer[0]);
            }
        }

        // ====================================================================================================
        // 4. 영역 밖 처리
        // ====================================================================================================

        [Test]
        public void 영역_밖의_대상도_누락되지_않는다()
        {
            // 물에 빠지는 중이거나 먼바다의 상륙정에 있는 유닛이 여기 해당합니다.
            // 버리면 조용히 결과가 비어 버리므로, 가장자리에 붙이더라도 찾을 수 있어야 합니다.
            var grid = CreateGrid();
            var buffer = new List<Marker>();

            var outside = new Marker(1);
            grid.Insert(new Vector3(500f, 0f, 500f), outside);

            grid.Query(new Vector3(500f, 0f, 500f), 1f, buffer);

            Assert.AreEqual(1, buffer.Count, "영역 밖의 대상이 누락되었습니다.");
            Assert.AreSame(outside, buffer[0]);
        }

        [Test]
        public void 영역_밖의_대상이_영역_안_질의에_섞이지_않는다()
        {
            var grid = CreateGrid();
            var buffer = new List<Marker>();

            grid.Insert(new Vector3(500f, 0f, 500f), new Marker(1));

            // 가장자리 셀에 붙었더라도 실제 거리로 걸러져야 합니다.
            grid.Query(new Vector3(28f, 0f, 28f), 5f, buffer);

            Assert.AreEqual(0, buffer.Count, "멀리 있는 대상이 가장자리 셀 때문에 결과에 섞였습니다.");
        }

        [Test]
        public void null_대상은_들어가지_않는다()
        {
            var grid = CreateGrid();

            grid.Insert(Vector3.zero, null);

            Assert.AreEqual(0, grid.Count);
        }
    }
}
