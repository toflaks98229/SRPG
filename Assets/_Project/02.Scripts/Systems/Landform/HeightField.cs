using SRPG.Common;
using UnityEngine;

namespace SRPG.Systems.Landform
{
    /// <summary>
    /// 완성된 지형입니다. 침식 시뮬레이션의 결과를 게임이 쓸 수 있게 다듬어 담습니다.
    ///
    /// <b>순서가 뒤집혔습니다</b>
    ///
    /// 예전에는 타일의 고도 단계가 먼저 있고, 그 안에서 흔들 수 있는 <b>기복</b>을 담았습니다.
    /// 그래서 침식이 움직일 폭이 한 단의 절반으로 묶였고, 골짜기가 파일 수 없었으며,
    /// 단 경계는 타일 격자를 따라 90도로 꺾인 채 남았습니다.
    ///
    /// 이제는 반대입니다. <see cref="TerrainSimulation"/>이 물과 중력으로 지형을 먼저 만들고,
    /// 여기서 그 <b>연속된 높이</b>를 그대로 들고 있습니다.
    /// 고도 단계는 그 높이를 <b>읽어서</b> 정합니다 — 그래서 단 경계가 자동으로 등고선이 됩니다.
    /// 타일 격자와는 아무 상관이 없습니다.
    ///
    /// <b>무엇이 게임 규칙이고 무엇이 보이는 땅인가</b>
    ///
    ///   · <see cref="GetLevel"/>  — 고도 단계. 통행 판정이 참고합니다.
    ///   · <see cref="GetSurface"/> — 실제로 그려지고 유닛이 딛는 높이입니다.
    ///
    /// 둘은 다릅니다. 단 위의 평지에서는 거의 같고, 사면에서는 표면이 단 사이를 지나갑니다.
    /// 그 어긋남이 곧 자연스러운 비탈입니다.
    /// </summary>
    public sealed class HeightField
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        private readonly float[] _surface;
        private readonly int[] _level;
        private readonly bool[] _land;
        private readonly float[] _slope;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>타일 한 변을 몇 조각으로 나눴는지입니다.</summary>
        public int Resolution { get; }

        /// <summary>가로 표본 수입니다.</summary>
        public int SamplesX { get; }

        /// <summary>세로 표본 수입니다.</summary>
        public int SamplesY { get; }

        /// <summary>표본 사이의 월드 거리입니다.</summary>
        public float Spacing { get; }

        /// <summary>(0,0) 표본의 월드 좌표입니다.</summary>
        public Vector3 Origin { get; }

        /// <summary>고도 한 단계의 월드 높이입니다.</summary>
        public float HeightStep { get; }

        // ====================================================================================================
        // 3. Constructor
        // ====================================================================================================

        /// <summary>
        /// 시뮬레이션 결과를 받아 지형을 완성합니다.
        /// </summary>
        public HeightField(TerrainSimulation simulation, Vector3 origin, float heightStep, int maxLevel)
        {
            Resolution = TerrainSimulation.Subdivision;

            SamplesX = simulation.Width;
            SamplesY = simulation.Depth;
            Spacing = simulation.Spacing;
            Origin = origin;
            HeightStep = heightStep;

            int count = SamplesX * SamplesY;

            _surface = new float[count];
            _level = new int[count];
            _land = new bool[count];
            _slope = new float[count];

            System.Array.Copy(simulation.Height, _surface, count);
            System.Array.Copy(simulation.Land, _land, count);

            // 딛을 수 있는 평지를 만듭니다. 가파른 곳은 침식이 남긴 사면을 그대로 둡니다.
            TerrainFlattening.Apply(_surface, _level, _land, SamplesX, SamplesY, Spacing, heightStep, maxLevel);

            CacheSlopes();
        }

        // ====================================================================================================
        // 4. Public Methods - Sample Access
        // ====================================================================================================

        public bool IsInside(int sx, int sy)
        {
            return sx >= 0 && sx < SamplesX && sy >= 0 && sy < SamplesY;
        }

        public int Index(int sx, int sy) => sy * SamplesX + sx;

        /// <summary>표본이 육지 위인지 여부입니다.</summary>
        public bool IsLand(int sx, int sy)
        {
            return IsInside(sx, sy) && _land[Index(sx, sy)];
        }

        /// <summary>실제로 그려지고 유닛이 딛는 높이입니다.</summary>
        public float GetSurface(int sx, int sy)
        {
            return IsInside(sx, sy) ? _surface[Index(sx, sy)] : 0f;
        }

        /// <summary>
        /// 표본의 고도 단계입니다. 침식된 높이에서 읽어 낸 값이라 타일 격자와 무관합니다.
        /// </summary>
        public int GetLevel(int sx, int sy)
        {
            return IsInside(sx, sy) ? _level[Index(sx, sy)] : 0;
        }

