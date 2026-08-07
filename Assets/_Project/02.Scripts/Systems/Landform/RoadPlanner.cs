using System.Collections.Generic;
using SRPG.Common;
using UnityEngine;

namespace SRPG.Systems.Landform
{
    /// <summary>
    /// 2단계 — 경사 제약 경로 탐색. 사람이 실제로 낼 법한 길을 찾습니다.
    ///
    /// <b>Galin et al. (2010), Procedural Generation of Roads</b>
    ///
    /// 핵심은 비용 함수입니다. 길은 <b>최단 거리</b>가 아니라 <b>최소 건설 비용</b>을 따릅니다.
    /// 그리고 건설 비용은 경사에 대해 <b>초선형</b>으로 늘어납니다 — 두 배 가파르면
    /// 두 배가 아니라 훨씬 더 비쌉니다. 깎아 낼 흙의 양이 그렇게 늘기 때문입니다.
    ///
    ///   C(a→b) = |ab| · (1 + λ · slope^p)
    ///
    /// 이 초선형성이 전부입니다. p가 1이면 길은 그냥 비스듬히 올라가고,
    /// p가 2를 넘어가면 길은 <b>등고선을 따라 돌아서</b> 완만한 곳으로만 오릅니다.
    /// 산길이 지그재그인 이유가 이것이고, 여기서도 같은 이유로 지그재그가 나옵니다.
    ///
    /// <b>왜 별도의 탐색기인가</b>
    ///
    /// <see cref="SRPG.Systems.Pathfinding.NavGrid.GridPathfinder"/>는 유닛이 다닐 길을 찾습니다.
    /// 타일 단위이고, 통행 가능 여부만 봅니다. 여기서 필요한 것은 그보다 훨씬 촘촘한
    /// 표본 격자 위에서 <b>지형을 얼마나 깎아야 하는가</b>를 재는 탐색입니다.
    /// 목적이 다르므로 섞지 않습니다.
    /// </summary>
    public static class RoadPlanner
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>경사 페널티의 세기입니다. 논문의 λ에 해당합니다.</summary>
        public const float SlopeWeight = 34f;

        /// <summary>
        /// 경사 페널티의 지수입니다. 논문의 p에 해당합니다.
        ///
        /// 이 값이 1이면 길이 비탈을 곧장 치고 올라갑니다.
        /// 2를 넘겨야 등고선을 따라 도는 우회로가 곧장 오르는 것보다 싸집니다.
        /// </summary>
        public const float SlopeExponent = 2.4f;

        /// <summary>탐색 상한입니다. 길이 없을 때 무한정 돌지 않게 합니다.</summary>
        private const int MaxExpansions = 200000;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 두 표본 사이의 최소 건설 비용 경로를 찾습니다.
        /// </summary>
        /// <param name="field">지형입니다.</param>
        /// <param name="start">출발 표본입니다.</param>
        /// <param name="goal">도착 표본입니다.</param>
        /// <param name="path">찾은 경로입니다. 실패하면 비어 있습니다.</param>
        /// <returns>경로를 찾았으면 true입니다.</returns>
        public static bool TryFindPath(HeightField field, GridCoord start, GridCoord goal, List<GridCoord> path)
        {
            if (path == null)
            {
                return false;
            }

            path.Clear();

            if (field == null || !field.IsLand(start.X, start.Y) || !field.IsLand(goal.X, goal.Y))
            {
                return false;
            }

            int count = field.SamplesX * field.SamplesY;

            var cameFrom = new int[count];
            var gCost = new float[count];
            var closed = new bool[count];

            for (int i = 0; i < count; i++)
            {
                cameFrom[i] = -1;
                gCost[i] = float.MaxValue;
            }

            int startIndex = field.Index(start.X, start.Y);
            int goalIndex = field.Index(goal.X, goal.Y);

            gCost[startIndex] = 0f;

            // 우선순위 큐가 없으므로 정렬된 리스트로 대신합니다.
            // 표본이 수만 개 수준이라 이 정도면 충분히 빠릅니다.
            var open = new SortedSet<OpenNode>(OpenNode.Comparer);
            open.Add(new OpenNode(startIndex, Heuristic(field, start, goal)));

            int expansions = 0;

            while (open.Count > 0 && expansions++ < MaxExpansions)
            {
                var current = open.Min;
                open.Remove(current);

                if (closed[current.Index])
                {
                    continue;
                }

                closed[current.Index] = true;

                if (current.Index == goalIndex)
                {
                    Reconstruct(cameFrom, field, goalIndex, path);
                    return true;
                }

                int cx = current.Index % field.SamplesX;
                int cy = current.Index / field.SamplesX;

                for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
                {
                    int nx = cx + GridCoord.Neighbors8[n].X;
                    int ny = cy + GridCoord.Neighbors8[n].Y;

                    if (!field.IsLand(nx, ny))
                    {
                        continue;
                    }

                    int neighborIndex = field.Index(nx, ny);
                    if (closed[neighborIndex])
                    {
                        continue;
                    }

                    float step = gCost[current.Index] + StepCost(field, cx, cy, nx, ny);

                    if (step >= gCost[neighborIndex])
                    {
                        continue;
                    }

                    gCost[neighborIndex] = step;
                    cameFrom[neighborIndex] = current.Index;

                    open.Add(new OpenNode(
                        neighborIndex,
                        step + Heuristic(field, new GridCoord(nx, ny), goal)));
                }
            }

            return false;
        }

