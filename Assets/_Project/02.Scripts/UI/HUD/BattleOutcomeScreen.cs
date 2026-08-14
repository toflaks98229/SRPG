using System;
using System.Text;
using SRPG.Data;
using UnityEngine;

namespace SRPG.UI.HUD
{
    /// <summary>
    /// 전투가 끝났음을 알리고 전과를 보여 주는 화면입니다.
    ///
    /// <b>왜 전투 안에 두는가</b>
    ///
    /// 결말은 캠페인이 받아 갑니다. 그래서 오랫동안 전투 쪽에는 끝을 <b>보여 주는</b> 것이
    /// 아무것도 없었습니다 — 판정은 나고 캠페인이 씬을 갈아 끼우는데,
    /// 그 사이에 사람이 볼 것이 없어 화면이 그냥 바뀌었습니다.
    ///
    /// 더 나쁜 것은 캠페인 없이 전투 씬만 열었을 때입니다. 아무도 결말을 받지 않으므로
    /// <b>전투가 끝난 티조차 나지 않습니다.</b> 판정이 났는데도 화면은 그대로 서 있고,
    /// 그 모습은 "판정이 안 났다"와 구별되지 않습니다.
    ///
    /// 여기서 끝을 눈에 보이게 만듭니다. 전환은 <b>사람이 확인을 누른 뒤에</b> 일어납니다.
    ///
    /// <b>왜 IMGUI 인가</b>
    ///
    /// 이 프로젝트는 에셋이 비어 있어도 재생 버튼만으로 돌아가야 합니다.
    /// 프리팹도 캔버스도 없이 코드만으로 서는 <see cref="BattleDebugHud"/> 와 같은 방식을 씁니다.
    /// 임시입니다 — 화면을 제대로 꾸밀 때 이 클래스는 통째로 교체됩니다.
    /// </summary>
    public sealed class BattleOutcomeScreen : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>패널의 가로 크기입니다.</summary>
        private const float PanelWidth = 420f;

        /// <summary>패널의 세로 크기입니다.</summary>
        private const float PanelHeight = 260f;

        /// <summary>확인 단추의 세로 크기입니다.</summary>
        private const float ButtonHeight = 40f;

        /// <summary>
        /// 화면이 뜨고 나서 확인을 받기까지 두는 시간(초)입니다.
        ///
        /// 마지막 일격의 순간에 마우스를 누르고 있던 손이 그대로 확인까지 눌러 버립니다.
        /// 전과를 읽을 틈도 없이 넘어가면 화면이 있으나 마나입니다.
        /// </summary>
        private const float InputGuardSeconds = 0.6f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        /// <summary>보여 줄 보고서입니다. 없으면 아무것도 그리지 않습니다.</summary>
        private BattleResult _result;

        /// <summary>확인을 눌렀을 때 부를 것입니다.</summary>
        private Action _onConfirm;

        /// <summary>화면이 뜬 뒤 흐른 시간입니다. 스케일되지 않은 시간으로 셉니다.</summary>
        private float _shownFor;

        /// <summary>이미 확인을 받았는지 여부입니다. 두 번 넘어가지 않게 합니다.</summary>
        private bool _confirmed;

        /// <summary>문자열을 조립하는 재사용 버퍼입니다. OnGUI 는 한 프레임에 여러 번 돕니다.</summary>
        private readonly StringBuilder _builder = new StringBuilder(256);

        private GUIStyle _panelStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 보고서를 받아 화면을 띄웁니다.
        /// </summary>
        /// <param name="result">보여 줄 보고서입니다. null이면 화면이 뜨지 않습니다.</param>
        /// <param name="onConfirm">확인을 눌렀을 때 부를 것입니다. 없어도 됩니다.</param>
        public void Show(BattleResult result, Action onConfirm)
        {
            _result = result;
            _onConfirm = onConfirm;
            _shownFor = 0f;
            _confirmed = false;
        }

        // ====================================================================================================
        // 4. Unity Lifecycle
        // ====================================================================================================

        private void Update()
        {
            if (_result == null || _confirmed)
            {
                return;
            }

            // 전투가 끝나면 타임스케일이 어떻게 되어 있을지 알 수 없습니다.
            // 화면이 뜨는 속도가 전황에 좌우되면 안 됩니다.
            _shownFor += Time.unscaledDeltaTime;
        }

        private void OnGUI()
        {
            if (_result == null)
            {
                return;
            }

            EnsureStyles();

            var panel = new Rect(
                (Screen.width - PanelWidth) * 0.5f,
                (Screen.height - PanelHeight) * 0.5f,
                PanelWidth,
                PanelHeight);

            GUI.Box(panel, GUIContent.none, _panelStyle);

            var inner = new Rect(panel.x + 24f, panel.y + 20f, panel.width - 48f, panel.height - 40f);

            GUILayout.BeginArea(inner);

            GUILayout.Label(TitleFor(_result.Outcome), _titleStyle);
            GUILayout.Space(12f);
            GUILayout.Label(BuildBody(), _bodyStyle);

            GUILayout.FlexibleSpace();

            DrawConfirm();

            GUILayout.EndArea();
        }

        // ====================================================================================================
        // 5. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 확인 단추를 그립니다. 뜬 직후에는 눌리지 않습니다.
        /// </summary>
        private void DrawConfirm()
        {
            bool ready = _shownFor >= InputGuardSeconds;

            GUI.enabled = ready && !_confirmed;

            if (GUILayout.Button("월드맵으로", GUILayout.Height(ButtonHeight)) && ready && !_confirmed)
            {
                _confirmed = true;

                // 부르는 쪽이 씬을 갈아 끼울 수 있습니다. 먼저 화면을 지워 두지 않으면
                // 전환이 시작된 뒤에도 한두 프레임 더 그려집니다.
                _result = null;

                _onConfirm?.Invoke();
            }

            GUI.enabled = true;
        }

        /// <summary>
        /// 결말에 맞는 문구를 고릅니다.
        /// </summary>
        /// <param name="outcome">전투의 결말입니다.</param>
        /// <returns>화면에 띄울 문구입니다.</returns>
        private static string TitleFor(BattleOutcome outcome)
        {
            return outcome switch
            {
                BattleOutcome.Victory => "<color=#dfe6a8>승리</color>",
                BattleOutcome.Defeat => "<color=#d98c7a>패배</color>",
                _ => "전투 종료",
            };
        }

        /// <summary>
        /// 전과를 문장으로 만듭니다.
        /// </summary>
        /// <returns>화면에 띄울 본문입니다.</returns>
        private string BuildBody()
        {
            _builder.Clear();

            _builder.AppendLine($"처치한 적      {_result.EnemiesKilled}명");
            _builder.AppendLine($"살아남은 분대  {_result.SurvivingSquads}개");
            _builder.AppendLine($"잃은 병사      {_result.TotalLosses}명");

            int minutes = Mathf.FloorToInt(_result.Duration / 60f);
            int seconds = Mathf.FloorToInt(_result.Duration % 60f);

            _builder.Append($"걸린 시간      {minutes}분 {seconds}초");

            return _builder.ToString();
        }

        /// <summary>
        /// IMGUI 스타일을 준비합니다. OnGUI 에서 매번 만들면 할당이 반복됩니다.
        /// </summary>
        private void EnsureStyles()
        {
            if (_panelStyle != null)
            {
                return;
            }

            var background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.08f, 0.92f));
            background.Apply();
            background.hideFlags = HideFlags.HideAndDontSave;

            _panelStyle = new GUIStyle
            {
                normal = { background = background },
            };

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
            };

            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                richText = true,
            };
        }
    }
}
