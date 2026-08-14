using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace SRPG.Systems.Pathfinding
{
    /// <summary>
    /// 두 자리 사이의 길을 구워진 길에서 뽑아냅니다.
    ///
    /// <b>분대 앵커가 쓰는 쪽입니다.</b>
    /// 병사는 각자 길잡이(<c>NavMeshAgent</c>)를 달고 스스로 길을 찾습니다.
    /// 앵커에는 그럴 몸이 없습니다 — 진형 중심은 아무도 서 있지 않은 자리라 길잡이를 붙일 수 없고,
    /// 목적지도 명령을 내릴 때만 바뀌므로 경로를 한 번 뽑아 그대로 들고 가면 됩니다.
    ///
    /// <b>왜 격자 A* 를 대신하는가</b>
    ///
    /// 통행 규칙이 두 벌이면 언젠가 어긋납니다. 격자는 칸 단위로 막힘을 재고
    /// 구워진 길은 복셀 단위로 재는데, 그 둘이 다르게 판단하는 자리가 반드시 생깁니다.
    /// 앵커는 지나갈 수 있다고 보는데 병사는 못 지나가면, 대열이 앵커를 따라가지 못한 채
    /// 늘어집니다 — 그리고 그 원인은 화면 어디에도 드러나지 않습니다.
    /// </summary>
    public static class NavRoute
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// 길 위의 자리를 찾을 때 허용하는 거리입니다.
        ///
        /// 앵커는 병사보다 넉넉해야 합니다 — 진형 중심은 실제로 아무도 서 있지 않는 자리이고,
        /// 물가나 벼랑 쪽으로 나가 있을 수 있습니다.
        /// </summary>
        private const float SampleRange = 4f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 출발지에서 목적지까지의 길을 뽑습니다.
        /// </summary>
        /// <param name="from">출발 자리입니다.</param>
        /// <param name="to">가려는 자리입니다.</param>
        /// <param name="corners">모퉁이를 담을 자리입니다. 부르는 쪽의 버퍼를 재사용합니다.</param>
        /// <param name="scratch">
        /// 계산에 쓰는 <see cref="NavMeshPath"/> 입니다.
        /// 부르는 쪽이 들고 있어야 합니다 — 판마다 새로 만들면 분대 수만큼 쓰레기가 쌓입니다.
        /// </param>
        /// <returns>
        /// 끝까지 이어지는 길을 찾았으면 true 입니다.
        /// <b>끊긴 길은 false 를 돌려주되 모퉁이는 채웁니다</b> —
        /// 갈 수 있는 데까지 가는 것과 아예 가지 않는 것은 다릅니다.
        /// </returns>
        public static bool TryFind(Vector3 from, Vector3 to, List<Vector3> corners, NavMeshPath scratch)
        {
            corners.Clear();

            if (scratch == null)
            {
                return false;
            }

            if (!NavMesh.SamplePosition(from, out var start, SampleRange, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(to, out var goal, SampleRange, NavMesh.AllAreas))
            {
                return false;
            }

            if (!NavMesh.CalculatePath(start.position, goal.position, NavMesh.AllAreas, scratch))
            {
                return false;
            }

            var found = scratch.corners;

            // 0번은 출발 자리입니다. 그리로 향하면 제자리걸음이 됩니다.
            for (int i = 1; i < found.Length; i++)
            {
                corners.Add(found[i]);
            }

            return scratch.status == NavMeshPathStatus.PathComplete && corners.Count > 0;
        }

        /// <summary>
        /// 길이 깔려 있는지 봅니다.
        ///
        /// 길이 없는 판(자동 검사, 편집 중의 전투 씬 단독 실행)에서는 경로를 물어도 소용없습니다.
        /// 그때는 부르는 쪽이 예전 방식으로 돌아가야 합니다.
        /// </summary>
        /// <returns>길이 깔려 있으면 true 입니다.</returns>
        public static bool IsBaked()
        {
            return NavMesh.CalculateTriangulation().vertices.Length > 0;
        }
    }
}
