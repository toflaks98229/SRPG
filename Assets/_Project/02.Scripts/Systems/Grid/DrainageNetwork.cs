using SRPG.Common;

namespace SRPG.Systems.Grid
{
    /// <summary>
    /// 타일 고도가 통행 규칙을 지키게 만듭니다.
    ///
    /// <b>봉우리와 계곡은 여기서 만들지 않습니다</b>
    ///
    /// 예전에는 이 클래스가 봉우리를 세우고 물길을 그려 계곡을 팠습니다.
    /// 타일의 고도 단계가 먼저 정해지는 구조였기 때문에, 물길을 손으로 그려 넣는 것
    /// 말고는 방법이 없었습니다. 그건 시뮬레이션이 아니라 작도였습니다.
    ///
    /// 지금은 <see cref="SRPG.Systems.Landform.TerrainSimulation"/>이 물과 중력으로
    /// 지형을 먼저 만들고, 타일은 그 결과를 읽어 갈 뿐입니다.
    /// 봉우리도 골짜기도 거기서 나옵니다.
    ///
    /// 여기 남은 일은 하나입니다 — 읽어 온 값이 <b>걸어 다닐 수 있는지</b> 보장하는 것.
    /// </summary>
    public static class DrainageNetwork
    {
        /// <summary>
        /// 인접한 칸의 고도 차이가 1을 넘지 않게 만듭니다.
        ///
        /// <b>낮추기만 합니다.</b> 이것이 전부입니다.
        ///
        /// 평활화하듯 양쪽을 평균 내면 방금 판 계곡이 도로 메워집니다.
        /// 위반이 생기면 언제나 <b>높은 쪽</b>을 내리고, 계곡 바닥은 절대 건드리지 않습니다.
        ///
        /// 이 제약은 장식이 아닙니다. 통행 판정이 "고도차 1 이하"를 쓰므로,
        /// 여기서 어기면 섬이 걸어서 못 가는 조각으로 쪼개집니다.
        /// </summary>
        public static void EnforceStepLimit(int w, int d, bool[] isLand, int[] height)
        {
            bool changed = true;
            int guard = 0;

            while (changed && guard++ < 64)
            {
                changed = false;

                for (int y = 0; y < d; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;

                        if (!isLand[i])
                        {
                            continue;
                        }

                        for (int n = 0; n < GridCoord.Neighbors4.Length; n++)
                        {
                            int nx = x + GridCoord.Neighbors4[n].X;
                            int ny = y + GridCoord.Neighbors4[n].Y;

                            if (nx < 0 || nx >= w || ny < 0 || ny >= d)
                            {
                                continue;
                            }

                            int ni = ny * w + nx;

                            // 바다와 맞닿은 육지는 0단이어야 합니다. 아니면 물속에서 절벽이 솟습니다.
                            int neighborHeight = isLand[ni] ? height[ni] : 0;

                            if (height[i] - neighborHeight > 1)
                            {
                                height[i] = neighborHeight + 1;
                                changed = true;
                            }
                        }
                    }
                }
            }
        }

    }
}
