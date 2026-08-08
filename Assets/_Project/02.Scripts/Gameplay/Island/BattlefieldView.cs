using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Visual;
using SRPG.Systems.Battlefield;
using UnityEngine;

namespace SRPG.Gameplay.Island
{
    /// <summary>
    /// 전장을 화면에 세웁니다. <b>유니티 터레인</b>과 물, 그리고 장애물입니다.
    ///
    /// <b>사각 발판을 굽지 않습니다</b>
    ///
    /// 예전에는 타일마다 평면 쿼드와 측면 벽을 만들어 지형을 그렸습니다.
    /// 그러면 아무리 잘게 나눠도 눈이 격자를 찾아냅니다.
    ///
    /// 터레인은 애초에 연속면입니다. 이음매도 계단도 없고,
    /// 렌더링·충돌·높이 질의를 엔진이 맡습니다.
    /// 여기서 하는 일은 <b>숫자를 넘겨주고 물을 깔고 장애물을 세우는 것</b>이 전부입니다.
    ///
    /// <b>클릭 판정이 콜라이더에서 나옵니다</b>
    ///
    /// <see cref="TerrainCollider"/>가 자동으로 붙으므로 지면 레이캐스트가 그대로 동작합니다.
    /// 예전처럼 메시 콜라이더를 손으로 붙일 필요가 없습니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattlefieldView : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>장애물이 지면 위로 솟는 높이입니다.</summary>
        private const float ObstacleHeight = 1.6f;

        /// <summary>장애물의 폭입니다. 칸 크기에 대한 비율입니다.</summary>
        private const float ObstacleWidth = 0.85f;

        // ====================================================================================================
        // 1-1. Palette
        // ====================================================================================================

        private static readonly Color GroundColor = new Color(0.44f, 0.62f, 0.36f);
        private static readonly Color RockColor = new Color(0.42f, 0.40f, 0.43f);
        private static readonly Color WaterColor = new Color(0.18f, 0.35f, 0.52f);

        // ====================================================================================================
        // 1-2. Fields
        // ====================================================================================================

        /// <summary>연결된 머티리얼입니다. 비어 있으면 임시 머티리얼을 만들어 씁니다.</summary>
        private TerrainMaterialSet _materials;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 전장을 세웁니다. 이미 세워져 있으면 지우고 다시 만듭니다.
        /// </summary>
        public void Build(Battlefield battlefield, TerrainMaterialSet materials = default)
        {
            ClearChildren();

            if (battlefield == null)
            {
                return;
            }

            // 하나라도 비면 전부 폴백을 씁니다. 절반만 적용된 화면은
            // 무엇이 연결되고 무엇이 빠졌는지 알아보기 더 어렵습니다.
            _materials = materials.IsComplete ? materials : default;

            BuildTerrain(battlefield);
            BuildWater(battlefield);
            BuildObstacles(battlefield);
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 유니티 터레인을 만들고 높이를 넘깁니다.
        /// </summary>
        private void BuildTerrain(Battlefield battlefield)
        {
            var heightmap = battlefield.Heightmap;

            var data = new TerrainData
            {
                name = "Battlefield",
                heightmapResolution = heightmap.Resolution,
            };

            // 크기는 반드시 해상도를 정한 뒤에 넣어야 합니다.
            // 순서를 바꾸면 유니티가 높이를 다시 늘려 지형이 뭉개집니다.
            data.size = new Vector3(heightmap.WorldSize, heightmap.MaxElevation, heightmap.WorldSize);
            data.SetHeights(0, 0, heightmap.ToTerrainHeights());

            var terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Terrain";
            terrainObject.transform.SetParent(transform, false);
            terrainObject.transform.position = battlefield.Origin;

            var terrain = terrainObject.GetComponent<Terrain>();
            terrain.materialTemplate = ResolveTerrainMaterial();

            // 지형 레이어로 표시해야 클릭 레이캐스트가 유닛을 건너뛰고 지면만 잡습니다.
            GameLayers.ApplyRecursively(terrainObject, GameLayers.Terrain);
        }

        /// <summary>
        /// 해수면을 깝니다. 터레인이 물 아래로 내려간 자리가 바다가 됩니다.
        /// </summary>
        private void BuildWater(Battlefield battlefield)
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Quad);
            water.name = "Water";
            water.transform.SetParent(transform, false);

