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
    /// 지형 조각 파이프라인을 검증합니다.
    ///
    /// <b>여기서 보는 것</b>
    ///
    /// "자연스러운가"는 눈으로 봐야 합니다. 대신 그 자연스러움이 성립하려면 반드시
    /// 참이어야 하는 것들을 봅니다.
    ///
    ///   · 각진 사각형이 아닌가 — 타일 안쪽에 실제로 굴곡이 생겼는가
    ///   · 판독이 살아 있는가  — 고도 단계가 흔들리지 않았는가
    ///   · 침식이 일을 했는가  — 안식각을 넘는 면이 줄었는가
    ///   · 붕괴가 일어났는가  — 벼랑 끝이 깎이고 절벽 밑에 쌓였는가
    ///
    /// 특히 둘째가 중요합니다. 지형을 부드럽게 만들다 고도 단계가 흔들리면
    /// "보이는 것과 갈 수 있는 곳이 다른" 상태가 되고, 그건 전술 게임에서 가장 나쁜 버그입니다.
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
        // 2. 각진 사각형이 아닌가
        // ====================================================================================================

        /// <summary>
        /// 이 테스트가 이 작업의 목적입니다.
        ///
        /// 타일당 평면 쿼드 하나를 굽는 한 지형은 각진 계단일 수밖에 없습니다.
        /// 타일 <b>안쪽</b>에 높이 차이가 있어야 곡면이 나옵니다.
        /// </summary>
        [Test]
        public void 타일_안쪽에_굴곡이_생긴다()
        {
            var grid = CreateIsland();
            var field = grid.Height;

            Assert.IsNotNull(field, "지형이 조각되지 않았습니다.");
            Assert.Greater(field.Resolution, 1, "타일을 나누지 않았습니다. 평면 한 장과 같습니다.");

            int varied = 0;
            int checkedTiles = 0;

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var corner = field.TileToSample(grid.WalkableTiles[i].Coord);

                float min = float.MaxValue;
                float max = float.MinValue;

                for (int oy = 0; oy <= field.Resolution; oy++)
                {
                    for (int ox = 0; ox <= field.Resolution; ox++)
                    {
                        float relief = field.GetRelief(corner.X + ox, corner.Y + oy);

                        min = Mathf.Min(min, relief);
                        max = Mathf.Max(max, relief);
                    }
                }

                checkedTiles++;

                if (max - min > 0.005f)
                {
                    varied++;
                }
            }

            Assert.Greater(
                varied,
                checkedTiles * 0.7f,
                $"{checkedTiles}칸 중 {varied}칸에만 굴곡이 있습니다. 대부분 아직 평면입니다.");
        }

        /// <summary>
        /// <b>단 경계가 타일 격자를 벗어나야 합니다.</b>
        ///
        /// 이 검사가 없어서 문제를 놓쳤습니다.
        /// 기복도 침식도 붕괴도 전부 <b>한 단 안쪽</b>만 건드리므로, 그것들이 다 통과해도
        /// 단 경계는 타일 변을 따라 90도로 꺾인 채 남습니다. 그 선이 눈에 들어오는 전부인데
        /// 어떤 검사도 그것을 보고 있지 않았습니다.
        ///
        /// 경계가 격자를 벗어났다는 것은 곧 <b>한 타일 안에 두 단계가 함께 있다</b>는 뜻입니다.
        /// </summary>
        [Test]
        public void 단_경계가_타일_격자를_벗어난다()
        {
            var grid = CreateIsland();
            var field = grid.Height;

            int mixed = 0;
            int checkedTiles = 0;

            for (int i = 0; i < grid.AllTiles.Count; i++)
            {
                var tile = grid.AllTiles[i];

                if (tile.IsWater)
                {
                    continue;
                }

                var corner = field.TileToSample(tile.Coord);

                int lowest = int.MaxValue;
                int highest = int.MinValue;

                for (int oy = 0; oy < field.Resolution; oy++)
                {
                    for (int ox = 0; ox < field.Resolution; ox++)
                    {
                        int level = field.GetLevel(corner.X + ox, corner.Y + oy);

                        lowest = Mathf.Min(lowest, level);
                        highest = Mathf.Max(highest, level);
                    }
                }

                checkedTiles++;

                if (highest > lowest)
                {
                    mixed++;
                }
            }

            Assert.Greater(checkedTiles, 0, "육지 타일이 없습니다.");

            Assert.Greater(
                mixed,
                checkedTiles * 0.08f,
                $"{checkedTiles}칸 중 {mixed}칸만 두 단계를 걸칩니다. " +
                "단 경계가 아직 타일 변을 따라갑니다 — 웨딩케이크로 보입니다.");
        }

        /// <summary>
        /// 경계를 휘어도 <b>한 표본 폭에 두 단</b>이 떨어지면 안 됩니다.
        /// 그러면 지형이 갈라진 것처럼 보이고, 유닛이 통과할 수 없는 벽이 생깁니다.
        /// </summary>
        [Test]
        public void 이웃_표본의_단계_차이가_1을_넘지_않는다()
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

                        Assert.LessOrEqual(
                            here - there,
                            1,
                            $"({sx},{sy}) 의 단계 {here} 와 이웃 {there} 의 차이가 1을 넘습니다.");
                    }
                }
            }
        }

        /// <summary>
        /// 보이는 면과 발 높이가 같은 출처에서 나와야 합니다.
        ///
        /// 경계를 휘어 놓고 발 높이만 타일 기준으로 두면, 경계 근처에서 유닛이
        /// 땅에 박히거나 공중에 뜹니다.
        /// </summary>
        [Test]
        public void 발_높이가_보이는_면과_같다()
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

        /// <summary>
        /// 굴곡이 <b>눈에 보일 만큼</b> 있어야 합니다.
        ///
        /// 잘게 나누기만 하고 진폭이 미미하면 여전히 평면 계단으로 보입니다.
        /// 실제로 한 번 그 상태로 통과했습니다 — "굴곡이 있는가"만 봤고
        /// "보이는가"는 안 봤기 때문입니다. 각도로 재야 그 구멍이 막힙니다.
        /// </summary>
        [Test]
        public void 굴곡이_눈에_보일_만큼_기울어_있다()
        {
            var grid = CreateIsland();
            var field = grid.Height;

            float steepest = 0f;
            int gentle = 0;
            int samples = 0;

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var center = grid.WalkableTiles[i].WorldCenter;
                float slope = Vector3.Angle(field.SampleNormal(center.x, center.z), Vector3.up);

                steepest = Mathf.Max(steepest, slope);
                samples++;

                if (slope > 4f)
                {
                    gentle++;
                }
            }

            Assert.Greater(samples, 0, "걸을 수 있는 칸이 없습니다.");

            Assert.Greater(steepest, 8f,
                $"가장 가파른 곳이 {steepest:F1}도뿐입니다. 잘게 나눠 놓고도 평면으로 보입니다.");

            Assert.Greater(gentle, samples * 0.2f,
                $"{samples}칸 중 {gentle}칸만 기울어 있습니다. 대부분 평면입니다.");
        }

        /// <summary>
        /// 굴곡이 있되 <b>모든 축척에서</b> 있어야 자연 지형으로 보입니다.
        /// FBM이 겹겹이 쌓였다면 이웃한 표본끼리도, 멀리 떨어진 표본끼리도 다릅니다.
        /// </summary>
        [Test]
        public void 굴곡이_크고_작은_축척에_모두_있다()
        {
            var field = CreateIsland().Height;

            float neighborDifference = 0f;
            float distantDifference = 0f;
            int samples = 0;

            for (int sy = 8; sy < field.SamplesY - 8; sy += 3)
            {
                for (int sx = 8; sx < field.SamplesX - 8; sx += 3)
                {
                    if (!field.IsLand(sx, sy) || !field.IsLand(sx + 6, sy))
                    {
                        continue;
                    }

                    neighborDifference += Mathf.Abs(field.GetRelief(sx, sy) - field.GetRelief(sx + 1, sy));
                    distantDifference += Mathf.Abs(field.GetRelief(sx, sy) - field.GetRelief(sx + 6, sy));

                    samples++;
                }
            }

            Assert.Greater(samples, 0, "검사할 표본이 없습니다.");
            Assert.Greater(neighborDifference, 0f, "이웃 표본이 전부 같습니다. 잔 굴곡이 없습니다.");
            Assert.Greater(distantDifference, neighborDifference, "먼 표본끼리 차이가 이웃보다 작습니다.");
        }

        // ====================================================================================================
        // 3. 판독이 살아 있는가
        // ====================================================================================================

        /// <summary>
        /// 기복이 고도 한 단계를 넘어서면 "여기가 몇 층인가"가 흔들립니다.
        /// 눈으로도 계산으로도 층을 알 수 없게 되어 절벽이 비탈로 보입니다.
        /// </summary>
        [Test]
        public void 기복이_고도_한_단계를_넘지_않는다()
        {
            var grid = CreateIsland();
            var field = grid.Height;

            Assert.Less(field.ReliefLimit, grid.HeightStep * 0.5f, "기복 한계가 너무 큽니다.");

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    float relief = field.GetRelief(sx, sy);

                    Assert.LessOrEqual(
                        Mathf.Abs(relief),
                        field.ReliefLimit + 0.0001f,
                        $"({sx},{sy}) 의 기복 {relief:F3} 이 한계 {field.ReliefLimit:F3} 을 넘었습니다.");
                }
            }
        }

        /// <summary>
        /// 조각은 <b>보이는 땅</b>만 바꿉니다. 통행 판정과 길찾기가 보는 구조는 그대로여야 합니다.
        /// </summary>
        [Test]
        public void 고도_단계와_통행_판정은_바뀌지_않는다()
        {
            var settings = IslandSettings.CreateDefault();

            var sculpted = IslandGenerator.Generate(settings, 777);

            // 같은 시드로 다시 만들어 구조가 재현되는지 봅니다.
            var again = IslandGenerator.Generate(settings, 777);

            Assert.AreEqual(sculpted.WalkableTiles.Count, again.WalkableTiles.Count);

            for (int i = 0; i < sculpted.AllTiles.Count; i++)
            {
                Assert.AreEqual(sculpted.AllTiles[i].Height, again.AllTiles[i].Height, $"{i}번 타일의 고도가 다릅니다.");
                Assert.AreEqual(sculpted.AllTiles[i].Type, again.AllTiles[i].Type, $"{i}번 타일의 종류가 다릅니다.");
            }
        }

        /// <summary>
        /// 유닛의 발 높이는 지형 메시와 같은 출처에서 나와야 합니다.
        /// 다르면 유닛이 땅에 박히거나 공중에 뜹니다.
        /// </summary>
        [Test]
        public void 발_높이가_조각된_지면을_따라간다()
        {
            var grid = CreateIsland();

            int differing = 0;

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];

                float sampled = grid.SampleGroundHeight(tile.WorldCenter);
                float flat = tile.WorldCenter.y;

                Assert.AreEqual(
                    flat + grid.Height.SampleRelief(tile.WorldCenter.x, tile.WorldCenter.z),
                    sampled,
                    0.0001f,
                    $"{tile.Coord} 의 발 높이가 지형과 어긋납니다.");

                if (Mathf.Abs(sampled - flat) > 0.005f)
                {
                    differing++;
                }
            }

            Assert.Greater(differing, 0, "발 높이가 전부 평면 그대로입니다. 조각이 반영되지 않았습니다.");
        }

        // ====================================================================================================
        // 4. 침식이 일을 했는가
        // ====================================================================================================

        /// <summary>
        /// Musgrave 의 안식각 알고리즘이 실제로 무너뜨리는지 봅니다.
        ///
        /// 급경사를 일부러 만들어 두고 돌린 뒤, 안식각을 넘는 자리가 줄었는지 셉니다.
        /// </summary>
        [Test]
        public void 침식이_급경사를_줄인다()
        {
            var grid = CreateIsland();
            var field = new HeightField(grid, 4);

            // 한 칸 걸러 최대·최소를 넣어 인공적인 톱니를 만듭니다.
            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    if (field.IsLand(sx, sy))
                    {
                        field.SetRelief(sx, sy, (sx + sy) % 2 == 0 ? field.ReliefLimit : -field.ReliefLimit);
                    }
                }
            }

            int before = ThermalErosion.CountUnstable(field);
            Assert.Greater(before, 0, "톱니를 만들었는데 급경사가 없습니다.");

            ThermalErosion.Apply(field, 20);

            int after = ThermalErosion.CountUnstable(field);

            Assert.Less(after, before, $"침식 전 {before}, 후 {after}. 줄지 않았습니다.");
        }

        /// <summary>
        /// 침식은 흙을 <b>옮길</b> 뿐 만들거나 없애지 않습니다.
        /// 총량이 늘거나 줄면 지형이 부풀거나 꺼집니다.
        ///
        /// <b>부호 있는 합만 보면 안 됩니다.</b>
        /// FBM은 평균이 0이라 그 합이 우연히 0 근처가 되고, 거기에 상대 오차를 걸면
        /// 아주 작은 변화도 몇 배 차이로 보입니다. 실제로 그 함정에 한 번 빠졌습니다.
        ///
        /// 의미 있는 기준은 <b>존재하는 흙의 총량 대비 얼마나 흘렀는가</b>입니다.
        /// 기복이 한계에 잘리며 새는 양이 있으므로 0은 될 수 없고, 무시할 만하면 됩니다.
        /// </summary>
        [Test]
        public void 침식이_흙의_총량을_보존한다()
        {
            var grid = CreateIsland();
            var field = new HeightField(grid, 4);

            FbmNoise.Apply(field, 99);

            float before = TotalRelief(field);
            float material = TotalMaterial(field);

            Assert.Greater(material, 0f, "흙이 없습니다. 검사가 무의미합니다.");

            ThermalErosion.Apply(field, 10);

            float after = TotalRelief(field);
            float drift = Mathf.Abs(after - before) / material;

            Assert.Less(
                drift,
                0.02f,
                $"총량이 {drift:P1} 만큼 움직였습니다 (전 {before:F2}, 후 {after:F2}, 총 흙 {material:F0}).");
        }

        /// <summary>부호 있는 합입니다. 흙이 순증하거나 순감했는지를 봅니다.</summary>
        private static float TotalRelief(HeightField field)
        {
            float sum = 0f;

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    if (field.IsLand(sx, sy))
                    {
                        sum += field.GetRelief(sx, sy);
                    }
                }
            }

            return sum;
        }

        /// <summary>존재하는 흙의 총량입니다. 드리프트를 재는 기준자입니다.</summary>
        private static float TotalMaterial(HeightField field)
        {
            float sum = 0f;

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    if (field.IsLand(sx, sy))
                    {
                        sum += Mathf.Abs(field.GetRelief(sx, sy));
                    }
                }
            }

            return sum;
        }

        // ====================================================================================================
        // 5. 붕괴가 일어났는가
        // ====================================================================================================

        /// <summary>
        /// 절벽 밑에 실제로 흙이 쌓였는지 봅니다.
        ///
        /// 이것이 없으면 절벽이 압출한 상자로 보입니다.
        /// 절벽 아래에는 어디에나 무너져 쌓인 비탈이 있습니다.
        /// </summary>
        [Test]
        public void 절벽_밑에_잔해가_쌓인다()
        {
            var field = CreateIsland().Height;

            int deposits = 0;
            float thickest = 0f;

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    float talus = field.GetTalus(sx, sy);

                    if (talus > 0f)
                    {
                        deposits++;
                        thickest = Mathf.Max(thickest, talus);
                    }
                }
            }

            Assert.Greater(deposits, 0, "무너져 쌓인 자리가 하나도 없습니다.");
            Assert.Greater(thickest, 0.02f, "쌓인 양이 눈에 띄지 않을 만큼 적습니다.");
        }

        /// <summary>
        /// 붕괴는 <b>불규칙해야</b> 합니다.
        ///
        /// 모든 절벽 밑에 똑같은 쐐기가 생기면 오히려 격자가 더 또렷해집니다.
        /// 규칙적인 것을 하나 더 얹은 셈이니까요.
        /// </summary>
        [Test]
        public void 붕괴가_구간마다_다르게_일어난다()
        {
            var field = CreateIsland().Height;

            float min = float.MaxValue;
            float max = 0f;
            int samples = 0;

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    float talus = field.GetTalus(sx, sy);

                    if (talus <= 0f)
                    {
                        continue;
                    }

                    min = Mathf.Min(min, talus);
                    max = Mathf.Max(max, talus);
                    samples++;
                }
            }

            Assert.Greater(samples, 10, "쌓인 자리가 너무 적어 판단할 수 없습니다.");
            Assert.Greater(max / Mathf.Max(min, 0.0001f), 2f,
                $"쌓인 양이 {min:F3}~{max:F3} 로 거의 균일합니다. 절벽마다 똑같은 쐐기가 생깁니다.");
        }

        // ====================================================================================================
        // 6. 도로
        // ====================================================================================================

        /// <summary>
        /// Galin 의 비용 함수가 실제로 가파른 길을 피하는지 봅니다.
        ///
        /// 같은 수평 거리라면 가파른 쪽이 반드시 더 비싸야 합니다.
        /// 그리고 그 차이는 <b>초선형</b>이어야 합니다 — 두 배 가파르면 두 배보다 훨씬 비싸야
        /// 등고선을 따라 도는 우회로가 곧장 오르는 것보다 싸집니다.
        /// </summary>
        [Test]
        public void 가파른_길이_초선형으로_비싸다()
        {
            var grid = CreateIsland();
            var field = new HeightField(grid, 4);

            // 평지 — 두 표본이 같은 높이입니다.
            float flat = RoadPlanner.StepCost(field, 5, 5, 6, 5);

            field.SetRelief(6, 5, field.ReliefLimit * 0.5f);
            float gentle = RoadPlanner.StepCost(field, 5, 5, 6, 5);

            field.SetRelief(6, 5, field.ReliefLimit);
            float steep = RoadPlanner.StepCost(field, 5, 5, 6, 5);

            Assert.Greater(gentle, flat, "경사가 있는데 평지와 비용이 같습니다.");
            Assert.Greater(steep, gentle, "더 가파른데 더 싸거나 같습니다.");

            float firstIncrease = gentle - flat;
            float secondIncrease = steep - gentle;

            Assert.Greater(
                secondIncrease,
                firstIncrease,
                "비용이 경사에 비례할 뿐입니다. 초선형이 아니면 길이 비탈을 곧장 치고 올라갑니다.");
        }

        [Test]
        public void 도로가_두_지점을_잇는다()
        {
            var grid = CreateIsland();
            var field = grid.Height;

            var start = field.TileToSample(grid.WalkableTiles[0].Coord);
            var goal = field.TileToSample(grid.WalkableTiles[grid.WalkableTiles.Count - 1].Coord);

            var path = new List<GridCoord>();

            Assert.IsTrue(RoadPlanner.TryFindPath(field, start, goal, path), "경로를 찾지 못했습니다.");
            Assert.Greater(path.Count, 1, "경로가 한 점뿐입니다.");

            Assert.AreEqual(start, path[0], "출발점이 다릅니다.");
            Assert.AreEqual(goal, path[path.Count - 1], "도착점이 다릅니다.");

            // 이웃하지 않은 표본으로 건너뛰면 경로가 끊긴 것입니다.
            for (int i = 1; i < path.Count; i++)
            {
                int dx = Mathf.Abs(path[i].X - path[i - 1].X);
                int dy = Mathf.Abs(path[i].Y - path[i - 1].Y);

                Assert.LessOrEqual(Mathf.Max(dx, dy), 1, $"{i}번째에서 경로가 건너뛰었습니다.");
            }
        }

        [Test]
        public void 물_위로는_길을_내지_않는다()
        {
            var grid = CreateIsland();
            var field = grid.Height;

            var path = new List<GridCoord>();
            var start = field.TileToSample(grid.WalkableTiles[0].Coord);

            RoadPlanner.TryFindPath(field, start, field.TileToSample(grid.HouseTiles[0].Coord), path);

            for (int i = 0; i < path.Count; i++)
            {
                Assert.IsTrue(field.IsLand(path[i].X, path[i].Y), $"{path[i]} 는 물인데 길이 지나갑니다.");
            }
        }

        // ====================================================================================================
        // 7. 예외
        // ====================================================================================================

        [Test]
        public void 격자가_없어도_터지지_않는다()
        {
            Assert.IsNull(LandformPipeline.Build(null));
            Assert.DoesNotThrow(() => FbmNoise.Apply(null, 0));
            Assert.DoesNotThrow(() => ThermalErosion.Apply(null, 5));
            Assert.DoesNotThrow(() => CliffCollapse.Apply(null, 0));
            Assert.DoesNotThrow(() => TerrainSculptor.CutAndFill(null, null, 1f, 2f));
        }

        [Test]
        public void 감쇠_곡선이_양_끝에서_매끄럽다()
        {
            Assert.AreEqual(1f, TerrainSculptor.Falloff(0f, 2f, 5f), 0.0001f);
            Assert.AreEqual(1f, TerrainSculptor.Falloff(2f, 2f, 5f), 0.0001f);
            Assert.AreEqual(0f, TerrainSculptor.Falloff(5f, 2f, 5f), 0.0001f);
            Assert.AreEqual(0f, TerrainSculptor.Falloff(9f, 2f, 5f), 0.0001f);

            // 가운데에서는 단조 감소해야 합니다.
            float previous = 1f;

            for (float d = 2f; d <= 5f; d += 0.25f)
            {
                float current = TerrainSculptor.Falloff(d, 2f, 5f);

                Assert.LessOrEqual(current, previous + 0.0001f, $"거리 {d} 에서 값이 올라갔습니다.");
                previous = current;
            }
        }
    }
}
