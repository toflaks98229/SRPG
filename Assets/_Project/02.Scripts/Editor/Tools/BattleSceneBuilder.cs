using System.IO;
using SRPG.Composition;
using SRPG.Data;
using SRPG.Gameplay.CameraControl;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SRPG.Editor.Tools
{
    /// <summary>
    /// 전투 프로토타입 씬을 만들어 저장하는 에디터 도구입니다.
    ///
    /// 씬 파일을 수작업으로 구성하는 대신 코드로 생성합니다.
    /// 구성이 바뀔 때마다 메뉴 한 번으로 씬을 다시 만들 수 있고, 씬 파일의 병합 충돌도 줄어듭니다.
    ///
    /// 씬에 배치되는 것은 "런타임에 만들 수 없거나, 만들면 손해인 것"으로 한정합니다.
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

        private const string SceneDirectory = "Assets/_Project/01.Scenes/Battle";
        private const string ScenePath = SceneDirectory + "/Battle.unity";

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

            var environment = new GameObject("Environment").transform;
            var camera = CreateCamera(environment);
            CreateLight(environment);
            ConfigureLighting();

            var runtimeRoot = new GameObject("Runtime").transform;
            CreateBootstrap(camera, runtimeRoot);

            Directory.CreateDirectory(SceneDirectory);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            RegisterInBuildSettings();

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

            // 실제 위치와 각도는 BattleCameraRig가 런타임에 섬 크기에 맞춰 잡습니다.
            // 여기 값은 편집 중 씬 뷰에서 대략의 구도를 보기 위한 것입니다.
            cameraObject.transform.SetLocalPositionAndRotation(
                new Vector3(0f, 25f, -23f),
                Quaternion.Euler(47f, 0f, 0f));

            cameraObject.AddComponent<AudioListener>();

            return camera;
        }

        private static void CreateLight(Transform parent)
        {
            var lightObject = new GameObject("DirectionalLight");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(48f, 138f, 0f);

            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.97f, 0.9f);
            light.shadows = LightShadows.Soft;
        }

        /// <summary>
        /// 환경광을 설정합니다.
        /// 기본 스카이박스를 그대로 두면 절벽 그늘이 새까맣게 죽어 지형 판독이 어려워집니다.
        /// </summary>
        private static void ConfigureLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.58f, 0.66f);
            RenderSettings.ambientEquatorColor = new Color(0.36f, 0.38f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.20f, 0.24f);
            RenderSettings.fog = false;
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
        private static void RegisterInBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;

            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path == ScenePath)
                {
                    return;
                }
            }

            var updated = new EditorBuildSettingsScene[scenes.Length + 1];
            scenes.CopyTo(updated, 0);
            updated[scenes.Length] = new EditorBuildSettingsScene(ScenePath, true);

            EditorBuildSettings.scenes = updated;
        }
    }
}
