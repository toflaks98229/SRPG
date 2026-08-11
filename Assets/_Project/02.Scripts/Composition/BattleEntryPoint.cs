using System;
using SRPG.Common;
using SRPG.Core.Events;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.CameraControl;
using SRPG.Gameplay.Debugging;
using SRPG.Gameplay.Deployment;
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
using VContainer.Unity;

namespace SRPG.Composition
{
    /// <summary>
    /// 확정된 계획을 실제 전장으로 세우고, 판이 도는 동안 결말을 지켜봅니다.
    ///
    /// <b>조립 지점과 무엇이 다른가</b>
    ///
    /// 스코프는 "무엇이 있는가"를 적고, 여기는 "그것을 어떤 순서로 세우는가"를 합니다.
    /// 세우는 순서에는 실제 제약이 있습니다 — 전장이 있어야 풀밭이 생기고,
    /// 풀밭이 있어야 병사가 그것을 눕힐 수 있습니다. 그 순서가 등록 줄의 위아래에 숨어 있으면
    /// 나중에 줄을 정리하다가 조용히 깨집니다. 그래서 여기에 문장으로 적습니다.
    /// </summary>
    public sealed class BattleEntryPoint : IStartable, ITickable, IDisposable
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>씬을 건드리기 전에 확정된 계획입니다.</summary>
        private readonly BattlePlan _plan;

        /// <summary>이번 판의 런타임 상태입니다. 진입점만이 이것을 통째로 듭니다.</summary>
        private readonly BattleContext _context;

        /// <summary>전황을 관측해 결말을 판정하고 보고서를 씁니다.</summary>
        private readonly BattleReporter _reporter;

        /// <summary>슬로우모션을 거는 전술 시간 제어기입니다.</summary>
        private readonly TacticalTimeController _time;

        /// <summary>씬 쪽 설정입니다.</summary>
        private readonly BattleSceneSettings _settings;

        /// <summary>결말을 바깥에 알리는 통로입니다.</summary>
        private readonly IEventBus _eventBus;

        /// <summary>플레이어의 유일한 조작 창구입니다.</summary>
        private SquadSelectionController _selection;

        /// <summary>양측을 세우고 지원군을 올려보내는 전개기입니다.</summary>
        private SquadDeployer _deployer;

        /// <summary>전장을 화면에 세운 뷰입니다. 격자만 받은 경로에서는 비어 있습니다.</summary>
        private BattlefieldView _view;

        /// <summary>병사를 찍어 내는 도구입니다. 전장을 세운 뒤에 만들어집니다.</summary>
        private UnitFactory _units;

        // ====================================================================================================
        // 2. Constructor
        // ====================================================================================================

        /// <param name="plan">확정된 계획입니다.</param>
        /// <param name="context">이번 판의 런타임 상태입니다.</param>
        /// <param name="reporter">결말을 판정하는 관측자입니다.</param>
        /// <param name="time">전술 시간 제어기입니다.</param>
        /// <param name="settings">씬 쪽 설정입니다.</param>
        /// <param name="eventBus">결말을 알릴 통로입니다.</param>
        [UnityEngine.Scripting.Preserve]
        public BattleEntryPoint(
            BattlePlan plan,
            BattleContext context,
            BattleReporter reporter,
            TacticalTimeController time,
            BattleSceneSettings settings,
            IEventBus eventBus)
        {
            _plan = plan;
            _context = context;
            _reporter = reporter;
            _time = time;
            _settings = settings;
            _eventBus = eventBus;
        }

        // ====================================================================================================
        // 3. Lifecycle
        // ====================================================================================================

        /// <summary>
        /// 계획을 전장으로 세웁니다.
        ///
        /// 순서에 이유가 있는 자리마다 그 이유를 적어 두었습니다.
        /// </summary>
        public void Start()
        {
            // 전장을 먼저 세웁니다. 여기서 풀밭이 생기고, 병사가 그것을 눕힐 주체로 등록됩니다.
            BuildBattlefieldView();

            var unitRoot = CreateChild("Units");
            var shadowRoot = CreateChild("Shadows");

            // 대기 중인 화살이 전투 루트 아래 모이게 합니다. 전투가 끝나면 함께 사라집니다.
            _context.ProjectilePool.SetRoot(CreateChild("Arrows"));

            _units = new UnitFactory(_context, unitRoot, shadowRoot, _view != null ? _view.Grass : null);

            var camera = EnsureCamera();
            EnsureLight();

            BuildSelection(camera);

            // 전개기가 양측을 한꺼번에 세웁니다. 선택 컨트롤러가 먼저 있어야
            // 세워지는 아군 분대를 그 자리에서 등록할 수 있습니다.
            BuildDeployer();

            BuildHud();
            BuildAiOverlay();
        }

