using System;
using System.Collections.Generic;
using SRPG.Common;
using SRPG.Systems.Grid;

namespace SRPG.Systems.Spatial
{
    /// <summary>
    /// 타일 한 칸을 한 주체가 점유하도록 관리합니다.
    ///
    /// <b>왜 필요한가</b>
    ///
    /// 분대 둘이 같은 타일을 목적지로 삼으면 두 진형이 같은 중심을 공유하게 됩니다.
    /// 병사들이 서로 밀어내며 뒤엉키고, 어느 분대가 어디 있는지 눈으로 구분할 수 없게 되며,
    /// 무엇보다 "한 칸을 누가 지키는가"라는 이 게임의 기본 단위가 무너집니다.
    ///
    /// 점유 대상은 <b>목적지 타일</b>입니다. 이동 중 서로 스쳐 지나가는 것은 막지 않습니다.
    /// 그건 물리적으로 자연스럽고, 막으려 들면 좁은 길에서 분대가 영영 못 지나갑니다.
    ///
    /// 이미 점유된 칸을 요청받으면 거절하지 않고 <b>가장 가까운 빈 칸으로 보정</b>합니다.
    /// 한 칸 빗나간 클릭 때문에 명령이 통째로 씹히면 조작이 고장 난 것처럼 느껴지기 때문입니다.
    ///
    /// <b>왜 제네릭인가</b>
    ///
    /// 같은 규칙이 플레이어 분대와 적 분대 양쪽에 필요합니다.
    /// 다만 <b>진영마다 별도 인스턴스</b>를 씁니다. 하나로 합치면 적이 플레이어가 선 칸을
    /// 목적지로 삼을 수 없게 되는데, 그건 "공격하지 말라"는 뜻이 되어 버립니다.
    /// </summary>
    /// <typeparam name="TOwner">점유 주체입니다. 이 클래스는 주체의 타입을 알 필요가 없습니다.</typeparam>
    public sealed class TileOccupancy<TOwner> where TOwner : class
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>빈 칸을 찾을 때 원래 목적지에서 벗어날 수 있는 최대 격자 거리입니다.</summary>
        public const int DefaultSearchRadius = 4;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly Dictionary<GridCoord, TOwner> _claims = new Dictionary<GridCoord, TOwner>();
        private readonly Dictionary<TOwner, GridCoord> _claimedBy = new Dictionary<TOwner, GridCoord>();

        // 탐색용 재사용 버퍼입니다. 명령을 내릴 때마다 할당하지 않기 위해 필드로 둡니다.
        /// <summary>빈 칸을 찾는 너비 우선 탐색의 대기열입니다.</summary>
        private readonly Queue<GridCoord> _searchQueue = new Queue<GridCoord>();

        /// <summary>같은 칸을 두 번 보지 않도록 표시해 두는 집합입니다.</summary>
        private readonly HashSet<GridCoord> _searchVisited = new HashSet<GridCoord>();

        /// <summary>사라진 주체가 붙잡고 있던 칸을 모아 두는 버퍼입니다. 순회 도중 삭제하지 않기 위한 것입니다.</summary>
        private readonly List<GridCoord> _staleBuffer = new List<GridCoord>(8);

        /// <summary>
        /// 주체가 더 이상 유효하지 않은지 판정합니다.
        ///
        /// <b>이걸 주입받는 이유</b>가 있습니다. 주체가 <c>UnityEngine.Object</c>면
        /// 파괴된 뒤에도 참조는 남고, null 비교는 유니티가 <b>연산자를 덮어써서</b> 처리합니다.
        /// 그런데 제네릭 안에서 <c>owner == null</c> 은 그 연산자를 타지 않고 순수 참조 비교가 됩니다.
        /// 그래서 파괴된 분대가 칸을 영원히 붙잡는 일이 생깁니다.
        /// 판정을 호출부(구체 타입을 아는 쪽)에 맡기면 그 함정이 사라집니다.
        /// </summary>
        private readonly Predicate<TOwner> _isStale;

        // ====================================================================================================
        // 3. Constructor
        // ====================================================================================================

        /// <param name="isStale">
        /// 주체가 사라졌거나 소멸했는지 판정하는 함수입니다.
        /// 구체 타입을 아는 쪽에서 넘겨야 유니티의 null 처리가 제대로 동작합니다.
        /// </param>
        public TileOccupancy(Predicate<TOwner> isStale)
        {
            _isStale = isStale ?? (owner => owner == null);
        }

        // ====================================================================================================
        // 4. Public Methods - Query
        // ====================================================================================================

        /// <summary>
        /// 해당 칸을 점유한 주체를 반환합니다.
        /// </summary>
        /// <param name="coord">확인할 격자 좌표입니다.</param>
        /// <returns>그 칸을 점유한 주체입니다. 비어 있거나 주체가 사라졌으면 null입니다.</returns>
        public TOwner GetOccupant(GridCoord coord)
        {
            return _claims.TryGetValue(coord, out var owner) && !_isStale(owner) ? owner : null;
        }

        /// <summary>
        /// <paramref name="asker"/> 이외의 주체가 그 칸을 점유하고 있는지 확인합니다.
        /// </summary>
        /// <param name="coord">확인할 격자 좌표입니다.</param>
        /// <param name="asker">묻는 주체입니다. 자기가 점유한 칸은 막힌 것으로 보지 않습니다.</param>
        /// <returns>다른 주체가 점유하고 있으면 true입니다.</returns>
        public bool IsBlockedFor(GridCoord coord, TOwner asker)
        {
            var occupant = GetOccupant(coord);
            return occupant != null && !ReferenceEquals(occupant, asker);
        }

