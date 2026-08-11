using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Units;
using SRPG.Gameplay.Weapons;
using SRPG.Systems.Grid;
using SRPG.Tests.Support;
using UnityEngine;
using UnityEngine.TestTools;

namespace SRPG.Tests
{
    /// <summary>
    /// 병사가 <b>전투 컨텍스트 없이도</b> 서는지 검증합니다.
    ///
    /// <b>이 테스트가 곧 분리의 증거입니다</b>
    ///
    /// <c>BattleContext</c>는 싱글턴이 아니지만 여덟 개 컴포넌트가 그것을 통째로 받고 있었습니다.
    /// 그러면 컨텍스트에 필드를 하나 더할 때마다 그것을 쓸 이유가 없는 여덟 곳이 함께 열립니다.
    ///
    /// 여기서는 <see cref="IUnitContext"/>만 구현한 <b>가짜</b>를 세우고 병사를 초기화합니다.
    /// 경로 탐색기도, 타일 점유도, 영향력 맵도, 공간 색인도 없습니다.
    /// 이 테스트가 컴파일되고 통과한다는 사실 자체가 병사가 그것들을 쓰지 않는다는 증명입니다.
    ///
    /// 언젠가 누군가 <c>Unit</c> 안에서 경로를 잡거나 칸을 점유하려 하면
    /// 여기가 <b>컴파일 단계에서</b> 무너집니다. 그때 무너지는 것이 옳습니다.
    /// </summary>
    public sealed class UnitContextTests
    {
        // ====================================================================================================
        // 1. Fake
        // ====================================================================================================

        /// <summary>
        /// 병사가 볼 수 있는 것만 들고 있는 최소 구현입니다.
        ///
        /// 공간 질의는 전수 조사로 답합니다. 색인이 필요할 만큼 유닛이 많지 않고,
        /// 무엇보다 <b>정답이 자명해야</b> 검증에 쓸 수 있습니다.
        /// </summary>
        private sealed class MinimalUnitContext : IUnitContext
        {
            private readonly List<Unit> _registered = new List<Unit>();

            public MinimalUnitContext(IslandGrid grid, BattleTuning tuning)
            {
                Grid = grid;
                Tuning = tuning;
            }

            public IslandGrid Grid { get; }

            public BattleTuning Tuning { get; }

            public ProjectilePool ProjectilePool { get; } = new ProjectilePool();

            /// <summary>소리를 내지 않습니다. 났는지를 검사가 판단할 방법이 없습니다.</summary>
            public IBattleAudio Audio { get; } = SilentBattleAudio.Instance;

            /// <summary>등록된 유닛입니다. 검증용으로만 노출합니다.</summary>
            public IReadOnlyList<Unit> Registered => _registered;

            public void Register(Unit unit)
            {
                if (unit != null && !_registered.Contains(unit))
                {
                    _registered.Add(unit);
                }
            }

            public void Unregister(Unit unit)
            {
                _registered.Remove(unit);
            }

