using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Systems.Grid;
using SRPG.Systems.Props;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 지형지물 배치를 검증합니다.
    ///
    /// <b>여기서 보는 것은 "예쁜가"가 아닙니다.</b>
    /// 그건 눈으로 봐야 합니다. 대신 눈으로 보면 <b>놓치기 쉬운 것</b>을 봅니다.
    ///
    ///   · 격자가 드러나는가 — 중심에 몰려 있지 않은가, 크기가 다 같지 않은가
    ///   · 판독성을 깨는가 — 걸을 수 있는 땅에 사람 키만 한 바위가 서 있지 않은가
    ///   · 같은 섬이 매번 달라지는가 — 결정론이 깨지면 저장 없이 복원할 수 없습니다
    ///
    /// 특히 첫째가 중요합니다. 화면을 보면 "뭔가 인위적이다"까지는 느끼는데
    /// 그 원인이 <b>모든 프롭이 칸 중심에 있어서</b>라는 것은 좀처럼 눈에 띄지 않습니다.
    /// </summary>
    public sealed class PropPlacementTests
    {
        // ====================================================================================================
        // 1. Setup
        // ====================================================================================================

        private const int Seed = 12345;

        private static IslandGrid CreateIsland(int seed = Seed)
        {
            var settings = IslandSettings.CreateDefault();
            return IslandGenerator.Generate(settings, seed);
        }

        private static List<PropInstance> Place(IslandGrid grid, float density = 1f)
        {
            var results = new List<PropInstance>();
            PropPlacement.Generate(grid, density, results);

            return results;
        }

        // ====================================================================================================
        // 2. 격자가 드러나지 않는가
        // ====================================================================================================

        /// <summary>
        /// 전부 칸 중심에 놓이면 지형이 아니라 격자가 보입니다.
        /// </summary>
        [Test]
        public void 칸_중심에_놓이지_않는다()
        {
            var grid = CreateIsland();
            var props = Place(grid);

            Assert.Greater(props.Count, 0, "지형지물이 하나도 놓이지 않았습니다.");

            int centered = 0;

            for (int i = 0; i < props.Count; i++)
            {
                var tile = grid.GetTile(grid.WorldToCoord(props[i].GroundPosition));
                if (tile == null)
                {
                    continue;
                }

                Vector3 delta = props[i].GroundPosition - tile.WorldCenter;
                delta.y = 0f;

                if (delta.magnitude < grid.CellSize * 0.02f)
                {
                    centered++;
                }
            }

            Assert.Less(
                centered,
                props.Count * 0.05f,
                $"{props.Count}개 중 {centered}개가 칸 중심에 붙어 있습니다. 격자가 드러납니다.");
        }

        /// <summary>
        /// 크기가 다 같으면 도장을 찍은 것으로 보입니다.
        /// 게다가 <b>큰 것이 흔하면</b> 지형이 아니라 창고처럼 보입니다. 큰 것은 드물어야 합니다.
        /// </summary>
        [Test]
        public void 크기가_작은_쪽에_몰리고_큰_것은_드물다()
        {
            var props = Place(CreateIsland());

            float min = float.MaxValue;
            float max = 0f;
            float sum = 0f;

            for (int i = 0; i < props.Count; i++)
            {
                min = Mathf.Min(min, props[i].Radius);
                max = Mathf.Max(max, props[i].Radius);
                sum += props[i].Radius;
            }

            Assert.Greater(max / min, 2f, $"크기 편차가 {max / min:F1}배뿐입니다. 전부 비슷해 보입니다.");

            float average = sum / props.Count;
            float middle = (min + max) * 0.5f;

            Assert.Less(average, middle, "평균이 중간보다 큽니다. 큰 것이 너무 흔합니다.");
        }

        /// <summary>
        /// 방향이 같으면 복제된 것으로 보입니다.
        /// </summary>
        [Test]
        public void 방향이_고르게_흩어진다()
        {
            var props = Place(CreateIsland());

            // 방위를 8구간으로 나눠 전부 쓰이는지 봅니다.
            var used = new HashSet<int>();

            for (int i = 0; i < props.Count; i++)
            {
                float yaw = props[i].Rotation.eulerAngles.y;
                used.Add(Mathf.FloorToInt(yaw / 45f) % 8);
            }

            Assert.AreEqual(8, used.Count, $"방위가 {used.Count}종류뿐입니다.");
        }

        /// <summary>
        /// 완전히 수직이면 세워 둔 티가 납니다. 다만 넘어져 보여도 안 됩니다.
        /// </summary>
        [Test]
        public void 살짝_기울지만_넘어지지는_않는다()
        {
            var props = Place(CreateIsland());

            int tilted = 0;

            for (int i = 0; i < props.Count; i++)
            {
                float tilt = Vector3.Angle(props[i].Rotation * Vector3.up, Vector3.up);

                Assert.Less(tilt, 32f, $"{tilt:F1}도 기울었습니다. 넘어진 것처럼 보입니다.");

                if (tilt > 1f)
                {
                    tilted++;
                }
            }

            Assert.Greater(tilted, props.Count * 0.8f, "대부분이 똑바로 서 있습니다.");
        }

        /// <summary>
        /// 비탈에 선 것이 평지에 선 것보다 더 기울어야 합니다.
        ///
        /// 전부 수직으로 세우면 경사면에서 즉시 티가 납니다 — 땅은 기울었는데
        /// 그 위의 바위만 꼿꼿하면 붙여 놓은 것으로 보입니다.
        /// 다만 비탈과 완전히 나란해져도 안 됩니다. 무거운 바위는 눕지 않습니다.
        /// </summary>
        [Test]
        public void 비탈에_선_것이_더_기운다()
        {
            var grid = CreateIsland();
            var props = Place(grid);

            Assert.IsNotNull(grid.Height, "지형이 조각되지 않았습니다.");

            float flatTilt = 0f;
            int flatCount = 0;
            float slopeTilt = 0f;
            int slopeCount = 0;

            for (int i = 0; i < props.Count; i++)
            {
                var position = props[i].GroundPosition;

                float slope = Vector3.Angle(grid.Height.SampleNormal(position.x, position.z), Vector3.up);
                float tilt = Vector3.Angle(props[i].Rotation * Vector3.up, Vector3.up);

                if (slope < 3f)
                {
                    flatTilt += tilt;
                    flatCount++;
                }
                else if (slope > 6f)
                {
                    slopeTilt += tilt;
                    slopeCount++;
                }
            }

            Assert.Greater(flatCount, 0, "평지에 선 것이 없습니다.");
            Assert.Greater(slopeCount, 0, "비탈에 선 것이 없습니다.");

            Assert.Greater(
                slopeTilt / slopeCount,
                flatTilt / flatCount,
                "비탈에 선 것이 평지에 선 것보다 덜 기웁니다. 지표면을 따르지 않습니다.");
        }

        /// <summary>
        /// 고르게 흩으면 잡음으로 보입니다. 뭉칠 곳은 뭉쳐야 지형으로 읽힙니다.
        ///
        /// 칸당 개수의 분산이 균일 분포보다 커야 군집이 있는 것입니다.
        /// </summary>
        [Test]
        public void 고르게_흩어지지_않고_뭉친다()
        {
            var grid = CreateIsland();
            var props = Place(grid);

            var perTile = new Dictionary<Vector2Int, int>();

            for (int i = 0; i < props.Count; i++)
            {
                var coord = grid.WorldToCoord(props[i].GroundPosition);
                var key = new Vector2Int(coord.X, coord.Y);

                perTile.TryGetValue(key, out int count);
                perTile[key] = count + 1;
            }

            int crowded = 0;
            foreach (var pair in perTile)
            {
                if (pair.Value >= 2)
                {
                    crowded++;
                }
            }

            Assert.Greater(
                crowded,
                perTile.Count * 0.1f,
                $"둘 이상 몰린 칸이 {crowded}개뿐입니다. 군집이 생기지 않았습니다.");
        }

        // ====================================================================================================
        // 3. 판독성을 깨지 않는가
        // ====================================================================================================

        /// <summary>
        /// 걸을 수 있는 땅에 사람 키만 한 바위가 서 있으면 못 가는 곳으로 읽힙니다.
        /// 지형지물은 통행을 막지 않으므로, 그 순간 보이는 것과 갈 수 있는 곳이 어긋납니다.
        /// </summary>
        [Test]
        public void 걸을_수_있는_땅에는_무릎_아래만_놓는다()
        {
            var grid = CreateIsland();
            var props = Place(grid);

            // 유닛 키가 1입니다. 그 절반을 넘으면 실루엣을 가립니다.
            const float KneeHeight = 0.5f;

            for (int i = 0; i < props.Count; i++)
            {
                var tile = grid.GetTile(grid.WorldToCoord(props[i].GroundPosition));

                if (tile == null || !tile.IsWalkable)
                {
                    continue;
                }

                Assert.LessOrEqual(
                    props[i].Height,
                    KneeHeight,
                    $"걸을 수 있는 칸 {tile.Coord}에 높이 {props[i].Height:F2}짜리가 있습니다.");
            }
        }

        /// <summary>
        /// 물속 바위는 <b>해식애 바로 밑에만</b> 놓입니다.
        ///
        /// 섬의 실루엣은 격자가 가장 또렷하게 드러나는 선이라, 무너져 물에 잠긴 바위 몇 개가
        /// 그 선을 깨 줍니다. 다만 완만한 해변 앞바다에까지 놓으면 상륙 지점만 어지럽히고
        /// 섬 둘레에 테두리가 생겨 오히려 윤곽이 또렷해집니다.
        /// </summary>
        [Test]
        public void 물속_바위는_해식애_밑에만_놓인다()
        {
            var grid = CreateIsland();
            var props = Place(grid);

            int inWater = 0;

            for (int i = 0; i < props.Count; i++)
            {
                var tile = grid.GetTile(grid.WorldToCoord(props[i].GroundPosition));

                if (tile == null || !tile.IsWater)
                {
                    continue;
                }

                inWater++;

                bool nextToCliffFace = false;

                for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
                {
                    var neighbor = grid.GetTile(tile.Coord + GridCoord.Neighbors8[n]);

                    if (neighbor != null && !neighbor.IsWater && neighbor.Height > 0)
                    {
                        nextToCliffFace = true;
                        break;
                    }
                }

                Assert.IsTrue(nextToCliffFace, $"물 타일 {tile.Coord} 은 절벽 밑이 아닌데 바위가 있습니다.");
            }

            Assert.Less(
                inWater,
                props.Count * 0.15f,
                $"물속 바위가 {inWater}개로 너무 많습니다. 섬 둘레에 테두리가 생깁니다.");
        }

        /// <summary>
        /// 잔해는 손규칙이 아니라 <b>지형에서 읽어 낸 값</b>이어야 합니다.
        ///
        /// "절벽에 인접하면 돌을 놓는다"가 아니라 "여기는 완만한데 바로 위가 가파르다",
        /// 즉 <b>사면의 발치</b>인지를 봅니다. 그것이 실제로 돌이 굴러 모이는 자리입니다.
        /// 그래야 잔해가 왜 거기 있는지를 지형이 설명하게 됩니다.
        /// </summary>
        [Test]
        public void 사면의_발치를_잔해_자리로_읽어_낸다()
        {
            var grid = CreateIsland();
            var field = grid.Height;

            Assert.IsNotNull(field, "지형이 만들어지지 않았습니다.");

            int deposits = 0;
            float strongest = 0f;

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    var world = field.SampleToWorld(sx, sy);
                    float talus = field.SampleTalus(world.x, world.y);

                    if (talus > 0f)
                    {
                        deposits++;
                        strongest = Mathf.Max(strongest, talus);
                    }
                }
            }

            Assert.Greater(deposits, 0, "사면의 발치를 하나도 찾지 못했습니다.");
            Assert.Greater(strongest, 0.1f, "잔해가 놓일 만큼 뚜렷한 발치가 없습니다.");
        }

        /// <summary>
        /// 각진 바위와 침식된 둔덕이 <b>한 섬에 같이</b> 있어야 지형이 시간을 겪은 것으로 보입니다.
        /// 한쪽만 있으면 인공물이거나 밋밋한 언덕입니다.
        /// </summary>
        [Test]
        public void 각진_것과_침식된_것이_공존한다()
        {
            var props = Place(CreateIsland());

            int angular = 0;
            int eroded = 0;

            for (int i = 0; i < props.Count; i++)
            {
                if (props[i].Weathering < 0.4f)
                {
                    angular++;
                }
                else if (props[i].Weathering > 0.75f)
                {
                    eroded++;
                }
            }

            Assert.Greater(angular, 0, "각진 바위가 하나도 없습니다.");
            Assert.Greater(eroded, 0, "침식된 둔덕이 하나도 없습니다.");
        }

        /// <summary>
        /// 암반과 지표가 섞여야 합니다.
        /// 암반은 측면과 같은 재질이라 <b>못 딛는 것</b>으로, 지표는 윗면과 같아 <b>땅의 일부</b>로 읽힙니다.
        /// </summary>
        [Test]
        public void 암반과_지표가_모두_나온다()
        {
            var props = Place(CreateIsland());

            int rock = 0;
            for (int i = 0; i < props.Count; i++)
            {
                if (props[i].IsRock)
                {
                    rock++;
                }
            }

            Assert.Greater(rock, 0, "암반 지형지물이 없습니다.");
            Assert.Less(rock, props.Count, "지표 지형지물이 없습니다.");
        }

        // ====================================================================================================
        // 4. 결정론
        // ====================================================================================================

        /// <summary>
        /// 시드가 같으면 결과가 같아야 합니다. 그래야 저장하지 않고 다시 만들 수 있습니다.
        /// </summary>
        [Test]
        public void 같은_섬이면_같은_배치가_나온다()
        {
            var first = Place(CreateIsland());
            var second = Place(CreateIsland());

            Assert.AreEqual(first.Count, second.Count, "개수부터 다릅니다.");

            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].GroundPosition, second[i].GroundPosition, $"{i}번째 자리가 다릅니다.");
                Assert.AreEqual(first[i].Shape, second[i].Shape, $"{i}번째 형상이 다릅니다.");
            }
        }

        [Test]
        public void 다른_섬이면_다른_배치가_나온다()
        {
            var first = Place(CreateIsland(1111));
            var second = Place(CreateIsland(2222));

            bool identical = first.Count == second.Count;

            for (int i = 0; identical && i < first.Count; i++)
            {
                if (first[i].GroundPosition != second[i].GroundPosition)
                {
                    identical = false;
                }
            }

            Assert.IsFalse(identical, "시드가 다른데 배치가 같습니다.");
        }

        // ====================================================================================================
        // 5. 예외
        // ====================================================================================================

        [Test]
        public void 밀도가_0이면_아무것도_놓지_않는다()
        {
            Assert.AreEqual(0, Place(CreateIsland(), density: 0f).Count);
        }

        [Test]
        public void 격자가_없어도_터지지_않는다()
        {
            var results = new List<PropInstance> { default };

            Assert.DoesNotThrow(() => PropPlacement.Generate(null, 1f, results));
            Assert.AreEqual(0, results.Count, "격자가 없는데 이전 결과가 남아 있습니다.");
        }
    }
}
