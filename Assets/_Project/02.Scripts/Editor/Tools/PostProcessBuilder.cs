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
    /// 픽셀화와 외곽선은 여기 없습니다. <c>PixelArtFeature</c> 가 렌더러 피처로 따로 붙습니다.
    /// 볼륨으로는 저해상도 렌더 타깃을 잡을 수 없고, 외곽선은 깊이·노멀을 읽어야 하기 때문입니다.
    ///
    /// <b>순서에 뜻이 있습니다.</b>
    /// 피처가 <c>AfterRenderingPostProcessing</c> 에 붙으므로 <b>여기 값들이 먼저</b> 전체 해상도에서 걸리고,
    /// 그다음 화면이 저해상도로 줄면서 외곽선이 얹힙니다.
    /// 그래서 외곽선 색만은 톤매핑을 타지 않습니다 — 적어 둔 색이 그대로 나옵니다.
    ///
    /// <b>블룸을 켤 때 주의</b>
    ///
    /// 번짐은 전체 해상도에서 일어난 뒤 축소되므로 애써 만든 1픽셀 선을 직접 뭉개지는 않습니다.
    /// 다만 문턱을 낮추면 들판 전체가 뿌옇게 번져 <b>픽셀 경계의 또렷함</b>이 사라집니다.
    /// 픽셀아트에서 가장 먼저 의심할 항목이라 문턱을 높게 잡아 두었습니다.
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
        [MenuItem("SRPG/배선/⑨ 후처리 프로필 생성", priority = 38)]
        public static void BuildBattleProfile()
        {
            Directory.CreateDirectory(ProfileDirectory);

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(BattleProfilePath);

            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, BattleProfilePath);
            }

            // 끊긴 참조를 먼저 걷어냅니다.
            //
            // 예전 판이 컴포넌트를 만들어 목록에 넣고도 <b>하위 에셋으로 붙이지 않아서</b>,
            // 저장될 때 넷이 전부 null 로 기록되어 있었습니다.
            // 그 상태로 두면 목록에 빈 칸이 남아 TryGet 이 엉뚱하게 대답합니다.
            int broken = profile.components.RemoveAll(component => component == null);

            ConfigureTonemapping(profile);
            ConfigureColor(profile);
            ConfigureVignette(profile);
            ConfigureBloom(profile);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            // 하위 에셋을 더한 뒤에는 다시 읽어야 인스펙터가 새 목록을 봅니다.
            AssetDatabase.ImportAsset(BattleProfilePath, ImportAssetOptions.ForceUpdate);

            Debug.Log(
                $"[PostProcessBuilder] 후처리 프로필을 구웠습니다 — 항목 {profile.components.Count}개" +
                (broken > 0 ? $", 끊긴 참조 {broken}개를 걷어냄" : string.Empty) +
                $": {BattleProfilePath}",
                profile);
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
            if (profile.TryGet<T>(out var existing) && existing != null)
            {
                Persist(existing, profile);

                return existing;
            }

            var created = profile.Add<T>();

            // 이름을 붙여 둡니다. 하위 에셋은 이름으로 구분되고,
            // 비어 있으면 프로젝트 창에서 무엇이 무엇인지 알 수 없습니다.
            created.name = typeof(T).Name;

            Persist(created, profile);

            return created;
        }

        /// <summary>
        /// 컴포넌트를 프로필 <b>안에</b> 저장합니다.
        ///
        /// <b>이 한 단계가 빠져 있었습니다.</b>
        ///
        /// <c>VolumeProfile.Add&lt;T&gt;</c> 는 컴포넌트를 메모리에 만들어 목록에 넣을 뿐입니다.
        /// 그것은 어느 에셋에도 속하지 않은 객체라, 프로필을 저장하면 <b>참조가 null 로 기록</b>됩니다.
        /// 오류는 나지 않습니다 — 다음에 열어 보면 목록에 빈 칸만 남아 있고,
        /// 화면에서는 "후처리가 왜 안 걸리지"로만 보입니다.
        ///
        /// 인스펙터에서 프로필을 편집할 때는 유니티의 볼륨 편집기가 이 일을 대신 해 줍니다.
        /// 코드로 구울 때는 우리가 해야 합니다.
        ///
        /// <b>감춥니다.</b> 프로젝트 창에서 프로필을 펼치면 컴포넌트가 낱개로 늘어서는데,
        /// 그것은 따로 만질 것이 아니라 프로필의 일부입니다. 유니티도 같은 플래그를 씁니다.
        /// </summary>
        /// <param name="component">저장할 컴포넌트입니다.</param>
        /// <param name="profile">그것이 속할 프로필입니다.</param>
        private static void Persist(VolumeComponent component, VolumeProfile profile)
        {
            if (component == null || AssetDatabase.Contains(component))
            {
                return;
            }

            component.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;

            AssetDatabase.AddObjectToAsset(component, profile);
        }
    }
}
