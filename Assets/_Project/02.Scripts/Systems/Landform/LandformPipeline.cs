using System.Collections.Generic;
using SRPG.Common;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Systems.Landform
{
    /// <summary>
    /// 지형 조각 파이프라인입니다. 네 단계를 정해진 순서로 돌립니다.
    ///
    ///   1. 기초 지형   — FBM으로 자연스러운 기복을 깝니다
    ///   2. 인공 변형   — 길을 다지고 건물 터를 계단식으로 깎습니다
    ///   3. 매스 무브먼트 — 2단계가 남긴 수직 절단면을 무너뜨립니다
    ///   4. 오브젝트 배치 — 다져진 평지 위에 지형지물을 세웁니다 (호출자가 담당)
    ///
    /// <b>순서가 전부입니다</b>
    ///
    /// 침식을 먼저 돌리고 길을 내면, 길의 절단면이 날것 그대로 남습니다.
    /// 사람이 땅을 깎은 자국은 반드시 그 뒤에 다시 무너져 있어야 합니다.
    /// 그 순서가 지켜져야 "예전에 누가 여기 길을 냈고 세월이 지났다"로 읽힙니다.
    ///
    /// <b>길은 어디에서 어디로 가는가</b>
    ///
    /// 논문의 예시는 마을 입구와 산 중턱이지만, 이 게임에서 의미 있는 두 지점은 따로 있습니다.
    ///   · <b>상륙 지점</b> — 적이 올라오는 곳이자, 원래 사람들이 배를 대던 곳
    ///   · <b>가옥</b>     — 지켜야 하는 곳
    ///
    /// 이 둘을 잇는 길은 장식이 아닙니다. <b>공격 경로</b>입니다.
    /// 플레이어가 지형을 보고 "적이 저리로 오겠구나"를 읽을 수 있게 됩니다.
    /// </summary>
    public static class LandformPipeline
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>타일 한 변을 나눌 조각 수입니다.</summary>
        public const int DefaultResolution = 4;

        /// <summary>완전히 다져지는 길의 반경입니다. 표본 단위입니다.</summary>
        private const float RoadRadius = 1.1f;

        /// <summary>길의 영향이 사라지는 반경입니다.</summary>
        private const float RoadShoulder = 3.2f;

        /// <summary>건물 터의 반경입니다.</summary>
        private const float TerraceRadius = 2.2f;

        /// <summary>건물 터의 영향이 사라지는 반경입니다.</summary>
        private const float TerraceBlend = 4.5f;

        /// <summary>침식 반복 횟수입니다.</summary>
        private const int ErosionIterations = 12;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 격자로부터 조각된 지형을 만듭니다.
        /// </summary>
        /// <param name="grid">지형 구조입니다. 고도 단계는 바뀌지 않습니다.</param>
        /// <param name="resolution">타일 한 변을 나눌 조각 수입니다.</param>
        /// <param name="roads">만들어진 길입니다. 필요 없으면 null을 넘겨도 됩니다.</param>
        public static HeightField Build(
            IslandGrid grid,
            int resolution = DefaultResolution,
            List<List<GridCoord>> roads = null)
        {
            if (grid == null)
            {
                return null;
            }

            var field = new HeightField(grid, resolution);

            roads?.Clear();

            // ── 0단계 ──────────────────────────────────────────────────────────────
            //
            // 단 경계를 타일 격자에서 떼어냅니다.
            //
            // 이것을 가장 먼저 해야 합니다. 뒤에 오는 모든 단계가 "여기가 몇 단인가"를
            // 기준으로 삼기 때문입니다. 침식은 같은 단끼리만 흙을 주고받고,
            // 붕괴는 단 경계를 찾아 무너뜨립니다. 경계가 나중에 움직이면 전부 어긋납니다.
            BoundaryWarp.Apply(field, grid, grid.Seed);

            // ── 1단계 ──────────────────────────────────────────────────────────────
            FbmNoise.Apply(field, grid.Seed);

            // ── 2단계 ──────────────────────────────────────────────────────────────
            CarveRoads(grid, field, roads);
            TerraceBuildingSites(grid, field);

            // ── 3단계 ──────────────────────────────────────────────────────────────
            //
            // 먼저 층 안에서의 급경사를 무너뜨리고, 그다음 층 경계를 무너뜨립니다.
            //
            // 순서가 중요합니다. 절벽 붕괴가 만든 비탈은 갓 쌓인 흙이라 다시 무너져야 하는데,
            // CliffCollapse 가 마지막에 스스로 한 번 더 안정화합니다.
            // 반대로 하면 붕괴가 만든 비탈이 날것 그대로 남습니다.
            ThermalErosion.Apply(field, ErosionIterations);
            CliffCollapse.Apply(field, grid.Seed);

            return field;
        }

        // ====================================================================================================
        // 3. Private Methods - Phase 2
        // ====================================================================================================

        /// <summary>
        /// 상륙 지점마다 가장 가까운 가옥까지 길을 냅니다.
        /// </summary>
        private static void CarveRoads(IslandGrid grid, HeightField field, List<List<GridCoord>> roads)
        {
            if (grid.HouseTiles.Count == 0 || grid.LandingZones.Count == 0)
            {
                return;
            }

            var path = new List<GridCoord>();

            for (int z = 0; z < grid.LandingZones.Count; z++)
            {
                var zone = grid.LandingZones[z];
                if (zone.Count == 0)
                {
                    continue;
                }

                // 구역의 한가운데 타일에서 출발합니다.
                var start = zone[zone.Count / 2];
                var house = FindNearestHouse(grid, start);

                if (house == null)
                {
                    continue;
                }

                var from = CenterSampleOf(field, start);
                var to = CenterSampleOf(field, house);

                if (!RoadPlanner.TryFindPath(field, from, to, path))
                {
                    continue;
                }

                TerrainSculptor.CutAndFill(field, path, RoadRadius, RoadShoulder);

                roads?.Add(new List<GridCoord>(path));
            }
        }

        /// <summary>
        /// 가옥이 앉을 터를 계단식으로 다집니다.
        /// </summary>
        private static void TerraceBuildingSites(IslandGrid grid, HeightField field)
        {
            for (int i = 0; i < grid.HouseTiles.Count; i++)
            {
                TerrainSculptor.Terrace(
                    field,
                    CenterSampleOf(field, grid.HouseTiles[i]),
                    TerraceRadius,
                    TerraceBlend);
            }
        }

        // ====================================================================================================
        // 4. Private Methods - Helpers
        // ====================================================================================================

        private static Tile FindNearestHouse(IslandGrid grid, Tile from)
        {
            Tile best = null;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < grid.HouseTiles.Count; i++)
            {
                int distance = GridCoord.ManhattanDistance(from.Coord, grid.HouseTiles[i].Coord);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = grid.HouseTiles[i];
                }
            }

            return best;
        }

        /// <summary>
        /// 타일 한가운데에 해당하는 표본입니다.
        ///
        /// 타일 모서리 표본을 쓰면 이웃 타일과 공유되어, 길이 타일 경계를 타고 갑니다.
        /// 한가운데를 잡아야 길이 타일을 가로질러 자연스럽게 흐릅니다.
        /// </summary>
        private static GridCoord CenterSampleOf(HeightField field, Tile tile)
        {
            var corner = field.TileToSample(tile.Coord);
            int half = field.Resolution / 2;

            return new GridCoord(corner.X + half, corner.Y + half);
        }
    }
}
