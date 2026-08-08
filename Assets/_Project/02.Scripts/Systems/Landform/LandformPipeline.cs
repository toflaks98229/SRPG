using SRPG.Common;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Systems.Landform
{
    /// <summary>
    /// 지형 생성 파이프라인입니다.
    ///
    /// <b>순서를 뒤집었습니다</b>
    ///
    /// 예전 순서는 이랬습니다 — 타일의 고도 단계를 먼저 정하고, 그 안에서 침식을 돌리고,
    /// 경계를 노이즈로 흐트러뜨렸습니다.
    ///
    /// 그 순서로는 한계가 뚜렷했습니다.
    ///   · 침식이 움직일 폭이 한 단의 절반으로 묶여 골짜기가 파이지 않았습니다.
    ///     그래서 물길을 손으로 그려 넣어야 했고, 그건 시뮬레이션이 아니라 작도였습니다.
    ///   · 단 사이를 수직 벽으로 이어서 어디서 보든 90도였고,
    ///     벽이 바닥과 만나는 자리도 90도였습니다.
    ///
    /// 지금 순서는 이렇습니다.
    ///
    ///   1. <b>침식</b> — 타일 없이 연속된 지형을 물과 중력으로 깎습니다.
    ///                    골짜기도 능선도 애추도 전부 여기서 나옵니다.
    ///   2. <b>평탄화</b> — 완만한 곳만 다져 딛을 수 있는 단을 만듭니다.
    ///                     가파른 곳은 침식이 남긴 사면 그대로 둡니다.
    ///   3. <b>읽기</b>   — 그 결과에서 고도 단계를 읽어 냅니다.
    ///
    /// 3번이 마지막이라는 것이 핵심입니다. 타일은 지형을 만드는 것이 아니라 <b>읽어 내는 것</b>입니다.
    /// 단 경계는 침식된 높이의 등고선이므로 타일 격자와 아무 상관이 없고,
    /// 단과 단 사이는 벽이 아니라 사면이므로 직각이 생길 자리가 없습니다.
    /// </summary>
    public static class LandformPipeline
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>건물 터의 반경입니다. 표본 단위입니다.</summary>
        private const float PadRadius = 2.6f;

        /// <summary>건물 터가 주변으로 풀리는 반경입니다.</summary>
        private const float PadBlend = 5.5f;

        /// <summary>완전히 다져지는 길의 반경입니다. 표본 단위입니다.</summary>
        private const float RoadRadius = 1.1f;

        /// <summary>길의 영향이 사라지는 반경입니다.</summary>
        private const float RoadShoulder = 2.8f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 섬 윤곽에 맞춰 지형을 시뮬레이션합니다. 타일 고도는 아직 정해지지 않습니다.
        /// </summary>
        /// <param name="width">타일 가로 칸 수입니다.</param>
        /// <param name="depth">타일 세로 칸 수입니다.</param>
        /// <param name="cellSize">타일 한 칸의 월드 크기입니다.</param>
        /// <param name="isLand">타일 단위 육지 여부입니다.</param>
        /// <param name="seed">같은 값이면 같은 지형이 나옵니다.</param>
        /// <param name="peakHeight">봉우리의 목표 높이입니다.</param>
        public static TerrainSimulation Simulate(
            int width, int depth, float cellSize,
            bool[] isLand,
            int seed,
            float peakHeight)
        {
            var simulation = new TerrainSimulation(width, depth, cellSize);

            // 타일 단위 육지를 표본 단위로 옮깁니다.
            for (int sy = 0; sy < simulation.Depth; sy++)
            {
                for (int sx = 0; sx < simulation.Width; sx++)
                {
                    int tileIndex = (sy / TerrainSimulation.Subdivision) * width + sx / TerrainSimulation.Subdivision;

                    simulation.Land[sy * simulation.Width + sx] = isLand[tileIndex];
                }
            }

            simulation.Simulate(seed, peakHeight);

            return simulation;
        }

        /// <summary>
        /// 시뮬레이션 결과를 다져 완성된 지형으로 만듭니다.
        /// </summary>
        /// <param name="simulation">침식이 끝난 지형입니다.</param>
        /// <param name="grid">타일 격자입니다. 원점과 가옥 위치를 씁니다.</param>
        /// <param name="maxLevel">최대 고도 단계입니다.</param>
        public static HeightField Finish(TerrainSimulation simulation, IslandGrid grid, int maxLevel)
        {
            if (simulation == null || grid == null)
            {
                return null;
            }

            // 사람이 땅을 건드린 자국입니다. 침식이 끝난 뒤, 다지기 전에 넣습니다.
            //
            // 자연 지형만 있으면 무인도로 보입니다. 사람이 살았다는 인상은 건물이 아니라
            // 땅에 남은 자국에서 옵니다 — 다져진 길, 깎아 만든 평평한 터.
            CarveRoads(simulation, grid);

            // 가옥 터는 사람이 삽으로 판 자리라 경사와 무관하게 눌러 버립니다.
            for (int i = 0; i < grid.HouseTiles.Count; i++)
            {
                var coord = grid.HouseTiles[i].Coord;
                int half = TerrainSimulation.Subdivision / 2;

                TerrainFlattening.FlattenPad(
                    simulation.Height,
                    simulation.Land,
                    simulation.Width,
                    simulation.Depth,
                    coord.X * TerrainSimulation.Subdivision + half,
                    coord.Y * TerrainSimulation.Subdivision + half,
                    PadRadius,
                    PadBlend);
            }

            return new HeightField(simulation, grid.Origin, grid.HeightStep, maxLevel);
        }

        /// <summary>
        /// 상륙 지점에서 가장 가까운 가옥까지 길을 다집니다.
        ///
        /// 이 게임에서 그 둘을 잇는 선은 장식이 아니라 <b>공격 경로</b>입니다.
        /// 플레이어가 지형을 보고 "적이 저리로 오겠구나"를 읽을 수 있게 됩니다.
        ///
        /// 경로는 최단 거리가 아니라 <b>최소 건설 비용</b>을 따릅니다 (Galin et al. 2010).
        /// 비용이 경사에 초선형으로 늘어나므로 길이 등고선을 따라 돌아 오릅니다.
        /// </summary>
        private static void CarveRoads(TerrainSimulation simulation, IslandGrid grid)
        {
            if (grid.HouseTiles.Count == 0 || grid.LandingZones.Count == 0)
            {
                return;
            }

            var path = new System.Collections.Generic.List<GridCoord>();
            int half = TerrainSimulation.Subdivision / 2;

            for (int z = 0; z < grid.LandingZones.Count; z++)
            {
                var zone = grid.LandingZones[z];
                if (zone.Count == 0)
                {
                    continue;
                }

                var start = zone[zone.Count / 2].Coord;
                var house = FindNearestHouse(grid, start);

                var from = new GridCoord(
                    start.X * TerrainSimulation.Subdivision + half,
                    start.Y * TerrainSimulation.Subdivision + half);

                var to = new GridCoord(
                    house.X * TerrainSimulation.Subdivision + half,
                    house.Y * TerrainSimulation.Subdivision + half);

                if (!RoadPlanner.TryFindPath(simulation, from, to, path))
                {
                    continue;
                }

                // 경로를 따라가며 다집니다. 폭이 좁으므로 터 다지기를 잘게 반복합니다.
                for (int p = 0; p < path.Count; p++)
                {
                    TerrainFlattening.FlattenPad(
                        simulation.Height, simulation.Land,
                        simulation.Width, simulation.Depth,
                        path[p].X, path[p].Y,
                        RoadRadius, RoadShoulder);
                }
            }
        }

        private static GridCoord FindNearestHouse(IslandGrid grid, GridCoord from)
        {
            var best = grid.HouseTiles[0].Coord;
            int bestDistance = int.MaxValue;

            for (int i = 0; i < grid.HouseTiles.Count; i++)
            {
                int distance = GridCoord.ManhattanDistance(from, grid.HouseTiles[i].Coord);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = grid.HouseTiles[i].Coord;
                }
            }

            return best;
        }

        /// <summary>
        /// 타일의 고도 단계를 지형에서 읽어 냅니다.
        ///
        /// <b>타일은 지형을 만들지 않습니다. 읽습니다.</b>
        /// 타일 한가운데의 표본이 속한 단을 그대로 씁니다.
        /// </summary>
        public static void ReadTileHeights(
            TerrainSimulation simulation,
            int width, int depth,
            bool[] isLand,
            float heightStep,
            int maxLevel,
            int[] height)
        {
            int half = TerrainSimulation.Subdivision / 2;

            for (int y = 0; y < depth; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = y * width + x;

                    if (!isLand[i])
                    {
                        height[i] = 0;
                        continue;
                    }

                    int sx = x * TerrainSimulation.Subdivision + half;
                    int sy = y * TerrainSimulation.Subdivision + half;

                    height[i] = Mathf.Clamp(
                        Mathf.RoundToInt(simulation.HeightAt(sx, sy) / heightStep),
                        0,
                        maxLevel);
                }
            }
        }
    }
}
