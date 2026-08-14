using NUnit.Framework;
using SRPG.Systems.Combat;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 접근 예측을 검증합니다.
    ///
    /// 창병이 "버티는 병과"로 성립하려면 <b>적이 올 자리를 미리 겨눠야</b> 합니다.
    /// 지금 있는 곳을 겨누면 항상 한 박자 늦고, 그러면 쫓아가지 않는 창병이 늘 뒤늦게 찌릅니다.
    ///
    /// 예측이 틀려도 게임은 그냥 돌아갑니다. 창병이 조금 굼떠 보일 뿐이고,
    /// 그게 버그인지 밸런스인지 구분되지 않습니다. 그래서 성질을 여기서 못 박습니다.
    /// </summary>
    public sealed class AimPredictorTests
    {
        // ====================================================================================================
        // 1. 도착 시간
        // ====================================================================================================

        [Test]
        public void 이미_사거리_안이면_기다릴_시간이_없다()
        {
            bool ok = AimPredictor.TryGetTimeToReach(
                Vector3.zero,
                new Vector3(1f, 0f, 0f),
                Vector3.zero,
                effectiveRange: 2f,
                out float time);

            Assert.IsTrue(ok);
            Assert.AreEqual(0f, time, 0.0001f);
        }

        [Test]
        public void 정면으로_다가오면_거리와_속도로_시간이_나온다()
        {
            // 10m 앞의 적이 초당 2m로 다가옵니다. 사거리 2m까지 남은 8m → 4초.
            bool ok = AimPredictor.TryGetTimeToReach(
                Vector3.zero,
                new Vector3(10f, 0f, 0f),
                new Vector3(-2f, 0f, 0f),
                effectiveRange: 2f,
                out float time);

            Assert.IsTrue(ok);
            Assert.AreEqual(4f, time, 0.001f);
        }

        [Test]
        public void 멀어지는_대상은_도착_시점을_말할_수_없다()
        {
            bool ok = AimPredictor.TryGetTimeToReach(
                Vector3.zero,
                new Vector3(10f, 0f, 0f),
                new Vector3(3f, 0f, 0f),
                effectiveRange: 2f,
                out _);

            Assert.IsFalse(ok, "멀어지는 대상에 도착 시간이 나왔습니다.");
        }

        [Test]
        public void 멈춰_선_대상은_도착_시점을_말할_수_없다()
        {
            bool ok = AimPredictor.TryGetTimeToReach(
                Vector3.zero,
                new Vector3(10f, 0f, 0f),
                Vector3.zero,
                effectiveRange: 2f,
                out _);

            Assert.IsFalse(ok);
        }

        [Test]
        public void 옆으로_지나가는_대상은_도착_시점을_말할_수_없다()
        {
            // 접근 성분이 0입니다. 거리가 줄지 않습니다.
            bool ok = AimPredictor.TryGetTimeToReach(
                Vector3.zero,
                new Vector3(10f, 0f, 0f),
                new Vector3(0f, 0f, 4f),
                effectiveRange: 2f,
                out _);

            Assert.IsFalse(ok);
        }

        [Test]
        public void 비스듬히_다가오면_접근_성분만_센다()
        {
            // 대각선으로 달려오지만, 거리를 줄이는 것은 나를 향한 성분뿐입니다.
            // 속도 (-2, 0, 2) 중 접근 성분은 2m/s, 남은 거리 8m → 4초.
            bool ok = AimPredictor.TryGetTimeToReach(
                Vector3.zero,
                new Vector3(10f, 0f, 0f),
                new Vector3(-2f, 0f, 2f),
                effectiveRange: 2f,
                out float time);

            Assert.IsTrue(ok);
            Assert.AreEqual(4f, time, 0.001f);
        }

        [Test]
        public void 높이_차는_무시한다()
        {
            // 고도 차 하나로 예측이 출렁이면 계단 지형에서 창이 흔들립니다.
            AimPredictor.TryGetTimeToReach(
                Vector3.zero,
                new Vector3(10f, 5f, 0f),
                new Vector3(-2f, 0f, 0f),
                effectiveRange: 2f,
                out float withHeight);

            AimPredictor.TryGetTimeToReach(
                Vector3.zero,
                new Vector3(10f, 0f, 0f),
                new Vector3(-2f, 0f, 0f),
                effectiveRange: 2f,
                out float flat);

            Assert.AreEqual(flat, withHeight, 0.001f);
        }

        // ====================================================================================================
        // 2. 겨눌 지점
        // ====================================================================================================

        /// <summary><b>이 테스트가 창병 예측의 핵심입니다.</b></summary>
        [Test]
        public void 다가오는_적의_앞을_겨눈다()
        {
            var self = Vector3.zero;
            var target = new Vector3(10f, 0f, 0f);
            var velocity = new Vector3(-2f, 0f, 0f);

            Vector3 aim = AimPredictor.PredictApproachPoint(
                self, target, velocity,
                effectiveRange: 2f,
                extraLeadSeconds: 0f,
                maxLeadSeconds: 10f);

            // 4초 뒤 위치 = 10 - 8 = 2. 정확히 사거리 경계입니다.
            Assert.AreEqual(2f, aim.x, 0.01f);
            Assert.Less(aim.x, target.x, "적의 현재 위치보다 앞을 겨누지 않았습니다.");
        }

        [Test]
        public void 준비_동작만큼_더_앞을_겨눈다()
        {
            var self = Vector3.zero;
            var target = new Vector3(10f, 0f, 0f);
            var velocity = new Vector3(-2f, 0f, 0f);

            Vector3 without = AimPredictor.PredictApproachPoint(
                self, target, velocity, 2f, extraLeadSeconds: 0f, maxLeadSeconds: 10f);

            Vector3 with = AimPredictor.PredictApproachPoint(
                self, target, velocity, 2f, extraLeadSeconds: 0.5f, maxLeadSeconds: 10f);

            Assert.Less(with.x, without.x, "준비 동작 보정이 반영되지 않았습니다.");
            Assert.AreEqual(1f, without.x - with.x, 0.01f);
        }

        /// <summary>
        /// 상한이 없으면 거의 멈춘 적을 향해 예측 지점이 지평선까지 날아갑니다.
        /// </summary>
        [Test]
        public void 예측_시간에_상한이_걸린다()
        {
            var self = Vector3.zero;
            var target = new Vector3(100f, 0f, 0f);
            var velocity = new Vector3(-0.1f, 0f, 0f);   // 거의 멈춤 → 도착까지 980초

            Vector3 aim = AimPredictor.PredictApproachPoint(
                self, target, velocity, 2f, extraLeadSeconds: 0f, maxLeadSeconds: 1.2f);

            // 상한 1.2초 × 0.1m/s = 0.12m 만 앞을 봅니다.
            Assert.AreEqual(100f - 0.12f, aim.x, 0.01f);
        }

        [Test]
        public void 멀어지는_적은_준비_동작만큼만_겨눈다()
        {
            var self = Vector3.zero;
            var target = new Vector3(10f, 0f, 0f);
            var velocity = new Vector3(3f, 0f, 0f);

            Vector3 aim = AimPredictor.PredictApproachPoint(
                self, target, velocity, 2f, extraLeadSeconds: 0.2f, maxLeadSeconds: 1.2f);

            // 도착 시점이 없으므로 준비 동작 0.2초분만 반영됩니다.
            Assert.AreEqual(10f + 3f * 0.2f, aim.x, 0.01f);
        }

        [Test]
        public void 멈춰_선_적은_현재_위치를_겨눈다()
        {
            var target = new Vector3(5f, 0f, 3f);

            Vector3 aim = AimPredictor.PredictApproachPoint(
                Vector3.zero, target, Vector3.zero, 2f, extraLeadSeconds: 0.2f, maxLeadSeconds: 1.2f);

            Assert.AreEqual(target.x, aim.x, 0.001f);
            Assert.AreEqual(target.z, aim.z, 0.001f);
        }

        [Test]
        public void 겨눌_지점은_수평면에서만_움직인다()
        {
            var target = new Vector3(10f, 4f, 0f);
            var velocity = new Vector3(-2f, 9f, 0f);   // 수직 성분은 무시되어야 합니다.

            Vector3 aim = AimPredictor.PredictApproachPoint(
                Vector3.zero, target, velocity, 2f, extraLeadSeconds: 0f, maxLeadSeconds: 10f);

            Assert.AreEqual(target.y, aim.y, 0.001f, "예측 지점이 위로 떠올랐습니다.");
        }
    }
}
