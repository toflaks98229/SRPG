using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Systems.Grid;
using SRPG.Systems.Motion;
using SRPG.Systems.Pathfinding;
using UnityEngine;
using UnityEngine.AI;

namespace SRPG.Gameplay.Units
{
    /// <summary>
    /// 병사 한 명의 <b>움직임</b>을 담당합니다. 조향을 합성하고, 외력을 얹고, 지형에 부딪혀 확정합니다.
    ///
    /// <b>왜 떼어 놓는가</b>
    ///
    /// 이동은 세 겹입니다 — 어디로 갈지(<see cref="ApproachSolver"/>), 어떻게 갈지(도착·분리 조향),
    /// 그리고 실제로 갈 수 있는지(<see cref="GroundMotion"/>). 여기에 넉백과 도약이 얹힙니다.
    /// 이 전부가 표적 선정·공격 판정과 한 클래스에 있으면, 한 겹을 고칠 때 나머지 전부를 읽어야 합니다.
    ///
    /// 판단 자체는 순수 솔버들이 하고, 이 클래스는 그것들을 잇고 버퍼를 재사용하는 일만 합니다.
    /// </summary>
    public sealed class UnitLocomotion
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>넉백 속도가 초당 얼마나 줄어드는지입니다.</summary>
        public const float KnockbackDecay = 11f;

        /// <summary>도약 속도가 초당 얼마나 줄어드는지입니다. 넉백보다 빨리 잦아듭니다.</summary>
        public const float LungeDecay = 20f;

        /// <summary>유닛 반경 대비 분리 조향이 작용하는 거리의 배수입니다.</summary>
        private const float SeparationRadiusFactor = 2.4f;

        /// <summary>
        /// 물러날 때 잡는 목표 거리입니다(월드 거리).
        ///
        /// 길에 물어보려면 <b>구체적인 자리</b>가 있어야 합니다 — "저쪽으로 계속"은 물어볼 수 없습니다.
        /// 짧게 잡습니다. 위협은 계속 움직이므로 목표도 매 순간 다시 잡히고,
        /// 길게 잡으면 이미 지나간 위협을 피해 계속 달아납니다.
        /// </summary>
        private const float RetreatDistance = 4f;

        /// <summary>병사 한 사람이 차지하는 반경입니다. 서로 비켜 갈 간격을 이 값이 정합니다.</summary>
        private const float BodyRadius = 0.3f;

        /// <summary>병사의 키입니다.</summary>
        private const float BodyHeight = 1.8f;

        /// <summary>최고 속도까지 붙는 데 쓰는 가속의 배수입니다. 크게 잡아 굼뜨지 않게 합니다.</summary>
        private const float AccelerationFactor = 8f;

        /// <summary>이만큼 다가가면 도착으로 봅니다. 0이면 마지막 몇 센티에서 계속 떱니다.</summary>
        private const float StoppingDistance = 0.15f;

        /// <summary>길 위에 세울 때 허용하는 거리입니다.</summary>
        private const float AgentSnapRange = 3f;

        /// <summary>
        /// 비켜 가기 우선순위의 범위입니다. 병사마다 흩뜨립니다.
        ///
        /// 전부 같은 값이면 마주친 둘이 <b>동시에 같은 쪽으로</b> 비키려 하다 서로 막습니다.
        /// 값이 다르면 한쪽이 양보하고 다른 쪽이 지나갑니다.
        /// </summary>
        private const int AvoidancePriorityMin = 30;

        /// <summary>비켜 가기 우선순위의 상한입니다.</summary>
        private const int AvoidancePriorityMax = 70;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        /// <summary>넉백·도약·분리를 채널별로 들고 있는 외력 상태입니다.</summary>
        private readonly ImpulseState _impulses = new ImpulseState();

        /// <summary>이 병사의 길잡이입니다. 길이 없는 판에서는 꺼져 있습니다.</summary>
        private NavMeshAgent _agent;

        /// <summary>분리 조향의 이웃 질의에 쓰는 재사용 버퍼입니다.</summary>
        private readonly List<Unit> _neighborBuffer = new List<Unit>(16);

