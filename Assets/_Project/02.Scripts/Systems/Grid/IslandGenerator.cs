using System;
using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using UnityEngine;

namespace SRPG.Systems.Grid
{
    /// <summary>
    /// 절차적으로 섬을 생성합니다.
    /// MonoBehaviour에 의존하지 않는 순수 알고리즘이므로 EditMode 테스트로 직접 검증할 수 있습니다.
    ///
    /// 생성 순서
    ///   1) 반경 + 노이즈로 섬 윤곽을 결정한다
    ///   2) 가장 큰 연결 덩어리만 남겨 고립된 섬 조각을 제거한다
    ///   3) 바다로부터의 거리로 고도를 계단식으로 부여한다
    ///   4) 바위(절벽)를 배치해 초크포인트를 만든다. 단, 섬을 단절시키면 되돌린다
    ///   5) 가옥을 서로 떨어뜨려 배치한다
    ///   6) 해안을 각도 기준으로 나눠 상륙 구역을 만든다
    /// </summary>
    public static class IslandGenerator
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>고도 한 단계가 차지하는 해안으로부터의 거리(칸)입니다. 클수록 완만한 섬이 됩니다.</summary>
        private const int HeightBandWidth = 2;

        /// <summary>바위 배치를 시도하는 횟수의 상한입니다. 무한 루프를 막습니다.</summary>
        private const int MaxRockAttempts = 40;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 설정에 따라 섬을 생성합니다.
        /// </summary>
        /// <param name="settings">생성 파라미터입니다. null이면 기본값을 사용합니다.</param>
        /// <param name="seedOverride">0이 아니면 설정의 시드 대신 이 값을 사용합니다.</param>
        public static IslandGrid Generate(IslandSettings settings, int seedOverride = 0)
        {
            if (settings == null)
            {
                settings = IslandSettings.CreateDefault();
            }

            int seed = seedOverride != 0 ? seedOverride : settings.Seed;
            if (seed == 0)
            {
                seed = Environment.TickCount;
            }

            var rng = new System.Random(seed);
            var grid = new IslandGrid(settings.Width, settings.Depth, settings.CellSize, settings.HeightStep)
            {
                Seed = seed,
            };

            int w = grid.Width;
            int d = grid.Depth;
            int cellCount = w * d;

            var isLand = new bool[cellCount];
            var isRock = new bool[cellCount];
            var distToWater = new int[cellCount];
            var height = new int[cellCount];

            BuildSilhouette(settings, rng, w, d, isLand);
            KeepLargestComponent(w, d, isLand);
            ComputeDistanceToWater(w, d, isLand, distToWater);
            AssignHeights(settings, w, d, isLand, distToWater, height);
            PlaceRocks(settings, rng, w, d, isLand, height, isRock);

            // 지형 확정
            for (int y = 0; y < d; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    var tile = grid.GetTile(new GridCoord(x, y));

                    if (!isLand[i])
                    {
                        tile.Type = TileType.Water;
                        tile.Height = 0;
                        continue;
                    }

                    tile.Height = height[i];

                    if (isRock[i])
                    {
                        tile.Type = TileType.Cliff;
                    }
                    else if (distToWater[i] == 1)
                    {
                        tile.Type = TileType.Beach;
                    }
                    else
                    {
                        tile.Type = TileType.Ground;
                    }
                }
            }

            grid.RebuildDerivedData();

            PlaceHouses(settings, rng, grid);
            grid.RebuildDerivedData();

            BuildLandingZones(settings, grid);

