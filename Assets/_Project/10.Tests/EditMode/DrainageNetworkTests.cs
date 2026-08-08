using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Data;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 계곡 생성을 검증합니다.
    ///
    /// <b>핵심은 하나입니다 — 고도가 해안 거리의 함수가 아니어야 합니다.</b>
    ///
    /// 그 함수 관계가 남아 있는 한 계곡은 만들어질 수 없습니다.
    /// "주변보다 낮은데 바다에서는 먼 곳"이 곧 계곡인데, 단조 함수는 그것을 금지합니다.
    /// 그래서 여기서 가장 먼저 보는 것은 <b>같은 해안 거리에 여러 고도가 존재하는가</b>입니다.
    ///
    /// 그리고 통행이 살아 있어야 합니다. 계곡을 파다 섬이 걸어서 못 가는 조각으로
    /// 쪼개지면 방어가 불가능해지고, 그건 지형이 아니라 버그입니다.
    /// </summary>
    public sealed class DrainageNetworkTests
    {
        // ====================================================================================================
        // 1. Setup
        // ====================================================================================================

        private static IslandGrid CreateIsland(int seed = 31337)
        {
            return IslandGenerator.Generate(IslandSettings.CreateDefault(), seed);
        }

        /// <summary>가로 8칸의 일자 육지를 만듭니다. 알고리즘만 따로 보기 위한 것입니다.</summary>
        private static void CreateStrip(out int w, out int d, out bool[] isLand, out int[] height)
        {
            w = 10;
            d = 3;

            isLand = new bool[w * d];
            height = new int[w * d];

            for (int x = 1; x < w - 1; x++)
            {
                isLand[1 * w + x] = true;
            }
        }

        // ====================================================================================================
        // 2. 고도가 해안 거리에서 풀렸는가
        // ====================================================================================================

        /// <summary>
        /// 이 테스트가 이 작업의 목적입니다.
        ///
        /// 고도가 해안 거리의 함수이면 같은 거리에는 반드시 같은 고도만 존재합니다.
        /// 그 관계가 깨져야 계곡이 있을 자리가 생깁니다.
        /// </summary>
        [Test]
        public void 같은_해안_거리에_여러_고도가_있다()
        {
            var grid = CreateIsland();

            // 해안 거리를 다시 계산합니다. 격자에는 저장되어 있지 않습니다.
            var byDistance = new Dictionary<int, HashSet<int>>();

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];
                int distance = DistanceToWater(grid, tile);

                if (!byDistance.TryGetValue(distance, out var levels))
                {
                    levels = new HashSet<int>();
                    byDistance[distance] = levels;
                }

                levels.Add(tile.Height);
            }

            int varied = 0;

            foreach (var pair in byDistance)
            {
                if (pair.Value.Count > 1)
                {
                    varied++;
                }
            }

            Assert.Greater(
                varied,
                0,
                "모든 해안 거리에서 고도가 하나뿐입니다. 고도가 아직 해안 거리의 함수라 계곡이 있을 수 없습니다.");
        }

        /// <summary>
        /// 계곡이란 <b>주변보다 낮은데 바다에서는 먼 곳</b>입니다. 그런 칸이 실제로 있어야 합니다.
        /// </summary>
        [Test]
        public void 주변보다_낮고_바다에서_먼_칸이_있다()
        {
            var grid = CreateIsland();
            int found = 0;

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];

                // 해안 근처는 원래 낮습니다. 안쪽만 봅니다.
                if (DistanceToWater(grid, tile) < 3)
                {
                    continue;
                }

                int higherNeighbors = 0;

                for (int n = 0; n < SRPG.Common.GridCoord.Neighbors4.Length; n++)
                {
                    var neighbor = grid.GetTile(tile.Coord + SRPG.Common.GridCoord.Neighbors4[n]);

                    if (neighbor != null && !neighbor.IsWater && neighbor.Height > tile.Height)
                    {
                        higherNeighbors++;
                    }
                }

                // 양옆이 솟아 있으면 골입니다.
                if (higherNeighbors >= 2)
                {
                    found++;
                }
            }

            Assert.Greater(found, 0, "골짜기가 하나도 없습니다.");
        }

        // ====================================================================================================
        // 3. 통행이 살아 있는가
        // ====================================================================================================

        /// <summary>
        /// 계단 제약은 장식이 아닙니다. 통행 판정이 "고도차 1 이하"를 쓰므로
        /// 여기서 어기면 섬이 걸어서 못 가는 조각으로 쪼개집니다.
        /// </summary>
        [Test]
        public void 인접_고도차가_1을_넘지_않는다()
        {
            var grid = CreateIsland();

            for (int i = 0; i < grid.AllTiles.Count; i++)
            {
                var tile = grid.AllTiles[i];

                if (tile.IsWater)
                {
                    continue;
                }

                // 절벽은 낙차가 커도 됩니다. 어차피 오를 수 없는 면이니까요.
                if (!tile.IsWalkable)
                {
                    continue;
                }

                for (int n = 0; n < SRPG.Common.GridCoord.Neighbors4.Length; n++)
                {
                    var neighbor = grid.GetTile(tile.Coord + SRPG.Common.GridCoord.Neighbors4[n]);

                    if (neighbor == null || !neighbor.IsWalkable)
                    {
                        continue;
                    }

                    Assert.LessOrEqual(
                        Mathf.Abs(tile.Height - neighbor.Height),
                        1,
                        $"{tile.Coord}({tile.Height}) 와 걸을 수 있는 이웃({neighbor.Height}) 의 차이가 1을 넘습니다.");
                }
            }
        }

        /// <summary>
        /// 계곡을 파도 섬 전체를 걸어서 돌 수 있어야 합니다.
        /// </summary>
        [Test]
        public void 섬이_하나로_이어져_있다()
        {
            var grid = CreateIsland();

            Assert.Greater(grid.WalkableTiles.Count, 0, "걸을 수 있는 칸이 없습니다.");

            var visited = new HashSet<SRPG.Common.GridCoord>();
            var queue = new Queue<Tile>();

            queue.Enqueue(grid.WalkableTiles[0]);
            visited.Add(grid.WalkableTiles[0].Coord);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                for (int n = 0; n < SRPG.Common.GridCoord.Neighbors4.Length; n++)
                {
                    var neighbor = grid.GetTile(current.Coord + SRPG.Common.GridCoord.Neighbors4[n]);

                    if (neighbor == null || !neighbor.IsWalkable || visited.Contains(neighbor.Coord))
                    {
                        continue;
                    }

                    if (Mathf.Abs(neighbor.Height - current.Height) > 1)
                    {
                        continue;
                    }

                    visited.Add(neighbor.Coord);
                    queue.Enqueue(neighbor);
                }
            }

            Assert.AreEqual(
                grid.WalkableTiles.Count,
                visited.Count,
                $"걸을 수 있는 {grid.WalkableTiles.Count}칸 중 {visited.Count}칸만 이어져 있습니다.");
        }

        // ====================================================================================================
        // 4. 계단 제약 자체
        // ====================================================================================================

        /// <summary>
        /// 계단 제약은 <b>낮추기만</b> 해야 합니다.
        /// 평활화하듯 양쪽을 평균 내면 방금 판 계곡이 도로 메워집니다.
        /// </summary>
        [Test]
        public void 계단_제약이_계곡을_메우지_않는다()
        {
            CreateStrip(out int w, out int d, out var isLand, out var height);

            // 가운데를 판 골짜기입니다. 양옆은 높습니다.
            int[] profile = { 0, 0, 1, 2, 3, 0, 3, 2, 1, 0 };

            for (int x = 0; x < w; x++)
            {
                height[1 * w + x] = profile[x];
            }

            DrainageNetwork.EnforceStepLimit(w, d, isLand, height);

            // 5번 칸이 골 바닥입니다. 올라가면 안 됩니다.
            Assert.AreEqual(0, height[1 * w + 5], "골 바닥이 메워졌습니다.");
        }

        [Test]
        public void 계단_제약이_차이를_1로_줄인다()
        {
            CreateStrip(out int w, out int d, out var isLand, out var height);

            for (int x = 1; x < w - 1; x++)
            {
                height[1 * w + x] = x == 5 ? 6 : 0;
            }

            DrainageNetwork.EnforceStepLimit(w, d, isLand, height);

            for (int x = 1; x < w - 1; x++)
            {
                int left = x > 1 ? height[1 * w + (x - 1)] : 0;

                Assert.LessOrEqual(
                    Mathf.Abs(height[1 * w + x] - left),
                    1,
                    $"{x}번 칸에서 차이가 1을 넘습니다.");
            }
        }

        [Test]
        public void 계단_제약이_끝나고_멈춘다()
        {
            CreateStrip(out int w, out int d, out var isLand, out var height);

            for (int x = 1; x < w - 1; x++)
            {
                height[1 * w + x] = 0;
            }

            var before = (int[])height.Clone();
            DrainageNetwork.EnforceStepLimit(w, d, isLand, height);

            CollectionAssert.AreEqual(before, height, "이미 조건을 만족하는데 값이 바뀌었습니다.");
        }

        // ====================================================================================================
        // 5. 결정론과 예외
        // ====================================================================================================

        [Test]
        public void 같은_시드면_같은_지형이_나온다()
        {
            var first = CreateIsland(555);
            var second = CreateIsland(555);

            for (int i = 0; i < first.AllTiles.Count; i++)
            {
                Assert.AreEqual(first.AllTiles[i].Height, second.AllTiles[i].Height, $"{i}번 칸의 고도가 다릅니다.");
            }
        }

        // ====================================================================================================
        // 6. Helpers
        // ====================================================================================================

        /// <summary>해당 타일에서 가장 가까운 물까지의 격자 거리입니다.</summary>
        private static int DistanceToWater(IslandGrid grid, Tile tile)
        {
            int best = int.MaxValue;

            for (int i = 0; i < grid.AllTiles.Count; i++)
            {
                var other = grid.AllTiles[i];

                if (!other.IsWater)
                {
                    continue;
                }

                best = Mathf.Min(best, SRPG.Common.GridCoord.ManhattanDistance(tile.Coord, other.Coord));
            }

            return best;
        }
    }
}
