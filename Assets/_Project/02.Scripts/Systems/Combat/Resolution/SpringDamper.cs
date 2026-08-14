using UnityEngine;

namespace SRPG.Systems.Combat
{
    /// <summary>
    /// 목표값을 향해 <b>무게를 가지고</b> 따라가는 스프링입니다.
    ///
    /// <b>왜 보간이 아니라 스프링인가</b>
    ///
    /// <c>Lerp</c>나 <c>Slerp</c>로 각도를 따라가면 언제나 목표를 향해 <b>곧바로</b> 줄어듭니다.
    /// 속도가 없으니 관성도 없고, 목표가 바뀌는 순간 방향도 즉시 바뀝니다.
    /// 그래서 표적을 옮길 때마다 무기가 로봇처럼 휙휙 꺾입니다.
    ///
    /// 스프링은 <b>속도를 상태로 들고 있습니다.</b> 목표가 바뀌어도 이미 돌던 관성이 남아 있어
    /// 잠깐 지나쳤다가 되돌아옵니다. 그 한 번의 넘침이 무기에 무게감을 줍니다.
    /// 조사에서 확인한 "창의 회전축에 가상 스프링을 달았다"가 이것입니다.
    ///
    /// <b>감쇠 계수의 의미</b>
    ///   · 1 미만 — 목표를 지나쳤다 돌아옵니다. 무거운 무기의 느낌입니다
    ///   · 1      — 지나치지 않고 가장 빨리 멈춥니다(임계 감쇠)
    ///   · 1 초과 — 굼뜨게 다가갑니다
    ///
    /// <b>큰 프레임 간격에서도 터지지 않습니다.</b>
    /// 명시적 적분은 <c>deltaTime</c>이 커지면 발산합니다. 프레임이 한 번 튀는 것만으로
    /// 창이 미친 듯이 회전할 수 있으므로, 해석적으로 안정된 형태를 씁니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 계산이라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public static class SpringDamper
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// 한 호출에서 시뮬레이션하는 최대 시간(초)입니다. 이보다 긴 프레임은 잘라 냅니다.
        ///
        /// <b>따라잡지 않는 것이 맞습니다.</b> 로딩으로 5초가 통째로 밀렸을 때
        /// 스프링이 그 5초를 성실히 재현하면 무기가 몇 바퀴를 돌고 나서 멈춥니다.
        /// 끊긴 동안의 움직임은 어차피 아무도 보지 못했으므로, 버리고 현재부터 이어 가는 편이 낫습니다.
        /// </summary>
        private const float MaxSimulatedStep = 0.1f;

        /// <summary>한 호출에서 밟는 최대 하위 걸음 수입니다.</summary>
        private const int MaxSubSteps = 32;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 각도를 스프링으로 한 걸음 진행시킵니다. 단위는 도(degree)입니다.
        ///
        /// 각도는 360도에서 되감기므로 최단 방향으로 오차를 잽니다.
        /// 그러지 않으면 350도에서 10도로 갈 때 먼 쪽으로 340도를 돕니다.
        /// </summary>
        /// <param name="current">현재 각도입니다.</param>
        /// <param name="target">목표 각도입니다.</param>
        /// <param name="angularVelocity">각속도입니다. 호출 간에 유지되어야 합니다.</param>
        /// <param name="frequency">
        /// 초당 진동 횟수입니다. 클수록 빠르게 따라붙습니다.
        /// 강성(stiffness)을 직접 넣는 것보다 감이 잡히는 단위라 이쪽을 받습니다.
        /// </param>
        /// <param name="damping">감쇠 계수입니다. 1이 임계 감쇠입니다.</param>
        /// <param name="deltaTime">경과 시간입니다.</param>
        /// <returns>새 각도입니다.</returns>
        public static float StepAngle(
            float current,
            float target,
            ref float angularVelocity,
            float frequency,
            float damping,
            float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return current;
            }

            // 최단 방향으로 편 목표입니다. 되감김을 여기서 한 번만 처리하면
            // 이후 계산은 그냥 실수 하나를 다루는 것과 같아집니다.
            float unwrappedTarget = current + Mathf.DeltaAngle(current, target);

            float result = Step(current, unwrappedTarget, ref angularVelocity, frequency, damping, deltaTime);

            return result;
        }

        /// <summary>
        /// 실수 값을 스프링으로 한 걸음 진행시킵니다.
        ///
        /// 반암시적(semi-implicit) 적분을 씁니다.
        /// 새 속도를 먼저 구하고 그것으로 위치를 옮기면, 명시적 적분보다 훨씬 늦게 발산합니다.
        /// 여기에 시간 간격 상한까지 두어 프레임이 크게 튀어도 안전하게 만듭니다.
        /// </summary>
        /// <param name="current">현재 값입니다.</param>
        /// <param name="target">따라갈 목표 값입니다.</param>
        /// <param name="velocity">각속도 상태입니다. 호출할 때마다 갱신됩니다.</param>
        /// <param name="frequency">진동수입니다. 클수록 빠르게 따라붙습니다.</param>
        /// <param name="damping">감쇠입니다. 1 미만이면 목표를 지나쳤다 돌아옵니다.</param>
        /// <param name="deltaTime">지난 시간입니다. 한 호출에서 시뮬레이션하는 상한이 있습니다.</param>
        /// <returns>이번 걸음 뒤의 값입니다.</returns>
        public static float Step(
            float current,
            float target,
            ref float velocity,
            float frequency,
            float damping,
            float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return current;
            }

            float omega = 2f * Mathf.PI * Mathf.Max(0.0001f, frequency);
            float zeta = Mathf.Max(0f, damping);

            // 아무리 긴 프레임이라도 이만큼만 시뮬레이션합니다.
            // 자르지 않으면 하위 걸음 상한에 걸려 한 걸음이 안정 한계를 넘고, 스프링이 그대로 발산합니다.
            float elapsed = Mathf.Min(deltaTime, MaxSimulatedStep);

            // 한 걸음이 너무 크면 나눠서 밟습니다.
            // 안정 한계를 넘는 간격 하나로도 스프링은 발산해 무기가 미친 듯이 돕니다.
            float maxStep = 1f / (omega * 4f);
            int steps = Mathf.Clamp(Mathf.CeilToInt(elapsed / maxStep), 1, MaxSubSteps);
            float step = elapsed / steps;

            float value = current;

            for (int i = 0; i < steps; i++)
            {
                float acceleration = omega * omega * (target - value) - 2f * zeta * omega * velocity;

                velocity += acceleration * step;
                value += velocity * step;
            }

            return value;
        }
    }
}
