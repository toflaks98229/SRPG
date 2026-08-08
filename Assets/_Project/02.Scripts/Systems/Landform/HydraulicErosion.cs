using UnityEngine;

namespace SRPG.Systems.Landform
{
    /// <summary>
    /// 물방울 침식입니다. 지형을 <b>타일 이전에</b> 깎습니다.
    ///
    /// <b>왜 이것이 먼저여야 하는가</b>
    ///
    /// 타일의 고도 단계를 먼저 정하고 그 안에서 침식을 돌리면, 침식이 움직일 수 있는 폭이
    /// 한 단의 절반으로 묶입니다. 그 폭으로는 골짜기가 파이지 않습니다 —
    /// 골짜기는 여러 단을 가로질러 내려가는 것이기 때문입니다.
    ///
    /// 그래서 순서를 뒤집습니다. 연속된 지형을 먼저 물로 깎고, <b>그 결과 위에</b>
    /// 타일을 얹습니다. 타일은 지형을 만드는 것이 아니라 읽어 내는 것이 됩니다.
    ///
    /// <b>왜 물방울인가</b>
    ///
    /// 골짜기가 "물이 판 것"으로 보이는 이유는 <b>지류가 아래로 갈수록 합쳐지기</b> 때문입니다.
    /// 그 위상을 손으로 만들 수도 있지만(그렇게 해 봤습니다), 물을 실제로 흘리면 공짜로 나옵니다.
    /// 게다가 물길이 지형을 깎으면서 스스로 깊어지는 되먹임까지 따라옵니다 —
    /// 한 번 파인 골로 다음 물이 더 모이고, 그래서 더 파입니다.
    ///
    /// <b>절차</b> (Musgrave 계열의 입자 기반 수문 침식)
    ///
    /// 물방울 하나를 무작위 지점에 떨어뜨리고, 다음을 반복합니다.
    ///
    ///   1. 기울기를 구해 관성과 섞어 방향을 정한다
    ///   2. 한 걸음 내려간다
    ///   3. 운반 능력 = max(낙차, 최소) × 속도 × 물의 양 × 계수
    ///   4. 실은 흙이 능력보다 많으면 <b>내려놓고</b>, 적으면 <b>깎아 싣는다</b>
    ///   5. 속도는 낙차만큼 붙고, 물은 조금씩 증발한다
    ///
    /// 4번이 전부입니다. 가파른 곳에서는 능력이 커져 깎고, 완만해지면 능력이 줄어 쌓습니다.
    /// 그래서 <b>골은 파이고 어귀에는 퇴적지가 생깁니다.</b>
    ///
    /// <b>왜 겹선형으로 주고받는가</b>
    ///
    /// 물방울은 격자 위가 아니라 그 사이를 흐릅니다. 가장 가까운 칸 하나에만 몰아 주면
    /// 격자 방향으로 줄무늬가 생깁니다. 네 칸에 나눠 주어야 그 무늬가 사라집니다.
    /// </summary>
    public static class HydraulicErosion
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>물방울이 살아 있는 최대 걸음 수입니다.</summary>
        private const int MaxLifetime = 48;

        /// <summary>깎고 쌓는 붓의 반경입니다. 칸 단위입니다.</summary>
        private const int BrushRadius = 2;

        /// <summary>이전 방향을 얼마나 유지하는지입니다. 0이면 늘 최급강하로만 흐릅니다.</summary>
        private const float Inertia = 0.06f;

        /// <summary>운반 능력 계수입니다. 클수록 골이 깊어집니다.</summary>
        private const float CapacityFactor = 3.2f;

        /// <summary>낙차가 거의 없어도 최소한 이만큼은 운반합니다. 평지에서 멈춰 버리지 않게 합니다.</summary>
        private const float MinimumSlope = 0.012f;

        /// <summary>한 걸음에 깎아 낼 수 있는 비율입니다.</summary>
        private const float ErodeRate = 0.32f;

        /// <summary>남는 흙을 내려놓는 비율입니다.</summary>
        private const float DepositRate = 0.28f;

        /// <summary>걸음마다 증발하는 물의 비율입니다.</summary>
        private const float Evaporation = 0.02f;

