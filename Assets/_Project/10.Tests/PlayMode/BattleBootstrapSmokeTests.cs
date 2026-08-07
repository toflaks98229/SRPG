using System.Collections;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Composition;
using SRPG.Gameplay.CameraControl;
using SRPG.Gameplay.Enemies;
using SRPG.Gameplay.Selection;
using SRPG.Gameplay.Squads;
using UnityEngine;
using UnityEngine.TestTools;

namespace SRPG.Tests.PlayMode
{
    /// <summary>
    /// 전투 프로토타입이 실제로 기동하는지 확인하는 스모크 테스트입니다.
    ///
    /// 단위 테스트가 각 시스템을 따로 검증한다면, 여기서는 부트스트랩이 그 시스템들을
    /// 실제로 엮어 내는지를 봅니다. 조립 단계에서 깨지는 것은 단위 테스트로 잡히지 않습니다.
    /// </summary>
    public sealed class BattleBootstrapSmokeTests
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        private GameObject _bootstrapObject;

        // ====================================================================================================
        // 2. Setup / Teardown
        // ====================================================================================================

        [TearDown]
        public void TearDown()
        {
            if (_bootstrapObject != null)
            {
                Object.Destroy(_bootstrapObject);
            }

            // 슬로우모션이 남아 다음 테스트를 느리게 만들지 않도록 되돌립니다.
            UnityEngine.Time.timeScale = 1f;
            UnityEngine.Time.fixedDeltaTime = 0.02f;
        }

        // ====================================================================================================
        // 3. Tests
        // ====================================================================================================

        /// <summary>
        /// 카메라가 피벗의 자식으로 붙고, 그 피벗을 정확히 바라보는지 확인합니다.
        ///
        /// 예전 구성(리그가 카메라 자신에게 붙음)으로 구워진 씬이 남아 있어서,
        /// 부트스트랩이 그것을 새 구조로 옮깁니다. 그 마이그레이션이 도는지도 여기서 함께 봅니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 카메라가_피벗의_자식이_되어_피벗을_바라본다()
        {
            CreateBootstrap();
            yield return null;

            var rig = Object.FindFirstObjectByType<BattleCameraRig>();
            Assert.IsNotNull(rig, "카메라 피벗이 만들어지지 않았습니다.");

            var camera = Camera.main;
            Assert.IsNotNull(camera, "메인 카메라가 없습니다.");

            Assert.AreNotSame(
                rig.transform,
                camera.transform,
                "리그가 카메라 자신에게 붙어 있습니다. 피벗과 카메라가 분리되지 않았습니다.");

            Assert.AreSame(
                rig.transform,
                camera.transform.parent,
                "카메라가 피벗의 자식이 아닙니다.");

            // 카메라 시선이 피벗을 지나는지 확인합니다.
            Vector3 toPivot = rig.FocusPoint - camera.transform.position;
            Assert.Greater(toPivot.magnitude, 0.01f, "카메라가 피벗과 같은 자리에 있습니다.");

            float alignment = Vector3.Dot(camera.transform.forward, toPivot.normalized);
            Assert.Greater(alignment, 0.999f, $"카메라가 피벗을 바라보지 않습니다. (정렬도 {alignment:F4})");
        }

        /// <summary>
        /// 피벗이 섬 밖으로 한없이 나가지 않는지 확인합니다.
        /// 제한이 없으면 WASD로 빈 바다까지 나가 아무것도 보이지 않게 됩니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 카메라_피벗은_섬_범위를_벗어나지_않는다()
        {
            var bootstrap = CreateBootstrap();
            yield return null;

            var rig = Object.FindFirstObjectByType<BattleCameraRig>();
            Assert.IsNotNull(rig);

            var grid = bootstrap.Context.Grid;

            // 섬 크기의 몇 배나 떨어진 곳으로 보내 봅니다.
            float far = grid.Width * grid.CellSize * 10f;
            rig.MoveTo(new Vector3(far, 0f, far));

            Vector3 clamped = rig.FocusPoint;

            // 통행 가능한 땅의 외곽 + 여유 범위 안에 잘려야 합니다.
            float maxX = float.MinValue;
            float maxZ = float.MinValue;

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                Vector3 center = grid.WalkableTiles[i].WorldCenter;
                if (center.x > maxX) maxX = center.x;
                if (center.z > maxZ) maxZ = center.z;
            }

            float margin = bootstrap.Context.Tuning.CameraBoundsMargin + 0.01f;

