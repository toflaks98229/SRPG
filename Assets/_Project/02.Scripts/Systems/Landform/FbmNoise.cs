using UnityEngine;

namespace SRPG.Systems.Landform
{
    /// <summary>
    /// 1단계 — 기초 지형. 프랙탈 브라운 운동으로 기복을 만듭니다.
    ///
    /// <b>왜 한 겹으로는 안 되는가</b>
    ///
    /// 펄린 노이즈 한 겹은 매끈한 물결입니다. 자연 지형처럼 보이지 않습니다.
    /// 실제 지형은 <b>모든 축척에서</b> 굴곡이 있습니다 — 멀리서 보면 능선,
    /// 가까이 보면 그 능선 위의 둔덕, 더 가까이 보면 그 둔덕 위의 요철.
    ///
    /// FBM은 그것을 흉내 냅니다. 주파수를 배로 올리면서 진폭을 반으로 줄여 겹칩니다.
    ///
    ///   H(x) = Σ  persistence^i · noise(x · frequency · lacunarity^i)
    ///
    /// 이 자기 유사성이 "축척을 알 수 없는" 인상을 만들고, 그게 곧 자연스러움입니다.
    ///
    /// <b>능선을 만드는 변형</b>
    ///
    /// 그냥 겹치면 둥근 언덕만 나옵니다. 절댓값을 뒤집으면(<c>1 - |n|</c>) 0을 지나는 자리가
    /// 뾰족한 선으로 남아 <b>능선</b>이 됩니다. 침식된 산맥의 인상이 여기서 나옵니다.
    /// 두 방식을 섞어 능선과 완만한 굴곡을 함께 냅니다.
    /// </summary>
    public static class FbmNoise
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>겹치는 층의 수입니다. 늘릴수록 잔 굴곡이 늘지만 금세 눈에 안 보입니다.</summary>
        public const int DefaultOctaves = 4;

        /// <summary>층마다 주파수가 몇 배가 되는지입니다.</summary>
        public const float Lacunarity = 2.03f;

        /// <summary>층마다 진폭이 몇 배가 되는지입니다.</summary>
        public const float Persistence = 0.5f;

        /// <summary>
        /// 기복 한계 중 이 단계가 쓰는 비율입니다. 나머지는 뒤 단계를 위해 남겨 둡니다.
        ///
        /// <b>한계까지 꽉 채우면 침식이 일을 못 합니다.</b>
        /// 기복은 고도 단계를 넘지 못하도록 잘려 있는데, 처음부터 그 한계에 붙여 놓으면
        /// 침식이 옮기려는 흙이 잘려 나가거나 없던 흙이 생깁니다.
        /// 흙의 총량이 보존되지 않으면 지형이 부풀거나 꺼집니다.
        ///
        /// 여유를 남겨 두면 침식과 붕괴가 한계에 닿지 않고 자기 일을 합니다.
        /// </summary>
        public const float AmplitudeHeadroom = 0.62f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 하이트필드 전체에 기초 기복을 씁니다.
        /// </summary>
        /// <param name="field">대상입니다.</param>
        /// <param name="seed">같은 값이면 같은 지형이 나옵니다.</param>
        /// <param name="frequency">
        /// 기본 주파수입니다. 표본 하나당 진행하는 양이라, 클수록 잘게 굴곡집니다.
        ///
        /// 너무 낮으면 굴곡의 파장이 타일 여러 칸을 덮어 <b>한 칸 안에서는 거의 평면</b>이 됩니다.
        /// 잘게 나눠 놓고도 각진 사각형으로 보이게 되므로, 파장이 타일 한 칸 수준이어야 합니다.
        ///
        /// <b>진폭이 아니라 주파수로 기울기를 법니다.</b>
        /// 기복의 진폭은 고도 한 단계의 절반이라는 상한에 묶여 있습니다 — 그걸 넘으면
        /// 아래층 봉우리가 위층 골보다 높아져 층이 흔들립니다.
        /// 같은 진폭에서 경사를 세우는 방법은 파장을 줄이는 것뿐입니다.
        /// </param>
        /// <param name="ridgeWeight">능선의 비중입니다. 0이면 둥근 언덕만, 1이면 능선만 나옵니다.</param>
        public static void Apply(HeightField field, int seed, float frequency = 0.28f, float ridgeWeight = 0.45f)
        {
            if (field == null)
            {
                return;
            }

            // 시드마다 다른 잡음 영역을 씁니다. 펄린은 정수 격자에서 0이 되므로 소수 오프셋을 씁니다.
            var random = new System.Random(seed);
            float offsetX = (float)random.NextDouble() * 1000f + 0.137f;
            float offsetY = (float)random.NextDouble() * 1000f + 0.731f;

            for (int sy = 0; sy < field.SamplesY; sy++)
            {
                for (int sx = 0; sx < field.SamplesX; sx++)
                {
                    if (!field.IsLand(sx, sy))
                    {
                        continue;
                    }

                    float nx = sx * frequency + offsetX;
                    float ny = sy * frequency + offsetY;

                    float rolling = Rolling(nx, ny, DefaultOctaves);
                    float ridged = Ridged(nx, ny, DefaultOctaves);

                    float value = Mathf.Lerp(rolling, ridged, ridgeWeight);

                    field.SetRelief(sx, sy, value * field.ReliefLimit * AmplitudeHeadroom);
                }
            }
        }

        /// <summary>
        /// 완만한 굴곡입니다. 결과는 -1~1입니다.
        /// </summary>
        public static float Rolling(float x, float y, int octaves)
        {
            float sum = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float normalizer = 0f;

            for (int i = 0; i < octaves; i++)
            {
                // 펄린은 0~1이므로 -1~1로 폅니다.
                sum += (Mathf.PerlinNoise(x * frequency, y * frequency) - 0.5f) * 2f * amplitude;

                normalizer += amplitude;
                amplitude *= Persistence;
                frequency *= Lacunarity;
            }

            return normalizer > 0f ? sum / normalizer : 0f;
        }

        /// <summary>
        /// 능선입니다. 결과는 -1~1입니다.
        ///
        /// 절댓값을 뒤집으면 노이즈가 0을 지나는 자리가 꼭짓점으로 남습니다.
        /// 그 꼭짓점들이 이어져 능선이 됩니다.
        /// </summary>
        public static float Ridged(float x, float y, int octaves)
        {
            float sum = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float normalizer = 0f;

            for (int i = 0; i < octaves; i++)
            {
                float n = (Mathf.PerlinNoise(x * frequency, y * frequency) - 0.5f) * 2f;

                // 1 - |n| 이 능선입니다. 제곱해 능선을 더 날카롭게 세웁니다.
                float ridge = 1f - Mathf.Abs(n);
                sum += ridge * ridge * amplitude;

                normalizer += amplitude;
                amplitude *= Persistence;
                frequency *= Lacunarity;
            }

            if (normalizer <= 0f)
            {
                return 0f;
            }

            // 0~1로 나온 것을 -1~1로 폅니다.
            return (sum / normalizer - 0.5f) * 2f;
        }
    }
}
