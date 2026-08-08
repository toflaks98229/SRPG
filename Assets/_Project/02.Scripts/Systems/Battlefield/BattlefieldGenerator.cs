using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Systems.Deployment;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Systems.Battlefield
{
    /// <summary>
    /// 전장을 만듭니다. <b>유니티 터레인 위의 해안 평야</b>입니다.
    ///
    /// <b>사각 발판을 쓰지 않습니다</b>
    ///
    /// 앞서 타일마다 사각 발판을 굽는 방식으로 지형을 만들었습니다. 그러면 아무리 잘게
    /// 나눠도 눈이 격자를 찾아냅니다 — 침식 시뮬레이션과 마칭 스퀘어까지 동원하고도 그랬습니다.
    ///
    /// 유니티 터레인은 애초에 연속면입니다. 이음매가 없고, 렌더링·충돌·높이 질의를
    /// 엔진이 맡습니다. 우리가 만드는 것은 숫자 격자 하나이고, 그 위에서
    /// <b>타일은 게임 규칙으로만</b> 남습니다.
    ///
    /// <b>타일은 지형을 읽습니다</b>
    ///
    ///   높이   — 하이트맵을 그 자리에서 샘플링합니다
    ///   통행   — 기울기가 오를 만한가, 물에 잠기지 않았는가, 장애물이 없는가
    ///
    /// 그래서 <b>보이는 것과 갈 수 있는 곳이 같은 값에서 나옵니다.</b>
    /// 지형을 바꾸면 통행이 따라 바뀌고, 둘이 어긋날 자리가 없습니다.
    ///
    /// <b>확장은 지형 종류 축으로만</b>
    ///
    /// 월드맵이 붙으면 좌표가 <see cref="TerrainKind"/>를 고르고, 그것이
    /// <see cref="BattlefieldProfile"/>을 고르고, 프로필이 여기 들어옵니다.
    /// 생성기는 좌표를 모르고, 월드맵은 생성 방식을 모릅니다.
    /// </summary>
    public static class BattlefieldGenerator
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>타일 한 칸의 월드 크기입니다.</summary>
        public const float CellSize = 2f;

        /// <summary>고도 눈금이 아무리 잘아도 이보다 잘지는 않습니다.</summary>
        private const float MinHeightStep = 0.4f;

        /// <summary>장애물 배치를 시도하는 횟수의 상한입니다. 무한 루프를 막습니다.</summary>
        private const int MaxObstacleAttempts = 400;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 고도 한 단의 월드 높이입니다. <b>등반 한계에서 유도됩니다.</b>
        ///
        /// <b>왜 상수가 아닌가</b>
        ///
        /// 통행 규칙(<see cref="TraversalRules.MaxHeightDelta"/>)은 이웃과의 고도 단차가
        /// 한 단을 넘으면 못 간다고 봅니다. 그런데 이 지형에서 오를 수 있는지를 정하는 것은 경사입니다.
        /// 눈금을 아무 값으로나 두면 두 규칙이 어긋나
        /// <b>보기에는 완만한 비탈인데 길찾기가 막힌 땅</b>이 생깁니다.
        /// 실제로 구릉 전장이 그렇게 갈라졌습니다.
        ///
        /// 한 단을 <b>한 칸을 건너며 오를 수 있는 최대 높이</b>로 두면 그 어긋남이 사라집니다.
        /// 경사가 한계 안이면 이웃과의 높이 차가 한 단 안이고, 따라서 단차도 반드시 한 단 이하입니다.
        /// 두 규칙이 같은 것을 말하게 됩니다.
        /// </summary>
        public static float ResolveHeightStep(float climbLimitDegrees)
        {
            float limit = Mathf.Clamp(climbLimitDegrees, 5f, 80f);

            return Mathf.Max(MinHeightStep, CellSize * Mathf.Tan(limit * Mathf.Deg2Rad));
        }

        /// <summary>
        /// 전장을 만듭니다.
        /// </summary>
        /// <param name="spec">어디서 싸우는지에 대한 지시입니다.</param>
        /// <param name="profile">그 지형의 성격입니다. null이면 코드 기본값을 씁니다.</param>
        public static Battlefield Generate(BattlefieldSpec spec, BattlefieldProfile profile = null)
        {
            spec = spec.WithDefaults();

            bool ownsProfile = profile == null;

            if (ownsProfile)
            {
                profile = BattlefieldProfile.CreateDefault(spec.Terrain);
            }

            try
            {
                return Build(spec, profile);
            }
            finally
            {
                if (ownsProfile)
                {
                    Object.DestroyImmediate(profile);
                }
            }
        }

        // ====================================================================================================
        // 3. Private Methods - Build
        // ====================================================================================================

        private static Battlefield Build(BattlefieldSpec spec, BattlefieldProfile profile)
        {
            int seed = spec.Seed != 0 ? spec.Seed : System.Environment.TickCount;

            float heightStep = ResolveHeightStep(profile.ClimbLimitDegrees);

            var heightmap = BattlefieldHeightmap.Create(spec, profile, CellSize);
            var grid = new IslandGrid(spec.Width, spec.Depth, CellSize, heightStep) { Seed = seed };

            // 터레인은 격자 원점에서 시작해 같은 넓이를 덮습니다.
            // 원점을 어긋나게 두면 유닛이 보이는 지면과 다른 높이에 섭니다.
            var origin = new Vector3(grid.Origin.x, -heightmap.SeaLevel, grid.Origin.z);

            // 타일 높이를 지형에서 읽어 오게 합니다.
            // 이 연결이 없으면 타일이 고도 단계로 계산한 값을 쓰고, 지형과 어긋납니다.
            grid.SurfaceSampler = (x, z) => heightmap.SampleHeight(x, z, origin);

            ReadTerrain(grid, heightmap, profile, origin);
            grid.RebuildDerivedData();

            SealStrandedGround(grid);
            grid.RebuildDerivedData();

            var random = new System.Random(seed);

            PlaceObstacles(grid, profile, random);
            grid.RebuildDerivedData();

            // 야전의 전개 구역입니다. 축을 시드로 뽑으므로 같은 전장이면 같은 대치 구도가 나옵니다.
            DeploymentZones.Build(grid, seed);

            return new Battlefield(grid, heightmap, origin, profile.Kind, profile.ClimbLimitDegrees);
        }

        /// <summary>
        /// 지형에서 타일의 높이와 통행 여부를 읽어 냅니다.
        ///
        /// <b>기울기가 통행을 정합니다.</b>
        /// 터레인은 연속면이라 어디든 걸을 수 있어 보이므로, 오를 수 있는지는
        /// 눈에 보이는 것과 같은 기준 — 경사 — 이 답해야 합니다.
        /// </summary>
        private static void ReadTerrain(
            IslandGrid grid, BattlefieldHeightmap heightmap, BattlefieldProfile profile, Vector3 origin)
        {
            float seaLevel = origin.y + heightmap.SeaLevel;

            for (int y = 0; y < grid.Depth; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var tile = grid.GetTile(new GridCoord(x, y));

                    var center = grid.CoordToWorld(new GridCoord(x, y));

                    float surface = heightmap.SampleHeight(center.x, center.z, origin);
                    float slope = heightmap.SampleSlopeDegrees(center.x, center.z, origin);

                    // 해수면 아래는 물입니다.
                    if (surface < seaLevel)
                    {
                        tile.Type = TileType.Water;
                        tile.Height = 0;
                        continue;
                    }

                    // 한 단이 곧 등반 한계입니다. 경사가 한계 안이면 이웃과의 단차도 1을 넘지 않습니다.
                    tile.Height = Mathf.Max(0, Mathf.RoundToInt((surface - seaLevel) / grid.HeightStep));

                    // 너무 가파르면 오를 수 없습니다. 드러난 암반으로 봅니다.
                    tile.Type = slope > profile.ClimbLimitDegrees
                        ? TileType.Cliff
                        : (IsNearWater(heightmap, origin, center, seaLevel) ? TileType.Beach : TileType.Ground);
                }
            }
        }

        /// <summary>물가인지 봅니다. 상륙 지점이 될 자리입니다.</summary>
        private static bool IsNearWater(
            BattlefieldHeightmap heightmap, Vector3 origin, Vector3 center, float seaLevel)
        {
            for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
            {
                float nx = center.x + GridCoord.Neighbors8[n].X * CellSize;
                float nz = center.z + GridCoord.Neighbors8[n].Y * CellSize;

                if (heightmap.SampleHeight(nx, nz, origin) < seaLevel)
                {
                    return true;
                }
            }

            return false;
        }

        // ====================================================================================================
        // 4. Private Methods - Content
        // ====================================================================================================

        /// <summary>
        /// 장애물을 덩이째 놓습니다. 바위와 수풀입니다.
        ///
        /// <b>왜 덩이인가</b>
        /// 같은 밀도라도 한 칸씩 흩으면 그저 성가시기만 합니다.
        /// 뭉쳐 놓아야 우회로가 생기고, 우회할지 돌아갈지가 판단이 됩니다.
        ///
        /// 연결을 끊는 배치는 되돌립니다. 갈 수 없는 구역이 생기면
        /// 부대를 못 보내는 땅이 되고, 그건 지형이 아니라 버그입니다.
        ///
        /// <b>파생 정보를 매번 다시 만들지 않습니다</b>
        ///
        /// 예전에는 덩이를 놓을 때마다 <see cref="IslandGrid.RebuildDerivedData"/>를 부르고
        /// 연결성 검사도 임시 컬렉션을 새로 만들어 돌렸습니다. 시도가 최대 400번이므로
        /// 로딩 한 번에 격자 전체 순회가 수백 번, 임시 컬렉션이 수백 개 쏟아졌습니다.
        ///
        /// 놓고 되돌리는 동안 바뀌는 것은 <b>덩이에 속한 칸의 통행 여부뿐</b>입니다.
        /// 그 칸만 뒤집고, 통행 가능 칸 수는 직접 세어 두고, 검사기는 하나를 재사용합니다.
        /// 파생 정보는 전부 끝난 뒤 한 번만 다시 만듭니다.
        /// </summary>
        private static void PlaceObstacles(IslandGrid grid, BattlefieldProfile profile, System.Random random)
        {
            if (profile.ObstacleDensity <= 0f)
            {
                return;
            }

            var candidates = new List<Tile>();

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];

                // 해변은 상륙 지점이라 막지 않습니다. 배가 닿을 곳이 없어집니다.
                if (tile.Type == TileType.Ground)
                {
                    candidates.Add(tile);
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            int target = Mathf.RoundToInt(candidates.Count * profile.ObstacleDensity);
            int placed = 0;

            // 지금 통행 가능한 칸 수입니다. 덩이를 놓을 때마다 그만큼 줄여 나갑니다.
            int walkableCount = grid.WalkableTiles.Count;

            var connectivity = new GridConnectivity(grid);
            var clump = new List<Tile>();

            for (int attempt = 0; attempt < MaxObstacleAttempts && placed < target; attempt++)
            {
                var seed = candidates[random.Next(candidates.Count)];

                if (seed.Type != TileType.Ground)
                {
                    continue;
                }

                GatherClump(grid, seed, profile.ObstacleClumpSize, random, clump);

                Block(clump);

                int remaining = walkableCount - clump.Count;

                if (remaining > 0 && connectivity.CountReachable(FindWalkable(grid)) == remaining)
                {
                    walkableCount = remaining;
                    placed += clump.Count;
                }
                else
                {
                    Unblock(clump);
                }
            }

            // 파생 정보는 호출부가 다시 만듭니다. 이 파일의 다른 배치 단계와 같은 관례입니다.
        }

        /// <summary>덩이를 막습니다. 통행 여부까지 함께 뒤집어야 연결성 검사가 이 배치를 봅니다.</summary>
        private static void Block(List<Tile> clump)
        {
            for (int i = 0; i < clump.Count; i++)
            {
                clump[i].Type = TileType.Cliff;
                clump[i].IsWalkable = false;
            }
        }

        /// <summary>막았던 덩이를 되돌립니다.</summary>
        private static void Unblock(List<Tile> clump)
        {
            for (int i = 0; i < clump.Count; i++)
            {
                clump[i].Type = TileType.Ground;
                clump[i].IsWalkable = true;
            }
        }

        /// <summary>
        /// 연결성 검사를 시작할 칸을 하나 찾습니다.
        ///
        /// 배치 도중에는 <see cref="IslandGrid.WalkableTiles"/>가 낡았지만,
        /// 이 루프에서 칸이 <b>새로 통행 가능해지는 일은 없으므로</b> 그 목록은 언제나 상위 집합입니다.
        /// 앞에서부터 아직 살아 있는 칸을 찾으면 됩니다.
        /// </summary>
        private static Tile FindWalkable(IslandGrid grid)
        {
            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                if (grid.WalkableTiles[i].IsWalkable)
                {
                    return grid.WalkableTiles[i];
                }
            }

            return null;
        }

        /// <summary>씨앗에서 이웃으로 번지며 덩이를 모읍니다.</summary>
        private static void GatherClump(
            IslandGrid grid, Tile seed, int size, System.Random random, List<Tile> result)
        {
            result.Clear();
            result.Add(seed);

            for (int step = 1; step < size; step++)
            {
                var from = result[random.Next(result.Count)];
                var offset = GridCoord.Neighbors4[random.Next(GridCoord.Neighbors4.Length)];

                var neighbor = grid.GetTile(from.Coord + offset);

                if (neighbor != null && neighbor.Type == TileType.Ground && !result.Contains(neighbor))
                {
                    result.Add(neighbor);
                }
            }
        }

        /// <summary>
        /// 어디서도 닿을 수 없는 땅을 바위로 덮습니다.
        ///
        /// <b>왜 필요한가</b>
        ///
        /// 지형은 연속이지만 통행은 이산입니다. 그래서 아무리 규칙을 맞춰도
        /// 비탈에 둘러싸인 작은 턱 같은 것이 남습니다. 그런 땅은
        /// <b>걸을 수 있다고 표시되어 있는데 갈 수는 없는 곳</b>이라,
        /// 부대에게 명령했을 때 왜 안 가는지 알 수 없게 만듭니다.
        ///
        /// 가장 큰 덩어리만 남기고 나머지를 바위로 바꿉니다.
        /// 갈 수 없다면 땅이 아니라 지형지물로 보이는 편이 정직합니다.
        /// </summary>
        private static void SealStrandedGround(IslandGrid grid)
        {
            if (grid.WalkableTiles.Count == 0)
            {
                return;
            }

            var unvisited = new HashSet<GridCoord>();

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                unvisited.Add(grid.WalkableTiles[i].Coord);
            }

            // 검사기 하나를 구역 수만큼 재사용합니다. 구역마다 컬렉션을 새로 만들지 않습니다.
            var connectivity = new GridConnectivity(grid);

            var region = new List<Tile>();
            var largest = new List<Tile>();

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];

                if (!unvisited.Contains(tile.Coord))
                {
                    continue;
                }

                connectivity.CollectRegion(tile, region);

                for (int r = 0; r < region.Count; r++)
                {
                    unvisited.Remove(region[r].Coord);
                }

                if (region.Count > largest.Count)
                {
                    largest.Clear();
                    largest.AddRange(region);
                }
            }

            var keep = new HashSet<GridCoord>();

            for (int i = 0; i < largest.Count; i++)
            {
                keep.Add(largest[i].Coord);
            }

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];

                if (!keep.Contains(tile.Coord))
                {
                    tile.Type = TileType.Cliff;
                }
            }
        }

    }
}
