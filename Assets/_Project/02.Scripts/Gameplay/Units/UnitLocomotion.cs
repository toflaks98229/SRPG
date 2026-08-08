using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Systems.Grid;
using SRPG.Systems.Motion;
using SRPG.Systems.Pathfinding;
using UnityEngine;

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

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly ImpulseState _impulses = new ImpulseState();
        private readonly List<Unit> _neighborBuffer = new List<Unit>(16);

        private Unit _owner;
        private IslandGrid _grid;
        private ISpatialQuery _spatial;
        private BattleTuning _tuning;
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
        }

        // ====================================================================================================
        // 5. Public Methods - Impulse
        // ====================================================================================================

        /// <summary>맞아서 밀려납니다. 밀려난 자리가 물이면 익사합니다.</summary>
        public void AddKnockback(Vector3 impulse)
        {
            _impulses.AddKnockback(impulse);
        }

        /// <summary>스스로 앞으로 몸을 던집니다. 물로 밀려나지 않습니다.</summary>
        public void AddLunge(Vector3 impulse)
        {
            _impulses.AddLunge(impulse);
        }

        /// <summary>외력을 감쇠시킵니다.</summary>
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
        public Vector3 Solve(in SteeringOrder order, Vector3 position, float deltaTime)
        {
            // 진형 붕괴: 품 안을 내준 무기는 싸울 방법이 없습니다. 물러나는 것이 유일한 반응입니다.
            //
            // 분리도 최대 속도 제한도 타지 않고 곧장 빠져나갑니다.
            // 물러나는 것 자체가 이미 예외 상태라, 여기서 옆 사람과의 간격을 따질 여유가 없습니다.
            if (order.Retreating)
            {
                Vector3 away = position - order.RetreatFrom;
                away.y = 0f;

                if (away.sqrMagnitude > 0.0001f)
                {
                    return away.normalized * _definition.MoveSpeed;
                }
            }

            var plan = ApproachSolver.Resolve(new ApproachRequest(
                position,
                order.SlotTarget,
                order.HasTarget,
                order.TargetPosition,
                _definition.AttackRange,
                order.LeashDistance));

            Vector3 velocity = SteeringSolver.Arrive(
                position, plan.Destination, _definition.MoveSpeed, ApproachSolver.ArriveSlowRadius);

            velocity += SolveSeparation(position, deltaTime);

            // 최대 속도를 넘지 않게 제한합니다.
            float maxSpeed = _definition.MoveSpeed;
            if (velocity.sqrMagnitude > maxSpeed * maxSpeed)
            {
                velocity = velocity.normalized * maxSpeed;
            }

            return velocity;
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

            Vector3 desired = position + _impulses.CombineWith(steering) * deltaTime;

            var step = GroundMotion.TryStep(
                _grid,
                position,
                desired,
                _impulses.IsPushedFasterThan(_tuning.DrownKnockbackThreshold),
                out next);

            return step == GroundStep.Moved;
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
                position, radius, _owner.Team, _owner, _tuning.AllySeparationWeight);

            var enemyTeam = _owner.Team == Team.Player ? Team.Enemy : Team.Player;

            separation += ComputeSeparation(
                position, radius, enemyTeam, null, _tuning.EnemySeparationWeight);

            // 분리 성분만 따로 부드럽게 따라갑니다.
            // 계산된 값을 그대로 더하면 이웃이 반경에 드나들 때마다 속도가 계단처럼 튀어,
            // 옆 사람이 다가왔을 뿐인데 홱 밀려나는 것처럼 보입니다.
            // 도착 조향은 원래 부드러우므로 여기만 눌러 주면 됩니다.
            _impulses.FollowSeparation(separation, _tuning.SeparationSmoothing, deltaTime);

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

            return separation * (_definition.MoveSpeed * weight);
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
