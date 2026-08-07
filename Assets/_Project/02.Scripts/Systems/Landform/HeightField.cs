using SRPG.Common;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Systems.Landform
{
    /// <summary>
    /// 타일보다 촘촘한 연속 지형 높이입니다.
    ///
    /// <b>왜 필요한가</b>
    ///
    /// 타일 하나당 평면 쿼드 하나를 굽는 한, 지형은 각진 사각형 계단일 수밖에 없습니다.
    /// 노이즈를 아무리 얹어도 그건 타일의 <b>고도 단계</b>를 흔들 뿐이라 계단이 울퉁불퉁해질 뿐입니다.
    ///
    /// 그래서 타일 안쪽을 <see cref="Resolution"/>×<see cref="Resolution"/>로 더 잘라
    /// 그 격자에 연속 높이를 담습니다. 지형 메시도, 유닛의 발 높이도 여기서 나옵니다.
    ///
    /// <b>고도 단계는 그대로 둡니다</b>
    ///
    /// 이 게임의 판독은 "윗면은 딛는 곳, 측면은 못 딛는 곳"에 걸려 있습니다.
    /// 전부 연속으로 만들면 절벽이 비탈이 되어 그 규칙이 무너집니다.
    ///
    /// 그래서 높이를 둘로 나눠 듭니다.
    ///   · <b>기준 높이</b> — 타일의 고도 단계입니다. 게임 규칙이라 침식도 도로도 건드리지 않습니다.
    ///   · <b>기복</b>      — 그 단계 안에서의 굴곡입니다. 이것만 조각합니다.
    ///
    /// 기복의 진폭은 고도 한 단계보다 훨씬 작게 묶습니다.
    /// 그래야 "이 지점이 몇 층인가"가 눈으로도 계산으로도 흔들리지 않습니다.
    ///
    /// 같은 층끼리는 경계 표본을 공유해 이음매가 없고,
    /// 다른 층끼리는 기준 높이가 달라 자연히 절벽이 섭니다. 벽을 따로 세울 필요가 없습니다.
    /// </summary>
    public sealed class HeightField
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// 기복의 최대 진폭입니다. 고도 한 단계에 대한 비율입니다.
        ///
        /// <b>0.5를 넘으면 안 됩니다.</b> 그 이상이면 아래층의 봉우리가 위층의 골보다 높아져
        /// "여기가 몇 층인가"가 눈으로도 계산으로도 흔들립니다.
        ///
        /// 반대로 너무 작으면 타일 윗면이 사실상 평면이라, 잘게 나눠 놓고도
        /// 각진 사각형 계단으로 보입니다. 0.5 바로 아래까지 씁니다.
        /// </summary>
        public const float MaxReliefRatio = 0.42f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly float[] _relief;
        private readonly float[] _baseHeight;
        private readonly bool[] _land;
        private readonly float[] _talus;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>타일 한 변을 몇 조각으로 나눴는지입니다.</summary>
        public int Resolution { get; }

        /// <summary>가로 표본 수입니다. 타일 경계마다 표본이 하나씩 더 있으므로 +1입니다.</summary>
        public int SamplesX { get; }

        /// <summary>세로 표본 수입니다.</summary>
        public int SamplesY { get; }

        /// <summary>표본 사이의 월드 거리입니다.</summary>
        public float Spacing { get; }

        /// <summary>(0,0) 표본의 월드 좌표입니다.</summary>
        public Vector3 Origin { get; }

        /// <summary>기복의 최대 진폭입니다.</summary>
        public float ReliefLimit { get; }

        // ====================================================================================================
        // 4. Constructor
        // ====================================================================================================

        /// <summary>
        /// 격자에 맞춰 하이트필드를 만듭니다. 기복은 0에서 시작합니다.
        /// </summary>
        public HeightField(IslandGrid grid, int resolution)
        {
            Resolution = Mathf.Clamp(resolution, 1, 16);

            SamplesX = grid.Width * Resolution + 1;
            SamplesY = grid.Depth * Resolution + 1;

            Spacing = grid.CellSize / Resolution;
            Origin = grid.Origin;
            ReliefLimit = grid.HeightStep * MaxReliefRatio;

            int count = SamplesX * SamplesY;

            _relief = new float[count];
            _baseHeight = new float[count];
            _land = new bool[count];
            _talus = new float[count];

            FillBaseFrom(grid);
        }

        // ====================================================================================================
        // 5. Public Methods - Sample Access
        // ====================================================================================================

        public bool IsInside(int sx, int sy)
        {
            return sx >= 0 && sx < SamplesX && sy >= 0 && sy < SamplesY;
        }

        public int Index(int sx, int sy) => sy * SamplesX + sx;

        /// <summary>표본의 기복입니다.</summary>
        public float GetRelief(int sx, int sy)
        {
            return IsInside(sx, sy) ? _relief[Index(sx, sy)] : 0f;
        }

        /// <summary>표본의 기복을 설정합니다. 진폭 한계를 넘지 못합니다.</summary>
        public void SetRelief(int sx, int sy, float value)
        {
            if (!IsInside(sx, sy))
            {
                return;
            }

            _relief[Index(sx, sy)] = Mathf.Clamp(value, -ReliefLimit, ReliefLimit);
        }

        /// <summary>표본의 고도 단계 기준 높이입니다. 조각 대상이 아닙니다.</summary>
        public float GetBase(int sx, int sy)
        {
            return IsInside(sx, sy) ? _baseHeight[Index(sx, sy)] : 0f;
        }

        /// <summary>표본이 육지 위인지 여부입니다.</summary>
        public bool IsLand(int sx, int sy)
        {
            return IsInside(sx, sy) && _land[Index(sx, sy)];
        }

        /// <summary>
        /// 이 표본에 쌓인 붕괴 잔해의 양입니다. 0이면 무너진 적이 없습니다.
        ///
        /// <see cref="CliffCollapse"/>가 씁니다. 지형지물이 이 값을 읽어
        /// <b>실제로 무너진 자리에</b> 무너진 만큼 바위를 놓습니다.
        /// "절벽에 인접하면 돌을 놓는다" 같은 손규칙과 달리, 잔해가 지형을 설명하게 됩니다.
        /// </summary>
        public float GetTalus(int sx, int sy)
        {
            return IsInside(sx, sy) ? _talus[Index(sx, sy)] : 0f;
        }

        /// <summary>붕괴 잔해의 양을 기록합니다.</summary>
        public void SetTalus(int sx, int sy, float value)
        {
            if (IsInside(sx, sy))
            {
                _talus[Index(sx, sy)] = Mathf.Clamp01(value);
            }
        }

        /// <summary>
        /// 월드 좌표에서의 잔해 양입니다. 가장 가까운 표본을 읽습니다.
        ///
        /// 여기는 겹선형이 필요 없습니다. 잔해의 양은 배치 확률을 정하는 값이라
        /// 표본 하나 크기의 계단이 결과에 드러나지 않습니다.
        /// </summary>
        public float SampleTalus(float worldX, float worldZ)
        {
            int sx = Mathf.RoundToInt((worldX - Origin.x) / Spacing);
            int sy = Mathf.RoundToInt((worldZ - Origin.z) / Spacing);

            return GetTalus(sx, sy);
        }

        /// <summary>
        /// 표본의 절대 높이입니다. 도로 탐색과 침식이 보는 값입니다.
        /// </summary>
        public float GetHeight(int sx, int sy)
        {
            if (!IsInside(sx, sy))
            {
                return 0f;
            }

            int i = Index(sx, sy);
            return _baseHeight[i] + _relief[i];
        }

        // ====================================================================================================
        // 6. Public Methods - World Sampling
        // ====================================================================================================

        /// <summary>
        /// 월드 좌표에서의 기복입니다. 네 표본을 겹선형으로 섞습니다.
        ///
        /// 표본을 그대로 반올림해 읽으면 유닛이 표본 경계마다 툭툭 튑니다.
        /// 메시는 삼각형 안을 선형으로 채우므로, 발 높이도 같은 방식으로 읽어야 정확히 맞습니다.
        /// </summary>
        public float SampleRelief(float worldX, float worldZ)
        {
            float fx = (worldX - Origin.x) / Spacing;
            float fy = (worldZ - Origin.z) / Spacing;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, SamplesX - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, SamplesY - 1);
            int x1 = Mathf.Min(x0 + 1, SamplesX - 1);
            int y1 = Mathf.Min(y0 + 1, SamplesY - 1);

            float tx = Mathf.Clamp01(fx - x0);
            float ty = Mathf.Clamp01(fy - y0);

            float bottom = Mathf.Lerp(GetRelief(x0, y0), GetRelief(x1, y0), tx);
            float top = Mathf.Lerp(GetRelief(x0, y1), GetRelief(x1, y1), tx);

            return Mathf.Lerp(bottom, top, ty);
        }

        /// <summary>
        /// 월드 좌표에서의 지표면 법선입니다. 중앙 차분으로 구합니다.
        ///
        /// 지형지물을 여기 맞춰 세우면 비탈에 선 바위가 비탈을 따라 기웁니다.
        /// 전부 수직으로 세우면 경사면에서 즉시 티가 납니다.
        /// </summary>
        public Vector3 SampleNormal(float worldX, float worldZ)
        {
            float step = Spacing;

            float dx = SampleRelief(worldX + step, worldZ) - SampleRelief(worldX - step, worldZ);
            float dz = SampleRelief(worldX, worldZ + step) - SampleRelief(worldX, worldZ - step);

            // 기울기를 법선으로 바꿉니다. 두 배 간격으로 잰 차분이므로 2*step으로 나눕니다.
            return new Vector3(-dx, 2f * step, -dz).normalized;
        }

        // ====================================================================================================
        // 7. Public Methods - Conversion
        // ====================================================================================================

        /// <summary>표본 좌표를 월드 평면 좌표로 바꿉니다.</summary>
        public Vector2 SampleToWorld(int sx, int sy)
        {
            return new Vector2(Origin.x + sx * Spacing, Origin.z + sy * Spacing);
        }

        /// <summary>타일 좌표에 해당하는 표본 격자의 좌하단 인덱스입니다.</summary>
        public GridCoord TileToSample(GridCoord coord)
        {
            return new GridCoord(coord.X * Resolution, coord.Y * Resolution);
        }

        // ====================================================================================================
        // 8. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 표본마다 기준 높이와 육지 여부를 채웁니다.
        ///
        /// 타일 경계에 놓인 표본은 맞닿은 타일이 여럿입니다.
        /// 그중 <b>가장 높은 육지</b>를 따릅니다. 낮은 쪽을 따르면 절벽 위 가장자리가
        /// 아래로 끌려 내려가 벽이 무너져 보입니다.
        /// </summary>
        private void FillBaseFrom(IslandGrid grid)
        {
            for (int sy = 0; sy < SamplesY; sy++)
            {
                for (int sx = 0; sx < SamplesX; sx++)
                {
                    int tileX = sx / Resolution;
                    int tileY = sy / Resolution;

                    // 경계 표본은 앞뒤 타일을 모두 봐야 합니다.
                    bool onEdgeX = sx % Resolution == 0;
                    bool onEdgeY = sy % Resolution == 0;

                    float best = 0f;
                    bool land = false;

                    for (int ox = onEdgeX ? -1 : 0; ox <= 0; ox++)
                    {
                        for (int oy = onEdgeY ? -1 : 0; oy <= 0; oy++)
                        {
                            var tile = grid.GetTile(new GridCoord(tileX + ox, tileY + oy));

                            if (tile == null || tile.IsWater)
                            {
                                continue;
                            }

                            float height = tile.Height * grid.HeightStep;

                            if (!land || height > best)
                            {
                                best = height;
                                land = true;
                            }
                        }
                    }

                    int i = Index(sx, sy);
                    _baseHeight[i] = best;
                    _land[i] = land;
                }
            }
        }
    }
}
