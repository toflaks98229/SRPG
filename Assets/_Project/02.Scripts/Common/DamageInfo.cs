using UnityEngine;

namespace SRPG.Common
{
    /// <summary>
    /// 피해의 종류입니다. 방패 저항의 적용 대상을 가릅니다.
    /// </summary>
    public enum DamageKind
    {
        /// <summary>근접 타격입니다. 방패로 막지 못합니다.</summary>
        Melee = 0,

        /// <summary>투사체입니다. 맞은 방향에 따라 방패가 막을 수 있습니다.</summary>
        Projectile = 1,
    }

    /// <summary>
    /// 한 번의 타격이 피격자에게 전달하는 전부입니다. 피해·방향·넉백·경직이 한 덩어리로 움직입니다.
    ///
    /// <b>이 구조체가 생긴 이유</b>
    ///
    /// 이전에는 피해와 넉백이 별도 호출이었고, 방패 감쇠를 <b>호출부가 이미 적용했는지</b>를
    /// <c>alreadyMitigated</c> 라는 bool 인자로 넘겼습니다. 두 가지가 망가져 있었습니다.
    ///
    ///   · 호출부의 사정이 피호출부 시그니처로 새어 나왔습니다.
    ///     새 피해원(폭발·낙사)을 추가할 때 어느 오버로드를 불러야 하는지가 코드로 표현되지 않았습니다
    ///   · 감쇠 규칙이 두 곳(<c>Unit</c>과 <c>Arrow</c>)에 나뉘어 있어, 한쪽만 고치면 조용히 어긋났습니다
    ///
    /// 지금은 무기가 <b>"무엇을 어느 방향으로 때렸는지"만</b> 말합니다.
    /// 얼마나 막혔는지는 피격자가 단독으로 계산하므로, 감쇠 규칙이 한 곳에만 존재합니다.
    /// </summary>
    public readonly struct DamageInfo
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>감쇠 이전의 피해량입니다.</summary>
        public readonly float Amount;

        /// <summary>피해의 종류입니다.</summary>
        public readonly DamageKind Kind;

        /// <summary>
        /// 타격이 진행한 방향입니다. 정규화되지 않아도 됩니다.
        ///
        /// 넉백은 이 방향의 <b>수평 성분</b>으로 밀어냅니다.
        /// 방패 판정은 수직 성분까지 함께 봅니다. 위에서 내리꽂히는 화살을 가려내야 하기 때문입니다.
        /// </summary>
        public readonly Vector3 Direction;

        /// <summary>넉백 세기입니다. 감쇠가 곱해진 뒤 적용됩니다.</summary>
        public readonly float KnockbackForce;

        /// <summary>경직 시간(초)입니다. 감쇠가 곱해진 뒤 적용됩니다.</summary>
        public readonly float StaggerSeconds;

        /// <summary>
        /// 이 공격이 <b>평지에서</b> 도달할 때의 하강각(도)입니다. 투사체에만 의미가 있습니다.
        ///
        /// 방패의 상방 판정 기준선이 됩니다.
        /// 곡사는 대칭 포물선이므로 평지에서는 발사각이 곧 하강각입니다.
        /// 실제 하강각이 이 값보다 충분히 가파르면 "고지대에서 내리꽂았다"로 봅니다.
        ///
        /// 이 값을 함께 실어 보내는 것이 핵심입니다.
        /// 예전에는 <c>Arrow</c>가 상수 0.78f 로 기준을 박아 두고, 발사각은 별도 에셋 필드였습니다.
        /// 기획자가 발사각을 올리면 <b>오류 하나 없이</b> 방패병이 화살에 무적이 되었습니다.
        /// </summary>
        public readonly float ArcAngleDegrees;

        /// <summary>피해를 준 주체입니다. 로그와 어그로 판단에 사용합니다.</summary>
        public readonly object Source;

        // ====================================================================================================
        // 2. Constructor
        // ====================================================================================================

        private DamageInfo(
            float amount,
            DamageKind kind,
            Vector3 direction,
            float knockbackForce,
            float staggerSeconds,
            float arcAngleDegrees,
            object source)
        {
            Amount = amount;
            Kind = kind;
            Direction = direction;
            KnockbackForce = knockbackForce;
            StaggerSeconds = staggerSeconds;
            ArcAngleDegrees = arcAngleDegrees;
            Source = source;
        }

        // ====================================================================================================
        // 3. Factories
        // ====================================================================================================

        /// <summary>
        /// 근접 타격을 만듭니다. 방패는 근접을 막지 못하므로 감쇠 관련 인자가 없습니다.
        /// </summary>
        /// <param name="amount">피해량입니다.</param>
        /// <param name="direction">밀어낼 방향입니다.</param>
        /// <param name="knockbackForce">넉백 세기입니다.</param>
        /// <param name="staggerSeconds">경직 시간입니다.</param>
        /// <param name="source">공격자입니다.</param>
        public static DamageInfo Melee(
            float amount,
            Vector3 direction,
            float knockbackForce,
            float staggerSeconds,
            object source)
        {
            return new DamageInfo(
                amount,
                DamageKind.Melee,
                direction,
                knockbackForce,
                staggerSeconds,
                arcAngleDegrees: 0f,
                source);
        }

        /// <summary>
        /// 투사체 타격을 만듭니다.
        /// </summary>
        /// <param name="amount">감쇠 이전 피해량입니다.</param>
        /// <param name="direction">투사체가 날아온 방향입니다. 수직 성분을 유지해야 상방 판정이 성립합니다.</param>
        /// <param name="knockbackForce">넉백 세기입니다.</param>
        /// <param name="staggerSeconds">경직 시간입니다.</param>
        /// <param name="arcAngleDegrees">평지 기준 하강각(도)입니다. 곡사의 발사각과 같습니다.</param>
        /// <param name="source">공격자입니다.</param>
        public static DamageInfo Projectile(
            float amount,
            Vector3 direction,
            float knockbackForce,
            float staggerSeconds,
            float arcAngleDegrees,
            object source)
        {
            return new DamageInfo(
                amount,
                DamageKind.Projectile,
                direction,
                knockbackForce,
                staggerSeconds,
                arcAngleDegrees,
                source);
        }
    }
}
