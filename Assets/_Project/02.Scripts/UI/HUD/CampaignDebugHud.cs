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

        /// <summary>거느린 부대를 적습니다. 이것이 전장에 그대로 섭니다.</summary>
        private void DrawRoster()
        {
            _builder.Clear();
            _builder.Append("<b>부대</b>  (분대 ").Append(_director.Roster.LivingSquadCount).AppendLine("개)");

            var squads = _director.Roster.Squads;

            for (int i = 0; i < squads.Count; i++)
            {
                var squad = squads[i];

                _builder
                    .Append("· ")
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
                    .AppendLine(DescribeProficiency(squad));
            }

            if (squads.Count == 0)
            {
                _builder.AppendLine("· 남은 부대가 없습니다.");
            }

            GUILayout.Label(_builder.ToString(), _labelStyle);
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
                string marker = _director.HasEnemyAt(i) ? " ⚔" : "";

                if (GUILayout.Button($"{node.DisplayName}{marker}"))
                {
                    _move?.Invoke(i);
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
        }
    }
}
