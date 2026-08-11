using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using UnityEngine;

namespace SRPG.Gameplay.Campaign
{
    /// <summary>
    /// 캠페인 한 회차의 진행을 쥐고 있습니다 — 부대가 어디에 있고, 어디로 갈 수 있고,
    /// 그 자리에서 누구와 싸우는지입니다.
    ///
    /// <b>씬을 모릅니다</b>
    ///
    /// 여기서 씬을 열거나 닫지 않습니다. 하는 일은 주문서를 쓰고 보고서를 장부에 반영하는 것까지이고,
    /// 그것을 화면으로 옮기는 일은 <c>CampaignEntryPoint</c> 가 합니다.
    ///
    /// 그 경계가 있어야 캠페인 진행 규칙을 씬 없이 검사할 수 있습니다 —
    /// "전투에서 진 뒤 부대가 제대로 줄었는가"를 확인하려고 전장을 열 필요가 없습니다.
    /// </summary>
    public sealed class CampaignDirector
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>거느린 부대의 장부입니다.</summary>
        private readonly CampaignRoster _roster;

        /// <summary>캠페인이 벌어지는 지도입니다.</summary>
        private readonly WorldMapDefinition _map;

        /// <summary>전투에 건넬 주문서를 올려 두는 자리입니다.</summary>
        private readonly BattleOrders _orders;

        /// <summary>적 분대 지시를 만들 때 쓰는 재사용 버퍼입니다.</summary>
        private readonly List<SquadOrder> _enemyBuffer = new List<SquadOrder>(8);

        /// <summary>
        /// 이미 쓸어 낸 지점들입니다.
        ///
        /// <b>지도 에셋에 적지 않습니다.</b> 지도는 기획이 그린 원본이고 이것은 이번 회차의 진행입니다.
        /// 스크립터블 오브젝트를 런타임에 고치면 에디터에서는 그 변경이 에셋 파일에 그대로 박혀,
        /// 다음에 재생할 때 이미 이겨 놓은 지도로 시작하게 됩니다.
        /// 빌드에서는 사라지므로 에디터에서만 나는 현상이 되고, 그래서 더 늦게 발견됩니다.
        /// </summary>
        private readonly HashSet<int> _clearedNodes = new HashSet<int>();

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>부대가 지금 있는 지점의 번호입니다.</summary>
        public int CurrentNode { get; private set; }

        /// <summary>캠페인이 시작된 뒤 지난 날수입니다. 지점을 옮길 때마다 하루가 갑니다.</summary>
        public int Day { get; private set; } = 1;

        /// <summary>거느린 부대의 장부입니다.</summary>
        public CampaignRoster Roster => _roster;

        /// <summary>캠페인이 벌어지는 지도입니다.</summary>
        public WorldMapDefinition Map => _map;

        /// <summary>부대가 지금 있는 지점입니다.</summary>
        public WorldNode CurrentLocation => _map.GetNode(CurrentNode);

        /// <summary>
        /// 캠페인이 끝났는지 여부입니다.
        ///
        /// 부대를 전부 잃으면 더 진행할 수 없습니다. 지도를 다 돌았는지는 여기서 보지 않습니다 —
        /// 그것은 승리 조건이고, 지금은 아직 정하지 않았습니다.
        /// </summary>
        public bool IsOver => _roster.LivingSquadCount == 0;

        // ====================================================================================================
        // 3. Constructor
        // ====================================================================================================

        /// <param name="roster">거느린 부대의 장부입니다.</param>
        /// <param name="map">캠페인이 벌어지는 지도입니다.</param>
        /// <param name="orders">전투에 건넬 주문서를 올려 둘 자리입니다.</param>
        [UnityEngine.Scripting.Preserve]
        public CampaignDirector(CampaignRoster roster, WorldMapDefinition map, BattleOrders orders)
        {
            _roster = roster;
            _map = map;
            _orders = orders;
        }

        // ====================================================================================================
        // 4. Public Methods - Movement
        // ====================================================================================================

        /// <summary>지금 자리에서 그 지점으로 갈 수 있는지 봅니다.</summary>
        /// <param name="node">가려는 지점 번호입니다.</param>
        /// <returns>길이 이어져 있고 지도 안이면 true입니다.</returns>
        public bool CanMoveTo(int node)
        {
            if (node < 0 || node >= _map.NodeCount || node == CurrentNode)
            {
                return false;
            }

            return _map.AreConnected(CurrentNode, node);
        }

