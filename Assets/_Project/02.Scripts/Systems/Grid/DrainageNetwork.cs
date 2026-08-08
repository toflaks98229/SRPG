using System.Collections.Generic;
using SRPG.Common;
using UnityEngine;

namespace SRPG.Systems.Grid
{
    /// <summary>
    /// 봉우리를 세우고 물길을 따라 계곡을 팝니다.
    ///
    /// <b>왜 필요한가</b>
    ///
    /// 고도를 해안 거리만으로 정하면 <c>level = f(해안거리)</c>가 단조 함수라
    /// 바다에서 멀수록 반드시 높습니다. 그러면 <b>계곡이 만들어질 수가 없습니다</b> —
    /// 계곡이란 "주변보다 낮은데 바다에서는 먼 곳"이고, 그건 정의상 그 식이 금지하는 형태입니다.
    /// 노이즈를 얹어도 계단이 울퉁불퉁해질 뿐 웨딩케이크는 웨딩케이크입니다.
    ///
    /// <b>왜 어떤 지형은 "물이 판 것"처럼 보이는가</b>
    ///
    /// 부드러워서가 아니라 <b>위상이 맞아서</b>입니다. 물이 판 골짜기는 예외 없이 이렇습니다.
    ///
    ///   · 높은 곳에서 시작한다
    ///   · 내려가기만 한다 — 되돌아 올라가지 않는다
    ///   · <b>아래로 갈수록 합쳐진다</b> — 갈라지지 않는다
    ///   · 바다에서 끝난다
    ///
    /// 이건 <b>바다를 뿌리로 하는 트리</b>입니다. 이 위상만 지키면 몇 단 안 되는 계단에서도
    /// "파인 것"으로 읽힙니다. 반대로 위상이 틀리면 아무리 부드러워도 그냥 울퉁불퉁한 땅입니다.
    ///
    /// <b>왜 침식 시뮬레이션이 아닌가</b>
    ///
    /// 물방울 침식은 연속 고도장에서 미세한 차이를 누적해 골을 만듭니다.
    /// 그 결과를 몇 단계로 양자화하면 만들어 낸 정보가 거의 전부 버려집니다.
    /// 살아남는 것은 결국 "골짜기가 어디를 지나는가"라는 위상뿐이므로, 그 위상을 직접 만듭니다.
    ///
    /// <b>여기서 다루는 것은 게임 구조입니다</b>
    ///
    /// 계곡은 통행로이자 엄폐물이자 초크포인트입니다. 그래서 <see cref="Landform.HeightField"/>의
    /// 기복이 아니라 <b>타일의 고도 단계</b>를 건드립니다. 보이는 굴곡은 그 위에 따로 얹힙니다.
    /// </summary>
    public static class DrainageNetwork
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>봉우리의 영향이 한 칸 멀어질 때마다 줄어드는 양입니다.</summary>
        private const int PeakFalloffPerTile = 1;

