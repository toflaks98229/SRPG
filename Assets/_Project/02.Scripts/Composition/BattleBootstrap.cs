using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.CameraControl;
using SRPG.Gameplay.Enemies;
using SRPG.Gameplay.Island;
using SRPG.Gameplay.Selection;
using SRPG.Gameplay.Squads;
using SRPG.Gameplay.Units;
using SRPG.Gameplay.Visual;
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

        private UnitDefinition[] _playerRoster;
        private UnitDefinition[] _enemyRoster;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>이 전투의 런타임 컨텍스트입니다.</summary>
        public BattleContext Context => _context;

        /// <summary>사용 중인 전투 구성 에셋입니다. 없을 수 있습니다.</summary>
        public BattleSetup Setup => _setup;

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
            var settings = ResolveIslandSettings();
            var waves = ResolveWaveDefinition();
            var tuning = ResolveTuning();

            var grid = IslandGenerator.Generate(settings, _seedOverride);
            Debug.Log($"[Bootstrap] 섬 생성 완료 seed={grid.Seed} 통행가능={grid.WalkableTiles.Count} " +
                      $"가옥={grid.HouseTiles.Count} 상륙구역={grid.LandingZones.Count} " +
                      $"({(_setup != null ? "에셋 구성" : "코드 기본값")})");

            _timeController = new TacticalTimeController(tuning.SlowMotionScale, tuning.SlowMotionTransitionSpeed);
            _context = new BattleContext(grid, _timeController, tuning);

            ResolveRosters();
            EnsureRuntimeRoot();
            BuildIslandView(grid);

            _unitRoot = CreateChild("Units");

            // 대기 중인 화살이 전투 루트 아래 모이게 합니다. 전투가 끝나면 함께 사라집니다.
            _context.ProjectilePool.SetRoot(CreateChild("Arrows"));

            var battleCamera = EnsureCamera(grid);
            EnsureLight();

            BuildSelectionController(battleCamera);
            SpawnPlayerSquads(grid);
            BuildSpawner(waves);
            BuildHud();
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

        private IslandSettings ResolveIslandSettings()
        {
            if (_setup != null && _setup.Island != null)
            {
                return _setup.Island;
            }

            return IslandSettings.CreateDefault();
        }

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

        private void BuildIslandView(IslandGrid grid)
        {
            var islandObject = new GameObject("Island");
            islandObject.transform.SetParent(_runtimeRoot, false);

            var view = islandObject.AddComponent<IslandView>();
            view.Build(grid, _setup != null ? _setup.TerrainMaterials : default);
        }

        private void BuildSelectionController(Camera battleCamera)
        {
            var selectionObject = new GameObject("SquadSelection");
            selectionObject.transform.SetParent(_runtimeRoot, false);

            _selectionController = selectionObject.AddComponent<SquadSelectionController>();
            _selectionController.Initialize(
                _context,
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
            hud.Initialize(_context, _selectionController, _spawner);
        }

        // ====================================================================================================
        // 8. Private Methods - Squads
        // ====================================================================================================

        /// <summary>
        /// 플레이어 분대를 섬 안쪽에 배치합니다.
        /// 분대끼리 겹치지 않도록 간격을 두고, 중심에 가까운 타일부터 채웁니다.
        /// </summary>
        private void SpawnPlayerSquads(IslandGrid grid)
        {
            var spawnTiles = SelectSquadSpawnTiles(grid, _playerSquadCount);

            for (int i = 0; i < spawnTiles.Count; i++)
            {
                var definition = _playerRoster[i % _playerRoster.Length];

                var squadObject = new GameObject($"Squad_{i + 1}_{definition.DisplayName}");
                squadObject.transform.SetParent(_runtimeRoot, false);

                var squad = squadObject.AddComponent<Squad>();
                squad.Initialize(
                    _context,
                    definition,
                    spawnTiles[i].Coord,
                    _soldiersPerSquad,
                    $"{i + 1}. {definition.DisplayName}",
                    CreateUnit);

                _selectionController.RegisterSquad(squad);
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
            return unit;
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
        private Camera EnsureCamera(IslandGrid grid)
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

            var rig = camera.GetComponent<BattleCameraRig>();
            if (rig == null)
            {
                rig = camera.gameObject.AddComponent<BattleCameraRig>();
            }

            Vector3 center = new Vector3(
                grid.Origin.x + grid.Width * grid.CellSize * 0.5f,
                0f,
                grid.Origin.z + grid.Depth * grid.CellSize * 0.5f);

            rig.SetFocus(center);
            rig.FrameArea(Mathf.Max(grid.Width, grid.Depth) * grid.CellSize);

            _battleCamera = camera;
            return camera;
        }

        /// <summary>
        /// 방향광이 없으면 하나 만듭니다. 조명이 없으면 지형이 전부 검게 보입니다.
        /// </summary>
        private void EnsureLight()
        {
            if (!_createCameraAndLight)
            {
                return;
            }

            var existing = FindAnyObjectByType<Light>();
            if (existing != null && existing.type == LightType.Directional)
            {
                return;
            }

            var lightObject = new GameObject("BattleLight");
            lightObject.transform.SetParent(_runtimeRoot, false);
            lightObject.transform.rotation = Quaternion.Euler(48f, 138f, 0f);

            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.97f, 0.9f);
            light.shadows = LightShadows.Soft;
        }
    }
}
