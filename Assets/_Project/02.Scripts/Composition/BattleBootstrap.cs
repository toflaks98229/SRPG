using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.CameraControl;
using SRPG.Gameplay.Debugging;
using SRPG.Gameplay.Enemies;
using SRPG.Gameplay.Island;
using SRPG.Gameplay.Selection;
using SRPG.Gameplay.Squads;
using SRPG.Gameplay.Units;
using SRPG.Gameplay.Visual;
using SRPG.Systems.Battle;
using SRPG.Systems.Battlefield;
using SRPG.Systems.Grid;
using SRPG.Systems.Time;
using SRPG.UI.HUD;
using UnityEngine;

namespace SRPG.Composition
{
    /// <summary>
    /// 전투 씬의 조립 지점입니다. 이 클래스만이 전 계층을 알고 있습니다.
    ///
    /// 에셋 사용 원칙: <see cref="_setup"/>에 연결된 에셋을 우선 사용하고, 비어 있으면 코드 기본값과
    /// 프리미티브로 대체합니다. 에셋이 아직 없는 상태에서도 프로젝트가 항상 실행 가능해야
    /// 새 팀원이 클론 직후 재생 버튼만으로 게임을 볼 수 있고, PlayMode 테스트도 에셋 없이 돌릴 수 있습니다.
    ///
    /// VContainer를 도입하면 이 클래스는 LifetimeScope로 대체됩니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleBootstrap : MonoBehaviour
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

        private readonly Dictionary<UnitDefinition, Material> _materialCache = new Dictionary<UnitDefinition, Material>();

        private BattleContext _context;
        private TacticalTimeController _timeController;
        private SquadSelectionController _selectionController;
        private EnemySpawner _spawner;
        private Transform _unitRoot;
        private Transform _shadowRoot;

        private UnitDefinition[] _playerRoster;
        private UnitDefinition[] _enemyRoster;

        private readonly List<DeployedSquad> _deployed = new List<DeployedSquad>();
        private readonly BattleConclusion _conclusion = new BattleConclusion();

        private BattleRequest _activeRequest;
        private Battlefield _battlefield;
        private int _enemiesSeen;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>
        /// 섬을 공급하는 함수입니다. 인자는 시드이고, null을 돌려주면 전투가 시작되지 않습니다.
        ///
        /// <b>Start 가 돌기 전에 연결해야 합니다.</b>
        /// 지금은 맵 생성기가 없으므로 비어 있고, 그래서 전투도 시작되지 않습니다.
        /// </summary>
        public System.Func<int, IslandGrid> IslandSource { get; set; }

        /// <summary>
        /// 지형 프로필입니다. 비우면 코드 기본값을 씁니다.
        ///
        /// 월드맵이 붙으면 좌표가 고른 종류에 맞는 프로필이 여기 들어옵니다.
        /// </summary>
        [field: SerializeField]
        [field: Tooltip("전장의 성격입니다. 비우면 코드 기본값을 씁니다.")]
        public BattlefieldProfile TerrainProfile { get; private set; }

        /// <summary>
        /// 이 전투의 주문서입니다. 무엇을 데리고 나가는지를 <b>바깥이</b> 정합니다.
        ///
        /// <b>Start 가 돌기 전에 연결해야 합니다.</b>
        /// 비워 두면 인스펙터 값으로 간단한 주문서를 만들어 씁니다 —
        /// 캠페인이 아직 없는 지금의 프로토타입 경로입니다.
        /// </summary>
        public BattleRequest Request { get; set; }

        /// <summary>
        /// 전투가 끝나면 한 번 발행됩니다.
        ///
        /// 캠페인은 이 보고를 받아 분대의 인원을 줄이고, 무너진 분대를 지웁니다.
        /// 전투는 그 뒤의 일을 알지 못합니다 — 알 필요도 없습니다.
        /// </summary>
        public event System.Action<BattleResult> BattleConcluded;

        /// <summary>이 전투의 결말입니다. 아직 끝나지 않았으면 <c>Undecided</c> 입니다.</summary>
        public BattleOutcome Outcome => _conclusion.Outcome;

