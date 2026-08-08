using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Enemies;
using SRPG.Gameplay.Units;
using SRPG.Gameplay.Visual;
using SRPG.Gameplay.Weapons;
using UnityEditor;
using UnityEngine;

namespace SRPG.Editor.Tools
{
    /// <summary>
    /// 프로토타입용 머티리얼·프리팹·데이터 에셋을 일괄 생성합니다.
    ///
    /// 손으로 만들지 않고 코드로 굽는 이유
    ///   · 반복 실행이 가능합니다. 구조가 바뀌면 메뉴 한 번으로 전부 다시 만듭니다
    ///   · 어떤 에셋이 왜 그 값인지가 코드에 남습니다
    ///   · 팀원이 클론한 뒤 동일한 에셋 세트를 얻습니다
    ///
    /// **이미 있는 에셋은 값을 덮어쓰지 않습니다.** 참조 연결만 보강합니다.
    /// 기획자가 인스펙터에서 조정한 밸런스 수치를 빌더가 되돌려 버리면 안 되기 때문입니다.
    /// 값까지 초기화하려면 해당 에셋을 지우고 다시 실행하세요.
    /// </summary>
    public static class PrototypeAssetBuilder
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        private const string MaterialDir = "Assets/_Project/04.Art/03.Shaders/Materials";
        private const string UnitPrefabDir = "Assets/_Project/05.Prefabs/Units";
        private const string EnemyPrefabDir = "Assets/_Project/05.Prefabs/Enemies";
        private const string SystemPrefabDir = "Assets/_Project/05.Prefabs/Systems";
        private const string UnitDataDir = "Assets/_Project/03.DataAssets/Units";
        private const string EnemyDataDir = "Assets/_Project/03.DataAssets/Enemies";
        private const string ConfigDataDir = "Assets/_Project/03.DataAssets/Configs";
        private const string TerrainDataDir = "Assets/_Project/03.DataAssets/Battlefields";

        /// <summary>전투 구성 에셋의 경로입니다. 씬 빌더가 이 경로를 참조합니다.</summary>
        public const string BattleSetupPath = ConfigDataDir + "/BattleSetup_Prototype.asset";

        // ====================================================================================================
        // 2. Menu Items
        // ====================================================================================================

        /// <summary>
        /// 프로토타입 에셋 일체를 생성하거나 갱신합니다.
        /// </summary>
        [MenuItem("SRPG/프로토타입 에셋 생성", priority = 1)]
        public static void BuildAll()
        {
            // 의도적으로 AssetDatabase.StartAssetEditing()으로 묶지 않습니다.
            // 그 블록 안에서는 임포트가 지연되어, 방금 구운 프리팹을 참조로 쓸 수 없습니다
            // (SaveAsPrefabAsset이 아직 임포트되지 않은 에셋을 돌려주어 참조가 전부 null이 됩니다).
            // 단계마다 저장·갱신을 끼워 넣어 다음 단계가 앞 단계의 결과를 확실히 볼 수 있게 합니다.
            EnsureFolders();
            EnsureLayers();

            var materials = BuildMaterials();
            AssetDatabase.SaveAssets();

            // 화살 프리팹을 먼저 굽습니다. 유닛 프리팹의 활이 이것을 참조합니다.
            var arrowPrefab = BuildArrowPrefab(materials);

            var unitPrefabs = BuildUnitPrefabs(materials);
            var markers = BuildMarkerPrefabs(materials);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var definitions = BuildUnitDefinitions(unitPrefabs, arrowPrefab);

            var tuning = LoadOrCreate(ConfigDataDir + "/BattleTuning_Default.asset", BattleTuning.CreateDefault);
            var profiles = BuildTerrainProfiles();

            BuildBattleSetup(materials, definitions, markers, tuning, profiles);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[PrototypeAssetBuilder] 에셋 생성 완료\n" +
                $"  머티리얼 : {MaterialDir}\n" +
                $"  프리팹   : {UnitPrefabDir}, {EnemyPrefabDir}, {SystemPrefabDir}\n" +
                $"  데이터   : {UnitDataDir}, {EnemyDataDir}\n" +
                $"  전투 구성: {BattleSetupPath}");

            VerifyReferences();

