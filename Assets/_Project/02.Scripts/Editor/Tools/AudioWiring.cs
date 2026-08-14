using System.Collections.Generic;
using System.Text;
using SRPG.Core.Managers;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace SRPG.Editor.Tools
{
    /// <summary>
    /// 소리 배선을 현재 코드와 에셋에 맞춥니다.
    ///
    /// <b>왜 배선 도구에서 갈라져 나왔는가</b>
    ///
    /// 소리는 "안 나는 이유"가 다른 배선보다 훨씬 많습니다 — 믹서, 감쇠, 듣는 귀,
    /// 에디터 음소거, 프로젝트 오디오 스위치. 그런데 <b>증상은 전부 같습니다.</b>
    /// 아무 소리도 안 납니다. 눈으로 구분되는 단서가 하나도 없습니다.
    ///
    /// 그래서 이 도구의 절반은 배선이 아니라 <b>진단</b>입니다.
    /// 무엇이 꺼져 있는지 한 번에 늘어놓지 않으면, 한 번에 하나씩 의심하며
    /// 그때마다 게임을 다시 켜는 일이 반복됩니다.
    /// </summary>
    public static class AudioWiring
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>믹서에서 찾을 배경음 그룹 이름입니다.</summary>
        private const string BgmGroupName = "BGM";

        /// <summary>믹서에서 찾을 효과음 그룹 이름입니다.</summary>
        private const string SfxGroupName = "SFX";

        // ====================================================================================================
        // 2. Menu - 배선
        // ====================================================================================================

        /// <summary>
        /// 믹서 그룹을 <see cref="AudioManager"/> 에 꽂습니다.
        ///
        /// 프로젝트의 믹서를 찾아 이름으로 그룹을 고릅니다. 경로가 아니라 이름으로 찾는 것은,
        /// 믹서를 어디에 두든(그리고 옮기든) 배선이 따라오게 하기 위해서입니다.
        ///
        /// <b>지금 열려 있는 씬만 봅니다.</b> 다른 씬을 몰래 열었다 닫으면
        /// 편집 중이던 것을 건드릴 위험이 있습니다.
        /// </summary>
        [MenuItem("SRPG/배선/⑬ 오디오 믹서 연결", priority = 42)]
        public static void WireAudioMixer()
        {
            var mixer = FindMixer();

            if (mixer == null)
            {
                Debug.LogWarning(
                    "[배선] 믹서를 찾지 못했습니다. Assets → Create → Audio Mixer 로 하나 만들고 " +
                    $"'{BgmGroupName}' 과 '{SfxGroupName}' 그룹을 두십시오.");

                return;
            }

            var bgm = FindGroup(mixer, BgmGroupName);
            var sfx = FindGroup(mixer, SfxGroupName);

            if (bgm == null || sfx == null)
            {
                Debug.LogWarning(
                    $"[배선] '{mixer.name}' 에 그룹이 모자랍니다. " +
                    $"{BgmGroupName}={(bgm != null ? "있음" : "없음")}, " +
                    $"{SfxGroupName}={(sfx != null ? "있음" : "없음")}");

                return;
            }

            int wired = 0;

            foreach (var manager in Object.FindObjectsByType<AudioManager>(FindObjectsInactive.Include))
            {
                var serialized = new SerializedObject(manager);

                bool changed = Assign(serialized, "_bgmGroup", bgm);
                changed |= Assign(serialized, "_sfxGroup", sfx);

                if (!changed)
                {
                    continue;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(manager);
                wired++;
            }

            if (wired > 0)
            {
                MarkScenesDirty();
            }

            Debug.Log(
                $"[배선] 오디오 믹서 — '{mixer.name}' 의 {BgmGroupName}·{SfxGroupName} 그룹을 " +
                $"AudioManager {wired}개에 연결했습니다.");
        }

        // ====================================================================================================
        // 3. Menu - 진단
        // ====================================================================================================

        /// <summary>
        /// 소리가 나지 않는 이유가 될 수 있는 것을 <b>전부</b> 한 번에 봅니다.
        ///
        /// <b>왜 한꺼번에 보는가</b>
        ///
        /// 아래 항목은 어느 하나만 걸려도 <b>완전한 무음</b>을 만듭니다. 그리고 그 무음은
        /// 서로 구별되지 않습니다. 하나씩 의심하면 그때마다 게임을 다시 켜야 하고,
        /// 둘이 동시에 걸려 있으면 하나를 고쳐도 아무 변화가 없어 엉뚱한 결론에 이릅니다.
        ///
        /// <b>에디터에만 있는 스위치가 섞여 있습니다.</b>
        /// Game 뷰의 음소거와 <c>AudioListener</c> 의 전역 음량·일시정지는 프로젝트가 아니라
        /// 에디터에 남습니다 — 저장소에는 흔적이 없고, 팀원 중 한 사람에게만 무음이 일어납니다.
        /// </summary>
        [MenuItem("SRPG/배선/⑭ 소리 진단 (변경 없음)", priority = 43)]
        public static void DiagnoseAudio()
        {
            var report = new StringBuilder();
            var faults = new List<string>();

            report.AppendLine("[진단] 소리");
            report.AppendLine();

            // --- 에디터 스위치 ---------------------------------------------------------------
            report.AppendLine("· 에디터 (프로젝트에 저장되지 않음)");

            bool muted = EditorUtility.audioMasterMute;
            report.AppendLine($"    Game 뷰 음소거    {(muted ? "켜짐 ← 이것만으로 완전 무음" : "꺼짐")}");

            if (muted)
            {
                faults.Add("Game 뷰의 음소거가 켜져 있습니다. Game 뷰 툴바의 스피커 아이콘을 끄십시오.");
            }

            report.AppendLine($"    전역 음량         {AudioListener.volume:F2}");

            if (AudioListener.volume < 0.01f)
            {
                faults.Add("AudioListener.volume 이 0입니다. 코드나 이전 실행이 낮춰 둔 값이 에디터에 남아 있습니다.");
            }

            if (AudioListener.pause)
            {
                faults.Add("AudioListener.pause 가 켜져 있습니다.");
            }

            report.AppendLine();

            // --- 프로젝트 설정 ---------------------------------------------------------------
            report.AppendLine("· 프로젝트 설정");

            var config = AudioSettings.GetConfiguration();
            report.AppendLine($"    출력 표본율       {config.sampleRate}Hz");
            report.AppendLine($"    실제 보이스 수    {config.numRealVoices}");
            report.AppendLine($"    스피커 구성       {config.speakerMode}");

            if (config.numRealVoices <= 0)
            {
                faults.Add("실제 보이스 수가 0입니다. Project Settings → Audio 를 확인하십시오.");
            }

            report.AppendLine();

            // --- 믹서 -------------------------------------------------------------------------
            report.AppendLine("· 믹서");

            var mixer = FindMixer();

            if (mixer == null)
            {
                report.AppendLine("    에셋              없음 (기본 출력으로 나갑니다 — 무음의 원인은 아닙니다)");
            }
            else
            {
                report.AppendLine($"    에셋              {AssetDatabase.GetAssetPath(mixer)}");

                foreach (string name in new[] { "Master", BgmGroupName, SfxGroupName })
                {
                    var group = FindGroup(mixer, name);
                    report.AppendLine($"    {name,-16}  {(group != null ? "있음" : "없음")}");
                }

                ReportMixerVolume(mixer, report, faults);
            }

            report.AppendLine();

            // --- 씬 ---------------------------------------------------------------------------
            report.AppendLine("· 열려 있는 씬");

            var managers = Object.FindObjectsByType<AudioManager>(FindObjectsInactive.Include);
            report.AppendLine($"    AudioManager      {managers.Length}개");

            if (managers.Length == 0)
            {
                faults.Add("씬에 AudioManager 가 없습니다. 소리를 낼 창구 자체가 없습니다.");
            }

            foreach (var manager in managers)
            {
                var serialized = new SerializedObject(manager);

                string bgm = Describe(serialized.FindProperty("_bgmGroup"));
                string sfx = Describe(serialized.FindProperty("_sfxGroup"));

                report.AppendLine($"      {manager.name}  활성={manager.isActiveAndEnabled}  BGM={bgm}  SFX={sfx}");

                if (!manager.gameObject.activeInHierarchy)
                {
                    faults.Add($"AudioManager '{manager.name}' 의 오브젝트가 꺼져 있습니다.");
                }
            }

            var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include);
            report.AppendLine($"    AudioListener     {listeners.Length}개");

            foreach (var listener in listeners)
            {
                report.AppendLine($"      {listener.name}  활성={listener.isActiveAndEnabled}");
            }

            if (listeners.Length == 0)
            {
                report.AppendLine("      (전투가 시작되면 리그가 초점에 하나 세웁니다)");
            }

            report.AppendLine();

            // --- 결론 -------------------------------------------------------------------------
            if (faults.Count == 0)
            {
                report.AppendLine("걸리는 것이 없습니다. 여기까지 정상이면 남은 것은 OS의 출력 장치입니다.");

                Debug.Log(report.ToString());

                return;
            }

            report.AppendLine($"걸리는 것 {faults.Count}가지:");

            for (int i = 0; i < faults.Count; i++)
            {
                report.AppendLine($"  {i + 1}. {faults[i]}");
            }

            Debug.LogWarning(report.ToString());
        }

        // ====================================================================================================
        // 4. Helpers
        // ====================================================================================================

        /// <summary>
        /// 프로젝트의 믹서를 찾습니다. 여럿이면 처음 것을 씁니다.
        /// </summary>
        /// <returns>찾은 믹서입니다. 없으면 null 입니다.</returns>
        private static AudioMixer FindMixer()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AudioMixer"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(path);

                if (mixer != null)
                {
                    return mixer;
                }
            }

            return null;
        }

        /// <summary>
        /// 이름으로 믹서 그룹을 찾습니다.
        /// </summary>
        /// <param name="mixer">뒤질 믹서입니다.</param>
        /// <param name="groupName">찾을 그룹 이름입니다.</param>
        /// <returns>찾은 그룹입니다. 없으면 null 입니다.</returns>
        private static AudioMixerGroup FindGroup(AudioMixer mixer, string groupName)
        {
            var found = mixer.FindMatchingGroups(groupName);

            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null && found[i].name == groupName)
                {
                    return found[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 그룹의 음량이 내려가 있는지 봅니다.
        ///
        /// 믹서를 새로 만들면 0dB 이 기본이라 보통 문제가 없지만, 한 번 내려 두면
        /// 다른 어떤 배선이 맞아도 소리가 나지 않습니다. <b>그리고 씬에는 드러나지 않습니다.</b>
        /// </summary>
        /// <param name="mixer">볼 믹서입니다.</param>
        /// <param name="report">적어 넣을 보고서입니다.</param>
        /// <param name="faults">걸리는 것을 모으는 목록입니다.</param>
        private static void ReportMixerVolume(AudioMixer mixer, StringBuilder report, List<string> faults)
        {
            // 노출된 파라미터가 있을 때만 읽을 수 있습니다. 없으면 조용히 넘어갑니다 —
            // 노출하지 않는 것이 정상이고, 그 자체는 결함이 아닙니다.
            foreach (string parameter in new[] { "MasterVolume", "BgmVolume", "SfxVolume" })
            {
                if (!mixer.GetFloat(parameter, out float decibels))
                {
                    continue;
                }

                report.AppendLine($"    {parameter,-16}  {decibels:F1}dB");

                if (decibels <= -79f)
                {
                    faults.Add($"믹서의 {parameter} 가 {decibels:F0}dB 로 사실상 무음입니다.");
                }
            }
        }

        /// <summary>
        /// 직렬화 필드에 값을 넣습니다. 이미 같으면 건드리지 않습니다.
        /// </summary>
        /// <param name="serialized">대상 객체입니다.</param>
        /// <param name="path">필드 이름입니다.</param>
        /// <param name="value">넣을 값입니다.</param>
        /// <returns>실제로 바꿨으면 true 입니다.</returns>
        private static bool Assign(SerializedObject serialized, string path, Object value)
        {
            var property = serialized.FindProperty(path);

            if (property == null || property.objectReferenceValue == value)
            {
                return false;
            }

            property.objectReferenceValue = value;

            return true;
        }

        /// <summary>
        /// 참조 필드를 사람이 읽을 수 있게 적습니다.
        /// </summary>
        /// <param name="property">읽을 필드입니다.</param>
        /// <returns>이름이거나 "비어 있음" 입니다.</returns>
        private static string Describe(SerializedProperty property)
        {
            if (property == null)
            {
                return "필드 없음";
            }

            return property.objectReferenceValue != null
                ? property.objectReferenceValue.name
                : "비어 있음";
        }

        /// <summary>
        /// 열려 있는 씬을 저장 대상으로 표시합니다.
        /// </summary>
        private static void MarkScenesDirty()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);

                if (scene.isLoaded)
                {
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                }
            }
        }
    }
}
