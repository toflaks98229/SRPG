using System.Collections.Generic;
using SRPG.Common;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Systems.Formation
{
    /// <summary>
    /// 진형의 중심점(앵커) 하나를 경로 위로 밀어 나갑니다.
    ///
    /// <b>왜 분리했는가</b>
    ///
    /// 플레이어 분대와 적 분대가 같은 방식으로 움직입니다. 앵커 하나가 경로를 따라가고
    /// 병사들은 각자 자기 슬롯으로 조향합니다. 이 구조가 양쪽에 그대로 필요한데,
    /// 예전에는 <c>Squad</c>와 <c>EnemyAgent</c>가 비슷하지만 미묘하게 다른 코드를 각각 들고 있었습니다.
    /// 한쪽에서 도착 판정을 고치면 다른 쪽은 그대로 남는 종류의 어긋남이 생깁니다.
    ///
    /// MonoBehaviour에 의존하지 않으므로 EditMode 테스트로 직접 검증할 수 있습니다.
    /// 예전 구조에서는 앵커 전진을 확인하려면 씬을 띄우고 분대를 만들어야 했습니다.
    /// </summary>
    public sealed class FormationMotor
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>다음 경로점으로 넘어가는 거리입니다.</summary>
        private const float WaypointReachDistance = 0.2f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly List<GridCoord> _path = new List<GridCoord>(64);
        private int _index;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>진형 중심의 현재 월드 좌표입니다.</summary>
        public Vector3 Anchor { get; private set; }

        /// <summary>현재 향하고 있는 목적지입니다. 경로가 없으면 <see cref="GridCoord.Invalid"/>입니다.</summary>
        public GridCoord Destination { get; private set; } = GridCoord.Invalid;

        /// <summary>
        /// 목적지에 도착했는지 여부입니다.
        /// 진형을 잡을지 말지를 가르는 유일한 기준이므로, 판정이 한 곳에만 있어야 합니다.
        /// </summary>
        public bool HasArrived => _index >= _path.Count;

        /// <summary>남은 경로점 수입니다. 디버그 표시에 씁니다.</summary>
        public int RemainingWaypoints => Mathf.Max(0, _path.Count - _index);

        // ====================================================================================================
        // 4. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 앵커를 지정 위치로 즉시 옮기고 경로를 지웁니다. 초기 배치에 씁니다.
        /// </summary>
        public void Teleport(Vector3 world, GridCoord coord)
        {
            Anchor = world;
            Destination = coord;
            _path.Clear();
            _index = 0;
        }

        /// <summary>
        /// 따라갈 경로를 설정합니다. 내용을 복사하므로 호출부는 버퍼를 계속 재사용해도 됩니다.
        /// </summary>
        /// <param name="path">출발지에서 목적지 순으로 정렬된 경로입니다.</param>
        /// <param name="destination">확정된 목적지 좌표입니다.</param>
        public void SetPath(IReadOnlyList<GridCoord> path, GridCoord destination)
        {
            _path.Clear();
            _index = 0;
            Destination = destination;

            if (path == null)
            {
                return;
            }

            for (int i = 0; i < path.Count; i++)
            {
                _path.Add(path[i]);
            }
        }

        /// <summary>
        /// 이동을 멈추고 현재 자리를 목적지로 삼습니다.
        /// </summary>
        public void Stop(GridCoord here)
        {
            _path.Clear();
            _index = 0;
            Destination = here;
        }

        /// <summary>
        /// 앵커를 경로를 따라 전진시킵니다.
        /// </summary>
        /// <param name="deltaTime">경과 시간입니다.</param>
        /// <param name="speed">앵커의 이동 속도입니다.</param>
        /// <param name="grid">지형입니다. 경로점의 월드 좌표와 지면 높이를 얻는 데 씁니다.</param>
        public void Advance(float deltaTime, float speed, IslandGrid grid)
        {
            if (HasArrived || grid == null || deltaTime <= 0f)
            {
                return;
            }

            Vector3 waypoint = grid.CoordToWorld(_path[_index]);

            Vector3 flatAnchor = new Vector3(Anchor.x, 0f, Anchor.z);
            Vector3 flatWaypoint = new Vector3(waypoint.x, 0f, waypoint.z);

            if (Vector3.Distance(flatAnchor, flatWaypoint) <= WaypointReachDistance)
            {
                _index++;

                if (HasArrived)
                {
                    _path.Clear();
                    _index = 0;
                }

                return;
            }

            Vector3 direction = (flatWaypoint - flatAnchor).normalized;

            Vector3 next = Anchor + direction * (speed * deltaTime);
            next.y = grid.SampleGroundHeight(next);

            Anchor = next;
        }
    }
}
