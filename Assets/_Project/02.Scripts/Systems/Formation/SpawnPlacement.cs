using System.Collections.Generic;
using SRPG.Common;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Systems.Formation
{
    /// <summary>
    /// 전투를 시작할 때 분대를 어느 칸에 세울지 고릅니다.
    ///
    /// <b>왜 조립 지점에서 떼어 내는가</b>
    ///
    /// 이건 조립이 아니라 <b>배치 알고리즘</b>입니다. 후보를 고르고, 정렬하고,
    /// 간격 조건으로 걸러 내고, 자리가 모자라면 조건을 풉니다.
    /// 부트스트랩 안에 있으면 "분대가 왜 저기 섰는가"를 씬을 재생해야만 볼 수 있습니다.
    ///
    /// 순수 계산으로 떼어 두면 좁은 섬·해안뿐인 섬·자리가 모자라는 경우를
    /// EditMode에서 직접 만들어 확인할 수 있습니다.
    /// </summary>
    public static class SpawnPlacement
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// 분대끼리 두는 최소 격자 간격입니다.
        ///
        /// 붙여 세우면 진형이 겹쳐 어느 분대가 어디 있는지 눈으로 구분할 수 없고,
        /// 병사들이 서로 밀어내며 뒤엉킵니다.
        /// </summary>
        public const int DefaultMinSpacing = 3;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 분대 초기 배치 칸을 고릅니다. 섬 중심에서 가까운 순으로, 서로 최소 간격을 두고 고릅니다.
        ///
        /// <b>간격은 지킬 수 있을 때만 지킵니다.</b>
        /// 섬이 좁아 자리가 모자라면 조건을 풀고 채웁니다.
        /// 여기서 빈손으로 돌아가면 그 분대는 전장에 아예 서지 못합니다 —
        /// 겹쳐 서는 것보다 나쁜 결과입니다.
        /// </summary>
        /// <param name="grid">지형입니다.</param>
        /// <param name="count">필요한 자리 수입니다.</param>
        /// <param name="result">고른 칸이 채워집니다. 호출 시 비워집니다.</param>
        /// <param name="minSpacing">분대끼리 둘 최소 격자 간격입니다.</param>
        /// <returns>실제로 고른 칸의 수입니다. 통행 가능한 땅이 모자라면 <paramref name="count"/>보다 적습니다.</returns>
        public static int SelectSquadTiles(
            IslandGrid grid, int count, List<Tile> result, int minSpacing = DefaultMinSpacing)
        {
            result.Clear();

            if (grid == null || count <= 0)
            {
                return 0;
            }

            var candidates = new List<Tile>(grid.WalkableTiles.Count);

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                var tile = grid.WalkableTiles[i];

                // 물가는 피해 안쪽 평지에 배치합니다.
                // 물가에 세우면 넉백 한 번에 병사가 빠져 죽습니다.
                if (tile.Type == TileType.Ground && !tile.IsCoastal)
                {
                    candidates.Add(tile);
                }
            }

            // 안쪽 평지가 없을 만큼 섬이 작으면 통행 가능한 아무 칸이나 씁니다.
            if (candidates.Count == 0)
            {
                candidates.AddRange(grid.WalkableTiles);
            }

            Vector3 center = grid.WorldCenter;

            candidates.Sort((a, b) =>
                (a.WorldCenter - center).sqrMagnitude.CompareTo((b.WorldCenter - center).sqrMagnitude));

            for (int i = 0; i < candidates.Count && result.Count < count; i++)
            {
                if (IsFarEnough(result, candidates[i], minSpacing))
                {
                    result.Add(candidates[i]);
                }
            }

            // 간격 조건 때문에 자리를 못 찾았으면 조건을 풀고 채웁니다.
            for (int i = 0; i < candidates.Count && result.Count < count; i++)
            {
                if (!result.Contains(candidates[i]))
                {
                    result.Add(candidates[i]);
                }
            }

            return result.Count;
        }

        /// <summary>
        /// <b>전개 구역 안에서</b> 분대 자리를 고릅니다. 야전의 배치 경로입니다.
        ///
        /// <b>왜 전장 중심 쪽부터 채우는가</b>
        ///
        /// 구역의 뒤쪽(자기 진영 끝)부터 채우면 부대가 서로 겹쳐 늘어서고,
        /// 앞줄이 붙는 동안 뒷줄은 한참을 걸어와야 합니다.
        /// 마주 보는 쪽부터 채우면 자연히 <b>전열이 먼저 서고 예비가 뒤에</b> 섭니다.
        ///
        /// 간격은 여기서도 지킬 수 있을 때만 지킵니다 — 좁은 전장에서 빈손으로 돌아가면
        /// 그 분대는 전투에 참가하지 못합니다.
        /// </summary>
        /// <param name="zone">이 진영의 전개 구역입니다.</param>
        /// <param name="facing">부대가 바라보는 지점입니다. 보통 전장 중심입니다.</param>
        /// <param name="count">필요한 자리 수입니다.</param>
        /// <param name="result">고른 칸이 채워집니다. 호출 시 비워집니다.</param>
        /// <param name="minSpacing">분대끼리 둘 최소 격자 간격입니다.</param>
        /// <returns>실제로 고른 칸의 수입니다.</returns>
        public static int SelectDeploymentTiles(
            IReadOnlyList<Tile> zone,
            Vector3 facing,
            int count,
            List<Tile> result,
            int minSpacing = DefaultMinSpacing)
        {
            result.Clear();

            if (zone == null || zone.Count == 0 || count <= 0)
            {
                return 0;
            }

            var candidates = new List<Tile>(zone.Count);

            for (int i = 0; i < zone.Count; i++)
            {
                if (zone[i] != null && zone[i].IsWalkable)
                {
                    candidates.Add(zone[i]);
                }
            }

            if (candidates.Count == 0)
            {
                return 0;
            }

            // 마주 보는 쪽에 가까운 칸부터 채웁니다.
            candidates.Sort((a, b) =>
                (a.WorldCenter - facing).sqrMagnitude.CompareTo((b.WorldCenter - facing).sqrMagnitude));

            for (int i = 0; i < candidates.Count && result.Count < count; i++)
            {
                if (IsFarEnough(result, candidates[i], minSpacing))
                {
                    result.Add(candidates[i]);
                }
            }

            for (int i = 0; i < candidates.Count && result.Count < count; i++)
            {
                if (!result.Contains(candidates[i]))
                {
                    result.Add(candidates[i]);
                }
            }

            return result.Count;
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>이미 고른 칸들에서 충분히 떨어져 있는지 확인합니다.</summary>
        private static bool IsFarEnough(List<Tile> chosen, Tile candidate, int minSpacing)
        {
            for (int i = 0; i < chosen.Count; i++)
            {
                if (GridCoord.ChebyshevDistance(candidate.Coord, chosen[i].Coord) < minSpacing)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