        /// <summary>
        /// 부대를 그 지점으로 옮깁니다.
        ///
        /// <b>도착한 자리에 적이 있으면 주문서를 씁니다.</b>
        /// 쓰기만 하고 전투를 열지는 않습니다 — 여는 것은 씬을 아는 쪽의 일입니다.
        /// </summary>
        /// <param name="node">갈 지점 번호입니다.</param>
        /// <returns>도착한 자리에서 전투가 벌어지면 true입니다.</returns>
        public bool MoveTo(int node)
        {
            if (!CanMoveTo(node))
            {
                Debug.LogWarning($"[Campaign] {CurrentNode}번에서 {node}번으로는 갈 수 없습니다.");
                return false;
            }

            CurrentNode = node;
            Day++;

            if (!HasEnemyAt(node))
            {
                return false;
            }

            _orders.Place(BuildRequest(_map.GetNode(node)), node);

            return true;
        }

        /// <summary>
        /// 그 지점에 아직 싸울 상대가 남아 있는지 봅니다.
        ///
        /// 지도가 적어 둔 것과 이번 회차에 쓸어 낸 것을 함께 봅니다.
        /// </summary>
        /// <param name="node">볼 지점 번호입니다.</param>
        /// <returns>싸울 상대가 남아 있으면 true입니다.</returns>
        public bool HasEnemyAt(int node)
        {
            return _map.GetNode(node).HasEnemy && !_clearedNodes.Contains(node);
        }

        // ====================================================================================================
        // 5. Public Methods - Battle Boundary
        // ====================================================================================================

        /// <summary>
        /// 전투가 돌려준 보고를 장부에 반영합니다.
        ///
        /// <b>이긴 자리에서는 적을 지웁니다.</b> 그러지 않으면 같은 자리를 지날 때마다
        /// 같은 군대가 되살아나, 전투에서 이긴 것이 지도에 아무 흔적도 남기지 못합니다.
        /// </summary>
        /// <param name="result">전투가 남긴 보고서입니다.</param>
        public void ApplyResult(BattleResult result)
        {
            if (result == null)
            {
                return;
            }

            _roster.ApplyResult(result);

            if (result.Outcome == BattleOutcome.Victory)
            {
                _clearedNodes.Add(_orders.OriginNode);
            }

            Debug.Log(
                $"[Campaign] {_map.GetNode(_orders.OriginNode).DisplayName} 전투 종료 — " +
                $"{result.Outcome}, 남은 분대 {_roster.LivingSquadCount}");
        }

        // ====================================================================================================
        // 6. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 이 자리에서 벌어질 전투의 주문서를 씁니다.
        ///
        /// 아군은 장부가 채우고, 적은 지점이 채웁니다.
        /// </summary>
        /// <param name="node">전투가 벌어질 지점입니다.</param>
        /// <returns>전투에 넘길 주문서입니다.</returns>
        private BattleRequest BuildRequest(WorldNode node)
        {
            _enemyBuffer.Clear();

            var roster = node.EnemyRoster;
            int soldiers = Mathf.Max(1, node.SoldiersPerEnemySquad);

            for (int i = 0; i < node.EnemySquadCount; i++)
            {
                _enemyBuffer.Add(new SquadOrder
                {
                    // 아군 식별자와 겹치지 않게 띄웁니다. 보고서가 양측을 같은 목록으로 다룰 때
                    // 1번 분대가 둘이면 캠페인이 어느 쪽인지 알 수 없습니다.
                    Id = 101 + i,
                    Definition = roster[i % roster.Length],
                    SoldierCount = soldiers,
                    Rank = CombatConstants.MinRank,
                });
            }

            // 지형 시드가 비어 있으면 날짜로 채웁니다. 같은 자리를 다시 지나도 같은 전장이 나오지 않게,
            // 그러면서도 같은 판을 다시 열면 같은 전장이 나오게 하려는 것입니다.
            var battlefield = node.Battlefield;

            if (battlefield.Seed == 0)
            {
                battlefield.Seed = Day * 7919;
            }

            return _roster.BuildRequest(_enemyBuffer, battlefield, battlefield.Seed);
        }
    }
}
