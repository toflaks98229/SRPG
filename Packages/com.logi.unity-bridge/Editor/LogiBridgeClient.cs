// Logi Unity Bridge - Unity 에디터 측 컴패니언.
//
// 이 파일은 Loupedeck 플러그인 어셈블리에 embedded resource로만 들어간다. 플러그인과 함께 컴파일되지 않는다.
// (UnityEditor를 참조하므로 컴파일되면 플러그인 빌드가 깨진다.)
//
// 플러그인이 서버, Unity가 클라이언트다. 도메인 리로드가 일어날 때마다 이 클래스는 통째로 사라졌다가
// [InitializeOnLoad]로 다시 살아나며 재접속한다.

namespace Logi.UnityBridge
{
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;

    using UnityEditor;
    using UnityEditor.SceneManagement;

    using UnityEngine;

    [InitializeOnLoad]
    internal static class LogiBridgeClient
    {
        private const Double ReconnectIntervalSeconds = 2.0;

        private static readonly ConcurrentQueue<String> InboundCommands = new ConcurrentQueue<String>();

        private static TcpClient _client;
        private static StreamWriter _writer;
        private static Thread _readThread;
        private static Double _nextConnectAttempt;

        // 마지막으로 플러그인에 보고한 에디터 상태. 바뀔 때만 보낸다.
        private static Boolean _reportedPlaying;
        private static Boolean _reportedCompiling;
        private static Boolean _hasReportedState;

        static LogiBridgeClient()
        {
            EditorApplication.update += Update;

            // 도메인 리로드 직전에 소켓을 정리한다. 이걸 빼면 서버가 죽은 연결을 붙들고 있게 된다.
            AssemblyReloadEvents.beforeAssemblyReload += Disconnect;
            EditorApplication.quitting += Disconnect;
        }

        private static String HandshakeFilePath
        {
            get
            {
#if UNITY_EDITOR_OSX
                var home = Environment.GetEnvironmentVariable("HOME");
                var applicationData = String.IsNullOrEmpty(home)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                    : Path.Combine(home, "Library", "Application Support");
#else
                var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#endif
                return Path.Combine(applicationData, "Logi", "LogiForUnity", "bridge.json");
            }
        }

        private static Boolean IsConnected => _client != null && _client.Connected;

        // EditorApplication.update는 항상 메인 스레드다. 에디터 API 호출은 전부 여기를 통과해야 한다.
        private static void Update()
        {
            while (InboundCommands.TryDequeue(out var line))
            {
                try
                {
                    Dispatch(line);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[LogiBridge] command '{line}' failed: {ex}");
                }
            }

            if (!IsConnected && EditorApplication.timeSinceStartup >= _nextConnectAttempt)
            {
                _nextConnectAttempt = EditorApplication.timeSinceStartup + ReconnectIntervalSeconds;
                TryConnect();
                return;
            }

            ReportStateIfChanged();
        }

        // 플러그인은 이 보고를 보고 패키지 재설치를 미룬다. 플레이 중이거나 컴파일 중에 파일을 쓰면
        // 에디터가 스크립트를 다시 컴파일하면서 플레이 모드가 튕기기 때문이다.
        private static void ReportStateIfChanged()
        {
            if (!IsConnected)
            {
                return;
            }

            var playing = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
            var compiling = EditorApplication.isCompiling || EditorApplication.isUpdating;

            if (_hasReportedState && playing == _reportedPlaying && compiling == _reportedCompiling)
            {
                return;
            }

            _reportedPlaying = playing;
            _reportedCompiling = compiling;
            _hasReportedState = true;

            Send($"{{\"cmd\":\"state\",\"playing\":{(playing ? "true" : "false")},\"compiling\":{(compiling ? "true" : "false")}}}");
        }

        private static void TryConnect()
        {
            var path = HandshakeFilePath;
            if (!File.Exists(path))
            {
                // 플러그인이 아직 안 떴다. 조용히 재시도한다.
                return;
            }

            Handshake handshake;
            try
            {
                handshake = JsonUtility.FromJson<Handshake>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LogiBridge] unreadable handshake file: {ex.Message}");
                return;
            }

            if (handshake == null || handshake.port <= 0)
            {
                return;
            }

            try
            {
                var client = new TcpClient { NoDelay = true };
                client.Connect("127.0.0.1", handshake.port);

                var stream = client.GetStream();
                var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                var reader = new StreamReader(stream, new UTF8Encoding(false));

                var project = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');
                writer.WriteLine($"{{\"token\":\"{handshake.token}\",\"unity\":\"{Application.unityVersion}\",\"project\":\"{project}\"}}");

                _client = client;
                _writer = writer;

                // 새 연결에는 현재 상태를 처음부터 다시 알려야 한다. 플러그인이 재시작됐을 수도 있다.
                _hasReportedState = false;

                _readThread = new Thread(() => ReadLoop(client, reader)) { IsBackground = true, Name = "LogiBridgeRead" };
                _readThread.Start();

                Debug.Log($"[LogiBridge] connected to Logi plugin on port {handshake.port}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LogiBridge] connect failed: {ex.Message}");
                Disconnect();
            }
        }

