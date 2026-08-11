using UnityEngine;

namespace SRPG.Rendering
{
    /// <summary>
    /// 이번 프레임에 쓸 픽셀 격자의 크기를 정합니다.
    ///
    /// <b>왜 한 곳에 모아 두는가</b>
    ///
    /// 이 값을 두 곳이 씁니다 — 렌더러 피처가 내부 해상도를 잡을 때, 그리고
    /// 카메라가 격자에 맞춰 설 때입니다. 둘이 <b>같은 수를 써야</b> 합니다.
    /// 어긋나면 카메라는 A 격자에 맞춰 서는데 화면은 B 격자로 잘려,
    /// 붙잡는 시늉만 하고 화면은 그대로 기어다닙니다.
    ///
    /// 각자 계산하게 두면 언젠가 한쪽만 고쳐집니다. 계산은 여기 한 번만 적습니다.
    ///
    /// <b>왜 공용 어셈블리에 있는가</b>
    ///
    /// 이름은 렌더링이지만 하는 일은 정수 산술뿐이고, 유니티에서 쓰는 것은 <c>Mathf</c> 와
    /// 카메라 하나가 전부입니다. 값을 담는 <c>PixelGridSettings</c> 는 에셋이라 데이터 계층에 있어야 하고,
    /// 그것을 쓰는 렌더러 피처와 카메라는 게임플레이 계층에 있습니다.
    /// 계산을 게임플레이 쪽에 두면 데이터가 게임플레이를 참조해야 해서 <b>어셈블리가 순환합니다</b>.
    /// 양쪽 아래에 있는 공용 계층이 이 식이 놓일 수 있는 유일한 자리입니다.
    /// </summary>
    public static class PixelGrid
    {
        /// <summary>
        /// 화면 높이를 정수로 나눌 수 있는 가장 가까운 내부 높이를 구합니다.
        ///
        /// <b>왜 정수 배율이어야 하는가</b>
        ///
        /// 내부 픽셀 하나가 화면 픽셀 3.7개를 덮으면, 어떤 것은 3개 어떤 것은 4개가 됩니다.
        /// 그 들쭉날쭉함이 확대할 때마다 자리를 바꾸며 <b>줄무늬처럼 흘러갑니다</b>.
        /// 정확히 N개씩 덮으면 모든 픽셀이 같은 크기가 되고 흐를 것이 없습니다.
        ///
        /// 그래서 이 함수가 돌려주는 높이는 연속적이지 않고 <b>계단</b>입니다 —
        /// 1080 화면이면 540 · 360 · 270 · 216 · 180 … 순으로 뜁니다.
        /// 줌에 따라 부드럽게 변하지 않는 것이 결함이 아니라 목적입니다.
        /// </summary>
        /// <param name="screenHeight">실제 화면의 세로 픽셀 수입니다.</param>
        /// <param name="desiredHeight">이상적으로 쓰고 싶은 내부 세로 픽셀 수입니다.</param>
        /// <returns>화면 높이를 정수로 나눈 내부 세로 픽셀 수입니다.</returns>
        public static int SnapToIntegerScale(int screenHeight, float desiredHeight)
        {
            if (screenHeight <= 0 || desiredHeight <= 0f)
            {
                return Mathf.Max(16, Mathf.RoundToInt(desiredHeight));
            }

            int scale = Mathf.Max(1, Mathf.RoundToInt(screenHeight / desiredHeight));

            return Mathf.Max(16, screenHeight / scale);
        }

        /// <summary>
        /// 줌을 반영한 내부 세로 해상도를 구합니다.
        ///
        /// <b>무엇을 일정하게 유지하는가</b>
        ///
        /// 한 픽셀이 덮는 <b>월드 크기</b>를 일정하게 둡니다.
        /// 그러지 않으면 줌인할 때 병사가 자기 스프라이트보다 촘촘해 보이고,
        /// 줌아웃하면 한 픽셀보다 작아져 뭉갭니다. 둘 다 픽셀아트의 환상을 깹니다.
        ///
        /// 보이는 월드 높이는 거리에 비례하므로, 내부 해상도도 거리에 비례시키면
        /// 픽셀 하나가 덮는 월드 크기가 그대로 유지됩니다.
        ///
        /// <b>왜 셰이더가 스스로 줌을 알 수 없는가</b>
        ///
        /// 이 게임의 줌은 시야각이 아니라 <b>카메라 거리</b>로 구현되어 있습니다.
        /// 시야각은 처음부터 끝까지 같으므로 투영 행렬이 전혀 바뀌지 않고,
        /// 셰이더가 읽을 수 있는 것에는 "초점까지의 거리"가 없습니다.
        /// 그래서 이 값은 CPU 가 구해 넘겨야 합니다.
        /// </summary>
        /// <param name="screenHeight">실제 화면의 세로 픽셀 수입니다.</param>
        /// <param name="baseHeight">기준 거리에서 쓸 내부 세로 픽셀 수입니다.</param>
        /// <param name="referenceDistance">기준이 되는 카메라 거리입니다.</param>
        /// <param name="currentDistance">지금 카메라가 초점에서 떨어진 거리입니다.</param>
        /// <param name="minHeight">아무리 줌인해도 이보다 거칠어지지 않습니다.</param>
        /// <param name="maxHeight">아무리 줌아웃해도 이보다 촘촘해지지 않습니다.</param>
        /// <returns>정수 배율로 맞춰진 내부 세로 픽셀 수입니다.</returns>
        public static int ResolveHeight(
            int screenHeight,
            int baseHeight,
            float referenceDistance,
            float currentDistance,
            int minHeight,
            int maxHeight)
        {
            if (referenceDistance <= 0.01f || currentDistance <= 0.01f)
            {
                return SnapToIntegerScale(screenHeight, baseHeight);
            }

            float desired = baseHeight * (currentDistance / referenceDistance);

            desired = Mathf.Clamp(desired, minHeight, maxHeight);

            return SnapToIntegerScale(screenHeight, desired);
        }

        /// <summary>
        /// 카메라가 초점에서 떨어진 거리입니다.
        ///
        /// 카메라 리그가 피벗의 자식으로 카메라를 두고, 피벗이 곧 부대가 선 자리입니다.
        /// 그래서 부모까지의 거리가 그대로 줌의 정도가 됩니다.
        /// </summary>
        /// <param name="camera">잴 카메라입니다.</param>
        /// <returns>초점까지의 거리입니다. 초점을 알 수 없으면 0입니다.</returns>
        public static float ResolveFocusDistance(Camera camera)
        {
            if (camera == null)
            {
                return 0f;
            }

            var pivot = camera.transform.parent;

            return pivot != null ? Vector3.Distance(camera.transform.position, pivot.position) : 0f;
        }
    }
}