        /// <summary>
        /// 매 프레임 시간을 흘리고 결말을 지켜봅니다.
        /// </summary>
        public void Tick()
        {
            // 스케일되지 않은 시간으로 갱신해야 슬로우모션 전환이 정상 속도로 진행됩니다.
            _time.Tick(Time.unscaledDeltaTime);

            TickConclusion();
        }

        /// <summary>
        /// 전투가 사라질 때 타임스케일을 되돌립니다.
        /// 그러지 않으면 다음 씬이 느려진 채 시작합니다.
        /// </summary>
        public void Dispose()
        {
            _time.Reset();
        }

        // ====================================================================================================
        // 4. Private Methods - Conclusion
        // ====================================================================================================

        /// <summary>
        /// 전투가 끝났는지 살피고, 끝났으면 보고서를 발행합니다.
        ///
        /// <b>관측도 판정도 여기서 하지 않습니다.</b>
        /// 하는 일은 관측값을 <see cref="BattleReporter"/> 에 넘기고,
        /// 돌아온 보고서를 버스에 올리는 것뿐입니다.
        /// 그래야 "왜 승리 처리가 안 됐는가"를 씬을 재생하지 않고도 확인할 수 있습니다.
        ///
        /// <b>"한 번만"을 여기서 다시 지키지 않습니다.</b>
        /// 결말이 이미 정해졌으면 <see cref="BattleReporter.Tick"/> 이 null 을 돌려줍니다.
        /// 여기에 플래그를 하나 더 두면 같은 규칙의 주인이 둘이 되고,
        /// 나중에 한쪽만 고쳐지는 순간 규칙이 아니라 버그가 됩니다.
        /// </summary>
        private void TickConclusion()
        {
            var result = _reporter.Tick(
                Time.deltaTime,
                _context.EnemyUnits.Count,
                _deployer != null ? _deployer.PlayerReserves : 0,
                _deployer == null || _deployer.EnemyReinforcementsExhausted);

            if (result == null)
            {
                return;
            }

            Debug.Log($"[Battle] 전투 종료 — {result}");

            _eventBus.Publish(new BattleConcludedEvent(result));
        }

        // ====================================================================================================
        // 5. Private Methods - Sub-systems
        // ====================================================================================================

        /// <summary>
        /// 전장을 화면에 세웁니다.
        ///
        /// 바깥이 격자만 꽂아 준 경우에는 그릴 지형이 없습니다.
        /// 자동 검사가 그 경로를 쓰므로 오류가 아니라 조용히 넘어갑니다.
        /// </summary>
        private void BuildBattlefieldView()
        {
            if (_plan.Battlefield == null)
            {
                return;
            }

            var fieldObject = new GameObject("Battlefield");
            fieldObject.transform.SetParent(_settings.RuntimeRoot, false);

            _view = fieldObject.AddComponent<BattlefieldView>();
            _view.Build(
                _plan.Battlefield,
                _settings.Setup != null ? _settings.Setup.TerrainMaterials : default,
                _settings.Setup != null ? _settings.Setup.GrassProfile : null);
        }

        /// <summary>플레이어의 조작 창구를 세웁니다.</summary>
        /// <param name="camera">클릭을 쏠 카메라입니다.</param>
        private void BuildSelection(Camera camera)
        {
            var selectionObject = new GameObject("SquadSelection");
            selectionObject.transform.SetParent(_settings.RuntimeRoot, false);

            _selection = selectionObject.AddComponent<SquadSelectionController>();
            _selection.Initialize(
                _context.Grid,
                _context.TimeController,
                _context.Tuning,
                camera,
                _settings.Setup != null ? _settings.Setup.SelectionMarkerPrefab : null,
                _settings.Setup != null ? _settings.Setup.OrderMarkerPrefab : null);
        }

