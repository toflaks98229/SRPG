using UnityEngine;

namespace SRPG.Systems.Grid
{
    /// <summary>
    /// 한 걸음의 결과입니다.
    /// </summary>
    public enum GroundStep
    {
        /// <summary>움직였습니다(막혀서 제자리인 경우도 포함합니다).</summary>
        Moved = 0,

        /// <summary>물로 밀려나 익사했습니다.</summary>
        Drowned,
    }

    /// <summary>
    /// 지형에 막혔을 때 어디까지 갈 수 있는지 정합니다.
    ///
    /// <b>왜 필요한가</b>
    ///
    /// 예전에는 목적지가 막히면 <b>제자리에 멈췄습니다.</b>
    /// 벽에 비스듬히 다가가는 병사는 X와 Z 어느 쪽으로도 나아가지 못한 채 그 자리에서 굳습니다.
    /// 분대는 앵커를 따라 떠나가고 그 한 명만 남아 낙오합니다.
    ///
    /// 실제로는 벽에 부딪히면 <b>벽을 따라 미끄러집니다.</b>
    /// 대각선이 막혀도 옆으로 한 칸은 갈 수 있고, 그렇게 조금씩 돌아 나옵니다.
    ///
    /// 축을 하나씩 시도하는 것만으로 그 움직임이 나옵니다.
    /// X로 갈 수 있으면 X만 가고, 거기서 다시 Z로 갈 수 있으면 Z도 갑니다.
    /// 두 축이 다 막혔을 때만 진짜로 멈춥니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 계산이라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public static class GroundMotion
    {
        // ====================================================================================================
        // 1. Public Methods - Step
        // ====================================================================================================

        /// <summary>
        /// 한 걸음을 지형에 대고 확정합니다. 익사 판정과 미끄러짐을 <b>정해진 순서로</b> 처리합니다.
        ///
        /// <b>익사 판정이 먼저여야 합니다.</b>
        ///
        /// 미끄러짐을 먼저 보면 밀려나던 병사가 물가를 따라 스르륵 비껴갑니다.
        /// 그러면 넉백은 그냥 밀치기 연출이 되고, 물이 위험 요소가 아니게 됩니다.
        /// 이 순서가 이 게임의 주요 사망 규칙 그 자체라, 호출부마다 다시 쓰게 두면
        /// 언젠가 한쪽에서만 순서가 뒤집힙니다. 그래서 여기 한 곳에 묶어 둡니다.
        /// </summary>
        /// <param name="grid">지형입니다.</param>
        /// <param name="from">현재 위치입니다.</param>
        /// <param name="desired">이번 프레임에 가려던 위치입니다.</param>
        /// <param name="mayDrown">
        /// 물 위로 밀려날 수 있는 상태인지입니다. 넉백에 세게 밀리는 중일 때만 true입니다.
        /// 스스로 달려드는 힘(도약)으로는 물에 빠지지 않습니다.
        /// </param>
        /// <param name="next">확정된 위치입니다. 익사했다면 수면 높이의 낙수 지점입니다.</param>
        public static GroundStep TryStep(
            IslandGrid grid, Vector3 from, Vector3 desired, bool mayDrown, out Vector3 next)
        {
            if (mayDrown && IsWater(grid, desired))
            {
                next = new Vector3(desired.x, 0f, desired.z);
                return GroundStep.Drowned;
            }

            next = Resolve(grid, from, desired);
            return GroundStep.Moved;
        }

        // ====================================================================================================
        // 2. Public Methods - Resolve
        // ====================================================================================================

        /// <summary>
        /// 목적지가 막혀 있으면 갈 수 있는 곳까지만 이동시킵니다.
        /// </summary>
        /// <param name="grid">지형입니다.</param>
        /// <param name="from">현재 위치입니다.</param>
        /// <param name="desired">이번 프레임에 가려던 위치입니다.</param>
        /// <returns>실제로 설 수 있는 위치입니다. 높이는 지면에 맞춰집니다.</returns>
        public static Vector3 Resolve(IslandGrid grid, Vector3 from, Vector3 desired)
        {
            if (grid == null)
            {
                return desired;
            }

            // 그대로 갈 수 있으면 끝입니다. 대부분의 프레임이 여기서 끝납니다.
            if (TryStand(grid, desired, out float straightHeight))
            {
                desired.y = straightHeight;
                return desired;
            }

            Vector3 result = from;

            // X축만 시도합니다. 세로 벽을 따라 미끄러지는 경우입니다.
            if (TryStand(grid, new Vector3(desired.x, from.y, from.z), out float heightX))
            {
                result.x = desired.x;
                result.y = heightX;
            }

            // 그 자리에서 다시 Z축을 시도합니다.
            // 옮겨진 X를 기준으로 보는 것이 중요합니다. 그래야 모서리를 돌아 나갈 수 있습니다.
            if (TryStand(grid, new Vector3(result.x, from.y, desired.z), out float heightZ))
            {
                result.z = desired.z;
                result.y = heightZ;
            }

            return result;
        }

        /// <summary>
        /// 그 자리에 설 수 있는지 확인하고, 설 수 있다면 발이 닿는 높이를 알려 줍니다.
        ///
        /// <b>통행은 타일이 정하고, 높이는 지형이 정합니다.</b>
        ///
        /// 갈 수 있는지는 이산적인 질문이라 타일이 답합니다 — 물인가, 절벽인가, 장애물인가.
        /// 그러나 <b>얼마나 높은가</b>는 연속적인 질문이고, 그 답은 실제 지형에 있습니다.
        ///
        /// 예전에는 여기서 타일 중심의 높이를 그대로 돌려주었습니다. 그러면 한 칸 안에서는
        /// 높이가 상수라, 비탈을 걷는 병사가 칸 경계마다 한 단씩 툭툭 튀어 올랐습니다.
        /// 정작 분대 앵커는 <see cref="IslandGrid.SampleGroundHeight"/>로 연속면을 타고 있었으므로,
        /// <b>앵커는 부드럽게 오르는데 병사만 계단으로 오르는</b> 어긋남이 남아 있었습니다.
        /// </summary>
        /// <param name="grid">지형입니다.</param>
        /// <param name="world">확인할 월드 좌표입니다.</param>
        /// <param name="groundHeight">설 수 있다면 그 자리의 지면 높이입니다.</param>
        public static bool TryStand(IslandGrid grid, Vector3 world, out float groundHeight)
        {
            groundHeight = 0f;

            if (grid == null)
            {
                return false;
            }

            var tile = grid.GetTile(grid.WorldToCoord(world));
            if (tile == null || !tile.IsWalkable)
            {
                return false;
            }

            groundHeight = grid.SampleGroundHeight(world);
            return true;
        }

        /// <summary>
        /// 그 자리가 물인지 확인합니다. 격자 밖도 물로 봅니다.
        /// </summary>
        public static bool IsWater(IslandGrid grid, Vector3 world)
        {
            if (grid == null)
            {
                return true;
            }

            var tile = grid.GetTile(grid.WorldToCoord(world));
            return tile == null || tile.IsWater;
        }
    }
}
