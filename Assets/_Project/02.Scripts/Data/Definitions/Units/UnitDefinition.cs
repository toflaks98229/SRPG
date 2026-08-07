using SRPG.Common;
using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 유닛 한 명의 정적 수치를 담는 정의 에셋입니다.
    /// 밸런스 수치를 코드에서 분리해 실험 가능하게 유지하는 것이 목적입니다.
    /// </summary>
    [CreateAssetMenu(fileName = "UnitDef_", menuName = "SRPG/Definitions/Unit Definition")]
    public sealed class UnitDefinition : ScriptableObject
    {
        // ====================================================================================================
        // 1. Schema
        // ====================================================================================================

        /// <summary>
        /// 현재 코드가 기대하는 스키마 버전입니다. 필드를 새로 추가할 때마다 올립니다.
        ///
        /// 이 장치가 필요한 이유:
        /// 에셋은 필드가 추가되기 <b>전에</b> 직렬화되어 있습니다. 나중에 필드를 추가하면
        /// 그 필드는 YAML에 없으므로 C# 기본값으로 로드됩니다.
        /// 문제는 그 기본값이 병과에 따라 <b>틀린 값</b>이라는 것입니다.
        /// (예: 궁수 에셋의 Style이 기본값 MeleeSwing이 되어 화살을 한 발도 쏘지 않음)
        ///
        /// 겉으로는 아무 오류도 나지 않아 가장 늦게 발견되는 종류의 고장입니다.
        /// 버전을 비교해 새로 생긴 필드만 채우고, 기존에 조정된 값은 건드리지 않습니다.
        /// </summary>
        public const int CurrentSchemaVersion = 3;

        [HideInInspector]
        [Tooltip("에셋이 마지막으로 갱신된 스키마 버전입니다. 에셋 빌더가 관리합니다.")]
        public int SchemaVersion;

        // ====================================================================================================
        // 2. Identity
        // ====================================================================================================

        [Header("정체성")]
        [Tooltip("병과입니다. 전투 규칙 분기의 기준이 됩니다.")]
        public UnitRole Role = UnitRole.Militia;

        [Tooltip("UI에 표시되는 이름입니다.")]
        public string DisplayName = "민병";

        // ====================================================================================================
        // 3. Survivability
        // ====================================================================================================

        [Header("생존")]
        [Min(1f)]
        [Tooltip("최대 체력입니다.")]
        public float MaxHealth = 30f;

        [Min(0f)]
        [Tooltip("투사체 피해 감소율입니다. 보병의 방패를 표현합니다. 1이면 완전 무효화입니다.")]
        [Range(0f, 1f)]
        public float ProjectileResistance = 0f;

        // ====================================================================================================
        // 4. Movement
        // ====================================================================================================

        [Header("이동")]
        [Min(0.1f)]
        [Tooltip("초당 이동 속도입니다.")]
        public float MoveSpeed = 3.5f;

        [Min(0.1f)]
        [Tooltip("유닛 반경입니다. 진형 간격과 분리 조향에 사용합니다.")]
        public float Radius = 0.35f;

        // ====================================================================================================
        // 5. Combat
        // ====================================================================================================

        [Header("전투")]
        [Min(0.1f)]
        [Tooltip("공격 사거리입니다.")]
        public float AttackRange = 1.2f;

        [Min(0f)]
        [Tooltip("1회 공격 피해량입니다.")]
        public float AttackDamage = 8f;

        [Min(0.05f)]
        [Tooltip("공격 간격(초)입니다.")]
        public float AttackInterval = 1.1f;

        [Tooltip("투사체 공격 여부입니다. 방패 저항의 적용 대상을 가릅니다.")]
        public bool IsRangedAttack = false;

        [Tooltip("이동 중에도 공격할 수 있는지 여부입니다. 창병은 false로 두어 자리 선점을 강제합니다.")]
        public bool CanAttackWhileMoving = true;

        [Min(0f)]
        [Tooltip("교전 대상을 탐색하는 반경입니다. 공격 사거리보다 넉넉하게 잡아 접근을 유도합니다.")]
        public float EngageRadius = 2.5f;

        // ====================================================================================================
        // 6. Weapon
        // ====================================================================================================

        [Header("무기")]
        [Tooltip("공격 방식입니다. 붙는 무기 컴포넌트가 결정됩니다.")]
        public AttackStyle Style = AttackStyle.MeleeSwing;

        [Min(0.1f)]
        [Tooltip("무기의 물리적 길이입니다. 타격 판정 형상의 길이가 됩니다.\n창은 이 값이 길어 사거리 우위를 갖습니다.")]
        public float WeaponLength = 0.85f;

        [Min(0.02f)]
        [Tooltip("타격 판정의 두께(반경)입니다. 크면 스치는 공격도 맞습니다.")]
        public float WeaponThickness = 0.18f;

        [Min(0.05f)]
        [Tooltip("한 번의 공격 동작에 걸리는 시간(초)입니다. 이 시간 안에 판정 구간이 들어 있습니다.")]
        public float AttackDuration = 0.42f;

        [Min(0f)]
        [Tooltip("공격 중 제자리에 붙잡히는 시간(초)입니다.\n" +
                 "궁수가 '쏘려고 잠깐 멈추는' 동작을 이 값으로 표현합니다.")]
        public float AttackRootDuration = 0f;

        [Min(0f)]
        [Tooltip("타격이 대상에게 주는 넉백 세기입니다.\n" +
                 "넉백은 이 게임의 핵심 사망 원인입니다. 밀려나 물에 빠지면 즉사합니다.")]
        public float KnockbackForce = 3.2f;

        [Min(0f)]
        [Tooltip("타격당한 대상이 경직되는 시간(초)입니다. 이 동안 이동도 공격도 못 합니다.")]
        public float KnockbackStagger = 0.18f;

        // ====================================================================================================
        // 7. Projectile
        // ====================================================================================================

        [Header("투사체 (Style = Projectile 일 때만)")]
        [Tooltip("발사할 화살 프리팹입니다. 루트에 Arrow 컴포넌트가 있어야 합니다.")]
        public GameObject ProjectilePrefab;

        [Range(10f, 75f)]
        [Tooltip("곡사 발사각(도)입니다. 클수록 화살이 높이 뜨고 오래 납니다.\n" +
                 "속력이 아니라 이 각이 궤도의 모양을 결정합니다. 속력은 거리에 맞춰 자동으로 계산됩니다.")]
        public float ArcLaunchAngleDegrees = 40f;

        [Min(1f)]
        [Tooltip("화살이 낼 수 있는 최대 속력입니다.\n" +
                 "곡사에서는 이 값이 곧 사거리 한계입니다. 필요한 속력이 이를 넘으면 쏘지 않습니다.")]
        public float ProjectileSpeed = 22f;

        [Min(0f)]
        [Tooltip("화살에 적용되는 중력 배율입니다. 곡사에는 중력이 반드시 있어야 하므로 0이면 발사되지 않습니다.")]
        public float ProjectileGravityScale = 1f;

        [Range(0f, 30f)]
        [Tooltip("최하 랭크에서의 조준 산포(도)입니다. 클수록 빗나갑니다.")]
        public float MaxSpreadDegrees = 9f;

        [Range(0f, 30f)]
        [Tooltip("최고 랭크에서의 조준 산포(도)입니다.")]
        public float MinSpreadDegrees = 1.6f;

        [Tooltip("움직이는 대상의 미래 위치를 예측해 조준할지 여부입니다.")]
        public bool UsePredictiveAim = true;

        // ====================================================================================================
        // 8. Presentation
        // ====================================================================================================

        [Header("표현")]
        [Tooltip("이 병과의 유닛 프리팹입니다. 루트에 Unit 컴포넌트가 있어야 합니다.\n" +
                 "비워 두면 부트스트랩이 프리미티브로 임시 몸체를 만듭니다.")]
        public GameObject Prefab;

        [Tooltip("프로토타입 단계에서 사용하는 식별 색상입니다. 프리팹이 없을 때의 폴백과 기즈모에 쓰입니다.")]
        public Color DebugColor = Color.white;

        [Min(0.1f)]
        [Tooltip("프로토타입 단계에서 사용하는 몸체 높이입니다.")]
        public float DebugHeight = 1.0f;

        // ====================================================================================================
        // 9. Factory
        // ====================================================================================================

        /// <summary>
        /// 에셋 없이 코드로 기본 병과 정의를 만듭니다.
        /// 부트스트랩이 정의 에셋 없이도 바로 실행될 수 있도록 하는 프로토타입용 진입점입니다.
        /// </summary>
        public static UnitDefinition CreateDefault(UnitRole role)
        {
            var def = CreateInstance<UnitDefinition>();
            def.Role = role;
            def.SchemaVersion = CurrentSchemaVersion;

            switch (role)
            {
                case UnitRole.Infantry:
                    def.DisplayName = "보병";
                    def.MaxHealth = 46f;
                    def.MoveSpeed = 3.8f;
                    def.AttackRange = 1.3f;
                    def.AttackDamage = 10f;
                    def.AttackInterval = 1.0f;
                    def.ProjectileResistance = 0.85f; // 방패로 화살을 거의 무효화합니다.
                    def.DebugColor = new Color(0.35f, 0.62f, 0.95f);

                    def.Style = AttackStyle.MeleeSwing;
                    def.WeaponLength = 0.9f;
                    def.WeaponThickness = 0.2f;
                    def.AttackDuration = 0.40f;
                    def.KnockbackForce = 3.4f;
                    def.KnockbackStagger = 0.18f;
                    break;

                case UnitRole.Archer:
                    def.DisplayName = "궁수";
                    def.MaxHealth = 26f;
                    def.MoveSpeed = 3.4f;
                    def.AttackRange = 9f;
                    def.AttackDamage = 7f;
                    def.AttackInterval = 1.4f;
                    def.IsRangedAttack = true;
                    def.EngageRadius = 10f;
                    def.DebugColor = new Color(0.45f, 0.85f, 0.45f);

                    def.Style = AttackStyle.Projectile;
                    def.WeaponLength = 0.6f;
                    def.AttackDuration = 0.30f;

                    // 조사에서 확인한 "개별 궁수는 쏘려고 잠깐 멈춘다"를 표현합니다.
                    def.AttackRootDuration = 0.30f;

                    // 화살은 밀어내는 힘이 큽니다. 배 위의 방패병을 물로 떨어뜨리는 것이 궁수의 역할입니다.
                    def.KnockbackForce = 4.6f;
                    def.KnockbackStagger = 0.12f;

                    // 곡사입니다. 40도로 던져 올리면 9m 사거리에서 1초 남짓 날아갑니다.
                    // 그 사이 대상이 3m 넘게 이동하므로 예측 사격이 필수가 됩니다.
                    def.ArcLaunchAngleDegrees = 40f;
                    def.ProjectileSpeed = 22f;
                    def.ProjectileGravityScale = 1f;
                    def.MaxSpreadDegrees = 9f;
                    def.MinSpreadDegrees = 1.6f;
                    def.UsePredictiveAim = true;
                    break;

                case UnitRole.Pike:
                    def.DisplayName = "창병";
                    def.MaxHealth = 34f;
                    def.MoveSpeed = 3.0f;
                    def.AttackRange = 2.2f;
                    def.AttackDamage = 14f;
                    def.AttackInterval = 1.3f;
                    def.CanAttackWhileMoving = false; // Bad North의 창병 규칙입니다.
                    def.EngageRadius = 3.0f;
                    def.DebugColor = new Color(0.95f, 0.78f, 0.3f);

                    def.Style = AttackStyle.MeleeThrust;

                    // 창의 사거리 우위는 스탯이 아니라 자루 길이에서 물리적으로 나옵니다.
                    def.WeaponLength = 2.1f;
                    def.WeaponThickness = 0.14f;
                    def.AttackDuration = 0.50f;
                    def.KnockbackForce = 4.0f;
                    def.KnockbackStagger = 0.24f;
                    break;

                default:
                    def.DisplayName = "민병";
                    def.MaxHealth = 30f;
                    def.MoveSpeed = 3.5f;
                    def.AttackRange = 1.2f;
                    def.AttackDamage = 8f;
                    def.AttackInterval = 1.1f;
                    def.DebugColor = new Color(0.8f, 0.8f, 0.85f);

                    def.Style = AttackStyle.MeleeSwing;
                    def.WeaponLength = 0.8f;
                    def.AttackDuration = 0.44f;
                    def.KnockbackForce = 2.8f;
                    break;
            }

            def.name = $"UnitDef_{role}";
            return def;
        }

        /// <summary>
        /// 적 유닛의 기본 정의를 만듭니다.
        /// </summary>
        public static UnitDefinition CreateEnemyDefault(UnitRole role)
        {
            var def = CreateDefault(role);
            def.DisplayName = $"침략자 {def.DisplayName}";

            // 적은 개별 성능을 약간 낮추고 수로 압박합니다.
            def.MaxHealth *= 0.8f;
            def.AttackDamage *= 0.85f;

            def.DebugColor = role switch
            {
                UnitRole.Archer => new Color(0.85f, 0.45f, 0.35f),
                UnitRole.Infantry => new Color(0.75f, 0.28f, 0.28f),
                UnitRole.Pike => new Color(0.62f, 0.32f, 0.42f),
                _ => new Color(0.7f, 0.35f, 0.3f),
            };

            def.name = $"EnemyDef_{role}";
            return def;
        }

        /// <summary>
        /// 스키마가 낡은 에셋에 새로 생긴 필드를 채웁니다.
        ///
        /// <b>기존에 있던 필드는 절대 건드리지 않습니다.</b> 기획자가 조정한 밸런스를 되돌리면 안 됩니다.
        /// 새로 추가되어 아직 한 번도 저작된 적 없는 필드만, 병과 기본값에서 가져옵니다.
        ///
        /// 이 함수가 없으면 무슨 일이 생기는지가 이 장치의 존재 이유입니다.
        /// 무기 필드를 추가했을 때 궁수 에셋의 Style이 기본값 MeleeSwing으로 로드되어,
        /// 궁수가 활을 들고도 화살을 한 발도 쏘지 않았습니다. 오류는 하나도 나지 않았습니다.
        /// </summary>
        /// <param name="isEnemy">적 정의인지 여부입니다. 기본값 산출 경로가 다릅니다.</param>
        /// <returns>실제로 갱신했으면 true입니다.</returns>
        public bool MigrateToCurrentSchema(bool isEnemy)
        {
            if (SchemaVersion >= CurrentSchemaVersion)
            {
                return false;
            }

            var template = isEnemy ? CreateEnemyDefault(Role) : CreateDefault(Role);

            // 버전 2에서 추가된 무기·투사체 필드입니다.
            if (SchemaVersion < 2)
            {
                Style = template.Style;
                WeaponLength = template.WeaponLength;
                WeaponThickness = template.WeaponThickness;
                AttackDuration = template.AttackDuration;
                AttackRootDuration = template.AttackRootDuration;
                KnockbackForce = template.KnockbackForce;
                KnockbackStagger = template.KnockbackStagger;
                ProjectileSpeed = template.ProjectileSpeed;
                ProjectileGravityScale = template.ProjectileGravityScale;
                MaxSpreadDegrees = template.MaxSpreadDegrees;
                MinSpreadDegrees = template.MinSpreadDegrees;
                UsePredictiveAim = template.UsePredictiveAim;
            }

            // 버전 3에서 곡사로 전환하며 발사각이 추가되었습니다.
            if (SchemaVersion < 3)
            {
                ArcLaunchAngleDegrees = template.ArcLaunchAngleDegrees;
            }

            DestroyImmediate(template);

            SchemaVersion = CurrentSchemaVersion;
            return true;
        }
    }
}
