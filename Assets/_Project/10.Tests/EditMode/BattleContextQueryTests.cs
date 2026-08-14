using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Units;
using SRPG.Systems.Grid;
using SRPG.Tests.Support;
using SRPG.Systems.Time;
using UnityEngine;
using UnityEngine.TestTools;

namespace SRPG.Tests
{
    /// <summary>
    /// 전투 컨텍스트의 공간 질의를 검증합니다.
    ///
    /// <c>SpatialGridTests</c>가 자료구조 자체를 보는 반면, 여기서는 <b>배선</b>을 봅니다.
    /// 진영별로 색인을 제대로 나눠 쓰는가, 죽은 유닛과 제외 대상이 걸러지는가.
    /// 색인을 도입하며 갈아 끼운 것이 정확히 이 부분이라, 여기가 회귀가 날 자리입니다.
    ///
    /// 색인은 프레임 단위로 갱신되는데 EditMode에서는 프레임이 흐르지 않습니다.
    /// 그래서 유닛을 옮기거나 죽인 뒤에는 <c>InvalidateSpatialIndex</c>로 다시 만들게 합니다.
    /// </summary>
    public sealed class BattleContextQueryTests
    {
        // ====================================================================================================
        // 1. Setup / Teardown
        // ====================================================================================================

        private readonly List<GameObject> _spawned = new List<GameObject>();

        private IslandGrid _grid;
        private BattleContext _context;
        private UnitDefinition _definition;

        [SetUp]
        public void SetUp()
        {

            _grid = TestIsland.Create(20260807);
            _context = new BattleContext(_grid, new TacticalTimeController());
            _definition = UnitDefinition.CreateDefault(UnitRole.Militia);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

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
        }

        /// <summary>지정한 월드 좌표에 유닛 하나를 만듭니다.</summary>
        private Unit CreateUnit(Vector3 position, Team team)
        {
            var go = new GameObject($"TestUnit_{team}");
            _spawned.Add(go);
            go.transform.position = position;

            var unit = go.AddComponent<Unit>();
            unit.Initialize(_definition, team, _context);

            return unit;
        }

        /// <summary>전수 조사로 구한 정답입니다.</summary>
        private HashSet<Unit> BruteForce(Vector3 center, float radius, Team team, Unit exclude)
        {
            var result = new HashSet<Unit>();
            var list = _context.GetUnits(team);
            float sqr = radius * radius;

            for (int i = 0; i < list.Count; i++)
            {
                var unit = list[i];
                if (unit == null || unit == exclude || !unit.IsAlive)
                {
                    continue;
                }

                if ((unit.Position - center).sqrMagnitude <= sqr)
                {
                    result.Add(unit);
                }
            }

            return result;
        }

        // ====================================================================================================
        // 2. QueryTeam
        // ====================================================================================================

        [Test]
        public void 진영_질의가_전수_조사와_같은_결과를_낸다()
        {
            var random = new System.Random(4242);
            var buffer = new List<Unit>();

            for (int i = 0; i < 60; i++)
            {
                var position = new Vector3(
                    (float)(random.NextDouble() * 40.0 - 20.0),
                    0f,
                    (float)(random.NextDouble() * 40.0 - 20.0));

                CreateUnit(position, i % 2 == 0 ? Team.Player : Team.Enemy);
            }

            _context.InvalidateSpatialIndex();

            for (int q = 0; q < 20; q++)
            {
                var center = new Vector3(
                    (float)(random.NextDouble() * 40.0 - 20.0),
                    0f,
                    (float)(random.NextDouble() * 40.0 - 20.0));

                float radius = (float)(random.NextDouble() * 10.0 + 0.5);
                var team = q % 2 == 0 ? Team.Player : Team.Enemy;

                _context.QueryTeam(center, radius, team, null, buffer);

                var expected = BruteForce(center, radius, team, null);

                Assert.AreEqual(expected.Count, buffer.Count, $"q={q} 결과 개수가 다릅니다.");

                for (int i = 0; i < buffer.Count; i++)
                {
                    Assert.IsTrue(expected.Contains(buffer[i]), "전수 조사에 없는 유닛이 나왔습니다.");
                }
            }
        }

