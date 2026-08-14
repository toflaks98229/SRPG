using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Data;
using UnityEditor;

namespace SRPG.Tests
{
    /// <summary>
    /// 캠페인 지도가 실제로 걸어갈 수 있는 모양인지 확인합니다.
    ///
    /// <b>왜 검사로 보는가</b>
    ///
    /// 지점끼리의 연결은 <b>번호</b>로 가리킵니다. 번호 하나만 어긋나도 길이 끊기거나
    /// 엉뚱한 곳으로 이어지는데, 인스펙터에서는 숫자 몇 개가 나열될 뿐이라
    /// <b>눈으로는 아무것도 보이지 않습니다.</b>
    ///
    /// 그리고 이 결함은 게임을 켜도 바로 드러나지 않습니다. 앞쪽 지점 몇 곳을
    /// 정상적으로 밟은 다음, 어느 지점에서 갈 곳이 없어지거나 지도 밖을 가리킵니다.
    /// 거기까지 가는 데 전투 여러 판이 듭니다.
    ///
    /// 여기서 확인하는 것은 하나입니다 — <b>본진에서 시작해 끝까지 갈 수 있는가.</b>
    /// </summary>
    public sealed class CampaignMapTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        /// <summary>검사할 지도 에셋의 경로입니다.</summary>
        private const string MapPath = "Assets/_Project/03.DataAssets/Campaign/WorldMap_Campaign.asset";

        private WorldMapDefinition _map;

        [SetUp]
        public void SetUp()
        {
            _map = AssetDatabase.LoadAssetAtPath<WorldMapDefinition>(MapPath);

            Assert.IsNotNull(_map, $"캠페인 지도를 찾지 못했습니다 — {MapPath}");
        }

        // ====================================================================================================
        // 2. Tests - 모양
        // ====================================================================================================

        /// <summary>
        /// 지점이 여러 곳이어야 캠페인이 됩니다.
        /// </summary>
        [Test]
        public void 지도에_지점이_있다()
        {
            Assert.Greater(_map.NodeCount, 1, "지점이 하나뿐이면 갈 곳이 없습니다.");
        }

        /// <summary>
        /// <b>연결이 지도 밖을 가리키지 않습니다.</b>
        ///
        /// 지점을 지우거나 순서를 바꾸면 남은 번호가 조용히 어긋납니다.
        /// 그 자리에 닿기 전까지는 아무 일도 일어나지 않습니다.
        /// </summary>
        [Test]
        public void 연결이_지도_안을_가리킨다()
        {
            for (int i = 0; i < _map.NodeCount; i++)
            {
                var node = _map.GetNode(i);
                var links = node.Links;

                if (links == null)
                {
                    continue;
                }

                for (int j = 0; j < links.Length; j++)
                {
                    Assert.IsTrue(
                        links[j] >= 0 && links[j] < _map.NodeCount,
                        $"{i}번 '{node.DisplayName}' 이 지도 밖({links[j]})을 가리킵니다.");

                    Assert.AreNotEqual(
                        i,
                        links[j],
                        $"{i}번 '{node.DisplayName}' 이 자기 자신으로 이어집니다.");
                }
            }
        }

        /// <summary>
        /// <b>본진에서 모든 지점에 닿을 수 있습니다.</b>
        ///
        /// 닿을 수 없는 지점은 만들어 두고도 영영 쓰이지 않습니다.
        /// 지도를 손보다 갈래 하나를 떼어 놓으면 이렇게 됩니다.
        /// </summary>
        [Test]
        public void 본진에서_모든_지점에_닿는다()
        {
            var reached = Reachable();

            for (int i = 0; i < _map.NodeCount; i++)
            {
                Assert.IsTrue(
                    reached.Contains(i),
                    $"{i}번 '{_map.GetNode(i).DisplayName}' 에 본진에서 닿을 수 없습니다.");
            }
        }

        /// <summary>
        /// <b>끝이 하나뿐입니다.</b>
        ///
        /// 갈 곳이 없는 지점이 곧 캠페인의 끝입니다. 그것이 둘이면
        /// 어느 쪽으로 갔느냐에 따라 캠페인이 다른 데서 끝나는데,
        /// 대개는 의도가 아니라 연결을 빠뜨린 것입니다.
        /// </summary>
        [Test]
        public void 끝나는_지점이_하나다()
        {
            var terminals = new List<string>();

            for (int i = 0; i < _map.NodeCount; i++)
            {
                var node = _map.GetNode(i);

                if (node.Links == null || node.Links.Length == 0)
                {
                    terminals.Add($"{i}번 '{node.DisplayName}'");
                }
            }

            Assert.AreEqual(1, terminals.Count, $"끝나는 지점이 여럿입니다 — {string.Join(", ", terminals)}");
        }

