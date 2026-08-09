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

        /// <summary>데리고 나가는 분대들입니다. 순서가 곧 투입 순서입니다.</summary>
        public List<SquadOrder> PlayerSquads = new List<SquadOrder>();

        /// <summary>
        /// 상대가 데리고 나온 분대들입니다. 순서가 곧 투입 순서입니다.
        ///
        /// <b>양측을 같은 모양으로 적습니다.</b>
        ///
        /// 예전에는 적이 웨이브 정의로 왔습니다 — 몇 초 뒤에 배 몇 척, 척당 몇 명.
        /// 상륙을 막는 게임에서는 그것이 곧 압박의 형태였습니다.
        ///
        /// 야전은 다릅니다. 상대도 <b>전투 서열을 가진 부대</b>입니다.
        /// 월드맵에서 만난 그 군대가 그대로 전장에 서는 것이므로,
        /// 이쪽과 다른 형식으로 적을 이유가 없습니다.
        ///
        /// 전장이 좁아 한 번에 다 붙지 못하면 나머지는 지원군으로 들어옵니다.
        /// 그 상한과 간격은 전투 튜닝이 정합니다.
        /// </summary>
        public List<SquadOrder> EnemySquads = new List<SquadOrder>();

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>데리고 나가는 병사의 총수입니다. 지휘관을 포함합니다.</summary>
        public int TotalSoldiers => CountSoldiers(PlayerSquads);

        /// <summary>상대가 데리고 나온 병사의 총수입니다. 지휘관을 포함합니다.</summary>
        public int TotalEnemySoldiers => CountSoldiers(EnemySquads);

        private static int CountSoldiers(List<SquadOrder> squads)
        {
            if (squads == null)
            {
                return 0;
            }

            int total = 0;

            for (int i = 0; i < squads.Count; i++)
            {
                total += squads[i].SoldierCount + 1;
            }

            return total;
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
        /// <param name="reason">문제가 있으면 그 이유입니다. 온전하면 null입니다.</param>
        /// <returns>전투를 시작할 수 있으면 true입니다.</returns>
        public bool IsValid(out string reason)
        {
            if (PlayerSquads == null || PlayerSquads.Count == 0)
            {
                reason = "데리고 나갈 분대가 없습니다.";
                return false;
            }

            // 상대가 없으면 전투가 아닙니다. 예전에는 웨이브가 없어도 시작됐지만,
            // 야전에서는 마주 설 부대가 곧 전투의 전제입니다.
            if (EnemySquads == null || EnemySquads.Count == 0)
            {
                reason = "맞설 상대 분대가 없습니다.";
                return false;
            }

            if (!ValidateSquads(PlayerSquads, "아군", out reason))
            {
                return false;
            }

            return ValidateSquads(EnemySquads, "적", out reason);
        }

        private static bool ValidateSquads(List<SquadOrder> squads, string side, out string reason)
        {
            for (int i = 0; i < squads.Count; i++)
            {
                var order = squads[i];

                if (order.Definition == null)
                {
                    reason = $"{side} {i}번 분대에 병과가 지정되지 않았습니다.";
                    return false;
                }

                if (order.SoldierCount < 0)
                {
                    reason = $"{side} {i}번 분대의 인원이 음수입니다.";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// 양측에 같은 병과를 돌려 간단한 주문서를 만듭니다.
        ///
        /// 캠페인이 아직 없을 때 프로토타입이 쓰는 경로입니다.
        /// 캠페인이 붙으면 이 메서드는 쓰이지 않고, 양쪽 군대가 직접 주문서를 채웁니다.
        /// </summary>
        /// <param name="playerRoster">아군 병과 후보입니다. 분대마다 순서대로 배분됩니다.</param>
        /// <param name="enemyRoster">적 병과 후보입니다.</param>
        /// <param name="squadsPerSide">진영당 분대 수입니다.</param>
        /// <param name="soldiersPerSquad">분대당 병사 수입니다. 지휘관은 별도로 한 명 더 붙습니다.</param>
        /// <param name="seed">전장을 재현하는 시드입니다.</param>
        /// <returns>양측에 같은 수의 분대가 채워진 주문서입니다. 식별자는 아군 1번대, 적 101번대입니다.</returns>
        public static BattleRequest CreateSimple(
            IReadOnlyList<UnitDefinition> playerRoster,
            IReadOnlyList<UnitDefinition> enemyRoster,
            int squadsPerSide,
            int soldiersPerSquad,
            int seed = 0)
        {
            var request = new BattleRequest { Seed = seed };

            Fill(request.PlayerSquads, playerRoster, squadsPerSide, soldiersPerSquad, 1);

            // 식별자를 겹치지 않게 띄웁니다. 보고서가 양측을 같은 목록으로 다루게 될 때
            // 1번 분대가 둘이면 캠페인이 어느 쪽인지 알 수 없습니다.
            Fill(request.EnemySquads, enemyRoster, squadsPerSide, soldiersPerSquad, 101);

            return request;
        }

        private static void Fill(
            List<SquadOrder> target,
            IReadOnlyList<UnitDefinition> roster,
            int squadCount,
            int soldiersPerSquad,
            int firstId)
        {
            if (roster == null || roster.Count == 0)
            {
                return;
            }

            for (int i = 0; i < squadCount; i++)
            {
                target.Add(new SquadOrder
                {
                    Id = firstId + i,
                    Definition = roster[i % roster.Count],
                    SoldierCount = soldiersPerSquad,
                    Rank = CombatConstants.MinRank,
                });
            }
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
        /// <returns>지정된 이름입니다. 비어 있으면 병과 이름이고, 그것도 없으면 "분대"입니다.</returns>
        public string ResolveName()
        {
            if (!string.IsNullOrEmpty(DisplayName))
            {
                return DisplayName;
            }

            return Definition != null ? Definition.DisplayName : "분대";
        }

        /// <summary>숙련도를 허용 범위로 묶습니다.</summary>
        /// <returns>허용 범위 안으로 잘린 숙련도입니다.</returns>
        public int ClampedRank()
        {
            return Mathf.Clamp(Rank, CombatConstants.MinRank, CombatConstants.MaxRank);
        }
    }
}
