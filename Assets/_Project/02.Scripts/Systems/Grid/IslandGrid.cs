using System.Collections.Generic;
using SRPG.Common;
using UnityEngine;

namespace SRPG.Systems.Grid
{
    /// <summary>
    /// 섬 전체의 타일 격자입니다.
    /// 격자 좌표와 월드 좌표의 변환, 이웃 조회, 주요 지점 목록을 제공합니다.
    /// </summary>
    public sealed class IslandGrid
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>격자 전체의 타일입니다. 행 우선(<c>y * Width + x</c>)으로 담습니다.</summary>
        private readonly Tile[] _tiles;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>격자의 가로 칸 수입니다.</summary>
        public int Width { get; }

        /// <summary>격자의 세로 칸 수입니다.</summary>
        public int Depth { get; }

        /// <summary>타일 한 칸의 월드 크기입니다.</summary>
        public float CellSize { get; }

        /// <summary>고도 한 단계의 월드 높이입니다.</summary>
        public float HeightStep { get; }

        /// <summary>격자 (0,0) 타일의 좌하단 모서리에 해당하는 월드 좌표입니다.</summary>
        public Vector3 Origin { get; }

        /// <summary>
        /// 격자 전체의 중심 월드 좌표입니다. 높이는 0입니다.
        ///
        /// 섬 중심을 세 곳이 각자 같은 식으로 계산하고 있었습니다 —
        /// 분대 초기 배치, 카메라 프레이밍, 상륙정 등장 지점.
        /// 원점을 잡는 규칙이 바뀌면 그중 한 곳만 남습니다.
        /// </summary>
        public Vector3 WorldCenter => new Vector3(
            Origin.x + Width * CellSize * 0.5f,
            0f,
            Origin.z + Depth * CellSize * 0.5f);

        /// <summary>이 섬을 만들 때 사용한 시드입니다. 동일 시드는 동일 섬을 재현합니다.</summary>
        public int Seed { get; set; }

        /// <summary>
        /// 지면 높이를 실제 지형에서 읽어 오는 함수입니다. 인자는 월드 x, z 입니다.
        ///
        /// <b>왜 필요한가</b>
        ///
        /// 격자는 원래 고도 단계로 높이를 계산했습니다 — 타일이 지형을 <b>만들던</b> 시절의 방식입니다.
        /// 이제 지형은 유니티 터레인이고, 타일은 그것을 <b>읽습니다</b>.
        /// 이 함수를 연결하지 않으면 타일이 계단으로 계산한 값을 쓰고,
        /// 유닛이 보이는 지면과 다른 높이에 서게 됩니다.
        ///
        /// 비어 있으면 예전처럼 고도 단계로 계산합니다.
        /// </summary>
        public System.Func<float, float, float> SurfaceSampler { get; set; }

        /// <summary>통행 가능한 모든 타일입니다.</summary>
        public List<Tile> WalkableTiles { get; } = new List<Tile>();

        /// <summary>
        /// 물과 맞닿은 통행 가능 타일입니다. 곧 해안선입니다.
        ///
        /// 침공은 언제나 바다에서 옵니다. 그래서 "어느 쪽이 바깥인가"가 곧 "어느 쪽을 봐야 하는가"입니다.
        /// 매번 전체 타일을 훑지 않도록 파생 정보를 만들 때 함께 모아 둡니다.
        /// </summary>
        public List<Tile> CoastalTiles { get; } = new List<Tile>();

        /// <summary>
        /// 플레이어 부대가 전장에 들어서는 구역입니다.
        ///
        /// 야전은 두 부대가 마주 보며 들어섭니다. 상륙 구역과 달리 <b>어느 쪽에서 오는지를
        /// 처음부터 서로 압니다</b> — 문제는 그 다음입니다. 어느 날개를 두껍게 할지,
        /// 고지를 먼저 잡을지, 언제 붙을지.
        /// </summary>
        public List<Tile> PlayerDeployment { get; } = new List<Tile>();

        /// <summary>적 부대가 전장에 들어서는 구역입니다. 플레이어 구역의 반대편입니다.</summary>
        public List<Tile> EnemyDeployment { get; } = new List<Tile>();

        // ====================================================================================================
        // 3. Constructor
        // ====================================================================================================

