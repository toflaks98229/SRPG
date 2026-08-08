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
    /// 그리고 그 결과를 <b>계단식 대지</b>로 다집니다. 평평한 상판과 가파른 절벽면이
    /// 쌓인 형태입니다 — 이 게임의 룩은 자연 그대로가 아니라 쌓인 판입니다.
    ///
    /// 그래서 여기서 보는 것은 이렇습니다.
    ///   · 상판이 완전히 평평한가   — 판으로 보이려면 기울면 안 됩니다
    ///   · 윤곽이 격자를 벗어나는가 — 경계가 셀을 가로질러야 계단이 아닙니다
    ///   · 물이 실제로 골을 팠는가  — 판의 배치를 침식이 정합니다
    ///   · 딛을 평지가 충분한가     — 온통 절벽이면 부대가 설 자리가 없습니다
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
        /// <b>상판은 완전히 평평해야 합니다.</b>
        ///
        /// 이 게임의 룩은 쌓인 판입니다. 상판이 조금이라도 기울면 판으로 안 보이고,
        /// 그 위에 선 부대의 대열도 흐트러져 보입니다.
        /// 자연스러움은 판의 <b>윤곽</b>이 맡습니다 — 그건 침식이 정합니다.
        /// </summary>
        [Test]
        public void 상판이_완전히_평평하다()
        {
            var field = CreateIsland().Height;

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    if (!field.IsLand(sx, sy))
                    {
                        continue;
                    }

                    // 높이는 언제나 단의 정수배여야 합니다.
                    float height = field.GetSurface(sx, sy);
                    float band = field.GetLevel(sx, sy) * field.HeightStep;

                    Assert.AreEqual(band, height, 0.0001f, $"({sx},{sy}) 의 상판이 단 높이에서 벗어났습니다.");
                }
            }
        }

        /// <summary>
        /// 이웃한 표본의 단 차이가 1을 넘으면 안 됩니다.
        ///
        /// 그리기가 한 셀 안에서 두 종류의 높이만 다루도록 만들어 둔 약속입니다.
        /// 어기면 마칭 스퀘어가 표현할 수 없는 형태가 나옵니다.
        /// </summary>
        [Test]
        public void 이웃_표본의_단_차이가_1을_넘지_않는다()
        {
            var field = CreateIsland().Height;

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    if (!field.IsLand(sx, sy))
                    {
                        continue;
                    }

                    int here = field.GetLevel(sx, sy);

                    for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                    {
                        int nx = sx + GridCoord.Neighbors4[n].X;
                        int ny = sy + GridCoord.Neighbors4[n].Y;

                        if (!field.IsInside(nx, ny))
                        {
                            continue;
                        }

                        int there = field.IsLand(nx, ny) ? field.GetLevel(nx, ny) : 0;

                        Assert.LessOrEqual(here - there, 1,
                            $"({sx},{sy}) 의 단 {here} 와 이웃 {there} 의 차이가 1을 넘습니다.");
                    }
                }
            }
        }

        /// <summary>
        /// <b>단 경계가 격자를 따라가면 안 됩니다.</b>
        ///
        /// 경계를 셀 변을 따라 그리면 윤곽이 축에 정렬된 계단이 됩니다.
        /// 셀을 아무리 잘게 나눠도 계단은 계단이고, 눈은 그 규칙을 즉시 찾아냅니다.
        ///
        /// 마칭 스퀘어는 경계를 셀 <b>안쪽으로 가로질러</b> 긋습니다.
        /// 그 대각선이 실제로 생기려면 한 셀 안에 두 단이 함께 있어야 합니다.
        /// </summary>
        [Test]
        public void 단_경계가_셀을_가로지른다()
        {
            var field = CreateIsland().Height;

            int mixed = 0;
            int boundary = 0;

            for (int sy = 0; sy < field.SamplesY - 1; sy++)
            {
                for (int sx = 0; sx < field.SamplesX - 1; sx++)
                {
                    if (!field.IsLand(sx, sy) || !field.IsLand(sx + 1, sy)
                        || !field.IsLand(sx, sy + 1) || !field.IsLand(sx + 1, sy + 1))
                    {
                        continue;
                    }

                    int a = field.GetLevel(sx, sy);
                    int b = field.GetLevel(sx + 1, sy);
                    int c = field.GetLevel(sx, sy + 1);
                    int e = field.GetLevel(sx + 1, sy + 1);

                    int low = Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, e));
                    int high = Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, e));

                    if (high == low)
                    {
                        continue;
                    }

                    boundary++;

                    // 네 모서리가 2:2 로 갈리지 않으면 대각선이 생깁니다.
                    int highCount = (a == high ? 1 : 0) + (b == high ? 1 : 0)
                                  + (c == high ? 1 : 0) + (e == high ? 1 : 0);

                    if (highCount == 1 || highCount == 3)
                    {
                        mixed++;
                    }
                }
            }

            Assert.Greater(boundary, 0, "단 경계가 하나도 없습니다.");
            Assert.Greater(mixed, boundary * 0.15f,
                $"경계 셀 {boundary}개 중 {mixed}개만 대각선을 만듭니다. 윤곽이 계단으로 남습니다.");
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

                    // 상판이 평평하므로 경사는 단 경계에만 있습니다.
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

                // 절벽은 낙차가 커도 됩니다. 오를 수 없는 면이니까요.
                if (!tile.IsWalkable)
                {
                    continue;
                }

                for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                {
                    var neighbor = grid.GetTile(tile.Coord + GridCoord.Neighbors4[n]);

                    if (neighbor == null || !neighbor.IsWalkable)
                    {
                        continue;
                    }

                    Assert.LessOrEqual(
                        Mathf.Abs(tile.Height - neighbor.Height),
                        1,
                        $"{tile.Coord}({tile.Height}) 와 걸을 수 있는 이웃({neighbor.Height}) 의 차이가 1을 넘습니다.");
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
            Assert.IsNull(LandformPipeline.BuildField(null, null, 4));
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
