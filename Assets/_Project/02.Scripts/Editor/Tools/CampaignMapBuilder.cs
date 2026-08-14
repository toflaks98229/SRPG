using System.Collections.Generic;
using System.IO;
using SRPG.Composition;
using SRPG.Data;
using UnityEditor;
using UnityEngine;

namespace SRPG.Editor.Tools
{
    /// <summary>
    /// 캠페인 지도를 만들어 씬에 연결합니다.
    ///
    /// <b>왜 코드로 만드는가</b>
    ///
    /// 지도는 결국 손으로 다듬을 것입니다 — 지점 이름도 위치도 편성도 인스펙터에서 만집니다.
    /// 그런데 <b>처음 한 벌</b>을 인스펙터에서 찍는 것은 다른 일입니다.
    /// 지점 열한 곳에 각각 이름·좌표·연결·지형·편성을 넣어야 하고,
    /// 연결은 번호로 가리키므로 하나만 어긋나도 길이 끊기는데 화면에는 드러나지 않습니다.
    ///
    /// 한 번 만들어 두면 그다음부터는 인스펙터가 편합니다.
    /// 그래서 이 도구는 <b>이미 있는 에셋을 덮어쓰지 않습니다.</b>
    /// 손으로 고친 것을 도구가 되돌리는 일이 없어야 합니다.
    /// </summary>
    public static class CampaignMapBuilder
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>만들 지도 에셋의 경로입니다.</summary>
        private const string MapPath = "Assets/_Project/03.DataAssets/Campaign/WorldMap_Campaign.asset";

        /// <summary>적 유닛 정의를 찾을 폴더입니다.</summary>
        private const string EnemyFolder = "Assets/_Project/03.DataAssets";

        // ====================================================================================================
        // 2. Menu
        // ====================================================================================================

