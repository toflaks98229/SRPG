using UnityEngine;

namespace SRPG.Common
{
    /// <summary>
    /// 타격을 받을 수 있는 대상의 공통 인터페이스입니다.
    /// 전투 계산부(Systems)가 구체 액터 타입(Gameplay)을 몰라도 되도록 분리하는 경계입니다.
    /// </summary>
    public interface IDamageable
    {
        /// <summary>생존 여부입니다.</summary>
        bool IsAlive { get; }

        /// <summary>소속 진영입니다.</summary>
        Team Team { get; }

        /// <summary>현재 월드 좌표입니다.</summary>
        Vector3 Position { get; }

        /// <summary>
        /// 타격을 받습니다. 피해·감쇠·넉백·경직이 <b>한 번의 호출로</b> 처리됩니다.
        ///
        /// 호출부는 감쇠를 계산하지 않습니다. 어떤 공격을 어느 방향으로 넣었는지만 서술하고,
        /// 얼마나 막혔는지는 구현체가 자기 방어 수단으로 판단합니다.
        /// </summary>
        /// <param name="hit">타격 정보입니다.</param>
        void ReceiveHit(in DamageInfo hit);
    }
}