        /// <summary>
        /// 양측 부대를 전장에 세우고 지원군을 관리할 전개기를 붙입니다.
        ///
        /// <b>무엇을 세울지는 주문서가, 어떻게 만들지는 여기가 압니다.</b>
        /// 전개기는 순서와 자리만 정하고, 실제 생성은 아래 두 함수가 합니다.
        /// </summary>
        private void BuildDeployer()
        {
            var deployerObject = new GameObject("SquadDeployer");
            deployerObject.transform.SetParent(_settings.RuntimeRoot, false);

            _deployer = deployerObject.AddComponent<SquadDeployer>();
            _deployer.Initialize(_context, _plan.Request, CreatePlayerSquad, CreateEnemySquad);
        }

        private void BuildHud()
        {
            if (!_settings.ShowDebugHud)
            {
                return;
            }

            var hudObject = new GameObject("BattleHud");
            hudObject.transform.SetParent(_settings.RuntimeRoot, false);

            hudObject.AddComponent<BattleDebugHud>()
                .Initialize(_context, _context, _context.TimeController, _selection, _deployer);
        }

        /// <summary>
        /// AI 판단을 씬 뷰에 그리는 오버레이를 붙입니다.
        /// 기즈모라 게임 화면에는 나오지 않고 빌드에도 남지 않습니다.
        /// </summary>
        private void BuildAiOverlay()
        {
            if (!_settings.ShowAiOverlay)
            {
                return;
            }

            var overlayObject = new GameObject("AiDebugOverlay");
            overlayObject.transform.SetParent(_settings.RuntimeRoot, false);

            overlayObject.AddComponent<AiDebugOverlay>().Initialize(_context.Grid, _context, _context);
        }

        // ====================================================================================================
        // 6. Private Methods - Squads
        // ====================================================================================================

        /// <summary>
        /// 아군 분대 하나를 전장에 세웁니다. 전개기가 자리를 정해 부릅니다.
        ///
        /// <b>무엇을 데려가는지는 여기서 정하지 않습니다.</b>
        /// 주문서가 이미 정해 놓은 것을 세우기만 합니다.
        /// </summary>
        /// <param name="order">세울 분대의 지시입니다.</param>
        /// <param name="coord">설 자리입니다.</param>
        private void CreatePlayerSquad(SquadOrder order, GridCoord coord)
        {
            var squadObject = new GameObject($"Squad_{order.Id}_{order.Definition.DisplayName}");
            squadObject.transform.SetParent(_settings.RuntimeRoot, false);

            var squad = squadObject.AddComponent<Squad>();
            squad.Initialize(
                _context,
                order.Definition,
                coord,
                order.SoldierCount,
                order.ResolveName(),
                _units.Create,
                order.ClampedRank(),
                order.Proficiency);

            _selection.RegisterSquad(squad);

            // 주문서의 식별자와 배치 인원을 기억해 둡니다.
            // 전투가 끝나면 이 짝으로 보고서가 만들어집니다.
            // 지원군으로 늦게 올라온 분대도 여기를 지나므로 함께 보고됩니다.
            _reporter.Track(order.Id, squad, squad.AliveCount);
        }

        /// <summary>
        /// 적 분대 하나를 전장에 세웁니다.
        ///
        /// 아군과 같은 자리에서 같은 방식으로 만듭니다 — 야전에서는 양측이 대칭입니다.
        /// </summary>
        /// <param name="order">세울 분대의 지시입니다.</param>
        /// <param name="coord">설 자리입니다.</param>
        private void CreateEnemySquad(SquadOrder order, GridCoord coord)
        {
            var squadObject = new GameObject($"EnemySquad_{order.Id}_{order.Definition.DisplayName}");
            squadObject.transform.SetParent(_settings.RuntimeRoot, false);

            squadObject.AddComponent<EnemySquad>().Initialize(
                _context,
                order.Definition,
                coord,
                order.SoldierCount,
                _units.Create,
                order.ClampedRank(),
                order.Proficiency);
        }

        // ====================================================================================================
        // 7. Private Methods - Scene Essentials
        // ====================================================================================================

