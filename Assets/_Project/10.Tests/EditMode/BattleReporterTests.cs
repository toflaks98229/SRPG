using NUnit.Framework;
using SRPG.Data;
using SRPG.Gameplay.Battle;

namespace SRPG.Tests
{
    /// <summary>
    /// 전황 관측과 보고서 작성을 검증합니다.
    ///
    /// <b>왜 씬 없이 확인해야 하는가</b>
    ///
    /// "왜 승리 처리가 안 됐는가"는 재생해 가며 찾기 가장 나쁜 종류의 질문입니다.
    /// 조건이 겹쳐야 재현되고(마지막 파도가 나왔는가, 분대가 남았는가),
    /// 그 조건을 손으로 만들려면 전투를 끝까지 치러야 합니다.
    ///
    /// 관측값을 직접 넣을 수 있으면 그 조합을 한 줄로 만들 수 있습니다.
    /// </summary>
    public sealed class BattleReporterTests
    {
        // ====================================================================================================
        // 1. Fake
        // ====================================================================================================

        /// <summary>
        /// 분대 흉내만 냅니다. 보고서가 보는 것은 이 둘뿐입니다.
        /// </summary>
        private sealed class FakeSquad : ISquadStatus
        {
            public bool IsDestroyed { get; set; }

            public int AliveCount { get; set; }
        }

        private const float Step = 0.1f;

        // ====================================================================================================
        // 2. 결말 판정
        // ====================================================================================================

        [Test]
        public void 분대가_남아_있고_적도_남아_있으면_끝나지_않는다()
        {
            var reporter = new BattleReporter();
            reporter.Track(1, new FakeSquad { AliveCount = 6 }, 6);

            Assert.IsNull(reporter.Tick(Step, enemiesAlive: 3, playerReserves: 0, enemyReinforcementsExhausted: false));
            Assert.IsFalse(reporter.IsDecided);
            Assert.AreEqual(BattleOutcome.Undecided, reporter.Outcome);
        }

        [Test]
        public void 분대를_모두_잃으면_패배다()
        {
            var reporter = new BattleReporter();
            reporter.Track(1, new FakeSquad { IsDestroyed = true }, 6);

            var result = reporter.Tick(Step, enemiesAlive: 3, playerReserves: 0, enemyReinforcementsExhausted: false);

            Assert.IsNotNull(result);
            Assert.AreEqual(BattleOutcome.Defeat, result.Outcome);
        }

        [Test]
        public void 더_올라올_적이_없고_전장도_비면_승리다()
        {
            var reporter = new BattleReporter();
            reporter.Track(1, new FakeSquad { AliveCount = 4 }, 6);

            var result = reporter.Tick(Step, enemiesAlive: 0, playerReserves: 0, enemyReinforcementsExhausted: true);

            Assert.IsNotNull(result);
            Assert.AreEqual(BattleOutcome.Victory, result.Outcome);
        }

        /// <summary>
        /// 앞 부대를 갈아 낸 그 순간을 승리로 읽으면, 뒤에 두 배가 남아 있어도 전투가 끝납니다.
        /// </summary>
        [Test]
        public void 지원군이_남았으면_적이_없어도_승리가_아니다()
        {
            var reporter = new BattleReporter();
            reporter.Track(1, new FakeSquad { AliveCount = 6 }, 6);

            Assert.IsNull(reporter.Tick(Step, enemiesAlive: 0, playerReserves: 0, enemyReinforcementsExhausted: false));
        }

        /// <summary>
        /// 마지막 분대와 마지막 적이 같은 순간에 쓰러질 수 있습니다.
        /// 지킬 사람이 없으면 지켜 낸 것이 아니므로 패배입니다.
        /// </summary>
        [Test]
        public void 동시에_쓰러지면_패배가_먼저다()
        {
            var reporter = new BattleReporter();
            reporter.Track(1, new FakeSquad { IsDestroyed = true }, 6);

            var result = reporter.Tick(Step, enemiesAlive: 0, playerReserves: 0, enemyReinforcementsExhausted: true);

            Assert.AreEqual(BattleOutcome.Defeat, result.Outcome);
        }

