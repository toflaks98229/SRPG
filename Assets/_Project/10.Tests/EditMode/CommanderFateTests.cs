using NUnit.Framework;
using SRPG.Data;
using SRPG.Systems.Combat;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 지휘관이 치명상을 입었을 때의 규칙을 검증합니다.
    ///
    /// <b>왜 이 규칙에 검사가 필요한가</b>
    ///
    /// 지휘관의 죽음은 이 게임에서 유일하게 되돌릴 수 없는 손실입니다.
    /// 그런데 그 판정은 <b>확률</b>이라, 잘못 걸려 있어도 화면에서는
    /// "오늘은 운이 나빴다"로만 보입니다. 안전장치가 통째로 죽어 있어도 마찬가지입니다 —
    /// 몇 판을 해 봐야 "어쩐지 지휘관이 자주 죽는다"는 인상이 생길 뿐입니다.
    ///
    /// 기술 문서 §7.4 의 죽은 표적 재평가, §2.7 의 방패 기준선과 같은 종류입니다.
    /// 컴파일러도 런타임도 잡지 못하고, 증상이 규칙의 부재와 구별되지 않습니다.
    /// </summary>
    public sealed class CommanderFateTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        private BattleTuning.CommanderTuning _rules;

        [SetUp]
        public void SetUp()
        {
            _rules = new BattleTuning.CommanderTuning
            {
                FallenEscortRatio = 0.6f,
                FallChance = 0.35f,
                MaxWounds = 2,
                WoundRecoveryRatio = 0.35f,
                WoundStaggerSeconds = 0.8f,
            };
        }

        /// <summary>호위 다섯 중 <paramref name="fallen"/> 명을 잃은 지휘관입니다.</summary>
        private static CommanderGuard Guard(int fallen, int wounds = 0, int deployed = 5)
        {
            return new CommanderGuard(deployed - fallen, deployed, wounds);
        }

        // ====================================================================================================
        // 2. 안전장치 — 호위가 남아 있으면 쓰러지지 않는다
        // ====================================================================================================

        /// <summary>
        /// 분대가 온전하면 <b>어떤 굴림에도</b> 쓰러지지 않습니다.
        ///
        /// 이것이 이 규칙의 핵심입니다. 온전한 분대의 지휘관이 빗나간 화살 하나에 사라지면
        /// 영구 손실이 플레이어의 판단이 아니라 사고가 됩니다.
        /// </summary>
        [Test]
        public void 호위가_온전하면_어떤_굴림에도_쓰러지지_않는다()
        {
            for (int i = 0; i <= 20; i++)
            {
                float roll = i / 20f;

                var fate = CommanderFate.Resolve(Guard(fallen: 0), _rules, roll);

                Assert.AreEqual(
                    CommanderFateOutcome.Wounded,
                    fate,
                    $"호위를 하나도 잃지 않았는데 굴림 {roll:F2} 에서 지휘관이 쓰러졌습니다.");
            }
        }

        /// <summary>
        /// 기준에 <b>한 명 모자라면</b> 아직 안전합니다. 경계에서 규칙이 흔들리지 않아야 합니다.
        /// </summary>
        [Test]
        public void 기준에_한_명_모자라면_아직_안전하다()
        {
            // 다섯 중 60% → 올림으로 3명. 두 명까지는 안전합니다.
            var fate = CommanderFate.Resolve(Guard(fallen: 2), _rules, roll: 0f);

            Assert.AreEqual(CommanderFateOutcome.Wounded, fate, "기준에 못 미쳤는데 쓰러졌습니다.");

            Assert.IsFalse(CommanderFate.IsExposed(Guard(fallen: 2), _rules));
            Assert.IsTrue(CommanderFate.IsExposed(Guard(fallen: 3), _rules));
        }

        /// <summary>
        /// 호위가 무너지면 그때부터 확률이 개입합니다.
        /// </summary>
        [Test]
        public void 호위가_무너지면_확률이_개입한다()
        {
            var broken = Guard(fallen: 5);

            Assert.AreEqual(
                CommanderFateOutcome.Fallen,
                CommanderFate.Resolve(broken, _rules, roll: 0f),
                "낮은 굴림인데 쓰러지지 않았습니다. 확률이 걸리지 않고 있습니다.");

            Assert.AreEqual(
                CommanderFateOutcome.Wounded,
                CommanderFate.Resolve(broken, _rules, roll: 0.99f),
                "높은 굴림인데 쓰러졌습니다.");
        }

        /// <summary>
        /// 비율이라 <b>분대 크기가 달라도 같은 조건</b>입니다.
        ///
        /// 절대 수로 두면 두 명짜리 분대의 지휘관이 여섯 명짜리보다 훨씬 쉽게 죽습니다.
        /// </summary>
        [Test]
        public void 분대가_작아도_같은_비율로_판정한다()
        {
            // 둘 다 60% 를 잃은 상태입니다.
            Assert.IsTrue(CommanderFate.IsExposed(Guard(fallen: 3, deployed: 5), _rules));
            Assert.IsTrue(CommanderFate.IsExposed(Guard(fallen: 6, deployed: 10), _rules));

            // 둘 다 40% 만 잃었습니다.
            Assert.IsFalse(CommanderFate.IsExposed(Guard(fallen: 2, deployed: 5), _rules));
            Assert.IsFalse(CommanderFate.IsExposed(Guard(fallen: 4, deployed: 10), _rules));
        }

        // ====================================================================================================
        // 3. 상한 — 운이 좋아도 영원히 살지는 못한다
        // ====================================================================================================

        /// <summary>
        /// 부상 한도를 넘기면 <b>판정 없이</b> 쓰러집니다.
        ///
        /// 상한이 없으면 운 좋은 지휘관이 영영 죽지 않고, 그러면 영구 손실이라는 규칙이 사라집니다.
        /// </summary>
        [Test]
        public void 부상_한도를_넘기면_판정_없이_쓰러진다()
        {
            var worn = Guard(fallen: 0, wounds: 2);

            Assert.AreEqual(
                CommanderFateOutcome.Fallen,
                CommanderFate.Resolve(worn, _rules, roll: 0.99f),
                "부상 한도를 넘겼는데 살아남았습니다.");
        }

        /// <summary>
        /// <b>한도 검사가 안전장치보다 먼저입니다.</b>
        ///
        /// 순서가 뒤집히면 호위를 충원하는 것만으로 부상 한도가 무의미해집니다 —
        /// 지휘관이 몇 번이든 살아나고 영구 손실이 사라집니다.
        /// 값이 아니라 <b>순서</b>가 규칙이라, 이 검사가 유일한 방어선입니다.
        /// </summary>
        [Test]
        public void 한도_검사가_안전장치보다_먼저다()
        {
            // 호위는 멀쩡한데 부상만 한도를 넘긴 상태입니다.
            var worn = Guard(fallen: 0, wounds: 2);

            Assert.IsFalse(CommanderFate.IsExposed(worn, _rules), "전제가 성립하지 않습니다 — 호위는 온전해야 합니다.");

            Assert.AreEqual(
                CommanderFateOutcome.Fallen,
                CommanderFate.Resolve(worn, _rules, roll: 0f),
                "안전장치가 부상 한도를 덮어써 지휘관이 영원히 살아남습니다.");
        }

        // ====================================================================================================
        // 4. 회복량
        // ====================================================================================================

        /// <summary>
        /// 부상이 쌓일수록 되찾는 체력이 줄어듭니다.
        ///
        /// 매번 같은 만큼 되찾으면 지휘관이 위태로워지는 것이 아니라 그냥 체력이 많은 병사가 됩니다.
        /// </summary>
        [Test]
        public void 부상이_쌓일수록_덜_회복한다()
        {
            const float MaxHealth = 100f;

            float first = CommanderFate.ResolveRecoveredHealth(MaxHealth, 0, _rules);
            float second = CommanderFate.ResolveRecoveredHealth(MaxHealth, 1, _rules);

            Assert.Greater(first, second, "두 번째 부상이 첫 번째만큼 회복했습니다.");
            Assert.AreEqual(35f, first, 0.01f);
            Assert.AreEqual(17.5f, second, 0.01f);
        }

        /// <summary>
        /// 아무리 깎여도 <b>0으로는 일어나지 않습니다.</b>
        /// 0이면 일어난 그 프레임에 다시 쓰러져 부상이 아무 의미도 없습니다.
        /// </summary>
        [Test]
        public void 회복량이_0이_되지_않는다()
        {
            float health = CommanderFate.ResolveRecoveredHealth(1f, 10, _rules);

            Assert.Greater(health, 0f, "부상에서 체력 0으로 일어났습니다.");
        }

        // ====================================================================================================
        // 5. 규칙이 없어도 터지지 않는다
        // ====================================================================================================

        /// <summary>
        /// 튜닝이 연결되지 않아도 코드 기본값으로 돕니다.
        ///
        /// 이 프로젝트는 "에셋 없이도 실행 가능"을 유지합니다.
        /// 여기서 터지면 튜닝 에셋 없이 여는 검사 경로가 통째로 막힙니다.
        /// </summary>
        [Test]
        public void 규칙이_비어도_판정이_돈다()
        {
            Assert.DoesNotThrow(() =>
            {
                var fate = CommanderFate.Resolve(Guard(fallen: 0), null, 0.5f);

                Assert.AreEqual(
                    CommanderFateOutcome.Wounded,
                    fate,
                    "기본값에서도 호위가 온전하면 버텨야 합니다.");
            });

            Assert.Greater(CommanderFate.ResolveRecoveredHealth(100f, 0, null), 0f);
        }

        /// <summary>
        /// 호위가 애초에 없었으면 지킬 것도 없습니다. 곧바로 위험합니다.
        /// </summary>
        [Test]
        public void 호위가_없으면_곧바로_위험하다()
        {
            var alone = new CommanderGuard(0, 0, 0);

            Assert.IsTrue(CommanderFate.IsExposed(alone, _rules));

            Assert.AreEqual(
                CommanderFateOutcome.Fallen,
                CommanderFate.Resolve(alone, _rules, roll: 0f));
        }
    }
}
