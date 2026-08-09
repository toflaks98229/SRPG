using System;
using System.Collections.Generic;
using UnityEngine;

namespace SRPG.Gameplay.Units
{
    /// <summary>
    /// 한 병사를 치기로 <b>예약한</b> 적들의 장부입니다.
    ///
    /// <b>왜 필요한가</b>
    ///
    /// 오버킬을 막기 위한 장치입니다. 창병 여럿이 최전방의 한 명만 동시에 찌르면
    /// 그가 죽은 자리로 뒤따라오던 적들이 그대로 통과합니다.
    /// 방어선의 목적은 잘 죽이는 것이 아니라 <b>새지 않는 것</b>입니다.
    /// 예약 인원에 상한을 두면 남는 창병이 자연히 다음 적을 겨눕니다.
    ///
    /// <b>정원은 체력으로 정해집니다.</b>
    ///
    /// 한 방에 죽는 적에게는 한 명만 붙고, 여러 대를 맞아야 하는 거구에게는 여럿이 함께 붙습니다.
    /// <see cref="ComputeCapacity"/> 한 줄이 "덩치 큰 적에게는 유동적으로 여럿이 달라붙는다"를
    /// 별도 규칙 없이 만들어 냅니다.
    /// </summary>
    public sealed class AttackerSlots
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>지금 이 병사를 치기로 예약한 적들입니다.</summary>
        private readonly HashSet<Unit> _committed = new HashSet<Unit>();

        /// <summary>
        /// 사라진 예약자를 걸러 내는 조건입니다.
        ///
        /// 구체 타입(<see cref="Unit"/>)으로 비교해야 파괴된 오브젝트를 유니티가 null로 봐 줍니다.
        /// 제네릭 안에서 비교하면 그 연산자를 타지 않아 죽은 예약이 자리를 영원히 붙잡습니다.
        /// </summary>
        private static readonly Predicate<Unit> IsGone = attacker => attacker == null || !attacker.IsAlive;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>지금 예약된 인원입니다.</summary>
        public int Count => _committed.Count;

        // ====================================================================================================
        // 3. Public Methods - Capacity
        // ====================================================================================================

        /// <summary>
        /// 동시에 붙을 수 있는 인원입니다. 지금 남은 체력을 한 방 피해로 나눈 값입니다.
        /// </summary>
        /// <param name="remainingHealth">대상의 남은 체력입니다.</param>
        /// <param name="damagePerHit">공격 한 번의 피해량입니다. 0에 가까우면 상한을 그대로 씁니다.</param>
        /// <param name="maxAttackers">정원의 상한입니다. 계산 결과가 아무리 커도 이를 넘지 않습니다.</param>
        /// <returns>동시에 붙을 수 있는 인원입니다. 최소 1명입니다.</returns>
        public static int ComputeCapacity(float remainingHealth, float damagePerHit, int maxAttackers)
        {
            if (damagePerHit <= 0.01f)
            {
                return Mathf.Max(1, maxAttackers);
            }

            int needed = Mathf.CeilToInt(remainingHealth / damagePerHit);

            return Mathf.Clamp(needed, 1, Mathf.Max(1, maxAttackers));
        }

        // ====================================================================================================
        // 4. Public Methods - Claim
        // ====================================================================================================

        /// <summary>
        /// 자리를 하나 예약합니다. 이미 자리가 찼으면 거절합니다.
        /// </summary>
        /// <param name="attacker">예약하려는 쪽입니다. 이미 잡아 둔 자리는 그대로 유지됩니다.</param>
        /// <param name="remainingHealth">대상의 남은 체력입니다. 정원 계산의 분자입니다.</param>
        /// <param name="damagePerHit">공격 한 번의 피해량입니다. 정원 계산의 분모입니다.</param>
        /// <param name="maxAttackers">정원의 상한입니다.</param>
        /// <returns>자리를 잡았으면 true입니다.</returns>
        public bool TryCommit(Unit attacker, float remainingHealth, float damagePerHit, int maxAttackers)
        {
            if (attacker == null)
            {
                return false;
            }

            // 이미 잡아 둔 자리는 그대로 유지합니다.
            if (_committed.Contains(attacker))
            {
                return true;
            }

            Prune();

            if (_committed.Count >= ComputeCapacity(remainingHealth, damagePerHit, maxAttackers))
            {
                return false;
            }

            _committed.Add(attacker);
            return true;
        }

        /// <summary>
        /// 예약을 <b>잡지 않고</b> 자리가 남았는지만 봅니다. 표적을 고를 때 씁니다.
        /// </summary>
        /// <param name="attacker">자리를 알아보는 쪽입니다. 이미 예약했으면 언제나 true입니다.</param>
        /// <param name="remainingHealth">대상의 남은 체력입니다.</param>
        /// <param name="damagePerHit">공격 한 번의 피해량입니다.</param>
        /// <param name="maxAttackers">정원의 상한입니다.</param>
        /// <returns>자리가 남아 있으면 true입니다.</returns>
        public bool HasRoom(Unit attacker, float remainingHealth, float damagePerHit, int maxAttackers)
        {
            if (attacker != null && _committed.Contains(attacker))
            {
                return true;
            }

            Prune();

            return _committed.Count < ComputeCapacity(remainingHealth, damagePerHit, maxAttackers);
        }

        /// <summary>예약을 놓습니다. 표적을 바꾸거나 죽을 때 호출합니다.</summary>
        /// <param name="attacker">예약을 놓을 쪽입니다. 예약이 없어도 안전합니다.</param>
        public void Release(Unit attacker)
        {
            if (attacker != null)
            {
                _committed.Remove(attacker);
            }
        }

        /// <summary>죽었거나 사라진 예약자를 정리합니다.</summary>
        public void Prune()
        {
            _committed.RemoveWhere(IsGone);
        }

        /// <summary>장부를 통째로 비웁니다.</summary>
        public void Clear()
        {
            _committed.Clear();
        }
    }
}
