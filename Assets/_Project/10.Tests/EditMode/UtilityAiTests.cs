using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Systems.AI;
using SRPG.Systems.Grid;
using SRPG.Tests.Support;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 유틸리티 판단부를 검증합니다.
    ///
    /// AI 코드는 "그럴듯하게 도는데 사실은 틀린" 상태가 가장 흔합니다.
    /// 적이 이상하게 움직여도 그게 버그인지 튜닝 문제인지 구분이 안 되기 때문입니다.
    /// 그래서 <b>판단이 만족해야 할 성질</b>을 여기서 못 박습니다.
    /// 이후 가중치를 아무리 만져도 이 성질들은 유지되어야 합니다.
    /// </summary>
    public sealed class UtilityAiTests
    {
        // ====================================================================================================
        // 1. ResponseCurve
        // ====================================================================================================

        [Test]
        public void 증가_곡선은_입력이_클수록_커진다()
        {
            var curve = ResponseCurve.Increasing;

            Assert.Less(curve.Evaluate(0.2f), curve.Evaluate(0.8f));
            Assert.AreEqual(0f, curve.Evaluate(0f), 0.001f);
            Assert.AreEqual(1f, curve.Evaluate(1f), 0.001f);
        }

        [Test]
        public void 감소_곡선은_입력이_클수록_작아진다()
        {
            var curve = ResponseCurve.Decreasing;

            Assert.Greater(curve.Evaluate(0.2f), curve.Evaluate(0.8f));
            Assert.AreEqual(1f, curve.Evaluate(0f), 0.001f);
            Assert.AreEqual(0f, curve.Evaluate(1f), 0.001f);
        }

        /// <summary>
        /// 급감 곡선은 가까울 때만 크게 좋고, 멀어지면 다 비슷하게 시들해야 합니다.
        /// "50m와 55m를 구분하지 않는다"가 이 곡선의 존재 이유입니다.
        /// </summary>
        [Test]
        public void 급감_곡선은_먼_구간에서_차이가_거의_없다()
        {
            var curve = ResponseCurve.SharpFalloff;

            float nearGap = curve.Evaluate(0.0f) - curve.Evaluate(0.2f);
            float farGap = curve.Evaluate(0.7f) - curve.Evaluate(0.9f);

            Assert.Greater(nearGap, farGap * 2f, "가까운 구간과 먼 구간의 민감도가 비슷합니다.");
        }

        [Test]
        public void 곡선_결과는_항상_0과_1_사이다()
        {
            var curves = new[]
            {
                ResponseCurve.Increasing,
                ResponseCurve.Decreasing,
                ResponseCurve.SharpFalloff,
                ResponseCurve.Threshold,
            };

            foreach (var curve in curves)
            {
                for (float x = -0.5f; x <= 1.5f; x += 0.1f)
                {
                    float y = curve.Evaluate(x);
                    Assert.GreaterOrEqual(y, 0f);
                    Assert.LessOrEqual(y, 1f);
                }
            }
        }

        // ====================================================================================================
        // 2. UtilityScorer
        // ====================================================================================================

        [Test]
        public void 결격_사유가_하나라도_있으면_점수는_0이다()
        {
            // 곱셈을 쓰는 이유입니다. 갈 수 없는 곳은 가치가 아무리 높아도 결격이어야 합니다.
            var scores = new[] { 1f, 1f, 0f, 1f };

            Assert.AreEqual(0f, UtilityScorer.Combine(scores, 4), 0.0001f);
        }

        [Test]
        public void 고려사항이_하나면_그_점수가_그대로다()
        {
            var scores = new[] { 0.63f };

            Assert.AreEqual(0.63f, UtilityScorer.Combine(scores, 1), 0.0001f);
        }

        /// <summary>
        /// 보상 계수가 없으면 고려사항을 늘릴수록 모든 후보가 0에 눌려 서로 구분되지 않습니다.
        /// </summary>
        [Test]
        public void 보상_계수가_곱셈의_눌림을_되살린다()
        {
            var scores = new[] { 0.9f, 0.9f, 0.9f, 0.9f };

            float rawProduct = 0.9f * 0.9f * 0.9f * 0.9f;   // 0.6561
            float combined = UtilityScorer.Combine(scores, 4);

            Assert.Greater(combined, rawProduct, "보상이 적용되지 않았습니다.");
            Assert.LessOrEqual(combined, 1f);
        }

        [Test]
        public void 보상을_적용해도_순위는_뒤집히지_않는다()
        {
            // 이게 깨지면 보상이 판단 자체를 바꿔 버립니다.
            var better = new[] { 0.9f, 0.8f, 0.7f };
            var worse = new[] { 0.8f, 0.7f, 0.6f };

            Assert.Greater(
                UtilityScorer.Combine(better, 3),
                UtilityScorer.Combine(worse, 3));
        }

        [Test]
        public void 가중치가_0이면_그_고려사항은_무시된다()
        {
            var scores = new[] { 0.5f, 0.01f };

            float withWeight = UtilityScorer.CombineWeighted(scores, new[] { 1f, 1f }, 2);
            float withoutWeight = UtilityScorer.CombineWeighted(scores, new[] { 1f, 0f }, 2);

            Assert.Greater(withoutWeight, withWeight, "가중치 0인 고려사항이 여전히 점수를 깎았습니다.");
            Assert.AreEqual(0.5f, withoutWeight, 0.0001f);
        }

        [Test]
        public void 가중치가_낮을수록_영향이_작다()
        {
            var scores = new[] { 1f, 0.2f };

            float strong = UtilityScorer.CombineWeighted(scores, new[] { 1f, 1f }, 2);
            float weak = UtilityScorer.CombineWeighted(scores, new[] { 1f, 0.3f }, 2);

            Assert.Greater(weak, strong, "가중치를 낮췄는데 영향이 줄지 않았습니다.");
        }

        [Test]
        public void 빈_입력은_0을_돌려준다()
        {
            Assert.AreEqual(0f, UtilityScorer.Combine(null, 3), 0.0001f);
            Assert.AreEqual(0f, UtilityScorer.Combine(new float[4], 0), 0.0001f);
        }

        // ====================================================================================================
        // 3. EnemyGoalPlanner
        // ====================================================================================================

        private static IslandGrid CreateIsland(int seed = 20260807)
        {
            return TestIsland.Create(seed);
        }

        [Test]
        public void 후보가_없으면_고르지_못한다()
        {
            var grid = CreateIsland();
            var planner = new EnemyGoalPlanner();

            bool ok = planner.TrySelectGoal(
                grid.WalkableTiles[0].Coord,
                new List<GoalCandidate>(),
                grid,
                null,
                EnemyGoalPlanner.Weights.Default,
                out _,
                out _);

            Assert.IsFalse(ok);
        }

        [Test]
        public void 통행_불가_목표는_결격이다()
        {
            var grid = CreateIsland();
            var planner = new EnemyGoalPlanner();

            GridCoord water = GridCoord.Invalid;
            for (int i = 0; i < grid.AllTiles.Count; i++)
            {
                if (grid.AllTiles[i].IsWater)
                {
                    water = grid.AllTiles[i].Coord;
                    break;
                }
            }

            float score = planner.ScoreCandidate(
                grid.WalkableTiles[0].Coord,
                new GoalCandidate(water, GoalKind.House),
                grid,
                null,
                EnemyGoalPlanner.Weights.Default);

            Assert.AreEqual(0f, score, 0.0001f);
        }

        [Test]
        public void 조건이_같으면_가까운_목표를_고른다()
        {
            var grid = CreateIsland();
            var planner = new EnemyGoalPlanner();

            var from = grid.WalkableTiles[0].Coord;

            // 같은 종류의 목표 두 개를 거리만 다르게 둡니다.
            var near = FindWalkableAtDistance(grid, from, 3);
            var far = FindWalkableAtDistance(grid, from, 15);

            Assert.IsTrue(near.IsValid && far.IsValid, "테스트할 타일을 찾지 못했습니다.");

            var candidates = new List<GoalCandidate>
            {
                new GoalCandidate(far, GoalKind.House),
                new GoalCandidate(near, GoalKind.House),
            };

            Assert.IsTrue(planner.TrySelectGoal(
                from, candidates, grid, null, EnemyGoalPlanner.Weights.Default, out var best, out _));

            Assert.AreEqual(near, best.Coord, "더 먼 목표를 골랐습니다.");
        }

        [Test]
        public void 거리가_같으면_가옥을_분대보다_우선한다()
        {
            var grid = CreateIsland();
            var planner = new EnemyGoalPlanner();

            var from = grid.WalkableTiles[0].Coord;
            var target = FindWalkableAtDistance(grid, from, 6);
            Assert.IsTrue(target.IsValid);

            float houseScore = planner.ScoreCandidate(
                from, new GoalCandidate(target, GoalKind.House), grid, null, EnemyGoalPlanner.Weights.Default);

            float squadScore = planner.ScoreCandidate(
                from, new GoalCandidate(target, GoalKind.PlayerSquad), grid, null, EnemyGoalPlanner.Weights.Default);

            Assert.Greater(houseScore, squadScore, "같은 자리인데 가옥이 분대보다 낮게 평가됐습니다.");
        }

        /// <summary>
        /// <b>이 테스트가 3순위 작업의 핵심입니다.</b>
        ///
        /// 방어가 두꺼운 목표의 점수가 낮아져야 우회가 나옵니다.
        /// 이 성질이 깨지면 적은 예전처럼 정면으로 걸어 들어가고, 전선 분산 압박이 사라집니다.
        /// </summary>
        [Test]
        public void 방어가_두꺼운_목표는_점수가_낮아진다()
        {
            var grid = CreateIsland();
            var planner = new EnemyGoalPlanner();

            var from = grid.WalkableTiles[0].Coord;
            var target = FindWalkableAtDistance(grid, from, 8);
            Assert.IsTrue(target.IsValid);

            var candidate = new GoalCandidate(target, GoalKind.House);
            var weights = EnemyGoalPlanner.Weights.Default;

            float undefended = planner.ScoreCandidate(from, candidate, grid, null, weights);

            // 목표 자리에 두꺼운 위협을 깝니다.
            var threat = new InfluenceMap(grid);
            threat.AddSource(target, 1f);
            threat.Propagate(0.7f);

            float defended = planner.ScoreCandidate(from, candidate, grid, threat, weights);

            Assert.Less(defended, undefended, "방어가 두꺼워졌는데 점수가 그대로입니다. 우회가 나오지 않습니다.");
        }

        [Test]
        public void 방어_가중치가_0이면_위협을_무시한다()
        {
            var grid = CreateIsland();
            var planner = new EnemyGoalPlanner();

            var from = grid.WalkableTiles[0].Coord;
            var target = FindWalkableAtDistance(grid, from, 8);
            Assert.IsTrue(target.IsValid);

            var candidate = new GoalCandidate(target, GoalKind.House);

            var weights = EnemyGoalPlanner.Weights.Default;
            weights.Undefended = 0f;

            var threat = new InfluenceMap(grid);
            threat.AddSource(target, 1f);
            threat.Propagate(0.7f);

            float withThreat = planner.ScoreCandidate(from, candidate, grid, threat, weights);
            float withoutThreat = planner.ScoreCandidate(from, candidate, grid, null, weights);

            Assert.AreEqual(withoutThreat, withThreat, 0.0001f, "가중치가 0인데 위협이 반영됐습니다.");
        }

        [Test]
        public void 방어가_얇은_쪽을_실제로_고른다()
        {
            var grid = CreateIsland();
            var planner = new EnemyGoalPlanner();

            var from = grid.WalkableTiles[0].Coord;
            var defended = FindWalkableAtDistance(grid, from, 8);
            var open = FindWalkableAtDistance(grid, from, 9);

            Assert.IsTrue(defended.IsValid && open.IsValid && defended != open);

            var threat = new InfluenceMap(grid);
            threat.AddSource(defended, 1f);
            threat.Propagate(0.55f);

            var candidates = new List<GoalCandidate>
            {
                new GoalCandidate(defended, GoalKind.House),
                new GoalCandidate(open, GoalKind.House),
            };

            // 방어 고려를 강하게, 거리 고려를 약하게 두어 판단을 뚜렷하게 만듭니다.
            var weights = EnemyGoalPlanner.Weights.Default;
            weights.Undefended = 1f;
            weights.Proximity = 0.2f;

            Assert.IsTrue(planner.TrySelectGoal(from, candidates, grid, threat, weights, out var best, out _));
            Assert.AreEqual(open, best.Coord, "방어가 두꺼운 쪽을 골랐습니다.");
        }

        // ====================================================================================================
        // 4. Helpers
        // ====================================================================================================

        /// <summary>출발점에서 대략 지정한 격자 거리에 있는 통행 가능 타일을 찾습니다.</summary>
        private static GridCoord FindWalkableAtDistance(IslandGrid grid, GridCoord from, int distance)
        {
            GridCoord best = GridCoord.Invalid;
            int bestDelta = int.MaxValue;

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var coord = grid.WalkableTiles[i].Coord;
                int delta = Mathf.Abs(GridCoord.ManhattanDistance(from, coord) - distance);

                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = coord;
                }
            }

            return best;
        }
    }
}
