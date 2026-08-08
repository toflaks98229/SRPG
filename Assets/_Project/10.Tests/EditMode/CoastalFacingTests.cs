using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Systems.Grid;
using SRPG.Tests.Support;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 해안선 조회를 검증합니다.
    ///
    /// 방어 부대가 어느 쪽을 보고 설지의 근거입니다.
    /// 예전에는 적이 없으면 월드 +Z를 보고 서 있었습니다. 아무 의미 없는 방향이라
    /// 창병 전열이 바다를 등지고 서는 그림이 나왔습니다.
    /// </summary>
    public sealed class CoastalFacingTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private static IslandGrid CreateIsland(int seed = 20260807)
        {
            return TestIsland.Create(seed);
        }

        // ====================================================================================================
        // 2. 해안선 목록
        // ====================================================================================================



    }
}
