using NUnit.Framework;
using SRPG.Common;
using SRPG.Systems.Grid;

namespace SRPG.Tests
{
    /// <summary>
    /// 한 칸에서 옆 칸으로 건너갈 수 있는가를 검증합니다.
    ///
    /// <b>이 규칙은 세 곳이 함께 씁니다</b>
    ///
    /// 길찾기·격자의 이웃 계산·생성기의 연결성 검사가 모두 같은 함수를 봅니다.
    /// 예전에는 셋이 각자 <c>Mathf.Abs(...) &lt;= 1</c> 을 적어 두고 있었고,
    /// 생성기 쪽 주석이 <i>"길찾기가 쓰는 것과 같아야 합니다"</i>라고 경고하고 있었습니다.
    /// 경고가 필요했다는 것은 곧 강제되지 않았다는 뜻입니다.
    ///
    /// 어긋나면 조용히 고장 납니다 — 생성기가 "이어져 있다"고 판단한 땅을 부대가 못 가거나,
    /// 초크포인트 점수가 실제 통행과 다른 지형을 가리킵니다. 어느 쪽도 예외를 내지 않습니다.
    /// </summary>
    public sealed class TraversalRulesTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private static Tile Ground(int height)
        {
            return new Tile
            {
                Coord = new GridCoord(0, 0),
                Type = TileType.Ground,
                Height = height,
                IsWalkable = true,
            };
        }

        private static Tile Blocked(int height)
        {
            return new Tile
            {
                Coord = new GridCoord(0, 0),
                Type = TileType.Cliff,
                Height = height,
                IsWalkable = false,
            };
        }

        // ====================================================================================================
        // 2. 고도 단차
        // ====================================================================================================

        [Test]
        public void 같은_높이는_지나갈_수_있다()
        {
            Assert.IsTrue(TraversalRules.IsClimbable(3, 3));
        }

        [Test]
        public void 한_단_차이는_오르내릴_수_있다()
        {
            Assert.IsTrue(TraversalRules.IsClimbable(3, 4), "한 단을 오르지 못합니다.");
            Assert.IsTrue(TraversalRules.IsClimbable(4, 3), "한 단을 내려가지 못합니다.");
        }

        /// <summary>
        /// 한 단을 넘으면 절벽입니다. 고도 눈금 자체가 등반 한계에서 유도되므로,
        /// 이 판정이 곧 "경사가 오를 만한가"와 같은 말이 됩니다.
        /// </summary>
        [Test]
        public void 두_단_차이는_막힌다()
        {
            Assert.IsFalse(TraversalRules.IsClimbable(3, 5));
            Assert.IsFalse(TraversalRules.IsClimbable(5, 3));
        }

        [Test]
        public void 판정은_방향에_대칭이다()
        {
            for (int from = 0; from < 6; from++)
            {
                for (int to = 0; to < 6; to++)
                {
                    Assert.AreEqual(
                        TraversalRules.IsClimbable(from, to),
                        TraversalRules.IsClimbable(to, from),
                        $"{from} → {to} 와 {to} → {from} 의 답이 다릅니다.");
                }
            }
        }

        [Test]
        public void 최대_단차_경계가_규칙과_일치한다()
        {
            Assert.IsTrue(TraversalRules.IsClimbable(0, TraversalRules.MaxHeightDelta));
            Assert.IsFalse(TraversalRules.IsClimbable(0, TraversalRules.MaxHeightDelta + 1));
        }

        // ====================================================================================================
        // 3. 칸 단위 판정
        // ====================================================================================================

        [Test]
        public void 평지끼리는_건너갈_수_있다()
        {
            Assert.IsTrue(TraversalRules.CanStep(Ground(0), Ground(0)));
            Assert.IsTrue(TraversalRules.CanStep(Ground(0), Ground(1)));
        }

        [Test]
        public void 단차가_크면_평지끼리도_막힌다()
        {
            Assert.IsFalse(TraversalRules.CanStep(Ground(0), Ground(2)));
        }

        [Test]
        public void 통행_불가_칸으로는_갈_수_없다()
        {
            Assert.IsFalse(TraversalRules.CanStep(Ground(0), Blocked(0)));
        }

        /// <summary>
        /// 출발 칸도 봅니다. 격자가 이웃 수를 셀 때 자기 칸이 절벽이면 이웃도 0이어야 합니다.
        /// </summary>
        [Test]
        public void 통행_불가_칸에서는_나갈_수_없다()
        {
            Assert.IsFalse(TraversalRules.CanStep(Blocked(0), Ground(0)));
        }

        [Test]
        public void 없는_칸은_막힌_것으로_본다()
        {
            Assert.IsFalse(TraversalRules.CanStep(null, Ground(0)), "격자 밖으로 나갔습니다.");
            Assert.IsFalse(TraversalRules.CanStep(Ground(0), null), "격자 밖으로 나갔습니다.");
            Assert.IsFalse(TraversalRules.CanStep(null, null));
        }
    }
}
