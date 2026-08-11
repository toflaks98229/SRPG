using System.Collections.Generic;
using System.IO;
using SRPG.Data;
using SRPG.Gameplay.Visual;
using UnityEditor;
using UnityEngine;

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

        /// <summary>빌드에 반드시 들어가야 하는 셰이더입니다.</summary>
        private static readonly string[] RequiredShaders =
        {
            PrototypeVisuals.TerrainShaderName,
            PrototypeVisuals.WaterShaderName,
            PrototypeVisuals.BillboardShaderName,
            PrototypeVisuals.ContactShadowShaderName,
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
