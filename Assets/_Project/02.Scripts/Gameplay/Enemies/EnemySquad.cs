using System.Collections.Generic;
using SRPG.Common;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Units;
using SRPG.Systems.AI;
using SRPG.Systems.Formation;
using UnityEngine;

namespace SRPG.Gameplay.Enemies
{
    /// <summary>
    /// 상륙정 한 척이 내려놓은 병력의 분대입니다. 플레이어 분대와 <b>같은 방식으로</b> 움직입니다.
    /// 앵커 하나가 경로를 따라가고 병사들은 각자 슬롯으로 조향합니다.
    ///
    /// <b>무엇이 달라졌는가</b>
    ///
    /// 예전에는 병사마다 <c>EnemyAgent</c>가 붙어 각자 경로를 잡았습니다. 결과가 두 가지로 나빴습니다.
    ///
    ///   · <b>판단이 유닛 단위였습니다.</b> "내 시야의 가장 가까운 적"만 보므로 전술이 성립하지 않고,
    ///     열 명이 각자 다른 판단을 해 무리가 흩어졌습니다
    ///   · <b>경로 탐색이 유닛 수만큼</b> 돌았습니다. 같은 목표를 향하는 열 명이 열 번 A*를 돌립니다
    ///
    /// 지금은 분대가 한 번 판단하고 한 번 경로를 잡습니다.
    /// 판단 자체는 <see cref="EnemyGoalPlanner"/>가 하고, 이 클래스는 그 결과를 실행만 합니다.
    /// 판단부가 MonoBehaviour 밖에 있으므로 EditMode에서 직접 검증됩니다.
    ///
    /// <b>교전은 여전히 병사가 알아서 합니다.</b>
    /// 분대는 "어디로 갈지"만 정하고, 사거리 안의 적을 치는 것은 <see cref="Unit"/>의 몫입니다.
    /// 플레이어 분대와 정확히 같은 분업입니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySquad : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>목표 재판단 시점을 분대마다 흩뜨리는 폭(초)입니다. 같은 프레임에 몰리지 않게 합니다.</summary>
        private const float ReplanJitter = 0.6f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly List<Unit> _units = new List<Unit>(8);
        private readonly List<Vector3> _slots = new List<Vector3>(8);
        private readonly List<GridCoord> _path = new List<GridCoord>(64);
        private readonly List<GoalCandidate> _candidates = new List<GoalCandidate>(16);

        private readonly FormationMotor _motor = new FormationMotor();
        private readonly EnemyGoalPlanner _planner = new EnemyGoalPlanner();

        private BattleContext _context;
        private float _replanTimer;
        private float _anchorSpeed = 3f;
        private float _currentGoalScore;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>소속 병사 목록입니다.</summary>
        public IReadOnlyList<Unit> Units => _units;

        /// <summary>생존 병사 수입니다.</summary>
        public int AliveCount => _units.Count;

        /// <summary>분대 진형의 중심 월드 좌표입니다.</summary>
        public Vector3 AnchorPosition => _motor.Anchor;

        /// <summary>현재 향하는 목표 좌표입니다.</summary>
        public GridCoord GoalCoord => _motor.Destination;

        /// <summary>현재 목표의 종류입니다. 디버그 표시에 씁니다.</summary>
        public GoalKind GoalKind { get; private set; } = GoalKind.House;

        /// <summary>현재 목표의 유용도 점수입니다. 디버그 표시에 씁니다.</summary>
        public float GoalScore => _currentGoalScore;

        /// <summary>병력이 모두 사라져 해산했는지 여부입니다.</summary>
        public bool IsDisbanded { get; private set; }

        // ====================================================================================================
        // 4. Unity Lifecycle
        // ====================================================================================================

