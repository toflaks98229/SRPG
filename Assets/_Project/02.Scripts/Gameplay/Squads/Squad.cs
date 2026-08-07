using System;
using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Units;
using SRPG.Systems.Combat;
using SRPG.Systems.Formation;
using UnityEngine;

namespace SRPG.Gameplay.Squads
{
    /// <summary>
    /// 플레이어가 지휘하는 분대입니다. 병사 여럿과 지휘관(깃발병) 하나로 구성됩니다.
    ///
    /// 손실 구조가 두 층입니다.
    ///   · 병사 사망 → 분대는 유지되고 충원 가능 (회복 가능한 손실)
    ///   · 지휘관 사망 → 분대 전체가 영구 소멸 (회복 불가능한 손실)
    ///
    /// <b>진형 규칙</b>
    /// 이동 중에는 진형을 잡지 않습니다. 전원이 앵커를 향해 몰려가는 느슨한 무리로 움직이고,
    /// 목적지에 <b>도착한 뒤에야</b> 방향 없는 동심원 진형으로 자리를 잡습니다.
    ///
    /// 이동 중에 진형을 유지시키면 두 가지가 망가집니다.
    ///   · 뒤쪽 병사가 앞쪽 자리를 맞추느라 옆걸음질하면서 행군이 느려지고 부자연스러워집니다
    ///   · 진형 방향이 계속 회전해 병사들이 제자리에서 도는 것처럼 보입니다
    /// 실제로 병력이 대열을 갖추는 것은 자리를 잡고 난 다음입니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Squad : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>슬롯 배정을 다시 짜는 주기(초)입니다. 매 프레임 짜면 병사들이 자리를 두고 떱니다.</summary>
        private const float AssignmentInterval = 0.35f;

        /// <summary>전열이 향할 방향을 다시 살피는 주기(초)입니다.</summary>
        private const float FacingScanInterval = 0.4f;

        /// <summary>적이 분대에 닿았다고 보는 거리(칸)입니다. 접근 예측의 목표 지점이 됩니다.</summary>
        private const float ContactRangeTiles = 1.5f;

        /// <summary>방향 예측의 상한(초)입니다. 너무 길면 먼 적의 진로에 전열이 끌려다닙니다.</summary>
        private const float FacingLeadSeconds = 1.5f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly List<Unit> _units = new List<Unit>(12);
        private readonly List<GridCoord> _path = new List<GridCoord>(64);
        private readonly List<Vector3> _slots = new List<Vector3>(12);
        private readonly List<Vector3> _positionBuffer = new List<Vector3>(12);

        /// <summary>병사별로 맡은 슬롯 인덱스입니다. <c>_units</c>와 같은 순서입니다.</summary>
        private readonly List<int> _slotOf = new List<int>(12);

        private float _assignmentTimer;
        private float _facingScanTimer;
        /// <summary>
        /// 병사들이 <b>고개를 돌릴</b> 방향입니다. 계속 갱신되며 슬롯에는 영향을 주지 않습니다.
        /// </summary>
        private Vector3 _lineFacing = Vector3.forward;

        /// <summary>
        /// 진형이 세워진 방향입니다. 도착할 때 한 번 정해지고 그 자리에 머무는 동안 바뀌지 않습니다.
        ///
        /// 시선과 나눠 둔 이유가 있습니다. 이 값이 바뀌면 슬롯이 전부 옮겨지고
        /// 병사들이 자리를 다시 찾아 걸어갑니다. 그 이동이 곧 재정렬 비용이고,
        /// 창병에게는 "이동 중 공격 불가"로 이어져 아예 못 싸우게 됩니다.
        /// </summary>
        private Vector3 _formationFacing = Vector3.forward;

        /// <summary>이번 정지 지점에서 진형을 이미 세웠는지 여부입니다.</summary>
        private bool _hasFormedUp;

        /// <summary>앵커 전진은 적 분대와 공유하는 순수 로직입니다.</summary>
        private readonly FormationMotor _motor = new FormationMotor();

        private BattleContext _context;
        private Unit _commander;
        private float _anchorSpeed = 3f;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>분대 소속 병사 목록입니다. 지휘관도 포함됩니다.</summary>
        public IReadOnlyList<Unit> Units => _units;

        /// <summary>지휘관(깃발병)입니다. 사망 시 분대가 소멸합니다.</summary>
        public Unit Commander => _commander;

        /// <summary>현재 분대 상태입니다.</summary>
        public SquadState State { get; private set; } = SquadState.Idle;