        /// <summary>이 전투의 런타임 컨텍스트입니다.</summary>
        public BattleContext Context => _context;

        /// <summary>사용 중인 전투 구성 에셋입니다. 없을 수 있습니다.</summary>
        public BattleSetup Setup => _setup;

        /// <summary>
        /// 이번 판에 만들어진 전장입니다. 바깥이 격자만 꽂아 준 경우에는 비어 있습니다.
        /// </summary>
        public Battlefield Battlefield => _battlefield;

        // ====================================================================================================
        // 4. Unity Lifecycle
        // ====================================================================================================

        private void Start()
        {
            BuildBattle();
        }

        private void Update()
        {
            // 스케일되지 않은 시간으로 갱신해야 슬로우모션 전환이 정상 속도로 진행됩니다.
            _timeController?.Tick(UnityEngine.Time.unscaledDeltaTime);

            TickConclusion();
        }

        private void OnDestroy()
        {
            // 씬을 벗어날 때 타임스케일을 반드시 되돌립니다. 그러지 않으면 다음 씬이 느려진 채 시작합니다.
            _timeController?.Reset();
        }

        // ====================================================================================================
        // 5. Private Methods - Composition Root
        // ====================================================================================================

        /// <summary>
        /// 전투 한 판에 필요한 모든 것을 조립합니다.
        /// </summary>
        private void BuildBattle()
        {
            var tuning = ResolveTuning();

            ResolveRosters();

            // 주문서를 먼저 풉니다. 어디서 싸우는지가 거기 적혀 있습니다.
            _activeRequest = ResolveRequest();

            if (!_activeRequest.IsValid(out string problem))
            {
                Debug.LogError($"[Bootstrap] 주문서가 온전하지 않아 전투를 시작하지 못했습니다: {problem}");
                return;
            }

            var grid = BuildIslandGrid();

            if (grid == null)
            {
                Debug.LogError("[Bootstrap] 전장을 만들지 못해 전투를 시작하지 못했습니다.");
                return;
            }

            _timeController = new TacticalTimeController(tuning.SlowMotionScale, tuning.SlowMotionTransitionSpeed);
            _context = new BattleContext(grid, _timeController, tuning);

            EnsureRuntimeRoot();
            BuildBattlefieldView();

            _unitRoot = CreateChild("Units");

            // 대기 중인 화살이 전투 루트 아래 모이게 합니다. 전투가 끝나면 함께 사라집니다.
            _context.ProjectilePool.SetRoot(CreateChild("Arrows"));

            var battleCamera = EnsureCamera(grid, tuning);
            EnsureLight();

            BuildSelectionController(battleCamera);
            SpawnPlayerSquads(grid, _activeRequest);
            BuildSpawner(_activeRequest.EnemyWaves ?? ResolveWaveDefinition());
            BuildHud();
            BuildAiOverlay();
        }

        /// <summary>
        /// 전투가 벌어질 섬을 얻습니다.
        ///
        /// <b>여기가 맵 생성이 붙는 자리입니다.</b>
        ///
        /// 절차적 지형 생성을 걷어냈으므로 부트스트랩은 섬을 <b>만들지 않고 받습니다</b>.
        /// 새 생성기가 생기면 <see cref="IslandSource"/> 에 연결하면 되고,
        /// 나머지 조립 과정은 <see cref="IslandGrid"/> 하나만 보므로 손댈 것이 없습니다.
        ///
        /// <b>부트스트랩이 맵을 만드는 방법을 아는 것은 좋지 않습니다.</b>
        /// 조립 지점은 조립만 해야 합니다. 공급자를 밖에서 꽂는 편이
        /// 생성 방식이 바뀔 때 이 클래스가 흔들리지 않게 합니다.
        /// </summary>
        private IslandGrid BuildIslandGrid()
        {
            // 바깥이 꽂아 준 것이 우선입니다. 테스트가 고정된 지형을 넣을 때 씁니다.
            if (IslandSource != null)
            {
                return IslandSource(_seedOverride);
            }

            var spec = _activeRequest != null ? _activeRequest.Battlefield : default;

            if (spec.Seed == 0)
            {
                spec.Seed = _seedOverride;
            }

            _battlefield = BattlefieldGenerator.Generate(spec.WithDefaults(), TerrainProfile);

            return _battlefield.Grid;
        }