        [Test]
        public void 질의한_진영만_돌려준다()
        {
            var ally = CreateUnit(new Vector3(0f, 0f, 0f), Team.Player);
            CreateUnit(new Vector3(1f, 0f, 0f), Team.Enemy);

            _context.InvalidateSpatialIndex();

            var buffer = new List<Unit>();
            _context.QueryTeam(Vector3.zero, 10f, Team.Player, null, buffer);

            CollectionAssert.AreEquivalent(new[] { ally }, buffer, "다른 진영이 섞였습니다.");
        }

        [Test]
        public void 제외_대상은_결과에_들어가지_않는다()
        {
            var self = CreateUnit(new Vector3(0f, 0f, 0f), Team.Player);
            var other = CreateUnit(new Vector3(1f, 0f, 0f), Team.Player);

            _context.InvalidateSpatialIndex();

            var buffer = new List<Unit>();
            _context.QueryTeam(Vector3.zero, 10f, Team.Player, self, buffer);

            CollectionAssert.AreEquivalent(new[] { other }, buffer, "자기 자신이 결과에 들어갔습니다.");
        }

        [Test]
        public void 죽은_유닛은_결과에_나오지_않는다()
        {
            var alive = CreateUnit(new Vector3(0f, 0f, 0f), Team.Player);
            var doomed = CreateUnit(new Vector3(1f, 0f, 0f), Team.Player);

            // Unit.Kill 은 마지막에 Object.Destroy 를 부릅니다. 런타임에서는 옳지만
            // EditMode에서는 유니티가 "Destroy may not be called from edit mode" 에러를 남깁니다.
            // 프로덕션 코드를 테스트에 맞춰 비틀지 않기 위해, 여기서만 그 잡음을 무시합니다.
            // 이 테스트가 실제로 보는 것은 파괴 여부가 아니라 IsAlive 로 걸러지는가입니다.
            LogAssert.ignoreFailingMessages = true;

            doomed.Kill();
            _context.InvalidateSpatialIndex();

            var buffer = new List<Unit>();
            _context.QueryTeam(Vector3.zero, 10f, Team.Player, null, buffer);

            CollectionAssert.AreEquivalent(new[] { alive }, buffer, "죽은 유닛이 결과에 남아 있습니다.");
        }

        // ====================================================================================================
        // 3. FindNearestEnemy
        // ====================================================================================================

        [Test]
        public void 가장_가까운_적을_찾는다()
        {
            CreateUnit(new Vector3(8f, 0f, 0f), Team.Enemy);
            var nearest = CreateUnit(new Vector3(2f, 0f, 0f), Team.Enemy);
            CreateUnit(new Vector3(5f, 0f, 0f), Team.Enemy);

            _context.InvalidateSpatialIndex();

            var found = _context.FindNearestEnemy(Vector3.zero, Team.Player, 20f);

            Assert.AreSame(nearest, found);
        }

        [Test]
        public void 같은_진영은_적으로_잡지_않는다()
        {
            CreateUnit(new Vector3(1f, 0f, 0f), Team.Player);

            _context.InvalidateSpatialIndex();

            Assert.IsNull(_context.FindNearestEnemy(Vector3.zero, Team.Player, 20f));
        }

        [Test]
        public void 탐색_반경_밖의_적은_잡지_않는다()
        {
            CreateUnit(new Vector3(30f, 0f, 0f), Team.Enemy);

            _context.InvalidateSpatialIndex();

            Assert.IsNull(_context.FindNearestEnemy(Vector3.zero, Team.Player, 5f));
            Assert.IsNotNull(_context.FindNearestEnemy(Vector3.zero, Team.Player, 40f));
        }

        [Test]
        public void 적이_없으면_null을_돌려준다()
        {
            _context.InvalidateSpatialIndex();

            Assert.IsNull(_context.FindNearestEnemy(Vector3.zero, Team.Player, 100f));
        }
    }
}