        /// <summary>이 이동을 소유한 병사입니다.</summary>
        private Unit _owner;
        /// <summary>설 수 있는 자리와 발 높이를 정하는 지형입니다.</summary>
        private IslandGrid _grid;
        /// <summary>이웃을 찾는 공간 질의입니다.</summary>
        private ISpatialQuery _spatial;
        /// <summary>분리 세기와 익사 문턱이 담긴 튜닝입니다.</summary>
        private BattleTuning _tuning;
        /// <summary>병과 정의입니다. 이동 속도와 반경을 읽습니다.</summary>
        private UnitDefinition _definition;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>직전에 적용된 <b>스스로 내는</b> 속도입니다. 예측 사격의 입력이기도 합니다.</summary>
        public Vector3 Velocity { get; private set; }

        // ====================================================================================================
        // 4. Public Methods - Setup
        // ====================================================================================================

        /// <summary>
        /// 필요한 것을 연결하고 외력을 지웁니다. 유닛 초기화 때 부릅니다.
        ///
        /// <b>지형·공간 질의·튜닝, 셋뿐입니다.</b>
        /// 어디에 설 수 있는지(지형), 옆에 누가 있는지(질의), 얼마나 세게 밀어낼지(튜닝).
        /// 경로 탐색기가 없는 것이 중요합니다 — 길을 잡는 것은 분대의 일이고,
        /// 병사는 가리키는 곳으로 조향할 뿐입니다.
        /// </summary>
        /// <param name="owner">이 이동을 소유한 병사입니다. 진영을 읽는 데 씁니다.</param>
        /// <param name="grid">지형입니다. 설 수 있는 자리와 발 높이를 정합니다.</param>
        /// <param name="spatial">이웃을 찾는 공간 질의입니다. 분리 조향의 입력입니다.</param>
        /// <param name="tuning">분리 세기와 익사 문턱이 담긴 튜닝입니다.</param>
        /// <param name="definition">병과 정의입니다. 이동 속도와 반경을 읽습니다.</param>
        public void Configure(
            Unit owner,
            IslandGrid grid,
            ISpatialQuery spatial,
            BattleTuning tuning,
            UnitDefinition definition)
        {
            _owner = owner;
            _grid = grid;
            _spatial = spatial;
            _tuning = tuning;
            _definition = definition;

            Velocity = Vector3.zero;
            _impulses.Reset();

            AttachAgent();
        }

        /// <summary>
        /// 병사에게 길잡이(<see cref="NavMeshAgent"/>)를 붙이고 길 위에 세웁니다.
        ///
        /// <b>왜 에이전트가 직접 움직이는가</b>
        ///
        /// 오래도록 조향이 자리를 정하고 길은 방향만 알려 주었습니다. 그런데 조향에는
        /// 길을 모르는 성분이 섞여 있습니다 — 옆 사람에게서 밀려나는 힘, 넉백, 도약.
        /// 그것들이 병사를 길 밖으로 밀면 남은 것은 <b>어떻게 되돌리느냐</b>뿐이고,
        /// 되돌리는 방법을 몇 가지나 만들어 봤지만 매번 굳는 자리가 남았습니다.
        ///
        /// 에이전트는 <b>길 밖으로 나갈 수가 없습니다.</b> 되돌릴 일이 없으니 굳을 자리도 없습니다.
        /// 서로 비켜 가는 것도 에이전트가 합니다 — 그것이 원래 병사를 밀어내던 힘이었습니다.
        ///
        /// <b>길이 없으면 붙이지 않습니다.</b> 지형 없이 격자만 꽂은 판(자동 검사)이 그렇고,
        /// 그때는 예전 조향이 그대로 돕니다. 붙여 두면 유니티가 매 프레임 경고를 쏟습니다.
        /// </summary>
        private void AttachAgent()
        {
            if (_owner == null)
            {
                return;
            }

            _agent = _owner.GetComponent<NavMeshAgent>();

            if (_agent == null)
            {
                _agent = _owner.gameObject.AddComponent<NavMeshAgent>();
            }

            // 방향과 높이는 우리가 정합니다. 에이전트에 맡기면 시선 규칙(창병의 자세)과
            // 지면 높이 보정이 둘 다 어긋납니다.
            _agent.updateRotation = false;
            _agent.updateUpAxis = false;

            _agent.radius = BodyRadius;
            _agent.height = BodyHeight;
            _agent.baseOffset = 0f;

            _agent.speed = _owner.Stats.MoveSpeed;
            _agent.acceleration = _owner.Stats.MoveSpeed * AccelerationFactor;
            _agent.angularSpeed = 0f;

            // 목적지에 딱 붙지 않습니다. 진형 슬롯은 사람이 설 자리이지 점이 아니고,
            // 0으로 두면 마지막 몇 센티를 두고 계속 미세하게 떱니다.
            _agent.stoppingDistance = StoppingDistance;
            _agent.autoBraking = false;

            // 서로 비켜 가는 것을 에이전트에 맡깁니다. 이것이 예전의 분리 조향을 대신합니다.
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.GoodQualityObstacleAvoidance;
            _agent.avoidancePriority = Random.Range(AvoidancePriorityMin, AvoidancePriorityMax);

            // 길 위에 세우지 못하면 쓰지 않습니다. 억지로 켜 두면 경고만 쏟고 움직이지 않습니다.
            _agent.enabled = NavMesh.SamplePosition(_owner.Position, out var hit, AgentSnapRange, NavMesh.AllAreas);

            if (_agent.enabled)
            {
                _agent.Warp(hit.position);
            }
        }