        /// <summary>
        /// 캠페인 지도를 만들고 월드맵 씬의 스코프에 연결합니다.
        ///
        /// 이미 있으면 만들지 않고 연결만 확인합니다.
        /// </summary>
        [MenuItem("SRPG/배선/⑯ 캠페인 지도 만들기", priority = 45)]
        public static void BuildCampaignMap()
        {
            var existing = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(MapPath);

            if (existing != null)
            {
                Debug.Log($"[배선] 지도가 이미 있습니다 — {MapPath}. 덮어쓰지 않습니다.");

                Wire(existing);

                return;
            }

            var roster = LoadEnemies();

            if (roster.Militia == null || roster.Infantry == null || roster.Archer == null)
            {
                Debug.LogWarning(
                    "[배선] 적 유닛 정의를 찾지 못했습니다 — " +
                    $"민병={Describe(roster.Militia)}, 보병={Describe(roster.Infantry)}, 궁수={Describe(roster.Archer)}");

                return;
            }

            var map = ScriptableObject.CreateInstance<WorldMapDefinition>();
            map.name = Path.GetFileNameWithoutExtension(MapPath);

            SetNodes(map, BuildNodes(roster));

            Directory.CreateDirectory(Path.GetDirectoryName(MapPath));

            AssetDatabase.CreateAsset(map, MapPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[배선] 캠페인 지도를 만들었습니다 — 지점 {map.NodeCount}곳, {MapPath}");

            Wire(map);
        }

        // ====================================================================================================
        // 3. Map
        // ====================================================================================================

        /// <summary>
        /// 지도의 지점들을 만듭니다.
        ///
        /// <b>어떤 모양인가</b>
        ///
        /// 본진에서 시작해 갈래로 나뉘었다가 다시 모이고, 마지막에 한 곳으로 좁혀집니다.
        /// 갈래가 있어야 "어디부터 칠 것인가"가 선택이 되고,
        /// 다시 모여야 어느 길로 가든 남은 판 수가 비슷해집니다.
        ///
        /// <b>난이도는 지형과 편성 둘로 올립니다.</b>
        /// 수만 늘리면 뒤로 갈수록 같은 싸움이 길어질 뿐입니다.
        /// 앞쪽은 민병 위주로 두고, 중반에 보병이, 후반에 궁수가 섞입니다 —
        /// 궁수가 섞이는 순간 "붙기 전에 무엇을 맞는가"가 판단에 들어옵니다.
        ///
        /// 지형은 판마다 다른 문제를 냅니다. 강은 도하 지점을 강요하고,
        /// 숲은 시야를 끊고, 바위땅은 통로를 좁히고, 구릉은 고지를 만듭니다.
        /// 같은 지형이 연달아 나오지 않게 늘어놓았습니다.
        /// </summary>
        /// <param name="roster">쓸 적 유닛 정의들입니다.</param>
        /// <returns>지도에 넣을 지점들입니다.</returns>
        private static WorldNode[] BuildNodes(EnemyRoster roster)
        {
            var militia = new[] { roster.Militia };
            var mixed = new[] { roster.Militia, roster.Infantry };
            var full = new[] { roster.Militia, roster.Infantry, roster.Archer };
            var veteran = new[] { roster.Infantry, roster.Archer };

            return new[]
            {
                // ── 시작 ────────────────────────────────────────────────────────────
                Node("본진", 0f, 0f, TerrainKind.Plains, 20260901, links: new[] { 1, 2 }),

                // ── 1층 — 두 갈래 ───────────────────────────────────────────────────
                Node("여울목", 3f, 1.6f, TerrainKind.River, 20260902, new[] { 3, 4 }, militia, 2, 4),
                Node("낮은 숲", 3f, -1.6f, TerrainKind.Forest, 20260903, new[] { 4, 5 }, militia, 2, 5),

                // ── 2층 — 세 갈래 ───────────────────────────────────────────────────
                Node("돌밭 고개", 6f, 2.8f, TerrainKind.Rocky, 20260904, new[] { 6 }, mixed, 3, 4),
                Node("바람 언덕", 6f, 0f, TerrainKind.Hills, 20260905, new[] { 6, 7 }, mixed, 3, 5),
                Node("물안개 늪", 6f, -2.8f, TerrainKind.River, 20260906, new[] { 7 }, mixed, 3, 5),

                // ── 3층 — 다시 둘 ───────────────────────────────────────────────────
                Node("무너진 다리", 9f, 1.6f, TerrainKind.River, 20260907, new[] { 8 }, full, 4, 5),
                Node("검은 숲", 9f, -1.6f, TerrainKind.Forest, 20260908, new[] { 8 }, full, 4, 5),

                // ── 4층 — 한 곳으로 ─────────────────────────────────────────────────
                Node("성문 앞 벌판", 12f, 0f, TerrainKind.Plains, 20260909, new[] { 9 }, full, 5, 5),

                // ── 끝 ──────────────────────────────────────────────────────────────
                Node("왕좌의 언덕", 15f, 0f, TerrainKind.Hills, 20260910, System.Array.Empty<int>(), veteran, 5, 6),
            };
        }

        /// <summary>
        /// 지점 하나를 만듭니다.
        /// </summary>
        /// <param name="displayName">화면에 띄울 이름입니다.</param>
        /// <param name="x">지도에서의 가로 위치입니다.</param>
        /// <param name="y">지도에서의 세로 위치입니다.</param>
        /// <param name="terrain">전장의 지형입니다.</param>
        /// <param name="seed">전장을 만들 씨앗입니다. 지점마다 달라야 같은 판이 반복되지 않습니다.</param>
        /// <param name="links">여기서 갈 수 있는 지점 번호들입니다.</param>
        /// <param name="enemies">이 지점을 지키는 적의 종류입니다. 없으면 빈 지점입니다.</param>
        /// <param name="squads">적 분대 수입니다.</param>
        /// <param name="soldiers">분대당 병사 수입니다.</param>
        /// <returns>만들어진 지점입니다.</returns>
        private static WorldNode Node(
            string displayName,
            float x,
            float y,
            TerrainKind terrain,
            int seed,
            int[] links,
            UnitDefinition[] enemies = null,
            int squads = 0,
            int soldiers = 0)
        {
            return new WorldNode
            {
                DisplayName = displayName,
                Position = new Vector2(x, y),
                Battlefield = BattlefieldSpec.CreateDefault(terrain, seed),
                Links = links,
                EnemyRoster = enemies,
                EnemySquadCount = squads,
                SoldiersPerEnemySquad = soldiers,
            };
        }

        // ====================================================================================================
        // 4. Wiring
        // ====================================================================================================

        /// <summary>
        /// 지금 열려 있는 씬의 캠페인 스코프에 지도를 꽂습니다.
        ///
        /// 열려 있는 씬만 봅니다. 다른 씬을 몰래 열었다 닫으면 편집 중이던 것을 건드립니다.
        /// </summary>
        /// <param name="map">꽂을 지도입니다.</param>
        private static void Wire(WorldMapDefinition map)
        {
            int wired = 0;

            foreach (var scope in Object.FindObjectsByType<CampaignLifetimeScope>(FindObjectsInactive.Include))
            {
                var serialized = new SerializedObject(scope);
                var property = serialized.FindProperty("_worldMap");

                if (property == null || property.objectReferenceValue == map)
                {
                    continue;
                }

                property.objectReferenceValue = map;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(scope);
                wired++;
            }

            if (wired > 0)
            {
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                {
                    var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);

                    if (scene.isLoaded)
                    {
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                    }
                }
            }

            Debug.Log(
                wired > 0
                    ? $"[배선] 캠페인 스코프 {wired}개에 지도를 연결했습니다."
                    : "[배선] 연결할 캠페인 스코프가 열려 있지 않습니다. 월드맵 씬을 열고 다시 실행하십시오.");
        }

