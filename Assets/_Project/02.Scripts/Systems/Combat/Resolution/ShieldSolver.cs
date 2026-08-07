using UnityEngine;

namespace SRPG.Systems.Combat
{
    /// <summary>
    /// 방패가 투사체를 얼마나 막아 내는지 계산합니다.
    ///
    /// 조사에서 확인한 두 가지를 그대로 옮긴 규칙입니다.
    ///   · 방패는 <b>정면</b>에서 오는 화살을 막는다 → 측면과 후방이 약점이 된다
    ///   · <b>고지대에서 쏘면 가장 안 통한다</b> → 위에서 내리꽂히면 방패 위를 넘는다
    ///
    /// <b>왜 별도 클래스로 분리했는가</b>
    ///
    /// 예전에는 이 판정이 <c>Arrow</c> 안에 있었고, 상방 기준값이 <c>const 0.78f</c> 였습니다.
    /// 그런데 이 값은 병과 에셋의 발사각(<c>ArcLaunchAngleDegrees</c>, 기본 40도)과 물려 있습니다.
    /// 평지에서 쏜 곡사도 발사각만큼 기울어 떨어지므로(sin 40° ≈ 0.64),
    /// 기준을 발사각보다 낮게 두면 <b>모든 화살이 상방으로 판정되어</b> 방패병이 통째로 무적이 됩니다.
    ///
    /// 상수와 에셋 필드는 서로를 강제할 수단이 없습니다.
    /// 기획자가 발사각을 55도로 올려도 컴파일 오류도 런타임 경고도 나지 않고, 밸런스만 조용히 무너집니다.
    /// 그래서 기준값을 <b>발사각으로부터 파생</b>시키고, 그 계산을 MonoBehaviour 밖으로 꺼내
    /// EditMode 테스트가 직접 검증할 수 있게 했습니다.
    /// </summary>
    public static class ShieldSolver
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// 방패가 막아 내는 정면 각도의 코사인 기준값입니다.
        /// 약 70도로, 정면을 중심으로 좌우 넓게 막습니다.
        /// </summary>
        public const float FrontalBlockDot = 0.35f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 상방 판정의 기준값(하강 방향 y 성분의 절댓값)을 구합니다.
        ///
        /// 곡사는 대칭 포물선이므로 평지에서는 발사각이 곧 하강각입니다.
        /// 여기에 여유각을 더한 각도보다 <b>가파르게</b> 떨어져야 "위에서 내리꽂았다"로 봅니다.
        /// 그래야 평지 사격은 통과시키고 고지대 사격만 걸러 냅니다.
        ///
        /// 발사각 40도 + 여유각 11도 = 51도이며, sin 51° ≈ 0.777 입니다.
        /// 예전에 손으로 박아 두었던 상수 0.78 과 사실상 같은 값이 자동으로 나옵니다.
        /// </summary>
        /// <param name="arcAngleDegrees">평지 기준 하강각(도)입니다.</param>
        /// <param name="steepMarginDegrees">평지 사격보다 얼마나 더 가팔라야 상방으로 볼지(도)입니다.</param>
        public static float GetSteepBlockThreshold(float arcAngleDegrees, float steepMarginDegrees)
        {
            float degrees = Mathf.Clamp(arcAngleDegrees + steepMarginDegrees, 0f, 90f);
            return Mathf.Sin(degrees * Mathf.Deg2Rad);
        }

        /// <summary>
        /// 정면에서 막혔는지 판정합니다.
        /// 수평면에서만 봅니다. 고개를 든 각도까지 따지면 판정이 불안정해집니다.
        /// </summary>
        /// <param name="incomingDirection">투사체가 진행한 방향입니다.</param>
        /// <param name="victimForward">피격자가 바라보는 방향입니다.</param>
        public static bool IsBlockedFromFront(Vector3 incomingDirection, Vector3 victimForward)
        {
            Vector3 facing = new Vector3(victimForward.x, 0f, victimForward.z);
            Vector3 incoming = new Vector3(incomingDirection.x, 0f, incomingDirection.z);

            if (facing.sqrMagnitude < 0.0001f || incoming.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            // 화살의 진행 방향을 뒤집으면 "화살이 날아온 쪽"이 됩니다. 그쪽을 보고 있으면 막습니다.
            return Vector3.Dot(facing.normalized, -incoming.normalized) > FrontalBlockDot;
        }

        /// <summary>
        /// 위에서 내리꽂혀 막혔는지 판정합니다.
        /// </summary>
        /// <param name="incomingDirection">투사체가 진행한 방향입니다.</param>
        /// <param name="arcAngleDegrees">평지 기준 하강각(도)입니다.</param>
        /// <param name="steepMarginDegrees">상방으로 인정할 추가 각도(도)입니다.</param>
        public static bool IsBlockedFromAbove(Vector3 incomingDirection, float arcAngleDegrees, float steepMarginDegrees)
        {
            if (incomingDirection.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            float descent = -incomingDirection.normalized.y;
            return descent > GetSteepBlockThreshold(arcAngleDegrees, steepMarginDegrees);
        }

        /// <summary>
        /// 방패 저항이 적용된 뒤 남는 피해 비율을 구합니다. 1이면 그대로 들어가고, 0에 가까우면 거의 막힙니다.
        ///
        /// 반환값은 피해뿐 아니라 <b>넉백과 경직에도 같은 비율로</b> 곱해집니다.
        /// 막아 낸 화살에 밀려나면 방패를 든 의미가 사라지기 때문입니다.
        /// </summary>
        /// <param name="incomingDirection">투사체가 진행한 방향입니다.</param>
        /// <param name="victimForward">피격자가 바라보는 방향입니다.</param>
        /// <param name="projectileResistance">피격자의 투사체 피해 감소율(0~1)입니다.</param>
        /// <param name="arcAngleDegrees">평지 기준 하강각(도)입니다.</param>
        /// <param name="steepMarginDegrees">상방으로 인정할 추가 각도(도)입니다.</param>
        public static float ComputeBlockFactor(
            Vector3 incomingDirection,
            Vector3 victimForward,
            float projectileResistance,
            float arcAngleDegrees,
            float steepMarginDegrees)
        {
            if (projectileResistance <= 0f)
            {
                return 1f;
            }

            bool blocked = IsBlockedFromFront(incomingDirection, victimForward)
                           || IsBlockedFromAbove(incomingDirection, arcAngleDegrees, steepMarginDegrees);

            // 측면과 후방은 방패가 못 막습니다.
            return blocked ? 1f - Mathf.Clamp01(projectileResistance) : 1f;
        }
    }
}
