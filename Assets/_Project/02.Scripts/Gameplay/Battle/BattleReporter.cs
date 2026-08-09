using System.Collections.Generic;
using SRPG.Data;
using SRPG.Systems.Battle;
using UnityEngine;

namespace SRPG.Gameplay.Battle
{
    /// <summary>
    /// 전황 보고에 필요한 분대의 상태입니다.
    ///
    /// <b>왜 인터페이스인가</b>
    ///
    /// 보고서를 만드는 일은 씬도 유닛도 몰라야 합니다. "왜 승리 처리가 안 됐는가"를
    /// 재생해 가며 찾는 상황을 없애려면 판정과 집계가 씬 밖에서 돌아야 합니다.
    ///
    /// <b>파괴된 오브젝트를 어떻게 다루는가</b>
    ///
    /// 유니티의 <c>== null</c> 은 파괴된 오브젝트를 null 로 봐 주지만, 인터페이스 너머에서는
    /// 그 연산자를 타지 않습니다(<c>TileOccupancy</c>가 같은 함정을 문서로 남겨 두었습니다).
    /// 여기서는 그 함정이 성립하지 않습니다 — <b>분대가 죽기 전에 스스로 깃발을 세우기</b> 때문입니다.
    /// <c>Squad</c>는 오브젝트를 파괴하기 <b>앞서</b> 상태를 <c>Destroyed</c> 로 바꾸므로,
    /// 파괴된 뒤에도 <see cref="IsDestroyed"/>는 관리 힙의 필드만 읽어 정확히 true 를 돌려줍니다.
    /// </summary>
    public interface ISquadStatus
    {
        /// <summary>분대가 소멸했는지 여부입니다. 지휘관을 잃으면 남은 병사가 있어도 true 입니다.</summary>
        bool IsDestroyed { get; }

        /// <summary>살아 있는 병사 수입니다.</summary>
        int AliveCount { get; }
    }

    /// <summary>
    /// 전황을 관측해 결말을 판정하고 보고서를 만듭니다.
    ///
    /// <b>왜 조립 지점에서 떼어 내는가</b>
    ///
    /// 부트스트랩은 조립만 해야 합니다. 그런데 승패 관측·집계·보고서 작성이 그 안에 있었고,
    /// 그건 <b>매 프레임 도는 게임 로직</b>이라 조립과 성격이 다릅니다.
    /// 무엇보다 씬을 재생하지 않으면 확인할 수 없었습니다 —
    /// 캠페인이 붙으면 가장 먼저 의심하게 될 코드인데도 그랬습니다.
    ///
    /// 규칙 자체는 <see cref="BattleConclusion"/>이 들고 있고, 여기서는 관측값을 모아 넘기고
    /// 그 결과로 보고서를 씁니다.
    /// </summary>
    public sealed class BattleReporter
    {
        // ====================================================================================================
        // 1. Types
        // ====================================================================================================

        /// <summary>
        /// 전장에 세운 분대 하나의 기록입니다.
        ///
        /// 주문서의 식별자와 실제 분대를 이어 둡니다. 전투가 끝나면 이 짝으로 보고서를 씁니다 —
        /// 분대가 파괴되어 오브젝트가 사라져도 <see cref="Id"/>와 <see cref="Deployed"/>는 남아야 합니다.
        /// </summary>
        private readonly struct Entry
        {
            public readonly int Id;
            public readonly ISquadStatus Squad;
            public readonly int Deployed;

            public Entry(int id, ISquadStatus squad, int deployed)
            {
                Id = id;
                Squad = squad;
                Deployed = deployed;
            }

            /// <summary>분대가 아직 싸우고 있는지 여부입니다.</summary>
            public bool IsAlive => Squad != null && !Squad.IsDestroyed;
        }

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        /// <summary>전장에 세운 분대의 기록입니다. 오브젝트가 사라져도 식별자와 배치 인원은 남습니다.</summary>
        private readonly List<Entry> _deployed = new List<Entry>(8);
        /// <summary>승패 규칙입니다. 관측값을 넣으면 결말을 돌려줍니다.</summary>
        private readonly BattleConclusion _conclusion = new BattleConclusion();

        /// <summary>지금까지 전장에 나온 적의 총수입니다.</summary>
        private int _enemiesSeen;

        /// <summary>직전에 관측한 생존 적 수입니다. 증감을 읽는 기준입니다.</summary>
        private int _previousAlive;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>지금까지 판정된 결말입니다. 한 번 정해지면 바뀌지 않습니다.</summary>
        public BattleOutcome Outcome => _conclusion.Outcome;

        /// <summary>결말이 정해졌는지 여부입니다.</summary>
        public bool IsDecided => _conclusion.IsDecided;

