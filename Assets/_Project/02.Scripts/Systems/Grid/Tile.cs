using SRPG.Common;
using UnityEngine;

namespace SRPG.Systems.Grid
{
    /// <summary>
    /// 섬 격자의 한 칸입니다.
    /// 지형 정보와 함께 경로 탐색·AI 평가에 필요한 파생 정보를 미리 계산해 들고 있습니다.
    /// </summary>
    public sealed class Tile
    {
        // ====================================================================================================
        // 1. Terrain
        // ====================================================================================================

        /// <summary>격자 좌표입니다.</summary>
        public GridCoord Coord;

        /// <summary>지형 종류입니다.</summary>
        public TileType Type;

        /// <summary>고도 단계입니다. 0이 해수면 바로 위입니다.</summary>
        public int Height;

        /// <summary>타일 중심의 월드 좌표입니다. 매 프레임 재계산하지 않도록 캐시합니다.</summary>
        public Vector3 WorldCenter;

        // ====================================================================================================
        // 2. Derived Flags
        // ====================================================================================================

        /// <summary>지상 유닛이 점유할 수 있는지 여부입니다.</summary>
        public bool IsWalkable;

        /// <summary>물과 맞닿은 육지인지 여부입니다. 상륙 방어선 판단에 사용합니다.</summary>
        public bool IsCoastal;

        /// <summary>
        /// 이 타일이 속한 상륙 구역 ID입니다. 상륙 가능한 해변에만 0 이상의 값이 붙고, 그 외에는 -1입니다.
        /// </summary>
        public int LandingZoneId = -1;

        /// <summary>
        /// 걸어서 지나갈 수 있는 이웃의 수입니다. 값이 작을수록 초크포인트에 가깝습니다.
        /// 창병 배치 판단과 영향력 맵의 <c>ChokePointValue</c> 레이어의 기초 입력이 됩니다.
        /// </summary>
        public int WalkableNeighborCount;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>물 타일인지 여부입니다.</summary>
        public bool IsWater => Type == TileType.Water;

        /// <summary>
        /// 초크포인트 점수입니다. 통행 가능한 이웃이 적을수록 1에 가까워집니다.
        /// 4방향 기준이므로 이웃이 1개면 막다른 길, 2개면 통로입니다.
        /// </summary>
        public float ChokeScore
        {
            get
            {
                if (!IsWalkable)
                {
                    return 0f;
                }

                return Mathf.Clamp01(1f - (WalkableNeighborCount - 1) / 3f);
            }
        }

        public override string ToString() => $"Tile{Coord} {Type} h={Height}";
    }
}