        /// <summary>물길이 한 번에 걸을 수 있는 최대 칸 수입니다. 무한 루프를 막습니다.</summary>
        private const int MaxFlowSteps = 512;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 봉우리를 세워 고도를 해안 거리에서 떼어냅니다.
        ///
        /// 해안 거리에 봉우리의 영향을 더해 <b>가공의 고도</b>를 만듭니다.
        /// 봉우리가 여럿이면 그 사이에 자연히 안부가 생기고, 그 안부가 계곡의 출발점이 됩니다.
        /// </summary>
        /// <param name="rng">난수원입니다.</param>
        /// <param name="w">격자 가로 칸 수입니다.</param>
        /// <param name="d">격자 세로 칸 수입니다.</param>
        /// <param name="isLand">육지 여부입니다.</param>
        /// <param name="distToWater">해안까지의 거리입니다.</param>
        /// <param name="peakCount">세울 봉우리 수입니다.</param>
        /// <param name="raw">결과로 채워지는 가공 고도입니다.</param>
        public static void RaisePeaks(
            System.Random rng,
            int w, int d,
            bool[] isLand,
            int[] distToWater,
            int peakCount,
            int[] raw)
        {
            for (int i = 0; i < raw.Length; i++)
            {
                raw[i] = isLand[i] ? distToWater[i] : 0;
            }

            if (peakCount <= 0)
            {
                return;
            }

            // 봉우리는 내륙 깊은 곳에 세웁니다. 해안에 세우면 절벽만 생기고 능선이 안 나옵니다.
            var candidates = new List<int>();
            int deepest = 0;

            for (int i = 0; i < isLand.Length; i++)
            {
                if (isLand[i])
                {
                    deepest = Mathf.Max(deepest, distToWater[i]);
                }
            }

            int threshold = Mathf.Max(2, deepest / 2);

            for (int i = 0; i < isLand.Length; i++)
            {
                if (isLand[i] && distToWater[i] >= threshold)
                {
                    candidates.Add(i);
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            var influence = new int[raw.Length];
            var queue = new Queue<int>();

            for (int p = 0; p < peakCount; p++)
            {
                int seed = candidates[rng.Next(candidates.Count)];

                // 봉우리마다 세기를 달리해 높낮이가 갈리게 합니다. 같으면 쌍둥이 산이 됩니다.
                int strength = 2 + rng.Next(3);

                System.Array.Clear(influence, 0, influence.Length);

                influence[seed] = strength;
                queue.Clear();
                queue.Enqueue(seed);

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    int next = influence[current] - PeakFalloffPerTile;

                    if (next <= 0)
                    {
                        continue;
                    }

                    int cx = current % w;
                    int cy = current / w;

                    for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                    {
                        int nx = cx + GridCoord.Neighbors4[n].X;
                        int ny = cy + GridCoord.Neighbors4[n].Y;

                        if (nx < 0 || nx >= w || ny < 0 || ny >= d)
                        {
                            continue;
                        }

                        int ni = ny * w + nx;

                        if (isLand[ni] && influence[ni] < next)
                        {
                            influence[ni] = next;
                            queue.Enqueue(ni);
                        }
                    }
                }

                for (int i = 0; i < raw.Length; i++)
                {
                    raw[i] += influence[i];
                }
            }
        }

        /// <summary>
        /// 물길을 따라 계곡을 팝니다.
        ///
        /// 고지에서 출발해 <b>가장 가파른 내리막</b>을 골라 바다까지 걸어갑니다.
        /// 여러 물길이 만나면 이후로는 같은 길을 공유합니다 — <b>합류가 공짜로</b> 생깁니다.
        /// 이 합류가 "물이 판 것"으로 읽히게 만드는 핵심입니다.
        /// </summary>
        /// <param name="rng">난수원입니다.</param>
        /// <param name="w">격자 가로 칸 수입니다.</param>
        /// <param name="d">격자 세로 칸 수입니다.</param>
        /// <param name="isLand">육지 여부입니다.</param>
        /// <param name="raw">가공 고도입니다. 물길을 고르는 기준입니다.</param>
        /// <param name="height">깎을 고도 단계입니다.</param>
        /// <param name="valleyCount">팔 계곡의 수입니다.</param>
        /// <param name="carved">계곡이 된 타일의 인덱스입니다. 필요 없으면 null입니다.</param>
        public static void Carve(
            System.Random rng,
            int w, int d,
            bool[] isLand,
            int[] raw,
            int[] height,
            int valleyCount,
            HashSet<int> carved = null)
        {
            if (valleyCount <= 0)
            {
                return;
            }

            // 물길의 출발점은 높은 곳입니다. 가장 높은 것들 중에서 고릅니다.
            var sources = new List<int>();
            int highest = 0;

            for (int i = 0; i < isLand.Length; i++)
            {
                if (isLand[i])
                {
                    highest = Mathf.Max(highest, raw[i]);
                }
            }

            for (int i = 0; i < isLand.Length; i++)
            {
                if (isLand[i] && raw[i] >= highest - 1)
                {
                    sources.Add(i);
                }
            }

            if (sources.Count == 0)
            {
                return;
            }

            var network = carved ?? new HashSet<int>();
            var visited = new HashSet<int>();

            for (int v = 0; v < valleyCount; v++)
            {
                int current = sources[rng.Next(sources.Count)];

                visited.Clear();

                for (int step = 0; step < MaxFlowSteps; step++)
                {
                    if (!visited.Add(current))
                    {
                        // 같은 칸으로 돌아왔습니다. 웅덩이에 갇힌 것이라 여기서 끊습니다.
                        break;
                    }

                    network.Add(current);

                    int next = SteepestDescent(rng, w, d, isLand, raw, current);

                    if (next < 0)
                    {
                        break;
                    }

                    // 바다에 닿았습니다. 물길의 끝입니다.
                    if (!isLand[next])
                    {
                        break;
                    }

                    current = next;
                }
            }

            foreach (int index in network)
            {
                height[index] = Mathf.Max(0, height[index] - 1);
            }
        }

        /// <summary>
        /// 인접한 칸의 고도 차이가 1을 넘지 않게 만듭니다.
        ///
        /// <b>낮추기만 합니다.</b> 이것이 전부입니다.
        ///
        /// 평활화하듯 양쪽을 평균 내면 방금 판 계곡이 도로 메워집니다.
        /// 위반이 생기면 언제나 <b>높은 쪽</b>을 내리고, 계곡 바닥은 절대 건드리지 않습니다.
        ///
        /// 이 제약은 장식이 아닙니다. 통행 판정이 "고도차 1 이하"를 쓰므로,
        /// 여기서 어기면 섬이 걸어서 못 가는 조각으로 쪼개집니다.
        /// </summary>
        public static void EnforceStepLimit(int w, int d, bool[] isLand, int[] height)
        {
            bool changed = true;
            int guard = 0;

            while (changed && guard++ < 64)
            {
                changed = false;

                for (int y = 0; y < d; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;

                        if (!isLand[i])
                        {
                            continue;
                        }

                        for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                        {
                            int nx = x + GridCoord.Neighbors4[n].X;
                            int ny = y + GridCoord.Neighbors4[n].Y;

                            if (nx < 0 || nx >= w || ny < 0 || ny >= d)
                            {
                                continue;
                            }

                            int ni = ny * w + nx;

                            // 바다와 맞닿은 육지는 0단이어야 합니다. 아니면 물속에서 절벽이 솟습니다.
                            int neighborHeight = isLand[ni] ? height[ni] : 0;

                            if (height[i] - neighborHeight > 1)
                            {
                                height[i] = neighborHeight + 1;
                                changed = true;
                            }
                        }
                    }
                }
            }
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 가장 가파른 내리막 이웃을 고릅니다. 동점이면 무작위로 가릅니다.
        ///
        /// 동점을 언제나 같은 순서로 고르면 물길이 전부 같은 방향으로 휘어
        /// 빗살무늬가 생깁니다. 무작위로 갈라야 물길이 흩어집니다.
        /// </summary>
        private static int SteepestDescent(System.Random rng, int w, int d, bool[] isLand, int[] raw, int from)
        {
            int cx = from % w;
            int cy = from / w;

            int bestDrop = 0;
            int bestIndex = -1;
            int tieCount = 0;

            for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
            {
                int nx = cx + GridCoord.Neighbors8[n].X;
                int ny = cy + GridCoord.Neighbors8[n].Y;

                if (nx < 0 || nx >= w || ny < 0 || ny >= d)
                {
                    continue;
                }

                int ni = ny * w + nx;

                // 바다는 언제나 최저점입니다. 물길은 거기서 끝납니다.
                if (!isLand[ni])
                {
                    return ni;
                }

                int drop = raw[from] - raw[ni];

                if (drop <= 0)
                {
                    continue;
                }

                if (drop > bestDrop)
                {
                    bestDrop = drop;
                    bestIndex = ni;
                    tieCount = 1;
                }
                else if (drop == bestDrop && rng.Next(++tieCount) == 0)
                {
                    bestIndex = ni;
                }
            }

            return bestIndex;
        }
    }
}