        // ====================================================================================================
        // 5. Helpers
        // ====================================================================================================

        /// <summary>지도에 쓸 적 유닛 정의 묶음입니다.</summary>
        private struct EnemyRoster
        {
            /// <summary>민병입니다. 초반 지점을 채웁니다.</summary>
            public UnitDefinition Militia;

            /// <summary>보병입니다. 중반부터 섞입니다.</summary>
            public UnitDefinition Infantry;

            /// <summary>궁수입니다. 후반에 붙기 전의 판단을 만듭니다.</summary>
            public UnitDefinition Archer;
        }

        /// <summary>
        /// 적 유닛 정의를 이름으로 찾습니다.
        /// </summary>
        /// <returns>찾은 정의들입니다. 없는 것은 null 입니다.</returns>
        private static EnemyRoster LoadEnemies()
        {
            return new EnemyRoster
            {
                Militia = FindUnit("EnemyDef_Militia"),
                Infantry = FindUnit("EnemyDef_Infantry"),
                Archer = FindUnit("EnemyDef_Archer"),
            };
        }

        /// <summary>
        /// 이름이 정확히 일치하는 유닛 정의를 찾습니다.
        /// </summary>
        /// <param name="assetName">찾을 에셋 이름입니다.</param>
        /// <returns>찾은 정의입니다. 없으면 null 입니다.</returns>
        private static UnitDefinition FindUnit(string assetName)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{assetName} t:UnitDefinition", new[] { EnemyFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (Path.GetFileNameWithoutExtension(path) == assetName)
                {
                    return AssetDatabase.LoadAssetAtPath<UnitDefinition>(path);
                }
            }

            return null;
        }

        /// <summary>
        /// 지도에 지점 배열을 넣습니다.
        ///
        /// <c>Nodes</c> 는 읽기 전용이라 직렬화 필드로 직접 넣습니다.
        /// 배열을 공개로 열면 실행 중에 지도가 바뀔 수 있게 되는데, 그럴 이유가 없습니다.
        /// </summary>
        /// <param name="map">넣을 지도입니다.</param>
        /// <param name="nodes">넣을 지점들입니다.</param>
        private static void SetNodes(WorldMapDefinition map, WorldNode[] nodes)
        {
            var serialized = new SerializedObject(map);
            var array = serialized.FindProperty("_nodes");

            array.arraySize = nodes.Length;

            for (int i = 0; i < nodes.Length; i++)
            {
                var element = array.GetArrayElementAtIndex(i);

                element.FindPropertyRelative("DisplayName").stringValue = nodes[i].DisplayName;
                element.FindPropertyRelative("Position").vector2Value = nodes[i].Position;

                var battlefield = element.FindPropertyRelative("Battlefield");
                battlefield.FindPropertyRelative("Seed").intValue = nodes[i].Battlefield.Seed;
                battlefield.FindPropertyRelative("Terrain").enumValueIndex = (int)nodes[i].Battlefield.Terrain;
                battlefield.FindPropertyRelative("Width").intValue = nodes[i].Battlefield.Width;
                battlefield.FindPropertyRelative("Depth").intValue = nodes[i].Battlefield.Depth;

                Fill(element.FindPropertyRelative("Links"), nodes[i].Links);
                Fill(element.FindPropertyRelative("EnemyRoster"), nodes[i].EnemyRoster);

                element.FindPropertyRelative("EnemySquadCount").intValue = nodes[i].EnemySquadCount;
                element.FindPropertyRelative("SoldiersPerEnemySquad").intValue = nodes[i].SoldiersPerEnemySquad;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>정수 배열 프로퍼티를 채웁니다.</summary>
        /// <param name="property">채울 프로퍼티입니다.</param>
        /// <param name="values">넣을 값들입니다. null이면 빈 배열이 됩니다.</param>
        private static void Fill(SerializedProperty property, IReadOnlyList<int> values)
        {
            property.arraySize = values != null ? values.Count : 0;

            for (int i = 0; i < property.arraySize; i++)
            {
                property.GetArrayElementAtIndex(i).intValue = values[i];
            }
        }

        /// <summary>참조 배열 프로퍼티를 채웁니다.</summary>
        /// <param name="property">채울 프로퍼티입니다.</param>
        /// <param name="values">넣을 값들입니다. null이면 빈 배열이 됩니다.</param>
        private static void Fill(SerializedProperty property, IReadOnlyList<UnitDefinition> values)
        {
            property.arraySize = values != null ? values.Count : 0;

            for (int i = 0; i < property.arraySize; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        /// <summary>참조를 사람이 읽을 수 있게 적습니다.</summary>
        /// <param name="asset">읽을 참조입니다.</param>
        /// <returns>이름이거나 "없음" 입니다.</returns>
        private static string Describe(Object asset)
        {
            return asset != null ? asset.name : "없음";
        }
    }
}
