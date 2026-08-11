using System;

namespace SRPG.Core
{
    /// <summary>
    /// 게임이 지금 어느 층에 있는지입니다.
    ///
    /// <b>화면이 아니라 층입니다</b>
    ///
    /// 여기에 "일시정지"나 "설정 창 열림" 같은 것을 넣지 마십시오.
    /// 그런 것들은 층을 옮기지 않습니다 — 전투 중에 설정을 열어도 전투는 그대로 진행 중입니다.
    /// 이 열거형이 구분하는 것은 <b>무엇이 살아 있는가</b>입니다.
    /// 층이 바뀌면 살아 있는 스코프가 바뀌고, 그때 무엇이 파괴되고 무엇이 남는지가 달라집니다.
    /// </summary>
    public enum GameState
    {
        /// <summary>전역 시스템을 세우는 중입니다. 아직 아무 판도 시작되지 않았습니다.</summary>
        Booting = 0,

        /// <summary>월드맵입니다. 캠페인이 살아 있고 전장은 없습니다.</summary>
        WorldMap = 1,

        /// <summary>전장입니다. 캠페인 위에 전투 한 판이 얹혀 있습니다.</summary>
        Battle = 2,
    }

    /// <summary>
    /// 지금의 층을 알리고 바꾸는 창구입니다.
    ///
    /// 층이 바뀌는 것을 알아야 하는 쪽은 대개 자기 일을 접거나 펴야 하는 쪽입니다 —
    /// 입력을 어느 쪽으로 보낼지, 어떤 배경음을 틀지 같은 것들입니다.
    /// </summary>
    public interface IGameStateService
    {
        /// <summary>지금의 층입니다.</summary>
        GameState CurrentState { get; }

        /// <summary>층이 바뀌면 새 층과 함께 불립니다. 같은 층으로 바꾸면 불리지 않습니다.</summary>
        event Action<GameState> StateChanged;

        /// <summary>층을 옮깁니다.</summary>
        /// <param name="next">옮겨 갈 층입니다. 지금과 같으면 아무것도 하지 않습니다.</param>
        void ChangeState(GameState next);
    }
}
