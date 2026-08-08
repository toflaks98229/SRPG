using SRPG.Data;

namespace SRPG.Systems.Battle
{
    /// <summary>
    /// 전투가 끝났는지, 끝났다면 어떻게 끝났는지 판정합니다.
    ///
    /// <b>왜 따로 떼어 놓는가</b>
    ///
    /// 지금까지 이 게임에는 <b>끝이 없었습니다.</b> 적을 다 잡아도, 분대를 다 잃어도
    /// 아무 일도 일어나지 않고 그대로 계속 돌았습니다. 한 판짜리 프로토타입이라 됐습니다.
    ///
    /// 캠페인이 붙으면 끝이 반드시 있어야 합니다. 그리고 그 판정은
    /// <b>씬도 유닛도 모르는 순수한 규칙</b>이어야 합니다 —
    /// 조립 지점 안에 묻어 두면 "왜 승리 처리가 안 됐는가"를 재생해 가며 찾아야 합니다.
    ///
    /// 그래서 관측값 셋만 받습니다. 그 셋이면 결말이 정해집니다.
    ///
    /// <b>왜 "적이 0명"만으로 승리가 아닌가</b>
    ///
    /// 다음 파도가 아직 안 왔을 뿐일 수 있습니다.
    /// 배가 오는 사이의 빈 순간을 승리로 읽으면 전투가 시작하자마자 끝납니다.
    /// 마지막 파도까지 나온 뒤에 비어 있어야 진짜로 끝난 것입니다.
    ///
    /// <b>패배가 승리보다 먼저입니다</b>
    ///
    /// 마지막 분대와 마지막 적이 같은 순간에 쓰러질 수 있습니다.
    /// 지킬 사람이 없으면 지켜 낸 것이 아니므로 패배로 봅니다.
    /// </summary>
    public sealed class BattleConclusion
    {
        // ====================================================================================================
        // 1. Properties
        // ====================================================================================================

        /// <summary>지금까지 판정된 결말입니다. 한 번 정해지면 바뀌지 않습니다.</summary>
        public BattleOutcome Outcome { get; private set; } = BattleOutcome.Undecided;

        /// <summary>결말이 정해졌는지 여부입니다.</summary>
        public bool IsDecided => Outcome != BattleOutcome.Undecided;

        /// <summary>전투가 시작된 뒤 흐른 시간입니다. 초 단위입니다.</summary>
        public float Elapsed { get; private set; }

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 관측값으로 결말을 갱신합니다.
        /// </summary>
        /// <param name="deltaTime">지난 시간입니다.</param>
        /// <param name="playerSquadsAlive">살아 있는 아군 분대 수입니다.</param>
        /// <param name="enemyUnitsAlive">살아 있는 적 유닛 수입니다.</param>
        /// <param name="allWavesSpawned">마지막 파도까지 전부 나왔는지 여부입니다.</param>
        /// <returns>이번 호출에서 결말이 <b>새로</b> 정해졌으면 true입니다.</returns>
        public bool Tick(float deltaTime, int playerSquadsAlive, int enemyUnitsAlive, bool allWavesSpawned)
        {
            if (IsDecided)
            {
                return false;
            }

            Elapsed += deltaTime;

            // 지킬 사람이 없으면 지켜 낸 것이 아닙니다. 패배를 먼저 봅니다.
            if (playerSquadsAlive <= 0)
            {
                Outcome = BattleOutcome.Defeat;
                return true;
            }

            if (allWavesSpawned && enemyUnitsAlive <= 0)
            {
                Outcome = BattleOutcome.Victory;
                return true;
            }

            return false;
        }

        /// <summary>다음 전투를 위해 되돌립니다.</summary>
        public void Reset()
        {
            Outcome = BattleOutcome.Undecided;
            Elapsed = 0f;
        }
    }
}