        /// <summary>에이전트가 이 프레임의 이동을 맡고 있는지 여부입니다.</summary>
        private bool AgentDrives => _agent != null && _agent.enabled && _agent.isOnNavMesh;

        /// <summary>
        /// 길잡이가 길 위에 있는지 확인하고, 벗어났으면 되돌립니다.
        ///
        /// <b>왜 매 프레임 확인하는가</b>
        ///
        /// 길잡이를 붙이는 시점에 길이 아직 없을 수 있습니다. 지금은 전장이 먼저 서고
        /// 병사가 나중에 나오지만, 그 순서에 기대면 <b>순서가 바뀌는 날 조용히 망가집니다</b> —
        /// 병사는 길잡이 없이 예전 조향으로 걷고, 화면에는 아무 표시도 나지 않습니다.
        ///
        /// 여기서 확인하면 순서가 어떻든 늦어도 다음 프레임에 붙습니다.
        /// 이미 붙어 있으면 <c>isOnNavMesh</c> 한 번 읽는 것이 전부입니다.
        /// </summary>
        private void EnsureAgentOnSurface()
        {
            if (_agent == null || AgentDrives || _owner == null)
            {
                return;
            }

            if (!NavMesh.SamplePosition(_owner.Position, out var hit, AgentSnapRange, NavMesh.AllAreas))
            {
                return;
            }

            _agent.enabled = true;
            _agent.Warp(hit.position);
        }


        // ====================================================================================================
        // 5. Public Methods - Impulse
        // ====================================================================================================

        /// <summary>맞아서 밀려납니다. 밀려난 자리가 물이면 익사합니다.</summary>
        /// <param name="impulse">밀어내는 속도입니다.</param>
        public void AddKnockback(Vector3 impulse)
        {
            _impulses.AddKnockback(impulse);
        }

        /// <summary>스스로 앞으로 몸을 던집니다. 물로 밀려나지 않습니다.</summary>
        /// <param name="impulse">앞으로 던지는 속도입니다.</param>
        public void AddLunge(Vector3 impulse)
        {
            _impulses.AddLunge(impulse);
        }

        /// <summary>외력을 감쇠시킵니다.</summary>
        /// <param name="deltaTime">지난 시간입니다.</param>
        public void Decay(float deltaTime)
        {
            _impulses.Decay(deltaTime, KnockbackDecay, LungeDecay);
        }

        // ====================================================================================================
        // 6. Public Methods - Steering
        // ====================================================================================================

        /// <summary>
        /// 이번 프레임의 목표 속도를 계산합니다. 도착 조향과 분리 조향의 합입니다.
        /// </summary>
        /// <param name="order">슬롯·표적·리시·후퇴가 담긴 이번 프레임의 이동 지시입니다.</param>
        /// <param name="position">병사의 현재 위치입니다.</param>
        /// <param name="deltaTime">지난 시간입니다. 분리 성분의 보간에 씁니다.</param>
        /// <returns>최대 이동 속도로 제한된 조향 속도입니다. 후퇴 중에는 제한을 타지 않습니다.</returns>
        public Vector3 Solve(in SteeringOrder order, Vector3 position, float deltaTime)
        {
            EnsureAgentOnSurface();

            Vector3 destination = ResolveDestination(in order, position);

            // <b>길잡이가 있으면 목적지만 넘깁니다.</b>
            //
            // 어느 길로 갈지, 옆 사람을 어떻게 비켜 갈지, 어디서 멈출지는 전부 길잡이가 정합니다.
            // 여기서 다시 정하면 두 벌의 판단이 서로를 밀어내고, 그 다툼이 곧 굳는 자리가 됩니다.
            if (AgentDrives)
            {
                _agent.speed = _owner.Stats.MoveSpeed;
                _agent.isStopped = false;
                _agent.SetDestination(destination);

                // 돌려주는 것은 <b>가려는 속도</b>입니다. 시선과 "움직이는 중인가" 판정이 이것을 봅니다.
                // 실제로 옮기는 것은 길잡이이므로, 여기서 자리를 만지지 않습니다.
                return _agent.desiredVelocity;
            }

            Vector3 velocity = SteeringSolver.Arrive(
                position, destination, _owner.Stats.MoveSpeed, ApproachSolver.ArriveSlowRadius);

            velocity += SolveSeparation(position, deltaTime);

            // 최대 속도를 넘지 않게 제한합니다.
            float maxSpeed = _owner.Stats.MoveSpeed;
            if (velocity.sqrMagnitude > maxSpeed * maxSpeed)
            {
                velocity = velocity.normalized * maxSpeed;
            }

            return velocity;
        }

