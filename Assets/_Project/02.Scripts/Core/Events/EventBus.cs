using System;
using System.Collections.Generic;

namespace SRPG.Core.Events
{
    /// <summary>
    /// <see cref="IEventBus"/>의 기본 구현입니다.
    ///
    /// <b>구독자를 정적으로 두지 않습니다</b>
    ///
    /// 구독 목록은 이 인스턴스의 필드입니다. 정적 저장소로 두면 에디터에서 도메인 리로드를 끈 채
    /// 재생을 반복할 때 지난 세션의 구독자가 그대로 남아, 죽은 오브젝트에 소식이 계속 갑니다.
    /// 인스턴스로 두면 버스가 사라질 때 구독도 함께 사라집니다.
    ///
    /// <b>발행 도중의 구독·해제를 견딥니다</b>
    ///
    /// 소식을 받은 쪽이 그 자리에서 구독을 거두는 일은 흔합니다 — 전투 종료 소식을 받고
    /// 자기 구독을 정리하는 경우가 그렇습니다. 순회 중에 목록을 건드리면 인덱스가 밀리므로,
    /// 순회 중의 삭제는 자리를 null로 비워 두었다가 순회가 끝난 뒤에 실제로 지웁니다.
    ///
    /// <b>잠그지 않습니다</b>
    ///
    /// 이 버스는 메인 스레드에서만 쓰입니다. 유니티의 게임 오브젝트와 컴포넌트를 만지는 핸들러가
    /// 대부분이라 애초에 다른 스레드에서 부를 수 없습니다. 락을 걸면 전투 중 초당 수백 번 오가는
    /// 발행마다 비용만 더합니다.
    /// </summary>
    public sealed class EventBus : IEventBus
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>소식 타입별 구독자 목록입니다.</summary>
        private readonly Dictionary<Type, IHandlerList> _lists = new Dictionary<Type, IHandlerList>();

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <inheritdoc />
        public void Subscribe<T>(Action<T> handler)
        {
            if (handler == null)
            {
                return;
            }

            GetOrCreate<T>().Add(handler);
        }

        /// <inheritdoc />
        public void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null)
            {
                return;
            }

            GetOrNull<T>()?.Remove(handler);
        }

        /// <inheritdoc />
        public void Publish<T>(T message)
        {
            GetOrNull<T>()?.Publish(message);
        }

        /// <inheritdoc />
        public void Clear()
        {
            foreach (var pair in _lists)
            {
                pair.Value.Clear();
            }

            _lists.Clear();
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        private HandlerList<T> GetOrCreate<T>()
        {
            var type = typeof(T);

            if (_lists.TryGetValue(type, out var existing))
            {
                return (HandlerList<T>)existing;
            }

            var created = new HandlerList<T>();
            _lists[type] = created;
            return created;
        }

        private HandlerList<T> GetOrNull<T>()
        {
            return _lists.TryGetValue(typeof(T), out var list) ? (HandlerList<T>)list : null;
        }

        // ====================================================================================================
        // 4. Nested Types
        // ====================================================================================================

        /// <summary>제네릭 인자를 모른 채 목록을 비우기 위한 내부 계약입니다.</summary>
        private interface IHandlerList
        {
            /// <summary>이 타입의 구독자를 전부 거둡니다.</summary>
            void Clear();
        }

        /// <summary>
        /// 소식 하나에 대한 구독자 목록입니다.
        ///
        /// 멀티캐스트 델리게이트(<c>+=</c>) 대신 리스트를 쓰는 이유는
        /// 발행할 때마다 <c>GetInvocationList()</c>가 배열을 새로 만들기 때문입니다.
        /// 전투 중 초당 수백 번 발행되는 소식에서는 그 배열이 그대로 쓰레기가 됩니다.
        /// </summary>
        /// <typeparam name="T">이 목록이 맡은 소식 타입입니다.</typeparam>
        private sealed class HandlerList<T> : IHandlerList
        {
            /// <summary>구독자입니다. 순회 중 지워진 자리는 null로 비어 있습니다.</summary>
            private readonly List<Action<T>> _handlers = new List<Action<T>>(8);

            /// <summary>지금 몇 겹으로 순회 중인지입니다. 소식이 소식을 부르는 경우를 셉니다.</summary>
            private int _depth;

            /// <summary>순회가 끝나면 빈자리를 걷어 내야 하는지입니다.</summary>
            private bool _needsCompact;

            /// <summary>구독자를 넣습니다. 같은 함수를 두 번 넣지 않습니다.</summary>
            /// <param name="handler">넣을 함수입니다.</param>
            public void Add(Action<T> handler)
            {
                if (!_handlers.Contains(handler))
                {
                    _handlers.Add(handler);
                }
            }

            /// <summary>구독자를 뺍니다. 순회 중이면 자리만 비워 두고 나중에 걷어 냅니다.</summary>
            /// <param name="handler">뺄 함수입니다.</param>
            public void Remove(Action<T> handler)
            {
                int index = _handlers.IndexOf(handler);

                if (index < 0)
                {
                    return;
                }

                if (_depth > 0)
                {
                    _handlers[index] = null;
                    _needsCompact = true;
                    return;
                }

                _handlers.RemoveAt(index);
            }

            /// <summary>
            /// 구독자에게 소식을 돌립니다.
            ///
            /// 순회 시작 시점의 개수를 고정합니다. 소식을 받은 쪽이 새로 구독하면
            /// 그 구독자는 <b>다음</b> 발행부터 듣습니다. 그러지 않으면 자기 자신을 다시 구독하는
            /// 핸들러 하나로 순회가 끝나지 않습니다.
            /// </summary>
            /// <param name="message">돌릴 내용입니다.</param>
            public void Publish(T message)
            {
                _depth++;

                try
                {
                    int count = _handlers.Count;

                    for (int i = 0; i < count; i++)
                    {
                        var handler = _handlers[i];

                        if (handler == null)
                        {
                            continue;
                        }

                        // 구독을 거두지 않고 파괴된 유니티 오브젝트입니다.
                        // 그대로 부르면 파괴된 객체의 메서드가 돌아 엉뚱한 자리에서 예외가 납니다.
                        if (handler.Target is UnityEngine.Object target && target == null)
                        {
                            _handlers[i] = null;
                            _needsCompact = true;
                            continue;
                        }

                        handler.Invoke(message);
                    }
                }
                finally
                {
                    _depth--;

                    if (_depth == 0 && _needsCompact)
                    {
                        Compact();
                    }
                }
            }

            /// <inheritdoc />
            public void Clear()
            {
                if (_depth > 0)
                {
                    for (int i = 0; i < _handlers.Count; i++)
                    {
                        _handlers[i] = null;
                    }

                    _needsCompact = true;
                    return;
                }

                _handlers.Clear();
            }

            /// <summary>비워 둔 자리를 실제로 걷어 냅니다.</summary>
            private void Compact()
            {
                for (int i = _handlers.Count - 1; i >= 0; i--)
                {
                    if (_handlers[i] == null)
                    {
                        _handlers.RemoveAt(i);
                    }
                }

                _needsCompact = false;
            }
        }
    }
}
