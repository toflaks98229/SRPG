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
        /// 화면에 담기는 월드 높이에 내부 해상도를 비례시키면
        /// 픽셀 하나가 덮는 월드 크기가 그대로 유지됩니다.
        ///
        /// <b>왜 셰이더가 스스로 줌을 알 수 없는가</b>
        ///
        /// 직교 투영에서 줌은 <c>orthographicSize</c> 이고, 그것은 투영 행렬에 들어 있습니다.
        /// 그러나 후처리 셰이더가 받는 것은 이미 그려진 그림 한 장이라
        /// <b>그 그림이 얼마만 한 월드를 담고 있는지</b>를 알 방법이 없습니다.
        /// 그래서 이 값은 CPU 가 구해 넘겨야 합니다.
        /// </summary>
        /// <param name="screenHeight">실제 화면의 세로 픽셀 수입니다.</param>
        /// <param name="baseHeight">기준 줌에서 쓸 내부 세로 픽셀 수입니다.</param>
        /// <param name="referenceExtent">기준이 되는 화면 높이의 절반(월드 단위)입니다.</param>
        /// <param name="currentExtent">지금 화면에 담기는 높이의 절반입니다.</param>
        /// <param name="minHeight">아무리 줌인해도 이보다 거칠어지지 않습니다.</param>
        /// <param name="maxHeight">아무리 줌아웃해도 이보다 촘촘해지지 않습니다.</param>
        /// <returns>정수 배율로 맞춰진 내부 세로 픽셀 수입니다.</returns>
        public static int ResolveHeight(
            int screenHeight,
            int baseHeight,
            float referenceExtent,
            float currentExtent,
            int minHeight,
            int maxHeight)
        {
            if (referenceExtent <= 0.01f || currentExtent <= 0.01f)
            {
                return SnapToIntegerScale(screenHeight, baseHeight);
            }

            float desired = baseHeight * (currentExtent / referenceExtent);

            desired = Mathf.Clamp(desired, minHeight, maxHeight);

            return SnapToIntegerScale(screenHeight, desired);
        }

        /// <summary>
        /// 화면에 담기는 월드 높이의 <b>절반</b>입니다. 이 게임에서 "줌"이 곧 이 값입니다.
        ///
        /// <b>왜 거리가 아니라 이것인가</b>
        ///
        /// 예전에는 카메라와 초점 사이의 거리를 줌으로 삼았습니다. 원근이었기 때문입니다.
        /// 직교로 옮기면서 거리는 줌과 무관해졌습니다 — 카메라를 뒤로 물려도 화면은 그대로입니다.
        ///
        /// 두 투영을 하나로 잇는 개념이 이것입니다. 직교에서는 <c>orthographicSize</c> 그대로이고,
        /// 원근에서는 초점 평면에서의 값이 됩니다. <b>원근 경로가 남아 있는 이유</b>는
        /// 씬 뷰 카메라와 편집 중의 미리보기가 원근일 수 있기 때문입니다 —
        /// 게임 카메라는 직교이지만 이 함수는 그것을 강제할 수 없습니다.
        /// </summary>
        /// <param name="camera">잴 카메라입니다.</param>
        /// <returns>화면 높이의 절반(월드 단위)입니다. 구할 수 없으면 0입니다.</returns>
        public static float ResolveViewExtent(Camera camera)
        {
            if (camera == null)
            {
                return 0f;
            }

            if (camera.orthographic)
            {
                return camera.orthographicSize;
            }

            var pivot = camera.transform.parent;

            if (pivot == null)
            {
                return 0f;
            }

            float distance = Vector3.Distance(camera.transform.position, pivot.position);

            return distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        }

        /// <summary>
        /// 내부 픽셀 하나가 덮는 월드 길이입니다.
        ///
        /// <b>직교에서만 이 값이 화면 전체에 통합니다.</b>
        /// 원근에서는 깊이마다 달라져 한 평면에서만 맞고, 그래서 픽셀 격자를 온전히 붙잡을 수 없습니다.
        /// 이 게임이 전투 카메라를 직교로 옮긴 이유가 이 한 줄입니다.
        /// </summary>
        /// <param name="viewExtent">화면 높이의 절반(월드 단위)입니다.</param>
        /// <param name="internalHeight">내부 세로 픽셀 수입니다.</param>
        /// <returns>픽셀 하나가 덮는 월드 길이입니다. 구할 수 없으면 0입니다.</returns>
        public static float TexelSize(float viewExtent, int internalHeight)
        {
            if (viewExtent <= 0f || internalHeight <= 0)
            {
                return 0f;
            }

            return viewExtent * 2f / internalHeight;
        }

        /// <summary>
        /// 격자에서 벗어난 만큼을 월드 길이로 구합니다.
        ///
        /// 결과는 항상 텍셀 하나의 절반 안쪽입니다 — 가장 가까운 격자점을 기준으로 재기 때문입니다.
        /// </summary>
        /// <param name="along">어떤 축 방향으로 잰 좌표입니다.</param>
        /// <param name="texelSize">픽셀 하나가 덮는 월드 길이입니다.</param>
        /// <returns>격자에서 벗어난 길이입니다. 텍셀 크기가 0이면 0입니다.</returns>
        public static float SubTexelOffset(float along, float texelSize)
        {
            if (texelSize <= 0f)
            {
                return 0f;
            }

            return along - Mathf.Round(along / texelSize) * texelSize;
        }
    }
}
