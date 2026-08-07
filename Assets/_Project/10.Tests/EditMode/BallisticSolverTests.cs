using NUnit.Framework;
using SRPG.Common;
using SRPG.Systems.Combat;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 투사체 조준 계산을 검증합니다.
    ///
    /// 조준은 눈으로 확인하기 가장 어려운 부분입니다. 화살이 빗나갔을 때
    /// 산포 때문인지 예측이 틀려서인지 탄도가 잘못됐는지 구분이 안 됩니다.
    /// 그래서 세 요소를 각각 따로 못 박아 둡니다.
    /// </summary>
    public sealed class BallisticSolverTests
    {
        // ====================================================================================================
        // 1. Prediction
        // ====================================================================================================

        [Test]
        public void 정지한_대상은_현재_위치가_요격_지점이다()
        {
            var shooter = Vector3.zero;
            var target = new Vector3(10f, 0f, 0f);

            bool found = BallisticSolver.TryPredictInterceptPoint(
                shooter, target, Vector3.zero, 20f, out var intercept, out float time);

            Assert.IsTrue(found);
            Assert.That(Vector3.Distance(intercept, target), Is.LessThan(0.001f));
            Assert.That(time, Is.EqualTo(0.5f).Within(0.001f), "10m를 20m/s로 가면 0.5초입니다.");
        }

        [Test]
        public void 움직이는_대상은_진행_방향_앞을_겨눈다()
        {
            var shooter = Vector3.zero;
            var target = new Vector3(10f, 0f, 0f);
            var targetVelocity = new Vector3(0f, 0f, 5f); // +Z로 이동

            bool found = BallisticSolver.TryPredictInterceptPoint(
                shooter, target, targetVelocity, 20f, out var intercept, out _);

            Assert.IsTrue(found);
            Assert.Greater(intercept.z, target.z, "대상이 +Z로 가는데 요격 지점이 앞서 있지 않습니다.");
        }

        [Test]
        public void 요격_지점은_실제로_동시에_도달한다()
        {
            // 예측이 맞다면, 화살이 요격 지점까지 가는 시간과 대상이 거기 도착하는 시간이 같아야 합니다.
            var shooter = new Vector3(-3f, 1f, 2f);
            var target = new Vector3(8f, 0f, -4f);
            var targetVelocity = new Vector3(2.5f, 0f, 1.5f);
            const float speed = 18f;

            bool found = BallisticSolver.TryPredictInterceptPoint(
                shooter, target, targetVelocity, speed, out var intercept, out float time);

            Assert.IsTrue(found);

            float arrowTravelTime = Vector3.Distance(shooter, intercept) / speed;
            Assert.That(arrowTravelTime, Is.EqualTo(time).Within(0.01f), "화살 도달 시간과 예측 시간이 다릅니다.");

            Vector3 targetAtImpact = target + targetVelocity * time;
            Assert.That(Vector3.Distance(targetAtImpact, intercept), Is.LessThan(0.01f), "대상이 요격 지점에 없습니다.");
        }

        [Test]
        public void 화살보다_빠른_대상은_요격할_수_없다()
        {
            var shooter = Vector3.zero;
            var target = new Vector3(5f, 0f, 0f);
            var fleeingFast = new Vector3(30f, 0f, 0f); // 화살보다 빠르게 도망

            bool found = BallisticSolver.TryPredictInterceptPoint(
                shooter, target, fleeingFast, 10f, out _, out _);

            Assert.IsFalse(found, "따라잡을 수 없는데 해가 나왔습니다.");
        }

        // ====================================================================================================
        // 2. Launch
        // ====================================================================================================

        [Test]
        public void 중력이_없으면_직선으로_겨눈다()
        {
            var from = Vector3.zero;
            var to = new Vector3(10f, 0f, 0f);

            bool solved = BallisticSolver.TrySolveLaunchVelocity(from, to, 20f, 0f, out var velocity);

            Assert.IsTrue(solved);
            Assert.That(velocity.normalized.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(velocity.magnitude, Is.EqualTo(20f).Within(0.001f));
        }

        [Test]
        public void 포물선_해는_실제로_목표에_도달한다()
        {
            // 구한 발사 속도로 직접 적분해서 정말 목표를 지나가는지 확인합니다.
            var from = new Vector3(0f, 1.2f, 0f);
            var to = new Vector3(12f, 0.5f, 5f);
            const float speed = 22f;
            const float gravity = 9.81f;

            bool solved = BallisticSolver.TrySolveLaunchVelocity(from, to, speed, gravity, out var velocity);
            Assert.IsTrue(solved, "닿을 수 있는 거리인데 해가 없습니다.");
            Assert.That(velocity.magnitude, Is.EqualTo(speed).Within(0.01f), "속력이 지정값과 다릅니다.");

            // 수평 거리로부터 비행 시간을 구해 그 시점의 높이를 확인합니다.
            Vector3 horizontal = new Vector3(to.x - from.x, 0f, to.z - from.z);
            float flatSpeed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
            float flightTime = horizontal.magnitude / flatSpeed;

            float y = from.y + velocity.y * flightTime - 0.5f * gravity * flightTime * flightTime;

            Assert.That(y, Is.EqualTo(to.y).Within(0.05f), $"비행 {flightTime:F2}초 후 높이가 목표와 다릅니다.");
        }

        [Test]
        public void 사거리를_넘으면_해가_없다()
        {
            var from = Vector3.zero;
            var to = new Vector3(500f, 0f, 0f);

            bool solved = BallisticSolver.TrySolveLaunchVelocity(from, to, 15f, 9.81f, out _);

            Assert.IsFalse(solved, "닿을 수 없는 거리인데 해가 나왔습니다.");
        }

        [Test]
        public void 높은_궤도는_낮은_궤도보다_가파르다()
        {
            var from = Vector3.zero;
            var to = new Vector3(14f, 0f, 0f);

            Assert.IsTrue(BallisticSolver.TrySolveLaunchVelocity(from, to, 20f, 9.81f, out var low, useHighArc: false));
            Assert.IsTrue(BallisticSolver.TrySolveLaunchVelocity(from, to, 20f, 9.81f, out var high, useHighArc: true));

            Assert.Greater(high.y, low.y, "높은 궤도의 수직 성분이 더 크지 않습니다.");
        }

        // ====================================================================================================
        // 3. Arcing Fire (곡사)
        // ====================================================================================================

        [Test]
        public void 곡사_해는_실제로_목표에_도달한다()
        {
            var from = new Vector3(0f, 0.7f, 0f);
            var to = new Vector3(9f, 0.5f, 0f);
            const float angle = 40f;
            const float gravity = 9.81f;

            bool solved = BallisticSolver.TrySolveArcingLaunch(from, to, angle, gravity, out var velocity, out float flightTime);
            Assert.IsTrue(solved);

            // 구해진 속도로 직접 적분해 착탄 지점을 확인합니다.
            float x = velocity.x * flightTime;
            float y = from.y + velocity.y * flightTime - 0.5f * gravity * flightTime * flightTime;

            Assert.That(x, Is.EqualTo(to.x).Within(0.02f), "수평 착탄 위치가 목표와 다릅니다.");
            Assert.That(y, Is.EqualTo(to.y).Within(0.02f), "착탄 높이가 목표와 다릅니다.");
        }

        [Test]
        public void 곡사는_지정한_발사각을_지킨다()
        {
            // 곡사의 핵심은 거리와 무관하게 궤도 모양이 일정하다는 것입니다.
            const float angle = 40f;

            foreach (float distance in new[] { 3f, 6f, 9f, 15f, 30f })
            {
                bool solved = BallisticSolver.TrySolveArcingLaunch(
                    Vector3.zero, new Vector3(distance, 0f, 0f), angle, 9.81f, out var velocity, out _);

                Assert.IsTrue(solved, $"{distance}m 에서 해가 없습니다.");

                float actualAngle = Mathf.Atan2(velocity.y, new Vector3(velocity.x, 0f, velocity.z).magnitude) * Mathf.Rad2Deg;
                Assert.That(actualAngle, Is.EqualTo(angle).Within(0.01f), $"{distance}m 에서 발사각이 어긋났습니다.");
            }
        }

        [Test]
        public void 곡사는_거리가_멀수록_더_빠른_속력을_요구한다()
        {
            float previousSpeed = 0f;

            foreach (float distance in new[] { 3f, 6f, 9f, 15f, 30f })
            {
                BallisticSolver.TrySolveArcingLaunch(
                    Vector3.zero, new Vector3(distance, 0f, 0f), 40f, 9.81f, out var velocity, out _);

                Assert.Greater(velocity.magnitude, previousSpeed, $"{distance}m 의 필요 속력이 더 가까운 거리보다 작습니다.");
                previousSpeed = velocity.magnitude;
            }
        }

        [Test]
        public void 궁수_사거리에서_비행_시간이_충분히_길다()
        {
            // 곡사로 바꾼 목적 자체가 "화살이 눈에 보이게 날아가는 것"입니다.
            // 비행 시간이 너무 짧으면 직사와 구분되지 않고, 예측 사격도 의미가 없어집니다.
            BallisticSolver.TrySolveArcingLaunch(
                new Vector3(0f, 0.72f, 0f), new Vector3(9f, 0.5f, 0f), 40f, 9.81f, out _, out float flightTime);

            Assert.Greater(flightTime, 0.7f, $"비행 시간 {flightTime:F2}초는 곡사로 보기에 너무 짧습니다.");
            Assert.Less(flightTime, 2.5f, $"비행 시간 {flightTime:F2}초는 너무 길어 답답합니다.");
        }

        [Test]
        public void 곡사_예측은_움직이는_대상의_앞을_겨눈다()
        {
            var from = new Vector3(0f, 0.7f, 0f);
            var target = new Vector3(9f, 0.5f, 0f);
            var targetVelocity = new Vector3(0f, 0f, 3.5f);

            bool solved = BallisticSolver.TrySolveArcingIntercept(
                from, target, targetVelocity, 40f, 9.81f, 30f, out _, out var aimPoint);

            Assert.IsTrue(solved);
            Assert.Greater(aimPoint.z, target.z + 1f, "긴 비행 시간을 감안한 리드가 거의 없습니다.");
        }

        [Test]
        public void 곡사_예측은_비행_시간과_리드가_일치한다()
        {
            // 반복 수렴이 제대로 되었다면, 최종 조준점까지의 비행 시간 동안
            // 대상이 정확히 그 지점으로 이동해야 합니다.
            var from = new Vector3(0f, 0.7f, 0f);
            var target = new Vector3(8f, 0.5f, -2f);
            var targetVelocity = new Vector3(1.5f, 0f, 3f);

            bool solved = BallisticSolver.TrySolveArcingIntercept(
                from, target, targetVelocity, 40f, 9.81f, 40f, out _, out var aimPoint);

            Assert.IsTrue(solved);

            BallisticSolver.TrySolveArcingLaunch(from, aimPoint, 40f, 9.81f, out _, out float flightTime);
            Vector3 targetAtImpact = target + targetVelocity * flightTime;

            Assert.That(
                Vector3.Distance(targetAtImpact, aimPoint),
                Is.LessThan(0.15f),
                "수렴이 부족합니다. 조준점과 착탄 시점의 대상 위치가 어긋납니다.");
        }

        [Test]
        public void 속력_한계를_넘는_거리는_쏘지_않는다()
        {
            bool solved = BallisticSolver.TrySolveArcingIntercept(
                Vector3.zero, new Vector3(200f, 0f, 0f), Vector3.zero, 40f, 9.81f, maxSpeed: 22f, out _, out _);

            Assert.IsFalse(solved, "속력 한계를 넘는 거리인데 발사 해가 나왔습니다.");
        }

        [Test]
        public void 중력이_없으면_곡사가_성립하지_않는다()
        {
            bool solved = BallisticSolver.TrySolveArcingLaunch(
                Vector3.zero, new Vector3(10f, 0f, 0f), 40f, 0f, out _, out _);

            Assert.IsFalse(solved, "중력 없이 포물선 해가 나왔습니다.");
        }

        [Test]
        public void 발사각보다_높이_있는_목표는_그_각으로_닿지_않는다()
        {
            // 40도로 던져서 도달할 수 있는 높이보다 목표가 높으면 해가 없어야 합니다.
            bool solved = BallisticSolver.TrySolveArcingLaunch(
                Vector3.zero, new Vector3(5f, 20f, 0f), 40f, 9.81f, out _, out _);

            Assert.IsFalse(solved, "발사각선보다 높은 목표에 해가 나왔습니다.");
        }

        // ====================================================================================================
        // 4. Spread
        // ====================================================================================================

        [Test]
        public void 산포는_속력을_바꾸지_않는다()
        {
            // 산포는 방향만 흔들어야 합니다. 속력이 바뀌면 탄도 계산이 무의미해집니다.
            var velocity = new Vector3(0f, 4f, 20f);
            var random = new System.Random(12345);

            for (int i = 0; i < 50; i++)
            {
                var spread = BallisticSolver.ApplySpread(velocity, 8f, random);
                Assert.That(spread.magnitude, Is.EqualTo(velocity.magnitude).Within(0.01f));
            }
        }

        [Test]
        public void 산포는_지정한_각도를_넘지_않는다()
        {
            var velocity = new Vector3(0f, 0f, 20f);
            var random = new System.Random(999);
            const float maxSpread = 7f;

            for (int i = 0; i < 200; i++)
            {
                var spread = BallisticSolver.ApplySpread(velocity, maxSpread, random);
                float angle = Vector3.Angle(velocity, spread);

                Assert.LessOrEqual(angle, maxSpread + 0.01f, $"산포 {angle:F2}도가 상한 {maxSpread}도를 넘었습니다.");
            }
        }

        [Test]
        public void 산포가_0이면_방향이_그대로다()
        {
            var velocity = new Vector3(3f, 1f, 7f);
            var spread = BallisticSolver.ApplySpread(velocity, 0f);

            Assert.That(Vector3.Distance(spread, velocity), Is.LessThan(0.0001f));
        }

        // ====================================================================================================
        // 4. Rank
        // ====================================================================================================

        [Test]
        public void 랭크가_높을수록_산포가_좁아진다()
        {
            const float worst = 9f;
            const float best = 1.6f;

            float previous = float.MaxValue;

            for (int rank = CombatConstants.MinRank; rank <= CombatConstants.MaxRank; rank++)
            {
                float spread = BallisticSolver.GetSpreadForRank(rank, CombatConstants.MaxRank, worst, best);

                Assert.Less(spread, previous, $"랭크 {rank}의 산포가 이전 랭크보다 좁아지지 않았습니다.");
                previous = spread;
            }
        }

        [Test]
        public void 최하_최고_랭크의_산포는_지정값과_같다()
        {
            const float worst = 9f;
            const float best = 1.6f;

            Assert.That(
                BallisticSolver.GetSpreadForRank(CombatConstants.MinRank, CombatConstants.MaxRank, worst, best),
                Is.EqualTo(worst).Within(0.001f));

            Assert.That(
                BallisticSolver.GetSpreadForRank(CombatConstants.MaxRank, CombatConstants.MaxRank, worst, best),
                Is.EqualTo(best).Within(0.001f));
        }
    }
}
