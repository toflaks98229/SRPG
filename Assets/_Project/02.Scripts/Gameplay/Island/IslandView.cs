using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Visual;
using SRPG.Systems.Grid;
using SRPG.Systems.Meshing;
using SRPG.Systems.Props;
using UnityEngine;

namespace SRPG.Gameplay.Island
{
    /// <summary>
    /// <see cref="IslandGrid"/>를 눈에 보이는 지형 메시로 변환합니다.
    ///
    /// 타일마다 GameObject를 만들지 않고 <b>역할별로</b> 메시를 하나씩 합쳐 굽습니다.
    /// 30x30 격자면 타일이 900개인데, 개별 오브젝트로 두면 드로우콜과 트랜스폼 갱신이 낭비됩니다.
    ///
    /// <b>윗면과 측면을 나눕니다</b>
    ///
    /// 이 게임에서 플레이어가 지형을 보고 판단해야 하는 것은 하나입니다 — <b>딛을 수 있는가</b>.
    /// 그 답은 면의 방향에 이미 들어 있습니다.
    ///
    ///   · 윗면 — 평평하고 걸을 수 있습니다. 잔디·모래가 덮입니다.
    ///   · 측면 — 수직이고 딛을 수 없습니다. 드러난 암반입니다.
    ///
    /// 그래서 둘을 <b>다른 메시, 다른 재질</b>로 굽습니다.
    /// 같은 재질로 두면 고도 차가 색이 아니라 음영으로만 남아, 절벽인지 언덕인지 헷갈립니다.
    /// 나눠 두면 나중에 윗면만 눈으로 갈아 끼우는 것도 재질 하나 바꾸는 일이 됩니다.
    ///
    /// 절벽 타일은 윗면까지 암반으로 갑니다. 딛을 수 없는 것은 전부 같은 재질이어야 합니다.
    /// </summary>
    public sealed class IslandView : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>해수면이 지형보다 살짝 아래에 오도록 하는 오프셋입니다.</summary>
        private const float WaterLevelOffset = -0.18f;

        /// <summary>측면 벽이 해수면 아래까지 내려가는 깊이입니다. 섬이 물에 떠 보이지 않게 합니다.</summary>
        private const float UnderwaterSkirtDepth = 1.2f;

        /// <summary>가옥 상자의 높이입니다.</summary>
        private const float HouseHeight = 1.05f;

        /// <summary>가옥 상자가 타일 안쪽으로 들어가는 비율입니다.</summary>
        private const float HouseInset = 0.34f;

        /// <summary>지형지물의 기본 밀도입니다.</summary>
        private const float PropDensity = 1f;

        /// <summary>
        /// 지형지물의 외곽선 두께입니다. 지형보다 훨씬 얇습니다.
        ///
        /// 외곽선은 뒤집힌 껍질을 <b>월드 단위로</b> 부풀려 만듭니다.
        /// 지형 두께(0.06)를 그대로 쓰면 반경 0.16짜리 조약돌은 껍질이 형상을 삼켜
        /// 검은 덩어리만 남습니다. 작은 것일수록 선이 가늘어야 형태가 보입니다.
        /// </summary>
        private const float PropOutlineWidth = 0.018f;

        // ====================================================================================================
        // 1-1. Palette
        // ====================================================================================================

        /// <summary>통행 가능한 평지의 윗면입니다.</summary>
        private static readonly Color WalkableGrassColor = new Color(0.44f, 0.62f, 0.36f);

        /// <summary>통행 가능한 해변의 윗면입니다. 평지와 같은 계열로 두어 구분이 정보로 읽히지 않게 합니다.</summary>
        private static readonly Color WalkableSandColor = new Color(0.55f, 0.66f, 0.40f);

        /// <summary>드러난 암반입니다. 측면과 절벽과 바위가 전부 이 색입니다.</summary>
        private static readonly Color RockColor = new Color(0.42f, 0.40f, 0.43f);

        /// <summary>방어 목표입니다. 유일하게 따뜻한 색이라 눈에 먼저 들어옵니다.</summary>
        private static readonly Color ObjectiveColor = new Color(0.72f, 0.48f, 0.28f);

        // ====================================================================================================
        // 1-2. Vertex Shading
        // ====================================================================================================

        /// <summary>타일 윗면의 접지 음영 값입니다. 밝은 쪽입니다.</summary>
        private const float TopShade = 1f;

        /// <summary>측면 벽 위쪽의 음영입니다. 윗면에서 살짝 꺾입니다.</summary>
        private const float WallTopShade = 0.72f;

        /// <summary>측면 벽 아래쪽의 음영입니다. 여기가 어두워야 고도 차가 읽힙니다.</summary>
        private const float WallBottomShade = 0.15f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private IslandGrid _grid;
        private TerrainMaterialSet _materials;
        private bool _useAuthoredMaterials;