            // 카메라가 전장 밖을 비출 때 빈 공간이 보이지 않도록 넉넉하게 키웁니다.
            float size = battlefield.WorldSize * 3f;

            water.transform.position = new Vector3(
                battlefield.Origin.x + battlefield.WorldSize * 0.5f,
                battlefield.SeaLevel,
                battlefield.Origin.z + battlefield.WorldSize * 0.5f);

            water.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            water.transform.localScale = new Vector3(size, size, 1f);

            // 물은 클릭 대상이 아닙니다. 콜라이더가 있으면 바다를 클릭해 이동 명령이 나갑니다.
            Destroy(water.GetComponent<Collider>());

            var renderer = water.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _materials.Water != null
                ? _materials.Water
                : PrototypeVisuals.CreateMaterial(WaterColor);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// 장애물을 세웁니다.
        ///
        /// 지형이 아니라 <b>오브젝트</b>입니다. 배너로드의 바위와 나무가 그렇듯,
        /// 지면은 연속으로 두고 그 위에 물체를 놓습니다.
        /// 지형을 깎아 길을 막으면 그 벽이 다시 격자를 드러냅니다.
        /// </summary>
        private void BuildObstacles(Battlefield battlefield)
        {
            var grid = battlefield.Grid;

            var obstacleRoot = new GameObject("Obstacles");
            obstacleRoot.transform.SetParent(transform, false);

            var rockMaterial = _materials.Cliff != null
                ? _materials.Cliff
                : PrototypeVisuals.CreateMaterial(RockColor);

            for (int i = 0; i < grid.AllTiles.Count; i++)
            {
                var tile = grid.AllTiles[i];

                if (tile.Type != TileType.Cliff)
                {
                    continue;
                }

                var block = new GameObject("Rock");
                block.transform.SetParent(obstacleRoot.transform, false);

                float height = ObstacleHeight;

                block.transform.position = tile.WorldCenter + Vector3.up * (height * 0.5f);
                block.transform.localScale = new Vector3(
                    grid.CellSize * ObstacleWidth,
                    height,
                    grid.CellSize * ObstacleWidth);

                block.AddComponent<MeshFilter>().sharedMesh = PrototypeVisuals.GetCubeMesh();
                block.AddComponent<MeshRenderer>().sharedMaterial = rockMaterial;
            }

            // 장애물은 통행에도 클릭에도 영향을 주지 않습니다.
            // 길을 막는 것은 격자가 이미 알고 있고, 콜라이더가 있으면
            // 바위를 클릭했을 때 지면이 아니라 바위 표면이 잡힙니다.
        }

        /// <summary>
        /// 터레인이 쓸 머티리얼을 정합니다.
        ///
        /// <b>터레인 전용 셰이더가 필요합니다.</b>
        /// 일반 Lit 셰이더를 터레인에 물리면 스플랫맵 경로가 없어 표면이 검게 나옵니다.
        /// 연결된 지면 머티리얼이 터레인용이 아니면 쓰지 않고 전용 머티리얼을 만듭니다.
        /// </summary>
        private Material ResolveTerrainMaterial()
        {
            if (_materials.Ground != null && IsTerrainShader(_materials.Ground.shader))
            {
                return _materials.Ground;
            }

            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");

            if (shader == null)
            {
                // 터레인 셰이더가 없으면 색만이라도 맞춰 둡니다.
                return PrototypeVisuals.CreateMaterial(GroundColor);
            }

            return new Material(shader) { color = GroundColor };
        }

        private static bool IsTerrainShader(Shader shader)
        {
            return shader != null && shader.name.Contains("Terrain");
        }

        private void ClearChildren()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i).gameObject;

                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }
    }
}
