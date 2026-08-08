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
