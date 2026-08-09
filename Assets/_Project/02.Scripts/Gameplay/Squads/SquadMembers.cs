using System.Collections.Generic;
using SRPG.Gameplay.Units;
using SRPG.Systems.Formation;
using UnityEngine;

namespace SRPG.Gameplay.Squads
{
    /// <summary>
    /// 분대에 속한 병사들과, 그들이 맡은 진형 자리의 <b>장부</b>입니다.
    ///
    /// <b>왜 떼어 놓는가</b>
    ///
    /// 아군 분대와 적 분대가 같은 일을 각자 하고 있었습니다 —
    /// 사망자 걸러내기, 병사와 슬롯의 짝 다시 짜기, 배정된 자리 꺼내기.
    /// 세 곳 모두 거의 같은 코드였고, 그래서 <b>한쪽만 고치는 사고</b>가 나기 좋았습니다.
    /// 실제로 이미 어긋나 있었습니다 — 재배정 주기를 아군은 튜닝 에셋에서 읽는데
    /// 적은 코드 상수를 보고 있었습니다. 기획자가 값을 바꿔도 절반만 바뀌는 상태였습니다.
    ///
    /// 이 프로젝트가 병사와 지휘관의 프리팹을 하나로 합칠 때 든 이유가 그대로 적용됩니다.
    /// 두 벌이면 반드시 한쪽만 고치게 됩니다.
    ///
    /// <b>지휘관을 인자로 받지 않습니다</b>
    ///
    /// 예전에는 아군 분대만 지휘관 인덱스를 찾아 넘기고 적 분대는 넘기지 않아,
    /// 두 호출이 서로 다른 모양이었습니다. 여기서 <see cref="Unit.IsCommander"/>를
    /// 직접 찾으면 호출부가 하나가 됩니다 — 지휘관이 없는 적 분대는 자연히 아무도 고정되지 않습니다.
    /// <b>"지휘관이 있으면 중심에 세운다"가 진영별 분기가 아니라 하나의 규칙이 됩니다.</b>
    ///
    /// MonoBehaviour가 아니므로 EditMode에서 직접 검증됩니다.
    /// </summary>
    public sealed class SquadMembers
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        private readonly List<Unit> _units;

        /// <summary>병사별로 맡은 슬롯 인덱스입니다. <see cref="_units"/>와 같은 순서입니다.</summary>
        private readonly List<int> _slotOf;

        /// <summary>배정에 넘길 위치를 담는 재사용 버퍼입니다.</summary>
        private readonly List<Vector3> _positionBuffer;

        private float _assignmentTimer;

        // ====================================================================================================
        // 2. Constructor
        // ====================================================================================================

        /// <param name="capacity">예상 인원입니다. 목록이 커지며 재할당되는 것을 줄입니다.</param>
        public SquadMembers(int capacity = 12)
        {
            int size = Mathf.Max(1, capacity);

            _units = new List<Unit>(size);
            _slotOf = new List<int>(size);
            _positionBuffer = new List<Vector3>(size);
        }

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>소속 병사 목록입니다.</summary>
        public IReadOnlyList<Unit> Units => _units;

        /// <summary>살아 있는 병사 수입니다. <see cref="PruneDead"/> 이후의 값입니다.</summary>
        public int Count => _units.Count;

        /// <summary>지정 순번의 병사입니다. 이미 사라졌으면 null일 수 있습니다.</summary>
        public Unit this[int index] => _units[index];

        // ====================================================================================================
        // 4. Public Methods - Roster
        // ====================================================================================================

        /// <summary>병사를 명부에 넣습니다.</summary>
        public void Add(Unit unit)
        {
            if (unit != null)
            {
                _units.Add(unit);
            }
        }

        /// <summary>
        /// 쓰러졌거나 사라진 병사를 걸러냅니다.
        ///
        /// 인원이 줄면 슬롯 배정이 어긋나지만, <see cref="ReassignSlots"/>가
        /// 인원 변화를 감지해 주기를 기다리지 않고 곧바로 다시 짭니다.
        /// </summary>
        public void PruneDead()
        {
            for (int i = _units.Count - 1; i >= 0; i--)
            {
                var unit = _units[i];

                if (unit == null || !unit.IsAlive)
                {
                    _units.RemoveAt(i);
                }
            }
        }

        /// <summary>명부를 비웁니다. 분대가 소멸하거나 해산할 때 부릅니다.</summary>
        public void Clear()
        {
            _units.Clear();
            _slotOf.Clear();
            _positionBuffer.Clear();
            _assignmentTimer = 0f;
        }

        // ====================================================================================================
        // 5. Public Methods - Slots
        // ====================================================================================================

        /// <summary>
        /// 병사와 진형 슬롯의 짝을 다시 짭니다.
        ///
        /// <b>매 프레임 짜지 않습니다.</b> 미세한 위치 변화로 슬롯이 서로 뒤바뀌며
        /// 병사들이 자리를 두고 떨기 때문입니다. 다만 <b>인원이 바뀌었을 때는
        /// 주기를 기다리지 않습니다</b> — 그러지 않으면 누가 쓰러진 뒤 대열이
        /// 한동안 비뚤어진 채로 남습니다.
        /// </summary>
        /// <param name="slots">진형 슬롯 위치입니다.</param>
        /// <param name="interval">다시 짜는 주기(초)입니다.</param>
        /// <param name="deltaTime">지난 시간입니다.</param>
        /// <param name="fallbackPosition">이미 사라진 병사 자리에 대신 넣을 위치입니다. 보통 앵커입니다.</param>
        public void ReassignSlots(
            IReadOnlyList<Vector3> slots, float interval, float deltaTime, Vector3 fallbackPosition)
        {
            _assignmentTimer -= deltaTime;

            bool countChanged = _slotOf.Count != _units.Count;

            if (!countChanged && _assignmentTimer > 0f)
            {
                return;
            }

            _assignmentTimer = interval;

            _positionBuffer.Clear();

            // 지휘관은 진형 중심(슬롯 0번)에 고정합니다.
            // 방향 없는 진형에서 사방으로부터 가장 안쪽이 곧 가장 안전한 자리입니다.
            int commanderIndex = -1;

            for (int i = 0; i < _units.Count; i++)
            {
                var unit = _units[i];
                _positionBuffer.Add(unit != null ? unit.Position : fallbackPosition);

                if (unit != null && unit.IsCommander)
                {
                    commanderIndex = i;
                }
            }

            SlotAssigner.AssignNearest(_positionBuffer, slots, _slotOf, commanderIndex);
        }

        /// <summary>
        /// 이 병사가 맡은 자리를 꺼냅니다.
        ///
        /// 아직 배정되지 않았으면 <b>돌려주지 않습니다.</b> 임의의 자리를 대신 주면
        /// 병사가 엉뚱한 곳으로 한 걸음 걸었다 돌아옵니다.
        /// </summary>
        /// <returns>배정된 자리가 있으면 true입니다.</returns>
        public bool TryGetSlot(int memberIndex, IReadOnlyList<Vector3> slots, out Vector3 slot)
        {
            slot = Vector3.zero;

            if (slots == null || slots.Count == 0 || memberIndex < 0 || memberIndex >= _slotOf.Count)
            {
                return false;
            }

            slot = slots[Mathf.Clamp(_slotOf[memberIndex], 0, slots.Count - 1)];
            return true;
        }
    }
}
