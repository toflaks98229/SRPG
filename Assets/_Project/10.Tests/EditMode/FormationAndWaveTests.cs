using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Data;
using SRPG.Systems.Formation;
using SRPG.Systems.Spawning;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 진형 배치와 웨이브 스케줄링을 검증합니다.
    /// </summary>
    public sealed class FormationAndWaveTests
    {
        // ====================================================================================================
        // 1. Formation
        // ====================================================================================================

        [Test]
        public void 진형_슬롯_수는_인원_수와_같다()
        {
            var slots = new List<Vector3>();

            for (int count = 1; count <= 12; count++)
            {
                FormationSolver.SolveGrid(Vector3.zero, Vector3.forward, count, 1f, slots);
                Assert.AreEqual(count, slots.Count, $"인원 {count}명에 슬롯이 {slots.Count}개입니다.");
            }
        }

        [Test]
        public void 진형_슬롯은_앵커를_중심으로_퍼진다()
        {
            var slots = new List<Vector3>();
            FormationSolver.SolveGrid(Vector3.zero, Vector3.forward, 6, 1f, slots);

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < slots.Count; i++)
            {
                sum += slots[i];
            }

            Vector3 centroid = sum / slots.Count;

            // 마지막 행이 덜 찰 수 있으므로 정확히 0은 아니지만, 앵커 근처여야 합니다.
            Assert.Less(centroid.magnitude, 1f, $"진형 중심이 앵커에서 {centroid.magnitude:F2} 만큼 벗어났습니다.");
        }

        [Test]
        public void 진형_슬롯끼리_겹치지_않는다()
        {
            var slots = new List<Vector3>();
            FormationSolver.SolveGrid(Vector3.zero, Vector3.forward, 8, 1f, slots);

            for (int i = 0; i < slots.Count; i++)
            {
                for (int j = i + 1; j < slots.Count; j++)
                {
                    float distance = Vector3.Distance(slots[i], slots[j]);
                    Assert.Greater(distance, 0.5f, $"슬롯 {i}와 {j}가 너무 가깝습니다 ({distance:F2}).");
                }
            }
        }

        [Test]
        public void 방향이_영벡터여도_예외가_나지_않는다()
        {
            var slots = new List<Vector3>();

            Assert.DoesNotThrow(() => FormationSolver.SolveGrid(Vector3.zero, Vector3.zero, 5, 1f, slots));
            Assert.AreEqual(5, slots.Count);
        }

        // ====================================================================================================
        // 2. Wave Scheduler
        // ====================================================================================================

        [Test]
        public void 준비_시간이_지나야_첫_웨이브가_시작된다()
        {
            var definition = WaveDefinition.CreateDefault();
            var scheduler = new WaveScheduler(definition);

            int triggered = 0;
            scheduler.WaveTriggered += (index, entry) => triggered++;

            scheduler.Tick(definition.PreparationTime - 0.5f);
            Assert.AreEqual(0, triggered, "준비 시간이 끝나기 전에 웨이브가 시작됐습니다.");

            scheduler.Tick(1f);
            Assert.AreEqual(1, triggered, "준비 시간이 끝났는데 웨이브가 시작되지 않았습니다.");
        }

        [Test]
        public void 모든_웨이브가_순서대로_한_번씩_발생한다()
        {
            var definition = WaveDefinition.CreateDefault();
            var scheduler = new WaveScheduler(definition);

            var order = new List<int>();
            bool finished = false;

            scheduler.WaveTriggered += (index, entry) => order.Add(index);
            scheduler.AllWavesTriggered += () => finished = true;

            // 충분히 오래 진행시킵니다.
            for (int i = 0; i < 500; i++)
            {
                scheduler.Tick(1f);
            }

            Assert.AreEqual(definition.WaveCount, order.Count, "발생한 웨이브 수가 정의와 다릅니다.");
            Assert.IsTrue(finished, "완료 이벤트가 발생하지 않았습니다.");

            for (int i = 0; i < order.Count; i++)
            {
                Assert.AreEqual(i, order[i], "웨이브 순서가 어긋났습니다.");
            }
        }

        [Test]
        public void 완료된_뒤에는_더_이상_웨이브가_발생하지_않는다()
        {
            var definition = WaveDefinition.CreateDefault();
            var scheduler = new WaveScheduler(definition);

            int triggered = 0;
            scheduler.WaveTriggered += (index, entry) => triggered++;

            for (int i = 0; i < 500; i++)
            {
                scheduler.Tick(1f);
            }

            int afterFinish = triggered;

            for (int i = 0; i < 100; i++)
            {
                scheduler.Tick(1f);
            }

            Assert.AreEqual(afterFinish, triggered, "완료 후에도 웨이브가 추가로 발생했습니다.");
            Assert.IsTrue(scheduler.IsFinished);
        }

        [Test]
        public void 초기화하면_처음부터_다시_시작한다()
        {
            var definition = WaveDefinition.CreateDefault();
            var scheduler = new WaveScheduler(definition);

            for (int i = 0; i < 500; i++)
            {
                scheduler.Tick(1f);
            }

            Assert.IsTrue(scheduler.IsFinished);

            scheduler.Reset();

            Assert.IsFalse(scheduler.IsFinished, "초기화했는데도 완료 상태입니다.");
            Assert.AreEqual(0, scheduler.NextWaveIndex);
            Assert.AreEqual(definition.PreparationTime, scheduler.TimeUntilNextWave, 0.001f);
        }
    }
}