        /// <summary>
        /// 주체가 현재 붙잡고 있는 칸을 반환합니다.
        /// </summary>
        /// <param name="owner">조회할 주체입니다.</param>
        /// <param name="coord">붙잡고 있는 칸입니다. 없으면 <see cref="GridCoord.Invalid"/>입니다.</param>
        /// <returns>점유한 칸이 있으면 true입니다.</returns>
        public bool TryGetClaim(TOwner owner, out GridCoord coord)
        {
            return _claimedBy.TryGetValue(owner, out coord);
        }

        // ====================================================================================================
        // 5. Public Methods - Claim
        // ====================================================================================================

        /// <summary>
        /// 칸을 점유합니다. 이 주체가 이전에 점유하던 칸은 자동으로 해제됩니다.
        /// </summary>
        /// <param name="coord">점유할 격자 좌표입니다.</param>
        /// <param name="owner">점유하는 주체입니다.</param>
        public void Claim(GridCoord coord, TOwner owner)
        {
            if (owner == null)
            {
                return;
            }

            Release(owner);

            _claims[coord] = owner;
            _claimedBy[owner] = coord;
        }

        /// <summary>
        /// 주체의 점유를 해제합니다. 주체가 소멸할 때 반드시 호출해야 합니다.
        /// </summary>
        /// <param name="owner">점유를 놓을 주체입니다.</param>
        public void Release(TOwner owner)
        {
            if (owner == null || !_claimedBy.TryGetValue(owner, out var previous))
            {
                return;
            }

            // 다른 주체가 이미 그 칸을 가져갔을 수 있으므로 주인이 맞는지 확인하고 지웁니다.
            if (_claims.TryGetValue(previous, out var current) && ReferenceEquals(current, owner))
            {
                _claims.Remove(previous);
            }

            _claimedBy.Remove(owner);
        }

        /// <summary>
        /// 사라진 주체의 점유를 정리합니다.
        /// </summary>
        public void PruneStale()
        {
            _staleBuffer.Clear();

            foreach (var pair in _claims)
            {
                if (_isStale(pair.Value))
                {
                    _staleBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < _staleBuffer.Count; i++)
            {
                if (_claims.TryGetValue(_staleBuffer[i], out var owner) && owner != null)
                {
                    _claimedBy.Remove(owner);
                }

                _claims.Remove(_staleBuffer[i]);
            }
        }

        // ====================================================================================================
        // 6. Public Methods - Resolution
        // ====================================================================================================

        /// <summary>
        /// 목적지를 확정합니다. 원하는 칸이 비어 있으면 그대로, 이미 점유되어 있으면 가장 가까운 빈 칸을 찾습니다.
        ///
        /// 너비 우선으로 바깥으로 퍼져 나가며 찾으므로, 결과는 항상 원래 목적지에서 가장 가까운 칸입니다.
        /// </summary>
        /// <param name="desired">원하는 칸입니다.</param>
        /// <param name="asker">요청하는 주체입니다. 자기 자신이 점유한 칸은 막힌 것으로 보지 않습니다.</param>
        /// <param name="grid">지형입니다. 통행 가능 여부 판정에 씁니다.</param>
        /// <param name="resolved">확정된 목적지입니다.</param>
        /// <param name="searchRadius">탐색 반경입니다.</param>
        /// <returns>목적지를 찾았으면 true입니다. 주변이 전부 막혀 있으면 false입니다.</returns>
        public bool TryResolveDestination(
            GridCoord desired,
            TOwner asker,
            IslandGrid grid,
            out GridCoord resolved,
            int searchRadius = DefaultSearchRadius)
        {
            resolved = desired;

            if (grid == null)
            {
                return false;
            }

            PruneStale();

            if (IsAcceptable(desired, asker, grid))
            {
                return true;
            }

            _searchQueue.Clear();
            _searchVisited.Clear();

            _searchQueue.Enqueue(desired);
            _searchVisited.Add(desired);

            while (_searchQueue.Count > 0)
            {
                var current = _searchQueue.Dequeue();

                for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                {
                    var next = current + GridCoord.Neighbors4[n];

                    if (_searchVisited.Contains(next))
                    {
                        continue;
                    }

                    if (GridCoord.ManhattanDistance(desired, next) > searchRadius)
                    {
                        continue;
                    }

                    _searchVisited.Add(next);

                    if (IsAcceptable(next, asker, grid))
                    {
                        resolved = next;
                        return true;
                    }

                    // 통행 가능한 칸만 통과 경로로 씁니다.
                    // 그래야 바다 건너 반대편 해안 같은 엉뚱한 칸이 나오지 않습니다.
                    var tile = grid.GetTile(next);
                    if (tile != null && tile.IsWalkable)
                    {
                        _searchQueue.Enqueue(next);
                    }
                }
            }

            return false;
        }

        // ====================================================================================================
        // 7. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 목적지로 쓸 수 있는 칸인지 확인합니다. 통행 가능하고, 다른 주체가 점유하지 않아야 합니다.
        /// </summary>
        private bool IsAcceptable(GridCoord coord, TOwner asker, IslandGrid grid)
        {
            var tile = grid.GetTile(coord);
            if (tile == null || !tile.IsWalkable)
            {
                return false;
            }

            return !IsBlockedFor(coord, asker);
        }
    }
}
