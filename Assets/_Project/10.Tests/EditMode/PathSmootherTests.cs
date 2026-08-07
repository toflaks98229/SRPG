using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Systems.Grid;
using SRPG.Systems.Pathfinding;

namespace SRPG.Tests
{
    /// <summary>
    /// 시야선 기반 경로 다듬기를 검증합니다.
    ///
    /// 다듬기는 <b>지름길을 만드는 것이 아니라 군더더기를 빼는 것</b>이어야 합니다.
    /// 그래서 확인할 것이 두 갈래입니다.
    ///   · 실제로 줄어드는가 (안 줄면 할 이유가 없음)
    ///   · <b>줄이면서 벽을 뚫지 않는가</b> (뚫으면 A*가 막아 둔 것을 되살리는 셈)
    ///
    /// 두 번째가 훨씬 중요합니다. 첫 번째가 깨지면 눈에 보이지만,
    /// 두 번째가 깨지면 경로만 뚫리고 유닛은 벽 앞에서 멈춰 섭니다.
    /// </summary>
    public sealed class PathSmootherTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        /// <summary>전부 통행 가능한 평지 격자를 만듭니다.</summary>
        private static IslandGrid BuildOpenField(int width, int depth)
        {
            var grid = new IslandGrid(width, depth, 2f, 0.9f);

            for (int y = 0; y < depth; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var tile = grid.GetTile(new GridCoord(x, y));
                    tile.Type = TileType.Ground;
                    tile.Height = 0;
                    tile.IsWalkable = true;

                    grid.WalkableTiles.Add(tile);
                }
            }

