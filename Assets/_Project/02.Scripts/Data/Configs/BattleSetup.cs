using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 전투 한 판을 구성하는 에셋 참조를 한곳에 모은 설정입니다.
    ///
    /// 씬에는 이 에셋 하나만 연결하면 됩니다.
    /// 구성이 바뀔 때 씬 파일을 건드리지 않아도 되므로 씬의 병합 충돌이 줄어듭니다.
    ///
    /// 모든 필드는 비워 둘 수 있습니다. 비면 부트스트랩이 코드 기본값과 프리미티브로 대체합니다.
    /// 에셋이 아직 없는 상태에서도 프로젝트가 항상 실행 가능하도록 유지하기 위한 규칙입니다.
    /// </summary>
    [CreateAssetMenu(fileName = "BattleSetup_", menuName = "SRPG/Configs/Battle Setup")]
    public sealed class BattleSetup : ScriptableObject
    {
        // ====================================================================================================
        // 1. Level
        // ====================================================================================================

        [Header("전장")]
        [Tooltip("웨이브 구성입니다.")]
        public WaveDefinition Waves;

        [Tooltip("지형 머티리얼입니다. 하나라도 비면 전부 런타임 임시 머티리얼로 대체됩니다.")]
        public TerrainMaterialSet TerrainMaterials;

        [Tooltip("전투 튜닝 수치입니다. 비우면 코드 기본값을 사용합니다.")]
        public BattleTuning Tuning;

        // ====================================================================================================
        // 2. Rosters
        // ====================================================================================================

        [Header("병력 구성")]
        [Tooltip("플레이어 분대가 사용할 병과입니다. 분대 수만큼 순서대로 배분됩니다.")]
        public UnitDefinition[] PlayerRoster;

        [Tooltip("적 상륙정이 실어 오는 병과 후보입니다. 하선할 때마다 무작위로 뽑습니다.")]
        public UnitDefinition[] EnemyRoster;

        // ====================================================================================================
        // 3. Prefabs
        // ====================================================================================================

        [Header("프리팹")]
        [Tooltip("적 상륙정 프리팹입니다. 루트에 EnemyShip 컴포넌트가 있어야 합니다.")]
        public GameObject EnemyShipPrefab;

        [Tooltip("선택된 분대 아래에 표시되는 마커입니다.")]
        public GameObject SelectionMarkerPrefab;

        [Tooltip("이동 명령 지점에 표시되는 마커입니다.")]
        public GameObject OrderMarkerPrefab;

        // ====================================================================================================
        // 4. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 플레이어 병과 목록을 반환합니다. 비어 있으면 null을 돌려 부트스트랩이 폴백을 쓰게 합니다.
        /// </summary>
        public UnitDefinition[] GetPlayerRosterOrNull()
        {
            return PlayerRoster != null && PlayerRoster.Length > 0 ? PlayerRoster : null;
        }

        /// <summary>
        /// 적 병과 목록을 반환합니다. 비어 있으면 null을 돌려 부트스트랩이 폴백을 쓰게 합니다.
        /// </summary>
        public UnitDefinition[] GetEnemyRosterOrNull()
        {
            return EnemyRoster != null && EnemyRoster.Length > 0 ? EnemyRoster : null;
        }
    }
}
