using System.Collections.Generic;

namespace SRPG.Data
{
    /// <summary>
    /// 전투가 끝나고 캠페인에 돌려주는 <b>전황 보고</b>입니다.
    ///
    /// <b>왜 필요한가</b>
    ///
    /// 지금까지 전투는 끝나도 아무것도 남기지 않았습니다. 씬이 사라지면 전부 사라졌고,
    /// 그래서 다음 전투는 언제나 처음부터였습니다.
    ///
    /// 캠페인이 붙으면 <b>지난 전투가 다음 전투를 바꿉니다.</b>
    /// 무너진 분대는 없어지고, 살아남은 분대는 인원이 줄어든 채로 다음 전장에 섭니다.
    /// 그 연결을 만드는 것이 이 타입입니다.
    ///
    /// <b>분대 단위로 보고합니다</b>
    ///
    /// 누가 죽었는지가 아니라 <b>몇 명이 남았는지</b>를 돌려줍니다.
    /// 병사는 이름이 없으므로 개별 생사를 추적할 이유도, 저장할 자리도 없습니다.
    /// </summary>
    public sealed class BattleResult
    {
        // ====================================================================================================
        // 1. Properties
        // ====================================================================================================

        /// <summary>전투의 결말입니다.</summary>
        public BattleOutcome Outcome { get; set; } = BattleOutcome.Undecided;

        /// <summary>분대별 보고입니다. 주문서에 있던 순서를 따릅니다.</summary>
        public List<SquadReport> Squads { get; } = new List<SquadReport>();

        /// <summary>쓰러뜨린 적의 수입니다.</summary>
        public int EnemiesKilled { get; set; }

        /// <summary>전투에 걸린 시간입니다. 초 단위입니다.</summary>
        public float Duration { get; set; }

        /// <summary>살아 돌아온 분대의 수입니다.</summary>
        public int SurvivingSquads
        {
            get
            {
                int count = 0;

                for (int i = 0; i < Squads.Count; i++)
                {
                    if (!Squads[i].Destroyed)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>전사한 병사의 총수입니다.</summary>
        public int TotalLosses
        {
            get
            {
                int total = 0;

                for (int i = 0; i < Squads.Count; i++)
                {
                    total += Squads[i].Losses;
                }

                return total;
            }
        }

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>디버그 표시용 문자열입니다.</summary>
        /// <returns>결말·소요 시간·전과를 한 줄로 적은 문자열입니다.</returns>
        public override string ToString()
        {
            return $"{Outcome} · 분대 {SurvivingSquads}/{Squads.Count} 생존 · " +
                   $"손실 {TotalLosses}명 · 적 {EnemiesKilled}명 처치 · {Duration:F0}초";
        }
    }

    /// <summary>
    /// 분대 하나의 전황입니다.
    /// </summary>
    public struct SquadReport
    {
        /// <summary>주문서가 준 식별자입니다. 캠페인이 이 값으로 자기 분대를 찾습니다.</summary>
        public int Id;

        /// <summary>전장에 세운 인원입니다. 지휘관을 포함합니다.</summary>
        public int Deployed;

        /// <summary>살아남은 인원입니다.</summary>
        public int Survivors;

        /// <summary>
        /// 분대가 소멸했는지 여부입니다.
        ///
        /// <b>생존자 0명과 같은 말이 아닙니다.</b>
        /// 이 게임에서는 지휘관이 쓰러지면 남은 병사가 있어도 분대가 무너집니다.
        /// 캠페인은 "몇 명 잃었는가"와 "분대가 사라졌는가"를 따로 알아야 합니다.
        /// </summary>
        public bool Destroyed;

        /// <summary>잃은 인원입니다.</summary>
        public int Losses => Deployed - Survivors;
    }

    /// <summary>
    /// 전투의 결말입니다.
    /// </summary>
    public enum BattleOutcome
    {
        /// <summary>아직 끝나지 않았습니다.</summary>
        Undecided = 0,

        /// <summary>적을 모두 물리쳤습니다.</summary>
        Victory = 1,

        /// <summary>모든 분대를 잃었습니다.</summary>
        Defeat = 2,
    }
}