            return grid;
        }

        // ====================================================================================================
        // 3. Private Methods - Silhouette
        // ====================================================================================================

        /// <summary>
        /// 중심으로부터의 거리에 펄린 노이즈를 더해 섬의 윤곽을 만듭니다.
        /// 노이즈 세기가 0이면 완전한 원형이 됩니다.
        /// </summary>
        private static void BuildSilhouette(IslandSettings settings, System.Random rng, int w, int d, bool[] isLand)
        {
            float cx = (w - 1) * 0.5f;
            float cy = (d - 1) * 0.5f;
            float maxRadius = Mathf.Min(w, d) * settings.IslandRadius;

            // 시드마다 다른 노이즈 영역을 쓰도록 오프셋을 흩뜨립니다.
            float noiseOffsetX = (float)rng.NextDouble() * 1000f;
            float noiseOffsetY = (float)rng.NextDouble() * 1000f;

            for (int y = 0; y < d; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    float nx = (float)x / w * settings.NoiseScale + noiseOffsetX;
                    float ny = (float)y / d * settings.NoiseScale + noiseOffsetY;
                    float noise = (Mathf.PerlinNoise(nx, ny) - 0.5f) * 2f;

                    float threshold = maxRadius * (1f + noise * settings.NoiseStrength);
                    isLand[y * w + x] = dist < threshold;
                }
            }

            // 격자 테두리는 항상 바다로 둡니다. 섬이 맵 밖으로 잘리면 해안선이 성립하지 않습니다.
            for (int x = 0; x < w; x++)
            {
                isLand[x] = false;
                isLand[(d - 1) * w + x] = false;
            }

            for (int y = 0; y < d; y++)
            {
                isLand[y * w] = false;
                isLand[y * w + (w - 1)] = false;
            }
        }

        /// <summary>
        /// 가장 큰 연결 덩어리만 남기고 나머지 육지는 바다로 되돌립니다.
        /// 노이즈 때문에 떨어져 나온 작은 섬 조각은 갈 수 없는 땅이 되므로 제거합니다.
        /// </summary>
        private static void KeepLargestComponent(int w, int d, bool[] isLand)
        {
            var componentId = new int[w * d];
            for (int i = 0; i < componentId.Length; i++)
            {
                componentId[i] = -1;
            }

            int bestId = -1;
            int bestSize = 0;
            int nextId = 0;
            var queue = new Queue<int>();

            for (int start = 0; start < isLand.Length; start++)
            {
                if (!isLand[start] || componentId[start] != -1)
                {
                    continue;
                }

                int id = nextId++;
                int size = 0;

                queue.Clear();
                queue.Enqueue(start);
                componentId[start] = id;

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    size++;

                    int cx = current % w;
                    int cy = current / w;

                    for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                    {
                        int nx = cx + GridCoord.Neighbors4[n].X;
                        int ny = cy + GridCoord.Neighbors4[n].Y;

                        if (nx < 0 || nx >= w || ny < 0 || ny >= d)
                        {
                            continue;
                        }

                        int ni = ny * w + nx;
                        if (isLand[ni] && componentId[ni] == -1)
                        {
                            componentId[ni] = id;
                            queue.Enqueue(ni);
                        }
                    }
                }

                if (size > bestSize)
                {
                    bestSize = size;
                    bestId = id;
                }
            }

            for (int i = 0; i < isLand.Length; i++)
            {
                if (isLand[i] && componentId[i] != bestId)
                {
                    isLand[i] = false;
                }
            }
        }

        // ====================================================================================================
        // 4. Private Methods - Height
        // ====================================================================================================

        /// <summary>
        /// 모든 바다 타일을 출발점으로 하는 다중 소스 BFS로 각 육지의 해안 거리를 계산합니다.
        /// </summary>
        private static void ComputeDistanceToWater(int w, int d, bool[] isLand, int[] distToWater)
        {
            var queue = new Queue<int>();

            for (int i = 0; i < isLand.Length; i++)
            {
                if (isLand[i])
                {
                    distToWater[i] = int.MaxValue;
                }
                else
                {
                    distToWater[i] = 0;
                    queue.Enqueue(i);
                }
            }

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                int cx = current % w;
                int cy = current / w;
                int nextDist = distToWater[current] + 1;

                for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                {
                    int nx = cx + GridCoord.Neighbors4[n].X;
                    int ny = cy + GridCoord.Neighbors4[n].Y;

                    if (nx < 0 || nx >= w || ny < 0 || ny >= d)
                    {
                        continue;
                    }

                    int ni = ny * w + nx;
                    if (distToWater[ni] > nextDist)
                    {
                        distToWater[ni] = nextDist;
                        queue.Enqueue(ni);
                    }
                }
            }
        }

        /// <summary>
        /// 해안 거리를 계단식 고도로 변환합니다. 인접 타일의 고도 차가 1을 넘지 않도록 보장됩니다.
        /// </summary>
        private static void AssignHeights(IslandSettings settings, int w, int d, bool[] isLand, int[] distToWater, int[] height)
        {
            for (int i = 0; i < isLand.Length; i++)
            {
                if (!isLand[i])
                {
                    height[i] = 0;
                    continue;
                }

                int level = (distToWater[i] - 1) / HeightBandWidth;
                height[i] = Mathf.Clamp(level, 0, settings.MaxHeightLevel);
            }
        }

        // ====================================================================================================
        // 5. Private Methods - Rocks
        // ====================================================================================================

        /// <summary>
        /// 고지대에 바위를 놓아 통행을 막고 초크포인트를 만듭니다.
        /// 섬을 두 조각으로 갈라 버리면 방어가 불가능해지므로, 배치할 때마다 연결성을 검사하고 어기면 되돌립니다.
        /// </summary>
        private static void PlaceRocks(IslandSettings settings, System.Random rng, int w, int d, bool[] isLand, int[] height, bool[] isRock)
        {
            var candidates = new List<int>();
            for (int i = 0; i < isLand.Length; i++)
            {
                // 해안(거리 1)은 상륙 지점이므로 막지 않습니다.
                if (isLand[i] && height[i] >= 1)
                {
                    candidates.Add(i);
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            int targetRockCount = Mathf.Max(1, candidates.Count / 12);
            int placed = 0;

            for (int attempt = 0; attempt < MaxRockAttempts && placed < targetRockCount; attempt++)
            {
                int pick = candidates[rng.Next(candidates.Count)];
                if (isRock[pick])
                {
                    continue;
                }

                isRock[pick] = true;

                if (IsConnected(w, d, isLand, isRock))
                {
                    placed++;
                }
                else
                {
                    isRock[pick] = false;
                }
            }
        }

        /// <summary>
        /// 바위를 제외한 육지가 하나로 이어져 있는지 확인합니다.
        /// </summary>
        private static bool IsConnected(int w, int d, bool[] isLand, bool[] isRock)
        {
            int start = -1;
            int total = 0;

            for (int i = 0; i < isLand.Length; i++)
            {
                if (isLand[i] && !isRock[i])
                {
                    total++;
                    if (start < 0)
                    {
                        start = i;
                    }
                }
            }

            if (start < 0)
            {
                return false;
            }

            var visited = new bool[isLand.Length];
            var queue = new Queue<int>();
            queue.Enqueue(start);
            visited[start] = true;
            int reached = 0;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                reached++;

                int cx = current % w;
                int cy = current / w;

                for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                {
                    int nx = cx + GridCoord.Neighbors4[n].X;
                    int ny = cy + GridCoord.Neighbors4[n].Y;

                    if (nx < 0 || nx >= w || ny < 0 || ny >= d)
                    {
                        continue;
                    }

                    int ni = ny * w + nx;
                    if (!visited[ni] && isLand[ni] && !isRock[ni])
                    {
                        visited[ni] = true;
                        queue.Enqueue(ni);
                    }
                }
            }

            return reached == total;
        }

        // ====================================================================================================
        // 6. Private Methods - Houses
        // ====================================================================================================

        /// <summary>
        /// 내륙의 평지에 가옥을 배치합니다. 가옥끼리 최소 간격을 두어 방어선이 한 곳에 몰리지 않게 합니다.
        /// </summary>
        private static void PlaceHouses(IslandSettings settings, System.Random rng, IslandGrid grid)
        {
            var candidates = new List<Tile>();
            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];

                // 해변은 상륙 지점이므로 가옥을 두지 않습니다.
                if (tile.Type == TileType.Ground && !tile.IsCoastal)
                {
                    candidates.Add(tile);
                }
            }

            if (candidates.Count == 0)
            {
                return;
            }

            // 순서를 섞어 매번 다른 배치가 나오게 합니다.
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            var placed = new List<Tile>();

            for (int i = 0; i < candidates.Count && placed.Count < settings.HouseCount; i++)
            {
                var tile = candidates[i];
                bool tooClose = false;

                for (int p = 0; p < placed.Count; p++)
                {
                    if (GridCoord.ChebyshevDistance(tile.Coord, placed[p].Coord) < settings.HouseMinSpacing)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (tooClose)
                {
                    continue;
                }

                tile.Type = TileType.House;
                placed.Add(tile);
            }
        }

        // ====================================================================================================
        // 7. Private Methods - Landing Zones
        // ====================================================================================================

        /// <summary>
        /// 해안 타일을 섬 중심 기준 각도로 나누어 상륙 구역을 만듭니다.
        /// 각도로 나누기 때문에 구역들이 섬 둘레에 고르게 흩어지고, 결과적으로 방어 전선이 분산됩니다.
        /// </summary>
        private static void BuildLandingZones(IslandSettings settings, IslandGrid grid)
        {
            grid.LandingZones.Clear();

            var coastal = new List<Tile>();
            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];
                if (tile.IsCoastal && tile.Type == TileType.Beach)
                {
                    coastal.Add(tile);
                }
            }

            if (coastal.Count == 0)
            {
                return;
            }

            int zoneCount = Mathf.Clamp(settings.LandingZoneCount, 1, coastal.Count);
            var buckets = new List<Tile>[zoneCount];
            for (int i = 0; i < zoneCount; i++)
            {
                buckets[i] = new List<Tile>();
            }

            float cx = (grid.Width - 1) * 0.5f;
            float cy = (grid.Depth - 1) * 0.5f;

            for (int i = 0; i < coastal.Count; i++)
            {
                var tile = coastal[i];
                float angle = Mathf.Atan2(tile.Coord.Y - cy, tile.Coord.X - cx);

                // atan2는 -π..π를 반환하므로 0..1로 정규화한 뒤 구역 수로 나눕니다.
                float normalized = (angle + Mathf.PI) / (2f * Mathf.PI);
                int bucket = Mathf.Clamp(Mathf.FloorToInt(normalized * zoneCount), 0, zoneCount - 1);

                buckets[bucket].Add(tile);
            }

            int zoneId = 0;
            for (int i = 0; i < zoneCount; i++)
            {
                if (buckets[i].Count == 0)
                {
                    continue;
                }

                for (int t = 0; t < buckets[i].Count; t++)
                {
                    buckets[i][t].LandingZoneId = zoneId;
                }

                grid.LandingZones.Add(buckets[i]);
                zoneId++;
            }
        }
    }
}
