using NUnit.Framework;
using SRPG.Data;
using SRPG.Systems.Combat;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 방패의 방향 판정을 검증합니다.
    ///
    /// 이 파일이 존재하는 이유는 <see cref="발사각을_올려도_평지_사격은_상방으로_판정되지_않는다"/> 하나입니다.
    /// 예전에는 상방 판정 기준이 <c>Arrow</c>의 <c>const 0.78f</c> 였고, 발사각은 별도 에셋 필드였습니다.
    /// 기획자가 발사각을 올리면 <b>오류 하나 없이</b> 방패병이 화살에 무적이 되었습니다.
    /// 컴파일러도 런타임도 잡지 못하는 종류의 고장이라, 테스트만이 유일한 방어선입니다.
    /// </summary>
    public sealed class ShieldSolverTests
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>보병의 기본 방패 저항입니다.</summary>
        private const float ShieldResistance = 0.85f;

        /// <summary>궁수의 기본 발사각입니다.</summary>
        private const float DefaultArc = 40f;

        private const float Margin = BattleTuning.DefaultSteepBlockMarginDegrees;

        // ====================================================================================================
        // 2. Helpers
        // ====================================================================================================

        /// <summary>
        /// 지정한 하강각으로 +Z 방향으로 날아오는 화살의 진행 방향을 만듭니다.
        /// </summary>
        private static Vector3 IncomingAtDescent(float descentDegrees)
        {
            float rad = descentDegrees * Mathf.Deg2Rad;
            return new Vector3(0f, -Mathf.Sin(rad), Mathf.Cos(rad)).normalized;
        }

        /// <summary>+Z를 향해 날아오는 화살을 정면으로 마주 보는 방향입니다.</summary>
        private static Vector3 FacingTheArrow => Vector3.back;

        /// <summary>화살에 등을 보이는 방향입니다.</summary>
        private static Vector3 FacingAway => Vector3.forward;

        /// <summary>화살에 측면을 보이는 방향입니다.</summary>
        private static Vector3 FacingSideways => Vector3.right;

        // ====================================================================================================
        // 3. 상방 판정 기준선
        // ====================================================================================================

        /// <summary>
        /// <b>이 테스트가 이 파일의 존재 이유입니다.</b>
        ///
        /// 발사각을 어떻게 바꾸든, 평지에서 쏜 화살은 상방으로 판정되면 안 됩니다.
        /// 곡사는 대칭 포물선이므로 평지 하강각 = 발사각이고, 기준선은 항상 그보다 위에 있어야 합니다.
        ///
        /// 기준선을 상수로 박아 두면 이 성질이 특정 발사각에서만 성립합니다.
        /// 그것이 정확히 예전에 있던 고장입니다.
        /// </summary>
        [TestCase(25f)]
        [TestCase(40f)]
        [TestCase(55f)]
        [TestCase(70f)]
        public void 발사각을_올려도_평지_사격은_상방으로_판정되지_않는다(float arcAngle)
        {
            // 평지 사격: 하강각이 발사각과 같습니다.
            Vector3 incoming = IncomingAtDescent(arcAngle);

            bool blockedFromAbove = ShieldSolver.IsBlockedFromAbove(incoming, arcAngle, Margin);

            Assert.IsFalse(
                blockedFromAbove,
                $"발사각 {arcAngle}도의 평지 사격이 상방으로 판정되었습니다. " +
                "이러면 방패병에게 화살이 통째로 무력해집니다.");
        }

        [TestCase(25f)]
        [TestCase(40f)]
        [TestCase(55f)]
        public void 평지보다_충분히_가파르면_상방으로_판정된다(float arcAngle)
        {
            // 여유각을 넘어서는 하강각 = 고지대에서 내리꽂은 경우입니다.
            Vector3 incoming = IncomingAtDescent(arcAngle + Margin + 5f);

            Assert.IsTrue(
                ShieldSolver.IsBlockedFromAbove(incoming, arcAngle, Margin),
                $"발사각 {arcAngle}도 기준으로 {arcAngle + Margin + 5f}도 하강이 상방으로 판정되지 않았습니다.");
        }

        [Test]
        public void 기준선은_발사각과_여유각의_사인값이다()
        {
            float threshold = ShieldSolver.GetSteepBlockThreshold(DefaultArc, Margin);
            float expected = Mathf.Sin((DefaultArc + Margin) * Mathf.Deg2Rad);

            Assert.AreEqual(expected, threshold, 0.0001f);
        }

        [Test]
        public void 기본_설정의_기준선은_예전_상수와_사실상_같다()
        {
            // 회귀 방지: 리팩토링으로 밸런스가 바뀌지 않았음을 고정합니다.
            // 예전에 손으로 박아 두었던 값은 0.78 이었습니다.
            float threshold = ShieldSolver.GetSteepBlockThreshold(DefaultArc, Margin);

            Assert.AreEqual(0.78f, threshold, 0.01f);
        }

        [Test]
        public void 발사각이_커지면_기준선도_함께_올라간다()
        {
            float low = ShieldSolver.GetSteepBlockThreshold(30f, Margin);
            float high = ShieldSolver.GetSteepBlockThreshold(50f, Margin);

            Assert.Greater(high, low, "발사각을 올렸는데 상방 기준선이 따라 오르지 않았습니다.");
        }

        [Test]
        public void 기준선은_수직을_넘지_않는다()
        {
            // 발사각 85도 + 여유각이면 90도를 넘습니다. sin이 다시 작아지면 안 됩니다.
            float threshold = ShieldSolver.GetSteepBlockThreshold(85f, Margin);

            Assert.AreEqual(1f, threshold, 0.0001f);
        }

        // ====================================================================================================
        // 4. 정면 판정
        // ====================================================================================================

        [Test]
        public void 정면에서_오는_화살은_막힌다()
        {
            Vector3 incoming = IncomingAtDescent(DefaultArc);

            Assert.IsTrue(ShieldSolver.IsBlockedFromFront(incoming, FacingTheArrow));
        }

        [Test]
        public void 측면에서_오는_화살은_막히지_않는다()
        {
            Vector3 incoming = IncomingAtDescent(DefaultArc);

            Assert.IsFalse(ShieldSolver.IsBlockedFromFront(incoming, FacingSideways));
        }

        [Test]
        public void 후방에서_오는_화살은_막히지_않는다()
        {
            Vector3 incoming = IncomingAtDescent(DefaultArc);

            Assert.IsFalse(ShieldSolver.IsBlockedFromFront(incoming, FacingAway));
        }

        [Test]
        public void 바라보는_방향이_영벡터면_막지_못한다()
        {
            Vector3 incoming = IncomingAtDescent(DefaultArc);

            Assert.IsFalse(ShieldSolver.IsBlockedFromFront(incoming, Vector3.zero));
        }

        // ====================================================================================================
        // 5. 최종 감쇠 계수
        // ====================================================================================================

        [Test]
        public void 방패가_없으면_어느_방향이든_그대로_들어간다()
        {
            Vector3 incoming = IncomingAtDescent(80f);

            float factor = ShieldSolver.ComputeBlockFactor(
                incoming, FacingTheArrow, projectileResistance: 0f, DefaultArc, Margin);

            Assert.AreEqual(1f, factor, 0.0001f);
        }

        [Test]
        public void 정면_피격은_저항만큼_깎인다()
        {
            Vector3 incoming = IncomingAtDescent(DefaultArc);

            float factor = ShieldSolver.ComputeBlockFactor(
                incoming, FacingTheArrow, ShieldResistance, DefaultArc, Margin);

            Assert.AreEqual(1f - ShieldResistance, factor, 0.0001f);
        }

        [Test]
        public void 측면_피격은_방패가_있어도_그대로_들어간다()
        {
            // 평지 사격이므로 상방 판정에도 걸리지 않아야 합니다.
            Vector3 incoming = IncomingAtDescent(DefaultArc);

            float factor = ShieldSolver.ComputeBlockFactor(
                incoming, FacingSideways, ShieldResistance, DefaultArc, Margin);

            Assert.AreEqual(1f, factor, 0.0001f, "측면 피격이 막혔습니다. 궁수의 각도 이점이 사라집니다.");
        }

        [Test]
        public void 고지대에서_측면을_쏴도_상방이면_막힌다()
        {
            Vector3 incoming = IncomingAtDescent(DefaultArc + Margin + 10f);

            float factor = ShieldSolver.ComputeBlockFactor(
                incoming, FacingSideways, ShieldResistance, DefaultArc, Margin);

            Assert.AreEqual(1f - ShieldResistance, factor, 0.0001f);
        }

        // ====================================================================================================
        // 6. 병과 정의와의 정합
        // ====================================================================================================

        /// <summary>
        /// 실제 궁수 정의와 실제 보병 정의를 그대로 넣어, 조합된 결과가 의도대로인지 봅니다.
        /// 개별 함수가 맞아도 에셋 기본값끼리 어긋나면 게임에서는 틀립니다.
        /// </summary>
        [Test]
        public void 실제_궁수와_보병_기본값으로_평지_측면_사격이_통한다()
        {
            var archer = UnitDefinition.CreateDefault(SRPG.Common.UnitRole.Archer);
            var infantry = UnitDefinition.CreateDefault(SRPG.Common.UnitRole.Infantry);

            try
            {
                Assert.Greater(infantry.ProjectileResistance, 0f, "보병에게 방패 저항이 없습니다.");

                Vector3 incoming = IncomingAtDescent(archer.ArcLaunchAngleDegrees);

                float sideFactor = ShieldSolver.ComputeBlockFactor(
                    incoming, FacingSideways, infantry.ProjectileResistance,
                    archer.ArcLaunchAngleDegrees, Margin);

                float frontFactor = ShieldSolver.ComputeBlockFactor(
                    incoming, FacingTheArrow, infantry.ProjectileResistance,
                    archer.ArcLaunchAngleDegrees, Margin);

                Assert.AreEqual(1f, sideFactor, 0.0001f, "측면 사격이 막혔습니다.");
                Assert.Less(frontFactor, 1f, "정면 사격이 막히지 않았습니다.");
            }
            finally
            {
                Object.DestroyImmediate(archer);
                Object.DestroyImmediate(infantry);
            }
        }
    }
}
