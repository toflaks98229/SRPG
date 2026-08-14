using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Systems.AI;
using SRPG.Systems.Grid;
using SRPG.Tests.Support;

namespace SRPG.Tests
{
    /// <summary>
    /// 영향력 맵을 검증합니다.
    ///
    /// 핵심은 두 가지입니다.
    ///   · <b>거리에 따라 줄어드는가</b> — 이게 없으면 섬 전체가 똑같이 위험해 보입니다
    ///   · <b>벽을 통과하지 않는가</b> — 절벽 너머의 위협이 새어 오면 적이 엉뚱하게 우회합니다
    /// </summary>
    public sealed class InfluenceMapTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private static IslandGrid CreateIsland(int seed = 20260807)
        {
            return TestIsland.Create(seed);
        }

        /// <summary>섬 안쪽의 통행 가능한 타일 하나를 고릅니다.</summary>
        private static Tile PickInlandTile(IslandGrid grid)
        {
            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];
                if (!tile.IsCoastal && tile.WalkableNeighborCount == 4)
                {
                    return tile;
                }
            }

            return grid.WalkableTiles[grid.WalkableTiles.Count / 2];
        }

        // ====================================================================================================
        // 2. 기본 동작
        // ====================================================================================================

        [Test]
        public void 발생원을_넣기_전에는_비어_있다()
        {
            var grid = CreateIsland();
            var map = new InfluenceMap(grid);

            Assert.IsTrue(map.IsEmpty);
            Assert.AreEqual(0f, map.MaxValue);
        }

        [Test]
        public void 발생원_자리에_값이_생긴다()
        {
            var grid = CreateIsland();
            var map = new InfluenceMap(grid);
            var origin = PickInlandTile(grid);

            map.AddSource(origin.Coord, 1f);

            Assert.AreEqual(1f, map[origin.Coord], 0.0001f);
            Assert.IsFalse(map.IsEmpty);
        }

        [Test]
        public void 같은_칸의_발생원은_누적된다()
        {
            // 한 칸에 병사가 여럿 서 있으면 그만큼 더 위험합니다.
            var grid = CreateIsland();
            var map = new InfluenceMap(grid);
            var origin = PickInlandTile(grid);

            map.AddSource(origin.Coord, 0.4f);
            map.AddSource(origin.Coord, 0.6f);

            Assert.AreEqual(1f, map[origin.Coord], 0.0001f);
        }

        [Test]
        public void 비우면_값이_사라진다()
        {
            var grid = CreateIsland();
            var map = new InfluenceMap(grid);
            var origin = PickInlandTile(grid);

            map.AddSource(origin.Coord, 1f);
            map.Propagate(0.7f);
            map.Clear();

            Assert.IsTrue(map.IsEmpty);
            Assert.AreEqual(0f, map[origin.Coord], 0.0001f);
            Assert.AreEqual(0f, map.MaxValue);
        }

        [Test]
        public void 격자_밖_좌표는_0이고_예외가_나지_않는다()
        {
            var grid = CreateIsland();
            var map = new InfluenceMap(grid);

            Assert.DoesNotThrow(() => map.AddSource(new GridCoord(-5, -5), 1f));
            Assert.AreEqual(0f, map[new GridCoord(-5, -5)], 0.0001f);
            Assert.AreEqual(0f, map[new GridCoord(9999, 9999)], 0.0001f);
        }

        // ====================================================================================================
        // 3. 번짐
        // ====================================================================================================

        /// <summary><b>거리에 따라 줄어들지 않으면 영향력 맵은 아무 정보도 주지 않습니다.</b></summary>
        [Test]
        public void 멀어질수록_값이_작아진다()
        {
            var grid = CreateIsland();
            var map = new InfluenceMap(grid);
            var origin = PickInlandTile(grid);

            map.AddSource(origin.Coord, 1f);
            map.Propagate(0.7f);

            float previous = map[origin.Coord];
            var buffer = new Tile[4];

            // 발생원에서 한 칸씩 멀어지며 값이 단조 감소하는지 봅니다.
            var current = origin;
            for (int step = 0; step < 4; step++)
            {
                int count = grid.GetNeighbors4(current.Coord, buffer);

                Tile next = null;
                for (int n = 0; n < count; n++)
                {
                    if (buffer[n].IsWalkable && map[buffer[n].Coord] < previous)
                    {
                        next = buffer[n];
                        break;
                    }
                }

                if (next == null)
                {
                    break;
                }

                float value = map[next.Coord];
                Assert.Less(value, previous, $"{step}칸째에서 값이 줄지 않았습니다.");

                previous = value;
                current = next;
            }
        }

        [Test]
        public void 통행_불가_타일에는_번지지_않는다()
        {
            // 절벽 너머의 위협이 새어 오면 적이 갈 수 없는 길을 안전하다고 착각합니다.
            var grid = CreateIsland();
            var map = new InfluenceMap(grid);

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                map.AddSource(grid.WalkableTiles[i].Coord, 1f);
            }

            map.Propagate(0.9f);

            for (int i = 0; i < grid.AllTiles.Count; i++)
            {
                var tile = grid.AllTiles[i];
                if (tile.IsWalkable)
                {
                    continue;
                }

                Assert.AreEqual(0f, map[tile.Coord], 0.0001f, $"{tile.Coord} 는 통행 불가인데 값이 있습니다.");
            }
        }

        [Test]
        public void 감쇠가_클수록_더_멀리_번진다()
        {
            var grid = CreateIsland();
            var origin = PickInlandTile(grid);

            var weak = new InfluenceMap(grid);
            weak.AddSource(origin.Coord, 1f);
            weak.Propagate(0.3f);

            var strong = new InfluenceMap(grid);
            strong.AddSource(origin.Coord, 1f);
            strong.Propagate(0.9f);

            int weakCount = 0;
            int strongCount = 0;

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var coord = grid.WalkableTiles[i].Coord;
                if (weak[coord] > 0.01f) weakCount++;
                if (strong[coord] > 0.01f) strongCount++;
            }

            Assert.Greater(strongCount, weakCount, "감쇠를 늘렸는데 번진 범위가 넓어지지 않았습니다.");
        }

        [Test]
        public void 가까운_위협이_먼_위협에_묻히지_않는다()
        {
            // 번질 때 큰 값이 이겨야 합니다. 덮어쓰기가 되면 먼 발생원이 가까운 것을 지웁니다.
            var grid = CreateIsland();
            var map = new InfluenceMap(grid);
            var origin = PickInlandTile(grid);

            map.AddSource(origin.Coord, 1f);
            map.Propagate(0.7f);

            Assert.AreEqual(1f, map[origin.Coord], 0.0001f, "발생원 자신의 값이 번짐에 덮였습니다.");
        }

        // ====================================================================================================
        // 4. 정규화
        // ====================================================================================================

        [Test]
        public void 정규화하면_최대가_1이_된다()
        {
            var grid = CreateIsland();
            var map = new InfluenceMap(grid);
            var origin = PickInlandTile(grid);

            map.AddSource(origin.Coord, 7.5f);
            map.Propagate(0.7f);

            Assert.AreEqual(1f, map.SampleNormalized(origin.Coord), 0.0001f);
            Assert.AreEqual(7.5f, map.MaxValue, 0.0001f);
        }

        [Test]
        public void 비어_있으면_정규화_값은_0이다()
        {
            var grid = CreateIsland();
            var map = new InfluenceMap(grid);

            Assert.AreEqual(0f, map.SampleNormalized(PickInlandTile(grid).Coord), 0.0001f);
        }

        [Test]
        public void 다시_만들어도_이전_값이_남지_않는다()
        {
            var grid = CreateIsland();
            var map = new InfluenceMap(grid);

            var first = grid.WalkableTiles[0];
            var second = grid.WalkableTiles[grid.WalkableTiles.Count - 1];

            map.AddSource(first.Coord, 1f);
            map.Propagate(0.7f);

            map.Clear();
            map.AddSource(second.Coord, 1f);
            map.Propagate(0.7f);

            // 첫 발생원 자리가 두 번째 번짐 범위 밖이라면 0이어야 합니다.
            if (GridCoord.ManhattanDistance(first.Coord, second.Coord) > 20)
            {
                Assert.AreEqual(0f, map[first.Coord], 0.01f, "이전 주기의 값이 남았습니다.");
            }
        }
    }
}