        /// <summary>전투가 시작된 뒤 흐른 시간입니다.</summary>
        public float Elapsed => _conclusion.Elapsed;

        /// <summary>지금까지 쓰러뜨린 적의 수입니다.</summary>
        public int EnemiesKilled => Mathf.Max(0, _enemiesSeen - _previousAlive);

        // ====================================================================================================
        // 4. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 전장에 세운 분대를 기록합니다. 배치 직후에 부릅니다.
        /// </summary>
        /// <param name="id">주문서가 준 식별자입니다. 해석하지 않고 보고서에 그대로 돌려줍니다.</param>
        /// <param name="squad">분대입니다.</param>
        /// <param name="deployed">전장에 세운 인원입니다. 지휘관을 포함합니다.</param>
        public void Track(int id, ISquadStatus squad, int deployed)
        {
            _deployed.Add(new Entry(id, squad, deployed));
        }

        /// <summary>
        /// 관측값을 넣고 결말을 살핍니다.
        /// </summary>
        /// <param name="deltaTime">지난 시간입니다.</param>
        /// <param name="enemiesAlive">지금 살아 있는 적 유닛 수입니다.</param>
        /// <param name="playerReserves">
        /// 아직 전장에 나가지 않은 아군 분대 수입니다.
        /// <b>전장이 비었어도 뒤에 부대가 남아 있으면 진 것이 아닙니다.</b>
        /// </param>
        /// <param name="enemyReinforcementsExhausted">더 올라올 적 지원군이 없는지 여부입니다.</param>
        /// <returns>이번 호출에서 결말이 <b>새로</b> 정해졌으면 보고서, 아니면 null 입니다.</returns>
        public BattleResult Tick(
            float deltaTime, int enemiesAlive, int playerReserves, bool enemyReinforcementsExhausted)
        {
            if (_conclusion.IsDecided)
            {
                return null;
            }

            ObserveEnemies(enemiesAlive);

            bool decided = _conclusion.Tick(
                deltaTime,
                CountLivingSquads() + Mathf.Max(0, playerReserves),
                enemiesAlive,
                enemyReinforcementsExhausted);

            return decided ? BuildResult() : null;
        }

        /// <summary>다음 전투를 위해 되돌립니다.</summary>
        public void Reset()
        {
            _deployed.Clear();
            _conclusion.Reset();

            _enemiesSeen = 0;
            _previousAlive = 0;
        }

        // ====================================================================================================
        // 5. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 적의 총원을 셉니다.
        ///
        /// <b>왜 증감으로 세는가</b>
        ///
        /// 적은 파도로 나뉘어 들어오므로 처음에 총원을 알 수 없습니다.
        /// 그렇다고 유닛마다 죽음을 구독하면 보고 담당이 전투 세부를 알게 됩니다.
        /// 생존 수의 <b>증감</b>만 보면 둘 다 피할 수 있습니다 —
        /// 늘어난 만큼은 새로 나온 적이고, 줄어든 만큼은 쓰러진 적입니다.
        ///
        /// 한 프레임 안에서 한 명이 상륙하고 한 명이 쓰러지면 그 둘이 상쇄되어
        /// 총원이 한 명 적게 잡힙니다. 상륙 간격이 0.35초라 드물고,
        /// 이 값은 보고용 집계라 그 정도 오차는 감수합니다.
        /// </summary>
        private void ObserveEnemies(int enemiesAlive)
        {
            if (enemiesAlive > _previousAlive)
            {
                _enemiesSeen += enemiesAlive - _previousAlive;
            }

            _previousAlive = enemiesAlive;
        }

        private int CountLivingSquads()
        {
            int alive = 0;

            for (int i = 0; i < _deployed.Count; i++)
            {
                if (_deployed[i].IsAlive)
                {
                    alive++;
                }
            }

            return alive;
        }

        /// <summary>
        /// 분대별 전황을 모아 보고서를 만듭니다.
        /// </summary>
        private BattleResult BuildResult()
        {
            var result = new BattleResult
            {
                Outcome = _conclusion.Outcome,
                Duration = _conclusion.Elapsed,
                EnemiesKilled = EnemiesKilled,
            };

            for (int i = 0; i < _deployed.Count; i++)
            {
                var entry = _deployed[i];

                result.Squads.Add(new SquadReport
                {
                    Id = entry.Id,
                    Deployed = entry.Deployed,

                    // 분대가 무너지면 남은 병사도 함께 사라집니다.
                    // 지휘관을 잃은 분대는 흩어지므로 생존자로 세지 않습니다.
                    Survivors = entry.IsAlive ? entry.Squad.AliveCount : 0,
                    Destroyed = !entry.IsAlive,
                });
            }

            return result;
        }
    }
}
