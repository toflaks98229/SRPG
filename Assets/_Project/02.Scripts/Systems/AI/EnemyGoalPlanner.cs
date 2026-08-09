using System.Collections.Generic;
using SRPG.Common;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Systems.AI
{
    /// <summary>
    /// 평가 대상이 되는 목표 하나입니다.
    ///
    /// 지금은 좌표뿐입니다. 예전에는 종류(가옥/분대)를 함께 들고 가치를 매겼지만,
    /// 야전에는 지킬 가옥이 없고 <b>칠 부대만</b> 있습니다.
    /// 고지 점령 같은 것이 붙으면 그때 다시 갈래가 생깁니다.
    ///
    /// <b>후보 하나는 부대 하나입니다.</b> 병사 하나가 아닙니다 —
    /// 이유는 <see cref="EnemyGoalPlanner.CollectCandidates"/>에 적어 두었습니다.
    /// </summary>
    public readonly struct GoalCandidate
    {
        /// <summary>목표 위치입니다.</summary>
        public readonly GridCoord Coord;

        public GoalCandidate(GridCoord coord)
        {
            Coord = coord;
        }
    }

    /// <summary>
    /// 적 분대가 어디로 갈지 정합니다.
    ///
    /// <b>무엇이 달라지는가</b>
    ///
    /// 예전 규칙은 "시야 안에 플레이어가 있으면 그쪽, 없으면 가장 가까운 가옥"이었습니다.
    /// 그러면 적은 방어가 가장 두꺼운 곳으로 곧장 걸어 들어갑니다. 플레이어는 한 곳만 막으면 되고,
    /// 조사 보고서가 Fire Emblem식 규칙 기반의 한계로 지적한 그 모습이 그대로 나옵니다.
    ///
    /// 여기서는 후보마다 세 가지를 <b>동시에</b> 따집니다.
    ///
    ///   1. 근접성 — 가까울수록 좋다 (급감 곡선: 멀면 다 비슷하게 시들하다)
    ///   2. 고립   — 그 지점의 플레이어 영향력이 낮을수록 좋다
    ///   3. 개활지 — 초크포인트일수록 나쁘다 (좁은 길은 방어자에게 유리하다)
    ///
    /// <b>2번이 이 판단의 핵심입니다.</b>
    ///
    /// 목표가 적 부대 자체이므로, 그 자리의 아군 영향력이 낮다는 것은
    /// <b>주위에 도와줄 부대가 없다</b>는 뜻입니다. 즉 "고립된 쪽을 친다"가 됩니다.
    /// 각개격파하라는 규칙을 따로 쓰지 않았는데 각개격파가 나오고,
    /// 플레이어는 전선을 붙여 두어야 할 이유를 갖게 됩니다.
    ///
    /// <b>가옥이 빠진 자리입니다.</b>
    /// 상륙을 막던 시절에는 "가옥으로 가되 방어가 얇은 쪽으로"가 측면 우회를 만들었습니다.
    /// 야전에는 지킬 가옥이 없으므로 가치 항목이 사라지고, 대신 같은 계산이
    /// 부대 사이의 우열을 읽는 데 쓰입니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 판단이라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public sealed class EnemyGoalPlanner
    {
        // ====================================================================================================
        // 1. Types
        // ====================================================================================================

        /// <summary>판단에 쓰이는 가중치입니다. 0이면 그 고려사항은 판단에 관여하지 않습니다.</summary>
        public struct Weights
        {
            /// <summary>가까운 목표를 얼마나 선호할지입니다.</summary>
            public float Proximity;

            /// <summary>고립된 부대를 얼마나 노릴지입니다. 높으면 각개격파를 자주 시도합니다.</summary>
            public float Isolation;

            /// <summary>초크포인트를 얼마나 피할지입니다.</summary>
            public float OpenGround;

            /// <summary>기본 가중치입니다.</summary>
            public static Weights Default => new Weights
            {
                Proximity = 0.7f,
                Isolation = 0.85f,
                OpenGround = 0.45f,
            };
        }

        // ====================================================================================================
        // 2. Constants
        // ====================================================================================================

        /// <summary>고려사항 개수입니다.</summary>
        private const int ConsiderationCount = 3;

        /// <summary>
        /// 근접성 평가에서 "충분히 멀다"고 보는 격자 거리입니다.
        /// 이보다 멀면 전부 비슷하게 시들한 것으로 취급합니다.
        /// </summary>
        private const float FarDistance = 40f;

        // ====================================================================================================
        // 3. Fields
        // ====================================================================================================

        /// <summary>고려사항별 점수를 담는 재사용 버퍼입니다.</summary>
        private readonly float[] _scores = new float[ConsiderationCount];
        /// <summary>고려사항별 가중치를 담는 재사용 버퍼입니다.</summary>
        private readonly float[] _weights = new float[ConsiderationCount];

        /// <summary>근접성 곡선입니다. 가까운 것을 크게 벌리고 먼 것은 비슷하게 눕힙니다.</summary>
        private static readonly ResponseCurve ProximityCurve = ResponseCurve.SharpFalloff;
        /// <summary>고립 곡선입니다. 아군 영향력이 낮을수록 좋게 봅니다.</summary>
        private static readonly ResponseCurve IsolationCurve = ResponseCurve.Decreasing;
        /// <summary>개활지 곡선입니다. 초크 점수가 높을수록 나쁘게 봅니다.</summary>
        private static readonly ResponseCurve OpenGroundCurve = ResponseCurve.Decreasing;

        // ====================================================================================================
        // 4. Public Methods - Candidates
        // ====================================================================================================

        /// <summary>
        /// 부대의 중심에서 목표 후보를 모읍니다. <b>부대 하나가 후보 하나입니다.</b>
        ///
        /// <b>왜 병사가 아니라 부대인가</b>
        ///
        /// 예전에는 살아 있는 적 병사가 선 칸을 전부 후보로 올렸습니다.
        /// 여섯 명짜리 분대 셋이면 후보가 열여덟 개인데, 그중 열다섯은
        /// <b>같은 분대를 가리키는 사실상 같은 자리</b>입니다. 점수를 열여덟 번 내고
        /// 열다섯 번은 이미 아는 답을 다시 냅니다.
        ///
        /// 결과도 이쪽이 낫습니다. 이 판단이 답하려는 질문은
        /// "어느 <b>부대</b>를 칠 것인가"이지 "어느 병사가 선 칸으로 갈 것인가"가 아닙니다.
        /// 병사의 칸을 노리면 행군 중 뒤처진 한 명을 향해 분대 전체가 방향을 잡는 일이 생기는데,
        /// 앵커는 그 부대가 <b>자리 잡으려는 곳</b>이라 겨눌 자리로 더 옳습니다.
        ///
        /// 통행할 수 없는 자리는 결격이라 아예 후보에 올리지 않습니다.
        /// 점수 단계에서도 걸러지지만, 그때는 이미 값을 계산한 뒤입니다.
        /// </summary>
        /// <param name="anchors">부대들의 중심 월드 좌표입니다.</param>
        /// <param name="grid">지형입니다. 좌표 변환과 통행 판정에 씁니다.</param>
        /// <param name="result">후보가 채워집니다. 호출 시 비워집니다.</param>
        /// <returns>모인 후보 수입니다.</returns>
        public static int CollectCandidates(
            IReadOnlyList<Vector3> anchors, IslandGrid grid, List<GoalCandidate> result)
        {
            result.Clear();

            if (anchors == null || grid == null)
            {
                return 0;
            }

            for (int i = 0; i < anchors.Count; i++)
            {
                var coord = grid.WorldToCoord(anchors[i]);
                var tile = grid.GetTile(coord);

                if (tile == null || !tile.IsWalkable)
                {
                    continue;
                }

                result.Add(new GoalCandidate(coord));
            }

            return result.Count;
        }

        // ====================================================================================================
        // 5. Public Methods - Selection
        // ====================================================================================================

        /// <summary>
        /// 후보 중 가장 점수가 높은 목표를 고릅니다.
        /// </summary>
        /// <param name="from">판단 주체의 현재 격자 위치입니다.</param>
        /// <param name="candidates">평가할 목표들입니다.</param>
        /// <param name="grid">지형입니다. 초크포인트 평가에 씁니다.</param>
        /// <param name="threat">플레이어 위협 영향력 맵입니다. null이면 방어 고려를 건너뜁니다.</param>
        /// <param name="weights">가중치입니다.</param>
        /// <param name="best">선택된 목표입니다.</param>
        /// <param name="bestScore">그 목표의 점수입니다.</param>
        /// <returns>고를 수 있는 목표가 있었으면 true입니다.</returns>
        public bool TrySelectGoal(
            GridCoord from,
            IReadOnlyList<GoalCandidate> candidates,
            IslandGrid grid,
            InfluenceMap threat,
            Weights weights,
            out GoalCandidate best,
            out float bestScore)
        {
            best = default;
            bestScore = 0f;

            if (candidates == null || candidates.Count == 0 || grid == null)
            {
                return false;
            }

            bool found = false;

            for (int i = 0; i < candidates.Count; i++)
            {
                float score = ScoreCandidate(from, candidates[i], grid, threat, weights);

                if (score > bestScore || !found)
                {
                    bestScore = score;
                    best = candidates[i];
                    found = true;
                }
            }

            return found && bestScore > 0f;
        }

        /// <summary>
        /// 후보 하나의 점수를 냅니다. 디버그 표시에서 개별 점수를 보고 싶을 때도 씁니다.
        /// </summary>
        /// <param name="from">판단 주체의 현재 격자 위치입니다.</param>
        /// <param name="candidate">점수를 낼 목표 후보입니다.</param>
        /// <param name="grid">지형입니다. 통행 판정과 초크 점수를 읽습니다.</param>
        /// <param name="threat">플레이어 위협 영향력 맵입니다. null이면 고립 고려를 건너뜁니다.</param>
        /// <param name="weights">고려사항별 가중치입니다.</param>
        /// <returns>0~1 유용도입니다. 갈 수 없는 곳이면 0입니다.</returns>
        public float ScoreCandidate(
            GridCoord from,
            GoalCandidate candidate,
            IslandGrid grid,
            InfluenceMap threat,
            Weights weights)
        {
            var tile = grid.GetTile(candidate.Coord);
            if (tile == null || !tile.IsWalkable)
            {
                // 갈 수 없는 곳은 결격입니다.
                return 0f;
            }

            // 1. 근접성 — 맨해튼 거리를 0~1로 정규화한 뒤 급감 곡선을 태웁니다.
            float distance = GridCoord.ManhattanDistance(from, candidate.Coord);
            _scores[0] = ProximityCurve.Evaluate(Mathf.Clamp01(distance / FarDistance));

            // 2. 고립 — 그 자리의 아군 영향력이 낮을수록, 즉 도와줄 부대가 멀수록 좋습니다.
            float normalizedThreat = threat != null ? threat.SampleNormalized(candidate.Coord) : 0f;
            _scores[1] = IsolationCurve.Evaluate(normalizedThreat);

            // 3. 개활지 — 초크 점수가 낮을수록 좋습니다.
            _scores[2] = OpenGroundCurve.Evaluate(tile.ChokeScore);

            _weights[0] = weights.Proximity;
            _weights[1] = weights.Isolation;
            _weights[2] = weights.OpenGround;

            return UtilityScorer.CombineWeighted(_scores, _weights, ConsiderationCount);
        }
    }
}
