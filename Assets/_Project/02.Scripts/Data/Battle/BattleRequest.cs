using System.Collections.Generic;
using SRPG.Common;
using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 전투 한 판에 무엇을 들고 나가는지에 대한 <b>주문서</b>입니다.
    ///
    /// <b>왜 필요한가</b>
    ///
    /// 지금까지 전투는 스스로 모든 것을 정했습니다 — 어떤 병과를 몇 분대,
    /// 몇 명씩 데려갈지를 조립 지점이 인스펙터 값에서 읽었습니다.
    /// 전투가 한 판으로 끝나는 동안에는 그래도 됐습니다.
    ///
    /// 캠페인이 붙으면 그 전제가 깨집니다. <b>무엇을 데려가는가는 바깥이 정합니다.</b>
    /// 지난 전투에서 살아남은 분대, 그동안 쌓인 숙련도, 보충한 인원 —
    /// 전투는 그것을 만들어 내는 것이 아니라 받아서 씁니다.
    ///
    /// 이 타입이 그 경계입니다. 전투는 <see cref="BattleRequest"/>를 받고
    /// <see cref="BattleResult"/>를 내놓습니다. 그 사이의 일만 전투의 소관입니다.
    ///
    /// <b>왜 순수 데이터인가</b>
    ///
    /// 캠페인이 저장되고 불러와져야 하므로 직렬화가 가능해야 하고,
    /// 전투가 없는 상태에서도 만들고 검사할 수 있어야 합니다.
    /// MonoBehaviour도 씬 참조도 들어가면 안 됩니다.
    /// </summary>
    [System.Serializable]
    public sealed class BattleRequest
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>지형과 배치를 재현하는 시드입니다. 0이면 매번 달라집니다.</summary>
        public int Seed;

        /// <summary>
        /// 어디서 싸우는지입니다.
        ///
        /// 월드맵이 붙으면 좌표가 이 값을 채웁니다 — 숲에서 만났으면 숲 전장이 나옵니다.
        /// </summary>
        public BattlefieldSpec Battlefield;

        /// <summary>데리고 나가는 분대들입니다.</summary>
        public List<SquadOrder> PlayerSquads = new List<SquadOrder>();

        /// <summary>
        /// 적이 밀려오는 구성입니다.
        ///
        /// 아직은 웨이브 정의를 그대로 씁니다. 양측이 처음부터 배치되는 야전으로 바꾸는 것은
        /// 전투 흐름 자체를 고치는 일이라 이 단계의 범위 밖입니다.
        /// 여기서 중요한 것은 <b>그 선택을 바깥이 한다</b>는 사실입니다.
        /// </summary>
        public WaveDefinition EnemyWaves;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>데리고 나가는 병사의 총수입니다. 지휘관을 포함합니다.</summary>
        public int TotalSoldiers
        {
            get
            {
                int total = 0;

                for (int i = 0; i < PlayerSquads.Count; i++)
                {
                    total += PlayerSquads[i].SoldierCount + 1;
                }

                return total;
            }
        }

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 주문서가 전투를 시작할 수 있는 상태인지 봅니다.
        ///
        /// <b>왜 검사가 필요한가</b>
        /// 분대가 없거나 병과가 비어 있으면 전투가 조용히 텅 빈 채로 시작됩니다.
        /// 캠페인이 잘못 채운 것을 전투 도중에 알아차리면 원인을 찾기 어렵습니다.
        /// </summary>
        /// <param name="reason">문제가 있으면 그 이유입니다.</param>
        public bool IsValid(out string reason)
        {
            if (PlayerSquads == null || PlayerSquads.Count == 0)
            {
                reason = "데리고 나갈 분대가 없습니다.";
                return false;
            }

            for (int i = 0; i < PlayerSquads.Count; i++)
            {
                var order = PlayerSquads[i];

                if (order.Definition == null)
                {
                    reason = $"{i}번 분대에 병과가 지정되지 않았습니다.";
                    return false;
                }

                if (order.SoldierCount < 0)
                {
                    reason = $"{i}번 분대의 인원이 음수입니다.";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 같은 병과의 분대를 여럿 세워 간단한 주문서를 만듭니다.
        ///
        /// 캠페인이 아직 없을 때 프로토타입이 쓰는 경로입니다.
        /// 캠페인이 붙으면 이 메서드는 쓰이지 않고, 로스터가 직접 주문서를 채웁니다.
        /// </summary>
        public static BattleRequest CreateSimple(
            IReadOnlyList<UnitDefinition> roster,
            int squadCount,
            int soldiersPerSquad,
            int seed = 0)
        {
            var request = new BattleRequest { Seed = seed };

            if (roster == null || roster.Count == 0)
            {
                return request;
            }

            for (int i = 0; i < squadCount; i++)
            {
                request.PlayerSquads.Add(new SquadOrder
                {
                    Id = i + 1,
                    Definition = roster[i % roster.Count],
                    SoldierCount = soldiersPerSquad,
                    Rank = CombatConstants.MinRank,
                });
            }

            return request;
        }
    }

    /// <summary>
    /// 분대 하나를 전장에 세우는 지시입니다.
    ///
    /// <b>분대가 단위입니다.</b>
    ///
    /// 병사는 이름도 이력도 없는 <b>인원 수</b>입니다. 식별과 숙련과 보고는 전부 분대가 집니다.
    /// 그래서 여기에 병사 목록이 없고 <see cref="SoldierCount"/> 하나만 있습니다.
    ///
    /// 개별 병사를 영속시키면 관리 부담이 급격히 커지고, 전투마다 수십 명의
    /// 상태를 오가며 저장해야 합니다. 분대를 단위로 두면 캠페인이 다루는 것이
    /// 열 개 남짓으로 줄어들고, "3분대가 무너졌다"가 그대로 서사가 됩니다.
    /// </summary>
    [System.Serializable]
    public struct SquadOrder
    {
        /// <summary>
        /// 캠페인이 부여한 분대 식별자입니다.
        ///
        /// 전투는 이 값을 <b>해석하지 않고 그대로 결과에 돌려줍니다.</b>
        /// 전투가 캠페인의 자료 구조를 알 필요가 없게 만드는 장치입니다.
        /// </summary>
        public int Id;

        /// <summary>병과입니다.</summary>
        public UnitDefinition Definition;

        /// <summary>지휘관을 제외한 병사 수입니다.</summary>
        public int SoldierCount;

        /// <summary>분대 숙련도입니다.</summary>
        public int Rank;

        /// <summary>HUD에 표시할 이름입니다. 비우면 병과 이름을 씁니다.</summary>
        public string DisplayName;

        /// <summary>표시할 이름을 정합니다.</summary>
        public string ResolveName()
        {
            if (!string.IsNullOrEmpty(DisplayName))
            {
                return DisplayName;
            }

            return Definition != null ? Definition.DisplayName : "분대";
        }

        /// <summary>숙련도를 허용 범위로 묶습니다.</summary>
        public int ClampedRank()
        {
            return Mathf.Clamp(Rank, CombatConstants.MinRank, CombatConstants.MaxRank);
        }
    }
}
