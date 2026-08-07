using SRPG.Data;
using UnityEngine;

namespace SRPG.Systems.Spawning
{
    /// <summary>
    /// 웨이브 진행 타이밍만 담당하는 순수 스케줄러입니다.
    /// 실제 적 생성은 Gameplay 계층의 스포너가 수행합니다.
    /// 타이밍 로직을 여기 분리해 두면 적 프리팹 없이도 웨이브 진행을 테스트할 수 있습니다.
    /// </summary>
    public sealed class WaveScheduler
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        private readonly WaveDefinition _definition;

        private float _timer;
        private int _nextWaveIndex;
        private bool _started;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>다음에 발생할 웨이브의 인덱스입니다.</summary>
        public int NextWaveIndex => _nextWaveIndex;

        /// <summary>전체 웨이브 수입니다.</summary>
        public int TotalWaves => _definition != null ? _definition.WaveCount : 0;

        /// <summary>모든 웨이브가 발생했는지 여부입니다. 적이 모두 죽었는지와는 별개입니다.</summary>
        public bool IsFinished => _definition == null || _nextWaveIndex >= _definition.WaveCount;

        /// <summary>다음 웨이브까지 남은 시간(초)입니다. HUD 표시에 사용합니다.</summary>
        public float TimeUntilNextWave => Mathf.Max(0f, _timer);

        // ====================================================================================================
        // 3. Events
        // ====================================================================================================

        /// <summary>웨이브가 시작될 때 발생합니다. 인자는 웨이브 인덱스와 구성입니다.</summary>
        public event System.Action<int, WaveEntry> WaveTriggered;

        /// <summary>마지막 웨이브까지 모두 발생했을 때 한 번 발생합니다.</summary>
        public event System.Action AllWavesTriggered;

        // ====================================================================================================
        // 4. Constructor
        // ====================================================================================================

        public WaveScheduler(WaveDefinition definition)
        {
            _definition = definition;
            _timer = definition != null ? definition.PreparationTime : 0f;
        }

        // ====================================================================================================
        // 5. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 스케줄러를 진행시킵니다.
        /// 슬로우모션의 영향을 받도록 스케일된 시간(<c>Time.deltaTime</c>)으로 호출합니다.
        /// 명령을 내리는 동안 웨이브가 그대로 밀려오면 슬로우모션이 곧 이득이 되어 버리기 때문입니다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (IsFinished)
            {
                return;
            }

            _timer -= deltaTime;
            if (_timer > 0f)
            {
                return;
            }

            var entry = _definition.Waves[_nextWaveIndex];
            WaveTriggered?.Invoke(_nextWaveIndex, entry);

            _nextWaveIndex++;
            _started = true;

            if (IsFinished)
            {
                AllWavesTriggered?.Invoke();
                return;
            }

            // 다음 웨이브의 대기 시간을 적재합니다.
            _timer = _definition.Waves[_nextWaveIndex].Delay;
        }

        /// <summary>
        /// 스케줄러를 초기 상태로 되돌립니다.
        /// </summary>
        public void Reset()
        {
            _nextWaveIndex = 0;
            _started = false;
            _timer = _definition != null ? _definition.PreparationTime : 0f;
        }

        /// <summary>첫 웨이브가 이미 시작되었는지 여부입니다.</summary>
        public bool HasStarted => _started;
    }
}
