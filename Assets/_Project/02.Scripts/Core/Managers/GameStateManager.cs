using System;
using UnityEngine;

namespace SRPG.Core.Managers
{
    /// <summary>
    /// 지금의 층을 들고 있는 매니저입니다.
    ///
    /// <b>왜 기본값이 <see cref="GameState.Booting"/> 인가</b>
    ///
    /// 전역 시스템이 다 서기 전에 누군가 "지금 전투인가"를 물으면, 기본값이 무엇이냐에 따라
    /// 대답이 달라집니다. 기본값을 실제 층 중 하나로 두면 아직 아무것도 준비되지 않은 시점에
    /// 그 층인 것처럼 대답하게 되고, 그 대답을 믿은 쪽이 없는 것을 찾습니다.
    /// 부팅 중임을 명시하면 "아직 아니다"가 대답이 됩니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameStateManager : MonoBehaviour, IGameStateService
    {
        // ====================================================================================================
        // 1. Inspector
        // ====================================================================================================

        [SerializeField]
        [Tooltip("지금의 층을 인스펙터에서 보기 위한 표시용 값입니다. 여기를 바꿔도 층은 옮겨지지 않습니다.")]
        private GameState _debugCurrentState = GameState.Booting;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <inheritdoc />
        public GameState CurrentState { get; private set; } = GameState.Booting;

        /// <inheritdoc />
        public event Action<GameState> StateChanged;

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <inheritdoc />
        public void ChangeState(GameState next)
        {
            if (CurrentState == next)
            {
                return;
            }

            var previous = CurrentState;

            CurrentState = next;
            _debugCurrentState = next;

            Debug.Log($"[GameState] {previous} → {next}", this);

            StateChanged?.Invoke(next);
        }
    }
}