        private Transform CreateChild(string childName)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(_settings.RuntimeRoot, false);
            return go.transform;
        }

        /// <summary>
        /// 전투 카메라를 준비합니다. 씬에 배치된 카메라를 우선 쓰고, 없으면 만듭니다.
        /// </summary>
        /// <returns>전투를 비출 카메라입니다. 만들 수도 찾을 수도 없으면 null입니다.</returns>
        private Camera EnsureCamera()
        {
            var camera = _settings.Camera != null ? _settings.Camera : Camera.main;

            if (camera == null && _settings.CreateCameraAndLight)
            {
                var cameraObject = new GameObject("BattleCamera");
                cameraObject.tag = "MainCamera";

                camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.11f, 0.15f, 0.21f);
            }

            if (camera == null)
            {
                Debug.LogError("[Battle] 씬에 카메라가 없습니다.");
                return null;
            }

            EnsureListener(camera);

            var rig = ResolveCameraRig(camera);
            var grid = _context.Grid;

            rig.AttachCamera(camera);
            rig.Configure(grid, _context.Tuning);

            rig.MoveTo(grid.WorldCenter);
            rig.FrameArea(Mathf.Max(grid.Width, grid.Depth) * grid.CellSize);

            return camera;
        }

        /// <summary>
        /// 듣는 귀가 씬에 있는지 확인하고, 없으면 카메라에 붙입니다.
        ///
        /// <b>왜 이것이 필요한가</b>
        ///
        /// 전투의 소리는 대부분 자리에서 납니다(<c>PlaySfxAt</c>). 그런데 <b>듣는 쪽</b>이 없으면
        /// 유니티는 그 소리를 아예 내지 않습니다. 오류도 경고도 없습니다.
        ///
        /// 그 증상이 "배선을 다 했는데 아무 소리도 안 난다"와 <b>구별되지 않습니다</b>.
        /// 구운 씬에는 귀가 들어 있지만, 카메라를 런타임에 만드는 경로에는 없었습니다.
        ///
        /// 씬 전체를 훑는 것은 <c>FindObjectsByType</c> 한 번이면 되고, 판마다 한 번뿐입니다.
        /// </summary>
        /// <param name="camera">귀가 없을 때 붙일 카메라입니다.</param>
        private static void EnsureListener(Camera camera)
        {
            var existing = UnityEngine.Object.FindAnyObjectByType<AudioListener>(FindObjectsInactive.Exclude);

            if (existing == null)
            {
                camera.gameObject.AddComponent<AudioListener>();
            }
        }

        /// <summary>
        /// 카메라 피벗을 확보합니다.
        ///
        /// <b>예전 구성을 함께 처리합니다.</b>
        /// 리그가 카메라 자신에게 붙어 있던 시절에 구워진 씬이 있습니다.
        /// 그대로 두면 카메라가 자기 자신의 자식이 되려다 깨지므로, 그 리그는 걷어 내고
        /// 부모 피벗을 새로 만듭니다. 씬을 다시 굽지 않아도 실행이 되게 하려는 것입니다.
        /// </summary>
        /// <param name="camera">피벗을 붙일 카메라입니다.</param>
        /// <returns>이 카메라를 돌릴 리그입니다.</returns>
        private BattleCameraRig ResolveCameraRig(Camera camera)
        {
            var onCamera = camera.GetComponent<BattleCameraRig>();

            if (onCamera != null)
            {
                // Destroy는 프레임 끝에 처리되므로, 한 프레임 더 도는 것을 막으려 즉시 꺼 둡니다.
                onCamera.enabled = false;
                UnityEngine.Object.Destroy(onCamera);
            }

            var inParent = camera.GetComponentInParent<BattleCameraRig>();

            if (inParent != null && inParent.transform != camera.transform)
            {
                return inParent;
            }

            var pivotObject = new GameObject("CameraPivot");
            pivotObject.transform.SetParent(_settings.RuntimeRoot, false);

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
            if (!_settings.CreateCameraAndLight)
            {
                return;
            }

            BattleLighting.ApplyAmbient();

            var existing = UnityEngine.Object.FindAnyObjectByType<Light>();

            if (existing != null && existing.type == LightType.Directional)
            {
                return;
            }

            var lightObject = new GameObject("BattleLight");
            lightObject.transform.SetParent(_settings.RuntimeRoot, false);

            // 값은 BattleLighting이 정합니다. 여기에 숫자를 적으면 씬 빌더와 조용히 어긋납니다.
            BattleLighting.ApplyDirectional(lightObject.AddComponent<Light>());
        }
    }
}
