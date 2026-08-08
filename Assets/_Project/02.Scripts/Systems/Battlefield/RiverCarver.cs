using UnityEngine;

namespace SRPG.Systems.Battlefield
{
    /// <summary>
    /// 전장을 가로지르는 <b>강</b>을 팝니다.
    ///
    /// <b>왜 강인가</b>
    ///
    /// 이 게임의 주요 사망 수단은 익사입니다. 넉백으로 물에 밀려나면 즉사하고,
    /// 그래서 넉백과 도약이 별도 채널로 나뉘어 있으며, 방패가 막아도 충격량은 전달됩니다.
    /// 설계 전체가 <b>물가가 위험하다</b>는 전제 위에 서 있습니다.
    ///
    /// 그런데 야전으로 오면서 물이 전장 가장자리에만 남았습니다.
    /// 지형은 여전히 섬 모양이고, 물은 아무도 싸우지 않는 테두리에 있습니다.
    /// 익사 규칙이 죽은 것은 아니지만 <b>닿지 않는 곳에</b> 있었습니다.
    ///
    /// 강을 전장 한가운데로 끌어오면 그 설계가 되살아납니다.
    /// 도하 지점에서 밀리면 빠지고, 강을 등지고 싸우는 것이 위험해집니다.
    ///
    /// <b>여울이 이 기능의 생사를 가릅니다</b>
    ///
    /// 강이 전장을 완전히 가르면 두 부대가 <b>영영 만나지 못합니다.</b>
    /// 길찾기는 경로를 못 찾고, 생성기의 고립지 정리가 강 건너편을 통째로 바위로 덮으며,
    /// 그러면 적 전개 구역이 사라집니다. 예외는 하나도 나지 않습니다.
    ///
    /// 그래서 여울은 선택이 아니라 <b>필수</b>입니다. 개수를 0으로 줄일 수 없게 막아 둡니다.
    /// 그리고 여울이 좁기 때문에 그 자리가 자연히 초크포인트가 됩니다 —
    /// 초크 점수는 격자가 이미 통행 가능 이웃 수로 계산하므로 따로 표시할 필요가 없습니다.
    ///
    /// MonoBehaviour에도 ScriptableObject에도 의존하지 않는 순수 계산이라
    /// EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public static class RiverCarver
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>강둑이 강폭의 몇 배까지 퍼지는지입니다. 1이면 절벽처럼 꺾입니다.</summary>
        private const float BankSpread = 1.9f;

        /// <summary>여울 하나가 강을 따라 차지하는 길이의 비율입니다.</summary>
        private const float FordSpan = 0.10f;

        /// <summary>물길이 굽이치는 폭입니다. 전장 크기에 대한 비율입니다.</summary>
        private const float MeanderAmplitude = 0.06f;

        /// <summary>물길이 굽이치는 횟수입니다.</summary>
        private const float MeanderFrequency = 1.7f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 정규화된 높이 배열에 강을 팝니다.
        ///
        /// 배열은 0~1 범위이고, <paramref name="seaLevelRatio"/>보다 낮은 곳이 물이 됩니다.
        /// </summary>
        /// <param name="heights">[y, x] 순서의 정규화 높이입니다. 제자리에서 고쳐집니다.</param>
        /// <param name="flow">강이 흐르는 방향입니다. 대치 축을 가로질러야 의미가 있습니다.</param>
        /// <param name="seaLevelRatio">이보다 낮으면 물입니다.</param>
        /// <param name="widthRatio">강폭입니다. 전장 한 변에 대한 비율입니다.</param>
        /// <param name="depthRatio">강바닥이 해수면보다 얼마나 낮은지입니다.</param>
        /// <param name="fordCount">여울 수입니다. 1 미만이면 1로 봅니다 — 건널 수 없는 강은 전장이 아닙니다.</param>
        public static void Carve(
            float[,] heights,
            Vector3 flow,
            float seaLevelRatio,
            float widthRatio,
            float depthRatio,
            int fordCount)
        {
            // 강폭 0은 "강이 없다"는 뜻입니다.
            // 여기서 최소폭으로 올려 버리면 강이 없어야 할 지형에 실개천이 생기고,
            // 호출부가 0을 막고 있는지에 따라 결과가 달라집니다.
            if (heights == null || widthRatio <= 0f)
            {
                return;
            }

            int resolution = heights.GetLength(0);
            if (resolution < 2)
            {
                return;
            }

            // 건널 수 없는 강은 전장을 두 개로 쪼갤 뿐입니다.
            int fords = Mathf.Max(1, fordCount);

            Vector3 direction = Flatten(flow);
            Vector3 across = new Vector3(-direction.z, 0f, direction.x);

            float halfWidth = Mathf.Max(0.005f, widthRatio * 0.5f);
            float bedRatio = Mathf.Max(0f, seaLevelRatio - Mathf.Max(0.01f, depthRatio));

            // 여울은 물에 잠기지 않을 만큼만 올립니다. 발목이 잠기는 높이면 충분합니다.
            float fordRatio = seaLevelRatio + Mathf.Max(0.01f, depthRatio) * 0.25f;

            float half = (resolution - 1) * 0.5f;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    // 전장 중심을 원점으로 하는 -1~1 좌표입니다.
                    float px = (x - half) / half;
                    float pz = (y - half) / half;

                    // 흐름 방향으로 얼마나 왔는지, 그리고 물길에서 얼마나 벗어났는지.
                    float along = px * direction.x + pz * direction.z;
                    float offset = px * across.x + pz * across.z;

                    // 곧은 강은 수로처럼 보입니다. 굽이를 주면 지형이 됩니다.
                    offset -= Mathf.Sin(along * MeanderFrequency * Mathf.PI) * MeanderAmplitude;

                    float distance = Mathf.Abs(offset);
                    if (distance >= halfWidth * BankSpread)
                    {
                        continue;
                    }

                    // 물길 중앙이 1, 강둑 바깥이 0입니다.
                    float inRiver = 1f - Mathf.InverseLerp(halfWidth, halfWidth * BankSpread, distance);

                    float target = Mathf.Lerp(bedRatio, fordRatio, FordLift(along, fords));

                    heights[y, x] = Mathf.Clamp01(Mathf.Lerp(heights[y, x], target, inRiver));
                }
            }
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 이 지점이 여울인 정도입니다. 1이면 마른 땅, 0이면 강바닥입니다.
        ///
        /// 여울을 흐름 방향으로 고르게 나눠 둡니다. 한쪽에 몰리면
        /// 그 반대편 부대가 전장을 가로질러 돌아와야 합니다.
        /// </summary>
        private static float FordLift(float along, int fordCount)
        {
            // -1~1 을 0~1 로 옮겨 여울 간격을 계산합니다.
            float t = Mathf.Clamp01((along + 1f) * 0.5f);

            float best = 0f;

            for (int i = 0; i < fordCount; i++)
            {
                // 양 끝이 아니라 구간의 가운데에 놓습니다. 가장자리 여울은 쓰이지 않습니다.
                float center = (i + 0.5f) / fordCount;

                float lift = 1f - Mathf.InverseLerp(0f, FordSpan, Mathf.Abs(t - center));

                if (lift > best)
                {
                    best = lift;
                }
            }

            // 가장자리를 부드럽게 해 여울 입구가 절벽이 되지 않게 합니다.
            return Mathf.SmoothStep(0f, 1f, best);
        }

        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;

            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }
    }
}