            var setup = AssetDatabase.LoadAssetAtPath<BattleSetup>(BattleSetupPath);
            if (setup != null)
            {
                EditorGUIUtility.PingObject(setup);
            }
        }

        /// <summary>
        /// 참조가 실제로 연결되었는지 확인합니다.
        ///
        /// 에셋 참조는 조용히 null로 남는 실패가 잦습니다. 파일은 전부 만들어졌는데 참조만 비어 있으면
        /// 겉보기엔 성공이고 실행하면 폴백으로 돌아가서, 원인을 찾기까지 한참 걸립니다.
        /// 빌더가 스스로 확인하고 소리 내게 만듭니다.
        /// </summary>
        private static void VerifyReferences()
        {
            var problems = new List<string>();

            var setup = AssetDatabase.LoadAssetAtPath<BattleSetup>(BattleSetupPath);
            if (setup == null)
            {
                Debug.LogError($"[PrototypeAssetBuilder] 전투 구성 에셋을 만들지 못했습니다: {BattleSetupPath}");
                return;
            }

            if (setup.Tuning == null) problems.Add("BattleSetup.Tuning");
            if (!setup.TerrainMaterials.IsComplete) problems.Add("BattleSetup.TerrainMaterials (3개 중 일부 누락)");
            if (setup.SelectionMarkerPrefab == null) problems.Add("BattleSetup.SelectionMarkerPrefab");
            if (setup.OrderMarkerPrefab == null) problems.Add("BattleSetup.OrderMarkerPrefab");

            CheckRoster(setup.PlayerRoster, "PlayerRoster", problems);
            CheckRoster(setup.EnemyRoster, "EnemyRoster", problems);

            if (problems.Count > 0)
            {
                Debug.LogError(
                    "[PrototypeAssetBuilder] 연결되지 않은 참조가 있습니다:\n  - " +
                    string.Join("\n  - ", problems));
                return;
            }

            Debug.Log("[PrototypeAssetBuilder] 참조 검증 통과: 모든 에셋이 연결되었습니다.");
        }

        private static void CheckRoster(UnitDefinition[] roster, string label, List<string> problems)
        {
            if (roster == null || roster.Length == 0)
            {
                problems.Add($"BattleSetup.{label} (비어 있음)");
                return;
            }

            for (int i = 0; i < roster.Length; i++)
            {
                if (roster[i] == null)
                {
                    problems.Add($"BattleSetup.{label}[{i}] (null)");
                }
                else
                {
                    CheckDefinition(roster[i], problems);
                }
            }
        }

        /// <summary>
        /// 정의 하나의 정합성을 확인합니다.
        ///
        /// 핵심은 <b>Style과 프리팹에 붙은 무기 컴포넌트가 일치하는가</b>입니다.
        /// 이 검사가 없었다면 궁수 에셋의 Style이 MeleeSwing으로 잘못 로드된 것을 잡지 못했습니다.
        /// 당시 검증은 "Style이 Projectile일 때만 화살을 확인"했기 때문에,
        /// Style 자체가 틀린 상황에서는 조건이 성립하지 않아 <b>공허하게 통과</b>했습니다.
        /// 검사는 "값이 맞는지"가 아니라 "서로 어긋나지 않는지"를 봐야 합니다.
        /// </summary>
        private static void CheckDefinition(UnitDefinition definition, List<string> problems)
        {
            if (definition.SchemaVersion < UnitDefinition.CurrentSchemaVersion)
            {
                problems.Add($"{definition.name}.SchemaVersion (v{definition.SchemaVersion}, 갱신 필요)");
            }

            if (definition.Prefab == null)
            {
                problems.Add($"{definition.name}.Prefab");
                return;
            }

            var weapon = definition.Prefab.GetComponent<WeaponBase>();
            if (weapon == null)
            {
                problems.Add($"{definition.name}.Prefab 에 무기 컴포넌트가 없습니다");
                return;
            }

            System.Type expected = definition.Style switch
            {
                AttackStyle.MeleeThrust => typeof(PikeWeapon),
                AttackStyle.Projectile => typeof(BowWeapon),
                _ => typeof(MeleeWeapon),
            };

            if (weapon.GetType() != expected)
            {
                problems.Add(
                    $"{definition.name}: Style={definition.Style} 인데 프리팹 무기는 {weapon.GetType().Name} 입니다 " +
                    $"(기대: {expected.Name})");
            }

            if (definition.Style == AttackStyle.Projectile && definition.ProjectilePrefab == null)
            {
                problems.Add($"{definition.name}.ProjectilePrefab (투사체 병과인데 화살이 없습니다)");
            }
        }

        // ====================================================================================================
        // 3. Private Methods - Layers
        // ====================================================================================================

        /// <summary>
        /// 물리 질의에 필요한 레이어를 프로젝트 설정에 등록합니다.
        ///
        /// 전투가 물리 기반이 되면서 레이어 분리가 필수가 되었습니다.
        /// 유닛에 콜라이더가 생겼기 때문에, 마스크 없이 클릭 레이캐스트를 쏘면
        /// 병사 몸에 막혀 이동 명령이 통째로 씹힙니다.
        ///
        /// 0~7번은 유니티 내장 레이어라 건드리지 않고 8번부터 빈자리를 찾아 채웁니다.
        /// </summary>
        private static void EnsureLayers()
        {
            const int FirstUserLayer = 8;
            const int LayerCount = 32;

            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (asset == null || asset.Length == 0)
            {
                Debug.LogError("[PrototypeAssetBuilder] TagManager.asset을 열지 못해 레이어를 등록하지 못했습니다.");
                return;
            }

            var tagManager = new SerializedObject(asset[0]);
            var layers = tagManager.FindProperty("layers");

            string[] required = { GameLayers.TerrainName, GameLayers.UnitName, GameLayers.ProjectileName };
            var added = new List<string>();

            foreach (string layerName in required)
            {
                if (ContainsLayer(layers, layerName, LayerCount))
                {
                    continue;
                }

                int slot = FindEmptyLayerSlot(layers, FirstUserLayer, LayerCount);
                if (slot < 0)
                {
                    Debug.LogError($"[PrototypeAssetBuilder] 빈 레이어 슬롯이 없어 '{layerName}'을 등록하지 못했습니다.");
                    continue;
                }

                layers.GetArrayElementAtIndex(slot).stringValue = layerName;
                added.Add($"{layerName}({slot})");
            }

            if (added.Count > 0)
            {
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();

                // 새로 등록한 레이어를 즉시 쓸 수 있도록 캐시를 비웁니다.
                GameLayers.InvalidateCache();

                Debug.Log($"[PrototypeAssetBuilder] 레이어 등록: {string.Join(", ", added)}");
            }
        }

        private static bool ContainsLayer(SerializedProperty layers, string layerName, int layerCount)
        {
            for (int i = 0; i < layerCount && i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName)
                {
                    return true;
                }
            }

            return false;
        }

        private static int FindEmptyLayerSlot(SerializedProperty layers, int from, int layerCount)
        {
            for (int i = from; i < layerCount && i < layers.arraySize; i++)
            {
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                {
                    return i;
                }
            }

            return -1;
        }

        // ====================================================================================================
        // 4. Private Methods - Materials
        // ====================================================================================================

        /// <summary>
        /// 프로토타입 머티리얼을 만듭니다. 이름 → 머티리얼 사전을 돌려줍니다.
        ///
        /// <b>지형과 물은 전용 셰이더를 씁니다</b>
        ///
        /// 나머지는 전부 URP Lit입니다. 지면과 물만 예외인 이유는 둘 다
        /// <b>화면에 정보를 실어야</b> 하기 때문입니다 — 지면은 어디가 절벽인지,
        /// 물은 어디가 얕은지를 색으로 말합니다. Lit으로는 그 둘 다 나오지 않습니다.
        /// </summary>
        private static Dictionary<string, Material> BuildMaterials()
        {
            var palette = new (string Name, Color Color, float Smoothness, string Shader)[]
            {
                // 지형 — 지면과 물은 전용 셰이더입니다.
                ("M_Terrain_Ground", new Color(0.44f, 0.62f, 0.36f), 0.05f, PrototypeVisuals.TerrainShaderName),
                ("M_Terrain_Water",  new Color(0.18f, 0.35f, 0.52f), 0.65f, PrototypeVisuals.WaterShaderName),

                // 절벽 머티리얼은 지형이 아니라 그 위에 세우는 <b>바위 오브젝트</b>가 씁니다.
                // 지형 셰이더를 물리면 바위 윗면이 경사 0이라 풀색으로 나옵니다.
                ("M_Terrain_Cliff",  new Color(0.44f, 0.42f, 0.43f), 0.10f, null),

                // 아군 병과 (조사에서 정리한 역할 구분이 색으로 읽히도록 한색 계열)
                ("M_Unit_Militia",  new Color(0.80f, 0.80f, 0.85f), 0.10f, null),
                ("M_Unit_Infantry", new Color(0.35f, 0.62f, 0.95f), 0.10f, null),
                ("M_Unit_Archer",   new Color(0.45f, 0.85f, 0.45f), 0.10f, null),
                ("M_Unit_Pike",     new Color(0.95f, 0.78f, 0.30f), 0.10f, null),

                // 적 (난색 계열로 묶어 진영이 한눈에 갈리게 함)
                ("M_Enemy_Militia",  new Color(0.70f, 0.35f, 0.30f), 0.10f, null),
                ("M_Enemy_Infantry", new Color(0.75f, 0.28f, 0.28f), 0.10f, null),
                ("M_Enemy_Archer",   new Color(0.85f, 0.45f, 0.35f), 0.10f, null),

                // 소품
                ("M_Flag_Player", new Color(0.95f, 0.92f, 0.85f), 0.05f, null),
                ("M_Flag_Enemy",  new Color(0.35f, 0.10f, 0.12f), 0.05f, null),
                ("M_FlagPole",    new Color(0.25f, 0.20f, 0.16f), 0.05f, null),

                // 무기 (금속은 반짝여야 휘두르는 궤적이 눈에 들어옵니다)
                ("M_Weapon_Steel", new Color(0.78f, 0.80f, 0.84f), 0.72f, null),
                ("M_Weapon_Wood",  new Color(0.42f, 0.31f, 0.21f), 0.08f, null),
                ("M_Arrow",        new Color(0.88f, 0.86f, 0.78f), 0.20f, null),

                // 마커
                ("M_Marker_Selection", new Color(1.00f, 0.95f, 0.55f), 0.20f, null),
                ("M_Marker_Order",     new Color(0.55f, 0.90f, 1.00f), 0.20f, null),
            };

            var litShader = FindLitShader();
            var result = new Dictionary<string, Material>(palette.Length);

            foreach (var entry in palette)
            {
                var shader = ResolveShader(entry.Shader, litShader);

                string path = $"{MaterialDir}/{entry.Name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null)
                {
                    material = new Material(shader);
                    ApplyMaterialValues(material, entry.Color, entry.Smoothness);
                    AssetDatabase.CreateAsset(material, path);
                }
                else if (material.shader != shader)
                {
                    // 이미 있는 에셋이 옛 셰이더를 물고 있을 수 있습니다.
                    // 만들 때만 정하면 한 번 구운 프로젝트에서는 영영 안 바뀝니다.
                    material.shader = shader;
                    ApplyMaterialValues(material, entry.Color, entry.Smoothness);
                    EditorUtility.SetDirty(material);
                }

                result[entry.Name] = material;
            }

            return result;
        }

        /// <summary>
        /// 이름으로 셰이더를 찾습니다. 이름이 비었거나 못 찾으면 Lit으로 물러납니다.
        /// </summary>
        private static Shader ResolveShader(string name, Shader fallback)
        {
            if (string.IsNullOrEmpty(name))
            {
                return fallback;
            }

            var shader = Shader.Find(name);

            if (shader == null)
            {
                Debug.LogWarning($"[PrototypeAssetBuilder] 셰이더 '{name}' 를 찾지 못해 Lit으로 대체합니다.");
                return fallback;
            }

            return shader;
        }

        private static Shader FindLitShader()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                Debug.LogWarning("[PrototypeAssetBuilder] URP Lit / Standard 셰이더를 찾지 못했습니다. 머티리얼이 분홍으로 보일 수 있습니다.");
            }

            return shader;
        }

        /// <summary>
        /// URP Lit은 색상 프로퍼티가 <c>_BaseColor</c>입니다. 표준 파이프라인 호환을 위해 둘 다 씁니다.
        /// </summary>
        private static void ApplyMaterialValues(Material material, Color color, float smoothness)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }
        }

        // ====================================================================================================
        // 5. Private Methods - Unit Prefabs
        // ====================================================================================================

        /// <summary>
        /// 병과별 유닛 프리팹을 만듭니다.
        ///
        /// 병사와 지휘관의 프리팹을 따로 두지 않습니다.
        /// 한 프리팹 안에 깃발을 꺼 둔 채 넣어 두고, <see cref="Unit.Initialize"/>가 지휘관일 때만 켭니다.
        /// 프리팹이 두 벌이면 밸런스나 구조를 고칠 때 한쪽만 고치는 사고가 반드시 납니다.
        /// </summary>
        private static Dictionary<string, GameObject> BuildUnitPrefabs(Dictionary<string, Material> materials)
        {
            var result = new Dictionary<string, GameObject>();

            var specs = new (string Key, string Prefab, string Dir, UnitRole Role, string Material, Team Team)[]
            {
                ("Unit_Militia",   "Unit_Militia",   UnitPrefabDir,  UnitRole.Militia,  "M_Unit_Militia",   Team.Player),
                ("Unit_Infantry",  "Unit_Infantry",  UnitPrefabDir,  UnitRole.Infantry, "M_Unit_Infantry",  Team.Player),
                ("Unit_Archer",    "Unit_Archer",    UnitPrefabDir,  UnitRole.Archer,   "M_Unit_Archer",    Team.Player),
                ("Unit_Pike",      "Unit_Pike",      UnitPrefabDir,  UnitRole.Pike,     "M_Unit_Pike",      Team.Player),
                ("Enemy_Militia",  "Enemy_Militia",  EnemyPrefabDir, UnitRole.Militia,  "M_Enemy_Militia",  Team.Enemy),
                ("Enemy_Infantry", "Enemy_Infantry", EnemyPrefabDir, UnitRole.Infantry, "M_Enemy_Infantry", Team.Enemy),
                ("Enemy_Archer",   "Enemy_Archer",   EnemyPrefabDir, UnitRole.Archer,   "M_Enemy_Archer",   Team.Enemy),
            };

            var capsule = GetBuiltinMesh(PrimitiveType.Capsule);
            var cube = GetBuiltinMesh(PrimitiveType.Cube);

            foreach (var spec in specs)
            {
                // 몸체 치수와 무기 형상은 정의 기본값을 그대로 따릅니다.
                var reference = spec.Team == Team.Player
                    ? UnitDefinition.CreateDefault(spec.Role)
                    : UnitDefinition.CreateEnemyDefault(spec.Role);

                float radius = reference.Radius;
                float height = reference.DebugHeight;

                string path = $"{spec.Dir}/{spec.Prefab}.prefab";
                var root = new GameObject(spec.Prefab);

                // 몸체 — 2.5D 빌보드입니다.
                //
                // 여기서 캡슐을 구우면 안 됩니다. 프리팹이 있으면 부트스트랩은 프리팹을 쓰므로,
                // 런타임 폴백이 아무리 빌보드를 만들어도 실제 게임에는 캡슐이 나옵니다.
                // 이 메뉴를 한 번 누를 때마다 빌보드 작업이 통째로 사라지던 원인이 여기였습니다.
                var body = UnitBodyBuilder.Build(root, radius, height, materials[spec.Material]);

                if (body == null)
                {
                    // 빌보드 셰이더가 없습니다. 셰이더 하나 때문에 프리팹이 안 만들어지면 안 됩니다.
                    body = CreateMeshChild(root.transform, "Body", capsule, materials[spec.Material]);
                    body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
                    body.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);

                    Debug.LogWarning(
                        $"[PrototypeAssetBuilder] 셰이더 '{PrototypeVisuals.BillboardShaderName}' 를 찾지 못해 " +
                        $"'{spec.Prefab}' 의 몸체를 캡슐로 만들었습니다.");
                }

                // 무기 판정에 걸릴 몸체 콜라이더입니다.
                // 트리거로 둡니다. 유닛끼리 물리로 밀어내면 분리 조향과 싸우게 됩니다.
                var bodyCollider = root.AddComponent<CapsuleCollider>();
                bodyCollider.radius = radius;
                bodyCollider.height = Mathf.Max(height, radius * 2f);
                bodyCollider.center = new Vector3(0f, height * 0.5f, 0f);
                bodyCollider.isTrigger = true;

                // 지휘관 깃발 (기본 비활성)
                float poleHeight = height * 1.6f;
                var flagRoot = new GameObject("CommanderFlag");
                flagRoot.transform.SetParent(root.transform, false);

                var pole = CreateMeshChild(flagRoot.transform, "Pole", cube, materials["M_FlagPole"]);
                pole.transform.localPosition = new Vector3(0f, poleHeight * 0.5f, 0f);
                pole.transform.localScale = new Vector3(0.07f, poleHeight, 0.07f);

                string flagMaterial = spec.Team == Team.Player ? "M_Flag_Player" : "M_Flag_Enemy";
                var cloth = CreateMeshChild(flagRoot.transform, "Cloth", cube, materials[flagMaterial]);
                cloth.transform.localPosition = new Vector3(0.22f, poleHeight * 0.86f, 0f);
                cloth.transform.localScale = new Vector3(0.42f, 0.28f, 0.05f);

                flagRoot.SetActive(false);

                // 무기: 병과에 맞는 컴포넌트와 형상을 붙이고 피벗을 연결합니다.
                AttachWeapon(root, reference, materials, cube);

                // Unit 컴포넌트와 직렬화 참조 연결
                var unit = root.AddComponent<Unit>();
                var serialized = new SerializedObject(unit);
                serialized.FindProperty("_commanderFlag").objectReferenceValue = flagRoot;
                serialized.FindProperty("_bodyRenderer").objectReferenceValue = body.GetComponent<MeshRenderer>();
                serialized.FindProperty("_bodyCollider").objectReferenceValue = bodyCollider;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                GameLayers.ApplyRecursively(root, GameLayers.Unit);

                Object.DestroyImmediate(reference);

                result[spec.Key] = SavePrefab(root, path);
            }

            return result;
        }

        /// <summary>
        /// 병과에 맞는 무기 컴포넌트와 형상을 붙입니다.
        ///
        /// 피벗의 로컬 +Z가 무기가 향하는 방향입니다.
        /// 무기 형상은 피벗에서 +Z로 뻗어 나가도록 배치해야 타격 판정 위치와 보이는 모양이 일치합니다.
        /// 어긋나면 "안 닿았는데 맞는" 상황이 생겨 물리 판정을 쓴 의미가 사라집니다.
        /// </summary>
        private static void AttachWeapon(
            GameObject root,
            UnitDefinition definition,
            Dictionary<string, Material> materials,
            Mesh cube)
        {
            var steel = materials["M_Weapon_Steel"];
            var wood = materials["M_Weapon_Wood"];
            float length = definition.WeaponLength;

            var pivot = new GameObject("WeaponPivot").transform;
            pivot.SetParent(root.transform, false);

            WeaponBase weapon;

            switch (definition.Style)
            {
                case AttackStyle.MeleeThrust:
                {
                    pivot.name = "SpearPivot";
                    pivot.localPosition = new Vector3(0.18f, definition.DebugHeight * 0.62f, 0f);

                    // 자루: 피벗에서 앞으로 길게 뻗습니다. 창의 사거리 우위가 눈에 보이는 지점입니다.
                    var shaft = CreateMeshChild(pivot, "Shaft", cube, wood);
                    shaft.transform.localPosition = new Vector3(0f, 0f, length * 0.5f);
                    shaft.transform.localScale = new Vector3(0.055f, 0.055f, length);

                    // 창끝
                    var head = CreateMeshChild(pivot, "Head", cube, steel);
                    head.transform.localPosition = new Vector3(0f, 0f, length - 0.09f);
                    head.transform.localScale = new Vector3(0.1f, 0.1f, 0.26f);

                    weapon = root.AddComponent<PikeWeapon>();
                    break;
                }

                case AttackStyle.Projectile:
                {
                    pivot.name = "BowPivot";
                    pivot.localPosition = new Vector3(0.16f, definition.DebugHeight * 0.6f, 0.1f);

                    var bow = CreateMeshChild(pivot, "Bow", cube, wood);
                    bow.transform.localScale = new Vector3(0.05f, 0.62f, 0.07f);

                    var grip = CreateMeshChild(pivot, "Grip", cube, steel);
                    grip.transform.localScale = new Vector3(0.07f, 0.12f, 0.09f);

                    weapon = root.AddComponent<BowWeapon>();
                    break;
                }

                default:
                {
                    pivot.name = "WeaponPivot";
                    pivot.localPosition = new Vector3(0f, definition.DebugHeight * 0.55f, 0f);

                    // 칼날: 피벗에서 앞으로 뻗습니다. 피벗이 회전하면서 호를 그립니다.
                    var blade = CreateMeshChild(pivot, "Blade", cube, steel);
                    blade.transform.localPosition = new Vector3(0f, 0f, length * 0.55f);
                    blade.transform.localScale = new Vector3(0.06f, 0.12f, length * 0.9f);

                    var hilt = CreateMeshChild(pivot, "Hilt", cube, wood);
                    hilt.transform.localPosition = new Vector3(0f, 0f, 0.06f);
                    hilt.transform.localScale = new Vector3(0.16f, 0.07f, 0.1f);

                    weapon = root.AddComponent<MeleeWeapon>();
                    break;
                }
            }

            var serialized = new SerializedObject(weapon);
            serialized.FindProperty("WeaponPivot").objectReferenceValue = pivot;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 화살 프리팹을 만듭니다.
        ///
        /// 화살대는 로컬 +Z 방향으로 눕힙니다.
        /// <see cref="Arrow"/>가 매 프레임 진행 방향으로 <c>LookRotation</c>을 걸기 때문에,
        /// 그래야 화살이 날아가는 방향을 향해 날아갑니다.
        /// </summary>
        private static GameObject BuildArrowPrefab(Dictionary<string, Material> materials)
        {
            var cube = GetBuiltinMesh(PrimitiveType.Cube);

            var root = new GameObject("Arrow");
            root.AddComponent<Arrow>();

            var shaft = CreateMeshChild(root.transform, "Shaft", cube, materials["M_Arrow"]);
            shaft.transform.localScale = new Vector3(0.028f, 0.028f, 0.52f);

            var head = CreateMeshChild(root.transform, "Head", cube, materials["M_Weapon_Steel"]);
            head.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            head.transform.localScale = new Vector3(0.05f, 0.05f, 0.1f);

            // 화살은 그림자를 드리우지 않습니다. 수십 발이 동시에 날면 그림자 비용이 커집니다.
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            GameLayers.ApplyRecursively(root, GameLayers.Projectile);

            return SavePrefab(root, $"{SystemPrefabDir}/Arrow.prefab");
        }

        // ====================================================================================================
        // 6. Private Methods - Other Prefabs
        // ====================================================================================================


        private static (GameObject Selection, GameObject Order) BuildMarkerPrefabs(Dictionary<string, Material> materials)
        {
            var cube = GetBuiltinMesh(PrimitiveType.Cube);

            var selection = BuildMarker(
                "FX_SelectionMarker",
                cube,
                materials["M_Marker_Selection"],
                new Vector3(1.7f, 0.06f, 1.7f));

            var order = BuildMarker(
                "FX_OrderMarker",
                cube,
                materials["M_Marker_Order"],
                new Vector3(0.7f, 0.06f, 0.7f));

            return (selection, order);
        }

        private static GameObject BuildMarker(string markerName, Mesh mesh, Material material, Vector3 scale)
        {
            var root = new GameObject(markerName);
            root.transform.localScale = scale;

            root.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            // 마커가 그림자를 드리우면 지형 판독을 방해합니다.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return SavePrefab(root, $"{SystemPrefabDir}/{markerName}.prefab");
        }

        // ====================================================================================================
        // 7. Private Methods - Data Assets
        // ====================================================================================================

        /// <summary>
        /// 병과 정의 에셋을 만들고 프리팹을 연결합니다.
        /// </summary>
        private static Dictionary<string, UnitDefinition> BuildUnitDefinitions(
            Dictionary<string, GameObject> prefabs,
            GameObject arrowPrefab)
        {
            var result = new Dictionary<string, UnitDefinition>();

            var specs = new (string Key, string Asset, string Dir, UnitRole Role, bool IsEnemy, string PrefabKey)[]
            {
                ("Militia",       "UnitDef_Militia",   UnitDataDir,  UnitRole.Militia,  false, "Unit_Militia"),
                ("Infantry",      "UnitDef_Infantry",  UnitDataDir,  UnitRole.Infantry, false, "Unit_Infantry"),
                ("Archer",        "UnitDef_Archer",    UnitDataDir,  UnitRole.Archer,   false, "Unit_Archer"),
                ("Pike",          "UnitDef_Pike",      UnitDataDir,  UnitRole.Pike,     false, "Unit_Pike"),
                ("EnemyMilitia",  "EnemyDef_Militia",  EnemyDataDir, UnitRole.Militia,  true,  "Enemy_Militia"),
                ("EnemyInfantry", "EnemyDef_Infantry", EnemyDataDir, UnitRole.Infantry, true,  "Enemy_Infantry"),
                ("EnemyArcher",   "EnemyDef_Archer",   EnemyDataDir, UnitRole.Archer,   true,  "Enemy_Archer"),
            };

            foreach (var spec in specs)
            {
                string path = $"{spec.Dir}/{spec.Asset}.asset";
                var definition = AssetDatabase.LoadAssetAtPath<UnitDefinition>(path);

                if (definition == null)
                {
                    definition = spec.IsEnemy
                        ? UnitDefinition.CreateEnemyDefault(spec.Role)
                        : UnitDefinition.CreateDefault(spec.Role);

                    AssetDatabase.CreateAsset(definition, path);
                }
                else if (definition.MigrateToCurrentSchema(spec.IsEnemy))
                {
                    // 필드가 추가된 뒤 처음 실행되는 경우입니다.
                    // 새로 생긴 필드만 병과 기본값으로 채웁니다. 기존 값은 그대로 둡니다.
                    EditorUtility.SetDirty(definition);
                    Debug.Log($"[PrototypeAssetBuilder] 스키마 갱신: {definition.name} → v{UnitDefinition.CurrentSchemaVersion}");
                }

                // 값은 건드리지 않고 프리팹 참조만 보강합니다.
                if (definition.Prefab == null && prefabs.TryGetValue(spec.PrefabKey, out var prefab))
                {
                    definition.Prefab = prefab;
                    EditorUtility.SetDirty(definition);
                }

                // 투사체 병과에는 화살 프리팹이 반드시 있어야 합니다. 없으면 조준까지 다 하고 아무것도 쏘지 않습니다.
                if (definition.Style == AttackStyle.Projectile && definition.ProjectilePrefab == null && arrowPrefab != null)
                {
                    definition.ProjectilePrefab = arrowPrefab;
                    EditorUtility.SetDirty(definition);
                }

                result[spec.Key] = definition;
            }

            return result;
        }

        /// <summary>
        /// 전투 구성 에셋을 만들고 모든 참조를 연결합니다.
        /// </summary>
        /// <summary>
        /// 지형 종류마다 프로필 에셋을 하나씩 굽습니다.
        ///
        /// <b>왜 전부 만드는가</b>
        ///
        /// 월드맵이 붙으면 좌표가 지형을 고르고, 그 지형에 맞는 프로필이 전장에 들어갑니다.
        /// 종류가 하나라도 비어 있으면 그 좌표에서만 전장이 코드 기본값으로 떨어지는데,
        /// 그건 "왜 이 지역만 다르게 생겼지"로만 보입니다.
        ///
        /// 지금은 그 연결이 없으므로 구성 에셋이 하나를 골라 씁니다.
        /// </summary>
        private static Dictionary<TerrainKind, BattlefieldProfile> BuildTerrainProfiles()
        {
            var profiles = new Dictionary<TerrainKind, BattlefieldProfile>();

            foreach (TerrainKind kind in System.Enum.GetValues(typeof(TerrainKind)))
            {
                var captured = kind;

                profiles[kind] = LoadOrCreate(
                    $"{TerrainDataDir}/Battlefield_{kind}.asset",
                    () => BattlefieldProfile.CreateDefault(captured));
            }

            return profiles;
        }

        private static void BuildBattleSetup(
            Dictionary<string, Material> materials,
            Dictionary<string, UnitDefinition> definitions,
            (GameObject Selection, GameObject Order) markers,
            BattleTuning tuning,
            Dictionary<TerrainKind, BattlefieldProfile> profiles)
        {
            var setup = AssetDatabase.LoadAssetAtPath<BattleSetup>(BattleSetupPath);
            bool isNew = setup == null;

            if (isNew)
            {
                setup = ScriptableObject.CreateInstance<BattleSetup>();
                AssetDatabase.CreateAsset(setup, BattleSetupPath);
            }

            setup.Tuning = tuning;

            // 기본 전장을 강으로 둡니다.
            //
            // 이 게임의 주요 사망 수단이 익사인데, 물이 없는 전장에서는 그 설계가 통째로 잠듭니다.
            // 다른 지형 프로필도 모두 구워 두었으니 인스펙터에서 갈아 끼우면 됩니다.
            setup.TerrainProfile = profiles[TerrainKind.River];

            setup.TerrainMaterials = new TerrainMaterialSet
            {
                Ground = materials["M_Terrain_Ground"],
                Cliff = materials["M_Terrain_Cliff"],
                Water = materials["M_Terrain_Water"],
            };

            setup.PlayerRoster = new[]
            {
                definitions["Infantry"],
                definitions["Archer"],
                definitions["Pike"],
            };

            setup.EnemyRoster = new[]
            {
                definitions["EnemyMilitia"],
                definitions["EnemyInfantry"],
                definitions["EnemyArcher"],
            };

            setup.SelectionMarkerPrefab = markers.Selection;
            setup.OrderMarkerPrefab = markers.Order;

            EditorUtility.SetDirty(setup);
        }

        // ====================================================================================================
        // 8. Private Methods - Helpers
        // ====================================================================================================

        /// <summary>
        /// 임시 계층을 프리팹으로 굽고, 원본을 지운 뒤 <b>경로에서 다시 불러와</b> 돌려줍니다.
        ///
        /// <c>SaveAsPrefabAsset</c>의 반환값을 그대로 쓰지 않는 이유는, 임포트 타이밍에 따라
        /// 아직 에셋으로 등록되지 않은 객체가 돌아올 수 있기 때문입니다.
        /// 그 참조를 다른 에셋에 저장하면 조용히 null로 직렬화됩니다.
        /// </summary>
        private static GameObject SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (loaded == null)
            {
                Debug.LogError($"[PrototypeAssetBuilder] 프리팹을 다시 불러오지 못했습니다: {path}");
            }

            return loaded;
        }

        private static GameObject CreateMeshChild(Transform parent, string childName, Mesh mesh, Material material)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(parent, false);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            return go;
        }

        /// <summary>
        /// 유니티 내장 프리미티브 메시를 가져옵니다.
        /// 임시 오브젝트를 만들어 메시 참조만 빼내고 즉시 지웁니다. 메시 자체는 내장 에셋이라 살아남습니다.
        /// </summary>
        private static Mesh GetBuiltinMesh(PrimitiveType type)
        {
            var temp = GameObject.CreatePrimitive(type);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            return mesh;
        }

        /// <summary>
        /// 에셋이 있으면 불러오고, 없으면 팩토리로 만들어 저장합니다.
        /// </summary>
        private static T LoadOrCreate<T>(string path, System.Func<T> factory) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = factory();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>
        /// 필요한 폴더를 만듭니다. 이미 있으면 아무것도 하지 않습니다.
        /// </summary>
        private static void EnsureFolders()
        {
            string[] folders =
            {
                MaterialDir,
                UnitPrefabDir,
                EnemyPrefabDir,
                SystemPrefabDir,
                UnitDataDir,
                EnemyDataDir,
                ConfigDataDir,
                TerrainDataDir,
            };

            foreach (string folder in folders)
            {
                EnsureFolder(folder);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int lastSlash = path.LastIndexOf('/');
            string parent = path.Substring(0, lastSlash);
            string leaf = path.Substring(lastSlash + 1);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
