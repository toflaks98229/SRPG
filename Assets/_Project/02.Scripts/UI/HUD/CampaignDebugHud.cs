using System.Text;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Campaign;
using UnityEngine;

namespace SRPG.UI.HUD
{
    /// <summary>
    /// 월드맵을 IMGUI로 그리는 검증용 화면입니다.
    ///
    /// <b>최종 화면이 아닙니다.</b> <see cref="BattleDebugHud"/> 와 같은 성격으로,
    /// 지도 표현이 붙기 전에도 캠페인이 실제로 도는지를 눈으로 확인하려고 둡니다 —
    /// 부대가 이동하고, 전투가 열리고, 손실이 장부에 반영되는지가 여기서 보입니다.
    ///
    /// <b>캠페인 스코프에 매답니다.</b>
    /// 월드맵 씬은 전투를 오갈 때마다 다시 열리지만 캠페인 스코프는 살아남습니다.
    /// 그 아래 붙어 있으면 이 화면도 함께 살아남아, 씬이 바뀔 때마다 다시 만들 필요가 없습니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CampaignDebugHud : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>표시 문자열을 조립하는 재사용 버퍼입니다. OnGUI 는 한 프레임에 여러 번 돕니다.</summary>
        private readonly StringBuilder _builder = new StringBuilder(512);

        /// <summary>캠페인 진행 상태입니다.</summary>
        private CampaignDirector _director;

        /// <summary>이동을 요청할 창구입니다.</summary>
        private System.Action<int> _move;

        /// <summary>이 화면을 그릴지 여부입니다. 전투 중에는 끕니다.</summary>
        private bool _visible = true;

        /// <summary>패널 배경 스타일입니다.</summary>
        private GUIStyle _panelStyle;

        /// <summary>본문 글자 스타일입니다.</summary>
        private GUIStyle _labelStyle;

        /// <summary>편성 체크박스 스타일입니다.</summary>
        private GUIStyle _toggleStyle;

        /// <summary>패널 배경 텍스처입니다. 파괴할 때 함께 정리합니다.</summary>
        private Texture2D _panelTexture;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>그릴 대상과 이동 창구를 연결합니다.</summary>
        /// <param name="director">캠페인 진행 상태입니다.</param>
        /// <param name="move">지점 번호를 받아 이동을 요청하는 함수입니다.</param>
        public void Initialize(CampaignDirector director, System.Action<int> move)
        {
            _director = director;
            _move = move;
        }

        /// <summary>이 화면을 그릴지 정합니다. 전투 중에는 꺼 둡니다.</summary>
        /// <param name="visible">그리면 true입니다.</param>
        public void SetVisible(bool visible)
        {
            _visible = visible;
        }

        // ====================================================================================================
        // 3. Unity Lifecycle
        // ====================================================================================================