        /// <summary>
        /// 표본의 경사입니다. tan(경사각)입니다.
        ///
        /// <b>이 값이 딛을 수 있는지를 정합니다.</b>
        /// 예전에는 타일 종류가 정했지만, 이제 지형이 연속이라 타일 안에서도 자리마다 다릅니다.
        /// 완만하면 잔디, 가파르면 드러난 암반입니다 — 보이는 것과 걸을 수 있는 것이 같아집니다.
        /// </summary>
        public float GetSlope(int sx, int sy)
        {
            return IsInside(sx, sy) ? _slope[Index(sx, sy)] : 0f;
        }

        // ====================================================================================================
        // 5. Public Methods - World Sampling
        // ====================================================================================================

        /// <summary>
        /// 월드 좌표에서의 지표면 높이입니다. 네 표본을 겹선형으로 섞습니다.
        ///
        /// 메시가 이 값으로 그려지므로 유닛의 발 높이도 여기서 읽어야 합니다.
        /// 출처가 다르면 유닛이 땅에 박히거나 뜹니다.
        /// </summary>
        public float SampleSurface(float worldX, float worldZ)
        {
            float fx = (worldX - Origin.x) / Spacing;
            float fy = (worldZ - Origin.z) / Spacing;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, SamplesX - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, SamplesY - 1);
            int x1 = Mathf.Min(x0 + 1, SamplesX - 1);
            int y1 = Mathf.Min(y0 + 1, SamplesY - 1);

            float tx = Mathf.Clamp01(fx - x0);
            float ty = Mathf.Clamp01(fy - y0);

            float bottom = Mathf.Lerp(GetSurface(x0, y0), GetSurface(x1, y0), tx);
            float top = Mathf.Lerp(GetSurface(x0, y1), GetSurface(x1, y1), tx);

            return Mathf.Lerp(bottom, top, ty);
        }

        /// <summary>
        /// 월드 좌표에서의 지표면 법선입니다. 중앙 차분으로 구합니다.
        ///
        /// 지형지물을 여기 맞춰 세우면 비탈에 선 바위가 비탈을 따라 기웁니다.
        /// </summary>
        public Vector3 SampleNormal(float worldX, float worldZ)
        {
            float step = Spacing;

            float dx = SampleSurface(worldX + step, worldZ) - SampleSurface(worldX - step, worldZ);
            float dz = SampleSurface(worldX, worldZ + step) - SampleSurface(worldX, worldZ - step);

            return new Vector3(-dx, 2f * step, -dz).normalized;
        }

        /// <summary>월드 좌표에서의 경사입니다. tan(경사각)입니다.</summary>
        public float SampleSlope(float worldX, float worldZ)
        {
            int sx = Mathf.RoundToInt((worldX - Origin.x) / Spacing);
            int sy = Mathf.RoundToInt((worldZ - Origin.z) / Spacing);

            return GetSlope(sx, sy);
        }

        /// <summary>
        /// 이 자리가 <b>무너져 쌓인 비탈의 발치</b>인지를 0~1로 냅니다.
        ///
        /// 조건은 둘입니다 — 여기는 완만한데 <b>바로 위에 가파른 면이 있다</b>.
        /// 그것이 애추(talus)의 정의이고, 실제로 돌이 굴러떨어져 모이는 자리입니다.
        ///
        /// "절벽에 인접하면 돌을 놓는다" 같은 손규칙이 아니라 지형에서 읽어 낸 값이므로,
        /// 잔해가 <b>왜 거기 있는지</b>를 지형이 설명하게 됩니다.
        /// </summary>
        public float SampleTalus(float worldX, float worldZ)
        {
            int sx = Mathf.RoundToInt((worldX - Origin.x) / Spacing);
            int sy = Mathf.RoundToInt((worldZ - Origin.z) / Spacing);

            if (!IsLand(sx, sy) || GetSlope(sx, sy) > 0.5f)
            {
                // 여기가 이미 가파르면 발치가 아니라 사면 자체입니다.
                return 0f;
            }

            float here = GetSurface(sx, sy);
            float strongest = 0f;

            for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
            {
                int nx = sx + GridCoord.Neighbors8[n].X * 2;
                int ny = sy + GridCoord.Neighbors8[n].Y * 2;

                if (!IsLand(nx, ny) || GetSurface(nx, ny) <= here)
                {
                    continue;
                }

                // 위쪽 이웃이 가파를수록, 그리고 높이 차가 클수록 잔해가 많습니다.
                float above = Mathf.InverseLerp(0.5f, 1.2f, GetSlope(nx, ny));
                float rise = Mathf.InverseLerp(0f, HeightStep, GetSurface(nx, ny) - here);

                strongest = Mathf.Max(strongest, above * rise);
            }

            return strongest;
        }

        // ====================================================================================================
        // 6. Public Methods - Conversion
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
        // 7. Private Methods
        // ====================================================================================================

        private void CacheSlopes()
        {
            for (int sy = 0; sy < SamplesY; sy++)
            {
                for (int sx = 0; sx < SamplesX; sx++)
                {
                    int i = Index(sx, sy);

                    _slope[i] = _land[i]
                        ? TerrainFlattening.SlopeAt(_surface, _land, SamplesX, SamplesY, Spacing, sx, sy)
                        : 0f;
                }
            }
        }
    }
}