        // 백그라운드 스레드. 여기서는 절대 에디터 API를 건드리지 않는다. 큐에만 넣는다.
        private static void ReadLoop(TcpClient client, StreamReader reader)
        {
            try
            {
                String line;
                while ((line = reader.ReadLine()) != null)
                {
                    InboundCommands.Enqueue(line);
                }
            }
            catch (Exception)
            {
                // 연결이 끊기면 Update()가 재접속을 맡는다.
            }
            finally
            {
                try { client.Close(); } catch { }
            }
        }

        private static void Disconnect()
        {
            try { _writer?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
            _writer = null;
            _client = null;
        }

        private static void Dispatch(String line)
        {
            var message = JsonUtility.FromJson<Command>(line);
            if (message == null || String.IsNullOrEmpty(message.cmd))
            {
                return;
            }

            switch (message.cmd)
            {
                case "welcome":
                    break;

                case "ping":
                    Send("{\"cmd\":\"pong\"}");
                    break;

                // --- 플레이 모드 ---
                case "play":
                    EditorApplication.isPlaying = !EditorApplication.isPlaying;
                    break;

                case "pause":
                    EditorApplication.isPaused = !EditorApplication.isPaused;
                    break;

                case "step":
                    EditorApplication.Step();
                    break;

                // --- 트랜스폼 툴 ---
                // Tools.current 를 직접 세팅하므로 사용자의 Shortcut Manager 설정과 무관하다.
                case "tool.hand":
                    Tools.current = Tool.View;
                    break;

                case "tool.move":
                    Tools.current = Tool.Move;
                    break;

                case "tool.rotate":
                    Tools.current = Tool.Rotate;
                    break;

                case "tool.scale":
                    Tools.current = Tool.Scale;
                    break;

                case "tool.rect":
                    Tools.current = Tool.Rect;
                    break;

                case "tool.transform":
                    Tools.current = Tool.Transform;
                    break;

                // --- 기즈모 기준 ---
                case "pivot.toggle":
                    Tools.pivotMode = Tools.pivotMode == PivotMode.Pivot ? PivotMode.Center : PivotMode.Pivot;
                    break;

                case "space.toggle":
                    Tools.pivotRotation = Tools.pivotRotation == PivotRotation.Global ? PivotRotation.Local : PivotRotation.Global;
                    break;

                // --- 씬 뷰 ---
                case "frame":
                    // 씬 뷰가 하나도 열려 있지 않으면 lastActiveSceneView 가 null 이다.
                    if (SceneView.lastActiveSceneView != null)
                    {
                        SceneView.lastActiveSceneView.FrameSelected();
                    }

                    break;

                // --- 편집 ---
                case "undo":
                    Undo.PerformUndo();
                    break;

                case "redo":
                    Undo.PerformRedo();
                    break;

                case "duplicate":
                    EditorApplication.ExecuteMenuItem("Edit/Duplicate");
                    break;

                case "gameobject.empty":
                    EditorApplication.ExecuteMenuItem("GameObject/Create Empty");
                    break;

                // --- 파일 ---
                case "save":
                    EditorSceneManager.SaveOpenScenes();
                    break;

                case "build.settings":
                    EditorApplication.ExecuteMenuItem("File/Build Settings...");
                    break;

                // --- 에디터 창 ---
                // 창 열기는 메뉴 경로에 의존한다. 경로는 Unity 버전에 따라 달라질 수 있어 실패하면 경고를 남긴다.
                case "window.scene":
                    OpenWindow("Window/General/Scene");
                    break;

                case "window.game":
                    OpenWindow("Window/General/Game");
                    break;

                case "window.inspector":
                    OpenWindow("Window/General/Inspector");
                    break;

                case "window.hierarchy":
                    OpenWindow("Window/General/Hierarchy");
                    break;

                case "window.project":
                    OpenWindow("Window/General/Project");
                    break;

                case "window.console":
                    OpenWindow("Window/General/Console");
                    break;

                // --- 다이얼 ---
                case "adjust":
                    LogiBridgeAdjustments.Apply(message.target, message.diff);
                    Send(LogiBridgeAdjustments.BuildValueMessage(message.target));
                    break;

                case "reset":
                    LogiBridgeAdjustments.Reset(message.target);
                    Send(LogiBridgeAdjustments.BuildValueMessage(message.target));
                    break;

                case "value":
                    Send(LogiBridgeAdjustments.BuildValueMessage(message.target));
                    break;

                default:
                    Debug.LogWarning($"[LogiBridge] unknown command '{message.cmd}'");
                    break;
            }
        }

        private static void OpenWindow(String menuPath)
        {
            if (!EditorApplication.ExecuteMenuItem(menuPath))
            {
                Debug.LogWarning($"[LogiBridge] menu item '{menuPath}' not found in Unity {Application.unityVersion}");
            }
        }

        private static void Send(String json)
        {
            try
            {
                _writer?.WriteLine(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LogiBridge] send failed: {ex.Message}");
                Disconnect();
            }
        }

        // JsonUtility 가 리플렉션으로 채우므로 컴파일러는 대입을 보지 못한다.
#pragma warning disable 0649
        [Serializable]
        private sealed class Handshake
        {
            public Int32 port;
            public String token;
        }

        [Serializable]
        private sealed class Command
        {
            public String cmd;
            public String target;
            public Int32 diff;
        }
#pragma warning restore 0649
    }
}