        /// <summary>
        /// 이번 프레임에 향할 자리를 정합니다.
        ///
        /// 물러나는 중이면 위협의 반대편, 아니면 무기가 정한 접근 지점입니다.
        /// <b>어디로 갈지만 정합니다</b> — 어떻게 갈지는 길잡이의 몫입니다.
        /// </summary>
        /// <param name="order">이번 프레임의 이동 지시입니다.</param>
        /// <param name="position">병사의 현재 위치입니다.</param>
        /// <returns>향할 자리입니다.</returns>
        private Vector3 ResolveDestination(in SteeringOrder order, Vector3 position)
        {
            if (order.Retreating)
            {
                Vector3 away = position - order.RetreatFrom;
                away.y = 0f;

                if (away.sqrMagnitude > 0.0001f)
                {
                    return position + away.normalized * RetreatDistance;
                }
            }

            var plan = ApproachSolver.Resolve(new ApproachRequest(
                position,
                order.SlotTarget,
                order.HasTarget,
                order.TargetPosition,
                _owner.Stats.AttackRange,
                order.LeashDistance));

            return plan.Destination;
        }

        // ====================================================================================================
        // 7. Public Methods - Movement
        // ====================================================================================================

        /// <summary>
        /// 조향과 외력을 합쳐 실제로 설 자리를 정합니다.
        /// </summary>
        /// <param name="position">현재 위치입니다.</param>
        /// <param name="steering">이번 프레임의 조향 속도입니다.</param>
        /// <param name="deltaTime">경과 시간입니다.</param>
        /// <param name="next">확정된 위치입니다. 익사했다면 낙수 지점입니다.</param>
        /// <returns>계속 서 있으면 true, 익사했으면 false입니다.</returns>
        public bool TryStep(Vector3 position, Vector3 steering, float deltaTime, out Vector3 next)
        {
            Velocity = steering;

            if (AgentDrives)
            {
                return StepWithAgent(position, deltaTime, out next);
            }

            Vector3 desired = position + _impulses.CombineWith(steering) * deltaTime;

            var step = GroundMotion.TryStep(
                _grid,
                position,
                desired,
                _impulses.IsPushedFasterThan(_tuning.Unit.DrownKnockbackThreshold),
                out next);

            return step == GroundStep.Moved;
        }

        /// <summary>
        /// 길잡이가 옮기는 프레임의 나머지를 처리합니다 — <b>외력과 익사</b>입니다.
        ///
        /// <b>왜 외력만 따로 다루는가</b>
        ///
        /// 걷는 것은 길잡이가 합니다. 그런데 넉백과 도약은 병사가 <b>가려는</b> 힘이 아니라
        /// 밖에서 가해지는 힘이라, 목적지로 표현할 수가 없습니다.
        /// <c>NavMeshAgent.Move</c> 로 밀면 그 밀림도 길 위에 갇힙니다 —
        /// 밀려나되 길 밖으로는 못 나갑니다.
        ///
        /// <b>익사는 그 전에 판정합니다.</b> 세게 밀려 물로 날아가는 것은 이 게임의 사망 규칙이고,
        /// 길에 갇힌 뒤에 보면 물에 닿는 일이 아예 없어져 규칙이 사라집니다.
        /// </summary>
        /// <param name="position">현재 위치입니다.</param>
        /// <param name="deltaTime">경과 시간입니다.</param>
        /// <param name="next">확정된 위치입니다. 익사했다면 낙수 지점입니다.</param>
        /// <returns>계속 서 있으면 true, 익사했으면 false입니다.</returns>
        private bool StepWithAgent(Vector3 position, float deltaTime, out Vector3 next)
        {
            Vector3 push = _impulses.CombineWith(Vector3.zero);

            if (push.sqrMagnitude > 1e-6f)
            {
                Vector3 landing = position + push * deltaTime;

                if (_impulses.IsPushedFasterThan(_tuning.Unit.DrownKnockbackThreshold) &&
                    GroundMotion.IsWater(_grid, landing))
                {
                    next = new Vector3(landing.x, 0f, landing.z);

                    return false;
                }

                _agent.Move(push * deltaTime);
            }

            next = _agent.transform.position;

            return true;
        }

