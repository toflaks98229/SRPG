using SRPG.Data;
using UnityEngine;

namespace SRPG.Composition
{
    /// <summary>
    /// 전투를 화면에 세울 때 필요한 씬 쪽 설정입니다.
    ///
    /// <b>왜 묶어서 넘기는가</b>
    ///
    /// 이것들은 전부 인스펙터에서 사람이 정하는 값이고, 받는 쪽은 진입점 하나뿐입니다.
    /// 하나씩 등록하면 <c>bool</c> 세 개가 컨테이너에 떠다니게 되는데,
    /// 그러면 다른 곳에서 <c>bool</c> 을 요구할 때 어느 것이 해석될지 알 수 없습니다.
    /// 원시 타입을 컨테이너에 그대로 올리면 안 되는 이유가 그것입니다.
    /// </summary>
    public sealed class BattleSceneSettings
    {
        /// <summary>전투 구성 에셋입니다. 없을 수 있습니다.</summary>
        public BattleSetup Setup { get; }

        /// <summary>생성물이 붙을 부모입니다. 절대 null이 아닙니다.</summary>
        public Transform RuntimeRoot { get; }

        /// <summary>씬에 미리 놓인 카메라입니다. 없으면 찾거나 만듭니다.</summary>
        public Camera Camera { get; }

        /// <summary>디버그 HUD를 띄울지 여부입니다.</summary>
        public bool ShowDebugHud { get; }

        /// <summary>AI 판단을 씬 뷰에 그릴지 여부입니다.</summary>
        public bool ShowAiOverlay { get; }

        /// <summary>카메라와 조명이 없을 때 만들어 줄지 여부입니다.</summary>
        public bool CreateCameraAndLight { get; }

        /// <param name="setup">전투 구성 에셋입니다.</param>
        /// <param name="runtimeRoot">생성물이 붙을 부모입니다.</param>
        /// <param name="camera">씬에 미리 놓인 카메라입니다.</param>
        /// <param name="showDebugHud">디버그 HUD를 띄울지 여부입니다.</param>
        /// <param name="showAiOverlay">AI 판단을 그릴지 여부입니다.</param>
        /// <param name="createCameraAndLight">카메라와 조명을 만들어 줄지 여부입니다.</param>
        public BattleSceneSettings(
            BattleSetup setup,
            Transform runtimeRoot,
            Camera camera,
            bool showDebugHud,
            bool showAiOverlay,
            bool createCameraAndLight)
        {
            Setup = setup;
            RuntimeRoot = runtimeRoot;
            Camera = camera;
            ShowDebugHud = showDebugHud;
            ShowAiOverlay = showAiOverlay;
            CreateCameraAndLight = createCameraAndLight;
        }
    }
}