        /// <summary>중력입니다. 속도가 붙는 정도를 정합니다.</summary>
        private const float Gravity = 4f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 지형을 물로 깎습니다.
        /// </summary>
        /// <param name="height">깎을 높이 배열입니다. 제자리에서 바뀝니다.</param>
        /// <param name="width">가로 칸 수입니다.</param>
        /// <param name="depth">세로 칸 수입니다.</param>
        /// <param name="inside">그 칸이 육지인지 여부입니다. 물 위에서는 침식하지 않습니다.</param>
        /// <param name="dropletCount">떨어뜨릴 물방울 수입니다.</param>
        /// <param name="seed">같은 값이면 같은 결과가 나옵니다.</param>
        public static void Apply(
            float[] height,
            int width,
            int depth,
            bool[] inside,
            int dropletCount,
            int seed)
        {
            if (height == null || inside == null || dropletCount <= 0)
            {
                return;
            }

            var random = new System.Random(seed);

            for (int i = 0; i < dropletCount; i++)
            {
                // 물방울은 육지 어디에나 떨어집니다. 가장자리는 피합니다 — 바로 흘러 나가 버립니다.
                float px = (float)(random.NextDouble() * (width - 3)) + 1.5f;
                float py = (float)(random.NextDouble() * (depth - 3)) + 1.5f;

                if (!IsInside(inside, width, depth, (int)px, (int)py))
                {
                    continue;
                }

                Simulate(height, width, depth, inside, px, py);
            }
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 물방울 하나의 일생입니다.
        /// </summary>
        private static void Simulate(float[] height, int width, int depth, bool[] inside, float px, float py)
        {
            float dirX = 0f;
            float dirY = 0f;
            float speed = 1f;
            float water = 1f;
            float sediment = 0f;

            for (int step = 0; step < MaxLifetime; step++)
            {
                int nodeX = (int)px;
                int nodeY = (int)py;

                float cellX = px - nodeX;
                float cellY = py - nodeY;

                float heightHere = SampleHeight(height, width, depth, px, py);
                Gradient(height, width, depth, px, py, out float gradX, out float gradY);

                // 관성 — 물은 방향을 바로 꺾지 않습니다. 0이면 흐름이 격자를 따라 각지게 됩니다.
                dirX = dirX * Inertia - gradX * (1f - Inertia);
                dirY = dirY * Inertia - gradY * (1f - Inertia);

                float length = Mathf.Sqrt(dirX * dirX + dirY * dirY);
                if (length < 1e-5f)
                {
                    break;
                }

                dirX /= length;
                dirY /= length;

                px += dirX;
                py += dirY;

                if (!IsInside(inside, width, depth, (int)px, (int)py))
                {
                    break;
                }

                float heightThere = SampleHeight(height, width, depth, px, py);
                float drop = heightThere - heightHere;

                // 운반 능력. 가파르고 빠르고 물이 많을수록 많이 실을 수 있습니다.
                float capacity = Mathf.Max(-drop, MinimumSlope) * speed * water * CapacityFactor;

                if (sediment > capacity || drop > 0f)
                {
                    // 능력을 넘겼거나 오르막을 만났습니다. 내려놓습니다.
                    //
                    // 오르막이면 웅덩이를 메우는 만큼만 놓습니다. 그래야 물이 고이지 않고
                    // 넘어가 계속 흐릅니다.
                    float amount = drop > 0f
                        ? Mathf.Min(drop, sediment)
                        : (sediment - capacity) * DepositRate;

                    sediment -= amount;

                    // 퇴적은 지금 있는 네 칸에 나눠 놓습니다. 붓으로 넓게 펴면 골이 메워집니다.
                    DepositBilinear(height, width, depth, nodeX, nodeY, cellX, cellY, amount);
                }
                else
                {
                    // 깎아 싣습니다. 낙차보다 많이 파면 구멍이 뚫리므로 그만큼으로 묶습니다.
                    float amount = Mathf.Min((capacity - sediment) * ErodeRate, -drop);

                    sediment += ErodeWithBrush(height, width, depth, inside, nodeX, nodeY, amount);
                }

                speed = Mathf.Sqrt(Mathf.Max(0f, speed * speed + -drop * Gravity));
                water *= 1f - Evaporation;

                if (water < 0.01f)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 붓 모양으로 넓게 깎습니다.
        ///
        /// 한 칸만 파면 바늘구멍이 뚫려 지형이 곰보가 됩니다.
        /// 가운데가 깊고 가장자리로 갈수록 얕게 파야 골짜기 모양이 나옵니다.
        /// </summary>
        /// <returns>실제로 깎아 낸 총량입니다.</returns>
        private static float ErodeWithBrush(
            float[] height, int width, int depth, bool[] inside,
            int centerX, int centerY, float amount)
        {
            if (amount <= 0f)
            {
                return 0f;
            }

            float totalWeight = 0f;

            for (int oy = -BrushRadius; oy <= BrushRadius; oy++)
            {
                for (int ox = -BrushRadius; ox <= BrushRadius; ox++)
                {
                    if (!IsInside(inside, width, depth, centerX + ox, centerY + oy))
                    {
                        continue;
                    }

                    float distance = Mathf.Sqrt(ox * ox + oy * oy);
                    if (distance > BrushRadius)
                    {
                        continue;
                    }

                    totalWeight += 1f - distance / (BrushRadius + 1f);
                }
            }

            if (totalWeight <= 0f)
            {
                return 0f;
            }

            float removed = 0f;

            for (int oy = -BrushRadius; oy <= BrushRadius; oy++)
            {
                for (int ox = -BrushRadius; ox <= BrushRadius; ox++)
                {
                    int x = centerX + ox;
                    int y = centerY + oy;

                    if (!IsInside(inside, width, depth, x, y))
                    {
                        continue;
                    }

                    float distance = Mathf.Sqrt(ox * ox + oy * oy);
                    if (distance > BrushRadius)
                    {
                        continue;
                    }

                    float share = amount * (1f - distance / (BrushRadius + 1f)) / totalWeight;

                    height[y * width + x] -= share;
                    removed += share;
                }
            }

            return removed;
        }

        /// <summary>
        /// 네 칸에 나눠 쌓습니다. 한 칸에 몰아 주면 격자 방향 줄무늬가 생깁니다.
        /// </summary>
        private static void DepositBilinear(
            float[] height, int width, int depth,
            int nodeX, int nodeY, float cellX, float cellY, float amount)
        {
            AddAt(height, width, depth, nodeX, nodeY, amount * (1f - cellX) * (1f - cellY));
            AddAt(height, width, depth, nodeX + 1, nodeY, amount * cellX * (1f - cellY));
            AddAt(height, width, depth, nodeX, nodeY + 1, amount * (1f - cellX) * cellY);
            AddAt(height, width, depth, nodeX + 1, nodeY + 1, amount * cellX * cellY);
        }

        private static void AddAt(float[] height, int width, int depth, int x, int y, float amount)
        {
            if (x >= 0 && x < width && y >= 0 && y < depth)
            {
                height[y * width + x] += amount;
            }
        }

        /// <summary>겹선형으로 읽은 높이입니다.</summary>
        public static float SampleHeight(float[] height, int width, int depth, float px, float py)
        {
            int x0 = Mathf.Clamp((int)px, 0, width - 1);
            int y0 = Mathf.Clamp((int)py, 0, depth - 1);
            int x1 = Mathf.Min(x0 + 1, width - 1);
            int y1 = Mathf.Min(y0 + 1, depth - 1);

            float tx = Mathf.Clamp01(px - x0);
            float ty = Mathf.Clamp01(py - y0);

            float bottom = Mathf.Lerp(height[y0 * width + x0], height[y0 * width + x1], tx);
            float top = Mathf.Lerp(height[y1 * width + x0], height[y1 * width + x1], tx);

            return Mathf.Lerp(bottom, top, ty);
        }

        /// <summary>겹선형으로 읽은 기울기입니다.</summary>
        private static void Gradient(
            float[] height, int width, int depth, float px, float py,
            out float gradX, out float gradY)
        {
            int x0 = Mathf.Clamp((int)px, 0, width - 2);
            int y0 = Mathf.Clamp((int)py, 0, depth - 2);

            float tx = Mathf.Clamp01(px - x0);
            float ty = Mathf.Clamp01(py - y0);

            float h00 = height[y0 * width + x0];
            float h10 = height[y0 * width + x0 + 1];
            float h01 = height[(y0 + 1) * width + x0];
            float h11 = height[(y0 + 1) * width + x0 + 1];

            gradX = (h10 - h00) * (1f - ty) + (h11 - h01) * ty;
            gradY = (h01 - h00) * (1f - tx) + (h11 - h10) * tx;
        }

        private static bool IsInside(bool[] inside, int width, int depth, int x, int y)
        {
            return x >= 0 && x < width && y >= 0 && y < depth && inside[y * width + x];
        }
    }
}
