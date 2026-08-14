using System;
using System.Collections.Generic;
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
using UnityEngine.SceneManagement;
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

        /// <summary>
        /// 캠페인이 없을 때 돌아갈 씬 이름입니다.
        ///
        /// 캠페인이 있으면 <c>CampaignSceneNames</c> 가 갈 곳을 정하고 여기는 쓰이지 않습니다.
        /// 이 값은 <b>캠페인 없이 전투 씬만 열어 본 경우</b>를 위한 것이라,
        /// 설정으로 빼면 "설정이 비면 아무 데도 못 간다"가 되어 목적이 사라집니다.
        /// </summary>
        private const string FallbackWorldMapScene = "WorldMap";

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

        /// <summary>이번 프레임에 돌릴 아군 분대의 사본입니다. 이유는 <see cref="TickSquads"/> 에 적어 두었습니다.</summary>
        private readonly List<Squad> _playerSquadBuffer = new List<Squad>(8);

        /// <summary>이번 프레임에 돌릴 적 분대의 사본입니다.</summary>
        private readonly List<EnemySquad> _enemySquadBuffer = new List<EnemySquad>(8);

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
        /// 전투의 한 프레임입니다. <b>이 순서 자체가 규칙입니다.</b>
        ///
        /// <b>왜 여기로 모으는가</b>
        ///
        /// 예전에는 선택 컨트롤러·전개기·분대가 각자 <c>Update</c> 를 들고 있었습니다.
        /// 그런데 이 넷은 <b>서로를 관측합니다</b> — 선택이 분대에 명령을 내리고,
        /// 전개기가 분대 수를 세고, 적 분대가 아군 분대의 앵커를 읽습니다.
        /// 유니티는 스크립트 사이의 <c>Update</c> 순서를 정해 주지 않으므로,
        /// 그 관측이 <b>이번 프레임의 값인지 지난 프레임의 값인지</b> 알 수 없었습니다.
        ///
        /// 이 결함은 예외를 내지 않습니다. "가끔 지원군이 한 박자 늦는다",
        /// "클릭이 이따금 씹힌 것 같다"로만 나타나고, 실행마다 달라져 재현되지도 않습니다.
        /// 이 프로젝트가 이미 세 번 겪은 종류입니다 — 겉으로 성공과 실패가 구분되지 않는 고장.
        ///
        /// 병사에게는 이 판단이 이미 내려져 있었습니다(<see cref="SquadMembers.Tick"/>).
        /// 여기는 <b>그것을 한 층 위에 마저 적용한 것</b>입니다.
        ///
        /// <b>순서에 이유가 있는 자리마다 그 이유를 적어 두었습니다.</b>
        /// <see cref="Start"/> 가 세우는 순서에 대해 하는 일과 같습니다.
        /// </summary>
        public void Tick()
        {
            // 한 프레임의 길이를 여기서 한 번만 정합니다.
            // 예전에는 분대·전개기가 각자 Time.deltaTime 을 읽었습니다. 값이 같아 아무 일도
            // 없었지만, 같은 사실을 여러 곳이 말하는 모양이었습니다(문서 §7.5 의 그 함정).
            float deltaTime = Time.deltaTime;

            // ① 입력 — 가장 먼저.
            //    여기서 내린 이동 명령이 ④ 의 앵커 전진에 같은 프레임에 반영되어야 합니다.
            //    뒤에 두면 클릭이 언제나 한 프레임 늦게 듣습니다.
            _selection?.Tick();

            // ② 시간 — 입력 다음.
            //    슬로우모션을 걸지 말지는 ① 이 정합니다. 앞에 두면 이번 프레임의 선택이
            //    다음 프레임에야 배율에 반영됩니다.
            //    스케일되지 않은 시간으로 갱신해야 전환 자체는 정상 속도로 진행됩니다 —
            //    스케일 시간을 쓰면 느려질수록 더 느리게 느려집니다.
            _time.Tick(Time.unscaledDeltaTime);

            // ③ 전개 — 분대 앞.
            //    전개기가 읽는 "몇 부대가 서 있는가"가 언제나 <b>지난 프레임까지 확정된 수</b>가
            //    되고, 이번 프레임에 올라온 지원군은 세워진 그 프레임부터 ④ 에서 함께 돕니다.
            //    뒤에 두면 새 분대가 한 프레임 멈춰 선 채로 태어납니다.
            _deployer?.Tick(deltaTime);

            // ④ 분대 — 아군 먼저, 그다음 적.
            TickSquads(deltaTime);

            // ⑤ 결말 — 마지막.
            //    이번 프레임의 결과를 보고 판정해야 합니다.
            //    앞에 두면 보고서가 언제나 한 프레임 낡은 전황을 봅니다.
            TickConclusion(deltaTime);
        }

        /// <summary>
        /// 양측 분대를 돌립니다.
        ///
        /// <b>아군을 먼저 돌립니다.</b>
        /// 적 분대의 판단(<see cref="EnemySquad"/> 의 재판단)이 아군 분대의 앵커를 후보로 읽으므로,
        /// 이 순서라야 적이 <b>이번 프레임의</b> 자리를 보고 목표를 고릅니다.
        /// 반대로 두면 적은 언제나 한 프레임 낡은 자리를 겨눕니다 — 빠르게 움직이는 분대일수록 더 어긋납니다.
        ///
        /// <b>명부를 그대로 순회하지 않습니다.</b>
        /// 분대는 자기 차례 안에서 무너지면서 스스로 명부에서 빠집니다
        /// (<c>Squad.DestroySquad</c> · <c>EnemySquad.Disband</c>).
        /// 순회 도중 목록이 줄면 뒤에 선 분대의 인덱스가 당겨져 <b>그 프레임이 통째로 날아갑니다.</b>
        /// <see cref="SquadMembers"/> 가 병사에게 겪은 것과 같은 문제인데,
        /// 병사는 명부에서 빼는 일을 다음 프레임으로 미뤄서 풀었고 분대는 그럴 수 없습니다 —
        /// 무너진 분대가 한 프레임 더 명부에 남으면 지원군이 그만큼 늦습니다.
        /// 그래서 <b>이번 프레임에 돌 대상을 먼저 확정</b>한 뒤 그 사본을 훑습니다.
        /// </summary>
        /// <param name="deltaTime">지난 시간입니다.</param>
        private void TickSquads(float deltaTime)
        {
            Snapshot(_context.PlayerSquads, _playerSquadBuffer);
            Snapshot(_context.EnemySquads, _enemySquadBuffer);

            for (int i = 0; i < _playerSquadBuffer.Count; i++)
            {
                var squad = _playerSquadBuffer[i];

                if (squad != null)
                {
                    squad.Tick(deltaTime);
                }
            }

            for (int i = 0; i < _enemySquadBuffer.Count; i++)
            {
                var squad = _enemySquadBuffer[i];

                if (squad != null)
                {
                    squad.Tick(deltaTime);
                }
            }

            // 사본이 죽은 분대를 다음 프레임까지 붙들고 있지 않게 합니다.
            _playerSquadBuffer.Clear();
            _enemySquadBuffer.Clear();
        }

        /// <summary>
        /// 명부를 사본으로 옮깁니다. 등록 순서를 그대로 지켜야 같은 시드가 같은 판을 만듭니다.
        /// </summary>
        /// <typeparam name="T">분대 타입입니다.</typeparam>
        /// <param name="source">읽어 갈 명부입니다.</param>
        /// <param name="destination">사본이 채워집니다. 호출 시 비워집니다.</param>
        private static void Snapshot<T>(IReadOnlyList<T> source, List<T> destination)
        {
            destination.Clear();

            for (int i = 0; i < source.Count; i++)
            {
                destination.Add(source[i]);
            }
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
        /// <param name="deltaTime">지난 시간입니다. 전투 경과 시간을 세는 데 씁니다.</param>
        private void TickConclusion(float deltaTime)
        {
            var result = _reporter.Tick(
                deltaTime,
                _context.EnemyUnits.Count,
                _deployer != null ? _deployer.PlayerReserves : 0,
                _deployer == null || _deployer.EnemyReinforcementsExhausted);

            if (result == null)
            {
                return;
            }

            Debug.Log($"[Battle] 전투 종료 — {result}");

            _eventBus.Publish(new BattleConcludedEvent(result));

            ShowOutcome(result);
        }

        /// <summary>
        /// 전과를 화면에 띄웁니다.
        ///
        /// <b>왜 결말과 전환 사이에 사람을 세우는가</b>
        ///
        /// 예전에는 판정이 나자마자 캠페인이 씬을 갈아 끼웠습니다.
        /// 무엇을 얻고 무엇을 잃었는지 볼 틈이 없었고, 캠페인이 없는 경우
        /// (전투 씬만 열어 본 실행)에는 <b>끝났다는 티조차 나지 않았습니다.</b>
        /// 판정은 났는데 화면이 그대로라, 그 모습이 "판정이 안 났다"와 구별되지 않았습니다.
        /// </summary>
        /// <param name="result">보여 줄 보고서입니다.</param>
        private void ShowOutcome(BattleResult result)
        {
            var host = new GameObject("BattleOutcome");
            host.transform.SetParent(_settings.RuntimeRoot, false);

            host.AddComponent<BattleOutcomeScreen>().Show(result, LeaveBattlefield);
        }

        /// <summary>
        /// 사람이 확인을 눌렀을 때 전장을 떠납니다.
        ///
        /// <b>떠나는 방법이 둘입니다.</b>
        /// 캠페인이 있으면 그쪽이 다음 갈 곳을 압니다 — 여기서 씬 이름을 알 필요가 없고,
        /// 알아서도 안 됩니다. 소식만 올리고 물러납니다.
        ///
        /// 캠페인이 없으면 아무도 듣지 않아 화면만 닫히고 끝납니다.
        /// 그때는 여기서 직접 넘어갑니다 — 전투 씬만 열어 본 경우에도
        /// 끝이 어디로 이어지는지는 보여야 합니다.
        /// </summary>
        private void LeaveBattlefield()
        {
            _eventBus.Publish(new BattleDismissedEvent());

            if (CampaignLifetimeScope.Live != null)
            {
                return;
            }

            SceneManager.LoadSceneAsync(FallbackWorldMapScene, LoadSceneMode.Single);
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
                _settings.Setup != null ? _settings.Setup.GrassProfile : null,
                _settings.Setup != null ? _settings.Setup.SkyProfile : null);
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
                order.Proficiency,
                order.ResolvePerks());

            _selection.RegisterSquad(squad);

            // 규칙은 분대가 쥐고, <b>연출은 여기서 엮습니다.</b>
            //
            // 시간을 붙잡는 것은 판 전체에 걸리는 일이라 병사도 분대도 손댈 자리가 아닙니다.
            // 실제로 <c>IUnitContext</c> 와 <c>ISquadContext</c> 어느 쪽에도 시간 제어기가 없습니다 —
            // 넣으면 언젠가 병사 하나가 자기 사정으로 판 전체를 멈춥니다.
            // 조립 지점은 전부를 알아도 되는 유일한 자리이므로, 이 매듭은 여기서 짓습니다.
            squad.SquadDestroyed += OnSquadLost;
            squad.CommanderWounded += OnCommanderWounded;

            // 주문서의 식별자와 배치 인원을 기억해 둡니다.
            // 전투가 끝나면 이 짝으로 보고서가 만들어집니다.
            // 지원군으로 늦게 올라온 분대도 여기를 지나므로 함께 보고됩니다.
            _reporter.Track(order.Id, squad, squad.AliveCount);
        }

        // ====================================================================================================
        // 6-1. Private Methods - 지휘관을 잃는 순간
        //
        // 이 게임에서 되돌릴 수 없는 손실은 이것 하나뿐입니다(조사 보고서 §2.5).
        // 그런데 화면에서는 병사 몇이 동시에 사라질 뿐이라, <b>가장 중요한 사건이
        // 가장 조용하게</b> 지나갑니다. 시간을 붙잡아 그 순간에 무게를 싣습니다.
        // ====================================================================================================

        /// <summary>지휘관을 잃어 분대가 사라졌습니다.</summary>
        /// <param name="squad">사라진 분대입니다.</param>
        private void OnSquadLost(Squad squad)
        {
            squad.SquadDestroyed -= OnSquadLost;
            squad.CommanderWounded -= OnCommanderWounded;

            var tuning = _context.Tuning.Time;

            _time.HitStop(tuning.LossHitStopSeconds, tuning.LossHitStopScale);

            Debug.Log($"[Battle] {squad.DisplayName} — 지휘관을 잃어 분대가 사라졌습니다.");
        }

        /// <summary>
        /// 지휘관이 쓰러질 뻔했다가 버텼습니다.
        ///
        /// <b>잃은 순간보다 약하게 겁니다.</b> 같은 세기로 두면 둘이 구별되지 않고,
        /// 무엇보다 부상은 전투 중 여러 번 일어날 수 있어 판이 계속 끊깁니다.
        /// 면했다는 것만 읽히면 충분합니다.
        /// </summary>
        /// <param name="squad">지휘관이 버텨 낸 분대입니다.</param>
        private void OnCommanderWounded(Squad squad)
        {
            var tuning = _context.Tuning.Time;

            _time.HitStop(tuning.WoundHitStopSeconds, tuning.WoundHitStopScale);
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

                // 리그가 붙일 때도 되돌리지만, 여기서 미리 맞춰 두면
                // 리그가 붙기 전 한 프레임이 원근으로 그려지는 것을 막습니다.
                camera.orthographic = true;
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
