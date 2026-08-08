using System.Collections.Generic;
using SRPG.Common;
using SRPG.Systems.Landform;
using UnityEngine;

namespace SRPG.Systems.Grid
{
    /// <summary>
    /// 자연 지형을 <b>전술 지형</b>으로 만듭니다.
    ///
    /// <b>자연스러운 것과 재미있는 것은 다릅니다</b>
    ///
    /// 물과 중력이 만든 섬은 아름답지만 그대로는 게임이 안 됩니다.
    /// 침식은 <b>깎아 고르게 만드는</b> 힘이라, 오래 돌릴수록 모든 곳이 완만해지고
    /// 모든 곳이 이어집니다. 어디로든 갈 수 있으면 지킬 곳도 없습니다.
    ///
    /// Bad North의 섬은 풍경이 아니라 <b>문제</b>입니다.
    /// "부대는 셋인데 적이 올라올 곳은 넷이다. 무엇을 포기할 것인가."
    /// 그 문제가 성립하려면 지형이 다음을 갖춰야 합니다.
    ///
    ///   · <b>장벽</b>       — 오를 수 없는 면. 이것이 없으면 전선이 무한히 넓어집니다.
    ///   · <b>초크포인트</b> — 좁은 통로. 한 부대가 여럿을 막을 수 있는 자리입니다.
    ///   · <b>연결</b>       — 그럼에도 모든 목표에 닿을 수 있어야 합니다.
    ///
    /// <b>이 셋은 서로 싸웁니다.</b> 장벽을 늘리면 연결이 끊기고, 연결을 보장하려고
    /// 전부 이으면 장벽이 사라집니다. 여기서 그 균형을 잡습니다.
    ///
    /// <b>보이는 것과 갈 수 있는 곳이 같습니다</b>
    ///
    /// 통행 불가는 손으로 찍지 않고 <b>경사에서 읽어 냅니다</b>.
    /// 화면에 암반으로 그려지는 기준과 같은 값(32도)을 씁니다.
    /// 그래서 플레이어가 "저기는 못 가겠다"고 본 곳이 실제로 못 가는 곳입니다.
    /// 예전에는 무작위로 바위를 뿌렸기 때문에 그 대응이 없었습니다.
    /// </summary>
    public static class TacticalShaping
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// 이 경사를 넘으면 오를 수 없습니다. tan(경사각)입니다.
        ///
        /// 0.62는 32도이고, 지형을 암반으로 그리는 기준과 같은 값입니다.
        /// 두 값이 갈라지면 보이는 것과 갈 수 있는 곳이 어긋납니다.
        /// </summary>
        public const float ClimbLimit = 0.62f;

        /// <summary>연결 복구를 시도하는 최대 횟수입니다.</summary>
        private const int MaxRepairPasses = 64;

        /// <summary>경사로가 완전히 다져지는 반경입니다. 표본 단위입니다.</summary>
        private const float RampRadius = 1.4f;

        /// <summary>경사로가 주변으로 풀리는 반경입니다.</summary>
        private const float RampBlend = 3.2f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 지형의 경사에서 통행 불가를 읽어 냅니다.
        /// </summary>
        /// <returns>통행 불가로 표시된 칸 수입니다.</returns>
        public static int MarkCliffs(
            TerrainSimulation simulation,
            int width, int depth,
            bool[] isLand,
            int[] height,
            bool[] isCliff,
            float heightStep,
            bool[] carved = null)
        {
            int marked = 0;
            int step = TerrainSimulation.Subdivision;

            for (int y = 0; y < depth; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;
                    isCliff[i] = false;

                    if (!isLand[i])
                    {
                        continue;
                    }

                    // 일부러 낸 길은 벽이 되지 않습니다. 사람이 깎아 둔 통로이기 때문입니다.
                    if (carved != null && carved[i])
                    {
                        continue;
                    }

                    // 칸 안에서 지형이 몇 단이나 오르내리는지 봅니다.
                    //
                    // 상판이 완전히 평평하므로 한 칸 안의 높이 차이는 곧 <b>그 칸이
                    // 절벽면을 몇 개나 품고 있는가</b>입니다.
                    //
                    //   0단 — 판 위. 평평합니다.
                    //   1단 — 판의 가장자리. 한 계단이라 올라설 수 있습니다.
                    //   2단 이상 — 벽. 오를 수 없습니다.
                    float lowest = float.MaxValue;
                    float highest = float.MinValue;

                    for (int oy = 0; oy < step; oy++)
                    {
                        for (int ox = 0; ox < step; ox++)
                        {
                            int sx = x * step + ox;
                            int sy = y * step + oy;

                            if (!simulation.IsLand(sx, sy))
                            {
                                continue;
                            }

                            float sample = simulation.HeightAt(sx, sy);

                            lowest = Mathf.Min(lowest, sample);
                            highest = Mathf.Max(highest, sample);
                        }
                    }

                    if (lowest <= highest && highest - lowest > heightStep * 1.5f)
                    {
                        isCliff[i] = true;
                        marked++;
                    }
                }
            }

