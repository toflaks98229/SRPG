using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Enemies;
using SRPG.Gameplay.Squads;
using SRPG.Gameplay.Units;
using SRPG.Gameplay.Weapons;
using SRPG.Systems.AI;
using SRPG.Systems.Grid;
using SRPG.Systems.Pathfinding;
using SRPG.Systems.Spatial;
using SRPG.Systems.Time;
using UnityEngine;

namespace SRPG.Gameplay.Battle
{
    /// <summary>
    /// 한 전투가 공유하는 런타임 상태입니다.
    /// 지형·경로탐색기·시간 제어기·유닛 레지스트리를 한곳에 모아, 액터들이 서로를 직접 찾아다니지 않게 합니다.
    ///
    /// <b>소비자는 이것을 통째로 받지 않습니다</b>
    ///
    /// 이 클래스는 역할별 인터페이스(<see cref="ISpatialQuery"/>, <see cref="IUnitRegistry"/>,
    /// <see cref="IUnitRoster"/>, <see cref="IThreatProvider"/>, <see cref="IUnitContext"/>)를
    /// <b>모아 구현할 뿐</b>이고, 각 소비자는 자기가 실제로 쓰는 역할만 받습니다.
    ///
    /// 그러지 않으면 여기에 필드를 하나 더할 때마다, 그것을 쓸 이유가 없는 모든 곳이
    /// 함께 접근 가능해집니다. 싱글턴이 아닌데도 사실상 서비스 로케이터처럼 굳는 경로입니다.
    ///
    /// DI 컨테이너(VContainer)를 도입하면 이 클래스는 컨테이너 등록으로 대체됩니다.
    /// 그때 등록 단위가 되는 것이 위 인터페이스들이고, 소비자는 지금 받는 것을 그대로 받게 됩니다.
    /// 지금은 패키지가 없으므로 부트스트랩이 직접 생성해 주입합니다.
    /// </summary>
    public sealed class BattleContext :
        IUnitContext,
        IUnitRoster,
        IThreatProvider,
        ISquadRoster,
        ISquadContext<Squad>,
        IEnemySquadContext,
        IDeploymentContext
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        private readonly List<Unit> _playerUnits = new List<Unit>(64);
        private readonly List<Unit> _enemyUnits = new List<Unit>(128);

        /// <summary>전장의 분대입니다. 분대가 태어나고 사라질 때 스스로 넣고 뺍니다.</summary>
        private readonly List<Squad> _playerSquads = new List<Squad>(8);
        private readonly List<EnemySquad> _enemySquads = new List<EnemySquad>(8);

        private readonly SpatialGrid<Unit> _playerIndex;
        private readonly SpatialGrid<Unit> _enemyIndex;

        /// <summary>공간 색인을 마지막으로 다시 만든 프레임입니다.</summary>
        private int _indexedFrame = -1;

        /// <summary>
        /// 공간 질의의 중간 결과를 담는 재사용 버퍼입니다.
        ///
        /// 질의가 재진입하지 않기 때문에 하나로 충분합니다.
        /// (질의 결과를 순회하는 도중에 다시 질의하는 코드가 생기면 이 전제가 깨집니다)
        /// </summary>
        private readonly List<Unit> _candidateBuffer = new List<Unit>(64);

        private readonly InfluenceMap _playerThreat;
        private readonly InfluenceMap _enemyThreat;

        /// <summary>영향력 맵을 마지막으로 다시 만든 시각입니다. 진영별로 따로 셉니다.</summary>
        private float _playerThreatTime = float.NegativeInfinity;
        private float _enemyThreatTime = float.NegativeInfinity;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>섬 지형입니다.</summary>
        public IslandGrid Grid { get; }

        /// <summary>
        /// 공유 경로 탐색기입니다. 내부 작업 배열을 재사용하므로 <b>스레드 안전하지 않습니다.</b>
        /// 같은 프레임 내 순차 호출을 전제로 합니다. 병행 호출이 필요하면 <see cref="CreatePathfinder"/>로
        /// 별도 인스턴스를 받으십시오.
        /// </summary>
        public GridPathfinder Pathfinder { get; }

        /// <summary>화살 재사용 풀입니다. 궁수가 여기서 화살을 꺼내 씁니다.</summary>
        public ProjectilePool ProjectilePool { get; } = new ProjectilePool();

