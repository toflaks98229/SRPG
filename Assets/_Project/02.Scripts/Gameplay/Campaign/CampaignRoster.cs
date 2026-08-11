using System.Collections.Generic;
using SRPG.Data;
using UnityEngine;

namespace SRPG.Gameplay.Campaign
{
    /// <summary>
    /// 캠페인이 거느린 부대의 <b>장부</b>입니다. 전투보다 오래 삽니다.
    ///
    /// <b>여기가 부대의 유일한 출처입니다</b>
    ///
    /// 전투는 부대를 만들어 내지 않습니다. 이 장부에 적힌 것을 주문서로 옮겨 세우고,
    /// 판이 끝나면 보고서를 받아 이 장부를 고칩니다.
    /// 그래서 지난 전투에서 다친 부대가 다음 전장에 다친 채로 섭니다 —
    /// 그것이 없으면 매 전투가 언제나 처음부터입니다.
    ///
    /// <b>왜 전투가 이것을 직접 보지 않는가</b>
    ///
    /// 전투는 <see cref="BattleRequest"/> 만 받습니다. 장부를 통째로 넘기면
    /// 전투가 캠페인의 자료 구조를 알게 되고, 그 순간 전투를 캠페인 없이 열 수 없게 됩니다.
    /// 지금 자동 검사가 전투만 따로 여는 것이 가능한 이유가 그 경계입니다.
    /// </summary>
    public sealed class CampaignRoster
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>거느린 분대입니다. 순서가 곧 투입 순서입니다.</summary>
        private readonly List<CampaignSquad> _squads = new List<CampaignSquad>(8);

        /// <summary>다음 분대에 붙일 식별자입니다. 한 번 쓴 번호는 다시 쓰지 않습니다.</summary>
        private int _nextId = 1;

        /// <summary>전투 성과를 성장으로 옮기는 규칙입니다. 절대 null이 아닙니다.</summary>
        private readonly CampaignProgression _progression;

        // ====================================================================================================
        // 1-1. Constructor
        // ====================================================================================================

        /// <param name="progression">
        /// 성장 규칙입니다. 비우면 기본 곡선을 씁니다 —
        /// 규칙 없이 장부만 만드는 경로(검사)가 성장까지 신경 쓰지 않아도 되게 합니다.
        /// </param>
        public CampaignRoster(CampaignProgression progression = null)
        {
            _progression = progression ?? new CampaignProgression();
        }

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>거느린 분대입니다.</summary>
        public IReadOnlyList<CampaignSquad> Squads => _squads;

        /// <summary>아직 남아 있는 분대 수입니다.</summary>
        public int LivingSquadCount
        {
            get
            {
                int count = 0;

                for (int i = 0; i < _squads.Count; i++)
                {
                    if (_squads[i].IsAlive)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        // ====================================================================================================
        // 3. Public Methods - Roster
        // ====================================================================================================

        /// <summary>분대를 장부에 올립니다. 식별자는 장부가 붙입니다.</summary>
        /// <param name="definition">병과입니다.</param>
        /// <param name="soldierCount">지휘관을 제외한 인원입니다.</param>
        /// <param name="displayName">표시할 이름입니다. 비우면 병과 이름을 씁니다.</param>
        /// <param name="proficiency">시작 숙련도입니다. 비우면 미숙 상태로 시작합니다.</param>
        /// <returns>장부에 오른 분대입니다.</returns>
        public CampaignSquad Enlist(
            UnitDefinition definition,
            int soldierCount,
            string displayName = null,
            WeaponProficiency proficiency = default)
        {
            var squad = new CampaignSquad
            {
                Id = _nextId++,
                Definition = definition,
                SoldierCount = soldierCount,
                MaxSoldiers = soldierCount,
                DisplayName = displayName,
                Proficiency = proficiency,
            };

            _squads.Add(squad);

            return squad;
        }

        /// <summary>식별자로 분대를 찾습니다.</summary>
        /// <param name="id">찾을 분대의 식별자입니다.</param>
        /// <returns>찾은 분대입니다. 없으면 null입니다.</returns>
        public CampaignSquad Find(int id)
        {
            for (int i = 0; i < _squads.Count; i++)
            {
                if (_squads[i].Id == id)
                {
                    return _squads[i];
                }
            }

            return null;
        }

        // ====================================================================================================
        // 4. Public Methods - Battle Boundary
        // ====================================================================================================

        /// <summary>
        /// 장부에 적힌 부대로 이번 전투의 주문서를 씁니다.
        ///
        /// <b>살아 있는 분대만 데려갑니다.</b> 무너진 분대를 인원 0으로 내보내면
        /// 전장에 지휘관만 선 유령 분대가 생깁니다.
        /// </summary>
        /// <param name="enemySquads">상대가 데리고 나온 분대입니다.</param>
        /// <param name="battlefield">어디서 싸우는지입니다.</param>
        /// <param name="seed">지형과 배치를 재현하는 시드입니다.</param>
        /// <returns>전투에 넘길 주문서입니다.</returns>
        public BattleRequest BuildRequest(
            IReadOnlyList<SquadOrder> enemySquads, BattlefieldSpec battlefield, int seed)
        {
            var request = new BattleRequest
            {
                Seed = seed,
                Battlefield = battlefield,
            };

            for (int i = 0; i < _squads.Count; i++)
            {
                if (_squads[i].IsAlive)
                {
                    request.PlayerSquads.Add(_squads[i].ToOrder());
                }
            }

            if (enemySquads != null)
            {
                for (int i = 0; i < enemySquads.Count; i++)
                {
                    request.EnemySquads.Add(enemySquads[i]);
                }
            }

            return request;
        }

        /// <summary>
        /// 전투가 돌려준 보고를 장부에 반영합니다.
        ///
        /// 무너진 분대는 장부에서 지웁니다. 남은 분대는 인원만 줄어듭니다.
        ///
        /// <b>보고에 없는 분대는 건드리지 않습니다.</b>
        /// 이번 전투에 나가지 않은 부대가 있을 수 있고, 그들이 손실을 볼 이유는 없습니다.
        /// </summary>
        /// <param name="result">전투가 남긴 보고서입니다. null이면 아무것도 하지 않습니다.</param>
        public void ApplyResult(BattleResult result)
        {
            if (result == null)
            {
                return;
            }

            for (int i = 0; i < result.Squads.Count; i++)
            {
                var report = result.Squads[i];
                var squad = Find(report.Id);

                if (squad == null)
                {
                    // 전투가 캠페인이 모르는 식별자를 돌려주었습니다. 조용히 넘기면
                    // "손실이 반영되지 않는다"는 증상만 남고 원인은 보이지 않습니다.
                    Debug.LogWarning($"[CampaignRoster] 보고서의 {report.Id}번 분대가 장부에 없습니다.");
                    continue;
                }

                squad.ApplyReport(report);

                // 손실을 반영한 <b>뒤에</b> 성장을 얹습니다.
                // 순서가 뒤바뀌면 무너진 분대가 한 번 성장하고 사라집니다.
                _progression.Apply(squad, report);
            }

            _squads.RemoveAll(squad => !squad.IsAlive);
        }
    }
}
