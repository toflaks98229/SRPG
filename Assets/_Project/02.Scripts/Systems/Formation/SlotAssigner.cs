using System.Collections.Generic;
using UnityEngine;

namespace SRPG.Systems.Formation
{
    /// <summary>
    /// 병사와 진형 슬롯을 짝지어 줍니다.
    ///
    /// <b>왜 순서대로 나눠 주면 안 되는가</b>
    ///
    /// 목록 순서대로 슬롯을 배분하면 병사가 자기 자리까지 <b>가로질러</b> 걸어갑니다.
    /// 앞줄에 서 있던 병사가 목록상 뒤라는 이유로 뒷줄 슬롯을 받으면, 대열을 통과해 뒤로 갑니다.
    /// 서로 엇갈리며 지나가는 그림이 나오고, 그 사이 방어선에 구멍이 생깁니다.
    ///
    /// 더 중요한 것은 <b>진형 복구</b>입니다.
    /// 앞줄 병사가 맞고 밀려나면 그 자리가 빕니다. 순서 배분이면 밀려난 그 병사가
    /// 여전히 같은 슬롯의 주인이라, 혼자 기어서 돌아올 때까지 구멍이 남습니다.
    ///
    /// 가까운 사람에게 주면 <b>빈자리를 뒤에 있던 병사가 즉시 메웁니다.</b>
    /// 밀려난 병사는 자연히 뒤쪽 슬롯을 받아 대열이 저절로 재구성됩니다.
    /// 조사에서 확인한 "빈 공간으로 빨려 들어가듯 전진한다"가 여기서 나옵니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 계산이라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public static class SlotAssigner
    {
        // ====================================================================================================
        // 1. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 각 병사가 맡을 슬롯 인덱스를 정합니다.
        ///
        /// 슬롯을 <b>앞에서부터</b> 훑으며 그 자리에 가장 가까운 남은 병사를 붙입니다.
        /// 슬롯 0번(진형 중심)부터 채우므로 안쪽 자리가 먼저 확정됩니다.
        ///
        /// 최적 배정(헝가리안)이 아니라 탐욕적 배정입니다.
        /// 인원이 열 명 남짓이고 매 프레임이 아니라 이따금 다시 짜므로, 차이가 눈에 띄지 않습니다.
        /// </summary>
        /// <param name="positions">병사들의 현재 위치입니다.</param>
        /// <param name="slots">진형 슬롯 위치입니다.</param>
        /// <param name="result">
        /// 병사별 슬롯 인덱스가 채워집니다. 호출 시 비워집니다.
        /// 슬롯이 모자라면 남는 병사에게 마지막 슬롯을 줍니다.
        /// </param>
        /// <param name="pinned">
        /// 슬롯 0번에 고정할 병사의 인덱스입니다. 지휘관 자리에 씁니다. 없으면 -1입니다.
        /// </param>
        public static void AssignNearest(
            IReadOnlyList<Vector3> positions,
            IReadOnlyList<Vector3> slots,
            List<int> result,
            int pinned = -1)
        {
            result.Clear();

            if (positions == null || slots == null || positions.Count == 0 || slots.Count == 0)
            {
                return;
            }

            int unitCount = positions.Count;

            for (int i = 0; i < unitCount; i++)
            {
                result.Add(slots.Count - 1);
            }

            // 이미 자리를 받은 병사를 표시합니다.
            var taken = new bool[unitCount];

            int firstSlot = 0;

            // 지휘관은 진형 중심에 고정합니다. 사방에서 가장 안쪽이라 가장 안전한 자리입니다.
            if (pinned >= 0 && pinned < unitCount)
            {
                result[pinned] = 0;
                taken[pinned] = true;
                firstSlot = 1;
            }

            for (int slot = firstSlot; slot < slots.Count; slot++)
            {
                int best = -1;
                float bestSqr = float.MaxValue;

                for (int unit = 0; unit < unitCount; unit++)
                {
                    if (taken[unit])
                    {
                        continue;
                    }

                    float sqr = (positions[unit] - slots[slot]).sqrMagnitude;
                    if (sqr < bestSqr)
                    {
                        bestSqr = sqr;
                        best = unit;
                    }
                }

                if (best < 0)
                {
                    // 남은 병사가 없습니다. 나머지 슬롯은 비워 둡니다.
                    break;
                }

                result[best] = slot;
                taken[best] = true;
            }
        }
    }
}
