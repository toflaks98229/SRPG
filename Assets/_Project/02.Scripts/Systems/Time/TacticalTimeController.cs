using UnityEngine;

namespace SRPG.Systems.Time
{
    /// <summary>
    /// 명령 입력 중 시간을 늦추는 전술 시간 제어기입니다.
    ///
    /// 완전 정지가 아니라 슬로우모션을 쓰는 이유는 조사 보고서(09.Docs/Research)에 정리한 대로입니다.
    /// 완전 정지는 최적해 탐색을 유도해 사실상 턴제가 되고, 실시간 특유의 압박이 사라집니다.
    ///
    /// 주의: 이 제어기는 <c>Time.timeScale</c>을 건드립니다.
    /// AI 평가와 입력 처리는 반드시 <c>Time.unscaledDeltaTime</c> 기준으로 돌려야
    /// 슬로우모션 중에 AI가 멈춘 것처럼 보이지 않습니다.
    /// </summary>
    public sealed class TacticalTimeController
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        private readonly float _slowMotionScale;
        private readonly float _transitionSpeed;

        private bool _slowMotionRequested;
        private float _currentScale = 1f;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>현재 슬로우모션이 요청된 상태인지 여부입니다.</summary>
        public bool IsSlowMotion => _slowMotionRequested;

        /// <summary>현재 적용 중인 타임스케일입니다.</summary>
        public float CurrentScale => _currentScale;

        // ====================================================================================================
        // 3. Constructor
        // ====================================================================================================

        /// <param name="slowMotionScale">슬로우모션 상태의 타임스케일입니다. 0에 가까울수록 턴제에 가까워집니다.</param>
        /// <param name="transitionSpeed">타임스케일이 목표값으로 수렴하는 속도입니다. 급격한 전환의 이질감을 줄입니다.</param>
        public TacticalTimeController(float slowMotionScale = 0.15f, float transitionSpeed = 8f)
        {
            _slowMotionScale = Mathf.Clamp(slowMotionScale, 0.01f, 1f);
            _transitionSpeed = Mathf.Max(0.1f, transitionSpeed);
        }

        // ====================================================================================================
        // 4. Public Methods
        // ====================================================================================================

        /// <summary>슬로우모션을 요청하거나 해제합니다.</summary>
        /// <param name="enabled">true면 느려지고, false면 정상 속도로 되돌아갑니다.</param>
        public void SetSlowMotion(bool enabled)
        {
            _slowMotionRequested = enabled;
        }

        /// <summary>
        /// 타임스케일을 목표값으로 보간합니다.
        /// 반드시 스케일되지 않은 시간(<c>Time.unscaledDeltaTime</c>)으로 호출해야 합니다.
        /// 스케일된 시간을 쓰면 느려질수록 보간도 느려져 전환이 끝나지 않습니다.
        /// </summary>
        /// <param name="unscaledDeltaTime">스케일되지 않은 지난 시간입니다.</param>
        public void Tick(float unscaledDeltaTime)
        {
            float target = _slowMotionRequested ? _slowMotionScale : 1f;
            _currentScale = Mathf.MoveTowards(_currentScale, target, _transitionSpeed * unscaledDeltaTime);

            UnityEngine.Time.timeScale = _currentScale;

            // 물리 스텝도 함께 줄여야 슬로우모션 중 물리 갱신이 성기게 보이지 않습니다.
            UnityEngine.Time.fixedDeltaTime = 0.02f * _currentScale;
        }

        /// <summary>
        /// 타임스케일을 즉시 정상으로 되돌립니다. 씬 전환이나 종료 시 반드시 호출해야
        /// 다음 씬이 느려진 상태로 시작하지 않습니다.
        /// </summary>
        public void Reset()
        {
            _slowMotionRequested = false;
            _currentScale = 1f;
            UnityEngine.Time.timeScale = 1f;
            UnityEngine.Time.fixedDeltaTime = 0.02f;
        }
    }
}
