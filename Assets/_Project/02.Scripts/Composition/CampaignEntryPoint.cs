using System;
using SRPG.Core;
using SRPG.Core.Events;
using SRPG.Gameplay.Campaign;
using SRPG.UI.HUD;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace SRPG.Composition
{
    /// <summary>
    /// 캠페인과 전투 사이를 오가는 유일한 창구입니다.
    ///
    /// <b>씬을 아는 것은 여기뿐입니다</b>
    ///
    /// <see cref="CampaignDirector"/> 는 규칙을, 여기는 그것을 화면으로 옮기는 일을 맡습니다.
    /// 그 경계가 있어야 "전투에서 진 뒤 부대가 제대로 줄었는가" 같은 것을
    /// 씬을 열지 않고 검사할 수 있습니다.
    ///
    /// <b>왜 전투 씬을 통째로 바꾸는가</b>
    ///
    /// 겹쳐 여는 방법도 있지만, 그러면 두 씬의 카메라와 오디오 리스너가 동시에 살아 있어
    /// 어느 쪽이 화면과 소리를 잡을지가 씬 로드 순서에 달리게 됩니다.
    /// 캠페인 상태는 씬이 아니라 <see cref="CampaignLifetimeScope"/> 가 들고 있고
    /// 그것은 씬 전환에서 살아남으므로, 씬은 그냥 갈아 끼워도 잃을 것이 없습니다.
    /// </summary>
    public sealed class CampaignEntryPoint : IStartable, IDisposable
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>캠페인 진행 규칙입니다.</summary>
        private readonly CampaignDirector _director;

        /// <summary>전투에 건넬 주문서를 올려 두는 자리입니다.</summary>
        private readonly BattleOrders _orders;

        /// <summary>오갈 씬의 이름입니다.</summary>
        private readonly CampaignSceneNames _scenes;

        /// <summary>전투의 결말을 듣는 통로입니다.</summary>
        private readonly IEventBus _eventBus;

        /// <summary>지금 게임이 어느 층에 있는지를 밝히는 창구입니다.</summary>
        private readonly IGameStateService _gameState;

        /// <summary>이 캠페인 스코프입니다. 화면을 그 아래 매달아 씬 전환에서 함께 살립니다.</summary>
        private readonly LifetimeScope _scope;

        /// <summary>월드맵 검증 화면입니다.</summary>
        private CampaignDebugHud _hud;

        /// <summary>지금 전투 씬으로 넘어가는 중인지입니다. 요청이 겹치는 것을 막습니다.</summary>
        private bool _transitioning;

        // ====================================================================================================
        // 2. Constructor
        // ====================================================================================================

        /// <param name="director">캠페인 진행 규칙입니다.</param>
        /// <param name="orders">전투에 건넬 주문서 자리입니다.</param>
        /// <param name="scenes">오갈 씬의 이름입니다.</param>
        /// <param name="eventBus">전투의 결말을 들을 통로입니다.</param>
        /// <param name="gameState">층 상태 창구입니다.</param>
        /// <param name="scope">이 캠페인 스코프입니다. 화면을 매달 자리로 씁니다.</param>
        [UnityEngine.Scripting.Preserve]
        public CampaignEntryPoint(
            CampaignDirector director,
            BattleOrders orders,
            CampaignSceneNames scenes,
            IEventBus eventBus,
            IGameStateService gameState,
            LifetimeScope scope)
        {
            _director = director;
            _orders = orders;
            _scenes = scenes;
            _eventBus = eventBus;
            _gameState = gameState;
            _scope = scope;
        }

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>캠페인 진행 상태입니다. 월드맵 화면이 이것을 읽어 그립니다.</summary>
        public CampaignDirector Director => _director;

        // ====================================================================================================
        // 4. Lifecycle
        // ====================================================================================================

        /// <summary>
        /// 전투의 결말을 듣기 시작하고 층을 월드맵으로 옮깁니다.
        /// </summary>
        public void Start()
        {
            _eventBus.Subscribe<BattleConcludedEvent>(OnBattleConcluded);
            _eventBus.Subscribe<BattleDismissedEvent>(OnBattleDismissed);

            BuildHud();

            _gameState.ChangeState(GameState.WorldMap);

            Debug.Log(
                $"[Campaign] 캠페인 시작 — 분대 {_director.Roster.LivingSquadCount}개, " +
                $"지점 {_director.Map.NodeCount}곳");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _eventBus.Unsubscribe<BattleConcludedEvent>(OnBattleConcluded);
            _eventBus.Unsubscribe<BattleDismissedEvent>(OnBattleDismissed);
        }

        /// <summary>
        /// 월드맵 검증 화면을 만들어 캠페인 스코프 아래 매답니다.
        ///
        /// 스코프가 씬 전환에서 살아남으므로 이 화면도 함께 살아남습니다.
        /// 씬을 오갈 때마다 다시 만들면 그때마다 상태 연결을 다시 해야 합니다.
        /// </summary>
        private void BuildHud()
        {
            if (_scope == null)
            {
                return;
            }

            var hudObject = new GameObject("CampaignHud");
            hudObject.transform.SetParent(_scope.transform, false);

            _hud = hudObject.AddComponent<CampaignDebugHud>();
            _hud.Initialize(_director, RequestMove);
        }

        // ====================================================================================================
        // 5. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 부대를 그 지점으로 옮기고, 거기에 적이 있으면 전투를 엽니다.
        /// </summary>
        /// <param name="node">갈 지점 번호입니다.</param>
        public void RequestMove(int node)
        {
            if (_transitioning)
            {
                return;
            }

            if (!_director.MoveTo(node))
            {
                return;
            }

            // 주문서가 올라와 있어야 전투가 무엇을 데리고 나갈지 압니다.
            // 전투 스코프는 씬이 열리면서 스스로 조립되므로, 여는 것은 반드시 채운 뒤여야 합니다.
            _transitioning = true;

            _gameState.ChangeState(GameState.Battle);
            _hud?.SetVisible(false);

            SceneManager.LoadSceneAsync(_scenes.Battle, LoadSceneMode.Single);
        }

        // ====================================================================================================
        // 6. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 전투가 끝났습니다. 보고를 장부에 반영하고 월드맵으로 돌아갑니다.
        /// </summary>
        /// <param name="message">결말 소식입니다.</param>
        private void OnBattleConcluded(BattleConcludedEvent message)
        {
            _director.ApplyResult(message.Result);

            _transitioning = false;

            if (_director.IsOver)
            {
                Debug.Log("[Campaign] 부대를 모두 잃어 캠페인이 끝났습니다.");
            }

            // 여기서 씬을 갈아 끼우지 않습니다.
            //
            // 성적은 지금 반영해야 맞습니다 — 전투는 이미 끝났고, 늦출 이유가 없습니다.
            // 그러나 <b>전장을 떠나는 것</b>은 다른 일입니다. 판정이 나자마자 넘어가면
            // 전과를 볼 틈이 없습니다. 예전에 그랬고, 화면이 그냥 바뀌었습니다.
            //
            // 떠날 때는 전장이 알려 줍니다(BattleDismissedEvent).
            // 얼마나 기다릴지는 화면이 무엇을 보여 주느냐에 달렸고, 그것은 캠페인이 알 일이 아닙니다.
        }

        /// <summary>
        /// 사람이 전과를 다 보고 전장을 떠나겠다고 했을 때 월드맵으로 돌립니다.
        /// </summary>
        /// <param name="message">떠나겠다는 소식입니다. 실린 내용은 없습니다.</param>
        private void OnBattleDismissed(BattleDismissedEvent message)
        {
            _gameState.ChangeState(GameState.WorldMap);
            _hud?.SetVisible(true);

            // 비동기로 넘깁니다. 지금은 전투가 소식을 돌리는 도중이라,
            // 여기서 씬을 즉시 갈아 끼우면 알림을 아직 다 돌리지 못한 채로 발신자가 파괴됩니다.
            SceneManager.LoadSceneAsync(_scenes.WorldMap, LoadSceneMode.Single);
        }
    }
}
