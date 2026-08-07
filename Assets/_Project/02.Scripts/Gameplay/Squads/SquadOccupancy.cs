using System.Collections.Generic;
using SRPG.Common;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Gameplay.Squads
{
    /// <summary>
    /// 타일 한 칸을 분대 하나가 점유하도록 관리합니다.
    ///
    /// 왜 필요한가:
    /// 분대 둘이 같은 타일을 목적지로 삼으면 두 진형이 같은 중심을 공유하게 됩니다.
    /// 병사들이 서로 밀어내며 뒤엉키고, 어느 분대가 어디 있는지 눈으로 구분할 수 없게 되며,
    /// 무엇보다 "한 칸을 누가 지키는가"라는 이 게임의 기본 단위가 무너집니다.
    ///
    /// 점유 대상은 <b>목적지 타일</b>입니다. 이동 중 서로 스쳐 지나가는 것은 막지 않습니다.
    /// 그건 물리적으로 자연스럽고, 막으려 들면 좁은 길에서 분대가 영영 못 지나갑니다.
    ///
    /// 이미 점유된 칸을 명령받으면 거절하지 않고 <b>가장 가까운 빈 칸으로 보정</b>합니다.
    /// 한 칸 빗나간 클릭 때문에 명령이 통째로 씹히면 조작이 고장 난 것처럼 느껴지기 때문입니다.
    /// </summary>
    public sealed class SquadOccupancy
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>빈 칸을 찾을 때 원래 목적지에서 벗어날 수 있는 최대 격자 거리입니다.</summary>
        private const int DefaultSearchRadius = 4;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly Dictionary<GridCoord, Squad> _claims = new Dictionary<GridCoord, Squad>();
        private readonly Dictionary<Squad, GridCoord> _claimedBy = new Dictionary<Squad, GridCoord>();

        // 탐색용 재사용 버퍼입니다. 명령을 내릴 때마다 할당하지 않기 위해 필드로 둡니다.
        private readonly Queue<GridCoord> _searchQueue = new Queue<GridCoord>();
        private readonly HashSet<GridCoord> _searchVisited = new HashSet<GridCoord>();

        // ====================================================================================================
        // 3. Public Methods - Query
        // ====================================================================================================

        /// <summary>
        /// 해당 칸을 점유한 분대를 반환합니다. 비어 있으면 null입니다.
        /// </summary>
        public Squad GetOccupant(GridCoord coord)
        {
            return _claims.TryGetValue(coord, out var squad) && squad != null && !squad.IsDestroyed ? squad : null;
        }

        /// <summary>
        /// <paramref name="asker"/> 이외의 분대가 이 칸을 점유하고 있는지 확인합니다.
        /// </summary>
        public bool IsBlockedFor(GridCoord coord, Squad asker)
        {
            var occupant = GetOccupant(coord);
            return occupant != null && occupant != asker;
        }

        /// <summary>
        /// 분대가 현재 점유 중인 칸을 반환합니다.
        /// </summary>
        public bool TryGetClaim(Squad squad, out GridCoord coord)
        {
            return _claimedBy.TryGetValue(squad, out coord);
        }

        // ====================================================================================================
        // 4. Public Methods - Claim
        // ====================================================================================================

        /// <summary>
        /// 칸을 점유합니다. 이 분대가 이전에 점유하던 칸은 자동으로 해제됩니다.
        /// </summary>
        public void Claim(GridCoord coord, Squad squad)
        {
            if (squad == null)
            {
                return;
            }

            Release(squad);

            _claims[coord] = squad;
            _claimedBy[squad] = coord;
        }

        /// <summary>
        /// 분대의 점유를 해제합니다. 분대가 소멸할 때 반드시 호출해야 합니다.
        /// </summary>
        public void Release(Squad squad)
        {
            if (squad == null || !_claimedBy.TryGetValue(squad, out var previous))
            {
                return;
            }

            // 다른 분대가 이미 그 칸을 가져갔을 수 있으므로 주인이 맞는지 확인하고 지웁니다.
            if (_claims.TryGetValue(previous, out var owner) && owner == squad)
            {
                _claims.Remove(previous);
            }

            _claimedBy.Remove(squad);
        }

        /// <summary>
        /// 소멸했거나 사라진 분대의 점유를 정리합니다.
        /// </summary>
        public void PruneDestroyed()
        {
            var stale = new List<GridCoord>();

            foreach (var pair in _claims)
            {
                if (pair.Value == null || pair.Value.IsDestroyed)
                {
                    stale.Add(pair.Key);
                }
            }

            for (int i = 0; i < stale.Count; i++)
            {
                if (_claims.TryGetValue(stale[i], out var squad) && squad != null)
                {
                    _claimedBy.Remove(squad);
                }

                _claims.Remove(stale[i]);
            }
        }

        // ====================================================================================================
        // 5. Public Methods - Resolution
        // ====================================================================================================

        /// <summary>
        /// 목적지를 확정합니다. 원하는 칸이 비어 있으면 그대로, 이미 점유되어 있으면 가장 가까운 빈 칸을 찾습니다.
        ///
        /// 너비 우선으로 바깥으로 퍼져 나가며 찾으므로, 결과는 항상 원래 목적지에서 가장 가까운 칸입니다.
        /// </summary>
        /// <param name="desired">플레이어가 찍은 칸입니다.</param>
        /// <param name="asker">명령을 받는 분대입니다. 자기 자신이 점유한 칸은 막힌 것으로 보지 않습니다.</param>
        /// <param name="grid">지형입니다. 통행 가능 여부 판정에 씁니다.</param>
        /// <param name="resolved">확정된 목적지입니다.</param>
        /// <param name="searchRadius">탐색 반경입니다.</param>
        /// <returns>목적지를 찾았으면 true입니다. 주변이 전부 막혀 있으면 false입니다.</returns>
        public bool TryResolveDestination(
            GridCoord desired,
            Squad asker,
            IslandGrid grid,
            out GridCoord resolved,
            int searchRadius = DefaultSearchRadius)
        {
            resolved = desired;

            if (grid == null)
            {
                return false;
            }

            PruneDestroyed();

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
        // 6. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 목적지로 쓸 수 있는 칸인지 확인합니다. 통행 가능하고, 다른 분대가 점유하지 않아야 합니다.
        /// </summary>
        private bool IsAcceptable(GridCoord coord, Squad asker, IslandGrid grid)
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
