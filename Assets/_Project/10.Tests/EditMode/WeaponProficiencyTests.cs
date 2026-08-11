using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Campaign;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 무기 숙련도가 계열별로 따로 쌓이고, 장부에서 전장까지 그대로 전달되는지 검증합니다.
    ///
    /// <b>왜 전달 경로까지 보는가</b>
    ///
    /// 숙련도는 캠페인이 소유하고 전투가 소비합니다. 그 사이에 네 단계가 있습니다 —
    /// 장부 → 주문서 → 분대 → 병사. 한 곳만 빠뜨려도 증상은 "숙련도가 안 오르는 것 같다"이고,
    /// 실제로는 오르되 전달되지 않는 상태라 원인을 찾기 매우 나쁩니다.
    /// </summary>
    public sealed class WeaponProficiencyTests
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
        // 2. 계열 분리
        // ====================================================================================================

        [Test]
        public void 계열마다_따로_쌓인다()
        {
            var proficiency = default(WeaponProficiency).Gain(AttackStyle.Projectile, 60);

            Assert.AreEqual(60, proficiency.Get(AttackStyle.Projectile));
            Assert.AreEqual(0, proficiency.Get(AttackStyle.MeleeSwing), "활을 쏘았는데 검술이 늘었습니다.");
            Assert.AreEqual(0, proficiency.Get(AttackStyle.MeleeThrust));
        }

        [Test]
        public void 휘두르기와_찌르기는_다른_계열이다()
        {
            var proficiency = default(WeaponProficiency).Gain(AttackStyle.MeleeSwing, 50);

            // 둘 다 근접이지만 익히는 동작이 다릅니다. 검을 다루던 병사가 창을 잘 쓰지는 않습니다.
            Assert.AreEqual(50, proficiency.Get(AttackStyle.MeleeSwing));
            Assert.AreEqual(0, proficiency.Get(AttackStyle.MeleeThrust));
        }

        [Test]
        public void 상한과_하한을_넘지_않는다()
        {
            var over = default(WeaponProficiency).Gain(AttackStyle.MeleeSwing, WeaponProficiency.MaxValue + 500);
            var under = default(WeaponProficiency).Gain(AttackStyle.MeleeSwing, -50);

            Assert.AreEqual(WeaponProficiency.MaxValue, over.Get(AttackStyle.MeleeSwing));
            Assert.AreEqual(0, under.Get(AttackStyle.MeleeSwing));
        }

        [Test]
        public void 바꾼_사본이_원본을_건드리지_않는다()
        {
            var original = WeaponProficiency.Uniform(10);
            var changed = original.Gain(AttackStyle.Projectile, 40);

            Assert.AreEqual(10, original.Get(AttackStyle.Projectile), "구조체인데 원본이 바뀌었습니다.");
            Assert.AreEqual(50, changed.Get(AttackStyle.Projectile));
        }

        // ====================================================================================================
        // 3. 성장 배율
        // ====================================================================================================

        [Test]
        public void 미숙하면_아무것도_바뀌지_않는다()
        {
            var growth = _tuning.EvaluateProficiency(0);

            Assert.AreEqual(1f, growth.Accuracy, 0.0001f);
            Assert.AreEqual(1f, growth.Damage, 0.0001f);
            Assert.AreEqual(1f, growth.AttackSpeed, 0.0001f);
        }

        [Test]
        public void 숙련도는_명중을_가장_크게_올린다()
        {
            var growth = _tuning.EvaluateProficiency(WeaponProficiency.MaxValue);

            Assert.Greater(growth.Accuracy, 1f);
            Assert.Greater(growth.Accuracy, growth.Damage, "숙련은 '잘 맞히는 것'이지 '세게 때리는 것'이 아닙니다.");
            Assert.Greater(growth.Accuracy, growth.AttackSpeed);
        }

        [Test]
        public void 숙련도는_체력과_이동을_건드리지_않는다()
        {
            var growth = _tuning.EvaluateProficiency(WeaponProficiency.MaxValue);

            // 그 축은 단련도(랭크)의 몫입니다. 겹치면 어느 쪽을 올렸는지 체감으로 갈리지 않습니다.
            Assert.AreEqual(1f, growth.Health, 0.0001f);
            Assert.AreEqual(1f, growth.MoveSpeed, 0.0001f);
        }

        [Test]
        public void 숙련도가_오르면_산포가_좁아진다()
        {
            var definition = UnitDefinition.CreateDefault(UnitRole.Archer);

            try
            {
                var green = new UnitStats(definition, _tuning.EvaluateProficiency(0));
                var veteran = new UnitStats(definition, _tuning.EvaluateProficiency(WeaponProficiency.MaxValue));

                Assert.Less(veteran.MaxSpreadDegrees, green.MaxSpreadDegrees);
                Assert.Less(veteran.MinSpreadDegrees, green.MinSpreadDegrees);
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void 단련도와_숙련도는_함께_걸린다()
        {
            var definition = UnitDefinition.CreateDefault(UnitRole.Archer);

            try
            {
                var combined = _tuning.EvaluateRank(CombatConstants.MaxRank)
                    .Combine(_tuning.EvaluateProficiency(WeaponProficiency.MaxValue));

                var stats = new UnitStats(definition, combined);
                var rankOnly = new UnitStats(definition, _tuning.EvaluateRank(CombatConstants.MaxRank));

                Assert.Greater(stats.AttackDamage, rankOnly.AttackDamage, "숙련도 몫이 더해지지 않았습니다.");
                Assert.Less(stats.MaxSpreadDegrees, rankOnly.MaxSpreadDegrees);
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        // ====================================================================================================
        // 4. 장부 → 주문서 전달
        // ====================================================================================================

        [Test]
        public void 장부의_숙련도가_주문서에_실린다()
        {
            var definition = UnitDefinition.CreateDefault(UnitRole.Archer);

            try
            {
                var roster = new CampaignRoster();

                roster.Enlist(
                    definition, 5, "궁수대",
                    default(WeaponProficiency).Gain(AttackStyle.Projectile, 75));

                var request = roster.BuildRequest(
                    new[] { new SquadOrder { Id = 101, Definition = definition, SoldierCount = 3 } },
                    BattlefieldSpec.CreateDefault(),
                    1);

                Assert.AreEqual(
                    75,
                    request.PlayerSquads[0].Proficiency.Get(AttackStyle.Projectile),
                    "장부에 쌓인 숙련도가 전장까지 전달되지 않았습니다.");
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void 전투를_치러도_숙련도가_사라지지_않는다()
        {
            var definition = UnitDefinition.CreateDefault(UnitRole.Archer);

            try
            {
                var roster = new CampaignRoster();

                var squad = roster.Enlist(
                    definition, 5, null,
                    default(WeaponProficiency).Gain(AttackStyle.Projectile, 40));

                var result = new BattleResult { Outcome = BattleOutcome.Victory };
                result.Squads.Add(new SquadReport { Id = squad.Id, Deployed = 6, Survivors = 4 });

                roster.ApplyResult(result);

                // 전투를 치르면 숙련도는 <b>오릅니다</b>. 여기서 보는 것은 그 값이 아니라,
                // 손실을 반영하는 과정에서 쌓아 둔 것이 지워지지 않는다는 사실입니다.
                Assert.GreaterOrEqual(
                    squad.Proficiency.Get(AttackStyle.Projectile),
                    40,
                    "손실을 반영하면서 쌓아 둔 숙련도를 지웠습니다.");
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }
    }
}
