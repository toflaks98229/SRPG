using NUnit.Framework;
using SRPG.Systems.Combat;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 가상 스프링을 검증합니다.
    ///
    /// 이 스프링의 목적은 무기에 <b>무게</b>를 주는 것입니다.
    /// 보간과 달라야 의미가 있고, 그 차이는 "목표를 지나쳤다 돌아온다"는 성질에 있습니다.
    ///
    /// 그리고 <b>절대 발산하면 안 됩니다.</b> 명시적 적분은 프레임이 한 번 튀는 것만으로
    /// 각속도가 폭발해 창이 미친 듯이 회전합니다. 재현이 어렵고 원인도 찾기 힘든 종류라
    /// 여기서 못 박아 둡니다.
    /// </summary>
    public sealed class SpringDamperTests
    {
        // ====================================================================================================
        // 1. 수렴
        // ====================================================================================================

        [Test]
        public void 시간이_지나면_목표에_수렴한다()
        {
            float value = 0f;
            float velocity = 0f;

            for (int i = 0; i < 600; i++)
            {
                value = SpringDamper.Step(value, 10f, ref velocity, frequency: 2f, damping: 1f, deltaTime: 1f / 60f);
            }

            Assert.AreEqual(10f, value, 0.01f);
            Assert.AreEqual(0f, velocity, 0.05f, "수렴했는데 속도가 남아 있습니다.");
        }

        [Test]
        public void 이미_목표에_있고_정지해_있으면_움직이지_않는다()
        {
            float value = 5f;
            float velocity = 0f;

            value = SpringDamper.Step(value, 5f, ref velocity, 2f, 1f, 1f / 60f);

            Assert.AreEqual(5f, value, 0.0001f);
            Assert.AreEqual(0f, velocity, 0.0001f);
        }

        [Test]
        public void 델타가_0이면_아무것도_바뀌지_않는다()
        {
            float velocity = 3f;
            float value = SpringDamper.Step(1f, 10f, ref velocity, 2f, 1f, 0f);

            Assert.AreEqual(1f, value, 0.0001f);
            Assert.AreEqual(3f, velocity, 0.0001f);
        }

        // ====================================================================================================
        // 2. 무게감 (이것이 보간과 다른 점입니다)
        // ====================================================================================================

        /// <summary>
        /// 감쇠가 낮으면 목표를 지나쳤다 돌아와야 합니다.
        /// <b>이 넘침이 무기의 무게감입니다.</b> 이게 없으면 그냥 느린 보간입니다.
        /// </summary>
        [Test]
        public void 감쇠가_낮으면_목표를_지나친다()
        {
            float value = 0f;
            float velocity = 0f;
            float maximum = 0f;

            for (int i = 0; i < 300; i++)
            {
                value = SpringDamper.Step(value, 10f, ref velocity, frequency: 2f, damping: 0.2f, deltaTime: 1f / 60f);
                maximum = Mathf.Max(maximum, value);
            }

            Assert.Greater(maximum, 10f, "감쇠가 낮은데 목표를 넘어서지 않았습니다. 관성이 없습니다.");
        }

        [Test]
        public void 감쇠가_높으면_지나치지_않는다()
        {
            float value = 0f;
            float velocity = 0f;
            float maximum = 0f;

            for (int i = 0; i < 300; i++)
            {
                value = SpringDamper.Step(value, 10f, ref velocity, frequency: 2f, damping: 1.2f, deltaTime: 1f / 60f);
                maximum = Mathf.Max(maximum, value);
            }

            Assert.LessOrEqual(maximum, 10.001f, "과감쇠인데 목표를 넘어섰습니다.");
        }

        [Test]
        public void 진동수가_클수록_빨리_따라붙는다()
        {
            float slow = 0f, slowVelocity = 0f;
            float fast = 0f, fastVelocity = 0f;

            for (int i = 0; i < 30; i++)
            {
                slow = SpringDamper.Step(slow, 10f, ref slowVelocity, 0.5f, 1f, 1f / 60f);
                fast = SpringDamper.Step(fast, 10f, ref fastVelocity, 4f, 1f, 1f / 60f);
            }

            Assert.Greater(fast, slow, "진동수를 올렸는데 더 빨리 따라붙지 않았습니다.");
        }

        // ====================================================================================================
        // 3. 안정성
        // ====================================================================================================

        /// <summary>
        /// 프레임이 크게 튀어도 발산하면 안 됩니다.
        /// 로딩이나 스파이크 한 번에 창이 폭주하면 원인을 찾기가 대단히 어렵습니다.
        /// </summary>
        [TestCase(0.5f)]
        [TestCase(1f)]
        [TestCase(5f)]
        public void 큰_프레임_간격에서도_발산하지_않는다(float deltaTime)
        {
            float value = 0f;
            float velocity = 0f;

            for (int i = 0; i < 50; i++)
            {
                value = SpringDamper.Step(value, 10f, ref velocity, frequency: 3f, damping: 0.5f, deltaTime: deltaTime);

                Assert.IsFalse(float.IsNaN(value), $"dt={deltaTime} 에서 값이 NaN이 되었습니다.");
                Assert.IsFalse(float.IsInfinity(value), $"dt={deltaTime} 에서 값이 발산했습니다.");
                Assert.Less(Mathf.Abs(value), 1000f, $"dt={deltaTime} 에서 값이 폭주했습니다: {value}");
            }
        }

        // ====================================================================================================
        // 4. 각도 되감기
        // ====================================================================================================

        /// <summary>
        /// 350도에서 10도로 갈 때 먼 쪽으로 340도를 돌면 안 됩니다.
        /// </summary>
        [Test]
        public void 각도는_최단_방향으로_돈다()
        {
            float angle = 350f;
            float velocity = 0f;

            angle = SpringDamper.StepAngle(angle, 10f, ref velocity, frequency: 2f, damping: 1f, deltaTime: 1f / 60f);

            // 짧은 쪽(+20도 방향)으로 움직여야 합니다.
            Assert.Greater(velocity, 0f, "먼 쪽으로 돌고 있습니다.");
            Assert.Greater(angle, 350f, "각도가 최단 방향으로 움직이지 않았습니다.");
        }

        [Test]
        public void 각도도_결국_목표에_수렴한다()
        {
            float angle = 350f;
            float velocity = 0f;

            for (int i = 0; i < 600; i++)
            {
                angle = SpringDamper.StepAngle(angle, 10f, ref velocity, 2f, 1f, 1f / 60f);
            }

            Assert.AreEqual(0f, Mathf.DeltaAngle(angle, 10f), 0.05f);
        }
    }
}
