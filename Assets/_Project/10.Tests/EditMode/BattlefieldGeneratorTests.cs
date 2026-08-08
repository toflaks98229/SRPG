using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Systems.Battlefield;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Tests.EditMode
{
    /// <summary>
    /// 전장 생성기가 <b>싸울 수 있는 땅</b>을 내놓는지 봅니다.
    ///
    /// <b>무엇을 검사하는가</b>
    ///
    /// 지형이 예뻐 보이는지는 여기서 알 수 없습니다. 대신 지형이 갖춰야 할
    /// 최소 조건을 봅니다 — 걸을 곳이 있는가, 서로 이어져 있는가,
    /// 상륙할 물가가 있는가, 그리고 <b>보이는 지면과 타일 높이가 같은가</b>.
    ///
    /// 마지막 항목이 이 설계의 핵심입니다. 지형은 터레인이 그리고 규칙은 타일이
    /// 정하므로, 둘이 어긋나면 유닛이 허공에 서거나 땅에 묻힙니다.
    ///
    /// <b>왜 EditMode 인가</b>
    ///
    /// 생성기는 유니티 터레인을 만들지 않습니다. 숫자와 격자만 내놓으므로
    /// 씬 없이 검사할 수 있습니다. 그것이 하이트맵을 따로 뗀 이유입니다.
    /// </summary>
    public sealed class BattlefieldGeneratorTests
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        private const int Seed = 20260808;

        // ====================================================================================================
        // 2. Tests - Basic Shape
        // ====================================================================================================

        [Test]
        public void Generate_ProducesRequestedSize()
        {
            var field = BattlefieldGenerator.Generate(BattlefieldSpec.CreateDefault(TerrainKind.Plains, Seed));

            Assert.AreEqual(32, field.Grid.Width, "가로 칸 수가 지시와 다릅니다.");
            Assert.AreEqual(32, field.Grid.Depth, "세로 칸 수가 지시와 다릅니다.");
        }

        [Test]
        public void Generate_HeightmapResolutionIsPowerOfTwoPlusOne()
        {
            var field = BattlefieldGenerator.Generate(BattlefieldSpec.CreateDefault(TerrainKind.Hills, Seed));

            int side = field.Heightmap.Resolution - 1;

            Assert.Greater(side, 0, "하이트맵이 비어 있습니다.");
            Assert.AreEqual(0, side & (side - 1), $"터레인은 2의 거듭제곱 + 1 을 요구합니다. 지금은 {field.Heightmap.Resolution} 입니다.");
        }

        [Test]
        public void Generate_SameSeedProducesSameField()
        {
            var first = BattlefieldGenerator.Generate(BattlefieldSpec.CreateDefault(TerrainKind.Rocky, Seed));
            var second = BattlefieldGenerator.Generate(BattlefieldSpec.CreateDefault(TerrainKind.Rocky, Seed));

            for (int i = 0; i < first.Grid.AllTiles.Count; i++)
            {
                Assert.AreEqual(
                    first.Grid.AllTiles[i].Type,
                    second.Grid.AllTiles[i].Type,
                    $"같은 시드인데 {first.Grid.AllTiles[i].Coord} 의 지형이 다릅니다.");
            }
        }

        // ====================================================================================================
        // 3. Tests - Playability
        // ====================================================================================================

        [Test]
        public void Generate_HasEnoughWalkableGround()
        {
            foreach (TerrainKind kind in System.Enum.GetValues(typeof(TerrainKind)))
            {
                var field = BattlefieldGenerator.Generate(BattlefieldSpec.CreateDefault(kind, Seed));

                float ratio = (float)field.Grid.WalkableTiles.Count / field.Grid.AllTiles.Count;

                // 절반이 물과 바위면 전장이 아니라 통로입니다.
                Assert.Greater(ratio, 0.25f, $"{kind} 전장에서 걸을 수 있는 땅이 {ratio:P0} 뿐입니다.");
            }
        }

        [Test]
        public void Generate_WalkableAreaIsOneConnectedPiece()
        {
            foreach (TerrainKind kind in System.Enum.GetValues(typeof(TerrainKind)))
            {
                var field = BattlefieldGenerator.Generate(BattlefieldSpec.CreateDefault(kind, Seed));

                int reached = CountReachable(field.Grid);

                Assert.AreEqual(
                    field.Grid.WalkableTiles.Count,
                    reached,
                    $"{kind} 전장이 갈라졌습니다. 부대를 보낼 수 없는 땅이 생깁니다.");
            }
        }

        [Test]
        public void Generate_HasFacingDeploymentZones()
        {
            var field = BattlefieldGenerator.Generate(BattlefieldSpec.CreateDefault(seed: 20260809));

            Assert.Greater(field.Grid.PlayerDeployment.Count, 0, "아군이 설 자리가 없습니다.");
            Assert.Greater(field.Grid.EnemyDeployment.Count, 0, "적이 설 자리가 없습니다.");
        }


        // ====================================================================================================
        // 4. Tests - Terrain and Grid Agree
        // ====================================================================================================

        [Test]
        public void TileCentersSitOnTheTerrainSurface()
        {
            var field = BattlefieldGenerator.Generate(BattlefieldSpec.CreateDefault(TerrainKind.Hills, Seed));

            float worst = 0f;

            for (int i = 0; i < field.Grid.WalkableTiles.Count; i++)
            {
                var tile = field.Grid.WalkableTiles[i];

                float surface = field.Heightmap.SampleHeight(
                    tile.WorldCenter.x, tile.WorldCenter.z, field.Origin);

                worst = Mathf.Max(worst, Mathf.Abs(tile.WorldCenter.y - surface));
            }

            // 어긋나면 유닛이 허공에 서거나 땅에 묻힙니다.
            Assert.Less(worst, 0.01f, $"타일 높이가 지면과 최대 {worst:F3} 만큼 어긋납니다.");
        }

        [Test]
        public void SteepGroundIsNotWalkable()
        {
            var profile = BattlefieldProfile.CreateDefault(TerrainKind.Hills);

            try
            {
                var field = BattlefieldGenerator.Generate(
                    BattlefieldSpec.CreateDefault(TerrainKind.Hills, Seed), profile);

                for (int i = 0; i < field.Grid.WalkableTiles.Count; i++)
                {
                    var tile = field.Grid.WalkableTiles[i];

                    float slope = field.Heightmap.SampleSlopeDegrees(
                        tile.WorldCenter.x, tile.WorldCenter.z, field.Origin);

                    Assert.LessOrEqual(
                        slope,
                        profile.ClimbLimitDegrees + 0.5f,
                        $"{tile.Coord} 는 {slope:F1}도 경사인데 걸을 수 있다고 되어 있습니다.");
                }
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void WalkableGroundStaysAboveTheWaterline()
        {
            var field = BattlefieldGenerator.Generate(BattlefieldSpec.CreateDefault(TerrainKind.Plains, Seed));

            for (int i = 0; i < field.Grid.WalkableTiles.Count; i++)
            {
                var tile = field.Grid.WalkableTiles[i];

                Assert.GreaterOrEqual(
                    tile.WorldCenter.y,
                    field.SeaLevel,
                    $"{tile.Coord} 가 물에 잠겼는데 걸을 수 있다고 되어 있습니다.");
            }
        }

        // ====================================================================================================
        // 5. Tests - Terrain Kind Actually Changes The Field
        // ====================================================================================================

        [Test]
        public void HillsAreTallerThanPlains()
        {
            var hills = BattlefieldGenerator.Generate(BattlefieldSpec.CreateDefault(TerrainKind.Hills, Seed));
            var plains = BattlefieldGenerator.Generate(BattlefieldSpec.CreateDefault(TerrainKind.Plains, Seed));

            // 같은 좌표에서 다른 지형이 나와야 월드맵을 붙일 의미가 있습니다.
            Assert.Greater(
                Relief(hills),
                Relief(plains),
                "구릉이 평야보다 평평합니다. 지형 종류가 결과에 반영되지 않았습니다.");
        }

        [Test]
        public void ForestHasMoreObstaclesThanPlains()
        {
            var forest = BattlefieldGenerator.Generate(BattlefieldSpec.CreateDefault(TerrainKind.Forest, Seed));
            var plains = BattlefieldGenerator.Generate(BattlefieldSpec.CreateDefault(TerrainKind.Plains, Seed));

            Assert.Greater(
                CountBlocked(forest.Grid),
                CountBlocked(plains.Grid),
                "숲이 평야보다 트여 있습니다. 지형 종류가 결과에 반영되지 않았습니다.");
        }

        // ====================================================================================================
        // 6. Helpers
        // ====================================================================================================

        /// <summary>가장 높은 곳과 가장 낮은 육지의 높이 차입니다.</summary>
        private static float Relief(Battlefield field)
        {
            float low = float.MaxValue;
            float high = float.MinValue;

            for (int i = 0; i < field.Grid.WalkableTiles.Count; i++)
            {
                float y = field.Grid.WalkableTiles[i].WorldCenter.y;

                low = Mathf.Min(low, y);
                high = Mathf.Max(high, y);
            }

            return high - low;
        }

        private static int CountBlocked(IslandGrid grid)
        {
            int blocked = 0;

            for (int i = 0; i < grid.AllTiles.Count; i++)
            {
                if (grid.AllTiles[i].Type == TileType.Cliff)
                {
                    blocked++;
                }
            }

            return blocked;
        }

        /// <summary>걸을 수 있는 칸 중 첫 칸에서 실제로 닿는 칸의 수입니다.</summary>
        private static int CountReachable(IslandGrid grid)
        {
            if (grid.WalkableTiles.Count == 0)
            {
                return 0;
            }

            var start = grid.WalkableTiles[0];
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

            return visited.Count;
        }
    }
}
