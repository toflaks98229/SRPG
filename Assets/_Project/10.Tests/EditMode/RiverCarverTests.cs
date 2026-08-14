using NUnit.Framework;
using SRPG.Systems.Battlefield;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 전장을 가르는 강을 검증합니다.
    ///
    /// <b>여기가 틀리면 전투가 성립하지 않습니다</b>
    ///
    /// 강이 전장을 완전히 가르면 두 부대가 영영 만나지 못합니다.
    /// 길찾기는 경로를 못 찾고, 생성기의 고립지 정리가 강 건너편을 통째로 바위로 덮으며,
    /// 그러면 적 전개 구역이 사라집니다. <b>예외는 하나도 나지 않습니다</b> —
    /// "이 시드에서는 적이 안 온다"로만 보이고, 지형은 시드마다 다르니 재현도 어렵습니다.
    ///
    /// 그래서 여울이 실제로 마른 땅으로 남는지를 못 박아 둡니다.
    /// </summary>
    public sealed class RiverCarverTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private const int Resolution = 129;
        private const float SeaLevel = 0.18f;
        private const float Width = 0.16f;
        private const float Depth = 0.12f;

        /// <summary>평평한 마른 땅입니다. 강만 보이도록 노이즈를 빼 둡니다.</summary>
        private static float[,] FlatLand(float height = 0.6f)
        {
            var heights = new float[Resolution, Resolution];

            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    heights[y, x] = height;
                }
            }

            return heights;
        }

        private static float[,] Carved(Vector3 flow, int fordCount = 2)
        {
            var heights = FlatLand();
            RiverCarver.Carve(heights, flow, SeaLevel, Width, Depth, fordCount);
            return heights;
        }

        /// <summary>물에 잠긴 표본 수입니다.</summary>
        private static int CountWater(float[,] heights)
        {
            int count = 0;

            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    if (heights[y, x] < SeaLevel)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// 흐름 방향으로 훑으며, 한 줄이라도 물 없이 건널 수 있는지 봅니다.
        ///
        /// 강이 세로(+Z)로 흐르면 가로(+X) 줄 하나가 통째로 마른 땅이어야 건널 수 있습니다.
        /// </summary>
        private static bool HasDryCrossing(float[,] heights)
        {
            for (int y = 0; y < Resolution; y++)
            {
                bool dry = true;

                for (int x = 0; x < Resolution; x++)
                {
                    if (heights[y, x] < SeaLevel)
                    {
                        dry = false;
                        break;
                    }
                }

                if (dry)
                {
                    return true;
                }
            }

            return false;
        }

        // ====================================================================================================
        // 2. 물이 생긴다
        // ====================================================================================================

        /// <summary>
        /// 야전으로 오면서 물이 전장 가장자리에만 남았습니다.
        /// 익사 규칙이 죽은 것은 아니었지만 <b>아무도 닿지 않는 곳에</b> 있었습니다.
        /// </summary>
        [Test]
        public void 강은_실제로_물을_만든다()
        {
            var heights = Carved(Vector3.forward);

            Assert.Greater(CountWater(heights), 0, "강을 팠는데 물이 하나도 없습니다.");
        }

        [Test]
        public void 강은_전장_한가운데를_지난다()
        {
            var heights = Carved(Vector3.forward);
            int center = Resolution / 2;

            // 세로로 흐르는 강이면 중앙 가로줄 어딘가는 반드시 물입니다.
            bool wet = false;

            for (int x = 0; x < Resolution; x++)
            {
                if (heights[center, x] < SeaLevel)
                {
                    wet = true;
                    break;
                }
            }

            Assert.IsTrue(wet, "강이 전장 가운데를 지나지 않습니다. 가장자리 물과 다를 바가 없습니다.");
        }

        [Test]
        public void 강폭이_넓을수록_물이_많아진다()
        {
            var narrow = FlatLand();
            var wide = FlatLand();

            RiverCarver.Carve(narrow, Vector3.forward, SeaLevel, 0.08f, Depth, 2);
            RiverCarver.Carve(wide, Vector3.forward, SeaLevel, 0.24f, Depth, 2);

            Assert.Greater(CountWater(wide), CountWater(narrow));
        }

        // ====================================================================================================
        // 3. 여울 — 이 기능의 생사
        // ====================================================================================================

        /// <summary>
        /// <b>건널 수 없는 강은 전장이 아닙니다.</b>
        /// 두 부대가 영영 만나지 못하고, 그 사실이 아무 예외도 내지 않습니다.
        /// </summary>
        [Test]
        public void 여울로_강을_건널_수_있다()
        {
            Assert.IsTrue(
                HasDryCrossing(Carved(Vector3.forward)),
                "여울이 물에 잠겨 강을 건널 수 없습니다. 두 부대가 만나지 못합니다.");
        }

        [Test]
        public void 여울은_방향이_바뀌어도_남는다()
        {
            foreach (var flow in new[]
            {
                Vector3.forward,
                Vector3.right,
                new Vector3(1f, 0f, 1f),
                new Vector3(-1f, 0f, 0.4f),
            })
            {
                var heights = FlatLand();
                RiverCarver.Carve(heights, flow, SeaLevel, Width, Depth, 2);

                Assert.Greater(CountWater(heights), 0, $"{flow} 방향에서 물이 생기지 않았습니다.");
            }
        }

        /// <summary>
        /// 여울 수를 0으로 넘겨도 하나는 만들어야 합니다.
        /// 기획이 실수로 0을 넣었다고 전장이 두 쪽으로 갈라지면 안 됩니다.
        /// </summary>
        [Test]
        public void 여울_수가_0이어도_하나는_생긴다()
        {
            Assert.IsTrue(
                HasDryCrossing(Carved(Vector3.forward, fordCount: 0)),
                "여울 0으로 전장이 갈라졌습니다.");
        }

        [Test]
        public void 여울이_많으면_물이_줄어든다()
        {
            var few = FlatLand();
            var many = FlatLand();

            RiverCarver.Carve(few, Vector3.forward, SeaLevel, Width, Depth, 1);
            RiverCarver.Carve(many, Vector3.forward, SeaLevel, Width, Depth, 4);

            Assert.Less(CountWater(many), CountWater(few), "여울을 늘렸는데 물이 줄지 않았습니다.");
        }

        // ====================================================================================================
        // 4. 강 밖은 건드리지 않는다
        // ====================================================================================================

        /// <summary>
        /// 강둑 바깥의 지형은 그대로여야 합니다.
        /// 강을 팠더니 언덕이 함께 뭉개지면 지형 종류의 성격이 사라집니다.
        /// </summary>
        [Test]
        public void 강에서_먼_곳은_높이가_그대로다()
        {
            var before = FlatLand();
            var after = Carved(Vector3.forward);

            // 세로로 흐르는 강이므로 좌우 끝은 손대지 않아야 합니다.
            for (int y = 0; y < Resolution; y++)
            {
                Assert.AreEqual(before[y, 0], after[y, 0], 0.0001f, "왼쪽 끝이 바뀌었습니다.");
                Assert.AreEqual(before[y, Resolution - 1], after[y, Resolution - 1], 0.0001f, "오른쪽 끝이 바뀌었습니다.");
            }
        }

        [Test]
        public void 강폭이_0이면_아무것도_바뀌지_않는다()
        {
            var heights = FlatLand();

            RiverCarver.Carve(heights, Vector3.forward, SeaLevel, 0f, Depth, 2);

            Assert.AreEqual(0, CountWater(heights));
        }

        [Test]
        public void 높이는_범위를_벗어나지_않는다()
        {
            var heights = FlatLand(0.2f);

            RiverCarver.Carve(heights, Vector3.forward, SeaLevel, 0.3f, 0.5f, 1);

            for (int y = 0; y < Resolution; y++)
            {
                for (int x = 0; x < Resolution; x++)
                {
                    Assert.GreaterOrEqual(heights[y, x], 0f);
                    Assert.LessOrEqual(heights[y, x], 1f);
                }
            }
        }

        // ====================================================================================================
        // 5. 안전
        // ====================================================================================================

        [Test]
        public void 배열이_없어도_터지지_않는다()
        {
            Assert.DoesNotThrow(() => RiverCarver.Carve(null, Vector3.forward, SeaLevel, Width, Depth, 2));
        }

        [Test]
        public void 흐름이_0이어도_강이_생긴다()
        {
            var heights = FlatLand();

            RiverCarver.Carve(heights, Vector3.zero, SeaLevel, Width, Depth, 2);

            Assert.Greater(CountWater(heights), 0, "흐름 방향이 비어 강이 사라졌습니다.");
        }

        // ====================================================================================================
        // 여울은 <b>부대</b>가 건널 만해야 한다
        // ====================================================================================================

        /// <summary>
        /// 마른 여울이 타일 여러 칸만큼 넓습니다.
        ///
        /// <b>앵커가 건너는 것과 부대가 건너는 것은 다릅니다.</b>
        ///
        /// 앵커는 점이라 한 줄만 말라 있어도 지나갑니다. 그런데 병사들은 그 둘레에 퍼져 있어서,
        /// 여울이 한 칸이면 바깥쪽 병사가 강에 발을 들입니다. 거기서 걸음이 막히고
        /// 분대는 떠나가므로, 화면에는 "다리를 건너다 멈춘 병사"로만 보입니다.
        ///
        /// 예전에는 여울 마루가 뾰족해서 마른 폭이 여울 폭의 20%뿐이었습니다 —
        /// 타일 크기 2미터 기준으로 <b>2.5미터, 사실상 한 줄</b>이었습니다.
        /// 이 검사는 그 상태로 되돌아가면 즉시 빨간불을 켭니다.
        /// </summary>
        [Test]
        public void 마른_여울이_한_줄보다_넓다()
        {
            // 강이 +Z 로 흐르므로 여울은 Z 축을 따라 늘어섭니다.
            var heights = Carved(Vector3.forward);

            // 강 한복판의 X 열을 고릅니다. 거기서 Z 방향으로 마른 구간의 길이를 셉니다.
            int midX = Resolution / 2;

            int longest = 0;
            int run = 0;

            for (int y = 0; y < Resolution; y++)
            {
                if (heights[y, midX] >= SeaLevel)
                {
                    run++;
                    longest = Mathf.Max(longest, run);
                }
                else
                {
                    run = 0;
                }
            }

            // 표본 간격은 놀이터 한 변을 Resolution-1 로 나눈 것입니다.
            // 타일 한 칸(2m)이 몇 표본인지는 전장 크기에 따라 다르므로, 표본 수로 견줍니다.
            // 여울 폭 FordSpan(0.10) 은 t 단위이고, t 1.0 이 표본 전체이므로
            // 여울 하나의 전체 폭은 대략 Resolution * 0.2 표본입니다.
            int fordSpanSamples = Mathf.RoundToInt(Resolution * 0.2f);

            Assert.Greater(
                longest,
                fordSpanSamples / 3,
                $"마른 여울이 {longest} 표본뿐입니다. 여울 폭 {fordSpanSamples} 표본의 3분의 1도 되지 않습니다 — " +
                "앵커는 건너지만 부대는 건너지 못합니다.");
        }

        /// <summary>
        /// 여울 가운데가 <b>평평합니다.</b>
        ///
        /// 뾰족한 마루는 꼭대기 한 점만 해수면 위로 올라옵니다.
        /// 등마루가 평평해야 그 구간이 통째로 마른 땅이 됩니다.
        /// </summary>
        [Test]
        public void 여울_가운데가_평평하다()
        {
            var heights = Carved(Vector3.forward, fordCount: 1);

            int midX = Resolution / 2;

            // 여울 하나이므로 중심은 흐름 방향의 한가운데입니다.
            int center = (Resolution - 1) / 2;

            float atCenter = heights[center, midX];

            // 등마루 안쪽이면 중심과 높이가 같아야 합니다.
            int inside = center + Mathf.RoundToInt(Resolution * 0.02f);

            Assert.AreEqual(
                atCenter,
                heights[inside, midX],
                1e-4f,
                "여울 등마루가 평평하지 않습니다. 가운데만 솟아 있으면 마른 폭이 한 줄로 좁아집니다.");

            Assert.GreaterOrEqual(atCenter, SeaLevel, "여울 마루가 물에 잠겨 있습니다.");
        }
    }
}
