using SRPG.Data;

namespace SRPG.Core.Events
{
    /// <summary>
    /// 전투 한 판이 끝났다는 소식입니다.
    ///
    /// <b>왜 이것이 버스를 타야 하는가</b>
    ///
    /// 이 소식을 듣는 쪽은 캠페인입니다. 그런데 캠페인은 전투보다 오래 삽니다 —
    /// 전투 오브젝트는 판이 끝나면 씬과 함께 사라지고, 캠페인은 다음 전장으로 넘어갑니다.
    ///
    /// 캠페인이 전투 오브젝트의 C# 이벤트를 직접 구독하면 두 가지가 따라옵니다.
    /// 캠페인이 전투의 <b>타입</b>을 알아야 하고, 판이 끝날 때마다 구독을 정확히 거둬야 합니다.
    /// 거두기를 한 번 빠뜨리면 죽은 참조가 다음 판까지 따라갑니다.
    ///
    /// 버스를 타면 전투는 누가 듣는지 모르고, 캠페인은 이번 판의 전투가 무엇이었는지 모릅니다.
    /// 남는 것은 보고서 한 장뿐이고, 그것이 정확히 두 층 사이에 있어야 할 전부입니다.
    /// </summary>
    public readonly struct BattleConcludedEvent
    {
        /// <summary>전투가 남긴 보고서입니다. 절대 null이 아닙니다.</summary>
        public readonly BattleResult Result;

        /// <param name="result">전투가 남긴 보고서입니다.</param>
        public BattleConcludedEvent(BattleResult result)
        {
            Result = result;
        }
    }

    /// <summary>
    /// 사람이 전과를 다 읽고 전장을 떠나겠다고 알린 소식입니다.
    ///
    /// <b>왜 결말과 따로 두는가</b>
    ///
    /// 결말이 정해지는 순간과 사람이 그것을 <b>다 본</b> 순간은 다릅니다.
    /// 예전에는 이 둘이 하나였습니다 — 판정이 나자마자 캠페인이 씬을 갈아 끼워서,
    /// 무엇을 얻고 무엇을 잃었는지 볼 틈이 없었습니다.
    ///
    /// 그렇다고 캠페인 쪽에 대기 시간을 두면 안 됩니다. 얼마나 기다릴지는
    /// 화면이 무엇을 보여 주느냐에 달렸고, 그것은 캠페인이 알 일이 아닙니다.
    /// 떠날 때가 되었다고 <b>전장이 말하게</b> 두면 양쪽 다 상대를 몰라도 됩니다.
    ///
    /// 성적은 <see cref="BattleConcludedEvent"/> 가 이미 날랐습니다.
    /// 이 소식에는 실을 것이 없습니다 — 오직 시점만이 내용입니다.
    /// </summary>
    public readonly struct BattleDismissedEvent
    {
    }
}
