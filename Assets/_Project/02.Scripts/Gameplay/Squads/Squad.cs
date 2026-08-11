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
    public sealed class Squad : MonoBehaviour, ISquadStatus, ISquadTally
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>병사 명부와 슬롯 배정입니다. 적 분대와 <b>같은 장부</b>를 씁니다.</summary>
        private readonly SquadMembers _members = new SquadMembers(12);

        /// <summary>앵커가 따라갈 경로입니다.</summary>
        private readonly List<GridCoord> _path = new List<GridCoord>(64);
        /// <summary>이번 진형의 슬롯 위치입니다.</summary>
        private readonly List<Vector3> _slots = new List<Vector3>(12);

        /// <summary>전열이 향할 방향을 다시 살피기까지 남은 시간입니다.</summary>
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

        /// <summary>분대가 볼 수 있는 것만 담긴 컨텍스트입니다.</summary>
        private ISquadContext<Squad> _context;
        /// <summary>지휘관(깃발병)입니다. 쓰러지면 분대가 영구 소멸합니다.</summary>
        private Unit _commander;
        /// <summary>앵커가 전진하는 속도입니다. 병사 이동 속도에서 유도합니다.</summary>
        private float _anchorSpeed = 3f;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>분대 소속 병사 목록입니다. 지휘관도 포함됩니다.</summary>
        public IReadOnlyList<Unit> Units => _members.Units;

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

        /// <summary>이 분대가 무기 계열마다 쌓은 숙련도입니다. 소속 병사 전원이 공유합니다.</summary>
        public WeaponProficiency Proficiency { get; private set; }

        /// <summary>이 분대가 이번 판에 명중시킨 타격 수입니다.</summary>
        public int HitsLanded { get; private set; }

        /// <summary>생존한 병사 수입니다.</summary>
        public int AliveCount => _members.Count;

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

            _members.PruneDead();

            if (_commander == null || !_commander.IsAlive || _members.Count == 0)
            {
                DestroySquad();
                return;
            }

            _facingScanTimer -= deltaTime;

            AdvanceAnchor(deltaTime);
            AssignUnitTargets();

            // 자리를 정한 <b>다음에</b> 병사를 돌립니다. 이 순서가 규칙입니다 —
            // 반대로 돌면 병사가 한 프레임 늦은 자리를 향해 걷습니다.
            _members.Tick(deltaTime);

            // 상태는 병사가 움직인 결과를 보고 정합니다. 앞에 두면 HUD가 늘 한 프레임 뒤처집니다.
            UpdateState();
        }

        // ====================================================================================================
        // 6. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 분대를 생성하고 병사들을 배치합니다.
        /// </summary>
        /// <param name="context">
        /// <b>분대가 볼 수 있는 것만</b> 담긴 컨텍스트입니다.
        /// 전군 명부도 투사체 풀도 적 진영의 점유 장부도 여기 없습니다.
        /// </param>
        /// <param name="definition">병사 정의입니다.</param>
        /// <param name="spawnCoord">초기 배치 타일입니다.</param>
        /// <param name="soldierCount">지휘관을 제외한 병사 수입니다.</param>
        /// <param name="displayName">HUD에 표시할 이름입니다.</param>
        /// <param name="unitFactory">유닛 GameObject를 만드는 함수입니다.</param>
        /// <param name="rank">분대 단련도 랭크입니다.</param>
        /// <param name="proficiency">무기 계열별 숙련도입니다. 비우면 미숙 상태로 섭니다.</param>
        public void Initialize(
            ISquadContext<Squad> context,
            UnitDefinition definition,
            GridCoord spawnCoord,
            int soldierCount,
            string displayName,
            Func<UnitDefinition, Team, bool, Vector3, Unit> unitFactory,
            int rank = CombatConstants.MinRank,
            WeaponProficiency proficiency = default)
        {
            _context = context;
            DisplayName = displayName;
            Rank = Mathf.Clamp(rank, CombatConstants.MinRank, CombatConstants.MaxRank);
            Proficiency = proficiency;

            _motor.Teleport(context.Grid.CoordToWorld(spawnCoord), spawnCoord);
            _anchorSpeed = definition.MoveSpeed * context.Tuning.Squad.AnchorSpeedFactor;

            // 초기 배치 칸도 점유해 두어야 다른 분대가 그 위로 명령받지 않습니다.
            context.Occupancy.Claim(spawnCoord, this);

            // 명부에 스스로 오릅니다. 지원군이 "지금 몇 분대가 서 있는가"를 여기서 셉니다.
            context.RegisterSquad(this);

            // 배치되자마자 해안을 봅니다. 첫 갱신을 기다리면 한순간 엉뚱한 쪽을 보고 서 있습니다.
            _lineFacing = ComputeFacing();
            _formationFacing = _lineFacing;
            _hasFormedUp = true;

            int total = Mathf.Max(1, soldierCount) + 1; // 지휘관 1명 포함
            FormationSolver.SolveRings(_motor.Anchor, total, context.Tuning.Squad.FormationSpacing, _slots);

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
                unit.SetTraining(Rank, Proficiency);
                unit.AttachTally(this);
                unit.Died += OnUnitDied;
                _members.Add(unit);

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
        /// <param name="target">가고자 하는 타일입니다. 점유됐거나 통행 불가면 가장 가까운 빈 칸으로 보정합니다.</param>
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

        /// <inheritdoc />
        public void ReportHitLanded()
        {
            HitsLanded++;
        }

        /// <summary>
        /// 분대 랭크를 올립니다. 소속 병사 전원에게 즉시 반영됩니다.
        /// </summary>
        /// <param name="rank">올릴 숙련도입니다. 허용 범위를 벗어나면 잘립니다.</param>
        public void PromoteTo(int rank)
        {
            Retrain(rank, Proficiency);
        }

        /// <summary>
        /// 단련도와 숙련도를 함께 갱신하고 소속 병사 전원에게 반영합니다.
        /// </summary>
        /// <param name="rank">새 단련도입니다. 허용 범위를 벗어나면 잘립니다.</param>
        /// <param name="proficiency">새 무기 숙련도입니다.</param>
        public void Retrain(int rank, in WeaponProficiency proficiency)
        {
            Rank = Mathf.Clamp(rank, CombatConstants.MinRank, CombatConstants.MaxRank);
            Proficiency = proficiency;

            for (int i = 0; i < _members.Count; i++)
            {
                _members[i]?.SetTraining(Rank, Proficiency);
            }
        }

        // ====================================================================================================
        // 7. Private Methods - Lifecycle
        // ====================================================================================================

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
            _context?.UnregisterSquad(this);

            for (int i = _members.Count - 1; i >= 0; i--)
            {
                var unit = _members[i];
                if (unit != null && unit.IsAlive)
                {
                    unit.Died -= OnUnitDied;
                    unit.Kill();
                }
            }

            _members.Clear();
            _commander = null;

            SquadDestroyed?.Invoke(this);
            Destroy(gameObject);
        }

        /// <summary>
        /// 소멸을 거치지 않고 사라지는 경우(씬 전환 등)에도 명부를 정리합니다.
        /// </summary>
        private void OnDestroy()
        {
            _context?.UnregisterSquad(this);
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
            int count = _members.Count;
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
                _members[i]?.SetIdleFacing(lookDirection);
            }

            if (!HasArrived)
            {
                for (int i = 0; i < count; i++)
                {
                    _members[i]?.SetSlotTarget(_motor.Anchor);
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

            _members.ReassignSlots(
                _slots,
                _context.Tuning.Squad.AssignmentInterval,
                UnityEngine.Time.deltaTime,
                _motor.Anchor);

            for (int i = 0; i < count; i++)
            {
                var unit = _members[i];

                if (unit != null && _members.TryGetSlot(i, _slots, out Vector3 slot))
                {
                    unit.SetSlotTarget(slot);
                }
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
            float spacing = _context.Tuning.Squad.FormationSpacing;

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
            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i] != null)
                {
                    return _members[i].PrefersLineFormation;
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
                _facingScanTimer = _context.Tuning.Squad.FacingScanInterval;
                _lineFacing = ComputeFacing();
            }

            return _lineFacing;
        }

        private Vector3 ComputeFacing()
        {
            Vector3 anchor = _motor.Anchor;

            var enemy = _context.FindNearestEnemy(anchor, Team.Player, _context.Tuning.Shield.ThreatRadius);
            if (enemy != null && TryResolveApproachFacing(anchor, enemy, out Vector3 approach))
            {
                return approach;
            }

            // 적이 안 보이면 전장 중심을 봅니다.
            //
            // 예전에는 해안을 봤습니다. 침공이 언제나 바다에서 왔기 때문입니다.
            // 야전에서는 양측이 마주 보고 들어서므로, 서로가 <b>가운데를 사이에 두고</b> 섭니다.
            // 각자 중심을 보면 그대로 상대를 보게 됩니다.
            Vector3 toCenter = _context.Grid.WorldCenter - anchor;
            toCenter.y = 0f;

            if (toCenter.sqrMagnitude > 0.0001f)
            {
                return toCenter.normalized;
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
            float contactRange = _context.Grid.CellSize * _context.Tuning.Squad.ContactRangeTiles;

            Vector3 arrival = AimPredictor.PredictApproachPoint(
                anchor,
                enemy.Position,
                enemy.Velocity,
                contactRange,
                extraLeadSeconds: 0f,
                maxLeadSeconds: _context.Tuning.Squad.FacingLeadSeconds);

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
        /// 분대 상태를 갱신합니다. HUD 표시와 이후 AI 판단의 입력이 됩니다.
        /// </summary>
        private void UpdateState()
        {
            bool anyEngaged = false;
            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i] != null && _members[i].IsEngaged)
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
