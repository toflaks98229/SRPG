using NUnit.Framework;
using SRPG.Systems.Combat;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 병사가 어디를 볼지의 판단을 검증합니다.
    ///
    /// <b>이 게임에서 시선은 연출이 아니라 규칙입니다.</b>
    /// 무기 판정이 정면 기준이고, 방패도 정면만 막습니다.
    /// 그래서 "대기 중일 때 어디를 보는가"가 곧 상륙을 맞이하는 자세가 됩니다.
    /// </summary>
    public sealed class FacingSolverTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private static readonly Vector3 Origin = Vector3.zero;

        private static FacingRequest Request(
            Vector3? aimPoint = null,
            Vector3? threat = null,
            bool isMoving = false,
            Vector3? steering = null,
            Vector3? idleFacing = null,
            Vector3? position = null)
        {
            return new FacingRequest(
                position ?? Origin,
                aimPoint.HasValue,
                aimPoint ?? Vector3.zero,
                threat.HasValue,
                threat ?? Vector3.zero,
                isMoving,
                steering ?? Vector3.zero,
                idleFacing.HasValue,
                idleFacing ?? Vector3.zero);
        }

        private static void AssertPointsAt(Vector3 expectedDirection, Vector3 facing)
        {
            Vector3 expected = expectedDirection.normalized;

            Assert.AreEqual(expected.x, facing.x, 0.001f, "X 방향이 다릅니다.");
            Assert.AreEqual(expected.z, facing.z, 0.001f, "Z 방향이 다릅니다.");
        }

        // ====================================================================================================
        // 2. 우선순위
        // ====================================================================================================

        [Test]
        public void 표적이_있으면_표적을_본다()
        {
            var request = Request(
                aimPoint: new Vector3(5f, 0f, 0f),
                threat: new Vector3(0f, 0f, -5f),
                isMoving: true,
                steering: new Vector3(0f, 0f, 5f),
                idleFacing: Vector3.left);

            Assert.AreEqual(FacingSource.Target, FacingSolver.Resolve(request, out Vector3 facing));
            AssertPointsAt(Vector3.right, facing);
        }

        [Test]
        public void 표적이_없으면_위협을_본다()
        {
            var request = Request(
                threat: new Vector3(0f, 0f, -5f),
                isMoving: true,
                steering: new Vector3(5f, 0f, 0f));

            Assert.AreEqual(FacingSource.Threat, FacingSolver.Resolve(request, out Vector3 facing));
            AssertPointsAt(Vector3.back, facing);
        }

        [Test]
        public void 표적도_위협도_없고_걷는_중이면_가는_쪽을_본다()
        {
            var request = Request(isMoving: true, steering: new Vector3(0f, 0f, 3f), idleFacing: Vector3.right);

            Assert.AreEqual(FacingSource.Movement, FacingSolver.Resolve(request, out Vector3 facing));
            AssertPointsAt(Vector3.forward, facing);
        }

        /// <summary>
        /// 이게 없으면 대기 중인 병사는 <b>마지막으로 걷던 방향</b>을 그대로 보고 서 있습니다.
        /// 분대가 해안을 등지고 도착하면 전원이 섬 안쪽을 보고 선 채로 상륙을 맞이합니다.
        /// </summary>
        [Test]
        public void 멈춰_서면_분대가_지정한_대기_방향을_본다()
        {
            var request = Request(
                isMoving: false,
                steering: new Vector3(0f, 0f, 3f),
                idleFacing: Vector3.right);

            Assert.AreEqual(FacingSource.Idle, FacingSolver.Resolve(request, out Vector3 facing));
            AssertPointsAt(Vector3.right, facing);
        }

        [Test]
        public void 대기_방향이_없으면_마지막_조향_방향으로_버틴다()
        {
            var request = Request(isMoving: false, steering: new Vector3(0f, 0f, 3f));

            Assert.AreEqual(FacingSource.Movement, FacingSolver.Resolve(request, out Vector3 facing));
            AssertPointsAt(Vector3.forward, facing);
        }

        // ====================================================================================================
        // 3. 방향을 만들 수 없는 경우
        // ====================================================================================================

        [Test]
        public void 볼_곳이_전혀_없으면_돌지_않는다()
        {
            Assert.AreEqual(FacingSource.None, FacingSolver.Resolve(Request(), out Vector3 facing));
            Assert.AreEqual(Vector3.zero, facing);
        }

        /// <summary>
        /// 겨눌 자리가 발밑과 겹치면 회전하지 않고 <b>그대로 멈춥니다.</b>
        ///
        /// 여기서 위협으로 내려가면, 칼을 휘두르는 도중에 몸이 홱 돌아가 헛치게 됩니다.
        /// 무기 판정이 정면 기준이라 그 한 프레임이 곧 빗나감입니다.
        /// </summary>
        [Test]
        public void 겨눌_자리가_겹치면_위협으로_내려가지_않는다()
        {
            var request = Request(
                aimPoint: Origin,
                threat: new Vector3(0f, 0f, -5f),
                isMoving: true,
                steering: new Vector3(5f, 0f, 0f));

            Assert.AreEqual(FacingSource.None, FacingSolver.Resolve(request, out _));
        }

        /// <summary>
        /// 위협은 반대입니다. 돌아볼 여유가 있는 상태이므로 다음 후보로 내려갑니다.
        /// </summary>
        [Test]
        public void 위협이_겹치면_다음_후보로_내려간다()
        {
            var request = Request(
                threat: Origin,
                isMoving: true,
                steering: new Vector3(5f, 0f, 0f));

            Assert.AreEqual(FacingSource.Movement, FacingSolver.Resolve(request, out Vector3 facing));
            AssertPointsAt(Vector3.right, facing);
        }

        // ====================================================================================================
        // 4. 결과의 형태
        // ====================================================================================================

        [Test]
        public void 결과는_정규화된_수평_방향이다()
        {
            var request = Request(aimPoint: new Vector3(9f, 40f, 12f));

            Assert.AreEqual(FacingSource.Target, FacingSolver.Resolve(request, out Vector3 facing));

            Assert.AreEqual(0f, facing.y, 0.0001f, "수직 성분이 남았습니다.");
            Assert.AreEqual(1f, facing.magnitude, 0.001f, "정규화되지 않았습니다.");
        }

        /// <summary>
        /// 높이 차이만 있는 대상은 방향을 만들 수 없습니다.
        /// 수평 성분을 남기지 않으면 <c>LookRotation</c>이 위를 보게 되어 병사가 하늘을 봅니다.
        /// </summary>
        [Test]
        public void 바로_위의_대상으로는_돌지_않는다()
        {
            var request = Request(aimPoint: new Vector3(0f, 10f, 0f));

            Assert.AreEqual(FacingSource.None, FacingSolver.Resolve(request, out _));
        }

        [Test]
        public void 위치가_원점이_아니어도_상대_방향을_돌려준다()
        {
            var request = Request(
                position: new Vector3(10f, 0f, 10f),
                aimPoint: new Vector3(10f, 0f, 16f));

            Assert.AreEqual(FacingSource.Target, FacingSolver.Resolve(request, out Vector3 facing));
            AssertPointsAt(Vector3.forward, facing);
        }
    }
}
