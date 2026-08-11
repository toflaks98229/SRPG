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

        /// <summary>분대 둘을 거느리고 <b>둘 다 출진하도록</b> 편성된 디렉터를 만듭니다.</summary>
        /// <param name="orders">주문서가 올라갈 자리입니다.</param>
        /// <returns>검사 대상 디렉터입니다.</returns>
        private CampaignDirector CreateDirector(out BattleOrders orders)
        {
            return CreateDirector(out orders, out _);
        }

        /// <summary>
        /// 분대 둘을 거느린 디렉터를 만듭니다. 편성은 회차 시작과 같이 상한까지 채워 둡니다.
        ///
        /// 채워 두지 않으면 싸울 자리로 갈 수 없으므로, 이동을 보는 검사가 전부
        /// 편성을 먼저 손봐야 합니다. 그것은 이 검사들이 보려는 것이 아닙니다.
        /// </summary>
        /// <param name="orders">주문서가 올라갈 자리입니다.</param>
        /// <param name="roster">거느린 부대의 장부입니다.</param>
        /// <returns>검사 대상 디렉터입니다.</returns>
        private CampaignDirector CreateDirector(out BattleOrders orders, out CampaignRoster roster)
        {
            roster = new CampaignRoster();

            roster.Enlist(_definition, 5, "선봉대");
            roster.Enlist(_definition, 5, "후위");

            orders = new BattleOrders();

            var plan = new DeploymentPlan(new CampaignTuning { MarchSquadCap = 3 });
            plan.Refill(roster.Squads);

            return new CampaignDirector(roster, _map, orders, plan);
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

        // ====================================================================================================
        // 6. 출진 편성
        // ====================================================================================================

        /// <summary>
        /// 편성이 비면 싸울 자리로 발을 떼지 않습니다.
        ///
        /// <b>옮긴 뒤에 막으면 안 됩니다.</b> 그러면 하루만 날아가고 부대는 엉뚱한 자리에 서 있게 됩니다.
        /// </summary>
        [Test]
        public void 편성이_비면_싸울_자리로_갈_수_없다()
        {
            var director = CreateDirector(out var orders);

            director.Deployment.Clear();

            int day = director.Day;
            int node = director.CurrentNode;

            Assert.IsFalse(director.MoveTo(1), "아무도 없이 전장에 들어섰습니다.");
            Assert.AreEqual(node, director.CurrentNode, "막혔는데 자리를 옮겼습니다.");
            Assert.AreEqual(day, director.Day, "막혔는데 하루가 갔습니다.");
            Assert.IsNull(orders.Pending, "아군 없는 주문서가 올라갔습니다.");
        }

        /// <summary>
        /// 싸울 일이 없는 자리로는 편성이 비어도 갈 수 있습니다.
        ///
        /// 편성은 <b>전투의 조건</b>이지 이동의 조건이 아닙니다.
        /// 이것을 막으면 부대를 물릴 수도 없어 캠페인이 그 자리에서 잠깁니다.
        ///
        /// <b><c>MoveTo</c> 의 반환값으로 판단하지 않습니다.</b>
        /// 저것은 "전투가 열렸는가"이지 "갔는가"가 아니라서, 조용한 자리로 잘 옮겨도 false 입니다.
        /// 실제로 옮겼는지는 <c>CurrentNode</c> 로 봅니다.
        /// </summary>
        [Test]
        public void 편성이_비어도_싸울_일이_없는_자리로는_간다()
        {
            var director = CreateDirector(out _);

            director.MoveTo(1);
            director.ApplyResult(new BattleResult { Outcome = BattleOutcome.Victory });

            director.Deployment.Clear();

            director.MoveTo(2);
            Assert.AreEqual(1, director.CurrentNode, "적이 있는 자리로 편성 없이 갔습니다.");

            director.MoveTo(0);
            Assert.AreEqual(0, director.CurrentNode, "쓸어 낸 자리로 물러나지도 못합니다.");
        }

        /// <summary>
        /// 고르지 않은 분대는 전장에 서지 않습니다.
        ///
        /// 디렉터를 거쳐 실제 주문서까지 이어지는지를 봅니다 —
        /// 장부 쪽 규칙만 맞고 배선이 끊겨 있으면 화면에서는 알 수 없습니다.
        /// </summary>
        [Test]
        public void 고른_분대만_주문서에_오른다()
        {
            var director = CreateDirector(out var orders, out var roster);

            var going = roster.Squads[0];

            director.Deployment.Clear();
            director.Deployment.Select(going.Id);

            director.MoveTo(1);

            Assert.AreEqual(1, orders.Pending.PlayerSquads.Count, "두고 온 분대가 따라 나갔습니다.");
            Assert.AreEqual(going.Id, orders.Pending.PlayerSquads[0].Id);
        }

        /// <summary>
        /// 데려간 부대를 모두 잃어도 남은 부대가 있으면 캠페인은 끝나지 않습니다.
        ///
        /// <b>편성이 생기면서 새로 갈라진 상황입니다.</b> 예전에는 전부 데리고 나갔으므로
        /// 전멸이 곧 캠페인의 끝이었습니다. 지금은 뼈아픈 한 판과 전멸이 다른 것이어야 하고,
        /// 그 구분이 이 게임에서 후퇴가 의미를 갖는 지점입니다.
        /// </summary>
        [Test]
        public void 데려간_부대를_잃어도_남은_부대가_있으면_끝이_아니다()
        {
            var director = CreateDirector(out _, out var roster);

            var going = roster.Squads[0];
            var staying = roster.Squads[1];

            director.Deployment.Clear();
            director.Deployment.Select(going.Id);

            director.MoveTo(1);

            var result = new BattleResult { Outcome = BattleOutcome.Defeat };
            result.Squads.Add(new SquadReport
            {
                Id = going.Id, Deployed = 6, Survivors = 0, Destroyed = true,
            });

            director.ApplyResult(result);

            Assert.IsFalse(director.IsOver, "본진에 부대가 남았는데 캠페인이 끝났습니다.");
            Assert.AreEqual(1, director.Roster.LivingSquadCount);
            Assert.AreEqual(staying.Id, director.Roster.Squads[0].Id, "두고 온 부대가 손실을 입었습니다.");
        }

        /// <summary>
        /// 무너진 분대는 편성에서도 걷힙니다.
        ///
        /// 남아 있으면 다음 출진에서 그 자리가 채워진 것으로 세어져
        /// 실제보다 적은 부대를 데리고 나갑니다. 오류는 나지 않습니다.
        /// </summary>
        [Test]
        public void 무너진_분대는_편성에서_걷힌다()
        {
            var director = CreateDirector(out _, out var roster);

            var lost = roster.Squads[0];
            var kept = roster.Squads[1];

            director.MoveTo(1);

            var result = new BattleResult { Outcome = BattleOutcome.Victory };
            result.Squads.Add(new SquadReport
            {
                Id = lost.Id, Deployed = 6, Survivors = 0, Destroyed = true,
            });
            result.Squads.Add(new SquadReport { Id = kept.Id, Deployed = 6, Survivors = 5 });

            director.ApplyResult(result);

            CollectionAssert.AreEqual(new[] { kept.Id }, director.Deployment.Selected);
            Assert.IsTrue(director.Deployment.HasRoom, "사라진 분대가 자리를 계속 차지하고 있습니다.");
        }
    }
}
