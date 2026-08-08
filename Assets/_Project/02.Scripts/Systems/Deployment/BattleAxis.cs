using UnityEngine;

namespace SRPG.Systems.Deployment
{
    /// <summary>
    /// 두 부대가 <b>마주 보는 방향</b>입니다. 전장의 모든 것이 이 축을 기준으로 배치됩니다.
    ///
    /// <b>왜 한곳에서 뽑는가</b>
    ///
    /// 전개 구역은 이 축의 양 끝에 놓입니다. 강은 이 축을 <b>가로질러야</b>
    /// 양측 사이를 가르는 장애물이 됩니다. 둘이 각자 방향을 뽑으면
    /// 어떤 시드에서는 강이 대치 축과 나란히 흘러 아무것도 가르지 않습니다 —
    /// 예외도 경고도 없이 "이 전장은 왜 강이 의미가 없지"로만 보입니다.
    ///
    /// 축 하나를 공유하면 그 어긋남이 생길 수 없습니다.
    ///
    /// <b>왜 시드마다 다른가</b>
    ///
    /// 대치 축이 늘 같으면 지형의 의미가 사라집니다. 언덕이 언제나 같은 쪽에 서면
    /// 그건 지형이 아니라 규칙입니다. 축을 매번 다시 뽑으면 같은 언덕이
    /// 어떤 판에서는 내 고지가 되고 어떤 판에서는 넘어야 할 벽이 됩니다.
    /// </summary>
    public static class BattleAxis
    {
        // ====================================================================================================
        // 1. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 대치 축입니다. 시드가 같으면 언제나 같은 축이 나옵니다.
        ///
        /// 시드가 0이면 고정 방향을 씁니다 — 전장은 재현 가능해야 하는데
        /// <c>System.Random(0)</c>은 그것을 보장하지 않습니다.
        /// </summary>
        public static Vector3 Resolve(int seed)
        {
            if (seed == 0)
            {
                return Vector3.forward;
            }

            var random = new System.Random(seed);
            float angle = (float)(random.NextDouble() * System.Math.PI * 2.0);

            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        /// <summary>
        /// 대치 축을 <b>가로지르는</b> 방향입니다. 강이 흐르는 방향입니다.
        ///
        /// 이 방향으로 흘러야 강이 두 진영 사이를 가릅니다.
        /// </summary>
        public static Vector3 ResolveCross(int seed)
        {
            Vector3 axis = Resolve(seed);

            // XZ 평면에서의 수직입니다.
            return new Vector3(-axis.z, 0f, axis.x);
        }
    }
}
