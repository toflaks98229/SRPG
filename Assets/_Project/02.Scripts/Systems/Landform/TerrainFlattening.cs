using SRPG.Common;
using UnityEngine;

namespace SRPG.Systems.Landform
{
    /// <summary>
    /// 지형 평탄화. 침식된 지형 위에 <b>딛을 수 있는 평지</b>를 만듭니다.
    ///
    /// <b>무엇을 푸는가</b>
    ///
    /// 물과 중력이 만든 지형은 자연스럽지만 그대로는 게임이 안 됩니다.
    /// 온통 비탈이라 어디가 딛는 곳인지 읽히지 않고, 부대가 설 자리도 없습니다.
    /// 반대로 전부 계단으로 깎으면 판독은 되지만 압출한 상자가 됩니다.
    ///
    /// 그래서 <b>경사에 따라 다르게</b> 다집니다.
    ///
    ///   · 완만한 곳 — 바싹 다져 진짜 평지로 만듭니다. 여기가 딛는 곳입니다.
    ///   · 가파른 곳 — 그대로 둡니다. 침식이 만든 비탈이 살아남습니다.
    ///
    /// 결과는 <b>계단식 밭</b>입니다. 평평한 단들이 자연스러운 비탈로 이어집니다.
    /// 단은 또렷하니 판독이 되고, 이음매는 깎인 사면이니 직각이 없습니다.
    ///
    /// <b>왜 이것이 90도를 없애는가</b>
    ///
    /// 예전에는 단 사이를 <b>수직 벽</b>으로 이었습니다. 벽은 어디서 보든 90도이고,
    /// 바닥과 만나는 자리도 90도입니다. 자연에 그런 면은 없습니다.
    ///
    /// 여기서는 벽을 세우지 않습니다. 단과 단 사이는 <b>침식이 남긴 사면</b>이고,
    /// 그 사면의 밑동에는 무너져 쌓인 흙이 이미 깔려 있습니다.
    /// 직각이 생길 자리가 아예 없습니다.
    /// </summary>
    public static class TerrainFlattening
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>이 각보다 완만하면 완전히 다집니다.</summary>
        private const float FlatBelowDegrees = 16f;

        /// <summary>이 각보다 가파르면 손대지 않습니다.</summary>
        private const float KeepAboveDegrees = 33f;

        /// <summary>
        /// 다져진 뒤에도 남기는 잔 굴곡의 비율입니다.
        ///
        /// 0이면 수학적으로 완벽한 평면이 되어 오히려 인공물로 보입니다.
        /// 아주 조금 남겨야 다져 놓은 흙으로 읽힙니다.
        /// </summary>
        private const float ResidualRoughness = 0.12f;

        /// <summary>다진 뒤 이음매를 무너뜨리는 반복 수입니다.</summary>
        private const int SettleAfterFlatten = 10;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 침식된 지형을 다져 계단식 지면을 만듭니다.
        /// </summary>
        /// <param name="surface">지형 높이입니다. 제자리에서 바뀝니다.</param>
        /// <param name="level">각 표본이 속한 고도 단계입니다. 결과로 채워집니다.</param>
        /// <param name="land">육지 여부입니다.</param>
        /// <param name="width">가로 표본 수입니다.</param>
        /// <param name="depth">세로 표본 수입니다.</param>
        /// <param name="spacing">표본 사이의 월드 거리입니다.</param>
        /// <param name="heightStep">고도 한 단계의 월드 높이입니다.</param>
        /// <param name="maxLevel">최대 고도 단계입니다.</param>
        public static void Apply(
            float[] surface,
            int[] level,
            bool[] land,
            int width,
            int depth,
            float spacing,
            float heightStep,
            int maxLevel)
        {
            if (surface == null || level == null)
            {
                return;
            }

            float flatLimit = Mathf.Tan(FlatBelowDegrees * Mathf.Deg2Rad);
            float keepLimit = Mathf.Tan(KeepAboveDegrees * Mathf.Deg2Rad);

            var flattened = new float[surface.Length];

            for (int y = 0; y < depth; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;

                    if (!land[i])
                    {
                        flattened[i] = 0f;
                        level[i] = 0;
                        continue;
                    }

                    // 이 표본이 어느 단에 속하는지는 <b>침식된 높이가</b> 정합니다.
                    // 그래서 단 경계가 등고선이 됩니다 — 타일 격자와 아무 상관이 없습니다.
                    int band = Mathf.Clamp(Mathf.RoundToInt(surface[i] / heightStep), 0, maxLevel);
                    level[i] = band;

                    float target = band * heightStep;

                    // 얼마나 가파른가로 다지는 정도를 정합니다.
                    float slope = SlopeAt(surface, land, width, depth, spacing, x, y);
                    float flatness = 1f - Mathf.InverseLerp(flatLimit, keepLimit, slope);

                    // 완만한 곳은 단 높이로 끌어당기고, 가파른 곳은 그대로 둡니다.
                    float pulled = Mathf.Lerp(surface[i], target, flatness);

                    // 다져도 아주 조금은 남깁니다. 완벽한 평면은 인공물로 보입니다.
                    flattened[i] = Mathf.Lerp(pulled, surface[i], ResidualRoughness * flatness);
                }
            }