        private void Update()
        {
            if (IsDisbanded || _context == null)
            {
                return;
            }

            PruneDeadUnits();

            if (_units.Count == 0)
            {
                Disband();
                return;
            }

            float deltaTime = UnityEngine.Time.deltaTime;

            _replanTimer -= deltaTime;
            if (_replanTimer <= 0f)
            {
                _replanTimer = _context.Tuning.EnemyReplanInterval;
                Replan();
            }

            _motor.Advance(deltaTime, _anchorSpeed, _context.Grid);
            AssignUnitTargets();
        }

        // ====================================================================================================
        // 5. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 분대를 초기화합니다. 상륙정이 병력을 내리기 직전에 호출합니다.
        /// </summary>
        /// <param name="context">전투 컨텍스트입니다.</param>
        /// <param name="beachhead">상륙 지점입니다. 앵커의 시작 위치가 됩니다.</param>
        public void Initialize(BattleContext context, Vector3 beachhead)
        {
            _context = context;

            var coord = context.Grid.WorldToCoord(beachhead);
            _motor.Teleport(beachhead, coord);

            // 분대마다 재판단 시점을 흩뜨립니다. 안 그러면 모든 분대가 같은 프레임에 A*를 돌립니다.
            _replanTimer = Random.Range(0f, ReplanJitter);
        }

        /// <summary>
        /// 병사를 분대에 넣습니다. 상륙정이 한 명씩 내릴 때마다 호출합니다.
        /// </summary>
        public void AddUnit(Unit unit)
        {
            if (unit == null || IsDisbanded)
            {
                return;
            }

            _units.Add(unit);
            unit.transform.SetParent(transform, worldPositionStays: true);

            // 가장 느린 병사에 앵커 속도를 맞춥니다. 빠른 쪽에 맞추면 대열이 길게 늘어집니다.
            float speed = unit.Definition != null ? unit.Definition.MoveSpeed : 3f;
            float candidate = speed * _context.Tuning.AnchorSpeedFactor;

            _anchorSpeed = _units.Count == 1 ? candidate : Mathf.Min(_anchorSpeed, candidate);

            // 새 병력이 합류하면 목표를 즉시 다시 봅니다. 병력이 늘면 판단이 달라질 수 있습니다.
            _replanTimer = 0f;
        }

        // ====================================================================================================
        // 6. Private Methods - Planning
        // ====================================================================================================

        /// <summary>
        /// 목표를 다시 고르고 경로를 잡습니다.
        ///
        /// 목표가 바뀌려면 새 목표가 현재 목표보다 <b>눈에 띄게</b> 나아야 합니다.
        /// 점수가 조금만 뒤집혀도 방향을 트는 AI는 제자리에서 갈팡질팡하는 것처럼 보입니다.
        /// </summary>
        private void Replan()
        {
            var from = _context.Grid.WorldToCoord(_motor.Anchor);

            BuildCandidates();

            if (_candidates.Count == 0)
            {
                return;
            }

            var threat = _context.GetThreatMap(Team.Player);
            var weights = ReadWeights();

            if (!_planner.TrySelectGoal(from, _candidates, _context.Grid, threat, weights, out var best, out float score))
            {
                return;
            }

            bool sameGoal = _motor.Destination.IsValid && best.Coord == _motor.Destination;

            // 이미 그 목표로 가는 중이면 경로만 유지합니다.
            if (sameGoal)
            {
                _currentGoalScore = score;
                return;
            }

            // 이동 중이라면, 바꿀 만큼 나은지 확인합니다.
            if (!_motor.HasArrived && score < _currentGoalScore + _context.Tuning.EnemyGoalSwitchMargin)
            {
                return;
            }

            TryPathTo(from, best, score);
        }

