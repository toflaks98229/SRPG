using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Campaign;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace SRPG.Composition
{
    /// <summary>
    /// 캠페인 한 회차 동안 유지되는 것들을 등록하는 스코프입니다.
    ///
    /// <b>이 층이 배너로드형 구조의 핵심입니다</b>
    ///
    /// 전역(<see cref="RootLifetimeScope"/>)과 전투(<see cref="BattleBootstrap"/>) 사이에는
    /// 수명이 하나 더 있습니다 — <b>캠페인 한 회차</b>입니다.
    /// 부대 명부는 전투가 끝나도 남아야 하지만, 새 캠페인을 시작하면 버려져야 합니다.
    /// 전역에 두면 새 회차를 시작해도 지난 부대가 따라오고,
    /// 전투에 두면 판이 끝날 때마다 부대가 사라집니다.
    ///
    /// <b>전투 스코프가 이 스코프의 자식이 됩니다</b>
    ///
    /// 그래서 전투는 부모 컨테이너에서 <see cref="BattleOrders"/> 를 꺼내
    /// "무엇을 데리고 나가는가"를 알아냅니다. 정적 필드도, 씬 간 전달 트릭도 필요 없습니다.
    /// 연결은 <see cref="BattleBootstrap.FindParent"/> 가 <see cref="Live"/> 를 보고 맺습니다.
    /// </summary>
    [DefaultExecutionOrder(-950)]
    [DisallowMultipleComponent]
    public sealed class CampaignLifetimeScope : LifetimeScope
    {
        // ====================================================================================================
        // 1. Static
        // ====================================================================================================

        /// <summary>지금 살아 있는 캠페인입니다.</summary>
        private static CampaignLifetimeScope _live;

        /// <summary>
        /// 지금 살아 있는 캠페인입니다. 전투 스코프가 부모를 찾을 때 씁니다.
        /// 캠페인 없이 전투만 여는 경로에서는 null입니다.
        /// </summary>
        public static CampaignLifetimeScope Live => _live;

        /// <summary>재생 세션이 시작될 때마다 정적 상태를 비웁니다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _live = null;
        }

        // ====================================================================================================
        // 2. Inspector
        // ====================================================================================================

        [Header("캠페인")]
        [SerializeField]
        [Tooltip("캠페인이 벌어질 지도입니다. 비우면 지점 셋짜리 기본 지도를 만들어 씁니다.")]
        private WorldMapDefinition _worldMap;

        [SerializeField]
        [Tooltip("시작 부대를 구성할 병과입니다. 비우면 전투 구성 에셋의 아군 명단을 씁니다.")]
        private BattleSetup _setup;

        [SerializeField]
        [Range(1, 6)]
        [Tooltip("캠페인을 시작할 때 거느리는 분대 수입니다.")]
        private int _startingSquadCount = 3;

        [SerializeField]
        [Range(2, 10)]
        [Tooltip("분대당 병사 수입니다. 지휘관은 별도로 1명 추가됩니다.")]
        private int _soldiersPerSquad = 5;

        [Tooltip("전투 성과를 부대의 성장으로 옮기는 규칙입니다. 비워 두면 기본 곡선을 씁니다.")]
        [SerializeField] private CampaignProgression _progression = new CampaignProgression();

        [Header("씬")]
        [SerializeField]
        [Tooltip("전투가 벌어질 씬의 이름입니다.")]
        private string _battleSceneName = "Battle";

        [SerializeField]
        [Tooltip("월드맵 씬의 이름입니다. 전투가 끝나면 여기로 돌아옵니다.")]
        private string _worldMapSceneName = "WorldMap";

        // ====================================================================================================
        // 3. Unity Lifecycle
        // ====================================================================================================

        /// <summary>
        /// 중복 캠페인을 걸러 내고 씬 전환 생존을 겁니다.
        ///
        /// 월드맵 씬으로 돌아올 때마다 그 씬의 캠페인 오브젝트가 새로 깨어납니다.
        /// 살아남아야 하는 것은 <b>처음 것</b>이므로, 나중 것이 스스로 물러납니다.
        /// 그러지 않으면 돌아올 때마다 부대 명부가 초기화됩니다.
        /// </summary>
        protected override void Awake()
        {
            if (_live != null && _live != this)
            {
                Destroy(gameObject);
                return;
            }

            _live = this;

            if (transform.parent != null)
            {
                Debug.LogError(
                    "[CampaignLifetimeScope] 이 오브젝트가 다른 오브젝트의 자식이라 씬 전환에서 살아남지 못합니다. " +
                    "씬의 최상위로 옮기십시오.", this);
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

        /// <summary>
        /// 전역 스코프를 부모로 삼습니다.
        /// </summary>
        /// <returns>살아 있는 전역 스코프입니다. 없으면 null입니다.</returns>
        protected override LifetimeScope FindParent()
        {
            return RootLifetimeScope.Live;
        }

        // ====================================================================================================
        // 4. Configuration
        // ====================================================================================================

        /// <summary>
        /// 캠페인 한 회차가 쓰는 것을 등록합니다.
        /// </summary>
        /// <param name="builder">등록을 받는 빌더입니다.</param>
        protected override void Configure(IContainerBuilder builder)
        {
            var playerRoster = ResolveRoster(true);
            var progression = _progression ?? new CampaignProgression();

            builder.RegisterInstance(progression);
            builder.RegisterInstance(ResolveWorldMap());
            builder.RegisterInstance(BuildRoster(playerRoster, progression));
            builder.RegisterInstance(new BattleOrders());
            builder.Register<CampaignDirector>(Lifetime.Singleton);

            builder.RegisterInstance(new CampaignSceneNames(_battleSceneName, _worldMapSceneName));

            // AsSelf 를 붙이는 이유는 월드맵 화면이 "이 지점으로 간다"를 여기에 요청하기 때문입니다.
            // 씬을 오가는 일은 이 진입점만 하므로, 창구도 하나로 둡니다.
            builder.RegisterEntryPoint<CampaignEntryPoint>().AsSelf();
        }

        // ====================================================================================================
        // 5. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 캠페인을 시작할 부대를 꾸립니다.
        ///
        /// 세이브가 붙으면 이 자리가 "저장된 장부를 불러오기"로 바뀝니다.
        /// 지금은 새 회차만 있으므로 매번 새로 꾸립니다.
        /// </summary>
        /// <param name="roster">시작 부대를 채울 병과입니다.</param>
        /// <param name="progression">전투 성과를 성장으로 옮길 규칙입니다.</param>
        /// <returns>부대가 채워진 장부입니다.</returns>
        private CampaignRoster BuildRoster(UnitDefinition[] roster, CampaignProgression progression)
        {
            var built = new CampaignRoster(progression);

            for (int i = 0; i < _startingSquadCount; i++)
            {
                built.Enlist(roster[i % roster.Length], _soldiersPerSquad);
            }

            return built;
        }

        /// <summary>지도를 정합니다. 연결되지 않았으면 기본 지도를 만듭니다.</summary>
        /// <returns>이번 회차에 쓸 지도입니다.</returns>
        private WorldMapDefinition ResolveWorldMap()
        {
            if (_worldMap != null && _worldMap.NodeCount > 0)
            {
                return _worldMap;
            }

            Debug.LogWarning(
                "[CampaignLifetimeScope] 월드맵 에셋이 없어 지점 셋짜리 기본 지도로 시작합니다.", this);

            return WorldMapDefinition.CreateDefault(ResolveRoster(false));
        }

        /// <summary>병과 명단을 정합니다. 에셋이 없으면 코드 기본값을 만들어 씁니다.</summary>
        /// <param name="player">아군 명단을 원하면 true입니다.</param>
        /// <returns>병과 명단입니다. 절대 비어 있지 않습니다.</returns>
        private UnitDefinition[] ResolveRoster(bool player)
        {
            var fromSetup = _setup == null
                ? null
                : player ? _setup.GetPlayerRosterOrNull() : _setup.GetEnemyRosterOrNull();

            if (fromSetup != null && fromSetup.Length > 0)
            {
                return fromSetup;
            }

            return player
                ? new[]
                {
                    UnitDefinition.CreateDefault(UnitRole.Infantry),
                    UnitDefinition.CreateDefault(UnitRole.Archer),
                    UnitDefinition.CreateDefault(UnitRole.Pike),
                }
                : new[]
                {
                    UnitDefinition.CreateEnemyDefault(UnitRole.Militia),
                    UnitDefinition.CreateEnemyDefault(UnitRole.Infantry),
                };
        }
    }

    /// <summary>
    /// 캠페인이 오갈 씬의 이름입니다.
    ///
    /// 문자열을 컨테이너에 그대로 올리면 어느 것이 해석될지 알 수 없으므로 묶어서 올립니다.
    /// </summary>
    public sealed class CampaignSceneNames
    {
        /// <summary>전투가 벌어질 씬의 이름입니다.</summary>
        public string Battle { get; }

        /// <summary>월드맵 씬의 이름입니다.</summary>
        public string WorldMap { get; }

        /// <param name="battle">전투 씬 이름입니다.</param>
        /// <param name="worldMap">월드맵 씬 이름입니다.</param>
        public CampaignSceneNames(string battle, string worldMap)
        {
            Battle = battle;
            WorldMap = worldMap;
        }
    }
}