        public IslandGrid(int width, int depth, float cellSize, float heightStep)
        {
            Width = Mathf.Max(1, width);
            Depth = Mathf.Max(1, depth);
            CellSize = Mathf.Max(0.01f, cellSize);
            HeightStep = Mathf.Max(0.01f, heightStep);

            // 섬의 중심이 월드 원점에 오도록 원점을 왼쪽 아래로 밀어 둡니다.
            Origin = new Vector3(-Width * CellSize * 0.5f, 0f, -Depth * CellSize * 0.5f);

            _tiles = new Tile[Width * Depth];
            for (int y = 0; y < Depth; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    var coord = new GridCoord(x, y);
                    _tiles[Index(x, y)] = new Tile
                    {
                        Coord = coord,
                        Type = TileType.Water,
                        Height = 0,
                        WorldCenter = ComputeWorldCenter(coord, 0),
                    };
                }
            }
        }

        // ====================================================================================================
        // 4. Public Methods - Access
        // ====================================================================================================

        /// <summary>지정 진영의 전개 구역을 반환합니다.</summary>
        /// <param name="team">전개 구역을 알고 싶은 진영입니다.</param>
        /// <returns>그 진영이 전장에 들어서는 타일 목록입니다. 생성기가 채우기 전이면 비어 있습니다.</returns>
        public List<Tile> GetDeployment(Team team)
        {
            return team == Team.Player ? PlayerDeployment : EnemyDeployment;
        }

        /// <summary>좌표가 격자 범위 안에 있는지 확인합니다.</summary>
        /// <param name="coord">검사할 격자 좌표입니다.</param>
        /// <returns>범위 안이면 true입니다.</returns>
        public bool IsInside(GridCoord coord)
        {
            return coord.X >= 0 && coord.X < Width && coord.Y >= 0 && coord.Y < Depth;
        }

        /// <summary>좌표의 타일을 반환합니다.</summary>
        /// <param name="coord">조회할 격자 좌표입니다.</param>
        /// <returns>해당 타일입니다. 범위를 벗어나면 null입니다.</returns>
        public Tile GetTile(GridCoord coord)
        {
            return IsInside(coord) ? _tiles[Index(coord.X, coord.Y)] : null;
        }

        /// <summary>좌표의 타일을 안전하게 조회합니다.</summary>
        /// <param name="coord">조회할 격자 좌표입니다.</param>
        /// <param name="tile">찾은 타일입니다. 실패하면 null입니다.</param>
        /// <returns>범위 안의 타일을 찾았으면 true입니다.</returns>
        public bool TryGetTile(GridCoord coord, out Tile tile)
        {
            tile = GetTile(coord);
            return tile != null;
        }

        /// <summary>격자 전체를 순회합니다.</summary>
        public IReadOnlyList<Tile> AllTiles => _tiles;

        // ====================================================================================================
        // 5. Public Methods - Conversion
        // ====================================================================================================

        /// <summary>
        /// 격자 좌표를 타일 표면 중심의 월드 좌표로 변환합니다.
        /// </summary>
        /// <param name="coord">변환할 격자 좌표입니다.</param>
        /// <returns>타일 표면 중심의 월드 좌표입니다. 범위 밖이면 고도 0으로 계산한 값입니다.</returns>
        public Vector3 CoordToWorld(GridCoord coord)
        {
            var tile = GetTile(coord);
            return tile != null ? tile.WorldCenter : ComputeWorldCenter(coord, 0);
        }

        /// <summary>
        /// 월드 좌표를 격자 좌표로 변환합니다. 범위를 벗어난 값도 그대로 반환하므로 <see cref="IsInside"/>로 확인해야 합니다.
        /// </summary>
        /// <param name="world">변환할 월드 좌표입니다. 높이는 무시합니다.</param>
        /// <returns>격자 좌표입니다. 범위를 벗어날 수 있습니다.</returns>
        public GridCoord WorldToCoord(Vector3 world)
        {
            int x = Mathf.FloorToInt((world.x - Origin.x) / CellSize);
            int y = Mathf.FloorToInt((world.z - Origin.z) / CellSize);
            return new GridCoord(x, y);
        }

        /// <summary>
        /// 월드 좌표에서 가장 가까운 통행 가능 타일을 찾습니다. 없으면 null입니다.
        /// 클릭 지점이 물이나 절벽일 때 가장 그럴듯한 목적지로 보정하는 용도입니다.
        /// </summary>
        /// <param name="world">기준이 되는 월드 좌표입니다.</param>
        /// <param name="maxDistance">이 거리 안에서만 찾습니다. 기본값은 제한 없음입니다.</param>
        /// <returns>가장 가까운 통행 가능 타일입니다. 범위 안에 없으면 null입니다.</returns>
        public Tile FindNearestWalkable(Vector3 world, float maxDistance = float.MaxValue)
        {
            Tile best = null;
            float bestSqr = maxDistance * maxDistance;

            for (int i = 0; i < WalkableTiles.Count; i++)
            {
                var tile = WalkableTiles[i];
                float sqr = (tile.WorldCenter - world).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = tile;
                }
            }

            return best;
        }

        /// <summary>
        /// 월드 좌표의 지면 높이를 반환합니다. 격자 밖이거나 통행 불가면 해수면(0)으로 봅니다.
        /// 유닛과 분대 앵커를 지면에 붙이는 데 사용합니다.
        /// </summary>
        /// <param name="world">높이를 알고 싶은 월드 좌표입니다. Y 성분은 무시합니다.</param>
        /// <returns>
        /// 그 자리의 지면 높이입니다.
        /// <see cref="SurfaceSampler"/>가 연결되어 있으면 실제 지형에서 읽고, 없으면 타일 고도로 계산합니다.
        /// </returns>
        public float SampleGroundHeight(Vector3 world)
        {
            // 지형이 연결되어 있으면 그 자리의 실제 높이를 씁니다.
            //
            // 타일 중심 높이를 그대로 쓰면 유닛이 칸 경계에서 툭툭 튑니다.
            // 터레인은 연속면이므로 발 높이도 연속이어야 합니다.
            if (SurfaceSampler != null)
            {
                return SurfaceSampler(world.x, world.z);
            }

            var tile = GetTile(WorldToCoord(world));

            return tile != null && tile.IsWalkable ? tile.WorldCenter.y : 0f;
        }

        // ====================================================================================================
        // 6. Public Methods - Neighbors
        // ====================================================================================================

        /// <summary>
        /// 4방향 이웃 타일을 <paramref name="buffer"/>에 채웁니다. 반환값은 채워진 개수입니다.
        /// 매 프레임 호출되는 경로 탐색에서 할당을 피하기 위해 버퍼를 받습니다.
        /// </summary>
        /// <param name="coord">기준 격자 좌표입니다.</param>
        /// <param name="buffer">이웃 타일이 채워집니다. 길이가 4 이상이어야 합니다.</param>
        /// <returns>실제로 채워진 이웃 수입니다. 격자 가장자리에서는 4보다 작습니다.</returns>
        public int GetNeighbors4(GridCoord coord, Tile[] buffer)
        {
            int count = 0;
            for (int i = 0; i < GridCoord.Neighbors4.Length; i++)
            {
                var tile = GetTile(coord + GridCoord.Neighbors4[i]);
                if (tile != null)
                {
                    buffer[count++] = tile;
                }
            }

            return count;
        }

        // ====================================================================================================
        // 7. Internal Methods
        // ====================================================================================================

        /// <summary>
        /// 타일의 지형이 확정된 뒤 파생 정보(월드 좌표, 해안 여부, 통행 가능 이웃 수)를 다시 계산합니다.
        /// 생성기가 지형을 모두 배치한 다음 한 번 호출합니다.
        /// </summary>
        public void RebuildDerivedData()
        {
            WalkableTiles.Clear();
            CoastalTiles.Clear();

            var buffer = new Tile[4];

            for (int i = 0; i < _tiles.Length; i++)
            {
                var tile = _tiles[i];
                tile.WorldCenter = ComputeWorldCenter(tile.Coord, tile.Height);

                // 지형이 연결되어 있으면 높이는 거기서 읽습니다.
                if (SurfaceSampler != null)
                {
                    var center = tile.WorldCenter;
                    center.y = SurfaceSampler(center.x, center.z);
                    tile.WorldCenter = center;
                }
                tile.IsWalkable = tile.Type != TileType.Water && tile.Type != TileType.Cliff;
            }

            for (int i = 0; i < _tiles.Length; i++)
            {
                var tile = _tiles[i];

                int neighborCount = GetNeighbors4(tile.Coord, buffer);
                int walkableNeighbors = 0;
                bool touchesWater = false;

                for (int n = 0; n < neighborCount; n++)
                {
                    var neighbor = buffer[n];
                    if (neighbor.IsWater)
                    {
                        touchesWater = true;
                    }

                    // 단차가 크면 절벽이라 이웃으로 세지 않습니다.
                    // 길찾기·생성기와 <b>같은 규칙</b>을 봅니다. 어긋나면 초크포인트 점수가
                    // 실제 통행과 다른 지형을 가리키게 됩니다.
                    if (TraversalRules.CanStep(tile, neighbor))
                    {
                        walkableNeighbors++;
                    }
                }

                tile.WalkableNeighborCount = walkableNeighbors;
                tile.IsCoastal = tile.IsWalkable && touchesWater;

                if (tile.IsWalkable)
                {
                    WalkableTiles.Add(tile);
                }

                if (tile.IsCoastal)
                {
                    CoastalTiles.Add(tile);
                }
            }
        }

        // ====================================================================================================
        // 8. Private Methods
        // ====================================================================================================

        private int Index(int x, int y) => y * Width + x;

        private Vector3 ComputeWorldCenter(GridCoord coord, int height)
        {
            return new Vector3(
                Origin.x + (coord.X + 0.5f) * CellSize,
                height * HeightStep,
                Origin.z + (coord.Y + 0.5f) * CellSize);
        }
    }
}
