using System.Collections.Generic;
using SRPG.Common;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Systems.AI
{
    /// <summary>목표의 종류입니다. 가치 평가의 기준이 됩니다.</summary>
    public enum GoalKind
    {
        /// <summary>가옥입니다. 침공의 최종 목적이므로 가치가 가장 높습니다.</summary>
        House = 0,

        /// <summary>플레이어 분대입니다. 길을 막고 있으므로 치워야 할 대상입니다.</summary>
        PlayerSquad = 1,
    }

    /// <summary>평가 대상이 되는 목표 하나입니다.</summary>
    public readonly struct GoalCandidate
    {
        /// <summary>목표 위치입니다.</summary>
        public readonly GridCoord Coord;

        /// <summary>목표의 종류입니다.</summary>
        public readonly GoalKind Kind;

        public GoalCandidate(GridCoord coord, GoalKind kind)
        {
            Coord = coord;
            Kind = kind;
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
    /// 여기서는 후보마다 네 가지를 <b>동시에</b> 따집니다.
    ///
    ///   1. 근접성   — 가까울수록 좋다 (급감 곡선: 멀면 다 비슷하게 시들하다)
    ///   2. 가치     — 가옥이 분대보다 중요하다
    ///   3. 방어 얇음 — 그 지점의 플레이어 영향력이 낮을수록 좋다
    ///   4. 개활지   — 초크포인트일수록 나쁘다 (좁은 길은 방어자에게 유리하다)
    ///
    /// <b>3번과 4번이 이 판단의 핵심입니다.</b>
    /// "가옥으로 가되 방어가 얇은 쪽으로"가 성립하면 그 결과가 곧 <b>측면 우회</b>입니다.
    /// 우회하라는 규칙을 따로 쓰지 않았는데 우회가 나오고, 플레이어는 전선을 나눠야 합니다.
    /// 조사에서 확인한 Bad North의 압박 구조가 여기서 만들어집니다.
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

            /// <summary>목표 종류의 가치 차이를 얼마나 반영할지입니다.</summary>
            public float Value;

            /// <summary>방어가 얇은 곳을 얼마나 선호할지입니다. 높으면 우회를 자주 시도합니다.</summary>
            public float Undefended;

            /// <summary>초크포인트를 얼마나 피할지입니다.</summary>
            public float OpenGround;

            /// <summary>기본 가중치입니다.</summary>
            public static Weights Default => new Weights
            {
                Proximity = 0.7f,
                Value = 1f,
                Undefended = 0.85f,
                OpenGround = 0.45f,
            };
        }

        // ====================================================================================================
        // 2. Constants
        // ====================================================================================================

        /// <summary>고려사항 개수입니다.</summary>
        private const int ConsiderationCount = 4;

        /// <summary>가옥의 기본 가치입니다.</summary>
        private const float HouseValue = 1f;

        /// <summary>플레이어 분대의 기본 가치입니다. 가옥보다 낮게 두어 우회를 유도합니다.</summary>
        private const float PlayerSquadValue = 0.55f;

        /// <summary>
        /// 근접성 평가에서 "충분히 멀다"고 보는 격자 거리입니다.
        /// 이보다 멀면 전부 비슷하게 시들한 것으로 취급합니다.
        /// </summary>
        private const float FarDistance = 40f;

        // ====================================================================================================
        // 3. Fields
        // ====================================================================================================

        private readonly float[] _scores = new float[ConsiderationCount];
        private readonly float[] _weights = new float[ConsiderationCount];

        private static readonly ResponseCurve ProximityCurve = ResponseCurve.SharpFalloff;
        private static readonly ResponseCurve UndefendedCurve = ResponseCurve.Decreasing;
        private static readonly ResponseCurve OpenGroundCurve = ResponseCurve.Decreasing;

        // ====================================================================================================
        // 4. Public Methods
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

            // 2. 가치 — 목표 종류에 따른 고정값입니다.
            _scores[1] = candidate.Kind == GoalKind.House ? HouseValue : PlayerSquadValue;

            // 3. 방어 얇음 — 위협이 낮을수록 좋습니다.
            float normalizedThreat = threat != null ? threat.SampleNormalized(candidate.Coord) : 0f;
            _scores[2] = UndefendedCurve.Evaluate(normalizedThreat);

            // 4. 개활지 — 초크 점수가 낮을수록 좋습니다.
            _scores[3] = OpenGroundCurve.Evaluate(tile.ChokeScore);

            _weights[0] = weights.Proximity;
            _weights[1] = weights.Value;
            _weights[2] = weights.Undefended;
            _weights[3] = weights.OpenGround;

            return UtilityScorer.CombineWeighted(_scores, _weights, ConsiderationCount);
        }
    }
}
