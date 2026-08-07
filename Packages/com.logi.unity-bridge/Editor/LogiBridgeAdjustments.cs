// Logi Unity Bridge - 다이얼(회전 노브) 처리.
//
// Loupedeck 은 다이얼을 돌린 만큼의 '틱 수'(diff)를 보낸다. 여기서 틱을 실제 값 변화로 환산한다.
// 모든 함수는 LogiBridgeClient 의 메인 스레드 큐에서 불린다. 에디터 API를 직접 써도 안전하다.

namespace Logi.UnityBridge
{
    using System;
    using System.Globalization;

    using UnityEditor;

    using UnityEngine;

    internal static class LogiBridgeAdjustments
    {
        // 틱당 변화량. 다이얼 한 칸이 너무 크면 미세 조정이 불가능하고, 너무 작으면 쓸모가 없다.
        private const Single PositionStep = 0.1f;
        private const Single RotationStep = 1f;
        private const Single ScaleStep = 0.05f;
        private const Single TimeScaleStep = 0.05f;
        private const Single ZoomStep = 0.05f;

        public static void Apply(String target, Int32 diff)
        {
            if (String.IsNullOrEmpty(target) || diff == 0)
            {
                return;
            }

            switch (target)
            {
                case "move.x":
                    Translate(new Vector3(diff * PositionStep, 0f, 0f));
                    break;

                case "move.y":
                    Translate(new Vector3(0f, diff * PositionStep, 0f));
                    break;

                case "move.z":
                    Translate(new Vector3(0f, 0f, diff * PositionStep));
                    break;

                case "rotate.x":
                    Rotate(new Vector3(diff * RotationStep, 0f, 0f));
                    break;

                case "rotate.y":
                    Rotate(new Vector3(0f, diff * RotationStep, 0f));
                    break;

                case "rotate.z":
                    Rotate(new Vector3(0f, 0f, diff * RotationStep));
                    break;

                case "scale.uniform":
                    ScaleUniform(diff * ScaleStep);
                    break;

                case "time.scale":
                    // 0 이면 완전히 멈춘다. 음수는 Unity 가 허용하지 않는다.
                    Time.timeScale = Mathf.Clamp(Time.timeScale + (diff * TimeScaleStep), 0f, 10f);
                    break;

                case "scene.zoom":
                    Zoom(diff);
                    break;

                default:
                    Debug.LogWarning($"[LogiBridge] unknown adjustment target '{target}'");
                    break;
            }
        }

        public static void Reset(String target)
        {
            switch (target)
            {
                case "time.scale":
                    Time.timeScale = 1f;
                    break;

                case "scene.zoom":
                    if (SceneView.lastActiveSceneView != null)
                    {
                        SceneView.lastActiveSceneView.FrameSelected();
                    }

                    break;

                case "move.x":
                case "move.y":
                case "move.z":
                    SetLocalPosition(target, 0f);
                    break;

                case "rotate.x":
                case "rotate.y":
                case "rotate.z":
                    SetLocalEuler(target, 0f);
                    break;

                case "scale.uniform":
                    RecordAndApply("Reset Scale", t => t.localScale = Vector3.one);
                    break;

                default:
                    Debug.LogWarning($"[LogiBridge] unknown reset target '{target}'");
                    break;
            }
        }

        // 다이얼 옆에 표시할 현재 값. 선택된 오브젝트가 없으면 값이 없다.
        public static String BuildValueMessage(String target)
        {
            var text = Describe(target);
            var escaped = text == null ? "null" : $"\"{text}\"";
            return $"{{\"cmd\":\"value\",\"target\":\"{target}\",\"text\":{escaped}}}";
        }

        private static String Describe(String target)
        {
            if (target == "time.scale")
            {
                return Time.timeScale.ToString("0.00", CultureInfo.InvariantCulture);
            }

            if (target == "scene.zoom")
            {
                return SceneView.lastActiveSceneView == null
                    ? null
                    : SceneView.lastActiveSceneView.size.ToString("0.0", CultureInfo.InvariantCulture);
            }

            var active = Selection.activeTransform;
            if (active == null)
            {
                return null;
            }

            switch (target)
            {
                case "move.x": return Format(active.localPosition.x);
                case "move.y": return Format(active.localPosition.y);
                case "move.z": return Format(active.localPosition.z);
                case "rotate.x": return Format(active.localEulerAngles.x);
                case "rotate.y": return Format(active.localEulerAngles.y);
                case "rotate.z": return Format(active.localEulerAngles.z);
                case "scale.uniform": return Format(active.localScale.x);
                default: return null;
            }
        }

        private static String Format(Single value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        private static void Translate(Vector3 delta) =>
            RecordAndApply("Move (Logi Dial)", t => t.localPosition += delta);

        private static void Rotate(Vector3 euler) =>
            RecordAndApply("Rotate (Logi Dial)", t => t.localEulerAngles += euler);

        private static void ScaleUniform(Single delta) =>
            RecordAndApply("Scale (Logi Dial)", t => t.localScale = Vector3.Max(t.localScale + (Vector3.one * delta), Vector3.zero));

        private static void SetLocalPosition(String target, Single value) =>
            RecordAndApply("Reset Position", t =>
            {
                var p = t.localPosition;
                if (target == "move.x") { p.x = value; }
                else if (target == "move.y") { p.y = value; }
                else { p.z = value; }

                t.localPosition = p;
            });

        private static void SetLocalEuler(String target, Single value) =>
            RecordAndApply("Reset Rotation", t =>
            {
                var e = t.localEulerAngles;
                if (target == "rotate.x") { e.x = value; }
                else if (target == "rotate.y") { e.y = value; }
                else { e.z = value; }

                t.localEulerAngles = e;
            });

        // 선택된 모든 트랜스폼에 적용한다. Undo.RecordObjects 를 먼저 불러야 Ctrl+Z 로 되돌릴 수 있고,
        // 씬이 더티로 표시되어 저장 대상이 된다.
        private static void RecordAndApply(String undoLabel, Action<Transform> mutate)
        {
            var transforms = Selection.transforms;
            if (transforms == null || transforms.Length == 0)
            {
                return;
            }

            Undo.RecordObjects(transforms, undoLabel);

            foreach (var t in transforms)
            {
                mutate(t);
            }
        }

        private static void Zoom(Int32 diff)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null)
            {
                return;
            }

            // 곱셈으로 줌해야 멀리서든 가까이서든 체감 속도가 일정하다.
            view.size = Mathf.Clamp(view.size * Mathf.Pow(1f - ZoomStep, diff), 0.01f, 10000f);
            view.Repaint();
        }
    }
}
