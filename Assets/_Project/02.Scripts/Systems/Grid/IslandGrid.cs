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

        /// <summary>이 섬을 만들 때 사용한 시드입니다. 동일 시드는 동일 섬을 재현합니다.</summary>
        public int Seed { get; internal set; }

        /// <summary>
        /// 타일보다 촘촘한 연속 지형 높이입니다. 아직 조각되지 않았으면 null입니다.
        ///
        /// 타일의 <see cref="Tile.Height"/>가 <b>게임 규칙</b>이라면 이쪽은 <b>보이는 땅</b>입니다.
        /// 통행 판정과 길찾기는 여전히 타일을 보고, 메시와 발 높이만 이쪽을 봅니다.
        /// 둘을 섞으면 "보이는 것과 갈 수 있는 곳이 다른" 상태가 됩니다.
        /// </summary>
        public Landform.HeightField Height { get; internal set; }

        /// <summary>통행 가능한 모든 타일입니다.</summary>
        public List<Tile> WalkableTiles { get; } = new List<Tile>();

        /// <summary>가옥 타일입니다. 방어 목표입니다.</summary>
        public List<Tile> HouseTiles { get; } = new List<Tile>();

        /// <summary>
        /// 물과 맞닿은 통행 가능 타일입니다. 곧 해안선입니다.
        ///
        /// 침공은 언제나 바다에서 옵니다. 그래서 "어느 쪽이 바깥인가"가 곧 "어느 쪽을 봐야 하는가"입니다.
        /// 매번 전체 타일을 훑지 않도록 파생 정보를 만들 때 함께 모아 둡니다.
        /// </summary>
        public List<Tile> CoastalTiles { get; } = new List<Tile>();

        /// <summary>
        /// 상륙 구역 목록입니다. 각 항목은 하나의 해안 구역을 이루는 해변 타일들입니다.
        /// 적 상륙정은 구역 단위로 접근합니다.
        /// </summary>
        public List<List<Tile>> LandingZones { get; } = new List<List<Tile>>();

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

        /// <summary>좌표가 격자 범위 안에 있는지 확인합니다.</summary>
        public bool IsInside(GridCoord coord)
        {
            return coord.X >= 0 && coord.X < Width && coord.Y >= 0 && coord.Y < Depth;
        }

        /// <summary>좌표의 타일을 반환합니다. 범위를 벗어나면 null입니다.</summary>
        public Tile GetTile(GridCoord coord)
        {
            return IsInside(coord) ? _tiles[Index(coord.X, coord.Y)] : null;
        }

        /// <summary>좌표의 타일을 안전하게 조회합니다.</summary>
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
        public Vector3 CoordToWorld(GridCoord coord)
        {
            var tile = GetTile(coord);
            return tile != null ? tile.WorldCenter : ComputeWorldCenter(coord, 0);
        }

        /// <summary>
        /// 월드 좌표를 격자 좌표로 변환합니다. 범위를 벗어난 값도 그대로 반환하므로 <see cref="IsInside"/>로 확인해야 합니다.
        /// </summary>
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
        /// 가장 가까운 해안 타일을 찾습니다. 없으면 null입니다.
        ///
        /// 방어 부대가 어느 쪽을 보고 설지 정하는 근거입니다.
        /// 해안선 목록만 훑으므로 섬 전체를 훑는 것보다 훨씬 쌉니다.
        /// </summary>
        public Tile FindNearestCoastal(Vector3 world)
        {
            Tile best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < CoastalTiles.Count; i++)
            {
                var tile = CoastalTiles[i];
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
        ///
        /// <b>하이트필드가 있으면 그쪽을 봅니다.</b>
        /// 지형 메시가 타일마다 평면이 아니라 굴곡을 가지므로, 발 높이도 같은 곡면에서 읽어야
        /// 유닛이 땅에 박히거나 떠 있지 않습니다. 메시와 발 높이는 반드시 같은 출처여야 합니다.
        /// </summary>
        public float SampleGroundHeight(Vector3 world)
        {
            var tile = GetTile(WorldToCoord(world));

            if (tile == null || !tile.IsWalkable)
            {
                return 0f;
            }

            // 표본의 단계를 씁니다. 타일의 단계가 아닙니다.
            //
            // BoundaryWarp 가 단 경계를 타일 격자에서 떼어냈으므로, 경계 근처에서는
            // 보이는 땅의 높이가 타일의 고도 단계와 다릅니다.
            // 타일 쪽을 쓰면 그 자리에서 유닛이 땅에 박히거나 공중에 뜹니다.
            return Height == null
                ? tile.WorldCenter.y
                : Height.SampleSurface(world.x, world.z);
        }

        /// <summary>
        /// 월드 좌표에서의 지표면 법선입니다. 하이트필드가 없으면 수직입니다.
        /// 지형지물을 비탈에 맞춰 세우는 데 씁니다.
        /// </summary>
        public Vector3 SampleGroundNormal(Vector3 world)
        {
            return Height == null ? Vector3.up : Height.SampleNormal(world.x, world.z);
        }

        // ====================================================================================================
        // 6. Public Methods - Neighbors
        // ====================================================================================================

        /// <summary>
        /// 4방향 이웃 타일을 <paramref name="buffer"/>에 채웁니다. 반환값은 채워진 개수입니다.
        /// 매 프레임 호출되는 경로 탐색에서 할당을 피하기 위해 버퍼를 받습니다.
        /// </summary>
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
        internal void RebuildDerivedData()
        {
            WalkableTiles.Clear();
            HouseTiles.Clear();
            CoastalTiles.Clear();

            var buffer = new Tile[4];

            for (int i = 0; i < _tiles.Length; i++)
            {
                var tile = _tiles[i];
                tile.WorldCenter = ComputeWorldCenter(tile.Coord, tile.Height);
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

                    // 고도 차가 1을 넘으면 절벽으로 간주해 통행 불가로 봅니다.
                    if (neighbor.IsWalkable && tile.IsWalkable && Mathf.Abs(neighbor.Height - tile.Height) <= 1)
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

                if (tile.Type == TileType.House)
                {
                    HouseTiles.Add(tile);
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
