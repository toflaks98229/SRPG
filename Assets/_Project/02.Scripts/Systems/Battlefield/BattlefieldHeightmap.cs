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

        /// <summary>
        /// 앞바다 바닥이 <b>싸울 수 있는 땅의 바깥</b>으로 뻗는 폭입니다. 놀이터 한 변에 대한 비율입니다.
        ///
        /// <b>왜 바다 밑에도 지형을 까는가</b>
        ///
        /// 물판은 놀이터보다 넓게 깔립니다. 그런데 지형이 놀이터에서 끝나면 그 바깥에는
        /// 물 뒤에 <b>아무것도 없습니다</b>. 물빛은 화면 깊이(물 표면과 그 뒤 지형의 거리)로 정해지므로,
        /// 뒤가 비면 깊이가 무한대로 튀어 <b>그 한 줄에서 즉시 먼바다</b>가 됩니다.
        /// 지형의 경계가 바다 위에 사각형으로 그어지는 것이 그 결과였습니다.
        ///
        /// 셰이더에서 얼버무릴 수도 있지만, 그러면 "물빛은 물 아래 지형까지의 거리"라는
        /// 이 게임의 규칙에 예외가 하나 생깁니다. 바닥을 실제로 깔면 예외가 필요 없습니다 —
        /// <b>깊이가 이어지므로 색도 이어집니다.</b>
        ///
        /// 0.5 면 사방으로 반 판씩 붙어 지형이 놀이터의 두 배가 됩니다.
        /// 이 값은 표본 간격을 그대로 두고 <b>배열만 두 배로 늘리는</b> 자리라서 상수입니다 —
        /// 어중간한 값을 넣으면 놀이터가 표본 격자에 딱 맞아떨어지지 않습니다.
        /// </summary>
        private const float SeafloorMarginRatio = 0.5f;

        /// <summary>
        /// 앞바다 바닥이 <b>지금까지의 0 높이보다</b> 얼마나 더 내려가는지입니다. 뭍의 높이 범위에 대한 비율입니다.
        ///
        /// 이만큼의 여유를 세로로 새로 만들고, 놀이터는 그 위에 <b>그대로</b> 얹습니다.
        /// 그래서 싸우는 땅의 월드 높이는 이 값과 무관하게 예전과 같습니다.
        /// </summary>
        private const float SeafloorDepthRatio = 1.6f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        /// <summary>정규화된 높이 배열입니다. 유니티 터레인이 쓰는 것과 같은 형식이라 그대로 넘길 수 있습니다.</summary>
        private readonly float[,] _heights;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>한 변의 표본 수입니다. 터레인이 요구하므로 2의 거듭제곱 + 1입니다.</summary>
        public int Resolution { get; }

        /// <summary>지형 전체의 월드 크기입니다. 앞바다 바닥을 포함합니다. 가로세로가 같습니다.</summary>
        public float WorldSize { get; }

        /// <summary>
        /// <b>싸울 수 있는 땅</b>의 월드 크기입니다. 격자가 덮는 범위와 같습니다.
        /// 지형은 이보다 넓고, 그 차이가 앞바다 바닥입니다.
        /// </summary>
        public float PlayWorldSize { get; }

        /// <summary>지형의 구석에서 놀이터의 구석까지의 거리입니다. 사방이 같습니다.</summary>
        public float PlayOffset => (WorldSize - PlayWorldSize) * 0.5f;

        /// <summary>지형의 최고 높이입니다. 터레인의 세로 크기가 됩니다.</summary>
        public float MaxElevation { get; }

        /// <summary>
        /// 해수면의 월드 높이입니다.
        ///
        /// <b>비율이 아니라 값으로 들고 있습니다.</b> 앞바다 바닥만큼 아래쪽 여유가 생기면서
        /// 해수면이 더 이상 전체 높이의 <see cref="SeaLevelRatio"/> 지점이 아니게 되었습니다.
        /// 식을 남겨 두면 물가와 통행 판정이 조용히 어긋납니다.
        /// </summary>
        public float SeaLevel { get; }

        // ====================================================================================================
        // 4. Constructor
        // ====================================================================================================

        private BattlefieldHeightmap(
            int resolution, float worldSize, float playWorldSize, float maxElevation, float seaLevel)
        {
            Resolution = resolution;
            WorldSize = worldSize;
            PlayWorldSize = playWorldSize;
            MaxElevation = Mathf.Max(0.01f, maxElevation);
            SeaLevel = seaLevel;

            _heights = new float[resolution, resolution];
        }

        // ====================================================================================================
        // 5. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 지형을 만듭니다.
        /// </summary>
        /// <param name="spec">이번 판의 크기와 시드입니다.</param>
        /// <param name="profile">지형의 성격입니다. 기복과 등반 한계를 여기서 읽습니다.</param>
        /// <param name="cellSize">타일 한 칸의 월드 크기입니다. 경사 계산의 기준이 됩니다.</param>
        /// <returns>완성된 높이 지도입니다.</returns>
        public static BattlefieldHeightmap Create(BattlefieldSpec spec, BattlefieldProfile profile, float cellSize)
        {
            spec = spec.WithDefaults();

            float playWorldSize = Mathf.Max(spec.Width, spec.Depth) * cellSize;

            // 터레인 하이트맵은 2의 거듭제곱 + 1 이어야 합니다.
            // 타일보다 촘촘해야 한 칸 안에서도 기복이 보입니다.
            int playResolution = NextPowerOfTwoPlusOne(Mathf.Max(spec.Width, spec.Depth) * 4);

            // 뭍의 높이 범위입니다. 해수면 아래로 내려갈 여유를 둡니다 — 그래야 물가가 생깁니다.
            float landRange = Mathf.Max(0.5f, profile.MaxElevation) / (1f - SeaLevelRatio);

            // ------------------------------------------------------------------------------------
            // 1. 싸울 수 있는 땅을 <b>예전과 똑같이</b> 만듭니다.
            //
            // 앞바다 바닥은 이 배열을 <b>건드리지 않습니다</b>. 그래서 강 파기도 가장자리 감쇠도
            // 그대로이고, 같은 시드는 예전과 같은 전장을 냅니다.
            // ------------------------------------------------------------------------------------
            var play = new float[playResolution, playResolution];

            int seed = spec.Seed != 0 ? spec.Seed : System.Environment.TickCount;
            var random = new System.Random(seed);

            // 정수 격자에서 펄린이 0이 되므로 소수 오프셋을 씁니다.
            float offsetX = (float)random.NextDouble() * 500f + 0.31f;
            float offsetY = (float)random.NextDouble() * 500f + 0.77f;

            // 노이즈의 잘기는 타일 기준으로 정해져 있으므로 표본 밀도에 맞춰 환산합니다.
            float scale = profile.HillScale * Mathf.Max(spec.Width, spec.Depth) / playResolution;

            for (int y = 0; y < playResolution; y++)
            {
                for (int x = 0; x < playResolution; x++)
                {
                    float hills = Mathf.PerlinNoise(x * scale + offsetX, y * scale + offsetY);

                    // 가장자리를 물 아래로 끌어내려 해안을 만듭니다.
                    //
                    // 이것이 없으면 전장이 화면 밖으로 잘린 채 끝나 보이고,
                    // 무엇보다 상륙할 물가가 없습니다.
                    float land = EdgeFalloff(x, y, playResolution);

                    // 해수면 위로 들어 올린 뒤 언덕을 얹습니다.
                    float value = Mathf.Lerp(0f, SeaLevelRatio + (1f - SeaLevelRatio) * hills, land);

                    play[y, x] = Mathf.Clamp01(value);
                }
            }

            // 강은 언덕을 다 얹은 뒤에 팝니다. 먼저 파면 노이즈가 물길을 도로 메웁니다.
            //
            // 흐름은 대치 축을 <b>가로지릅니다.</b> 나란히 흐르면 아무것도 가르지 않아
            // 강이 있으나 마나 한 전장이 됩니다.
            //
            // <b>놀이터 배열에만 팝니다.</b> 앞바다까지 파면 물길이 바다로 이어지는데,
            // 여울도 함께 딸려 나가 앞바다에 건널 자리가 생깁니다.
            if (profile.Kind == TerrainKind.River && profile.RiverWidth > 0f)
            {
                RiverCarver.Carve(
                    play,
                    BattleAxis.ResolveCross(seed),
                    SeaLevelRatio,
                    profile.RiverWidth,
                    profile.RiverDepth,
                    profile.FordCount);
            }

            return EmbedInSeafloor(play, playResolution, playWorldSize, landRange);
        }

        /// <summary>
        /// 놀이터를 앞바다 바닥 한가운데에 끼워 넣습니다.
        ///
        /// <b>표본을 다시 뜨지 않습니다.</b>
        /// 캔버스를 정확히 두 배로 잡으면 표본 간격이 그대로이므로, 놀이터 배열은
        /// 중앙에 <b>한 칸씩 그대로</b> 옮겨집니다. 보간이 끼면 애써 만든 능선이 뭉개지고
        /// 같은 시드가 다른 전장을 내게 됩니다.
        ///
        /// <b>세로로도 여유를 새로 만듭니다.</b>
        /// 예전 배열의 0(가장 낮은 물가)이 새 배열에서는 <c>baseOffset</c> 이 되고,
        /// 그 아래가 앞바다 바닥 몫입니다. 놀이터는 그 위에 그대로 얹히므로
        /// <b>싸우는 땅의 월드 높이는 바뀌지 않습니다.</b>
        /// </summary>
        /// <param name="play">놀이터의 정규화된 높이입니다.</param>
        /// <param name="playResolution">놀이터 배열 한 변의 표본 수입니다.</param>
        /// <param name="playWorldSize">놀이터의 월드 크기입니다.</param>
        /// <param name="landRange">뭍의 높이 범위입니다. 예전의 <c>MaxElevation</c> 과 같습니다.</param>
        /// <returns>앞바다 바닥까지 포함한 높이 지도입니다.</returns>
        private static BattlefieldHeightmap EmbedInSeafloor(
            float[,] play, int playResolution, float playWorldSize, float landRange)
        {
            // 여백은 사방으로 놀이터의 절반씩입니다. 그래서 전체가 정확히 두 배가 되고,
            // 표본 수도 2의 거듭제곱 + 1 을 유지합니다. (129 → 257)
            int margin = (playResolution - 1) / 2;
            int resolution = (playResolution - 1) * 2 + 1;

            float worldSize = playWorldSize * (1f + 2f * SeafloorMarginRatio);

            float seafloorDrop = landRange * SeafloorDepthRatio;
            float maxElevation = landRange + seafloorDrop;

            // 예전 배열의 0 이 새 배열에서 놓이는 자리입니다.
            float baseOffset = seafloorDrop / maxElevation;
            float landScale  = landRange / maxElevation;

            var map = new BattlefieldHeightmap(
                resolution,
                worldSize,
                playWorldSize,
                maxElevation,
                seafloorDrop + landRange * SeaLevelRatio);

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int px = x - margin;
                    int py = y - margin;

                    if (px >= 0 && px < playResolution && py >= 0 && py < playResolution)
                    {
                        map._heights[y, x] = baseOffset + play[py, px] * landScale;
                        continue;
                    }

                    // 놀이터 바깥입니다. 경계에서 멀어질수록 바닥이 내려갑니다.
                    //
                    // 거리를 <b>유클리드로</b> 잽니다. 축마다 따로 재면 등심선이 사각형을 그려,
                    // 애써 지형을 깔고도 바다 위에 네모난 테두리가 다시 보입니다.
                    int dx = Mathf.Max(0, Mathf.Max(-px, px - (playResolution - 1)));
                    int dy = Mathf.Max(0, Mathf.Max(-py, py - (playResolution - 1)));

                    float distance = Mathf.Sqrt(dx * dx + dy * dy) / Mathf.Max(margin, 1);

                    // 물가에서 천천히 시작해 앞바다에서 바닥에 닿습니다.
                    // 선형으로 두면 경계에서 기울기가 꺾여 그 선이 그대로 보입니다.
                    float offshore = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance));

                    map._heights[y, x] = baseOffset * (1f - offshore);
                }
            }

            return map;
        }

        /// <summary>터레인에 넘길 높이 배열을 반환합니다.</summary>
        /// <returns>0~1로 정규화된 높이 배열입니다. 내부 배열을 그대로 돌려주므로 수정하면 안 됩니다.</returns>
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
        /// <param name="worldX">월드 X 좌표입니다.</param>
        /// <param name="worldZ">월드 Z 좌표입니다.</param>
        /// <param name="origin">지형의 원점입니다. 월드 좌표를 배열 좌표로 옮기는 기준입니다.</param>
        /// <returns>그 자리의 지면 높이입니다. 월드 단위입니다.</returns>
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
        /// <param name="worldX">월드 X 좌표입니다.</param>
        /// <param name="worldZ">월드 Z 좌표입니다.</param>
        /// <param name="origin">지형의 원점입니다.</param>
        /// <returns>그 자리의 경사입니다. 도 단위이며 0이면 평지입니다.</returns>
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
