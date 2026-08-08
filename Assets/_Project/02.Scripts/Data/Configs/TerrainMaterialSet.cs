using System;
using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 지형 종류별 머티리얼 묶음입니다.
    ///
    /// 지면은 유니티 터레인 하나이고, 그 위에 장애물과 물만 얹습니다.
    /// 그래서 필요한 것도 셋뿐입니다.
    ///
    /// 예전에는 해변과 가옥 머티리얼도 있었습니다. 타일마다 발판을 굽던 시절의 잔재로,
    /// 터레인으로 넘어온 뒤로는 아무도 읽지 않았습니다 —
    /// 그런데 <see cref="IsComplete"/>가 그것들까지 요구해서,
    /// 쓰지도 않는 머티리얼이 비면 나머지가 통째로 폴백으로 떨어졌습니다.
    ///
    /// 비워 두면 <c>BattlefieldView</c>가 런타임에 임시 머티리얼을 만들어 대신 씁니다.
    /// </summary>
    [Serializable]
    public struct TerrainMaterialSet
    {
        [Tooltip("지면 머티리얼입니다. 터레인 전용 셰이더여야 합니다.")]
        public Material Ground;

        [Tooltip("바위·장애물 머티리얼입니다.")]
        public Material Cliff;

        [Tooltip("물 머티리얼입니다.")]
        public Material Water;

        /// <summary>
        /// 세 머티리얼이 모두 지정되었는지 여부입니다.
        /// 하나라도 비면 폴백 경로를 타는 편이 부분 적용보다 결과가 일관됩니다.
        /// </summary>
        public bool IsComplete => Ground != null && Cliff != null && Water != null;
    }
}
