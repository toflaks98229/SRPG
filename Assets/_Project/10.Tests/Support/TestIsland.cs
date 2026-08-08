using System.Collections.Generic;
using SRPG.Common;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Tests.Support
{
    /// <summary>
    /// 테스트가 쓸 섬을 짓습니다.
    ///
    /// <b>이것은 맵 생성기가 아닙니다</b>
    ///
    /// 절차적 맵 생성을 걷어내면서, 섬을 필요로 하던 테스트들이 갈 곳을 잃었습니다.
    /// 그 테스트들이 검증하는 것은 길찾기·영향력 맵·대열·분대 점유처럼
    /// <b>맵이 어떻게 만들어지는지와 무관한</b> 시스템입니다.
    /// 지형 하나가 필요할 뿐이라 여기서 가장 단순한 것을 지어 줍니다.
    ///
    /// 그래서 이 클래스는 <b>테스트 어셈블리에만</b> 있습니다.
    /// 게임 코드에서는 참조할 수 없고, 참조해서도 안 됩니다.
    ///
    /// <b>왜 생성기에 의존하지 않는 편이 나은가</b>
    ///
    /// 예전에는 이 테스트들이 실제 맵 생성기를 불러 섬을 얻었습니다.
    /// 그러면 생성기가 바뀔 때마다 길찾기 테스트가 같이 흔들립니다 —
    /// 길찾기는 아무것도 바뀌지 않았는데 말입니다.
    /// 픽스처를 따로 두면 검사 대상이 하나로 좁혀집니다.
    ///
    /// <b>무엇을 보장하는가</b>
    ///
    ///   · 걸을 수 있는 땅이 하나로 이어집니다
    ///   · 이웃한 통행 가능 타일의 고도 차가 1을 넘지 않습니다
    ///   · 해안선·가옥·상륙 구역이 최소 하나씩 있습니다
    ///
    /// 이 셋이 깨지면 테스트가 검증하려던 것이 아니라 픽스처가 실패합니다.
    /// </summary>
    public static class TestIsland
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>고도 한 단이 차지하는 해안으로부터의 거리입니다.</summary>
        private const int HeightBandWidth = 2;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 원형에 가까운 섬을 짓습니다.
        /// </summary>
        /// <param name="seed">윤곽과 배치를 흔듭니다. 같은 값이면 같은 섬이 나옵니다.</param>
        /// <param name="width">격자 가로 칸 수입니다.</param>
        /// <param name="depth">격자 세로 칸 수입니다.</param>
        /// <param name="maxHeightLevel">최대 고도 단계입니다.</param>
        /// <param name="cliffCount">놓을 절벽 수입니다. 연결을 끊는 자리는 건너뜁니다.</param>
        /// <param name="houseCount">놓을 가옥 수입니다.</param>
        /// <param name="landingZoneCount">나눌 상륙 구역 수입니다.</param>
        public static IslandGrid Create(
            int seed = 1,
            int width = 24,
            int depth = 24,
            int maxHeightLevel = 2,
            int cliffCount = 6,
            int houseCount = 3,
            int landingZoneCount = 4)
        {
            var grid = new IslandGrid(width, depth, cellSize: 2f, heightStep: 0.9f) { Seed = seed };
            var random = new System.Random(seed);

            var isLand = new bool[width * depth];
            var distance = new int[width * depth];

            BuildOutline(random, width, depth, isLand);
            ComputeDistanceToWater(width, depth, isLand, distance);

            // 고도는 해안 거리를 그대로 계단으로 바꿉니다.
            //
            // 단조 함수라 이웃 간 차이가 반드시 1 이하입니다.
            // 픽스처에는 그 보장이 자연스러움보다 중요합니다 — 길찾기 테스트가
            // "갈 수 있어야 하는데 못 가는" 상황에 걸리면 안 되기 때문입니다.
            for (int i = 0; i < isLand.Length; i++)
            {
                var tile = grid.GetTile(new GridCoord(i % width, i / width));

                if (!isLand[i])
                {
                    tile.Type = TileType.Water;
                    tile.Height = 0;
                    continue;
                }

                tile.Height = Mathf.Clamp((distance[i] - 1) / HeightBandWidth, 0, maxHeightLevel);
                tile.Type = distance[i] == 1 ? TileType.Beach : TileType.Ground;
            }

            grid.RebuildDerivedData();

            PlaceCliffs(grid, random, cliffCount);
            grid.RebuildDerivedData();

            PlaceHouses(grid, random, houseCount);
            grid.RebuildDerivedData();

            BuildLandingZones(grid, landingZoneCount);

            return grid;
        }

        /// <summary>
        /// 절벽도 가옥도 없는 평평한 사각 섬입니다.
        ///
        /// 길찾기나 대열처럼 <b>지형이 방해하면 안 되는</b> 검사에 씁니다.
        /// </summary>
        public static IslandGrid CreateFlat(int width = 16, int depth = 16)
        {
            return Create(seed: 1, width: width, depth: depth, maxHeightLevel: 0, cliffCount: 0, houseCount: 1);
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 가운데를 중심으로 한 원형 육지를 만듭니다. 테두리는 언제나 바다입니다.
        /// </summary>
        private static void BuildOutline(System.Random random, int width, int depth, bool[] isLand)
        {
            float cx = (width - 1) * 0.5f;
            float cy = (depth - 1) * 0.5f;
            float radius = Mathf.Min(width, depth) * 0.36f;

            // 시드마다 반경을 조금씩 흔들어 같은 섬만 반복되지 않게 합니다.
            radius *= 0.85f + (float)random.NextDouble() * 0.3f;

            for (int y = 0; y < depth; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;

                    isLand[y * width + x] = Mathf.Sqrt(dx * dx + dy * dy) < radius;
                }
            }

            // 테두리는 반드시 바다여야 합니다. 섬이 격자 밖으로 잘리면 해안선이 성립하지 않습니다.
            for (int x = 0; x < width; x++)
            {
                isLand[x] = false;
                isLand[(depth - 1) * width + x] = false;
            }

            for (int y = 0; y < depth; y++)
            {
                isLand[y * width] = false;
                isLand[y * width + (width - 1)] = false;
            }
        }

        private static void ComputeDistanceToWater(int width, int depth, bool[] isLand, int[] distance)
        {
            var queue = new Queue<int>();

            for (int i = 0; i < isLand.Length; i++)
            {
                if (isLand[i])
                {
                    distance[i] = int.MaxValue;
                }
                else
                {
                    distance[i] = 0;
                    queue.Enqueue(i);
                }
            }

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();

                int cx = current % width;
                int cy = current / width;
                int next = distance[current] + 1;

                for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                {
                    int nx = cx + GridCoord.Neighbors4[n].X;
                    int ny = cy + GridCoord.Neighbors4[n].Y;

                    if (nx < 0 || nx >= width || ny < 0 || ny >= depth)
                    {
                        continue;
                    }

                    int ni = ny * width + nx;

                    if (distance[ni] > next)
                    {
                        distance[ni] = next;
                        queue.Enqueue(ni);
                    }
                }
            }
        }

        /// <summary>
        /// 절벽을 놓습니다. <b>연결을 끊는 자리는 되돌립니다.</b>
        ///
        /// 섬이 두 조각으로 갈라지면 길찾기 테스트가 "경로가 없다"로 실패하는데,
        /// 그건 길찾기의 잘못이 아니라 픽스처의 잘못입니다.
        /// </summary>
        private static void PlaceCliffs(IslandGrid grid, System.Random random, int count)
        {
            if (count <= 0)
            {
                return;
            }

            var candidates = new List<Tile>();

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];

                // 해변은 상륙 지점이므로 막지 않습니다.
                if (tile.Type == TileType.Ground && !tile.IsCoastal)
                {
                    candidates.Add(tile);
                }
            }

            int placed = 0;

            for (int attempt = 0; attempt < count * 8 && placed < count; attempt++)
            {
                if (candidates.Count == 0)
                {
                    break;
                }

                var tile = candidates[random.Next(candidates.Count)];

                if (tile.Type == TileType.Cliff)
                {
                    continue;
                }

                tile.Type = TileType.Cliff;
                grid.RebuildDerivedData();

                if (IsConnected(grid))
                {
                    placed++;
                }
                else
                {
                    tile.Type = TileType.Ground;
                    grid.RebuildDerivedData();
                }
            }
        }

        private static bool IsConnected(IslandGrid grid)
        {
            if (grid.WalkableTiles.Count == 0)
            {
                return false;
            }

            var visited = new HashSet<GridCoord> { grid.WalkableTiles[0].Coord };
            var queue = new Queue<Tile>();

            queue.Enqueue(grid.WalkableTiles[0]);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                {
                    var neighbor = grid.GetTile(current.Coord + GridCoord.Neighbors4[n]);

                    if (neighbor == null || !neighbor.IsWalkable || visited.Contains(neighbor.Coord))
                    {
                        continue;
                    }

                    if (Mathf.Abs(neighbor.Height - current.Height) > 1)
                    {
                        continue;
                    }

                    visited.Add(neighbor.Coord);
                    queue.Enqueue(neighbor);
                }
            }

            return visited.Count == grid.WalkableTiles.Count;
        }

        private static void PlaceHouses(IslandGrid grid, System.Random random, int count)
        {
            if (count <= 0)
            {
                return;
            }

            var candidates = new List<Tile>();

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];

                if (tile.Type == TileType.Ground && !tile.IsCoastal)
                {
                    candidates.Add(tile);
                }
            }

            // 내륙이 없으면 조건을 풉니다. 가옥이 없으면 목표가 없는 섬이 됩니다.
            if (candidates.Count == 0)
            {
                candidates.AddRange(grid.WalkableTiles);
            }

            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            for (int i = 0; i < candidates.Count && i < count; i++)
            {
                candidates[i].Type = TileType.House;
            }
        }

        /// <summary>
        /// 해변을 각도로 나눠 상륙 구역을 만듭니다. 구역들이 섬 둘레에 고르게 흩어집니다.
        /// </summary>
        private static void BuildLandingZones(IslandGrid grid, int count)
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

            if (coastal.Count == 0 || count <= 0)
            {
                return;
            }

            int zoneCount = Mathf.Clamp(count, 1, coastal.Count);
            var buckets = new List<Tile>[zoneCount];

            for (int i = 0; i < zoneCount; i++)
            {
                buckets[i] = new List<Tile>();
            }

            float cx = (grid.Width - 1) * 0.5f;
            float cy = (grid.Depth - 1) * 0.5f;

            for (int i = 0; i < coastal.Count; i++)
            {
                float angle = Mathf.Atan2(coastal[i].Coord.Y - cy, coastal[i].Coord.X - cx);
                float normalized = (angle + Mathf.PI) / (2f * Mathf.PI);

                int bucket = Mathf.Clamp(Mathf.FloorToInt(normalized * zoneCount), 0, zoneCount - 1);
                buckets[bucket].Add(coastal[i]);
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