        /// <summary>분대 진형의 중심 월드 좌표입니다.</summary>
        public Vector3 AnchorPosition => _motor.Anchor;

        /// <summary>분대가 명령받은 목적지 좌표입니다.</summary>
        public GridCoord OrderedCoord => _motor.Destination;

        /// <summary>분대의 표시 이름입니다.</summary>
        public string DisplayName { get; private set; } = "분대";

        /// <summary>
        /// 분대의 숙련도 랭크입니다. 소속 병사 전원이 같은 값을 갖습니다.
        /// 궁수 분대의 명중률이 이 값에 직접 좌우됩니다.
        /// </summary>
        public int Rank { get; private set; } = CombatConstants.MinRank;

        /// <summary>생존한 병사 수입니다.</summary>
        public int AliveCount => _units.Count;

        /// <summary>분대가 소멸했는지 여부입니다.</summary>
        public bool IsDestroyed => State == SquadState.Destroyed;

        /// <summary>
        /// 명령받은 지점에 도착했는지 여부입니다.
        /// 진형을 잡을지 말지를 가르는 유일한 기준입니다.
        /// </summary>
        public bool HasArrived => _motor.HasArrived;

        // ====================================================================================================
        // 4. Events
        // ====================================================================================================

        /// <summary>지휘관 사망으로 분대가 소멸했을 때 발생합니다.</summary>
        public event Action<Squad> SquadDestroyed;

        // ====================================================================================================
        // 5. Unity Lifecycle
        // ====================================================================================================

        private void Update()
        {
            if (State == SquadState.Destroyed || _context == null)
            {
                return;
            }

            float deltaTime = UnityEngine.Time.deltaTime;

            PruneDeadUnits();

            if (_commander == null || !_commander.IsAlive || _units.Count == 0)
            {
                DestroySquad();
                return;
            }

            _facingScanTimer -= deltaTime;

            AdvanceAnchor(deltaTime);
            AssignUnitTargets();
            UpdateState();
        }

        // ====================================================================================================
        // 6. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 분대를 생성하고 병사들을 배치합니다.
        /// </summary>
        /// <param name="context">전투 컨텍스트입니다.</param>
        /// <param name="definition">병사 정의입니다.</param>
        /// <param name="spawnCoord">초기 배치 타일입니다.</param>
        /// <param name="soldierCount">지휘관을 제외한 병사 수입니다.</param>
        /// <param name="displayName">HUD에 표시할 이름입니다.</param>
        /// <param name="unitFactory">유닛 GameObject를 만드는 함수입니다.</param>
        /// <param name="rank">분대 숙련도 랭크입니다.</param>
        public void Initialize(
            BattleContext context,
            UnitDefinition definition,
            GridCoord spawnCoord,
            int soldierCount,
            string displayName,
            Func<UnitDefinition, Team, bool, Vector3, Unit> unitFactory,
            int rank = CombatConstants.MinRank)
        {
            _context = context;
            DisplayName = displayName;
            Rank = Mathf.Clamp(rank, CombatConstants.MinRank, CombatConstants.MaxRank);

            _motor.Teleport(context.Grid.CoordToWorld(spawnCoord), spawnCoord);
            _anchorSpeed = definition.MoveSpeed * context.Tuning.AnchorSpeedFactor;

            // 초기 배치 칸도 점유해 두어야 다른 분대가 그 위로 명령받지 않습니다.
            context.Occupancy.Claim(spawnCoord, this);

            // 배치되자마자 해안을 봅니다. 첫 갱신을 기다리면 한순간 엉뚱한 쪽을 보고 서 있습니다.
            _lineFacing = ComputeFacing();
            _formationFacing = _lineFacing;
            _hasFormedUp = true;

            int total = Mathf.Max(1, soldierCount) + 1; // 지휘관 1명 포함
            FormationSolver.SolveRings(_motor.Anchor, total, context.Tuning.FormationSpacing, _slots);

            for (int i = 0; i < total; i++)
            {
                // 슬롯 0번은 진형의 중심입니다. 사방에서 가장 안쪽이라 지휘관 자리로 씁니다.
                bool isCommander = i == 0;

                var unit = unitFactory(definition, Team.Player, isCommander, _slots[i]);
                if (unit == null)
                {
                    continue;
                }

                unit.transform.SetParent(transform, true);
                unit.SetRank(Rank);
                unit.Died += OnUnitDied;
                _units.Add(unit);

                if (isCommander)
                {
                    _commander = unit;
                }
            }

            State = SquadState.Idle;
        }