        /// <summary>전술 시간 제어기입니다.</summary>
        public TacticalTimeController TimeController { get; }

        /// <summary>
        /// 전투 튜닝 수치입니다. 읽기 전용으로만 쓰며, 절대 null이 아닙니다.
        /// 에셋이 연결되지 않았으면 생성자가 코드 기본값으로 채웁니다.
        /// </summary>
        public BattleTuning Tuning { get; }

        /// <summary>
        /// 플레이어 분대의 타일 점유 현황입니다. 한 칸에 분대 하나만 자리 잡을 수 있게 강제합니다.
        /// </summary>
        public TileOccupancy<Squad> Occupancy { get; } =
            new TileOccupancy<Squad>(squad => squad == null || squad.IsDestroyed);

        /// <summary>
        /// 적 분대의 타일 점유 현황입니다.
        ///
        /// 플레이어와 <b>따로</b> 관리합니다. 하나로 합치면 적이 플레이어가 선 칸을 목적지로 삼을 수 없게 되는데,
        /// 그건 "공격하지 말라"는 뜻이 되어 버립니다.
        /// </summary>
        public TileOccupancy<EnemySquad> EnemyOccupancy { get; } =
            new TileOccupancy<EnemySquad>(squad => squad == null || squad.IsDisbanded);

        /// <summary>플레이어 유닛 목록입니다.</summary>
        public IReadOnlyList<Unit> PlayerUnits => _playerUnits;

        /// <summary>적 유닛 목록입니다.</summary>
        public IReadOnlyList<Unit> EnemyUnits => _enemyUnits;

        /// <summary>전장의 플레이어 분대 목록입니다.</summary>
        public IReadOnlyList<Squad> PlayerSquads => _playerSquads;

        /// <summary>전장의 적 분대 목록입니다.</summary>
        public IReadOnlyList<EnemySquad> EnemySquads => _enemySquads;

        // ====================================================================================================
        // 3. Constructor
        // ====================================================================================================

        /// <param name="grid">섬 지형입니다.</param>
        /// <param name="timeController">전술 시간 제어기입니다.</param>
        /// <param name="tuning">전투 튜닝 에셋입니다. null이면 코드 기본값을 씁니다.</param>
        public BattleContext(IslandGrid grid, TacticalTimeController timeController, BattleTuning tuning = null)
        {
            Grid = grid;
            Pathfinder = CreatePathfinder();
            TimeController = timeController;

            // 이후 모든 소비자가 null 검사 없이 쓸 수 있도록 여기서 한 번만 확정합니다.
            Tuning = tuning != null ? tuning : BattleTuning.CreateDefault();

            _playerIndex = CreateUnitIndex(grid);
            _enemyIndex = CreateUnitIndex(grid);

            _playerThreat = new InfluenceMap(grid);
            _enemyThreat = new InfluenceMap(grid);
        }

        /// <summary>
        /// 유닛 색인을 만듭니다.
        ///
        /// 섬 밖에도 유닛이 존재할 수 있습니다. 상륙정에서 막 내린 병사, 밀려나 물에 빠지는 중인 병사가 그렇습니다.
        /// 격자 크기만큼만 덮으면 그들이 전부 가장자리 셀에 몰려 그 셀의 질의가 느려지므로, 여유를 둡니다.
        ///
        /// 셀 크기는 타일 크기를 그대로 씁니다.
        /// 분리 조향(반경 1m 미만)은 1~4개 셀만 훑고, 궁수 교전 반경(10m)도 배열 인덱스 산술로 끝납니다.
        /// 게임의 공간 축척이 바뀌면 셀 크기도 자연히 따라옵니다.
        /// </summary>
        private static SpatialGrid<Unit> CreateUnitIndex(IslandGrid grid)
        {
            float width = grid.Width * grid.CellSize;
            float depth = grid.Depth * grid.CellSize;
            float margin = grid.CellSize * 8f;

            return new SpatialGrid<Unit>(
                new Vector3(grid.Origin.x - margin, 0f, grid.Origin.z - margin),
                width + margin * 2f,
                depth + margin * 2f,
                grid.CellSize);
        }

        // ====================================================================================================
        // 4. Public Methods - Registry
        // ====================================================================================================

