using System.Collections.Generic;
using SRPG.Common;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Systems.Pathfinding
{
    /// <summary>
    /// 격자 경로에서 <b>없어도 되는 경유점을 걷어냅니다.</b> 흔히 스트링 풀링이라 부르는 방식입니다.
    ///
    /// <b>왜 필요한가</b>
    ///
    /// A*는 격자 칸을 따라가므로 결과가 언제나 칸 단위로 꺾입니다.
    /// 8방향으로 넓혀 계단은 사라졌지만, 여전히 45도의 배수로만 방향이 바뀝니다.
    /// 열린 평지를 비스듬히 가로지를 때도 경로가 몇 번씩 각지고, 앵커가 그 각을 그대로 따라갑니다.
    ///
    /// 실제로 사람은 <b>보이는 곳까지 직선으로</b> 걷습니다.
    /// 그래서 "여기서 저기가 보이면 사이의 경유점은 지운다"만 반복하면 됩니다.
    ///
    /// <b>다듬어도 안전한 이유</b>
    ///
    /// 두 점을 직선으로 이을지는 그 선이 지나는 <b>모든 칸</b>을 확인해서 정합니다.
    /// 경로 탐색과 똑같은 규칙(통행 가능·고도 차·모서리 통과 금지)을 쓰므로,
    /// 다듬은 경로는 원래 경로만큼 안전합니다. 지름길이 생기는 것이 아니라 군더더기가 빠질 뿐입니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 계산이라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public static class PathSmoother
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>통행 가능한 최대 고도 차입니다. 경로 탐색과 같은 값이어야 합니다.</summary>
        private const int MaxTraversableHeightDelta = 1;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 경로에서 불필요한 경유점을 걷어냅니다.
        ///
        /// 기준점에서 앞쪽 칸이 보이는 동안 계속 건너뛰고, 보이지 않는 순간 <b>직전 칸</b>을 남깁니다.
        /// 그 칸이 새 기준점이 됩니다. 시작과 끝은 항상 보존됩니다.
        /// </summary>
        /// <param name="grid">지형입니다.</param>
        /// <param name="path">원본 경로입니다. 인접한 칸이 이어져 있어야 합니다.</param>
        /// <param name="result">다듬은 경로가 채워집니다. 호출 시 비워집니다.</param>
        public static void Smooth(IslandGrid grid, IReadOnlyList<GridCoord> path, List<GridCoord> result)
        {
            result.Clear();

            if (path == null || path.Count == 0)
            {
                return;
            }

            result.Add(path[0]);

            if (path.Count == 1 || grid == null)
            {
                for (int i = 1; i < path.Count; i++)
                {
                    result.Add(path[i]);
                }

                return;
            }

            int anchor = 0;

            for (int i = 2; i < path.Count; i++)
            {
                if (HasLineOfSight(grid, path[anchor], path[i]))
                {
                    continue;
                }

                // 여기서 시야가 끊겼습니다. 직전 칸까지는 보였으므로 그것을 꺾이는 지점으로 남깁니다.
                anchor = i - 1;
                result.Add(path[anchor]);
            }

            result.Add(path[path.Count - 1]);
        }

        /// <summary>
        /// 두 칸 사이를 직선으로 갈 수 있는지 확인합니다.
        ///
        /// 선이 지나는 칸을 브레젠험으로 하나씩 밟으며 봅니다.
        /// 대각으로 건너뛰는 걸음에서는 경로 탐색과 마찬가지로 <b>양옆 두 칸</b>을 함께 확인합니다.
        /// 여기서 빠뜨리면 A*가 막아 둔 모서리 통과가 다듬기 단계에서 그대로 되살아납니다.
        /// </summary>
        /// <param name="grid">통행 규칙을 읽을 지형입니다.</param>
        /// <param name="from">기준 칸입니다.</param>
        /// <param name="to">바라보는 칸입니다.</param>
        /// <returns>두 칸을 직선으로 이을 수 있으면 true입니다.</returns>
        public static bool HasLineOfSight(IslandGrid grid, GridCoord from, GridCoord to)
        {
            if (grid == null)
            {
                return false;
            }

            var current = grid.GetTile(from);
            var goal = grid.GetTile(to);

            if (!IsOpen(current) || !IsOpen(goal))
            {
                return false;
            }

            int x = from.X;
            int y = from.Y;

            int dx = Mathf.Abs(to.X - from.X);
            int dy = Mathf.Abs(to.Y - from.Y);

            int stepX = to.X >= from.X ? 1 : -1;
            int stepY = to.Y >= from.Y ? 1 : -1;

            int error = dx - dy;

            while (x != to.X || y != to.Y)
            {
                int doubled = 2 * error;

                bool movesX = doubled > -dy;
                bool movesY = doubled < dx;

                if (movesX)
                {
                    error -= dy;
                    x += stepX;
                }

                if (movesY)
                {
                    error += dx;
                    y += stepY;
                }

                var next = grid.GetTile(new GridCoord(x, y));
                if (!IsTraversable(current, next))
                {
                    return false;
                }

                // 대각으로 건너뛴 걸음입니다. 모서리를 뚫고 지나가지 않는지 확인합니다.
                if (movesX && movesY)
                {
                    var sideX = grid.GetTile(new GridCoord(x, y - stepY));
                    var sideY = grid.GetTile(new GridCoord(x - stepX, y));

                    if (!IsTraversable(current, sideX) || !IsTraversable(current, sideY))
                    {
                        return false;
                    }
                }

                current = next;
            }

            return true;
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        private static bool IsOpen(Tile tile) => tile != null && tile.IsWalkable;

        /// <summary>한 칸에서 다음 칸으로 넘어갈 수 있는지 확인합니다. 경로 탐색과 같은 규칙입니다.</summary>
        private static bool IsTraversable(Tile from, Tile to)
        {
            return IsOpen(to)
                   && from != null
                   && Mathf.Abs(to.Height - from.Height) <= MaxTraversableHeightDelta;
        }
    }
}
