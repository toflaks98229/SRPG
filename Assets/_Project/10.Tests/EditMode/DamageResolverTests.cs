using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Systems.Combat;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 한 번의 타격이 얼마나 들어가고 얼마나 밀어내는지를 검증합니다.
    ///
    /// <b>이 검사가 존재할 수 있게 된 것 자체가 분리의 목적입니다</b>
    ///
    /// 이 계산은 <c>Unit.ReceiveHit</c> 안에 있었습니다. 그때는 여기 있는 어느 것도
    /// 이렇게 물을 수 없었습니다 — 병사를 세우고, 정의를 붙이고, 컨텍스트를 만들고,
    /// 때린 다음 <b>남은 체력을 역산</b>해야 했습니다.
    /// 그러면 검사가 규칙이 아니라 병사의 상태 기계를 보게 됩니다.
    ///
    /// <b>숫자가 아니라 관계를 지킵니다</b>
    ///
    /// 구체적인 배율은 밸런스라 계속 바뀝니다. 여기서 값을 못박으면 수치를 만질 때마다
    /// 검사가 깨지고, 결국 검사 쪽을 고치게 되어 아무것도 지키지 못합니다.
    /// </summary>
    public sealed class DamageResolverTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        /// <summary>검사에 쓰는 튜닝입니다.</summary>
        private BattleTuning _tuning;

        [SetUp]
        public void SetUp()
        {
            _tuning = BattleTuning.CreateDefault();
        }

        [TearDown]
        public void TearDown()
        {
            if (_tuning != null)
            {
                Object.DestroyImmediate(_tuning);
            }
        }

        // ====================================================================================================
        // 2. 방패 — 상황 방어
        // ====================================================================================================

        /// <summary>
        /// 방패는 정면에서 오는 화살을 막고 측면에서 오는 것은 못 막습니다.
        ///
        /// 이 비대칭이 이 게임 기동의 이유입니다. 막을 수 없는 방향이 있어야
        /// "돌아 들어간다"가 전술이 됩니다.
        /// </summary>
        [Test]
        public void 방패는_정면_화살만_막는다()
        {
            var shielded = Defender(Vector3.forward, resistance: 0.8f);

            // 앞에서 날아옵니다 — 화살의 진행 방향은 병사가 보는 쪽의 반대입니다.
            var fromFront = Arrow(direction: Vector3.back);
            var fromSide = Arrow(direction: Vector3.right);

            float front = DamageResolver.Resolve(fromFront, shielded, _tuning).HealthLoss;
            float side = DamageResolver.Resolve(fromSide, shielded, _tuning).HealthLoss;

            Assert.Less(front, side, "정면 사격이 측면 사격보다 아프면 방패를 든 의미가 없습니다.");
        }

        /// <summary>
        /// 방패는 <b>투사체만</b> 막습니다. 근접 타격은 그냥 지나갑니다.
        ///
        /// 누락이 아니라 규칙입니다 — 방패로 칼을 받아 내는 것은 이 게임이 다루는 층위가 아닙니다.
        /// </summary>
        [Test]
        public void 방패는_근접_타격을_막지_않는다()
        {
            var shielded = Defender(Vector3.forward, resistance: 0.8f);
            var bare = Defender(Vector3.forward, resistance: 0f);

            var melee = DamageInfo.Melee(10f, Vector3.back, 1f, 0.1f, null, DamageType.Slash);

            float withShield = DamageResolver.Resolve(melee, shielded, _tuning).HealthLoss;
            float without = DamageResolver.Resolve(melee, bare, _tuning).HealthLoss;

            Assert.AreEqual(without, withShield, 0.0001f);
        }

        /// <summary>
        /// 뛰는 동안에는 방패가 제 몫을 못 합니다.
        ///
        /// "궁수 앞에서 뛰어다니지 마라"가 별도 규칙이 아니라 이 한 줄에서 나옵니다.
        /// </summary>
        [Test]
        public void 이동_중에는_방패가_덜_막는다()
        {
            var standing = Defender(Vector3.forward, resistance: 0.8f, moving: false);
            var running = Defender(Vector3.forward, resistance: 0.8f, moving: true);

            var arrow = Arrow(direction: Vector3.back);

            float held = DamageResolver.Resolve(arrow, standing, _tuning).HealthLoss;
            float shaken = DamageResolver.Resolve(arrow, running, _tuning).HealthLoss;

            Assert.Greater(shaken, held, "뛰는 동안에도 방패가 그대로면 붙잡아 둘 이유가 사라집니다.");
        }

        // ====================================================================================================
        // 3. 갑옷 — 상시 방어
        // ====================================================================================================

        /// <summary>
        /// 갑옷 상성이 피해에 실제로 곱해집니다.
        ///
        /// 표 자체는 <c>ArmorEffectivenessTests</c> 가 봅니다.
        /// 여기서 보는 것은 그 표가 <b>결과에 닿는가</b>입니다 — 표만 맞고 배선이 끊겨 있으면
        /// 아무 오류 없이 상성이 사라집니다.
        /// </summary>
        [Test]
        public void 갑옷_상성이_피해에_반영된다()
        {
            var heavy = Defender(Vector3.forward, resistance: 0f, armor: ArmorType.Heavy);

            var pierce = DamageInfo.Melee(10f, Vector3.back, 0f, 0f, null, DamageType.Pierce);
            var slash = DamageInfo.Melee(10f, Vector3.back, 0f, 0f, null, DamageType.Slash);

            float byPierce = DamageResolver.Resolve(pierce, heavy, _tuning).HealthLoss;
            float bySlash = DamageResolver.Resolve(slash, heavy, _tuning).HealthLoss;

            Assert.Greater(byPierce, bySlash, "중갑 상대로 자돌이 참격보다 잘 들지 않습니다.");
        }

        // ====================================================================================================
        // 4. 충격 — 피해와 다른 것을 탄다
        // ====================================================================================================

        /// <summary>
        /// <b>갑옷은 넉백을 줄이지 않습니다.</b>
        ///
        /// 갑옷은 살을 지킬 뿐 운동량을 없애지 못합니다 — 판금에 막힌 철퇴도 사람을 밀어냅니다.
        /// 이 구분이 무너지면 중갑 보병이 밀리지도 죽지도 않는 벽이 됩니다.
        /// </summary>
        [Test]
        public void 갑옷은_넉백을_줄이지_않는다()
        {
            var heavy = Defender(Vector3.forward, resistance: 0f, armor: ArmorType.Heavy);
            var bare = Defender(Vector3.forward, resistance: 0f, armor: ArmorType.Unarmored);

            // 참격은 중갑에 잘 안 듭니다. 그런데도 밀어내는 힘은 같아야 합니다.
            var hit = DamageInfo.Melee(10f, Vector3.back, 5f, 0.2f, null, DamageType.Slash);

            var onHeavy = DamageResolver.Resolve(hit, heavy, _tuning);
            var onBare = DamageResolver.Resolve(hit, bare, _tuning);

            Assert.Less(onHeavy.HealthLoss, onBare.HealthLoss, "전제가 성립하지 않습니다 — 참격이 중갑에 덜 들어야 합니다.");
            Assert.AreEqual(onBare.Impulse.magnitude, onHeavy.Impulse.magnitude, 0.0001f);
            Assert.AreEqual(onBare.StaggerSeconds, onHeavy.StaggerSeconds, 0.0001f);
        }

        /// <summary>
        /// 막아 내도 충격은 전달됩니다.
        ///
        /// 큰 적의 일격이 피해가 막혀도 방패벽을 뒤로 밀어야 틈이 생기고,
        /// 그 틈이 다음 수의 재료가 됩니다.
        /// </summary>
        [Test]
        public void 막아_내도_충격은_전달된다()
        {
            _tuning.Shield.BlockedKnockbackRetention = 1f;

            var shielded = Defender(Vector3.forward, resistance: 1f);
            var arrow = Arrow(direction: Vector3.back, knockback: 5f);

            var outcome = DamageResolver.Resolve(arrow, shielded, _tuning);

            Assert.AreEqual(0f, outcome.HealthLoss, 0.0001f, "완전히 막았는데 피해가 들어갑니다.");
            Assert.Greater(outcome.Impulse.magnitude, 0f, "완전히 막았다고 충격까지 사라지면 방패벽이 밀리지 않습니다.");
        }

        /// <summary>
        /// 반대로 보정을 0으로 두면 막힌 만큼 충격도 함께 줄어듭니다.
        ///
        /// 이 값이 실제로 손잡이 노릇을 하는지를 봅니다. 위와 아래가 같은 결과라면
        /// 튜닝이 배선되지 않은 것입니다.
        /// </summary>
        [Test]
        public void 충격_보존을_0으로_두면_막은_만큼_덜_밀린다()
        {
            _tuning.Shield.BlockedKnockbackRetention = 0f;

            var shielded = Defender(Vector3.forward, resistance: 1f);
            var arrow = Arrow(direction: Vector3.back, knockback: 5f);

            var outcome = DamageResolver.Resolve(arrow, shielded, _tuning);

            Assert.AreEqual(0f, outcome.Impulse.magnitude, 0.0001f);
        }

        /// <summary>
        /// 넉백은 <b>수평 성분만</b> 씁니다.
        ///
        /// 위에서 내리꽂힌 화살이 사람을 땅에 박을 수는 없습니다.
        /// </summary>
        [Test]
        public void 넉백은_수평_성분만_쓴다()
        {
            var bare = Defender(Vector3.forward, resistance: 0f);

            var slanted = DamageInfo.Melee(10f, new Vector3(1f, -3f, 0f), 5f, 0.2f, null);
            var straightDown = DamageInfo.Melee(10f, Vector3.down, 5f, 0.2f, null);

            var pushed = DamageResolver.Resolve(slanted, bare, _tuning);
            var dropped = DamageResolver.Resolve(straightDown, bare, _tuning);

            Assert.AreEqual(0f, pushed.Impulse.y, 0.0001f, "수직 성분이 남아 있으면 병사가 땅으로 박힙니다.");
            Assert.IsTrue(pushed.HasImpulse);

            Assert.IsFalse(dropped.HasImpulse, "밀어낼 방향이 없는데 밀어냅니다.");
            Assert.Greater(dropped.HealthLoss, 0f, "밀지 못한다고 피해까지 사라지면 안 됩니다.");
        }

        // ====================================================================================================
        // 5. 튜닝이 없는 경로
        // ====================================================================================================

        /// <summary>
        /// 튜닝 없이도 답이 나옵니다. 상성도 감쇠 보정도 없는 것으로 봅니다.
        ///
        /// 병사만 떼어 세우는 경로(자동 검사, 무기 하나만 시험하는 씬)가 이쪽으로 옵니다.
        /// 여기서 터지면 그 경로 전체가 막힙니다.
        /// </summary>
        [Test]
        public void 튜닝이_없어도_피해가_그대로_들어간다()
        {
            var bare = Defender(Vector3.forward, resistance: 0f, armor: ArmorType.Heavy);
            var hit = DamageInfo.Melee(10f, Vector3.back, 5f, 0.2f, null, DamageType.Slash);

            var outcome = DamageResolver.Resolve(hit, bare, null);

            Assert.AreEqual(10f, outcome.HealthLoss, 0.0001f, "튜닝이 없으면 상성 없이 그대로 들어가야 합니다.");
            Assert.AreEqual(1f, outcome.Mitigation, 0.0001f);
            Assert.Greater(outcome.Impulse.magnitude, 0f);
        }

        /// <summary>
        /// 튜닝이 없어도 방패는 듣습니다. 각도 기준선이 코드 상수로 남아 있기 때문입니다.
        /// </summary>
        [Test]
        public void 튜닝이_없어도_방패는_듣는다()
        {
            var shielded = Defender(Vector3.forward, resistance: 0.8f);

            float front = DamageResolver.Resolve(Arrow(Vector3.back), shielded, null).HealthLoss;
            float side = DamageResolver.Resolve(Arrow(Vector3.right), shielded, null).HealthLoss;

            Assert.Less(front, side);
        }

        // ====================================================================================================
        // 6. Helpers
        // ====================================================================================================

        /// <summary>맞는 쪽 상태를 짧게 만듭니다.</summary>
        /// <param name="forward">바라보는 방향입니다.</param>
        /// <param name="resistance">투사체 피해 감소율입니다.</param>
        /// <param name="armor">걸친 방어입니다.</param>
        /// <param name="moving">지금 움직이고 있는지입니다.</param>
        /// <returns>검사에 쓸 방어 측 상태입니다.</returns>
        private static DefenderProfile Defender(
            Vector3 forward,
            float resistance,
            ArmorType armor = ArmorType.Unarmored,
            bool moving = false)
        {
            return new DefenderProfile(forward, resistance, armor, moving);
        }

        /// <summary>
        /// 검사용 화살을 만듭니다.
        ///
        /// 하강각은 0으로 둡니다. 상방 판정은 <c>ShieldSolver</c> 가 따로 보고 있고,
        /// 여기서 각도까지 섞으면 무엇이 막았는지가 흐려집니다.
        /// </summary>
        /// <param name="direction">화살이 진행한 방향입니다.</param>
        /// <param name="knockback">넉백 세기입니다.</param>
        /// <returns>투사체 타격 정보입니다.</returns>
        private static DamageInfo Arrow(Vector3 direction, float knockback = 0f)
        {
            return DamageInfo.Projectile(10f, direction, knockback, 0.2f, 0f, null, DamageType.Pierce);
        }
    }
}
