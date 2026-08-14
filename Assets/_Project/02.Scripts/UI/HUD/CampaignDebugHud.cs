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
        // 1. Constants
        // ====================================================================================================

        /// <summary>승급 패널이 놓이는 자리입니다.</summary>
        private const float PanelX = 372f;

        /// <summary>패널이 밀려 들어오는 거리입니다.</summary>
        private const float PanelSlideDistance = 90f;

        /// <summary>패널이 다 들어오기까지의 시간입니다.</summary>
        private const float PanelRevealSeconds = 0.35f;

        /// <summary>단련도 숫자가 새 값으로 넘어가는 시각입니다.</summary>
        private const float RankFlipSeconds = 0.55f;

        /// <summary>첫 선택지가 놓이기까지 기다리는 시간입니다.</summary>
        private const float ChoiceDelaySeconds = 0.45f;

        /// <summary>선택지 하나씩의 간격입니다.</summary>
        private const float ChoiceStaggerSeconds = 0.14f;

        /// <summary>선택지 버튼의 높이입니다.</summary>
        private const float ChoiceHeight = 34f;

        /// <summary>고른 특전을 보여 주는 시간입니다.</summary>
        private const float ConfirmSeconds = 1.6f;

        // ====================================================================================================
        // 1-1. Fields
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

        // ---------------------------------------------------------------------------------------
        // 승급 연출
        //
        // <b>왜 연출이 필요한가</b>
        //
        // 규칙만 있으면 패널이 <b>그냥 나타납니다.</b> 그러면 승급이 사건이 아니라 상태로 읽히고,
        // 무엇보다 직전까지 지도를 누르던 손이 <b>의도치 않게 특전을 골라 버립니다</b> —
        // 이 단계에서 유일한 선택이 그렇게 소모되면 없느니만 못합니다.
        //
        // 그래서 셋을 둡니다. 패널이 밀려 들어오고, 선택지가 하나씩 놓이고,
        // <b>다 놓이기 전에는 눌리지 않습니다.</b>
        // ---------------------------------------------------------------------------------------

        /// <summary>지금 연출 중인 승급입니다. 바뀌면 연출을 처음부터 다시 돌립니다.</summary>
        private PendingPromotion _revealing;

        /// <summary>이 승급이 나타난 뒤 흐른 시간입니다.</summary>
        private float _revealTime;

        /// <summary>방금 고른 특전입니다. 잠깐 보여 주고 사라집니다.</summary>
        private SquadPerkKind _confirmedPerk;

        /// <summary>확정 표시가 남은 시간입니다.</summary>
        private float _confirmTime;

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

        /// <summary>
        /// 연출 시각만 흘립니다.
        ///
        /// <b>왜 OnGUI 가 아닌가</b>
        /// <c>OnGUI</c> 는 한 프레임에 레이아웃과 리페인트로 여러 번 돕니다.
        /// 거기서 시간을 더하면 연출이 프레임마다 두세 배 빨리 흐릅니다.
        ///
        /// <b>왜 진입점이 부르지 않는가</b>
        /// 전투에서는 분대·전개기·선택이 <b>서로를 관측하기</b> 때문에 순서를 진입점이 쥐었습니다.
        /// 여기는 자기 패널의 등장 시각만 세고 아무것도 관측하지 않습니다 —
        /// 숨은 순서 조건이 생길 자리가 없으므로 유니티에 맡겨 둡니다.
        ///
        /// <b>스케일되지 않은 시간</b>을 씁니다. 월드맵은 배율이 1이지만,
        /// 전투에서 돌아오는 길에 배율이 아직 되돌아오지 않았을 수 있습니다.
        /// </summary>
        private void Update()
        {
            if (_director == null)
            {
                return;
            }

            float delta = Time.unscaledDeltaTime;

            var current = _director.Promotions.Current;

            // 물어야 할 승급이 바뀌었으면 연출을 처음부터 다시 돌립니다.
            if (!ReferenceEquals(current, _revealing))
            {
                _revealing = current;
                _revealTime = 0f;
            }
            else if (current != null)
            {
                _revealTime += delta;
            }

            if (_confirmTime > 0f)
            {
                _confirmTime -= delta;
            }
        }

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

            // <b>승급은 다른 것 위에 그립니다.</b>
            // 목록 사이에 끼워 넣으면 스크롤 아래로 밀려 못 보고 지나칩니다 —
            // 이 단계에서 플레이어가 하는 유일한 선택인데 그러면 있으나 마나 합니다.
            DrawPromotion();
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

        /// <summary>
        /// 승급한 분대의 특전을 고르게 합니다.
        ///
        /// <b>답하기 전에는 이동이 막힙니다</b>(<see cref="CampaignDirector.MoveTo"/>).
        /// 그래서 이 화면은 "닫기"를 주지 않습니다 — 닫을 수 있으면 답하지 않고 넘어가게 되고,
        /// 그러면 특전이 한 판을 통째로 놓칩니다.
        ///
        /// 여럿이 한꺼번에 승급하면 하나씩 묻습니다. 한 화면에 다 펼치면
        /// 어느 분대의 선택인지가 흐려집니다.
        /// </summary>
        private void DrawPromotion()
        {
            DrawConfirmation();

            var pending = _director.Promotions.Current;

            if (pending == null)
            {
                return;
            }

            // 패널이 밀려 들어옵니다. 끝에서 감속하는 곡선이라 "놓인다"는 느낌이 납니다.
            float slide = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_revealTime / PanelRevealSeconds));

            float x = Mathf.Lerp(PanelX + PanelSlideDistance, PanelX, slide);

            var previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, slide);

            GUILayout.BeginArea(new Rect(x, 16f, 360f, 320f), _panelStyle);

            DrawPromotionHeader(pending);
            DrawPromotionChoices(pending);

            GUILayout.EndArea();

            GUI.color = previousColor;
        }

        /// <summary>승급한 분대와 오른 단련도를 적습니다.</summary>
        /// <param name="pending">지금 묻고 있는 승급입니다.</param>
        private void DrawPromotionHeader(PendingPromotion pending)
        {
            var squad = _director.Roster.Find(pending.SquadId);

            _builder.Clear();
            _builder.Append("<b>▲ 승급</b>  —  ")
                    .AppendLine(squad != null ? squad.ResolveName() : $"{pending.SquadId}번 분대");

            // 단련도가 <b>올라가는 것을 보여 줍니다.</b> 결과만 적으면 숫자가 하나 바뀐 것으로 보입니다.
            int shown = _revealTime < RankFlipSeconds ? pending.FromRank : pending.ToRank;

            _builder.Append("단련도 ").Append(pending.FromRank).Append(" → <b>").Append(shown).Append("</b>");

            if (_director.Promotions.PendingCount > 1)
            {
                _builder.Append("    (남은 승급 ").Append(_director.Promotions.PendingCount - 1).Append(')');
            }

            GUILayout.Label(_builder.ToString(), _labelStyle);
            GUILayout.Space(6f);
        }

        /// <summary>
        /// 특전 세 개를 하나씩 놓고, <b>다 놓인 뒤에야</b> 누를 수 있게 합니다.
        ///
        /// 곧바로 누를 수 있으면 지도를 누르던 손이 그대로 특전을 골라 버립니다.
        /// 이 단계에서 유일한 선택이라 그렇게 소모되면 없느니만 못합니다.
        /// </summary>
        /// <param name="pending">지금 묻고 있는 승급입니다.</param>
        private void DrawPromotionChoices(PendingPromotion pending)
        {
            GUILayout.Label("특전 하나를 고르십시오.", _labelStyle);
            GUILayout.Space(4f);

            bool armed = _revealTime >= ChoiceDelaySeconds + pending.Offer.Count * ChoiceStaggerSeconds;

            for (int i = 0; i < pending.Offer.Count; i++)
            {
                // 아직 놓일 차례가 아니면 자리만 비워 둡니다.
                // 목록이 아래에서 밀려 올라오면 누르려던 버튼이 손 밑에서 움직입니다.
                if (_revealTime < ChoiceDelaySeconds + i * ChoiceStaggerSeconds)
                {
                    GUILayout.Space(ChoiceHeight + 2f);
                    continue;
                }

                if (!SquadPerks.TryGet(pending.Offer[i], out var perk))
                {
                    continue;
                }

                _builder.Clear();
                _builder.Append("<b>").Append(perk.DisplayName).Append("</b>  —  ").Append(perk.Description);

                using (new EditorDisabledScope(!armed))
                {
                    if (GUILayout.Button(_builder.ToString(), GUILayout.Height(ChoiceHeight)))
                    {
                        Choose(perk.Kind);
                    }
                }
            }

            GUILayout.Space(6f);
            GUILayout.Label("장비는 여기서 주지 않습니다. 무기와 방패는 사서 듭니다.", _labelStyle);
        }

        /// <summary>
        /// 특전을 고르고, 무엇을 골랐는지 잠깐 남깁니다.
        ///
        /// <b>고른 것이 곧바로 사라지면 안 됩니다.</b> 패널이 닫히고 다음 승급이 밀려 들어오면
        /// 방금 무엇을 골랐는지가 화면에서 즉시 없어집니다. 짧게 남겨 두면 선택이 확정된 것으로 읽힙니다.
        /// </summary>
        /// <param name="perk">고른 특전입니다.</param>
        private void Choose(SquadPerkKind perk)
        {
            if (!_director.ChoosePerk(perk))
            {
                return;
            }

            _confirmedPerk = perk;
            _confirmTime = ConfirmSeconds;
        }

        /// <summary>방금 고른 특전을 잠깐 보여 주고 흐려집니다.</summary>
        private void DrawConfirmation()
        {
            if (_confirmTime <= 0f || !SquadPerks.TryGet(_confirmedPerk, out var perk))
            {
                return;
            }

            // 마지막 구간에서만 흐려집니다. 처음부터 흐려지면 읽기 전에 사라집니다.
            float alpha = Mathf.Clamp01(_confirmTime / (ConfirmSeconds * 0.4f));

            var previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);

            GUILayout.BeginArea(new Rect(PanelX, 348f, 360f, 44f), _panelStyle);

            _builder.Clear();
            _builder.Append("익혔습니다 — <b>").Append(perk.DisplayName).Append("</b>");

            GUILayout.Label(_builder.ToString(), _labelStyle);

            GUILayout.EndArea();

            GUI.color = previousColor;
        }

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

                // 승급을 기다리는 분대를 목록에서도 짚어 줍니다.
                // 패널이 이름을 적어 주더라도 <b>어느 줄인지</b>가 보여야 그 부대의 일로 읽힙니다.
                if (_director.Promotions.IsPending(squad.Id))
                {
                    _builder.Append("<b>▲</b> ");
                }

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
