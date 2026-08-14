using UnityEngine;

namespace SRPG.Systems.Pathfinding
{
    /// <summary>
    /// 이번 프레임에 병사가 향할 지점 하나를 정합니다.
    ///
    /// <b>왜 떼어 놓는가</b>
    ///
    /// "자리를 지킬 것인가, 대열을 풀고 나설 것인가"는 이 게임의 전술이 성립하는 지점입니다.
    /// 창병이 자리를 지키고 검병이 한두 걸음 나가는 그 차이가 병과를 병과답게 만듭니다.
    /// 그런데 이 판단이 <c>Unit.Update</c> 한복판에 조향·분리·넉백과 뒤엉켜 있으면
    /// "왜 저 병사가 안 나가는가"를 씬을 재생해 가며 찾아야 합니다.
    ///
    /// 순수 계산으로 떼어 두면 EditMode에서 리시 거리 하나만 바꿔 가며 확인할 수 있습니다.
    /// </summary>
    public static class ApproachSolver
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>슬롯에 도착했다고 판단하는 거리입니다.</summary>
        public const float SlotArrivalThreshold = 0.45f;

        /// <summary>도착 감속이 시작되는 거리입니다.</summary>
        public const float ArriveSlowRadius = 1.2f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 향할 지점을 정합니다.
        ///
        /// <b>규칙</b>
        ///   · 기본은 자기 진형 슬롯입니다
        ///   · 대열을 풀 수 있는 병과(<c>LeashDistance &gt; 0</c>)는 <b>행군 중에도</b> 눈앞의 적에게 붙습니다.
        ///     자리를 잡은 뒤에만 나서게 하면 행군로에 선 적을 그냥 지나쳐 가 버립니다
        ///   · 리시는 <b>자기 자리</b>에서 잽니다. 분대가 지나가 버리면 거리가 벌어져
        ///     자연히 교전을 접고 따라붙습니다 — 별도의 "복귀" 규칙이 필요 없습니다
        ///   · 사거리 안이면 멈춰 싸웁니다
        /// </summary>
        /// <param name="request">위치·슬롯·표적·리시가 담긴 입력입니다.</param>
        /// <returns>향할 지점과 그렇게 정한 맥락(슬롯 도착 여부·교전 여부)입니다.</returns>
        public static ApproachPlan Resolve(in ApproachRequest request)
        {
            float slotDistance = FlatDistance(request.Position, request.SlotTarget);
            bool atSlot = slotDistance <= SlotArrivalThreshold;

            Vector3 destination = request.SlotTarget;
            bool engaging = false;

            // 얼마나 나설 수 있는지는 무기가 정합니다. 창병과 궁수는 0이라 자리를 지킵니다.
            bool mayEngage = atSlot || request.LeashDistance > 0f;

            if (mayEngage && request.HasTarget)
            {
                bool withinLeash = request.LeashDistance > 0f
                                   && Vector3.Distance(request.TargetPosition, request.SlotTarget) <= request.LeashDistance;

                float targetDistance = Vector3.Distance(request.Position, request.TargetPosition);

                if (targetDistance <= request.AttackRange)
                {
                    // 사거리 안입니다. 자리를 지키는 병과이거나 아직 리시 안이면 멈춰 싸웁니다.
                    if (atSlot || withinLeash)
                    {
                        destination = request.Position;
                        engaging = true;
                    }
                }
                else if (withinLeash)
                {
                    destination = request.TargetPosition;
                    engaging = true;
                }
            }

            return new ApproachPlan(destination, atSlot, engaging);
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 높이를 뺀 평면 거리입니다.
        ///
        /// 슬롯 도착 판정은 반드시 평면이어야 합니다. 비탈에 선 병사는 슬롯과 높이가 다르므로,
        /// 3차원 거리로 재면 제자리에 서 있는데도 영영 "도착하지 않은" 것이 됩니다.
        /// </summary>
        /// <param name="a">기준 위치입니다.</param>
        /// <param name="b">대상 위치입니다.</param>
        /// <returns>Y를 무시한 XZ 평면 거리입니다.</returns>
        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;

            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>
    /// 향할 지점을 정하는 데 필요한 입력입니다.
    /// </summary>
    public readonly struct ApproachRequest
    {
        /// <summary>병사의 현재 위치입니다.</summary>
        public readonly Vector3 Position;

        /// <summary>분대가 배정한 진형 슬롯입니다. 행군 중에는 앵커입니다.</summary>
        public readonly Vector3 SlotTarget;

        /// <summary>교전 대상이 있는지 여부입니다.</summary>
        public readonly bool HasTarget;

        /// <summary>교전 대상의 위치입니다. <see cref="HasTarget"/>이 false면 무시됩니다.</summary>
        public readonly Vector3 TargetPosition;

        /// <summary>공격 사거리입니다.</summary>
        public readonly float AttackRange;

        /// <summary>대열을 풀고 나갈 수 있는 월드 거리입니다. 0이면 자리를 지킵니다.</summary>
        public readonly float LeashDistance;

        /// <summary>입력을 묶습니다.</summary>
        /// <param name="position">병사의 현재 위치입니다.</param>
        /// <param name="slotTarget">분대가 배정한 진형 슬롯입니다. 행군 중에는 앵커입니다.</param>
        /// <param name="hasTarget">교전 대상이 있는지 여부입니다.</param>
        /// <param name="targetPosition">교전 대상의 위치입니다. <paramref name="hasTarget"/>이 false면 무시됩니다.</param>
        /// <param name="attackRange">공격 사거리입니다.</param>
        /// <param name="leashDistance">대열을 풀고 나갈 수 있는 월드 거리입니다. 0이면 자리를 지킵니다.</param>
        public ApproachRequest(
            Vector3 position,
            Vector3 slotTarget,
            bool hasTarget,
            Vector3 targetPosition,
            float attackRange,
            float leashDistance)
        {
            Position = position;
            SlotTarget = slotTarget;
            HasTarget = hasTarget;
            TargetPosition = targetPosition;
            AttackRange = attackRange;
            LeashDistance = leashDistance;
        }
    }

    /// <summary>
    /// 향할 지점과 그렇게 정한 맥락입니다.
    /// </summary>
    public readonly struct ApproachPlan
    {
        /// <summary>이번 프레임에 향할 월드 좌표입니다.</summary>
        public readonly Vector3 Destination;

        /// <summary>자기 슬롯에 도착해 있는지 여부입니다.</summary>
        public readonly bool AtSlot;

        /// <summary>슬롯이 아니라 적을 기준으로 목적지를 정했는지 여부입니다. 디버그와 검증에 씁니다.</summary>
        public readonly bool Engaging;

        /// <summary>결과를 묶습니다.</summary>
        /// <param name="destination">이번 프레임에 향할 월드 좌표입니다.</param>
        /// <param name="atSlot">자기 슬롯에 도착해 있는지 여부입니다.</param>
        /// <param name="engaging">적을 기준으로 목적지를 정했는지 여부입니다.</param>
        public ApproachPlan(Vector3 destination, bool atSlot, bool engaging)
        {
            Destination = destination;
            AtSlot = atSlot;
            Engaging = engaging;
        }
    }
}
