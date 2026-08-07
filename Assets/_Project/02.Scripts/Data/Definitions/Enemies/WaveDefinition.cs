using System;
using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 웨이브 한 번의 구성입니다.
    /// </summary>
    [Serializable]
    public struct WaveEntry
    {
        [Min(0f)]
        [Tooltip("이전 웨이브 종료 후 이 웨이브가 시작되기까지의 대기 시간(초)입니다.")]
        public float Delay;

        [Min(1)]
        [Tooltip("이 웨이브에서 접근하는 상륙정 수입니다. 상륙 지점이 분산될수록 방어가 어려워집니다.")]
        public int ShipCount;

        [Min(1)]
        [Tooltip("상륙정 한 척이 내려놓는 병력 수입니다.")]
        public int UnitsPerShip;
    }

    /// <summary>
    /// 한 전투에서 순차적으로 진행되는 웨이브 구성입니다.
    /// </summary>
    [CreateAssetMenu(fileName = "WaveDef_", menuName = "SRPG/Definitions/Wave Definition")]
    public sealed class WaveDefinition : ScriptableObject
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        [Min(0f)]
        [Tooltip("전투 시작 후 첫 웨이브까지의 준비 시간(초)입니다. 플레이어가 초기 배치를 정하는 시간입니다.")]
        public float PreparationTime = 6f;

        [Tooltip("순차 진행되는 웨이브 목록입니다.")]
        public WaveEntry[] Waves = Array.Empty<WaveEntry>();

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>총 웨이브 수입니다.</summary>
        public int WaveCount => Waves?.Length ?? 0;

        // ====================================================================================================
        // 3. Factory
        // ====================================================================================================

        /// <summary>
        /// 에셋 없이 코드로 기본 웨이브 구성을 만듭니다.
        /// 뒤로 갈수록 상륙정 수를 늘려 전선이 점점 분산되도록 했습니다.
        /// </summary>
        public static WaveDefinition CreateDefault()
        {
            var def = CreateInstance<WaveDefinition>();
            def.name = "WaveDef_Default";
            def.PreparationTime = 6f;
            def.Waves = new[]
            {
                new WaveEntry { Delay = 0f,  ShipCount = 1, UnitsPerShip = 4 },
                new WaveEntry { Delay = 14f, ShipCount = 2, UnitsPerShip = 4 },
                new WaveEntry { Delay = 16f, ShipCount = 2, UnitsPerShip = 6 },
                new WaveEntry { Delay = 18f, ShipCount = 3, UnitsPerShip = 6 },
            };
            return def;
        }
    }
}