            return grid;
        }

        private static void Block(IslandGrid grid, int x, int y)
        {
            var tile = grid.GetTile(new GridCoord(x, y));
            tile.Type = TileType.Cliff;
            tile.IsWalkable = false;

            grid.WalkableTiles.Remove(tile);
        }

        private static IslandGrid CreateIsland(int seed = 20260807)
        {
            var settings = IslandSettings.CreateDefault();
            settings.Width = 30;
            settings.Depth = 30;
            return IslandGenerator.Generate(settings, seed);
        }

        /// <summary>칸 단위로 이어진 직선 경로를 만듭니다.</summary>
        private static List<GridCoord> Line(int fromX, int fromY, int toX, int toY)
        {
            var path = new List<GridCoord>();

            int x = fromX;
            int y = fromY;

            path.Add(new GridCoord(x, y));

            while (x != toX || y != toY)
            {
                if (x < toX) x++;
                else if (x > toX) x--;

                if (y < toY) y++;
                else if (y > toY) y--;

                path.Add(new GridCoord(x, y));
            }

            return path;
        }

        // ====================================================================================================
        // 2. 군더더기 제거
        // ====================================================================================================

        [Test]
        public void 열린_땅의_직선_경로는_양_끝만_남는다()
        {
            var grid = BuildOpenField(11, 11);
            var path = Line(0, 5, 10, 5);
            var result = new List<GridCoord>();

            PathSmoother.Smooth(grid, path, result);

            Assert.AreEqual(2, result.Count, "열린 직선인데 경유점이 남았습니다.");
            Assert.AreEqual(path[0], result[0]);
            Assert.AreEqual(path[path.Count - 1], result[1]);
        }

        [Test]
        public void 열린_땅의_대각선_경로도_양_끝만_남는다()
        {
            var grid = BuildOpenField(11, 11);
            var path = Line(0, 0, 9, 9);
            var result = new List<GridCoord>();

            PathSmoother.Smooth(grid, path, result);

            Assert.AreEqual(2, result.Count);
        }

        /// <summary>
        /// 격자 경로는 45도의 배수로만 꺾입니다. 다듬으면 그 각이 사라져야 합니다.
        /// </summary>
        [Test]
        public void 계단형_경로가_직선으로_줄어든다()
        {
            var grid = BuildOpenField(13, 13);

            // (0,0) → 대각으로 3칸 → 수평으로 6칸. 사이의 꺾임이 필요 없어야 합니다.
            var path = new List<GridCoord>();
            for (int i = 0; i <= 3; i++) path.Add(new GridCoord(i, i));
            for (int i = 4; i <= 9; i++) path.Add(new GridCoord(i, 3));

            var result = new List<GridCoord>();
            PathSmoother.Smooth(grid, path, result);

            Assert.Less(result.Count, path.Count, "경유점이 하나도 줄지 않았습니다.");
        }

        [Test]
        public void 시작과_끝은_항상_보존된다()
        {
            var grid = BuildOpenField(9, 9);
            var path = Line(1, 1, 7, 4);
            var result = new List<GridCoord>();

            PathSmoother.Smooth(grid, path, result);

            Assert.AreEqual(path[0], result[0], "시작점이 사라졌습니다.");
            Assert.AreEqual(path[path.Count - 1], result[result.Count - 1], "목적지가 사라졌습니다.");
        }

        [Test]
        public void 다듬은_경로가_원본보다_길어지지_않는다()
        {
            var grid = BuildOpenField(15, 15);
            var path = Line(0, 0, 14, 7);
            var result = new List<GridCoord>();

            PathSmoother.Smooth(grid, path, result);

            Assert.LessOrEqual(result.Count, path.Count);
        }

        // ====================================================================================================
        // 3. 안전성 — 여기가 핵심입니다
        // ====================================================================================================

        /// <summary>
        /// <b>벽 너머로 질러가면 안 됩니다.</b>
        /// 다듬기가 A*의 결과를 무효로 만드는 가장 흔한 방식입니다.
        /// </summary>
        [Test]
        public void 벽을_가로질러_질러가지_않는다()
        {
            var grid = BuildOpenField(11, 11);

            // 가운데를 세로로 막고 위쪽에만 통로를 남깁니다.
            for (int y = 0; y <= 8; y++)
            {
                Block(grid, 5, y);
            }

            // 벽을 돌아가는 경로를 흉내 냅니다.
            var path = new List<GridCoord>();
            for (int y = 0; y <= 9; y++) path.Add(new GridCoord(0, y));
            for (int x = 0; x <= 10; x++) path.Add(new GridCoord(x, 9));
            for (int y = 8; y >= 0; y--) path.Add(new GridCoord(10, y));

            var result = new List<GridCoord>();
            PathSmoother.Smooth(grid, path, result);

            // 다듬은 경로의 모든 구간이 실제로 통과 가능해야 합니다.
            for (int i = 1; i < result.Count; i++)
            {
                Assert.IsTrue(
                    PathSmoother.HasLineOfSight(grid, result[i - 1], result[i]),
                    $"{result[i - 1]} → {result[i]} 구간이 벽을 통과합니다.");
            }
        }

        [Test]
        public void 막힌_두_점_사이에는_시야선이_없다()
        {
            var grid = BuildOpenField(9, 9);

            for (int y = 0; y < 9; y++)
            {
                Block(grid, 4, y);
            }

            Assert.IsFalse(PathSmoother.HasLineOfSight(grid, new GridCoord(1, 4), new GridCoord(7, 4)));
        }

        [Test]
        public void 열린_두_점_사이에는_시야선이_있다()
        {
            var grid = BuildOpenField(9, 9);

            Assert.IsTrue(PathSmoother.HasLineOfSight(grid, new GridCoord(1, 1), new GridCoord(7, 7)));
            Assert.IsTrue(PathSmoother.HasLineOfSight(grid, new GridCoord(1, 1), new GridCoord(1, 1)));
        }

        /// <summary>
        /// 모서리 통과 금지는 다듬기 단계에서도 지켜져야 합니다.
        /// 여기서 빠뜨리면 A*가 막아 둔 것이 그대로 되살아납니다.
        /// </summary>
        [Test]
        public void 모서리를_대각으로_통과하는_시야선은_없다()
        {
            var grid = BuildOpenField(5, 5);

            Block(grid, 2, 1);
            Block(grid, 1, 2);

            Assert.IsFalse(
                PathSmoother.HasLineOfSight(grid, new GridCoord(1, 1), new GridCoord(2, 2)),
                "막힌 모서리를 대각으로 통과했습니다.");
        }

        [Test]
        public void 넘을_수_없는_고도_차는_시야선을_막는다()
        {
            var grid = BuildOpenField(9, 9);

            // 통행은 가능하지만 고도가 2단 높은 칸입니다.
            grid.GetTile(new GridCoord(4, 4)).Height = 2;

            Assert.IsFalse(PathSmoother.HasLineOfSight(grid, new GridCoord(1, 4), new GridCoord(7, 4)));
        }

        [Test]
        public void 통행_불가_지점은_시야선의_끝이_될_수_없다()
        {
            var grid = BuildOpenField(9, 9);
            Block(grid, 7, 7);

            Assert.IsFalse(PathSmoother.HasLineOfSight(grid, new GridCoord(1, 1), new GridCoord(7, 7)));
            Assert.IsFalse(PathSmoother.HasLineOfSight(grid, new GridCoord(7, 7), new GridCoord(1, 1)));
        }

        // ====================================================================================================
        // 4. 실제 경로와의 결합
        // ====================================================================================================

        /// <summary>
        /// 절차적 섬에서 실제 A* 경로를 다듬어도 모든 구간이 통과 가능해야 합니다.
        /// 합성 격자와 달리 지형이 들쭉날쭉해 모서리와 고도 조건이 함께 걸립니다.
        /// </summary>
        [Test]
        public void 실제_섬의_경로를_다듬어도_모든_구간이_통과_가능하다()
        {
            var grid = CreateIsland();
            var pathfinder = new GridPathfinder(grid);
            var smoothed = new List<GridCoord>();

            int checkedPaths = 0;

            for (int i = 0; i < 12; i++)
            {
                var start = grid.WalkableTiles[(i * 17) % grid.WalkableTiles.Count].Coord;
                var goal = grid.WalkableTiles[(i * 53 + 7) % grid.WalkableTiles.Count].Coord;

                if (!pathfinder.TryFindSmoothedPath(start, goal, smoothed))
                {
                    continue;
                }

                checkedPaths++;

                Assert.AreEqual(start, smoothed[0]);
                Assert.AreEqual(goal, smoothed[smoothed.Count - 1]);

                for (int s = 1; s < smoothed.Count; s++)
                {
                    Assert.IsTrue(
                        PathSmoother.HasLineOfSight(grid, smoothed[s - 1], smoothed[s]),
                        $"{smoothed[s - 1]} → {smoothed[s]} 구간을 실제로는 갈 수 없습니다.");
                }
            }

            Assert.Greater(checkedPaths, 0, "검증할 경로를 하나도 찾지 못했습니다.");
        }

        [Test]
        public void 다듬은_경로가_날것보다_짧거나_같다()
        {
            var grid = CreateIsland();
            var pathfinder = new GridPathfinder(grid);

            var raw = new List<GridCoord>();
            var smoothed = new List<GridCoord>();

            var start = grid.WalkableTiles[0].Coord;
            var goal = grid.WalkableTiles[grid.WalkableTiles.Count - 1].Coord;

            Assert.IsTrue(pathfinder.TryFindPath(start, goal, raw));
            Assert.IsTrue(pathfinder.TryFindSmoothedPath(start, goal, smoothed));

            Assert.LessOrEqual(smoothed.Count, raw.Count, "다듬었는데 경유점이 늘었습니다.");
        }

        // ====================================================================================================
        // 5. 방어적 동작
        // ====================================================================================================

        [Test]
        public void 빈_경로와_한_칸_경로는_안전하다()
        {
            var grid = BuildOpenField(5, 5);
            var result = new List<GridCoord>();

            Assert.DoesNotThrow(() => PathSmoother.Smooth(grid, new List<GridCoord>(), result));
            Assert.AreEqual(0, result.Count);

            var single = new List<GridCoord> { new GridCoord(2, 2) };
            PathSmoother.Smooth(grid, single, result);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(single[0], result[0]);
        }

        [Test]
        public void 격자가_null이면_원본을_그대로_돌려준다()
        {
            var path = Line(0, 0, 3, 3);
            var result = new List<GridCoord>();

            PathSmoother.Smooth(null, path, result);

            CollectionAssert.AreEqual(path, result);
        }
    }
}
