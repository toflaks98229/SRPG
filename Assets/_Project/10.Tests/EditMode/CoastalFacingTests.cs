using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Systems.Grid;
using SRPG.Tests.Support;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 해안선 조회를 검증합니다.
    ///
    /// 방어 부대가 어느 쪽을 보고 설지의 근거입니다.
    /// 예전에는 적이 없으면 월드 +Z를 보고 서 있었습니다. 아무 의미 없는 방향이라
    /// 창병 전열이 바다를 등지고 서는 그림이 나왔습니다.
    /// </summary>
    public sealed class CoastalFacingTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private static IslandGrid CreateIsland(int seed = 20260807)
        {
            return TestIsland.Create(seed);
        }

        // ====================================================================================================
        // 2. 해안선 목록
        // ====================================================================================================

        [Test]
        public void 섬에는_해안선이_존재한다()
        {
            var grid = CreateIsland();

            Assert.Greater(grid.CoastalTiles.Count, 0, "해안선 타일이 하나도 없습니다.");
        }

        [Test]
        public void 해안선은_전부_통행_가능하고_물에_닿아_있다()
        {
            var grid = CreateIsland();
            var buffer = new Tile[4];

            for (int i = 0; i < grid.CoastalTiles.Count; i++)
            {
                var tile = grid.CoastalTiles[i];

                Assert.IsTrue(tile.IsWalkable, $"{tile.Coord} 해안선인데 통행 불가입니다.");

                int count = grid.GetNeighbors4(tile.Coord, buffer);
                bool touchesWater = false;

                for (int n = 0; n < count; n++)
                {
                    if (buffer[n].IsWater)
                    {
                        touchesWater = true;
                        break;
                    }
                }

                // 격자 가장자리는 이웃이 없을 수 있으나, 생성기가 테두리를 물로 두므로 항상 이웃이 있습니다.
                Assert.IsTrue(touchesWater, $"{tile.Coord} 해안선인데 물과 맞닿지 않았습니다.");
            }
        }

        [Test]
        public void 해안선은_통행_가능_타일의_부분집합이다()
        {
            var grid = CreateIsland();

            for (int i = 0; i < grid.CoastalTiles.Count; i++)
            {
                CollectionAssert.Contains(grid.WalkableTiles, grid.CoastalTiles[i]);
            }
        }

        [Test]
        public void 다시_만들어도_해안선이_중복되지_않는다()
        {
            // RebuildDerivedData 는 생성 과정에서 두 번 불립니다. 목록을 비우지 않으면 두 배가 됩니다.
            var grid = CreateIsland();

            var unique = new System.Collections.Generic.HashSet<Tile>(grid.CoastalTiles);

            Assert.AreEqual(unique.Count, grid.CoastalTiles.Count, "해안선 타일이 중복 등록되었습니다.");
        }

        // ====================================================================================================
        // 3. 최근접 해안
        // ====================================================================================================

        [Test]
        public void 가장_가까운_해안을_찾는다()
        {
            var grid = CreateIsland();

            // 임의의 통행 가능 지점에서 조회한 결과가 실제로 최근접이어야 합니다.
            for (int i = 0; i < grid.WalkableTiles.Count; i += 37)
            {
                Vector3 from = grid.WalkableTiles[i].WorldCenter;

                var found = grid.FindNearestCoastal(from);
                Assert.IsNotNull(found);

                float foundSqr = (found.WorldCenter - from).sqrMagnitude;

                for (int c = 0; c < grid.CoastalTiles.Count; c++)
                {
                    float sqr = (grid.CoastalTiles[c].WorldCenter - from).sqrMagnitude;
                    Assert.LessOrEqual(foundSqr, sqr + 0.001f, "더 가까운 해안이 있는데 놓쳤습니다.");
                }
            }
        }

        [Test]
        public void 해안_방향은_섬_바깥을_향한다()
        {
            var grid = CreateIsland();

            Vector3 center = new Vector3(
                grid.Origin.x + grid.Width * grid.CellSize * 0.5f,
                0f,
                grid.Origin.z + grid.Depth * grid.CellSize * 0.5f);

            // 섬 중심에 가까운 지점에서 본 해안 방향은 중심에서 멀어지는 쪽이어야 합니다.
            var inland = grid.FindNearestCoastal(center);
            Assert.IsNotNull(inland);

            Vector3 toCoast = inland.WorldCenter - center;
            toCoast.y = 0f;

            Assert.Greater(toCoast.magnitude, 0.1f, "중심과 해안이 같은 자리로 나왔습니다.");
        }

        [Test]
        public void 해안이_없으면_null을_돌려준다()
        {
            // 해안선 목록이 비어 있는 격자를 직접 만듭니다.
            var grid = new IslandGrid(5, 5, 2f, 0.9f);

            Assert.IsNull(grid.FindNearestCoastal(Vector3.zero));
        }
    }
}