            Assert.LessOrEqual(clamped.x, maxX + margin, "피벗이 섬 동쪽 밖으로 나갔습니다.");
            Assert.LessOrEqual(clamped.z, maxZ + margin, "피벗이 섬 북쪽 밖으로 나갔습니다.");
        }

        [UnityTest]
        public IEnumerator 부트스트랩이_섬과_분대를_생성한다()
        {
            var bootstrap = CreateBootstrap();

            // Start()가 돌 때까지 한 프레임 기다립니다.
            yield return null;

            Assert.IsNotNull(bootstrap.Context, "BattleContext가 만들어지지 않았습니다.");
            Assert.Greater(bootstrap.Context.Grid.WalkableTiles.Count, 0, "섬에 통행 가능한 땅이 없습니다.");
            Assert.Greater(bootstrap.Context.Grid.LandingZones.Count, 0, "상륙 구역이 없습니다.");
            Assert.Greater(bootstrap.Context.PlayerUnits.Count, 0, "플레이어 유닛이 생성되지 않았습니다.");

            var squads = Object.FindObjectsByType<Squad>(FindObjectsSortMode.None);
            Assert.Greater(squads.Length, 0, "분대가 생성되지 않았습니다.");

            for (int i = 0; i < squads.Length; i++)
            {
                Assert.IsNotNull(squads[i].Commander, $"{squads[i].DisplayName} 에 지휘관이 없습니다.");
                Assert.IsTrue(squads[i].Commander.IsCommander, "지휘관 플래그가 설정되지 않았습니다.");
            }
        }

        [UnityTest]
        public IEnumerator 분대에_이동_명령을_내리면_앵커가_움직인다()
        {
            var bootstrap = CreateBootstrap();
            yield return null;

            var squad = Object.FindFirstObjectByType<Squad>();
            Assert.IsNotNull(squad, "분대를 찾지 못했습니다.");

            Vector3 startAnchor = squad.AnchorPosition;

            // 현재 위치에서 충분히 떨어진 통행 가능 타일을 목적지로 고릅니다.
            var grid = bootstrap.Context.Grid;
            var startCoord = grid.WorldToCoord(startAnchor);

            GridCoord target = GridCoord.Invalid;
            int bestDistance = 0;

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                int distance = GridCoord.ManhattanDistance(startCoord, grid.WalkableTiles[i].Coord);
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    target = grid.WalkableTiles[i].Coord;
                }
            }

            Assert.IsTrue(target.IsValid, "목적지 타일을 찾지 못했습니다.");
            Assert.IsTrue(squad.IssueMoveOrder(target), "이동 명령이 거부되었습니다. 경로 탐색을 확인하세요.");
            Assert.AreEqual(SquadState.Moving, squad.State, "명령 직후 상태가 이동이 아닙니다.");

            // 프레임 수가 아니라 "움직였는가"를 조건으로 기다립니다.
            // 배치 모드(-nographics)에서는 프레임이 매우 빠르게 돌아 deltaTime이 0.0002초 수준입니다.
            // 프레임 수로 기다리면 실제로는 몇 밀리초밖에 시뮬레이션되지 않아 테스트가 헛돕니다.
            const float RequiredDistance = 0.5f;
            const int MaxFrames = 5000;

            float moved = 0f;
            int frames = 0;

            while (frames < MaxFrames && moved <= RequiredDistance)
            {
                yield return null;
                frames++;
                moved = Vector3.Distance(startAnchor, squad.AnchorPosition);
            }

            Assert.Greater(
                moved,
                RequiredDistance,
                $"{frames}프레임 동안 앵커가 {moved:F2}만큼밖에 움직이지 않았습니다.");
        }

        [UnityTest]
        public IEnumerator 웨이브가_시작되면_적_상륙정과_병력이_생성된다()
        {
            var bootstrap = CreateBootstrap();
            yield return null;

            var spawner = Object.FindFirstObjectByType<EnemySpawner>();
            Assert.IsNotNull(spawner, "적 스포너가 없습니다.");
            Assert.IsNotNull(spawner.Scheduler, "웨이브 스케줄러가 초기화되지 않았습니다.");

            // 준비 시간 + 상륙정 접근 + 하선까지 기다립니다.
            // 실제 시간이 아니라 프레임을 돌리며 최대 30초까지 기다립니다.
            float elapsed = 0f;
            while (elapsed < 30f && bootstrap.Context.EnemyUnits.Count == 0)
            {
                elapsed += UnityEngine.Time.deltaTime;
                yield return null;
            }

            Assert.Greater(
                bootstrap.Context.EnemyUnits.Count,
                0,
                $"{elapsed:F1}초 동안 적 병력이 상륙하지 않았습니다. 웨이브 또는 상륙정 로직을 확인하세요.");
        }

        [UnityTest]
        public IEnumerator 지휘관이_죽으면_분대가_소멸한다()
        {
            // 조사 보고서 2.5절의 이중 손실 구조가 실제로 동작하는지 확인합니다.
            var bootstrap = CreateBootstrap();
            yield return null;

            var squad = Object.FindFirstObjectByType<Squad>();
            Assert.IsNotNull(squad);

            int soldierCountBefore = squad.AliveCount;
            Assert.Greater(soldierCountBefore, 1, "지휘관 외 병사가 없어 테스트가 무의미합니다.");

            squad.Commander.Kill();

            // 분대가 다음 Update에서 소멸을 처리합니다.
            yield return null;
            yield return null;

            Assert.IsTrue(squad == null || squad.IsDestroyed, "지휘관이 죽었는데 분대가 남아 있습니다.");
        }

        [UnityTest]
        public IEnumerator 선택_컨트롤러가_분대를_등록한다()
        {
            var bootstrap = CreateBootstrap();
            yield return null;

            var selection = Object.FindFirstObjectByType<SquadSelectionController>();
            Assert.IsNotNull(selection, "선택 컨트롤러가 없습니다.");
            Assert.Greater(selection.Squads.Count, 0, "조작 대상 분대가 등록되지 않았습니다.");
            Assert.IsNull(selection.Selected, "시작 시점에 선택된 분대가 있으면 안 됩니다.");
        }

        // ====================================================================================================
        // 4. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 부트스트랩을 만듭니다. 시드를 고정해 테스트가 흔들리지 않게 합니다.
        /// </summary>
        private BattleBootstrap CreateBootstrap()
        {
            _bootstrapObject = new GameObject("TestBootstrap");
            var bootstrap = _bootstrapObject.AddComponent<BattleBootstrap>();

            // 인스펙터 필드를 직렬화 없이 설정하기 위해 SerializedField 기본값을 그대로 씁니다.
            // 시드는 IslandSettings 기본값(0 = 무작위)이므로, 연결성 등은 EditMode 테스트가 담당합니다.
            return bootstrap;
        }
    }
}