            marked += EnforceClimbable(width, depth, isLand, height, isCliff, carved);

            return marked;
        }

        /// <summary>
        /// 걸을 수 있는 땅이 하나로 이어지도록 경사로를 깎습니다.
        ///
        /// <b>왜 경사로가 곧 초크포인트인가</b>
        ///
        /// 장벽에 구멍을 하나 뚫으면 그 구멍이 유일한 통로가 됩니다.
        /// 연결을 복구하는 일이 그대로 <b>지킬 자리를 만드는 일</b>이 됩니다.
        /// 그래서 여기서는 넓게 트지 않고 <b>한 칸씩</b> 잇습니다.
        /// </summary>
        /// <returns>깎은 경사로의 수입니다.</returns>
        public static int ConnectRegions(
            TerrainSimulation simulation,
            int width, int depth,
            bool[] isLand,
            int[] height,
            bool[] isCliff,
            float heightStep,
            int maxLevel,
            bool[] carved = null)
        {
            int ramps = 0;

            for (int pass = 0; pass < MaxRepairPasses; pass++)
            {
                var component = new int[width * depth];
                int componentCount = LabelComponents(width, depth, isLand, height, isCliff, component);

                if (componentCount <= 1)
                {
                    break;
                }

                int largest = LargestComponent(component, componentCount);

                if (!TryFindBridge(width, depth, isLand, height, isCliff, component, largest,
                        out int fromIndex, out int toIndex))
                {
                    break;
                }

                CarveRamp(simulation, width, fromIndex, toIndex, heightStep);

                // 깎은 자리를 다시 계단으로 다집니다.
                // 이것을 빼면 경사로만 연속면으로 남아, 절벽 판정이 지형과 어긋납니다.
                LandformPipeline.Quantize(simulation, heightStep, maxLevel);

                // 지형이 바뀌었으니 고도와 통행 불가를 다시 읽습니다.
                LandformPipeline.ReadTileHeights(
                    simulation, width, depth, isLand, heightStep, maxLevel, height);

                MarkCliffs(simulation, width, depth, isLand, height, isCliff, heightStep, carved);

                ramps++;
            }

            SealUnreachablePockets(width, depth, isLand, height, isCliff, carved);

            return ramps;
        }

        /// <summary>
        /// 반드시 닿아야 하는 자리를 <b>확실히</b> 잇습니다.
        ///
        /// <b>왜 별도의 보장이 필요한가</b>
        ///
        /// <see cref="ConnectRegions"/>는 가장 싼 자리를 골라 조금씩 깎는 점진적 복구입니다.
        /// 대개 잘 듣지만, 깎은 자리가 다시 벽으로 판정되는 되먹임에 걸리면 수렴하지 못합니다.
        ///
        /// 가옥과 상륙 지점은 <b>못 닿으면 게임이 성립하지 않는</b> 자리입니다.
        /// 여기만은 "대개 된다"로 둘 수 없어서, 지형을 직접 계단으로 깎아 길을 냅니다.
        /// 한 번에 한 단씩만 오르내리므로 결과는 걸어 다닐 수 있는 통로입니다.
        /// </summary>
        /// <returns>실제로 뚫었으면 true입니다.</returns>
        public static bool ForceCorridor(
            TerrainSimulation simulation,
            int width, int depth,
            bool[] isLand,
            int[] height,
            bool[] isCliff,
            int fromIndex,
            int toIndex,
            float heightStep,
            int maxLevel,
            bool[] carved)
        {
            var cameFrom = new int[width * depth];

            for (int i = 0; i < cameFrom.Length; i++)
            {
                cameFrom[i] = -2;
            }

            // 절벽도 높이도 무시하고 육지만 따라 최단 경로를 찾습니다.
            // 어차피 지나갈 자리는 깎을 것이므로 지금의 통행 여부는 볼 필요가 없습니다.
            var queue = new Queue<int>();

            queue.Enqueue(fromIndex);
            cameFrom[fromIndex] = -1;

            while (queue.Count > 0 && cameFrom[toIndex] == -2)
            {
                int current = queue.Dequeue();

                int cx = current % width;
                int cy = current / width;

                for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                {
                    int nx = cx + GridCoord.Neighbors4[n].X;
                    int ny = cy + GridCoord.Neighbors4[n].Y;

                    if (nx < 0 || nx >= width || ny < 0 || ny >= depth)
                    {
                        continue;
                    }

                    int ni = ny * width + nx;

                    if (cameFrom[ni] != -2 || !isLand[ni])
                    {
                        continue;
                    }

                    cameFrom[ni] = current;
                    queue.Enqueue(ni);
                }
            }

            if (cameFrom[toIndex] == -2)
            {
                return false;
            }

            // 도착에서 출발로 되짚어 경로를 모읍니다.
            var path = new List<int>();

            for (int node = toIndex; node >= 0; node = cameFrom[node])
            {
                path.Add(node);
            }

            path.Reverse();

            // 출발 높이에서 도착 높이까지 한 단씩만 움직이는 계단을 놓습니다.
            int current2 = height[path[0]];

            for (int p = 0; p < path.Count; p++)
            {
                int target = height[path[path.Count - 1]];

                if (current2 < target)
                {
                    current2++;
                }
                else if (current2 > target)
                {
                    current2--;
                }

                // 마지막 칸은 원래 높이를 지켜야 합니다. 목표 자체를 옮기면 안 됩니다.
                int level = p == path.Count - 1 ? target : Mathf.Clamp(current2, 0, maxLevel);

                height[path[p]] = level;
                isCliff[path[p]] = false;

                // 이미 낸 길은 표시해 둡니다.
                // 나중에 다른 통로를 내면서 이 자리를 벽으로 돌리면 앞의 길이 끊깁니다.
                if (carved != null)
                {
                    carved[path[p]] = true;
                }

                WriteTileHeight(simulation, width, path[p], level * heightStep);
            }

            // 통로를 냈으니 그 옆이 어긋날 수 있습니다.
            //
            // 통로는 한 단씩만 오르내리지만, 통로 <b>바깥</b>의 땅과는 두 단 넘게 벌어질 수 있습니다.
            // 그런 자리를 두면 화면에는 이어져 보이는데 실제로는 못 가는 칸이 생깁니다.
            //
            // 어긋나면 언제나 <b>바깥쪽</b>을 벽으로 돌립니다. 통로를 막으면 방금 낸 길이 없어집니다.
            for (int p = 0; p < path.Count; p++)
            {
                int cx = path[p] % width;
                int cy = path[p] / width;

                for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                {
                    int nx = cx + GridCoord.Neighbors4[n].X;
                    int ny = cy + GridCoord.Neighbors4[n].Y;

                    if (nx < 0 || nx >= width || ny < 0 || ny >= depth)
                    {
                        continue;
                    }

                    int ni = ny * width + nx;

                    // 이미 낸 길은 막지 않습니다. 막으면 앞서 이어 둔 곳이 끊깁니다.
                    if (carved != null && carved[ni])
                    {
                        continue;
                    }

                    if (IsWalkable(isLand, isCliff, ni)
                        && Mathf.Abs(height[ni] - height[path[p]]) > 1)
                    {
                        isCliff[ni] = true;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 타일 한 칸에 해당하는 표본 전체를 한 높이로 눌러 놓습니다.
        /// </summary>
        private static void WriteTileHeight(TerrainSimulation simulation, int width, int tileIndex, float value)
        {
            int step = TerrainSimulation.Subdivision;

            int baseX = (tileIndex % width) * step;
            int baseY = (tileIndex / width) * step;

            for (int oy = 0; oy < step; oy++)
            {
                for (int ox = 0; ox < step; ox++)
                {
                    int sx = baseX + ox;
                    int sy = baseY + oy;

                    if (simulation.IsLand(sx, sy))
                    {
                        simulation.Height[sy * simulation.Width + sx] = value;
                    }
                }
            }
        }

        /// <summary>
        /// 초크포인트의 수를 셉니다.
        ///
        /// 그 칸을 막으면 걸을 수 있는 땅이 갈라지는 자리를 셉니다.
        /// 곧 <b>한 부대가 지키면 뒤가 안전해지는 자리</b>의 수입니다.
        /// 하나도 없으면 전선이 사방으로 열려 있다는 뜻이고, 그건 지킬 수 없는 섬입니다.
        /// </summary>
        public static int CountChokePoints(int width, int depth, bool[] isLand, int[] height, bool[] isCliff)
        {
            var component = new int[width * depth];
            int baseline = LabelComponents(width, depth, isLand, height, isCliff, component);

            int chokes = 0;
            var blocked = new bool[isCliff.Length];
            System.Array.Copy(isCliff, blocked, isCliff.Length);

            for (int i = 0; i < isLand.Length; i++)
            {
                if (!isLand[i] || isCliff[i])
                {
                    continue;
                }

                blocked[i] = true;

                if (LabelComponents(width, depth, isLand, height, blocked, component) > baseline)
                {
                    chokes++;
                }

                blocked[i] = false;
            }

            return chokes;
        }

        /// <summary>
        /// 이어 붙이지 못한 자투리 땅을 벽으로 돌립니다.
        ///
        /// <b>왜 남겨 두면 안 되는가</b>
        ///
        /// 절벽 위의 좁은 턱처럼, 어디서도 걸어갈 수 없는데 통행 가능으로 표시된 칸이
        /// 몇 개씩 남습니다. 그런 칸은 게임에 아무 기여도 하지 않으면서
        /// 부대 배치나 길찾기가 목표로 삼을 수 있어 <b>버그처럼 보이는 상황</b>을 만듭니다.
        ///
        /// 갈 수 없으면 갈 수 없는 것으로 그려야 합니다.
        /// </summary>
        public static void SealUnreachablePockets(
            int width, int depth, bool[] isLand, int[] height, bool[] isCliff, bool[] keep = null)
        {
            var component = new int[width * depth];
            int count = LabelComponents(width, depth, isLand, height, isCliff, component);

            if (count <= 1)
            {
                return;
            }

            int main = LargestComponent(component, count);

            for (int i = 0; i < component.Length; i++)
            {
                if (component[i] >= 0 && component[i] != main && (keep == null || !keep[i]))
                {
                    isCliff[i] = true;
                }
            }
        }

        /// <summary>
        /// 걸을 수 있는 이웃끼리는 한 계단 안에 있어야 합니다.
        ///
        /// <b>길찾기가 이 불변식을 전제로 돌아갑니다.</b>
        /// 두 칸 차이가 나는데 둘 다 통행 가능이면, 화면에는 이어져 보이는데
        /// 실제로는 못 가는 자리가 생깁니다. 그런 곳은 플레이어가 절대 이해하지 못합니다.
        ///
        /// 어기는 자리를 찾으면 <b>높은 쪽</b>을 벽으로 돌립니다.
        /// 낮은 쪽을 막으면 평지 한가운데에 구멍이 나지만,
        /// 높은 쪽은 어차피 올라설 수 없는 턱이라 벽으로 읽는 편이 자연스럽습니다.
        /// </summary>
        private static int EnforceClimbable(int width, int depth, bool[] isLand, int[] height, bool[] isCliff, bool[] carved)
        {
            int marked = 0;
            bool changed = true;
            int guard = 0;

            while (changed && guard++ < 32)
            {
                changed = false;

                for (int y = 0; y < depth; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = y * width + x;

                        if (!IsWalkable(isLand, isCliff, i))
                        {
                            continue;
                        }

                        for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                        {
                            int nx = x + GridCoord.Neighbors4[n].X;
                            int ny = y + GridCoord.Neighbors4[n].Y;

                            if (nx < 0 || nx >= width || ny < 0 || ny >= depth)
                            {
                                continue;
                            }

                            int ni = ny * width + nx;

                            if (!IsWalkable(isLand, isCliff, ni))
                            {
                                continue;
                            }

                            if (Mathf.Abs(height[i] - height[ni]) <= 1)
                            {
                                continue;
                            }

                            int higher = height[i] > height[ni] ? i : ni;

                            // 일부러 낸 길은 막지 않습니다. 막으면 이어 둔 곳이 끊깁니다.
                            if (carved != null && carved[higher])
                            {
                                higher = higher == i ? ni : i;

                                if (carved[higher])
                                {
                                    continue;
                                }
                            }

                            isCliff[higher] = true;
                            marked++;
                            changed = true;

                            if (higher == i)
                            {
                                break;
                            }
                        }
                    }
                }
            }

            return marked;
        }

        // ====================================================================================================
        // 3. Private Methods - Connectivity
        // ====================================================================================================

        /// <summary>
        /// 걸을 수 있는 칸을 이어진 덩어리로 나눕니다.
        ///
        /// <b>길찾기와 같은 규칙을 씁니다.</b>
        /// 통행 가능하고, 고도 차가 1 이하여야 이웃으로 칩니다.
        /// 규칙이 다르면 "이어진 줄 알았는데 못 가는" 섬이 나옵니다.
        /// </summary>
        private static int LabelComponents(
            int width, int depth, bool[] isLand, int[] height, bool[] isCliff, int[] component)
        {
            for (int i = 0; i < component.Length; i++)
            {
                component[i] = -1;
            }

            int next = 0;
            var queue = new Queue<int>();

            for (int start = 0; start < component.Length; start++)
            {
                if (!IsWalkable(isLand, isCliff, start) || component[start] >= 0)
                {
                    continue;
                }

                int id = next++;

                component[start] = id;
                queue.Clear();
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();

                    int cx = current % width;
                    int cy = current / width;

                    for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                    {
                        int nx = cx + GridCoord.Neighbors4[n].X;
                        int ny = cy + GridCoord.Neighbors4[n].Y;

                        if (nx < 0 || nx >= width || ny < 0 || ny >= depth)
                        {
                            continue;
                        }

                        int ni = ny * width + nx;

                        if (component[ni] >= 0 || !IsWalkable(isLand, isCliff, ni))
                        {
                            continue;
                        }

                        if (Mathf.Abs(height[ni] - height[current]) > 1)
                        {
                            continue;
                        }

                        component[ni] = id;
                        queue.Enqueue(ni);
                    }
                }
            }

            return next;
        }

        private static bool IsWalkable(bool[] isLand, bool[] isCliff, int index)
        {
            return isLand[index] && !isCliff[index];
        }

        private static int LargestComponent(int[] component, int count)
        {
            var sizes = new int[count];

            for (int i = 0; i < component.Length; i++)
            {
                if (component[i] >= 0)
                {
                    sizes[component[i]]++;
                }
            }

            int best = 0;

            for (int i = 1; i < count; i++)
            {
                if (sizes[i] > sizes[best])
                {
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// 본토와 떨어진 덩어리를 잇기에 가장 싼 자리를 찾습니다.
        ///
        /// 싸다는 것은 <b>깎을 흙이 적다</b>는 뜻입니다. 고도 차가 작은 자리를 고릅니다.
        /// 그래야 경사로가 짧고, 짧아야 좁고, 좁아야 초크포인트가 됩니다.
        /// </summary>
        private static bool TryFindBridge(
            int width, int depth, bool[] isLand, int[] height, bool[] isCliff,
            int[] component, int mainComponent,
            out int fromIndex, out int toIndex)
        {
            fromIndex = -1;
            toIndex = -1;

            int bestCost = int.MaxValue;

            for (int y = 0; y < depth; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;

                    if (component[i] != mainComponent)
                    {
                        continue;
                    }

                    for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                    {
                        int nx = x + GridCoord.Neighbors4[n].X;
                        int ny = y + GridCoord.Neighbors4[n].Y;

                        if (nx < 0 || nx >= width || ny < 0 || ny >= depth)
                        {
                            continue;
                        }

                        int ni = ny * width + nx;

                        // 본토가 아닌 육지면 이을 후보입니다. 통행 불가여도 깎으면 됩니다.
                        if (!isLand[ni] || component[ni] == mainComponent)
                        {
                            continue;
                        }

                        int cost = Mathf.Abs(height[ni] - height[i]) + (isCliff[ni] ? 2 : 0);

                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            fromIndex = i;
                            toIndex = ni;
                        }
                    }
                }
            }

            return fromIndex >= 0;
        }

        /// <summary>
        /// 두 칸 사이에 경사로를 깎습니다. 지형 자체를 바꿉니다.
        ///
        /// <b>타일의 고도를 직접 고치지 않습니다.</b>
        /// 고도는 지형에서 읽어 내는 값이라, 지형을 그대로 두고 고도만 바꾸면
        /// 보이는 땅과 걸을 수 있는 곳이 어긋납니다. 깎을 것은 지형입니다.
        /// </summary>
        private static void CarveRamp(
            TerrainSimulation simulation, int width, int fromIndex, int toIndex, float heightStep)
        {
            int step = TerrainSimulation.Subdivision;
            int half = step / 2;

            int fromX = (fromIndex % width) * step + half;
            int fromY = (fromIndex / width) * step + half;
            int toX = (toIndex % width) * step + half;
            int toY = (toIndex / width) * step + half;

            float startHeight = simulation.HeightAt(fromX, fromY);
            float endHeight = simulation.HeightAt(toX, toY);

            // 한 칸 안에서 한 단 넘게 오르내리지 않도록 목표를 눌러 둡니다.
            float clamped = Mathf.Clamp(endHeight, startHeight - heightStep, startHeight + heightStep);

            int steps = Mathf.Max(1, Mathf.Max(Mathf.Abs(toX - fromX), Mathf.Abs(toY - fromY)));

            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;

                int x = Mathf.RoundToInt(Mathf.Lerp(fromX, toX, t));
                int y = Mathf.RoundToInt(Mathf.Lerp(fromY, toY, t));

                SetLocalHeight(simulation, x, y, Mathf.Lerp(startHeight, clamped, t));
            }
        }

        /// <summary>
        /// 한 지점을 목표 높이로 끌어당깁니다. 가장자리는 부드럽게 풀립니다.
        /// </summary>
        private static void SetLocalHeight(TerrainSimulation simulation, int centerX, int centerY, float target)
        {
            int reach = Mathf.CeilToInt(RampBlend);

            for (int oy = -reach; oy <= reach; oy++)
            {
                for (int ox = -reach; ox <= reach; ox++)
                {
                    int x = centerX + ox;
                    int y = centerY + oy;

                    if (!simulation.IsLand(x, y))
                    {
                        continue;
                    }

                    float distance = Mathf.Sqrt(ox * ox + oy * oy);
                    float weight = TerrainSculptor.Falloff(distance, RampRadius, RampBlend);

                    if (weight <= 0f)
                    {
                        continue;
                    }

                    int i = y * simulation.Width + x;
                    simulation.Height[i] = Mathf.Lerp(simulation.Height[i], target, weight);
                }
            }
        }

        // ====================================================================================================
        // 4. Private Methods - Slope
        // ====================================================================================================

        private static float SlopeAt(TerrainSimulation simulation, int sx, int sy)
        {
            float here = simulation.HeightAt(sx, sy);
            float steepest = 0f;

            for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
            {
                var offset = GridCoord.Neighbors8[n];

                int nx = sx + offset.X;
                int ny = sy + offset.Y;

                if (!simulation.IsLand(nx, ny))
                {
                    continue;
                }

                float distance = Mathf.Sqrt(offset.X * offset.X + offset.Y * offset.Y) * simulation.Spacing;

                steepest = Mathf.Max(steepest, Mathf.Abs(here - simulation.HeightAt(nx, ny)) / distance);
            }

            return steepest;
        }
    }
}
