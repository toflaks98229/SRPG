using NUnit.Framework;
using SRPG.Common;
using SRPG.Systems.Grid;
using SRPG.Systems.Pathfinding;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 지형에 막혔을 때의 이동을 검증합니다.
    ///
    /// <b>이 파일이 존재하는 이유는 실제로 관찰된 낙오 때문입니다.</b>
    ///
    /// 예전에는 목적지가 막히면 제자리에 멈췄습니다. 벽에 비스듬히 다가가는 병사는
    /// X와 Z 어느 쪽으로도 나아가지 못한 채 굳고, 분대는 떠나가고 그 한 명만 남았습니다.
    /// 예외도 경고도 나지 않고 "가끔 한 명이 안 따라온다"로만 보이는 종류입니다.
    /// </summary>
    public sealed class GroundMotionTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private const float Cell = 2f;

        private static IslandGrid BuildOpenField(int width, int depth)
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

        private static void MakeWater(IslandGrid grid, int x, int y)
        {
            var tile = grid.GetTile(new GridCoord(x, y));
            tile.Type = TileType.Water;
            tile.IsWalkable = false;

            grid.WalkableTiles.Remove(tile);
        }

        /// <summary>격자 좌표의 중심 월드 좌표입니다.</summary>
        private static Vector3 Center(IslandGrid grid, int x, int y)
        {
            return grid.CoordToWorld(new GridCoord(x, y));
        }

        // ====================================================================================================
        // 2. 막히지 않은 경우
        // ====================================================================================================

        [Test]
        public void 열린_곳으로는_그대로_간다()
        {
            var grid = BuildOpenField(9, 9);

            Vector3 from = Center(grid, 4, 4);
            Vector3 desired = Center(grid, 5, 4);

            Vector3 result = GroundMotion.Resolve(grid, from, desired);

            Assert.AreEqual(desired.x, result.x, 0.001f);
            Assert.AreEqual(desired.z, result.z, 0.001f);
        }

        // ====================================================================================================
        // 3. 미끄러짐 — 여기가 낙오를 고친 부분입니다
        // ====================================================================================================

        /// <summary>
        /// 세로 벽에 비스듬히 다가가면 X는 막히고 Z로는 갈 수 있어야 합니다.
        /// 예전에는 여기서 완전히 멈춰 낙오했습니다.
        /// </summary>
        [Test]
        public void 벽에_비스듬히_부딪히면_벽을_따라_미끄러진다()
        {
            var grid = BuildOpenField(9, 9);

            // (5,4) 와 (5,5) 를 막아 세로 벽을 만듭니다.
            Block(grid, 5, 4);
            Block(grid, 5, 5);

            Vector3 from = Center(grid, 4, 4);
            Vector3 desired = Center(grid, 5, 5);   // 대각선으로 벽을 향해 감

            Vector3 result = GroundMotion.Resolve(grid, from, desired);

            // 벽 쪽(X)으로는 못 갔지만 Z로는 나아가야 합니다.
            Assert.AreEqual(from.x, result.x, 0.001f, "막힌 축으로 밀고 들어갔습니다.");
            Assert.Greater(result.z, from.z + 0.01f, "벽을 따라 미끄러지지 못하고 멈췄습니다.");
        }

        [Test]
        public void 가로_벽에서도_미끄러진다()
        {
            var grid = BuildOpenField(9, 9);

            Block(grid, 4, 5);
            Block(grid, 5, 5);

            Vector3 from = Center(grid, 4, 4);
            Vector3 desired = Center(grid, 5, 5);

            Vector3 result = GroundMotion.Resolve(grid, from, desired);

            Assert.Greater(result.x, from.x + 0.01f, "열린 축으로도 나아가지 못했습니다.");
            Assert.AreEqual(from.z, result.z, 0.001f);
        }

        [Test]
        public void 두_축이_모두_막히면_제자리에_머문다()
        {
            var grid = BuildOpenField(9, 9);

            // 모서리 안쪽에 갇힌 상황입니다.
            Block(grid, 5, 4);
            Block(grid, 4, 5);
            Block(grid, 5, 5);

            Vector3 from = Center(grid, 4, 4);
            Vector3 desired = Center(grid, 5, 5);

            Vector3 result = GroundMotion.Resolve(grid, from, desired);

            Assert.AreEqual(from.x, result.x, 0.001f);
            Assert.AreEqual(from.z, result.z, 0.001f);
        }

        /// <summary>
        /// 대각선만 막힌 경우입니다. 두 축이 다 열려 있으므로 모서리를 돌아 나가야 합니다.
        /// </summary>
        [Test]
        public void 대각선만_막히면_모서리를_돌아_나간다()
        {
            var grid = BuildOpenField(9, 9);

            Block(grid, 5, 5);

            Vector3 from = Center(grid, 4, 4);
            Vector3 desired = Center(grid, 5, 5);

            Vector3 result = GroundMotion.Resolve(grid, from, desired);

            // 한 축이라도 나아가야 합니다.
            bool moved = Mathf.Abs(result.x - from.x) > 0.01f || Mathf.Abs(result.z - from.z) > 0.01f;
            Assert.IsTrue(moved, "두 축이 다 열려 있는데 한 발짝도 못 갔습니다.");
        }

        [Test]
        public void 미끄러진_자리도_반드시_설_수_있는_곳이다()
        {
            var grid = BuildOpenField(9, 9);

            Block(grid, 5, 4);
            Block(grid, 5, 5);
            Block(grid, 4, 5);

            Vector3 from = Center(grid, 4, 4);

            // 여러 방향으로 밀어 봅니다.
            for (int i = 0; i < 8; i++)
            {
                var offset = GridCoord.Neighbors8[i];
                Vector3 desired = from + new Vector3(offset.X * Cell, 0f, offset.Y * Cell);

                Vector3 result = GroundMotion.Resolve(grid, from, desired);

                Assert.IsTrue(
                    GroundMotion.TryStand(grid, result, out _),
                    $"{offset} 방향으로 밀었더니 설 수 없는 자리로 갔습니다.");
            }
        }

        // ====================================================================================================
        // 4. 물 판정
        // ====================================================================================================

        [Test]
        public void 물은_물로_격자_밖도_물로_본다()
        {
            var grid = BuildOpenField(9, 9);
            MakeWater(grid, 2, 2);

            Assert.IsTrue(GroundMotion.IsWater(grid, Center(grid, 2, 2)));
            Assert.IsFalse(GroundMotion.IsWater(grid, Center(grid, 4, 4)));
            Assert.IsTrue(GroundMotion.IsWater(grid, new Vector3(9999f, 0f, 9999f)), "격자 밖은 물로 봐야 합니다.");
        }

        [Test]
        public void 절벽은_물이_아니다()
        {
            // 절벽에 밀려도 익사하면 안 됩니다.
            var grid = BuildOpenField(9, 9);
            Block(grid, 2, 2);

            Assert.IsFalse(GroundMotion.IsWater(grid, Center(grid, 2, 2)));
        }

        // ====================================================================================================
        // 5. 진형 슬롯 보정
        // ====================================================================================================

        /// <summary>
        /// 갈 수 없는 자리에 생긴 슬롯은 안쪽으로 당겨야 합니다.
        /// 그러지 않으면 그 자리를 받은 병사가 벽에 붙어 영영 도착하지 못합니다.
        /// </summary>
        [Test]
        public void 지형에_걸린_슬롯은_앵커_쪽으로_당겨진다()
        {
            var grid = BuildOpenField(9, 9);

            Block(grid, 6, 4);
            Block(grid, 7, 4);

            Vector3 anchor = Center(grid, 4, 4);

            var slots = new System.Collections.Generic.List<Vector3>
            {
                anchor,
                Center(grid, 6, 4),   // 절벽 위
            };

            SRPG.Systems.Formation.FormationSolver.ClampToWalkable(grid, anchor, slots);

            foreach (var slot in slots)
            {
                Assert.IsTrue(
                    GroundMotion.TryStand(grid, slot, out _),
                    $"보정 후에도 설 수 없는 슬롯이 남았습니다: {slot}");
            }
        }

        [Test]
        public void 멀쩡한_슬롯은_그대로_둔다()
        {
            var grid = BuildOpenField(9, 9);

            Vector3 anchor = Center(grid, 4, 4);
            Vector3 good = Center(grid, 5, 4);

            var slots = new System.Collections.Generic.List<Vector3> { good };

            SRPG.Systems.Formation.FormationSolver.ClampToWalkable(grid, anchor, slots);

            Assert.AreEqual(good.x, slots[0].x, 0.001f);
            Assert.AreEqual(good.z, slots[0].z, 0.001f);
        }

        // ====================================================================================================
        // 6. 분리 조향의 부드러움
        // ====================================================================================================

        /// <summary>
        /// 반경 경계에서 힘이 거의 0이어야 합니다.
        /// 선형이면 들어서는 순간부터 무시 못 할 힘이 붙어 툭 밀려나는 것처럼 보입니다.
        /// </summary>
        [Test]
        public void 분리_힘은_반경_가장자리에서_거의_0이다()
        {
            float radius = 1f;

            Vector3 nearEdge = SteeringSolver.SeparationFrom(
                new Vector3(0.95f, 0f, 0f), Vector3.zero, radius);

            Assert.Less(nearEdge.magnitude, 0.01f, "가장자리에서 이미 힘이 붙습니다.");
        }

        [Test]
        public void 분리_힘은_가까울수록_급격히_커진다()
        {
            float radius = 1f;

            float far = SteeringSolver.SeparationFrom(new Vector3(0.8f, 0f, 0f), Vector3.zero, radius).magnitude;
            float mid = SteeringSolver.SeparationFrom(new Vector3(0.5f, 0f, 0f), Vector3.zero, radius).magnitude;
            float near = SteeringSolver.SeparationFrom(new Vector3(0.2f, 0f, 0f), Vector3.zero, radius).magnitude;

            Assert.Less(far, mid);
            Assert.Less(mid, near);

            // 제곱 감쇠이므로 가까운 쪽의 증가폭이 훨씬 커야 합니다.
            Assert.Greater(near - mid, mid - far, "감쇠가 선형에 가깝습니다.");
        }

        [Test]
        public void 반경_밖에서는_밀어내지_않는다()
        {
            Assert.AreEqual(
                Vector3.zero,
                SteeringSolver.SeparationFrom(new Vector3(2f, 0f, 0f), Vector3.zero, 1f));
        }

        [Test]
        public void 반경이_0이하면_안전하게_0을_돌려준다()
        {
            Assert.AreEqual(
                Vector3.zero,
                SteeringSolver.SeparationFrom(new Vector3(0.5f, 0f, 0f), Vector3.zero, 0f));
        }

        // ====================================================================================================
        // 7. 한 걸음 — 익사 판정과 미끄러짐의 순서
        // ====================================================================================================

        /// <summary>
        /// <b>익사 판정이 미끄러짐보다 먼저여야 합니다.</b>
        ///
        /// 순서가 뒤집히면 밀려나던 병사가 물가를 따라 스르륵 비껴갑니다.
        /// 그러면 넉백은 그냥 밀치기 연출이 되고, 이 게임의 주요 사망 수단이 사라집니다.
        /// </summary>
        [Test]
        public void 세게_밀려_물에_닿으면_미끄러지지_않고_익사한다()
        {
            var grid = BuildOpenField(9, 9);

            // 오른쪽 한 줄을 바다로 만듭니다. 물가에 선 병사를 바다 쪽으로 밉니다.
            MakeWater(grid, 5, 4);

            Vector3 from = Center(grid, 4, 4);
            Vector3 desired = Center(grid, 5, 4);

            var step = GroundMotion.TryStep(grid, from, desired, mayDrown: true, out Vector3 next);

            Assert.AreEqual(GroundStep.Drowned, step, "물로 밀려났는데 익사하지 않았습니다.");
            Assert.AreEqual(desired.x, next.x, 0.001f, "낙수 지점이 물 위가 아닙니다.");
            Assert.AreEqual(0f, next.y, 0.001f, "낙수 지점이 수면 높이가 아닙니다.");
        }

        /// <summary>
        /// 밀려서 빠지는 것은 사고지만, 달려들다 빠지는 것은 자살입니다.
        /// 스스로 낸 힘(도약)으로는 물에 들어가지 않고 물가에서 막혀야 합니다.
        /// </summary>
        [Test]
        public void 스스로_달려드는_힘으로는_물에_빠지지_않는다()
        {
            var grid = BuildOpenField(9, 9);

            MakeWater(grid, 5, 4);

            Vector3 from = Center(grid, 4, 4);
            Vector3 desired = Center(grid, 5, 4);

            var step = GroundMotion.TryStep(grid, from, desired, mayDrown: false, out Vector3 next);

            Assert.AreEqual(GroundStep.Moved, step, "달려들었을 뿐인데 익사했습니다.");
            Assert.IsTrue(GroundMotion.TryStand(grid, next, out _), "설 수 없는 자리로 갔습니다.");
        }

        [Test]
        public void 물이_아니면_밀려나도_그냥_움직인다()
        {
            var grid = BuildOpenField(9, 9);

            Vector3 from = Center(grid, 4, 4);
            Vector3 desired = Center(grid, 5, 4);

            var step = GroundMotion.TryStep(grid, from, desired, mayDrown: true, out Vector3 next);

            Assert.AreEqual(GroundStep.Moved, step);
            Assert.AreEqual(desired.x, next.x, 0.001f);
        }

        /// <summary>
        /// 절벽으로 밀려도 익사하면 안 됩니다. 막혀서 미끄러질 뿐입니다.
        /// </summary>
        [Test]
        public void 절벽으로_밀리면_익사가_아니라_막힌다()
        {
            var grid = BuildOpenField(9, 9);

            Block(grid, 5, 4);

            Vector3 from = Center(grid, 4, 4);
            Vector3 desired = Center(grid, 5, 4);

            Assert.AreEqual(
                GroundStep.Moved,
                GroundMotion.TryStep(grid, from, desired, mayDrown: true, out _));
        }

        // ====================================================================================================
        // 8. 발 높이 — 통행은 타일이, 높이는 지형이 정합니다
        // ====================================================================================================

        /// <summary>
        /// <b>한 칸 안에서도 높이가 달라야 합니다.</b>
        ///
        /// 예전에는 타일 중심의 높이를 그대로 돌려주었습니다. 그러면 칸 안에서 높이가 상수라,
        /// 비탈을 걷는 병사가 칸 경계마다 한 단씩 툭툭 튀어 올랐습니다.
        /// 정작 분대 앵커는 연속면을 타고 있었으므로 앵커만 부드럽게 오르고 병사는 계단으로 올랐습니다.
        /// </summary>
        [Test]
        public void 발_높이는_칸_안에서도_연속이다()
        {
            var grid = BuildOpenField(9, 9);

            // 동쪽으로 갈수록 높아지는 비탈입니다.
            grid.SurfaceSampler = (x, z) => x * 0.25f;

            Vector3 center = Center(grid, 4, 4);

            // 같은 칸 안의 서쪽 끝과 동쪽 끝입니다.
            var west = new Vector3(center.x - Cell * 0.45f, 0f, center.z);
            var east = new Vector3(center.x + Cell * 0.45f, 0f, center.z);

            Assert.IsTrue(GroundMotion.TryStand(grid, west, out float westHeight));
            Assert.IsTrue(GroundMotion.TryStand(grid, east, out float eastHeight));

            Assert.AreNotEqual(
                westHeight,
                eastHeight,
                "한 칸 안에서 높이가 상수입니다. 병사가 칸 경계마다 튀어 오릅니다.");

            Assert.Less(westHeight, eastHeight, "비탈의 방향이 뒤집혔습니다.");
        }

        [Test]
        public void 발_높이는_지형이_말하는_값_그대로다()
        {
            var grid = BuildOpenField(9, 9);

            grid.SurfaceSampler = (x, z) => x * 0.25f + z * 0.1f;

            var probe = new Vector3(0.7f, 0f, -1.3f);

            Assert.IsTrue(GroundMotion.TryStand(grid, probe, out float height));

            Assert.AreEqual(
                grid.SampleGroundHeight(probe),
                height,
                0.0001f,
                "앵커가 쓰는 높이와 병사가 쓰는 높이가 다릅니다.");
        }

        /// <summary>
        /// 지형이 연결되지 않은 프로토타입 경로에서는 예전처럼 타일 높이를 씁니다.
        /// </summary>
        [Test]
        public void 지형이_없으면_타일_높이로_돌아간다()
        {
            var grid = BuildOpenField(9, 9);

            Vector3 center = Center(grid, 4, 4);

            Assert.IsTrue(GroundMotion.TryStand(grid, center, out float height));
            Assert.AreEqual(center.y, height, 0.0001f);
        }

        [Test]
        public void 설_수_없는_자리는_여전히_거절한다()
        {
            var grid = BuildOpenField(9, 9);

            grid.SurfaceSampler = (x, z) => 5f;

            MakeWater(grid, 2, 2);
            Block(grid, 3, 3);

            Assert.IsFalse(
                GroundMotion.TryStand(grid, Center(grid, 2, 2), out _),
                "지형 높이가 있다고 해서 물 위에 설 수는 없습니다.");

            Assert.IsFalse(
                GroundMotion.TryStand(grid, Center(grid, 3, 3), out _),
                "지형 높이가 있다고 해서 절벽에 설 수는 없습니다.");
        }

        // ====================================================================================================
        // 6. 갇힌 병사가 빠져나온다
        //
        // 물 위에 서 있으면 X도 Z도 물이라 두 축이 모두 막힙니다. 미끄러짐은 딛고 선 자리가
        // 온전하다는 전제 위에 있어서, 그대로 두면 제자리가 답으로 나오고 <b>영영 굳습니다.</b>
        // 증상은 오류가 아니라 "저 병사만 안 따라온다"로만 보입니다.
        // ====================================================================================================

        /// <summary>
        /// 물 위에 갇힌 병사가 뭍으로 나옵니다.
        ///
        /// 좁은 여울을 건너다 흐트러진 병사와, 물가에 선 분대의 진형 슬롯이 이 경로로 옵니다.
        ///
        /// <b>한 걸음에 나오지 않습니다.</b>
        /// 걸음의 크기는 원래 가려던 만큼이고, 물 타일은 그보다 넓습니다.
        /// 한 번에 빼내면 이 경로에서만 이동 속도가 달라져 병사가 순간이동하는 것처럼 보입니다.
        /// 그래서 여러 걸음에 걸쳐 <b>실제로 나오는지</b>를 봅니다 — 제자리에서 떠는 것도 여기서 걸립니다.
        /// </summary>
        [Test]
        public void 물_위에_갇히면_뭍으로_빠져나온다()
        {
            var grid = BuildOpenField(9, 9);

            // 한가운데 웅덩이. 4,4 에 갇힌 상태를 만듭니다.
            MakeWater(grid, 4, 4);

            Vector3 position = Center(grid, 4, 4);

            const float StepLength = 0.5f;
            const int MaxSteps = 40;

            int steps = 0;

            while (steps < MaxSteps && !GroundMotion.TryStand(grid, position, out _))
            {
                // 가려던 곳도 물입니다 — 강 건너를 향하는 상황입니다.
                Vector3 desired = position + new Vector3(StepLength, 0f, 0f);

                Vector3 next = GroundMotion.Resolve(grid, position, desired);

                Assert.AreNotEqual(
                    new Vector2(position.x, position.z),
                    new Vector2(next.x, next.z),
                    $"{steps}번째 걸음에서 제자리에 굳었습니다.");

                position = next;
                steps++;
            }

            Assert.IsTrue(
                GroundMotion.TryStand(grid, position, out _),
                $"{MaxSteps}걸음을 걷고도 물에서 나오지 못했습니다. ({position})");
        }

        /// <summary>
        /// 빠져나오는 걸음이 원래 걸음보다 커지지 않습니다.
        ///
        /// 여기서 걸음 크기를 따로 정하면 이 경로에서만 이동 속도가 달라집니다 —
        /// 물에 빠진 병사가 갑자기 순간이동하는 것처럼 보입니다.
        /// </summary>
        [Test]
        public void 빠져나오는_걸음이_원래_걸음을_넘지_않는다()
        {
            var grid = BuildOpenField(9, 9);

            MakeWater(grid, 4, 4);

            Vector3 stuck = Center(grid, 4, 4);

            const float StepLength = 0.3f;
            Vector3 desired = stuck + new Vector3(StepLength, 0f, 0f);

            Vector3 next = GroundMotion.Resolve(grid, stuck, desired);

            float moved = Vector2.Distance(
                new Vector2(stuck.x, stuck.z),
                new Vector2(next.x, next.z));

            Assert.LessOrEqual(
                moved,
                StepLength + 1e-3f,
                $"한 걸음에 {moved:F2} 를 갔습니다. 원래 걸음은 {StepLength} 입니다.");
        }

        /// <summary>
        /// 나갈 뭍이 아예 없으면 제자리에 둡니다. 여기서 터지면 안 됩니다.
        /// </summary>
        [Test]
        public void 나갈_뭍이_없으면_제자리에_둔다()
        {
            var grid = new IslandGrid(5, 5, Cell, 0.9f);

            // 통행 가능한 타일을 하나도 만들지 않습니다. 전부 물인 격자입니다.
            Vector3 stuck = Center(grid, 2, 2);

            Assert.DoesNotThrow(() =>
            {
                Vector3 next = GroundMotion.Resolve(grid, stuck, stuck + new Vector3(0.5f, 0f, 0f));

                Assert.AreEqual(stuck.x, next.x, 1e-3f);
                Assert.AreEqual(stuck.z, next.z, 1e-3f);
            });
        }
    }
}