        private void OnGUI()
        {
            if (!_visible || _director == null)
            {
                return;
            }

            EnsureStyles();

            GUILayout.BeginArea(new Rect(16f, 16f, 340f, 460f), _panelStyle);

            DrawSummary();
            DrawRoster();
            DrawDestinations();

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
        // 4. Private Methods
        // ====================================================================================================

        /// <summary>지금 어디에 며칠째 있는지를 적습니다.</summary>
        private void DrawSummary()
        {
            var here = _director.CurrentLocation;

            _builder.Clear();
            _builder.AppendLine("<b>월드맵</b>");
            _builder.Append("· 위치 : ").AppendLine(string.IsNullOrEmpty(here.DisplayName) ? "이름 없음" : here.DisplayName);
            _builder.Append("· 날짜 : ").Append(_director.Day).AppendLine("일째");
            _builder.Append("· 지형 : ").AppendLine(here.Battlefield.Terrain.ToString());

            GUILayout.Label(_builder.ToString(), _labelStyle);
        }

        /// <summary>
        /// 거느린 부대를 적고, <b>데리고 나갈 것을 고르게</b> 합니다.
        ///
        /// 체크를 뺀 분대는 이 자리에 남습니다 — 주문서에 오르지 않으므로
        /// 손실도 성장도 없습니다.
        /// </summary>
        private void DrawRoster()
        {
            var plan = _director.Deployment;
            var squads = _director.Roster.Squads;

            _builder.Clear();
            _builder
                .Append("<b>출진 편성</b>  ")
                .Append(plan.Count)
                .Append('/')
                .Append(plan.Cap)
                .Append("  (거느린 분대 ")
                .Append(_director.Roster.LivingSquadCount)
                .Append("개)");

            GUILayout.Label(_builder.ToString(), _labelStyle);

            if (squads.Count == 0)
            {
                GUILayout.Label("· 남은 부대가 없습니다.", _labelStyle);
                return;
            }

            for (int i = 0; i < squads.Count; i++)
            {
                var squad = squads[i];
                bool selected = plan.IsSelected(squad.Id);

                _builder.Clear();
                _builder
                    .Append(squad.Id)
                    .Append("번 ")
                    .Append(squad.ResolveName())
                    .Append(" — ")
                    .Append(squad.SoldierCount)
                    .Append('/')
                    .Append(squad.MaxSoldiers)
                    .Append("명, 단련 ")
                    .Append(squad.Rank)
                    .Append(", 숙련 ")
                    .Append(DescribeProficiency(squad));

                // 자리가 찼으면 <b>고르지 않은 것만</b> 잠급니다.
                // 전부 잠그면 하나를 빼서 다른 하나를 넣는 것조차 못 하게 됩니다.
                bool locked = !selected && !plan.HasRoom;

                using (new EditorDisabledScope(locked))
                {
                    if (GUILayout.Toggle(selected, _builder.ToString(), _toggleStyle) != selected)
                    {
                        plan.Toggle(squad.Id);
                    }
                }
            }

            if (!plan.IsReady)
            {
                GUILayout.Label(
                    $"<color=#FFB37A>· 최소 {plan.Minimum}개는 골라야 싸울 자리로 갈 수 있습니다.</color>",
                    _labelStyle);
            }
        }

        /// <summary>
        /// <c>GUI.enabled</c> 를 잠시 껐다가 반드시 되돌리는 자리입니다.
        ///
        /// IMGUI 의 <c>enabled</c> 는 전역 상태라, 끄고 되돌리지 않으면
        /// <b>그 아래로 그리는 모든 것</b>이 함께 잠깁니다. 그 증상은
        /// "왜 갑자기 이동 버튼이 안 눌리지"로만 보입니다.
        /// </summary>
        private readonly struct EditorDisabledScope : System.IDisposable
        {
            /// <summary>들어오기 전의 상태입니다. 나갈 때 이것으로 되돌립니다.</summary>
            private readonly bool _previous;

            /// <param name="disabled">잠글지 여부입니다.</param>
            public EditorDisabledScope(bool disabled)
            {
                _previous = GUI.enabled;

                if (disabled)
                {
                    GUI.enabled = false;
                }
            }

            /// <inheritdoc />
            public void Dispose()
            {
                GUI.enabled = _previous;
            }
        }

        /// <summary>갈 수 있는 곳을 단추로 늘어놓습니다.</summary>
        private void DrawDestinations()
        {
            GUILayout.Label("<b>이동</b>", _labelStyle);

            if (_director.IsOver)
            {
                GUILayout.Label("부대를 모두 잃어 더 나아갈 수 없습니다.", _labelStyle);
                return;
            }

            var map = _director.Map;
            bool any = false;

            for (int i = 0; i < map.NodeCount; i++)
            {
                if (!_director.CanMoveTo(i))
                {
                    continue;
                }

                any = true;

                var node = map.GetNode(i);
                bool fight = _director.HasEnemyAt(i);
                string marker = fight ? " ⚔" : "";

                // 편성이 비면 싸울 자리로는 갈 수 없습니다. 눌러도 되는 것처럼 두고
                // 아무 일도 일어나지 않게 하는 것이 가장 나쁩니다 — 눌린 것인지 고장인지 알 수 없습니다.
                using (new EditorDisabledScope(fight && !_director.Deployment.IsReady))
                {
                    if (GUILayout.Button($"{node.DisplayName}{marker}"))
                    {
                        _move?.Invoke(i);
                    }
                }
            }

            if (!any)
            {
                GUILayout.Label("여기서 갈 수 있는 곳이 없습니다.", _labelStyle);
            }
        }

        /// <summary>
        /// 그 분대가 <b>실제로 쓰는</b> 무기의 숙련도를 적습니다.
        ///
        /// 세 계열을 다 적지 않는 이유는, 쓰지 않는 무기의 숙련도가 지금 전투력에
        /// 아무 영향도 주지 않기 때문입니다. 한 줄에 필요한 것만 남깁니다.
        /// </summary>
        /// <param name="squad">적을 분대입니다.</param>
        /// <returns>"베기 40" 같은 표시 문자열입니다.</returns>
        private static string DescribeProficiency(CampaignSquad squad)
        {
            if (squad.Definition == null)
            {
                return "-";
            }

            var style = squad.Definition.Style;

            string label = style switch
            {
                AttackStyle.MeleeThrust => "찌르기",
                AttackStyle.Projectile => "사격",
                _ => "베기",
            };

            return $"{label} {squad.Proficiency.Get(style)}";
        }

        /// <summary>스타일을 한 번만 준비합니다. OnGUI 는 한 프레임에 여러 번 돕니다.</summary>
        private void EnsureStyles()
        {
            if (_panelStyle != null)
            {
                return;
            }

            _panelTexture = new Texture2D(1, 1);
            _panelTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
            _panelTexture.Apply();

            _panelStyle = new GUIStyle(GUI.skin.box);
            _panelStyle.normal.background = _panelTexture;
            _panelStyle.padding = new RectOffset(12, 12, 12, 12);

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                wordWrap = true,
            };

            _labelStyle.normal.textColor = Color.white;

            _toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                richText = true,
                wordWrap = true,
            };

            _toggleStyle.normal.textColor = Color.white;
            _toggleStyle.onNormal.textColor = Color.white;
            _toggleStyle.hover.textColor = Color.white;
            _toggleStyle.onHover.textColor = Color.white;
        }
    }
}
