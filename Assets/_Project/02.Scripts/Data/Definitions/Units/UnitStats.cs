using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 병사 한 명에게 걸리는 <b>배율</b>입니다. 랭크·숙련도·상성이 여기로 모입니다.
    ///
    /// <b>왜 배율만 모으는가</b>
    ///
    /// 성장 요소는 앞으로 계속 늘어납니다 — 랭크가 오르고, 무기 숙련도가 붙고,
    /// 병과 계열이 생깁니다. 그 각각이 <see cref="UnitDefinition"/> 의 수치를 직접 고치기 시작하면
    /// "이 병사의 피해량이 왜 이 값인가"에 답하려고 여러 곳을 뒤져야 합니다.
    ///
    /// 배율을 한곳에 모아 두면 그 답이 한 줄이 됩니다 — 기본값 곱하기 여기 적힌 것들입니다.
    /// 그리고 새 성장 요소는 <see cref="Combine"/> 로 곱해 넣기만 하면 됩니다.
    ///
    /// <b>1이 기준입니다</b>
    ///
    /// 전부 1이면 정의 에셋의 값이 그대로 쓰입니다.
    /// 더하기가 아니라 곱하기로 모으는 이유는, 요소가 늘어도 "10% 더" 같은 말의 뜻이
    /// 변하지 않기 때문입니다. 더하기로 모으면 요소가 많아질수록 각각의 몫이 희미해집니다.
    /// </summary>
    public readonly struct UnitModifiers
    {
        /// <summary>최대 체력 배율입니다.</summary>
        public readonly float Health;

        /// <summary>피해량 배율입니다.</summary>
        public readonly float Damage;

        /// <summary>
        /// 공격 속도 배율입니다. 1보다 크면 빠릅니다.
        ///
        /// 공격 간격과 동작 시간은 <b>시간</b>이므로 이 값으로 나눕니다.
        /// 배율을 곱해 버리면 "공속이 올랐는데 느려지는" 반대 결과가 납니다.
        /// </summary>
        public readonly float AttackSpeed;

        /// <summary>이동 속도 배율입니다.</summary>
        public readonly float MoveSpeed;

        /// <summary>
        /// 명중 배율입니다. 1보다 크면 정확합니다.
        ///
        /// 조준 산포는 <b>흩어지는 정도</b>이므로 이 값으로 나눕니다.
        /// </summary>
        public readonly float Accuracy;

        /// <summary>넉백 세기 배율입니다.</summary>
        public readonly float Knockback;

        /// <summary>투사체 피해 감소 배율입니다.</summary>
        public readonly float ProjectileResistance;

        /// <summary>아무것도 바꾸지 않는 배율입니다.</summary>
        public static UnitModifiers Identity => new UnitModifiers(1f, 1f, 1f, 1f, 1f, 1f, 1f);

        /// <param name="health">최대 체력 배율입니다.</param>
        /// <param name="damage">피해량 배율입니다.</param>
        /// <param name="attackSpeed">공격 속도 배율입니다.</param>
        /// <param name="moveSpeed">이동 속도 배율입니다.</param>
        /// <param name="accuracy">명중 배율입니다.</param>
        /// <param name="knockback">넉백 세기 배율입니다.</param>
        /// <param name="projectileResistance">투사체 피해 감소 배율입니다.</param>
        public UnitModifiers(
            float health,
            float damage,
            float attackSpeed,
            float moveSpeed,
            float accuracy,
            float knockback,
            float projectileResistance)
        {
            // 0이나 음수가 들어오면 나눗셈이 무한대가 되거나 수치가 뒤집힙니다.
            // 배율을 만드는 쪽이 실수해도 전투가 깨지지 않도록 여기서 한 번 막습니다.
            Health = Mathf.Max(0.01f, health);
            Damage = Mathf.Max(0f, damage);
            AttackSpeed = Mathf.Max(0.01f, attackSpeed);
            MoveSpeed = Mathf.Max(0.01f, moveSpeed);
            Accuracy = Mathf.Max(0.01f, accuracy);
            Knockback = Mathf.Max(0f, knockback);
            ProjectileResistance = Mathf.Max(0f, projectileResistance);
        }

        /// <summary>두 배율을 곱해 하나로 합칩니다.</summary>
        /// <param name="other">함께 걸릴 배율입니다.</param>
        /// <returns>둘을 곱한 배율입니다.</returns>
        public UnitModifiers Combine(in UnitModifiers other)
        {
            return new UnitModifiers(
                Health * other.Health,
                Damage * other.Damage,
                AttackSpeed * other.AttackSpeed,
                MoveSpeed * other.MoveSpeed,
                Accuracy * other.Accuracy,
                Knockback * other.Knockback,
                ProjectileResistance * other.ProjectileResistance);
        }
    }

    /// <summary>
    /// 병사 한 명에게 <b>실제로 적용되는</b> 수치입니다.
    ///
    /// <b>왜 정의 에셋을 직접 읽지 않는가</b>
    ///
    /// 예전에는 여덟 개 파일이 <c>_definition.AttackDamage</c> 처럼 정의를 직접 읽었습니다.
    /// 그 구조에서는 성장 요소를 하나 붙일 때마다 그 여덟 곳을 전부 고쳐야 하고,
    /// 한 군데를 빠뜨리면 "어떤 상황에서만 랭크가 안 먹는" 형태로 조용히 어긋납니다.
    ///
    /// 유효 수치를 한 번 계산해 두면 소비자는 그것만 읽으면 되고,
    /// 성장 요소는 <see cref="UnitModifiers"/> 에만 손대면 됩니다.
    ///
    /// <b>여기 없는 것</b>
    ///
    /// 몸 크기·무기 길이·프리팹처럼 <b>성장으로 변하지 않는</b> 것은 여기 없습니다.
    /// 그런 것은 계속 정의 에셋에서 직접 읽습니다 — 배율이 걸릴 일이 없는데
    /// 여기 넣으면 "이것도 성장하는가" 하는 오해만 만듭니다.
    /// </summary>
    public readonly struct UnitStats
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>최대 체력입니다.</summary>
        public readonly float MaxHealth;

        /// <summary>투사체 피해 감소율입니다. 1이면 완전 무효화입니다.</summary>
        public readonly float ProjectileResistance;

        /// <summary>초당 이동 속도입니다.</summary>
        public readonly float MoveSpeed;

        /// <summary>공격 사거리입니다.</summary>
        public readonly float AttackRange;

        /// <summary>1회 공격 피해량입니다.</summary>
        public readonly float AttackDamage;

        /// <summary>공격 간격(초)입니다.</summary>
        public readonly float AttackInterval;

        /// <summary>교전 대상을 탐색하는 반경입니다.</summary>
        public readonly float EngageRadius;

        /// <summary>한 번의 공격 동작에 걸리는 시간(초)입니다.</summary>
        public readonly float AttackDuration;

        /// <summary>공격 중 제자리에 붙잡히는 시간(초)입니다.</summary>
        public readonly float AttackRootDuration;

        /// <summary>타격이 대상에게 주는 넉백 세기입니다.</summary>
        public readonly float KnockbackForce;

        /// <summary>타격당한 대상이 경직되는 시간(초)입니다.</summary>
        public readonly float KnockbackStagger;

        /// <summary>최하 랭크에서의 조준 산포(도)입니다.</summary>
        public readonly float MaxSpreadDegrees;

        /// <summary>최고 랭크에서의 조준 산포(도)입니다.</summary>
        public readonly float MinSpreadDegrees;

        // ====================================================================================================
        // 2. Constructor
        // ====================================================================================================

        /// <summary>
        /// 정의와 배율로 유효 수치를 계산합니다.
        /// </summary>
        /// <param name="definition">기본 수치를 담은 정의입니다. null이면 전부 0이 됩니다.</param>
        /// <param name="modifiers">걸릴 배율입니다.</param>
        public UnitStats(UnitDefinition definition, in UnitModifiers modifiers)
        {
            if (definition == null)
            {
                this = default;
                return;
            }

            MaxHealth = definition.MaxHealth * modifiers.Health;
            MoveSpeed = definition.MoveSpeed * modifiers.MoveSpeed;
            AttackDamage = definition.AttackDamage * modifiers.Damage;
            KnockbackForce = definition.KnockbackForce * modifiers.Knockback;

            // 시간은 배율로 나눕니다. 공속이 오르면 간격이 짧아져야 합니다.
            AttackInterval = definition.AttackInterval / modifiers.AttackSpeed;
            AttackDuration = definition.AttackDuration / modifiers.AttackSpeed;
            AttackRootDuration = definition.AttackRootDuration / modifiers.AttackSpeed;

            // 산포도 마찬가지입니다. 명중이 오르면 좁아져야 합니다.
            MaxSpreadDegrees = definition.MaxSpreadDegrees / modifiers.Accuracy;
            MinSpreadDegrees = definition.MinSpreadDegrees / modifiers.Accuracy;

            // 감소율은 비율이라 1을 넘으면 피해가 음수가 됩니다. 여기서 한 번 묶습니다.
            ProjectileResistance = Mathf.Clamp01(definition.ProjectileResistance * modifiers.ProjectileResistance);

            // 사거리와 경직은 지금 어떤 배율도 걸지 않습니다.
            // 그래도 여기 두는 이유는 소비자가 정의와 유효 수치 두 곳을 오가지 않게 하기 위해서입니다.
            AttackRange = definition.AttackRange;
            EngageRadius = definition.EngageRadius;
            KnockbackStagger = definition.KnockbackStagger;
        }

        // ====================================================================================================
        // 3. Factory
        // ====================================================================================================

        /// <summary>배율 없이 정의 그대로의 수치를 만듭니다.</summary>
        /// <param name="definition">기본 수치를 담은 정의입니다.</param>
        /// <returns>정의 값이 그대로 담긴 유효 수치입니다.</returns>
        public static UnitStats From(UnitDefinition definition)
        {
            return new UnitStats(definition, UnitModifiers.Identity);
        }
    }
}
