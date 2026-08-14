using System.Collections.Generic;
using SRPG.Common;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Systems.Deployment
{
    /// <summary>
    /// 전장의 양 끝에 <b>마주 보는 전개 구역</b>을 긋습니다.
    ///
    /// <b>왜 상륙 구역을 대신하는가</b>
    ///
    /// 예전에는 적이 바다에서 왔습니다. 그래서 전장 둘레의 해변을 각도로 갈라
    /// 상륙 구역을 만들고, 파도마다 다른 구역으로 배를 보냈습니다.
    /// 방어자는 어디가 위험한지 모른 채 전선을 나눠야 했고, 그것이 압박의 원천이었습니다.
    ///
    /// 야전은 다릅니다. 두 부대가 <b>서로를 보며</b> 전장에 들어섭니다.
    /// 어디서 오는지는 처음부터 알고, 문제는 그 다음입니다 —
    /// 어느 쪽 날개를 두껍게 할지, 고지를 먼저 잡을지, 언제 붙을지.
    ///
    /// <b>축을 시드로 뽑는 이유</b>
    ///
    /// 대치 축이 늘 같으면 지형의 의미가 사라집니다. 언덕이 언제나 같은 쪽에 서면
    /// 그건 지형이 아니라 규칙입니다. 축을 매 전장마다 다시 뽑으면
    /// 같은 언덕이 어떤 판에서는 내 고지가 되고 어떤 판에서는 넘어야 할 벽이 됩니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 계산이라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public static class DeploymentZones
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// 한 진영의 전개 구역이 차지하는 전장 비율입니다.
        ///
        /// 양쪽을 합쳐도 절반을 넘지 않아야 <b>가운데가 비어</b> 접근하는 시간이 생깁니다.
        /// 그 시간이 곧 진형을 갖추고 고지를 다투는 여유입니다.
        /// </summary>
        public const float DefaultDepthRatio = 0.22f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 전개 구역을 다시 긋습니다. 격자의 <see cref="IslandGrid.PlayerDeployment"/>와
        /// <see cref="IslandGrid.EnemyDeployment"/>가 채워집니다.
        ///
        /// <b>구역은 절대 비지 않습니다.</b>
        /// 거리로 자르면 한쪽 끝이 통째로 절벽일 때 구역이 비고, 그러면 부대를 세울 곳이 없어집니다.
        /// 대신 축에 투영해 <b>정렬한 뒤 개수로</b> 자릅니다. 통행 가능한 칸이 하나라도 있으면
        /// 양쪽 모두 최소 한 칸을 받습니다.
        /// </summary>
        /// <param name="grid">전장입니다.</param>
        /// <param name="seed">대치 축을 정하는 시드입니다. 같은 값은 같은 축을 만듭니다.</param>
        /// <param name="depthRatio">한 진영이 차지하는 비율입니다.</param>
        public static void Build(IslandGrid grid, int seed, float depthRatio = DefaultDepthRatio)
        {
            if (grid == null)
            {
                return;
            }

            grid.PlayerDeployment.Clear();
            grid.EnemyDeployment.Clear();

            int count = grid.WalkableTiles.Count;
            if (count == 0)
            {
                return;
            }

            Vector3 axis = BattleAxis.Resolve(seed);
            Vector3 center = grid.WorldCenter;

            // 축 위의 위치로 줄을 세웁니다. 앞쪽이 한 진영, 뒤쪽이 다른 진영입니다.
            var ordered = new List<Tile>(grid.WalkableTiles);

            ordered.Sort((a, b) => Project(a, center, axis).CompareTo(Project(b, center, axis)));

            int band = Mathf.Clamp(Mathf.RoundToInt(count * Mathf.Clamp01(depthRatio)), 1, count / 2);

            // 칸이 하나뿐이면 양쪽이 같은 칸을 나눠 가질 수는 없습니다.
            // 그래도 전투가 시작되도록 한쪽씩 배정합니다.
            if (count == 1)
            {
                grid.PlayerDeployment.Add(ordered[0]);
                grid.EnemyDeployment.Add(ordered[0]);
                return;
            }

            for (int i = 0; i < band; i++)
            {
                grid.PlayerDeployment.Add(ordered[i]);
                grid.EnemyDeployment.Add(ordered[count - 1 - i]);
            }
        }

        /// <summary>
        /// 전개 구역의 중심입니다. 부대가 처음 바라볼 방향을 정하는 기준이 됩니다.
        /// 비어 있으면 전장 중심을 돌려줍니다.
        /// </summary>
        /// <param name="zone">중심을 구할 전개 구역입니다.</param>
        /// <param name="grid">구역이 비었을 때 기준이 될 지형입니다.</param>
        /// <returns>구역에 속한 칸들의 평균 위치입니다. 비어 있으면 전장 중심입니다.</returns>
        public static Vector3 CenterOf(IReadOnlyList<Tile> zone, IslandGrid grid)
        {
            if (zone == null || zone.Count == 0)
            {
                return grid != null ? grid.WorldCenter : Vector3.zero;
            }

            Vector3 sum = Vector3.zero;

            for (int i = 0; i < zone.Count; i++)
            {
                sum += zone[i].WorldCenter;
            }

            return sum / zone.Count;
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>전장 중심에서 잰 축 위의 위치입니다.</summary>
        private static float Project(Tile tile, Vector3 center, Vector3 axis)
        {
            Vector3 offset = tile.WorldCenter - center;

            return offset.x * axis.x + offset.z * axis.z;
        }
    }
}
