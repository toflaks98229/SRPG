using System.Collections.Generic;
using SRPG.Common;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Units;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Gameplay.Enemies
{
    /// <summary>
    /// 적 병사 한 명의 이동 판단입니다.
    ///
    /// 지금은 의도적으로 단순한 탐욕적(greedy) 규칙입니다.
    ///   · 시야 안에 플레이어 유닛이 있으면 그쪽으로 간다
    ///   · 없으면 가장 가까운 가옥으로 간다
    ///
    /// 조사 보고서에서 Fire Emblem식 규칙 기반 AI의 한계로 정리한 바로 그 수준이며,
    /// 이후 Systems/AI의 유틸리티 평가 + 영향력 맵으로 교체될 자리입니다.
    /// 교체 시점에 이 클래스는 "실행 계층"만 남기고 판단부를 SquadAI로 올립니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyAgent : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>다음 경로점으로 넘어가는 거리입니다.</summary>
        private const float WaypointReachDistance = 0.55f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly List<GridCoord> _path = new List<GridCoord>(64);

        private BattleContext _context;
        private Unit _unit;
        private float _repathTimer;
        private int _pathIndex;
        private GridCoord _currentGoal = GridCoord.Invalid;

        // ====================================================================================================
        // 3. Unity Lifecycle
        // ====================================================================================================

        private void Update()
        {
            if (_context == null || _unit == null || !_unit.IsAlive)
            {
                return;
            }

            _repathTimer -= UnityEngine.Time.deltaTime;
            if (_repathTimer <= 0f)
            {
                _repathTimer = _context.Tuning.EnemyRepathInterval;
                RecomputePath();
            }

            FollowPath();
        }

        // ====================================================================================================
        // 4. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 에이전트를 초기화합니다.
        /// </summary>
        public void Initialize(BattleContext context, Unit unit)
        {
            _context = context;
            _unit = unit;

            // 모든 적이 같은 프레임에 경로를 계산하면 스파이크가 생기므로 시작 시점을 흩뜨립니다.
            _repathTimer = Random.Range(0f, context.Tuning.EnemyRepathInterval);

            RecomputePath();
        }

        // ====================================================================================================
        // 5. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 목표를 정하고 경로를 다시 계산합니다.
        /// </summary>
        private void RecomputePath()
        {
            var goalTile = SelectGoalTile();
            if (goalTile == null)
            {
                return;
            }

            var startCoord = _context.Grid.WorldToCoord(_unit.Position);
            var startTile = _context.Grid.GetTile(startCoord);

            if (startTile == null || !startTile.IsWalkable)
            {
                // 아직 상륙정 위에 있거나 물 위에 있는 경우입니다. 가장 가까운 육지를 출발점으로 삼습니다.
                var nearest = _context.Grid.FindNearestWalkable(_unit.Position);
                if (nearest == null)
                {
                    return;
                }

                startCoord = nearest.Coord;
            }

            if (_currentGoal == goalTile.Coord && _path.Count > 0 && _pathIndex < _path.Count)
            {
                // 목표가 그대로면 기존 경로를 유지합니다.
                return;
            }

            if (_context.Pathfinder.TryFindPath(startCoord, goalTile.Coord, _path))
            {
                _currentGoal = goalTile.Coord;
                _pathIndex = 0;
            }
        }

        /// <summary>
        /// 목표 타일을 고릅니다. 플레이어 유닛이 우선이고, 없으면 가옥입니다.
        /// </summary>
        private Tile SelectGoalTile()
        {
            var nearestPlayer = _context.FindNearestEnemy(_unit.Position, Team.Enemy, _context.Tuning.EnemyAggroRadius);
            if (nearestPlayer != null)
            {
                var coord = _context.Grid.WorldToCoord(nearestPlayer.Position);
                var tile = _context.Grid.GetTile(coord);
                if (tile != null && tile.IsWalkable)
                {
                    return tile;
                }
            }

            return FindNearestHouse();
        }

        /// <summary>
        /// 가장 가까운 가옥 타일을 찾습니다. 가옥이 없으면 섬에서 가장 가까운 통행 가능 타일로 향합니다.
        /// </summary>
        private Tile FindNearestHouse()
        {
            var houses = _context.Grid.HouseTiles;

            Tile best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < houses.Count; i++)
            {
                float sqr = (houses[i].WorldCenter - _unit.Position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = houses[i];
                }
            }

            return best ?? _context.Grid.FindNearestWalkable(_unit.Position);
        }

        /// <summary>
        /// 경로를 따라 다음 지점을 유닛의 슬롯 목표로 넘깁니다.
        /// 실제 이동과 교전 판단은 <see cref="Unit"/>이 수행합니다.
        /// </summary>
        private void FollowPath()
        {
            if (_path.Count == 0 || _pathIndex >= _path.Count)
            {
                // 경로가 없으면 제자리를 목표로 두어 그 자리에서 교전하게 합니다.
                _unit.SetSlotTarget(_unit.Position);
                return;
            }

            Vector3 waypoint = _context.Grid.CoordToWorld(_path[_pathIndex]);

            Vector3 flatUnit = new Vector3(_unit.Position.x, 0f, _unit.Position.z);
            Vector3 flatWaypoint = new Vector3(waypoint.x, 0f, waypoint.z);

            if (Vector3.Distance(flatUnit, flatWaypoint) <= WaypointReachDistance)
            {
                _pathIndex++;
                if (_pathIndex >= _path.Count)
                {
                    return;
                }

                waypoint = _context.Grid.CoordToWorld(_path[_pathIndex]);
            }

            _unit.SetSlotTarget(waypoint);
        }
    }
}
