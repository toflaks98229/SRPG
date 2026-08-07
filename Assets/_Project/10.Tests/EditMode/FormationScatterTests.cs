using NUnit.Framework;
using SRPG.Systems.Formation;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 진형 흐트러뜨리기를 검증합니다.
    ///
    /// 이 값이 만족해야 할 성질은 둘입니다.
    ///   · <b>고정</b> — 같은 병사는 언제나 같은 어긋남. 아니면 제자리에서 부들부들 떱니다
    ///   · <b>고르게 퍼짐</b> — 한쪽으로 쏠리면 무리가 통째로 비뚤어져 흐트러진 느낌이 안 납니다
    /// </summary>
    public sealed class FormationScatterTests
    {
        // ====================================================================================================
        // 1. 고정성
        // ====================================================================================================

        /// <summary><b>같은 식별자는 언제나 같은 값을 내야 합니다.</b></summary>
        [Test]
        public void 같은_식별자는_언제나_같은_어긋남을_낸다()
        {
            for (int id = -50; id < 50; id++)
            {
                Vector3 first = FormationScatter.Offset(id, 1f);
                Vector3 second = FormationScatter.Offset(id, 1f);

                Assert.AreEqual(first, second, $"id={id} 에서 값이 달라졌습니다. 병사가 떨게 됩니다.");
            }
        }

        [Test]
        public void 다른_식별자는_다른_어긋남을_낸다()
        {
            var seen = new System.Collections.Generic.HashSet<Vector3>();
            int duplicates = 0;

            for (int id = 0; id < 200; id++)
            {
                if (!seen.Add(FormationScatter.Offset(id, 1f)))
                {
                    duplicates++;
                }
            }

            // 완전한 무충돌을 요구하지는 않지만, 대부분은 서로 달라야 합니다.
            Assert.Less(duplicates, 5, "서로 다른 병사가 같은 자리로 어긋납니다.");
        }

        // ====================================================================================================
        // 2. 범위
        // ====================================================================================================

        [Test]
        public void 어긋남은_지정한_반경을_넘지_않는다()
        {
            for (int id = -200; id < 200; id++)
            {
                Vector3 offset = FormationScatter.Offset(id, 2f);

                Assert.LessOrEqual(offset.magnitude, 2.0001f, $"id={id} 어긋남이 반경을 넘었습니다.");
            }
        }

        [Test]
        public void 어긋남은_수평면에만_생긴다()
        {
            for (int id = 0; id < 100; id++)
            {
                Assert.AreEqual(0f, FormationScatter.Offset(id, 1f).y, 0.0001f, "어긋남이 위아래로 생겼습니다.");
            }
        }

        [Test]
        public void 반경이_0이하면_어긋나지_않는다()
        {
            Assert.AreEqual(Vector3.zero, FormationScatter.Offset(1234, 0f));
            Assert.AreEqual(Vector3.zero, FormationScatter.Offset(1234, -1f));
        }

        // ====================================================================================================
        // 3. 분포
        // ====================================================================================================

        /// <summary>
        /// 한쪽으로 쏠리면 무리가 통째로 비뚤어집니다. 평균이 중심 근처여야 합니다.
        /// </summary>
        [Test]
        public void 어긋남이_한쪽으로_쏠리지_않는다()
        {
            Vector3 sum = Vector3.zero;
            const int Count = 2000;

            for (int id = 0; id < Count; id++)
            {
                sum += FormationScatter.Offset(id, 1f);
            }

            Vector3 average = sum / Count;

            Assert.Less(average.magnitude, 0.08f, $"어긋남이 한쪽으로 쏠렸습니다: {average}");
        }

        /// <summary>
        /// 거리에 제곱근을 씌우지 않으면 중심 근처가 빽빽해집니다.
        /// 원 안에 고르게 퍼지면 절반은 반경의 √0.5 ≈ 0.707 바깥에 있어야 합니다.
        /// </summary>
        [Test]
        public void 중심_근처에_몰리지_않는다()
        {
            int outerHalf = 0;
            const int Count = 2000;

            for (int id = 0; id < Count; id++)
            {
                if (FormationScatter.Offset(id, 1f).magnitude > 0.707f)
                {
                    outerHalf++;
                }
            }

            float ratio = outerHalf / (float)Count;

            Assert.Greater(ratio, 0.4f, $"중심 근처에 몰렸습니다. 바깥 절반 비율 {ratio:F2}");
            Assert.Less(ratio, 0.6f, $"바깥 테두리에 몰렸습니다. 바깥 절반 비율 {ratio:F2}");
        }

        [Test]
        public void 해시는_0과_1_사이를_돌려준다()
        {
            for (int id = -500; id < 500; id++)
            {
                float value = FormationScatter.Hash01(id, 0x9E3779B9u);

                Assert.GreaterOrEqual(value, 0f);
                Assert.LessOrEqual(value, 1f);
            }
        }
    }
}
