using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Squads;
using SRPG.Gameplay.Units;
using SRPG.Tests.Support;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 양측 분대가 <b>함께 쓰는</b> 명부·슬롯 장부를 검증합니다.
    ///
    /// <b>왜 떼어 놓고 검증하는가</b>
    ///
    /// 아군 분대와 적 분대가 같은 일을 각자 하고 있었습니다 —
    /// 사망자 걸러내기, 병사와 슬롯의 짝 다시 짜기, 배정된 자리 꺼내기.
    /// 거의 같은 코드였는데 <b>이미 어긋나 있었습니다</b>:
    /// 재배정 주기를 아군은 튜닝 에셋에서 읽고 적은 코드 상수를 보고 있어서,
    /// 기획자가 값을 바꾸면 절반만 바뀌었습니다.
    ///
    /// 하나로 합친 지금, 그 하나가 만족해야 할 성질을 여기 못 박습니다.
    /// 성질이 깨지면 <b>양측이 함께</b> 빨간불이 켜집니다 — 그게 합친 이유입니다.
    /// </summary>
    public sealed class SquadMembersTests
    {
        // ====================================================================================================
        // 1. Setup / Teardown
        // ====================================================================================================

        /// <summary>중심 하나와 좌우 둘. 자리 배정을 눈으로 따라갈 수 있는 최소 진형입니다.</summary>
        private static readonly List<Vector3> Slots = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(4f, 0f, 0f),
            new Vector3(-4f, 0f, 0f),
        };

        private readonly List<GameObject> _spawned = new List<GameObject>();

        private TestUnitContext _context;
        private BattleTuning _tuning;
        private UnitDefinition _definition;

        [SetUp]
        public void SetUp()
        {
            _tuning = BattleTuning.CreateDefault();
            _definition = UnitDefinition.CreateDefault(UnitRole.Militia);
            _context = new TestUnitContext(TestIsland.Create(20260809), _tuning);
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

        private Unit CreateUnit(Vector3 position, bool isCommander = false)
        {
            var go = new GameObject($"TestUnit_{_spawned.Count}");
            _spawned.Add(go);
            go.transform.position = position;

            var unit = go.AddComponent<Unit>();
            unit.Initialize(_definition, Team.Player, _context, isCommander);

            return unit;
        }

        // ====================================================================================================
        // 2. Tests - 명부
        // ====================================================================================================

        [Test]
        public void 사라진_병사는_명부에서_빠진다()
        {
            var members = new SquadMembers(4);

            members.Add(CreateUnit(Vector3.zero));
            var doomed = CreateUnit(new Vector3(4f, 0f, 0f));
            members.Add(doomed);

            Assert.AreEqual(2, members.Count);

            Object.DestroyImmediate(doomed.gameObject);
            members.PruneDead();

            Assert.AreEqual(1, members.Count, "사라진 병사가 명부에 남았습니다.");
        }

        [Test]
        public void 비우면_배정도_함께_사라진다()
        {
            var members = new SquadMembers(4);
            members.Add(CreateUnit(Vector3.zero));

            members.ReassignSlots(Slots, 10f, 0f, Vector3.zero);
            Assert.IsTrue(members.TryGetSlot(0, Slots, out _));

            members.Clear();

            Assert.AreEqual(0, members.Count);
            Assert.IsFalse(members.TryGetSlot(0, Slots, out _), "비운 뒤에도 배정이 남았습니다.");
        }

        // ====================================================================================================
        // 3. Tests - 배정 주기
        // ====================================================================================================

        /// <summary>
        /// 매 프레임 다시 짜면 미세한 위치 변화로 슬롯이 뒤바뀌며 병사들이 자리를 두고 떱니다.
        /// </summary>
        [Test]
        public void 주기_안에는_다시_짜지_않는다()
        {
            var members = new SquadMembers(4);

            var mover = CreateUnit(new Vector3(4f, 0f, 0f));   // 오른쪽 자리 근처
            var other = CreateUnit(new Vector3(-4f, 0f, 0f));  // 왼쪽 자리 근처

            members.Add(mover);
            members.Add(other);

            members.ReassignSlots(Slots, 10f, 0f, Vector3.zero);

            Assert.IsTrue(members.TryGetSlot(0, Slots, out Vector3 before));

            // 반대편으로 옮겨 놓습니다. 지금 다시 짜면 자리가 뒤바뀔 상황입니다.
            mover.transform.position = new Vector3(-4.5f, 0f, 0f);

            members.ReassignSlots(Slots, 10f, 0.1f, Vector3.zero);

            Assert.IsTrue(members.TryGetSlot(0, Slots, out Vector3 after));
            Assert.AreEqual(before, after, "주기가 지나지 않았는데 자리를 다시 짰습니다.");
        }

        [Test]
        public void 주기가_지나면_다시_짠다()
        {
            var members = new SquadMembers(4);

            var mover = CreateUnit(new Vector3(4f, 0f, 0f));
            members.Add(mover);
            members.Add(CreateUnit(new Vector3(-4f, 0f, 0f)));

            members.ReassignSlots(Slots, 1f, 0f, Vector3.zero);
            Assert.IsTrue(members.TryGetSlot(0, Slots, out Vector3 before));

            mover.transform.position = new Vector3(-4.5f, 0f, 0f);

            members.ReassignSlots(Slots, 1f, 1.5f, Vector3.zero);

            Assert.IsTrue(members.TryGetSlot(0, Slots, out Vector3 after));
            Assert.AreNotEqual(before, after, "주기가 지났는데도 자리를 다시 짜지 않았습니다.");
        }

        /// <summary>
        /// 인원이 바뀌면 주기를 기다리지 않습니다.
        /// 그러지 않으면 누가 쓰러진 뒤 대열이 한동안 비뚤어진 채로 남습니다.
        /// </summary>
        [Test]
        public void 인원이_바뀌면_주기를_기다리지_않는다()
        {
            var members = new SquadMembers(4);

            members.Add(CreateUnit(Vector3.zero));
            members.Add(CreateUnit(new Vector3(4f, 0f, 0f)));

            var doomed = CreateUnit(new Vector3(-4f, 0f, 0f));
            members.Add(doomed);

            members.ReassignSlots(Slots, 100f, 0f, Vector3.zero);
            Assert.IsTrue(members.TryGetSlot(2, Slots, out _), "세 번째 병사가 자리를 못 받았습니다.");

            Object.DestroyImmediate(doomed.gameObject);
            members.PruneDead();

            // 주기는 한참 남았지만 인원이 줄었으므로 곧바로 다시 짜야 합니다.
            members.ReassignSlots(Slots, 100f, 0f, Vector3.zero);

            Assert.AreEqual(2, members.Count);
            Assert.IsFalse(members.TryGetSlot(2, Slots, out _), "줄어든 인원의 배정이 남았습니다.");
        }

        // ====================================================================================================
        // 4. Tests - 지휘관
        // ====================================================================================================

        /// <summary>
        /// 지휘관은 진형 중심에 섭니다. 방향 없는 진형에서 사방으로부터 가장 안쪽이
        /// 곧 가장 안전한 자리이고, 지휘관이 죽으면 분대가 영구 소멸하기 때문입니다.
        ///
        /// <b>장부가 스스로 지휘관을 찾습니다.</b> 예전에는 아군 분대만 인덱스를 찾아 넘기고
        /// 적 분대는 넘기지 않아 두 호출이 서로 다른 모양이었습니다.
        /// </summary>
        [Test]
        public void 지휘관은_어디_서_있든_중심_자리를_받는다()
        {
            var members = new SquadMembers(4);

            // 지휘관을 일부러 중심에서 가장 먼 곳에 세웁니다.
            members.Add(CreateUnit(new Vector3(0.2f, 0f, 0f)));
            members.Add(CreateUnit(new Vector3(4f, 0f, 0f)));
            members.Add(CreateUnit(new Vector3(-9f, 0f, 0f), isCommander: true));

            members.ReassignSlots(Slots, 10f, 0f, Vector3.zero);

            Assert.IsTrue(members.TryGetSlot(2, Slots, out Vector3 commanderSlot));
            Assert.AreEqual(Slots[0], commanderSlot, "지휘관이 중심에 서지 않았습니다.");
        }

        /// <summary>
        /// 지휘관이 없는 적 분대는 아무도 고정되지 않습니다.
        /// 진영별 분기 없이 <b>같은 규칙 하나</b>로 양측이 처리된다는 뜻입니다.
        /// </summary>
        [Test]
        public void 지휘관이_없으면_중심도_가까운_병사가_받는다()
        {
            var members = new SquadMembers(4);

            var nearest = CreateUnit(new Vector3(0.2f, 0f, 0f));
            members.Add(nearest);
            members.Add(CreateUnit(new Vector3(4f, 0f, 0f)));
            members.Add(CreateUnit(new Vector3(-9f, 0f, 0f)));

            members.ReassignSlots(Slots, 10f, 0f, Vector3.zero);

            Assert.IsTrue(members.TryGetSlot(0, Slots, out Vector3 slot));
            Assert.AreEqual(Slots[0], slot, "중심에 가장 가까운 병사가 중심을 받아야 합니다.");
        }

        // ====================================================================================================
        // 5. Tests - 자리 조회
        // ====================================================================================================

        /// <summary>
        /// 배정 전에는 자리를 돌려주지 않습니다.
        /// 임의의 자리를 대신 주면 병사가 엉뚱한 곳으로 한 걸음 걸었다 돌아옵니다.
        /// </summary>
        [Test]
        public void 배정_전에는_자리를_돌려주지_않는다()
        {
            var members = new SquadMembers(4);
            members.Add(CreateUnit(Vector3.zero));

            Assert.IsFalse(members.TryGetSlot(0, Slots, out _), "배정도 하기 전에 자리를 돌려주었습니다.");
        }

        [Test]
        public void 슬롯이_없으면_자리를_돌려주지_않는다()
        {
            var members = new SquadMembers(4);
            members.Add(CreateUnit(Vector3.zero));

            members.ReassignSlots(new List<Vector3>(), 10f, 0f, Vector3.zero);

            Assert.IsFalse(members.TryGetSlot(0, new List<Vector3>(), out _));
        }
    }
}
