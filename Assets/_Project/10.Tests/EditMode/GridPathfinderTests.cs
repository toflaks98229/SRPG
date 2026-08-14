using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Systems.Grid;
using SRPG.Tests.Support;
using SRPG.Systems.Pathfinding;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 격자 경로 탐색의 정확성을 검증합니다.
    /// </summary>
    public sealed class GridPathfinderTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private static IslandGrid CreateIsland(int seed = 20260807)
        {
            return TestIsland.Create(seed);
        }

        // ====================================================================================================
        // 2. Tests
        // ====================================================================================================

        [Test]
        public void 통행_가능한_두_지점_사이의_경로를_찾는다()
        {
            var grid = CreateIsland();
            var pathfinder = new GridPathfinder(grid);
            var path = new List<GridCoord>();

            var start = grid.WalkableTiles[0].Coord;
            var goal = grid.WalkableTiles[grid.WalkableTiles.Count - 1].Coord;

            Assert.IsTrue(pathfinder.TryFindPath(start, goal, path), "연결된 섬인데 경로를 찾지 못했습니다.");
            Assert.AreEqual(start, path[0], "경로가 출발지에서 시작하지 않습니다.");
            Assert.AreEqual(goal, path[path.Count - 1], "경로가 목적지에서 끝나지 않습니다.");
        }

        [Test]
        public void 찾은_경로는_한_칸씩_이어진다()
        {
            var grid = CreateIsland();
            var pathfinder = new GridPathfinder(grid);
            var path = new List<GridCoord>();

            var start = grid.WalkableTiles[0].Coord;
            var goal = grid.WalkableTiles[grid.WalkableTiles.Count / 2].Coord;

            Assert.IsTrue(pathfinder.TryFindPath(start, goal, path));

            for (int i = 1; i < path.Count; i++)
            {
                // 8방향이므로 체비셰프 1칸이 인접의 기준입니다.
                // 맨해튼으로 재면 대각선 한 걸음이 2가 나와 정상 경로를 실패로 봅니다.
                int step = GridCoord.ChebyshevDistance(path[i - 1], path[i]);
                Assert.AreEqual(1, step, $"{path[i - 1]} → {path[i]} 구간이 인접하지 않습니다.");

                var tile = grid.GetTile(path[i]);
                Assert.IsTrue(tile.IsWalkable, $"{path[i]} 는 통행 불가 타일입니다.");
            }
        }

        [Test]
        public void 출발지와_목적지가_같으면_한_칸짜리_경로를_반환한다()
        {
            var grid = CreateIsland();
            var pathfinder = new GridPathfinder(grid);
            var path = new List<GridCoord>();

            var coord = grid.WalkableTiles[0].Coord;

            Assert.IsTrue(pathfinder.TryFindPath(coord, coord, path));
            Assert.AreEqual(1, path.Count);
            Assert.AreEqual(coord, path[0]);
        }

        [Test]
        public void 물_타일로는_경로를_찾지_못한다()
        {
            var grid = CreateIsland();
            var pathfinder = new GridPathfinder(grid);
            var path = new List<GridCoord>();

            var start = grid.WalkableTiles[0].Coord;

            // 격자 테두리는 항상 바다입니다.
            var water = new GridCoord(0, 0);
            Assert.IsTrue(grid.GetTile(water).IsWater, "테스트 전제가 깨졌습니다. 테두리가 바다가 아닙니다.");

            Assert.IsFalse(pathfinder.TryFindPath(start, water, path), "물 위로 경로가 나왔습니다.");
            Assert.AreEqual(0, path.Count, "실패했는데 경로가 남아 있습니다.");
        }

        [Test]
        public void 스냅_탐색은_물을_찍어도_가까운_육지로_보정한다()
        {
            // 플레이어가 바다를 클릭했을 때 명령이 그냥 씹히지 않도록 하는 폴백 경로입니다.
            var grid = CreateIsland();
            var pathfinder = new GridPathfinder(grid);
            var path = new List<GridCoord>();

            var start = grid.WalkableTiles[0].Coord;
            var water = new GridCoord(0, 0);

            bool found = pathfinder.TryFindPathSnapped(start, water, path, out var resolved);

            Assert.IsTrue(found, "스냅 탐색이 실패했습니다.");
            Assert.AreNotEqual(water, resolved, "목적지가 물 그대로입니다. 보정되지 않았습니다.");
            Assert.IsTrue(grid.GetTile(resolved).IsWalkable, "보정된 목적지가 통행 불가입니다.");
        }

        [Test]
        public void 여러_번_호출해도_결과가_일관된다()
        {
            // 내부 작업 배열을 재사용하므로, 이전 호출의 잔여 상태가 다음 결과를 오염시키지 않아야 합니다.
            var grid = CreateIsland();
            var pathfinder = new GridPathfinder(grid);

            var first = new List<GridCoord>();
            var second = new List<GridCoord>();
            var scratch = new List<GridCoord>();

            var start = grid.WalkableTiles[0].Coord;
            var goal = grid.WalkableTiles[grid.WalkableTiles.Count - 1].Coord;

            Assert.IsTrue(pathfinder.TryFindPath(start, goal, first));

            // 사이에 다른 탐색을 여러 번 끼워 넣습니다.
            for (int i = 0; i < 5; i++)
            {
                pathfinder.TryFindPath(
                    grid.WalkableTiles[i % grid.WalkableTiles.Count].Coord,
                    grid.WalkableTiles[(i * 7 + 3) % grid.WalkableTiles.Count].Coord,
                    scratch);
            }

            Assert.IsTrue(pathfinder.TryFindPath(start, goal, second));
            CollectionAssert.AreEqual(first, second, "같은 입력인데 경로가 달라졌습니다.");
        }

        [Test]
        public void 넓은_격자에서도_힙이_넘치지_않는다()
        {
            // 감소 키를 쓰지 않아 같은 셀이 힙에 여러 번 들어갑니다.
            // 힙 크기를 셀 수만큼만 잡으면 여기서 IndexOutOfRange가 납니다.

            var grid = TestIsland.Create(55555);
            var pathfinder = new GridPathfinder(grid);
            var path = new List<GridCoord>();

            var start = grid.WalkableTiles[0].Coord;
            var goal = grid.WalkableTiles[grid.WalkableTiles.Count - 1].Coord;

            Assert.DoesNotThrow(() => pathfinder.TryFindPath(start, goal, path));
        }

        // ====================================================================================================
        // 3. 세대 스탬프 (작업 배열을 비우지 않는 방식)
        // ====================================================================================================

        /// <summary>
        /// 탐색을 시작할 때 작업 배열을 통째로 비우는 대신 세대 번호만 올립니다.
        /// 셀의 초기화는 실제로 방문할 때 일어나므로, 지난 탐색의 값이 남아 있어도 무해해야 합니다.
        ///
        /// 이 방식이 깨지면 증상은 <b>"가끔 엉뚱한 경로가 나온다"</b> 입니다.
        /// 예외도 나지 않고 재현도 어려우므로 여기서 못 박아 둡니다.
        /// </summary>
        [Test]
        public void 많은_탐색을_반복해도_결과가_흔들리지_않는다()
        {
            var grid = CreateIsland();
            var pathfinder = new GridPathfinder(grid);

            var reference = new List<GridCoord>();
            var scratch = new List<GridCoord>();
            var again = new List<GridCoord>();

            var start = grid.WalkableTiles[0].Coord;
            var goal = grid.WalkableTiles[grid.WalkableTiles.Count / 2].Coord;

            Assert.IsTrue(pathfinder.TryFindPath(start, goal, reference));

            // 격자 전역을 훑는 탐색을 대량으로 끼워 넣어 배열을 낡은 값으로 가득 채웁니다.
            int walkable = grid.WalkableTiles.Count;
            for (int i = 0; i < 400; i++)
            {
                pathfinder.TryFindPath(
                    grid.WalkableTiles[(i * 13) % walkable].Coord,
                    grid.WalkableTiles[(i * 29 + 11) % walkable].Coord,
                    scratch);
            }

            Assert.IsTrue(pathfinder.TryFindPath(start, goal, again));
            CollectionAssert.AreEqual(reference, again, "탐색을 반복한 뒤 같은 입력의 경로가 달라졌습니다.");
        }

        /// <summary>
        /// 실패한 탐색이 다음 탐색을 오염시키지 않아야 합니다.
        /// 실패는 열린 목록이 마를 때까지 격자를 헤집고 끝나므로, 잔여 상태가 가장 많이 남는 경우입니다.
        /// </summary>
        [Test]
        public void 실패한_탐색_뒤에도_정상_경로를_찾는다()
        {
            var grid = CreateIsland();
            var pathfinder = new GridPathfinder(grid);
            var path = new List<GridCoord>();

            // 물 타일을 목적지로 삼아 실패시킵니다.
            GridCoord water = GridCoord.Invalid;
            for (int i = 0; i < grid.AllTiles.Count; i++)
            {
                if (grid.AllTiles[i].IsWater)
                {
                    water = grid.AllTiles[i].Coord;
                    break;
                }
            }

            Assert.IsTrue(water.IsValid, "테스트할 물 타일이 없습니다.");

            var start = grid.WalkableTiles[0].Coord;
            var goal = grid.WalkableTiles[grid.WalkableTiles.Count - 1].Coord;

            var expected = new List<GridCoord>();
            Assert.IsTrue(pathfinder.TryFindPath(start, goal, expected));

            Assert.IsFalse(pathfinder.TryFindPath(start, water, path), "물로 가는 경로를 찾아 버렸습니다.");

            Assert.IsTrue(pathfinder.TryFindPath(start, goal, path), "실패한 탐색 뒤에 경로를 찾지 못했습니다.");
            CollectionAssert.AreEqual(expected, path, "실패한 탐색이 다음 결과를 오염시켰습니다.");
        }

        /// <summary>
        /// 한 칸짜리 이동이 격자 크기와 무관하게 처리되어야 합니다.
        ///
        /// 예전에는 탐색마다 배열 세 개를 셀 수만큼 초기화했으므로, 옆 칸으로 가는 경로에도
        /// 64×64 격자에서 12,288칸을 훑었습니다. 세대 스탬프는 방문한 셀만 건드립니다.
        /// 비용은 직접 잴 수 없지만, 결과가 맞는지는 확인할 수 있습니다.
        /// </summary>
        [Test]
        public void 인접한_칸으로의_경로는_두_칸이다()
        {
            var grid = CreateIsland();
            var pathfinder = new GridPathfinder(grid);
            var path = new List<GridCoord>();

            // 서로 이웃한 통행 가능 타일 한 쌍을 찾습니다.
            var buffer = new Tile[4];
            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];
                int count = grid.GetNeighbors4(tile.Coord, buffer);

                for (int n = 0; n < count; n++)
                {
                    var neighbor = buffer[n];
                    if (!neighbor.IsWalkable || Mathf.Abs(neighbor.Height - tile.Height) > 1)
                    {
                        continue;
                    }

                    Assert.IsTrue(pathfinder.TryFindPath(tile.Coord, neighbor.Coord, path));
                    Assert.AreEqual(2, path.Count, "인접한 칸인데 경로가 두 칸이 아닙니다.");
                    Assert.AreEqual(tile.Coord, path[0]);
                    Assert.AreEqual(neighbor.Coord, path[1]);
                    return;
                }
            }

            Assert.Fail("이웃한 통행 가능 타일 쌍을 찾지 못했습니다.");
        }

        // ====================================================================================================
        // 4. 대각선 (8방향)
        // ====================================================================================================

        /// <summary>
        /// 열린 땅에서 비스듬한 목적지로 갈 때 대각선을 실제로 씁니다.
        ///
        /// 4방향만 쓰면 경로가 계단 모양이 되고 앵커가 그대로 지그재그로 행군합니다.
        /// 거리도 최대 41% 길어집니다.
        /// </summary>
        [Test]
        public void 비스듬한_목적지로는_대각선을_쓴다()
        {
            var grid = BuildOpenField(9, 9);
            var pathfinder = new GridPathfinder(grid);
            var path = new List<GridCoord>();

            var start = new GridCoord(1, 1);
            var goal = new GridCoord(7, 7);

            Assert.IsTrue(pathfinder.TryFindPath(start, goal, path));

            // 완전한 대각선이므로 7걸음(시작 포함)이면 충분합니다.
            // 4방향이었다면 13칸이 나옵니다.
            Assert.AreEqual(7, path.Count, "대각선을 쓰지 않고 계단으로 돌아갔습니다.");

            int diagonalSteps = 0;
            for (int i = 1; i < path.Count; i++)
            {
                var delta = path[i] - path[i - 1];
                if (delta.X != 0 && delta.Y != 0)
                {
                    diagonalSteps++;
                }
            }

            Assert.Greater(diagonalSteps, 0, "대각선 걸음이 하나도 없습니다.");
        }

        /// <summary>
        /// <b>모서리를 대각으로 뚫고 지나가면 안 됩니다.</b>
        ///
        /// 안 막으면 병사가 절벽 모서리를 비스듬히 통과합니다.
        /// 경로는 뚫렸는데 물리 이동은 막히므로 유닛이 그 자리에서 멈춰 섭니다.
        /// </summary>
        [Test]
        public void 모서리를_대각으로_통과하지_못한다()
        {
            var grid = BuildOpenField(5, 5);

            // (1,1) 에서 (2,2) 로 가는 대각선의 양옆을 모두 막습니다.
            Block(grid, new GridCoord(2, 1));
            Block(grid, new GridCoord(1, 2));

            var pathfinder = new GridPathfinder(grid);
            var path = new List<GridCoord>();

            // (1,1) 은 이제 완전히 갇혔습니다. 대각선으로도 빠져나갈 수 없어야 합니다.
            Block(grid, new GridCoord(0, 1));
            Block(grid, new GridCoord(1, 0));
            Block(grid, new GridCoord(0, 0));
            Block(grid, new GridCoord(2, 0));
            Block(grid, new GridCoord(0, 2));

            bool found = pathfinder.TryFindPath(new GridCoord(1, 1), new GridCoord(3, 3), path);

            Assert.IsFalse(found, "막힌 모서리를 대각으로 통과해 경로를 찾았습니다.");
        }

        [Test]
        public void 한쪽만_막힌_모서리도_통과하지_못한다()
        {
            // 느슨한 규칙이라면 한 칸만 막혀도 지나갑니다. 그러면 모서리를 긁고 지나가는 그림이 남습니다.
            var grid = BuildOpenField(5, 5);

            Block(grid, new GridCoord(2, 1));

            var pathfinder = new GridPathfinder(grid);
            var path = new List<GridCoord>();

            Assert.IsTrue(pathfinder.TryFindPath(new GridCoord(1, 1), new GridCoord(2, 2), path));

            // (1,1) → (2,2) 직행 대각선은 막혀야 하므로 최소 3칸을 거쳐 갑니다.
            Assert.GreaterOrEqual(path.Count, 3, "막힌 모서리를 대각으로 질러갔습니다.");
        }

        // ====================================================================================================
        // 5. Helpers
        // ====================================================================================================

        /// <summary>
        /// 전부 통행 가능한 평지 격자를 만듭니다.
        ///
        /// 절차적 섬은 모양을 통제할 수 없어 모서리 규칙을 시험하기 어렵습니다.
        /// <c>Tile</c>의 필드가 공개되어 있어 파생 정보를 직접 채울 수 있습니다.
        /// </summary>
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

        private static void Block(IslandGrid grid, GridCoord coord)
        {
            var tile = grid.GetTile(coord);
            tile.Type = TileType.Cliff;
            tile.IsWalkable = false;

            grid.WalkableTiles.Remove(tile);
        }
    }
}