        /// <summary>
        /// 지정 타일로 이동을 명령합니다. 플레이어의 유일한 조작입니다.
        /// </summary>
        /// <returns>경로를 찾아 명령을 수락했으면 true입니다.</returns>
        public bool IssueMoveOrder(GridCoord target)
        {
            if (State == SquadState.Destroyed || _context == null)
            {
                return false;
            }

            var startCoord = _context.Grid.WorldToCoord(_motor.Anchor);

            // 앵커가 통행 불가 지점에 있으면 가장 가까운 통행 가능 타일에서 출발합니다.
            var startTile = _context.Grid.GetTile(startCoord);
            if (startTile == null || !startTile.IsWalkable)
            {
                var nearest = _context.Grid.FindNearestWalkable(_motor.Anchor);
                if (nearest == null)
                {
                    return false;
                }

                startCoord = nearest.Coord;
            }

            // 한 칸에는 분대 하나만 자리 잡습니다.
            // 이미 다른 분대가 찜한 칸이면 가장 가까운 빈 칸으로 보정합니다.
            if (!_context.Occupancy.TryResolveDestination(target, this, _context.Grid, out var destination))
            {
                Debug.Log($"[Squad] {DisplayName}: {target} 주변에 빈 칸이 없습니다.");
                return false;
            }

            if (!_context.Pathfinder.TryFindSmoothedPathSnapped(startCoord, destination, _path, out var resolved))
            {
                return false;
            }

            // 경로 탐색이 목적지를 또 보정했을 수 있으므로(물·절벽 클릭), 최종 좌표로 다시 확인합니다.
            if (resolved != destination && _context.Occupancy.IsBlockedFor(resolved, this))
            {
                if (!_context.Occupancy.TryResolveDestination(resolved, this, _context.Grid, out resolved) ||
                    !_context.Pathfinder.TryFindSmoothedPath(startCoord, resolved, _path))
                {
                    return false;
                }
            }

            _context.Occupancy.Claim(resolved, this);

            _motor.SetPath(_path, resolved);
            State = SquadState.Moving;
            return true;
        }

        /// <summary>
        /// 현재 이동 명령을 취소하고 제자리에 멈춥니다.
        /// </summary>
        public void StopOrder()
        {
            if (_context == null)
            {
                _motor.Stop(GridCoord.Invalid);
                return;
            }

            // 멈춘 자리를 새 점유 칸으로 삼습니다.
            var here = _context.Grid.WorldToCoord(_motor.Anchor);
            _motor.Stop(here);
            _context.Occupancy.Claim(here, this);
        }

        /// <summary>
        /// 분대 랭크를 올립니다. 소속 병사 전원에게 즉시 반영됩니다.
        /// </summary>
        public void PromoteTo(int rank)
        {
            Rank = Mathf.Clamp(rank, CombatConstants.MinRank, CombatConstants.MaxRank);

            for (int i = 0; i < _units.Count; i++)
            {
                _units[i]?.SetRank(Rank);
            }
        }

        // ====================================================================================================
        // 7. Private Methods - Lifecycle
        // ====================================================================================================

        /// <summary>
        /// 사망한 병사를 목록에서 제거합니다.
        /// </summary>
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

        private void OnUnitDied(Unit unit)
        {
            unit.Died -= OnUnitDied;

            // 지휘관 사망은 다음 Update에서 분대 소멸로 처리합니다.
            // 이벤트 콜백 안에서 다른 유닛을 파괴하면 순회 중 컬렉션이 변하는 문제가 생깁니다.
        }

        /// <summary>
        /// 분대를 영구 소멸시킵니다. 남은 병사도 함께 사라집니다.
        /// </summary>
        private void DestroySquad()
        {
            if (State == SquadState.Destroyed)
            {
                return;
            }

            State = SquadState.Destroyed;

            // 점유하던 칸을 놓아 줍니다. 이걸 빠뜨리면 죽은 분대가 칸을 영원히 막습니다.
            _context?.Occupancy.Release(this);

            for (int i = _units.Count - 1; i >= 0; i--)
            {
                var unit = _units[i];
                if (unit != null && unit.IsAlive)
                {
                    unit.Died -= OnUnitDied;
                    unit.Kill();
                }
            }

            _units.Clear();
            _commander = null;

            SquadDestroyed?.Invoke(this);
            Destroy(gameObject);
        }

        // ====================================================================================================
        // 8. Private Methods - Movement
        // ====================================================================================================

