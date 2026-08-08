using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Squads;
using SRPG.Systems.Grid;
using SRPG.Tests.Support;
using SRPG.Systems.Spatial;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 타일 점유 규칙을 검증합니다. 한 칸에는 분대 하나만 자리 잡을 수 있어야 합니다.
    /// </summary>
    public sealed class SquadOccupancyTests
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private IslandGrid _grid;

        // ====================================================================================================
        // 2. Setup / Teardown
        // ====================================================================================================

        [SetUp]
        public void SetUp()
        {
            _grid = TestIsland.Create(20260807);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();
        }

        /// <summary>
        /// 테스트용 빈 분대를 만듭니다. Initialize를 부르지 않으므로 병사는 없고 참조로만 쓰입니다.
        /// </summary>
        private Squad CreateSquad(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<Squad>();
        }

        /// <summary>
        /// 통행 가능한 타일 좌표를 인덱스로 가져옵니다.
        /// </summary>
        private GridCoord WalkableAt(int index)
        {
            return _grid.WalkableTiles[index % _grid.WalkableTiles.Count].Coord;
        }

        // ====================================================================================================
        // 3. Claim / Release
        // ====================================================================================================

        [Test]
        public void 점유하면_다른_분대에게_막힌다()
        {
            var occupancy = new TileOccupancy<Squad>(s => s == null || s.IsDestroyed);
            var alpha = CreateSquad("Alpha");
            var bravo = CreateSquad("Bravo");
            var coord = WalkableAt(0);

            occupancy.Claim(coord, alpha);

            Assert.IsTrue(occupancy.IsBlockedFor(coord, bravo), "다른 분대에게 막히지 않았습니다.");
            Assert.IsFalse(occupancy.IsBlockedFor(coord, alpha), "자기가 점유한 칸이 자기에게 막혔습니다.");
            Assert.AreEqual(alpha, occupancy.GetOccupant(coord));
        }

        [Test]
        public void 새로_점유하면_이전_칸이_풀린다()
        {
            var occupancy = new TileOccupancy<Squad>(s => s == null || s.IsDestroyed);
            var alpha = CreateSquad("Alpha");

            var first = WalkableAt(0);
            var second = WalkableAt(10);

            occupancy.Claim(first, alpha);
            occupancy.Claim(second, alpha);

            Assert.IsNull(occupancy.GetOccupant(first), "이전 점유가 풀리지 않아 칸이 영영 막힙니다.");
            Assert.AreEqual(alpha, occupancy.GetOccupant(second));
        }

        [Test]
        public void 해제하면_다시_비어_있다()
        {
            var occupancy = new TileOccupancy<Squad>(s => s == null || s.IsDestroyed);
            var alpha = CreateSquad("Alpha");
            var coord = WalkableAt(0);

            occupancy.Claim(coord, alpha);
            occupancy.Release(alpha);

            Assert.IsNull(occupancy.GetOccupant(coord));
        }

        // ====================================================================================================
        // 4. Destination Resolution
        // ====================================================================================================

        [Test]
        public void 빈_칸을_요청하면_그대로_돌려준다()
        {
            var occupancy = new TileOccupancy<Squad>(s => s == null || s.IsDestroyed);
            var alpha = CreateSquad("Alpha");
            var coord = WalkableAt(0);

            Assert.IsTrue(occupancy.TryResolveDestination(coord, alpha, _grid, out var resolved));
            Assert.AreEqual(coord, resolved);
        }

        [Test]
        public void 점유된_칸을_요청하면_가까운_빈_칸으로_보정한다()
        {
            var occupancy = new TileOccupancy<Squad>(s => s == null || s.IsDestroyed);
            var alpha = CreateSquad("Alpha");
            var bravo = CreateSquad("Bravo");

            var contested = WalkableAt(0);
            occupancy.Claim(contested, alpha);

            Assert.IsTrue(occupancy.TryResolveDestination(contested, bravo, _grid, out var resolved));

            Assert.AreNotEqual(contested, resolved, "점유된 칸이 그대로 돌아왔습니다.");
            Assert.IsTrue(_grid.GetTile(resolved).IsWalkable, "보정된 칸이 통행 불가입니다.");
            Assert.IsFalse(occupancy.IsBlockedFor(resolved, bravo), "보정된 칸도 점유되어 있습니다.");
        }

        [Test]
        public void 보정된_칸은_원래_목적지에_인접할_만큼_가깝다()
        {
            // 한 칸 빗나간 클릭 때문에 분대가 엉뚱한 곳으로 가면 조작이 고장 난 것처럼 느껴집니다.
            var occupancy = new TileOccupancy<Squad>(s => s == null || s.IsDestroyed);
            var alpha = CreateSquad("Alpha");
            var bravo = CreateSquad("Bravo");

            var contested = WalkableAt(20);
            occupancy.Claim(contested, alpha);

            Assert.IsTrue(occupancy.TryResolveDestination(contested, bravo, _grid, out var resolved));

            int distance = GridCoord.ManhattanDistance(contested, resolved);
            Assert.LessOrEqual(distance, 4, $"보정 거리가 {distance}칸으로 너무 멉니다.");
        }

        [Test]
        public void 자기가_점유한_칸은_보정하지_않는다()
        {
            // 같은 자리에 다시 명령했을 때 옆 칸으로 밀려나면 안 됩니다.
            var occupancy = new TileOccupancy<Squad>(s => s == null || s.IsDestroyed);
            var alpha = CreateSquad("Alpha");
            var coord = WalkableAt(5);

            occupancy.Claim(coord, alpha);

            Assert.IsTrue(occupancy.TryResolveDestination(coord, alpha, _grid, out var resolved));
            Assert.AreEqual(coord, resolved);
        }

        [Test]
        public void 통행_불가_칸을_요청하면_통행_가능한_칸으로_보정한다()
        {
            var occupancy = new TileOccupancy<Squad>(s => s == null || s.IsDestroyed);
            var alpha = CreateSquad("Alpha");

            // 격자 테두리는 항상 바다입니다. 다만 바다 한가운데는 탐색 반경 안에 육지가 없을 수 있으므로,
            // 육지에 인접한 물 타일을 골라 확인합니다.
            GridCoord water = GridCoord.Invalid;
            for (int i = 0; i < _grid.WalkableTiles.Count && !water.IsValid; i++)
            {
                var tile = _grid.WalkableTiles[i];
                for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                {
                    var neighbor = _grid.GetTile(tile.Coord + GridCoord.Neighbors4[n]);
                    if (neighbor != null && neighbor.IsWater)
                    {
                        water = neighbor.Coord;
                        break;
                    }
                }
            }

            Assert.IsTrue(water.IsValid, "테스트 전제가 깨졌습니다. 해안에 인접한 물 타일이 없습니다.");
            Assert.IsTrue(occupancy.TryResolveDestination(water, alpha, _grid, out var resolved));
            Assert.IsTrue(_grid.GetTile(resolved).IsWalkable, "보정 결과가 여전히 통행 불가입니다.");
        }

        [Test]
        public void 서로_다른_분대는_결국_서로_다른_칸을_갖는다()
        {
            // 여러 분대가 같은 칸을 연달아 노려도 최종 점유는 겹치지 않아야 합니다.
            var occupancy = new TileOccupancy<Squad>(s => s == null || s.IsDestroyed);
            var target = WalkableAt(30);

            var claims = new List<GridCoord>();

            for (int i = 0; i < 4; i++)
            {
                var squad = CreateSquad($"Squad{i}");

                Assert.IsTrue(occupancy.TryResolveDestination(target, squad, _grid, out var resolved),
                    $"{i}번 분대가 자리를 찾지 못했습니다.");

                occupancy.Claim(resolved, squad);
                claims.Add(resolved);
            }

            CollectionAssert.AllItemsAreUnique(claims, "두 분대가 같은 칸을 점유했습니다.");
        }
    }
}