        /// <summary>
        /// 이 전투의 주문서를 정합니다.
        ///
        /// 바깥이 꽂아 준 것이 있으면 그것을 쓰고, 없으면 인스펙터 값으로 간단히 만듭니다.
        /// 캠페인이 붙으면 후자는 쓰이지 않습니다.
        /// </summary>
        private BattleRequest ResolveRequest()
        {
            if (Request != null)
            {
                return Request;
            }

            return BattleRequest.CreateSimple(_playerRoster, _playerSquadCount, _soldiersPerSquad, _seedOverride);
        }

        // ====================================================================================================
        // 5-1. Private Methods - Conclusion
        // ====================================================================================================

        /// <summary>
        /// 전투가 끝났는지 살피고, 끝났으면 보고서를 발행합니다.
        ///
        /// <b>판정은 여기서 하지 않습니다.</b>
        /// 규칙은 <see cref="BattleConclusion"/>이 들고 있고, 여기서는 관측값만 모아 넘깁니다.
        /// 그래야 "왜 승리 처리가 안 됐는가"를 씬을 재생하지 않고도 확인할 수 있습니다.
        /// </summary>
        private void TickConclusion()
        {
            if (_context == null || _conclusion.IsDecided)
            {
                return;
            }

            int enemiesAlive = _context.EnemyUnits.Count;

            // 처치 수는 "지금까지 본 적의 최대치 - 지금 남은 수"로 셉니다.
            // 파도로 나뉘어 들어오므로 처음에 총원을 알 수 없고,
            // 유닛마다 죽음을 구독하면 조립 지점이 전투 세부를 알게 됩니다.
            _enemiesSeen = Mathf.Max(_enemiesSeen, enemiesAlive + CountEnemyLosses());

            bool decided = _conclusion.Tick(
                UnityEngine.Time.deltaTime,
                CountLivingSquads(),
                enemiesAlive,
                _spawner == null || _spawner.Scheduler == null || _spawner.Scheduler.IsFinished);

            if (decided)
            {
                PublishResult();
            }
        }

        private int CountLivingSquads()
        {
            int alive = 0;

            for (int i = 0; i < _deployed.Count; i++)
            {
                if (_deployed[i].IsAlive)
                {
                    alive++;
                }
            }

            return alive;
        }

        private int CountEnemyLosses()
        {
            return _enemiesSeen > 0 ? _enemiesSeen - _context.EnemyUnits.Count : 0;
        }

        /// <summary>
        /// 분대별 전황을 모아 보고서를 발행합니다.
        /// </summary>
        private void PublishResult()
        {
            var result = new BattleResult
            {
                Outcome = _conclusion.Outcome,
                Duration = _conclusion.Elapsed,
                EnemiesKilled = Mathf.Max(0, _enemiesSeen - _context.EnemyUnits.Count),
            };

            for (int i = 0; i < _deployed.Count; i++)
            {
                var entry = _deployed[i];

                result.Squads.Add(new SquadReport
                {
                    Id = entry.Id,
                    Deployed = entry.Deployed,

                    // 분대가 무너지면 남은 병사도 함께 사라집니다.
                    // 지휘관을 잃은 분대는 흩어지므로 생존자로 세지 않습니다.
                    Survivors = entry.IsAlive ? entry.Squad.AliveCount : 0,
                    Destroyed = !entry.IsAlive,
                });
            }

            Debug.Log($"[Bootstrap] 전투 종료 — {result}");

            BattleConcluded?.Invoke(result);
        }

        /// <summary>
        /// 생성물이 붙을 루트를 확보합니다. 씬에 미리 배치된 루트가 있으면 그것을 씁니다.
        /// </summary>
        private void EnsureRuntimeRoot()
        {
            if (_runtimeRoot == null)
            {
                _runtimeRoot = transform;
            }
        }

