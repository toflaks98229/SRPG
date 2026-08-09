using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Enemies;
using SRPG.Gameplay.Squads;
using SRPG.Gameplay.Units;
using SRPG.Systems.AI;
using SRPG.Systems.Grid;
using SRPG.Systems.Pathfinding;
using SRPG.Systems.Spatial;
using SRPG.Tests.Support;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 분대가 <b>전투 컨텍스트 없이도</b> 서는지 검증합니다.
    ///
    /// <b>이 테스트가 곧 경계의 증거입니다</b>
    ///
    /// <see cref="UnitContextTests"/> 가 병사에 대해 하는 일을 분대에 대해 합니다.
    /// <c>BattleContext</c> 는 싱글턴이 아니지만, 세 컴포넌트가 그것을 <b>통째로</b> 받고
    /// 각자 그중 일부만 쓰고 있었습니다. 그러면 컨텍스트에 필드를 하나 더할 때마다
    /// 그것을 쓸 이유가 없는 세 곳이 함께 열립니다.
    ///
    /// 여기서는 좁은 인터페이스만 구현한 <b>가짜</b>를 세우고 분대를 초기화합니다.
    /// 이 파일이 컴파일된다는 사실 자체가, 분대가 그 이상을 쓰지 않는다는 증명입니다.
    ///
    /// 언젠가 누군가 <c>Squad</c> 안에서 전군 명부를 훑거나 적 진영의 점유를 만지려 하면
    /// 여기가 <b>컴파일 단계에서</b> 무너집니다. 그때 무너지는 것이 옳습니다.
    /// </summary>
    public sealed class SquadContextTests
    {
        // ====================================================================================================
        // 1. Fakes
        // ====================================================================================================

        /// <summary>
        /// 분대가 볼 수 있는 것만 들고 있는 최소 구현입니다.
        ///
        /// 양측 분대가 <b>같은 타입</b>을 타입 인자만 바꿔 쓴다는 사실도 함께 증명합니다.
        /// 규칙이 대칭이므로 가짜도 하나면 충분합니다.
        /// </summary>
        private class FakeSquadContext<TSquad> : ISquadContext<TSquad>
            where TSquad : class
        {
            private readonly List<TSquad> _registered = new List<TSquad>();

            public FakeSquadContext(IslandGrid grid, BattleTuning tuning, System.Predicate<TSquad> isStale)
            {
                Grid = grid;
                Tuning = tuning;
                Pathfinder = new GridPathfinder(grid);
                Occupancy = new TileOccupancy<TSquad>(isStale);
            }

            public IslandGrid Grid { get; }

            public BattleTuning Tuning { get; }

            public GridPathfinder Pathfinder { get; }

            public TileOccupancy<TSquad> Occupancy { get; }

            /// <summary>명부입니다. 검증용으로만 노출합니다.</summary>
            public IReadOnlyList<TSquad> Registered => _registered;

            public void RegisterSquad(TSquad squad)
            {
                if (squad != null && !_registered.Contains(squad))
                {
                    _registered.Add(squad);
                }
            }

            public void UnregisterSquad(TSquad squad)
            {
                _registered.Remove(squad);
            }

            // 분대는 시선을 정할 때만 공간 질의를 씁니다. 적이 없으면 null 이면 됩니다.
            public Unit FindNearestEnemy(Vector3 position, Team myTeam, float maxDistance) => null;

            public int QueryTeam(Vector3 position, float radius, Team team, Unit exclude, List<Unit> buffer)
            {
                buffer.Clear();
                return 0;
            }
        }

        /// <summary>적 분대가 추가로 보는 것까지 갖춘 가짜입니다.</summary>
        private sealed class FakeEnemySquadContext : FakeSquadContext<EnemySquad>, IEnemySquadContext
        {
            private readonly InfluenceMap _threat;

            public FakeEnemySquadContext(IslandGrid grid, BattleTuning tuning)
                : base(grid, tuning, squad => squad == null || squad.IsDisbanded)
            {
                _threat = new InfluenceMap(grid);
            }

            /// <summary>적 AI가 후보로 삼을 아군 부대입니다.</summary>
            public List<Squad> Players { get; } = new List<Squad>();

            public IReadOnlyList<Squad> PlayerSquads => Players;

            public IReadOnlyList<EnemySquad> EnemySquads => Registered;

            public int CountLivingSquads(Team team) => team == Team.Player ? Players.Count : Registered.Count;

            public InfluenceMap GetThreatMap(Team team) => _threat;
        }

        // ====================================================================================================
        // 2. Setup / Teardown
        // ====================================================================================================

        private readonly List<GameObject> _spawned = new List<GameObject>();

        private IslandGrid _grid;
        private BattleTuning _tuning;
        private UnitDefinition _definition;

        [SetUp]
        public void SetUp()
        {
            _grid = TestIsland.Create(20260809);
            _tuning = BattleTuning.CreateDefault();
            _definition = UnitDefinition.CreateDefault(UnitRole.Militia);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();

            if (_definition != null)
            {
                Object.DestroyImmediate(_definition);
            }

            if (_tuning != null)
            {
                Object.DestroyImmediate(_tuning);
            }
        }

        /// <summary>
        /// 유닛을 만드는 함수입니다.
        ///
        /// <b>병사는 세우지 않습니다.</b> 여기서 검증하려는 것은 분대가 무엇을 보는가이지
        /// 병사가 어떻게 서는가가 아닙니다. 병사까지 세우면 <c>IUnitContext</c> 가 필요해지고,
        /// 그러면 "분대는 그것 없이도 선다"는 이 테스트의 주장이 흐려집니다.
        /// </summary>
        private Unit CreateHollowUnit(UnitDefinition definition, Team team, bool isCommander, Vector3 position)
        {
            var go = new GameObject($"FakeUnit_{team}");
            _spawned.Add(go);
            go.transform.position = position;

            return go.AddComponent<Unit>();
        }

        private GridCoord SomeWalkableCoord(int index = 0)
        {
            return _grid.WalkableTiles[index % _grid.WalkableTiles.Count].Coord;
        }

        // ====================================================================================================
        // 3. Tests - Squad
        // ====================================================================================================

        [Test]
        public void 전군_명부_없이도_아군_분대가_선다()
        {
            var context = new FakeSquadContext<Squad>(
                _grid, _tuning, squad => squad == null || squad.IsDestroyed);

            var squad = CreateSquad(context, SomeWalkableCoord());

            Assert.IsNotNull(squad);
            Assert.IsFalse(squad.IsDestroyed);
        }

        [Test]
        public void 아군_분대는_스스로_명부에_오른다()
        {
            var context = new FakeSquadContext<Squad>(
                _grid, _tuning, squad => squad == null || squad.IsDestroyed);

            var squad = CreateSquad(context, SomeWalkableCoord());

            Assert.AreEqual(1, context.Registered.Count, "명부에 오르지 않았습니다.");
            Assert.AreSame(squad, context.Registered[0]);
        }

        /// <summary>
        /// 배치 칸을 점유해 두어야 다른 분대가 그 위로 명령받지 않습니다.
        /// </summary>
        [Test]
        public void 아군_분대는_배치_칸을_점유한다()
        {
            var context = new FakeSquadContext<Squad>(
                _grid, _tuning, squad => squad == null || squad.IsDestroyed);

            var coord = SomeWalkableCoord();
            var first = CreateSquad(context, coord);
            var second = CreateSquad(context, SomeWalkableCoord(50));

            Assert.IsTrue(context.Occupancy.IsBlockedFor(coord, second), "배치 칸이 점유되지 않았습니다.");
            Assert.IsFalse(context.Occupancy.IsBlockedFor(coord, first), "자기 칸을 자기가 막고 있습니다.");
        }

        // ====================================================================================================
        // 4. Tests - EnemySquad
        // ====================================================================================================

        [Test]
        public void 전군_명부_없이도_적_분대가_선다()
        {
            var context = new FakeEnemySquadContext(_grid, _tuning);

            var squad = CreateEnemySquad(context, SomeWalkableCoord());

            Assert.IsNotNull(squad);
            Assert.IsFalse(squad.IsDisbanded);
            Assert.AreEqual(1, context.Registered.Count, "명부에 오르지 않았습니다.");
        }

        /// <summary>
        /// 양측이 <b>서로의 장부를 만질 수 없습니다.</b>
        ///
        /// 타입 인자로 갈라 두었기 때문에, 아군 분대에게 넘긴 컨텍스트에는
        /// 적 분대를 담을 방법이 아예 없습니다. 이 테스트는 그 사실을
        /// 런타임에서 한 번 더 확인할 뿐이고, 진짜 방어선은 컴파일러입니다.
        /// </summary>
        [Test]
        public void 진영마다_점유_장부가_따로다()
        {
            var playerContext = new FakeSquadContext<Squad>(
                _grid, _tuning, squad => squad == null || squad.IsDestroyed);
            var enemyContext = new FakeEnemySquadContext(_grid, _tuning);

            var coord = SomeWalkableCoord();

            var player = CreateSquad(playerContext, coord);
            var enemy = CreateEnemySquad(enemyContext, coord);

            Assert.IsFalse(
                enemyContext.Occupancy.IsBlockedFor(coord, enemy),
                "아군이 선 칸을 적이 목적지로 삼지 못하면 공격하지 말라는 뜻이 됩니다.");

            Assert.IsTrue(playerContext.Occupancy.IsBlockedFor(coord, CreateSquad(playerContext, SomeWalkableCoord(50))),
                "같은 진영끼리는 한 칸을 나눠 쓸 수 없어야 합니다.");

            Assert.IsNotNull(player);
        }

        // ====================================================================================================
        // 5. Helpers
        // ====================================================================================================

        private Squad CreateSquad(ISquadContext<Squad> context, GridCoord coord)
        {
            var go = new GameObject("TestSquad");
            _spawned.Add(go);

            var squad = go.AddComponent<Squad>();
            squad.Initialize(context, _definition, coord, 2, "테스트 분대", CreateHollowUnit);

            return squad;
        }

        private EnemySquad CreateEnemySquad(IEnemySquadContext context, GridCoord coord)
        {
            var go = new GameObject("TestEnemySquad");
            _spawned.Add(go);

            var squad = go.AddComponent<EnemySquad>();
            squad.Initialize(context, _definition, coord, 2, CreateHollowUnit);

            return squad;
        }
    }
}
