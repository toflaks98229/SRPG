using System.Text;
using SRPG.Common;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Enemies;
using SRPG.Gameplay.Selection;
using SRPG.Systems.AI;
using SRPG.Systems.Time;
using UnityEngine;

namespace SRPG.UI.HUD
{
    /// <summary>
    /// 프로토타입 검증용 HUD입니다. IMGUI로 그려 UI 에셋 의존 없이 즉시 동작합니다.
    ///
    /// 최종 게임의 HUD가 아닙니다. Bad North의 미니멀 원칙에 따르면 이런 수치는 대부분 숨겨야 하지만,
    /// 지금 단계에서는 "AI가 무슨 판단을 했는지"를 눈으로 봐야 하므로 일부러 전부 노출합니다.
    /// 조사 보고서 5.2절에서 강조한 AI 디버그 시각화의 첫 조각입니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleDebugHud : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        private readonly StringBuilder _builder = new StringBuilder(512);

        private IUnitRoster _roster;
        private TacticalTimeController _clock;
        private SquadSelectionController _selection;
        private EnemySpawner _spawner;

        private GUIStyle _panelStyle;
        private GUIStyle _labelStyle;
        private Texture2D _panelTexture;

        // ====================================================================================================
        // 2. Unity Lifecycle
        // ====================================================================================================

        private void OnGUI()
        {
            if (_roster == null)
            {
                return;
            }

            EnsureStyles();

            GUILayout.BeginArea(new Rect(12f, 12f, 330f, 400f), _panelStyle);
            GUILayout.Label(BuildStatusText(), _labelStyle);
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            if (_panelTexture != null)
            {
                Destroy(_panelTexture);
            }
        }

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>
        /// HUD를 초기화합니다.
        ///
        /// <b>세는 데 필요한 것만 받습니다.</b>
        /// 표시용 화면이 경로 탐색기나 타일 점유에 손댈 일은 없고,
        /// 받을 수 있게 두면 언젠가 손댑니다.
        /// </summary>
        public void Initialize(
            IUnitRoster roster,
            TacticalTimeController clock,
            SquadSelectionController selection,
            EnemySpawner spawner)
        {
            _roster = roster;
            _clock = clock;
            _selection = selection;
            _spawner = spawner;
        }

        // ====================================================================================================
        // 4. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 표시할 상태 문자열을 만듭니다.
        /// </summary>
        private string BuildStatusText()
        {
            _builder.Clear();

            _builder.AppendLine("<b>── 전투 상태 ──</b>");

            if (_spawner?.Scheduler != null)
            {
                var scheduler = _spawner.Scheduler;

                if (scheduler.IsFinished)
                {
                    _builder.AppendLine("웨이브: 전부 출현 완료");
                }
                else
                {
                    _builder.AppendLine($"웨이브: {scheduler.NextWaveIndex + 1} / {scheduler.TotalWaves}");
                    _builder.AppendLine($"다음까지: {scheduler.TimeUntilNextWave:F1}초");
                }

                _builder.AppendLine($"상륙정: {_spawner.ActiveShipCount}척");
            }

            _builder.AppendLine($"적 병력: {_roster.EnemyUnits.Count}명");
            _builder.AppendLine($"아군 병력: {_roster.PlayerUnits.Count}명");

            if (_clock != null)
            {
                _builder.AppendLine($"타임스케일: {_clock.CurrentScale:F2}");
            }
            _builder.AppendLine();

            _builder.AppendLine("<b>── 분대 ──</b>");

            if (_selection != null)
            {
                var squads = _selection.Squads;
                if (squads.Count == 0)
                {
                    _builder.AppendLine("남은 분대가 없습니다.");
                }

                for (int i = 0; i < squads.Count; i++)
                {
                    var squad = squads[i];
                    if (squad == null)
                    {
                        continue;
                    }

                    string marker = squad == _selection.Selected ? "▶ " : "   ";
                    _builder.AppendLine($"{marker}{squad.DisplayName}  {squad.AliveCount}명  [{ToKorean(squad.State)}]");
                }
            }

            AppendEnemySquads();

            _builder.AppendLine();
            _builder.AppendLine("<b>── 조작 ──</b>");
            _builder.AppendLine("좌클릭(분대 근처): 선택");
            _builder.AppendLine("좌클릭(지형): 이동 명령");
            _builder.AppendLine("우클릭 / Esc: 선택 해제");
            _builder.AppendLine("WASD / 방향키: 시점 이동");
            _builder.AppendLine("Q / E: 카메라 회전");
            _builder.AppendLine("휠: 확대·축소");
            _builder.AppendLine("<i>선택 중에는 시간이 느려집니다</i>");

            return _builder.ToString();
        }

        /// <summary>
        /// 적 분대가 무슨 판단을 했는지 씁니다.
        ///
        /// AI를 튜닝할 때 가장 먼저 보게 되는 정보입니다.
        /// "가옥으로 가는가, 분대를 치러 가는가"와 그 판단의 점수가 여기 나옵니다.
        /// 실제 경로는 씬 뷰의 <c>AiDebugOverlay</c> 기즈모로 확인합니다.
        /// </summary>
        private void AppendEnemySquads()
        {
            var squads = Object.FindObjectsByType<EnemySquad>(FindObjectsSortMode.None);

            _builder.AppendLine();
            _builder.AppendLine($"<b>── 적 분대 ({squads.Length}) ──</b>");

            if (squads.Length == 0)
            {
                _builder.AppendLine("상륙한 적이 없습니다.");
                return;
            }

            for (int i = 0; i < squads.Length; i++)
            {
                var squad = squads[i];
                if (squad == null || squad.IsDisbanded)
                {
                    continue;
                }

                string goal = squad.GoalKind == GoalKind.House ? "가옥" : "분대";
                _builder.AppendLine($"   {squad.AliveCount}명 → {goal} {squad.GoalCoord}  ({squad.GoalScore:F2})");
            }
        }

        /// <summary>
        /// 분대 상태를 한국어로 바꿉니다.
        /// </summary>
        private static string ToKorean(SquadState state)
        {
            return state switch
            {
                SquadState.Idle => "대기",
                SquadState.Moving => "이동",
                SquadState.Fighting => "교전",
                SquadState.Destroyed => "소멸",
                _ => state.ToString(),
            };
        }

        /// <summary>
        /// IMGUI 스타일을 준비합니다. OnGUI에서 매번 만들면 할당이 반복되므로 한 번만 만듭니다.
        /// </summary>
        private void EnsureStyles()
        {
            if (_panelStyle != null)
            {
                return;
            }

            _panelTexture = new Texture2D(1, 1);
            _panelTexture.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.09f, 0.82f));
            _panelTexture.Apply();

            _panelStyle = new GUIStyle
            {
                padding = new RectOffset(12, 12, 10, 10),
            };
            _panelStyle.normal.background = _panelTexture;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 13,
                wordWrap = true,
            };
            _labelStyle.normal.textColor = new Color(0.92f, 0.94f, 0.97f);
        }
    }
}