        /// <summary>
        /// 한 걸음의 건설 비용입니다.
        ///
        ///   C = |ab| · (1 + λ · slope^p)
        ///
        /// slope 는 수평 거리에 대한 높이 변화의 비, 즉 tan(경사각)입니다.
        /// </summary>
        public static float StepCost(HeightField field, int fromX, int fromY, int toX, int toY)
        {
            float dx = (toX - fromX) * field.Spacing;
            float dy = (toY - fromY) * field.Spacing;

            float horizontal = Mathf.Sqrt(dx * dx + dy * dy);
            if (horizontal <= 0f)
            {
                return 0f;
            }

            float rise = Mathf.Abs(field.GetHeight(toX, toY) - field.GetHeight(fromX, fromY));
            float slope = rise / horizontal;

            return horizontal * (1f + SlopeWeight * Mathf.Pow(slope, SlopeExponent));
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 남은 비용의 하한입니다.
        ///
        /// 경사 페널티는 언제나 0 이상이므로 <b>수평 거리만</b> 세는 것이 안전한 하한입니다.
        /// 하한을 넘겨 잡으면 A*가 최적해를 놓칩니다.
        /// </summary>
        private static float Heuristic(HeightField field, GridCoord from, GridCoord to)
        {
            float dx = (to.X - from.X) * field.Spacing;
            float dy = (to.Y - from.Y) * field.Spacing;

            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static void Reconstruct(int[] cameFrom, HeightField field, int goalIndex, List<GridCoord> path)
        {
            int current = goalIndex;

            while (current >= 0)
            {
                path.Add(new GridCoord(current % field.SamplesX, current / field.SamplesX));
                current = cameFrom[current];
            }

            path.Reverse();
        }

        // ====================================================================================================
        // 4. Nested Types
        // ====================================================================================================

        private readonly struct OpenNode
        {
            public readonly int Index;
            public readonly float FCost;

            public OpenNode(int index, float fCost)
            {
                Index = index;
                FCost = fCost;
            }

            /// <summary>
            /// 비용이 같은 노드가 서로를 밀어내지 않도록 인덱스로 동점을 가릅니다.
            /// <see cref="SortedSet{T}"/>는 비교 결과가 0이면 같은 원소로 보고 버립니다.
            /// </summary>
            public static readonly IComparer<OpenNode> Comparer = new NodeComparer();

            private sealed class NodeComparer : IComparer<OpenNode>
            {
                public int Compare(OpenNode a, OpenNode b)
                {
                    int byCost = a.FCost.CompareTo(b.FCost);
                    return byCost != 0 ? byCost : a.Index.CompareTo(b.Index);
                }
            }
        }
    }
}
