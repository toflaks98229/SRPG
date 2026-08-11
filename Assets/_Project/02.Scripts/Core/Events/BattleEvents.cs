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
}
