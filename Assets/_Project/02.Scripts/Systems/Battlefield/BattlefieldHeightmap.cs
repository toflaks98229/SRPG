using SRPG.Data;
using SRPG.Systems.Deployment;
using UnityEngine;

namespace SRPG.Systems.Battlefield
{
    /// <summary>
    /// 전장의 <b>연속 지형 높이</b>입니다. 유니티 터레인이 그대로 받아 쓰는 형태입니다.
    ///
    /// <b>왜 타일이 아니라 하이트맵인가</b>
    ///
    /// 앞서 타일마다 사각 발판을 굽는 방식으로 만들었습니다. 그러면 아무리 잘게 나눠도
    /// 눈이 격자를 찾아냅니다 — 침식과 마칭 스퀘어까지 동원하고도 그랬습니다.
    ///
    /// 유니티 터레인은 애초에 연속면입니다. 발판도 이음매도 없고,
    /// 렌더링·충돌·높이 질의를 엔진이 맡습니다. 우리가 만들 것은 <b>숫자 격자 하나</b>뿐입니다.
    ///
    /// <b>격자는 사라지지 않습니다</b>
    ///
    /// 길찾기·점유·영향력 맵은 여전히 타일을 씁니다. 다만 이제 타일은
    /// 지형을 <b>만드는 것이 아니라 읽습니다</b> — 높이도 통행 여부도 여기서 나옵니다.
    ///
    /// <b>왜 순수 클래스인가</b>
    ///
    /// 터레인은 씬 오브젝트라 헤드리스 테스트에서 만들 수 없습니다.
    /// 숫자만 따로 떼어 두면 지형이 실제로 어떤 모양인지 씬 없이 검사할 수 있습니다.
    /// </summary>
    public sealed class BattlefieldHeightmap
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// 해수면의 높이입니다. 지형 전체 높이에 대한 비율입니다.
        ///
        /// 이보다 낮은 곳은 물에 잠깁니다. 가장자리를 이 아래로 내려
        /// 상륙정이 접근할 물가를 만듭니다.
        /// </summary>
        public const float SeaLevelRatio = 0.18f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly float[,] _heights;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>한 변의 표본 수입니다. 터레인이 요구하므로 2의 거듭제곱 + 1입니다.</summary>
        public int Resolution { get; }

        /// <summary>전장의 월드 크기입니다. 가로세로가 같습니다.</summary>
        public float WorldSize { get; }

        /// <summary>지형의 최고 높이입니다. 터레인의 세로 크기가 됩니다.</summary>
        public float MaxElevation { get; }

        /// <summary>해수면의 월드 높이입니다.</summary>
        public float SeaLevel => MaxElevation * SeaLevelRatio;

        // ====================================================================================================
        // 4. Constructor
        // ====================================================================================================

        private BattlefieldHeightmap(int resolution, float worldSize, float maxElevation)
        {
            Resolution = resolution;
            WorldSize = worldSize;
            MaxElevation = Mathf.Max(0.01f, maxElevation);

            _heights = new float[resolution, resolution];
        }