        /// <summary>
        /// 앵커를 경로를 따라 전진시킵니다. 실제 계산은 적 분대와 공유하는 <see cref="FormationMotor"/>가 합니다.
        /// </summary>
        private void AdvanceAnchor(float deltaTime)
        {
            _motor.Advance(deltaTime, _anchorSpeed, _context.Grid);
        }

        /// <summary>
        /// 병사들에게 향할 지점을 배정합니다.
        ///
        /// 이동 중에는 전원이 같은 지점(앵커)을 향합니다. 슬롯을 주지 않는다는 뜻입니다.
        /// 겹침은 분리 조향이 알아서 풀어 주므로, 결과적으로 앵커를 뒤따르는 느슨한 무리가 됩니다.
        ///
        /// 도착한 뒤에야 슬롯을 배정합니다. 이때 지휘관은 항상 중심입니다.
        /// 진형의 모양은 병과가 정합니다. 창은 횡대, 검과 활은 동심원입니다.
        /// </summary>
        private void AssignUnitTargets()
        {
            int count = _units.Count;
            if (count == 0)
            {
                return;
            }

            // 병사 개인이 바라볼 방향입니다. 계속 갱신합니다.
            //
            // <b>회전은 이동이 아닙니다.</b> 제자리에서 고개만 돌리는 것이라
            // 슬롯이 움직이지 않고, 따라서 재정렬 이동도 창병의 공격 불가도 일어나지 않습니다.
            Vector3 lookDirection = ResolveFacing();

            for (int i = 0; i < count; i++)
            {
                _units[i]?.SetIdleFacing(lookDirection);
            }

            if (!HasArrived)
            {
                for (int i = 0; i < count; i++)
                {
                    _units[i]?.SetSlotTarget(_motor.Anchor);
                }

                _hasFormedUp = false;
                return;
            }

            // 도착하는 순간 진형 방향을 <b>한 번만</b> 확정하고, 그 뒤로는 건드리지 않습니다.
            //
            // 매번 다시 잡으면 위협이 조금만 움직여도 전열이 통째로 돌아가고,
            // 슬롯이 전부 옮겨져 병사들이 자리를 다시 찾아 걸어갑니다.
            // 그 이동 때문에 창병은 계속 "이동 중"이 되어 영영 찌르지 못합니다.
            if (!_hasFormedUp)
            {
                _formationFacing = lookDirection;
                _hasFormedUp = true;
            }

            SolveSlots(count, _formationFacing);
            RefreshAssignment(count);

            for (int i = 0; i < count; i++)
            {
                var unit = _units[i];
                if (unit == null || i >= _slotOf.Count)
                {
                    continue;
                }

                int slotIndex = Mathf.Clamp(_slotOf[i], 0, _slots.Count - 1);
                unit.SetSlotTarget(_slots[slotIndex]);
            }
        }

        /// <summary>
        /// 진형 슬롯 위치를 계산합니다. 모양은 병과가 정합니다.
        ///
        /// 창은 정면 좁은 각도만 위험하므로 <b>옆으로 늘어서야</b> 방어선이 됩니다.
        /// 검과 활은 사방에서 오는 위협에 대응해야 하므로 동심원이 낫습니다.
        /// </summary>
        private void SolveSlots(int count, Vector3 facing)
        {
            float spacing = _context.Tuning.FormationSpacing;

            if (PrefersLineFormation())
            {
                FormationSolver.SolveGrid(_motor.Anchor, facing, count, spacing, _slots);
            }
            else
            {
                FormationSolver.SolveRings(_motor.Anchor, count, spacing, _slots);
            }

            // 절벽이나 물에 걸린 슬롯을 안쪽으로 당깁니다.
            // 그러지 않으면 그 자리를 받은 병사가 벽에 붙어 영영 도착하지 못합니다.
            FormationSolver.ClampToWalkable(_context.Grid, _motor.Anchor, _slots);
        }

        /// <summary>이 분대가 횡대를 선호하는지 무기에게 묻습니다.</summary>
        private bool PrefersLineFormation()
        {
            for (int i = 0; i < _units.Count; i++)
            {
                if (_units[i] != null)
                {
                    return _units[i].PrefersLineFormation;
                }
            }

            return false;
        }

