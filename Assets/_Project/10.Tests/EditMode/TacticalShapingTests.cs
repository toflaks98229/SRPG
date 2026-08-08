using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 섬이 <b>지킬 만한 문제</b>인지 검증합니다.
    ///
    /// <b>여기서 보는 것은 지형이 아니라 게임입니다</b>
    ///
    /// 자연스러운 섬과 재미있는 섬은 다릅니다. 침식은 깎아 고르게 만드는 힘이라
    /// 오래 돌릴수록 모든 곳이 완만해지고 모든 곳이 이어집니다.
    /// 어디로든 갈 수 있으면 지킬 곳이 없습니다.
    ///
    /// Bad North의 섬은 풍경이 아니라 문제입니다 —
    /// "부대는 셋인데 적이 올라올 곳은 넷이다. 무엇을 포기할 것인가."
    ///
    /// 그 문제가 성립하려면 다음이 참이어야 합니다.
    ///   · 오를 수 없는 면이 있다        — 없으면 전선이 무한히 넓어진다
    ///   · 좁은 통로가 있다              — 한 부대가 여럿을 막을 수 있어야 한다
    ///   · 그럼에도 모든 목표에 닿는다   — 못 가는 목표는 목표가 아니다
    ///   · 적이 여러 곳에서 온다         — 한 곳이면 고민할 게 없다
    ///
    /// 앞의 셋은 서로 싸웁니다. 장벽을 늘리면 연결이 끊기고, 전부 이으면 장벽이 사라집니다.
    /// 이 검사들은 그 균형이 실제로 잡혔는지를 봅니다.
    /// </summary>
    public sealed class TacticalShapingTests
    {
        // ====================================================================================================
        // 1. Setup
        // ====================================================================================================

        /// <summary>시드를 여러 개 도는 이유는 한 판만 우연히 좋을 수 있기 때문입니다.</summary>
        private static readonly int[] Seeds = { 101, 202, 303, 404, 505 };

        private static IslandGrid CreateIsland(int seed)
        {
            return IslandGenerator.Generate(IslandSettings.CreateDefault(), seed);
        }

        // ====================================================================================================
        // 2. 장벽이 있는가
        // ====================================================================================================

        /// <summary>
        /// 오를 수 없는 면이 없으면 전선이 사방으로 열립니다.
        /// 그러면 부대를 어디에 두든 같아지고, 배치가 선택이 아니게 됩니다.
        /// </summary>
        [Test]
        public void 오를_수_없는_면이_있다()
        {
            foreach (int seed in Seeds)
            {
                var grid = CreateIsland(seed);

                int cliffs = 0;
                int land = 0;

                for (int i = 0; i < grid.AllTiles.Count; i++)
                {
                    var tile = grid.AllTiles[i];

                    if (tile.IsWater)
                    {
                        continue;
                    }

                    land++;

                    if (tile.Type == TileType.Cliff)
                    {
                        cliffs++;
                    }
                }

                Assert.Greater(cliffs, land * 0.03f,
                    $"시드 {seed}: 육지 {land}칸 중 절벽이 {cliffs}칸뿐입니다. 장벽이 없습니다.");

                Assert.Less(cliffs, land * 0.6f,
                    $"시드 {seed}: 육지 {land}칸 중 {cliffs}칸이 절벽입니다. 걸을 땅이 없습니다.");
            }
        }

        /// <summary>
        /// <b>보이는 것과 갈 수 있는 곳이 같아야 합니다.</b>
        ///
        /// 통행 불가는 손으로 찍은 것이 아니라 경사에서 읽어 낸 값이므로,
        /// 화면에 암반으로 그려지는 자리와 일치해야 합니다.
        /// 어긋나면 플레이어가 "갈 수 있어 보이는데 막힌" 곳을 만나게 됩니다.
        /// </summary>
        [Test]
        public void 절벽은_실제로_가파른_자리다()
        {
            var grid = CreateIsland(303);
            var field = grid.Height;

            int checkedTiles = 0;
            int steep = 0;

            for (int i = 0; i < grid.AllTiles.Count; i++)
            {
                var tile = grid.AllTiles[i];

                if (tile.Type != TileType.Cliff)
                {
                    continue;
                }

                checkedTiles++;

                if (field.SampleSlope(tile.WorldCenter.x, tile.WorldCenter.z) > TacticalShaping.ClimbLimit * 0.6f)
                {
                    steep++;
                }
            }

            Assert.Greater(checkedTiles, 0, "절벽이 없습니다.");
            Assert.Greater(
                steep,
                checkedTiles * 0.7f,
                $"절벽 {checkedTiles}칸 중 {steep}칸만 실제로 가파릅니다. 보이는 것과 막힌 곳이 다릅니다.");
        }

        // ====================================================================================================
        // 3. 이어져 있는가
        // ====================================================================================================

        /// <summary>
        /// 못 가는 목표는 목표가 아닙니다.
        /// 모든 상륙 지점에서 모든 가옥까지 걸어갈 수 있어야 합니다.
        /// </summary>
        [Test]
        public void 모든_상륙_지점에서_모든_가옥에_닿는다()
        {
            foreach (int seed in Seeds)
            {
                var grid = CreateIsland(seed);

                Assert.Greater(grid.HouseTiles.Count, 0, $"시드 {seed}: 가옥이 없습니다.");
                Assert.Greater(grid.LandingZones.Count, 0, $"시드 {seed}: 상륙 지점이 없습니다.");

                for (int z = 0; z < grid.LandingZones.Count; z++)
                {
                    var zone = grid.LandingZones[z];
                    if (zone.Count == 0)
                    {
                        continue;
                    }

                    var reachable = Reachable(grid, zone[zone.Count / 2]);

                    for (int h = 0; h < grid.HouseTiles.Count; h++)
                    {
                        Assert.IsTrue(
                            reachable.Contains(grid.HouseTiles[h].Coord),
                            $"시드 {seed}: 상륙 구역 {z} 에서 가옥 {grid.HouseTiles[h].Coord} 에 갈 수 없습니다.");
                    }
                }
            }
        }

        /// <summary>
        /// 걸을 수 있는 땅이 여러 조각으로 흩어지면 안 됩니다.
        /// 부대를 옮길 수 없는 구역이 생기면 그 구역은 게임에서 없는 것과 같습니다.
        /// </summary>
        [Test]
        public void 걸을_수_있는_땅이_거의_하나로_이어진다()
        {
            foreach (int seed in Seeds)
            {
                var grid = CreateIsland(seed);

                Assert.Greater(grid.WalkableTiles.Count, 0, $"시드 {seed}: 걸을 땅이 없습니다.");

                var reachable = Reachable(grid, grid.WalkableTiles[0]);

                // 완전히 하나일 필요는 없습니다. 절벽 위 작은 턱 같은 것은 남아도 됩니다.
                Assert.Greater(
                    reachable.Count,
                    grid.WalkableTiles.Count * 0.85f,
                    $"시드 {seed}: 걸을 수 있는 {grid.WalkableTiles.Count}칸 중 " +
                    $"{reachable.Count}칸만 이어져 있습니다.");
            }
        }

        // ====================================================================================================
        // 4. 지킬 자리가 있는가
        // ====================================================================================================

        /// <summary>
        /// <b>이것이 전술 지형의 핵심입니다.</b>
        ///
        /// 초크포인트란 그 칸을 막으면 뒤가 안전해지는 자리입니다.
        /// 하나도 없으면 적이 사방에서 밀려들고, 부대 배치가 의미를 잃습니다.
        /// </summary>
        [Test]
        public void 초크포인트가_존재한다()
        {
            int withChokes = 0;

            foreach (int seed in Seeds)
            {
                var grid = CreateIsland(seed);

                if (CountChokePoints(grid) > 0)
                {
                    withChokes++;
                }
            }

            Assert.Greater(
                withChokes,
                Seeds.Length / 2,
                $"{Seeds.Length}개 시드 중 {withChokes}개에만 초크포인트가 있습니다. " +
                "대부분의 섬이 사방으로 열려 있어 지킬 자리가 없습니다.");
        }

        /// <summary>
        /// 고지대가 있어야 궁수의 자리가 생깁니다.
        ///
        /// 다만 <b>드물어야</b> 합니다. 흔하면 아무 데나 올려도 되니 선택이 아니게 됩니다.
        /// </summary>
        [Test]
        public void 고지대가_드물게_존재한다()
        {
            var grid = CreateIsland(202);

            int high = 0;
            int walkable = grid.WalkableTiles.Count;

            for (int i = 0; i < walkable; i++)
            {
                if (grid.WalkableTiles[i].Height >= 2)
                {
                    high++;
                }
            }

            Assert.Greater(high, 0, "걸어 올라갈 수 있는 고지대가 없습니다.");
            Assert.Less(high, walkable * 0.5f, "고지대가 절반을 넘습니다. 높은 자리가 특별하지 않습니다.");
        }

        // ====================================================================================================
        // 5. 적이 여러 곳에서 오는가
        // ====================================================================================================

        /// <summary>
        /// 상륙 지점이 하나면 고민할 것이 없습니다. 전부 거기 두면 됩니다.
        /// 여럿이어야 "무엇을 포기할 것인가"가 질문이 됩니다.
        /// </summary>
        [Test]
        public void 상륙_지점이_여럿이고_흩어져_있다()
        {
            foreach (int seed in Seeds)
            {
                var grid = CreateIsland(seed);

                Assert.GreaterOrEqual(grid.LandingZones.Count, 2,
                    $"시드 {seed}: 상륙 지점이 {grid.LandingZones.Count}곳뿐입니다.");

                // 구역끼리 충분히 떨어져 있어야 한 부대가 둘을 동시에 막지 못합니다.
                for (int a = 0; a < grid.LandingZones.Count; a++)
                {
                    for (int b = a + 1; b < grid.LandingZones.Count; b++)
                    {
                        var first = Centroid(grid.LandingZones[a]);
                        var second = Centroid(grid.LandingZones[b]);

                        Assert.Greater(
                            Vector3.Distance(first, second),
                            grid.CellSize * 2f,
                            $"시드 {seed}: 상륙 구역 {a} 와 {b} 가 붙어 있습니다.");
                    }
                }
            }
        }

        // ====================================================================================================
        // 6. Helpers
        // ====================================================================================================

        /// <summary>
        /// 길찾기와 같은 규칙으로 갈 수 있는 칸을 모읍니다.
        /// 규칙이 다르면 "이어진 줄 알았는데 못 가는" 결과가 나옵니다.
        /// </summary>
        private static HashSet<GridCoord> Reachable(IslandGrid grid, Tile start)
        {
            var visited = new HashSet<GridCoord> { start.Coord };
            var queue = new Queue<Tile>();

            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                {
                    var neighbor = grid.GetTile(current.Coord + GridCoord.Neighbors4[n]);

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

            return visited;
        }

        /// <summary>그 칸을 막으면 갈 수 있는 범위가 줄어드는 자리의 수입니다.</summary>
        private static int CountChokePoints(IslandGrid grid)
        {
            if (grid.WalkableTiles.Count == 0)
            {
                return 0;
            }

            int baseline = Reachable(grid, grid.WalkableTiles[0]).Count;
            int chokes = 0;

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];

                if (tile == grid.WalkableTiles[0])
                {
                    continue;
                }

                var saved = tile.IsWalkable;
                tile.IsWalkable = false;

                int reduced = Reachable(grid, grid.WalkableTiles[0]).Count;

                tile.IsWalkable = saved;

                // 한 칸 막았는데 두 칸 넘게 못 가게 되면 그 칸이 통로입니다.
                if (reduced < baseline - 2)
                {
                    chokes++;
                }
            }

            return chokes;
        }

        private static Vector3 Centroid(List<Tile> tiles)
        {
            var sum = Vector3.zero;

            for (int i = 0; i < tiles.Count; i++)
            {
                sum += tiles[i].WorldCenter;
            }

            return tiles.Count > 0 ? sum / tiles.Count : Vector3.zero;
        }
    }
}
