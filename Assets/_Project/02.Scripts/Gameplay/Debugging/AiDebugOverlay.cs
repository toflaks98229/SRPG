using System.Collections.Generic;
using SRPG.Common;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Enemies;
using SRPG.Systems.AI;
using UnityEngine;

namespace SRPG.Gameplay.Debugging
{
    /// <summary>
    /// AI가 무슨 판단을 했는지 씬 뷰에 그립니다.
    ///
    /// <b>왜 AI와 동시에 만드는가</b>
    ///
    /// 유틸리티 점수와 영향력 맵은 숫자입니다. 숫자만 보고 튜닝할 수 없습니다.
    /// "적이 왜 저기로 갔지?"에 답하려면 그 순간의 위협 분포와 후보 점수를 <b>눈으로</b> 봐야 합니다.
    /// 조사 보고서 5.2절이 AI 구현과 디버그 시각화를 함께 만들라고 한 이유입니다.
    ///
    /// 나중에 만들면 늦습니다. 그때는 이미 "왜 이러는지 모르겠는" 상태로 튜닝을 시작하게 됩니다.
    ///
    /// <b>기즈모로 그리는 이유</b>
    ///
    /// 게임 화면을 가리지 않고, 빌드에 아무것도 남지 않으며, 씬 뷰에서 자유롭게 돌려 볼 수 있습니다.
    /// 이건 개발자를 위한 도구지 플레이어를 위한 화면이 아닙니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AiDebugOverlay : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Inspector
        // ====================================================================================================

        [Header("표시 대상")]
        [SerializeField]
        [Tooltip("플레이어 위협 영향력을 타일 색으로 그립니다. 적이 '어디를 피하는지'를 보여 줍니다.")]
        private bool _showThreatMap = true;

        [SerializeField]
        [Tooltip("적 분대가 현재 어느 목표를 향하는지 선으로 그립니다.")]
        private bool _showEnemyGoals = true;

        [SerializeField]
        [Tooltip("타일의 초크포인트 점수를 그립니다. 위협 맵과 함께 켜면 겹쳐 보이니 따로 확인하세요.")]
        private bool _showChokePoints;

        [Header("표현")]
        [SerializeField]
        [Range(0.05f, 1f)]
        [Tooltip("타일 표시의 불투명도입니다.")]
        private float _tileOpacity = 0.45f;

        [SerializeField]
        [Range(0.01f, 1f)]
        [Tooltip("이 값보다 작은 영향력은 그리지 않습니다. 화면이 지저분해지는 것을 막습니다.")]
        private float _minimumToDraw = 0.06f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private BattleContext _context;
        private readonly List<EnemySquad> _squadBuffer = new List<EnemySquad>(16);

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 오버레이를 초기화합니다.
        /// </summary>
        public void Initialize(BattleContext context)
        {
            _context = context;
        }

        // ====================================================================================================
        // 4. Unity Lifecycle
        // ====================================================================================================

        private void OnDrawGizmos()
        {
            if (_context == null || _context.Grid == null)
            {
                return;
            }

            if (_showThreatMap)
            {
                DrawThreatMap();
            }

            if (_showChokePoints)
            {
                DrawChokePoints();
            }

            if (_showEnemyGoals)
            {
                DrawEnemyGoals();
            }
        }

        // ====================================================================================================
        // 5. Private Methods - Layers
        // ====================================================================================================

        /// <summary>
        /// 플레이어 위협을 타일 색으로 그립니다. 붉을수록 방어가 두꺼운 곳입니다.
        /// 적의 유틸리티 판단에서 "방어 얇음" 고려사항이 바로 이 값을 뒤집어 쓴 것입니다.
        /// </summary>
        private void DrawThreatMap()
        {
            var map = _context.GetThreatMap(Team.Player);
            if (map.IsEmpty)
            {
                return;
            }

            var grid = _context.Grid;
            Vector3 size = new Vector3(grid.CellSize * 0.9f, 0.05f, grid.CellSize * 0.9f);

            for (int i = 0; i < grid.AllTiles.Count; i++)
            {
                var tile = grid.AllTiles[i];
                if (!tile.IsWalkable)
                {
                    continue;
                }

                float value = map.SampleNormalized(tile.Coord);
                if (value < _minimumToDraw)
                {
                    continue;
                }

                // 낮으면 노랑, 높으면 빨강.
                Gizmos.color = new Color(1f, 1f - value, 0.1f, _tileOpacity * value);
                Gizmos.DrawCube(tile.WorldCenter + Vector3.up * 0.06f, size);
            }
        }

        /// <summary>
        /// 초크포인트를 그립니다. 파랄수록 좁은 길이며, 적은 이런 곳을 피하려 합니다.
        /// </summary>
        private void DrawChokePoints()
        {
            var grid = _context.Grid;
            Vector3 size = new Vector3(grid.CellSize * 0.55f, 0.05f, grid.CellSize * 0.55f);

            for (int i = 0; i < grid.AllTiles.Count; i++)
            {
                var tile = grid.AllTiles[i];
                if (!tile.IsWalkable)
                {
                    continue;
                }

                float value = tile.ChokeScore;
                if (value < _minimumToDraw)
                {
                    continue;
                }

                Gizmos.color = new Color(0.2f, 0.5f, 1f, _tileOpacity * value);
                Gizmos.DrawCube(tile.WorldCenter + Vector3.up * 0.12f, size);
            }
        }

        /// <summary>
        /// 적 분대가 어디로 가는지 그립니다. 목표 종류에 따라 색이 다릅니다.
        /// 우회가 실제로 일어나는지 확인하는 가장 빠른 방법입니다.
        /// </summary>
        private void DrawEnemyGoals()
        {
            _squadBuffer.Clear();
            GetComponentsInChildrenSafe(_squadBuffer);

            for (int i = 0; i < _squadBuffer.Count; i++)
            {
                var squad = _squadBuffer[i];
                if (squad == null || squad.IsDisbanded || !squad.GoalCoord.IsValid)
                {
                    continue;
                }

                Vector3 from = squad.AnchorPosition + Vector3.up * 0.5f;
                Vector3 to = _context.Grid.CoordToWorld(squad.GoalCoord) + Vector3.up * 0.5f;

                Gizmos.color = squad.GoalKind == GoalKind.House
                    ? new Color(1f, 0.4f, 0.2f)
                    : new Color(1f, 0.85f, 0.2f);

                Gizmos.DrawLine(from, to);
                Gizmos.DrawWireSphere(to, _context.Grid.CellSize * 0.35f);

                // 분대 규모를 구로 표시합니다. 커질수록 위협적입니다.
                Gizmos.DrawWireSphere(from, 0.25f + squad.AliveCount * 0.06f);
            }
        }

        /// <summary>
        /// 씬 전체에서 적 분대를 모읍니다.
        ///
        /// 기즈모는 에디터에서만 도는 표시 코드라 전역 탐색을 써도 됩니다.
        /// 런타임 코드였다면 레지스트리를 두었을 자리입니다.
        /// </summary>
        private static void GetComponentsInChildrenSafe(List<EnemySquad> buffer)
        {
            var found = FindObjectsByType<EnemySquad>(FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
            {
                buffer.Add(found[i]);
            }
        }
    }
}
