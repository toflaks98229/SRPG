using SRPG.Common;
using SRPG.Core;
using SRPG.Core.Events;
using SRPG.Core.Managers;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace SRPG.Composition
{
    /// <summary>
    /// 앱이 살아 있는 동안 유지되는 것들을 등록하는 최상위 스코프입니다.
    ///
    /// <b>여기에 무엇을 두는가</b>
    ///
    /// 판이 바뀌어도 남아야 하는 것만 둡니다 — 소리, 설정, 입력, 저장, 그리고 층 상태.
    /// 캠페인 한 회차짜리는 <c>CampaignScope</c> 로, 전투 한 판짜리는 <c>BattleScope</c> 로 갑니다.
    ///
    /// 판단 기준은 "전투가 끝나도 살아 있어야 하는가"입니다.
    /// 이것을 어기면 전투가 끝난 뒤 죽은 씬 오브젝트를 가리키는 참조가 다음 판으로 따라갑니다.
    ///
    /// <b>이 스코프는 씬 전환에서 살아남습니다</b>
    ///
    /// 그러라고 있는 것이므로 조건 없이 <c>DontDestroyOnLoad</c> 를 겁니다.
    /// 대신 <b>이 프리팹에 씬 전용 설치자를 절대 넣지 마십시오.</b>
    /// 넣는 순간 살아남는 것은 전역 상태가 아니라 죽은 씬 참조가 됩니다.
    /// 씬 전용 스코프는 각 씬에 따로 놓고, 이 스코프를 부모로 삼게 하십시오.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class RootLifetimeScope : LifetimeScope
    {
        // ====================================================================================================
        // 1. Static
        // ====================================================================================================

        /// <summary>지금 살아 있는 루트입니다. 두 번째 것을 걸러 내는 기준입니다.</summary>
        private static RootLifetimeScope _live;

        /// <summary>
        /// 지금 살아 있는 루트입니다. 씬 전용 스코프가 부모를 찾을 때 씁니다.
        /// 아직 서지 않았으면 null입니다.
        /// </summary>
        public static RootLifetimeScope Live => _live;

        /// <summary>
        /// 재생 세션이 시작될 때마다 정적 상태를 비웁니다.
        ///
        /// 에디터에서 도메인 리로드를 꺼 두면 정적 필드가 지난 세션의 값을 그대로 들고 있습니다.
        /// 그러면 이미 파괴된 루트를 "살아 있다"고 판단해 이번 세션의 루트가 스스로를 지웁니다.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _live = null;
        }

        // ====================================================================================================
        // 2. Inspector
        // ====================================================================================================

        [Header("전역 매니저")]
        [SerializeField]
        [Tooltip("지금 게임이 어느 층에 있는지를 들고 있는 매니저입니다.")]
        private GameStateManager _gameState;

        [SerializeField]
        [Tooltip("소리를 내는 유일한 창구입니다.")]
        private AudioManager _audio;

        // ====================================================================================================
        // 3. Unity Lifecycle
        // ====================================================================================================

        /// <summary>
        /// 컨테이너를 조립하기 전에 중복 루트를 걸러 내고 씬 전환 생존을 겁니다.
        /// </summary>
        protected override void Awake()
        {
            if (_live != null && _live != this)
            {
                // 씬마다 루트 프리팹을 놓아 두면 여기로 옵니다. 첫 씬의 것만 남기는 것이 맞습니다.
                // 조립하기 전에 지워야 두 개의 EventBus가 잠깐이라도 공존하지 않습니다.
                Destroy(gameObject);
                return;
            }

            _live = this;

            if (transform.parent != null)
            {
                Debug.LogError(
                    "[RootLifetimeScope] 이 오브젝트가 다른 오브젝트의 자식이라 씬 전환에서 살아남지 못합니다. " +
                    "씬의 최상위로 옮기십시오. (DontDestroyOnLoad는 루트 오브젝트에만 걸립니다)", this);
            }
            else
            {
                DontDestroyOnLoad(gameObject);
            }

            base.Awake();
        }

        /// <inheritdoc />
        protected override void OnDestroy()
        {
            if (_live == this)
            {
                _live = null;
            }

            base.OnDestroy();
        }

        // ====================================================================================================
        // 4. Configuration
        // ====================================================================================================

        /// <summary>
        /// 전역 시스템을 컨테이너에 등록합니다.
        /// </summary>
        /// <param name="builder">등록을 받는 빌더입니다.</param>
        protected override void Configure(IContainerBuilder builder)
        {
            // 비어 있는 참조를 여기서 전부 드러냅니다. 하나 발견할 때마다 멈추지 않는 이유는
            // 그러면 고치고 실행하기를 빠진 개수만큼 반복하게 되기 때문입니다.
            DIValidation.RequireRef(this, _gameState, nameof(_gameState));
            DIValidation.RequireRef(this, _audio, nameof(_audio));

            // 수명이 다른 층을 잇는 통로입니다. 전투가 끝나도 남아야 하므로 여기에 둡니다.
            builder.Register<EventBus>(Lifetime.Singleton).As<IEventBus>();

            builder.RegisterComponent(_gameState).As<IGameStateService>().AsSelf();
            builder.RegisterComponent(_audio).As<IAudioService>().AsSelf();

            // 초기화 '순서'는 스코프가 아니라 이 진입점이 정합니다.
            // 등록 순서에 기대면 인스펙터를 한 번 정리하는 것만으로 조용히 어긋납니다.
            builder.RegisterEntryPoint<GameBootstrap>();
        }
    }
}
