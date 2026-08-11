using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Squads;
using SRPG.Gameplay.Units;
using SRPG.Systems.Grid;
using SRPG.Tests.Support;
using UnityEngine;
using UnityEngine.TestTools;

namespace SRPG.Tests
{
    /// <summary>
    /// 분대가 병사를 대신 돌리는 규약을 검증합니다.
    ///
    /// <b>무엇이 위험해졌는가</b>
    ///
    /// 병사에게 <c>Update</c> 가 있던 시절에는 유니티가 알아서 불러 주었으므로
    /// "안 돌 수 있다"는 상태 자체가 없었습니다. 지금은 <b>부르는 쪽이 코드</b>라
    /// 새로 생긴 실패 방식이 셋 있습니다.
    ///
    ///   · 아무도 안 돌린다 — 전장이 아무 오류 없이 얼어붙습니다
    ///   · 죽은 병사를 계속 돌린다 — 시체가 마지막 명령을 따라 계속 걸어갑니다
    ///   · 순회 도중 누가 죽으면 뒷사람 차례가 날아간다 — 한 프레임씩 멎는 병사가 생깁니다
    ///
    /// 셋 다 화면에서는 "가끔 이상하다"로만 보이고 오류는 나지 않습니다.
    /// 명부가 <c>MonoBehaviour</c> 가 아니라서 여기서 직접 물을 수 있습니다.
    ///
    /// <b>경직 시간으로 확인합니다.</b> 자리 이동은 표적·슬롯·지형에 좌우되어 전제가 늘어나는데,
    /// 경직은 시간만 흐르면 줄어듭니다. 여기서 보려는 것이 "시간이 전달되는가"이므로 그 편이 맞습니다.
    /// </summary>
    public sealed class SquadTickTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        /// <summary>검사에서 만든 오브젝트입니다. 끝나면 지웁니다.</summary>
        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>병사를 세울 최소 지형입니다.</summary>
        private IslandGrid _grid;

        /// <summary>병사가 볼 컨텍스트입니다.</summary>
        private TestUnitContext _context;

        /// <summary>검사에 쓰는 튜닝입니다.</summary>
        private BattleTuning _tuning;

        /// <summary>병사를 찍어 낼 정의입니다.</summary>
        private UnitDefinition _definition;

