using UnityEngine;

namespace SRPG.Systems.Pathfinding
{
    /// <summary>
    /// 개별 유닛의 국소 조향을 계산합니다.
    /// 경로 탐색이 "어디로 갈지"를 정한다면 조향은 "어떻게 자연스럽게 갈지"를 정합니다.
    /// Craig Reynolds의 조향 행동 중 도착(Arrive)과 분리(Separation)만 사용합니다.
    /// </summary>
    public static class SteeringSolver
    {
        // ====================================================================================================
        // 1. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 목표 지점에 부드럽게 도착하는 속도를 계산합니다.
        /// 감속 반경 안에서는 거리에 비례해 속도를 줄여 목표를 지나치는 진동을 막습니다.
        /// </summary>
        /// <param name="position">현재 위치입니다.</param>
        /// <param name="target">목표 위치입니다.</param>
        /// <param name="maxSpeed">최대 속도입니다.</param>
        /// <param name="slowRadius">감속을 시작하는 반경입니다.</param>
        public static Vector3 Arrive(Vector3 position, Vector3 target, float maxSpeed, float slowRadius)
        {
            Vector3 offset = target - position;
            offset.y = 0f;

            float distance = offset.magnitude;
            if (distance < 0.0001f)
            {
                return Vector3.zero;
            }

            float speed = distance < slowRadius && slowRadius > 0.0001f
                ? maxSpeed * (distance / slowRadius)
                : maxSpeed;

            return offset / distance * speed;
        }

        /// <summary>
        /// 이웃과 겹치지 않도록 밀어내는 속도를 계산합니다.
        /// 가까울수록 강하게 밀어내며, 반경 밖의 이웃은 영향을 주지 않습니다.
        ///
        /// <b>세기가 거리의 제곱으로 줄어듭니다.</b>
        ///
        /// 선형으로 두면 이웃이 반경에 <b>들어서는 순간부터</b> 이미 무시 못 할 힘이 붙습니다.
        /// 옆 사람이 조금 다가왔을 뿐인데 툭 밀려나는 것처럼 보입니다.
        /// 제곱은 가장자리에서 거의 0으로 시작해 정말 겹칠 때만 세게 밀어냅니다.
        ///
        /// 반경 경계에서 값과 기울기가 모두 0이라 이웃이 드나들어도 속도가 튀지 않습니다.
        /// </summary>
        /// <param name="position">현재 위치입니다.</param>
        /// <param name="neighborPosition">이웃의 위치입니다.</param>
        /// <param name="separationRadius">분리가 작용하기 시작하는 거리입니다.</param>
        public static Vector3 SeparationFrom(Vector3 position, Vector3 neighborPosition, float separationRadius)
        {
            if (separationRadius <= 0.0001f)
            {
                return Vector3.zero;
            }

            Vector3 offset = position - neighborPosition;
            offset.y = 0f;

            float sqrDistance = offset.sqrMagnitude;
            if (sqrDistance < 0.000001f || sqrDistance > separationRadius * separationRadius)
            {
                return Vector3.zero;
            }

            float distance = Mathf.Sqrt(sqrDistance);

            // 가장자리에서 0, 완전히 겹치면 1. 제곱이라 경계 근처가 완만합니다.
            float falloff = 1f - distance / separationRadius;
            float strength = falloff * falloff;

            return offset / distance * strength;
        }
    }
}