        /// <summary>
        /// 분대가 바라볼 방향입니다.
        ///
        /// <b>우선순위</b>
        ///   1. 공격자가 있으면 <b>그들이 다가오는 길</b>을 봅니다
        ///   2. 없으면 <b>해안선</b>을 봅니다. 침공은 언제나 바다에서 오기 때문입니다
        ///   3. 둘 다 없으면 마지막 방향을 유지합니다
        ///
        /// 2번이 기본값인 것이 중요합니다.
        /// 예전에는 적이 없으면 월드 +Z를 보고 서 있었습니다. 아무 의미 없는 방향이라,
        /// 창병 전열이 바다를 등지고 서는 그림이 나왔습니다.
        /// 방어 부대가 아무 일도 없을 때 바다를 보고 서 있는 것은 그 자체로 옳습니다.
        /// </summary>
        private Vector3 ResolveFacing()
        {
            if (_facingScanTimer <= 0f)
            {
                _facingScanTimer = FacingScanInterval;
                _lineFacing = ComputeFacing();
            }

            return _lineFacing;
        }

        private Vector3 ComputeFacing()
        {
            Vector3 anchor = _motor.Anchor;

            var enemy = _context.FindNearestEnemy(anchor, Team.Player, _context.Tuning.ShieldThreatRadius);
            if (enemy != null && TryResolveApproachFacing(anchor, enemy, out Vector3 approach))
            {
                return approach;
            }

            var coast = _context.Grid.FindNearestCoastal(anchor);
            if (coast != null)
            {
                Vector3 toCoast = coast.WorldCenter - anchor;
                toCoast.y = 0f;

                if (toCoast.sqrMagnitude > 0.0001f)
                {
                    return toCoast.normalized;
                }
            }

            return _lineFacing;
        }

        /// <summary>
        /// 공격자가 다가오는 길을 봅니다.
        ///
        /// 지금 서 있는 자리가 아니라 <b>닿을 자리</b>를 겨눕니다.
        /// 옆으로 흘러가는 적을 그대로 따라 보면 전열이 함께 돌아가 옆구리를 내줍니다.
        /// 창병의 조준과 같은 계산을 씁니다.
        /// </summary>
        private bool TryResolveApproachFacing(Vector3 anchor, Unit enemy, out Vector3 facing)
        {
            float contactRange = _context.Grid.CellSize * ContactRangeTiles;

            Vector3 arrival = AimPredictor.PredictApproachPoint(
                anchor,
                enemy.Position,
                enemy.Velocity,
                contactRange,
                extraLeadSeconds: 0f,
                maxLeadSeconds: FacingLeadSeconds);

            facing = arrival - anchor;
            facing.y = 0f;

            if (facing.sqrMagnitude > 0.0001f)
            {
                facing.Normalize();
                return true;
            }

            // 이미 코앞이면 예측이 무의미합니다. 현재 위치를 그대로 봅니다.
            facing = enemy.Position - anchor;
            facing.y = 0f;

            if (facing.sqrMagnitude > 0.0001f)
            {
                facing.Normalize();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 병사와 슬롯의 짝을 다시 짭니다.
        ///
        /// 매 프레임 다시 짜면 미세한 위치 변화로 슬롯이 서로 뒤바뀌며 병사들이 떱니다.
        /// 인원이 바뀌었을 때와 일정 시간마다만 다시 짭니다.
        /// </summary>
        private void RefreshAssignment(int count)
        {
            _assignmentTimer -= UnityEngine.Time.deltaTime;

            bool countChanged = _slotOf.Count != count;

            if (!countChanged && _assignmentTimer > 0f)
            {
                return;
            }

            _assignmentTimer = AssignmentInterval;

            _positionBuffer.Clear();
            int commanderIndex = -1;

            for (int i = 0; i < count; i++)
            {
                var unit = _units[i];
                _positionBuffer.Add(unit != null ? unit.Position : _motor.Anchor);

                if (unit != null && unit.IsCommander)
                {
                    commanderIndex = i;
                }
            }

            SlotAssigner.AssignNearest(_positionBuffer, _slots, _slotOf, commanderIndex);
        }

        /// <summary>
        /// 분대 상태를 갱신합니다. HUD 표시와 이후 AI 판단의 입력이 됩니다.
        /// </summary>
        private void UpdateState()
        {
            bool anyEngaged = false;
            for (int i = 0; i < _units.Count; i++)
            {
                if (_units[i] != null && _units[i].IsEngaged)
                {
                    anyEngaged = true;
                    break;
                }
            }

            if (anyEngaged)
            {
                State = SquadState.Fighting;
            }
            else if (!HasArrived)
            {
                State = SquadState.Moving;
            }
            else
            {
                State = SquadState.Idle;
            }
        }
    }
}