        private readonly List<PropInstance> _props = new List<PropInstance>(512);

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 격자로부터 지형 메시를 만들어 자식 오브젝트로 붙입니다.
        /// </summary>
        /// <param name="grid">지형 데이터입니다.</param>
        /// <param name="materials">
        /// 지형 머티리얼입니다. 다섯 개가 모두 지정된 경우에만 사용하고,
        /// 하나라도 비면 전부 런타임 임시 머티리얼로 대체합니다.
        /// 일부만 적용하면 지형마다 톤이 달라져 오히려 알아보기 어려워지기 때문입니다.
        /// </param>
        public void Build(IslandGrid grid, TerrainMaterialSet materials = default)
        {
            _grid = grid;
            _materials = materials;
            _useAuthoredMaterials = materials.IsComplete;

            ClearChildren();

            // 색을 셋으로 줄였습니다. 플레이어가 알아야 하는 것은 딱 그만큼입니다.
            //   · 갈 수 있는 땅   — 윗면. 해변과 평지를 같은 계열로 묶습니다
            //   · 갈 수 없는 땅   — 암반. 모든 측면과 절벽과 바위
            //   · 목표            — 가옥
            //
            // 해변과 평지의 색을 나누면 정보가 하나 더 늘어나는데,
            // 그 구분은 적의 상륙 판정에만 쓰이지 플레이어의 이동 판단에는 쓰이지 않습니다.
            // 판단에 쓰이지 않는 구분은 화면에서는 잡음입니다.
            BuildWalkableTops();
            BuildRockLayer();
            BuildHouseLayer();
            BuildProps();
            BuildWaterPlane();
        }

        // ====================================================================================================
        // 4. Private Methods - Layers
        // ====================================================================================================

        /// <summary>
        /// 걸을 수 있는 타일의 윗면입니다. 잔디와 모래가 덮이는 면입니다.
        ///
        /// 해변과 평지는 재질만 다르고 역할은 같습니다. 둘 다 딛을 수 있습니다.
        /// </summary>
        private void BuildWalkableTops()
        {
            BuildTopLayer("Terrain_Top_Beach", TileType.Beach, WalkableSandColor, _materials.Beach);
            BuildTopLayer("Terrain_Top_Ground", TileType.Ground, WalkableGrassColor, _materials.Ground);
        }

        private void BuildTopLayer(string layerName, TileType type, Color fallbackColor, Material authoredMaterial)
        {
            var buffer = new MeshBuffer();
            float half = _grid.CellSize * 0.5f;

            for (int i = 0; i < _grid.AllTiles.Count; i++)
            {
                var tile = _grid.AllTiles[i];

                if (tile.Type == type)
                {
                    AddTopQuad(buffer, tile.WorldCenter, half);
                }
            }

            CreateMeshObject(layerName, buffer, fallbackColor, authoredMaterial);
        }

        /// <summary>
        /// 드러난 암반입니다. <b>딛을 수 없는 것이 전부 여기 모입니다.</b>
        ///
        ///   · 모든 타일의 측면 벽 — 수직면이라 오를 수 없습니다
        ///   · 절벽 타일의 윗면   — 통행 불가로 확정된 곳입니다
        ///
        /// 측면을 각 지형 종류의 메시에 남겨 두면, 잔디 재질의 벽이 생깁니다.
        /// 위에서 내려다보는 게임에서 벽은 실루엣의 대부분을 차지하므로 그건 곧 판독 실패입니다.
        /// </summary>
        private void BuildRockLayer()
        {
            var buffer = new MeshBuffer();
            float half = _grid.CellSize * 0.5f;

            for (int i = 0; i < _grid.AllTiles.Count; i++)
            {
                var tile = _grid.AllTiles[i];

                if (tile.IsWater)
                {
                    continue;
                }

                if (tile.Type == TileType.Cliff)
                {
                    AddTopQuad(buffer, tile.WorldCenter, half);
                }

                AddSideWalls(buffer, tile, tile.WorldCenter, half);
            }

            CreateMeshObject("Terrain_Rock", buffer, RockColor, _materials.Cliff);
        }

        /// <summary>
        /// 가옥입니다. 윗면과 상자를 함께 굽습니다. 측면은 암반 층이 이미 세웠습니다.
        /// </summary>
        private void BuildHouseLayer()
        {
            var buffer = new MeshBuffer();
            float half = _grid.CellSize * 0.5f;

            for (int i = 0; i < _grid.AllTiles.Count; i++)
            {
                var tile = _grid.AllTiles[i];

                if (tile.Type != TileType.House)
                {
                    continue;
                }

                AddTopQuad(buffer, tile.WorldCenter, half);
                AddHouseBox(buffer, tile.WorldCenter, half);
            }

            CreateMeshObject("Terrain_House", buffer, ObjectiveColor, _materials.House);
        }

