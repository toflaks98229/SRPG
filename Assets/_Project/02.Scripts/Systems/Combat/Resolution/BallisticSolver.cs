using UnityEngine;

namespace SRPG.Systems.Combat
{
    /// <summary>
    /// 투사체 조준을 계산합니다.
    ///
    /// 두 계열이 있습니다.
    ///
    /// <b>직사</b> — 속력을 고정하고 발사각을 푼다
    ///   1) <see cref="TryPredictInterceptPoint"/> — 움직이는 대상의 "어디를 쏠 것인가"
    ///   2) <see cref="TrySolveLaunchVelocity"/> — 그 지점에 닿는 "어떤 각으로 쏠 것인가"
    ///
    /// <b>곡사</b> — 발사각을 고정하고 속력을 푼다 (현재 활이 쓰는 방식)
    ///   · <see cref="TrySolveArcingLaunch"/> — 일정한 모양의 포물선
    ///   · <see cref="TrySolveArcingIntercept"/> — 긴 비행 시간을 반영한 반복 예측
    ///
    /// MonoBehaviour에 의존하지 않는 순수 계산이라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public static class BallisticSolver
    {
        // ====================================================================================================
        // 1. Public Methods - Prediction
        // ====================================================================================================

        /// <summary>
        /// 등속으로 움직이는 대상을 맞히기 위한 요격 지점을 구합니다.
        ///
        /// 화살이 날아가는 동안 대상도 이동하므로, 현재 위치를 그대로 겨누면 뒤를 쏘게 됩니다.
        /// 화살이 t초 뒤 도달할 거리와 대상이 t초 뒤 있을 위치가 같아지는 t를 풉니다.
        ///
        ///   | P + V·t - S | = speed · t
        ///
        /// 양변을 제곱하면 t에 대한 이차방정식이 되고, 양수인 가장 이른 해를 씁니다.
        /// </summary>
        /// <param name="shooterPosition">발사 지점입니다.</param>
        /// <param name="targetPosition">대상의 현재 위치입니다.</param>
        /// <param name="targetVelocity">대상의 현재 속도입니다.</param>
        /// <param name="projectileSpeed">투사체의 속력입니다.</param>
        /// <param name="interceptPoint">요격 지점입니다.</param>
        /// <param name="timeToImpact">도달까지 걸리는 시간입니다.</param>
        /// <returns>해가 있으면 true입니다. 대상이 투사체보다 빠르게 달아나면 false입니다.</returns>
        public static bool TryPredictInterceptPoint(
            Vector3 shooterPosition,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float projectileSpeed,
            out Vector3 interceptPoint,
            out float timeToImpact)
        {
            interceptPoint = targetPosition;
            timeToImpact = 0f;

            if (projectileSpeed <= 0.01f)
            {
                return false;
            }

            Vector3 relativePosition = targetPosition - shooterPosition;

            float a = targetVelocity.sqrMagnitude - projectileSpeed * projectileSpeed;
            float b = 2f * Vector3.Dot(relativePosition, targetVelocity);
            float c = relativePosition.sqrMagnitude;

            float t;

            if (Mathf.Abs(a) < 0.0001f)
            {
                // 대상 속력이 투사체 속력과 같으면 이차항이 사라져 일차식이 됩니다.
                if (Mathf.Abs(b) < 0.0001f)
                {
                    return false;
                }

                t = -c / b;
            }
            else
            {
                float discriminant = b * b - 4f * a * c;
                if (discriminant < 0f)
                {
                    // 실근이 없습니다. 대상이 투사체보다 빨라 따라잡을 수 없는 경우입니다.
                    return false;
                }

                float sqrtD = Mathf.Sqrt(discriminant);
                float t1 = (-b + sqrtD) / (2f * a);
                float t2 = (-b - sqrtD) / (2f * a);

                // 양수인 해 중 이른 쪽을 고릅니다.
                t = SelectEarliestPositive(t1, t2);
            }

            if (t <= 0f || float.IsNaN(t) || float.IsInfinity(t))
            {
                return false;
            }

            timeToImpact = t;
            interceptPoint = targetPosition + targetVelocity * t;
            return true;
        }

        // ====================================================================================================
        // 2. Public Methods - Launch
        // ====================================================================================================

        /// <summary>
        /// 정해진 속력으로 목표 지점에 닿는 발사 속도를 구합니다.
        ///
        /// 수평 거리 x, 높이 차 y, 속력 v, 중력 g에 대해 발사각 θ는 다음을 만족합니다.
        ///
        ///   tan θ = ( v² ± √( v⁴ − g( g·x² + 2·y·v² ) ) ) / ( g·x )
        ///
        /// 판별식이 음수면 사거리 밖입니다.
        /// 부호가 둘인데, 낮은 궤도(−)를 씁니다. 명중까지 시간이 짧아 예측 오차가 덜 쌓이고,
        /// 아군 머리 위를 넘기지 않아 오사(誤射)로 보이는 상황이 줄기 때문입니다.
        /// 높은 궤도(+)는 나중에 장애물을 넘기는 볼리 사격에 쓸 자리입니다.
        /// </summary>
        /// <param name="from">발사 지점입니다.</param>
        /// <param name="to">목표 지점입니다.</param>
        /// <param name="speed">투사체 속력입니다.</param>
        /// <param name="gravity">적용할 중력 크기(양수)입니다. 0이면 직사로 계산합니다.</param>
        /// <param name="launchVelocity">구해진 발사 속도입니다.</param>
        /// <param name="useHighArc">true면 높은 궤도를 고릅니다.</param>
        public static bool TrySolveLaunchVelocity(
            Vector3 from,
            Vector3 to,
            float speed,
            float gravity,
            out Vector3 launchVelocity,
            bool useHighArc = false)
        {
            launchVelocity = Vector3.zero;

            if (speed <= 0.01f)
            {
                return false;
            }

            Vector3 delta = to - from;
            Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
            float x = horizontal.magnitude;
            float y = delta.y;

            // 중력이 없으면 직선으로 겨누면 됩니다.
            if (gravity <= 0.0001f)
            {
                Vector3 direction = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector3.forward;
                launchVelocity = direction * speed;
                return true;
            }

            // 바로 위나 아래를 쏘는 경우 수평 거리가 0이라 tan θ 식이 성립하지 않습니다.
            if (x < 0.0001f)
            {
                launchVelocity = Vector3.up * speed * Mathf.Sign(y == 0f ? 1f : y);
                return true;
            }

            float speedSqr = speed * speed;
            float discriminant = speedSqr * speedSqr - gravity * (gravity * x * x + 2f * y * speedSqr);

            if (discriminant < 0f)
            {
                // 이 속력으로는 닿지 않습니다. 호출부가 사거리 밖으로 처리해야 합니다.
                return false;
            }

            float sqrtD = Mathf.Sqrt(discriminant);
            float tanTheta = (speedSqr + (useHighArc ? sqrtD : -sqrtD)) / (gravity * x);
            float theta = Mathf.Atan(tanTheta);

            Vector3 flatDirection = horizontal / x;

            launchVelocity = flatDirection * (speed * Mathf.Cos(theta)) + Vector3.up * (speed * Mathf.Sin(theta));
            return true;
        }

        // ====================================================================================================
        // 3. Public Methods - Arcing Fire (곡사)
        // ====================================================================================================

        /// <summary>
        /// <b>발사각을 고정하고 속력을 푸는</b> 곡사 계산입니다.
        ///
        /// <see cref="TrySolveLaunchVelocity"/>와 반대 방향의 문제를 풉니다. 이유가 있습니다.
        /// 속력을 고정하고 각을 풀면, 사거리에 비해 속력이 과하면 높은 궤도 해가 거의 수직이 됩니다.
        /// (22m/s로 9m 앞을 곡사로 쏘면 발사각이 85도에 가까워져 화살이 하늘로 솟구쳤다 떨어집니다)
        /// 각을 고정하면 어떤 거리에서도 <b>일정한 모양의 포물선</b>이 나오고, 속력만 거리에 맞춰 변합니다.
        ///
        /// 수평 거리 x, 높이 차 y, 발사각 θ에 대해
        ///
        ///   y = x·tanθ − g·x² / (2·v²·cos²θ)
        ///
        /// 를 v에 대해 풀면
        ///
        ///   v² = g·x² / ( 2·cos²θ·(x·tanθ − y) )
        ///
        /// 분모가 0 이하면 그 각으로는 목표 높이에 닿지 못합니다(목표가 발사각선보다 높은 경우).
        /// </summary>
        /// <param name="from">발사 지점입니다.</param>
        /// <param name="to">목표 지점입니다.</param>
        /// <param name="launchAngleDegrees">고정 발사각(도)입니다. 클수록 높이 뜹니다.</param>
        /// <param name="gravity">중력 크기(양수)입니다.</param>
        /// <param name="launchVelocity">구해진 발사 속도입니다.</param>
        /// <param name="flightTime">목표까지 걸리는 비행 시간입니다.</param>
        public static bool TrySolveArcingLaunch(
            Vector3 from,
            Vector3 to,
            float launchAngleDegrees,
            float gravity,
            out Vector3 launchVelocity,
            out float flightTime)
        {
            launchVelocity = Vector3.zero;
            flightTime = 0f;

            if (gravity <= 0.0001f)
            {
                // 중력이 없으면 포물선이 성립하지 않습니다.
                return false;
            }

            Vector3 delta = to - from;
            Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
            float x = horizontal.magnitude;
            float y = delta.y;

            if (x < 0.01f)
            {
                // 바로 위나 아래는 곡사로 겨눌 수 없습니다.
                return false;
            }

            float theta = Mathf.Clamp(launchAngleDegrees, 1f, 89f) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(theta);
            float sin = Mathf.Sin(theta);
            float tan = sin / cos;

            // 발사각선이 목표보다 낮으면 그 각도로는 도달할 수 없습니다.
            float denominator = x * tan - y;
            if (denominator <= 0.0001f)
            {
                return false;
            }

            float speedSqr = gravity * x * x / (2f * cos * cos * denominator);
            if (speedSqr <= 0f || float.IsNaN(speedSqr) || float.IsInfinity(speedSqr))
            {
                return false;
            }

            float speed = Mathf.Sqrt(speedSqr);
            Vector3 flatDirection = horizontal / x;

            launchVelocity = flatDirection * (speed * cos) + Vector3.up * (speed * sin);
            flightTime = x / (speed * cos);
            return true;
        }

        /// <summary>
        /// 움직이는 대상을 향한 곡사를 계산합니다.
        ///
        /// 곡사는 비행 시간이 깁니다(근거리에서도 1초 내외). 그 사이 대상은 계속 움직입니다.
        /// 그런데 <see cref="TryPredictInterceptPoint"/>는 투사체가 <b>직선으로</b> 날아간다고 가정하므로
        /// 곡사에 그대로 쓰면 비행 시간을 크게 과소평가해 항상 뒤를 쏘게 됩니다.
        ///
        /// 그래서 반복해서 수렴시킵니다.
        ///   조준점을 잡는다 → 그 점까지의 실제 비행 시간을 구한다 → 그 시간만큼 앞을 다시 겨눈다
        /// 두세 번이면 충분히 수렴합니다.
        /// </summary>
        /// <param name="from">발사 지점입니다.</param>
        /// <param name="targetPosition">대상의 현재 위치입니다.</param>
        /// <param name="targetVelocity">대상의 속도입니다. 예측하지 않으려면 0을 넘깁니다.</param>
        /// <param name="launchAngleDegrees">고정 발사각(도)입니다.</param>
        /// <param name="gravity">중력 크기(양수)입니다.</param>
        /// <param name="maxSpeed">허용 최대 속력입니다. 이를 넘으면 사거리 밖으로 봅니다.</param>
        /// <param name="launchVelocity">구해진 발사 속도입니다.</param>
        /// <param name="aimPoint">최종 조준 지점입니다.</param>
        /// <param name="iterations">수렴 반복 횟수입니다.</param>
        public static bool TrySolveArcingIntercept(
            Vector3 from,
            Vector3 targetPosition,
            Vector3 targetVelocity,
            float launchAngleDegrees,
            float gravity,
            float maxSpeed,
            out Vector3 launchVelocity,
            out Vector3 aimPoint,
            int iterations = 3)
        {
            aimPoint = targetPosition;
            launchVelocity = Vector3.zero;

            for (int i = 0; i < iterations; i++)
            {
                if (!TrySolveArcingLaunch(from, aimPoint, launchAngleDegrees, gravity, out launchVelocity, out float flightTime))
                {
                    return false;
                }

                Vector3 nextAim = targetPosition + targetVelocity * flightTime;
                if ((nextAim - aimPoint).sqrMagnitude < 0.0001f)
                {
                    break;
                }

                aimPoint = nextAim;
            }

            // 마지막 조준점 기준으로 한 번 더 풀어, 반환하는 속도와 조준점을 일치시킵니다.
            if (!TrySolveArcingLaunch(from, aimPoint, launchAngleDegrees, gravity, out launchVelocity, out _))
            {
                return false;
            }

            return launchVelocity.magnitude <= maxSpeed;
        }

        // ====================================================================================================
        // 4. Public Methods - Spread
        // ====================================================================================================

        /// <summary>
        /// 발사 방향에 원뿔 형태의 무작위 산포를 적용합니다.
        ///
        /// 조사에 따르면 Bad North의 궁수는 명중이 들쭉날쭉하고 "개별 표적보다 무리에게 더 정확"합니다.
        /// 그 성질은 별도 규칙 없이 산포만으로 자연히 나옵니다.
        /// 한 명을 빗나간 화살이 옆에 선 다른 적에게 맞기 때문입니다.
        /// </summary>
        /// <param name="velocity">원래 발사 속도입니다.</param>
        /// <param name="spreadDegrees">원뿔의 반각(도)입니다.</param>
        /// <param name="random">난수원입니다. 테스트에서 고정 시드를 넣을 수 있게 받습니다.</param>
        public static Vector3 ApplySpread(Vector3 velocity, float spreadDegrees, System.Random random = null)
        {
            if (spreadDegrees <= 0.0001f || velocity.sqrMagnitude < 0.0001f)
            {
                return velocity;
            }

            float u1 = random != null ? (float)random.NextDouble() : UnityEngine.Random.value;
            float u2 = random != null ? (float)random.NextDouble() : UnityEngine.Random.value;

            // 원뿔 안에서 면적이 고르게 분포하도록 반각에 제곱근을 씁니다.
            // 그냥 균등 분포를 쓰면 화살이 중심축에 몰립니다.
            float angle = spreadDegrees * Mathf.Sqrt(u1);
            float roll = u2 * 360f;

            Vector3 axis = velocity.normalized;
            Vector3 perpendicular = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.99f ? Vector3.forward : Vector3.up).normalized;

            Quaternion tilt = Quaternion.AngleAxis(angle, perpendicular);
            Quaternion spin = Quaternion.AngleAxis(roll, axis);

            return spin * tilt * velocity;
        }

        /// <summary>
        /// 랭크에 따른 조준 산포를 구합니다. 랭크가 높을수록 산포가 좁아집니다.
        /// </summary>
        /// <param name="rank">현재 랭크입니다.</param>
        /// <param name="maxRank">최고 랭크입니다.</param>
        /// <param name="spreadAtLowestRank">최하 랭크의 산포(도)입니다.</param>
        /// <param name="spreadAtHighestRank">최고 랭크의 산포(도)입니다.</param>
        public static float GetSpreadForRank(int rank, int maxRank, float spreadAtLowestRank, float spreadAtHighestRank)
        {
            if (maxRank <= 1)
            {
                return spreadAtHighestRank;
            }

            float t = Mathf.Clamp01((rank - 1f) / (maxRank - 1f));
            return Mathf.Lerp(spreadAtLowestRank, spreadAtHighestRank, t);
        }

        // ====================================================================================================
        // 5. Private Methods
        // ====================================================================================================

        private static float SelectEarliestPositive(float a, float b)
        {
            if (a > 0f && b > 0f)
            {
                return Mathf.Min(a, b);
            }

            return Mathf.Max(a, b);
        }
    }
}
