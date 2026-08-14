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

        /// <summary>슬로우모션일 때의 타임스케일입니다.</summary>
        private readonly float _slowMotionScale;
        /// <summary>타임스케일이 목표값으로 수렴하는 속도입니다.</summary>
        private readonly float _transitionSpeed;

        /// <summary>지금 슬로우모션이 요청된 상태인지 여부입니다.</summary>
        private bool _slowMotionRequested;
        /// <summary>보간 중인 현재 타임스케일입니다.</summary>
        private float _currentScale = 1f;

        /// <summary>히트스톱이 남은 시간입니다. 0보다 크면 배율이 그 값에 붙잡혀 있습니다.</summary>
        private float _hitStopTimer;
        /// <summary>히트스톱 동안 유지할 배율입니다.</summary>
        private float _hitStopScale = 1f;

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
            if (_hitStopTimer > 0f)
            {
                _hitStopTimer -= unscaledDeltaTime;

                // 히트스톱은 <b>보간하지 않습니다.</b> 부딪힌 순간 곧바로 붙잡혀야 충격으로 읽힙니다.
                // 부드럽게 들어가면 그냥 느려지는 것이고, 그것은 이미 슬로우모션이 하는 일입니다.
                _currentScale = _hitStopScale;
            }
            else
            {
                float target = _slowMotionRequested ? _slowMotionScale : 1f;
                _currentScale = Mathf.MoveTowards(_currentScale, target, _transitionSpeed * unscaledDeltaTime);
            }

            UnityEngine.Time.timeScale = _currentScale;

            // 물리 스텝도 함께 줄여야 슬로우모션 중 물리 갱신이 성기게 보이지 않습니다.
            UnityEngine.Time.fixedDeltaTime = 0.02f * _currentScale;
        }

        /// <summary>
        /// 잠깐 시간을 붙잡습니다. 무게 있는 사건이 지나갔음을 몸으로 알리는 장치입니다.
        ///
        /// <b>왜 슬로우모션으로는 안 되는가</b>
        ///
        /// 슬로우모션은 <b>플레이어가 요청하는</b> 상태이고, 명령을 고르는 동안 계속 걸려 있습니다.
        /// 그 위에 사건을 얹으면 구분이 되지 않습니다 — 이미 느린데 조금 더 느려질 뿐입니다.
        /// 히트스톱은 요청과 무관하게 <b>끼어들어</b> 배율을 붙잡았다가 놓습니다.
        ///
        /// <b>놓을 때는 보간으로 돌아갑니다.</b> 붙잡을 때만 즉시이고, 풀릴 때는
        /// <see cref="Tick"/> 의 평소 보간이 이어받습니다. 그래서 "탁 멈췄다 스르르 풀린다"가 됩니다.
        ///
        /// 이미 걸려 있는 히트스톱보다 짧은 요청은 무시합니다. 여럿이 겹칠 때
        /// 나중 것이 앞의 것을 잘라 먹으면, <b>더 큰 사건이 작은 사건에 지워집니다</b> —
        /// 지휘관이 쓰러지는 순간 옆에서 병사가 맞으면 그렇게 됩니다.
        /// </summary>
        /// <param name="seconds">붙잡을 시간입니다. 스케일되지 않은 시간 기준입니다.</param>
        /// <param name="scale">붙잡는 동안의 배율입니다. 0에 가까울수록 완전 정지에 가깝습니다.</param>
        public void HitStop(float seconds, float scale)
        {
            if (seconds <= 0f || seconds <= _hitStopTimer)
            {
                return;
            }

            _hitStopTimer = seconds;
            _hitStopScale = Mathf.Clamp(scale, 0.01f, 1f);
        }

        /// <summary>
        /// 타임스케일을 즉시 정상으로 되돌립니다. 씬 전환이나 종료 시 반드시 호출해야
        /// 다음 씬이 느려진 상태로 시작하지 않습니다.
        /// </summary>
        public void Reset()
        {
            _slowMotionRequested = false;
            _hitStopTimer = 0f;
            _currentScale = 1f;
            UnityEngine.Time.timeScale = 1f;
            UnityEngine.Time.fixedDeltaTime = 0.02f;
        }
    }
}
