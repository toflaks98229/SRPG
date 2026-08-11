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

        /// <summary>수면 메시 한 칸의 월드 크기입니다. 파도의 최단 파장보다 촘촘해야 합니다.</summary>
        private const float WaterCellSize = 1f;

        // ====================================================================================================
        // 1-1. Palette
        // ====================================================================================================

        /// <summary>머티리얼이 없을 때 쓰는 지면 색입니다.</summary>
        private static readonly Color GroundColor = new Color(0.44f, 0.62f, 0.36f);
        /// <summary>머티리얼이 없을 때 쓰는 바위 색입니다.</summary>
        private static readonly Color RockColor = new Color(0.42f, 0.40f, 0.43f);
        /// <summary>머티리얼이 없을 때 쓰는 물 색입니다.</summary>
        private static readonly Color WaterColor = new Color(0.18f, 0.35f, 0.52f);

        // ====================================================================================================
        // 1-2. Fields
        // ====================================================================================================

        /// <summary>연결된 머티리얼입니다. 비어 있으면 임시 머티리얼을 만들어 씁니다.</summary>
        private TerrainMaterialSet _materials;

        /// <summary>이번 전장의 풀밭입니다. 바깥이 유닛을 눌림 주체로 등록할 때 씁니다.</summary>
        public GrassField Grass { get; private set; }

        // ====================================================================================================
        // 1-3. Clouds
        // ====================================================================================================

        /// <summary>
        /// 이번 전장의 하늘입니다. 절대 null 이 아닙니다.
        ///
        /// <b>왜 인스펙터 필드가 아닌가</b>
        ///
        /// 이 컴포넌트는 런타임에 <c>AddComponent</c> 됩니다. 여기 붙인 직렬화 필드는
        /// 인스펙터에 뜰 기회가 없어, 구름을 짙게 해 보려면 코드를 고쳐야 했습니다.
        /// 들판 프로필이 먼저 같은 이유로 에셋이 되었고, 구름도 같은 길을 갑니다.
        /// </summary>
        private SkyProfile _sky;

        /// <summary>구름의 배율·속도·깊이·높이 식별자입니다.</summary>
        private static readonly int CloudParamsId = Shader.PropertyToID("_CloudParams");
        /// <summary>구름의 방향·덮임·부드러움 식별자입니다.</summary>
        private static readonly int CloudFlowId = Shader.PropertyToID("_CloudFlow");

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 전장을 세웁니다. 이미 세워져 있으면 지우고 다시 만듭니다.
        /// </summary>
        /// <param name="battlefield">화면에 세울 전장입니다. 지형과 물이 여기서 나옵니다.</param>
        /// <param name="materials">지형·물 머티리얼 묶음입니다. 비어 있으면 코드로 만듭니다.</param>
        /// <param name="grass">들판의 생김새입니다. 비우면 코드 기본값을 씁니다.</param>
        public void Build(
            Battlefield battlefield,
            TerrainMaterialSet materials = default,
            GrassProfile grass = null,
            SkyProfile sky = null)
        {
            _sky = sky;

            ClearChildren();

            if (battlefield == null)
            {
                return;
            }

            // 하나라도 비면 전부 폴백을 씁니다. 절반만 적용된 화면은
            // 무엇이 연결되고 무엇이 빠졌는지 알아보기 더 어렵습니다.
            _materials = materials.IsComplete ? materials : default;

            PublishCloudSettings();

            BuildTerrain(battlefield);
            BuildWater(battlefield);
            BuildObstacles(battlefield);
            BuildGrass(battlefield, grass);
        }

        /// <summary>
        /// 구름 설정을 전역으로 올립니다.
        ///
        /// <b>왜 머티리얼이 아니라 전역인가</b>
        ///
        /// 구름은 머티리얼의 성질이 아니라 하늘의 성질입니다.
        /// 지형·물·풀·유닛이 각자 값을 들면 언젠가 하나가 어긋나고,
        /// 그때부터 <b>지형은 그늘인데 그 위의 풀은 볕을 받습니다</b>.
        /// 값을 한 곳에서 올리고 네 셰이더가 같은 함수를 부릅니다.
        /// </summary>
        private void PublishCloudSettings()
        {
            var sky = _sky != null ? _sky : SkyProfile.CreateDefault();

            Shader.SetGlobalVector(CloudParamsId, new Vector4(
                sky.Scale,
                sky.Speed,
                sky.Depth,
                sky.Height));

            var direction = sky.Direction.sqrMagnitude > 1e-6f
                ? sky.Direction.normalized
                : Vector2.right;

            Shader.SetGlobalVector(CloudFlowId, new Vector4(
                direction.x,
                direction.y,
                sky.Coverage,
                sky.Softness));
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
            terrain.materialTemplate = ResolveTerrainMaterial(battlefield);

            // GPU 인스턴싱을 끕니다.
            // 켜면 정점 위치가 하이트맵 텍스처에서 나오는데, 그 경로는 유니티의 터레인 셰이더에만
            // 들어 있습니다. 우리 셰이더로는 <b>지형이 평평하게 납작해집니다</b>.
            terrain.drawInstanced = false;

            // 지형 레이어로 표시해야 클릭 레이캐스트가 유닛을 건너뛰고 지면만 잡습니다.
            GameLayers.ApplyRecursively(terrainObject, GameLayers.Terrain);
        }

        /// <summary>
        /// 해수면을 깝니다. 터레인이 물 아래로 내려간 자리가 강과 물가가 됩니다.
        ///
        /// <b>판 하나가 전부입니다</b>
        ///
        /// 강의 모양을 따로 만들지 않습니다. 하이트맵이 이미 강줄기를 파 놓았으므로,
        /// 평평한 판을 해수면 높이에 깔면 <b>파인 곳에만 물이 보입니다</b>.
        /// 물의 형태는 지형이 정하고, 여기서는 수면의 높이만 정합니다.
        ///
        /// 물 셰이더가 그 아래 지형까지의 거리를 재기 때문에
        /// 얕은 여울은 저절로 밝게 드러납니다 — 도하 지점이 눈에 보입니다.
        /// </summary>
        private void BuildWater(Battlefield battlefield)
        {
            // 물은 클릭 대상이 아닙니다. 프리미티브로 만들면 콜라이더가 딸려 와
            // 바다를 클릭했을 때 이동 명령이 나가므로, 필요한 것만 직접 붙입니다.
            var water = new GameObject("Water");
            water.transform.SetParent(transform, false);

            // 카메라가 전장 밖을 비출 때 빈 공간이 보이지 않도록 넉넉하게 키웁니다.
            float size = battlefield.WorldSize * 3f;

            water.transform.position = new Vector3(
                battlefield.Origin.x + battlefield.WorldSize * 0.5f,
                battlefield.SeaLevel,
                battlefield.Origin.z + battlefield.WorldSize * 0.5f);

            water.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            water.transform.localScale = new Vector3(size, size, 1f);

            // 한 칸이 대략 1 월드 단위가 되도록 나눕니다.
            // 파도의 가장 짧은 파장이 3~4 단위이므로 이 정도면 마루가 뭉개지지 않습니다.
            int segments = Mathf.Clamp(Mathf.RoundToInt(size / WaterCellSize), 32, 250);

            water.AddComponent<MeshFilter>().sharedMesh = PrototypeVisuals.GetWaterGridMesh(segments);

            var renderer = water.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ResolveWaterMaterial(battlefield);

            // 물은 그림자를 드리우지 않습니다. 반투명한 판이 강바닥을 검게 덮어 버립니다.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
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
        /// 풀을 심습니다.
        ///
        /// 지형과 물과 장애물이 모두 선 뒤에 심습니다 —
        /// 어디가 물이고 어디가 절벽인지 정해진 다음이라야 심을 자리를 고를 수 있습니다.
        /// </summary>
        /// <param name="battlefield">풀을 심을 전장입니다.</param>
        /// <param name="grass">들판의 생김새입니다. 비우면 코드 기본값을 씁니다.</param>
        private void BuildGrass(Battlefield battlefield, GrassProfile grass)
        {
            var grassObject = new GameObject("Grass");
            grassObject.transform.SetParent(transform, false);

            // 인스턴싱 호출이 이 오브젝트의 레이어로 나갑니다.
            // 지형과 같은 레이어에 두어야 카메라 컬링 마스크가 땅과 풀을 함께 다룹니다.
            // 콜라이더는 붙이지 않으므로 클릭 레이캐스트는 풀을 그대로 통과합니다.
            GameLayers.ApplyRecursively(grassObject, GameLayers.Terrain);

            Grass = grassObject.AddComponent<GrassField>();
            Grass.Build(battlefield, grass);
        }

        /// <summary>
        /// 터레인이 쓸 머티리얼을 정합니다.
        ///
        /// <b>이 전장의 숫자를 셰이더에 넣어야 합니다</b>
        ///
        /// 지형 셰이더는 해수면과 고도폭을 알아야 저지와 고지를 가릅니다.
        /// 그 숫자는 전장마다 다르므로 에셋에 굳어 있으면 안 됩니다.
        /// 연결된 머티리얼은 색 취향만 물려주고, 세 숫자는 여기서 덮습니다.
        ///
        /// 전용 셰이더가 없으면 URP 터레인 셰이더로 물러납니다.
        /// <b>일반 Lit은 안 됩니다</b> — 터레인에 물리면 스플랫맵 경로가 없어 표면이 검게 나옵니다.
        /// </summary>
        private Material ResolveTerrainMaterial(Battlefield battlefield)
        {
            // 해수면 위로 남은 높이입니다. 이 폭 안에서 저지→고지 색이 갈립니다.
            float heightRange = battlefield.Heightmap.MaxElevation - battlefield.Heightmap.SeaLevel;

            var material = PrototypeVisuals.CreateTerrainMaterial(
                _materials.Ground,
                battlefield.SeaLevel,
                heightRange,
                battlefield.ClimbLimitDegrees);

            if (material != null)
            {
                return material;
            }

            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");

            if (shader == null)
            {
                // 터레인 셰이더가 없으면 색만이라도 맞춰 둡니다.
                return PrototypeVisuals.CreateMaterial(GroundColor);
            }

            return new Material(shader) { color = GroundColor };
        }

        /// <summary>
        /// 물이 쓸 머티리얼을 정합니다.
        ///
        /// <b>연결된 에셋을 그대로 쓰지 않습니다</b>
        ///
        /// 색은 여전히 화면 깊이에서 나오지만, <b>파도는 정지 수심 지도를 알아야</b> 합니다 —
        /// 물가에서 파고를 죽이지 않으면 해수면이 오르내려 보이는 물가와
        /// 실제로 건널 수 있는 경계가 어긋납니다.
        ///
        /// 그 지도는 전장마다 다릅니다. 공유 에셋에 써 넣으면 마지막 전장의 지도가 남으므로,
        /// 지형 머티리얼과 같이 복제해서 이번 전장의 것을 덮어씁니다.
        /// </summary>
        private Material ResolveWaterMaterial(Battlefield battlefield)
        {
            var restDepthMap = CreateWaterRestDepthMap(
                battlefield,
                out float maxRestDepth,
                out float shorelineSlope);

            var heightmap = battlefield.Heightmap;
            int resolution = heightmap.Resolution;

            // 하이트맵의 표본은 격자의 <b>모서리</b>에 있고 텍스처의 표본은 텍셀 <b>가운데</b>에 있습니다.
            // 반 텍셀을 보정하지 않으면 물가에서 파도가 죽는 선이 실제 물가와 어긋납니다.
            var depthArea = new Vector4(
                battlefield.Origin.x,
                battlefield.Origin.z,
                (resolution - 1f) / (resolution * heightmap.WorldSize),
                0.5f / resolution);

            return PrototypeVisuals.CreateWaterMaterial(
                _materials.Water,
                restDepthMap,
                depthArea,
                maxRestDepth,
                shorelineSlope,
                WaterColor);
        }

        /// <summary>
        /// 파도가 없을 때의 수심을 구워 텍스처로 만듭니다. 붉은 채널이 미터 단위 수심입니다.
        ///
        /// <b>왜 화면 깊이를 쓰지 않는가</b>
        ///
        /// 파고를 정하는 일은 정점 셰이더가 하는데, 그 단계에는 화면 깊이가 없습니다.
        /// 있다 해도 쓰면 안 됩니다 — 파도가 만든 깊이 변화에 파도가 다시 반응하면
        /// 되먹임이 생겨 수면이 요동칩니다. 지형에서 한 번 구운 값이라야 안정적입니다.
        /// </summary>
        /// <param name="battlefield">수심을 읽을 전장입니다.</param>
        /// <param name="maxDepth">이 전장에서 가장 깊은 물의 깊이입니다. 파도가 잦아드는 폭이 여기서 나옵니다.</param>
        /// <param name="shorelineSlope">
        /// 물가에서 1미터 나아갈 때 깊어지는 정도입니다. 거품선의 폭이 여기서 나옵니다.
        /// </param>
        /// <returns>정지 수심 지도입니다.</returns>
        private static Texture2D CreateWaterRestDepthMap(
            Battlefield battlefield,
            out float maxDepth,
            out float shorelineSlope)
        {
            var heightmap = battlefield.Heightmap;

            int resolution = heightmap.Resolution;
            float[,] heights = heightmap.ToTerrainHeights();

            float maxElevation = heightmap.MaxElevation;

            // 원점의 높이는 양변에서 지워지므로 지형 안쪽 값끼리 비교하면 됩니다.
            float seaLevel = heightmap.SeaLevel;

            var texture = new Texture2D(resolution, resolution, TextureFormat.RFloat, false, true)
            {
                name = "Water_RestDepth",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color[resolution * resolution];
            var depths = new float[resolution, resolution];

            maxDepth = 0f;

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    // 터레인 높이 배열은 [z, x] 순서입니다. 뒤집으면 지도가 대각으로 어긋납니다.
                    float ground = heights[z, x] * maxElevation;
                    float depth = Mathf.Max(0f, seaLevel - ground);

                    depths[z, x] = depth;
                    pixels[z * resolution + x] = new Color(depth, 0f, 0f, 0f);

                    if (depth > maxDepth)
                    {
                        maxDepth = depth;
                    }
                }
            }

            shorelineSlope = MeasureShorelineSlope(depths, resolution, heightmap.WorldSize);

            texture.SetPixels(pixels);
            texture.Apply(false, false);

            return texture;
        }

        /// <summary>
        /// 물가에서 물이 얼마나 가파르게 깊어지는지 잽니다. 1미터 나아갈 때의 깊이 변화입니다.
        ///
        /// <b>왜 최대 수심으로는 안 되는가</b>
        ///
        /// 처음에는 거품선의 폭을 전장의 <b>가장 깊은 물</b>에 비례시켰습니다.
        /// 그런데 가장 깊은 곳은 지도 가장자리의 먼바다입니다 — 강과는 아무 상관이 없습니다.
        /// 그래서 얕은 강이 있는 전장에서는 거품선이 <b>강 전체를 덮어</b> 하얗게 만들었습니다.
        ///
        /// 거품선은 물가에서 일정한 <b>폭</b>으로 보여야 합니다.
        /// 그 폭을 깊이로 옮기려면 물가가 얼마나 가파른지를 알아야 합니다 —
        /// 완만한 모래톱에서는 얕은 깊이가 넓게 퍼지고, 가파른 벼랑에서는 좁습니다.
        /// </summary>
        /// <param name="depths">정지 수심 배열입니다.</param>
        /// <param name="resolution">한 변의 표본 수입니다.</param>
        /// <param name="worldSize">전장의 월드 크기입니다.</param>
        /// <returns>물가의 평균 깊이 기울기입니다. 물가가 없으면 완만한 기본값입니다.</returns>
        private static float MeasureShorelineSlope(float[,] depths, int resolution, float worldSize)
        {
            float spacing = worldSize / Mathf.Max(1, resolution - 1);

            double total = 0.0;
            int samples = 0;

            for (int z = 1; z < resolution - 1; z++)
            {
                for (int x = 1; x < resolution - 1; x++)
                {
                    if (depths[z, x] <= 0f)
                    {
                        continue;
                    }

                    // 물가란 물이면서 뭍과 맞닿은 자리입니다.
                    bool touchesLand = depths[z, x - 1] <= 0f || depths[z, x + 1] <= 0f
                                    || depths[z - 1, x] <= 0f || depths[z + 1, x] <= 0f;

                    if (!touchesLand)
                    {
                        continue;
                    }

                    float gradientX = (depths[z, x + 1] - depths[z, x - 1]) / (2f * spacing);
                    float gradientZ = (depths[z + 1, x] - depths[z - 1, x]) / (2f * spacing);

                    total += Mathf.Sqrt(gradientX * gradientX + gradientZ * gradientZ);
                    samples++;
                }
            }

            // 물가가 한 곳도 없는 전장(물이 없거나 통째로 잠긴)에서는 완만하다고 봅니다.
            return samples > 0 ? (float)(total / samples) : 0.05f;
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
