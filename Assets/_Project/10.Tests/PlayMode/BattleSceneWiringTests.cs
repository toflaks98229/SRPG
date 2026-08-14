using System.Collections;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Composition;
using SRPG.Gameplay.Squads;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SRPG.Tests.PlayMode
{
    /// <summary>
    /// 실제 전투 씬의 배선을 확인합니다.
    ///
    /// <b>왜 이 검사가 따로 필요한가</b>
    ///
    /// 이 프로젝트에서 같은 종류의 결함이 반복해서 나왔습니다 — 코드는 맞는데
    /// <b>씬이 그 코드를 타지 않는</b> 상태입니다. 생성기가 멀쩡히 돌아가고
    /// 단위 검사가 전부 통과해도, 부트스트랩이 그것을 부르지 않으면 화면은 비어 있습니다.
    ///
    /// 그래서 여기서는 씬을 실제로 열고 <b>결과물이 씬에 있는지</b>를 봅니다.
    /// 생성기가 옳은가는 EditMode 가 봅니다. 여기서 보는 것은 연결입니다.
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
        /// </summary>
        [UnityTest]
        public IEnumerator 씬에_부트스트랩과_구성_에셋이_연결되어_있다()
        {
            yield return LoadBattleScene();

            var bootstrap = Object.FindFirstObjectByType<BattleBootstrap>();

            Assert.IsNotNull(bootstrap, "씬에 BattleBootstrap 이 없습니다.");
            Assert.IsNotNull(bootstrap.Setup, "씬의 부트스트랩에 전투 구성 에셋이 연결되어 있지 않습니다.");
            Assert.IsNotNull(bootstrap.Setup.Tuning, "BattleSetup.Tuning 이 비어 있습니다. 수치가 코드 기본값으로 돌아갑니다.");
        }

        /// <summary>
        /// 씬을 열면 전투가 실제로 조립되는지 봅니다.
        ///
        /// 컨텍스트가 있다는 것은 지형과 시간과 수치가 모두 자리를 잡았다는 뜻입니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 씬을_열면_전투가_조립된다()
        {
            yield return LoadBattleScene();

            var bootstrap = Object.FindFirstObjectByType<BattleBootstrap>();

            Assert.IsNotNull(bootstrap.Context, "전투 컨텍스트가 만들어지지 않았습니다. 조립이 도중에 멈췄습니다.");
            Assert.IsNotNull(bootstrap.Battlefield, "전장이 만들어지지 않았습니다.");
            Assert.Greater(bootstrap.Context.Grid.WalkableTiles.Count, 0, "걸을 수 있는 땅이 없습니다.");
        }

        /// <summary>
        /// 지형이 <b>유니티 터레인</b>으로 서 있는지 봅니다.
        ///
        /// <b>왜 종류까지 보는가</b>
        ///
        /// 예전에는 타일마다 사각 발판을 구워 지형을 만들었고, 그것이 격자로 보인다는
        /// 문제가 끝내 해결되지 않아 방식을 바꿨습니다. 이 검사는 그 결정이
        /// 씬에서 지켜지고 있는지를 지킵니다 — 지면이 다시 메시로 돌아오면 여기서 걸립니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 지면이_유니티_터레인으로_서_있다()
        {
            yield return LoadBattleScene();

            var terrain = Object.FindFirstObjectByType<Terrain>();

            Assert.IsNotNull(terrain, "씬에 터레인이 없습니다. 지면이 만들어지지 않았거나 다시 메시로 돌아갔습니다.");
            Assert.IsNotNull(terrain.terrainData, "터레인에 지형 데이터가 없습니다.");
            Assert.Greater(terrain.terrainData.heightmapResolution, 1, "하이트맵이 비어 있습니다.");
        }

        /// <summary>
        /// 지면을 클릭할 수 있는지 봅니다.
        ///
        /// 이동 명령은 지면 레이캐스트로 나갑니다. 콜라이더가 없거나 레이어가 틀리면
        /// 화면은 멀쩡한데 <b>부대가 움직이지 않습니다</b>.
        /// </summary>
        [UnityTest]
        public IEnumerator 지면을_클릭할_수_있다()
        {
            yield return LoadBattleScene();

            var terrain = Object.FindFirstObjectByType<Terrain>();

            Assert.IsNotNull(terrain.GetComponent<TerrainCollider>(), "터레인에 콜라이더가 없어 클릭이 지면을 잡지 못합니다.");

            Assert.AreEqual(
                GameLayers.Terrain,
                terrain.gameObject.layer,
                "터레인이 지형 레이어에 있지 않아 이동 명령 레이캐스트가 빗나갑니다.");
        }

        /// <summary>
        /// 주문서에 적힌 분대가 실제로 전장에 섰는지 봅니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 주문서의_분대가_전장에_선다()
        {
            yield return LoadBattleScene();

            var squads = Object.FindObjectsByType<Squad>(FindObjectsSortMode.None);

            Assert.Greater(squads.Length, 0, "전장에 선 아군 분대가 없습니다.");
        }

        // ====================================================================================================
        // 3. Helpers
        // ====================================================================================================

        /// <summary>
        /// 전투 씬을 열고 조립이 끝날 때까지 기다립니다.
        ///
        /// <see cref="BattleBootstrap"/> 은 <c>Start</c> 에서 조립하므로 한 프레임으로는 모자랍니다.
        /// </summary>
        private static IEnumerator LoadBattleScene()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);

            yield return null;
            yield return null;
        }
    }
}