        // ====================================================================================================
        // 8. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 아군과 적에게서 밀려나는 성분을 구해 부드럽게 따라갑니다.
        ///
        /// 적과도 밀어내되 <b>약하게</b>입니다.
        /// 안 밀어내면 난전에서 몸이 그대로 겹쳐 어느 쪽이 어디 있는지 알 수 없게 됩니다.
        /// 그렇다고 아군만큼 세게 밀면 서로 다가가지 못해 영영 칼이 닿지 않습니다.
        /// </summary>
        private Vector3 SolveSeparation(Vector3 position, float deltaTime)
        {
            float radius = _definition.Radius * SeparationRadiusFactor;

            Vector3 separation = ComputeSeparation(
                position, radius, _owner.Team, _owner, _tuning.Unit.AllySeparationWeight);

            var enemyTeam = _owner.Team == Team.Player ? Team.Enemy : Team.Player;

            separation += ComputeSeparation(
                position, radius, enemyTeam, null, _tuning.Unit.EnemySeparationWeight);

            // 분리 성분만 따로 부드럽게 따라갑니다.
            // 계산된 값을 그대로 더하면 이웃이 반경에 드나들 때마다 속도가 계단처럼 튀어,
            // 옆 사람이 다가왔을 뿐인데 홱 밀려나는 것처럼 보입니다.
            // 도착 조향은 원래 부드러우므로 여기만 눌러 주면 됩니다.
            _impulses.FollowSeparation(separation, _tuning.Unit.SeparationSmoothing, deltaTime);

            return _impulses.Separation;
        }

        /// <summary>
        /// 지정 진영의 이웃에게서 밀려나는 속도를 구합니다.
        /// </summary>
        private Vector3 ComputeSeparation(Vector3 position, float radius, Team team, Unit exclude, float weight)
        {
            if (weight <= 0f)
            {
                return Vector3.zero;
            }

            int count = _spatial.QueryTeam(position, radius, team, exclude, _neighborBuffer);
            if (count == 0)
            {
                return Vector3.zero;
            }

            Vector3 separation = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                separation += SteeringSolver.SeparationFrom(position, _neighborBuffer[i].Position, radius);
            }

            return separation * (_owner.Stats.MoveSpeed * weight);
        }
    }

    /// <summary>
    /// 이번 프레임에 병사가 받은 이동 지시입니다.
    ///
    /// 슬롯은 분대가 정하고, 리시와 후퇴는 무기가 정합니다.
    /// 이동을 계산하는 쪽이 그 둘을 직접 캐묻지 않도록 값으로 받습니다.
    /// </summary>
    public readonly struct SteeringOrder
    {
        /// <summary>분대가 배정한 진형 슬롯입니다. 행군 중에는 앵커입니다.</summary>
        public readonly Vector3 SlotTarget;

        /// <summary>살아 있는 교전 대상이 있는지 여부입니다.</summary>
        public readonly bool HasTarget;

        /// <summary>교전 대상의 위치입니다.</summary>
        public readonly Vector3 TargetPosition;

        /// <summary>대열을 풀고 나갈 수 있는 월드 거리입니다. 0이면 자리를 지킵니다.</summary>
        public readonly float LeashDistance;

        /// <summary>지금 물러나야 하는지 여부입니다. 창병이 품 안을 내준 경우입니다.</summary>
        public readonly bool Retreating;

        /// <summary>물러날 기준이 되는 위협의 위치입니다.</summary>
        public readonly Vector3 RetreatFrom;

        public SteeringOrder(
            Vector3 slotTarget,
            bool hasTarget,
            Vector3 targetPosition,
            float leashDistance,
            bool retreating,
            Vector3 retreatFrom)
        {
            SlotTarget = slotTarget;
            HasTarget = hasTarget;
            TargetPosition = targetPosition;
            LeashDistance = leashDistance;
            Retreating = retreating;
            RetreatFrom = retreatFrom;
        }
    }
}
