using System;
using SRPG.Common;
using SRPG.Core;
using SRPG.Core.Events;
using SRPG.Core.Managers;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Campaign;
using SRPG.Systems.Battlefield;
using SRPG.Systems.Grid;
using SRPG.Systems.Time;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace SRPG.Composition
{
    /// <summary>
    /// 전투 한 판의 조립 지점입니다. 이 스코프가 사는 동안만 전투가 존재합니다.
    ///
    /// <b>여기가 하는 일은 등록뿐입니다</b>
    ///
    /// 예전에 이 클래스는 등록과 조립과 생성과 판정을 전부 겸했습니다.
    /// 지금은 셋으로 나뉘어 있습니다.
    ///   · <b>여기</b> — 무엇이 있는지 적습니다. 씬을 건드리지 않습니다.
    ///   · <see cref="BattleEntryPoint"/> — 그것을 어떤 순서로 화면에 세울지 정합니다.
    ///   · <see cref="Gameplay.Units.UnitFactory"/> — 병사를 찍어 냅니다. 판이 도는 내내 불립니다.
    ///
    /// <b>수명이 여기서 갈립니다</b>
    ///
    /// 이 스코프에 등록된 것은 전투가 끝나면 전부 사라져야 합니다 —
    /// 유닛 명부도, 공간 색인도, 위협 지도도 이번 판의 씬 오브젝트를 가리키고 있습니다.
    /// 판을 넘겨 살아야 하는 것은 여기가 아니라 <see cref="RootLifetimeScope"/> 에 둡니다.
    ///
    /// 에셋 사용 원칙은 그대로입니다 — <see cref="_setup"/> 이 연결돼 있으면 그것을,
    /// 비어 있으면 코드 기본값과 프리미티브를 씁니다. 클론 직후 재생 버튼만으로 게임이 보여야 합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleBootstrap : LifetimeScope
    {
        // ====================================================================================================
        // 1. Inspector
        // ====================================================================================================

        [Header("설정 에셋")]
        [SerializeField]
        [Tooltip("전투 구성 에셋입니다. 비워 두면 전부 코드 기본값과 프리미티브로 실행됩니다.")]
        private BattleSetup _setup;

        [Header("생성")]
        [SerializeField]
        [Tooltip("0이 아니면 이 시드로 섬을 생성합니다. 같은 값은 항상 같은 섬을 만듭니다.")]
        private int _seedOverride;

        [SerializeField]
        [Range(1, 6)]
        [Tooltip("플레이어가 지휘할 분대 수입니다. Bad North는 4개 내외를 유지합니다.")]
        private int _playerSquadCount = 3;

        [SerializeField]
        [Range(2, 10)]
        [Tooltip("분대당 병사 수입니다. 지휘관은 별도로 1명 추가됩니다.")]
        private int _soldiersPerSquad = 5;

        [Header("씬 구성")]
        [SerializeField]
        [Tooltip("생성된 오브젝트가 붙을 부모입니다. 비우면 이 오브젝트 아래에 만듭니다.")]
        private Transform _runtimeRoot;

        [SerializeField]
        [Tooltip("씬에 배치된 카메라입니다. 비우면 Camera.main을 찾고, 그것도 없으면 만듭니다.")]
        private Camera _battleCamera;

        [Header("옵션")]
        [SerializeField]
        [Tooltip("디버그 HUD를 표시합니다.")]
        private bool _showDebugHud = true;

        [SerializeField]
        [Tooltip("AI 판단(위협 영향력·목표)을 씬 뷰에 기즈모로 그립니다. 게임 화면에는 나오지 않습니다.")]
        private bool _showAiDebugOverlay = true;

        [SerializeField]
        [Tooltip("씬에 카메라와 조명이 없으면 자동으로 만듭니다.")]
        private bool _createCameraAndLight = true;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        /// <summary>결말 소식을 듣고 있는 통로입니다. 사라질 때 구독을 거두려고 들고 있습니다.</summary>
        private IEventBus _eventBus;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>
        /// 섬을 공급하는 함수입니다. 인자는 시드이고, null을 돌려주면 전투가 시작되지 않습니다.
        ///
        /// <b>컨테이너가 조립되기 전에 연결해야 합니다.</b>
        /// 자동 검사가 고정된 지형을 꽂을 때 쓰는 경로입니다.
        /// 비어 있으면 <see cref="BattlefieldGenerator"/> 가 주문서에 적힌 대로 전장을 만듭니다.
        /// </summary>
        public Func<int, IslandGrid> IslandSource { get; set; }

        /// <summary>
        /// 지형 프로필입니다. 비우면 구성 에셋의 것을, 그것도 없으면 코드 기본값을 씁니다.
        ///
        /// 월드맵이 붙으면 좌표가 고른 종류에 맞는 프로필이 여기 들어옵니다.
        /// </summary>
        [field: SerializeField]
        [field: Tooltip("전장의 성격입니다. 비우면 구성 에셋의 프로필을, 그것도 없으면 코드 기본값을 씁니다.")]
        public BattlefieldProfile TerrainProfile { get; private set; }

        /// <summary>
        /// 이 전투의 주문서입니다. 무엇을 데리고 나가는지를 <b>바깥이</b> 정합니다.
        ///
        /// <b>컨테이너가 조립되기 전에 연결해야 합니다.</b>
        /// 비워 두면 인스펙터 값으로 간단한 주문서를 만들어 씁니다 —
        /// 캠페인이 아직 없는 지금의 프로토타입 경로입니다.
        /// </summary>
        public BattleRequest Request { get; set; }

        /// <summary>
        /// 전투가 끝나면 한 번 발행됩니다.
        ///
        /// <b>이것은 편의 창구입니다.</b> 실제 소식은 <see cref="BattleConcludedEvent"/> 로
        /// 버스에 오르고, 여기서는 그것을 다시 흘려 줄 뿐입니다.
        /// 캠페인처럼 전투보다 오래 사는 쪽은 이 이벤트가 아니라 <b>버스를 구독해야 합니다</b> —
        /// 이 스코프는 판이 끝나면 사라지고, 그때 이 이벤트도 함께 사라집니다.
        /// </summary>
        public event Action<BattleResult> BattleConcluded;

        /// <summary>이 전투의 결말입니다. 아직 끝나지 않았으면 <c>Undecided</c> 입니다.</summary>
        public BattleOutcome Outcome =>
            TryResolve<BattleReporter>(out var reporter) ? reporter.Outcome : BattleOutcome.Undecided;

        /// <summary>이 전투의 런타임 컨텍스트입니다. 조립이 도중에 멈췄으면 null입니다.</summary>
        public BattleContext Context => TryResolve<BattleContext>(out var context) ? context : null;

        /// <summary>사용 중인 전투 구성 에셋입니다. 없을 수 있습니다.</summary>
        public BattleSetup Setup => _setup;

        /// <summary>
        /// 이번 판에 만들어진 전장입니다. 바깥이 격자만 꽂아 준 경우에는 비어 있습니다.
        /// </summary>
        public Battlefield Battlefield => TryResolve<BattlePlan>(out var plan) ? plan.Battlefield : null;

        // ====================================================================================================
        // 4. Unity Lifecycle
        // ====================================================================================================

        /// <summary>
        /// 컨테이너를 조립한 뒤 결말 소식을 듣기 시작합니다.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();

            if (TryResolve(out _eventBus))
            {
                _eventBus.Subscribe<BattleConcludedEvent>(OnBattleConcluded);
            }
        }

        /// <inheritdoc />
        protected override void OnDestroy()
        {
            // 버스는 이 스코프보다 오래 삽니다. 거두지 않으면 죽은 구독이 다음 판까지 남습니다.
            _eventBus?.Unsubscribe<BattleConcludedEvent>(OnBattleConcluded);
            _eventBus = null;

            base.OnDestroy();
        }

        /// <summary>
        /// 살아 있는 위층을 부모로 삼습니다. 캠페인이 있으면 캠페인, 없으면 전역입니다.
        ///
        /// <b>왜 캠페인이 먼저인가</b>
        ///
        /// 캠페인이 도는 중이라면 이 전투는 그 캠페인의 한 판입니다.
        /// 캠페인을 부모로 삼아야 부대 명부와 주문서를 부모 컨테이너에서 꺼낼 수 있습니다.
        /// 전역을 부모로 삼으면 캠페인이 등록한 것에 손이 닿지 않습니다.
        ///
        /// <b>왜 씬을 훑지 않는가</b>
        ///
        /// 위층 스코프는 씬 전환에서 살아남는 별도 오브젝트라 부모·자식 관계로는 찾을 수 없고,
        /// 타입으로 씬을 훑는 방식은 씬을 두 개 열어 두었을 때 엉뚱한 것을 잡습니다.
        /// 살아 있는 쪽이 스스로를 밝히고 있으니 그것을 그대로 씁니다.
        ///
        /// <b>위층이 아예 없어도 됩니다.</b> 전투 씬만 열어 보는 편집 중의 실행과 자동 검사가 그렇습니다.
        /// 그때는 홀로 조립되고, <see cref="Configure"/> 가 빠진 전역 서비스를 채웁니다.
        /// </summary>
        /// <returns>살아 있는 위층 스코프입니다. 없으면 null입니다.</returns>
        protected override LifetimeScope FindParent()
        {
            if (CampaignLifetimeScope.Live != null)
            {
                return CampaignLifetimeScope.Live;
            }

            return RootLifetimeScope.Live;
        }

        // ====================================================================================================
        // 5. Configuration
        // ====================================================================================================

        /// <summary>
        /// 이번 판에 필요한 것을 컨테이너에 등록합니다.
        /// </summary>
        /// <param name="builder">등록을 받는 빌더입니다.</param>
        protected override void Configure(IContainerBuilder builder)
        {
            // 루트 없이 전투만 여는 경로에서는 전역 서비스가 없습니다.
            // 해석 실패는 컨테이너 조립 전체를 무너뜨리므로, 조용한 대체물로 자리를 채웁니다.
            if (Parent == null)
            {
                builder.Register<EventBus>(Lifetime.Singleton).As<IEventBus>();
                builder.Register<SilentAudioService>(Lifetime.Singleton).As<IAudioService>();
            }

            var tuning = _setup != null && _setup.Tuning != null ? _setup.Tuning : BattleTuning.CreateDefault();
            builder.RegisterInstance(tuning);

            var plan = BuildPlan();
            builder.RegisterInstance(plan);

            if (!plan.IsReady)
            {
                // 진입점을 등록하지 않으므로 씬에는 아무것도 세워지지 않습니다.
                // 컨테이너 자체는 조립되므로, 무엇이 등록되었는지는 그대로 들여다볼 수 있습니다.
                Debug.LogError($"[Battle] 전투를 시작하지 못했습니다: {plan.Problem}", this);
                return;
            }

            var time = new TacticalTimeController(tuning.Time.SlowMotionScale, tuning.Time.SlowMotionTransitionSpeed);
            builder.RegisterInstance(time);

            var audio = BuildAudio();
            builder.RegisterInstance(audio).As<IBattleAudio>();

            // 컨텍스트는 역할별 인터페이스로 함께 등록합니다.
            // 소비자는 자기가 실제로 쓰는 역할만 받고, 통째로 드는 것은 진입점뿐입니다.
            // (ISpatialQuery · IUnitContext · ISquadContext<T> · IDeploymentContext 등)
            builder.RegisterInstance(new BattleContext(plan.Grid, time, tuning, audio))
                   .AsImplementedInterfaces()
                   .AsSelf();

            builder.RegisterInstance(new BattleReporter());

            builder.RegisterInstance(new BattleSceneSettings(
                _setup,
                _runtimeRoot != null ? _runtimeRoot : transform,
                _battleCamera,
                _showDebugHud,
                _showAiDebugOverlay,
                _createCameraAndLight));

            builder.RegisterEntryPoint<BattleEntryPoint>();
        }

        // ====================================================================================================
        // 6. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 씬을 건드리기 전에 확정할 수 있는 것을 전부 확정합니다.
        ///
        /// 주문서를 먼저 풉니다 — 어디서 싸우는지가 거기 적혀 있습니다.
        /// </summary>
        /// <returns>확정된 계획입니다. 열 수 없으면 이유가 담깁니다.</returns>
        private BattlePlan BuildPlan()
        {
            var request = ResolveRequest();

            if (!request.IsValid(out string problem))
            {
                return new BattlePlan($"주문서가 온전하지 않습니다 — {problem}");
            }

            // 바깥이 꽂아 준 것이 우선입니다. 자동 검사가 고정된 지형을 넣을 때 씁니다.
            if (IslandSource != null)
            {
                var supplied = IslandSource(request.Seed);

                return supplied != null
                    ? new BattlePlan(request, null, supplied)
                    : new BattlePlan("바깥이 꽂아 준 섬 공급자가 빈 지형을 돌려주었습니다.");
            }

            var spec = request.Battlefield;

            if (spec.Seed == 0)
            {
                spec.Seed = request.Seed;
            }

            var battlefield = BattlefieldGenerator.Generate(spec.WithDefaults(), ResolveTerrainProfile());

            return battlefield != null && battlefield.Grid != null
                ? new BattlePlan(request, battlefield, battlefield.Grid)
                : new BattlePlan("전장을 만들지 못했습니다.");
        }

        /// <summary>
        /// 이 전투의 주문서를 정합니다. 셋 중 먼저 있는 것을 씁니다.
        ///
        ///   1. <see cref="Request"/> — 코드가 직접 꽂은 것입니다. 자동 검사가 씁니다.
        ///   2. <b>캠페인이 올려 둔 것</b> — 월드맵에 기록된 부대가 여기로 들어옵니다.
        ///   3. 인스펙터 값 — 캠페인 없이 전투 씬만 열어 볼 때의 경로입니다.
        ///
        /// <b>2번이 이 게임의 정상 경로입니다.</b>
        /// 캠페인이 살아 있으면 전투는 부대를 만들어 내지 않고 장부에 적힌 것을 받습니다.
        /// 3번은 편집 중에 전장만 보려고 남겨 둔 것이고, 캠페인이 도는 중에는 쓰이지 않습니다.
        /// </summary>
        /// <returns>이번 판에 쓸 주문서입니다.</returns>
        private BattleRequest ResolveRequest()
        {
            var supplied = Request ?? TakeCampaignOrders();

            if (supplied != null)
            {
                // 시드가 비어 있으면 인스펙터 값으로 채웁니다.
                // 지형과 배치가 <b>같은</b> 시드를 써야 두 실행을 비교할 수 있습니다.
                if (supplied.Seed == 0)
                {
                    supplied.Seed = _seedOverride;
                }

                return supplied;
            }

            return BattleRequest.CreateSimple(
                ResolvePlayerRoster(), ResolveEnemyRoster(), _playerSquadCount, _soldiersPerSquad, _seedOverride);
        }

        /// <summary>
        /// 캠페인이 올려 둔 주문서를 가져옵니다.
        ///
        /// <b>부모 컨테이너에서 꺼냅니다.</b>
        /// 부모는 이 스코프가 조립되기 전에 이미 조립을 마쳤으므로 순서가 보장됩니다.
        /// 이것이 예전 <c>ForcedSeed</c> 같은 정적 필드를 대신하는 자리입니다.
        /// </summary>
        /// <returns>캠페인이 올려 둔 주문서입니다. 캠페인이 없거나 비어 있으면 null입니다.</returns>
        private BattleRequest TakeCampaignOrders()
        {
            if (Parent == null || Parent.Container == null)
            {
                return null;
            }

            return Parent.Container.TryResolve<BattleOrders>(out var orders) ? orders.Take() : null;
        }

        /// <summary>
        /// 이번 판의 소리 창구를 만듭니다.
        ///
        /// <b>왜 컨테이너에 맡기지 않는가</b>
        ///
        /// 소리를 실제로 내는 <see cref="IAudioService"/> 는 <b>위층</b>이 등록한 것입니다.
        /// 전투 컨테이너는 아직 조립되는 중이라 여기서 스스로에게 물을 수 없고,
        /// 물을 수 있는 것은 이미 조립을 마친 부모뿐입니다.
        /// 주문서를 꺼내는 <see cref="TakeCampaignOrders"/> 와 같은 자리, 같은 이유입니다.
        ///
        /// <b>위층이 없어도 됩니다.</b> 전투 씬만 열어 보는 편집 중의 실행과 자동 검사가 그렇습니다.
        /// 그때는 창구가 비고, <see cref="BattleAudio"/> 가 아무것도 하지 않습니다 —
        /// 소리가 안 나는 것은 그 경로에서 결함이 아닙니다.
        /// </summary>
        /// <returns>이번 판의 소리 창구입니다. 절대 null이 아닙니다.</returns>
        private IBattleAudio BuildAudio()
        {
            IAudioService service = null;

            if (Parent != null && Parent.Container != null)
            {
                Parent.Container.TryResolve(out service);
            }

            // 뱅크가 없거나 어느 칸이 비어 있으면 합성음이 그 자리를 메웁니다.
            // 그 판단은 BattleAudio 안에 있습니다 — 여기서 또 하면 규칙이 두 곳으로 갈라집니다.
            return new BattleAudio(service, _setup != null ? _setup.AudioBank : null);
        }

        /// <summary>
        /// 전장의 성격을 정합니다.
        ///
        /// 인스펙터에 직접 꽂은 것이 우선입니다 — 한 씬에서 지형만 바꿔 보는 경로입니다.
        /// 비어 있으면 구성 에셋의 것을 쓰고, 그것도 없으면 생성기가 코드 기본값을 만듭니다.
        /// </summary>
        /// <returns>쓸 지형 프로필입니다. 없으면 null입니다.</returns>
        private BattlefieldProfile ResolveTerrainProfile()
        {
            if (TerrainProfile != null)
            {
                return TerrainProfile;
            }

            return _setup != null ? _setup.TerrainProfile : null;
        }

        /// <summary>아군 병과 목록을 정합니다. 에셋이 없으면 코드 기본값을 만들어 씁니다.</summary>
        /// <returns>아군 병과 후보입니다.</returns>
        private UnitDefinition[] ResolvePlayerRoster()
        {
            var roster = _setup != null ? _setup.GetPlayerRosterOrNull() : null;

            return roster ?? new[]
            {
                UnitDefinition.CreateDefault(UnitRole.Infantry),
                UnitDefinition.CreateDefault(UnitRole.Archer),
                UnitDefinition.CreateDefault(UnitRole.Pike),
            };
        }

        /// <summary>적 병과 목록을 정합니다. 에셋이 없으면 코드 기본값을 만들어 씁니다.</summary>
        /// <returns>적 병과 후보입니다.</returns>
        private UnitDefinition[] ResolveEnemyRoster()
        {
            var roster = _setup != null ? _setup.GetEnemyRosterOrNull() : null;

            return roster ?? new[]
            {
                UnitDefinition.CreateEnemyDefault(UnitRole.Militia),
                UnitDefinition.CreateEnemyDefault(UnitRole.Infantry),
                UnitDefinition.CreateEnemyDefault(UnitRole.Archer),
            };
        }

        /// <summary>
        /// 컨테이너에서 하나 꺼냅니다. 조립이 실패했거나 등록되지 않았으면 실패합니다.
        /// </summary>
        /// <typeparam name="T">꺼낼 타입입니다.</typeparam>
        /// <param name="resolved">꺼낸 것입니다. 실패하면 기본값입니다.</param>
        /// <returns>꺼냈으면 true입니다.</returns>
        private bool TryResolve<T>(out T resolved)
        {
            if (Container != null)
            {
                return Container.TryResolve(out resolved);
            }

            resolved = default;
            return false;
        }

        /// <summary>버스에 오른 결말을 편의 창구로 흘려 줍니다.</summary>
        /// <param name="message">결말 소식입니다.</param>
        private void OnBattleConcluded(BattleConcludedEvent message)
        {
            BattleConcluded?.Invoke(message.Result);
        }
    }
}
