using System.Collections;
using NUnit.Framework;
using SRPG.Composition;
using SRPG.Gameplay.Island;
using SRPG.Gameplay.Visual;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SRPG.Tests.PlayMode
{
    /// <summary>
    /// 실제 전투 씬이 <b>연결된 에셋으로</b> 뜨는지 확인합니다.
    ///
    /// <b>왜 따로 필요한가</b>
    ///
    /// 이 프로젝트에는 두 갈래의 실행 경로가 있습니다.
    ///   · <b>에셋 경로</b> — 프리팹과 머티리얼이 연결되어 있으면 그것을 씁니다.
    ///   · <b>폴백 경로</b> — 비어 있으면 코드가 프리미티브로 만듭니다.
    ///
    /// 기존 스모크 테스트는 부트스트랩을 맨손으로 만들어 <b>폴백 경로만</b> 지나갑니다.
    /// 그래서 새 시각 시스템을 폴백에만 붙여 놓아도 전부 통과했습니다.
    /// 실제로 그런 상태였습니다 — 지형 셰이더도 빌보드도 실제 게임에서는 한 번도 안 나왔습니다.
    ///
    /// 여기서는 씬을 진짜로 로드합니다. 에셋이 어긋나면 여기서 걸립니다.
    /// </summary>
    public sealed class BattleSceneWiringTests
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        private const string ScenePath = "Assets/_Project/01.Scenes/Battle/Battle.unity";

        /// <summary>병력이 생성될 때까지 기다릴 최대 시간입니다.</summary>
        private const float SpawnTimeout = 10f;

        // ====================================================================================================
        // 2. Setup
        // ====================================================================================================

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Time.timeScale = 1f;
            UnityEngine.Time.fixedDeltaTime = 0.02f;
        }

        /// <summary>
        /// 전투 씬을 로드하고 병력이 생길 때까지 기다립니다.
        /// </summary>
        private static IEnumerator LoadBattleScene()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);

            // 부트스트랩은 Start에서 조립합니다. 한 프레임 넘겨야 결과를 볼 수 있습니다.
            yield return null;

            var bootstrap = Object.FindFirstObjectByType<BattleBootstrap>();
            Assert.IsNotNull(bootstrap, "씬에 BattleBootstrap 이 없습니다.");
            Assert.IsNotNull(bootstrap.Setup, "씬의 부트스트랩에 전투 구성 에셋이 연결되어 있지 않습니다.");

            float elapsed = 0f;

            while (elapsed < SpawnTimeout && Object.FindFirstObjectByType<UnitBillboard>() == null)
            {
                elapsed += UnityEngine.Time.deltaTime;
                yield return null;
            }
        }

        // ====================================================================================================
        // 3. Tests
        // ====================================================================================================

        /// <summary>
        /// 씬이 그냥 뜨는지부터 봅니다. 여기가 깨지면 아래 검사는 볼 것도 없습니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 전투_씬이_연결된_에셋으로_뜬다()
        {
            yield return LoadBattleScene();

            var bootstrap = Object.FindFirstObjectByType<BattleBootstrap>();

            Assert.IsNotNull(bootstrap.Context, "전투 컨텍스트가 만들어지지 않았습니다.");
            Assert.Greater(bootstrap.Context.PlayerUnits.Count, 0, "플레이어 병력이 없습니다.");
        }

        /// <summary>
        /// 튜닝 에셋이 실제로 쓰이는지 봅니다.
        ///
        /// 비어 있어도 코드 기본값으로 조용히 굴러갑니다. 그래서 아무도 모릅니다.
        /// 기획자가 인스펙터에서 만질 대상이 존재하지 않는 상태가 됩니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 튜닝_수치가_코드_기본값이_아니라_에셋에서_온다()
        {
            yield return LoadBattleScene();

            var bootstrap = Object.FindFirstObjectByType<BattleBootstrap>();

            Assert.IsNotNull(bootstrap.Setup.Tuning, "BattleSetup.Tuning 이 비어 있습니다.");
            Assert.AreSame(
                bootstrap.Setup.Tuning,
                bootstrap.Context.Tuning,
                "연결된 튜닝 에셋이 있는데 전투가 다른 것을 쓰고 있습니다.");
        }

        /// <summary>
        /// 지형이 SRPG 셰이더로 그려지는지 봅니다.
        ///
        /// 연결된 머티리얼이 URP/Lit이면 외곽선도 정점 컬러 접지 음영도 나오지 않습니다.
        /// 메시에 접지 음영을 써 넣는 코드는 계속 도는데 읽는 쪽이 없는 상태가 됩니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 지형이_SRPG_지형_셰이더로_그려진다()
        {
            yield return LoadBattleScene();

            var island = Object.FindFirstObjectByType<IslandView>();
            Assert.IsNotNull(island, "섬이 만들어지지 않았습니다.");

            var renderers = island.GetComponentsInChildren<MeshRenderer>();
            Assert.Greater(renderers.Length, 0, "섬에 렌더러가 없습니다.");

            int terrainCount = 0;

            for (int i = 0; i < renderers.Length; i++)
            {
                var material = renderers[i].sharedMaterial;

                if (material != null && material.shader != null &&
                    material.shader.name == PrototypeVisuals.TerrainShaderName)
                {
                    terrainCount++;
                }
            }

            Assert.Greater(
                terrainCount,
                0,
                $"지형 {renderers.Length}개 중 SRPG/Terrain 을 쓰는 것이 하나도 없습니다. " +
                "외곽선과 접지 음영이 나오지 않습니다.");
        }

        /// <summary>
        /// 유닛이 빌보드로 나오는지 봅니다.
        ///
        /// <see cref="SRPG.Data.UnitDefinition.Prefab"/>이 연결되어 있으면 부트스트랩은 프리팹을 씁니다.
        /// 빌보드를 만드는 코드는 프리팹이 <b>없을 때만</b> 도는 폴백이라, 프리팹에 직접 붙어 있지 않으면
        /// 실제 게임에서는 한 번도 실행되지 않습니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 유닛이_빌보드로_그려진다()
        {
            yield return LoadBattleScene();

            var billboard = Object.FindFirstObjectByType<UnitBillboard>();

            Assert.IsNotNull(
                billboard,
                "빌보드 유닛이 하나도 없습니다. 유닛 프리팹이 아직 캡슐입니다.");

            var body = billboard.transform.Find("Body");
            Assert.IsNotNull(body, "빌보드 유닛에 Body 오브젝트가 없습니다.");

            var renderer = body.GetComponent<MeshRenderer>();
            Assert.IsNotNull(renderer, "Body 에 렌더러가 없습니다.");

            Assert.AreEqual(
                PrototypeVisuals.BillboardShaderName,
                renderer.sharedMaterial.shader.name,
                "유닛 몸체가 빌보드 셰이더를 쓰지 않습니다.");
        }

        /// <summary>
        /// 접지 그림자가 붙는지, 그리고 유닛을 따라다니는지 봅니다.
        ///
        /// 빌보드는 평면이라 지면과 닿는 면이 없습니다.
        /// 그림자가 없으면 카메라를 돌릴 때 스프라이트가 배경 위에 떠 있는 것처럼 보입니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 유닛마다_접지_그림자가_붙는다()
        {
            yield return LoadBattleScene();

            var bootstrap = Object.FindFirstObjectByType<BattleBootstrap>();
            var shadows = Object.FindObjectsByType<ContactShadow>(FindObjectsSortMode.None);

            Assert.Greater(shadows.Length, 0, "접지 그림자가 하나도 없습니다.");

            Assert.GreaterOrEqual(
                shadows.Length,
                bootstrap.Context.PlayerUnits.Count,
                "그림자 수가 유닛 수보다 적습니다. 일부 유닛이 그림자 없이 떠 있습니다.");
        }

        /// <summary>
        /// 그림자가 유닛의 발밑에 있는지 봅니다.
        ///
        /// 붙어 있기만 하고 딴 데 있으면 접지가 아니라 노이즈입니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 접지_그림자가_유닛의_발밑에_있다()
        {
            yield return LoadBattleScene();

            var bootstrap = Object.FindFirstObjectByType<BattleBootstrap>();
            Assert.Greater(bootstrap.Context.PlayerUnits.Count, 0, "유닛이 없습니다.");

            var unit = bootstrap.Context.PlayerUnits[0];
            var shadows = Object.FindObjectsByType<ContactShadow>(FindObjectsSortMode.None);

            float nearest = float.MaxValue;

            for (int i = 0; i < shadows.Length; i++)
            {
                Vector3 delta = shadows[i].transform.position - unit.transform.position;
                delta.y = 0f;

                nearest = Mathf.Min(nearest, delta.magnitude);
            }

            Assert.Less(nearest, 0.05f, $"가장 가까운 그림자가 유닛에서 {nearest:F2}만큼 떨어져 있습니다.");
        }
    }
}
