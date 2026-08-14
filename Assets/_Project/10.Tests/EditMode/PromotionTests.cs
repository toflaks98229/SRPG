using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Campaign;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 성과 기반 승급과 특전 선택을 검증합니다.
    ///
    /// <b>무엇이 바뀌었는가</b>
    ///
    /// 예전에는 단련도가 <b>살아 돌아온 횟수</b>였습니다. 겪은 만큼 오르는 값이라
    /// 판마다의 결과가 성장에 닿지 않았습니다. 지금은 공적이 문턱을 넘을 때 승급합니다.
    ///
    /// 그 전환에는 함께 옮겨야 하는 것이 하나 있습니다 — <b>무기별 공정성 보정</b>입니다.
    /// 숙련도가 이미 겪은 문제인데(자주 때리는 무기가 저절로 빨리 자람), 승급으로 자리만
    /// 옮기면 같은 결함이 되살아납니다. 그것을 여기서 못 박습니다.
    /// </summary>
    public sealed class PromotionTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        private CampaignProgression _progression;
        private UnitDefinition _infantry;
        private UnitDefinition _archer;

        [SetUp]
        public void SetUp()
        {
            _progression = new CampaignProgression();
            _infantry = UnitDefinition.CreateDefault(UnitRole.Infantry);
            _archer = UnitDefinition.CreateDefault(UnitRole.Archer);
        }

        [TearDown]
        public void TearDown()
        {
            if (_infantry != null) Object.DestroyImmediate(_infantry);
            if (_archer != null) Object.DestroyImmediate(_archer);
        }

        /// <summary>제안 목록에 그 특전이 들어 있는지 봅니다.</summary>
        /// <param name="offer">제안 목록입니다.</param>
        /// <param name="kind">찾을 특전입니다.</param>
        /// <returns>들어 있으면 true입니다.</returns>
        private static bool Offered(IReadOnlyList<SquadPerkKind> offer, SquadPerkKind kind)
        {
            for (int i = 0; i < offer.Count; i++)
            {
                if (offer[i] == kind)
                {
                    return true;
                }
            }

            return false;
        }

        private static SquadReport Report(int hits, int survivors = 6, int deployed = 6)
        {
            return new SquadReport
            {
                Id = 1,
                Deployed = deployed,
                Survivors = survivors,
                Destroyed = false,
                HitsLanded = hits,
            };
        }

        // ====================================================================================================
        // 2. 승급은 성과가 정한다
        // ====================================================================================================

        /// <summary>
        /// 잘 싸운 분대가 먼저 승급합니다.
        ///
        /// 이것이 이번 변경의 핵심입니다. 예전에는 둘이 같은 랭크였습니다.
        /// </summary>
        [Test]
        public void 잘_싸운_분대가_먼저_승급한다()
        {
            var lazy = new CampaignSquad { Id = 1, Definition = _infantry };
            var busy = new CampaignSquad { Id = 2, Definition = _infantry };

            for (int i = 0; i < 3; i++)
            {
                _progression.Apply(lazy, Report(hits: 1));
                _progression.Apply(busy, Report(hits: 60));
            }

            Assert.Greater(
                busy.Rank,
                lazy.Rank,
                "전과가 승급에 닿지 않고 있습니다. 승급이 다시 전투 횟수만 세고 있는지 확인하십시오.");
        }

        /// <summary>
        /// 온전히 지켜 낸 분대가 더 많이 얻습니다.
        ///
        /// 명중만 보면 병사를 갈아 넣고 이기는 쪽이 빨리 자랍니다.
        /// </summary>
        [Test]
        public void 온전히_지켜_내면_공적이_더_쌓인다()
        {
            float intact = _progression.ScoreMerit(_infantry, Report(hits: 10, survivors: 6, deployed: 6));
            float bloodied = _progression.ScoreMerit(_infantry, Report(hits: 10, survivors: 2, deployed: 6));

            Assert.Greater(intact, bloodied, "같은 전과인데 온전함이 공적에 반영되지 않았습니다.");
        }

        /// <summary>
        /// <b>무기가 달라도 같은 시간을 싸우면 비슷하게 승급합니다.</b>
        ///
        /// 보병은 1초마다 휘두르고 거의 다 닿습니다. 궁수는 1.4초마다 쏘고 상당수가 빗나갑니다.
        /// 명중 수를 날것으로 세면 보병이 저절로 빨리 승급하고, 무기를 고르는 이유가
        /// 성능이 아니라 "빨리 크는 쪽"이 됩니다.
        ///
        /// 숙련도가 <c>GetExperienceScale</c> 로 이미 푼 문제인데, 승급이 그 보정을 빠뜨리면
        /// 같은 결함이 자리만 옮겨 되살아납니다. 이 검사가 그것을 막습니다.
        /// </summary>
        [Test]
        public void 무기가_달라도_같은_시간이면_비슷하게_승급한다()
        {
            const float Seconds = 60f;

            // <b>"같은 시간"은 각자의 기대 명중 수만큼 맞혔다는 뜻입니다.</b>
            //
            // 처음에는 "공격 간격으로 나눈 횟수만큼 전부 맞혔다"로 뒀다가 틀렸습니다.
            // 그러면 궁수가 실제로는 상당수 빗나가는데도 백발백중한 것이 되어,
            // 보정이 제대로 걸려 있는데도 검사가 실패합니다.
            //
            // 보정치는 기대 명중 수의 역수이므로(<c>GetExperienceScale</c>),
            // 시간을 그것으로 나누면 그 무기가 그 시간에 낼 기대 명중 수가 나옵니다.
            int infantryHits = ExpectedHits(_infantry, Seconds);
            int archerHits = ExpectedHits(_archer, Seconds);

            Assert.AreNotEqual(
                infantryHits, archerHits, "전제가 성립하지 않습니다 — 두 무기의 기대 명중 수가 같습니다.");

            float infantryMerit = _progression.ScoreMerit(_infantry, Report(infantryHits));
            float archerMerit = _progression.ScoreMerit(_archer, Report(archerHits));

            float ratio = infantryMerit / Mathf.Max(0.01f, archerMerit);

            Assert.That(
                ratio,
                Is.EqualTo(1f).Within(0.15f),
                $"같은 시간을 싸웠는데 공적이 {ratio:F2}배 차이납니다. " +
                $"(보병 명중 {infantryHits} → {infantryMerit:F2}, 궁수 명중 {archerHits} → {archerMerit:F2}) " +
                "무기별 보정이 빠졌습니다.");
        }

        /// <summary>이 무기가 주어진 시간에 낼 기대 명중 수입니다.</summary>
        /// <param name="definition">병과 정의입니다.</param>
        /// <param name="seconds">싸운 시간입니다.</param>
        /// <returns>기대 명중 수입니다.</returns>
        private int ExpectedHits(UnitDefinition definition, float seconds)
        {
            return Mathf.RoundToInt(seconds / Mathf.Max(0.01f, _progression.GetExperienceScale(definition)));
        }

        /// <summary>
        /// 공적은 줄지 않습니다. 못 싸운 판도 깎지는 않습니다.
        ///
        /// 깎으면 플레이어가 <b>지는 판을 피하려고</b> 위험한 곳에 부대를 보내지 않게 됩니다.
        /// </summary>
        [Test]
        public void 공적은_줄지_않는다()
        {
            var squad = new CampaignSquad { Id = 1, Definition = _infantry };

            _progression.Apply(squad, Report(hits: 50));
            float after = squad.Merit;

            _progression.Apply(squad, Report(hits: 0, survivors: 1, deployed: 6));

            Assert.GreaterOrEqual(squad.Merit, after, "못 싸운 판이 공적을 깎았습니다.");
        }

        /// <summary>단련도는 상한을 넘지 않습니다.</summary>
        [Test]
        public void 단련도가_상한을_넘지_않는다()
        {
            var squad = new CampaignSquad { Id = 1, Definition = _infantry };

            for (int i = 0; i < 200; i++)
            {
                _progression.Apply(squad, Report(hits: 100));
            }

            Assert.AreEqual(CombatConstants.MaxRank, squad.Rank);
        }

        // ====================================================================================================
        // 3. 특전 — 장비가 아니다
        // ====================================================================================================

        /// <summary>
        /// 제안은 언제나 세 개이고, <b>이미 가진 것은 다시 나오지 않습니다.</b>
        /// </summary>
        [Test]
        public void 제안은_가진_것을_빼고_세_개다()
        {
            var owned = new List<SquadPerkKind> { SquadPerkKind.Hardened, SquadPerkKind.Deadly };
            var offer = new List<SquadPerkKind>();

            SquadPerks.BuildOffer(owned, seed: 12345, offer);

            Assert.AreEqual(SquadPerks.OfferSize, offer.Count);

            for (int i = 0; i < offer.Count; i++)
            {
                Assert.IsFalse(owned.Contains(offer[i]), $"이미 가진 {offer[i]} 가 다시 제안되었습니다.");
            }

            CollectionAssert.AllItemsAreUnique(offer, "같은 특전이 두 번 제안되었습니다.");
        }

        /// <summary>
        /// <b>같은 씨앗은 같은 제안을 냅니다.</b>
        ///
        /// 매번 달라지면 마음에 들 때까지 화면을 다시 여는 놀이가 되고,
        /// 고르는 일이 선택이 아니라 뽑기가 됩니다.
        /// </summary>
        [Test]
        public void 같은_씨앗은_같은_제안을_낸다()
        {
            var first = new List<SquadPerkKind>();
            var second = new List<SquadPerkKind>();

            SquadPerks.BuildOffer(null, seed: 777, first);
            SquadPerks.BuildOffer(null, seed: 777, second);

            CollectionAssert.AreEqual(first, second, "같은 씨앗인데 제안이 달라졌습니다.");
        }

        /// <summary>전부 가졌으면 제안이 비고, 게시판은 묻지 않습니다.</summary>
        [Test]
        public void 전부_가졌으면_묻지_않는다()
        {
            var owned = new List<SquadPerkKind>();

            foreach (SquadPerkKind kind in System.Enum.GetValues(typeof(SquadPerkKind)))
            {
                if (kind != SquadPerkKind.None)
                {
                    owned.Add(kind);
                }
            }

            var offer = new List<SquadPerkKind>();
            Assert.AreEqual(0, SquadPerks.BuildOffer(owned, seed: 1, offer));

            var board = new PromotionBoard();
            var squad = new CampaignSquad { Id = 1, Definition = _infantry, Rank = 2 };
            squad.Perks.AddRange(owned);

            board.Enqueue(squad, fromRank: 1, seed: 1);

            Assert.IsFalse(board.HasPending, "고를 것이 없는데 질문이 쌓였습니다.");
        }

        /// <summary>
        /// 특전은 배율로만 표현됩니다 — <b>장비를 주지 않습니다.</b>
        ///
        /// 승급이 장비를 주기 시작하면 상점에서 살 이유가 사라집니다.
        /// 이 검사는 목록의 모든 항목이 <see cref="UnitModifiers"/> 안에 머무는지를 봅니다.
        /// </summary>
        [Test]
        public void 특전은_배율일_뿐_장비가_아니다()
        {
            foreach (SquadPerkKind kind in System.Enum.GetValues(typeof(SquadPerkKind)))
            {
                if (kind == SquadPerkKind.None)
                {
                    continue;
                }

                Assert.IsTrue(SquadPerks.TryGet(kind, out var perk), $"{kind} 가 목록에 없습니다.");

                var m = perk.Modifiers;

                Assert.Greater(m.Health, 0f, $"{kind} 의 체력 배율이 0 이하입니다.");
                Assert.Greater(m.Damage, 0f);
                Assert.Greater(m.AttackSpeed, 0f);
                Assert.Greater(m.MoveSpeed, 0f);
                Assert.Greater(m.Accuracy, 0f);
                Assert.Greater(m.Knockback, 0f);
                Assert.Greater(m.ProjectileResistance, 0f);
            }
        }

        /// <summary>빈 목록을 합치면 아무것도 바뀌지 않습니다.</summary>
        [Test]
        public void 특전이_없으면_배율이_1이다()
        {
            var combined = SquadPerks.Combine(null);

            Assert.AreEqual(1f, combined.Health, 1e-4f);
            Assert.AreEqual(1f, combined.Damage, 1e-4f);
        }

        // ====================================================================================================
        // 4. 게시판
        // ====================================================================================================

        /// <summary>고른 특전이 장부에 실제로 붙습니다.</summary>
        [Test]
        public void 고른_특전이_장부에_붙는다()
        {
            var roster = new CampaignRoster(_progression);
            var squad = roster.Enlist(_infantry, 5);

            var board = new PromotionBoard();
            board.Enqueue(squad, fromRank: 1, seed: 42);

            Assert.IsTrue(board.HasPending);

            var chosen = board.Current.Offer[0];

            Assert.IsTrue(board.Choose(roster, chosen));
            Assert.Contains(chosen, squad.Perks);
            Assert.IsFalse(board.HasPending, "답했는데 질문이 남아 있습니다.");
        }

        /// <summary>
        /// <b>제안에 없던 특전은 거절합니다.</b>
        /// 화면이 잘못 그려져도 규칙이 무너지지 않아야 합니다.
        /// </summary>
        [Test]
        public void 제안에_없던_특전은_거절한다()
        {
            var roster = new CampaignRoster(_progression);
            var squad = roster.Enlist(_infantry, 5);

            var board = new PromotionBoard();
            board.Enqueue(squad, fromRank: 1, seed: 42);

            SquadPerkKind outsider = SquadPerkKind.None;

            foreach (SquadPerkKind kind in System.Enum.GetValues(typeof(SquadPerkKind)))
            {
                if (kind == SquadPerkKind.None || Offered(board.Current.Offer, kind))
                {
                    continue;
                }

                outsider = kind;
                break;
            }

            Assert.AreNotEqual(SquadPerkKind.None, outsider, "전제가 성립하지 않습니다.");

            Assert.IsFalse(board.Choose(roster, outsider), "제안에 없던 특전이 받아들여졌습니다.");
            Assert.IsEmpty(squad.Perks);
            Assert.IsTrue(board.HasPending, "거절했는데 질문이 사라졌습니다.");
        }

        /// <summary>여럿이 승급하면 하나씩 묻습니다.</summary>
        [Test]
        public void 여럿이_승급하면_하나씩_묻는다()
        {
            var roster = new CampaignRoster(_progression);
            var first = roster.Enlist(_infantry, 5);
            var second = roster.Enlist(_archer, 5);

            var board = new PromotionBoard();
            board.Enqueue(first, 1, 1);
            board.Enqueue(second, 1, 2);

            Assert.AreEqual(2, board.PendingCount);
            Assert.AreEqual(first.Id, board.Current.SquadId);

            board.Choose(roster, board.Current.Offer[0]);

            Assert.AreEqual(1, board.PendingCount);
            Assert.AreEqual(second.Id, board.Current.SquadId);
        }
    }
}
