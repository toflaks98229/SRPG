using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SRPG.Editor.Tools
{
    /// <summary>
    /// 전장의 후처리 프로필을 만들어 저장하는 도구입니다.
    ///
    /// <b>왜 코드로 굽는가</b>
    ///
    /// 이 프로젝트의 다른 에셋과 같은 이유입니다 — 값이 코드에 적혀 있으면
    /// 무엇을 왜 그렇게 두었는지가 함께 남고, 잃어버려도 메뉴 한 번으로 되돌아옵니다.
    /// 손으로 만든 프로필은 그 이유가 아무 데도 남지 않습니다.
    ///
    /// <b>여기 없는 것</b>
    ///
    /// 픽셀화는 후처리가 아닙니다. URP 가 <b>애초에 저해상도로 그리고</b>
    /// 점 필터로 확대합니다(파이프라인 에셋의 Render Scale · Upscaling Filter).
    /// 화면을 다 그린 뒤 뭉개는 것이 아니라 처음부터 적게 그리는 것이라,
    /// 그림이 더 정직하고 성능도 함께 좋아집니다.
    /// </summary>
    public static class PostProcessBuilder
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>후처리 프로필을 굽는 폴더입니다.</summary>
        private const string ProfileDirectory = "Assets/_Project/03.DataAssets/Rendering";

        /// <summary>전투 후처리 프로필의 경로입니다.</summary>
        public const string BattleProfilePath = ProfileDirectory + "/PostProcess_Battle.asset";

        // ====================================================================================================
        // 2. Menu Items
        // ====================================================================================================

        /// <summary>
        /// 전투 후처리 프로필을 만들어 저장합니다. 이미 있으면 값만 다시 맞춥니다.
        /// </summary>
        [MenuItem("SRPG/배선/⑦ 후처리 프로필 생성", priority = 36)]
        public static void BuildBattleProfile()
        {
            Directory.CreateDirectory(ProfileDirectory);

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(BattleProfilePath);

            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, BattleProfilePath);
            }

            ConfigureTonemapping(profile);
            ConfigureColor(profile);
            ConfigureVignette(profile);
            ConfigureBloom(profile);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            Debug.Log($"[PostProcessBuilder] 후처리 프로필을 만들었습니다: {BattleProfilePath}", profile);
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 색조 매핑입니다.
        ///
        /// <b>Neutral 을 씁니다.</b> ACES 는 밝은 곳을 강하게 눌러 색을 바래게 만드는데,
        /// 이 게임의 색은 셰이더가 이미 정해 둔 것이라 그것을 다시 주무르면 안 됩니다.
        /// 여기서 하는 일은 값이 1을 넘겼을 때 부드럽게 접어 두는 것뿐입니다.
        /// </summary>
        private static void ConfigureTonemapping(VolumeProfile profile)
        {
            var tonemapping = GetOrAdd<Tonemapping>(profile);

            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.Neutral;
        }

        /// <summary>
        /// 색 보정입니다.
        ///
        /// 픽셀아트는 색이 <b>또렷해야</b> 읽힙니다. 저해상도로 그리면 한 픽셀이 넓어져
        /// 색 하나하나가 곧 정보가 되기 때문입니다.
        /// 다만 채도를 크게 올리면 셰이더가 좁혀 놓은 색조 폭이 다시 벌어집니다.
        /// </summary>
        private static void ConfigureColor(VolumeProfile profile)
        {
            var color = GetOrAdd<ColorAdjustments>(profile);

            color.postExposure.overrideState = true;
            color.postExposure.value = 0.05f;

            color.contrast.overrideState = true;
            color.contrast.value = 12f;

            color.saturation.overrideState = true;
            color.saturation.value = 8f;
        }

        /// <summary>
        /// 가장자리를 살짝 어둡게 눌러 시선을 가운데로 모읍니다.
        ///
        /// 전술 게임에서는 화면 가운데에 부대가 있습니다. 진하게 걸면 가장자리의 적이
        /// 안 보이게 되므로 <b>연출을 얻고 게임을 잃습니다</b>. 아주 얕게만 겁니다.
        /// </summary>
        private static void ConfigureVignette(VolumeProfile profile)
        {
            var vignette = GetOrAdd<Vignette>(profile);

            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.18f;

            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.5f;
        }

        /// <summary>
        /// 밝은 곳이 살짝 번지게 합니다.
        ///
        /// <b>문턱을 높게 잡습니다.</b> 낮게 두면 들판 전체가 뿌옇게 번져
        /// 애써 만든 픽셀 경계가 뭉개집니다. 물의 반짝임과 잎을 통과한 빛만 걸리게 합니다.
        /// </summary>
        private static void ConfigureBloom(VolumeProfile profile)
        {
            var bloom = GetOrAdd<Bloom>(profile);

            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.1f;

            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.35f;

            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.6f;
        }

        /// <summary>
        /// 프로필에서 컴포넌트를 얻거나, 없으면 더합니다.
        ///
        /// 여러 번 실행해도 같은 결과가 되게 합니다 — 다시 구울 때마다
        /// 같은 컴포넌트가 겹겹이 쌓이면 어느 것이 먹히는지 알 수 없어집니다.
        /// </summary>
        /// <typeparam name="T">얻을 볼륨 컴포넌트입니다.</typeparam>
        /// <param name="profile">대상 프로필입니다.</param>
        /// <returns>프로필에 들어 있는 컴포넌트입니다.</returns>
        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            return profile.TryGet<T>(out var existing) ? existing : profile.Add<T>();
        }
    }
}
