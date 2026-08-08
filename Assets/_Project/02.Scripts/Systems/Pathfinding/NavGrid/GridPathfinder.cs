using System.Collections.Generic;
using SRPG.Common;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Systems.Pathfinding
{
    /// <summary>
    /// 타일 격자 위에서 A*로 경로를 찾습니다. <b>대각선을 포함한 8방향</b>입니다.
    ///
    /// <b>왜 8방향인가</b>
    ///
    /// 4방향만 쓰면 비스듬한 목적지로 갈 때 경로가 계단 모양이 됩니다.
    /// 앵커는 그 계단을 그대로 따라가므로 분대가 눈에 띄게 지그재그로 행군합니다.
    /// 실제로 사람이 그렇게 걷지 않고, 무엇보다 <b>거리가 최대 41% 길어집니다</b>.
    ///
    /// 대각선을 넣을 때 함께 따라오는 것이 세 가지입니다.
    ///   · 대각선 비용을 √2로 둔다 — 1로 두면 대각선이 공짜라 직선 구간도 지그재그로 갑니다
    ///   · 휴리스틱을 옥타일로 바꾼다 — 맨해튼은 8방향에서 과대평가라 최적 경로를 보장하지 않습니다
    ///   · <b>모서리 통과를 막는다</b> — 안 막으면 병사가 절벽 모서리를 대각으로 뚫고 지나갑니다
    ///
    /// 인스턴스가 내부 작업 배열을 재사용하므로, 매 프레임 여러 번 호출해도 힙 할당이 발생하지 않습니다.
    /// 다만 그 때문에 스레드 안전하지 않습니다. 에이전트별로 공유하되 같은 프레임 내 순차 호출을 전제로 합니다.
    ///
    /// 유닛 수가 수백 단위로 늘어나면 이 클래스를 플로우 필드로 교체합니다.
    /// (경로가 목적지당 1회만 계산되므로 대군에서 비용이 극적으로 낮아집니다)
    /// </summary>
    public sealed class GridPathfinder
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>고도 한 단계를 오르내릴 때 추가되는 이동 비용입니다. 경사를 우회하도록 유도합니다.</summary>
        private const float SlopeCost = 1.5f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly IslandGrid _grid;
        private readonly float[] _gScore;
        private readonly int[] _cameFrom;
        private readonly int[] _openHeap;
        private readonly float[] _fScore;

        /// <summary>
        /// 각 셀이 마지막으로 <b>초기화된</b> 탐색 번호입니다. 값이 현재 세대와 다르면 미방문으로 봅니다.
        /// </summary>
        private readonly int[] _visitedStamp;

        /// <summary>각 셀이 마지막으로 <b>닫힌</b> 탐색 번호입니다.</summary>
        private readonly int[] _closedStamp;

        /// <summary>현재 탐색 번호입니다. 탐색을 시작할 때마다 하나씩 올립니다.</summary>
        private int _generation;

        /// <summary>다듬기 전의 날것 경로를 담는 재사용 버퍼입니다.</summary>
        private readonly List<GridCoord> _rawPath = new List<GridCoord>(64);

        private int _openCount;

        // ====================================================================================================
        // 3. Constructor
        // ====================================================================================================

        public GridPathfinder(IslandGrid grid)
        {
            _grid = grid;

            int cellCount = grid.Width * grid.Depth;
            _gScore = new float[cellCount];
            _fScore = new float[cellCount];
            _cameFrom = new int[cellCount];
            _visitedStamp = new int[cellCount];
            _closedStamp = new int[cellCount];

            // 감소 키(decrease-key)를 쓰지 않고 같은 셀을 여러 번 밀어 넣는 방식이므로,
            // 힙에는 셀 하나가 이웃 수만큼 중복 적재될 수 있습니다.
            // 셀 수만큼만 잡으면 넓은 맵에서 힙이 넘칩니다.
            //
            // 8방향으로 넓히면서 이 배수도 4에서 8로 함께 올려야 했습니다.
            // 이웃 수를 늘리면서 여기를 잊으면 넓은 맵에서만, 그것도 가끔 터집니다.
            _openHeap = new int[cellCount * GridCoord.Neighbors8.Length + 1];
        }

        // ====================================================================================================
        // 4. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 두 타일 사이의 경로를 찾습니다.
        /// </summary>
        /// <param name="start">출발 좌표입니다.</param>
        /// <param name="goal">목표 좌표입니다.</param>
        /// <param name="result">찾은 경로가 출발지에서 목적지 순으로 채워집니다. 호출 시 비워집니다.</param>
        /// <returns>경로를 찾으면 true입니다.</returns>
        public bool TryFindPath(GridCoord start, GridCoord goal, List<GridCoord> result)
        {
            result.Clear();

            var startTile = _grid.GetTile(start);
            var goalTile = _grid.GetTile(goal);

            if (startTile == null || goalTile == null || !startTile.IsWalkable || !goalTile.IsWalkable)
            {
                return false;
            }

            if (start == goal)
            {
                result.Add(start);
                return true;
            }

            int startIndex = ToIndex(start);
            int goalIndex = ToIndex(goal);

            BeginSearch();

            _openCount = 0;
            Visit(startIndex, 0f, -1);
            _fScore[startIndex] = Heuristic(start, goal);
            HeapPush(startIndex);

            while (_openCount > 0)
            {
                int current = HeapPop();

                if (current == goalIndex)
                {
                    ReconstructPath(startIndex, goalIndex, result);
                    return true;
                }

                if (IsClosed(current))
                {
                    continue;
                }

                Close(current);

                var currentTile = _grid.GetTile(ToCoord(current));
                var currentCoord = currentTile.Coord;

                for (int n = 0; n < GridCoord.Neighbors8.Length; n++)
                {
                    var offset = GridCoord.Neighbors8[n];
                    var neighborCoord = currentCoord + offset;

                    var neighbor = _grid.GetTile(neighborCoord);

                    // 통행 가능 여부와 단차를 한 규칙으로 봅니다.
                    // 생성기의 연결성 검사와 격자의 이웃 계산이 같은 함수를 씁니다.
                    if (!TraversalRules.CanStep(currentTile, neighbor))
                    {
                        continue;
                    }

                    int heightDelta = Mathf.Abs(neighbor.Height - currentTile.Height);

                    bool isDiagonal = offset.X != 0 && offset.Y != 0;

                    // 모서리를 뚫고 지나가지 못하게 막습니다.
                    if (isDiagonal && !CanPassCorner(currentTile, currentCoord, offset))
                    {
                        continue;
                    }

                    int neighborIndex = ToIndex(neighborCoord);
                    if (IsClosed(neighborIndex))
                    {
                        continue;
                    }

                    float stepCost = isDiagonal ? GridCoord.DiagonalStepCost : 1f;
                    float tentative = _gScore[current] + stepCost + heightDelta * SlopeCost;

                    if (tentative >= GetCost(neighborIndex))
                    {
                        continue;
                    }

                    Visit(neighborIndex, tentative, current);
                    _fScore[neighborIndex] = tentative + Heuristic(neighborCoord, goal);
                    HeapPush(neighborIndex);
                }
            }

            return false;
        }

        /// <summary>
        /// 목적지가 통행 불가일 때 가장 가까운 통행 가능 타일로 보정한 뒤 경로를 찾습니다.
        /// 플레이어가 물이나 절벽을 클릭했을 때의 폴백 경로입니다.
        /// </summary>
        public bool TryFindPathSnapped(GridCoord start, GridCoord goal, List<GridCoord> result, out GridCoord resolvedGoal)
        {
            resolvedGoal = goal;

            var goalTile = _grid.GetTile(goal);
            if (goalTile == null || !goalTile.IsWalkable)
            {
                var fallback = _grid.FindNearestWalkable(_grid.CoordToWorld(goal));
                if (fallback == null)
                {
                    result.Clear();
                    return false;
                }

                resolvedGoal = fallback.Coord;
            }

            return TryFindPath(start, resolvedGoal, result);
        }

        // ====================================================================================================
        // 4-1. Public Methods - Smoothed
        // ====================================================================================================

        /// <summary>
        /// 경로를 찾은 뒤 <b>시야선 기준으로 다듬어</b> 돌려줍니다.
        ///
        /// 격자 경로는 45도의 배수로만 꺾입니다. 열린 평지를 비스듬히 가로지를 때도 몇 번씩 각지고,
        /// 앵커가 그 각을 그대로 따라갑니다. 보이는 곳까지는 직선으로 걷는 편이 자연스럽습니다.
        ///
        /// 결과는 더 이상 <b>인접한 칸의 나열이 아닙니다.</b> 경유점 사이가 여러 칸 떨어질 수 있습니다.
        /// 칸 단위로 무언가를 하려는 소비자는 <see cref="TryFindPath"/>의 날것을 써야 합니다.
        /// </summary>
        public bool TryFindSmoothedPath(GridCoord start, GridCoord goal, List<GridCoord> result)
        {
            if (!TryFindPath(start, goal, _rawPath))
            {
                result.Clear();
                return false;
            }

            PathSmoother.Smooth(_grid, _rawPath, result);
            return true;
        }

        /// <summary>
        /// 목적지를 보정한 뒤 경로를 찾고 시야선 기준으로 다듬습니다.
        /// 플레이어가 물이나 절벽을 클릭했을 때의 경로입니다.
        /// </summary>
        public bool TryFindSmoothedPathSnapped(
            GridCoord start,
            GridCoord goal,
            List<GridCoord> result,
            out GridCoord resolvedGoal)
        {
            if (!TryFindPathSnapped(start, goal, _rawPath, out resolvedGoal))
            {
                result.Clear();
                return false;
            }

            PathSmoother.Smooth(_grid, _rawPath, result);
            return true;
        }

        // ====================================================================================================
        // 5. Private Methods - Generation Stamping
        // ====================================================================================================

        /// <summary>
        /// 새 탐색을 시작합니다. 작업 배열을 <b>비우지 않습니다.</b>
        ///
        /// 예전에는 탐색을 시작할 때마다 <c>gScore</c>·<c>cameFrom</c>·<c>closed</c> 세 배열을
        /// 셀 수만큼 훑어 초기화했습니다. 그러면 <b>경로 길이와 무관하게</b> 매번 O(격자 크기)가 듭니다.
        /// 옆 칸으로 한 발짝 가는 경로를 구할 때도 64×64 격자면 12,288칸을 씁니다.
        ///
        /// 지금은 세대 번호만 올립니다. 셀의 스탬프가 현재 세대와 다르면 "이번 탐색에서 아직 안 건드림"이므로,
        /// 초기화는 <b>실제로 방문한 셀에 대해서만</b> 그 순간에 일어납니다.
        /// 비용이 격자 크기가 아니라 탐색 범위에 비례하게 됩니다.
        /// </summary>
        private void BeginSearch()
        {
            // 세대가 한 바퀴 돌면 옛 스탬프가 현재 세대와 우연히 같아질 수 있습니다.
            // 초당 수백 번 탐색해도 수십 일이 걸리지만, 그때 나는 버그는 재현이 불가능합니다.
            if (_generation == int.MaxValue)
            {
                System.Array.Clear(_visitedStamp, 0, _visitedStamp.Length);
                System.Array.Clear(_closedStamp, 0, _closedStamp.Length);
                _generation = 0;
            }

            _generation++;
        }

        /// <summary>셀을 이번 탐색에서 방문 처리하고 비용과 이전 셀을 기록합니다.</summary>
        private void Visit(int index, float cost, int cameFrom)
        {
            _visitedStamp[index] = _generation;
            _gScore[index] = cost;
            _cameFrom[index] = cameFrom;
        }

        /// <summary>
        /// 이번 탐색에서 이 셀까지의 최소 비용입니다. 아직 방문하지 않았으면 무한대로 봅니다.
        /// </summary>
        private float GetCost(int index)
        {
            return _visitedStamp[index] == _generation ? _gScore[index] : float.MaxValue;
        }

        private bool IsClosed(int index) => _closedStamp[index] == _generation;

        private void Close(int index) => _closedStamp[index] = _generation;

        // ====================================================================================================
        // 6. Private Methods - Path
        // ====================================================================================================

        private void ReconstructPath(int startIndex, int goalIndex, List<GridCoord> result)
        {
            int current = goalIndex;

            // 이번 세대에 기록된 셀만 따라갑니다.
            // 스탬프 방식에서는 옛 세대의 cameFrom 값이 배열에 그대로 남아 있으므로,
            // 확인 없이 따라가면 지난 탐색의 경로로 새어 나갈 수 있습니다.
            while (current != -1 && _visitedStamp[current] == _generation)
            {
                result.Add(ToCoord(current));
                if (current == startIndex)
                {
                    break;
                }

                current = _cameFrom[current];
            }

            result.Reverse();
        }

        /// <summary>
        /// 옥타일 거리 휴리스틱입니다. 8방향 이동에서 허용 가능(admissible)하므로 최적 경로를 보장합니다.
        ///
        /// 고도 비용은 무시합니다. 실제 비용을 <b>과소평가</b>하는 것은 허용 가능성을 해치지 않습니다.
        /// </summary>
        private static float Heuristic(GridCoord a, GridCoord b)
        {
            return GridCoord.OctileDistance(a, b);
        }

        /// <summary>
        /// 대각선으로 모서리를 뚫고 지나갈 수 있는지 확인합니다.
        ///
        /// <b>왜 필요한가</b>
        ///
        /// 대각선 이동을 그냥 허용하면 병사가 절벽 모서리를 <b>대각으로 스쳐 통과</b>합니다.
        /// 두 벽이 만나는 안쪽 모서리에서는 지나갈 틈이 없는데도 경로가 뚫립니다.
        /// 눈으로는 벽을 뚫고 지나간 것처럼 보이고, 물리 이동은 그 자리에서 막혀 유닛이 멈춰 섭니다.
        ///
        /// 그래서 대각선의 <b>양옆 두 칸이 모두</b> 지나갈 수 있어야 통과를 허용합니다.
        /// 한 칸만 요구하는 느슨한 규칙도 있지만, 그러면 모서리를 비스듬히 긁고 지나가는 그림이 남습니다.
        /// </summary>
        private bool CanPassCorner(Tile from, GridCoord fromCoord, GridCoord offset)
        {
            var sideX = _grid.GetTile(new GridCoord(fromCoord.X + offset.X, fromCoord.Y));
            var sideY = _grid.GetTile(new GridCoord(fromCoord.X, fromCoord.Y + offset.Y));

            return IsSideOpen(from, sideX) && IsSideOpen(from, sideY);
        }

        /// <summary>옆 칸이 열려 있는지 확인합니다. 통행 가능하고 고도 차도 넘을 수 있어야 합니다.</summary>
        private static bool IsSideOpen(Tile from, Tile side)
        {
            return TraversalRules.CanStep(from, side);
        }

        private int ToIndex(GridCoord coord) => coord.Y * _grid.Width + coord.X;

        private GridCoord ToCoord(int index) => new GridCoord(index % _grid.Width, index / _grid.Width);

        // ====================================================================================================
        // 7. Private Methods - Binary Min Heap
        // ====================================================================================================

        private void HeapPush(int cellIndex)
        {
            _openCount++;
            _openHeap[_openCount] = cellIndex;

            int child = _openCount;
            while (child > 1)
            {
                int parent = child / 2;
                if (_fScore[_openHeap[parent]] <= _fScore[_openHeap[child]])
                {
                    break;
                }

                (_openHeap[parent], _openHeap[child]) = (_openHeap[child], _openHeap[parent]);
                child = parent;
            }
        }

        private int HeapPop()
        {
            int result = _openHeap[1];
            _openHeap[1] = _openHeap[_openCount];
            _openCount--;

            int parent = 1;
            while (true)
            {
                int left = parent * 2;
                int right = left + 1;
                int smallest = parent;

                if (left <= _openCount && _fScore[_openHeap[left]] < _fScore[_openHeap[smallest]])
                {
                    smallest = left;
                }

                if (right <= _openCount && _fScore[_openHeap[right]] < _fScore[_openHeap[smallest]])
                {
                    smallest = right;
                }

                if (smallest == parent)
                {
                    break;
                }

                (_openHeap[parent], _openHeap[smallest]) = (_openHeap[smallest], _openHeap[parent]);
                parent = smallest;
            }

            return result;
        }
    }
}
