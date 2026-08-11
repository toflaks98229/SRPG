using SRPG.Core;
using SRPG.Core.Events;
using UnityEngine;
using VContainer.Unity;

namespace SRPG.Composition
{
    /// <summary>
    /// 전역 시스템이 다 선 뒤에 한 번 도는 진입점입니다.
    ///
    /// <b>왜 스코프가 아니라 여기서 순서를 정하는가</b>
    ///
    /// 스코프의 <c>Configure</c> 는 "무엇이 있는가"를 적는 자리입니다.
    /// 거기에 순서까지 얹으면 등록 줄의 위아래가 곧 초기화 순서가 되어,
    /// 나중에 줄을 정리하다가 조용히 순서가 바뀝니다.
    ///
    /// 순서가 중요한 일은 여기에 문장으로 적습니다. 읽으면 순서가 보이고, 바꾸려면 문장을 옮겨야 합니다.
    /// </summary>
    public sealed class GameBootstrap : IStartable
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>층 상태 서비스입니다.</summary>
        private readonly IGameStateService _gameState;

        /// <summary>소리 창구입니다.</summary>
        private readonly IAudioService _audio;

        /// <summary>수명이 다른 층을 잇는 통로입니다.</summary>
        private readonly IEventBus _eventBus;

        // ====================================================================================================
        // 2. Constructor
        // ====================================================================================================

        /// <param name="gameState">층 상태 서비스입니다.</param>
        /// <param name="audio">소리 창구입니다.</param>
        /// <param name="eventBus">전역 이벤트 버스입니다.</param>
        [UnityEngine.Scripting.Preserve]
        public GameBootstrap(IGameStateService gameState, IAudioService audio, IEventBus eventBus)
        {
            _gameState = gameState;
            _audio = audio;
            _eventBus = eventBus;
        }

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 전역 시스템이 다 선 뒤 첫 프레임에 한 번 돕니다.
        ///
        /// <b>여기서 층을 옮기지 않습니다.</b>
        /// 지금 열려 있는 씬이 무엇인지는 이 클래스가 알 수 없고, 알 필요도 없습니다.
        /// 층은 그 씬의 스코프가 자기가 선 시점에 스스로 밝힙니다 —
        /// 전투 씬이 열렸으면 전투 스코프가 <see cref="GameState.Battle"/> 로 옮깁니다.
        /// </summary>
        public void Start()
        {
            // 재생을 눌렀을 때 루트가 실제로 조립되었는지를 한 줄로 확인할 수 있게 합니다.
            // 이것이 안 보이면 루트 프리팹이 씬에 없거나 중복 루트가 걸러진 것입니다.
            Debug.Log(
                $"[GameBootstrap] 전역 시스템 준비 완료 — 층={_gameState.CurrentState}, " +
                $"오디오={_audio.GetType().Name}, 버스={_eventBus.GetType().Name}");
        }
    }
}
