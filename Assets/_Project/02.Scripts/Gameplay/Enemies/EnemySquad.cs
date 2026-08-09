using System;
using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
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

        /// <summary>슬롯 배정을 다시 짜는 주기(초)입니다.</summary>
        private const float AssignmentInterval = 0.35f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly List<Unit> _units = new List<Unit>(8);
        private readonly List<Vector3> _slots = new List<Vector3>(8);
        private readonly List<GridCoord> _path = new List<GridCoord>(64);
        private readonly List<GoalCandidate> _candidates = new List<GoalCandidate>(16);
        private readonly List<Vector3> _positionBuffer = new List<Vector3>(8);

        /// <summary>목표 후보를 모을 때 쓰는 적 부대 중심 버퍼입니다. 매 판단마다 다시 채웁니다.</summary>
        private readonly List<Vector3> _anchorBuffer = new List<Vector3>(8);

        /// <summary>병사별로 맡은 슬롯 인덱스입니다. <c>_units</c>와 같은 순서입니다.</summary>
        private readonly List<int> _slotOf = new List<int>(8);

        private float _assignmentTimer;

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
        /// 분대를 만들고 전개 지점에 병력을 세웁니다.
        ///
        /// <b>상륙정이 한 명씩 내려놓던 자리입니다.</b>
        /// 야전에서는 부대가 이미 편성된 채로 전장에 들어서므로,
        /// 플레이어 분대(<see cref="Squad.Initialize"/>)와 똑같이 한 번에 세웁니다.
        /// </summary>
        /// <param name="context">전투 컨텍스트입니다.</param>
        /// <param name="definition">병사 정의입니다.</param>
        /// <param name="deployCoord">전개 타일입니다. 앵커의 시작 위치가 됩니다.</param>
        /// <param name="soldierCount">병사 수입니다.</param>
        /// <param name="unitFactory">유닛을 만드는 함수입니다.</param>
        /// <param name="rank">분대 숙련도입니다.</param>
        public void Initialize(
            BattleContext context,
            UnitDefinition definition,
            GridCoord deployCoord,
            int soldierCount,
            Func<UnitDefinition, Team, bool, Vector3, Unit> unitFactory,
            int rank = CombatConstants.MinRank)
        {
            _context = context;

            Vector3 anchor = context.Grid.CoordToWorld(deployCoord);
            _motor.Teleport(anchor, deployCoord);

            _anchorSpeed = definition.MoveSpeed * context.Tuning.AnchorSpeedFactor;

            // 명부에 스스로 올립니다. 이게 없으면 표시하는 쪽이 씬 전체를 훑어야 합니다.
            context.RegisterEnemySquad(this);

            // 전개 칸을 점유해 두어야 다른 적 분대가 그 위로 겹쳐 서지 않습니다.
            context.EnemyOccupancy.Claim(deployCoord, this);

            int total = Mathf.Max(1, soldierCount);

            // 플레이어보다 넓게 섭니다 — 질서 대 혼돈의 대비가 첫 화면부터 보여야 합니다.
            float spacing = context.Tuning.FormationSpacing
                            * Mathf.Max(0.1f, context.Tuning.EnemyFormationLooseness);

            FormationSolver.SolveRings(anchor, total, spacing, _slots);
            FormationSolver.ClampToWalkable(context.Grid, anchor, _slots);

            for (int i = 0; i < total; i++)
            {
                var unit = unitFactory(definition, Team.Enemy, false, _slots[i]);
                if (unit == null)
                {
                    continue;
                }

                unit.transform.SetParent(transform, worldPositionStays: true);
                unit.SetRank(rank);

                _units.Add(unit);
            }

            // 분대마다 재판단 시점을 흩뜨립니다. 안 그러면 모든 분대가 같은 프레임에 A*를 돌립니다.
            _replanTimer = UnityEngine.Random.Range(0f, ReplanJitter);
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
        /// 목표 후보를 모읍니다. <b>서 있는 적 부대의 중심</b>입니다.
        ///
        /// 예전에는 살아 있는 적 병사가 선 칸을 전부 올렸습니다. 그러면 분대 하나가
        /// 인원수만큼 후보를 만들어, 같은 답을 여러 번 내느라 판단 비용이 인원에 비례했습니다.
        /// 부대가 판단의 단위이므로 후보도 부대여야 합니다.
        ///
        /// 모으는 규칙 자체는 <see cref="EnemyGoalPlanner.CollectCandidates"/>에 있습니다.
        /// MonoBehaviour 밖에 두어야 EditMode에서 직접 검증됩니다.
        /// </summary>
        private void BuildCandidates()
        {
            _anchorBuffer.Clear();

            var squads = _context.PlayerSquads;
            for (int i = 0; i < squads.Count; i++)
            {
                var squad = squads[i];

                // 무너진 분대는 오브젝트가 사라지기 전 한 프레임 동안 명부에 남아 있습니다.
                if (squad == null || squad.IsDestroyed)
                {
                    continue;
                }

                _anchorBuffer.Add(squad.AnchorPosition);
            }

            EnemyGoalPlanner.CollectCandidates(_anchorBuffer, _context.Grid, _candidates);

            // 칠 부대가 없으면 갈 곳이 없습니다. 그 자리에 머뭅니다.
        }

        private EnemyGoalPlanner.Weights ReadWeights()
        {
            var tuning = _context.Tuning;

            return new EnemyGoalPlanner.Weights
            {
                Proximity = tuning.AiProximityWeight,
                Isolation = tuning.AiIsolationWeight,
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

            if (!_context.Pathfinder.TryFindSmoothedPathSnapped(startCoord, destination, _path, out var resolved))
            {
                return;
            }

            _context.EnemyOccupancy.Claim(resolved, this);

            _motor.SetPath(_path, resolved);
            _currentGoalScore = score;
        }

        // ====================================================================================================
        // 7. Private Methods - Execution
        // ====================================================================================================

        /// <summary>
        /// 병사들에게 향할 지점을 배정합니다.
        ///
        /// <b>플레이어 분대와 일부러 다르게 만듭니다.</b>
        ///
        /// 이 게임의 시각적 뼈대는 "질서 대 혼돈"입니다. 플레이어는 격자 위에서 정연하게 움직이고,
        /// 침략자는 그리드를 무시하고 사방에서 각기 다른 각도로 들이닥칩니다.
        /// 그 대비가 곧 "누가 지키고 누가 쳐들어오는가"를 말해 줍니다.
        ///
        /// 그런데 적 분대를 플레이어와 같은 구조로 만들면서 그 대비가 사라졌습니다.
        /// 같은 동심원, 같은 간격, 같은 경로라 화면에는 정연한 두 무리가 마주 설 뿐이었습니다.
        ///
        /// 그렇다고 진형을 통째로 뺄 수는 없습니다. 응집이 없으면 하나씩 도착해 방어가 너무 쉬워집니다.
        /// <b>뭉치되 줄 서지 않게</b> — 간격을 넓히고 각자에게 고정된 어긋남을 줍니다.
        /// </summary>
        private void AssignUnitTargets()
        {
            int count = _units.Count;
            if (count == 0)
            {
                return;
            }

            var tuning = _context.Tuning;

            // 돌격 중에는 슬롯을 주지 않습니다. 전원이 앵커로 몰려가는 무리입니다.
            if (!_motor.HasArrived)
            {
                for (int i = 0; i < count; i++)
                {
                    var unit = _units[i];
                    if (unit == null)
                    {
                        continue;
                    }

                    // 앵커 한 점으로 몰리면 서로 밀어내느라 뭉개집니다.
                    // 각자 조금씩 다른 지점을 향하게 해 자연스러운 무리로 흘러가게 합니다.
                    unit.SetSlotTarget(_motor.Anchor + ScatterFor(unit, tuning.EnemyChargeScatter));
                }

                return;
            }

            // 자리를 잡을 때도 플레이어보다 넓게 섭니다.
            float spacing = tuning.FormationSpacing * Mathf.Max(0.1f, tuning.EnemyFormationLooseness);

            FormationSolver.SolveRings(_motor.Anchor, count, spacing, _slots);
            FormationSolver.ClampToWalkable(_context.Grid, _motor.Anchor, _slots);

            // 가까운 병사에게 자리를 줍니다. 순서대로 나눠 주면 대열을 가로질러 걸어갑니다.
            RefreshAssignment(count);

            for (int i = 0; i < count; i++)
            {
                var unit = _units[i];
                if (unit == null || i >= _slotOf.Count)
                {
                    continue;
                }

                int slotIndex = Mathf.Clamp(_slotOf[i], 0, _slots.Count - 1);
                unit.SetSlotTarget(_slots[slotIndex] + ScatterFor(unit, tuning.EnemyFormationJitter));
            }
        }

        /// <summary>
        /// 이 병사가 늘 갖는 어긋남입니다.
        ///
        /// 태어날 때 정해진 고정 씨앗에서 만들어 내므로 매 프레임 같은 값이 나옵니다.
        /// 무작위를 매번 새로 뽑으면 병사가 제자리에서 부들부들 떱니다.
        /// </summary>
        private static Vector3 ScatterFor(Unit unit, float radius)
        {
            return FormationScatter.Offset(unit.VisualSeed, radius);
        }

        /// <summary>
        /// 병사와 슬롯의 짝을 다시 짭니다. 매 프레임 짜면 병사들이 자리를 두고 떱니다.
        /// </summary>
        private void RefreshAssignment(int count)
        {
            _assignmentTimer -= UnityEngine.Time.deltaTime;

            if (_slotOf.Count == count && _assignmentTimer > 0f)
            {
                return;
            }

            _assignmentTimer = AssignmentInterval;

            _positionBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                _positionBuffer.Add(_units[i] != null ? _units[i].Position : _motor.Anchor);
            }

            SlotAssigner.AssignNearest(_positionBuffer, _slots, _slotOf);
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
            _context?.UnregisterEnemySquad(this);

            Destroy(gameObject);
        }

        /// <summary>
        /// 해산을 거치지 않고 사라지는 경우(씬 전환 등)에도 명부를 정리합니다.
        /// </summary>
        private void OnDestroy()
        {
            _context?.UnregisterEnemySquad(this);
        }
    }
}
