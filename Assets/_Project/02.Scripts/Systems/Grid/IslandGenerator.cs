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
    ///   3) <b>지형을 시뮬레이션한다</b> — 물과 중력이 골짜기와 능선을 만든다
    ///   4) 그 지형에서 고도 단계와 통행 불가를 <b>읽어 낸다</b>
    ///   5) 걸을 수 있는 땅이 이어지도록 경사로를 깎는다 — 그 경사로가 초크포인트가 된다
    ///   6) 가옥을 서로 떨어뜨려 배치한다
    ///   7) 해안을 각도 기준으로 나눠 상륙 구역을 만든다
    ///
    /// 3번과 4번의 순서가 핵심입니다. 타일은 지형을 만들지 않고 읽습니다.
    /// </summary>
    public static class IslandGenerator
    {
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

            // 지형을 먼저 만듭니다. 타일은 아직 높이를 모릅니다.
            //
            // 순서가 뒤집힌 자리입니다. 예전에는 여기서 고도 단계를 정하고 그 안에서
            // 침식을 돌렸는데, 그러면 침식이 움직일 폭이 한 단의 절반으로 묶여
            // 골짜기가 파이지 않았습니다.
            var simulation = Landform.LandformPipeline.Simulate(
                w, d, settings.CellSize,
                isLand,
                seed,
                settings.MaxHeightLevel * settings.HeightStep);

            // 침식된 지형을 계단식 대지로 다집니다.
            //
            // <b>이것이 타일을 읽기 전에 와야 합니다.</b>
            // 침식이 끝난 지형은 매끈해서 절벽이 없습니다. 다지기 전에 절벽을 찾으면
            // 하나도 나오지 않고, 그러면 장벽도 초크포인트도 없는 섬이 됩니다.
            Landform.LandformPipeline.Quantize(simulation, settings.HeightStep, settings.MaxHeightLevel);

            // 다져진 지형을 읽어 고도 단계를 정합니다.
            Landform.LandformPipeline.ReadTileHeights(
                simulation, w, d, isLand, settings.HeightStep, settings.MaxHeightLevel, height);

            // 통행 불가를 경사에서 읽어 냅니다.
            //
            // <b>여기서 섬 전체를 평평하게 만들면 안 됩니다.</b>
            // 예전에는 고도차를 1 이하로 강제해 모든 곳을 걸을 수 있게 만들었는데,
            // 그러면 장벽이 사라져 초크포인트가 없어지고 전선이 사방으로 열립니다.
            // 지킬 곳이 없는 섬은 전술 게임이 아닙니다.
            //
            // 대신 오를 수 없는 면을 그대로 두고, 걸을 수 있는 땅만 이어 줍니다.
            TacticalShaping.MarkCliffs(simulation, w, d, isLand, height, isRock, settings.HeightStep);

            TacticalShaping.ConnectRegions(
                simulation, w, d, isLand, height, isRock,
                settings.HeightStep, settings.MaxHeightLevel);

            ApplyTerrain(grid, w, d, isLand, isRock, distToWater, height);

            grid.RebuildDerivedData();

            PlaceHouses(settings, rng, grid);
            grid.RebuildDerivedData();

            BuildLandingZones(settings, grid);

            // 가옥과 상륙 지점이 정해졌으니 길과 터를 깎습니다.
            // 지형을 바꾸는 마지막 작업입니다.
            Landform.LandformPipeline.CarveRoadsAndPads(simulation, grid);
            Landform.LandformPipeline.Quantize(simulation, settings.HeightStep, settings.MaxHeightLevel);

            // <b>여기서 한 번에 확정합니다.</b>
            //
            // 지형을 깎을 때마다 타일을 다시 읽어야 하는데, 읽고 나서 또 깎으면
            // 끝없이 어긋납니다. 그래서 지형 변경을 여기까지로 못 박고
            // 그 뒤에 딱 한 번 읽습니다.
            Landform.LandformPipeline.ReadTileHeights(
                simulation, w, d, isLand, settings.HeightStep, settings.MaxHeightLevel, height);

            // <b>통로를 벽 판정보다 먼저 냅니다.</b>
            //
            // 예전에는 벽을 정한 뒤에 통로를 뚫었는데, 그러면 나중에 낸 통로가
            // 앞서 낸 통로를 다시 막는 되먹임이 생겨 끝내 수렴하지 않았습니다.
            // 먼저 길을 내고, 그 길을 건드리지 않는다는 조건으로 벽을 정합니다.
            var carved = EnsureObjectivesReachable(grid, simulation, w, d, isLand, height, settings);

            TacticalShaping.MarkCliffs(
                simulation, w, d, isLand, height, isRock, settings.HeightStep, carved);

            TacticalShaping.ConnectRegions(
                simulation, w, d, isLand, height, isRock,
                settings.HeightStep, settings.MaxHeightLevel, carved);

            ApplyTerrain(grid, w, d, isLand, isRock, distToWater, height);
            grid.RebuildDerivedData();

            grid.Height = Landform.LandformPipeline.BuildField(simulation, grid, settings.MaxHeightLevel);

            return grid;
        }


        /// <summary>
        /// 모든 상륙 지점에서 모든 가옥까지 걸어갈 수 있게 만듭니다.
        ///
        /// <b>왜 여기만 따로 보장하는가</b>
        ///
        /// 연결 복구는 가장 싼 자리를 골라 조금씩 깎는 방식이라 대개 잘 듣지만,
        /// 깎은 자리가 다시 벽으로 판정되는 되먹임에 걸리면 수렴하지 못합니다.
        ///
        /// 다른 자리는 못 이어도 그만입니다 — 절벽 위 좁은 턱 같은 것들입니다.
        /// 하지만 <b>가옥에 못 닿으면 지킬 것이 없고, 상륙 지점에서 못 나가면 적이 갇힙니다.</b>
        /// 그 둘은 게임의 전제라서 확실히 뚫어야 합니다.
        /// </summary>
        private static bool[] EnsureObjectivesReachable(
            IslandGrid grid, Landform.TerrainSimulation simulation,
            int w, int d, bool[] isLand, int[] height,
            IslandSettings settings)
        {
            var carved = new bool[w * d];

            if (grid.HouseTiles.Count == 0)
            {
                return carved;
            }

            int anchor = grid.HouseTiles[0].Coord.Y * w + grid.HouseTiles[0].Coord.X;

            // 이미 낸 길을 기억해 둡니다. 나중 통로가 앞 통로를 막으면 안 됩니다.
            carved[anchor] = true;

            var ignored = new bool[w * d];

            for (int i = 1; i < grid.HouseTiles.Count; i++)
            {
                var coord = grid.HouseTiles[i].Coord;

                TacticalShaping.ForceCorridor(
                    simulation, w, d, isLand, height, ignored,
                    anchor, coord.Y * w + coord.X,
                    settings.HeightStep, settings.MaxHeightLevel, carved);
            }

            for (int z = 0; z < grid.LandingZones.Count; z++)
            {
                var zone = grid.LandingZones[z];

                if (zone.Count == 0)
                {
                    continue;
                }

                var coord = zone[zone.Count / 2].Coord;

                TacticalShaping.ForceCorridor(
                    simulation, w, d, isLand, height, ignored,
                    anchor, coord.Y * w + coord.X,
                    settings.HeightStep, settings.MaxHeightLevel, carved);
            }

            return carved;
        }

        /// <summary>
        /// 계산된 고도와 통행 불가를 타일에 옮겨 적습니다.
        ///
        /// 지형을 깎을 때마다 불러야 합니다. 타일은 언제나 지금의 지형을 읽고 있어야
        /// "보이는 것과 갈 수 있는 곳"이 어긋나지 않습니다.
        /// </summary>
        private static void ApplyTerrain(
            IslandGrid grid, int w, int d,
            bool[] isLand, bool[] isRock, int[] distToWater, int[] height)
        {
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

                    // 가옥은 이미 정해진 목표입니다. 지형을 다시 읽는다고 지워지면 안 됩니다.
                    //
                    // 이 함수는 지형을 깎을 때마다 다시 불리는데, 그때마다 종류를 새로 쓰면
                    // 나중에 배치한 가옥이 통째로 사라집니다. 실제로 그렇게 되어
                    // "가옥이 없습니다"로 검사가 잡았습니다.
                    if (tile.Type == TileType.House)
                    {
                        // 가옥 터는 다져 놓은 평지라 벽이 될 수 없습니다.
                        isRock[i] = false;
                        continue;
                    }

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

            // 절벽이 많은 섬에서는 내륙 평지가 아예 없을 수 있습니다.
            // 가옥이 없으면 지킬 목표가 없어 전투가 성립하지 않으므로, 조건을 풀어 다시 찾습니다.
            if (candidates.Count == 0)
            {
                candidates.AddRange(grid.WalkableTiles);
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
