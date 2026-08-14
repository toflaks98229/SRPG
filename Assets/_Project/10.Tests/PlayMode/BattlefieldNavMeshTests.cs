using System.Collections;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Island;
using SRPG.Systems.Battlefield;
using SRPG.Systems.Grid;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace SRPG.Tests.PlayMode
{
    /// <summary>
    /// 전장 위에 구워진 길이 지형과 맞는지 확인합니다.
    ///
    /// <b>왜 이것을 검사로 보는가</b>
    ///
    /// 길은 눈에 보이지 않습니다. 씬 뷰의 표시를 켜야 겨우 보이고, 실행 중에 구워지므로
    /// 편집기에서 미리 볼 수도 없습니다. 잘못 구워졌을 때 드러나는 방식은 하나뿐입니다 —
    /// <b>병사가 이상하게 움직입니다.</b> 물 위를 걷거나, 지나갈 수 있는 곳을 돌아가거나,
    /// 어딘가에 갇힙니다. 그 증상에서 원인을 되짚는 것은 이 프로젝트가 이미 여러 번 겪었습니다.
    ///
    /// 여기서는 구워진 길에 직접 질문합니다 — <b>이 자리가 길입니까.</b>
    /// </summary>
    public sealed class BattlefieldNavMeshTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        /// <summary>길을 찾을 때 허용하는 거리입니다. 이보다 멀면 그 자리에는 길이 없는 것입니다.</summary>
        private const float SampleRange = 0.75f;

        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }

            // 길은 씬이 아니라 전역에 남습니다. 거두지 않으면 다음 검사가 지난 판의 길을 봅니다.
            BattlefieldNavMesh.Clear();
        }

        /// <summary>
        /// 전장을 세우고 길을 굽습니다.
        ///
        /// <b>실제 생성기를 그대로 씁니다.</b> 검사용 지형을 따로 만들면
        /// 게임이 실제로 굽는 것과 다른 것을 검사하게 됩니다 — 강도 바위도 없는 판이면
        /// 도려내기가 도는지 알 수 없습니다.
        /// </summary>
        /// <param name="seed">전장을 만들 씨앗입니다.</param>
        /// <param name="kind">전장의 지형입니다. 물을 보려면 강이어야 합니다.</param>
        /// <returns>세워진 전장입니다.</returns>
        private Battlefield BuildField(int seed, TerrainKind kind = TerrainKind.River)
        {
            var profile = BattlefieldProfile.CreateDefault(kind);

            try
            {
                var battlefield = BattlefieldGenerator.Generate(
                    BattlefieldSpec.CreateDefault(kind, seed),
                    profile);

                _host = new GameObject("BattlefieldUnderTest");
                _host.AddComponent<BattlefieldView>().Build(battlefield);

                return battlefield;
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        /// <summary>
        /// 그 자리에 길이 깔려 있는지 봅니다.
        /// </summary>
        /// <param name="world">확인할 월드 좌표입니다.</param>
        /// <returns>길이 있으면 true 입니다.</returns>
        private static bool HasPathAt(Vector3 world)
        {
            return NavMesh.SamplePosition(world, out _, SampleRange, NavMesh.AllAreas);
        }

        // ====================================================================================================
        // 2. Tests
        // ====================================================================================================

        /// <summary>
        /// 길이 아예 구워졌는지부터 봅니다.
        ///
        /// 여기서 실패하면 아래 검사들은 볼 것도 없습니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 전장이_서면_길이_구워진다()
        {
            var battlefield = BuildField(20260901);
            yield return null;

            var triangulation = NavMesh.CalculateTriangulation();

            Assert.Greater(
                triangulation.vertices.Length,
                0,
                "길이 하나도 구워지지 않았습니다. 지형이 수집되지 않았거나 전부 도려내졌습니다.");

            // <b>뭍의 실제 자리를 씁니다.</b> 전장 한복판을 좌표로 계산하면 높이를 알 수 없습니다 —
            // PlayOrigin.y 는 지형 <b>바닥</b>이고 그 위는 언덕일 수 있어, 몇 미터 아래를 짚게 됩니다.
            // 타일의 WorldCenter 는 지표를 따라가므로 그런 어긋남이 없습니다.
            int found = 0;

            foreach (var tile in battlefield.Grid.WalkableTiles)
            {
                if (HasPathAt(tile.WorldCenter))
                {
                    found++;
                }
            }

            Assert.Greater(
                found / (float)battlefield.Grid.WalkableTiles.Count,
                0.8f,
                $"걸어 다닐 수 있는 칸 {battlefield.Grid.WalkableTiles.Count}개 중 {found}개에만 길이 있습니다.");
        }

        /// <summary>
        /// <b>물 위에는 길이 없습니다.</b>
        ///
        /// 지형은 물 밑으로도 이어집니다. 도려내지 않으면 길이 강바닥을 따라 이어져
        /// 병사가 물속을 걸어 건넙니다 — 익사 규칙과 정면으로 어긋납니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 해수면_아래에는_길이_없다()
        {
            var battlefield = BuildField(20260902);
            yield return null;

            // <b>길의 정점을 직접 봅니다.</b>
            //
            // 물가에서 자리를 짚어 묻는 방식은 쓸 수 없습니다. 표본은 공 모양으로 주변을 뒤지므로,
            // 물 위를 짚어도 한 걸음 옆의 뭍이 걸려 "길이 있다"가 나옵니다.
            // 물가는 정의상 물과 뭍이 맞닿은 곳이라, 그 오차를 없앨 방법이 없습니다.
            //
            // 정점의 높이는 그런 애매함이 없습니다. 해수면보다 아래에 정점이 있으면
            // 그 자리에 실제로 길이 깔린 것입니다.
            var triangulation = NavMesh.CalculateTriangulation();

            // 복셀 크기만큼은 봐줍니다. 굽는 과정이 격자에 맞춰 떨어뜨리므로
            // 수면에 딱 붙은 물가가 몇 센티 내려앉을 수 있습니다.
            float floor = battlefield.SeaLevel - 0.25f;

            int submerged = 0;
            float deepest = float.MaxValue;

            for (int i = 0; i < triangulation.vertices.Length; i++)
            {
                float y = triangulation.vertices[i].y;

                if (y < floor)
                {
                    submerged++;
                    deepest = Mathf.Min(deepest, y);
                }
            }

            Assert.AreEqual(
                0,
                submerged,
                $"길의 정점 {submerged}개가 해수면({battlefield.SeaLevel:F2}) 아래에 있습니다. " +
                $"가장 깊은 곳 {deepest:F2}. 병사가 물속을 걸어 건넙니다.");
        }

        /// <summary>
        /// <b>손바닥만 한 길 조각이 남지 않습니다.</b>
        ///
        /// 바위 사이나 물가에 떨어진 조각이 생기면, 거기 올라선 병사는 어디로도 갈 수 없습니다.
        /// <b>정확히 예전의 갇힘이 재현됩니다</b> — 길을 깔고도 같은 증상이 나옵니다.
        ///
        /// <b>강이 없는 지형에서 봅니다.</b> 강은 <b>일부러</b> 땅을 가르므로,
        /// 강 지형에서 이것을 재면 "잘게 부서졌다"와 "강이 제 일을 했다"가 섞입니다.
        /// 강을 건널 수 있는지는 <see cref="여울로_강을_건널_수_있다"/> 가 따로 봅니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 길이_하나로_이어져_있다()
        {
            var battlefield = BuildField(20260903, TerrainKind.Plains);
            yield return null;

            var grid = battlefield.Grid;

            // 출발점도 실제 뭍에서 잡습니다. 좌표로 계산한 한복판은 높이를 알 수 없습니다.
            Assert.IsTrue(
                NavMesh.SamplePosition(grid.WalkableTiles[0].WorldCenter, out var hit, SampleRange, NavMesh.AllAreas),
                "첫 뭍에서 길을 찾지 못했습니다.");

            int reachable = 0;
            int tested = 0;

            var path = new NavMeshPath();

            // 고르게 훑습니다. 앞에서부터 마흔 개만 보면 전장의 한쪽 귀퉁이만 보게 됩니다.
            int stride = Mathf.Max(1, grid.WalkableTiles.Count / 40);

            for (int i = 0; i < grid.WalkableTiles.Count; i += stride)
            {
                if (!NavMesh.SamplePosition(grid.WalkableTiles[i].WorldCenter, out var target, SampleRange, NavMesh.AllAreas))
                {
                    continue;
                }

                tested++;

                if (NavMesh.CalculatePath(hit.position, target.position, NavMesh.AllAreas, path) &&
                    path.status == NavMeshPathStatus.PathComplete)
                {
                    reachable++;
                }
            }

            Assert.Greater(tested, 0, "확인할 뭍이 없습니다.");

            // 전부를 요구하지 않습니다. 강 건너처럼 <b>일부러</b> 끊어 놓은 자리가 있습니다.
            // 여기서 보는 것은 길이 잘게 부서지지 않았는가입니다.
            Assert.Greater(
                reachable / (float)tested,
                0.7f,
                $"첫 뭍에서 닿는 곳이 {reachable}/{tested} 뿐입니다. 길이 잘게 끊어져 있습니다.");
        }

        /// <summary>
        /// <b>여울로 강을 건널 수 있습니다.</b>
        ///
        /// 강은 전장을 가르고, 여울이 유일한 도하 지점입니다.
        /// 그 여울이 길로 이어지지 않으면 <b>양쪽이 영영 만나지 못합니다</b> —
        /// 적이 오지 않아 전투가 시작되지도, 끝나지도 않습니다.
        ///
        /// <b>이 검사는 도려내기의 대가를 재는 자리이기도 합니다.</b>
        /// 물을 도려내면 강은 진짜 장벽이 됩니다. 여울이 병사 반경(0.3m)보다 좁게 남으면
        /// 길이 끊기고, 그러면 도려내기를 되돌리는 대신 <b>여울을 넓혀야</b> 합니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 여울로_강을_건널_수_있다()
        {
            var battlefield = BuildField(20260902, TerrainKind.River);
            yield return null;

            var grid = battlefield.Grid;

            Assert.IsTrue(
                NavMesh.SamplePosition(grid.WalkableTiles[0].WorldCenter, out var near, SampleRange, NavMesh.AllAreas),
                "첫 뭍에서 길을 찾지 못했습니다.");

            // 가장 먼 뭍을 고릅니다. 강이 가른 전장에서 그곳은 십중팔구 건너편입니다.
            Tile farthest = null;
            float best = -1f;

            foreach (var tile in grid.WalkableTiles)
            {
                float distance = Vector3.Distance(tile.WorldCenter, grid.WalkableTiles[0].WorldCenter);

                if (distance > best)
                {
                    best = distance;
                    farthest = tile;
                }
            }

            Assert.IsNotNull(farthest);

            Assert.IsTrue(
                NavMesh.SamplePosition(farthest.WorldCenter, out var far, SampleRange, NavMesh.AllAreas),
                $"가장 먼 뭍({farthest.Coord})에 길이 없습니다.");

            var path = new NavMeshPath();

            bool crossed = NavMesh.CalculatePath(near.position, far.position, NavMesh.AllAreas, path) &&
                           path.status == NavMeshPathStatus.PathComplete;

            Assert.IsTrue(
                crossed,
                $"{best:F0}m 떨어진 {farthest.Coord} 까지 길이 이어지지 않습니다. " +
                "여울이 병사가 지나갈 만큼 넓지 않은 것입니다.");
        }

        /// <summary>
        /// 오를 수 있는 기울기가 격자와 같습니다.
        ///
        /// 두 벌의 통행 규칙이 생기면 한쪽만 막힌 자리가 나옵니다.
        /// 그런 자리는 병사가 실제로 서 보기 전까지 아무 데도 드러나지 않습니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 오를_수_있는_기울기가_격자와_같다()
        {
            var battlefield = BuildField(20260904);
            yield return null;

            // 구워진 뒤에는 유니티에 되물을 방법이 없습니다.
            // (NavMesh.GetSettingsByID 는 프로젝트 설정의 복사본을 돌려줄 뿐,
            //  이번 판을 무슨 값으로 구웠는지는 모릅니다.)
            var settings = BattlefieldNavMesh.SettingsFor(battlefield);

            Assert.AreEqual(
                battlefield.ClimbLimitDegrees,
                settings.agentSlope,
                0.01f,
                "길과 격자가 서로 다른 기울기를 오를 수 있다고 봅니다.");

            Assert.AreEqual(BattlefieldNavMesh.AgentRadius, settings.agentRadius, 0.01f);
            Assert.AreEqual(BattlefieldNavMesh.AgentHeight, settings.agentHeight, 0.01f);
        }
    }
}
