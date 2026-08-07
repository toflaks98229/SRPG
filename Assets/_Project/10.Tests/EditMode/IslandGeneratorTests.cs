using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 절차적 섬 생성이 지켜야 할 불변 조건을 검증합니다.
    ///
    /// 절차적 생성은 "가끔 이상한 섬이 나오는" 실패가 가장 잡기 어렵습니다.
    /// 눈으로 확인하는 대신 여기서 규칙을 못 박아 둡니다.
    /// </summary>
    public sealed class IslandGeneratorTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private static IslandSettings CreateSettings()
        {
            var settings = IslandSettings.CreateDefault();
            settings.Width = 30;
            settings.Depth = 30;
            settings.HouseCount = 3;
            settings.LandingZoneCount = 4;
            return settings;
        }

        // ====================================================================================================
        // 2. Tests
        // ====================================================================================================

        [Test]
        public void 생성된_섬에는_통행_가능한_땅이_있다()
        {
            var grid = IslandGenerator.Generate(CreateSettings(), seedOverride: 12345);

            Assert.Greater(grid.WalkableTiles.Count, 0, "통행 가능한 타일이 하나도 없습니다.");
        }

        [Test]
        public void 통행_가능한_모든_타일은_하나로_이어져_있다()
        {
            // 섬이 두 조각으로 갈라지면 분대가 닿을 수 없는 영역이 생기고 방어가 불가능해집니다.
            // 바위 배치가 연결성을 깨뜨리지 않는지 확인하는 것이 이 테스트의 핵심입니다.
            for (int seed = 1; seed <= 20; seed++)
            {
                var grid = IslandGenerator.Generate(CreateSettings(), seed);
                int reachable = CountReachableFromFirstWalkable(grid);

                Assert.AreEqual(
                    grid.WalkableTiles.Count,
                    reachable,
                    $"seed={seed}: 섬이 분리되었습니다. 전체 {grid.WalkableTiles.Count}, 도달 {reachable}");
            }
        }

        [Test]
        public void 같은_시드는_같은_섬을_만든다()
        {
            var a = IslandGenerator.Generate(CreateSettings(), seedOverride: 777);
            var b = IslandGenerator.Generate(CreateSettings(), seedOverride: 777);

            Assert.AreEqual(a.WalkableTiles.Count, b.WalkableTiles.Count, "통행 가능 타일 수가 다릅니다.");

            for (int i = 0; i < a.AllTiles.Count; i++)
            {
                Assert.AreEqual(a.AllTiles[i].Type, b.AllTiles[i].Type, $"{a.AllTiles[i].Coord} 지형이 다릅니다.");
                Assert.AreEqual(a.AllTiles[i].Height, b.AllTiles[i].Height, $"{a.AllTiles[i].Coord} 고도가 다릅니다.");
            }
        }

        [Test]
        public void 상륙_구역이_최소_하나_이상_만들어진다()
        {
            for (int seed = 1; seed <= 10; seed++)
            {
                var grid = IslandGenerator.Generate(CreateSettings(), seed);

                Assert.Greater(grid.LandingZones.Count, 0, $"seed={seed}: 상륙 구역이 없습니다. 적이 진입할 수 없습니다.");

                for (int i = 0; i < grid.LandingZones.Count; i++)
                {
                    Assert.Greater(grid.LandingZones[i].Count, 0, $"seed={seed}: 빈 상륙 구역이 있습니다.");
                }
            }
        }

        [Test]
        public void 가옥은_설정한_수를_넘지_않고_해안에_놓이지_않는다()
        {
            var settings = CreateSettings();
            var grid = IslandGenerator.Generate(settings, seedOverride: 4242);

            Assert.LessOrEqual(grid.HouseTiles.Count, settings.HouseCount, "가옥이 설정보다 많습니다.");

            for (int i = 0; i < grid.HouseTiles.Count; i++)
            {
                Assert.IsFalse(
                    grid.HouseTiles[i].IsCoastal,
                    $"{grid.HouseTiles[i].Coord} 가옥이 해안에 있습니다. 상륙 즉시 파괴됩니다.");
            }
        }

        [Test]
        public void 인접한_통행_가능_타일의_고도차는_1을_넘지_않는다()
        {
            // 고도차가 2 이상이면 경로 탐색이 막아 버려 통행 불가 구역이 생깁니다.
            var grid = IslandGenerator.Generate(CreateSettings(), seedOverride: 999);

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];

                for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                {
                    var neighbor = grid.GetTile(tile.Coord + GridCoord.Neighbors4[n]);
                    if (neighbor == null || !neighbor.IsWalkable)
                    {
                        continue;
                    }

                    Assert.LessOrEqual(
                        Mathf.Abs(neighbor.Height - tile.Height),
                        1,
                        $"{tile.Coord} 와 {neighbor.Coord} 의 고도차가 1을 넘습니다.");
                }
            }
        }

        [Test]
        public void 격자_테두리는_항상_바다다()
        {
            var grid = IslandGenerator.Generate(CreateSettings(), seedOverride: 31337);

            for (int x = 0; x < grid.Width; x++)
            {
                Assert.IsTrue(grid.GetTile(new GridCoord(x, 0)).IsWater, "아래쪽 테두리에 육지가 있습니다.");
                Assert.IsTrue(grid.GetTile(new GridCoord(x, grid.Depth - 1)).IsWater, "위쪽 테두리에 육지가 있습니다.");
            }

            for (int y = 0; y < grid.Depth; y++)
            {
                Assert.IsTrue(grid.GetTile(new GridCoord(0, y)).IsWater, "왼쪽 테두리에 육지가 있습니다.");
                Assert.IsTrue(grid.GetTile(new GridCoord(grid.Width - 1, y)).IsWater, "오른쪽 테두리에 육지가 있습니다.");
            }
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 첫 번째 통행 가능 타일에서 너비 우선 탐색으로 도달 가능한 타일 수를 셉니다.
        /// </summary>
        private static int CountReachableFromFirstWalkable(IslandGrid grid)
        {
            if (grid.WalkableTiles.Count == 0)
            {
                return 0;
            }

            var visited = new HashSet<GridCoord>();
            var queue = new Queue<Tile>();

            var start = grid.WalkableTiles[0];
            queue.Enqueue(start);
            visited.Add(start.Coord);

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

            return visited.Count;
        }
    }
}
