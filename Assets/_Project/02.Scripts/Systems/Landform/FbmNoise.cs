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

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

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
