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

        /// <summary>
        /// 오를 수 있는 기울기의 한계입니다. 도 단위입니다.
        ///
        /// <b>왜 결과물에 남겨 두는가</b>
        ///
        /// 이 값은 생성이 끝나면 사라져도 될 것처럼 보입니다 — 절벽은 이미 타일에 새겨졌으니까요.
        /// 그런데 <b>화면</b>이 같은 숫자를 알아야 합니다. 지형 셰이더는 이 각도에서 암반을 드러내고,
        /// 그래야 눈에 보이는 바위와 실제로 막히는 곳이 같은 선에서 갈립니다.
        ///
        /// 셰이더에 34를 적어 두고 프로필을 40으로 고치면, 걸어 올라갈 수 있는 바위와
        /// 풀밭인데 막히는 자리가 생깁니다. 컴파일도 되고 테스트도 통과합니다.
        /// </summary>
        public float ClimbLimitDegrees { get; }

        /// <summary>해수면의 월드 높이입니다.</summary>
        public float SeaLevel => Origin.y + Heightmap.SeaLevel;

        /// <summary>전장의 한 변 길이입니다.</summary>
        public float WorldSize => Heightmap.WorldSize;

        /// <summary>
        /// <b>싸울 수 있는 땅</b>의 구석입니다. 격자의 원점과 같습니다.
        ///
        /// <see cref="Origin"/> 은 앞바다 바닥까지 포함한 <b>지형</b>의 구석이라 이보다 바깥입니다.
        /// 뭍 위에만 놓을 것(풀·소품)은 이쪽을 기준으로 삼아야 합니다 —
        /// 지형 전체를 훑으면 넉 배를 돌면서 그 대부분을 물속이라고 버리게 됩니다.
        /// </summary>
        public Vector3 PlayOrigin => new Vector3(
            Origin.x + Heightmap.PlayOffset,
            Origin.y,
            Origin.z + Heightmap.PlayOffset);

        /// <summary>싸울 수 있는 땅의 월드 크기입니다.</summary>
        public float PlayWorldSize => Heightmap.PlayWorldSize;

        // ====================================================================================================
        // 2. Constructor
        // ====================================================================================================

        public Battlefield(
            IslandGrid grid,
            BattlefieldHeightmap heightmap,
            Vector3 origin,
            TerrainKind terrain,
            float climbLimitDegrees = 34f)
        {
            Grid = grid;
            Heightmap = heightmap;
            Origin = origin;
            Terrain = terrain;
            ClimbLimitDegrees = climbLimitDegrees;
        }
    }
}