        [Test]
        public void 보고서는_한_번만_나온다()
        {
            var reporter = new BattleReporter();
            reporter.Track(1, new FakeSquad { AliveCount = 4 }, 6);

            Assert.IsNotNull(reporter.Tick(Step, 0, 0, true), "첫 보고가 나오지 않았습니다.");
            Assert.IsNull(reporter.Tick(Step, 0, 0, true), "보고가 두 번 나갔습니다. 캠페인이 같은 전투를 두 번 정산합니다.");
        }

        [Test]
        public void 시간이_쌓인다()
        {
            var reporter = new BattleReporter();
            reporter.Track(1, new FakeSquad { AliveCount = 4 }, 6);

            for (int i = 0; i < 10; i++)
            {
                reporter.Tick(Step, enemiesAlive: 2, playerReserves: 0, enemyReinforcementsExhausted: true);
            }

            var result = reporter.Tick(Step, enemiesAlive: 0, playerReserves: 0, enemyReinforcementsExhausted: true);

            Assert.AreEqual(1.1f, result.Duration, 0.001f);
        }

        // ====================================================================================================
        // 3. 분대별 보고
        // ====================================================================================================

        [Test]
        public void 주문서의_식별자를_그대로_돌려준다()
        {
            var reporter = new BattleReporter();

            reporter.Track(7, new FakeSquad { AliveCount = 3 }, 6);
            reporter.Track(42, new FakeSquad { AliveCount = 5 }, 6);

            var result = reporter.Tick(Step, 0, 0, true);

            Assert.AreEqual(2, result.Squads.Count);
            Assert.AreEqual(7, result.Squads[0].Id);
            Assert.AreEqual(42, result.Squads[1].Id);
        }

        [Test]
        public void 살아남은_인원을_그대로_싣는다()
        {
            var reporter = new BattleReporter();
            reporter.Track(1, new FakeSquad { AliveCount = 3 }, 6);

            var result = reporter.Tick(Step, 0, 0, true);

            Assert.AreEqual(6, result.Squads[0].Deployed);
            Assert.AreEqual(3, result.Squads[0].Survivors);
            Assert.AreEqual(3, result.Squads[0].Losses);
            Assert.IsFalse(result.Squads[0].Destroyed);
        }

        /// <summary>
        /// <b>지휘관을 잃은 분대는 남은 병사가 있어도 흩어집니다.</b>
        /// 생존자로 세면 캠페인이 있지도 않은 병력을 다음 전투에 데려갑니다.
        /// </summary>
        [Test]
        public void 소멸한_분대는_남은_병사가_있어도_생존자가_없다()
        {
            var reporter = new BattleReporter();
            reporter.Track(1, new FakeSquad { IsDestroyed = true, AliveCount = 4 }, 6);

            // 분대를 모두 잃었으므로 패배로 끝납니다.
            var result = reporter.Tick(Step, 3, 0, false);

            Assert.IsTrue(result.Squads[0].Destroyed);
            Assert.AreEqual(0, result.Squads[0].Survivors, "무너진 분대의 병사를 생존자로 셌습니다.");
            Assert.AreEqual(6, result.Squads[0].Losses);
        }

        /// <summary>
        /// 생존자 0명과 분대 소멸은 다른 말입니다.
        /// 캠페인은 둘을 따로 알아야 보충할지 해체할지 정할 수 있습니다.
        /// </summary>
        [Test]
        public void 생존자_0명과_소멸은_다르다()
        {
            var reporter = new BattleReporter();

            reporter.Track(1, new FakeSquad { IsDestroyed = false, AliveCount = 0 }, 6);
            reporter.Track(2, new FakeSquad { AliveCount = 4 }, 6);

            var result = reporter.Tick(Step, 0, 0, true);

            Assert.AreEqual(0, result.Squads[0].Survivors);
            Assert.IsFalse(result.Squads[0].Destroyed, "인원만 0인 분대를 소멸로 처리했습니다.");
            Assert.AreEqual(2, result.SurvivingSquads);
        }

