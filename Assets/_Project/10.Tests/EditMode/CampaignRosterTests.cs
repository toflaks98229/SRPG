using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Campaign;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 월드맵에 기록된 부대가 전장에 서고, 전장의 손실이 다시 월드맵에 남는지를 검증합니다.
    ///
    /// <b>왜 씬 없이 확인해야 하는가</b>
    ///
    /// 이 규칙이 깨졌을 때의 증상은 "몇 판을 치른 뒤에 부대 수가 이상하다"입니다.
    /// 재생해 가며 찾으려면 전투를 여러 판 끝까지 치러야 하고, 그때쯤이면
    /// 어느 판에서 어긋났는지 알 수 없습니다.
    ///
    /// 장부와 보고서만 놓고 보면 그 조합을 한 줄로 만들 수 있습니다.
    /// </summary>
    public sealed class CampaignRosterTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        /// <summary>검사에 쓰는 병과입니다. 각 검사가 끝나면 지웁니다.</summary>
        private UnitDefinition _definition;

        [SetUp]
        public void SetUp()
        {
            _definition = UnitDefinition.CreateDefault(UnitRole.Infantry);
        }

        [TearDown]
        public void TearDown()
        {
            if (_definition != null)
            {
                Object.DestroyImmediate(_definition);
            }
        }

        // ====================================================================================================
        // 2. 장부 → 주문서
        // ====================================================================================================

        [Test]
        public void 장부의_분대가_그대로_주문서에_오른다()
        {
            var roster = new CampaignRoster();

            roster.Enlist(_definition, 5, "선봉대");
            roster.Enlist(_definition, 3, "후위");

            var request = roster.BuildRequest(
                new[] { new SquadOrder { Id = 101, Definition = _definition, SoldierCount = 4 } },
                BattlefieldSpec.CreateDefault(),
                1234);

            Assert.AreEqual(2, request.PlayerSquads.Count, "장부의 분대 수와 주문서가 다릅니다.");
            Assert.AreEqual("선봉대", request.PlayerSquads[0].DisplayName);
            Assert.AreEqual(5, request.PlayerSquads[0].SoldierCount);
            Assert.AreEqual(3, request.PlayerSquads[1].SoldierCount);

            Assert.IsTrue(request.IsValid(out string problem), $"주문서가 온전하지 않습니다: {problem}");
        }

        [Test]
        public void 무너진_분대는_주문서에_오르지_않는다()
        {
            var roster = new CampaignRoster();

            var doomed = roster.Enlist(_definition, 5);
            roster.Enlist(_definition, 5);

            doomed.Disbanded = true;

            var request = roster.BuildRequest(
                new[] { new SquadOrder { Id = 101, Definition = _definition, SoldierCount = 4 } },
                BattlefieldSpec.CreateDefault(),
                1);

            Assert.AreEqual(1, request.PlayerSquads.Count, "무너진 분대가 전장에 섰습니다.");
        }

        // ====================================================================================================
        // 3. 보고서 → 장부
        // ====================================================================================================

        [Test]
        public void 살아_돌아온_분대는_인원이_줄어든_채로_남는다()
        {
            var roster = new CampaignRoster();
            var squad = roster.Enlist(_definition, 5);

            var result = new BattleResult { Outcome = BattleOutcome.Victory };

            // 보고서의 인원은 지휘관을 포함합니다. 6명이 나가 4명이 돌아왔으므로 병사는 3명입니다.
            result.Squads.Add(new SquadReport { Id = squad.Id, Deployed = 6, Survivors = 4 });

            roster.ApplyResult(result);

            Assert.AreEqual(3, squad.SoldierCount, "생존자에서 지휘관을 빼지 않았습니다.");
            Assert.IsFalse(squad.Disbanded);
        }

        [Test]
        public void 소멸한_분대는_장부에서_사라진다()
        {
            var roster = new CampaignRoster();
            var squad = roster.Enlist(_definition, 5);

            var result = new BattleResult { Outcome = BattleOutcome.Defeat };
            result.Squads.Add(new SquadReport { Id = squad.Id, Deployed = 6, Survivors = 2, Destroyed = true });

            roster.ApplyResult(result);

            Assert.AreEqual(0, roster.LivingSquadCount, "지휘관을 잃은 분대가 장부에 남았습니다.");
            Assert.AreEqual(0, roster.Squads.Count);
        }

        [Test]
        public void 보고에_없는_분대는_건드리지_않는다()
        {
            var roster = new CampaignRoster();

            var fought = roster.Enlist(_definition, 5);
            var stayed = roster.Enlist(_definition, 4);

            var result = new BattleResult { Outcome = BattleOutcome.Victory };
            result.Squads.Add(new SquadReport { Id = fought.Id, Deployed = 6, Survivors = 3 });

            roster.ApplyResult(result);

            Assert.AreEqual(4, stayed.SoldierCount, "전투에 나가지 않은 분대가 손실을 봤습니다.");
        }

        [Test]
        public void 보충_상한을_넘겨_불어나지_않는다()
        {
            var roster = new CampaignRoster();
            var squad = roster.Enlist(_definition, 5);

            var result = new BattleResult { Outcome = BattleOutcome.Victory };

            // 나간 인원보다 많이 돌아오는 일은 없어야 하지만, 보고가 어긋나도 장부가 불어나면 안 됩니다.
            result.Squads.Add(new SquadReport { Id = squad.Id, Deployed = 6, Survivors = 12 });

            roster.ApplyResult(result);

            Assert.AreEqual(5, squad.SoldierCount, "보충 상한을 넘겨 부대가 불어났습니다.");
        }

        // ====================================================================================================
        // 4. 여러 판에 걸친 연속성
        // ====================================================================================================

        [Test]
        public void 지난_전투의_손실이_다음_주문서에_반영된다()
        {
            var roster = new CampaignRoster();
            var squad = roster.Enlist(_definition, 5);

            var first = new BattleResult { Outcome = BattleOutcome.Victory };
            first.Squads.Add(new SquadReport { Id = squad.Id, Deployed = 6, Survivors = 4 });

            roster.ApplyResult(first);

            var next = roster.BuildRequest(
                new[] { new SquadOrder { Id = 101, Definition = _definition, SoldierCount = 4 } },
                BattlefieldSpec.CreateDefault(),
                2);

            Assert.AreEqual(
                3,
                next.PlayerSquads[0].SoldierCount,
                "다친 부대가 다음 전장에 온전한 채로 섰습니다. 캠페인이 이어지지 않습니다.");
        }
    }
}
