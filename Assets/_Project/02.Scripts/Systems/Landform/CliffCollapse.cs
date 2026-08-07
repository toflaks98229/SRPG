using SRPG.Common;
using SRPG.Systems.Formation;
using UnityEngine;

namespace SRPG.Systems.Landform
{
    /// <summary>
    /// 3단계 보강 — 절벽의 붕괴. 층 경계를 넘는 물질 이동입니다.
    ///
    /// <b>왜 열침식만으로는 부족한가</b>
    ///
    /// <see cref="ThermalErosion"/>은 <b>기복만</b> 건드립니다. 고도 단계는 게임 규칙이라
    /// 손대지 않게 막아 두었기 때문입니다. 그래서 위층의 흙이 아래층으로 내려올 수가 없습니다.
    /// 붕괴의 핵심인 <b>층간 물질 이동</b>이 원천 차단되어 있는 셈입니다.
    ///
    /// 결과적으로 윗면은 부드러워지는데 <b>절벽 윤곽은 격자 직선 그대로</b> 남습니다.
    /// 위에서 내려다보는 게임에서 절벽선은 눈에 가장 먼저 들어오는 선이라, 거기가 각지면
    /// 나머지를 아무리 다듬어도 사각형 계단으로 보입니다.
    ///
    /// <b>여기서 하는 일</b>
    ///
    /// 고도 단계는 여전히 건드리지 않습니다. 대신 그 경계 <b>양쪽의 기복</b>을 옮깁니다.
    ///
    ///   · <b>벼랑 끝</b>은 깎입니다. 실제 절벽 윗면은 모서리가 닳아 둥급니다.
    ///     압출한 상자처럼 각진 모서리는 자연에 없습니다.
    ///   · <b>절벽 밑</b>에는 쌓입니다. 위에서 떨어진 것이 모여 완만한 비탈을 이룹니다.
    ///     이것이 애추(talus)이고, 절벽 아래 어디에나 있습니다.
    ///
    /// 깎인 양과 쌓인 양은 <b>낙차에 비례</b>합니다. 높은 절벽일수록 많이 무너집니다.
    ///
    /// <b>불규칙해야 합니다</b>
    ///
    /// 균일하게 깎고 쌓으면 모든 절벽 밑에 똑같은 쐐기가 생깁니다.
    /// 그러면 오히려 격자가 더 또렷해집니다 — 규칙적인 것을 더 얹은 셈이니까요.
    /// 그래서 잡음으로 변조합니다. 어떤 구간은 크게 무너지고, 어떤 구간은 거의 그대로입니다.
    /// 그 차이가 "저기가 무너졌구나"를 읽게 만듭니다.
    ///
    /// <b>잔해는 계산 결과에서 나옵니다</b>
    ///
    /// 쌓인 양을 하이트필드에 남겨 둡니다. 지형지물이 그 값을 읽어 바위를 놓습니다.
    /// "절벽에 인접하면 돌을 놓는다" 같은 손규칙이 아니라 <b>실제로 무너진 자리에</b>
    /// 무너진 만큼 놓이는 것이라, 잔해가 지형을 설명하게 됩니다.
    /// </summary>
    public static class CliffCollapse
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>벼랑 끝이 깎이는 최대 깊이입니다. 낙차 한 단에 대한 비율입니다.</summary>
        private const float BrinkErosionRatio = 0.22f;

        /// <summary>벼랑 끝에서 깎임이 미치는 거리입니다. 표본 단위입니다.</summary>
        private const float BrinkReach = 2.6f;

        /// <summary>절벽 밑에 쌓이는 최대 높이입니다. 낙차 한 단에 대한 비율입니다.</summary>
        private const float TalusRatio = 0.34f;

        /// <summary>비탈이 벽에서 뻗어 나가는 거리입니다. 표본 단위입니다.</summary>
        private const float TalusReach = 3.4f;

        /// <summary>잡음의 크기입니다. 작을수록 무너진 구간이 길게 이어집니다.</summary>
        private const float NoiseScale = 0.16f;

