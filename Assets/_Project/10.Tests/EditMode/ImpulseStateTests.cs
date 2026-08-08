using NUnit.Framework;
using SRPG.Systems.Motion;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 외력 채널을 검증합니다.
    ///
    /// <b>이 파일의 존재 이유는 넉백과 도약을 나눈 규칙입니다.</b>
    ///
    /// 밀려서 물에 빠지는 것은 사고지만, 달려들다 빠지는 것은 자살입니다.
    /// 두 힘을 한 채널에 합치면 검병이 물가의 적에게 달려들 때마다 스스로 익사합니다.
    /// 겉으로는 "가끔 검병이 물에 빠진다"로만 보이는 종류의 고장입니다.
    /// </summary>
    public sealed class ImpulseStateTests
    {
        // ====================================================================================================
        // 1. 채널 분리 — 익사 규칙의 근거
        // ====================================================================================================

        [Test]
        public void 익사_판정은_넉백만_본다()
        {
            var impulses = new ImpulseState();

            impulses.AddLunge(new Vector3(10f, 0f, 0f));

            Assert.IsFalse(
                impulses.IsPushedFasterThan(1.2f),
                "스스로 달려드는 힘으로 익사 판정이 켜졌습니다. 검병이 물가의 적에게 뛰어들 때마다 죽습니다.");
        }

        [Test]
        public void 세게_밀리면_익사_판정이_켜진다()
        {
            var impulses = new ImpulseState();

            impulses.AddKnockback(new Vector3(0f, 0f, -5f));

            Assert.IsTrue(impulses.IsPushedFasterThan(1.2f));
        }

        [Test]
        public void 문턱보다_약하게_밀리면_익사하지_않는다()
        {
            var impulses = new ImpulseState();

            impulses.AddKnockback(new Vector3(0.5f, 0f, 0f));

            Assert.IsFalse(
                impulses.IsPushedFasterThan(1.2f),
                "잔여 넉백만으로 익사하면 물가에 선 병사가 스치듯 맞기만 해도 빠져 죽습니다.");
        }

        [Test]
        public void 두_채널은_합쳐져_이동에_적용된다()
        {
            var impulses = new ImpulseState();

            impulses.AddKnockback(new Vector3(1f, 0f, 0f));
            impulses.AddLunge(new Vector3(0f, 0f, 2f));

            Vector3 total = impulses.CombineWith(new Vector3(0.5f, 0f, 0f));

            Assert.AreEqual(1.5f, total.x, 0.001f);
            Assert.AreEqual(2f, total.z, 0.001f);
        }

        // ====================================================================================================
        // 2. 수평 제약
        // ====================================================================================================

        /// <summary>
        /// 위에서 내리꽂힌 화살이 유닛을 땅으로 박을 수는 없습니다.
        /// </summary>
        [Test]
        public void 수직_성분은_버린다()
        {
            var impulses = new ImpulseState();

            impulses.AddKnockback(new Vector3(1f, -9f, 0f));
            impulses.AddLunge(new Vector3(0f, 5f, 1f));

            Assert.AreEqual(0f, impulses.Knockback.y, 0.0001f);
            Assert.AreEqual(0f, impulses.Lunge.y, 0.0001f);
        }

        // ====================================================================================================
        // 3. 감쇠
        // ====================================================================================================

        /// <summary>
        /// 도약은 한 걸음 파고드는 것이지 미끄러지는 것이 아닙니다. 넉백보다 빨리 잦아들어야 합니다.
        /// </summary>
        [Test]
        public void 도약이_넉백보다_빨리_잦아든다()
        {
            var impulses = new ImpulseState();

            impulses.AddKnockback(new Vector3(5f, 0f, 0f));
            impulses.AddLunge(new Vector3(5f, 0f, 0f));

            impulses.Decay(0.1f, knockbackDecay: 11f, lungeDecay: 20f);

            Assert.Less(
                impulses.Lunge.magnitude,
                impulses.Knockback.magnitude,
                "도약이 넉백만큼 오래 남습니다.");
        }

        [Test]
        public void 충분히_시간이_지나면_0이_된다()
        {
            var impulses = new ImpulseState();

            impulses.AddKnockback(new Vector3(5f, 0f, 0f));
            impulses.AddLunge(new Vector3(5f, 0f, 0f));

            for (int i = 0; i < 60; i++)
            {
                impulses.Decay(0.0166f, 11f, 20f);
            }

            Assert.AreEqual(0f, impulses.Knockback.magnitude, 0.001f);
            Assert.AreEqual(0f, impulses.Lunge.magnitude, 0.001f);
        }

        [Test]
        public void 감쇠는_0을_지나쳐_반대로_가지_않는다()
        {
            var impulses = new ImpulseState();

            impulses.AddKnockback(new Vector3(1f, 0f, 0f));

            // 한 번에 다 깎이고도 남을 만큼 큰 감쇠입니다.
            impulses.Decay(1f, knockbackDecay: 999f, lungeDecay: 999f);

            Assert.AreEqual(Vector3.zero, impulses.Knockback);
        }

        // ====================================================================================================
        // 4. 분리 성분
        // ====================================================================================================

        /// <summary>
        /// 계산된 분리를 그대로 쓰면 이웃이 반경에 드나들 때마다 속도가 계단처럼 튑니다.
        /// 한 프레임 만에 목표에 도달하면 안 됩니다.
        /// </summary>
        [Test]
        public void 분리_성분은_한_번에_따라붙지_않는다()
        {
            var impulses = new ImpulseState();
            var target = new Vector3(10f, 0f, 0f);

            impulses.FollowSeparation(target, smoothing: 8f, deltaTime: 0.0166f);

            Assert.Greater(impulses.Separation.x, 0f, "전혀 따라가지 않았습니다.");
            Assert.Less(impulses.Separation.x, target.x * 0.5f, "한 프레임에 너무 많이 따라갔습니다.");
        }

        [Test]
        public void 분리_성분은_결국_목표로_수렴한다()
        {
            var impulses = new ImpulseState();
            var target = new Vector3(10f, 0f, 0f);

            for (int i = 0; i < 120; i++)
            {
                impulses.FollowSeparation(target, smoothing: 8f, deltaTime: 0.0166f);
            }

            Assert.AreEqual(target.x, impulses.Separation.x, 0.05f);
        }

        // ====================================================================================================
        // 5. 초기화
        // ====================================================================================================

        [Test]
        public void 초기화하면_모든_채널이_비워진다()
        {
            var impulses = new ImpulseState();

            impulses.AddKnockback(new Vector3(3f, 0f, 0f));
            impulses.AddLunge(new Vector3(0f, 0f, 3f));
            impulses.FollowSeparation(new Vector3(5f, 0f, 0f), 8f, 0.5f);

            impulses.Reset();

            Assert.AreEqual(Vector3.zero, impulses.Knockback);
            Assert.AreEqual(Vector3.zero, impulses.Lunge);
            Assert.AreEqual(Vector3.zero, impulses.Separation);
        }
    }
}
