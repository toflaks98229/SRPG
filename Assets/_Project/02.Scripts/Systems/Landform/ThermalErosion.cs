using SRPG.Common;
using UnityEngine;

namespace SRPG.Systems.Landform
{
    /// <summary>
    /// 3단계 — 매스 무브먼트. 너무 가파른 면을 무너뜨립니다.
    ///
    /// <b>Musgrave, Kolb &amp; Mace (1989), The Synthesis and Rendering of Eroded Fractal Terrains</b>
    ///
    /// 마른 흙과 자갈은 일정 각도 이상으로 쌓이지 못합니다. 그 한계를 <b>안식각</b>이라 합니다.
    /// 그보다 가파른 면은 저절로 무너져 내려 아래에 쌓입니다. 그렇게 쌓인 비탈이 <b>애추(talus)</b>이고,
    /// 절벽 아래 어디에나 있는 그 완만한 흙더미가 지형을 "겪은 것"으로 보이게 합니다.
    ///
    /// 논문의 절차 그대로입니다. 셀 하나에 대해,
    ///
    ///   d_i     = h - h_i                     (이웃과의 높이차)
    ///   d_max   = max(d_i)                    (가장 가파른 쪽)
    ///   d_total = Σ d_i   단, d_i > T 인 것만  (무너질 수 있는 쪽의 합)
    ///   Δh      = c · (d_max - T)             (이번에 흘려보낼 양)
    ///   Δh_i    = Δh · d_i / d_total          (각 이웃에게 비례 배분)
    ///
    /// T는 안식각이 허용하는 높이차이고, 이웃까지의 수평 거리에 tan(안식각)을 곱한 값입니다.
    /// 대각선 이웃은 거리가 √2배라 T도 그만큼 큽니다 — 이 보정을 빼면 대각 방향으로만
    /// 흙이 새어 나가 지형에 격자 무늬가 생깁니다.
    ///
    /// <b>여기가 파이프라인의 핵심입니다</b>
    ///
    /// 2단계가 남긴 절단면은 수직에 가까운 흙벽입니다. 자연에는 그런 면이 없습니다.
    /// 이 단계가 그것을 무너뜨려 도로 양옆과 터 주변에 비탈을 만듭니다.
    /// 이 단계가 없으면 지형은 조각칼 자국이 그대로 남은 모형처럼 보입니다.
    ///
    /// <b>동시에 갱신합니다</b>
    ///
    /// 읽으면서 바로 쓰면 순회 방향에 따라 결과가 달라져, 흙이 한쪽으로 쓸려 갑니다.
    /// 한 번의 반복 안에서는 모두 같은 시점의 높이를 보고, 변화량을 모았다가 마지막에 더합니다.
    /// </summary>
    public static class ThermalErosion
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>기본 안식각입니다. 마른 흙과 자갈이 대체로 이 근처입니다.</summary>
        public const float DefaultAngleOfRepose = 33f;

