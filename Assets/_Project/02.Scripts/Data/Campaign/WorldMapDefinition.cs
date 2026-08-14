using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 월드맵의 지점 하나입니다.
    ///
    /// <b>여기가 좌표와 전장 사이입니다</b>
    ///
    /// <see cref="BattlefieldSpec"/> 이 예고한 흐름의 가운데 토막입니다 —
    /// 월드맵 좌표가 지형 종류를 정하고, 그것이 전장이 됩니다.
    /// 생성기는 이 지점이 어디인지 모르고, 이 지점은 생성기가 어떻게 만드는지 모릅니다.
    /// </summary>
    [System.Serializable]
    public struct WorldNode
    {
        /// <summary>지도에 표시할 이름입니다.</summary>
        public string DisplayName;

        /// <summary>지도 위의 자리입니다. 표시에만 쓰이고 전투에는 영향을 주지 않습니다.</summary>
        public Vector2 Position;

        /// <summary>이 자리에서 싸울 때 만들어질 전장입니다.</summary>
        public BattlefieldSpec Battlefield;

        /// <summary>
        /// 여기서 갈 수 있는 지점들의 번호입니다.
        ///
        /// <b>한쪽만 적어도 됩니다.</b> 길은 양방향이므로
        /// <see cref="WorldMapDefinition.AreConnected"/> 가 양쪽을 다 봅니다.
        /// 두 번 적게 하면 한쪽만 고쳐 놓고 "왜 못 가는가"를 찾게 됩니다.
        /// </summary>
        public int[] Links;

        /// <summary>
        /// 여기서 기다리고 있는 적입니다. 비어 있으면 아무도 없는 안전한 지점입니다.
        /// </summary>
        public UnitDefinition[] EnemyRoster;

        /// <summary>적이 데리고 나올 분대 수입니다.</summary>
        public int EnemySquadCount;

        /// <summary>적 분대당 병사 수입니다. 지휘관은 별도로 한 명 더 붙습니다.</summary>
        public int SoldiersPerEnemySquad;

        /// <summary>여기에 싸울 상대가 있는지 여부입니다.</summary>
        public bool HasEnemy =>
            EnemyRoster != null && EnemyRoster.Length > 0 && EnemySquadCount > 0;
    }

    /// <summary>
    /// 캠페인이 벌어지는 지도입니다.
    ///
    /// <b>왜 에셋인가</b>
    ///
    /// 지도는 기획이 만지는 것이고 코드가 만드는 것이 아닙니다.
    /// 지점을 하나 늘릴 때마다 컴파일을 기다려야 한다면 지도를 그릴 수 없습니다.
    ///
    /// <b>없어도 됩니다.</b> 이 프로젝트는 에셋이 비어 있어도 실행되어야 하므로,
    /// 연결되지 않았으면 <see cref="CreateDefault"/> 가 만든 작은 지도로 대체됩니다.
    /// </summary>
    [CreateAssetMenu(fileName = "WorldMap", menuName = "SRPG/월드맵", order = 40)]
    public sealed class WorldMapDefinition : ScriptableObject
    {
        // ====================================================================================================
        // 1. Inspector
        // ====================================================================================================

        [SerializeField]
        [Tooltip("지도의 지점들입니다. 0번이 시작 지점입니다.")]
        private WorldNode[] _nodes = System.Array.Empty<WorldNode>();

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>지도의 지점들입니다.</summary>
        public WorldNode[] Nodes => _nodes;

        /// <summary>지점 수입니다.</summary>
        public int NodeCount => _nodes != null ? _nodes.Length : 0;

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>번호로 지점을 얻습니다.</summary>
        /// <param name="index">지점 번호입니다.</param>
        /// <returns>그 지점입니다. 번호가 범위를 벗어나면 빈 지점입니다.</returns>
        public WorldNode GetNode(int index)
        {
            if (_nodes == null || index < 0 || index >= _nodes.Length)
            {
                return default;
            }

            return _nodes[index];
        }

        /// <summary>
        /// 두 지점이 이어져 있는지 봅니다. 어느 쪽에 적혀 있어도 이어진 것으로 봅니다.
        /// </summary>
        /// <param name="from">출발 지점 번호입니다.</param>
        /// <param name="to">도착 지점 번호입니다.</param>
        /// <returns>둘 사이에 길이 있으면 true입니다.</returns>
        public bool AreConnected(int from, int to)
        {
            return HasLink(from, to) || HasLink(to, from);
        }

        /// <summary>
        /// 지도가 비었을 때 쓸 작은 기본 지도를 만듭니다.
        ///
        /// 지점 셋을 한 줄로 잇습니다 — 시작 지점 하나와 싸울 자리 둘입니다.
        /// 캠페인이 도는지 확인하기에 충분한 최소한입니다.
        /// </summary>
        /// <param name="enemyRoster">싸울 자리에 세울 적 병과입니다.</param>
        /// <returns>메모리에만 있는 임시 지도입니다. 에셋으로 저장되지 않습니다.</returns>
        public static WorldMapDefinition CreateDefault(UnitDefinition[] enemyRoster)
        {
            var map = CreateInstance<WorldMapDefinition>();
            map.name = "WorldMap_Default";

            map._nodes = new[]
            {
                new WorldNode
                {
                    DisplayName = "본진",
                    Position = new Vector2(0f, 0f),
                    Battlefield = BattlefieldSpec.CreateDefault(),
                    Links = new[] { 1 },
                },
                new WorldNode
                {
                    DisplayName = "강 나루",
                    Position = new Vector2(3f, 1f),
                    Battlefield = BattlefieldSpec.CreateDefault(TerrainKind.River, 20260807),
                    Links = new[] { 2 },
                    EnemyRoster = enemyRoster,
                    EnemySquadCount = 2,
                    SoldiersPerEnemySquad = 4,
                },
                new WorldNode
                {
                    DisplayName = "언덕 마을",
                    Position = new Vector2(6f, -1f),
                    Battlefield = BattlefieldSpec.CreateDefault(TerrainKind.Hills, 20260808),
                    Links = System.Array.Empty<int>(),
                    EnemyRoster = enemyRoster,
                    EnemySquadCount = 3,
                    SoldiersPerEnemySquad = 5,
                },
            };

            return map;
        }

        // ====================================================================================================
        // 4. Private Methods
        // ====================================================================================================

        /// <summary>한쪽 방향으로만 길이 적혀 있는지 봅니다.</summary>
        /// <param name="from">출발 지점 번호입니다.</param>
        /// <param name="to">도착 지점 번호입니다.</param>
        /// <returns>출발 지점에 도착 지점이 적혀 있으면 true입니다.</returns>
        private bool HasLink(int from, int to)
        {
            if (_nodes == null || from < 0 || from >= _nodes.Length)
            {
                return false;
            }

            var links = _nodes[from].Links;

            if (links == null)
            {
                return false;
            }

            for (int i = 0; i < links.Length; i++)
            {
                if (links[i] == to)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