        /// <summary>붕괴 뒤 흙이 자리를 잡도록 돌리는 침식 반복 수입니다.</summary>
        private const int SettleIterations = 5;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 절벽 경계를 무너뜨립니다.
        /// </summary>
        /// <param name="field">지형입니다.</param>
        /// <param name="seed">불규칙화에 쓰는 씨앗입니다.</param>
        public static void Apply(HeightField field, int seed)
        {
            if (field == null)
            {
                return;
            }

            int count = field.SamplesX * field.SamplesY;

            // 벼랑 끝과 절벽 밑까지의 거리, 그리고 그 지점의 낙차입니다.
            var brinkDistance = new float[count];
            var brinkDrop = new float[count];
            var footDistance = new float[count];
            var footDrop = new float[count];

            FindEdges(field, brinkDistance, brinkDrop, footDistance, footDrop);

            Spread(field, brinkDistance, brinkDrop, BrinkReach);
            Spread(field, footDistance, footDrop, TalusReach);

            var random = new System.Random(seed);
            float offsetX = (float)random.NextDouble() * 800f + 0.29f;
            float offsetY = (float)random.NextDouble() * 800f + 0.83f;

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    if (!field.IsLand(sx, sy))
                    {
                        continue;
                    }

                    int i = field.Index(sx, sy);

                    // 구간마다 다르게 무너지도록 변조합니다. 0.35~1.0 사이를 씁니다.
                    // 0을 허용하면 아예 안 무너진 구간이 직선으로 남아 격자가 다시 드러납니다.
                    float variation = 0.35f + 0.65f * Mathf.PerlinNoise(
                        sx * NoiseScale + offsetX,
                        sy * NoiseScale + offsetY);

                    // 낙차가 크면 계산값이 기복 한계를 넘습니다.
                    // 그대로 두면 SetRelief 가 잘라 내면서 흙이 조용히 사라지거나 생깁니다.
                    // 여기서 미리 묶어야 총량이 보존됩니다.
                    float cap = field.ReliefLimit * 0.45f;

                    float cut = 0f;
                    if (brinkDrop[i] > 0f)
                    {
                        float falloff = Falloff(brinkDistance[i], BrinkReach);
                        cut = Mathf.Min(brinkDrop[i] * BrinkErosionRatio, cap) * falloff * variation;
                    }

                    float fill = 0f;
                    if (footDrop[i] > 0f)
                    {
                        float falloff = Falloff(footDistance[i], TalusReach);
                        fill = Mathf.Min(footDrop[i] * TalusRatio, cap) * falloff * variation;
                    }

                    if (cut == 0f && fill == 0f)
                    {
                        continue;
                    }

                    field.SetRelief(sx, sy, field.GetRelief(sx, sy) - cut + fill);

                    // 쌓인 양을 남겨 둡니다. 지형지물이 이 값을 읽어 잔해를 놓습니다.
                    if (fill > 0f)
                    {
                        field.SetTalus(sx, sy, fill / Mathf.Max(0.0001f, field.ReliefLimit));
                    }
                }
            }

            // 갓 쌓인 흙은 아직 안식각을 넘습니다. 한 번 더 무너뜨려 자리를 잡게 합니다.
            ThermalErosion.Apply(field, SettleIterations);
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 층 경계에 닿은 표본을 찾습니다.
        ///
        /// 기준 높이가 이웃보다 <b>높으면</b> 벼랑 끝, <b>낮으면</b> 절벽 밑입니다.
        /// 낙차는 가장 큰 차이를 씁니다. 높은 쪽이 더 많이 무너집니다.
        /// </summary>
        private static void FindEdges(
            HeightField field,
            float[] brinkDistance, float[] brinkDrop,
            float[] footDistance, float[] footDrop)
        {
            for (int i = 0; i < brinkDistance.Length; i++)
            {
                brinkDistance[i] = float.MaxValue;
                footDistance[i] = float.MaxValue;
            }

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    if (!field.IsLand(sx, sy))
                    {
                        continue;
                    }

                    float here = field.GetBase(sx, sy);

                    float above = 0f;
                    float below = 0f;

                    for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
                    {
                        int nx = sx + GridCoord.Neighbors8[n].X;
                        int ny = sy + GridCoord.Neighbors8[n].Y;

                        if (!field.IsInside(nx, ny))
                        {
                            continue;
                        }

                        // 바다는 높이 0의 낮은 이웃으로 봅니다. 해식애도 무너져야 합니다.
                        float there = field.IsLand(nx, ny) ? field.GetBase(nx, ny) : 0f;

                        below = Mathf.Max(below, here - there);
                        above = Mathf.Max(above, there - here);
                    }

                    int i = field.Index(sx, sy);

                    if (below > 0f)
                    {
                        brinkDistance[i] = 0f;
                        brinkDrop[i] = below;
                    }

                    if (above > 0f)
                    {
                        footDistance[i] = 0f;
                        footDrop[i] = above;
                    }
                }
            }
        }

        /// <summary>
        /// 경계에서 안쪽으로 거리와 낙차를 퍼뜨립니다.
        ///
        /// 다중 소스 너비 우선 탐색입니다. 프로젝트의 영향력 맵과 같은 구조입니다.
        /// 거리를 알아야 "벽에서 멀어질수록 얇아지는" 비탈을 만들 수 있습니다.
        /// </summary>
        private static void Spread(HeightField field, float[] distance, float[] drop, float reach)
        {
            var queue = new System.Collections.Generic.Queue<int>();

            for (int i = 0; i < distance.Length; i++)
            {
                if (distance[i] == 0f)
                {
                    queue.Enqueue(i);
                }
            }

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();

                int cx = current % field.SamplesX;
                int cy = current / field.SamplesX;

                for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
                {
                    var offset = GridCoord.Neighbors8[n];

                    int nx = cx + offset.X;
                    int ny = cy + offset.Y;

                    if (!field.IsLand(nx, ny))
                    {
                        continue;
                    }

                    float stepLength = offset.X != 0 && offset.Y != 0 ? 1.41421356f : 1f;
                    float candidate = distance[current] + stepLength;

                    if (candidate > reach)
                    {
                        continue;
                    }

                    int neighbor = field.Index(nx, ny);

                    if (candidate >= distance[neighbor])
                    {
                        continue;
                    }

                    distance[neighbor] = candidate;

                    // 낙차는 근원의 것을 그대로 물려받습니다. 높은 절벽의 영향이 멀리 갑니다.
                    drop[neighbor] = drop[current];

                    queue.Enqueue(neighbor);
                }
            }
        }

        /// <summary>
        /// 거리에 따른 감쇠입니다. 벽에 붙은 곳이 가장 두껍고 끝에서 0이 됩니다.
        ///
        /// 제곱으로 떨어뜨립니다. 선형이면 비탈이 직선 경사가 되어 인공물로 보입니다.
        /// 실제 애추는 벽 가까이가 급하고 끝으로 갈수록 완만하게 눕습니다.
        /// </summary>
        private static float Falloff(float distance, float reach)
        {
            if (distance >= reach)
            {
                return 0f;
            }

            float t = 1f - distance / reach;
            return t * t;
        }
    }
}
