using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Systems.Battle;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 전투의 <b>계약</b>을 검증합니다 — 무엇을 받고 무엇을 내놓는가.
    ///
    /// <b>왜 이것이 캠페인의 전제인가</b>
    ///
    /// 지금까지 전투는 스스로 모든 것을 정하고, 끝나도 아무것도 남기지 않았습니다.
    /// 씬이 사라지면 전부 사라졌고 다음 전투는 언제나 처음부터였습니다.
    ///
    /// 캠페인이 붙으려면 그 둘이 뒤집혀야 합니다.
    ///   · 무엇을 데려가는지는 <b>바깥이</b> 정한다  → <see cref="BattleRequest"/>
    ///   · 무슨 일이 있었는지를 <b>돌려준다</b>      → <see cref="BattleResult"/>
    ///
    /// 그 사이를 잇는 것이 결말 판정입니다. 전투에 끝이 없으면 돌려줄 것도 없습니다.
    ///
    /// <b>분대가 단위입니다</b>
    ///
    /// 병사는 이름도 이력도 없는 인원 수입니다. 식별과 숙련과 보고는 전부 분대가 집니다.
    /// 그래서 보고서에 병사 목록이 없고 "몇 명이 남았는가"만 있습니다.
    /// </summary>
    public sealed class BattleContractTests
    {
        // ====================================================================================================
        // 1. 결말 판정
        // ====================================================================================================

        /// <summary>
        /// 아직 싸울 사람도 적도 남아 있으면 끝난 것이 아닙니다.
        /// </summary>
        [Test]
        public void 양쪽이_남아_있으면_결말이_나지_않는다()
        {
            var conclusion = new BattleConclusion();

            bool decided = conclusion.Tick(1f, playerSquadsAlive: 3, enemyUnitsAlive: 10, enemyReinforcementsExhausted: false);

            Assert.IsFalse(decided);
            Assert.AreEqual(BattleOutcome.Undecided, conclusion.Outcome);
        }

        /// <summary>
        /// <b>이 검사가 없으면 전투가 시작하자마자 끝납니다.</b>
        ///
        /// 배가 오는 사이에는 전장이 잠시 비어 있습니다.
        /// 그 순간을 승리로 읽으면 첫 파도가 도착하기도 전에 이겨 버립니다.
        /// </summary>
        [Test]
        public void 파도가_남았으면_적이_없어도_승리가_아니다()
        {
            var conclusion = new BattleConclusion();

            bool decided = conclusion.Tick(1f, playerSquadsAlive: 3, enemyUnitsAlive: 0, enemyReinforcementsExhausted: false);

            Assert.IsFalse(decided, "다음 파도가 남았는데 승리로 판정했습니다.");
            Assert.AreEqual(BattleOutcome.Undecided, conclusion.Outcome);
        }

        [Test]
        public void 마지막_파도까지_비면_승리다()
        {
            var conclusion = new BattleConclusion();

            bool decided = conclusion.Tick(1f, playerSquadsAlive: 2, enemyUnitsAlive: 0, enemyReinforcementsExhausted: true);

            Assert.IsTrue(decided);
            Assert.AreEqual(BattleOutcome.Victory, conclusion.Outcome);
        }

        [Test]
        public void 분대를_모두_잃으면_패배다()
        {
            var conclusion = new BattleConclusion();

            bool decided = conclusion.Tick(1f, playerSquadsAlive: 0, enemyUnitsAlive: 5, enemyReinforcementsExhausted: false);

            Assert.IsTrue(decided);
            Assert.AreEqual(BattleOutcome.Defeat, conclusion.Outcome);
        }

        /// <summary>
        /// 마지막 분대와 마지막 적이 같은 순간에 쓰러질 수 있습니다.
        /// <b>지킬 사람이 없으면 지켜 낸 것이 아닙니다.</b>
        /// </summary>
        [Test]
        public void 동시에_전멸하면_패배가_이긴다()
        {
            var conclusion = new BattleConclusion();

            conclusion.Tick(1f, playerSquadsAlive: 0, enemyUnitsAlive: 0, enemyReinforcementsExhausted: true);

            Assert.AreEqual(BattleOutcome.Defeat, conclusion.Outcome);
        }

        /// <summary>
        /// 한 번 정해진 결말이 뒤집히면 보고서가 두 번 나가거나 값이 달라집니다.
        /// </summary>
        [Test]
        public void 결말은_한_번만_정해진다()
        {
            var conclusion = new BattleConclusion();

            Assert.IsTrue(conclusion.Tick(1f, 0, 5, false));
            Assert.IsFalse(conclusion.Tick(1f, 3, 0, true), "이미 끝난 전투가 다시 판정되었습니다.");

            Assert.AreEqual(BattleOutcome.Defeat, conclusion.Outcome);
        }

        [Test]
        public void 흐른_시간을_센다()
        {
            var conclusion = new BattleConclusion();

            conclusion.Tick(0.5f, 3, 5, false);
            conclusion.Tick(0.25f, 3, 5, false);

            Assert.AreEqual(0.75f, conclusion.Elapsed, 0.0001f);
        }

        /// <summary>
        /// 결말이 난 뒤에는 시간이 더 흐르지 않아야 합니다.
        /// 그러지 않으면 보고서의 소요 시간이 씬을 켜 둔 시간이 됩니다.
        /// </summary>
        [Test]
        public void 결말_뒤에는_시간이_멈춘다()
        {
            var conclusion = new BattleConclusion();

            conclusion.Tick(1f, 0, 5, false);
            conclusion.Tick(10f, 0, 5, false);

            Assert.AreEqual(1f, conclusion.Elapsed, 0.0001f);
        }

        // ====================================================================================================
        // 2. 주문서
        // ====================================================================================================

        /// <summary>
        /// 분대가 없으면 전투가 조용히 텅 빈 채로 시작됩니다.
        /// 캠페인이 잘못 채운 것을 전투 도중에 알아차리면 원인을 찾기 어렵습니다.
        /// </summary>
        [Test]
        public void 분대가_없는_주문서는_거부된다()
        {
            var request = new BattleRequest();

            Assert.IsFalse(request.IsValid(out string reason));
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void 병과가_비면_거부된다()
        {
            var request = new BattleRequest();
            request.PlayerSquads.Add(new SquadOrder { Id = 1, Definition = null, SoldierCount = 5 });

            Assert.IsFalse(request.IsValid(out _));
        }

        /// <summary>
        /// 야전에서는 마주 설 부대가 곧 전투의 전제입니다.
        /// 예전에는 웨이브가 비어도 전투가 시작됐지만, 이제는 상대 없는 전장이 성립하지 않습니다.
        /// </summary>
        [Test]
        public void 상대가_없는_주문서는_거부된다()
        {
            var definition = UnitDefinition.CreateDefault(UnitRole.Infantry);

            try
            {
                var request = new BattleRequest();
                request.PlayerSquads.Add(new SquadOrder { Id = 1, Definition = definition, SoldierCount = 5 });

                Assert.IsFalse(request.IsValid(out string reason), "상대가 없는데 전투가 시작됩니다.");
                Assert.IsNotEmpty(reason);
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void 온전한_주문서는_통과한다()
        {
            var definition = UnitDefinition.CreateDefault(UnitRole.Infantry);

            try
            {
                var request = new BattleRequest();
                request.PlayerSquads.Add(new SquadOrder { Id = 1, Definition = definition, SoldierCount = 5 });

                // 야전은 마주 설 상대가 있어야 성립합니다.
                request.EnemySquads.Add(new SquadOrder { Id = 101, Definition = definition, SoldierCount = 5 });

                Assert.IsTrue(request.IsValid(out string reason), reason);
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        /// <summary>
        /// 지휘관은 인원에 따로 더해집니다. 캠페인이 임금을 셀 때 이 값을 씁니다.
        /// </summary>
        [Test]
        public void 총원은_지휘관을_포함한다()
        {
            var definition = UnitDefinition.CreateDefault(UnitRole.Infantry);

            try
            {
                var request = new BattleRequest();
                request.PlayerSquads.Add(new SquadOrder { Id = 1, Definition = definition, SoldierCount = 5 });
                request.PlayerSquads.Add(new SquadOrder { Id = 2, Definition = definition, SoldierCount = 3 });

                Assert.AreEqual(10, request.TotalSoldiers);
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        /// <summary>
        /// 숙련도는 허용 범위를 벗어날 수 없습니다.
        /// 캠페인이 잘못 쌓아 올린 값이 전투 계산을 흔들면 안 됩니다.
        /// </summary>
        [Test]
        public void 숙련도가_범위를_벗어나지_않는다()
        {
            var tooLow = new SquadOrder { Rank = -5 };
            var tooHigh = new SquadOrder { Rank = 99 };

            Assert.AreEqual(CombatConstants.MinRank, tooLow.ClampedRank());
            Assert.AreEqual(CombatConstants.MaxRank, tooHigh.ClampedRank());
        }

        /// <summary>
        /// 이름을 비우면 병과 이름을 씁니다. 캠페인이 이름을 안 붙여도 HUD가 비지 않아야 합니다.
        /// </summary>
        [Test]
        public void 이름이_없으면_병과_이름을_쓴다()
        {
            var definition = UnitDefinition.CreateDefault(UnitRole.Archer);

            try
            {
                var unnamed = new SquadOrder { Definition = definition };
                var named = new SquadOrder { Definition = definition, DisplayName = "선봉대" };

                Assert.AreEqual(definition.DisplayName, unnamed.ResolveName());
                Assert.AreEqual("선봉대", named.ResolveName());
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        // ====================================================================================================
        // 3. 보고서
        // ====================================================================================================

        /// <summary>
        /// <b>생존자 0명과 분대 소멸은 다른 말입니다.</b>
        ///
        /// 이 게임에서는 지휘관이 쓰러지면 남은 병사가 있어도 분대가 흩어집니다.
        /// 캠페인은 "몇 명 잃었는가"와 "분대가 사라졌는가"를 따로 알아야
        /// 보충을 할지 해체를 할지 정할 수 있습니다.
        /// </summary>
        [Test]
        public void 손실과_소멸을_따로_보고한다()
        {
            var result = new BattleResult { Outcome = BattleOutcome.Victory };

            result.Squads.Add(new SquadReport { Id = 1, Deployed = 6, Survivors = 4, Destroyed = false });
            result.Squads.Add(new SquadReport { Id = 2, Deployed = 6, Survivors = 0, Destroyed = true });

            Assert.AreEqual(2, result.Squads[0].Losses);
            Assert.AreEqual(6, result.Squads[1].Losses);

            Assert.AreEqual(1, result.SurvivingSquads);
            Assert.AreEqual(8, result.TotalLosses);
        }

        /// <summary>
        /// 식별자는 캠페인이 준 것을 <b>그대로</b> 돌려주어야 합니다.
        /// 전투가 번호를 다시 매기면 캠페인이 자기 분대를 못 찾습니다.
        /// </summary>
        [Test]
        public void 식별자를_그대로_돌려준다()
        {
            var result = new BattleResult();

            result.Squads.Add(new SquadReport { Id = 17, Deployed = 6, Survivors = 6 });

            Assert.AreEqual(17, result.Squads[0].Id);
        }
    }
}