            System.Array.Copy(flattened, surface, surface.Length);

            // <b>다지고 나면 반드시 다시 무너뜨려야 합니다.</b>
            //
            // 완만한 곳만 단 높이로 끌어당겼으므로, 그대로 둔 사면과의 이음매에
            // 새로운 급경사가 생깁니다. 그것을 두면 수직 벽이 되살아납니다 —
            // 벽을 없애려고 한 일이 벽을 다시 만드는 셈입니다.
            //
            // 평지는 이미 안식각 안이라 이 과정에서 움직이지 않습니다.
            // 이음매만 무너져 발치에 비탈이 깔립니다.
            TerrainSimulation.Settle(surface, land, width, depth, spacing, SettleAfterFlatten);

            for (int i = 0; i < surface.Length; i++)
            {
                if (!land[i])
                {
                    continue;
                }

                // 육지가 해수면 아래로 내려가면 안 됩니다.
                // 물이 깎다 보면 해안 근처가 0 밑으로 파이는데, 그대로 두면
                // 걸을 수 있는 땅이 물에 잠겨 보입니다.
                surface[i] = Mathf.Max(0f, surface[i]);

                // 무너지면서 단 경계가 조금 움직였으므로 다시 읽습니다.
                level[i] = Mathf.Clamp(Mathf.RoundToInt(surface[i] / heightStep), 0, maxLevel);
            }
        }

        /// <summary>
        /// 한 지점 주변을 강하게 다집니다. 건물이 앉을 터입니다.
        ///
        /// 경사와 무관하게 눌러 버립니다. 사람이 삽으로 판 자리이기 때문입니다.
        /// 다만 가장자리는 부드럽게 풀어 주어야 접힘선이 남지 않습니다.
        /// </summary>
        public static void FlattenPad(
            float[] surface,
            bool[] land,
            int width,
            int depth,
            int centerX,
            int centerY,
            float radius,
            float blendRadius)
        {
            if (surface == null || radius <= 0f)
            {
                return;
            }

            blendRadius = Mathf.Max(blendRadius, radius + 0.01f);

            int reach = Mathf.CeilToInt(blendRadius);
            int center = centerY * width + centerX;

            if (centerX < 0 || centerX >= width || centerY < 0 || centerY >= depth || !land[center])
            {
                return;
            }

            float target = surface[center];

            for (int oy = -reach; oy <= reach; oy++)
            {
                for (int ox = -reach; ox <= reach; ox++)
                {
                    int x = centerX + ox;
                    int y = centerY + oy;

                    if (x < 0 || x >= width || y < 0 || y >= depth)
                    {
                        continue;
                    }

                    int i = y * width + x;
                    if (!land[i])
                    {
                        continue;
                    }

                    float distance = Mathf.Sqrt(ox * ox + oy * oy);
                    if (distance > blendRadius)
                    {
                        continue;
                    }

                    float weight = TerrainSculptor.Falloff(distance, radius, blendRadius);

                    surface[i] = Mathf.Lerp(surface[i], target, weight);
                }
            }
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 표본의 경사입니다. 수평 거리에 대한 높이 변화의 비, 즉 tan(경사각)입니다.
        /// </summary>
        public static float SlopeAt(
            float[] surface, bool[] land, int width, int depth, float spacing, int x, int y)
        {
            float here = surface[y * width + x];
            float steepest = 0f;

            for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
            {
                var offset = GridCoord.Neighbors8[n];

                int nx = x + offset.X;
                int ny = y + offset.Y;

                if (nx < 0 || nx >= width || ny < 0 || ny >= depth || !land[ny * width + nx])
                {
                    continue;
                }

                float distance = Mathf.Sqrt(offset.X * offset.X + offset.Y * offset.Y) * spacing;
                float difference = Mathf.Abs(here - surface[ny * width + nx]);

                steepest = Mathf.Max(steepest, difference / distance);
            }

            return steepest;
        }
    }
}