        // ====================================================================================================
        // 4. 적 처치 수 — 파도로 나뉘어 들어옵니다
        // ====================================================================================================

        [Test]
        public void 한_무리만_있으면_쓰러진_수를_그대로_센다()
        {
            var reporter = new BattleReporter();
            reporter.Track(1, new FakeSquad { AliveCount = 6 }, 6);

            reporter.Tick(Step, 0, 0, false);   // 아직 아무도 오지 않았습니다
            reporter.Tick(Step, 5, 0, false);   // 다섯 명 상륙
            reporter.Tick(Step, 3, 0, false);   // 두 명 쓰러짐

            Assert.AreEqual(2, reporter.EnemiesKilled);
        }

        /// <summary>
        /// <b>이것이 예전 계산이 틀렸던 자리입니다.</b>
        ///
        /// 총원을 "지금 남은 수 + 지금까지의 손실"로 구하고, 그 손실을 다시 총원에서 빼서 구했습니다.
        /// 식이 자기 자신을 참조해 상쇄되는 바람에, 총원은 <b>첫 파도의 규모에서 멈춰</b>
        /// 두 번째 파도부터는 아무리 잡아도 처치 수가 늘지 않았습니다.
        ///
        /// 증감만 보면 그 순환이 사라집니다 —
        /// 늘어난 만큼은 새로 나온 적이고, 줄어든 만큼은 쓰러진 적입니다.
        /// </summary>
        [Test]
        public void 지원군이_이어져도_처치_수가_누적된다()
        {
            var reporter = new BattleReporter();
            reporter.Track(1, new FakeSquad { AliveCount = 6 }, 6);

            reporter.Tick(Step, 0, 0, false);
            reporter.Tick(Step, 5, 0, false);   // 첫 무리 5명
            reporter.Tick(Step, 3, 0, false);   // 2명 쓰러짐
            reporter.Tick(Step, 9, 0, false);   // 지원군 6명 추가 → 지금까지 11명이 나왔습니다

            Assert.AreEqual(2, reporter.EnemiesKilled, "지원군이 오자 처치 수가 흐트러졌습니다.");

            var result = reporter.Tick(Step, 0, 0, true);   // 전부 쓰러짐

            Assert.AreEqual(11, result.EnemiesKilled, "뒤이어 올라온 적이 처치 수에 잡히지 않았습니다.");
        }

        [Test]
        public void 아무도_오지_않으면_처치_수는_0이다()
        {
            var reporter = new BattleReporter();
            reporter.Track(1, new FakeSquad { AliveCount = 6 }, 6);

            var result = reporter.Tick(Step, 0, 0, true);

            Assert.AreEqual(0, result.EnemiesKilled);
        }

        // ====================================================================================================
        // 5. 되돌리기
        // ====================================================================================================

        [Test]
        public void 되돌리면_다음_전투를_다시_받을_수_있다()
        {
            var reporter = new BattleReporter();
            reporter.Track(1, new FakeSquad { AliveCount = 4 }, 6);

            reporter.Tick(Step, 3, 0, false);
            reporter.Tick(Step, 0, 0, true);

            Assert.IsTrue(reporter.IsDecided);

            reporter.Reset();

            Assert.IsFalse(reporter.IsDecided);
            Assert.AreEqual(BattleOutcome.Undecided, reporter.Outcome);
            Assert.AreEqual(0, reporter.EnemiesKilled);
            Assert.AreEqual(0f, reporter.Elapsed, 0.0001f);
        }
    }
}
