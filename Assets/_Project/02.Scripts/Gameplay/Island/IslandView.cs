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

            BuildTerrainLayer("Terrain_Beach", TileType.Beach, new Color(0.85f, 0.79f, 0.6f), _materials.Beach);
            BuildTerrainLayer("Terrain_Ground", TileType.Ground, new Color(0.44f, 0.62f, 0.36f), _materials.Ground);
            BuildTerrainLayer("Terrain_Cliff", TileType.Cliff, new Color(0.44f, 0.42f, 0.43f), _materials.Cliff);
            BuildTerrainLayer("Terrain_House", TileType.House, new Color(0.62f, 0.47f, 0.33f), _materials.House);
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
            var vertices = new List<Vector3>(1024);
            var triangles = new List<int>(2048);

            float half = _grid.CellSize * 0.5f;

            for (int i = 0; i < _grid.AllTiles.Count; i++)
            {
                var tile = _grid.AllTiles[i];
                if (tile.Type != type)
                {
                    continue;
                }

                Vector3 center = tile.WorldCenter;

                AddTopQuad(vertices, triangles, center, half);
                AddSideWalls(vertices, triangles, tile, center, half);

                if (type == TileType.House)
                {
                    AddHouseBox(vertices, triangles, center, half);
                }
            }

            if (vertices.Count == 0)
            {
                return;
            }

            CreateMeshObject(layerName, vertices, triangles, fallbackColor, authoredMaterial);
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

            var vertices = new List<Vector3>
            {
                new Vector3(minX, y, minZ),
                new Vector3(minX, y, maxZ),
                new Vector3(maxX, y, maxZ),
                new Vector3(maxX, y, minZ),
            };

            var triangles = new List<int> { 0, 1, 2, 0, 2, 3 };

            var water = CreateMeshObject(
                "Water",
                vertices,
                triangles,
                new Color(0.18f, 0.35f, 0.52f),
                _materials.Water,
                addCollider: false);

            water.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ====================================================================================================
        // 5. Private Methods - Geometry
        // ====================================================================================================

        /// <summary>
        /// 타일 상단 면을 추가합니다.
        /// </summary>
        private static void AddTopQuad(List<Vector3> vertices, List<int> triangles, Vector3 center, float half)
        {
            Vector3 a = new Vector3(center.x - half, center.y, center.z - half);
            Vector3 b = new Vector3(center.x - half, center.y, center.z + half);
            Vector3 c = new Vector3(center.x + half, center.y, center.z + half);
            Vector3 d = new Vector3(center.x + half, center.y, center.z - half);

            AddQuad(vertices, triangles, a, b, c, d, Vector3.up);
        }

        /// <summary>
        /// 이웃보다 높은 쪽에 측면 벽을 세웁니다. 바다와 맞닿은 쪽은 수면 아래까지 내려 스커트를 만듭니다.
        /// </summary>
        private void AddSideWalls(List<Vector3> vertices, List<int> triangles, Tile tile, Vector3 center, float half)
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

                AddQuad(vertices, triangles, topA, topB, bottomB, bottomA, outward);
            }
        }

        /// <summary>
        /// 가옥 상자를 추가합니다. 방어 목표를 실루엣으로 알아볼 수 있게 하는 최소한의 표현입니다.
        /// </summary>
        private static void AddHouseBox(List<Vector3> vertices, List<int> triangles, Vector3 center, float half)
        {
            float inset = half * (1f - HouseInset);
            float baseY = center.y;
            float topY = center.y + HouseHeight;

            Vector3 min = new Vector3(center.x - inset, baseY, center.z - inset);
            Vector3 max = new Vector3(center.x + inset, topY, center.z + inset);

            // 윗면
            AddQuad(vertices, triangles,
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, max.y, max.z),
                new Vector3(max.x, max.y, min.z),
                Vector3.up);

            // 옆면 4개
            AddQuad(vertices, triangles,
                new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, min.y, min.z), new Vector3(min.x, min.y, min.z), Vector3.back);

            AddQuad(vertices, triangles,
                new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z),
                new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z), Vector3.forward);

            AddQuad(vertices, triangles,
                new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z), Vector3.left);

            AddQuad(vertices, triangles,
                new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z),
                new Vector3(max.x, min.y, max.z), new Vector3(max.x, min.y, min.z), Vector3.right);
        }

        /// <summary>
        /// 사각형을 추가합니다.
        /// 방향을 일일이 계산해 감기 순서를 맞추는 대신, 만들어진 법선이 원하는 방향과 반대면 뒤집습니다.
        /// 벽 방향마다 감기 순서를 손으로 유도하다 생기는 실수를 원천 차단하기 위한 방식입니다.
        /// </summary>
        private static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 desiredNormal)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            bool flip = Vector3.Dot(normal, desiredNormal) < 0f;

            int baseIndex = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);

            if (flip)
            {
                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 1);
            }
            else
            {
                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 0);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 3);
            }
        }

        // ====================================================================================================
        // 6. Private Methods - Object Creation
        // ====================================================================================================

        private GameObject CreateMeshObject(
            string objectName,
            List<Vector3> vertices,
            List<int> triangles,
            Color fallbackColor,
            Material authoredMaterial,
            bool addCollider = true)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);

            var mesh = new Mesh
            {
                name = objectName,
                indexFormat = vertices.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var material = _useAuthoredMaterials && authoredMaterial != null
                ? authoredMaterial
                : PrototypeVisuals.CreateMaterial(fallbackColor);

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
