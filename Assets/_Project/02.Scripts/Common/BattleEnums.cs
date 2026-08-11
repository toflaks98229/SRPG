namespace SRPG.Common
{
    /// <summary>
    /// 섬 타일의 종류입니다. 이동 가능 여부와 시각 표현을 함께 결정합니다.
    /// </summary>
    public enum TileType
    {
        /// <summary>바다입니다. 지상 유닛은 통과할 수 없고, 적 상륙정의 경로가 됩니다.</summary>
        Water = 0,

        /// <summary>해변입니다. 육지 중 물과 맞닿은 가장 낮은 지대로, 적의 상륙 지점이 됩니다.</summary>
        Beach = 1,

        /// <summary>평지입니다. 전투가 벌어지는 주 전장입니다.</summary>
        Ground = 2,

        /// <summary>절벽입니다. 높이 차가 커서 통행할 수 없습니다.</summary>
        Cliff = 3,
    }

    /// <summary>
    /// 전투 진영입니다.
    /// </summary>
    public enum Team
    {
        /// <summary>플레이어가 지휘하는 방어측입니다.</summary>
        Player = 0,

        /// <summary>침공하는 적측입니다.</summary>
        Enemy = 1,
    }

    /// <summary>
    /// 유닛 병과입니다. Bad North의 병과 구성을 참조했습니다.
    /// </summary>
    public enum UnitRole
    {
        /// <summary>민병입니다. 업그레이드 이전의 기본 상태입니다.</summary>
        Militia = 0,

        /// <summary>보병입니다. 방패로 투사체를 막으며 아군을 보호합니다.</summary>
        Infantry = 1,

        /// <summary>궁수입니다. 원거리 딜러로, 적이 상륙하기 전에 최대 효율을 냅니다.</summary>
        Archer = 2,

        /// <summary>창병입니다. 이동 중 공격할 수 없으나 자리를 지킬 때 강력합니다.</summary>
        Pike = 3,
    }

    /// <summary>
    /// 공격 방식입니다. 어떤 무기 컴포넌트를 붙일지 결정합니다.
    ///
    /// 셋 다 물리 판정을 씁니다. 로직으로 즉시 피해를 주지 않고,
    /// 무기가 실제로 지나간 자리 또는 화살이 실제로 닿은 지점에서 판정합니다.
    /// </summary>
    public enum AttackStyle
    {
        /// <summary>휘두르는 근접 공격입니다. 검·도끼류로, 칼날이 훑고 지나간 영역을 판정합니다.</summary>
        MeleeSwing = 0,

        /// <summary>찌르는 근접 공격입니다. 창류로, 앞으로 뻗은 자루 선분을 판정합니다.</summary>
        MeleeThrust = 1,

        /// <summary>투사체 공격입니다. 화살을 실제로 날려 충돌 지점에서 판정합니다.</summary>
        Projectile = 2,
    }

    /// <summary>
    /// 타격이 <b>어떤 성질로</b> 들어가는지입니다. 갑옷 상성의 한쪽 축입니다.
    ///
    /// <b>왜 <see cref="AttackStyle"/> 에서 유도하지 않는가</b>
    ///
    /// 둘은 닮았지만 다른 것을 말합니다.
    /// <see cref="AttackStyle"/> 은 <b>어떻게 판정하는가</b>입니다 — 훑는 영역인지, 뻗은 선분인지, 날아가는 화살인지.
    /// 이쪽은 <b>무엇으로 때리는가</b>입니다.
    ///
    /// 유도하면 철퇴를 만들 수 없습니다. 철퇴는 검과 똑같이 휘두르지만(<c>MeleeSwing</c>)
    /// 들어가는 성질은 정반대입니다 — 판금에 검은 미끄러지고 철퇴는 그대로 전달됩니다.
    /// 무기를 늘릴 자리를 처음부터 열어 둡니다.
    /// </summary>
    public enum DamageType
    {
        /// <summary>참격입니다. 베는 무기로, 맨몸과 가죽에 잘 들지만 판금에는 미끄러집니다.</summary>
        Slash = 0,

        /// <summary>자돌입니다. 찌르는 무기와 화살로, 갑옷의 틈을 파고듭니다.</summary>
        Pierce = 1,

        /// <summary>타격입니다. 둔기로, 갑옷을 뚫지 않고 충격을 그대로 전달합니다.</summary>
        Blunt = 2,
    }

    /// <summary>
    /// 몸에 걸친 <b>상시</b> 방어입니다. 갑옷 상성의 다른 한쪽 축입니다.
    ///
    /// <b>방패와 다른 것입니다</b>
    ///
    /// 방패(<c>UnitDefinition.ProjectileResistance</c>)는 <b>상황</b> 방어입니다 —
    /// 투사체만, 그것도 방패를 향한 방향에서 올 때만 막습니다.
    /// 그래서 측면으로 돌아가거나 고지에서 내리쏘면 뚫립니다. 그 판정이 이 게임 전술의 핵심입니다.
    ///
    /// 갑옷은 <b>무방향 상시</b> 방어입니다. 어디서 맞든 같은 규칙이고, 대신
    /// 무엇으로 맞았는지에 따라 결과가 갈립니다.
    ///
    /// 둘을 하나로 합치지 않는 이유가 이것입니다. 합치면 "측면을 잡는다"와
    /// "맞는 무기를 고른다"라는 서로 다른 두 해법이 하나로 뭉개집니다.
    /// </summary>
    public enum ArmorType
    {
        /// <summary>무갑입니다. 걸친 것이 없어 무엇에나 그대로 당합니다.</summary>
        Unarmored = 0,

        /// <summary>경갑입니다. 가죽과 누비로, 베는 것을 덜어 냅니다.</summary>
        Light = 1,

        /// <summary>중갑입니다. 사슬과 판금으로, 날붙이를 흘리지만 충격에는 약합니다.</summary>
        Heavy = 2,
    }

    /// <summary>
    /// 분대의 현재 상태입니다. HUD 표시와 AI 판단의 입력으로 사용됩니다.
    /// </summary>
    public enum SquadState
    {
        /// <summary>명령 없이 현재 위치를 유지하는 상태입니다.</summary>
        Idle = 0,

        /// <summary>지정된 타일로 이동 중인 상태입니다.</summary>
        Moving = 1,

        /// <summary>대열에 도착했고 교전 중인 상태입니다.</summary>
        Fighting = 2,

        /// <summary>지휘관이 사망하여 소멸한 상태입니다. 복구되지 않습니다.</summary>
        Destroyed = 3,
    }
}
