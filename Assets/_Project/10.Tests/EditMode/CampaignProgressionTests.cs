using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Campaign;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 전투 성과가 부대의 성장으로 옮겨지는지 검증합니다.
    ///
    /// <b>이것이 캠페인의 고리를 닫는 지점입니다</b>
    ///
    /// 여기까지 와야 "전투 → 보고 → 성장 → 다음 전투"가 한 바퀴 돕니다.
    /// 고리가 끊기면 증상은 늘 같습니다 — 몇 판을 치러도 부대가 그대로입니다.
    /// 그때 원인이 집계인지 전달인지 반영인지를 가리려면 각 마디가 따로 검사되어야 합니다.
    /// </summary>
    public sealed class CampaignProgressionTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        /// <summary>검사에 쓰는 궁수 병과입니다. 사격 계열이 오르는지 보기 위해 씁니다.</summary>
        private UnitDefinition _archer;

        /// <summary>기본 성장 곡선입니다.</summary>
        private CampaignProgression _progression;

        [SetUp]
        public void SetUp()
        {
            _archer = UnitDefinition.CreateDefault(UnitRole.Archer);
            _progression = new CampaignProgression();
        }

        [TearDown]
        public void TearDown()
        {
            if (_archer != null)
            {
                Object.DestroyImmediate(_archer);
            }
        }

        /// <summary>보고서 한 장을 만듭니다.</summary>
        /// <param name="id">분대 식별자입니다.</param>
        /// <param name="hits">명중 타격 수입니다.</param>
        /// <param name="destroyed">분대가 무너졌는지 여부입니다.</param>
        /// <returns>전황 보고 한 장입니다.</returns>
        private static SquadReport Report(int id, int hits, bool destroyed = false)
        {
            return new SquadReport
            {
                Id = id,
                Deployed = 6,
                Survivors = destroyed ? 0 : 5,
                Destroyed = destroyed,
                HitsLanded = hits,
            };
        }

        // ====================================================================================================
        // 2. 숙련도 성장
        // ====================================================================================================

        [Test]
        public void 명중이_많을수록_숙련도가_더_오른다()
        {
            var lazy = new CampaignSquad { Definition = _archer };
            var busy = new CampaignSquad { Definition = _archer };

            _progression.Apply(lazy, Report(1, 10));
            _progression.Apply(busy, Report(2, 100));

            Assert.Greater(
                busy.Proficiency.Get(AttackStyle.Projectile),
                lazy.Proficiency.Get(AttackStyle.Projectile));
        }

        [Test]
        public void 한_대도_못_맞혀도_살아_돌아오면_조금은_오른다()
        {
            var squad = new CampaignSquad { Definition = _archer };

            _progression.Apply(squad, Report(1, 0));

            Assert.Greater(
                squad.Proficiency.Get(AttackStyle.Projectile),
                0,
                "명중만으로 세면 못 맞히는 부대가 영영 늘지 않습니다.");
        }

        [Test]
        public void 자기_무기_계열만_오른다()
        {
            var squad = new CampaignSquad { Definition = _archer };

            _progression.Apply(squad, Report(1, 50));

            Assert.Greater(squad.Proficiency.Get(AttackStyle.Projectile), 0);
            Assert.AreEqual(0, squad.Proficiency.Get(AttackStyle.MeleeSwing), "활을 쏘았는데 검술이 늘었습니다.");
            Assert.AreEqual(0, squad.Proficiency.Get(AttackStyle.MeleeThrust));
        }

        [Test]
        public void 무너진_분대는_성장하지_않는다()
        {
            var squad = new CampaignSquad { Definition = _archer };

            _progression.Apply(squad, Report(1, 100, destroyed: true));

            Assert.AreEqual(0, squad.Proficiency.Get(AttackStyle.Projectile));
            Assert.AreEqual(0, squad.BattlesSurvived);
        }

        [Test]
        public void 숙련도는_상한을_넘지_않는다()
        {
            var squad = new CampaignSquad { Definition = _archer };

            for (int i = 0; i < 50; i++)
            {
                _progression.Apply(squad, Report(1, 200));
            }

            Assert.AreEqual(WeaponProficiency.MaxValue, squad.Proficiency.Get(AttackStyle.Projectile));
        }

        // ====================================================================================================
        // 3. 단련도 성장
        // ====================================================================================================

        [Test]
        public void 살아남은_전투_수로_단련도가_오른다()
        {
            var squad = new CampaignSquad { Definition = _archer };

            for (int i = 0; i < _progression.BattlesPerRank; i++)
            {
                _progression.Apply(squad, Report(1, 0));
            }

            Assert.AreEqual(CombatConstants.MinRank + 1, squad.Rank);
        }

        [Test]
        public void 단련도는_상한을_넘지_않는다()
        {
            var squad = new CampaignSquad { Definition = _archer };

            for (int i = 0; i < _progression.BattlesPerRank * (CombatConstants.MaxRank + 5); i++)
            {
                _progression.Apply(squad, Report(1, 0));
            }

            Assert.AreEqual(CombatConstants.MaxRank, squad.Rank);
        }

        [Test]
        public void 단련도는_전과를_보지_않는다()
        {
            var lazy = new CampaignSquad { Definition = _archer };
            var busy = new CampaignSquad { Definition = _archer };

            for (int i = 0; i < _progression.BattlesPerRank; i++)
            {
                _progression.Apply(lazy, Report(1, 0));
                _progression.Apply(busy, Report(2, 300));
            }

            // 단련은 '얼마나 겪었는가'입니다. 잘 싸웠는가는 숙련도가 잽니다.
            Assert.AreEqual(lazy.Rank, busy.Rank);
            Assert.Greater(
                busy.Proficiency.Get(AttackStyle.Projectile),
                lazy.Proficiency.Get(AttackStyle.Projectile));
        }

        // ====================================================================================================
        // 4. 장부를 통한 한 바퀴
        // ====================================================================================================

        [Test]
        public void 장부가_전투_보고를_받아_부대를_성장시킨다()
        {
            var roster = new CampaignRoster(_progression);
            var squad = roster.Enlist(_archer, 5, "궁수대");

            var result = new BattleResult { Outcome = BattleOutcome.Victory };
            result.Squads.Add(Report(squad.Id, 40));

            roster.ApplyResult(result);

            Assert.Greater(squad.Proficiency.Get(AttackStyle.Projectile), 0, "장부가 성장을 반영하지 않았습니다.");
            Assert.AreEqual(1, squad.BattlesSurvived);
        }

        [Test]
        public void 성장한_숙련도가_다음_주문서에_실린다()
        {
            var roster = new CampaignRoster(_progression);
            var squad = roster.Enlist(_archer, 5);

            var result = new BattleResult { Outcome = BattleOutcome.Victory };
            result.Squads.Add(Report(squad.Id, 40));

            roster.ApplyResult(result);

            var next = roster.BuildRequest(
                new[] { new SquadOrder { Id = 101, Definition = _archer, SoldierCount = 3 } },
                BattlefieldSpec.CreateDefault(),
                1);

            Assert.AreEqual(
                squad.Proficiency.Get(AttackStyle.Projectile),
                next.PlayerSquads[0].Proficiency.Get(AttackStyle.Projectile),
                "성장한 숙련도가 다음 전장에 전달되지 않아 고리가 끊깁니다.");
        }

        [Test]
        public void 무너진_분대는_성장하지도_장부에_남지도_않는다()
        {
            var roster = new CampaignRoster(_progression);
            var squad = roster.Enlist(_archer, 5);

            var result = new BattleResult { Outcome = BattleOutcome.Defeat };
            result.Squads.Add(Report(squad.Id, 30, destroyed: true));

            roster.ApplyResult(result);

            Assert.AreEqual(0, roster.LivingSquadCount);
            Assert.AreEqual(0, squad.BattlesSurvived);
        }
    }
}
