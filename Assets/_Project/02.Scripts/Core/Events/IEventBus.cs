using System;

namespace SRPG.Core.Events
{
    /// <summary>
    /// 발신자와 수신자가 서로를 모른 채 소식을 주고받는 통로입니다.
    ///
    /// <b>무엇에 쓰는가</b>
    ///
    /// 이 게임에서 이것이 꼭 필요한 자리는 <b>수명이 다른 두 층 사이</b>입니다.
    /// 전투는 한 판이 끝나면 통째로 사라지고 캠페인은 남습니다.
    /// 캠페인이 전투 오브젝트를 직접 구독하면, 그 참조는 전투가 끝나는 순간 죽은 참조가 됩니다.
    ///
    /// 소식을 버스에 올리면 전투는 누가 듣는지 몰라도 되고,
    /// 캠페인은 이번 판의 전투 오브젝트가 무엇인지 몰라도 됩니다.
    ///
    /// <b>남용하지 마십시오</b>
    ///
    /// 같은 수명 안에서 서로를 아는 것이 자연스러운 관계까지 버스로 옮기면,
    /// 호출 관계가 코드에서 사라져 "이 값을 누가 바꾸는가"를 추적할 수 없게 됩니다.
    /// 그런 곳에는 생성자 주입을 쓰십시오.
    /// </summary>
    public interface IEventBus
    {
        /// <summary>지정 타입의 소식을 듣습니다.</summary>
        /// <typeparam name="T">들을 소식의 타입입니다.</typeparam>
        /// <param name="handler">소식이 올 때 불릴 함수입니다. null이면 아무것도 하지 않습니다.</param>
        void Subscribe<T>(Action<T> handler);

        /// <summary>구독을 거둡니다.</summary>
        /// <typeparam name="T">거둘 소식의 타입입니다.</typeparam>
        /// <param name="handler">거둘 함수입니다. 등록되지 않았어도 안전합니다.</param>
        void Unsubscribe<T>(Action<T> handler);

        /// <summary>소식을 냅니다. 듣는 이가 없어도 오류가 아닙니다.</summary>
        /// <typeparam name="T">낼 소식의 타입입니다.</typeparam>
        /// <param name="message">전달할 내용입니다.</param>
        void Publish<T>(T message);

        /// <summary>모든 구독을 거둡니다.</summary>
        void Clear();
    }
}