        private Transform CreateChild(string childName)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(_runtimeRoot, false);
            return go.transform;
        }

        // ====================================================================================================
        // 6. Private Methods - Asset Resolution
        // ====================================================================================================

        private WaveDefinition ResolveWaveDefinition()
        {
            if (_setup != null && _setup.Waves != null)
            {
                return _setup.Waves;
            }

            return WaveDefinition.CreateDefault();
        }

        /// <summary>
        /// 전투 튜닝을 확정합니다. 절대 null을 돌려주지 않으므로 이후 소비자는 검사할 필요가 없습니다.
        /// </summary>
        private BattleTuning ResolveTuning()
        {
            if (_setup != null && _setup.Tuning != null)
            {
                return _setup.Tuning;
            }

            return BattleTuning.CreateDefault();
        }

        /// <summary>
        /// 병과 목록을 확정합니다. 에셋이 없으면 코드 기본값을 만들어 씁니다.
        /// </summary>
        private void ResolveRosters()
        {
            _playerRoster = _setup != null ? _setup.GetPlayerRosterOrNull() : null;
            _enemyRoster = _setup != null ? _setup.GetEnemyRosterOrNull() : null;

            if (_playerRoster == null)
            {
                _playerRoster = new[]
                {
                    UnitDefinition.CreateDefault(UnitRole.Infantry),
                    UnitDefinition.CreateDefault(UnitRole.Archer),
                    UnitDefinition.CreateDefault(UnitRole.Pike),
                };
            }

            if (_enemyRoster == null)
            {
                _enemyRoster = new[]
                {
                    UnitDefinition.CreateEnemyDefault(UnitRole.Militia),
                    UnitDefinition.CreateEnemyDefault(UnitRole.Infantry),
                    UnitDefinition.CreateEnemyDefault(UnitRole.Archer),
                };
            }
        }

        // ====================================================================================================
        // 7. Private Methods - Sub-systems
        // ====================================================================================================

        /// <summary>
        /// 전장을 화면에 세웁니다.
        ///
        /// 바깥이 격자만 꽂아 준 경우에는 그릴 지형이 없습니다.
        /// 테스트가 그 경로를 쓰므로 오류가 아니라 조용히 넘어갑니다.
        /// </summary>
        private void BuildBattlefieldView()
        {
            if (_battlefield == null)
            {
                return;
            }

            var fieldObject = new GameObject("Battlefield");
            fieldObject.transform.SetParent(_runtimeRoot, false);

            var view = fieldObject.AddComponent<BattlefieldView>();
            view.Build(_battlefield, _setup != null ? _setup.TerrainMaterials : default);
        }

        private void BuildSelectionController(Camera battleCamera)
        {
            var selectionObject = new GameObject("SquadSelection");
            selectionObject.transform.SetParent(_runtimeRoot, false);

            _selectionController = selectionObject.AddComponent<SquadSelectionController>();
            _selectionController.Initialize(
                _context.Grid,
                _context.TimeController,
                battleCamera,
                _setup != null ? _setup.SelectionMarkerPrefab : null,
                _setup != null ? _setup.OrderMarkerPrefab : null);
        }

        private void BuildSpawner(WaveDefinition waves)
        {
            var spawnerObject = new GameObject("EnemySpawner");
            spawnerObject.transform.SetParent(_runtimeRoot, false);

            _spawner = spawnerObject.AddComponent<EnemySpawner>();
            _spawner.Initialize(
                _context,
                waves,
                _enemyRoster,
                CreateUnit,
                _setup != null ? _setup.EnemyShipPrefab : null);
        }

        private void BuildHud()
        {
            if (!_showDebugHud)
            {
                return;
            }

            var hudObject = new GameObject("BattleHud");
            hudObject.transform.SetParent(_runtimeRoot, false);

            var hud = hudObject.AddComponent<BattleDebugHud>();
            hud.Initialize(_context, _context.TimeController, _selectionController, _spawner);
        }

        /// <summary>
        /// AI 판단을 씬 뷰에 그리는 오버레이를 붙입니다.
        /// 기즈모라 게임 화면에는 나오지 않고 빌드에도 남지 않습니다.
        /// </summary>
        private void BuildAiOverlay()
        {
            if (!_showAiDebugOverlay)
            {
                return;
            }

            var overlayObject = new GameObject("AiDebugOverlay");
            overlayObject.transform.SetParent(_runtimeRoot, false);

            overlayObject.AddComponent<AiDebugOverlay>().Initialize(_context.Grid, _context);
        }

        // ====================================================================================================
        // 7-1. Nested Types
        // ====================================================================================================

        /// <summary>
        /// 전장에 세운 분대 하나의 기록입니다.
        ///
        /// 주문서의 식별자와 실제 분대를 이어 둡니다.
        /// 전투가 끝나면 이 짝으로 보고서를 씁니다 — 분대가 파괴되어 오브젝트가
        /// 사라져도 <see cref="Id"/>와 <see cref="Deployed"/>는 남아 있어야 합니다.
        /// </summary>
        private struct DeployedSquad
        {
            public int Id;
            public Squad Squad;
            public int Deployed;

            /// <summary>분대가 아직 싸우고 있는지 여부입니다.</summary>
            public bool IsAlive => Squad != null && !Squad.IsDestroyed;
        }

        // ====================================================================================================
        // 8. Private Methods - Squads
        // ====================================================================================================

        /// <summary>
        /// 주문서가 지시한 분대를 섬 안쪽에 배치합니다.
        /// 분대끼리 겹치지 않도록 간격을 두고, 중심에 가까운 타일부터 채웁니다.
        ///
        /// <b>무엇을 데려가는지는 여기서 정하지 않습니다.</b>
        /// 주문서가 이미 정해 놓은 것을 전장에 세우기만 합니다.
        /// </summary>
        private void SpawnPlayerSquads(IslandGrid grid, BattleRequest request)
        {
            var spawnTiles = SelectSquadSpawnTiles(grid, request.PlayerSquads.Count);

            for (int i = 0; i < spawnTiles.Count && i < request.PlayerSquads.Count; i++)
            {
                var order = request.PlayerSquads[i];

                var squadObject = new GameObject($"Squad_{order.Id}_{order.Definition.DisplayName}");
                squadObject.transform.SetParent(_runtimeRoot, false);

                var squad = squadObject.AddComponent<Squad>();
                squad.Initialize(
                    _context,
                    order.Definition,
                    spawnTiles[i].Coord,
                    order.SoldierCount,
                    order.ResolveName(),
                    CreateUnit,
                    order.ClampedRank());

                _selectionController.RegisterSquad(squad);

                // 주문서의 식별자와 배치 인원을 기억해 둡니다.
                // 전투가 끝나면 이 짝으로 보고서를 만듭니다.
                _deployed.Add(new DeployedSquad
                {
                    Id = order.Id,
                    Squad = squad,
                    Deployed = squad.AliveCount,
                });
            }
        }

        /// <summary>
        /// 분대 초기 배치 타일을 고릅니다. 섬 중심에서 가까운 순으로, 서로 최소 간격을 두고 선택합니다.
        /// </summary>
        private List<Tile> SelectSquadSpawnTiles(IslandGrid grid, int count)
        {
            var result = new List<Tile>(count);

            Vector3 center = new Vector3(
                grid.Origin.x + grid.Width * grid.CellSize * 0.5f,
                0f,
                grid.Origin.z + grid.Depth * grid.CellSize * 0.5f);

            var candidates = new List<Tile>(grid.WalkableTiles.Count);
            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];

                // 가옥 위와 해안은 피해 안쪽 평지에 배치합니다.
                if (tile.Type == TileType.Ground && !tile.IsCoastal)
                {
                    candidates.Add(tile);
                }
            }

            // 안쪽 평지가 없을 만큼 섬이 작으면 통행 가능한 아무 타일이나 씁니다.
            if (candidates.Count == 0)
            {
                candidates.AddRange(grid.WalkableTiles);
            }

            candidates.Sort((a, b) =>
                (a.WorldCenter - center).sqrMagnitude.CompareTo((b.WorldCenter - center).sqrMagnitude));

            const int MinSpacing = 3;

            for (int i = 0; i < candidates.Count && result.Count < count; i++)
            {
                var tile = candidates[i];
                bool tooClose = false;

                for (int r = 0; r < result.Count; r++)
                {
                    if (GridCoord.ChebyshevDistance(tile.Coord, result[r].Coord) < MinSpacing)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    result.Add(tile);
                }
            }

            // 간격 조건 때문에 자리를 못 찾았으면 조건을 풀고 채웁니다.
            for (int i = 0; i < candidates.Count && result.Count < count; i++)
            {
                if (!result.Contains(candidates[i]))
                {
                    result.Add(candidates[i]);
                }
            }

            return result;
        }

        // ====================================================================================================
        // 9. Private Methods - Unit Factory
        // ====================================================================================================

        /// <summary>
        /// 유닛 하나를 만듭니다. 분대와 상륙정이 공유하는 생성 경로입니다.
        ///
        /// 정의에 프리팹이 연결되어 있으면 그것을 인스턴스화하고, 없으면 프리미티브로 임시 몸체를 만듭니다.
        /// 두 경로 모두 마지막에 <see cref="Unit.Initialize"/>를 거치므로 이후 동작은 완전히 같습니다.
        /// </summary>
        private Unit CreateUnit(UnitDefinition definition, Team team, bool isCommander, Vector3 position)
        {
            position.y = _context.Grid.SampleGroundHeight(position);

            Unit unit = definition.Prefab != null
                ? InstantiateFromPrefab(definition, position)
                : CreatePrimitiveUnit(definition, team, isCommander, position);

            if (unit == null)
            {
                return null;
            }

            unit.Initialize(definition, team, _context, isCommander);
            AttachContactShadow(unit, definition);

            return unit;
        }

        /// <summary>
        /// 유닛 발밑에 접지 그림자를 답니다.
        ///
        /// 프리팹 경로와 프리미티브 경로가 함께 지나는 자리라 여기 한 번만 붙이면 됩니다.
        ///
        /// <b>왜 필요한가</b>
        /// 빌보드는 평면이라 지면과 닿는 면이 없습니다.
        /// 방향광 그림자만으로는 유닛이 어디에 서 있는지 읽히지 않고, 카메라를 돌리면 떠 보입니다.
        /// </summary>
        private void AttachContactShadow(Unit unit, UnitDefinition definition)
        {
            if (_shadowRoot == null)
            {
                _shadowRoot = CreateChild("Shadows");
            }

            // 발자국보다 조금 넓어야 그림자로 읽힙니다. 딱 맞으면 유닛에 가려 안 보입니다.
            ContactShadow.Attach(unit.transform, _context.Grid, definition.Radius * 1.4f, _shadowRoot);
        }

        /// <summary>
        /// 프리팹으로 유닛을 만듭니다.
        /// </summary>
        private Unit InstantiateFromPrefab(UnitDefinition definition, Vector3 position)
        {
            var instance = Instantiate(definition.Prefab, position, Quaternion.identity, _unitRoot);
            var unit = instance.GetComponent<Unit>();

            if (unit == null)
            {
                Debug.LogError($"[Bootstrap] 프리팹 '{definition.Prefab.name}' 루트에 Unit 컴포넌트가 없습니다. ({definition.name})");
                Destroy(instance);
                return null;
            }

            return unit;
        }

        /// <summary>
        /// 프리팹 없이 프리미티브로 유닛을 만듭니다.
        /// </summary>
        private Unit CreatePrimitiveUnit(UnitDefinition definition, Team team, bool isCommander, Vector3 position)
        {
            var material = GetFallbackMaterial(definition);
            var visual = PrototypeVisuals.CreateUnitVisual(definition, team, isCommander, material);

            visual.transform.SetParent(_unitRoot, false);
            visual.transform.position = position;

            return visual.AddComponent<Unit>();
        }

        /// <summary>
        /// 정의별 폴백 머티리얼을 캐시합니다. 유닛마다 머티리얼을 새로 만들면 배칭이 깨집니다.
        /// </summary>
        private Material GetFallbackMaterial(UnitDefinition definition)
        {
            if (_materialCache.TryGetValue(definition, out var cached))
            {
                return cached;
            }

            var material = PrototypeVisuals.CreateMaterial(definition.DebugColor);
            _materialCache[definition] = material;
            return material;
        }

        // ====================================================================================================
        // 10. Private Methods - Scene Essentials
        // ====================================================================================================

        /// <summary>
        /// 전투 카메라를 준비합니다. 씬에 배치된 카메라를 우선 쓰고, 없으면 만듭니다.
        /// </summary>
        private Camera EnsureCamera(IslandGrid grid, BattleTuning tuning)
        {
            var camera = _battleCamera != null ? _battleCamera : Camera.main;

            if (camera == null && _createCameraAndLight)
            {
                var cameraObject = new GameObject("BattleCamera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.11f, 0.15f, 0.21f);
            }

            if (camera == null)
            {
                Debug.LogError("[Bootstrap] 씬에 카메라가 없습니다.");
                return null;
            }

            var rig = ResolveCameraRig(camera);

            rig.AttachCamera(camera);
            rig.Configure(grid, tuning);

            Vector3 center = new Vector3(
                grid.Origin.x + grid.Width * grid.CellSize * 0.5f,
                0f,
                grid.Origin.z + grid.Depth * grid.CellSize * 0.5f);

            rig.MoveTo(center);
            rig.FrameArea(Mathf.Max(grid.Width, grid.Depth) * grid.CellSize);

            _battleCamera = camera;
            return camera;
        }

        /// <summary>
        /// 카메라 피벗을 확보합니다.
        ///
        /// <b>예전 구성을 함께 처리합니다.</b>
        /// 리그가 카메라 자신에게 붙어 있던 시절에 구워진 씬이 있습니다.
        /// 그대로 두면 카메라가 자기 자신의 자식이 되려다 깨지므로, 그 리그는 걷어 내고
        /// 부모 피벗을 새로 만듭니다. 씬을 다시 굽지 않아도 실행이 되게 하려는 것입니다.
        /// </summary>
        private BattleCameraRig ResolveCameraRig(Camera camera)
        {
            var onCamera = camera.GetComponent<BattleCameraRig>();
            if (onCamera != null)
            {
                // Destroy는 프레임 끝에 처리되므로, 한 프레임 더 도는 것을 막으려 즉시 꺼 둡니다.
                onCamera.enabled = false;
                Destroy(onCamera);
            }

            var inParent = camera.GetComponentInParent<BattleCameraRig>();
            if (inParent != null && inParent.transform != camera.transform)
            {
                return inParent;
            }

            var pivotObject = new GameObject("CameraPivot");
            pivotObject.transform.SetParent(_runtimeRoot, false);

            return pivotObject.AddComponent<BattleCameraRig>();
        }

        /// <summary>
        /// 방향광이 없으면 하나 만듭니다. 조명이 없으면 지형이 전부 검게 보입니다.
        ///
        /// 환경광은 조명을 만들지 않더라도 맞춥니다.
        /// 씬에 조명만 있고 환경광이 기본값이면 절벽 그늘이 새까맣게 죽습니다.
        /// </summary>
        private void EnsureLight()
        {
            if (!_createCameraAndLight)
            {
                return;
            }

            BattleLighting.ApplyAmbient();

            var existing = FindAnyObjectByType<Light>();
            if (existing != null && existing.type == LightType.Directional)
            {
                return;
            }

            var lightObject = new GameObject("BattleLight");
            lightObject.transform.SetParent(_runtimeRoot, false);

            // 값은 BattleLighting이 정합니다. 여기에 숫자를 적으면 씬 빌더와 조용히 어긋납니다.
            BattleLighting.ApplyDirectional(lightObject.AddComponent<Light>());
        }
    }
}
