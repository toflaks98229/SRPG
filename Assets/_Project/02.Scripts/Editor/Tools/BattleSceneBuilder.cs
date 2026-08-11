using System.IO;
using SRPG.Composition;
using SRPG.Core.Managers;
using SRPG.Data;
using SRPG.Gameplay.CameraControl;
using SRPG.Gameplay.Visual;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SRPG.Editor.Tools
{
    /// <summary>
    /// 전투 프로토타입 씬을 만들어 저장하는 에디터 도구입니다.
    ///
    /// 씬 파일을 수작업으로 구성하는 대신 코드로 생성합니다.
    /// 구성이 바뀔 때마다 메뉴 한 번으로 씬을 다시 만들 수 있고, 씬 파일의 병합 충돌도 줄어듭니다.
    ///
    /// 씬에 배치되는 것은 "런타임에 만들 수 없거나, 만들면 손해인 것"으로 한정합니다.
    ///   · 전역 스코프 + 그 아래 매니저 — 앱 수명 동안 유지되어야 하는 것
    ///   · 카메라 / 조명 / 조명 환경 설정 — 씬 에셋에 저장되어야 하는 것
    ///   · 부트스트랩 + 구성 에셋 참조 — 기획자가 인스펙터에서 바꿔야 하는 것
    ///   · 생성물이 담길 빈 루트 — 하이라키를 정돈하기 위한 것
    /// 지형·유닛·상륙정은 절차적으로 만들어지므로 씬에 넣지 않습니다.
    /// </summary>
    public static class BattleSceneBuilder
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>전투 씬을 굽는 폴더입니다.</summary>
        private const string SceneDirectory = "Assets/_Project/01.Scenes/Battle";
        /// <summary>전투 씬 에셋의 경로입니다.</summary>
        private const string ScenePath = SceneDirectory + "/Battle.unity";

        /// <summary>월드맵 씬을 굽는 폴더입니다.</summary>
        private const string WorldMapDirectory = "Assets/_Project/01.Scenes/WorldMap";
        /// <summary>월드맵 씬 에셋의 경로입니다.</summary>
        private const string WorldMapScenePath = WorldMapDirectory + "/WorldMap.unity";

        // ====================================================================================================
        // 2. Menu Items
        // ====================================================================================================

        /// <summary>
        /// 프로토타입 에셋을 만든 뒤 전투 씬을 새로 굽습니다. 처음 세팅할 때 이것만 누르면 됩니다.
        /// </summary>
        [MenuItem("SRPG/전체 세팅 (에셋 + 씬)", priority = 0)]
        public static void BuildEverything()
        {
            PrototypeAssetBuilder.BuildAll();
            CreateBattleScene();
            CreateWorldMapScene();
        }

        /// <summary>
        /// 월드맵 씬을 새로 만들어 저장합니다.
        ///
        /// <b>여기가 게임의 시작 씬입니다.</b>
        /// 캠페인 스코프가 여기서 서고, 전투는 그 아래에서 한 판씩 열립니다.
        /// 그래서 빌드 목록에서 이 씬이 전투 씬보다 앞에 와야 합니다.
        /// </summary>
        [MenuItem("SRPG/월드맵 씬 생성 및 열기", priority = 12)]
        public static void CreateWorldMapScene()
        {
            if (!Application.isBatchMode)
            {
                if (File.Exists(WorldMapScenePath) &&
                    !EditorUtility.DisplayDialog(
                        "월드맵 씬 덮어쓰기",
                        $"이미 {WorldMapScenePath} 이 있습니다.\n새로 만들어 덮어쓸까요?",
                        "덮어쓰기",
                        "취소"))
                {
                    return;
                }

                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return;
                }
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateGameSystems();
            CreateCampaign();

            // 월드맵은 아직 표현이 없어 IMGUI 화면만 뜹니다.
            // 그래도 카메라와 오디오 리스너는 있어야 합니다 — 없으면 화면이 검고 소리가 나지 않습니다.
            var environment = new GameObject("Environment").transform;

            var cameraObject = new GameObject("WorldMapCamera");
            cameraObject.transform.SetParent(environment, false);
            cameraObject.tag = "MainCamera";

            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.09f, 0.13f);
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraObject.AddComponent<AudioListener>();

            Directory.CreateDirectory(WorldMapDirectory);
            EditorSceneManager.SaveScene(scene, WorldMapScenePath);
            AssetDatabase.Refresh();

            RegisterInBuildSettings(WorldMapScenePath, true);

            Debug.Log(
                $"[BattleSceneBuilder] 월드맵 씬을 만들었습니다: {WorldMapScenePath}\n" +
                "재생하면 부대 장부가 뜨고, 적이 있는 지점으로 이동하면 전투 씬이 열립니다.");
        }

        /// <summary>
        /// 캠페인 스코프를 만듭니다.
        ///
        /// <b>씬의 최상위에 둡니다.</b> 전역 스코프와 같은 이유입니다 —
        /// 이 오브젝트는 전투 씬을 오가는 동안 살아남아야 하고,
        /// <c>DontDestroyOnLoad</c> 는 루트 오브젝트에만 걸립니다.
        /// </summary>
        private static void CreateCampaign()
        {
            var campaignObject = new GameObject("Campaign");
            var scope = campaignObject.AddComponent<CampaignLifetimeScope>();

            var setup = AssetDatabase.LoadAssetAtPath<BattleSetup>(PrototypeAssetBuilder.BattleSetupPath);

            var serialized = new SerializedObject(scope);
            serialized.FindProperty("_setup").objectReferenceValue = setup;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 전투 씬을 새로 만들어 저장하고 엽니다. 기존 파일이 있으면 덮어씁니다.
        /// </summary>
        [MenuItem("SRPG/전투 씬 생성 및 열기", priority = 10)]
        public static void CreateBattleScene()
        {
            // 배치 모드(-executeMethod)에서는 대화상자를 띄울 수 없으므로 건너뜁니다.
            if (!Application.isBatchMode)
            {
                if (File.Exists(ScenePath) &&
                    !EditorUtility.DisplayDialog(
                        "전투 씬 덮어쓰기",
                        $"이미 {ScenePath} 이 있습니다.\n새로 만들어 덮어쓸까요?",
                        "덮어쓰기",
                        "취소"))
                {
                    return;
                }

                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return;
                }
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateGameSystems();

            var environment = new GameObject("Environment").transform;
            var camera = CreateCamera(environment);
            CreateLight(environment);
            ConfigureLighting();

            CreateGlobalVolume(environment);

            var runtimeRoot = new GameObject("Runtime").transform;
            CreateBootstrap(camera, runtimeRoot);

            Directory.CreateDirectory(SceneDirectory);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            RegisterInBuildSettings(ScenePath);

            Debug.Log(
                $"[BattleSceneBuilder] 전투 씬을 만들었습니다: {ScenePath}\n" +
                "재생 버튼을 누르면 섬·분대·적이 Runtime 아래에 생성됩니다.");

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
        }

        /// <summary>
        /// 현재 열린 씬에 부트스트랩만 추가합니다. 기존 씬에 프로토타입을 얹어 볼 때 사용합니다.
        /// </summary>
        [MenuItem("SRPG/현재 씬에 BattleBootstrap 추가", priority = 11)]
        public static void AddBootstrapToCurrentScene()
        {
            if (Object.FindAnyObjectByType<BattleBootstrap>() != null)
            {
                Debug.LogWarning("[BattleSceneBuilder] 이 씬에는 이미 BattleBootstrap이 있습니다.");
                return;
            }

            var bootstrap = CreateBootstrap(Camera.main, null);
            Selection.activeGameObject = bootstrap.gameObject;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        // ====================================================================================================
        // 3. Private Methods - Scene Objects
        // ====================================================================================================

        /// <summary>
        /// 앱 수명 동안 유지되는 전역 시스템을 만듭니다.
        ///
        /// <b>씬의 최상위에 둡니다.</b>
        /// <see cref="RootLifetimeScope"/> 는 <c>DontDestroyOnLoad</c> 로 씬 전환을 넘기는데,
        /// 그것은 루트 오브젝트에만 걸립니다. 정돈하겠다고 Environment 아래로 넣으면
        /// 씬을 넘길 때 조용히 함께 파괴됩니다.
        ///
        /// <b>여기에 씬 전용 물건을 매달지 마십시오.</b>
        /// 이 오브젝트는 다음 씬으로 따라갑니다. 전투 전용 컴포넌트를 자식으로 넣으면
        /// 살아남는 것이 전역 상태가 아니라 죽은 씬 참조가 됩니다.
        /// </summary>
        private static void CreateGameSystems()
        {
            var rootObject = new GameObject("GameSystems");
            var scope = rootObject.AddComponent<RootLifetimeScope>();

            var stateObject = new GameObject("GameState");
            stateObject.transform.SetParent(rootObject.transform, false);
            var gameState = stateObject.AddComponent<GameStateManager>();

            var audioObject = new GameObject("Audio");
            audioObject.transform.SetParent(rootObject.transform, false);
            var audio = audioObject.AddComponent<AudioManager>();

            // private [SerializeField] 는 SerializedObject로 연결합니다.
            var serialized = new SerializedObject(scope);
            serialized.FindProperty("_gameState").objectReferenceValue = gameState;
            serialized.FindProperty("_audio").objectReferenceValue = audio;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 카메라 피벗과 그 자식 카메라를 만듭니다.
        ///
        /// 리그는 <b>피벗</b>에 붙습니다. 피벗이 초점이자 WASD로 움직이는 주체이고,
        /// 카메라는 그 둘레를 도는 자식입니다.
        /// </summary>
        private static Camera CreateCamera(Transform parent)
        {
            var pivotObject = new GameObject("CameraPivot");
            pivotObject.transform.SetParent(parent, false);
            pivotObject.AddComponent<BattleCameraRig>();

            var cameraObject = new GameObject("BattleCamera");
            cameraObject.transform.SetParent(pivotObject.transform, false);
            cameraObject.tag = "MainCamera";

            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.11f, 0.15f, 0.21f);
            camera.farClipPlane = 400f;

            // <b>직교입니다.</b> 픽셀 격자를 붙잡으려면 텍셀이 덮는 월드 길이가
            // 화면 전체에서 같아야 하고, 그것은 직교에서만 성립합니다.
            // 리그가 실행 중에도 되돌리지만, 씬에 저장된 것과 실제로 도는 것이 달라지면
            // 편집 중 씬 뷰에서 보는 구도가 실행 결과와 어긋납니다.
            camera.orthographic = true;
            camera.orthographicSize = 19.5f;

            // URP 추가 데이터를 씬에 명시적으로 넣습니다.
            //
            // 없어도 URP가 실행 중에 붙여 주긴 합니다. 그래서 없어도 굴러갑니다.
            // 하지만 그러면 씬 에셋에 저장된 것과 실제로 도는 것이 달라지고,
            // 안티에일리어싱이나 포스트 프로세싱 같은 설정을 씬에서 만질 수가 없습니다.
            var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();

            // 실제 위치와 각도는 BattleCameraRig가 런타임에 섬 크기에 맞춰 잡습니다.
            // 여기 값은 편집 중 씬 뷰에서 대략의 구도를 보기 위한 것입니다.
            // 리그의 고정 거리(60)에 맞춘 자리입니다. 피치 47도에서 뒤로 물린 지점입니다.
            cameraObject.transform.SetLocalPositionAndRotation(
                new Vector3(0f, 43.9f, -40.9f),
                Quaternion.Euler(47f, 0f, 0f));

            cameraObject.AddComponent<AudioListener>();

            // <b>저해상도로 그리므로 픽셀 격자에 맞춰 세워야 합니다.</b>
            // 없으면 카메라를 움직일 때마다 화면 전체가 지글지글 기어다닙니다.
            //
            // 격자 에셋은 렌더러 피처가 쓰는 것과 <b>같은 것</b>이어야 합니다.
            // 여기서 함께 꽂아 두지 않으면 씬을 구울 때마다 카메라만 비어,
            // 화면은 A 격자로 잘리는데 카메라는 B 격자에 맞춰 서게 됩니다.
            BattleWiring.AssignPixelGrid(cameraObject.AddComponent<PixelSnapCamera>());

            // 후처리는 카메라가 켜 주어야 볼륨이 먹힙니다.
            cameraData.renderPostProcessing = true;

            return camera;
        }

        /// <summary>
        /// 전장 전체에 걸리는 후처리 볼륨을 만듭니다.
        ///
        /// <b>전역 볼륨입니다.</b> 경계가 있는 볼륨은 카메라가 그 안에 들어가야 먹히는데,
        /// 이 게임의 카메라는 전장 위를 자유롭게 돕니다. 자리에 따라 색이 달라지면
        /// 그것은 연출이 아니라 결함으로 보입니다.
        ///
        /// 프로필이 없으면 볼륨만 세워 둡니다 — 메뉴에서 프로필을 구우면 그때 연결됩니다.
        /// </summary>
        /// <param name="parent">볼륨을 매달 부모입니다.</param>
        private static void CreateGlobalVolume(Transform parent)
        {
            var volumeObject = new GameObject("PostProcessing");
            volumeObject.transform.SetParent(parent, false);

            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 0f;

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProcessBuilder.BattleProfilePath);

            if (profile != null)
            {
                volume.sharedProfile = profile;
                return;
            }

            Debug.LogWarning(
                $"[BattleSceneBuilder] 후처리 프로필을 찾지 못했습니다: {PostProcessBuilder.BattleProfilePath}\n" +
                "메뉴 'SRPG → 배선 → ⑦ 후처리 프로필 생성'을 실행한 뒤 씬을 다시 구우십시오.");
        }

        private static void CreateLight(Transform parent)
        {
            var lightObject = new GameObject("DirectionalLight");
            lightObject.transform.SetParent(parent, false);

            // 값은 BattleLighting이 정합니다. 여기에 숫자를 적으면 런타임 조명과 조용히 어긋납니다.
            BattleLighting.ApplyDirectional(lightObject.AddComponent<Light>());
        }

        /// <summary>
        /// 환경광을 설정합니다.
        /// </summary>
        private static void ConfigureLighting()
        {
            BattleLighting.ApplyAmbient();
        }

        /// <summary>
        /// 부트스트랩을 만들고 구성 에셋·카메라·런타임 루트를 연결합니다.
        /// </summary>
        private static BattleBootstrap CreateBootstrap(Camera camera, Transform runtimeRoot)
        {
            var bootstrapObject = new GameObject("BattleBootstrap");
            var bootstrap = bootstrapObject.AddComponent<BattleBootstrap>();

            var setup = AssetDatabase.LoadAssetAtPath<BattleSetup>(PrototypeAssetBuilder.BattleSetupPath);
            if (setup == null)
            {
                Debug.LogWarning(
                    $"[BattleSceneBuilder] 전투 구성 에셋을 찾지 못했습니다: {PrototypeAssetBuilder.BattleSetupPath}\n" +
                    "메뉴 'SRPG → 프로토타입 에셋 생성'을 먼저 실행하세요. " +
                    "지금 상태로도 실행은 되지만 프리미티브 폴백으로 동작합니다.");
            }

            // private [SerializeField] 는 SerializedObject로 연결합니다.
            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("_setup").objectReferenceValue = setup;
            serialized.FindProperty("_battleCamera").objectReferenceValue = camera;
            serialized.FindProperty("_runtimeRoot").objectReferenceValue = runtimeRoot;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return bootstrap;
        }

        // ====================================================================================================
        // 4. Private Methods - Build Settings
        // ====================================================================================================

        /// <summary>
        /// 빌드 설정에 씬을 등록합니다. 이미 있으면 아무것도 하지 않습니다.
        /// </summary>
        /// <param name="scenePath">등록할 씬의 경로입니다.</param>
        /// <param name="first">
        /// 목록의 맨 앞에 둘지 여부입니다.
        /// 빌드는 첫 씬부터 시작하므로, 게임의 시작점인 월드맵이 앞에 와야 합니다.
        /// </param>
        private static void RegisterInBuildSettings(string scenePath, bool first = false)
        {
            var scenes = EditorBuildSettings.scenes;

            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path == scenePath)
                {
                    // 이미 있는데 앞으로 와야 한다면 자리를 옮깁니다.
                    if (!first || i == 0)
                    {
                        return;
                    }

                    var moved = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes);
                    var entry = moved[i];

                    moved.RemoveAt(i);
                    moved.Insert(0, entry);

                    EditorBuildSettings.scenes = moved.ToArray();
                    return;
                }
            }

            var updated = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes);
            var added = new EditorBuildSettingsScene(scenePath, true);

            if (first)
            {
                updated.Insert(0, added);
            }
            else
            {
                updated.Add(added);
            }

            EditorBuildSettings.scenes = updated.ToArray();
        }
    }
}
