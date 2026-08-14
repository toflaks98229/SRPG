using System.Collections.Generic;
using SRPG.Common;
using SRPG.Gameplay.Battle;
using SRPG.Systems.Grid;
using SRPG.Systems.Pathfinding;
using UnityEngine;
using UnityEngine.AI;

namespace SRPG.Gameplay.Squads
{
    /// <summary>
    /// 분대 앵커가 갈 길을 뽑습니다. 아군과 적이 같은 규칙을 씁니다.
    ///
    /// <b>왜 한곳에 모으는가</b>
    ///
    /// 아군 분대와 적 분대는 서로 다른 클래스지만 <b>같은 지형 위를 걷습니다</b>.
    /// 길을 뽑는 규칙이 두 곳에 있으면 언젠가 한쪽만 고쳐지고,
    /// 그때부터 같은 자리에서 아군은 지나가는데 적은 못 지나갑니다.
    ///
    /// <b>격자로 되돌아가는 길을 남겨 둡니다</b>
    ///
    /// 구워진 길이 없는 판이 있습니다 — 자동 검사와, 전투 씬만 열어 보는 편집 중의 실행입니다.
    /// 그때 분대가 아예 움직이지 않으면 그 경로로는 아무것도 확인할 수 없게 됩니다.
    /// 길이 없으면 예전의 격자 A* 를 그대로 씁니다.
    ///
    /// <b>평소에는 쓰이지 않습니다.</b> 실제 전투에는 언제나 길이 깔려 있습니다.
    /// </summary>
    public static class SquadRoute
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>격자로 되돌아갈 때 쓰는 자리입니다. 한 번에 한 분대만 길을 뽑습니다.</summary>
        private static readonly List<GridCoord> CoordBuffer = new List<GridCoord>(64);

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 출발 칸에서 목적지 칸까지 앵커가 따라갈 월드 경로를 뽑습니다.
        /// </summary>
        /// <typeparam name="TSquad">이 맥락이 다루는 분대 종류입니다. 아군과 적이 같은 규칙을 씁니다.</typeparam>
        /// <param name="context">전장입니다.</param>
        /// <param name="scratch">
        /// 계산에 쓰는 경로 객체입니다. 부르는 쪽이 들고만 있고, <b>만드는 것은 여기</b>입니다 —
        /// 유니티가 MonoBehaviour 의 필드 초기화 시점에 만드는 것을 막기 때문입니다.
        /// </param>
        /// <param name="from">출발 칸입니다.</param>
        /// <param name="to">가려는 칸입니다.</param>
        /// <param name="route">모퉁이를 담을 자리입니다.</param>
        /// <param name="resolved">
        /// 실제로 향하게 된 칸입니다. 점유·배치가 이 값으로 셈을 하므로 반드시 칸이어야 합니다.
        /// </param>
        /// <returns>따라갈 경로를 얻었으면 true 입니다.</returns>
        public static bool TryPlan<TSquad>(
            ISquadContext<TSquad> context,
            ref NavMeshPath scratch,
            GridCoord from,
            GridCoord to,
            List<Vector3> route,
            out GridCoord resolved)
            where TSquad : class
        {
            resolved = to;

            scratch ??= new NavMeshPath();

            var grid = context.Grid;

            if (NavRoute.TryFind(grid.CoordToWorld(from), grid.CoordToWorld(to), route, scratch))
            {
                return true;
            }

            // 끊긴 길이라도 모퉁이가 있으면 갈 수 있는 데까지 갑니다.
            // 강 건너를 찍었을 때 물가까지는 나가 서야 합니다 — 제자리에 서 버리면
            // 명령이 먹히지 않은 것처럼 보입니다.
            if (route.Count > 0)
            {
                resolved = grid.WorldToCoord(route[route.Count - 1]);

                return true;
            }

            return TryPlanOnGrid(context, from, to, route, out resolved);
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 구워진 길이 없을 때 격자 A* 로 길을 뽑습니다.
        /// </summary>
        /// <typeparam name="TSquad">이 맥락이 다루는 분대 종류입니다.</typeparam>
        /// <param name="context">전장입니다.</param>
        /// <param name="from">출발 칸입니다.</param>
        /// <param name="to">가려는 칸입니다.</param>
        /// <param name="route">모퉁이를 담을 자리입니다.</param>
        /// <param name="resolved">실제로 향하게 된 칸입니다.</param>
        /// <returns>경로를 얻었으면 true 입니다.</returns>
        private static bool TryPlanOnGrid<TSquad>(
            ISquadContext<TSquad> context,
            GridCoord from,
            GridCoord to,
            List<Vector3> route,
            out GridCoord resolved)
            where TSquad : class
        {
            resolved = to;

            if (context.Pathfinder == null ||
                !context.Pathfinder.TryFindSmoothedPathSnapped(from, to, CoordBuffer, out resolved))
            {
                return false;
            }

            route.Clear();

            for (int i = 0; i < CoordBuffer.Count; i++)
            {
                route.Add(context.Grid.CoordToWorld(CoordBuffer[i]));
            }

            return route.Count > 0;
        }
    }
}
