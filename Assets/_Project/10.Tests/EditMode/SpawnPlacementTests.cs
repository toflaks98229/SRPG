using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Systems.Formation;
using SRPG.Systems.Grid;

namespace SRPG.Tests
{
    /// <summary>
    /// 전투를 시작할 때 분대를 어느 칸에 세울지 검증합니다.
    ///
    /// 조립 지점 안에 있던 시절에는 "분대가 왜 저기 섰는가"를 씬을 재생해야만 볼 수 있었습니다.
    /// 좁은 섬·해안뿐인 섬·자리가 모자라는 경우는 특정 시드에서만 나오므로
    /// 재생으로 확인하려면 운이 좋아야 했습니다.
    /// </summary>
    public sealed class SpawnPlacementTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private const float Cell = 2f;

        /// <summary>전부 안쪽 평지인 격자를 만듭니다.</summary>
        private static IslandGrid BuildInland(int width, int depth)
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
                    tile.IsCoastal = false;
                    tile.WorldCenter = grid.CoordToWorld(tile.Coord);

                    grid.WalkableTiles.Add(tile);
                }
            }

            return grid;
        }

        private static Tile At(IslandGrid grid, int x, int y) => grid.GetTile(new GridCoord(x, y));

        /// <summary>그 칸을 통행 불가로 만들고 목록에서 뺍니다.</summary>
        private static void Block(IslandGrid grid, int x, int y)
        {
            var tile = At(grid, x, y);
            tile.Type = TileType.Cliff;
            tile.IsWalkable = false;

            grid.WalkableTiles.Remove(tile);
        }

        /// <summary>통행 가능한 칸을 지정한 것들만 남깁니다.</summary>
        private static void KeepOnly(IslandGrid grid, params GridCoord[] keep)
        {
            var wanted = new HashSet<GridCoord>(keep);

            for (int y = 0; y < grid.Depth; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    if (!wanted.Contains(new GridCoord(x, y)))
                    {
                        Block(grid, x, y);
                    }
                }
            }
        }

        // ====================================================================================================
        // 2. 기본
        // ====================================================================================================

        [Test]
        public void 요청한_만큼_고른다()
        {
            var grid = BuildInland(15, 15);
            var result = new List<Tile>();

            Assert.AreEqual(4, SpawnPlacement.SelectSquadTiles(grid, 4, result));
            Assert.AreEqual(4, result.Count);
        }

        /// <summary>
        /// 섬 중심에 가까운 칸부터 채웁니다. 가장자리에 세우면 상륙 지점에 바로 노출됩니다.
        /// </summary>
        [Test]
        public void 중심에_가까운_칸부터_고른다()
        {
            var grid = BuildInland(9, 9);
            var result = new List<Tile>();

            SpawnPlacement.SelectSquadTiles(grid, 1, result);

            // 9칸 격자의 한가운데인 (4,4) 가 월드 원점입니다.
            Assert.AreEqual(new GridCoord(4, 4), result[0].Coord);
        }

        [Test]
        public void 고른_칸은_모두_통행_가능하다()
        {
            var grid = BuildInland(11, 11);

            Block(grid, 5, 5);
            Block(grid, 5, 6);
            Block(grid, 6, 5);

            var result = new List<Tile>();
            SpawnPlacement.SelectSquadTiles(grid, 4, result);

            foreach (var tile in result)
            {
                Assert.IsTrue(tile.IsWalkable, $"설 수 없는 칸을 골랐습니다: {tile.Coord}");
            }
        }

        [Test]
        public void 결과_버퍼는_호출할_때_비워진다()
        {
            var grid = BuildInland(9, 9);
            var result = new List<Tile> { At(grid, 0, 0), At(grid, 1, 1) };

            SpawnPlacement.SelectSquadTiles(grid, 2, result);

            Assert.AreEqual(2, result.Count, "이전 내용이 남아 있습니다.");
        }

        [Test]
        public void 요청이_0이면_아무것도_고르지_않는다()
        {
            var grid = BuildInland(9, 9);
            var result = new List<Tile>();

            Assert.AreEqual(0, SpawnPlacement.SelectSquadTiles(grid, 0, result));
            Assert.AreEqual(0, result.Count);
        }

        // ====================================================================================================
        // 3. 간격
        // ====================================================================================================

        /// <summary>
        /// 붙여 세우면 진형이 겹쳐 어느 분대가 어디 있는지 구분할 수 없고,
        /// 병사들이 서로 밀어내며 뒤엉킵니다.
        /// </summary>
        [Test]
        public void 분대끼리_최소_간격을_둔다()
        {
            var grid = BuildInland(21, 21);
            var result = new List<Tile>();

            SpawnPlacement.SelectSquadTiles(grid, 4, result);

            Assert.AreEqual(4, result.Count);

            for (int i = 0; i < result.Count; i++)
            {
                for (int j = i + 1; j < result.Count; j++)
                {
                    Assert.GreaterOrEqual(
                        GridCoord.ChebyshevDistance(result[i].Coord, result[j].Coord),
                        SpawnPlacement.DefaultMinSpacing,
                        $"{result[i].Coord} 와 {result[j].Coord} 가 너무 붙었습니다.");
                }
            }
        }

        /// <summary>
        /// <b>간격은 지킬 수 있을 때만 지킵니다.</b>
        /// 여기서 빈손으로 돌아가면 그 분대는 전장에 아예 서지 못합니다 —
        /// 겹쳐 서는 것보다 나쁜 결과입니다.
        /// </summary>
        [Test]
        public void 자리가_모자라면_간격을_풀고_채운다()
        {
            var grid = BuildInland(9, 9);

            // 2×2 만 남깁니다. 간격 3을 지키면 한 자리밖에 못 잡습니다.
            KeepOnly(grid,
                new GridCoord(4, 4), new GridCoord(5, 4),
                new GridCoord(4, 5), new GridCoord(5, 5));

            var result = new List<Tile>();

            Assert.AreEqual(4, SpawnPlacement.SelectSquadTiles(grid, 4, result), "간격 때문에 분대를 세우지 못했습니다.");
            Assert.AreEqual(4, result.Count);
            CollectionAssertUnique(result);
        }

        [Test]
        public void 땅이_모자라면_있는_만큼만_고른다()
        {
            var grid = BuildInland(9, 9);

            KeepOnly(grid, new GridCoord(4, 4), new GridCoord(5, 4));

            var result = new List<Tile>();

            Assert.AreEqual(2, SpawnPlacement.SelectSquadTiles(grid, 5, result));
        }

        [Test]
        public void 설_땅이_없으면_빈손으로_돌아온다()
        {
            var grid = BuildInland(9, 9);
            KeepOnly(grid);

            var result = new List<Tile>();

            Assert.AreEqual(0, SpawnPlacement.SelectSquadTiles(grid, 3, result));
        }

        // ====================================================================================================
        // 4. 해안 회피
        // ====================================================================================================

        /// <summary>
        /// 해안에 세우면 상륙정이 닿는 자리에 그대로 서 있게 됩니다.
        /// </summary>
        [Test]
        public void 해안은_피해_안쪽에_선다()
        {
            var grid = BuildInland(11, 11);

            // 바깥 한 겹을 해안으로 표시합니다.
            for (int y = 0; y < 11; y++)
            {
                for (int x = 0; x < 11; x++)
                {
                    if (x == 0 || y == 0 || x == 10 || y == 10)
                    {
                        At(grid, x, y).IsCoastal = true;
                    }
                }
            }

            var result = new List<Tile>();
            SpawnPlacement.SelectSquadTiles(grid, 4, result);

            foreach (var tile in result)
            {
                Assert.IsFalse(tile.IsCoastal, $"해안에 분대를 세웠습니다: {tile.Coord}");
            }
        }

        /// <summary>
        /// 안쪽 평지가 없을 만큼 섬이 작으면 해안이라도 씁니다.
        /// 세울 곳이 없다고 물러나면 전투가 시작되지 않습니다.
        /// </summary>
        [Test]
        public void 전부_해안이면_해안에라도_선다()
        {
            var grid = BuildInland(7, 7);

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                grid.WalkableTiles[i].IsCoastal = true;
            }

            var result = new List<Tile>();

            Assert.AreEqual(3, SpawnPlacement.SelectSquadTiles(grid, 3, result), "설 곳이 없다고 물러났습니다.");
        }

        /// <summary>
        /// 물가에는 서지 않습니다. 넉백 한 번에 병사가 빠져 죽는 자리입니다.
        /// </summary>
        [Test]
        public void 물가에는_서지_않는다()
        {
            var grid = BuildInland(11, 11);

            At(grid, 5, 5).IsCoastal = true;
            At(grid, 5, 6).IsCoastal = true;

            var result = new List<Tile>();
            SpawnPlacement.SelectSquadTiles(grid, 4, result);

            foreach (var tile in result)
            {
                Assert.IsFalse(tile.IsCoastal, $"물가에 섰습니다: {tile.Coord}");
            }
        }

        // ====================================================================================================
        // 5. Private Methods
        // ====================================================================================================

        private static void CollectionAssertUnique(List<Tile> tiles)
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                for (int j = i + 1; j < tiles.Count; j++)
                {
                    Assert.AreNotEqual(
                        tiles[i].Coord,
                        tiles[j].Coord,
                        "같은 칸을 두 번 골랐습니다. 두 분대가 겹쳐 섭니다.");
                }
            }
        }
    }
}
