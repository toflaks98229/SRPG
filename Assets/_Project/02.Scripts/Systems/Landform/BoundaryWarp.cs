using SRPG.Common;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Systems.Landform
{
    /// <summary>
    /// 단 경계를 타일 격자에서 떼어냅니다.
    ///
    /// <b>무엇이 남아 있었는가</b>
    ///
    /// 기복과 침식과 붕괴를 전부 얹고도 지형이 사각형으로 보였습니다.
    /// 이유는 그 모두가 <b>한 단 안쪽</b>만 건드렸기 때문입니다.
    ///
    /// 실제로 재 보면 타일 폭이 2.0인데 타일 안쪽 높이차는 0.22입니다.
    /// 그 정도 굴곡은 멀리서 보면 평면입니다. 반면 단 경계는 고도 한 단(0.5)만큼
    /// 수직으로 떨어지고, 그 선이 <b>정확히 타일 변을 따라 90도로 꺾입니다.</b>
    /// 눈에 들어오는 것은 그 선입니다. 단을 잘게 나눠도 웨딩케이크는 웨딩케이크입니다.
    ///
    /// <b>도메인 워핑</b>
    ///
    /// 경계선을 직접 옮기려 들면 어느 선을 어디로 밀지 정하는 일이 지저분해집니다.
    /// 대신 <b>조회하는 위치를 휘게</b> 합니다.
    ///
    ///   level(표본) = 타일단계( 표본위치 + 워프(표본위치) )
    ///
    /// 워프가 매끄러운 2차원 잡음이면 경계선이 통째로 구불구불해집니다.
    /// 단계 2인 땅이 이웃 타일 영역으로 반 칸 불거지고 다른 데선 물러납니다.
    /// 없던 단계가 생기지도, 섬이 끊기지도 않습니다 — 있는 값을 다른 자리에서 읽을 뿐입니다.
    ///
    /// <b>게임 규칙은 그대로입니다</b>
    ///
    /// 통행 판정과 길찾기는 여전히 타일을 봅니다. 여기서 바뀌는 것은 <b>그려지는 경계와
    /// 발 높이</b>뿐이고, 그 이동 폭은 반 칸을 넘지 않습니다.
    /// 걸을 수 있던 곳은 계속 걸을 수 있고, 막힌 곳은 계속 막혀 있습니다.
    /// </summary>
    public static class BoundaryWarp
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// 조회 위치를 밀어내는 최대 거리입니다. 타일 크기에 대한 비율입니다.
        ///
        /// 반 칸을 넘기면 경계가 원래 자리에서 너무 멀어져, 보이는 지형과
        /// 걸을 수 있는 지형이 어긋나기 시작합니다.
        /// </summary>
        private const float MaxWarpRatio = 0.42f;

        /// <summary>
        /// 워프 잡음의 주파수입니다. 표본 하나당 진행하는 양입니다.
        ///
        /// 낮아야 합니다. 높으면 경계가 톱니처럼 잘게 떨려 지형이 아니라 잡음으로 보이고,
        /// 이웃한 표본이 서로 멀리 떨어진 타일을 읽어 단차가 벌어집니다.
        /// </summary>
        private const float NoiseFrequency = 0.055f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 표본마다 고도 단계를 다시 읽습니다.
        /// </summary>
        /// <param name="field">지형입니다.</param>
        /// <param name="grid">타일 격자입니다. 단계의 출처입니다.</param>
        /// <param name="seed">같은 값이면 같은 경계가 나옵니다.</param>
        public static void Apply(HeightField field, IslandGrid grid, int seed)
        {
            if (field == null || grid == null)
            {
                return;
            }

            var random = new System.Random(seed);

            // 두 축에 서로 다른 잡음을 씁니다. 같은 것을 쓰면 경계가 대각선으로만 밀립니다.
            float offsetX = (float)random.NextDouble() * 700f + 0.19f;
            float offsetY = (float)random.NextDouble() * 700f + 0.61f;

            float maxWarp = grid.CellSize * MaxWarpRatio;

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    if (!field.IsLand(sx, sy))
                    {
                        continue;
                    }

                    var world = field.SampleToWorld(sx, sy);

                    float warpX = (Mathf.PerlinNoise(sx * NoiseFrequency + offsetX, sy * NoiseFrequency) - 0.5f) * 2f;
                    float warpY = (Mathf.PerlinNoise(sx * NoiseFrequency, sy * NoiseFrequency + offsetY) - 0.5f) * 2f;

                    var probe = new Vector3(
                        world.x + warpX * maxWarp,
                        0f,
                        world.y + warpY * maxWarp);

                    var tile = grid.GetTile(grid.WorldToCoord(probe));

                    // 휘어 나간 자리가 바다이거나 격자 밖이면 원래 단계를 지킵니다.
                    // 억지로 끌어오면 육지가 물속으로 잠깁니다.
                    if (tile != null && !tile.IsWater)
                    {
                        field.SetLevel(sx, sy, tile.Height);
                    }
                }
            }

            Smooth(field);
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 이웃한 표본의 단계 차이를 1 이하로 눌러 둡니다.
        ///
        /// 워프가 매끄러워도 경사가 급한 자리에서는 이웃 표본이 두 단 떨어진 타일을 읽을 수 있습니다.
        /// 그대로 두면 한 표본 폭(0.5)에 두 단(1.0)이 떨어지는 벽이 생겨,
        /// 지형이 갈라진 것처럼 보입니다.
        ///
        /// <b>낮추기만 합니다.</b> 올리면 계곡이 메워집니다.
        /// </summary>
        private static void Smooth(HeightField field)
        {
            bool changed = true;
            int guard = 0;

            while (changed && guard++ < 16)
            {
                changed = false;

                for (int sy = 0; sy < field.SamplesY; sy++)
                {
                    for (int sx = 0; sx < field.SamplesX; sx++)
                    {
                        if (!field.IsLand(sx, sy))
                        {
                            continue;
                        }

                        int here = field.GetLevel(sx, sy);

                        for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                        {
                            int nx = sx + GridCoord.Neighbors4[n].X;
                            int ny = sy + GridCoord.Neighbors4[n].Y;

                            if (!field.IsInside(nx, ny))
                            {
                                continue;
                            }

                            // 바다는 0단으로 봅니다. 물속에서 절벽이 솟으면 안 됩니다.
                            int there = field.IsLand(nx, ny) ? field.GetLevel(nx, ny) : 0;

                            if (here - there > 1)
                            {
                                here = there + 1;
                                field.SetLevel(sx, sy, here);
                                changed = true;
                            }
                        }
                    }
                }
            }
        }
    }
}
