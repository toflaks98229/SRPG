using SRPG.Common;
using UnityEngine;

namespace SRPG.Systems.Landform
{
    /// <summary>
    /// 타일이 있기 전의 지형입니다. 연속된 높이 하나뿐이고 고도 단계라는 개념이 없습니다.
    ///
    /// <b>순서를 뒤집었습니다</b>
    ///
    /// 예전에는 타일의 고도 단계를 먼저 정하고 그 안에서 침식을 돌렸습니다.
    /// 그러면 침식이 움직일 수 있는 폭이 한 단의 절반으로 묶입니다.
    /// 그 폭으로는 골짜기가 파이지 않습니다 — 골짜기는 여러 단을 가로질러 내려가니까요.
    /// 결국 물길을 손으로 그려 넣어야 했고, 그건 시뮬레이션이 아니라 작도였습니다.
    ///
    /// 이제는 여기서 <b>지형을 먼저 만들고</b>, 타일은 그 결과를 읽어 갑니다.
    /// 봉우리도 골짜기도 애추도 전부 물과 중력이 만듭니다.
    ///
    /// <b>해상도</b>
    ///
    /// 타일보다 촘촘해야 합니다. 타일 해상도로 침식을 돌리면 물길이 한 칸 폭이라
    /// 골짜기가 아니라 홈이 됩니다. 타일 하나를 <see cref="Subdivision"/>으로 나눕니다.
    /// </summary>
    public sealed class TerrainSimulation
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>타일 한 변을 나누는 수입니다.</summary>
        public const int Subdivision = 4;

        /// <summary>물방울 수입니다. 칸 하나당 몇 개인지로 셉니다.</summary>
        private const float DropletsPerCell = 5.5f;

        /// <summary>침식 뒤 사면을 안정시키는 반복 수입니다.</summary>
        private const int SettleIterations = 14;

        /// <summary>
        /// 버틸 수 있는 최대 경사입니다.
        ///
        /// <b>왜 흙의 안식각(34도)이 아닌가</b>
        ///
        /// 이 섬의 절벽은 <b>암반</b>입니다. 무른 흙이라면 34도에서 무너지지만,
        /// 바위는 훨씬 가파르게 서 있습니다. 34도로 두면 섬 전체가 완만한 언덕이 되어
        /// 절벽이 하나도 남지 않습니다 — 실제로 그렇게 되어 검사가 잡았습니다.
        ///
        /// 이 값이 낮으면 지형은 자연스러워지지만 <b>지킬 자리가 사라집니다</b>.
        /// 오를 수 없는 면이 없으면 전선이 사방으로 열리기 때문입니다.
        /// 물길이 판 골은 남기되 벽은 세워 두는 자리가 이 근처입니다.
        /// </summary>
        private const float AngleOfRepose = 56f;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>가로 표본 수입니다.</summary>
        public int Width { get; }

        /// <summary>세로 표본 수입니다.</summary>
        public int Depth { get; }

        /// <summary>표본 사이의 월드 거리입니다.</summary>
        public float Spacing { get; }

        /// <summary>표본 높이입니다. 월드 단위입니다.</summary>
        public float[] Height { get; }

        /// <summary>육지 여부입니다.</summary>
        public bool[] Land { get; }

        // ====================================================================================================
        // 3. Constructor
        // ====================================================================================================

        public TerrainSimulation(int tileWidth, int tileDepth, float cellSize)
        {
            Width = tileWidth * Subdivision;
            Depth = tileDepth * Subdivision;
            Spacing = cellSize / Subdivision;

            Height = new float[Width * Depth];
            Land = new bool[Width * Depth];
        }

