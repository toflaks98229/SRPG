using UnityEngine;

namespace SRPG.Systems.Rendering
{
    /// <summary>
    /// 빌보드 스프라이트가 <b>어느 방향 그림을 써야 하는지</b> 정합니다.
    ///
    /// <b>2.5D의 근본 문제</b>
    ///
    /// 빌보드는 언제나 카메라를 향합니다. 그래서 그림 자체로는 방향을 말할 수 없습니다.
    /// 그런데 이 게임에서 방향은 연출이 아니라 <b>규칙</b>입니다.
    /// 창은 정면 좁은 각도만 위험하고, 방패는 정면에서 온 화살만 막습니다.
    /// 플레이어가 "저 창병이 어디를 보고 있는가"를 못 읽으면 그 규칙들이 전부 무의미해집니다.
    ///
    /// <b>해법은 상대 각도입니다.</b>
    /// 유닛의 논리적 방향은 트랜스폼이 계속 들고 있습니다(무기 판정이 그걸 씁니다).
    /// 화면에 보일 그림은 그 방향과 <b>카메라가 보는 방향의 차이</b>로 정합니다.
    /// 카메라를 돌리면 같은 유닛이 다른 면을 보여 주게 되고, 그것이 곧 방향의 표현입니다.
    ///
    /// <b>좌우 반전</b>
    ///
    /// 8방향을 전부 그리면 비용이 두 배입니다. 오른쪽 절반만 그리고 뒤집어 쓰면 절반으로 줄지만,
    /// 조사에서 확인했듯 <b>비대칭 장비는 뒤집으면 망가집니다.</b>
    /// 방패를 왼팔에 든 병사를 뒤집으면 오른팔에 들게 되고, 활은 시위 방향이 뒤집힙니다.
    /// 그래서 반전 여부를 강제하지 않고 <b>선택으로 남깁니다.</b>
    /// 대칭인 것은 반전으로 아끼고, 비대칭인 것은 여덟 장을 다 그립니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 계산이라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public static class BillboardDirection
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>표준 방향 수입니다. 8방향이면 45도마다 한 장입니다.</summary>
        public const int DirectionCount = 8;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 유닛이 보는 방향과 카메라가 보는 방향의 차이를 각도로 구합니다.
        ///
        /// 0도면 유닛이 <b>카메라를 등지고</b> 있습니다(뒷모습).
        /// 180도면 카메라를 마주 봅니다(앞모습).
        /// </summary>
        /// <param name="unitForward">유닛의 논리적 정면입니다.</param>
        /// <param name="cameraForward">카메라가 보는 방향입니다.</param>
        /// <returns>0~360 사이의 각도입니다.</returns>
        public static float RelativeAngle(Vector3 unitForward, Vector3 cameraForward)
        {
            Vector3 unit = Flatten(unitForward);
            Vector3 camera = Flatten(cameraForward);

            if (unit.sqrMagnitude < 0.000001f || camera.sqrMagnitude < 0.000001f)
            {
                return 0f;
            }

            float unitYaw = Mathf.Atan2(unit.x, unit.z) * Mathf.Rad2Deg;
            float cameraYaw = Mathf.Atan2(camera.x, camera.z) * Mathf.Rad2Deg;

            float relative = unitYaw - cameraYaw;

            // 0~360으로 접습니다. 음수 나머지를 그대로 두면 인덱스가 음수가 됩니다.
            relative %= 360f;
            if (relative < 0f)
            {
                relative += 360f;
            }

            return relative;
        }

        /// <summary>
        /// 상대 각도를 스프라이트 번호로 바꿉니다.
        ///
        /// 각 구간의 <b>가운데</b>가 대표 각도가 되도록 반 칸 밀어서 반올림합니다.
        /// 그러지 않으면 정면을 보고 있는데도 비스듬한 그림이 나옵니다.
        /// </summary>
        /// <param name="relativeAngle">0~360 상대 각도입니다.</param>
        /// <param name="directionCount">쓸 방향 수입니다. 보통 8입니다.</param>
        /// <returns>0부터 <paramref name="directionCount"/>−1 사이의 방향 칸 번호입니다.</returns>
        public static int ToIndex(float relativeAngle, int directionCount = DirectionCount)
        {
            int count = Mathf.Max(1, directionCount);
            float step = 360f / count;

            int index = Mathf.RoundToInt(relativeAngle / step);

            // 마지막 구간이 한 바퀴 돌아 0번으로 돌아옵니다.
            return ((index % count) + count) % count;
        }

        /// <summary>
        /// 좌우 반전을 써서 절반의 그림으로 여덟 방향을 표현합니다.
        ///
        /// <b>대칭인 유닛에만 쓸 수 있습니다.</b>
        /// 방패나 활처럼 한쪽에 쏠린 장비가 있으면 뒤집는 순간 반대 손으로 옮겨 갑니다.
        /// </summary>
        /// <param name="index">원래 방향 번호입니다.</param>
        /// <param name="directionCount">전체 방향 수입니다.</param>
        /// <param name="mirrored">뒤집어 그려야 하면 true입니다.</param>
        /// <returns>실제로 그릴 그림의 번호입니다.</returns>
        public static int ToMirroredIndex(int index, int directionCount, out bool mirrored)
        {
            int count = Mathf.Max(1, directionCount);
            int wrapped = ((index % count) + count) % count;

            int half = count / 2;

            // 앞쪽 절반은 그대로 씁니다.
            if (half <= 0 || wrapped <= half)
            {
                mirrored = false;
                return wrapped;
            }

            // 뒤쪽 절반은 거울에 비친 짝을 씁니다.
            mirrored = true;
            return count - wrapped;
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 수평면으로 눕힙니다.
        ///
        /// 카메라가 내려다보고 있으므로 그 전방 벡터에는 큰 아래 성분이 있습니다.
        /// 그대로 쓰면 카메라 피치가 방향 판정에 섞여 들어옵니다.
        /// </summary>
        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            return direction;
        }
    }
}