            public Unit FindNearestEnemy(Vector3 position, Team myTeam, float maxDistance)
            {
                Unit best = null;
                float bestSqr = maxDistance * maxDistance;

                for (int i = 0; i < _registered.Count; i++)
                {
                    var candidate = _registered[i];
                    if (candidate == null || !candidate.IsAlive || candidate.Team == myTeam)
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

            public int QueryTeam(Vector3 position, float radius, Team team, Unit exclude, List<Unit> buffer)
            {
                buffer.Clear();

                float sqrRadius = radius * radius;

                for (int i = 0; i < _registered.Count; i++)
                {
                    var candidate = _registered[i];
                    if (candidate == null || candidate == exclude || !candidate.IsAlive || candidate.Team != team)
                    {
                        continue;
                    }

                    if ((candidate.Position - position).sqrMagnitude <= sqrRadius)
                    {
                        buffer.Add(candidate);
                    }
                }

                return buffer.Count;
            }
        }

        // ====================================================================================================
        // 2. Setup / Teardown
        // ====================================================================================================

        private readonly List<GameObject> _spawned = new List<GameObject>();

        private MinimalUnitContext _context;
        private BattleTuning _tuning;
        private UnitDefinition _definition;

        [SetUp]
        public void SetUp()
        {
            _tuning = BattleTuning.CreateDefault();
            _definition = UnitDefinition.CreateDefault(UnitRole.Militia);
            _context = new MinimalUnitContext(TestIsland.Create(20260808), _tuning);
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

            if (_tuning != null)
            {
                Object.DestroyImmediate(_tuning);
            }
        }

        private Unit CreateUnit(Vector3 position, Team team)
        {
            var go = new GameObject($"TestUnit_{team}");
            _spawned.Add(go);
            go.transform.position = position;

            var unit = go.AddComponent<Unit>();
            unit.Initialize(_definition, team, _context);

            return unit;
        }

        // ====================================================================================================
        // 3. Tests
        // ====================================================================================================

        [Test]
        public void 경로_탐색기와_점유_없이도_병사가_선다()
        {
            var unit = CreateUnit(Vector3.zero, Team.Player);

            Assert.IsNotNull(unit);
            Assert.IsTrue(unit.IsAlive);
            Assert.AreEqual(_definition.MaxHealth, unit.Health, 0.001f);
        }

        [Test]
        public void 초기화하면_스스로_등록한다()
        {
            var unit = CreateUnit(Vector3.zero, Team.Player);

            Assert.AreEqual(1, _context.Registered.Count, "등록부에 들어가지 않았습니다.");
            Assert.AreSame(unit, _context.Registered[0]);
        }

        /// <summary>
        /// 쓰러지면 명부에서 빠집니다.
        ///
        /// <b>왜 파괴가 아니라 <c>Kill</c> 로 확인하는가</b>
        ///
        /// 병사는 <c>OnDestroy</c> 에서도 등록을 푸는데, EditMode 에서는 일반 MonoBehaviour 의
        /// 생명주기 콜백이 돌지 않습니다. 오브젝트를 지워 놓고 기다려도 아무 일도 일어나지 않고,
        /// 그건 코드가 틀린 것이 아니라 <b>여기서 관측할 수 없는 것</b>입니다.
        ///
        /// 실제로 중요한 경로는 이쪽입니다. 전투 중에 명부를 비우는 것은 파괴가 아니라 죽음이고,
        /// <c>OnDestroy</c> 는 씬을 벗어날 때를 위한 이중 장치입니다.
        /// 그쪽은 PlayMode 가 봅니다.
        /// </summary>
        [Test]
        public void 쓰러지면_등록이_풀린다()
        {
            var unit = CreateUnit(Vector3.zero, Team.Player);

            Assert.AreEqual(1, _context.Registered.Count);

            // Kill 은 마지막에 Object.Destroy 를 부릅니다. 런타임에서는 옳지만
            // EditMode 에서는 유니티가 오류를 남기므로, 그 잡음만 무시합니다.
            LogAssert.ignoreFailingMessages = true;

            unit.Kill();

            Assert.AreEqual(0, _context.Registered.Count, "쓰러진 병사가 등록부에 남아 있습니다.");
        }

        /// <summary>
        /// 무기가 근접 위협을 살필 때 쓰는 경로입니다. 공간 질의 하나면 답이 나와야 합니다.
        /// </summary>
        [Test]
        public void 공간_질의만으로_가까운_적을_찾는다()
        {
            var me = CreateUnit(Vector3.zero, Team.Player);

            CreateUnit(new Vector3(8f, 0f, 0f), Team.Enemy);
            var nearest = CreateUnit(new Vector3(2f, 0f, 0f), Team.Enemy);

            Assert.AreSame(nearest, me.FindClosestEnemyWithin(20f));
        }

        [Test]
        public void 같은_진영은_적으로_잡지_않는다()
        {
            var me = CreateUnit(Vector3.zero, Team.Player);
            CreateUnit(new Vector3(1f, 0f, 0f), Team.Player);

            Assert.IsNull(me.FindClosestEnemyWithin(20f));
        }

        /// <summary>
        /// 무기가 붙고 튜닝이 전달되었는지 봅니다.
        /// 병사가 받은 것만으로 무기까지 세워진다는 뜻입니다.
        /// </summary>
        [Test]
        public void 무기가_붙고_진형_선호도가_읽힌다()
        {
            var unit = CreateUnit(Vector3.zero, Team.Player);

            Assert.IsNotNull(unit.GetComponent<WeaponBase>(), "무기가 붙지 않았습니다.");
            Assert.IsFalse(unit.PrefersLineFormation, "민병은 횡대를 선호하지 않습니다.");
        }

        [Test]
        public void 공격_예약_정원은_체력에서_나온다()
        {
            var target = CreateUnit(Vector3.zero, Team.Enemy);
            var first = CreateUnit(new Vector3(1f, 0f, 0f), Team.Player);
            var second = CreateUnit(new Vector3(-1f, 0f, 0f), Team.Player);

            // 한 방에 죽는 피해량이면 정원은 한 명입니다.
            float oneShot = target.Health + 1f;

            Assert.IsTrue(target.TryCommitAttacker(first, oneShot, 4));
            Assert.IsFalse(target.TryCommitAttacker(second, oneShot, 4), "정원을 넘겨 예약했습니다.");

            target.ReleaseAttacker(first);

            Assert.IsTrue(target.TryCommitAttacker(second, oneShot, 4));
        }
    }
}
