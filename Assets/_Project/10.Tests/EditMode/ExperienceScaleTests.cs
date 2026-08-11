using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 무기별 숙련도 획득 보정을 검증합니다.
    ///
    /// <b>막으려는 것</b>
    ///
    /// 명중 수를 그대로 세면 자주 때리고 잘 맞는 무기가 저절로 빨리 숙련됩니다.
    /// 그러면 무기를 고르는 이유가 성능이 아니라 "빨리 크는 쪽"이 됩니다.
    ///
    /// <b>보정이 부대 상태를 따라가면 안 됩니다</b>
    ///
    /// 보정은 무기의 성질입니다. 성장으로 오른 공격 속도와 명중을 보정에 넣으면
    /// 숙련될수록 보정이 줄어드는 되먹임이 생겨, 이미 오른 능력으로 다시 벌을 받게 됩니다.
    /// 이 파일에서 가장 중요한 검사가 그것을 지키는 것들입니다.
    /// </summary>
    public sealed class ExperienceScaleTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        /// <summary>기본 성장 곡선입니다.</summary>
        private CampaignProgression _progression;

        /// <summary>검사에 쓰는 전투 튜닝입니다. 성장 배율을 만들 때 씁니다.</summary>
        private BattleTuning _tuning;

        [SetUp]
        public void SetUp()
        {
            _progression = new CampaignProgression();
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
        // 2. 보정의 방향
        // ====================================================================================================

        [Test]
        public void 자주_때리는_무기일수록_한_대의_값이_싸다()
        {
            var fast = UnitDefinition.CreateDefault(UnitRole.Infantry);
            var slow = UnitDefinition.CreateDefault(UnitRole.Infantry);

            try
            {
                fast.AttackInterval = 0.5f;
                slow.AttackInterval = 2.0f;

                Assert.Less(
                    _progression.GetExperienceScale(fast),
                    _progression.GetExperienceScale(slow),
                    "빨리 휘두르는 무기가 한 대당 같은 값을 받으면 저절로 빨리 숙련됩니다.");
            }
            finally
            {
                Object.DestroyImmediate(fast);
                Object.DestroyImmediate(slow);
            }
        }

        [Test]
        public void 맞히기_어려운_무기일수록_한_대의_값이_비싸다()
        {
            var accurate = UnitDefinition.CreateDefault(UnitRole.Archer);
            var wild = UnitDefinition.CreateDefault(UnitRole.Archer);

            try
            {
                accurate.MaxSpreadDegrees = 3f;
                wild.MaxSpreadDegrees = 20f;

                Assert.Greater(
                    _progression.GetExperienceScale(wild),
                    _progression.GetExperienceScale(accurate));
            }
            finally
            {
                Object.DestroyImmediate(accurate);
                Object.DestroyImmediate(wild);
            }
        }

        [Test]
        public void 근접은_명중률을_1로_본다()
        {
            var infantry = UnitDefinition.CreateDefault(UnitRole.Infantry);
            var pike = UnitDefinition.CreateDefault(UnitRole.Pike);

            try
            {
                Assert.AreEqual(1f, _progression.EstimateHitRate(infantry), 0.0001f);
                Assert.AreEqual(1f, _progression.EstimateHitRate(pike), 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(infantry);
                Object.DestroyImmediate(pike);
            }
        }

        [Test]
        public void 궁수는_근접보다_한_대의_값이_비싸다()
        {
            var archer = UnitDefinition.CreateDefault(UnitRole.Archer);
            var infantry = UnitDefinition.CreateDefault(UnitRole.Infantry);

            try
            {
                Assert.Greater(
                    _progression.GetExperienceScale(archer),
                    _progression.GetExperienceScale(infantry),
                    "궁수가 근접과 같은 값을 받으면, 명중 수가 적은 만큼 그대로 뒤처집니다.");
            }
            finally
            {
                Object.DestroyImmediate(archer);
                Object.DestroyImmediate(infantry);
            }
        }

        [Test]
        public void 보정치는_상하한_안에_있다()
        {
            var broken = UnitDefinition.CreateDefault(UnitRole.Archer);

            try
            {
                // 수치를 잘못 적은 무기가 터무니없는 성장을 얻지 않아야 합니다.
                broken.AttackInterval = 0.05f;
                broken.MaxSpreadDegrees = 0.01f;

                float tooEasy = _progression.GetExperienceScale(broken);

                broken.AttackInterval = 30f;
                broken.MaxSpreadDegrees = 30f;

                float tooHard = _progression.GetExperienceScale(broken);

                Assert.GreaterOrEqual(tooEasy, _progression.MinExperienceScale);
                Assert.LessOrEqual(tooHard, _progression.MaxExperienceScale);
            }
            finally
            {
                Object.DestroyImmediate(broken);
            }
        }

        // ====================================================================================================
        // 3. 보정은 부대 상태를 따라가지 않는다
        // ====================================================================================================

        [Test]
        public void 숙련도가_올라도_보정치는_그대로다()
        {
            var definition = UnitDefinition.CreateDefault(UnitRole.Archer);

            try
            {
                float before = _progression.GetExperienceScale(definition);

                // 숙련도가 오르면 실제 공격 속도와 명중은 오릅니다.
                var grown = _tuning.EvaluateProficiency(WeaponProficiency.MaxValue);
                var grownStats = new UnitStats(definition, grown);

                Assert.Less(grownStats.AttackInterval, definition.AttackInterval, "전제가 깨졌습니다 — 숙련도가 공속을 올려야 합니다.");
                Assert.Less(grownStats.MaxSpreadDegrees, definition.MaxSpreadDegrees);

                // 그런데도 보정치는 움직이지 않아야 합니다.
                Assert.AreEqual(
                    before,
                    _progression.GetExperienceScale(definition),
                    0.0001f,
                    "숙련도가 보정에 새어 들어가면, 숙련될수록 성장이 느려지는 이중 감속이 됩니다.");
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void 랭크가_올라도_보정치는_그대로다()
        {
            var definition = UnitDefinition.CreateDefault(UnitRole.Infantry);

            try
            {
                float before = _progression.GetExperienceScale(definition);

                var grownStats = new UnitStats(definition, _tuning.EvaluateRank(CombatConstants.MaxRank));

                Assert.Less(grownStats.AttackInterval, definition.AttackInterval);

                Assert.AreEqual(
                    before,
                    _progression.GetExperienceScale(definition),
                    0.0001f,
                    "보정은 무기의 성질이지 그 무기를 든 부대의 상태가 아닙니다.");
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void 같은_무기라면_신병과_고참의_한_대_값이_같다()
        {
            var definition = UnitDefinition.CreateDefault(UnitRole.Archer);

            try
            {
                var green = new CampaignSquad { Definition = definition };

                // 고참의 숙련도를 상한에서 멀리 둡니다.
                // 100에 붙여 두면 상한 클램프가 증가폭을 잘라, 보정이 아니라 클램프를 재게 됩니다.
                var veteran = new CampaignSquad
                {
                    Definition = definition,
                    Rank = CombatConstants.MaxRank,
                    BattlesSurvived = 30,
                    Proficiency = WeaponProficiency.Uniform(20),
                };

                int greenBefore = green.Proficiency.Get(definition.Style);
                int veteranBefore = veteran.Proficiency.Get(definition.Style);

                var report = new SquadReport { Deployed = 6, Survivors = 5, HitsLanded = 20 };

                _progression.Apply(green, report);
                _progression.Apply(veteran, report);

                Assert.AreEqual(
                    green.Proficiency.Get(definition.Style) - greenBefore,
                    veteran.Proficiency.Get(definition.Style) - veteranBefore,
                    "고참이 같은 전과로 더 적게 받습니다. 보정이 부대 상태를 타고 있습니다.");
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        // ====================================================================================================
        // 4. 보정의 효과
        // ====================================================================================================

        [Test]
        public void 같은_시간을_싸우면_무기가_달라도_비슷하게_자란다()
        {
            var archer = UnitDefinition.CreateDefault(UnitRole.Archer);
            var infantry = UnitDefinition.CreateDefault(UnitRole.Infantry);

            try
            {
                const float BattleSeconds = 120f;

                int archerHits = ExpectedHits(archer, BattleSeconds);
                int infantryHits = ExpectedHits(infantry, BattleSeconds);

                Assert.Greater(infantryHits, archerHits, "전제가 깨졌습니다 — 근접이 더 많이 맞혀야 합니다.");

                var archerSquad = new CampaignSquad { Definition = archer };
                var infantrySquad = new CampaignSquad { Definition = infantry };

                _progression.Apply(archerSquad, new SquadReport { Survivors = 5, HitsLanded = archerHits });
                _progression.Apply(infantrySquad, new SquadReport { Survivors = 5, HitsLanded = infantryHits });

                int archerGain = archerSquad.Proficiency.Get(archer.Style);
                int infantryGain = infantrySquad.Proficiency.Get(infantry.Style);

                // 완전히 같을 필요는 없습니다. 보정 없이 몇 배씩 벌어지던 것이
                // 서로 견줄 만한 범위로 좁혀졌는지를 봅니다.
                float ratio = archerGain / (float)infantryGain;

                Assert.Greater(ratio, 0.5f, $"궁수 {archerGain} 대 보병 {infantryGain} — 궁수가 지나치게 뒤처집니다.");
                Assert.Less(ratio, 2.0f, $"궁수 {archerGain} 대 보병 {infantryGain} — 궁수가 지나치게 앞섭니다.");
            }
            finally
            {
                Object.DestroyImmediate(archer);
                Object.DestroyImmediate(infantry);
            }
        }

        /// <summary>무기 자체의 값으로 그 시간 동안의 기대 명중 수를 셉니다.</summary>
        /// <param name="definition">무기를 든 병과입니다.</param>
        /// <param name="seconds">싸운 시간입니다.</param>
        /// <returns>기대 명중 수입니다.</returns>
        private int ExpectedHits(UnitDefinition definition, float seconds)
        {
            float attacks = seconds / Mathf.Max(0.05f, definition.AttackInterval);

            return Mathf.RoundToInt(attacks * _progression.EstimateHitRate(definition));
        }
    }
}
