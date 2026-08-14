using UnityEngine;
using UnityEngine.Serialization;

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
        // 0. Schema
        // ====================================================================================================

        /// <summary>
        /// 지금 코드가 기대하는 스키마 판입니다.
        ///
        /// <b>2판에서 무엇이 바뀌었는가</b>
        ///
        /// 전투 카메라를 원근에서 직교로 옮기면서 줌의 단위가 바뀌었습니다.
        /// 예전에는 <c>ReferenceDistance</c>(카메라 거리, 34)였고 지금은
        /// <see cref="ReferenceExtent"/>(화면 높이의 절반, 약 19.6)입니다.
        ///
        /// <b>이름만 바꿔서는 안 됩니다.</b> 같은 34가 새 단위에서는 전혀 다른 줌을 뜻합니다 —
        /// 그대로 두면 기준 해상도가 적용되는 지점이 통째로 어긋나고, 오류는 나지 않습니다.
        /// 그래서 <see cref="MigrateToCurrentSchema"/> 가 값을 실제로 환산합니다.
        /// </summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>
        /// 1판이 쓰던 시야각입니다. 값을 환산할 때만 씁니다.
        ///
        /// 유니티 카메라의 기본값이고 이 프로젝트도 손대지 않았습니다.
        /// 환산식이 이 값에 기대므로 상수로 남겨 둡니다 — 나중에 이 줄만 보고도 왜 0.577이 나왔는지 알 수 있게.
        /// </summary>
        private const float LegacyFieldOfView = 60f;

        /// <summary>
        /// 이 에셋이 마지막으로 맞춰진 스키마 판입니다. 배선 도구가 관리합니다.
        ///
        /// <b>초기값이 0인 것이 핵심입니다.</b> 유니티는 YAML 에 없는 키를 만나면 필드 초기값을 그대로 둡니다.
        /// 최신 판을 적어 두면 이 필드가 생기기 전에 구워진 에셋이 "이미 최신"이라 대답해
        /// 이관이 영영 돌지 않습니다.
        /// </summary>
        [HideInInspector]
        [Tooltip("이 에셋이 마지막으로 갱신된 스키마 버전입니다. 배선 도구가 관리합니다.")]
        public int SchemaVersion;

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

        [Range(2f, 80f)]
        [Tooltip("위의 내부 해상도가 적용되는 기준 줌입니다. 화면에 담기는 월드 높이의 <b>절반</b>이며, " +
                 "직교 카메라의 Orthographic Size 와 같은 단위입니다.\n" +
                 "전투 리그의 기본 줌(19.5)에 맞춰 두면 지금 보이는 굵기가 기준이 됩니다.")]
        [FormerlySerializedAs("ReferenceDistance")]
        public float ReferenceExtent = 19.5f;

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
        /// <param name="viewExtent">지금 화면에 담기는 월드 높이의 절반입니다.</param>
        /// <returns>내부 세로 픽셀 수입니다.</returns>
        public int ResolveHeight(int screenHeight, float viewExtent)
        {
            if (!ZoomAdaptive)
            {
                return Mathf.Clamp(InternalHeight, 16, Mathf.Max(16, screenHeight));
            }

            return Rendering.PixelGrid.ResolveHeight(
                screenHeight, InternalHeight, ReferenceExtent, viewExtent, MinHeight, MaxHeight);
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
            settings.SchemaVersion = CurrentSchemaVersion;

            return settings;
        }

        /// <summary>
        /// 인스펙터에서 새로 만들거나 초기화할 때 유니티가 부릅니다.
        /// 갓 만든 에셋이 옛 판으로 표시되지 않게 판 번호를 적어 둡니다.
        /// </summary>
        private void Reset()
        {
            SchemaVersion = CurrentSchemaVersion;
        }

        // ====================================================================================================
        // 4. Migration
        // ====================================================================================================

        /// <summary>
        /// 에셋을 지금 스키마에 맞춥니다. 이미 최신이면 아무것도 하지 않습니다.
        ///
        /// <b>1판 → 2판: 거리를 화면 높이로 환산합니다</b>
        ///
        /// 옛 값은 카메라 거리였고, 그 거리에서 원근 카메라가 담던 높이의 절반이 새 값입니다.
        /// <c>extent = distance × tan(시야각 ÷ 2)</c> 이며 시야각 60도에서 계수는 약 0.577 입니다.
        /// 기본값이던 34는 19.6이 되어, <b>화면에 보이던 픽셀 굵기가 그대로 유지됩니다</b>.
        ///
        /// 이름만 이어받는 것으로는 부족했던 이유가 여기 있습니다 —
        /// <c>FormerlySerializedAs</c> 는 값을 옮겨 줄 뿐 단위를 바꿔 주지 않습니다.
        /// </summary>
        /// <returns>실제로 무언가 바꿨으면 true입니다.</returns>
        public bool MigrateToCurrentSchema()
        {
            if (SchemaVersion >= CurrentSchemaVersion)
            {
                return false;
            }

            if (ReferenceExtent > 0f)
            {
                ReferenceExtent *= Mathf.Tan(LegacyFieldOfView * 0.5f * Mathf.Deg2Rad);
            }
            else
            {
                ReferenceExtent = 19.5f;
            }

            SchemaVersion = CurrentSchemaVersion;

            return true;
        }
    }
}
