using UnityEngine;

namespace SRPG.Systems.AI
{
    /// <summary>
    /// 여러 고려사항의 점수를 하나로 합칩니다.
    ///
    /// <b>왜 더하지 않고 곱하는가</b>
    ///
    /// 더하면 하나가 0이어도 나머지가 크면 통과합니다. "갈 수 없는 곳인데 가치가 높아서 간다"가 됩니다.
    /// 곱하면 <b>하나라도 0이면 전체가 0</b>이 되어, 결격 사유가 확실히 결격으로 작동합니다.
    ///
    /// <b>왜 보상 계수가 필요한가</b>
    ///
    /// 곱셈에는 부작용이 있습니다. 0.9짜리 고려사항 다섯 개를 곱하면 0.59입니다.
    /// 전부 "꽤 좋다"인데 합계는 "그저 그렇다"가 됩니다. 고려사항을 <b>추가할수록</b> 모든 후보가
    /// 0에 눌려 서로 구분되지 않습니다.
    ///
    /// 그래서 고려사항 수에 비례해 점수를 되살립니다(Dave Mark의 IAUS 보상식).
    /// 순위는 그대로 유지되면서 점수대만 펴지므로, 고려사항을 늘려도 판단이 무뎌지지 않습니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 계산이라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public static class UtilityScorer
    {
        // ====================================================================================================
        // 1. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 고려사항 점수들을 곱하고 보상 계수를 적용합니다.
        /// </summary>
        /// <param name="scores">각 고려사항의 0~1 점수입니다.</param>
        /// <param name="count">실제로 사용할 개수입니다. 배열을 재사용하기 위해 따로 받습니다.</param>
        /// <returns>0~1 최종 점수입니다. 고려사항이 없으면 0입니다.</returns>
        public static float Combine(float[] scores, int count)
        {
            if (scores == null || count <= 0)
            {
                return 0f;
            }

            float product = 1f;

            for (int i = 0; i < count; i++)
            {
                float score = Mathf.Clamp01(scores[i]);

                // 결격 사유는 확실히 결격입니다. 나머지를 계산할 필요가 없습니다.
                if (score <= 0f)
                {
                    return 0f;
                }

                product *= score;
            }

            if (count == 1)
            {
                return product;
            }

            // 고려사항이 많을수록 곱이 눌리는 만큼 되살립니다.
            float modificationFactor = 1f - 1f / count;
            float makeUp = (1f - product) * modificationFactor;

            return Mathf.Clamp01(product + makeUp * product);
        }

        /// <summary>
        /// 가중치를 적용해 합칩니다. 가중치가 낮은 고려사항은 1에 가깝게 끌어올려 영향력을 줄입니다.
        ///
        /// 가중치 0이면 그 고려사항은 점수를 전혀 깎지 않고(항상 1),
        /// 가중치 1이면 원래 점수가 그대로 반영됩니다.
        /// </summary>
        /// <param name="scores">각 고려사항의 0~1 점수입니다.</param>
        /// <param name="weights">각 고려사항의 0~1 가중치입니다.</param>
        /// <param name="count">사용할 개수입니다.</param>
        /// <returns>고려사항 수에 맞춰 보상된 0~1 점수입니다. 하나라도 0이면 전체가 0입니다.</returns>
        public static float CombineWeighted(float[] scores, float[] weights, int count)
        {
            if (scores == null || weights == null || count <= 0)
            {
                return 0f;
            }

            float product = 1f;
            int effective = 0;

            for (int i = 0; i < count; i++)
            {
                float weight = Mathf.Clamp01(weights[i]);
                if (weight <= 0f)
                {
                    continue;
                }

                float score = Mathf.Clamp01(scores[i]);

                // 가중치만큼만 원래 점수 쪽으로 당깁니다.
                float weighted = Mathf.Lerp(1f, score, weight);

                if (weighted <= 0f)
                {
                    return 0f;
                }

                product *= weighted;
                effective++;
            }

            if (effective == 0)
            {
                return 0f;
            }

            if (effective == 1)
            {
                return product;
            }

            float modificationFactor = 1f - 1f / effective;
            float makeUp = (1f - product) * modificationFactor;

            return Mathf.Clamp01(product + makeUp * product);
        }
    }
}