        /// <summary>유닛을 레지스트리에 등록합니다.</summary>
        public void Register(Unit unit)
        {
            if (unit == null)
            {
                return;
            }

            var list = unit.Team == Team.Player ? _playerUnits : _enemyUnits;
            if (!list.Contains(unit))
            {
                list.Add(unit);
            }
        }

        /// <summary>유닛을 레지스트리에서 제거합니다.</summary>
        public void Unregister(Unit unit)
        {
            if (unit == null)
            {
                return;
            }

            var list = unit.Team == Team.Player ? _playerUnits : _enemyUnits;
            list.Remove(unit);
        }

        /// <summary>지정 진영의 유닛 목록을 반환합니다.</summary>
        public IReadOnlyList<Unit> GetUnits(Team team)
        {
            return team == Team.Player ? _playerUnits : _enemyUnits;
        }

        /// <summary>
        /// 지정 진영에서 아직 싸우고 있는 분대 수입니다.
        ///
        /// 무너진 분대는 오브젝트가 사라지기 전 한 프레임 동안 명부에 남아 있으므로,
        /// 세는 쪽이 상태를 확인해야 합니다. 지원군이 이 수를 보고 올라옵니다.
        /// </summary>
        public int CountLivingSquads(Team team)
        {
            int alive = 0;

            if (team == Team.Player)
            {
                for (int i = 0; i < _playerSquads.Count; i++)
                {
                    if (_playerSquads[i] != null && !_playerSquads[i].IsDestroyed)
                    {
                        alive++;
                    }
                }

                return alive;
            }

            for (int i = 0; i < _enemySquads.Count; i++)
            {
                if (_enemySquads[i] != null && !_enemySquads[i].IsDisbanded)
                {
                    alive++;
                }
            }

            return alive;
        }

        /// <summary>플레이어 분대를 명부에 넣습니다. 분대가 초기화될 때 스스로 부릅니다.</summary>
        public void RegisterPlayerSquad(Squad squad)
        {
            if (squad != null && !_playerSquads.Contains(squad))
            {
                _playerSquads.Add(squad);
            }
        }

        /// <summary>플레이어 분대를 명부에서 뺍니다.</summary>
        public void UnregisterPlayerSquad(Squad squad)
        {
            _playerSquads.Remove(squad);
        }

        /// <summary>
        /// 적 분대를 명부에 넣습니다. 분대가 전장에 설 때 스스로 부릅니다.
        /// </summary>
        public void RegisterEnemySquad(EnemySquad squad)
        {
            if (squad != null && !_enemySquads.Contains(squad))
            {
                _enemySquads.Add(squad);
            }
        }

        /// <summary>
        /// 적 분대를 명부에서 뺍니다. 해산할 때와 오브젝트가 사라질 때 부릅니다.
        ///
        /// 두 번 불려도 안전합니다. 해산과 파괴는 한 프레임 떨어져 일어나므로
        /// 양쪽에서 부르지 않으면 씬을 벗어날 때 빠진 항목이 남습니다.
        /// </summary>
        public void UnregisterEnemySquad(EnemySquad squad)
        {
            _enemySquads.Remove(squad);
        }

        // ====================================================================================================
        // 4-1. Explicit Interface Implementations - Squad Contexts
        // ====================================================================================================

        /// <summary>
        /// 진영별 장부를 <see cref="ISquadContext{TSquad}"/> 의 <c>Occupancy</c> 한 이름으로 잇습니다.
        ///
        /// <b>왜 명시적 구현인가</b>
        ///
        /// 두 인터페이스가 같은 이름의 멤버를 서로 다른 타입으로 요구하므로
        /// 암시적 구현으로는 둘을 동시에 만족시킬 수 없습니다.
        /// 그리고 그 편이 낫습니다 — 여기서 <c>Occupancy</c> 를 그냥 부르면
        /// <b>어느 진영의 장부인지 이름만 보고는 알 수 없기</b> 때문입니다.
        /// 컨텍스트를 통째로 든 쪽은 지금처럼 <c>Occupancy</c> / <c>EnemyOccupancy</c> 로
        /// 진영을 밝혀 부르고, 분대는 자기 것만 <c>Occupancy</c> 로 받습니다.
        /// </summary>
        TileOccupancy<Squad> ISquadContext<Squad>.Occupancy => Occupancy;

