using UnityEngine;

namespace SRPG.Systems.Combat
{
    /// <summary>
    /// 다가오는 대상이 <b>어디에 도착할지</b>를 예측합니다.
    ///
    /// <b>무엇을 푸는 문제인가</b>
    ///
    /// <see cref="BallisticSolver.TryPredictInterceptPoint"/>는 "날아가는 투사체가 움직이는 대상을 어디서 만나는가"를
    /// 풉니다. 여기서 푸는 것은 정반대입니다. <b>나는 제자리에 버티고 있고, 대상이 나에게 다가옵니다.</b>
    /// 대상이 내 사거리에 들어오는 순간 그가 어디 있을지를 구합니다.
    ///
    /// 창병이 이걸 씁니다. 조사에서 확인한 Bad North 창병의 행동은
    /// "적이 아직 사거리 밖이어도 적이 올 자리를 미리 겨눈다"입니다.
    /// 창을 적이 <b>지금 있는 곳</b>으로 돌리면 항상 한 박자 늦습니다.
    /// 적이 도착했을 때 창은 이미 그 자리를 향해 있어야 합니다.
    ///
    /// 이 차이가 창병을 "버티는 병과"로 만듭니다. 쫓아가지 않고 기다리는데도 먼저 닿습니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 계산이라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public static class AimPredictor
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>이보다 느리게 접근하면 다가오지 않는 것으로 봅니다.</summary>
        private const float MinimumClosingSpeed = 0.05f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 대상이 유효 사거리 안으로 들어올 때까지 걸리는 시간을 구합니다.
        ///
        /// 수평면에서만 계산합니다. 고도 차가 있어도 창이 닿는지는 결국 평면 거리로 결정되고,
        /// 높이를 넣으면 계단 하나 차이로 예측이 출렁입니다.
        /// </summary>
        /// <param name="selfPosition">버티고 있는 쪽의 위치입니다.</param>
        /// <param name="targetPosition">대상의 현재 위치입니다.</param>
        /// <param name="targetVelocity">대상의 현재 속도입니다.</param>
        /// <param name="effectiveRange">대상이 들어오면 공격할 수 있는 거리입니다.</param>
        /// <param name="timeToReach">사거리에 닿기까지의 시간입니다.</param>
        /// <returns>
        /// 다가오는 중이면 true입니다.
        /// 이미 사거리 안이면 시간 0으로 true, 멀어지거나 제자리면 false입니다.
        /// </returns>
        public static bool TryGetTimeToReach(
            Vector3 selfPosition,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float effectiveRange,
            out float timeToReach)
        {
            timeToReach = 0f;

            Vector3 toSelf = selfPosition - targetPosition;
            toSelf.y = 0f;

            float distance = toSelf.magnitude;

            // 이미 닿는 거리입니다. 기다릴 필요가 없습니다.
            if (distance <= effectiveRange)
            {
                return true;
            }

            if (distance < 0.0001f)
            {
                return true;
            }

            Vector3 velocity = targetVelocity;
            velocity.y = 0f;

            // 나를 향하는 방향의 속도 성분만이 거리를 줄입니다.
            float closingSpeed = Vector3.Dot(velocity, toSelf / distance);

            if (closingSpeed < MinimumClosingSpeed)
            {
                // 멀어지거나 옆으로 지나가는 중입니다. 도착 시점을 말할 수 없습니다.
                return false;
            }

            timeToReach = (distance - effectiveRange) / closingSpeed;
            return true;
        }

        /// <summary>
        /// 대상이 사거리에 닿는 시점의 위치를 구합니다. 창이 겨눌 지점입니다.
        ///
        /// 도착 시간에 <paramref name="extraLeadSeconds"/>를 더하는 이유는 준비 동작 때문입니다.
        /// 찌르기는 즉시 나가지 않고 앞부분에 준비 구간이 있으므로, 그만큼 더 앞을 봐야 합니다.
        ///
        /// 상한을 두는 이유는 접근 속도가 아주 느릴 때입니다.
        /// 거의 멈춰 선 적을 향해 도착 시간을 그대로 쓰면 예측 지점이 지평선까지 날아갑니다.
        /// </summary>
        /// <param name="selfPosition">버티고 있는 쪽의 위치입니다.</param>
        /// <param name="targetPosition">대상의 현재 위치입니다.</param>
        /// <param name="targetVelocity">대상의 현재 속도입니다.</param>
        /// <param name="effectiveRange">공격할 수 있는 거리입니다.</param>
        /// <param name="extraLeadSeconds">준비 동작 보정입니다.</param>
        /// <param name="maxLeadSeconds">예측 시간의 상한입니다.</param>
        /// <returns>대상이 사거리에 닿을 것으로 예측한 월드 좌표입니다. 높이는 무시합니다.</returns>
        public static Vector3 PredictApproachPoint(
            Vector3 selfPosition,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float effectiveRange,
            float extraLeadSeconds,
            float maxLeadSeconds)
        {
            float lead = extraLeadSeconds;

            if (TryGetTimeToReach(selfPosition, targetPosition, targetVelocity, effectiveRange, out float timeToReach))
            {
                lead += timeToReach;
            }

            lead = Mathf.Clamp(lead, 0f, Mathf.Max(0f, maxLeadSeconds));

            Vector3 velocity = targetVelocity;
            velocity.y = 0f;

            return targetPosition + velocity * lead;
        }
    }
}
