using System.Collections;
using NUnit.Framework;
using SRPG.Composition;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SRPG.Tests.PlayMode
{
    /// <summary>
    /// 실제 전투 씬의 배선을 확인합니다.
    ///
    /// <b>지금은 맵 생성이 없습니다</b>
    ///
    /// 절차적 지형 생성을 걷어내면서 씬의 부트스트랩에는 섬을 공급할 것이 없습니다.
    /// 그래서 씬을 열어도 전투가 조립되지 않습니다. 그건 <b>지금의 정상 상태</b>입니다.
    ///
    /// 예전에는 여기서 지형 셰이더·빌보드·접지 그림자·지형지물이 실제로 나오는지를
    /// 봤지만, 그 전부가 섬 하나를 전제로 하고 있었습니다.
    /// 새 생성기가 붙으면 그 검사들을 git 이력에서 되살려야 합니다.
    ///
    /// 그때까지 여기서 지킬 수 있는 것은 둘입니다.
    ///   · 씬 자체는 멀쩡히 열리고 부트스트랩과 구성 에셋이 연결되어 있는가
    ///   · 맵이 없다는 사실을 <b>조용히</b> 넘기지 않고 분명히 알리는가
    ///
    /// 둘째가 중요합니다. 맵이 없어 아무것도 안 만들어졌는데 오류도 없으면,
    /// 나중에 그 상태를 "왜 화면이 비었지"로 다시 헤매게 됩니다.
    /// </summary>
    public sealed class BattleSceneWiringTests
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        private const string ScenePath = "Assets/_Project/01.Scenes/Battle/Battle.unity";

        // ====================================================================================================
        // 2. Tests
        // ====================================================================================================

        /// <summary>
        /// 씬이 열리고 조립 지점과 구성 에셋이 연결되어 있는지 봅니다.
        ///
        /// 맵이 없어도 이 배선은 살아 있어야 합니다. 새 생성기를 꽂을 자리가 여기이기 때문입니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 씬에_부트스트랩과_구성_에셋이_연결되어_있다()
        {
            // 맵이 없다는 오류는 지금의 정상 상태입니다. 이 검사가 보려는 것은 그게 아닙니다.
            LogAssert.ignoreFailingMessages = true;

            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            var bootstrap = Object.FindFirstObjectByType<BattleBootstrap>();

            Assert.IsNotNull(bootstrap, "씬에 BattleBootstrap 이 없습니다.");
            Assert.IsNotNull(bootstrap.Setup, "씬의 부트스트랩에 전투 구성 에셋이 연결되어 있지 않습니다.");
            Assert.IsNotNull(bootstrap.Setup.Tuning, "BattleSetup.Tuning 이 비어 있습니다. 수치가 코드 기본값으로 돌아갑니다.");

            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// 맵이 없다는 것을 <b>분명히 알리는지</b> 봅니다.
        ///
        /// 조용히 아무것도 안 만들면 화면이 빈 이유를 나중에 다시 찾아야 합니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 맵_공급자가_없으면_전투를_시작하지_않고_알린다()
        {
            LogAssert.ignoreFailingMessages = true;

            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            yield return null;

            var bootstrap = Object.FindFirstObjectByType<BattleBootstrap>();

            Assert.IsNull(
                bootstrap.IslandSource,
                "씬의 부트스트랩에 맵 공급자가 연결되어 있습니다. 이 검사를 갱신하세요.");

            Assert.IsNull(
                bootstrap.Context,
                "맵이 없는데 전투 컨텍스트가 만들어졌습니다. 조립이 절반만 진행된 상태입니다.");

            LogAssert.ignoreFailingMessages = false;
        }
    }
}
