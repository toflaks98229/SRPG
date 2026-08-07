using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Systems.Formation;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 앵커 전진 로직을 검증합니다.
    ///
    /// 이 로직은 예전에 <c>Squad</c>와 <c>EnemyAgent</c>에 각각 들어 있어 MonoBehaviour 없이는
    /// 확인할 수 없었습니다. 순수 클래스로 꺼내 놓으니 씬 없이 직접 돌려 볼 수 있습니다.
    ///
    /// 특히 <b>도착 판정</b>이 중요합니다. 진형을 잡을지 말지를 가르는 유일한 기준이라,
    /// 여기가 틀리면 분대가 이동 중에 대열을 잡으려 하거나 도착하고도 뭉치지 않습니다.
    /// </summary>
    public sealed class FormationMotorTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private static IslandGrid CreateIsland(int seed = 20260807)
        {
            var settings = IslandSettings.CreateDefault();
            settings.Width = 30;
            settings.Depth = 30;
            return IslandGenerator.Generate(settings, seed);
        }

        /// <summary>서로 이어진 통행 가능 타일 경로를 만듭니다.</summary>
        private static List<GridCoord> BuildStraightPath(IslandGrid grid, out GridCoord start)
        {
            var path = new List<GridCoord>();
            var buffer = new Tile[4];

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];
                path.Clear();
                path.Add(tile.Coord);

                var current = tile;
                for (int step = 0; step < 4; step++)
                {
                    int count = grid.GetNeighbors4(current.Coord, buffer);
                    Tile next = null;

                    for (int n = 0; n < count; n++)
                    {
                        if (buffer[n].IsWalkable && !path.Contains(buffer[n].Coord))
                        {
                            next = buffer[n];
                            break;
                        }
                    }

                    if (next == null)
                    {
                        break;
                    }

                    path.Add(next.Coord);
                    current = next;
                }

                if (path.Count >= 4)
                {
                    start = tile.Coord;
                    return path;
                }
            }

            start = GridCoord.Invalid;
            return path;
        }

        // ====================================================================================================
        // 2. 초기 상태
        // ====================================================================================================

        [Test]
        public void 경로가_없으면_이미_도착한_것이다()
        {
            var motor = new FormationMotor();

            Assert.IsTrue(motor.HasArrived, "경로가 없는데 이동 중으로 판정됐습니다.");
            Assert.AreEqual(0, motor.RemainingWaypoints);
        }

        [Test]
        public void 순간이동하면_앵커가_그_자리로_간다()
        {
            var motor = new FormationMotor();
            var position = new Vector3(3f, 1f, -4f);
            var coord = new GridCoord(7, 9);

            motor.Teleport(position, coord);

            Assert.AreEqual(position, motor.Anchor);
            Assert.AreEqual(coord, motor.Destination);
            Assert.IsTrue(motor.HasArrived);
        }

        // ====================================================================================================
        // 3. 전진
        // ====================================================================================================

        [Test]
        public void 경로를_받으면_이동_중이_된다()
        {
            var grid = CreateIsland();
            var path = BuildStraightPath(grid, out var start);
            Assert.IsTrue(start.IsValid, "테스트용 경로를 만들지 못했습니다.");

            var motor = new FormationMotor();
            motor.Teleport(grid.CoordToWorld(start), start);
            motor.SetPath(path, path[path.Count - 1]);

            Assert.IsFalse(motor.HasArrived);
            Assert.AreEqual(path.Count, motor.RemainingWaypoints);
            Assert.AreEqual(path[path.Count - 1], motor.Destination);
        }

        [Test]
        public void 전진하면_목적지에_가까워진다()
        {
            var grid = CreateIsland();
            var path = BuildStraightPath(grid, out var start);
            Assert.IsTrue(start.IsValid);

            var motor = new FormationMotor();
            motor.Teleport(grid.CoordToWorld(start), start);
            motor.SetPath(path, path[path.Count - 1]);

            Vector3 goal = grid.CoordToWorld(path[path.Count - 1]);
            float before = Vector3.Distance(motor.Anchor, goal);

            for (int i = 0; i < 10; i++)
            {
                motor.Advance(0.05f, 3f, grid);
            }

            float after = Vector3.Distance(motor.Anchor, goal);

            Assert.Less(after, before, "전진했는데 목적지에 가까워지지 않았습니다.");
        }

        [Test]
        public void 충분히_전진하면_도착_상태가_된다()
        {
            var grid = CreateIsland();
            var path = BuildStraightPath(grid, out var start);
            Assert.IsTrue(start.IsValid);

            var motor = new FormationMotor();
            motor.Teleport(grid.CoordToWorld(start), start);
            motor.SetPath(path, path[path.Count - 1]);

            // 넉넉히 돌립니다. 경로 길이보다 훨씬 많은 걸음을 줍니다.
            for (int i = 0; i < 2000 && !motor.HasArrived; i++)
            {
                motor.Advance(0.02f, 5f, grid);
            }

            Assert.IsTrue(motor.HasArrived, "경로를 다 갔는데 도착 판정이 나지 않았습니다.");
            Assert.AreEqual(0, motor.RemainingWaypoints);
        }

        [Test]
        public void 도착한_뒤에는_더_움직이지_않는다()
        {
            var grid = CreateIsland();
            var path = BuildStraightPath(grid, out var start);
            Assert.IsTrue(start.IsValid);

            var motor = new FormationMotor();
            motor.Teleport(grid.CoordToWorld(start), start);
            motor.SetPath(path, path[path.Count - 1]);

            for (int i = 0; i < 2000 && !motor.HasArrived; i++)
            {
                motor.Advance(0.02f, 5f, grid);
            }

            Vector3 settled = motor.Anchor;

            for (int i = 0; i < 20; i++)
            {
                motor.Advance(0.02f, 5f, grid);
            }

            Assert.AreEqual(settled, motor.Anchor, "도착한 뒤에도 앵커가 움직였습니다.");
        }

        [Test]
        public void 멈추면_현재_자리가_목적지가_된다()
        {
            var grid = CreateIsland();
            var path = BuildStraightPath(grid, out var start);
            Assert.IsTrue(start.IsValid);

            var motor = new FormationMotor();
            motor.Teleport(grid.CoordToWorld(start), start);
            motor.SetPath(path, path[path.Count - 1]);

            motor.Advance(0.05f, 3f, grid);

            var here = grid.WorldToCoord(motor.Anchor);
            motor.Stop(here);

            Assert.IsTrue(motor.HasArrived);
            Assert.AreEqual(here, motor.Destination);
        }

        // ====================================================================================================
        // 4. 방어적 동작
        // ====================================================================================================

        [Test]
        public void 경로를_복사하므로_호출부가_버퍼를_재사용해도_된다()
        {
            // Squad와 EnemySquad 모두 경로 버퍼를 재사용합니다. 참조를 그대로 들고 있으면
            // 다음 탐색이 이전 분대의 경로를 덮어씁니다.
            var grid = CreateIsland();
            var path = BuildStraightPath(grid, out var start);
            Assert.IsTrue(start.IsValid);

            var motor = new FormationMotor();
            motor.Teleport(grid.CoordToWorld(start), start);
            motor.SetPath(path, path[path.Count - 1]);

            int expected = motor.RemainingWaypoints;

            // 호출부가 버퍼를 비우고 다시 씁니다.
            path.Clear();

            Assert.AreEqual(expected, motor.RemainingWaypoints, "경로가 호출부 버퍼를 참조하고 있습니다.");
        }

        [Test]
        public void null_경로와_0_델타는_안전하다()
        {
            var grid = CreateIsland();
            var motor = new FormationMotor();

            Assert.DoesNotThrow(() => motor.SetPath(null, GridCoord.Invalid));
            Assert.DoesNotThrow(() => motor.Advance(0f, 3f, grid));
            Assert.DoesNotThrow(() => motor.Advance(0.1f, 3f, null));
        }
    }
}
