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
    /// 병사가 <b>표적을 다시 고르는</b> 규칙을 고정합니다.
    ///
    /// <b>왜 이 테스트가 필요한가</b>
    ///
    /// 재평가는 오래 죽어 있었습니다. "고정 시간이 남았으면 유지" 다음에
    /// "표적이 있으면 유지"가 그대로 남아 있어서, 둘을 합치면
    /// <b>표적이 있으면 언제나 유지</b>였습니다. 결과는 두 가지였습니다.
    ///
    ///   · <c>PikeTargetLockSeconds</c> 를 아무 값으로 바꿔도 게임이 달라지지 않는다
    ///   · 대기열이 꽉 찼을 때 다음 적을 겨누는 <see cref="UnitTargeting.BreakLock"/> 가 아무 일도 하지 않는다
    ///
    /// 둘 다 <b>컴파일러도 런타임도 잡지 못하는</b> 종류입니다. 예외가 나지 않고,
    /// 화면에는 "창병이 어쩐지 뒤를 안 막네" 로만 나타납니다.
    /// 그래서 성질을 여기 못 박아 두지 않으면 같은 자리로 조용히 되돌아갑니다.
    ///
    /// 검증 대상은 <see cref="UnitTargeting"/> 하나입니다. 무기도 분대도 끼우지 않습니다 —
    /// 표적을 고르는 데 필요한 것은 "내 주변에 누가 있는가" 가 전부이기 때문입니다.
    /// </summary>
    public sealed class UnitTargetingTests
    {
        // ====================================================================================================
        // 1. Fake
        // ====================================================================================================

        /// <summary>
        /// 병사를 세우는 데 필요한 최소 구현입니다.
        ///
        /// 공간 질의는 전수 조사로 답합니다. 유닛이 서너 개뿐이고, 무엇보다
        /// <b>정답이 자명해야</b> 재평가 규칙을 검증하는 데 쓸 수 있습니다.
        /// (색인 자체의 정확성은 <c>SpatialGridTests</c> 가 따로 봅니다)
        /// </summary>
        private sealed class BruteForceUnitContext : IUnitContext
        {
            private readonly List<Unit> _registered = new List<Unit>();

            public BruteForceUnitContext(IslandGrid grid, BattleTuning tuning)
            {
                Grid = grid;
                Tuning = tuning;
            }

            public IslandGrid Grid { get; }

            public BattleTuning Tuning { get; }

            public ProjectilePool ProjectilePool { get; } = new ProjectilePool();

            /// <summary>소리를 내지 않습니다. 났는지를 검사가 판단할 방법이 없습니다.</summary>
            public IBattleAudio Audio { get; } = SilentBattleAudio.Instance;

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
        // 2. Constants
        // ====================================================================================================

        /// <summary>창병의 표적 고정 시간에 해당하는 값입니다.</summary>
        private const float LockSeconds = 1.1f;

        /// <summary>고정 시간을 확실히 넘기는 경과 시간입니다.</summary>
        private const float PastLock = LockSeconds + 0.1f;

        /// <summary>고정은 없지만 최소 재평가 주기는 넘기는 경과 시간입니다.</summary>
        private const float PastRetargetInterval = 0.4f;

        /// <summary>대기열을 쓰지 않는 무기(검·활)의 고정 시간입니다.</summary>
        private const float NoLock = 0f;

        // ====================================================================================================
        // 3. Setup / Teardown
        // ====================================================================================================

        private readonly List<GameObject> _spawned = new List<GameObject>();

        private BruteForceUnitContext _context;
        private BattleTuning _tuning;
        private UnitDefinition _definition;
        private UnitTargeting _targeting;
        private Unit _owner;

        [SetUp]
        public void SetUp()
        {
            _tuning = BattleTuning.CreateDefault();

            _definition = UnitDefinition.CreateDefault(UnitRole.Militia);

            // 수치를 여기서 못 박습니다. 병과 기본값이 바뀌어도 이 테스트가 검증하는
            // 재평가 규칙은 그대로여야 하고, 거리도 눈으로 읽을 수 있어야 합니다.
            _definition.EngageRadius = 10f;
            _definition.MaxHealth = 30f;
            _definition.AttackDamage = 8f;

            _context = new BruteForceUnitContext(TestIsland.Create(20260809), _tuning);

            _owner = CreateUnit(Vector3.zero, Team.Player);

            _targeting = new UnitTargeting();
            _targeting.Configure(_owner, _context, _definition);
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

        // ====================================================================================================
        // 4. Helpers
        // ====================================================================================================

        private Unit CreateUnit(Vector3 position, Team team)
        {
            var go = new GameObject($"TestUnit_{team}_{_spawned.Count}");
            _spawned.Add(go);
            go.transform.position = position;

            var unit = go.AddComponent<Unit>();
            unit.Initialize(_definition, team, _context);

            return unit;
        }

        /// <summary>소유자로부터 <paramref name="distance"/> 만큼 떨어진 곳에 적을 세웁니다.</summary>
        private Unit CreateEnemyAt(float distance)
        {
            return CreateUnit(new Vector3(0f, 0f, distance), Team.Enemy);
        }

        /// <summary>대기열을 쓰는 무기(창)의 갱신입니다.</summary>
        private void RefreshWithQueue(int maxAttackers = 4)
        {
            _targeting.Refresh(usesAttackQueue: true, LockSeconds, maxAttackers);
        }

        /// <summary>대기열을 쓰지 않는 무기(검·활)의 갱신입니다.</summary>
        private void RefreshPlain(float lockSeconds = NoLock)
        {
            _targeting.Refresh(usesAttackQueue: false, lockSeconds, 4);
        }

        // ====================================================================================================
        // 5. Tests - 고정
        // ====================================================================================================

        /// <summary>
        /// 고정이 실제로 무언가를 막습니다.
        ///
        /// 이것이 성립하지 않으면 <c>PikeTargetLockSeconds</c> 는 인스펙터에만 있는 값입니다.
        /// </summary>
        [Test]
        public void 고정_시간_동안에는_더_가까운_적이_와도_표적을_바꾸지_않는다()
        {
            var far = CreateEnemyAt(6f);

            RefreshPlain(LockSeconds);
            Assert.AreSame(far, _targeting.Target, "먼 적을 먼저 잡지 못했습니다.");

            CreateEnemyAt(2f);

            _targeting.Tick(LockSeconds * 0.5f);
            RefreshPlain(LockSeconds);

            Assert.AreSame(far, _targeting.Target, "고정 시간이 남았는데 표적이 바뀌었습니다.");
        }

        /// <summary>
        /// 고정이 풀리면 다시 봅니다. <b>이 성질이 오래 죽어 있었습니다.</b>
        /// </summary>
        [Test]
        public void 고정이_풀리면_눈에_띄게_가까운_적으로_옮긴다()
        {
            var far = CreateEnemyAt(6f);

            RefreshPlain(LockSeconds);
            Assert.AreSame(far, _targeting.Target);

            var near = CreateEnemyAt(2f);

            _targeting.Tick(PastLock);
            RefreshPlain(LockSeconds);

            Assert.AreSame(near, _targeting.Target, "고정이 풀렸는데도 표적을 다시 고르지 않았습니다.");
        }

        /// <summary>
        /// 마진이 없으면 나란히 선 두 적 사이에서 표적이 계속 뒤집힙니다.
        ///
        /// 재평가를 되살리면서 <b>함께 들어와야 하는</b> 성질입니다.
        /// 이것이 없으면 "죽은 로직을 고쳤더니 병사가 떤다"가 됩니다.
        /// </summary>
        [Test]
        public void 거리가_엇비슷하면_고정이_풀려도_표적을_유지한다()
        {
            var first = CreateEnemyAt(6f);

            RefreshPlain(LockSeconds);
            Assert.AreSame(first, _targeting.Target);

            // 조금 더 가깝지만(6 → 5.4) 갈아탈 만큼은 아닙니다.
            CreateEnemyAt(5.4f);

            _targeting.Tick(PastLock);
            RefreshPlain(LockSeconds);

            Assert.AreSame(first, _targeting.Target, "미세하게 가까운 적에게 표적이 넘어갔습니다.");
        }

        /// <summary>
        /// 고정을 쓰지 않는 무기도 <b>매 프레임 다시 고르지는 않습니다.</b>
        ///
        /// 재평가는 공간 질의를 한 번 씁니다. 살아 있는 표적을 든 병사가 프레임마다 이것을 돌리면
        /// 유닛 수에 비례해 질의가 그대로 늘어납니다. 재평가를 되살리는 대가로
        /// 프레임 비용을 올리지 않겠다는 결정을 여기 고정합니다.
        /// </summary>
        [Test]
        public void 고정_시간이_0이어도_매_프레임_다시_고르지_않는다()
        {
            var far = CreateEnemyAt(6f);

            RefreshPlain();
            Assert.AreSame(far, _targeting.Target);

            var near = CreateEnemyAt(2f);

            // 같은 프레임에 바로 다시 부릅니다. 최소 주기가 지나지 않았습니다.
            RefreshPlain();
            Assert.AreSame(far, _targeting.Target, "최소 재평가 주기를 지나지 않았는데 다시 골랐습니다.");

            _targeting.Tick(PastRetargetInterval);
            RefreshPlain();

            Assert.AreSame(near, _targeting.Target, "최소 주기가 지났는데도 다시 고르지 않았습니다.");
        }

        // ====================================================================================================
        // 6. Tests - 공격 대기열
        // ====================================================================================================

        /// <summary>
        /// <b>이 테스트가 이번 수정의 본체입니다.</b>
        ///
        /// 창병 여럿이 최전방의 한 명만 동시에 찌르면, 그가 쓰러진 자리로
        /// 뒤따라오던 적들이 그대로 통과합니다. 방어선의 목적은 잘 죽이는 것이 아니라
        /// <b>새지 않는 것</b>이므로, 자리가 찬 적은 <b>거리를 따지지 않고</b> 놓아야 합니다.
        ///
        /// 재평가가 죽어 있던 동안 이 규칙은 코드에 있으면서 동작하지 않았습니다.
        /// </summary>
        [Test]
        public void 대기열이_꽉_찬_표적은_거리를_따지지_않고_놓는다()
        {
            var near = CreateEnemyAt(2f);
            var far = CreateEnemyAt(6f);

            // 정원 상한 1이면 한 명만 붙을 수 있습니다.
            RefreshWithQueue(maxAttackers: 1);
            Assert.AreSame(near, _targeting.Target, "가까운 적을 먼저 잡지 못했습니다.");

            // 다른 병사가 먼저 자리를 채웁니다.
            var rival = CreateUnit(new Vector3(0f, 0f, -3f), Team.Player);
            Assert.IsTrue(near.TryCommitAttacker(rival, _definition.AttackDamage, 1));

            // 실제로는 Unit.TickCombat 이 예약에 실패하면서 이것을 부릅니다.
            _targeting.BreakLock();
            RefreshWithQueue(maxAttackers: 1);

            Assert.AreSame(far, _targeting.Target,
                "자리가 없는 적을 계속 겨누고 있습니다. 그 뒤로 걸어 들어오는 적을 아무도 막지 않습니다.");
        }

        /// <summary>
        /// 전부 자리가 찼으면 평소대로 가장 가까운 적을 봅니다.
        /// 그러지 않으면 방어선 전체가 손을 놓고 서 있게 됩니다.
        /// </summary>
        [Test]
        public void 모두_자리가_차_있으면_가장_가까운_적으로_되돌아간다()
        {
            var near = CreateEnemyAt(2f);
            var far = CreateEnemyAt(6f);

            RefreshWithQueue(maxAttackers: 1);
            Assert.AreSame(near, _targeting.Target);

            var firstRival = CreateUnit(new Vector3(0f, 0f, -3f), Team.Player);
            var secondRival = CreateUnit(new Vector3(0f, 0f, -4f), Team.Player);

            Assert.IsTrue(near.TryCommitAttacker(firstRival, _definition.AttackDamage, 1));
            Assert.IsTrue(far.TryCommitAttacker(secondRival, _definition.AttackDamage, 1));

            _targeting.BreakLock();
            RefreshWithQueue(maxAttackers: 1);

            Assert.AreSame(near, _targeting.Target, "갈 곳이 없으면 가장 가까운 적을 봐야 합니다.");
        }

        // ====================================================================================================
        // 7. Tests - 예약 반납
        // ====================================================================================================

        /// <summary>
        /// 표적을 옮기면 두고 온 자리를 반드시 놓아야 합니다.
        ///
        /// <b>재평가를 되살리면서 새로 생기는 위험입니다.</b>
        /// 표적을 바꾸지 않던 동안에는 예약이 샐 일이 없었습니다.
        /// 빠뜨리면 떠나온 적의 정원이 한 자리 줄어든 채 영영 남고,
        /// 그 적에게는 실제 인원보다 적은 수만 달라붙습니다. 증상이 겉으로 드러나지 않습니다.
        /// </summary>
        [Test]
        public void 표적을_옮기면_이전_표적의_예약이_풀린다()
        {
            var first = CreateEnemyAt(6f);

            RefreshWithQueue();
            Assert.AreSame(first, _targeting.Target);

            Assert.IsTrue(first.TryCommitAttacker(_owner, _definition.AttackDamage, 4));
            Assert.AreEqual(1, first.CommittedAttackerCount);

            var closer = CreateEnemyAt(2f);

            _targeting.Tick(PastLock);
            RefreshWithQueue();

            Assert.AreSame(closer, _targeting.Target, "가까운 적으로 옮기지 않았습니다.");
            Assert.AreEqual(0, first.CommittedAttackerCount, "떠나온 표적에 예약이 남았습니다.");
        }

        /// <summary>
        /// 표적이 쓰러지면 예약을 반납하고 다음 적을 고릅니다.
        /// </summary>
        [Test]
        public void 표적이_쓰러지면_예약을_반납하고_다음_적을_고른다()
        {
            var first = CreateEnemyAt(2f);
            var next = CreateEnemyAt(6f);

            RefreshWithQueue();
            Assert.AreSame(first, _targeting.Target);

            Assert.IsTrue(first.TryCommitAttacker(_owner, _definition.AttackDamage, 4));

            // Kill 은 마지막에 Object.Destroy 를 부릅니다. 런타임에서는 옳지만
            // EditMode 에서는 유니티가 오류를 남기므로, 그 잡음만 무시합니다.
            LogAssert.ignoreFailingMessages = true;
            first.Kill();

            RefreshWithQueue();

            Assert.AreSame(next, _targeting.Target, "쓰러진 표적을 놓고 다음 적을 고르지 못했습니다.");
            Assert.AreEqual(0, first.CommittedAttackerCount, "쓰러진 표적에 예약이 남았습니다.");
        }

        /// <summary>
        /// 교전 반경 밖으로 멀어지면 놓습니다. 히스테리시스 때문에 반경보다 넓게 잡습니다.
        /// </summary>
        [Test]
        public void 표적이_이탈_거리를_넘으면_놓는다()
        {
            var enemy = CreateEnemyAt(6f);

            RefreshPlain(LockSeconds);
            Assert.AreSame(enemy, _targeting.Target);

            // 교전 반경 10, 이탈 배수 1.5 → 15 를 넘겨야 놓습니다.
            enemy.transform.position = new Vector3(0f, 0f, 16f);

            RefreshPlain(LockSeconds);

            Assert.IsNull(_targeting.Target, "이탈 거리를 넘겼는데 표적을 붙들고 있습니다.");
        }
    }
}