        // ====================================================================================================
        // 4. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 지형을 만듭니다. 물과 중력만 씁니다.
        /// </summary>
        /// <param name="seed">같은 값이면 같은 지형이 나옵니다.</param>
        /// <param name="peakHeight">봉우리의 목표 높이입니다. 월드 단위입니다.</param>
        public void Simulate(int seed, float peakHeight)
        {
            RaiseBase(seed, peakHeight);

            int droplets = Mathf.RoundToInt(CountLand() * DropletsPerCell);
            HydraulicErosion.Apply(Height, Width, Depth, Land, droplets, seed);

            // 물이 깎고 남긴 급경사를 무너뜨립니다. 절벽 밑에 비탈이 생기는 곳이 여기입니다.
            SettleSlopes();
        }

        /// <summary>
        /// 월드 좌표에서의 높이입니다.
        /// </summary>
        public float SampleAt(float localX, float localZ)
        {
            return HydraulicErosion.SampleHeight(Height, Width, Depth, localX / Spacing, localZ / Spacing);
        }

        /// <summary>표본이 육지인지 봅니다.</summary>
        public bool IsLand(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Depth && Land[y * Width + x];
        }

        /// <summary>표본의 높이입니다.</summary>
        public float HeightAt(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Depth ? Height[y * Width + x] : 0f;
        }