        /// <summary>
        /// 지형지물입니다. 암반 계열과 지표 계열을 나눠 굽습니다.
        ///
        /// <b>콜라이더를 붙이지 않습니다.</b>
        /// 지형지물은 통행에도, 클릭 판정에도 영향을 주면 안 됩니다.
        /// 콜라이더가 있으면 바위를 클릭했을 때 지면이 아니라 바위 표면이 잡혀
        /// 이동 명령이 엉뚱한 곳에 떨어집니다.
        /// </summary>
        private void BuildProps()
        {
            PropPlacement.Generate(_grid, PropDensity, _props);

            if (_props.Count == 0)
            {
                return;
            }

            var rock = new MeshBuffer();
            var ground = new MeshBuffer();

            for (int i = 0; i < _props.Count; i++)
            {
                var prop = _props[i];

                PropMeshBuilder.AddBoulder(
                    prop.IsRock ? rock : ground,
                    prop.GroundPosition,
                    prop.Rotation,
                    prop.Radius,
                    prop.Height,
                    prop.Weathering,
                    prop.Shape);
            }

            CreateMeshObject(
                "Props_Rock", rock,
                PropMaterial(RockColor, _materials.Cliff),
                addCollider: false);

            CreateMeshObject(
                "Props_Ground", ground,
                PropMaterial(WalkableGrassColor, _materials.Ground),
                addCollider: false);
        }

        /// <summary>
        /// 섬 전체를 감싸는 바다 평면을 만듭니다.
        /// </summary>
        private void BuildWaterPlane()
        {
            float width = _grid.Width * _grid.CellSize;
            float depth = _grid.Depth * _grid.CellSize;

            // 카메라가 섬 밖을 비출 때 빈 공간이 보이지 않도록 넉넉하게 키웁니다.
            float margin = Mathf.Max(width, depth) * 1.5f;

            float minX = _grid.Origin.x - margin;
            float maxX = _grid.Origin.x + width + margin;
            float minZ = _grid.Origin.z - margin;
            float maxZ = _grid.Origin.z + depth + margin;
            float y = WaterLevelOffset;

            var buffer = new MeshBuffer();

            buffer.AddQuad(
                new Vector3(minX, y, minZ),
                new Vector3(minX, y, maxZ),
                new Vector3(maxX, y, maxZ),
                new Vector3(maxX, y, minZ),
                Vector3.up,
                TopShade);

            var water = CreateMeshObject(
                "Water",
                buffer,
                new Color(0.18f, 0.35f, 0.52f),
                _materials.Water,
                addCollider: false);

            if (water == null)
            {
                return;
            }

            var renderer = water.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 바다는 외곽선을 두르지 않습니다. 화면 끝까지 뻗은 평면이라
            // 껍데기를 부풀리면 화면 가장자리에 검은 띠가 생깁니다.
            renderer.sharedMaterial = PrototypeVisuals.CreateMaterial(new Color(0.18f, 0.35f, 0.52f));
        }

        // ====================================================================================================
        // 5. Private Methods - Geometry
        // ====================================================================================================

        /// <summary>
        /// 타일 상단 면을 추가합니다.
        /// </summary>
        private static void AddTopQuad(MeshBuffer buffer, Vector3 center, float half)
        {
            Vector3 a = new Vector3(center.x - half, center.y, center.z - half);
            Vector3 b = new Vector3(center.x - half, center.y, center.z + half);
            Vector3 c = new Vector3(center.x + half, center.y, center.z + half);
            Vector3 d = new Vector3(center.x + half, center.y, center.z - half);

            buffer.AddQuad(a, b, c, d, Vector3.up, TopShade);
        }

        /// <summary>
        /// 이웃보다 높은 쪽에 측면 벽을 세웁니다. 바다와 맞닿은 쪽은 수면 아래까지 내려 스커트를 만듭니다.
        ///
        /// <b>벽의 아래쪽을 어둡게 칠합니다.</b>
        /// 벽과 윗면이 같은 밝기면 계단 고도가 눈에 들어오지 않습니다.
        /// 텍스처를 붙이는 대신 경계에 음영을 넣는 것이 이 룩의 방식입니다.
        /// </summary>
        private void AddSideWalls(MeshBuffer buffer, Tile tile, Vector3 center, float half)
        {
            for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
            {
                var offset = GridCoord.Neighbors4[n];
                var neighbor = _grid.GetTile(tile.Coord + offset);

                float bottomY;
                if (neighbor == null || neighbor.IsWater)
                {
                    bottomY = WaterLevelOffset - UnderwaterSkirtDepth;
                }
                else if (neighbor.Height < tile.Height)
                {
                    bottomY = neighbor.WorldCenter.y;
                }
                else
                {
                    continue;
                }

                Vector3 outward = new Vector3(offset.X, 0f, offset.Y);

                // 이웃을 향한 변의 두 끝점을 구합니다. 변은 outward에 수직입니다.
                Vector3 edgeDirection = new Vector3(-outward.z, 0f, outward.x);
                Vector3 edgeCenter = center + outward * half;

                Vector3 topA = edgeCenter + edgeDirection * half;
                Vector3 topB = edgeCenter - edgeDirection * half;
                Vector3 bottomA = new Vector3(topA.x, bottomY, topA.z);
                Vector3 bottomB = new Vector3(topB.x, bottomY, topB.z);

                buffer.AddQuad(
                    topA, topB, bottomB, bottomA,
                    outward,
                    WallTopShade, WallTopShade, WallBottomShade, WallBottomShade);
            }
        }

