using System.Collections.Generic;
using System.IO;
using SRPG.Data;
using SRPG.Gameplay.CameraControl;
using SRPG.Gameplay.Visual;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SRPG.Editor.Tools
{
    /// <summary>
    /// 에셋과 씬의 배선을 현재 코드에 맞춥니다.
    ///
    /// <b>왜 별도의 도구인가</b>
    ///
    /// 이 프로젝트에는 두 갈래의 실행 경로가 있습니다.
    ///   · <b>에셋 경로</b> — 프리팹과 머티리얼이 연결되어 있으면 그것을 씁니다.
    ///   · <b>폴백 경로</b> — 비어 있으면 코드가 프리미티브로 만듭니다.
    ///
    /// 새 시각 시스템(지형 셰이더·빌보드·접지 그림자)을 <b>폴백 경로에만</b> 붙이면
    /// 에디터에서 부트스트랩만 띄웠을 때는 잘 보이는데, 에셋이 연결된 실제 게임에서는
    /// 하나도 나오지 않습니다. 컴파일도 되고 테스트도 통과하니 아무도 모릅니다.
    ///
    /// 실제로 그 상태였습니다. 이 도구가 그 간극을 메웁니다.
    ///
    /// <b>왜 <see cref="PrototypeAssetBuilder"/>를 쓰지 않는가</b>
    ///
    /// 그쪽은 프리팹을 <b>통째로 다시 굽습니다</b>. 손으로 고친 것이 전부 날아갑니다.
    /// 여기서 필요한 것은 재생성이 아니라 <b>바뀐 부분만 갈아 끼우기</b>입니다.
    /// 모든 항목은 여러 번 실행해도 결과가 같습니다.
    /// </summary>
    public static class BattleWiring
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>렌더 파이프라인 설정 파일의 경로입니다.</summary>
        private const string GraphicsSettingsPath = "ProjectSettings/GraphicsSettings.asset";
        /// <summary>생성한 머티리얼이 저장되는 폴더입니다.</summary>
        private const string MaterialDirectory = "Assets/_Project/04.Art/03.Shaders/Materials";
        /// <summary>전투 구성 에셋이 저장되는 폴더입니다.</summary>
        private const string ConfigDirectory = "Assets/_Project/03.DataAssets/Configs";

        /// <summary>기본 튜닝 에셋의 경로입니다.</summary>
        private const string TuningPath = ConfigDirectory + "/BattleTuning_Default.asset";

        /// <summary>픽셀 격자 설정 에셋의 경로입니다. 렌더러 피처와 카메라가 <b>둘 다</b> 이것을 봅니다.</summary>
        private const string PixelGridPath = ConfigDirectory + "/PixelGrid_Default.asset";

        /// <summary>전투 사운드 뱅크 에셋의 경로입니다.</summary>
        private const string AudioBankPath = ConfigDirectory + "/BattleAudio_Default.asset";

        /// <summary>하늘 프로필 에셋의 경로입니다. 구름 그늘이 여기서 옵니다.</summary>
        private const string SkyProfilePath = ConfigDirectory + "/SkyProfile_Default.asset";

        /// <summary>빌드에 반드시 들어가야 하는 셰이더입니다.</summary>
        private static readonly string[] RequiredShaders =
        {
            PrototypeVisuals.TerrainShaderName,
            PrototypeVisuals.WaterShaderName,
            PrototypeVisuals.BillboardShaderName,
            PrototypeVisuals.ContactShadowShaderName,
            PrototypeVisuals.GrassShaderName,

            // 픽셀아트 패스가 쓰는 셰이더입니다. 빌드에 없으면 Shader.Find 가 실패하고
            // 화면이 그대로 나옵니다 — 에디터에서는 되고 빌드에서만 안 되는 종류의 고장입니다.
            "SRPG/PixelOutline",
            "SRPG/OutlineMask",
        };

        /// <summary>
        /// 전용 셰이더를 써야 하는 머티리얼입니다.
        ///
        /// <b>절벽은 여기 없습니다.</b> <c>M_Terrain_Cliff</c>는 지형이 아니라
        /// 그 위에 세우는 바위 오브젝트가 씁니다. 일반 Lit이 맞습니다.
        /// </summary>
        private static readonly (string Name, string Shader)[] TerrainMaterials =
        {
            ("M_Terrain_Ground", PrototypeVisuals.TerrainShaderName),
            ("M_Terrain_Water", PrototypeVisuals.WaterShaderName),
        };

        // ====================================================================================================
        // 2. Menu Items
        // ====================================================================================================

        /// <summary>
        /// 배선을 전부 수행합니다. 씬까지 다시 굽습니다.
        /// </summary>
        [MenuItem("SRPG/배선/전체 수행", priority = 20)]
        public static void WireEverything()
        {
            RegisterShaders();
            WireTerrainMaterials();
            WireUnitBillboards();
            WireMissingConfigs();
            MigrateGrassProfiles();
            MigrateBattleTuning();
            MigratePixelGrids();

            // 후처리 프로필도 여기서 굽습니다. 씬을 굽는 쪽이 이 에셋을 찾아 볼륨에 꽂으므로
            // 반드시 씬보다 <b>먼저</b> 있어야 합니다.
            PostProcessBuilder.BuildBattleProfile();

            // 픽셀아트 피처도 배선 항목입니다. 여기서 빠져 있으면 '전체 수행'을 눌러도
            // 아웃라인이 붙지 않고, 그 사실을 화면을 들여다보기 전까지 알 수 없습니다.
            WirePixelArt();

            // 씬을 굽기 전이어야 합니다. 굽는 쪽이 여기서 만든 격자 에셋을 카메라에 꽂습니다.
            WireGridAndAudio();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            BattleSceneBuilder.CreateBattleScene();

            Diagnose();
        }

        /// <summary>
        /// 무엇이 연결되어 있고 무엇이 빠졌는지 보고합니다. 아무것도 바꾸지 않습니다.
        /// </summary>
        [MenuItem("SRPG/배선/진단 (변경 없음)", priority = 21)]
        public static void Diagnose()
        {
            var problems = new List<string>();

            CheckShadersExist(problems);
            CheckShadersIncludedInBuild(problems);
            CheckTerrainMaterials(problems);
            CheckUnitBillboards(problems);
            CheckConfigs(problems);
            CheckPixelGrid(problems);
            CheckPostProcess(problems);

            if (problems.Count > 0)
            {
                Debug.LogWarning(
                    "[BattleWiring] 배선이 끊긴 곳이 있습니다:\n  - " +
                    string.Join("\n  - ", problems) +
                    "\n\n메뉴 'SRPG → 배선 → 전체 수행'으로 고칠 수 있습니다.");
                return;
            }

            Debug.Log("[BattleWiring] 진단 통과: 에셋 경로가 현재 시각 시스템과 맞물려 있습니다.");
        }

        /// <summary>
        /// SRPG 셰이더를 빌드에 포함시킵니다.
        ///
        /// <b>왜 필요한가</b>
        /// 셰이더는 <see cref="Shader.Find"/>로만 참조되고 있습니다.
        /// 빌드에는 씬이나 Resources가 참조하는 셰이더만 들어가므로,
        /// 지금 빌드하면 셋 다 null이 되어 <b>전부 폴백으로 떨어집니다</b>.
        /// 에디터에서는 멀쩡해서 빌드하기 전까지 아무도 모릅니다.
        /// </summary>
        [MenuItem("SRPG/배선/① 셰이더를 빌드에 포함", priority = 30)]
        public static void RegisterShaders()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath(GraphicsSettingsPath);
            if (settings == null || settings.Length == 0)
            {
                Debug.LogError($"[BattleWiring] {GraphicsSettingsPath} 를 열지 못했습니다.");
                return;
            }

            var serialized = new SerializedObject(settings[0]);
            var included = serialized.FindProperty("m_AlwaysIncludedShaders");

            if (included == null)
            {
                Debug.LogError("[BattleWiring] m_AlwaysIncludedShaders 프로퍼티를 찾지 못했습니다.");
                return;
            }

            int added = 0;

            for (int i = 0; i < RequiredShaders.Length; i++)
            {
                var shader = Shader.Find(RequiredShaders[i]);
                if (shader == null)
                {
                    Debug.LogError($"[BattleWiring] 셰이더 '{RequiredShaders[i]}' 를 찾지 못했습니다.");
                    continue;
                }

                if (IsInArray(included, shader))
                {
                    continue;
                }

                included.InsertArrayElementAtIndex(included.arraySize);
                included.GetArrayElementAtIndex(included.arraySize - 1).objectReferenceValue = shader;
                added++;
            }

            if (added == 0)
            {
                Debug.Log("[BattleWiring] ① 셰이더는 이미 전부 빌드에 포함되어 있습니다.");
                return;
            }

            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            Debug.Log($"[BattleWiring] ① 셰이더 {added}개를 빌드에 포함시켰습니다.");
        }

        /// <summary>
        /// 지면과 물 머티리얼을 전용 셰이더로 바꿉니다.
        ///
        /// <b>왜 필요한가</b>
        /// <see cref="SRPG.Gameplay.Island.BattlefieldView"/>는 연결된 머티리얼이 있으면 그것을 씁니다.
        /// 연결된 것이 URP/Lit이면 지면은 고도도 경사도 말하지 않는 단색이 되고,
        /// 물은 수심을 재지 못해 <b>여울이 드러나지 않습니다</b>. 도하 지점이 눈에 안 보인다는 뜻입니다.
        ///
        /// 화면은 멀쩡해 보입니다. 그래서 아무도 모릅니다.
        /// </summary>
        [MenuItem("SRPG/배선/② 지형 머티리얼을 SRPG 셰이더로", priority = 31)]
        public static void WireTerrainMaterials()
        {
            int changed = 0;

            for (int i = 0; i < TerrainMaterials.Length; i++)
            {
                var shader = Shader.Find(TerrainMaterials[i].Shader);

                if (shader == null)
                {
                    Debug.LogError($"[BattleWiring] 셰이더 '{TerrainMaterials[i].Shader}' 를 찾지 못했습니다.");
                    continue;
                }

                string path = $"{MaterialDirectory}/{TerrainMaterials[i].Name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null)
                {
                    Debug.LogWarning($"[BattleWiring] 머티리얼을 찾지 못했습니다: {path}");
                    continue;
                }

                if (material.shader != shader)
                {
                    material.shader = shader;
                    changed++;
                }

                // 지형만 조명 하한을 맞춥니다. 물은 조명을 받지 않습니다.
                if (material.HasProperty("_AmbientBoost"))
                {
                    material.SetFloat("_AmbientBoost", BattleLighting.AmbientBoost);
                }

                EditorUtility.SetDirty(material);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[BattleWiring] ② 지형·물 머티리얼 {changed}개를 전용 셰이더로 바꿨습니다.");
        }

        /// <summary>
        /// 유닛 프리팹의 몸체를 캡슐에서 2.5D 빌보드로 바꿉니다.
        ///
        /// <b>왜 필요한가</b>
        /// <see cref="UnitDefinition.Prefab"/>이 연결되어 있으면 부트스트랩은 프리팹을 씁니다.
        /// 빌보드를 만드는 코드는 프리팹이 <b>없을 때만</b> 도는 폴백이라 한 번도 실행되지 않습니다.
        ///
        /// <b>몸만 바꿉니다.</b>
        /// 창·활·방패는 방향이 게임 규칙이므로 3D로 남습니다.
        /// 지휘관 깃발도 그대로 둡니다 — 실루엣으로 읽혀야 하는 것이라 카메라를 향할 이유가 없습니다.
        /// </summary>
        [MenuItem("SRPG/배선/③ 유닛 몸체를 빌보드로", priority = 32)]
        public static void WireUnitBillboards()
        {
            var shader = Shader.Find(PrototypeVisuals.BillboardShaderName);
            if (shader == null)
            {
                Debug.LogError($"[BattleWiring] 셰이더 '{PrototypeVisuals.BillboardShaderName}' 를 찾지 못했습니다.");
                return;
            }

            int converted = 0;

            foreach (var definition in LoadAllUnitDefinitions())
            {
                if (definition.Prefab == null)
                {
                    continue;
                }

                if (ConvertPrefabBody(definition))
                {
                    converted++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[BattleWiring] ③ 유닛 프리팹 {converted}개의 몸체를 빌보드로 바꿨습니다.");
        }

        /// <summary>
        /// 빠진 설정 에셋을 만들어 연결합니다.
        ///
        /// <b>왜 필요한가</b>
        /// <see cref="BattleSetup.Tuning"/>이 비어 있으면 전투 수치가 전부 코드 기본값으로 돌아갑니다.
        /// 기획자가 인스펙터에서 만질 대상이 아예 존재하지 않는 상태입니다.
        /// </summary>
        /// <summary>
        /// 유닛 정의 에셋의 스키마를 현재 코드에 맞춥니다.
        ///
        /// <b>왜 <see cref="PrototypeAssetBuilder"/> 를 쓰지 않는가</b>
        ///
        /// 그쪽은 프리팹과 머티리얼까지 함께 굽습니다. 스키마 하나 올리려고 부르면
        /// 손으로 다듬어 둔 셰이더와 머티리얼이 같이 날아갑니다.
        /// 여기서 필요한 것은 <b>새로 생긴 필드만 채우기</b>이고,
        /// 이관 자체는 정의가 스스로 합니다(<c>MigrateToCurrentSchema</c>).
        ///
        /// 여러 번 실행해도 결과가 같습니다. 이미 최신인 에셋은 건드리지 않습니다.
        /// </summary>
        [MenuItem("SRPG/배선/⑤ 유닛 정의 스키마 갱신", priority = 34)]
        public static void MigrateUnitDefinitions()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(UnitDefinition)}");
            int migrated = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var definition = AssetDatabase.LoadAssetAtPath<UnitDefinition>(path);

                if (definition == null)
                {
                    continue;
                }

                // 적 정의인지는 경로가 아니라 파일 이름으로 봅니다.
                // 폴더는 옮겨질 수 있지만 이름 규칙은 에셋 빌더가 정한 것이라 함께 움직입니다.
                bool isEnemy = definition.name.StartsWith("EnemyDef", System.StringComparison.Ordinal);

                if (!definition.MigrateToCurrentSchema(isEnemy))
                {
                    continue;
                }

                EditorUtility.SetDirty(definition);
                migrated++;

                Debug.Log(
                    $"[BattleWiring] 스키마 갱신: {definition.name} → v{UnitDefinition.CurrentSchemaVersion} " +
                    $"(피해 {definition.Damage}, 방어 {definition.Armor})", definition);
            }

            if (migrated > 0)
            {
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"[BattleWiring] 유닛 정의 {guids.Length}개 중 {migrated}개를 갱신했습니다.");
        }

        /// <summary>
        /// 들판 프로필 에셋의 스키마를 현재 코드에 맞춥니다.
        ///
        /// <b>왜 필요한가</b>
        ///
        /// 종의 설정은 구조체라 필드 초기값을 가질 수 없습니다.
        /// 에셋이 만들어진 뒤에 필드를 추가하면 그 필드는 YAML에 없어 0으로 로드되고,
        /// 0은 대개 <b>틀린 값</b>입니다 — 잎이 예전처럼 카메라 자리를 향하고, 색이 검어지고,
        /// 크기가 0이라 아예 보이지 않게 됩니다. 오류는 나지 않고 화면만 조용히 틀립니다.
        ///
        /// 손으로 맞춰 둔 값은 건드리지 않고 빠진 것만 채웁니다.
        /// 여러 번 실행해도 결과가 같습니다.
        /// </summary>
        [MenuItem("SRPG/배선/⑥ 들판 프로필 스키마 갱신", priority = 35)]
        public static void MigrateGrassProfiles()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(GrassProfile)}");
            int migrated = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var profile = AssetDatabase.LoadAssetAtPath<GrassProfile>(path);

                if (profile == null || !profile.MigrateToCurrentSchema())
                {
                    continue;
                }

                EditorUtility.SetDirty(profile);
                migrated++;

                Debug.Log(
                    $"[BattleWiring] 스키마 갱신: {profile.name} → v{GrassProfile.CurrentSchemaVersion} " +
                    $"(풀 시선정렬 {profile.Grass.ViewAlign}, 색결속 {profile.Grass.ColorCohesion})", profile);
            }

            if (migrated > 0)
            {
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"[BattleWiring] 들판 프로필 {guids.Length}개 중 {migrated}개를 갱신했습니다.");
        }

        /// <summary>
        /// 전투 튜닝 에셋의 스키마를 현재 코드에 맞춥니다.
        ///
        /// <b>왜 필요한가</b>
        ///
        /// 쉰여덟 개가 평평하게 놓여 있던 것을 영역별 묶음으로 중첩했습니다.
        /// 그러면 YAML 의 키 모양이 통째로 바뀝니다 — 옛 키는 갈 곳이 없어 버려지고,
        /// 새 키는 파일에 없어 코드 기본값으로 로드됩니다.
        ///
        /// <b>이번에는 그것이 손실이 아닙니다.</b> 옮기기 전에 에셋을 열어 확인한 결과
        /// 담긴 값이 전부 코드 기본값과 같았고, 그중 여섯 개는 이미 사라진 필드를 가리키고
        /// 있었습니다(<c>EnemyAggroRadius</c> · <c>ShipSpeed</c> 따위). 손으로 맞춰 둔 값이 없었습니다.
        ///
        /// 여기서 하는 일은 그 사실을 <b>파일에 적어 두는 것</b>입니다 —
        /// 새 모양으로 다시 구워 두지 않으면 죽은 키가 계속 남아, 다음 사람이
        /// "이 값이 왜 안 먹히지"를 묻게 됩니다.
        ///
        /// 여러 번 실행해도 결과가 같습니다.
        /// </summary>
        [MenuItem("SRPG/배선/⑦ 전투 튜닝 스키마 갱신", priority = 36)]
        public static void MigrateBattleTuning()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(BattleTuning)}");
            int migrated = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tuning = AssetDatabase.LoadAssetAtPath<BattleTuning>(path);

                if (tuning == null || !tuning.MigrateToCurrentSchema())
                {
                    continue;
                }

                EditorUtility.SetDirty(tuning);
                migrated++;

                Debug.Log(
                    $"[BattleWiring] 스키마 갱신: {tuning.name} → v{BattleTuning.CurrentSchemaVersion} " +
                    $"(느린시간 {tuning.Time.SlowMotionScale}, 방패 충격보존 {tuning.Shield.BlockedKnockbackRetention})",
                    tuning);
            }

            if (migrated > 0)
            {
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"[BattleWiring] 전투 튜닝 {guids.Length}개 중 {migrated}개를 갱신했습니다.");
        }

        /// <summary>
        /// 픽셀 격자 에셋의 스키마를 현재 코드에 맞춥니다.
        ///
        /// <b>왜 필요한가</b>
        ///
        /// 전투 카메라를 원근에서 직교로 옮기면서 줌의 단위가 바뀌었습니다 —
        /// 카메라 거리(34)에서 화면 높이의 절반(약 19.6)으로.
        ///
        /// 이름은 <c>FormerlySerializedAs</c> 가 이어 주지만 <b>단위는 이어 주지 않습니다.</b>
        /// 그대로 두면 34가 새 단위로 읽혀 기준 해상도가 적용되는 지점이 통째로 어긋나고,
        /// 화면은 멀쩡해 보이는데 굵기만 달라집니다.
        ///
        /// 여러 번 실행해도 결과가 같습니다.
        /// </summary>
        [MenuItem("SRPG/배선/⑧ 픽셀 격자 스키마 갱신", priority = 37)]
        public static void MigratePixelGrids()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(PixelGridSettings)}");
            int migrated = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var settings = AssetDatabase.LoadAssetAtPath<PixelGridSettings>(path);

                if (settings == null || !settings.MigrateToCurrentSchema())
                {
                    continue;
                }

                EditorUtility.SetDirty(settings);
                migrated++;

                Debug.Log(
                    $"[BattleWiring] 스키마 갱신: {settings.name} → v{PixelGridSettings.CurrentSchemaVersion} " +
                    $"(기준 줌 {settings.ReferenceExtent:F2})",
                    settings);
            }

            if (migrated > 0)
            {
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"[BattleWiring] 픽셀 격자 {guids.Length}개 중 {migrated}개를 갱신했습니다.");
        }

        /// <summary>
        /// 프로젝트의 SRPG 셰이더가 실제로 컴파일되는지 확인합니다.
        ///
        /// <b>왜 별도의 점검이 필요한가</b>
        ///
        /// 배치 모드는 화면이 없으면 셰이더 변형을 굽지 않습니다.
        /// 그래서 "컴파일 오류 0"이 곧 "셰이더가 멀쩡하다"는 뜻이 아닙니다.
        /// include 경로를 하나 잘못 적으면 C# 은 통과하고 화면만 조용히 비어 있습니다.
        ///
        /// <c>ShaderUtil</c> 은 임포터가 기록해 둔 오류를 읽으므로 배치에서도 대답할 수 있습니다.
        /// </summary>
        [MenuItem("SRPG/배선/⑫ 셰이더 오류 점검", priority = 41)]
        public static void InspectShaders()
        {
            var guids = AssetDatabase.FindAssets("t:Shader");
            int checkedCount = 0;
            int brokenCount = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // 패키지의 셰이더까지 훑을 이유는 없습니다.
                if (!path.StartsWith("Assets/", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);

                if (shader == null)
                {
                    continue;
                }

                checkedCount++;

                if (!ShaderUtil.ShaderHasError(shader))
                {
                    continue;
                }

                brokenCount++;

                var messages = ShaderUtil.GetShaderMessages(shader);

                foreach (var message in messages)
                {
                    if (message.severity != UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    {
                        continue;
                    }

                    Debug.LogError(
                        $"[BattleWiring] {shader.name} ({System.IO.Path.GetFileName(path)}:{message.line}) — {message.message}",
                        shader);
                }
            }

            if (brokenCount == 0)
            {
                Debug.Log($"[BattleWiring] 셰이더 {checkedCount}개를 점검했고 오류가 없습니다.");
                return;
            }

            Debug.LogError($"[BattleWiring] 셰이더 {checkedCount}개 중 {brokenCount}개에 오류가 있습니다.");
        }

        /// <summary>
        /// 픽셀아트 렌더러 피처를 쓸 수 있는 상태로 맞춥니다.
        ///
        /// <b>왜 셰이더를 직접 꽂는가</b>
        ///
        /// 피처는 비어 있으면 <see cref="Shader.Find"/> 로 찾습니다. 에디터에서는 프로젝트 전체를
        /// 뒤지므로 잘 됩니다. 그런데 <b>빌드에서는 포함된 셰이더만</b> 찾습니다.
        /// 참조가 없으면 포함되지 않고, 포함되지 않으면 찾지 못하고, 화면은 그냥 원본이 나옵니다.
        /// 에디터에서는 되고 빌드에서만 안 되는, 가장 늦게 발견되는 종류의 고장입니다.
        ///
        /// 에셋 참조로 꽂아 두면 그 자체가 포함 근거가 되어 이 고리가 끊깁니다.
        ///
        /// <b>SSAO 를 끕니다</b>
        ///
        /// 화면 공간 차폐는 GPU 인스턴싱으로 그리는 풀과 맞지 않습니다.
        /// 인스턴스마다 노멀이 제대로 들어가지 않아 들판에 검은 얼룩이 집니다.
        /// 참조 구현들이 공통으로 경고하는 항목이고, 이 프로젝트가 정확히 그 조건입니다.
        /// </summary>
        [MenuItem("SRPG/배선/⑩ 픽셀아트 피처 배선", priority = 39)]
        public static void WirePixelArt()
        {
            var outline = Shader.Find("SRPG/PixelOutline");
            var mask = Shader.Find("SRPG/OutlineMask");

            if (outline == null || mask == null)
            {
                Debug.LogError("[BattleWiring] 픽셀아트 셰이더를 찾지 못했습니다. 임포트 오류를 먼저 확인하십시오.");
                return;
            }

            int wired = 0;
            int disabledAo = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableRendererData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset == null)
                    {
                        continue;
                    }

                    string typeName = asset.GetType().Name;

                    if (typeName == "PixelArtFeature")
                    {
                        var serialized = new SerializedObject(asset);

                        serialized.FindProperty("_outlineShader").objectReferenceValue = outline;
                        serialized.FindProperty("_maskShader").objectReferenceValue = mask;
                        serialized.ApplyModifiedPropertiesWithoutUndo();

                        EditorUtility.SetDirty(asset);
                        wired++;

                        continue;
                    }

                    // 이름으로 봅니다. SSAO 타입은 URP 내부에 있어 직접 참조할 수 없습니다.
                    if (typeName != "ScreenSpaceAmbientOcclusion")
                    {
                        continue;
                    }

                    var ao = new SerializedObject(asset);
                    var active = ao.FindProperty("m_Active");

                    if (active == null || !active.boolValue)
                    {
                        continue;
                    }

                    active.boolValue = false;
                    ao.ApplyModifiedPropertiesWithoutUndo();

                    EditorUtility.SetDirty(asset);
                    disabledAo++;
                }
            }

            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[BattleWiring] ⑩ 픽셀아트 피처 {wired}개에 셰이더를 꽂았고, " +
                $"SSAO {disabledAo}개를 껐습니다.");
        }

        /// <summary>
        /// 픽셀 격자 에셋과 사운드 뱅크를 만들어 쓰는 쪽에 꽂습니다.
        ///
        /// <b>격자를 왜 도구가 꽂는가</b>
        ///
        /// 격자 값을 렌더러 피처와 카메라가 <b>둘 다</b> 씁니다. 각자 값을 들고 있던 것을
        /// 에셋 하나로 모았지만, 그 에셋을 <b>한쪽에만</b> 꽂으면 예전과 똑같아집니다 —
        /// 한쪽은 에셋을, 다른 쪽은 코드 기본값을 보게 되니까요.
        /// 어긋나도 컴파일은 통과하고 오류도 나지 않으므로 손으로 맡길 일이 아닙니다.
        ///
        /// <b>뱅크는 왜 만들어 두는가</b>
        ///
        /// 비워 두어도 코드가 파형을 합성해 채웁니다. 다만 그것은 <b>매번 새로 합성</b>되고
        /// 인스펙터에 보이지 않아, 실제 클립으로 갈아 끼울 자리가 프로젝트에 없습니다.
        /// 에셋으로 한 번 구워 두면 그 자리가 생깁니다.
        ///
        /// 여러 번 실행해도 결과가 같습니다. 이미 꽂혀 있으면 건드리지 않습니다.
        /// </summary>
        [MenuItem("SRPG/배선/⑪ 픽셀 격자·사운드 에셋 연결", priority = 40)]
        public static void WireGridAndAudio()
        {
            var grid = EnsurePixelGrid();
            var bank = EnsureAudioBank();

            int features = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableRendererData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset == null || asset.GetType().Name != "PixelArtFeature")
                    {
                        continue;
                    }

                    var serialized = new SerializedObject(asset);
                    var property = serialized.FindProperty("_grid");

                    if (property == null || property.objectReferenceValue == grid)
                    {
                        continue;
                    }

                    property.objectReferenceValue = grid;
                    serialized.ApplyModifiedPropertiesWithoutUndo();

                    EditorUtility.SetDirty(asset);
                    features++;
                }
            }

            // 지금 열려 있는 씬만 봅니다. 다른 씬을 몰래 열었다 닫으면
            // 사용자가 편집 중이던 것을 건드릴 위험이 있고, 전투 씬은 어차피 다시 구워집니다.
            int cameras = 0;

            foreach (var snap in UnityEngine.Object.FindObjectsByType<PixelSnapCamera>(
                         FindObjectsInactive.Include))
            {
                if (AssignPixelGrid(snap))
                {
                    cameras++;
                }
            }

            var setup = AssetDatabase.LoadAssetAtPath<BattleSetup>(PrototypeAssetBuilder.BattleSetupPath);
            bool wiredBank = false;

            if (setup != null && setup.AudioBank == null)
            {
                setup.AudioBank = bank;
                EditorUtility.SetDirty(setup);
                wiredBank = true;
            }

            if (setup != null && setup.SkyProfile == null)
            {
                setup.SkyProfile = EnsureSkyProfile();
                EditorUtility.SetDirty(setup);
            }

            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[BattleWiring] ⑪ 격자를 피처 {features}개와 열린 씬의 카메라 {cameras}개에 꽂았습니다. " +
                (wiredBank ? "사운드 뱅크도 연결했습니다." : "사운드 뱅크는 이미 연결되어 있거나 구성 에셋이 없습니다."));
        }

        /// <summary>
        /// 픽셀 격자 에셋을 카메라에 꽂습니다. 이미 같은 것이 꽂혀 있으면 아무것도 하지 않습니다.
        ///
        /// 씬을 굽는 쪽(<see cref="BattleSceneBuilder"/>)도 이것을 부릅니다 —
        /// 꽂는 규칙이 두 곳에 있으면 언젠가 한쪽만 고쳐집니다.
        /// </summary>
        /// <param name="snap">격자를 꽂을 카메라 컴포넌트입니다. null이면 아무것도 하지 않습니다.</param>
        /// <returns>실제로 바꿨으면 true입니다.</returns>
        public static bool AssignPixelGrid(PixelSnapCamera snap)
        {
            if (snap == null)
            {
                return false;
            }

            var serialized = new SerializedObject(snap);
            var property = serialized.FindProperty("_grid");
            var grid = EnsurePixelGrid();

            if (property == null || property.objectReferenceValue == grid)
            {
                return false;
            }

            property.objectReferenceValue = grid;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(snap);
            return true;
        }

        /// <summary>
        /// 하늘 프로필을 확보합니다. 없으면 코드 기본값으로 하나 굽습니다.
        ///
        /// <b>왜 에셋으로 꺼냈는가</b>
        ///
        /// 구름 값이 <c>BattlefieldView</c> 의 직렬화 필드였습니다. 그 컴포넌트는 런타임에
        /// <c>AddComponent</c> 되므로 인스펙터에 뜰 기회가 없어, 구름을 짙게 해 보려면
        /// 코드를 고치고 컴파일해야 했습니다. 들판 프로필과 같은 함정, 같은 해법입니다.
        /// </summary>
        /// <returns>프로젝트에 저장된 하늘 프로필입니다.</returns>
        private static SkyProfile EnsureSkyProfile()
        {
            var existing = AssetDatabase.LoadAssetAtPath<SkyProfile>(SkyProfilePath);

            if (existing != null)
            {
                return existing;
            }

            EnsureFolder(ConfigDirectory);

            var created = SkyProfile.CreateDefault();
            AssetDatabase.CreateAsset(created, SkyProfilePath);

            return created;
        }

        /// <summary>격자 에셋을 확보합니다. 없으면 코드 기본값으로 하나 굽습니다.</summary>
        /// <returns>프로젝트에 저장된 격자 에셋입니다.</returns>
        private static PixelGridSettings EnsurePixelGrid()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PixelGridSettings>(PixelGridPath);

            if (existing != null)
            {
                return existing;
            }

            EnsureFolder(ConfigDirectory);

            var created = PixelGridSettings.CreateDefault();
            AssetDatabase.CreateAsset(created, PixelGridPath);

            return created;
        }

        /// <summary>
        /// 사운드 뱅크 에셋을 확보합니다. 없으면 하나 굽습니다.
        ///
        /// <b>합성 클립은 굽지 않습니다.</b> <see cref="BattleAudioBank.CreateDefault"/> 가 만드는 클립은
        /// 런타임 <c>AudioClip</c> 이라 에셋으로 직렬화되지 않습니다. 여기서는 <b>빈</b> 뱅크를 만듭니다 —
        /// 클립 칸이 비면 소비자 쪽에서 그 칸만 조용해지고, 실제 소리를 넣을 자리는 남습니다.
        /// </summary>
        /// <returns>프로젝트에 저장된 사운드 뱅크입니다.</returns>
        private static BattleAudioBank EnsureAudioBank()
        {
            var existing = AssetDatabase.LoadAssetAtPath<BattleAudioBank>(AudioBankPath);

            if (existing != null)
            {
                return existing;
            }

            EnsureFolder(ConfigDirectory);

            var created = ScriptableObject.CreateInstance<BattleAudioBank>();
            AssetDatabase.CreateAsset(created, AudioBankPath);

            return created;
        }

        [MenuItem("SRPG/배선/④ 빠진 설정 에셋 연결", priority = 33)]
        public static void WireMissingConfigs()
        {
            var setup = AssetDatabase.LoadAssetAtPath<BattleSetup>(PrototypeAssetBuilder.BattleSetupPath);
            if (setup == null)
            {
                Debug.LogError($"[BattleWiring] 전투 구성 에셋이 없습니다: {PrototypeAssetBuilder.BattleSetupPath}");
                return;
            }

            if (setup.Tuning != null)
            {
                Debug.Log("[BattleWiring] ④ 설정 에셋은 이미 연결되어 있습니다.");
                return;
            }

            var tuning = AssetDatabase.LoadAssetAtPath<BattleTuning>(TuningPath);

            if (tuning == null)
            {
                EnsureFolder(ConfigDirectory);

                tuning = BattleTuning.CreateDefault();
                AssetDatabase.CreateAsset(tuning, TuningPath);
            }

            setup.Tuning = tuning;
            EditorUtility.SetDirty(setup);
            AssetDatabase.SaveAssets();

            Debug.Log($"[BattleWiring] ④ 튜닝 에셋을 만들어 연결했습니다: {TuningPath}");
        }

        // ====================================================================================================
        // 3. Private Methods - Prefab Conversion
        // ====================================================================================================

        /// <summary>
        /// 프리팹 하나의 몸체를 빌보드로 바꿉니다. 이미 바뀌어 있으면 아무것도 하지 않습니다.
        /// </summary>
        /// <returns>실제로 바꿨으면 true입니다.</returns>
        private static bool ConvertPrefabBody(UnitDefinition definition)
        {
            string path = AssetDatabase.GetAssetPath(definition.Prefab);
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                if (UnitBodyBuilder.IsBillboard(root))
                {
                    return false;
                }

                var body = root.transform.Find("Body");
                var renderer = body != null ? body.GetComponent<MeshRenderer>() : null;

                if (renderer == null)
                {
                    Debug.LogWarning($"[BattleWiring] '{root.name}' 의 Body에 렌더러가 없습니다.");
                    return false;
                }

                // 몸체를 만드는 규칙은 UnitBodyBuilder 하나에만 둡니다.
                // 여기서 따로 구현하면 프리팹 빌더와 갈라지고, 그 갈라짐이
                // "메뉴 한 번에 빌보드가 캡슐로 돌아가는" 사고를 냅니다.
                if (UnitBodyBuilder.Build(root, definition.Radius, definition.DebugHeight, renderer.sharedMaterial) == null)
                {
                    return false;
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ====================================================================================================
        // 4. Private Methods - Diagnosis
        // ====================================================================================================

        private static void CheckShadersExist(List<string> problems)
        {
            for (int i = 0; i < RequiredShaders.Length; i++)
            {
                if (Shader.Find(RequiredShaders[i]) == null)
                {
                    problems.Add($"셰이더 '{RequiredShaders[i]}' 가 없습니다.");
                }
            }
        }

        private static void CheckShadersIncludedInBuild(List<string> problems)
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath(GraphicsSettingsPath);
            if (settings == null || settings.Length == 0)
            {
                return;
            }

            var included = new SerializedObject(settings[0]).FindProperty("m_AlwaysIncludedShaders");
            if (included == null)
            {
                return;
            }

            for (int i = 0; i < RequiredShaders.Length; i++)
            {
                var shader = Shader.Find(RequiredShaders[i]);

                if (shader != null && !IsInArray(included, shader))
                {
                    problems.Add($"'{RequiredShaders[i]}' 가 빌드에 포함되지 않습니다. 빌드하면 폴백으로 떨어집니다.");
                }
            }
        }

        private static void CheckTerrainMaterials(List<string> problems)
        {
            for (int i = 0; i < TerrainMaterials.Length; i++)
            {
                var shader = Shader.Find(TerrainMaterials[i].Shader);

                if (shader == null)
                {
                    continue;
                }

                string path = $"{MaterialDirectory}/{TerrainMaterials[i].Name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null)
                {
                    problems.Add($"지형 머티리얼이 없습니다: {path}");
                }
                else if (material.shader != shader)
                {
                    problems.Add(
                        $"{TerrainMaterials[i].Name} 이 '{material.shader.name}' 를 씁니다. " +
                        $"'{TerrainMaterials[i].Shader}' 여야 고도·수심이 화면에 나옵니다.");
                }
            }
        }

        private static void CheckUnitBillboards(List<string> problems)
        {
            var shader = Shader.Find(PrototypeVisuals.BillboardShaderName);
            if (shader == null)
            {
                return;
            }

            foreach (var definition in LoadAllUnitDefinitions())
            {
                if (definition.Prefab == null)
                {
                    continue;
                }

                if (definition.Prefab.GetComponent<UnitBillboard>() == null)
                {
                    problems.Add($"{definition.Prefab.name} 에 UnitBillboard 가 없습니다. 캡슐로 나옵니다.");
                    continue;
                }

                var body = definition.Prefab.transform.Find("Body");
                var renderer = body != null ? body.GetComponent<MeshRenderer>() : null;

                if (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader != shader)
                {
                    problems.Add($"{definition.Prefab.name} 의 몸체가 '{renderer.sharedMaterial.shader.name}' 를 씁니다.");
                }
            }
        }

        private static void CheckConfigs(List<string> problems)
        {
            var setup = AssetDatabase.LoadAssetAtPath<BattleSetup>(PrototypeAssetBuilder.BattleSetupPath);

            if (setup == null)
            {
                problems.Add($"전투 구성 에셋이 없습니다: {PrototypeAssetBuilder.BattleSetupPath}");
                return;
            }

            if (setup.Tuning == null)
            {
                problems.Add("BattleSetup.Tuning 이 비어 있습니다. 전투 수치가 전부 코드 기본값으로 돌아갑니다.");
            }

            // 전장 프로필의 종류는 파일 이름에서 파생됩니다. 어긋나면 강을 고르고 숲에서 싸우게 되는데,
            // 인스펙터에서 한 번 잘못 누르면 생기고 아무 소리도 나지 않습니다. 실제로 한 번 그랬습니다.
            foreach (TerrainKind kind in System.Enum.GetValues(typeof(TerrainKind)))
            {
                string path = $"Assets/_Project/03.DataAssets/Battlefields/Battlefield_{kind}.asset";
                var profile = AssetDatabase.LoadAssetAtPath<BattlefieldProfile>(path);

                if (profile == null)
                {
                    problems.Add($"{kind} 전장 프로필이 없습니다: {path}");
                    continue;
                }

                if (profile.Kind != kind)
                {
                    problems.Add($"{path} 의 종류가 {profile.Kind} 입니다. 파일 이름과 어긋납니다.");
                }
            }
        }

        /// <summary>
        /// 렌더러 피처와 열린 씬의 카메라가 <b>같은</b> 격자를 보는지 확인합니다.
        ///
        /// <b>이것이 이 진단의 유일한 이유입니다.</b>
        /// 둘이 어긋나도 컴파일은 통과하고 오류도 나지 않습니다. 증상은
        /// "카메라가 붙잡는 시늉만 하고 화면은 그대로 기어다닌다" 하나뿐이라,
        /// 원인이 설정 불일치라는 것을 화면만 보고 알아내기는 매우 어렵습니다.
        ///
        /// 둘 다 비어 있는 것은 <b>문제가 아닙니다</b> — 그때는 양쪽이 같은 코드 기본값을 씁니다.
        /// </summary>
        /// <param name="problems">발견한 문제가 여기 쌓입니다.</param>
        private static void CheckPixelGrid(List<string> problems)
        {
            Object featureGrid = null;
            bool sawFeature = false;

            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableRendererData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset == null || asset.GetType().Name != "PixelArtFeature")
                    {
                        continue;
                    }

                    var property = new SerializedObject(asset).FindProperty("_grid");

                    if (property == null)
                    {
                        continue;
                    }

                    featureGrid = property.objectReferenceValue;
                    sawFeature = true;
                }
            }

            if (!sawFeature)
            {
                return;
            }

            foreach (var snap in UnityEngine.Object.FindObjectsByType<PixelSnapCamera>(
                         FindObjectsInactive.Include))
            {
                var property = new SerializedObject(snap).FindProperty("_grid");

                if (property == null || property.objectReferenceValue == featureGrid)
                {
                    continue;
                }

                problems.Add(
                    $"'{snap.name}' 의 픽셀 격자가 렌더러 피처와 다릅니다 " +
                    $"(카메라: {Describe(property.objectReferenceValue)}, 피처: {Describe(featureGrid)}). " +
                    "화면이 기어다닙니다.");
            }
        }

        /// <summary>
        /// 후처리 프로필에 실제로 항목이 들어 있는지 확인합니다.
        ///
        /// <b>왜 이 확인이 필요한가</b>
        ///
        /// 프로필이 비어 있어도 아무 오류가 나지 않습니다. 볼륨은 그대로 서 있고,
        /// 화면은 그려지고, 그저 톤매핑과 색보정이 <b>걸리지 않을 뿐</b>입니다.
        ///
        /// 실제로 그 상태였습니다. 굽는 도구가 컴포넌트를 하위 에셋으로 붙이지 않아
        /// 넷이 전부 끊긴 참조로 저장되어 있었고, 그것을 알아차리는 데
        /// "후처리 수치가 어디 있는가"를 되짚는 일이 필요했습니다.
        /// </summary>
        /// <param name="problems">발견한 문제가 여기 쌓입니다.</param>
        private static void CheckPostProcess(List<string> problems)
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                PostProcessBuilder.BattleProfilePath);

            if (profile == null)
            {
                problems.Add($"후처리 프로필이 없습니다: {PostProcessBuilder.BattleProfilePath}");
                return;
            }

            int broken = 0;

            for (int i = 0; i < profile.components.Count; i++)
            {
                if (profile.components[i] == null)
                {
                    broken++;
                }
            }

            if (broken > 0)
            {
                problems.Add(
                    $"후처리 프로필에 끊긴 항목이 {broken}개 있습니다. " +
                    "컴포넌트가 하위 에셋으로 저장되지 않았습니다.");
            }
            else if (profile.components.Count == 0)
            {
                problems.Add("후처리 프로필이 비어 있습니다. 톤매핑도 색보정도 걸리지 않습니다.");
            }
        }

        /// <summary>진단 문구에 쓸 에셋 이름입니다.</summary>
        /// <param name="asset">이름을 물을 에셋입니다. 비어 있어도 됩니다.</param>
        /// <returns>에셋 이름, 또는 비어 있다는 표시입니다.</returns>
        private static string Describe(Object asset)
        {
            return asset != null ? asset.name : "비어 있음(코드 기본값)";
        }

        // ====================================================================================================
        // 5. Private Methods - Helpers
        // ====================================================================================================

        /// <summary>
        /// 프로젝트의 모든 병과 정의를 읽습니다. 아군과 적군을 가리지 않습니다.
        /// </summary>
        private static IEnumerable<UnitDefinition> LoadAllUnitDefinitions()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(UnitDefinition)}");

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var definition = AssetDatabase.LoadAssetAtPath<UnitDefinition>(path);

                if (definition != null)
                {
                    yield return definition;
                }
            }
        }

        private static bool IsInArray(SerializedProperty array, Object value)
        {
            for (int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == value)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 폴더가 없으면 상위부터 차례로 만듭니다.
        /// </summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');

            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
