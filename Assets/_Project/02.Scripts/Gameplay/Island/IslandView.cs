using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Visual;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Gameplay.Island
{
    /// <summary>
    /// <see cref="IslandGrid"/>를 눈에 보이는 지형 메시로 변환합니다.
    ///
    /// 타일마다 GameObject를 만들지 않고 지형 종류별로 메시를 하나씩 합쳐 굽습니다.
    /// 30x30 격자면 타일이 900개인데, 개별 오브젝트로 두면 드로우콜과 트랜스폼 갱신이 낭비됩니다.
    /// 합친 결과는 지형 종류 수(4~5개)만큼의 드로우콜로 끝납니다.
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

        // ====================================================================================================
        // 1-1. Palette
        // ====================================================================================================

        /// <summary>통행 가능한 평지입니다.</summary>
        private static readonly Color WalkableGrassColor = new Color(0.44f, 0.62f, 0.36f);

        /// <summary>통행 가능한 해변입니다. 평지와 같은 계열로 두어 구분이 정보로 읽히지 않게 합니다.</summary>
        private static readonly Color WalkableSandColor = new Color(0.55f, 0.66f, 0.40f);

        /// <summary>통행 불가입니다. 통행 가능 계열과 확실히 갈라져야 합니다.</summary>
        private static readonly Color BlockedColor = new Color(0.42f, 0.40f, 0.43f);

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
            //   · 갈 수 있는 땅   — 해변과 평지를 같은 계열로 묶습니다
            //   · 갈 수 없는 땅   — 절벽
            //   · 목표            — 가옥
            //
            // 해변과 평지의 색을 나누면 정보가 하나 더 늘어나는데,
            // 그 구분은 적의 상륙 판정에만 쓰이지 플레이어의 이동 판단에는 쓰이지 않습니다.
            // 판단에 쓰이지 않는 구분은 화면에서는 잡음입니다.
            BuildTerrainLayer("Terrain_Beach", TileType.Beach, WalkableSandColor, _materials.Beach);
            BuildTerrainLayer("Terrain_Ground", TileType.Ground, WalkableGrassColor, _materials.Ground);
            BuildTerrainLayer("Terrain_Cliff", TileType.Cliff, BlockedColor, _materials.Cliff);
            BuildTerrainLayer("Terrain_House", TileType.House, ObjectiveColor, _materials.House);
            BuildWaterPlane();
        }

        // ====================================================================================================
        // 4. Private Methods - Layers
        // ====================================================================================================

        /// <summary>
        /// 특정 지형 종류의 타일을 하나의 메시로 합쳐 생성합니다.
        /// </summary>
        private void BuildTerrainLayer(string layerName, TileType type, Color fallbackColor, Material authoredMaterial)
        {
            var buffer = new MeshBuffer();

            float half = _grid.CellSize * 0.5f;

            for (int i = 0; i < _grid.AllTiles.Count; i++)
            {
                var tile = _grid.AllTiles[i];
                if (tile.Type != type)
                {
                    continue;
                }

                Vector3 center = tile.WorldCenter;

                AddTopQuad(buffer, center, half);
                AddSideWalls(buffer, tile, center, half);

                if (type == TileType.House)
                {
                    AddHouseBox(buffer, center, half);
                }
            }

            if (buffer.Count == 0)
            {
                return;
            }

            CreateMeshObject(layerName, buffer, fallbackColor, authoredMaterial);
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
        /// 메시를 쌓아 올리는 버퍼입니다.
        ///
        /// 정점 컬러를 함께 담습니다. 셰이더가 이 값의 R을 <b>접지 음영</b>으로 읽습니다.
        /// 텍스처 없이 경계를 세우는 것이 이 게임 룩의 핵심이라, 그 정보가 지오메트리와 같이 만들어져야 합니다.
        /// </summary>
        private sealed class MeshBuffer
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(1024);
            public readonly List<int> Triangles = new List<int>(2048);
            public readonly List<Color> Colors = new List<Color>(1024);

            public int Count => Vertices.Count;

            /// <summary>
            /// 사각형을 추가합니다. 네 꼭짓점의 음영을 각각 받습니다.
            ///
            /// 방향을 일일이 계산해 감기 순서를 맞추는 대신, 만들어진 법선이 원하는 방향과 반대면 뒤집습니다.
            /// 벽 방향마다 감기 순서를 손으로 유도하다 생기는 실수를 원천 차단하기 위한 방식입니다.
            /// </summary>
            public void AddQuad(
                Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                Vector3 desiredNormal,
                float shadeA, float shadeB, float shadeC, float shadeD)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a);
                bool flip = Vector3.Dot(normal, desiredNormal) < 0f;

                int baseIndex = Vertices.Count;

                Vertices.Add(a);
                Vertices.Add(b);
                Vertices.Add(c);
                Vertices.Add(d);

                Colors.Add(new Color(shadeA, 0f, 0f, 1f));
                Colors.Add(new Color(shadeB, 0f, 0f, 1f));
                Colors.Add(new Color(shadeC, 0f, 0f, 1f));
                Colors.Add(new Color(shadeD, 0f, 0f, 1f));

                if (flip)
                {
                    Triangles.Add(baseIndex + 0);
                    Triangles.Add(baseIndex + 3);
                    Triangles.Add(baseIndex + 2);
                    Triangles.Add(baseIndex + 0);
                    Triangles.Add(baseIndex + 2);
                    Triangles.Add(baseIndex + 1);
                }
                else
                {
                    Triangles.Add(baseIndex + 0);
                    Triangles.Add(baseIndex + 1);
                    Triangles.Add(baseIndex + 2);
                    Triangles.Add(baseIndex + 0);
                    Triangles.Add(baseIndex + 2);
                    Triangles.Add(baseIndex + 3);
                }
            }

            /// <summary>네 꼭짓점의 음영이 같은 사각형입니다.</summary>
            public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 desiredNormal, float shade)
            {
                AddQuad(a, b, c, d, desiredNormal, shade, shade, shade, shade);
            }
        }

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
        /// 지금까지는 벽과 윗면이 같은 색이라 계단 고도가 눈에 들어오지 않았습니다.
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
            float baseY = center.y;
            float topY = center.y + HouseHeight;

            Vector3 min = new Vector3(center.x - inset, baseY, center.z - inset);
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
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);

            var mesh = new Mesh
            {
                name = objectName,
                indexFormat = buffer.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };

            mesh.SetVertices(buffer.Vertices);
            mesh.SetTriangles(buffer.Triangles, 0);

            // 접지 음영입니다. 셰이더가 R 채널을 읽습니다.
            mesh.SetColors(buffer.Colors);

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var material = _useAuthoredMaterials && authoredMaterial != null
                ? authoredMaterial
                : PrototypeVisuals.CreateTerrainMaterial(fallbackColor);

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