        // ====================================================================================================
        // 3. Tests - 내용
        // ====================================================================================================

        /// <summary>
        /// 시작 지점에는 적이 없습니다. 첫 화면부터 전투로 밀어 넣지 않습니다.
        /// </summary>
        [Test]
        public void 시작_지점은_비어_있다()
        {
            Assert.IsFalse(_map.GetNode(0).HasEnemy, "본진에 적이 있습니다.");
        }

        /// <summary>
        /// <b>적이 있다고 적힌 지점에는 실제로 적 정의가 붙어 있습니다.</b>
        ///
        /// 분대 수만 넣고 명부를 비워 두면 전투가 열리는데 아무도 오지 않습니다.
        /// 그 판은 시작하자마자 승리로 끝나고, 지도만 보면 알 수 없습니다.
        /// </summary>
        [Test]
        public void 적이_있는_지점에는_명부가_붙어_있다()
        {
            for (int i = 1; i < _map.NodeCount; i++)
            {
                var node = _map.GetNode(i);

                Assert.IsTrue(
                    node.HasEnemy,
                    $"{i}번 '{node.DisplayName}' 에 적이 없습니다. 시작 지점 말고는 전투가 있어야 합니다.");

                for (int j = 0; j < node.EnemyRoster.Length; j++)
                {
                    Assert.IsNotNull(
                        node.EnemyRoster[j],
                        $"{i}번 '{node.DisplayName}' 의 명부 {j}번 칸이 비어 있습니다.");
                }
            }
        }

        /// <summary>
        /// <b>뒤로 갈수록 어려워집니다.</b>
        ///
        /// 여기서 보는 것은 정확한 곡선이 아니라 방향입니다 —
        /// 마지막 지점이 첫 전투보다 확실히 무거워야 캠페인에 진행감이 생깁니다.
        /// 지점을 옮기다 보면 이 순서가 조용히 뒤집힙니다.
        /// </summary>
        [Test]
        public void 마지막_지점이_첫_전투보다_무겁다()
        {
            var first = _map.GetNode(1);
            var last = _map.GetNode(_map.NodeCount - 1);

            int firstWeight = first.EnemySquadCount * first.SoldiersPerEnemySquad;
            int lastWeight = last.EnemySquadCount * last.SoldiersPerEnemySquad;

            Assert.Greater(
                lastWeight,
                firstWeight,
                $"'{last.DisplayName}'({lastWeight}명) 이 '{first.DisplayName}'({firstWeight}명) 보다 가볍습니다.");
        }

        /// <summary>
        /// 전장의 씨앗이 지점마다 다릅니다.
        ///
        /// 같으면 지형만 다른 <b>같은 지형지물</b>이 반복됩니다.
        /// 캠페인을 도는 동안 사람은 그것을 알아봅니다.
        /// </summary>
        [Test]
        public void 지점마다_다른_전장이_만들어진다()
        {
            var seeds = new HashSet<int>();

            for (int i = 0; i < _map.NodeCount; i++)
            {
                var node = _map.GetNode(i);

                Assert.IsTrue(
                    seeds.Add(node.Battlefield.Seed),
                    $"{i}번 '{node.DisplayName}' 의 씨앗({node.Battlefield.Seed})이 앞의 지점과 같습니다.");
            }
        }

        // ====================================================================================================
        // 4. Helpers
        // ====================================================================================================

        /// <summary>
        /// 본진에서 걸어 닿을 수 있는 지점을 모읍니다.
        /// </summary>
        /// <returns>닿을 수 있는 지점 번호들입니다.</returns>
        private HashSet<int> Reachable()
        {
            var seen = new HashSet<int> { 0 };
            var pending = new Queue<int>();

            pending.Enqueue(0);

            while (pending.Count > 0)
            {
                var links = _map.GetNode(pending.Dequeue()).Links;

                if (links == null)
                {
                    continue;
                }

                for (int i = 0; i < links.Length; i++)
                {
                    // 지도 밖을 가리키는 것은 다른 검사가 봅니다.
                    // 여기서 그것까지 보면 실패 하나에 두 검사가 함께 무너집니다.
                    if (links[i] < 0 || links[i] >= _map.NodeCount || !seen.Add(links[i]))
                    {
                        continue;
                    }

                    pending.Enqueue(links[i]);
                }
            }

            return seen;
        }
    }
}
