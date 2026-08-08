using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Systems.Deployment;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 야전의 전개 구역을 검증합니다.
    ///
    /// <b>여기가 틀리면 전투가 성립하지 않습니다</b>
    ///
    /// 두 구역이 겹치면 양측이 뒤엉킨 채 시작하고, 한쪽이 비면 그 진영은 전장에 서지 못합니다.
    /// 둘 다 예외를 내지 않습니다 — 시드에 따라 가끔 "전투가 이상하게 시작된다"로만 보입니다.
    /// 지형은 시드마다 다르니 재현도 어렵습니다.
    /// </summary>
    public sealed class DeploymentZoneTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private const float Cell = 2f;

        private static IslandGrid BuildField(int width, int depth)
        {
            var grid = new IslandGrid(width, depth, Cell, 0.9f);

            for (int y = 0; y < depth; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var tile = grid.GetTile(new GridCoord(x, y));
                    tile.Type = TileType.Ground;
                    tile.Height = 0;
                    tile.IsWalkable = true;
                    tile.WorldCenter = grid.CoordToWorld(tile.Coord);

                    grid.WalkableTiles.Add(tile);
                }
            }

            return grid;
        }

        private static void Block(IslandGrid grid, int x, int y)
        {
            var tile = grid.GetTile(new GridCoord(x, y));
            tile.Type = TileType.Cliff;
            tile.IsWalkable = false;

            grid.WalkableTiles.Remove(tile);
        }

        private static bool Overlaps(IReadOnlyList<Tile> a, IReadOnlyList<Tile> b)
        {
            var seen = new HashSet<GridCoord>();

            for (int i = 0; i < a.Count; i++)
            {
                seen.Add(a[i].Coord);
            }

            for (int i = 0; i < b.Count; i++)
            {
                if (seen.Contains(b[i].Coord))
                {
                    return true;
                }
            }

            return false;
        }

        // ====================================================================================================
        // 2. 기본
        // ====================================================================================================

        [Test]
        public void 양쪽_모두_설_자리를_받는다()
        {
            var grid = BuildField(20, 20);

            DeploymentZones.Build(grid, seed: 1234);

            Assert.Greater(grid.PlayerDeployment.Count, 0, "플레이어가 설 자리가 없습니다.");
            Assert.Greater(grid.EnemyDeployment.Count, 0, "적이 설 자리가 없습니다.");
        }

        [Test]
        public void 두_구역은_겹치지_않는다()
        {
            var grid = BuildField(20, 20);

            DeploymentZones.Build(grid, seed: 1234);

            Assert.IsFalse(
                Overlaps(grid.PlayerDeployment, grid.EnemyDeployment),
                "양측이 같은 칸에서 시작합니다. 뒤엉킨 채로 전투가 열립니다.");
        }

        /// <summary>
        /// 가운데가 비어야 접근하는 시간이 생깁니다.
        /// 그 시간이 곧 진형을 갖추고 고지를 다투는 여유입니다.
        /// </summary>
        [Test]
        public void 가운데는_비워_둔다()
        {
            var grid = BuildField(20, 20);

            DeploymentZones.Build(grid, seed: 1234);

            int occupied = grid.PlayerDeployment.Count + grid.EnemyDeployment.Count;

            Assert.Less(
                occupied,
                grid.WalkableTiles.Count,
                "전장 전체가 전개 구역입니다. 접근할 거리가 없습니다.");
        }

        [Test]
        public void 두_구역은_서로_반대편이다()
        {
            var grid = BuildField(24, 24);

            DeploymentZones.Build(grid, seed: 777);

            Vector3 player = DeploymentZones.CenterOf(grid.PlayerDeployment, grid);
            Vector3 enemy = DeploymentZones.CenterOf(grid.EnemyDeployment, grid);
            Vector3 center = grid.WorldCenter;

            // 전장 중심을 사이에 두고 마주 봐야 합니다.
            Vector3 toPlayer = player - center;
            Vector3 toEnemy = enemy - center;

            float dot = toPlayer.x * toEnemy.x + toPlayer.z * toEnemy.z;

            Assert.Less(dot, 0f, "두 진영이 같은 쪽에 몰려 있습니다.");
        }

        [Test]
        public void 진영으로_구역을_조회할_수_있다()
        {
            var grid = BuildField(16, 16);

            DeploymentZones.Build(grid, seed: 42);

            Assert.AreSame(grid.PlayerDeployment, grid.GetDeployment(Team.Player));
            Assert.AreSame(grid.EnemyDeployment, grid.GetDeployment(Team.Enemy));
        }

        // ====================================================================================================
        // 3. 재현성
        // ====================================================================================================

        /// <summary>
        /// 같은 전장이 같은 대치 구도를 내야 합니다.
        /// 시드가 전장을 재현하는데 전개만 달라지면 재현이 재현이 아닙니다.
        /// </summary>
        [Test]
        public void 같은_시드는_같은_구역을_만든다()
        {
            var first = BuildField(20, 20);
            var second = BuildField(20, 20);

            DeploymentZones.Build(first, seed: 20260809);
            DeploymentZones.Build(second, seed: 20260809);

            Assert.AreEqual(first.PlayerDeployment.Count, second.PlayerDeployment.Count);

            for (int i = 0; i < first.PlayerDeployment.Count; i++)
            {
                Assert.AreEqual(first.PlayerDeployment[i].Coord, second.PlayerDeployment[i].Coord);
            }
        }

        /// <summary>
        /// 대치 축이 늘 같으면 지형의 의미가 사라집니다.
        /// 언덕이 언제나 같은 쪽에 서면 그건 지형이 아니라 규칙입니다.
        /// </summary>
        [Test]
        public void 다른_시드는_다른_축을_만든다()
        {
            var a = BuildField(20, 20);
            var b = BuildField(20, 20);

            DeploymentZones.Build(a, seed: 111);
            DeploymentZones.Build(b, seed: 999);

            Vector3 centerA = DeploymentZones.CenterOf(a.PlayerDeployment, a);
            Vector3 centerB = DeploymentZones.CenterOf(b.PlayerDeployment, b);

            Assert.Greater(
                (centerA - centerB).magnitude,
                0.5f,
                "시드가 달라도 같은 자리에서 시작합니다.");
        }

        [Test]
        public void 다시_그으면_이전_구역이_남지_않는다()
        {
            var grid = BuildField(20, 20);

            DeploymentZones.Build(grid, seed: 1);
            int first = grid.PlayerDeployment.Count;

            DeploymentZones.Build(grid, seed: 2);

            Assert.AreEqual(first, grid.PlayerDeployment.Count, "이전 구역이 남아 누적됐습니다.");
        }

        // ====================================================================================================
        // 4. 험한 지형
        // ====================================================================================================

        /// <summary>
        /// <b>구역은 절대 비지 않습니다.</b>
        ///
        /// 거리로 잘랐다면 한쪽 끝이 통째로 절벽일 때 구역이 비어 부대를 세울 곳이 없어집니다.
        /// 정렬한 뒤 개수로 자르므로 통행 가능한 칸이 있는 한 양쪽 모두 자리를 받습니다.
        /// </summary>
        [Test]
        public void 한쪽_끝이_막혀_있어도_자리를_받는다()
        {
            var grid = BuildField(20, 20);

            // 왼쪽 여섯 열을 통째로 막습니다.
            for (int x = 0; x < 6; x++)
            {
                for (int y = 0; y < 20; y++)
                {
                    Block(grid, x, y);
                }
            }

            DeploymentZones.Build(grid, seed: 1234);

            Assert.Greater(grid.PlayerDeployment.Count, 0);
            Assert.Greater(grid.EnemyDeployment.Count, 0);

            foreach (var tile in grid.PlayerDeployment)
            {
                Assert.IsTrue(tile.IsWalkable, $"설 수 없는 칸이 구역에 들어갔습니다: {tile.Coord}");
            }

            foreach (var tile in grid.EnemyDeployment)
            {
                Assert.IsTrue(tile.IsWalkable, $"설 수 없는 칸이 구역에 들어갔습니다: {tile.Coord}");
            }
        }

        [Test]
        public void 설_땅이_없으면_빈_구역을_돌려준다()
        {
            var grid = new IslandGrid(8, 8, Cell, 0.9f);

            DeploymentZones.Build(grid, seed: 5);

            Assert.AreEqual(0, grid.PlayerDeployment.Count);
            Assert.AreEqual(0, grid.EnemyDeployment.Count);
        }

        /// <summary>
        /// 칸이 하나뿐이면 나눌 수가 없습니다. 그래도 전투는 시작되어야 합니다.
        /// </summary>
        [Test]
        public void 칸이_하나뿐이면_양쪽이_같은_칸을_쓴다()
        {
            var grid = BuildField(8, 8);

            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    if (x != 4 || y != 4)
                    {
                        Block(grid, x, y);
                    }
                }
            }

            DeploymentZones.Build(grid, seed: 5);

            Assert.AreEqual(1, grid.PlayerDeployment.Count);
            Assert.AreEqual(1, grid.EnemyDeployment.Count);
        }
    }
}
