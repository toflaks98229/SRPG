using UnityEngine;

namespace SRPG.Systems.Formation
{
    /// <summary>
    /// 진형 슬롯을 <b>일부러 흐트러뜨립니다.</b>
    ///
    /// <b>왜 흐트러뜨리는가</b>
    ///
    /// Bad North의 시각적 뼈대는 "질서 대 혼돈"입니다.
    /// 플레이어 부대는 격자 위에서 정연하게 움직이고, 침략자는 그리드를 무시하고
    /// 사방에서 각기 다른 각도로 들이닥칩니다. 그 대비가 곧 "누가 지키고 누가 쳐들어오는가"입니다.
    ///
    /// 그런데 적 분대를 플레이어 분대와 <b>같은 방식으로</b> 만들면 그 대비가 사라집니다.
    /// 같은 동심원, 같은 간격, 같은 경로. 화면에는 정연한 두 무리가 마주 설 뿐입니다.
    ///
    /// 그렇다고 적에게서 진형을 통째로 뺄 수는 없습니다.
    /// 응집이 없으면 무리가 흩어져 하나씩 도착하고, 방어가 너무 쉬워집니다.
    /// <b>뭉치되 줄 서지 않게</b> 하는 것이 필요합니다.
    ///
    /// 그래서 간격을 넓히고 각자에게 고정된 어긋남을 줍니다.
    /// 결과는 여전히 하나의 무리인데 대열로는 읽히지 않습니다.
    ///
    /// <b>어긋남은 매 프레임 흔들리면 안 됩니다.</b>
    /// 무작위를 매번 새로 뽑으면 병사가 제자리에서 부들부들 떱니다.
    /// 대상마다 <b>고정된</b> 값이어야 하므로 식별자에서 해시로 만들어 냅니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 계산이라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public static class FormationScatter
    {
        // ====================================================================================================
        // 1. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 식별자로부터 고정된 어긋남을 만듭니다. 같은 식별자는 언제나 같은 값을 냅니다.
        /// </summary>
        /// <param name="identity">대상의 고정 식별자입니다. 인스턴스 ID 같은 것을 넣습니다.</param>
        /// <param name="radius">어긋날 수 있는 최대 거리입니다.</param>
        /// <returns>XZ 평면 위의 어긋남입니다.</returns>
        public static Vector3 Offset(int identity, float radius)
        {
            if (radius <= 0f)
            {
                return Vector3.zero;
            }

            // 식별자를 두 개의 독립적인 0~1 값으로 흩뜨립니다.
            // 같은 씨앗에서 각도와 거리를 함께 뽑으면 둘이 상관되어 어긋남이 한쪽으로 쏠립니다.
            float angle = Hash01(identity, 0x9E3779B9u) * Mathf.PI * 2f;

            // 거리에 제곱근을 씌워 원 안에 고르게 퍼뜨립니다.
            // 그냥 쓰면 중심 근처가 빽빽해져 흐트러진 느낌이 덜합니다.
            float distance = Mathf.Sqrt(Hash01(identity, 0x85EBCA6Bu)) * radius;

            return new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
        }

        /// <summary>
        /// 정수를 0~1 사이의 값으로 흩뜨립니다.
        /// </summary>
        /// <param name="value">씨앗입니다.</param>
        /// <param name="salt">같은 씨앗에서 서로 다른 값을 뽑기 위한 소금입니다.</param>
        /// <returns>같은 입력이면 언제나 같은 0~1 값입니다.</returns>
        public static float Hash01(int value, uint salt)
        {
            unchecked
            {
                uint hash = (uint)value ^ salt;

                // MurmurHash3 의 최종 혼합 단계입니다. 비트를 고르게 섞습니다.
                hash ^= hash >> 16;
                hash *= 0x85EBCA6Bu;
                hash ^= hash >> 13;
                hash *= 0xC2B2AE35u;
                hash ^= hash >> 16;

                return hash / (float)uint.MaxValue;
            }
        }
    }
}
