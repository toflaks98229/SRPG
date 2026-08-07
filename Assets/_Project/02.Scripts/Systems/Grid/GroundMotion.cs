using UnityEngine;

namespace SRPG.Systems.Grid
{
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
        // 1. Public Methods
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
        /// 그 자리에 설 수 있는지 확인합니다.
        /// </summary>
        /// <param name="grid">지형입니다.</param>
        /// <param name="world">확인할 월드 좌표입니다.</param>
        /// <param name="groundHeight">설 수 있다면 그 지면 높이입니다.</param>
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

            groundHeight = tile.WorldCenter.y;
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
