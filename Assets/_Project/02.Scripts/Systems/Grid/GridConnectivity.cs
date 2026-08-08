using System.Collections.Generic;
using SRPG.Common;

namespace SRPG.Systems.Grid
{
    /// <summary>
    /// 한 칸에서 <b>실제로 걸어 닿을 수 있는</b> 칸들을 셉니다.
    ///
    /// <b>왜 클래스인가</b>
    ///
    /// 전장 생성기는 장애물 덩이를 놓을 때마다 "이 배치가 땅을 갈라놓지 않는가"를 확인합니다.
    /// 최대 400번 시도하므로 검사도 400번 돕니다. 예전에는 그 한 번마다
    /// <c>HashSet</c>과 <c>Queue</c>를 새로 만들었고, 격자 전체의 파생 정보까지 다시 계산했습니다.
    /// 64×64 전장이면 수백만 번의 순회와 수백 개의 임시 컬렉션이 로딩 한 번에 쏟아집니다.
    ///
    /// 작업 배열을 인스턴스가 들고 재사용하면 그 비용이 사라집니다.
    /// <b>세대 스탬프</b>를 쓰므로 탐색을 시작할 때 배열을 비울 필요도 없습니다 —
    /// 비용이 격자 크기가 아니라 실제로 닿은 범위에 비례합니다.
    /// (같은 방식을 <c>GridPathfinder</c>가 A*에 쓰고 있습니다)
    ///
    /// <b>이동 규칙은 하나뿐입니다</b>
    ///
    /// 여기서 쓰는 규칙은 <see cref="TraversalRules"/>이고, 길찾기도 같은 것을 봅니다.
    /// 그래서 "생성기가 이어져 있다고 판단한 땅을 부대가 못 가는" 어긋남이 생기지 않습니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 자료구조라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public sealed class GridConnectivity
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        private readonly IslandGrid _grid;

        /// <summary>각 칸이 마지막으로 닿은 탐색 번호입니다. 현재 세대와 다르면 아직 안 닿은 것입니다.</summary>
        private readonly int[] _stamp;

        private readonly Queue<Tile> _frontier;

        private int _generation;

        // ====================================================================================================
        // 2. Constructor
        // ====================================================================================================

        public GridConnectivity(IslandGrid grid)
        {
            _grid = grid;
            _stamp = new int[grid.Width * grid.Depth];
            _frontier = new Queue<Tile>(64);
        }

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 시작 칸에서 걸어 닿을 수 있는 칸의 수입니다. 시작 칸 자신을 포함합니다.
        /// 시작이 통행 불가면 0입니다.
        /// </summary>
        public int CountReachable(Tile start)
        {
            return Flood(start, null);
        }

        /// <summary>
        /// 시작 칸에서 걸어 닿을 수 있는 칸들을 모읍니다.
        /// </summary>
        /// <param name="result">결과가 채워집니다. 호출 시 비워집니다.</param>
        /// <returns>모인 칸의 수입니다.</returns>
        public int CollectRegion(Tile start, List<Tile> result)
        {
            return Flood(start, result);
        }

        // ====================================================================================================
        // 4. Private Methods
        // ====================================================================================================

        private int Flood(Tile start, List<Tile> result)
        {
            result?.Clear();

            if (start == null || !start.IsWalkable)
            {
                return 0;
            }

            BeginSearch();
            _frontier.Clear();

            Mark(start);
            result?.Add(start);
            _frontier.Enqueue(start);

            int count = 1;

            while (_frontier.Count > 0)
            {
                var current = _frontier.Dequeue();

                for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                {
                    var neighbor = _grid.GetTile(current.Coord + GridCoord.Neighbors4[n]);

                    if (!TraversalRules.CanStep(current, neighbor) || IsMarked(neighbor))
                    {
                        continue;
                    }

                    Mark(neighbor);
                    result?.Add(neighbor);
                    _frontier.Enqueue(neighbor);

                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 새 탐색을 시작합니다. 스탬프 배열은 <b>비우지 않습니다.</b>
        ///
        /// 세대가 한 바퀴 돌면 옛 스탬프가 현재 세대와 우연히 같아질 수 있습니다.
        /// 실제로 도달하려면 수십억 번 탐색해야 하지만, 그때 나는 버그는 재현이 불가능합니다.
        /// </summary>
        private void BeginSearch()
        {
            if (_generation == int.MaxValue)
            {
                System.Array.Clear(_stamp, 0, _stamp.Length);
                _generation = 0;
            }

            _generation++;
        }

        private int IndexOf(Tile tile) => tile.Coord.Y * _grid.Width + tile.Coord.X;

        private bool IsMarked(Tile tile) => _stamp[IndexOf(tile)] == _generation;

        private void Mark(Tile tile) => _stamp[IndexOf(tile)] = _generation;
    }
}
