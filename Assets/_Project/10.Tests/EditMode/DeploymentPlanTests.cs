using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Campaign;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 출진 편성의 규칙을 검증합니다.
    ///
    /// <b>왜 이 검사가 필요한가</b>
    ///
    /// 편성이 잘못되어도 오류는 나지 않습니다. 증상은 전부 "전장에 선 부대가 생각과 다르다" 하나이고,
    /// 그것을 확인하려면 전투를 열어 세어 봐야 합니다. 규칙이 <c>MonoBehaviour</c> 밖에 있으므로
    /// 여기서 직접 물을 수 있습니다.
    /// </summary>
    public sealed class DeploymentPlanTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        /// <summary>검사에 쓰는 병과입니다.</summary>
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
        // 2. 고르기
        // ====================================================================================================

        [Test]
        public void 고른_것과_고르지_않은_것이_구분된다()
        {
            var plan = new DeploymentPlan(Tuning(cap: 3));

            plan.Select(1);

            Assert.IsTrue(plan.IsSelected(1));
            Assert.IsFalse(plan.IsSelected(2));
            Assert.AreEqual(1, plan.Count);
        }

        [Test]
        public void 같은_분대를_두_번_고를_수_없다()
        {
            var plan = new DeploymentPlan(Tuning(cap: 3));

            Assert.IsTrue(plan.Select(1));
            Assert.IsFalse(plan.Select(1), "같은 분대가 두 번 나가려 합니다.");
            Assert.AreEqual(1, plan.Count);
        }

        [Test]
        public void 고른_순서가_유지된다()
        {
            var plan = new DeploymentPlan(Tuning(cap: 3));

            plan.Select(7);
            plan.Select(3);
            plan.Select(5);

            // 전개기가 앞에서부터 자리를 채우므로 이 순서가 곧 전장에 먼저 서는 순서입니다.
            CollectionAssert.AreEqual(new[] { 7, 3, 5 }, plan.Selected);
        }

        // ====================================================================================================
        // 3. 상한
        // ====================================================================================================

        [Test]
        public void 상한을_넘겨_고를_수_없다()
        {
            var plan = new DeploymentPlan(Tuning(cap: 2));

            Assert.IsTrue(plan.Select(1));
            Assert.IsTrue(plan.Select(2));
            Assert.IsFalse(plan.Select(3), "상한을 넘겨 데리고 나가려 합니다.");

            Assert.AreEqual(2, plan.Count);
            Assert.IsFalse(plan.HasRoom);
        }

        /// <summary>
        /// 자리가 찼어도 하나를 빼면 다른 하나를 넣을 수 있습니다.
        ///
        /// 이것이 안 되면 편성을 바꾸려고 전부 지웠다 다시 골라야 합니다.
        /// </summary>
        [Test]
        public void 하나를_빼면_다른_하나를_넣을_수_있다()
        {
            var plan = new DeploymentPlan(Tuning(cap: 2));

            plan.Select(1);
            plan.Select(2);

            plan.Deselect(1);

            Assert.IsTrue(plan.Select(3));
            CollectionAssert.AreEqual(new[] { 2, 3 }, plan.Selected);
        }

        /// <summary>
        /// 최소치가 상한보다 크게 적혀 있어도 아무도 출진할 수 없게 되지는 않습니다.
        ///
        /// 인스펙터에서 한 번 잘못 적으면 증상이 "이동 버튼이 안 눌린다" 하나뿐이라
        /// 원인을 찾기 어렵습니다.
        /// </summary>
        [Test]
        public void 최소치가_상한보다_커도_출진할_수_있다()
        {
            var plan = new DeploymentPlan(Tuning(cap: 1, minimum: 3));

            Assert.GreaterOrEqual(plan.Cap, plan.Minimum, "고를 수 있는 수가 최소치보다 적습니다.");

            for (int id = 1; id <= plan.Minimum; id++)
            {
                Assert.IsTrue(plan.Select(id), $"{id}번을 고를 자리가 없습니다.");
            }

            Assert.IsTrue(plan.IsReady);
        }

        // ====================================================================================================
        // 4. 출진 가능 여부
        // ====================================================================================================

        [Test]
        public void 아무도_고르지_않으면_출진할_수_없다()
        {
            var plan = new DeploymentPlan(Tuning(cap: 3));

            Assert.IsFalse(plan.IsReady, "아무도 없이 전장에 들어서려 합니다.");

            plan.Select(1);

            Assert.IsTrue(plan.IsReady);
        }

        [Test]
        public void 전부_뺐다가_다시_고르면_출진할_수_있다()
        {
            var plan = new DeploymentPlan(Tuning(cap: 3));

            plan.Select(1);
            plan.Clear();

            Assert.IsFalse(plan.IsReady);

            plan.Toggle(2);

            Assert.IsTrue(plan.IsReady);
        }

        // ====================================================================================================
        // 5. 장부와 맞추기
        // ====================================================================================================

        /// <summary>
        /// 장부에서 사라진 분대는 편성에서도 걷힙니다.
        ///
        /// <b>이것을 빠뜨리면 다음 출진에서 부대가 조용히 줄어듭니다.</b>
        /// 사라진 식별자가 자리를 차지한 채로 세어지기 때문입니다. 오류는 나지 않습니다.
        /// </summary>
        [Test]
        public void 사라진_분대는_편성에서_걷힌다()
        {
            var roster = new CampaignRoster();
            var stays = roster.Enlist(_definition, 5);
            var falls = roster.Enlist(_definition, 5);

            var plan = new DeploymentPlan(Tuning(cap: 3));
            plan.Select(stays.Id);
            plan.Select(falls.Id);

            falls.Disbanded = true;

            Assert.AreEqual(1, plan.Retain(roster.Squads));
            CollectionAssert.AreEqual(new[] { stays.Id }, plan.Selected);
            Assert.IsTrue(plan.HasRoom, "사라진 분대가 자리를 계속 차지하고 있습니다.");
        }

        [Test]
        public void 장부에_없는_식별자도_걷힌다()
        {
            var roster = new CampaignRoster();
            var real = roster.Enlist(_definition, 5);

            var plan = new DeploymentPlan(Tuning(cap: 3));
            plan.Select(real.Id);
            plan.Select(999);

            plan.Retain(roster.Squads);

            CollectionAssert.AreEqual(new[] { real.Id }, plan.Selected);
        }

        // ====================================================================================================
        // 6. 처음 채우기
        // ====================================================================================================

        /// <summary>
        /// 회차를 시작할 때 상한까지 채웁니다.
        ///
        /// 처음 월드맵을 열었을 때 아무도 고르지 않은 채 이동이 막혀 있으면
        /// 그것은 규칙이 아니라 고장으로 보입니다.
        /// </summary>
        [Test]
        public void 처음에는_상한까지_장부_순서로_채운다()
        {
            var roster = new CampaignRoster();
            var first = roster.Enlist(_definition, 5);
            var second = roster.Enlist(_definition, 5);
            roster.Enlist(_definition, 5);

            var plan = new DeploymentPlan(Tuning(cap: 2));

            Assert.AreEqual(2, plan.Refill(roster.Squads));
            CollectionAssert.AreEqual(new[] { first.Id, second.Id }, plan.Selected);
        }

        [Test]
        public void 채울_때_무너진_분대는_건너뛴다()
        {
            var roster = new CampaignRoster();
            var doomed = roster.Enlist(_definition, 5);
            var alive = roster.Enlist(_definition, 5);

            doomed.Disbanded = true;

            var plan = new DeploymentPlan(Tuning(cap: 3));
            plan.Refill(roster.Squads);

            CollectionAssert.AreEqual(new[] { alive.Id }, plan.Selected);
        }

        /// <summary>
        /// 이미 고른 것이 있으면 빈 자리만 채웁니다. 고른 것을 밀어내지 않습니다.
        /// </summary>
        [Test]
        public void 채우기가_이미_고른_것을_밀어내지_않는다()
        {
            var roster = new CampaignRoster();
            roster.Enlist(_definition, 5);
            var second = roster.Enlist(_definition, 5);

            var plan = new DeploymentPlan(Tuning(cap: 2));
            plan.Select(second.Id);

            plan.Refill(roster.Squads);

            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual(second.Id, plan.Selected[0], "먼저 고른 것이 뒤로 밀렸습니다.");
        }

        // ====================================================================================================
        // 7. Helpers
        // ====================================================================================================

        /// <summary>검사용 출진 규칙을 만듭니다.</summary>
        /// <param name="cap">출진 상한입니다.</param>
        /// <param name="minimum">최소 출진 수입니다.</param>
        /// <returns>그 값이 담긴 규칙입니다.</returns>
        private static CampaignTuning Tuning(int cap, int minimum = 1)
        {
            return new CampaignTuning
            {
                MarchSquadCap = cap,
                MinMarchSquads = minimum,
            };
        }
    }
}
