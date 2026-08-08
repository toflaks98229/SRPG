using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 재컴파일 없이 조정해야 하는 전투 수치를 모은 설정입니다.
    ///
    /// <b>무엇이 여기 들어오는가</b>
    ///
    /// 기준은 하나입니다. <b>기획자가 게임의 감각이나 밸런스를 바꾸려고 만질 값인가.</b>
    /// 그렇다면 데이터고, 아니라면 코드 상수로 남습니다.
    /// (예: 슬로우모션 배율은 여기, "슬롯 도착 판정 거리 0.45"는 구현 세부라 코드에 남습니다)
    ///
    /// 이 에셋이 생기기 전에는 이 값들이 전부 <c>private const</c> 였습니다.
    /// 기술 문서가 "슬로우모션 0.15가 너무 느리지 않은가"를 검증 항목으로 올려 두었는데
    /// 정작 그 값을 바꾸려면 코드를 고치고 컴파일을 기다려야 했습니다.
    /// 검증하겠다고 적어 둔 것과 구조가 어긋나 있었습니다.
    ///
    /// 비워 두어도 됩니다. 부트스트랩이 <see cref="CreateDefault"/>로 대체합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "BattleTuning_", menuName = "SRPG/Configs/Battle Tuning")]
    public sealed class BattleTuning : ScriptableObject
    {
        // ====================================================================================================
        // 1. Defaults
        // ====================================================================================================

        /// <summary>
        /// <b>기본값은 전부 여기 있습니다.</b>
        ///
        /// 아래 필드 초기값과, 튜닝 에셋이 연결되지 않았을 때 소비자가 쓰는 폴백이
        /// <b>같은 상수</b>를 봐야 합니다. 예전에는 무기마다 자기 폴백 상수를 따로 들고 있어서
        /// 한쪽만 고치면 "에셋을 연결했을 때와 안 했을 때 감각이 다른" 상태가 조용히 생겼습니다.
        /// 그런 어긋남은 재현 조건이 "에셋 연결 여부"라 원인을 찾기가 특히 어렵습니다.
        /// </summary>
        public const float DefaultSteepBlockMarginDegrees = 11f;

        /// <summary>검병이 대열을 풀고 나갈 수 있는 기본 격자 거리(칸)입니다.</summary>
        public const float DefaultMeleeBreakFormationTiles = 2f;

        /// <summary>검병이 벨 때 앞으로 몸을 던지는 기본 세기입니다.</summary>
        public const float DefaultMeleeLungeForce = 3.6f;

        /// <summary>이보다 가까우면 도약하지 않는 기본 거리입니다.</summary>
        public const float DefaultMeleeLungeMinDistance = 0.7f;

        /// <summary>자루 길이 대비 안쪽 사각지대의 기본 비율입니다.</summary>
        public const float DefaultPikeInnerDeadZoneRatio = 0.45f;

        /// <summary>창을 겨눌 때 더하는 기본 준비 동작 보정(초)입니다.</summary>
        public const float DefaultPikeAimLeadSeconds = 0.15f;

        /// <summary>접근 예측 시간의 기본 상한(초)입니다.</summary>
        public const float DefaultPikeMaxAimLeadSeconds = 1.2f;

        /// <summary>한 번 겨눈 적을 놓지 않는 기본 시간(초)입니다.</summary>
        public const float DefaultPikeTargetLockSeconds = 1.1f;

        /// <summary>창 회전 스프링의 기본 진동수입니다.</summary>
        public const float DefaultPikeTurnSpringFrequency = 1.4f;

        /// <summary>창 회전 스프링의 기본 감쇠입니다.</summary>
        public const float DefaultPikeTurnSpringDamping = 0.5f;

        /// <summary>품 안을 내준 뒤 창을 다시 내리기까지의 기본 시간(초)입니다.</summary>
        public const float DefaultPikeBreakRecoverySeconds = 1.4f;

        /// <summary>분대를 고르는 기본 클릭 반경입니다.</summary>
        public const float DefaultSquadPickRadius = 2.2f;

        // ====================================================================================================
        // 2. Time
        // ====================================================================================================

        [Header("시간")]
        [Range(0.02f, 1f)]
        [Tooltip("명령 입력 중의 타임스케일입니다.\n" +
                 "0에 가까울수록 턴제에 가까워지고, 1에 가까울수록 실시간 압박이 커집니다.")]
        public float SlowMotionScale = 0.15f;

        [Min(0.1f)]
        [Tooltip("타임스케일이 목표값으로 수렴하는 속도입니다. 급격한 전환의 이질감을 줄입니다.")]
        public float SlowMotionTransitionSpeed = 8f;

        // ====================================================================================================
        // 3. Squad
        // ====================================================================================================

        [Header("분대")]
        [Min(0.2f)]
        [Tooltip("진형 슬롯 간 간격입니다.\n" +
                 "좁히면 분대가 뭉쳐 궁수의 광역 효율이 오르고, 넓히면 화살을 덜 맞습니다.")]
        public float FormationSpacing = 0.95f;

        [Range(0.5f, 1f)]
        [Tooltip("앵커가 병사 이동 속도의 몇 배로 전진할지입니다.\n" +
                 "1에 가까우면 병사들이 앵커를 놓치고 대열이 길게 늘어집니다.")]
        public float AnchorSpeedFactor = 0.92f;

        [Min(0.05f)]
        [Tooltip("병사와 진형 슬롯의 짝을 다시 짜는 주기(초)입니다.\n" +
                 "짧으면 미세한 위치 변화로 슬롯이 서로 뒤바뀌며 병사들이 자리를 두고 떱니다.\n" +
                 "길면 인원이 줄어든 뒤 대열이 한동안 비뚤어진 채로 남습니다.")]
        public float SquadAssignmentInterval = 0.35f;

        [Min(0.05f)]
        [Tooltip("전열이 향할 방향을 다시 살피는 주기(초)입니다.\n" +
                 "짧으면 위협이 조금만 움직여도 전원이 고개를 돌립니다.")]
        public float SquadFacingScanInterval = 0.4f;

        [Min(0.2f)]
        [Tooltip("클릭 지점이 분대 중심에서 이 거리 안이면 분대를 고른 것으로 봅니다.\n" +
                 "<b>조작감에 직결됩니다.</b> 좁으면 정확히 찍어야 하고,\n" +
                 "넓으면 분대 옆 지형으로 보내려던 명령이 선택으로 먹힙니다.")]
        public float SquadPickRadius = DefaultSquadPickRadius;

        // ====================================================================================================
        // 4. Unit
        // ====================================================================================================

        [Header("난전")]
        [Min(0f)]
        [Tooltip("검병이 벨 때 <b>몸을 앞으로 던지는</b> 세기입니다.\n" +
                 "0이면 제자리에서 허공에 칼을 휘두르는 것처럼 보입니다.\n" +
                 "크면 적을 지나쳐 뚫고 나갑니다.")]
        public float MeleeLungeForce = DefaultMeleeLungeForce;

        [Min(0f)]
        [Tooltip("이 거리보다 가까우면 도약하지 않습니다.\n" +
                 "밀고 들어갈 공간이 없는데 힘을 주면 서로 통과하거나 어색하게 미끄러집니다.")]
        public float MeleeLungeMinDistance = DefaultMeleeLungeMinDistance;

        [Range(0f, 4f)]
        [Tooltip("아군끼리 서로 밀어내는 세기입니다.\n" +
                 "0이면 병사들이 겹쳐 서고, 크면 진형이 부풀어 슬롯을 벗어납니다.")]
        public float AllySeparationWeight = 1.6f;

        [Range(0f, 2f)]
        [Tooltip("<b>적</b>과 서로 밀어내는 세기입니다. 아군끼리보다 약해야 합니다.\n" +
                 "0이면 난전에서 몸이 그대로 겹쳐 어느 쪽이 어디 있는지 알 수 없습니다.\n" +
                 "크면 서로 다가가지 못해 영영 칼이 닿지 않습니다.")]
        public float EnemySeparationWeight = 0.6f;

        [Range(1f, 30f)]
        [Tooltip("분리 힘이 목표값을 따라가는 속도입니다. <b>작을수록 부드럽습니다.</b>\n" +
                 "값을 그대로 더하면 이웃이 반경에 드나들 때마다 속도가 계단처럼 튀어\n" +
                 "옆 사람이 다가왔을 뿐인데 홱 밀려나는 것처럼 보입니다.\n" +
                 "너무 낮추면 반응이 늦어 병사들이 잠깐 겹쳤다 떨어집니다.")]
        public float SeparationSmoothing = 8f;

        [Header("유닛")]
        [Min(0f)]
        [Tooltip("근접병이 대열을 풀고 적에게 다가갈 수 있는 최대 <b>격자 거리(칸)</b>입니다.\n" +
                 "크면 병사들이 적을 따라 흩어지고, 작으면 자리를 지키느라 눈앞의 적을 놓칩니다.\n" +
                 "창병과 궁수는 이 값과 무관하게 자리를 지킵니다.")]
        public float MeleeBreakFormationTiles = DefaultMeleeBreakFormationTiles;

        [Min(0f)]
        [Tooltip("<b>이 속도 이상으로 밀려나는 중이면 물 위로도 밀려납니다 — 곧 익사합니다.</b>\n" +
                 "이 게임의 주요 사망 수단이라 물가의 위험도가 이 값 하나로 정해집니다.\n" +
                 "낮추면 스치듯 맞아도 물에 빠지고, 높이면 넉백이 밀치기 연출에 가까워집니다.")]
        public float DrownKnockbackThreshold = 1.2f;

        // ====================================================================================================
        // 5. Enemy
        // ====================================================================================================

        [Header("적")]
        [Min(0.2f)]
        [Tooltip("적 분대가 목표를 다시 고르는 주기(초)입니다.\n" +
                 "짧으면 상황 변화에 민감해지지만, 너무 짧으면 목표를 계속 바꾸며 갈팡질팡합니다.")]
        public float EnemyReplanInterval = 2.5f;

        [Min(0f)]
        [Tooltip("적 분대가 목표를 바꾸려면 새 목표가 현재 목표보다 이만큼은 나아야 합니다.\n" +
                 "0이면 점수가 조금만 뒤집혀도 방향을 틀어 우왕좌왕합니다.")]
        public float EnemyGoalSwitchMargin = 0.08f;

        // ====================================================================================================
        // 5-0. Enemy Chaos
        // ====================================================================================================

        [Header("적 진형의 흐트러짐")]
        [Range(1f, 3f)]
        [Tooltip("적 진형이 플레이어보다 몇 배 넓게 서는지입니다.\n" +
                 "1이면 침략자가 방어자만큼 정연해져 <b>질서 대 혼돈</b>의 대비가 사라집니다.")]
        public float EnemyFormationLooseness = 1.5f;

        [Min(0f)]
        [Tooltip("자리를 잡은 적이 슬롯에서 벗어나는 거리입니다.\n" +
                 "병사마다 고정된 값이라 떨지 않고, 대열이 대열로 읽히지 않게 합니다.")]
        public float EnemyFormationJitter = 0.5f;

        [Min(0f)]
        [Tooltip("돌격 중인 적이 앵커에서 벗어나는 거리입니다.\n" +
                 "0이면 전원이 한 점으로 몰려 서로 밀어내느라 뭉개집니다.")]
        public float EnemyChargeScatter = 1.2f;

        // ====================================================================================================
        // 5-1. Enemy AI Weights
        // ====================================================================================================

        [Header("적 AI 판단 가중치")]
        [Range(0f, 1f)]
        [Tooltip("가까운 목표를 얼마나 선호할지입니다. 높이면 눈앞의 것만 봅니다.")]
        public float AiProximityWeight = 0.7f;

        [Range(0f, 1f)]
        [Tooltip("고립된 부대를 얼마나 노릴지입니다.\n" +
                 "<b>이 값이 각개격파 성향을 결정합니다.</b>\n" +
                 "목표 자리의 아군 영향력이 낮다는 것은 도와줄 부대가 멀다는 뜻입니다.\n" +
                 "높이면 떨어져 나온 분대를 집요하게 물고, 0이면 가까운 쪽으로만 갑니다.")]
        public float AiIsolationWeight = 0.85f;

        [Range(0f, 1f)]
        [Tooltip("초크포인트를 얼마나 피할지입니다. 높이면 좁은 길을 우회합니다.")]
        public float AiOpenGroundWeight = 0.45f;

        // ====================================================================================================
        // 5-2. Influence Map
        // ====================================================================================================

        [Header("영향력 맵")]
        [Min(0.02f)]
        [Tooltip("위협 영향력 맵을 다시 만드는 주기(초)입니다.\n" +
                 "갱신 비용이 격자 크기에 비례하므로 매 프레임 만들지 않습니다.")]
        public float InfluenceRefreshInterval = 0.4f;

        [Range(0.1f, 0.95f)]
        [Tooltip("위협이 한 칸 번질 때 남는 비율입니다.\n" +
                 "낮으면 위협이 유닛 주변에만 맺히고, 높으면 넓게 퍼져 섬 전체가 위험해 보입니다.")]
        public float InfluenceDecayPerTile = 0.72f;

        // ====================================================================================================
        // 6. Ship
        // ====================================================================================================

        [Header("전개")]
        [Range(1, 12)]
        [Tooltip("한 진영이 전장에 <b>동시에</b> 세울 수 있는 분대 수입니다.\n" +
                 "서열이 이보다 길면 나머지는 지원군으로 대기하다가 자리가 나면 올라옵니다.\n" +
                 "낮추면 좁은 전장에서 긴 소모전이 되고, 높이면 한 번에 부딪히는 회전이 됩니다.")]
        public int FieldSquadCap = 4;

        [Min(0f)]
        [Tooltip("지원군이 올라오는 최소 간격(초)입니다.\n" +
                 "0이면 앞 부대가 쓰러진 그 자리에 다음 부대가 곧바로 나타납니다.\n" +
                 "짧게라도 두어야 전선이 한 번 숨을 쉬고 재배치할 여유가 생깁니다.")]
        public float ReinforcementInterval = 6f;

        // ====================================================================================================
        // 6-1. Camera
        // ====================================================================================================

        [Header("카메라")]
        [Min(1f)]
        [Tooltip("WASD로 시점을 옮기는 속도입니다.\n" +
                 "느리면 섬을 훑어보기 답답하고, 빠르면 어디를 보고 있는지 놓칩니다.")]
        public float CameraPanSpeed = 18f;

        [Min(0f)]
        [Tooltip("카메라가 육지 바깥으로 나갈 수 있는 여유 거리입니다.\n" +
                 "0이면 해안선에 딱 붙어 상륙정이 화면에 안 들어옵니다.")]
        public float CameraBoundsMargin = 8f;

        // ====================================================================================================
        // 7. Combat
        // ====================================================================================================

        [Header("전투")]
        [Range(0f, 45f)]
        [Tooltip("방패의 상방 판정 여유각(도)입니다.\n" +
                 "평지 곡사보다 이만큼 더 가파르게 떨어져야 '위에서 내리꽂았다'로 봅니다.\n" +
                 "0에 가까우면 평지 사격까지 방패에 막히고, 크면 고지대 이점이 사라집니다.")]
        public float ShieldSteepBlockMarginDegrees = DefaultSteepBlockMarginDegrees;

        // ====================================================================================================
        // 7-1. Pike
        // ====================================================================================================

        [Header("창병")]
        [Range(0f, 0.9f)]
        [Tooltip("자루 길이 대비 <b>안쪽 사각지대</b> 비율입니다.\n" +
                 "이 안쪽으로 파고든 적은 찌르기에 맞지 않습니다.\n" +
                 "0이면 품 안의 적도 찔러 창병에게 약점이 없어지고, 크면 파고들기만 하면 무력해집니다.")]
        public float PikeInnerDeadZoneRatio = DefaultPikeInnerDeadZoneRatio;

        [Min(0f)]
        [Tooltip("창을 겨눌 때 더하는 준비 동작 보정(초)입니다.\n" +
                 "찌르기는 즉시 나가지 않으므로 그만큼 더 앞을 봐야 합니다.")]
        public float PikeAimLeadSeconds = DefaultPikeAimLeadSeconds;

        [Min(0f)]
        [Tooltip("접근 예측 시간의 상한(초)입니다.\n" +
                 "거의 멈춰 선 적을 향해 도착 시간을 그대로 쓰면 예측 지점이 지평선까지 날아갑니다.")]
        public float PikeMaxAimLeadSeconds = DefaultPikeMaxAimLeadSeconds;

        [Min(0f)]
        [Tooltip("한 번 겨눈 적을 놓지 않는 시간(초)입니다.\n" +
                 "0이면 더 가까운 적이 나타날 때마다 창끝이 돌아가 방어선에 구멍이 납니다.")]
        public float PikeTargetLockSeconds = DefaultPikeTargetLockSeconds;

        [Min(0.1f)]
        [Tooltip("창 회전 스프링의 진동수입니다. 클수록 빠르게 따라붙습니다.")]
        public float PikeTurnSpringFrequency = DefaultPikeTurnSpringFrequency;

        [Range(0.05f, 2f)]
        [Tooltip("창 회전 스프링의 감쇠입니다.\n" +
                 "1 미만이면 목표를 지나쳤다 돌아와 무게감이 생기고, 1을 넘으면 굼떠집니다.")]
        public float PikeTurnSpringDamping = DefaultPikeTurnSpringDamping;

        [Min(0f)]
        [Tooltip("품 안을 내준 뒤 창을 다시 내리기까지의 시간(초)입니다.\n" +
                 "이 동안 창병은 공격하지 못하고 물러납니다. 길수록 진형 붕괴가 치명적입니다.")]
        public float PikeBreakRecoverySeconds = DefaultPikeBreakRecoverySeconds;

        // ====================================================================================================
        // 7-2. Attack Queue
        // ====================================================================================================

        [Header("공격 대기열")]
        [Range(1, 8)]
        [Tooltip("한 적에게 동시에 붙을 수 있는 인원의 <b>상한</b>입니다.\n" +
                 "실제 정원은 적의 남은 체력을 한 방 피해로 나눈 값이라, 거구에게는 여럿이 붙습니다.\n" +
                 "1로 두면 큰 적을 영영 못 잡고, 크게 두면 오버킬이 그대로 납니다.")]
        public int MaxSimultaneousAttackers = 4;

        // ====================================================================================================
        // 7-3. Shield
        // ====================================================================================================

        [Header("방패")]
        [Range(0f, 1f)]
        [Tooltip("<b>막아 냈을 때도 전달되는 충격량</b>의 비율입니다.\n" +
                 "1이면 피해는 막아도 넉백은 그대로 받습니다. 큰 적의 일격이 방패벽을 밀어 틈을 벌립니다.\n" +
                 "0이면 막은 공격은 밀리지도 않아 방패병이 요지부동이 됩니다.")]
        public float BlockedKnockbackRetention = 1f;

        [Range(0f, 1f)]
        [Tooltip("<b>이동 중</b> 방패가 유지하는 저항 비율입니다.\n" +
                 "뛰는 동안 방패가 흔들려 빈틈이 생기는 것을 표현합니다.\n" +
                 "1이면 뛰면서도 완벽히 막아 '멈춰서 막는다'는 판단이 사라집니다.")]
        public float ShieldMovingEffectiveness = 0.35f;

        [Min(0f)]
        [Tooltip("방패병이 몸을 돌릴 위협을 살피는 반경입니다.\n" +
                 "교전 반경보다 넓어야 합니다. 궁수는 사거리 밖에서 쏘기 때문입니다.")]
        public float ShieldThreatRadius = 14f;

        // ====================================================================================================
        // 8. Factory
        // ====================================================================================================

        /// <summary>
        /// 에셋 없이 코드로 기본 설정을 만듭니다. 부트스트랩의 폴백 경로입니다.
        /// </summary>
        public static BattleTuning CreateDefault()
        {
            var tuning = CreateInstance<BattleTuning>();
            tuning.name = "BattleTuning_Default";
            return tuning;
        }
    }
}
