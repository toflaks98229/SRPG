using UnityEngine;

namespace SRPG.Systems.Props
{
    /// <summary>
    /// 놓이기로 정해진 지형지물 하나입니다.
    ///
    /// <b>배치와 형상을 분리하기 위한 그릇입니다.</b>
    /// <see cref="PropPlacement"/>가 "어디에 무엇을"을 정하고,
    /// <see cref="PropMeshBuilder"/>가 "어떻게 생겼는지"를 만듭니다.
    /// 둘 사이를 이 구조체가 잇습니다.
    ///
    /// 나눠 두면 배치 규칙만 따로 테스트할 수 있습니다.
    /// 지오메트리가 섞여 있으면 "바위가 절벽 밑에 몰리는가" 같은 것을 확인할 수 없습니다.
    /// </summary>
    public struct PropInstance
    {
        /// <summary>지면과 닿는 지점입니다.</summary>
        public Vector3 GroundPosition;

        /// <summary>기울기를 포함한 방향입니다.</summary>
        public Quaternion Rotation;

        /// <summary>밑동의 반경입니다.</summary>
        public float Radius;

        /// <summary>지면 위로 솟는 높이입니다.</summary>
        public float Height;

        /// <summary>풍화도입니다. 0은 각진 바위, 1은 침식된 둔덕입니다.</summary>
        public float Weathering;

        /// <summary>
        /// 암반인지 여부입니다.
        ///
        /// 암반은 측면 벽과 같은 재질로 그려집니다. 즉 <b>못 딛는 것</b>으로 읽힙니다.
        /// 흙·이끼 둔덕은 윗면과 같은 재질이라 <b>딛는 땅의 일부</b>로 읽힙니다.
        /// 이 구분이 지형 판독의 규칙을 그대로 따르게 합니다.
        /// </summary>
        public bool IsRock;

        /// <summary>형상을 정하는 씨앗입니다. 자리마다 달라야 복제된 티가 나지 않습니다.</summary>
        public int Shape;
    }
}