        /// <summary>
        /// 목표 후보를 모읍니다. 가옥 전부와 플레이어 유닛이 서 있는 칸입니다.
        /// </summary>
        private void BuildCandidates()
        {
            _candidates.Clear();

            var houses = _context.Grid.HouseTiles;
            for (int i = 0; i < houses.Count; i++)
            {
                _candidates.Add(new GoalCandidate(houses[i].Coord, GoalKind.House));
            }

            var players = _context.PlayerUnits;
            for (int i = 0; i < players.Count; i++)
            {
                var unit = players[i];
                if (unit == null || !unit.IsAlive)
                {
                    continue;
                }

                var coord = _context.Grid.WorldToCoord(unit.Position);
                var tile = _context.Grid.GetTile(coord);

                if (tile != null && tile.IsWalkable)
                {
                    _candidates.Add(new GoalCandidate(coord, GoalKind.PlayerSquad));
                }
            }

            // 가옥도 플레이어도 없으면 갈 곳이 없습니다. 상륙 지점에 머뭅니다.
        }

        private EnemyGoalPlanner.Weights ReadWeights()
        {
            var tuning = _context.Tuning;

            return new EnemyGoalPlanner.Weights
            {
                Proximity = tuning.AiProximityWeight,
                Value = tuning.AiValueWeight,
                Undefended = tuning.AiUndefendedWeight,
                OpenGround = tuning.AiOpenGroundWeight,
            };
        }

        /// <summary>
        /// 선택한 목표로 가는 경로를 잡습니다. 다른 적 분대가 찜한 칸은 피해 보정합니다.
        /// </summary>
        private void TryPathTo(GridCoord from, GoalCandidate goal, float score)
        {
            var startCoord = from;
            var startTile = _context.Grid.GetTile(startCoord);

            // 아직 상륙정 위이거나 물 위에 있는 경우입니다.
            if (startTile == null || !startTile.IsWalkable)
            {
                var nearest = _context.Grid.FindNearestWalkable(_motor.Anchor);
                if (nearest == null)
                {
                    return;
                }

                startCoord = nearest.Coord;
            }

            if (!_context.EnemyOccupancy.TryResolveDestination(goal.Coord, this, _context.Grid, out var destination))
            {
                return;
            }

            if (!_context.Pathfinder.TryFindPathSnapped(startCoord, destination, _path, out var resolved))
            {
                return;
            }

            _context.EnemyOccupancy.Claim(resolved, this);

            _motor.SetPath(_path, resolved);
            GoalKind = goal.Kind;
            _currentGoalScore = score;
        }

        // ====================================================================================================
        // 7. Private Methods - Execution
        // ====================================================================================================

        /// <summary>
        /// 병사들에게 향할 지점을 배정합니다. 플레이어 분대와 같은 규칙입니다.
        /// 이동 중에는 전원이 앵커를 향하는 느슨한 무리이고, 도착한 뒤에야 동심원 진형을 잡습니다.
        /// </summary>
        private void AssignUnitTargets()
        {
            int count = _units.Count;
            if (count == 0)
            {
                return;
            }

            if (!_motor.HasArrived)
            {
                for (int i = 0; i < count; i++)
                {
                    _units[i]?.SetSlotTarget(_motor.Anchor);
                }

                return;
            }

            FormationSolver.SolveRings(_motor.Anchor, count, _context.Tuning.FormationSpacing, _slots);

            for (int i = 0; i < count; i++)
            {
                int slotIndex = Mathf.Min(i, _slots.Count - 1);
                _units[i]?.SetSlotTarget(_slots[slotIndex]);
            }
        }

        private void PruneDeadUnits()
        {
            for (int i = _units.Count - 1; i >= 0; i--)
            {
                var unit = _units[i];
                if (unit == null || !unit.IsAlive)
                {
                    _units.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 병력이 전멸해 분대를 해산합니다. 점유하던 칸을 놓아 주어야 다른 분대가 쓸 수 있습니다.
        /// </summary>
        private void Disband()
        {
            if (IsDisbanded)
            {
                return;
            }

            IsDisbanded = true;
            _context?.EnemyOccupancy.Release(this);

            Destroy(gameObject);
        }
    }
}