        /// <summary>
        /// 가옥 상자를 추가합니다. 방어 목표를 실루엣으로 알아볼 수 있게 하는 최소한의 표현입니다.
        /// </summary>
        private static void AddHouseBox(MeshBuffer buffer, Vector3 center, float half)
        {
            float inset = half * (1f - HouseInset);
            float topY = center.y + HouseHeight;

            Vector3 min = new Vector3(center.x - inset, center.y, center.z - inset);
            Vector3 max = new Vector3(center.x + inset, topY, center.z + inset);

            // 윗면
            buffer.AddQuad(
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, max.y, max.z),
                new Vector3(max.x, max.y, min.z),
                Vector3.up, TopShade);

            // 옆면 4개. 가옥도 아래로 갈수록 어두워야 땅에 붙어 보입니다.
            buffer.AddQuad(
                new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, min.y, min.z), new Vector3(min.x, min.y, min.z), Vector3.back,
                WallTopShade, WallTopShade, WallBottomShade, WallBottomShade);

            buffer.AddQuad(
                new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z),
                new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), Vector3.forward,
                WallTopShade, WallTopShade, WallBottomShade, WallBottomShade);

            buffer.AddQuad(
                new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z), Vector3.left,
                WallTopShade, WallTopShade, WallBottomShade, WallBottomShade);

            buffer.AddQuad(
                new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z),
                new Vector3(max.x, min.y, max.z), new Vector3(max.x, min.y, min.z), Vector3.right,
                WallTopShade, WallTopShade, WallBottomShade, WallBottomShade);
        }

        // ====================================================================================================
        // 6. Private Methods - Object Creation
        // ====================================================================================================

        private GameObject CreateMeshObject(
            string objectName,
            MeshBuffer buffer,
            Color fallbackColor,
            Material authoredMaterial,
            bool addCollider = true)
        {
            return CreateMeshObject(
                objectName,
                buffer,
                TerrainMaterial(fallbackColor, authoredMaterial),
                addCollider);
        }

        private GameObject CreateMeshObject(
            string objectName,
            MeshBuffer buffer,
            Material material,
            bool addCollider)
        {
            if (buffer.IsEmpty)
            {
                return null;
            }

            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);

            var mesh = buffer.ToMesh(objectName);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            if (addCollider)
            {
                // 클릭 레이캐스트와 화살 충돌이 모두 이 콜라이더를 씁니다.
                go.AddComponent<MeshCollider>().sharedMesh = mesh;

                // 지형 레이어로 표시해 두어야 클릭 레이캐스트가 유닛을 건너뛰고 지형만 잡습니다.
                GameLayers.ApplyRecursively(go, GameLayers.Terrain);
            }

            return go;
        }

        /// <summary>
        /// 지형 재질을 고릅니다. 연결된 것이 있으면 그것을, 없으면 임시 재질을 씁니다.
        /// </summary>
        private Material TerrainMaterial(Color fallbackColor, Material authoredMaterial)
        {
            return _useAuthoredMaterials && authoredMaterial != null
                ? authoredMaterial
                : PrototypeVisuals.CreateTerrainMaterial(fallbackColor);
        }

        /// <summary>
        /// 지형지물 재질을 만듭니다. 지형과 같은 색을 쓰되 외곽선만 가늘게 줄입니다.
        ///
        /// 지형 재질을 그대로 공유하지 않고 복제하는 이유는 두께 하나 때문입니다.
        /// 공유하면 두께를 바꾸는 순간 지형의 외곽선까지 같이 얇아집니다.
        /// 지형은 굵어야 경계가 서고, 지형지물은 가늘어야 형태가 보입니다.
        /// </summary>
        private Material PropMaterial(Color fallbackColor, Material authoredMaterial)
        {
            var source = TerrainMaterial(fallbackColor, authoredMaterial);

            var material = new Material(source)
            {
                name = $"{source.name}_Prop",
                hideFlags = HideFlags.DontSave,
            };

            if (material.HasProperty("_OutlineWidth"))
            {
                material.SetFloat("_OutlineWidth", PropOutlineWidth);
            }

            return material;
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
