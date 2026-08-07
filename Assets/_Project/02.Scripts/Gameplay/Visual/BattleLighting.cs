using UnityEngine;
using UnityEngine.Rendering;

namespace SRPG.Gameplay.Visual
{
    /// <summary>
    /// 전투 조명의 기준값입니다. 방향광과 환경광을 한 곳에서 정합니다.
    ///
    /// <b>왜 한 곳에 모으는가</b>
    ///
    /// 같은 숫자가 씬 빌더와 부트스트랩 두 곳에 적혀 있었습니다.
    /// 한쪽만 고치면 아무 오류 없이 조용히 어긋나고, 그 어긋남은
    /// "에디터에서 만든 씬"과 "런타임에 만든 씬"의 인상이 다른 형태로만 드러납니다.
    /// 컴파일도 되고 테스트도 통과하는데 화면만 다른, 가장 찾기 힘든 종류의 버그입니다.
    ///
    /// <b>셰이더와의 약속</b>
    ///
    /// 지형과 유닛 빌보드는 같은 half lambert 곡선을 쓰고, 같은 <c>_AmbientBoost</c>를 씁니다.
    /// 빌보드의 노멀은 위쪽으로 고정되어 있어 <b>평지의 지형면과 같은 노멀</b>이 됩니다.
    /// 그래서 평지에 선 유닛은 발밑의 땅과 정확히 같은 밝기가 됩니다.
    ///
    /// 이 대응이 깨지면 유닛만 다른 세계의 조명을 받는 것처럼 떠 보입니다.
    /// 방향광의 각도나 세기를 바꿀 때는 두 셰이더의 결과를 같이 봐야 합니다.
    /// </summary>
    public static class BattleLighting
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// 방향광의 각도입니다.
        ///
        /// 48도로 내려다보는 각도는 카메라의 부감각(47도)과 거의 같습니다.
        /// 빛과 시선이 나란하면 절벽의 그림자가 카메라 쪽으로 드리워 지형을 가립니다.
        /// 그래서 방위각을 138도로 크게 틀어, 그림자가 화면의 옆쪽으로 눕게 했습니다.
        /// </summary>
        public static readonly Vector3 DirectionalAngles = new Vector3(48f, 138f, 0f);

        /// <summary>방향광의 세기입니다.</summary>
        public const float DirectionalIntensity = 1.15f;

        /// <summary>방향광의 색입니다. 살짝 따뜻하게 기울여 바다의 푸른 환경광과 대비시킵니다.</summary>
        public static readonly Color DirectionalColor = new Color(1f, 0.97f, 0.9f);

        /// <summary>하늘 쪽 환경광입니다.</summary>
        public static readonly Color AmbientSky = new Color(0.52f, 0.58f, 0.66f);

        /// <summary>수평 방향 환경광입니다.</summary>
        public static readonly Color AmbientEquator = new Color(0.36f, 0.38f, 0.42f);

        /// <summary>땅 쪽 환경광입니다.</summary>
        public static readonly Color AmbientGround = new Color(0.20f, 0.20f, 0.24f);

        /// <summary>
        /// 빛을 등진 면의 밝기 하한입니다. 지형·빌보드 셰이더의 <c>_AmbientBoost</c> 기본값과 같아야 합니다.
        /// </summary>
        public const float AmbientBoost = 0.35f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 환경광을 설정합니다.
        /// 기본 스카이박스를 그대로 두면 절벽 그늘이 새까맣게 죽어 지형이 안 읽힙니다.
        /// </summary>
        public static void ApplyAmbient()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            RenderSettings.fog = false;
        }

        /// <summary>
        /// 방향광 하나를 기준값에 맞춰 설정합니다.
        /// </summary>
        /// <param name="light">설정할 조명입니다.</param>
        public static void ApplyDirectional(Light light)
        {
            if (light == null)
            {
                return;
            }

            light.transform.rotation = Quaternion.Euler(DirectionalAngles);

            light.type = LightType.Directional;
            light.intensity = DirectionalIntensity;
            light.color = DirectionalColor;
            light.shadows = LightShadows.Soft;
        }
    }
}