        // ====================================================================================================
        // 5. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 침식할 재료를 쌓습니다. 해안에서 멀수록 높고, 그 위에 FBM으로 굴곡을 줍니다.
        ///
        /// 해안 거리를 기본으로 쓰는 이유는 섬이 바다에서 솟아야 하기 때문입니다.
        /// 그것만 쓰면 웨딩케이크가 되지만, 여기서는 <b>침식 이전의 재료</b>일 뿐입니다.
        /// 물이 이 위를 흐르며 골짜기를 파고 나면 원래 모양은 거의 남지 않습니다.
        /// </summary>
        private void RaiseBase(int seed, float peakHeight)
        {
            var distance = new int[Width * Depth];
            ComputeDistanceToWater(distance);

            int farthest = 1;
            for (int i = 0; i < distance.Length; i++)
            {
                if (Land[i])
                {
                    farthest = Mathf.Max(farthest, distance[i]);
                }
            }

            var random = new System.Random(seed);
            float offsetX = (float)random.NextDouble() * 900f + 0.37f;
            float offsetY = (float)random.NextDouble() * 900f + 0.11f;

            for (int y = 0; y < Depth; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;

                    if (!Land[i])
                    {
                        Height[i] = 0f;
                        continue;
                    }

                    // 해안에서 멀수록 높되, 제곱근으로 눌러 봉우리가 뾰족해지지 않게 합니다.
                    float t = Mathf.Sqrt((float)distance[i] / farthest);

                    // 능선을 섞습니다. 이게 있어야 물이 갈라져 흐를 등뼈가 생깁니다.
                    float shape = FbmNoise.Rolling(x * 0.035f + offsetX, y * 0.035f + offsetY, 4) * 0.5f + 0.5f;
                    float ridge = FbmNoise.Ridged(x * 0.05f + offsetY, y * 0.05f + offsetX, 3) * 0.5f + 0.5f;

                    Height[i] = t * peakHeight * Mathf.Lerp(0.55f, 1.25f, shape * 0.5f + ridge * 0.5f);
                }
            }
        }

        private void SettleSlopes()
        {
            Settle(Height, Land, Width, Depth, Spacing, SettleIterations);
        }

        /// <summary>
        /// 안식각을 넘는 면을 무너뜨립니다. (Musgrave, Kolb &amp; Mace 1989)
        ///
        /// 마른 흙과 자갈은 일정 각도 이상으로 쌓이지 못합니다. 그보다 가파른 면은
        /// 저절로 무너져 아래에 쌓이고, 그렇게 쌓인 비탈이 <b>애추</b>입니다.
        /// 절벽 아래 어디에나 있는 그 완만한 흙더미가 지형을 "겪은 것"으로 보이게 합니다.
        ///
        ///   d_i     = h - h_i                     (이웃과의 높이차)
        ///   d_max   = max(d_i - T)                (허용치를 넘은 정도)
        ///   Δh      = c · d_max                   (이번에 흘려보낼 양)
        ///   Δh_i    = Δh · d_i / Σd_i             (각 이웃에게 비례 배분)
        ///
        /// 대각선 이웃은 거리가 √2배라 허용치도 그만큼 큽니다.
        /// 이 보정을 빼면 대각 방향으로만 흙이 새어 격자 무늬가 생깁니다.
        ///
        /// <b>다지기 뒤에도 반드시 한 번 더 돌려야 합니다.</b>
        /// 평탄화는 완만한 곳만 단 높이로 끌어당기므로, 그대로 둔 사면과의 이음매에
        /// 새로운 급경사가 생깁니다. 그것을 무너뜨리지 않으면 수직 벽이 되살아납니다.
        /// 평지는 이미 안정적이라 이 과정에서 움직이지 않습니다.
        /// </summary>
        public static void Settle(
            float[] height, bool[] land, int width, int depth, float spacing, int iterations)
        {
            if (height == null || land == null || iterations <= 0)
            {
                return;
            }

            float tangent = Mathf.Tan(AngleOfRepose * Mathf.Deg2Rad);
            var delta = new float[width * depth];

            var threshold = new float[GridCoord.Neighbors8.Length];
            for (int n = 0; n < threshold.Length; n++)
            {
                var offset = GridCoord.Neighbors8[n];
                threshold[n] = Mathf.Sqrt(offset.X * offset.X + offset.Y * offset.Y) * spacing * tangent;
            }

            var drop = new float[GridCoord.Neighbors8.Length];

            for (int step = 0; step < iterations; step++)
            {
                System.Array.Clear(delta, 0, delta.Length);

                for (int y = 0; y < depth; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (!land[y * width + x])
                        {
                            continue;
                        }

                        float here = height[y * width + x];
                        float total = 0f;
                        float steepest = 0f;

                        for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
                        {
                            drop[n] = 0f;

                            int nx = x + GridCoord.Neighbors8[n].X;
                            int ny = y + GridCoord.Neighbors8[n].Y;

                            if (nx < 0 || nx >= width || ny < 0 || ny >= depth || !land[ny * width + nx])
                            {
                                continue;
                            }

                            float difference = here - height[ny * width + nx];

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

                        // 읽으면서 바로 쓰면 순회 방향으로 흙이 쓸립니다. 변화량을 모았다가 한 번에 더합니다.
                        float moved = 0.5f * steepest;
                        delta[y * width + x] -= moved;

                        for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
                        {
                            if (drop[n] <= 0f)
                            {
                                continue;
                            }

                            int nx = x + GridCoord.Neighbors8[n].X;
                            int ny = y + GridCoord.Neighbors8[n].Y;

                            delta[ny * width + nx] += moved * (drop[n] / total);
                        }
                    }
                }

                for (int i = 0; i < delta.Length; i++)
                {
                    if (delta[i] != 0f)
                    {
                        height[i] = Mathf.Max(0f, height[i] + delta[i]);
                    }
                }
            }
        }

        private void ComputeDistanceToWater(int[] distance)
        {
            var queue = new System.Collections.Generic.Queue<int>();

            for (int i = 0; i < Land.Length; i++)
            {
                if (Land[i])
                {
                    distance[i] = int.MaxValue;
                }
                else
                {
                    distance[i] = 0;
                    queue.Enqueue(i);
                }
            }

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();

                int cx = current % Width;
                int cy = current / Width;
                int next = distance[current] + 1;

                for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                {
                    int nx = cx + GridCoord.Neighbors4[n].X;
                    int ny = cy + GridCoord.Neighbors4[n].Y;

                    if (nx < 0 || nx >= Width || ny < 0 || ny >= Depth)
                    {
                        continue;
                    }

                    int ni = ny * Width + nx;

                    if (distance[ni] > next)
                    {
                        distance[ni] = next;
                        queue.Enqueue(ni);
                    }
                }
            }
        }

        private int CountLand()
        {
            int count = 0;

            for (int i = 0; i < Land.Length; i++)
            {
                if (Land[i])
                {
                    count++;
                }
            }

            return count;
        }
    }
}
