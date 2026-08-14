using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Systems.Grid;

namespace SRPG.Tests
{
    /// <summary>
    /// "여기서 저기까지 걸어갈 수 있는가"를 검증합니다.
    ///
    /// <b>왜 따로 떼어 검증하는가</b>
    ///
    /// 전장 생성기는 장애물을 놓을 때마다 이 검사로 "땅이 갈라지지 않았는가"를 확인합니다.
    /// 이 판정이 틀리면 <b>부대를 보낼 수 없는 땅</b>이 생기는데, 예외도 경고도 나지 않고
    /// "가끔 저기로 명령이 안 먹는다"로만 보입니다. 지형 생성은 시드마다 다르니
    /// 재현조차 어렵습니다.
    ///
    /// 생성기를 통째로 돌려서는 이걸 확인할 수 없습니다. 순수 자료구조로 떼어 두면
    /// 갈라진 지형을 손으로 만들어 놓고 답을 직접 확인할 수 있습니다.
    /// </summary>
    public sealed class GridConnectivityTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private const float Cell = 2f;

        /// <summary>전부 평지인 격자를 만듭니다.</summary>
        private static IslandGrid BuildOpenField(int width, int depth)
        {
            var grid = new IslandGrid(width, depth, Cell, 0.9f);

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

        private static Tile At(IslandGrid grid, int x, int y) => grid.GetTile(new GridCoord(x, y));

        private static void Block(IslandGrid grid, int x, int y)
        {
            var tile = At(grid, x, y);
            tile.Type = TileType.Cliff;
            tile.IsWalkable = false;

            grid.WalkableTiles.Remove(tile);
        }

        /// <summary>세로 벽을 세워 격자를 좌우로 가릅니다.</summary>
        private static void BuildWall(IslandGrid grid, int x)
        {
            for (int y = 0; y < grid.Depth; y++)
            {
                Block(grid, x, y);
            }
        }

        // ====================================================================================================
        // 2. 기본
        // ====================================================================================================

        [Test]
        public void 열린_평지는_전부_닿는다()
        {
            var grid = BuildOpenField(6, 5);
            var connectivity = new GridConnectivity(grid);

            Assert.AreEqual(30, connectivity.CountReachable(At(grid, 0, 0)));
        }

        [Test]
        public void 시작이_통행_불가면_0이다()
        {
            var grid = BuildOpenField(6, 5);
            Block(grid, 2, 2);

            var connectivity = new GridConnectivity(grid);

            Assert.AreEqual(0, connectivity.CountReachable(At(grid, 2, 2)));
            Assert.AreEqual(0, connectivity.CountReachable(null), "없는 칸에서 출발했는데 닿았습니다.");
        }

        [Test]
        public void 혼자_남은_칸은_자기_자신만_센다()
        {
            var grid = BuildOpenField(5, 5);

            // (2,2) 를 사방으로 둘러쌉니다.
            Block(grid, 1, 2);
            Block(grid, 3, 2);
            Block(grid, 2, 1);
            Block(grid, 2, 3);

            var connectivity = new GridConnectivity(grid);

            Assert.AreEqual(1, connectivity.CountReachable(At(grid, 2, 2)));
        }

        // ====================================================================================================
        // 3. 갈라진 지형 — 생성기가 되돌려야 하는 경우
        // ====================================================================================================

        [Test]
        public void 벽으로_갈라지면_반대편에_닿지_않는다()
        {
            var grid = BuildOpenField(7, 4);
            BuildWall(grid, 3);

            var connectivity = new GridConnectivity(grid);

            // 왼쪽 3열 × 4행 = 12칸만 닿아야 합니다.
            Assert.AreEqual(12, connectivity.CountReachable(At(grid, 0, 0)));

            // 오른쪽도 3열 × 4행 = 12칸입니다.
            Assert.AreEqual(12, connectivity.CountReachable(At(grid, 6, 0)));
        }

        [Test]
        public void 벽에_구멍이_하나만_있어도_이어진다()
        {
            var grid = BuildOpenField(7, 4);
            BuildWall(grid, 3);

            // 한 칸만 되살립니다.
            var gap = At(grid, 3, 1);
            gap.Type = TileType.Ground;
            gap.IsWalkable = true;
            grid.WalkableTiles.Add(gap);

            var connectivity = new GridConnectivity(grid);

            Assert.AreEqual(25, connectivity.CountReachable(At(grid, 0, 0)), "구멍이 있는데 갈라졌습니다.");
        }

        /// <summary>
        /// <b>이동 규칙과 같은 것을 봐야 합니다.</b>
        /// 단차로 막힌 곳은 통행 가능 표시가 붙어 있어도 닿을 수 없어야 합니다.
        /// 여기가 어긋나면 생성기가 "이어져 있다"고 판단한 땅을 부대가 못 갑니다.
        /// </summary>
        [Test]
        public void 단차로_막힌_곳은_통행_가능해도_닿지_않는다()
        {
            var grid = BuildOpenField(5, 3);

            // 3열 전체를 두 단 높은 대지로 만듭니다. 걸어 오를 수 없습니다.
            for (int y = 0; y < 3; y++)
            {
                At(grid, 3, y).Height = TraversalRules.MaxHeightDelta + 1;
                At(grid, 4, y).Height = TraversalRules.MaxHeightDelta + 1;
            }

            var connectivity = new GridConnectivity(grid);

            // 낮은 쪽 3열 × 3행 = 9칸만 닿습니다.
            Assert.AreEqual(9, connectivity.CountReachable(At(grid, 0, 0)));
        }

        /// <summary>
        /// <b>규칙이 보는 것은 언제나 한 걸음의 단차입니다.</b>
        ///
        /// 0 → 1 → 2 단으로 계단이 놓이면 총 고도차가 두 단이어도 전부 오를 수 있습니다.
        /// 앞 테스트의 "두 단을 한 번에 뛰는" 경우와 대비됩니다.
        /// 계단식 대지를 초크포인트로 쓰려면 이 차이가 성립해야 합니다 —
        /// 벽이 되는 것은 높이가 아니라 <b>한 걸음에 걸린 단차</b>입니다.
        /// </summary>
        [Test]
        public void 한_단씩_계단이면_총_고도차가_커도_오른다()
        {
            var grid = BuildOpenField(5, 3);

            for (int y = 0; y < 3; y++)
            {
                At(grid, 3, y).Height = TraversalRules.MaxHeightDelta;
                At(grid, 4, y).Height = TraversalRules.MaxHeightDelta * 2;
            }

            var connectivity = new GridConnectivity(grid);

            Assert.AreEqual(15, connectivity.CountReachable(At(grid, 0, 0)), "계단을 한 칸씩 오르지 못했습니다.");
        }

        // ====================================================================================================
        // 4. 구역 수집
        // ====================================================================================================

        [Test]
        public void 닿은_칸을_그대로_모은다()
        {
            var grid = BuildOpenField(7, 4);
            BuildWall(grid, 3);

            var connectivity = new GridConnectivity(grid);
            var region = new List<Tile>();

            int count = connectivity.CollectRegion(At(grid, 0, 0), region);

            Assert.AreEqual(count, region.Count, "센 수와 모은 수가 다릅니다.");
            Assert.AreEqual(12, region.Count);

            foreach (var tile in region)
            {
                Assert.Less(tile.Coord.X, 3, $"벽 너머의 칸이 섞였습니다: {tile.Coord}");
                Assert.IsTrue(tile.IsWalkable);
            }
        }

        [Test]
        public void 결과_버퍼는_호출할_때_비워진다()
        {
            var grid = BuildOpenField(4, 4);
            var connectivity = new GridConnectivity(grid);

            var region = new List<Tile> { At(grid, 0, 0), At(grid, 1, 1) };

            connectivity.CollectRegion(At(grid, 0, 0), region);

            Assert.AreEqual(16, region.Count, "이전 내용이 남아 있습니다.");
        }

        // ====================================================================================================
        // 5. 재사용 — 세대 스탬프가 새지 않는가
        // ====================================================================================================

        /// <summary>
        /// 작업 배열을 비우지 않고 세대 번호만 올리는 방식이라, 지난 탐색의 흔적이
        /// 다음 탐색에 새어 들어오면 <b>조용히 적게 세게 됩니다.</b>
        /// 생성기는 이 검사기 하나를 400번까지 재사용하므로 여기가 무너지면 전부 무너집니다.
        /// </summary>
        [Test]
        public void 여러_번_재사용해도_결과가_같다()
        {
            var grid = BuildOpenField(6, 5);
            var connectivity = new GridConnectivity(grid);

            int first = connectivity.CountReachable(At(grid, 0, 0));

            for (int i = 0; i < 50; i++)
            {
                Assert.AreEqual(first, connectivity.CountReachable(At(grid, 0, 0)), $"{i}번째 재사용에서 달라졌습니다.");
            }
        }

        /// <summary>
        /// 생성기가 실제로 하는 일입니다 — 막고, 검사하고, 되돌리고, 다시 검사합니다.
        /// </summary>
        [Test]
        public void 막았다_되돌리면_원래_결과로_돌아온다()
        {
            var grid = BuildOpenField(7, 4);
            var connectivity = new GridConnectivity(grid);

            int open = connectivity.CountReachable(At(grid, 0, 0));
            Assert.AreEqual(28, open);

            // 벽을 세웁니다.
            var wall = new List<Tile>();
            for (int y = 0; y < 4; y++)
            {
                var tile = At(grid, 3, y);
                tile.Type = TileType.Cliff;
                tile.IsWalkable = false;
                wall.Add(tile);
            }

            Assert.AreEqual(12, connectivity.CountReachable(At(grid, 0, 0)), "벽이 서지 않았습니다.");

            // 되돌립니다.
            foreach (var tile in wall)
            {
                tile.Type = TileType.Ground;
                tile.IsWalkable = true;
            }

            Assert.AreEqual(open, connectivity.CountReachable(At(grid, 0, 0)), "되돌렸는데 원래대로 돌아오지 않았습니다.");
        }

        [Test]
        public void 서로_다른_구역을_번갈아_세도_섞이지_않는다()
        {
            var grid = BuildOpenField(7, 4);
            BuildWall(grid, 3);

            var connectivity = new GridConnectivity(grid);

            for (int i = 0; i < 20; i++)
            {
                Assert.AreEqual(12, connectivity.CountReachable(At(grid, 0, 0)));
                Assert.AreEqual(12, connectivity.CountReachable(At(grid, 6, 3)));
            }
        }
    }
}