        [SetUp]
        public void SetUp()
        {
            _grid = TestIsland.CreateFlat();
            _tuning = BattleTuning.CreateDefault();
            _context = new TestUnitContext(_grid, _tuning);
            _definition = UnitDefinition.CreateDefault(UnitRole.Infantry);
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
        // 2. Tests - 시간이 전달된다
        // ====================================================================================================

        /// <summary>
        /// 명부가 병사를 실제로 돌립니다.
        ///
        /// <b>이것이 끊기면 아무 오류 없이 전장이 얼어붙습니다.</b>
        /// 분대는 앵커를 옮기고 화면은 멀쩡해 보이는데 병사만 서 있습니다.
        /// </summary>
        [Test]
        public void 명부가_소속_병사를_돌린다()
        {
            var members = new SquadMembers();
            var unit = CreateUnit(Vector3.zero, Team.Player);

            members.Add(unit);

            unit.ApplyKnockback(Vector3.zero, 0.2f);
            Assert.IsTrue(unit.IsStaggered, "전제가 성립하지 않습니다 — 경직이 걸려야 합니다.");

            members.Tick(0.5f);

            Assert.IsFalse(unit.IsStaggered, "병사가 돌지 않아 경직이 풀리지 않았습니다.");
        }

        /// <summary>
        /// 시간이 흐르지 않았으면 아무것도 하지 않습니다.
        ///
        /// 일시정지에서 <c>deltaTime</c> 이 0이 됩니다.
        /// 그때 도는 것이 있으면 "멈췄는데 조금씩 진행되는" 상태가 됩니다.
        /// </summary>
        [Test]
        public void 시간이_흐르지_않으면_돌지_않는다()
        {
            var members = new SquadMembers();
            var unit = CreateUnit(Vector3.zero, Team.Player);

            members.Add(unit);

            unit.ApplyKnockback(Vector3.zero, 0.2f);

            members.Tick(0f);

            Assert.IsTrue(unit.IsStaggered, "시간이 흐르지 않았는데 경직이 줄었습니다.");
        }

        // ====================================================================================================
        // 3. Tests - 순회 중 사망
        // ====================================================================================================

        /// <summary>
        /// 쓰러진 병사는 건너뜁니다.
        ///
        /// 명부에서 빠지는 것은 다음 프레임의 <c>PruneDead</c> 이므로,
        /// 쓰러진 그 프레임에는 아직 목록에 남아 있습니다. 그 사이를 여기서 막습니다.
        /// </summary>
        [Test]
        public void 쓰러진_병사는_건너뛴다()
        {
            var members = new SquadMembers();
            var alive = CreateUnit(Vector3.zero, Team.Player);
            var fallen = CreateUnit(new Vector3(2f, 0f, 0f), Team.Player);

            members.Add(alive);
            members.Add(fallen);

            // Unit.Kill 은 마지막에 Object.Destroy 를 부릅니다. 런타임에서는 옳지만
            // EditMode 에서는 유니티가 "Destroy may not be called from edit mode" 에러를 남깁니다.
            // 프로덕션 코드를 검사에 맞춰 비틀지 않기 위해, 여기서만 그 잡음을 무시합니다.
            LogAssert.ignoreFailingMessages = true;
            fallen.Kill();

            Assert.DoesNotThrow(() => members.Tick(0.5f), "쓰러진 병사를 돌리려다 터졌습니다.");
            Assert.AreEqual(2, members.Count, "이번 프레임에는 아직 명부에 남아 있어야 합니다.");
        }

        /// <summary>
        /// 순회 도중 누가 죽어도 뒤에 선 병사의 차례가 날아가지 않습니다.
        ///
        /// 한 병사가 다른 병사를 죽이는 것은 전투에서 늘 일어나는 일입니다.
        /// 그때 목록을 줄이면 뒷사람의 인덱스가 당겨져 그 프레임을 통째로 건너뜁니다.
        /// </summary>
        [Test]
        public void 순회_중_사망해도_뒷사람의_차례가_남는다()
        {
            var members = new SquadMembers();
            var first = CreateUnit(Vector3.zero, Team.Player);
            var second = CreateUnit(new Vector3(2f, 0f, 0f), Team.Player);
            var third = CreateUnit(new Vector3(4f, 0f, 0f), Team.Player);

            members.Add(first);
            members.Add(second);
            members.Add(third);

            third.ApplyKnockback(Vector3.zero, 0.2f);

            LogAssert.ignoreFailingMessages = true;
            second.Kill();

            members.Tick(0.5f);

            Assert.IsFalse(third.IsStaggered, "가운데 병사가 쓰러지자 뒤에 선 병사의 차례가 날아갔습니다.");
        }

        /// <summary>
        /// 빈 명부를 돌려도 터지지 않습니다. 전원이 쓰러진 직후의 한 프레임이 이 경로입니다.
        /// </summary>
        [Test]
        public void 빈_명부를_돌려도_터지지_않는다()
        {
            var members = new SquadMembers();

            Assert.DoesNotThrow(() => members.Tick(0.5f));
        }

        // ====================================================================================================
        // 4. Helpers
        // ====================================================================================================

        /// <summary>
        /// 검사용 병사를 세웁니다. 정리는 <see cref="TearDown"/> 이 합니다.
        /// </summary>
        /// <param name="position">세울 자리입니다.</param>
        /// <param name="team">소속 진영입니다.</param>
        /// <returns>초기화된 병사입니다.</returns>
        private Unit CreateUnit(Vector3 position, Team team)
        {
            var go = new GameObject($"TestUnit_{team}");
            go.transform.position = position;

            _spawned.Add(go);

            var unit = go.AddComponent<Unit>();
            unit.Initialize(_definition, team, _context);

            return unit;
        }
    }
}