        TileOccupancy<EnemySquad> ISquadContext<EnemySquad>.Occupancy => EnemyOccupancy;

        void ISquadContext<Squad>.RegisterSquad(Squad squad) => RegisterPlayerSquad(squad);

        void ISquadContext<Squad>.UnregisterSquad(Squad squad) => UnregisterPlayerSquad(squad);

        void ISquadContext<EnemySquad>.RegisterSquad(EnemySquad squad) => RegisterEnemySquad(squad);

        void ISquadContext<EnemySquad>.UnregisterSquad(EnemySquad squad) => UnregisterEnemySquad(squad);

        /// <summary>
        /// 전용 경로 탐색기를 새로 만듭니다.
        ///
        /// 탐색기는 인스턴스마다 자기 작업 배열을 가지므로, 서로 다른 인스턴스는 간섭하지 않습니다.
        /// 나중에 경로 탐색을 잡(Job)이나 별도 스레드로 옮길 때 각 실행 단위가 자기 것을 들고 가면 됩니다.
        /// 지금은 <see cref="Pathfinder"/> 하나를 공유하는 것으로 충분합니다.
        /// </summary>
        public GridPathfinder CreatePathfinder()
        {
            return new GridPathfinder(Grid);
        }

        // ====================================================================================================
        // 5. Public Methods - Queries
        // ====================================================================================================

