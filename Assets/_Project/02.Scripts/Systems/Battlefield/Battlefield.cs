using SRPG.Data;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Systems.Battlefield
{
    /// <summary>
    /// 만들어진 전장 하나입니다. <b>보이는 지형</b>과 <b>게임 규칙</b>을 함께 들고 있습니다.
    ///
    /// <b>둘을 함께 두는 이유</b>
    ///
    /// 타일은 지형을 읽어서 만들어집니다 — 높이도 통행 여부도 하이트맵에서 나옵니다.
    /// 그래서 둘은 언제나 짝입니다. 하나만 들고 다니면
    /// "격자는 새것인데 그려진 지형은 옛것" 같은 상태가 생깁니다.
    ///
    /// <b>여기에 유니티 터레인은 없습니다</b>
    ///
    /// 터레인은 씬 오브젝트라 헤드리스 테스트에서 만들 수 없습니다.
    /// 숫자와 규칙만 여기 두고, 실제 터레인을 세우는 일은 씬 쪽이 맡습니다.
    /// 덕분에 "이 전장이 걸어 다닐 만한가"를 씬 없이 검사할 수 있습니다.
    /// </summary>
    public sealed class Battlefield
    {
        // ====================================================================================================
        // 1. Properties
        // ====================================================================================================

        /// <summary>길찾기·점유·AI가 보는 타일 격자입니다.</summary>
        public IslandGrid Grid { get; }

        /// <summary>유니티 터레인이 그대로 받아 쓰는 연속 지형 높이입니다.</summary>
        public BattlefieldHeightmap Heightmap { get; }

        /// <summary>터레인이 놓일 월드 원점입니다. 격자와 어긋나면 유닛이 허공에 섭니다.</summary>
        public Vector3 Origin { get; }

        /// <summary>이 전장의 지형 종류입니다. 월드맵이 골라 준 값입니다.</summary>
        public TerrainKind Terrain { get; }

        /// <summary>해수면의 월드 높이입니다.</summary>
        public float SeaLevel => Origin.y + Heightmap.SeaLevel;

        /// <summary>전장의 한 변 길이입니다.</summary>
        public float WorldSize => Heightmap.WorldSize;

        // ====================================================================================================
        // 2. Constructor
        // ====================================================================================================

        public Battlefield(IslandGrid grid, BattlefieldHeightmap heightmap, Vector3 origin, TerrainKind terrain)
        {
            Grid = grid;
            Heightmap = heightmap;
            Origin = origin;
            Terrain = terrain;
        }
    }
}