        // ====================================================================================================
        // 5. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 지형을 만듭니다.
        /// </summary>
        public static BattlefieldHeightmap Create(BattlefieldSpec spec, BattlefieldProfile profile, float cellSize)
        {
            spec = spec.WithDefaults();

            float worldSize = Mathf.Max(spec.Width, spec.Depth) * cellSize;

            // 터레인 하이트맵은 2의 거듭제곱 + 1 이어야 합니다.
            // 타일보다 촘촘해야 한 칸 안에서도 기복이 보입니다.
            int resolution = NextPowerOfTwoPlusOne(Mathf.Max(spec.Width, spec.Depth) * 4);

            // 해수면 아래로 내려갈 여유를 둡니다. 그래야 물가가 생깁니다.
            float maxElevation = Mathf.Max(0.5f, profile.MaxElevation) / (1f - SeaLevelRatio);

            var map = new BattlefieldHeightmap(resolution, worldSize, maxElevation);

            int seed = spec.Seed != 0 ? spec.Seed : System.Environment.TickCount;
            var random = new System.Random(seed);

            // 정수 격자에서 펄린이 0이 되므로 소수 오프셋을 씁니다.
            float offsetX = (float)random.NextDouble() * 500f + 0.31f;
            float offsetY = (float)random.NextDouble() * 500f + 0.77f;

            // 노이즈의 잘기는 타일 기준으로 정해져 있으므로 표본 밀도에 맞춰 환산합니다.
            float scale = profile.HillScale * Mathf.Max(spec.Width, spec.Depth) / resolution;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float hills = Mathf.PerlinNoise(x * scale + offsetX, y * scale + offsetY);

                    // 가장자리를 물 아래로 끌어내려 해안을 만듭니다.
                    //
                    // 이것이 없으면 전장이 화면 밖으로 잘린 채 끝나 보이고,
                    // 무엇보다 상륙정이 댈 곳이 없습니다.
                    float land = EdgeFalloff(x, y, resolution);

                    // 해수면 위로 들어 올린 뒤 언덕을 얹습니다.
                    float value = Mathf.Lerp(0f, SeaLevelRatio + (1f - SeaLevelRatio) * hills, land);

                    map._heights[y, x] = Mathf.Clamp01(value);
                }
            }

            // 강은 언덕을 다 얹은 뒤에 팝니다. 먼저 파면 노이즈가 물길을 도로 메웁니다.
            //
            // 흐름은 대치 축을 <b>가로지릅니다.</b> 나란히 흐르면 아무것도 가르지 않아
            // 강이 있으나 마나 한 전장이 됩니다.
            if (profile.Kind == TerrainKind.River && profile.RiverWidth > 0f)
            {
                RiverCarver.Carve(
                    map._heights,
                    BattleAxis.ResolveCross(seed),
                    SeaLevelRatio,
                    profile.RiverWidth,
                    profile.RiverDepth,
                    profile.FordCount);
            }

            return map;
        }

        /// <summary>터레인에 그대로 넘길 높이 배열입니다.</summary>
        public float[,] ToTerrainHeights()
        {
            return _heights;
        }

        /// <summary>
        /// 월드 좌표에서의 지면 높이입니다.
        ///
        /// 터레인 없이도 답이 나와야 합니다 — 헤드리스 테스트와 격자 파생이 이것을 씁니다.
        /// 유니티 터레인도 같은 배열을 겹선형으로 읽으므로 결과가 일치합니다.
        /// </summary>
        public float SampleHeight(float worldX, float worldZ, Vector3 origin)
        {
            float u = Mathf.Clamp01((worldX - origin.x) / WorldSize) * (Resolution - 1);
            float v = Mathf.Clamp01((worldZ - origin.z) / WorldSize) * (Resolution - 1);

            int x0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, Resolution - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, Resolution - 1);
            int x1 = Mathf.Min(x0 + 1, Resolution - 1);
            int y1 = Mathf.Min(y0 + 1, Resolution - 1);

            float tx = u - x0;
            float ty = v - y0;

            float bottom = Mathf.Lerp(_heights[y0, x0], _heights[y0, x1], tx);
            float top = Mathf.Lerp(_heights[y1, x0], _heights[y1, x1], tx);

            return Mathf.Lerp(bottom, top, ty) * MaxElevation + origin.y;
        }

        /// <summary>
        /// 월드 좌표에서의 경사입니다. 도 단위입니다.
        ///
        /// <b>이 값이 통행을 정합니다.</b>
        /// 터레인은 연속면이라 어디든 걸을 수 있어 보이므로, 오를 수 있는지는
        /// 기울기가 답해야 합니다.
        /// </summary>
        public float SampleSlopeDegrees(float worldX, float worldZ, Vector3 origin)
        {
            float step = WorldSize / (Resolution - 1);

            float dx = SampleHeight(worldX + step, worldZ, origin) - SampleHeight(worldX - step, worldZ, origin);
            float dz = SampleHeight(worldX, worldZ + step, origin) - SampleHeight(worldX, worldZ - step, origin);

            float gradient = Mathf.Sqrt(dx * dx + dz * dz) / (2f * step);

            return Mathf.Atan(gradient) * Mathf.Rad2Deg;
        }

        // ====================================================================================================
        // 6. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 가장자리로 갈수록 0에 가까워지는 값입니다.
        ///
        /// 사각형 가장자리를 그대로 쓰면 해안이 직선이 됩니다.
        /// 두 축의 감쇠를 곱해 모서리가 둥글게 빠지도록 합니다.
        /// </summary>
        private static float EdgeFalloff(int x, int y, int resolution)
        {
            float half = (resolution - 1) * 0.5f;

            float nx = Mathf.Abs(x - half) / half;
            float ny = Mathf.Abs(y - half) / half;

            // 가장자리 15%를 물로 씁니다.
            float fx = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.7f, 1f, nx));
            float fy = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.7f, 1f, ny));

            return fx * fy;
        }

        private static int NextPowerOfTwoPlusOne(int minimum)
        {
            int size = 32;

            while (size < minimum)
            {
                size *= 2;
            }

            return size + 1;
        }
    }
}
