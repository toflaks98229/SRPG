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

        private const string GraphicsSettingsPath = "ProjectSettings/GraphicsSettings.asset";
        private const string MaterialDirectory = "Assets/_Project/04.Art/03.Shaders/Materials";
        private const string ConfigDirectory = "Assets/_Project/03.DataAssets/Configs";

        private const string TuningPath = ConfigDirectory + "/BattleTuning_Default.asset";

        /// <summary>빌드에 반드시 들어가야 하는 셰이더입니다.</summary>
        private static readonly string[] RequiredShaders =
        {
            PrototypeVisuals.TerrainShaderName,
            PrototypeVisuals.BillboardShaderName,
            PrototypeVisuals.ContactShadowShaderName,
        };

        /// <summary>지형 머티리얼과 외곽선 두께입니다. 물만 0입니다.</summary>
        private static readonly (string Name, float OutlineWidth)[] TerrainMaterials =
        {
            ("M_Terrain_Beach", 0.06f),
            ("M_Terrain_Ground", 0.06f),
            ("M_Terrain_Cliff", 0.06f),
            ("M_Terrain_House", 0.06f),

            // 물은 화면 밖까지 이어집니다. 뒤집힌 껍질을 씌우면 화면 가장자리에 검은 띠가 생깁니다.
            ("M_Terrain_Water", 0f),
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
        /// 지형 머티리얼을 <c>SRPG/Terrain</c> 셰이더로 바꿉니다.
        ///
        /// <b>왜 필요한가</b>
        /// <see cref="SRPG.Gameplay.Island.IslandView"/>는 연결된 머티리얼이 있으면 그것을 씁니다.
        /// 지금 연결된 것은 URP/Lit이라 <b>외곽선도, 정점 컬러 접지 음영도 나오지 않습니다</b>.
        /// 메시에 접지 음영을 써 넣는 코드는 이미 돌고 있는데 읽는 쪽이 없는 상태입니다.
        /// </summary>
        [MenuItem("SRPG/배선/② 지형 머티리얼을 SRPG 셰이더로", priority = 31)]
        public static void WireTerrainMaterials()
        {
            var shader = Shader.Find(PrototypeVisuals.TerrainShaderName);
            if (shader == null)
            {
                Debug.LogError($"[BattleWiring] 셰이더 '{PrototypeVisuals.TerrainShaderName}' 를 찾지 못했습니다.");
                return;
            }

            int changed = 0;

            for (int i = 0; i < TerrainMaterials.Length; i++)
            {
                string path = $"{MaterialDirectory}/{TerrainMaterials[i].Name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null)
                {
                    Debug.LogWarning($"[BattleWiring] 머티리얼을 찾지 못했습니다: {path}");
                    continue;
                }

                // 셰이더를 바꾸기 전에 색을 빼 둡니다.
                // 같은 이름의 프로퍼티는 유지되지만, 순서에 기대지 않는 편이 안전합니다.
                Color baseColor = material.HasProperty("_BaseColor")
                    ? material.GetColor("_BaseColor")
                    : Color.gray;

                if (material.shader != shader)
                {
                    material.shader = shader;
                    changed++;
                }

                material.SetColor("_BaseColor", baseColor);

                // 그늘색은 기본색을 어둡게 민 것입니다. 따로 지정하게 두면 지형마다 손으로 맞춰야 합니다.
                material.SetColor("_ShadeColor", baseColor * 0.42f);
                material.SetFloat("_OutlineWidth", TerrainMaterials[i].OutlineWidth);
                material.SetFloat("_AmbientBoost", BattleLighting.AmbientBoost);

                EditorUtility.SetDirty(material);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[BattleWiring] ② 지형 머티리얼 {changed}개를 SRPG/Terrain 으로 바꿨습니다.");
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
            var shader = Shader.Find(PrototypeVisuals.TerrainShaderName);
            if (shader == null)
            {
                return;
            }

            for (int i = 0; i < TerrainMaterials.Length; i++)
            {
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
                        "외곽선과 접지 음영이 나오지 않습니다.");
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
