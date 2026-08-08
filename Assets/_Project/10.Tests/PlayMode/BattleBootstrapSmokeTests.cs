using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Composition;
using SRPG.Data;
using SRPG.Gameplay.CameraControl;
using SRPG.Gameplay.Enemies;
using SRPG.Gameplay.Selection;
using SRPG.Gameplay.Squads;
using SRPG.Tests.Support;
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
        // 3-1. Tests - 전투 계약
        // ====================================================================================================

        /// <summary>
        /// <b>주문서가 실제로 전장에 반영되는지</b> 봅니다.
        ///
        /// 순수 로직 검사는 주문서의 모양만 봅니다. 그것이 정말 분대 수와 인원으로
        /// 이어지지 않으면, 캠페인이 아무리 정확한 주문서를 만들어도 소용이 없습니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 주문서가_지시한_분대가_그대로_배치된다()
        {
            var definition = UnitDefinition.CreateDefault(UnitRole.Infantry);

            try
            {
                var request = new BattleRequest();
                request.PlayerSquads.Add(new SquadOrder
                {
                    Id = 11, Definition = definition, SoldierCount = 4, DisplayName = "선봉대",
                });
                request.PlayerSquads.Add(new SquadOrder
                {
                    Id = 22, Definition = definition, SoldierCount = 2, DisplayName = "후위",
                });

                CreateBootstrap(request);
                yield return null;

                var squads = Object.FindObjectsByType<Squad>(FindObjectsSortMode.None);
                var mine = new List<Squad>();

                for (int i = 0; i < squads.Length; i++)
                {
                    if (squads[i].DisplayName == "선봉대" || squads[i].DisplayName == "후위")
                    {
                        mine.Add(squads[i]);
                    }
                }

                Assert.AreEqual(2, mine.Count, "주문서가 지시한 분대 수와 다릅니다.");

                for (int i = 0; i < mine.Count; i++)
                {
                    int expected = mine[i].DisplayName == "선봉대" ? 5 : 3;

                    Assert.AreEqual(expected, mine[i].AliveCount,
                        $"{mine[i].DisplayName} 의 인원이 주문서와 다릅니다. 지휘관을 포함해야 합니다.");
                }
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        /// <summary>
        /// <b>전투에 끝이 있어야 캠페인에 돌려줄 것이 생깁니다.</b>
        ///
        /// 지금까지 이 게임에는 끝이 없었습니다. 분대를 다 잃어도 그대로 계속 돌았습니다.
        /// 여기서는 일부러 전멸시켜 패배 보고가 실제로 발행되는지 봅니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 분대를_모두_잃으면_패배_보고가_발행된다()
        {
            var bootstrap = CreateBootstrap();
            yield return null;

            BattleResult received = null;
            bootstrap.BattleConcluded += r => received = r;

            var squads = Object.FindObjectsByType<Squad>(FindObjectsSortMode.None);
            Assert.Greater(squads.Length, 0, "분대가 배치되지 않았습니다.");

            int deployedTotal = 0;

            for (int i = 0; i < squads.Length; i++)
            {
                deployedTotal += squads[i].AliveCount;
                squads[i].Commander.Kill();
            }

            // 분대 소멸은 다음 갱신에서 처리됩니다.
            yield return null;
            yield return null;

            Assert.IsNotNull(received, "분대를 모두 잃었는데 보고가 발행되지 않았습니다.");
            Assert.AreEqual(BattleOutcome.Defeat, received.Outcome);

            Assert.AreEqual(0, received.SurvivingSquads, "전멸했는데 생존 분대가 있습니다.");
            Assert.AreEqual(deployedTotal, received.TotalLosses, "손실 인원이 배치 인원과 맞지 않습니다.");

            for (int i = 0; i < received.Squads.Count; i++)
            {
                Assert.IsTrue(received.Squads[i].Destroyed, $"{received.Squads[i].Id} 번 분대가 소멸로 보고되지 않았습니다.");
            }
        }

        /// <summary>
        /// 결말은 한 번만 나야 합니다. 보고가 두 번 가면 캠페인이 손실을 두 번 반영합니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 보고는_한_번만_발행된다()
        {
            var bootstrap = CreateBootstrap();
            yield return null;

            int received = 0;
            bootstrap.BattleConcluded += _ => received++;

            var squads = Object.FindObjectsByType<Squad>(FindObjectsSortMode.None);

            for (int i = 0; i < squads.Length; i++)
            {
                squads[i].Commander.Kill();
            }

            for (int frame = 0; frame < 6; frame++)
            {
                yield return null;
            }

            Assert.AreEqual(1, received, "보고가 여러 번 발행되었습니다.");
        }

        // ====================================================================================================
        // 4. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 부트스트랩을 만듭니다. 시드를 고정해 테스트가 흔들리지 않게 합니다.
        /// </summary>
        private BattleBootstrap CreateBootstrap(BattleRequest request = null)
        {
            _bootstrapObject = new GameObject("TestBootstrap");
            _bootstrapObject.SetActive(false);

            var bootstrap = _bootstrapObject.AddComponent<BattleBootstrap>();

            // 부트스트랩은 섬을 만들지 않고 받습니다. 테스트가 픽스처를 꽂아 줍니다.
            //
            // 오브젝트를 꺼 둔 채로 붙였다가 연결한 뒤 켭니다.
            // 켜져 있으면 Start 가 먼저 돌아 섬 없이 조립을 시도합니다.
            bootstrap.IslandSource = seed => TestIsland.Create(seed == 0 ? 20260807 : seed);

            // 무엇을 데리고 나가는지도 바깥이 정합니다. 비우면 인스펙터 기본값이 쓰입니다.
            bootstrap.Request = request;

            _bootstrapObject.SetActive(true);

            return bootstrap;
        }
    }
}