        /// <summary>
        /// 지정 위치에서 가장 가까운 적대 유닛을 찾습니다.
        ///
        /// 공간 색인으로 후보를 좁힌 뒤, <b>실제 위치</b>로 거리를 다시 재어 가장 가까운 것을 고릅니다.
        /// 색인은 프레임 시작 시점의 스냅샷이라 좁히는 데만 쓰고, 순위는 최신 위치로 정합니다.
        /// </summary>
        public Unit FindNearestEnemy(Vector3 position, Team myTeam, float maxDistance)
        {
            EnsureSpatialIndex();

            var index = myTeam == Team.Player ? _enemyIndex : _playerIndex;
            int count = index.Query(position, maxDistance, _candidateBuffer);

            Unit best = null;
            float bestSqr = maxDistance * maxDistance;

            for (int i = 0; i < count; i++)
            {
                var candidate = _candidateBuffer[i];
                if (candidate == null || !candidate.IsAlive)
                {
                    continue;
                }

                float sqr = (candidate.Position - position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// 지정 반경 안의 특정 진영 유닛을 <paramref name="buffer"/>에 채웁니다. 분리 조향의 입력입니다.
        ///
        /// 진영을 <paramref name="exclude"/>에서 유추하지 않고 <b>명시적으로 받습니다.</b>
        /// 예전 시그니처는 제외 대상에서 진영을 읽어 냈는데, 그것이 null이면
        /// 조용히 적군 목록을 돌려주었습니다. 오류도 경고도 없이 의미만 뒤집히는 형태였습니다.
        /// </summary>
        /// <param name="position">질의 중심입니다.</param>
        /// <param name="radius">질의 반경입니다.</param>
        /// <param name="team">찾을 진영입니다.</param>
        /// <param name="exclude">결과에서 제외할 유닛입니다. null이어도 됩니다.</param>
        /// <param name="buffer">결과가 채워집니다. 호출 시 비워집니다.</param>
        public int QueryTeam(Vector3 position, float radius, Team team, Unit exclude, List<Unit> buffer)
        {
            EnsureSpatialIndex();

            var index = team == Team.Player ? _playerIndex : _enemyIndex;
            int count = index.Query(position, radius, _candidateBuffer);

            buffer.Clear();

            for (int i = 0; i < count; i++)
            {
                var other = _candidateBuffer[i];
                if (other == null || other == exclude || !other.IsAlive)
                {
                    continue;
                }

                buffer.Add(other);
            }

            return buffer.Count;
        }

        // ====================================================================================================
        // 6. Public Methods - Spatial Index
        // ====================================================================================================

        /// <summary>
        /// 공간 색인을 다음 질의 때 다시 만들도록 표시합니다.
        ///
        /// 평소에는 프레임이 바뀔 때 알아서 갱신되므로 부를 일이 없습니다.
        /// 한 프레임 안에서 유닛을 옮긴 직후 곧바로 질의해야 할 때(순간이동, 테스트)만 필요합니다.
        /// </summary>
        public void InvalidateSpatialIndex()
        {
            _indexedFrame = -1;
        }

        /// <summary>
        /// 이번 프레임의 공간 색인을 준비합니다. 프레임당 한 번만 실제로 다시 만듭니다.
        ///
        /// <b>왜 실행 순서에 기대지 않는가</b>
        ///
        /// "매 프레임 시작에 갱신"을 부트스트랩의 <c>Update</c>에 두면, 유닛보다 먼저 돌아야 한다는
        /// 숨은 조건이 생깁니다. 유니티의 스크립트 실행 순서는 기본적으로 정해져 있지 않으므로
        /// 그 조건은 언젠가 조용히 깨집니다. 첫 질의가 스스로 갱신하게 하면 순서를 신경 쓸 필요가 없습니다.
        /// </summary>
        private void EnsureSpatialIndex()
        {
            int frame = UnityEngine.Time.frameCount;
            if (_indexedFrame == frame)
            {
                return;
            }

            _indexedFrame = frame;

            RebuildIndex(_playerIndex, _playerUnits);
            RebuildIndex(_enemyIndex, _enemyUnits);
        }

        // ====================================================================================================
        // 7. Public Methods - Influence
        // ====================================================================================================

        /// <summary>
        /// 지정 진영이 뿜는 위협 영향력 맵을 반환합니다.
        ///
        /// 갱신 비용이 격자 크기에 비례하므로 매 프레임 다시 만들지 않고 <b>주기적으로만</b> 갱신합니다.
        /// AI가 0.1초 늦게 아는 것은 문제가 되지 않습니다. 어차피 목표 재판단 자체가 그보다 드뭅니다.
        ///
        /// 공간 색인과 같은 방식으로, <b>첫 조회가 스스로</b> 갱신 여부를 판단합니다.
        /// 어딘가의 Update에 갱신을 두면 "AI보다 먼저 돌아야 한다"는 숨은 순서 조건이 생깁니다.
        /// </summary>
        public InfluenceMap GetThreatMap(Team team)
        {
            float now = UnityEngine.Time.time;
            float interval = Mathf.Max(0.02f, Tuning.InfluenceRefreshInterval);

            if (team == Team.Player)
            {
                if (now - _playerThreatTime >= interval)
                {
                    _playerThreatTime = now;
                    RebuildThreat(_playerThreat, _playerUnits);
                }

                return _playerThreat;
            }

            if (now - _enemyThreatTime >= interval)
            {
                _enemyThreatTime = now;
                RebuildThreat(_enemyThreat, _enemyUnits);
            }

            return _enemyThreat;
        }

        /// <summary>
        /// 영향력 맵을 다음 조회 때 다시 만들도록 표시합니다.
        /// 평소에는 주기적으로 갱신되므로 부를 일이 없습니다. 테스트에서만 필요합니다.
        /// </summary>
        public void InvalidateThreatMaps()
        {
            _playerThreatTime = float.NegativeInfinity;
            _enemyThreatTime = float.NegativeInfinity;
        }

        /// <summary>
        /// 유닛 위치를 발생원으로 삼아 위협을 다시 펼칩니다.
        ///
        /// 세기를 체력 비율로 두는 것이 핵심입니다. 반쯤 죽은 분대는 위협도 절반입니다.
        /// 그래야 적이 "약해진 쪽을 노린다"는 판단을 별도 규칙 없이 하게 됩니다.
        /// </summary>
        private void RebuildThreat(InfluenceMap map, List<Unit> units)
        {
            map.Clear();

            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null || !unit.IsAlive)
                {
                    continue;
                }

                map.AddSourceWorld(unit.Position, Mathf.Max(0.05f, unit.HealthRatio));
            }

            map.Propagate(Tuning.InfluenceDecayPerTile);
        }

        // ====================================================================================================
        // 8. Private Methods
        // ====================================================================================================

        private static void RebuildIndex(SpatialGrid<Unit> index, List<Unit> units)
        {
            index.Clear();

            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null || !unit.IsAlive)
                {
                    continue;
                }

                index.Insert(unit.Position, unit);
            }
        }
    }
}
