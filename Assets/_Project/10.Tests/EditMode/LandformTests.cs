using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Systems.Grid;
using SRPG.Systems.Landform;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 지형 시뮬레이션을 검증합니다.
    ///
    /// <b>순서가 뒤집혔습니다</b>
    ///
    /// 예전에는 타일의 고도 단계가 먼저 있고 그 안에서 침식을 돌렸습니다.
    /// 이제는 물과 중력이 지형을 먼저 만들고, 타일이 그 결과를 읽습니다.
    ///
    /// 그래서 여기서 보는 것도 달라졌습니다.
    ///   · 물이 실제로 골을 팠는가 — 지류가 합쳐지는 위상이 나왔는가
    ///   · 직각이 남아 있지 않은가 — 안식각을 넘는 면이 없는가
    ///   · 딛을 평지가 있는가     — 다지기가 실제로 평면을 만들었는가
    ///   · 판독이 사는가         — 평지와 사면이 또렷이 갈리는가
    ///
    /// 특히 둘째가 이번 작업의 목적입니다.
    /// 수직 벽이 사라졌으므로 <b>지형 어디에도 90도가 없어야</b> 합니다.
    /// </summary>
    public sealed class LandformTests
    {
        // ====================================================================================================
        // 1. Setup
        // ====================================================================================================

        private static IslandGrid CreateIsland(int seed = 4242)
        {
            return IslandGenerator.Generate(IslandSettings.CreateDefault(), seed);
        }

        // ====================================================================================================
        // 2. 직각이 남아 있지 않은가
        // ====================================================================================================

        /// <summary>
        /// 이 테스트가 이번 작업의 목적입니다.
        ///
        /// 예전에는 단 사이를 수직 벽으로 이었습니다. 벽은 어디서 보든 90도이고
        /// 바닥과 만나는 자리도 90도입니다. 자연에 그런 면은 없습니다.
        ///
        /// 이제 단 사이는 침식이 남긴 사면이므로, 지형 어디에도 안식각을 크게 넘는
        /// 면이 있어서는 안 됩니다.
        /// </summary>
        [Test]
        public void 지형에_수직에_가까운_면이_없다()
        {
            var field = CreateIsland().Height;

            // 안식각 34도에 여유를 둡니다. tan(50도) = 1.19.
            const float NearVertical = 1.19f;

            float steepest = 0f;
            int violations = 0;

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    if (!field.IsLand(sx, sy))
                    {
                        continue;
                    }

                    float slope = field.GetSlope(sx, sy);
                    steepest = Mathf.Max(steepest, slope);

                    if (slope > NearVertical)
                    {
                        violations++;
                    }
                }
            }

            Assert.AreEqual(
                0,
                violations,
                $"수직에 가까운 면이 {violations}곳 있습니다. 가장 가파른 곳은 {Mathf.Atan(steepest) * Mathf.Rad2Deg:F1}도입니다.");
        }

        /// <summary>
        /// 사면의 밑동이 바닥과 <b>부드럽게</b> 만나야 합니다.
        ///
        /// 무너져 쌓인 흙(애추)이 거기 깔려 있기 때문입니다.
        /// 급경사 바로 아래가 곧바로 평지면 그건 무너지지 않은 인공 절벽입니다.
        /// </summary>
        [Test]
        public void 사면의_밑동에_비탈이_깔려_있다()
        {
            var field = CreateIsland();
            var height = field.Height;

            int feet = 0;
            int cushioned = 0;

            for (int sy = 2; sy < height.SamplesY - 2; sy++)
            {
                for (int sx = 2; sx < height.SamplesX - 2; sx++)
                {
                    if (!height.IsLand(sx, sy) || height.GetSlope(sx, sy) > 0.35f)
                    {
                        continue;
                    }

                    // 두 칸 위가 가파른 자리를 찾습니다. 그곳이 사면의 발치입니다.
                    bool underSlope = false;

                    for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
                    {
                        int nx = sx + GridCoord.Neighbors8[n].X * 2;
                        int ny = sy + GridCoord.Neighbors8[n].Y * 2;

                        if (height.IsLand(nx, ny)
                            && height.GetSlope(nx, ny) > 0.62f
                            && height.GetSurface(nx, ny) > height.GetSurface(sx, sy))
                        {
                            underSlope = true;
                            break;
                        }
                    }

                    if (!underSlope)
                    {
                        continue;
                    }

                    feet++;

                    // 바로 옆이 완전 평지가 아니라 살짝 기울어 있어야 합니다.
                    if (height.GetSlope(sx, sy) > 0.03f)
                    {
                        cushioned++;
                    }
                }
            }

            Assert.Greater(feet, 0, "사면의 발치를 찾지 못했습니다.");
            Assert.Greater(
                cushioned,
                feet * 0.6f,
                $"발치 {feet}곳 중 {cushioned}곳만 기울어 있습니다. 나머지는 직각으로 꺾입니다.");
        }

        // ====================================================================================================
        // 3. 물이 골을 팠는가
        // ====================================================================================================

        /// <summary>
        /// 고도가 해안 거리의 함수이면 계곡이 있을 수 없습니다.
        /// 물이 실제로 팠다면 같은 거리에 여러 높이가 존재합니다.
        /// </summary>
        [Test]
        public void 같은_해안_거리에_여러_높이가_있다()
        {
            var grid = CreateIsland();
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

            Assert.Greater(varied, 0, "모든 해안 거리에서 높이가 하나뿐입니다. 웨딩케이크입니다.");
        }

        /// <summary>
        /// 물이 판 골은 <b>주변보다 낮고 바다에서는 먼</b> 자리로 나타납니다.
        /// </summary>
        [Test]
        public void 골짜기가_생긴다()
        {
            var field = CreateIsland().Height;
            int hollows = 0;

            for (int sy = 3; sy < field.SamplesY - 3; sy++)
            {
                for (int sx = 3; sx < field.SamplesX - 3; sx++)
                {
                    if (!field.IsLand(sx, sy))
                    {
                        continue;
                    }

                    float here = field.GetSurface(sx, sy);

                    int higher = 0;

                    for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                    {
                        int nx = sx + GridCoord.Neighbors4[n].X * 3;
                        int ny = sy + GridCoord.Neighbors4[n].Y * 3;

                        if (field.IsLand(nx, ny) && field.GetSurface(nx, ny) > here + 0.05f)
                        {
                            higher++;
                        }
                    }

                    // 양옆이 솟아 있으면 골입니다.
                    if (higher >= 2)
                    {
                        hollows++;
                    }
                }
            }

            Assert.Greater(hollows, 0, "골짜기가 하나도 없습니다. 물이 파지 않았습니다.");
        }

        // ====================================================================================================
        // 4. 딛을 평지가 있는가
        // ====================================================================================================

        /// <summary>
        /// 온통 비탈이면 게임이 안 됩니다. 다지기가 실제로 평면을 만들어야 합니다.
        /// </summary>
        [Test]
        public void 딛을_수_있는_평지가_충분하다()
        {
            var field = CreateIsland().Height;

            int land = 0;
            int flat = 0;

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    if (!field.IsLand(sx, sy))
                    {
                        continue;
                    }

                    land++;

                    // tan(12도) = 0.21. 이보다 완만하면 서 있을 만합니다.
                    if (field.GetSlope(sx, sy) < 0.21f)
                    {
                        flat++;
                    }
                }
            }

            Assert.Greater(land, 0, "육지가 없습니다.");
            Assert.Greater(
                flat,
                land * 0.45f,
                $"육지 {land}곳 중 {flat}곳만 평지입니다. 온통 비탈이라 부대가 설 자리가 없습니다.");
        }

        /// <summary>
        /// 평지와 사면이 <b>또렷이 갈려야</b> 판독이 됩니다.
        ///
        /// 전부 어중간하게 기울어 있으면 어디가 딛는 곳인지 눈으로 알 수 없습니다.
        /// 완만한 쪽과 가파른 쪽에 몰리고 가운데가 적어야 합니다.
        /// </summary>
        [Test]
        public void 평지와_사면이_또렷이_갈린다()
        {
            var field = CreateIsland().Height;

            int flat = 0;
            int middle = 0;
            int steep = 0;

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    if (!field.IsLand(sx, sy))
                    {
                        continue;
                    }

                    float slope = field.GetSlope(sx, sy);

                    if (slope < 0.21f) flat++;
                    else if (slope < 0.62f) middle++;
                    else steep++;
                }
            }

            Assert.Greater(flat, 0, "평지가 없습니다.");
            Assert.Greater(steep, 0, "가파른 사면이 없습니다. 전부 밋밋합니다.");
            Assert.Less(middle, flat, "어중간한 경사가 평지보다 많습니다. 판독이 흐려집니다.");
        }

        // ====================================================================================================
        // 5. 게임 구조가 지켜지는가
        // ====================================================================================================

        /// <summary>
        /// 타일은 지형을 <b>읽어 냅니다</b>. 그 읽은 값이 통행 규칙을 지켜야 합니다.
        /// </summary>
        [Test]
        public void 인접_타일의_고도차가_1을_넘지_않는다()
        {
            var grid = CreateIsland();

            for (int i = 0; i < grid.AllTiles.Count; i++)
            {
                var tile = grid.AllTiles[i];

                if (tile.IsWater)
                {
                    continue;
                }

                for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                {
                    var neighbor = grid.GetTile(tile.Coord + GridCoord.Neighbors4[n]);
                    int neighborHeight = neighbor == null || neighbor.IsWater ? 0 : neighbor.Height;

                    Assert.LessOrEqual(
                        tile.Height - neighborHeight,
                        1,
                        $"{tile.Coord}({tile.Height}) 와 이웃({neighborHeight}) 의 차이가 1을 넘습니다.");
                }
            }
        }

        /// <summary>
        /// 발 높이는 그려지는 지형과 같은 출처여야 합니다.
        /// 다르면 유닛이 땅에 박히거나 뜹니다.
        /// </summary>
        [Test]
        public void 발_높이가_그려지는_지형과_같다()
        {
            var grid = CreateIsland();

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var center = grid.WalkableTiles[i].WorldCenter;

                Assert.AreEqual(
                    grid.Height.SampleSurface(center.x, center.z),
                    grid.SampleGroundHeight(center),
                    0.0001f,
                    $"{grid.WalkableTiles[i].Coord} 에서 발 높이가 지형과 어긋납니다.");
            }
        }

        [Test]
        public void 같은_시드면_같은_지형이_나온다()
        {
            var first = CreateIsland(777).Height;
            var second = CreateIsland(777).Height;

            for (int sy = 0; sy < first.SamplesY; sy += 5)
            {
                for (int sx = 0; sx < first.SamplesX; sx += 5)
                {
                    Assert.AreEqual(
                        first.GetSurface(sx, sy),
                        second.GetSurface(sx, sy),
                        0.0001f,
                        $"({sx},{sy}) 의 높이가 다릅니다.");
                }
            }
        }

        // ====================================================================================================
        // 6. 침식 자체
        // ====================================================================================================

        /// <summary>
        /// 물방울이 실제로 지형을 바꾸는지 봅니다.
        /// </summary>
        [Test]
        public void 물방울이_지형을_깎는다()
        {
            const int W = 40;
            const int D = 40;

            var height = new float[W * D];
            var land = new bool[W * D];

            for (int y = 0; y < D; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    land[y * W + x] = true;

                    // 한쪽으로 기운 비탈에 잔 요철을 얹습니다.
                    height[y * W + x] = (D - y) * 0.08f + Mathf.PerlinNoise(x * 0.3f, y * 0.3f) * 0.3f;
                }
            }

            var before = (float[])height.Clone();
            HydraulicErosion.Apply(height, W, D, land, 3000, 1234);

            float changed = 0f;

            for (int i = 0; i < height.Length; i++)
            {
                changed += Mathf.Abs(height[i] - before[i]);
            }

            Assert.Greater(changed, 1f, "물방울이 지형을 전혀 바꾸지 않았습니다.");
        }

        [Test]
        public void 침식이_지형을_무너뜨리지_않는다()
        {
            const int W = 30;
            const int D = 30;

            var height = new float[W * D];
            var land = new bool[W * D];

            for (int i = 0; i < height.Length; i++)
            {
                land[i] = true;
                height[i] = 2f;
            }

            HydraulicErosion.Apply(height, W, D, land, 2000, 99);

            for (int i = 0; i < height.Length; i++)
            {
                Assert.IsFalse(float.IsNaN(height[i]), $"{i}번 칸이 NaN입니다.");
                Assert.Less(Mathf.Abs(height[i] - 2f), 1f, $"{i}번 칸이 평지에서 크게 벗어났습니다.");
            }
        }

        // ====================================================================================================
        // 7. 도로
        // ====================================================================================================

        /// <summary>
        /// Galin 의 비용 함수가 가파른 길을 <b>초선형으로</b> 비싸게 매기는지 봅니다.
        /// 비례에 그치면 길이 비탈을 곧장 치고 올라갑니다.
        /// </summary>
        [Test]
        public void 가파른_길이_초선형으로_비싸다()
        {
            var simulation = new TerrainSimulation(10, 10, 2f);

            for (int i = 0; i < simulation.Land.Length; i++)
            {
                simulation.Land[i] = true;
            }

            float flat = RoadPlanner.StepCost(simulation, 5, 5, 6, 5);

            simulation.Height[5 * simulation.Width + 6] = 0.25f;
            float gentle = RoadPlanner.StepCost(simulation, 5, 5, 6, 5);

            simulation.Height[5 * simulation.Width + 6] = 0.5f;
            float steep = RoadPlanner.StepCost(simulation, 5, 5, 6, 5);

            Assert.Greater(gentle, flat, "경사가 있는데 평지와 비용이 같습니다.");
            Assert.Greater(steep - gentle, gentle - flat, "비용이 경사에 비례할 뿐입니다.");
        }

        // ====================================================================================================
        // 8. 예외
        // ====================================================================================================

        [Test]
        public void 격자가_없어도_터지지_않는다()
        {
            Assert.IsNull(LandformPipeline.Finish(null, null, 4));
            Assert.DoesNotThrow(() => HydraulicErosion.Apply(null, 0, 0, null, 10, 0));
            Assert.DoesNotThrow(() => TerrainFlattening.Apply(null, null, null, 0, 0, 1f, 1f, 4));
        }

        [Test]
        public void 감쇠_곡선이_양_끝에서_매끄럽다()
        {
            Assert.AreEqual(1f, TerrainSculptor.Falloff(0f, 2f, 5f), 0.0001f);
            Assert.AreEqual(1f, TerrainSculptor.Falloff(2f, 2f, 5f), 0.0001f);
            Assert.AreEqual(0f, TerrainSculptor.Falloff(5f, 2f, 5f), 0.0001f);
            Assert.AreEqual(0f, TerrainSculptor.Falloff(9f, 2f, 5f), 0.0001f);
        }

        // ====================================================================================================
        // 9. Helpers
        // ====================================================================================================

        private static int DistanceToWater(IslandGrid grid, Tile tile)
        {
            int best = int.MaxValue;

            for (int i = 0; i < grid.AllTiles.Count; i++)
            {
                if (grid.AllTiles[i].IsWater)
                {
                    best = Mathf.Min(best, GridCoord.ManhattanDistance(tile.Coord, grid.AllTiles[i].Coord));
                }
            }

            return best;
        }
    }
}
