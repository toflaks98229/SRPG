using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Campaign;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 월드맵 진행 규칙을 검증합니다 — 어디로 갈 수 있고, 언제 전투가 열리고,
    /// 이긴 자리가 어떻게 달라지는지입니다.
    ///
    /// <b>씬을 열지 않습니다.</b>
    /// <see cref="CampaignDirector"/> 가 씬을 모르도록 만든 이유가 이것입니다.
    /// 규칙만 따로 확인할 수 있으면 "전투에서 진 뒤 진행이 어떻게 되는가"를
    /// 전장을 끝까지 치르지 않고도 확인할 수 있습니다.
    /// </summary>
    public sealed class CampaignDirectorTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        /// <summary>검사에 쓰는 병과입니다.</summary>
        private UnitDefinition _definition;

        /// <summary>검사에 쓰는 지도입니다.</summary>
        private WorldMapDefinition _map;

        [SetUp]
        public void SetUp()
        {
            _definition = UnitDefinition.CreateDefault(UnitRole.Infantry);
            _map = WorldMapDefinition.CreateDefault(new[] { _definition });
        }

        [TearDown]
        public void TearDown()
        {
            if (_map != null)
            {
                Object.DestroyImmediate(_map);
            }

            if (_definition != null)
            {
                Object.DestroyImmediate(_definition);
            }
        }

        /// <summary>분대 둘을 거느린 디렉터를 만듭니다.</summary>
        /// <param name="orders">주문서가 올라갈 자리입니다.</param>
        /// <returns>검사 대상 디렉터입니다.</returns>
        private CampaignDirector CreateDirector(out BattleOrders orders)
        {
            var roster = new CampaignRoster();

            roster.Enlist(_definition, 5, "선봉대");
            roster.Enlist(_definition, 5, "후위");

            orders = new BattleOrders();

            return new CampaignDirector(roster, _map, orders);
        }

        // ====================================================================================================
        // 2. 이동
        // ====================================================================================================

        [Test]
        public void 이어지지_않은_지점으로는_갈_수_없다()
        {
            var director = CreateDirector(out _);

            // 기본 지도는 0 → 1 → 2 로 한 줄입니다. 0에서 2로 바로 갈 수 없습니다.
            Assert.IsFalse(director.CanMoveTo(2), "이어지지 않은 지점으로 갈 수 있습니다.");
            Assert.IsTrue(director.CanMoveTo(1));
        }

        [Test]
        public void 지도_밖과_제자리로는_갈_수_없다()
        {
            var director = CreateDirector(out _);

            Assert.IsFalse(director.CanMoveTo(-1));
            Assert.IsFalse(director.CanMoveTo(99));
            Assert.IsFalse(director.CanMoveTo(0), "지금 있는 자리로 다시 갈 수 있습니다.");
        }

        [Test]
        public void 지점을_옮기면_하루가_간다()
        {
            var director = CreateDirector(out _);
            int before = director.Day;

            director.MoveTo(1);

            Assert.AreEqual(before + 1, director.Day);
            Assert.AreEqual(1, director.CurrentNode);
        }

        // ====================================================================================================
        // 3. 전투 개시
        // ====================================================================================================

        [Test]
        public void 적이_있는_자리에_닿으면_주문서가_올라간다()
        {
            var director = CreateDirector(out var orders);

            bool battle = director.MoveTo(1);

            Assert.IsTrue(battle, "적이 있는 자리인데 전투가 열리지 않았습니다.");
            Assert.IsNotNull(orders.Pending, "주문서가 올라가지 않았습니다.");
            Assert.AreEqual(1, orders.OriginNode);
        }

        [Test]
        public void 주문서의_아군은_장부에서_온다()
        {
            var director = CreateDirector(out var orders);

            director.MoveTo(1);

            var request = orders.Pending;

            Assert.AreEqual(2, request.PlayerSquads.Count, "장부의 분대가 주문서에 오르지 않았습니다.");
            Assert.AreEqual("선봉대", request.PlayerSquads[0].DisplayName);
            Assert.Greater(request.EnemySquads.Count, 0, "지점이 적을 채우지 않았습니다.");
            Assert.IsTrue(request.IsValid(out string problem), $"주문서가 온전하지 않습니다: {problem}");
        }

        [Test]
        public void 주문서는_한_번만_가져갈_수_있다()
        {
            var director = CreateDirector(out var orders);

            director.MoveTo(1);

            Assert.IsNotNull(orders.Take(), "올려 둔 주문서를 가져오지 못했습니다.");
            Assert.IsNull(orders.Take(), "지난 판의 주문서가 그대로 남아 다시 쓰일 수 있습니다.");
        }

        // ====================================================================================================
        // 4. 전투 결과
        // ====================================================================================================

        [Test]
        public void 이긴_자리에서는_적이_사라진다()
        {
            var director = CreateDirector(out _);

            director.MoveTo(1);

            Assert.IsTrue(director.HasEnemyAt(1));

            director.ApplyResult(new BattleResult { Outcome = BattleOutcome.Victory });

            Assert.IsFalse(director.HasEnemyAt(1), "이겼는데 같은 자리에 적이 그대로 남아 있습니다.");
        }

        [Test]
        public void 진_자리에는_적이_남는다()
        {
            var director = CreateDirector(out _);

            director.MoveTo(1);
            director.ApplyResult(new BattleResult { Outcome = BattleOutcome.Defeat });

            Assert.IsTrue(director.HasEnemyAt(1), "졌는데 적이 사라졌습니다.");
        }

        [Test]
        public void 이긴_자리의_적을_지워도_지도_에셋은_그대로다()
        {
            var director = CreateDirector(out _);

            director.MoveTo(1);
            director.ApplyResult(new BattleResult { Outcome = BattleOutcome.Victory });

            // 진행 상태를 지도에 적으면 에디터에서 그 변경이 에셋에 박혀,
            // 다음에 재생할 때 이미 이겨 놓은 지도로 시작하게 됩니다.
            Assert.IsTrue(
                _map.GetNode(1).HasEnemy,
                "이번 회차의 진행이 지도 에셋에 기록되었습니다. 다음 재생이 오염됩니다.");
        }

        [Test]
        public void 부대를_모두_잃으면_캠페인이_끝난다()
        {
            var director = CreateDirector(out _);

            director.MoveTo(1);

            var result = new BattleResult { Outcome = BattleOutcome.Defeat };

            var squads = director.Roster.Squads;

            for (int i = squads.Count - 1; i >= 0; i--)
            {
                result.Squads.Add(new SquadReport
                {
                    Id = squads[i].Id, Deployed = 6, Survivors = 0, Destroyed = true,
                });
            }

            director.ApplyResult(result);

            Assert.IsTrue(director.IsOver, "부대를 모두 잃었는데 캠페인이 계속됩니다.");
        }
    }
}