        /// <summary>
        /// 한 번에 흘려보내는 비율입니다. 논문의 c에 해당합니다.
        ///
        /// 0.5는 초과분의 절반을 옮긴다는 뜻입니다. 1에 가까우면 진동하고,
        /// 너무 작으면 같은 결과를 얻는 데 반복이 많이 듭니다.
        /// </summary>
        public const float DefaultTransferRate = 0.5f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 안식각을 넘는 면을 무너뜨립니다.
        /// </summary>
        /// <param name="field">지형입니다.</param>
        /// <param name="iterations">반복 횟수입니다. 많을수록 비탈이 길게 뻗습니다.</param>
        /// <param name="angleOfReposeDegrees">안식각입니다. 작을수록 더 완만해집니다.</param>
        /// <param name="transferRate">한 번에 옮기는 비율입니다.</param>
        public static void Apply(
            HeightField field,
            int iterations,
            float angleOfReposeDegrees = DefaultAngleOfRepose,
            float transferRate = DefaultTransferRate)
        {
            if (field == null || iterations <= 0)
            {
                return;
            }

            float tangent = Mathf.Tan(Mathf.Clamp(angleOfReposeDegrees, 1f, 89f) * Mathf.Deg2Rad);
            transferRate = Mathf.Clamp(transferRate, 0.01f, 0.9f);

            int count = field.SamplesX * field.SamplesY;
            var delta = new float[count];

            // 이웃까지의 수평 거리로 정해지는 허용 높이차입니다. 방향마다 다릅니다.
            var threshold = new float[GridCoord.Neighbors8.Length];
            for (int n = 0; n < threshold.Length; n++)
            {
                var offset = GridCoord.Neighbors8[n];
                float distance = Mathf.Sqrt(offset.X * offset.X + offset.Y * offset.Y) * field.Spacing;

                threshold[n] = distance * tangent;
            }

            var drop = new float[GridCoord.Neighbors8.Length];

            for (int step = 0; step < iterations; step++)
            {
                System.Array.Clear(delta, 0, count);

                for (int sy = 0; sy < field.SamplesY; sy++)
                {
                    for (int sx = 0; sx < field.SamplesX; sx++)
                    {
                        if (!field.IsLand(sx, sy))
                        {
                            continue;
                        }

                        float here = field.GetHeight(sx, sy);

                        float total = 0f;
                        float steepest = 0f;

                        for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
                        {
                            drop[n] = 0f;

                            int nx = sx + GridCoord.Neighbors8[n].X;
                            int ny = sy + GridCoord.Neighbors8[n].Y;

                            if (!SharesLevel(field, sx, sy, nx, ny))
                            {
                                continue;
                            }

                            float difference = here - field.GetHeight(nx, ny);

                            // 안식각 안쪽이면 안정적입니다. 무너지지 않습니다.
                            if (difference <= threshold[n])
                            {
                                continue;
                            }

                            drop[n] = difference;
                            total += difference;
                            steepest = Mathf.Max(steepest, difference - threshold[n]);
                        }

                        if (total <= 0f)
                        {
                            continue;
                        }

                        float moved = transferRate * steepest;

                        delta[field.Index(sx, sy)] -= moved;

                        for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
                        {
                            if (drop[n] <= 0f)
                            {
                                continue;
                            }

                            int nx = sx + GridCoord.Neighbors8[n].X;
                            int ny = sy + GridCoord.Neighbors8[n].Y;

                            delta[field.Index(nx, ny)] += moved * (drop[n] / total);
                        }
                    }
                }

                for (int sy = 0; sy < field.SamplesY; sy++)
                {
                    for (int sx = 0; sx < field.SamplesX; sx++)
                    {
                        float change = delta[field.Index(sx, sy)];

                        if (change != 0f)
                        {
                            field.SetRelief(sx, sy, field.GetRelief(sx, sy) + change);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 두 표본이 같은 고도 단계에 있는지 봅니다.
        ///
        /// <b>이 검사가 없으면 침식이 절벽을 경사로 착각합니다.</b>
        ///
        /// 고도 한 단계는 0.9인데 기복이 움직일 수 있는 폭은 ±0.27뿐입니다.
        /// 절벽을 무너뜨리라고 시키면 매 반복마다 한계까지 밀어붙이다 양끝에서 잘려 나가고,
        /// 그 과정에서 흙이 사라지거나 생깁니다. 지형이 부풀거나 꺼집니다.
        ///
        /// 층 경계는 이 단계가 다룰 수 있는 것이 아닙니다.
        /// 그것은 <see cref="CliffCollapse"/>가 낙차에 비례해 따로 처리합니다.
        /// </summary>
        private static bool SharesLevel(HeightField field, int sx, int sy, int nx, int ny)
        {
            if (!field.IsLand(nx, ny))
            {
                return false;
            }

            // 고도 단계는 정수 배수라 정확히 같거나 확실히 다릅니다. 여유는 부동소수점 오차용입니다.
            return Mathf.Abs(field.GetBase(sx, sy) - field.GetBase(nx, ny)) < 0.001f;
        }

        /// <summary>
        /// 안식각을 넘는 표본이 몇 개인지 셉니다.
        ///
        /// 침식이 실제로 일을 했는지 재는 데 씁니다.
        /// "부드러워 보이는가"는 눈으로 봐야 하지만, "무너질 곳이 남았는가"는 셀 수 있습니다.
        /// </summary>
        public static int CountUnstable(HeightField field, float angleOfReposeDegrees = DefaultAngleOfRepose)
        {
            if (field == null)
            {
                return 0;
            }

            float tangent = Mathf.Tan(Mathf.Clamp(angleOfReposeDegrees, 1f, 89f) * Mathf.Deg2Rad);
            int unstable = 0;

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    if (!field.IsLand(sx, sy))
                    {
                        continue;
                    }

                    float here = field.GetHeight(sx, sy);

                    for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
                    {
                        var offset = GridCoord.Neighbors8[n];

                        int nx = sx + offset.X;
                        int ny = sy + offset.Y;

                        // 층 경계는 이 단계의 소관이 아닙니다. 세는 기준도 같아야 합니다.
                        if (!SharesLevel(field, sx, sy, nx, ny))
                        {
                            continue;
                        }

                        float distance = Mathf.Sqrt(offset.X * offset.X + offset.Y * offset.Y) * field.Spacing;

                        if (here - field.GetHeight(nx, ny) > distance * tangent)
                        {
                            unstable++;
                            break;
                        }
                    }
                }
            }

            return unstable;
        }
    }
}
