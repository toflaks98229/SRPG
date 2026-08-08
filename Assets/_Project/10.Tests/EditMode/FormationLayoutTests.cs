using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Systems.Formation;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 진형 배치와 웨이브 스케줄링을 검증합니다.
    /// </summary>
    public sealed class FormationLayoutTests
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

    }
}
