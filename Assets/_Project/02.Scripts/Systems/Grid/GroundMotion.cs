using SRPG.Systems.Pathfinding;
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
        /// <returns>실제로 옮겼는지, 벽에 막혔는지, 물에 빠졌는지입니다.</returns>
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

            // <b>설 수 없는 자리에 이미 서 있으면 빠져나갈 길을 열어 줍니다.</b>
            //
            // 아래의 미끄러짐은 <b>딛고 선 자리가 온전하다</b>는 전제 위에 있습니다.
            // 물 위에 서 있으면 X도 Z도 물이라 두 축이 모두 막히고, 그러면 제자리가 답으로 나옵니다.
            // 한 번 그렇게 되면 <b>영영 그 자리에 굳습니다</b> — 스스로는 절대 못 나옵니다.
            //
            // 실제로 그렇게 되는 길이 있습니다.
            //   · 분대가 물가에 서면서 진형 슬롯이 물 위에 놓이는 경우
            //     (<c>Squad.Initialize</c> 는 첫 배치에서 슬롯을 뭍으로 당기지 않습니다)
            //   · 좁은 여울을 건너다 흐트러진 병사가 강에 발을 들이는 경우
            //
            // 증상은 오류가 아니라 "저 병사만 안 따라온다"로만 보입니다.
            // 물가로 되돌리는 것은 익사 규칙과 다투지 않습니다 — 익사는 <b>넉백으로 밀려날 때</b>
            // <see cref="TryStep"/> 가 먼저 판정하고, 여기까지 오지 않습니다.
            if (!TryStand(grid, from, out _))
            {
                return Escape(grid, from, desired);
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
        /// 설 수 없는 자리에 갇힌 병사를 가장 가까운 뭍으로 한 걸음 옮깁니다.
        ///
        /// <b>가려던 방향을 따르지 않습니다.</b>
        /// 갇힌 병사가 향하는 곳은 대개 강 건너입니다. 그쪽으로 밀어 봐야 다시 물이라
        /// 아무 데도 가지 못합니다. 나가는 방향은 <b>뭍이 어디인가</b>가 정해야 합니다.
        ///
        /// 걸음의 크기는 원래 가려던 만큼입니다. 여기서 따로 정하면 이 경로에서만
        /// 이동 속도가 달라져, 물에 빠진 병사가 갑자기 빨라지거나 느려집니다.
        /// </summary>
        /// <param name="grid">지형입니다.</param>
        /// <param name="from">지금 갇혀 있는 자리입니다.</param>
        /// <param name="desired">이번 프레임에 가려던 자리입니다. 걸음의 크기만 씁니다.</param>
        /// <returns>뭍 쪽으로 한 걸음 옮긴 자리입니다. 나갈 뭍이 없으면 제자리입니다.</returns>
        private static Vector3 Escape(IslandGrid grid, Vector3 from, Vector3 desired)
        {
            var refuge = grid.FindNearestWalkable(from);

            if (refuge == null)
            {
                return from;
            }

            Vector3 target = grid.CoordToWorld(refuge.Coord);

            Vector3 toward = target - from;
            toward.y = 0f;

            float distance = toward.magnitude;

            if (distance <= 1e-4f)
            {
                return from;
            }

            // 남은 거리보다 크게 내딛지 않습니다. 넘어서면 뭍을 지나쳐 반대편 물로 나갑니다.
            float step = Mathf.Min(Vector3.Distance(new Vector3(from.x, 0f, from.z),
                                                    new Vector3(desired.x, 0f, desired.z)),
                                   distance);

            Vector3 next = from + toward / distance * step;

            // 도착한 자리가 뭍이면 발 높이를 맞춥니다.
            // 아직 물 위라면 다음 프레임에 다시 이 길로 들어와 조금씩 나옵니다.
            next.y = TryStand(grid, next, out float height) ? height : from.y;

            return next;
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
        /// <returns>그 자리에 설 수 있으면 true입니다.</returns>
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
        /// <param name="grid">통행 판정을 읽을 지형입니다.</param>
        /// <param name="world">확인할 월드 좌표입니다.</param>
        /// <returns>그 자리가 물이면 true입니다. 격자 밖도 물로 봅니다.</returns>
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
