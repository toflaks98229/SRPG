using System.Collections.Generic;
using SRPG.Common;
using SRPG.Systems.Formation;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Systems.Props
{
    /// <summary>
    /// 지형지물을 어디에 어떻게 놓을지 정합니다.
    ///
    /// <b>격자가 보이면 실패입니다</b>
    ///
    /// 지형지물의 목적은 "여기가 몇 번 칸인지" 알려 주는 것이 아닙니다.
    /// 그런데 아무 생각 없이 놓으면 정확히 그 일을 합니다 — 칸마다 하나씩,
    /// 중심에, 같은 크기로, 같은 방향으로. 눈은 그 규칙을 즉시 찾아내고
    /// 지형 대신 <b>격자</b>를 봅니다.
    ///
    /// 그래서 여기서 하는 일은 전부 <b>규칙을 지우는</b> 쪽입니다.
    ///   · 중심에 놓지 않습니다 — 칸 안 어디든, 경계를 넘어가도 됩니다.
    ///   · 같은 방향을 보지 않습니다 — 자유 회전에 기울기까지 줍니다.
    ///   · 같은 크기가 아닙니다 — 대부분 작고 가끔 아주 큽니다.
    ///   · 고르게 흩지 않습니다 — 뭉칠 곳은 뭉치고 빌 곳은 텅 빕니다.
    ///
    /// 특히 <b>큰 것은 여러 칸에 걸치게</b> 합니다. 모든 것이 한 칸에 들어맞으면
    /// 눈이 칸 크기를 역산해 냅니다.
    ///
    /// <b>판독성은 건드리지 않습니다</b>
    ///
    /// 걸을 수 있는 땅에는 <b>무릎 아래</b>만 놓습니다.
    /// 걸을 수 있어 보이는데 커다란 바위가 서 있으면 플레이어는 못 가는 곳으로 읽습니다.
    /// 덩치 큰 바위는 이미 통행 불가가 확정된 절벽 위에만 올립니다.
    ///
    /// 지형지물은 <b>통행에 아무 영향을 주지 않습니다.</b> 길을 막는 것은 지형이지 장식이 아닙니다.
    /// 이 경계가 흐려지면 "보이는 것과 갈 수 있는 것이 다른" 상태가 되고,
    /// 그건 전술 게임에서 가장 나쁜 버그입니다.
    /// </summary>
    public static class PropPlacement
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>한 칸에서 시도하는 최대 개수입니다. 군집이 진해질 여지를 줍니다.</summary>
        private const int MaxPerTile = 3;

        /// <summary>군집 잡음의 크기입니다. 작을수록 뭉치는 덩어리가 커집니다.</summary>
        private const float ClusterNoiseScale = 0.22f;

        /// <summary>칸 중심에서 벗어나는 최대 거리입니다. 셀 크기에 대한 비율입니다.</summary>
        private const float ScatterRatio = 0.42f;

        /// <summary>해안에서는 덜 벗어나게 합니다. 물 위로 너무 튀어나가면 떠 보입니다.</summary>
        private const float CoastalScatterRatio = 0.2f;

        /// <summary>최대 기울기입니다. 이보다 크면 넘어진 것처럼 보입니다.</summary>
        private const float MaxTiltDegrees = 9f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 섬 전체의 지형지물을 정합니다.
        /// </summary>
        /// <param name="grid">지형입니다.</param>
        /// <param name="density">전체 밀도 배수입니다. 0이면 아무것도 놓지 않습니다.</param>
        /// <param name="results">결과를 담을 목록입니다. 호출 전에 비워집니다.</param>
        public static void Generate(IslandGrid grid, float density, List<PropInstance> results)
        {
            if (results == null)
            {
                return;
            }

            results.Clear();

            if (grid == null || density <= 0f)
            {
                return;
            }

            int seed = grid.Seed;
            float cell = grid.CellSize;

            // 시드마다 다른 잡음 영역을 쓰도록 흩뜨립니다.
            float clusterOffsetX = FormationScatter.Hash01(seed, 0x27D4EB2Fu) * 500f;
            float clusterOffsetY = FormationScatter.Hash01(seed, 0x165667B1u) * 500f;

            for (int i = 0; i < grid.AllTiles.Count; i++)
            {
                var tile = grid.AllTiles[i];

                if (tile.IsWater || tile.Type == TileType.House)
                {
                    continue;
                }

                var rule = RuleFor(grid, tile);
                if (rule.Chance <= 0f)
                {
                    continue;
                }

                // 군집 — 고르게 흩으면 잡음으로 보입니다. 뭉칠 곳은 뭉쳐야 지형으로 읽힙니다.
                float cluster = Mathf.PerlinNoise(
                    tile.Coord.X * ClusterNoiseScale + clusterOffsetX,
                    tile.Coord.Y * ClusterNoiseScale + clusterOffsetY);

                float chance = rule.Chance * density * Mathf.Lerp(0.1f, 1.9f, cluster);

                for (int n = 0; n < MaxPerTile; n++)
                {
                    if (Roll(seed, tile.Coord, n, 0x9E3779B9u) >= chance)
                    {
                        continue;
                    }

                    results.Add(Build(seed, tile, rule, cell, n));
                }
            }
        }

        // ====================================================================================================
        // 3. Private Methods - Instance
        // ====================================================================================================

        /// <summary>
        /// 지형지물 하나의 자리·크기·방향을 정합니다.
        /// </summary>
        private static PropInstance Build(int seed, Tile tile, PlacementRule rule, float cell, int index)
        {
            // 크기는 제곱으로 눌러 작은 쪽에 몰리게 합니다.
            // 큰 것이 흔하면 지형이 아니라 창고처럼 보입니다. 큰 것은 드물어야 큽니다.
            float sizeRoll = Roll(seed, tile.Coord, index, 0x85EBCA6Bu);
            sizeRoll *= sizeRoll;

            float radius = Mathf.Lerp(rule.MinRadius, rule.MaxRadius, sizeRoll) * cell;
            float height = Mathf.Lerp(rule.MinHeight, rule.MaxHeight, sizeRoll) * cell;

            // 풍화도는 크기와 반대로 갑니다.
            // 작은 것일수록 오래 굴러 둥글고, 큰 것일수록 덜 깎여 각집니다.
            float weathering = Mathf.Lerp(
                rule.MaxWeathering,
                rule.MinWeathering,
                sizeRoll);

            float scatter = (tile.IsCoastal ? CoastalScatterRatio : ScatterRatio) * cell;

            float offsetX = (Roll(seed, tile.Coord, index, 0xC2B2AE35u) - 0.5f) * 2f * scatter;
            float offsetZ = (Roll(seed, tile.Coord, index, 0x7FEB352Du) - 0.5f) * 2f * scatter;

            float yaw = Roll(seed, tile.Coord, index, 0x94D049BBu) * 360f;
            float tiltX = (Roll(seed, tile.Coord, index, 0xD6E8FEB8u) - 0.5f) * 2f * MaxTiltDegrees;
            float tiltZ = (Roll(seed, tile.Coord, index, 0xCB1EA4B9u) - 0.5f) * 2f * MaxTiltDegrees;

            return new PropInstance
            {
                GroundPosition = tile.WorldCenter + new Vector3(offsetX, 0f, offsetZ),
                Rotation = Quaternion.Euler(tiltX, yaw, tiltZ),
                Radius = radius,
                Height = height,
                Weathering = weathering,
                IsRock = rule.IsRock,

                // 형상 씨앗은 자리마다 달라야 합니다. 같으면 복제된 티가 납니다.
                Shape = Mix(seed, tile.Coord, index),
            };
        }

        // ====================================================================================================
        // 4. Private Methods - Rules
        // ====================================================================================================

        /// <summary>
        /// 타일의 문맥에 맞는 배치 규칙을 고릅니다.
        ///
        /// 지형지물은 아무 데나 같은 것이 나면 안 됩니다.
        /// 절벽 밑에는 굴러떨어진 돌이, 물가에는 깎인 조약돌이, 들판에는 이끼 둔덕이 있어야
        /// 각각이 <b>왜 거기 있는지</b> 설명됩니다. 그 설명이 되는 순간 지형이 진짜로 보입니다.
        /// </summary>
        private static PlacementRule RuleFor(IslandGrid grid, Tile tile)
        {
            if (tile.Type == TileType.Cliff)
            {
                // 절벽 위 — 이미 통행 불가가 확정된 곳입니다. 여기만 큰 바위를 올릴 수 있습니다.
                //
                // 넓게 퍼지되 높이 솟지는 않습니다.
                // 폭은 여러 칸에 걸쳐야 격자가 지워지지만, 높이는 그 뒤에 선 유닛을 가립니다.
                // 빌보드는 실루엣이 조금만 잘려도 병종 판독이 안 되므로 여기서는 폭만 씁니다.
                return new PlacementRule
                {
                    Chance = 0.55f,
                    MinRadius = 0.18f, MaxRadius = 0.58f,
                    MinHeight = 0.2f, MaxHeight = 0.68f,
                    MinWeathering = 0.05f, MaxWeathering = 0.45f,
                    IsRock = true,
                };
            }

            if (IsNextToCliff(grid, tile))
            {
                // 절벽 밑 — 위에서 굴러떨어진 돌입니다. 아직 덜 깎였습니다.
                return new PlacementRule
                {
                    Chance = 0.34f,
                    MinRadius = 0.09f, MaxRadius = 0.22f,
                    MinHeight = 0.07f, MaxHeight = 0.17f,
                    MinWeathering = 0.3f, MaxWeathering = 0.7f,
                    IsRock = true,
                };
            }

            if (tile.Type == TileType.Beach)
            {
                // 물가 — 파도가 오래 훑었습니다. 낮고 둥급니다.
                return new PlacementRule
                {
                    Chance = 0.26f,
                    MinRadius = 0.08f, MaxRadius = 0.2f,
                    MinHeight = 0.04f, MaxHeight = 0.1f,
                    MinWeathering = 0.75f, MaxWeathering = 1f,
                    IsRock = true,
                };
            }

            // 들판 — 흙과 이끼가 덮인 둔덕입니다. 암반이 아니라 지표에 가깝습니다.
            return new PlacementRule
            {
                Chance = 0.3f,
                MinRadius = 0.12f, MaxRadius = 0.3f,
                MinHeight = 0.04f, MaxHeight = 0.13f,
                MinWeathering = 0.82f, MaxWeathering = 1f,
                IsRock = false,
            };
        }

        /// <summary>
        /// 절벽과 맞닿아 있는지 봅니다. 대각선까지 셉니다 — 모서리에 붙은 것도 절벽 밑입니다.
        /// </summary>
        private static bool IsNextToCliff(IslandGrid grid, Tile tile)
        {
            for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
            {
                var neighbor = grid.GetTile(tile.Coord + GridCoord.Neighbors8[n]);

                if (neighbor != null && neighbor.Type == TileType.Cliff)
                {
                    return true;
                }
            }

            return false;
        }

        // ====================================================================================================
        // 5. Private Methods - Hashing
        // ====================================================================================================

        /// <summary>
        /// 자리마다 다른 0~1 값을 냅니다. 같은 입력이면 항상 같은 값이 나옵니다.
        ///
        /// 난수 발생기를 돌리지 않는 이유는 <b>순서에 기대지 않기 위해서</b>입니다.
        /// 타일을 도는 순서가 바뀌거나 규칙이 하나 늘어도 이미 놓인 것들이 흔들리지 않습니다.
        /// </summary>
        private static float Roll(int seed, GridCoord coord, int index, uint salt)
        {
            return FormationScatter.Hash01(Mix(seed, coord, index), salt);
        }

        private static int Mix(int seed, GridCoord coord, int index)
        {
            unchecked
            {
                return seed * 73856093 ^ coord.X * 19349663 ^ coord.Y * 83492791 ^ (index + 1) * 374761393;
            }
        }

        // ====================================================================================================
        // 6. Nested Types
        // ====================================================================================================

        /// <summary>
        /// 한 문맥에서 나올 수 있는 지형지물의 범위입니다.
        ///
        /// 값 하나가 아니라 <b>범위</b>인 것이 중요합니다.
        /// 같은 절벽 위에도 큰 바위와 자잘한 돌이 섞여 있어야 무리로 보입니다.
        /// 하나로 고정하면 도장을 찍은 것처럼 보입니다.
        /// </summary>
        private struct PlacementRule
        {
            /// <summary>한 번 시도할 때 놓일 확률입니다.</summary>
            public float Chance;

            /// <summary>반경의 범위입니다. 셀 크기에 대한 비율입니다.</summary>
            public float MinRadius;
            public float MaxRadius;

            /// <summary>높이의 범위입니다. 셀 크기에 대한 비율입니다.</summary>
            public float MinHeight;
            public float MaxHeight;

            /// <summary>풍화도의 범위입니다.</summary>
            public float MinWeathering;
            public float MaxWeathering;

            /// <summary>암반인지 여부입니다.</summary>
            public bool IsRock;
        }
    }
}
