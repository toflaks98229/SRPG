using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 픽셀 격자의 크기를 정하는 에셋입니다.
    ///
    /// <b>왜 한 에셋에 모으는가</b>
    ///
    /// 이 값을 두 곳이 씁니다 — 화면을 저해상도로 그리는 렌더러 피처와,
    /// 그 격자에 맞춰 서는 카메라입니다. 둘이 <b>같은 수를 써야만</b> 화면이 붙잡힙니다.
    ///
    /// 처음에는 양쪽이 각자 값을 들고 있었습니다. 계산식은 한 곳에 모았지만
    /// 입력이 둘이라 손으로 맞춰야 했고, 어긋나도 <b>컴파일이 통과하고 오류도 나지 않습니다</b>.
    /// 증상은 "카메라가 붙잡는 시늉만 하고 화면은 그대로 기어다닌다" 하나뿐이라,
    /// 원인이 설정 불일치라는 것을 알아내기가 매우 어렵습니다.
    ///
    /// 에셋 하나를 둘이 참조하면 어긋날 자리가 사라집니다.
    ///
    /// <b>없어도 됩니다.</b> 연결되지 않으면 양쪽 모두 <see cref="CreateDefault"/> 를 씁니다.
    /// 다만 그때는 각자 만든 기본값이므로 <b>둘 다 비어 있어야</b> 합니다 — 한쪽만 연결하면 어긋납니다.
    /// </summary>
    [CreateAssetMenu(fileName = "PixelGrid_", menuName = "SRPG/픽셀 격자", order = 42)]
    public sealed class PixelGridSettings : ScriptableObject
    {
        // ====================================================================================================
        // 1. Inspector
        // ====================================================================================================

        [Header("해상도")]
        [Range(16, 1080)]
        [Tooltip("기준 거리에서 그릴 내부 세로 픽셀 수입니다. 가로는 화면 비율로 따라옵니다.\n" +
                 "180이면 320×180, 270이면 480×270입니다. 낮출수록 픽셀이 굵어집니다.")]
        public int InternalHeight = 270;

        [Header("줌 연동")]
        [Tooltip("줌에 따라 내부 해상도를 바꿉니다.\n" +
                 "끄면 화면 픽셀 크기가 늘 같고, 켜면 한 픽셀이 덮는 월드 크기가 늘 같습니다.\n" +
                 "켜는 쪽이 병사 스프라이트의 픽셀 밀도와 화면 픽셀이 어긋나지 않습니다.")]
        public bool ZoomAdaptive = true;

        [Range(4f, 120f)]
        [Tooltip("위의 내부 해상도가 적용되는 기준 카메라 거리입니다.\n" +
                 "전투 리그의 기본 거리(34)에 맞춰 두면 지금 보이는 굵기가 기준이 됩니다.")]
        public float ReferenceDistance = 34f;

        [Range(16, 1080)]
        [Tooltip("줌인해도 이보다 거칠어지지 않습니다.")]
        public int MinHeight = 140;

        [Range(16, 2160)]
        [Tooltip("줌아웃해도 이보다 촘촘해지지 않습니다.")]
        public int MaxHeight = 540;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 이번 프레임에 쓸 내부 세로 해상도를 구합니다.
        ///
        /// 계산은 <see cref="Rendering.PixelGrid"/> 가 합니다. 여기서는 값만 넘깁니다 —
        /// 식이 두 곳에 있으면 언젠가 한쪽만 고쳐집니다.
        /// </summary>
        /// <param name="screenHeight">실제 화면의 세로 픽셀 수입니다.</param>
        /// <param name="focusDistance">카메라가 초점에서 떨어진 거리입니다.</param>
        /// <returns>내부 세로 픽셀 수입니다.</returns>
        public int ResolveHeight(int screenHeight, float focusDistance)
        {
            if (!ZoomAdaptive)
            {
                return Mathf.Clamp(InternalHeight, 16, Mathf.Max(16, screenHeight));
            }

            return Rendering.PixelGrid.ResolveHeight(
                screenHeight, InternalHeight, ReferenceDistance, focusDistance, MinHeight, MaxHeight);
        }

        // ====================================================================================================
        // 3. Factory
        // ====================================================================================================

        /// <summary>
        /// 에셋 없이 코드로 기본 격자를 만듭니다.
        /// </summary>
        /// <returns>필드 초기값이 그대로 담긴 설정입니다. 에셋으로 저장되지 않습니다.</returns>
        public static PixelGridSettings CreateDefault()
        {
            var settings = CreateInstance<PixelGridSettings>();
            settings.name = "PixelGrid_Default";

            return settings;
        }
    }
}
