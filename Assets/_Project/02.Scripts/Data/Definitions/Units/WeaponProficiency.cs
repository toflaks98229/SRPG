using SRPG.Common;
using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 한 분대가 <b>무기 계열마다</b> 쌓은 숙련도입니다.
    ///
    /// <b>왜 계열의 축을 <see cref="AttackStyle"/> 로 잡는가</b>
    ///
    /// 숙련은 <b>몸이 익히는 동작</b>입니다. 검을 휘두르는 것과 창을 뻗는 것과 활을 당기는 것은
    /// 서로 다른 동작이고, 하나를 오래 했다고 다른 것이 늘지 않습니다.
    /// <see cref="AttackStyle"/> 이 정확히 그 동작의 구분입니다 —
    /// 훑는 궤적인지, 뻗는 선분인지, 날려 보내는지.
    ///
    /// <see cref="DamageType"/> 를 축으로 삼지 않는 이유가 여기 있습니다.
    /// 저쪽은 <b>무엇이 상대에게 어떻게 먹히는가</b>라 재질의 문제이고, 익히는 동작과 무관합니다.
    /// 검과 철퇴는 성질이 정반대지만 휘두르는 동작은 같아서, 검을 다루던 병사가 철퇴를 들어도 어색하지 않습니다.
    ///
    /// <b>랭크와 무엇이 다른가</b>
    ///
    /// 랭크는 부대 전체가 얼마나 <b>단련되었는가</b>입니다 — 체력·피해·공속·이동이 함께 오릅니다.
    /// 숙련도는 <b>이 무기를 얼마나 잘 다루는가</b>입니다. 명중이 중심입니다.
    /// 둘을 나누면 "베테랑이지만 활은 처음 잡는 부대"가 표현됩니다.
    /// </summary>
    [System.Serializable]
    public struct WeaponProficiency
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>숙련도의 상한입니다. 0이 미숙, 이 값이 완숙입니다.</summary>
        public const int MaxValue = 100;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        /// <summary>베는 동작의 숙련도입니다. 검·도끼·철퇴가 여기 듭니다.</summary>
        [Range(0, MaxValue)]
        public int Swing;

        /// <summary>찌르는 동작의 숙련도입니다. 창과 파이크가 여기 듭니다.</summary>
        [Range(0, MaxValue)]
        public int Thrust;

        /// <summary>쏘는 동작의 숙련도입니다. 활과 석궁이 여기 듭니다.</summary>
        [Range(0, MaxValue)]
        public int Shot;

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 그 동작의 숙련도를 얻습니다.
        /// </summary>
        /// <param name="style">알고 싶은 동작입니다.</param>
        /// <returns>0에서 <see cref="MaxValue"/> 사이의 숙련도입니다.</returns>
        public int Get(AttackStyle style)
        {
            int raw = style switch
            {
                AttackStyle.MeleeThrust => Thrust,
                AttackStyle.Projectile => Shot,
                _ => Swing,
            };

            return Mathf.Clamp(raw, 0, MaxValue);
        }

        /// <summary>
        /// 그 동작의 숙련도를 바꾼 사본을 돌려줍니다.
        ///
        /// 구조체라 원본은 바뀌지 않습니다. 장부가 값을 갱신할 때 씁니다.
        /// </summary>
        /// <param name="style">바꿀 동작입니다.</param>
        /// <param name="value">새 숙련도입니다. 범위를 벗어나면 잘립니다.</param>
        /// <returns>그 동작만 바뀐 사본입니다.</returns>
        public WeaponProficiency With(AttackStyle style, int value)
        {
            var copy = this;
            int clamped = Mathf.Clamp(value, 0, MaxValue);

            switch (style)
            {
                case AttackStyle.MeleeThrust:
                    copy.Thrust = clamped;
                    break;

                case AttackStyle.Projectile:
                    copy.Shot = clamped;
                    break;

                default:
                    copy.Swing = clamped;
                    break;
            }

            return copy;
        }

        /// <summary>
        /// 그 동작의 숙련도를 올린 사본을 돌려줍니다.
        /// </summary>
        /// <param name="style">올릴 동작입니다.</param>
        /// <param name="amount">올릴 양입니다. 음수면 내려갑니다.</param>
        /// <returns>그 동작만 오른 사본입니다.</returns>
        public WeaponProficiency Gain(AttackStyle style, int amount)
        {
            return With(style, Get(style) + amount);
        }

        /// <summary>
        /// 모든 동작을 같은 숙련도로 채웁니다.
        ///
        /// 캠페인이 시작 부대를 꾸릴 때와 검사에서 씁니다.
        /// 실제 부대는 쓰는 무기 쪽만 오르므로 대개 고르지 않습니다.
        /// </summary>
        /// <param name="value">채울 숙련도입니다.</param>
        /// <returns>전부 같은 값으로 채워진 숙련도입니다.</returns>
        public static WeaponProficiency Uniform(int value)
        {
            int clamped = Mathf.Clamp(value, 0, MaxValue);

            return new WeaponProficiency
            {
                Swing = clamped,
                Thrust = clamped,
                Shot = clamped,
            };
        }
    }
}
