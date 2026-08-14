using UnityEngine;

namespace SRPG.Systems.Grid
{
    /// <summary>
    /// 한 칸에서 옆 칸으로 <b>건너갈 수 있는가</b>를 정합니다.
    ///
    /// <b>왜 한곳에 모으는가</b>
    ///
    /// 같은 규칙이 세 곳에서 각자 쓰이고 있었습니다.
    ///
    ///   · 길찾기 — 이웃과의 단차가 1을 넘으면 지나가지 않습니다
    ///   · 격자   — 통행 가능 이웃 수를 셀 때 같은 조건을 봅니다 (초크포인트 점수의 입력입니다)
    ///   · 생성기 — 지형을 놓은 뒤 "이어져 있는가"를 확인할 때 같은 조건을 봅니다
    ///
    /// 셋이 어긋나면 조용히 고장 납니다. 생성기가 "이어져 있다"고 판단한 땅을 부대가 못 가거나,
    /// 초크포인트 점수가 실제 통행과 다른 지형을 가리킵니다. 어느 쪽도 예외를 내지 않습니다.
    /// 실제로 생성기 쪽 주석이 <i>"여기서 쓰는 이동 규칙은 길찾기가 쓰는 것과 같아야 합니다"</i>라고
    /// 경고하고 있었는데, 그 말은 곧 <b>강제되지 않는다</b>는 뜻이었습니다.
    ///
    /// <b>고도 눈금과의 관계</b>
    ///
    /// <c>BattlefieldGenerator.ResolveHeightStep</c>이 고도 한 단을 <b>등반 한계에서 유도</b>합니다.
    /// 경사가 한계 안이면 이웃과의 높이 차가 한 단 안이고, 따라서 단차도 반드시
    /// <see cref="MaxHeightDelta"/> 이하가 됩니다. 두 규칙이 같은 것을 말하게 하려는 장치입니다.
    /// 여기 값을 바꾸면 그쪽 유도식도 함께 봐야 합니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 규칙이라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public static class TraversalRules
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// 걸어서 오르내릴 수 있는 최대 고도 단차입니다. 이보다 크면 절벽으로 봅니다.
        /// </summary>
        public const int MaxHeightDelta = 1;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 두 고도 사이를 오르내릴 수 있는지 판정합니다.
        /// </summary>
        /// <param name="fromHeight">출발 칸의 고도 단계입니다.</param>
        /// <param name="toHeight">도착 칸의 고도 단계입니다.</param>
        /// <returns>단차가 <see cref="MaxHeightDelta"/> 이하면 true입니다.</returns>
        public static bool IsClimbable(int fromHeight, int toHeight)
        {
            return Mathf.Abs(toHeight - fromHeight) <= MaxHeightDelta;
        }

        /// <summary>
        /// 한 칸에서 다른 칸으로 실제로 건너갈 수 있는지 여부입니다.
        ///
        /// 양쪽 모두 통행 가능해야 하고, 단차도 넘을 수 있어야 합니다.
        /// 인접 여부는 <b>보지 않습니다</b> — 호출부가 이미 이웃만 넘깁니다.
        /// </summary>
        /// <param name="from">출발 칸입니다. null이면 건너갈 수 없는 것으로 봅니다.</param>
        /// <param name="to">도착 칸입니다. null이면 건너갈 수 없는 것으로 봅니다.</param>
        /// <returns>양쪽 모두 통행 가능하고 단차를 넘을 수 있으면 true입니다.</returns>
        public static bool CanStep(Tile from, Tile to)
        {
            if (from == null || to == null)
            {
                return false;
            }

            return from.IsWalkable
                   && to.IsWalkable
                   && IsClimbable(from.Height, to.Height);
        }
    }
}
