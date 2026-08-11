using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 유효 수치 계산과 랭크 성장을 검증합니다.
    ///
    /// <b>왜 이것부터 지켜야 하는가</b>
    ///
    /// 앞으로 붙을 성장 요소(무기 숙련도·상성)는 전부 같은 자리를 지납니다.
    /// 그 자리의 규칙 — 시간은 나누고 배율은 곱한다 — 이 어긋나면
    /// "공속을 올렸는데 느려진다" 같은 형태로 조용히 뒤집힙니다.
    /// 수치 계산은 화면 없이 확인할 수 있으므로 여기서 못박아 둡니다.
    /// </summary>
    public sealed class UnitStatsTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        /// <summary>검사에 쓰는 병과입니다.</summary>
        private UnitDefinition _definition;

        /// <summary>검사에 쓰는 튜닝입니다.</summary>
        private BattleTuning _tuning;

        [SetUp]
        public void SetUp()
        {
            _definition = UnitDefinition.CreateDefault(UnitRole.Infantry);
            _tuning = BattleTuning.CreateDefault();
        }

        [TearDown]
        public void TearDown()
        {
            if (_definition != null)
            {
                Object.DestroyImmediate(_definition);
            }

            if (_tuning != null)
            {
                Object.DestroyImmediate(_tuning);
            }
        }

        // ====================================================================================================
        // 2. 항등
        // ====================================================================================================

        [Test]
        public void 배율이_없으면_정의_그대로다()
        {
            var stats = UnitStats.From(_definition);

            Assert.AreEqual(_definition.MaxHealth, stats.MaxHealth, 0.0001f);
            Assert.AreEqual(_definition.AttackDamage, stats.AttackDamage, 0.0001f);
            Assert.AreEqual(_definition.AttackInterval, stats.AttackInterval, 0.0001f);
            Assert.AreEqual(_definition.MoveSpeed, stats.MoveSpeed, 0.0001f);
            Assert.AreEqual(_definition.AttackDuration, stats.AttackDuration, 0.0001f);
        }

        [Test]
        public void 정의가_없으면_전부_0이다()
        {
            var stats = UnitStats.From(null);

            Assert.AreEqual(0f, stats.MaxHealth);
            Assert.AreEqual(0f, stats.AttackDamage);
        }

        // ====================================================================================================
        // 3. 배율의 방향
        // ====================================================================================================

        [Test]
        public void 공속이_오르면_간격과_동작이_짧아진다()
        {
            var faster = new UnitModifiers(1f, 1f, 2f, 1f, 1f, 1f, 1f);
            var stats = new UnitStats(_definition, faster);

            Assert.Less(stats.AttackInterval, _definition.AttackInterval, "공속을 올렸는데 간격이 늘었습니다.");
            Assert.AreEqual(_definition.AttackInterval / 2f, stats.AttackInterval, 0.0001f);
            Assert.AreEqual(_definition.AttackDuration / 2f, stats.AttackDuration, 0.0001f);
        }

        [Test]
        public void 명중이_오르면_산포가_좁아진다()
        {
            var sharper = new UnitModifiers(1f, 1f, 1f, 1f, 2f, 1f, 1f);
            var stats = new UnitStats(_definition, sharper);

            Assert.Less(stats.MaxSpreadDegrees, _definition.MaxSpreadDegrees, "명중을 올렸는데 산포가 넓어졌습니다.");
            Assert.AreEqual(_definition.MinSpreadDegrees / 2f, stats.MinSpreadDegrees, 0.0001f);
        }

        [Test]
        public void 투사체_감소율은_1을_넘지_않는다()
        {
            _definition.ProjectileResistance = 0.8f;

            var stats = new UnitStats(_definition, new UnitModifiers(1f, 1f, 1f, 1f, 1f, 1f, 5f));

            Assert.AreEqual(1f, stats.ProjectileResistance, 0.0001f, "감소율이 1을 넘으면 피해가 음수가 됩니다.");
        }

        [Test]
        public void 배율은_곱해서_합쳐진다()
        {
            var a = new UnitModifiers(1.5f, 2f, 1f, 1f, 1f, 1f, 1f);
            var b = new UnitModifiers(2f, 1.5f, 1f, 1f, 1f, 1f, 1f);

            var combined = a.Combine(b);

            Assert.AreEqual(3f, combined.Health, 0.0001f);
            Assert.AreEqual(3f, combined.Damage, 0.0001f);
        }

        [Test]
        public void 배율이_0이하로_들어와도_수치가_뒤집히지_않는다()
        {
            var broken = new UnitModifiers(0f, 1f, 0f, -1f, 0f, 1f, 1f);
            var stats = new UnitStats(_definition, broken);

            Assert.Greater(stats.MaxHealth, 0f);
            Assert.Greater(stats.AttackInterval, 0f, "공속 배율이 0이면 간격이 무한대가 됩니다.");
            Assert.Greater(stats.MoveSpeed, 0f);
            Assert.IsFalse(float.IsInfinity(stats.MaxSpreadDegrees));
        }

        // ====================================================================================================
        // 4. 랭크 성장
        // ====================================================================================================

        [Test]
        public void 최저_랭크는_정의_그대로다()
        {
            var growth = _tuning.EvaluateRank(CombatConstants.MinRank);

            Assert.AreEqual(1f, growth.Health, 0.0001f, "1랭크가 기준선이 아니면 밸런스를 잡을 기준이 없어집니다.");
            Assert.AreEqual(1f, growth.Damage, 0.0001f);
            Assert.AreEqual(1f, growth.AttackSpeed, 0.0001f);
        }

        [Test]
        public void 랭크가_오르면_수치가_오른다()
        {
            var low = new UnitStats(_definition, _tuning.EvaluateRank(CombatConstants.MinRank));
            var high = new UnitStats(_definition, _tuning.EvaluateRank(CombatConstants.MaxRank));

            Assert.Greater(high.MaxHealth, low.MaxHealth);
            Assert.Greater(high.AttackDamage, low.AttackDamage);
            Assert.Less(high.AttackInterval, low.AttackInterval, "랭크가 올랐는데 공격이 느려졌습니다.");
            Assert.Greater(high.MoveSpeed, low.MoveSpeed);
        }

        [Test]
        public void 랭크_성장은_명중을_건드리지_않는다()
        {
            var growth = _tuning.EvaluateRank(CombatConstants.MaxRank);

            // 궁수의 산포는 BallisticSolver 가 이미 랭크로 보간합니다.
            // 여기서도 걸면 같은 성장이 두 번 적용됩니다.
            Assert.AreEqual(1f, growth.Accuracy, 0.0001f, "랭크 성장이 명중에도 걸려 궁수가 두 번 강해집니다.");
        }

        [Test]
        public void 랭크는_허용_범위로_잘린다()
        {
            var beyond = _tuning.EvaluateRank(CombatConstants.MaxRank + 10);
            var top = _tuning.EvaluateRank(CombatConstants.MaxRank);

            Assert.AreEqual(top.Health, beyond.Health, 0.0001f);

            var below = _tuning.EvaluateRank(CombatConstants.MinRank - 5);

            Assert.AreEqual(1f, below.Health, 0.0001f);
        }
    }
}
