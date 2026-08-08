using System;
using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 지형 종류별 머티리얼 묶음입니다.
    ///
    /// 지면은 유니티 터레인 하나이고 나머지는 종류별로 합쳐 굽기 때문에
    /// 머티리얼도 종류당 하나면 충분합니다.
    /// 비워 두면 <c>BattlefieldView</c>가 런타임에 임시 머티리얼을 만들어 대신 씁니다.
    /// </summary>
    [Serializable]
    public struct TerrainMaterialSet
    {
        [Tooltip("해변 머티리얼입니다.")]
        public Material Beach;

        [Tooltip("평지 머티리얼입니다.")]
        public Material Ground;

        [Tooltip("절벽·바위 머티리얼입니다.")]
        public Material Cliff;

        [Tooltip("가옥 머티리얼입니다.")]
        public Material House;

        [Tooltip("바다 머티리얼입니다.")]
        public Material Water;

        /// <summary>
        /// 다섯 머티리얼이 모두 지정되었는지 여부입니다.
        /// 하나라도 비면 폴백 경로를 타는 편이 부분 적용보다 결과가 일관됩니다.
        /// </summary>
        public bool IsComplete =>
            Beach != null && Ground != null && Cliff != null && House != null && Water != null;
    }
}
