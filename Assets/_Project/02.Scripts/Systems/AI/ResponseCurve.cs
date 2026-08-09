using UnityEngine;

namespace SRPG.Systems.AI
{
    /// <summary>응답 곡선의 모양입니다.</summary>
    public enum ResponseCurveType
    {
        /// <summary>직선입니다. 입력이 그대로 반영됩니다.</summary>
        Linear = 0,

        /// <summary>거듭제곱입니다. 지수가 크면 늦게 오르다 급히 오릅니다.</summary>
        Polynomial = 1,

        /// <summary>S자입니다. 가운데에서 급변하고 양 끝에서 완만합니다.</summary>
        Logistic = 2,
    }

    /// <summary>
    /// 0~1 입력을 0~1 유용도로 바꾸는 곡선입니다.
    ///
    /// <b>왜 곡선이 필요한가</b>
    ///
    /// "거리가 가까울수록 좋다"는 것만으로는 부족합니다. <b>얼마나</b> 좋은지가 판단을 가릅니다.
    /// 5m와 10m의 차이는 큰데 50m와 55m의 차이는 사실상 없습니다. 직선으로는 이걸 표현할 수 없습니다.
    ///
    /// 유틸리티 AI에서 고려사항의 성격을 정하는 것이 바로 이 곡선입니다.
    /// 같은 입력이라도 곡선을 바꾸면 "가까운 것만 본다"에서 "웬만하면 다 본다"로 성격이 달라집니다.
    ///
    /// <b>수식</b> (Dave Mark의 IAUS에서 쓰는 형태입니다)
    ///   · 직선   : m·(x − c) + b
    ///   · 거듭제곱: m·(x − c)^k + b
    ///   · 로지스틱: m / (1 + e^(−10k·(x − 0.5 − c))) + b
    ///
    /// 결과는 항상 0~1로 잘립니다. 유틸리티 점수를 곱해 합칠 때 범위를 벗어나면 의미가 깨집니다.
    /// </summary>
    public readonly struct ResponseCurve
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>곡선의 모양입니다.</summary>
        public readonly ResponseCurveType Type;

        /// <summary>기울기(m)입니다. 음수면 감소 곡선이 됩니다.</summary>
        public readonly float Slope;

        /// <summary>지수(k)입니다. 거듭제곱과 로지스틱에서 급경사 정도를 정합니다.</summary>
        public readonly float Exponent;

        /// <summary>가로 이동(c)입니다. 곡선이 꺾이는 지점을 옮깁니다.</summary>
        public readonly float XShift;

        /// <summary>세로 이동(b)입니다. 바닥값을 올립니다.</summary>
        public readonly float YShift;

        // ====================================================================================================
        // 2. Presets
        // ====================================================================================================

        /// <summary>입력이 클수록 선형으로 좋아집니다.</summary>
        public static ResponseCurve Increasing =>
            new ResponseCurve(ResponseCurveType.Linear, 1f, 1f, 0f, 0f);

        /// <summary>입력이 클수록 선형으로 나빠집니다. 거리·위협처럼 낮을수록 좋은 값에 씁니다.</summary>
        public static ResponseCurve Decreasing =>
            new ResponseCurve(ResponseCurveType.Linear, -1f, 1f, 0f, 1f);

        /// <summary>
        /// 가까울 때 차이가 크고 멀어지면 급히 평평해집니다. (1 − x)²
        ///
        /// 거리 고려사항의 기본형입니다. <b>5m와 10m의 차이는 크고 50m와 55m의 차이는 없다</b>를 표현합니다.
        /// 부호를 뒤집은 −x² + 1 과 헷갈리기 쉬운데, 그쪽은 정반대로
        /// 가까운 구간이 평평하고 먼 구간이 가파릅니다.
        /// </summary>
        public static ResponseCurve SharpFalloff =>
            new ResponseCurve(ResponseCurveType.Polynomial, 1f, 2f, 1f, 0f);

        /// <summary>
        /// 임계값을 넘어야 의미가 생깁니다. "방어가 어느 정도 이상 두꺼우면 피한다" 같은 판단에 씁니다.
        /// </summary>
        public static ResponseCurve Threshold =>
            new ResponseCurve(ResponseCurveType.Logistic, 1f, 1.2f, 0f, 0f);

        // ====================================================================================================
        // 3. Constructor
        // ====================================================================================================

        /// <summary>
        /// 곡선을 만듭니다. 대개는 직접 부르지 않고 위의 프리셋을 씁니다.
        /// </summary>
        /// <param name="type">곡선의 모양입니다.</param>
        /// <param name="slope">기울기(m)입니다. 음수면 감소 곡선이 됩니다.</param>
        /// <param name="exponent">지수(k)입니다. 거듭제곱과 로지스틱의 급경사 정도를 정합니다.</param>
        /// <param name="xShift">가로 이동(c)입니다. 곡선이 꺾이는 지점을 옮깁니다.</param>
        /// <param name="yShift">세로 이동(b)입니다. 바닥값을 올립니다.</param>
        public ResponseCurve(ResponseCurveType type, float slope, float exponent, float xShift, float yShift)
        {
            Type = type;
            Slope = slope;
            Exponent = exponent;
            XShift = xShift;
            YShift = yShift;
        }

        // ====================================================================================================
        // 4. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 입력을 유용도로 변환합니다.
        /// </summary>
        /// <param name="input">평가할 값입니다. 0~1 범위를 벗어나면 잘립니다.</param>
        /// <returns>
        /// 0~1로 잘린 유용도입니다. 유틸리티 점수를 곱해 합칠 때
        /// 범위를 벗어나면 의미가 깨지므로 반드시 잘라서 돌려줍니다.
        /// </returns>
        public float Evaluate(float input)
        {
            float x = Mathf.Clamp01(input);
            float y;

            switch (Type)
            {
                case ResponseCurveType.Polynomial:
                {
                    float k = Mathf.Max(0.0001f, Exponent);

                    // 음수의 실수 거듭제곱은 정의되지 않으므로 크기만 취합니다.
                    // 부호는 <see cref="Slope"/>로 정합니다. 그래야 (1 − x)² 처럼
                    // 가로 이동이 큰 곡선도 짝수 지수에서 제대로 나옵니다.
                    y = Slope * Mathf.Pow(Mathf.Abs(x - XShift), k) + YShift;
                    break;
                }

                case ResponseCurveType.Logistic:
                {
                    float k = Mathf.Max(0.0001f, Exponent);
                    y = Slope / (1f + Mathf.Exp(-10f * k * (x - 0.5f - XShift))) + YShift;
                    break;
                }

                default:
                    y = Slope * (x - XShift) + YShift;
                    break;
            }

            return Mathf.Clamp01(y);
        }
    }
}
