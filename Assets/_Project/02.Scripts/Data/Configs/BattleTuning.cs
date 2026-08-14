using System;
using SRPG.Common;
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
    ///
    /// <b>왜 영역별로 중첩하는가</b>
    ///
    /// 한때 쉰여덟 개가 한 층에 평평하게 놓여 있었습니다. 두 가지가 나빴습니다.
    ///
    ///   · <b>인스펙터</b> — 창병 사거리 하나를 만지려고 AI 가중치와 갑옷 상성을 지나쳐야 했습니다.
    ///     머리말로 나눠 두었지만 접히지 않으니 화면에는 늘 전부가 펼쳐져 있었습니다.
    ///   · <b>코드</b> — 방패 계산에 필요한 것은 넷인데 받는 것은 쉰여덟이었습니다.
    ///     받을 수 있게 두면 언젠가 만지고, 그러면 "왜 이 계산이 카메라 속도를 보는가"가 생깁니다.
    ///
    /// 중첩하면 둘 다 풀립니다. 인스펙터는 접히고, 계산은 자기에게 필요한 묶음만 받을 수 있습니다.
    ///
    /// 비워 두어도 됩니다. 부트스트랩이 <see cref="CreateDefault"/>로 대체합니다.
    /// </summary>
    [CreateAssetMenu(fileName = "BattleTuning_", menuName = "SRPG/Configs/Battle Tuning")]
    public sealed class BattleTuning : ScriptableObject
    {
        // ====================================================================================================
        // 1. Schema
        // ====================================================================================================

        /// <summary>
        /// 지금 코드가 기대하는 스키마 판입니다.
        ///
        /// <c>GrassProfile</c> 과 <c>UnitDefinition</c> 이 같은 이유로 같은 장치를 들고 있습니다.
        /// </summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>
        /// 이 에셋이 마지막으로 맞춰진 스키마 판입니다. 배선 도구가 관리합니다.
        ///
        /// <b>초기값이 0인 것이 핵심입니다.</b>
        /// 유니티는 YAML 에 없는 키를 만나면 필드 초기값을 그대로 둡니다.
        /// 여기에 <see cref="CurrentSchemaVersion"/> 을 적어 두면 이 필드가 생기기 <b>전에</b> 구워진 에셋이
        /// "이미 최신"이라고 대답해 이관이 영영 돌지 않습니다.
        /// 0은 "판을 모른다"는 뜻이고, 그것이 옛 에셋의 정확한 상태입니다.
        /// </summary>
        [HideInInspector]
        [Tooltip("이 에셋이 마지막으로 갱신된 스키마 버전입니다. 배선 도구가 관리합니다.")]
        public int SchemaVersion;

        // ====================================================================================================
        // 2. Defaults
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

        /// <summary>적이 전열에 닿았다고 보는 기본 거리(칸)입니다.</summary>
        public const float DefaultSquadContactRangeTiles = 1.5f;

        /// <summary>전열이 적의 진로를 내다보는 기본 상한(초)입니다.</summary>
        public const float DefaultSquadFacingLeadSeconds = 1.5f;

        // ====================================================================================================
        // 3. Groups
        // ====================================================================================================

        [SerializeField]
        private TimeTuning _time = new TimeTuning();

        [SerializeField]
        private SquadTuning _squad = new SquadTuning();

        [SerializeField]
        private UnitTuning _unit = new UnitTuning();

        [SerializeField]
        private CommanderTuning _commander = new CommanderTuning();

        [SerializeField]
        private EnemyTuning _enemy = new EnemyTuning();

        [SerializeField]
        private AiTuning _ai = new AiTuning();

        [SerializeField]
        private DeploymentTuning _deployment = new DeploymentTuning();

        [SerializeField]
        private CameraTuning _camera = new CameraTuning();

        [SerializeField]
        private ShieldTuning _shield = new ShieldTuning();

        [SerializeField]
        private PikeTuning _pike = new PikeTuning();

        [SerializeField]
        private GrowthTuning _growth = new GrowthTuning();

        [SerializeField]
        private ArmorMatchupTuning _matchup = new ArmorMatchupTuning();

        // ====================================================================================================
        // 4. Properties
        // ====================================================================================================

        /// <summary>시간 흐름입니다.</summary>
        public TimeTuning Time => _time;

        /// <summary>분대의 대열과 조작입니다.</summary>
        public SquadTuning Squad => _squad;

        /// <summary>병사 한 명의 몸놀림입니다.</summary>
        public UnitTuning Unit => _unit;

        /// <summary>적 분대가 서고 움직이는 방식입니다.</summary>
        /// <summary>지휘관이 쓰러질 뻔했을 때의 규칙입니다.</summary>
        public CommanderTuning Commander => _commander;

        public EnemyTuning Enemy => _enemy;

        /// <summary>적의 판단입니다.</summary>
        public AiTuning Ai => _ai;

        /// <summary>전장에 부대를 세우는 규칙입니다.</summary>
        public DeploymentTuning Deployment => _deployment;

        /// <summary>시점 조작입니다.</summary>
        public CameraTuning Camera => _camera;

        /// <summary>방패입니다. 상황 방어의 전부가 여기 있습니다.</summary>
        public ShieldTuning Shield => _shield;

        /// <summary>창입니다. 사거리가 길고 안쪽이 비는 무기의 규칙입니다.</summary>
        public PikeTuning Pike => _pike;

        /// <summary>랭크와 숙련도가 얹는 성장입니다.</summary>
        public GrowthTuning Growth => _growth;

        /// <summary>피해 성질과 갑옷의 상성표입니다.</summary>
        public ArmorMatchupTuning Matchup => _matchup;

        // ====================================================================================================
        // 5. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 랭크에 해당하는 성장 배율을 구합니다.
        ///
        /// <b>명중은 여기 없습니다.</b>
        /// 궁수의 조준 산포는 이미 랭크로 보간되고 있습니다
        /// (<c>BallisticSolver.GetSpreadForRank</c> 가 정의의 최저·최고 산포 사이를 랭크로 잇습니다).
        /// 여기서 명중 배율까지 걸면 같은 성장이 두 번 적용되어,
        /// 랭크를 조금만 올려도 궁수가 과녁처럼 맞히게 됩니다.
        ///
        /// 근접 무기에는 산포 개념이 없으므로 명중이 걸릴 자리도 없습니다.
        /// </summary>
        /// <param name="rank">병사의 랭크입니다. 허용 범위를 벗어나면 잘립니다.</param>
        /// <returns>그 랭크의 성장 배율입니다. 최저 랭크에서는 아무것도 바꾸지 않습니다.</returns>
        public UnitModifiers EvaluateRank(int rank)
        {
            // 최저 랭크가 기준선입니다. 1랭크 병사는 정의 에셋의 수치 그대로 싸웁니다.
            // 그러지 않으면 "정의에 적힌 값"과 "실제로 나오는 값"이 처음부터 어긋나
            // 밸런스를 잡을 때 기준으로 삼을 수치가 없어집니다.
            int steps = Mathf.Clamp(rank, CombatConstants.MinRank, CombatConstants.MaxRank) - CombatConstants.MinRank;

            if (steps <= 0)
            {
                return UnitModifiers.Identity;
            }

            return new UnitModifiers(
                1f + _growth.RankHealthGain * steps,
                1f + _growth.RankDamageGain * steps,
                1f + _growth.RankAttackSpeedGain * steps,
                1f + _growth.RankMoveSpeedGain * steps,
                1f,
                1f,
                1f);
        }

        /// <summary>
        /// 숙련도에 해당하는 성장 배율을 구합니다.
        ///
        /// <b>랭크와 겹치지 않게 나눠 두었습니다.</b>
        /// 랭크는 체력·피해·공속·이동을, 숙련도는 명중을 중심으로 맡습니다.
        /// 겹치는 두 축(피해·공속)은 숙련도 쪽 몫을 작게 두어, 어느 쪽을 올렸는지가 체감으로 갈리게 합니다.
        ///
        /// <b>궁수의 산포는 두 축이 함께 작용합니다.</b>
        /// 랭크는 정의에 적힌 최저·최고 산포 <b>사이를 이동</b>시키고
        /// (<c>BallisticSolver.GetSpreadForRank</c>), 숙련도는 그 <b>두 끝점을 함께 좁힙니다</b>.
        /// 출처가 다른 두 성장이라 겹쳐도 같은 것이 두 번 걸리는 것은 아닙니다.
        /// </summary>
        /// <param name="proficiency">0에서 <see cref="WeaponProficiency.MaxValue"/> 사이의 숙련도입니다.</param>
        /// <returns>그 숙련도의 성장 배율입니다. 0이면 아무것도 바꾸지 않습니다.</returns>
        public UnitModifiers EvaluateProficiency(int proficiency)
        {
            float t = Mathf.Clamp01(proficiency / (float)WeaponProficiency.MaxValue);

            if (t <= 0f)
            {
                return UnitModifiers.Identity;
            }

            return new UnitModifiers(
                1f,
                1f + _growth.ProficiencyDamageGain * t,
                1f + _growth.ProficiencyAttackSpeedGain * t,
                1f,
                1f + _growth.ProficiencyAccuracyGain * t,
                1f,
                1f);
        }

        /// <summary>
        /// 이 타격이 저 갑옷에 얼마나 잘 드는지 구합니다.
        ///
        /// 표는 <see cref="ArmorMatchupTuning"/> 이 들고 있고 여기서는 그리로 넘깁니다.
        /// 부르는 쪽이 대부분 튜닝 전체를 들고 있어, 상성 하나 물으려고 묶음을 꺼내게 하지 않습니다.
        /// </summary>
        /// <param name="damage">때리는 성질입니다.</param>
        /// <param name="armor">맞는 쪽이 걸친 방어입니다.</param>
        /// <returns>피해량에 곱할 배율입니다. 1이면 상성이 없습니다.</returns>
        public float GetArmorEffectiveness(DamageType damage, ArmorType armor)
        {
            return _matchup.GetEffectiveness(damage, armor);
        }

        // ====================================================================================================
        // 6. Factory
        // ====================================================================================================

        /// <summary>
        /// 에셋 없이 코드로 기본 설정을 만듭니다. 부트스트랩의 폴백 경로입니다.
        /// </summary>
        /// <returns>필드 초기값이 그대로 담긴 튜닝 인스턴스입니다. 에셋으로 저장되지 않습니다.</returns>
        public static BattleTuning CreateDefault()
        {
            var tuning = CreateInstance<BattleTuning>();
            tuning.name = "BattleTuning_Default";
            tuning.SchemaVersion = CurrentSchemaVersion;

            return tuning;
        }

        /// <summary>
        /// 인스펙터에서 새로 만들거나 초기화할 때 유니티가 부릅니다.
        /// 갓 만든 에셋이 옛 판으로 표시되지 않게 판 번호를 적어 둡니다.
        /// </summary>
        private void Reset()
        {
            SchemaVersion = CurrentSchemaVersion;
        }

        // ====================================================================================================
        // 7. Migration
        // ====================================================================================================

        /// <summary>
        /// 에셋을 지금 스키마에 맞춥니다. 이미 최신이면 아무것도 하지 않습니다.
        ///
        /// <b>1판에서 2판으로 넘어올 때는 옮길 것이 없었습니다</b>
        ///
        /// 평평하던 시절의 에셋을 열어 보니 담긴 값이 <b>전부 코드 기본값과 같았고</b>,
        /// 그중 여섯 개는 이미 사라진 필드를 가리키고 있었습니다
        /// (<c>EnemyAggroRadius</c> · <c>ShipSpeed</c> 따위). 손으로 맞춰 둔 값이 하나도 없었으므로
        /// 옛 키를 읽어 옮기는 코드를 두어도 지킬 것이 없습니다.
        ///
        /// 그래서 이 판에서는 판 번호만 올립니다. 이 함수가 존재하는 이유는 <b>다음 번</b>입니다 —
        /// 그때는 기획이 손으로 맞춘 값이 들어 있을 것이고, 그것을 옮길 자리가 미리 있어야 합니다.
        /// </summary>
        /// <returns>실제로 무언가 바꿨으면 true입니다.</returns>
        public bool MigrateToCurrentSchema()
        {
            if (SchemaVersion >= CurrentSchemaVersion)
            {
                return false;
            }

            SchemaVersion = CurrentSchemaVersion;
            return true;
        }

        // ====================================================================================================
        // 8. Nested Types
        // ====================================================================================================

        /// <summary>
        /// 시간이 흐르는 방식입니다.
        /// </summary>
        [Serializable]
        public sealed class TimeTuning
        {
            [Range(0.02f, 1f)]
            [Tooltip("명령 입력 중의 타임스케일입니다.\n" +
                     "0에 가까울수록 턴제에 가까워지고, 1에 가까울수록 실시간 압박이 커집니다.")]
            public float SlowMotionScale = 0.15f;

            [Min(0.1f)]
            [Tooltip("타임스케일이 목표값으로 수렴하는 속도입니다. 급격한 전환의 이질감을 줄입니다.")]
            public float SlowMotionTransitionSpeed = 8f;

            /// <summary>지휘관을 잃은 순간 시간을 붙잡는 길이입니다. 스케일되지 않은 시간입니다.</summary>
            [Range(0f, 1.5f)]
            [Tooltip("지휘관을 잃은 순간 시간을 붙잡는 길이(초)입니다. 0이면 걸지 않습니다.")]
            public float LossHitStopSeconds = 0.35f;

            /// <summary>그 동안의 배율입니다. 0에 가까울수록 완전 정지에 가깝습니다.</summary>
            [Range(0.01f, 1f)]
            [Tooltip("지휘관을 잃은 순간의 타임스케일입니다.")]
            public float LossHitStopScale = 0.05f;

            /// <summary>
            /// 지휘관이 부상으로 버텼을 때 붙잡는 길이입니다.
            ///
            /// <b>잃은 순간보다 짧아야 합니다.</b> 부상은 한 전투에 여러 번 일어날 수 있어서,
            /// 같은 길이로 두면 판이 계속 끊기고 <b>정작 잃는 순간이 특별해 보이지 않습니다.</b>
            /// </summary>
            [Range(0f, 1f)]
            [Tooltip("지휘관이 부상으로 버텼을 때 시간을 붙잡는 길이(초)입니다.")]
            public float WoundHitStopSeconds = 0.12f;

            /// <summary>그 동안의 배율입니다.</summary>
            [Range(0.01f, 1f)]
            [Tooltip("지휘관 부상 순간의 타임스케일입니다.")]
            public float WoundHitStopScale = 0.25f;
        }

        /// <summary>
        /// 분대가 대열을 이루고 명령을 받는 방식입니다.
        ///
        /// <b>양측이 같은 값을 씁니다.</b> 예전에는 적만 코드 상수를 보고 있어서
        /// 이 묶음을 바꿔도 절반만 바뀌었습니다.
        /// </summary>
        [Serializable]
        public sealed class SquadTuning
        {
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
            public float AssignmentInterval = 0.35f;

            [Min(0.05f)]
            [Tooltip("전열이 향할 방향을 다시 살피는 주기(초)입니다.\n" +
                     "짧으면 위협이 조금만 움직여도 전원이 고개를 돌립니다.")]
            public float FacingScanInterval = 0.4f;

            [Min(0.2f)]
            [Tooltip("클릭 지점이 분대 중심에서 이 거리 안이면 분대를 고른 것으로 봅니다.\n" +
                     "<b>조작감에 직결됩니다.</b> 좁으면 정확히 찍어야 하고,\n" +
                     "넓으면 분대 옆 지형으로 보내려던 명령이 선택으로 먹힙니다.")]
            public float PickRadius = DefaultSquadPickRadius;

            [Min(0.2f)]
            [Tooltip("적이 전열에 <b>닿았다</b>고 보는 거리(칸)입니다.\n" +
                     "전열은 적이 지금 선 자리가 아니라 이 거리에 <b>도착할 자리</b>를 미리 봅니다.\n" +
                     "짧으면 코앞에 와서야 몸을 틀고, 길면 아직 먼 적을 향해 미리 돌아섭니다.")]
            public float ContactRangeTiles = DefaultSquadContactRangeTiles;

            [Min(0f)]
            [Tooltip("전열이 적의 진로를 내다보는 시간의 상한(초)입니다.\n" +
                     "길면 옆으로 흘러가는 먼 적의 진로에 전열이 통째로 끌려다닙니다.\n" +
                     "창병의 조준 상한(Pike.MaxAimLeadSeconds)과 같은 성격의 값입니다.")]
            public float FacingLeadSeconds = DefaultSquadFacingLeadSeconds;
        }

        /// <summary>
        /// 병사 한 명이 부딪히고 밀리는 방식입니다. 난전의 감각이 여기서 나옵니다.
        /// </summary>
        [Serializable]
        public sealed class UnitTuning
        {
            [Min(0f)]
            [Tooltip("검병이 벨 때 <b>몸을 앞으로 던지는</b> 세기입니다.\n" +
                     "0이면 제자리에서 허공에 칼을 휘두르는 것처럼 보입니다.\n" +
                     "크면 적을 지나쳐 뚫고 나갑니다.")]
            public float LungeForce = DefaultMeleeLungeForce;

            [Min(0f)]
            [Tooltip("이 거리보다 가까우면 도약하지 않습니다.\n" +
                     "밀고 들어갈 공간이 없는데 힘을 주면 서로 통과하거나 어색하게 미끄러집니다.")]
            public float LungeMinDistance = DefaultMeleeLungeMinDistance;

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

            [Min(0f)]
            [Tooltip("근접병이 대열을 풀고 적에게 다가갈 수 있는 최대 <b>격자 거리(칸)</b>입니다.\n" +
                     "크면 병사들이 적을 따라 흩어지고, 작으면 자리를 지키느라 눈앞의 적을 놓칩니다.\n" +
                     "창병과 궁수는 이 값과 무관하게 자리를 지킵니다.")]
            public float BreakFormationTiles = DefaultMeleeBreakFormationTiles;

            [Min(0f)]
            [Tooltip("<b>이 속도 이상으로 밀려나는 중이면 물 위로도 밀려납니다 — 곧 익사합니다.</b>\n" +
                     "이 게임의 주요 사망 수단이라 물가의 위험도가 이 값 하나로 정해집니다.\n" +
                     "낮추면 스치듯 맞아도 물에 빠지고, 높이면 넉백이 밀치기 연출에 가까워집니다.")]
            public float DrownKnockbackThreshold = 1.2f;

            [Range(1, 8)]
            [Tooltip("한 적에게 동시에 붙을 수 있는 인원의 <b>상한</b>입니다.\n" +
                     "실제 정원은 적의 남은 체력을 한 방 피해로 나눈 값이라, 거구에게는 여럿이 붙습니다.\n" +
                     "1로 두면 큰 적을 영영 못 잡고, 크게 두면 오버킬이 그대로 납니다.")]
            public int MaxSimultaneousAttackers = 4;
        }

        /// <summary>
        /// 적 분대가 서고 움직이는 방식입니다.
        ///
        /// <b>흐트러짐이 여기 있는 이유</b>
        ///
        /// 침략자는 방어자보다 성기게 서야 합니다. 그 대비가 사라지면
        /// 양측이 같은 규율로 싸우는 것처럼 보이고, 이 게임이 말하려는 <b>질서 대 혼돈</b>이 흐려집니다.
        /// </summary>
        /// <summary>
        /// 지휘관이 치명상을 입었을 때 무슨 일이 일어나는지입니다.
        ///
        /// <b>왜 즉사가 아닌가</b>
        ///
        /// 지휘관의 죽음은 이 게임에서 <b>유일하게 되돌릴 수 없는 손실</b>입니다.
        /// 그런데 병사 하나와 같은 방식으로, 즉 날아온 화살 하나에 즉시 쓰러지면
        /// 그 손실이 <b>플레이어의 판단이 아니라 사고</b>가 됩니다.
        /// 잃을 때 "내가 무리했다"가 아니라 "운이 나빴다"가 되면 로그라이트의 긴장이 서지 않습니다.
        ///
        /// 그래서 둘로 나눕니다 — <b>호위가 남아 있으면 쓰러지지 않고</b>, 그마저 무너진 뒤에야
        /// 확률이 개입합니다. 분대가 온전한데 지휘관만 잃는 일은 일어나지 않습니다.
        /// </summary>
        [Serializable]
        public sealed class CommanderTuning
        {
            /// <summary>
            /// 지휘관이 위험해지기까지 <b>호위가 얼마나 쓰러져야</b> 하는지입니다. 배치 인원에 대한 비율입니다.
            ///
            /// 이것이 안전장치입니다. 이만큼 잃기 전에는 지휘관이 <b>어떤 타격에도 쓰러지지 않습니다</b>.
            /// 분대가 멀쩡한데 지휘관만 사라지는 일이 없어야, 잃는 순간이
            /// "저 분대는 이미 무너지고 있었다"로 읽힙니다.
            /// </summary>
            [Range(0f, 1f)]
            [Tooltip("지휘관이 위험해지기까지 호위가 쓰러져야 하는 비율입니다. 1이면 마지막 한 명까지 지켜 줍니다.")]
            public float FallenEscortRatio = 0.6f;

            /// <summary>
            /// 호위가 무너진 뒤, 치명상 한 번이 실제로 <b>죽음</b>이 될 확률입니다. 나머지는 부상입니다.
            ///
            /// 낮출수록 지휘관이 오래 버팁니다. 다만 <see cref="MaxWounds"/> 가 상한을 쥐고 있어
            /// 아무리 낮춰도 무한히 살아남지는 않습니다.
            /// </summary>
            [Range(0f, 1f)]
            [Tooltip("호위가 무너진 뒤 치명상이 죽음이 될 확률입니다. 나머지는 부상으로 넘어갑니다.")]
            public float FallChance = 0.35f;

            /// <summary>
            /// 견딜 수 있는 부상의 수입니다. 이만큼 부상한 뒤의 치명상은 <b>판정 없이</b> 죽음입니다.
            ///
            /// <b>상한이 없으면 확률이 무의미해집니다.</b> 운이 좋은 지휘관은 영영 죽지 않고,
            /// 그러면 영구 손실이라는 규칙 자체가 사라집니다.
            /// </summary>
            [Range(0, 5)]
            [Tooltip("견딜 수 있는 부상 수입니다. 이를 넘기면 다음 치명상은 판정 없이 죽음입니다.")]
            public int MaxWounds = 2;

            /// <summary>
            /// 부상에서 일어날 때 되찾는 체력입니다. 최대 체력에 대한 비율입니다.
            ///
            /// <b>부상마다 줄어듭니다.</b> 첫 부상은 이 비율만큼, 두 번째는 그 절반입니다.
            /// 그래야 살아남을수록 위태로워지고, 물러날지 버틸지를 다시 판단하게 됩니다.
            /// </summary>
            [Range(0.05f, 1f)]
            [Tooltip("부상에서 일어날 때 되찾는 체력 비율입니다. 부상이 쌓일수록 줄어듭니다.")]
            public float WoundRecoveryRatio = 0.35f;

            /// <summary>부상 직후 움직이지 못하는 시간입니다. 쓰러졌다 일어나는 틈입니다.</summary>
            [Range(0f, 3f)]
            [Tooltip("부상 직후의 경직 시간입니다.")]
            public float WoundStaggerSeconds = 0.8f;
        }

        [Serializable]
        public sealed class EnemyTuning
        {
            [Min(0.2f)]
            [Tooltip("적 분대가 목표를 다시 고르는 주기(초)입니다.\n" +
                     "짧으면 상황 변화에 민감해지지만, 너무 짧으면 목표를 계속 바꾸며 갈팡질팡합니다.")]
            public float ReplanInterval = 2.5f;

            [Min(0f)]
            [Tooltip("적 분대가 목표를 바꾸려면 새 목표가 현재 목표보다 이만큼은 나아야 합니다.\n" +
                     "0이면 점수가 조금만 뒤집혀도 방향을 틀어 우왕좌왕합니다.")]
            public float GoalSwitchMargin = 0.08f;

            [Range(1f, 3f)]
            [Tooltip("적 진형이 플레이어보다 몇 배 넓게 서는지입니다.\n" +
                     "1이면 침략자가 방어자만큼 정연해져 <b>질서 대 혼돈</b>의 대비가 사라집니다.")]
            public float FormationLooseness = 1.5f;

            [Min(0f)]
            [Tooltip("자리를 잡은 적이 슬롯에서 벗어나는 거리입니다.\n" +
                     "병사마다 고정된 값이라 떨지 않고, 대열이 대열로 읽히지 않게 합니다.")]
            public float FormationJitter = 0.5f;

            [Min(0f)]
            [Tooltip("돌격 중인 적이 앵커에서 벗어나는 거리입니다.\n" +
                     "0이면 전원이 한 점으로 몰려 서로 밀어내느라 뭉개집니다.")]
            public float ChargeScatter = 1.2f;
        }

        /// <summary>
        /// 적이 무엇을 노릴지 정하는 저울입니다.
        ///
        /// 영향력 맵 설정이 함께 있는 것은 그것이 <b>판단의 입력</b>이기 때문입니다 —
        /// 고립 판정은 목표 자리의 아군 영향력을 읽어서 나옵니다.
        /// </summary>
        [Serializable]
        public sealed class AiTuning
        {
            [Range(0f, 1f)]
            [Tooltip("가까운 목표를 얼마나 선호할지입니다. 높이면 눈앞의 것만 봅니다.")]
            public float ProximityWeight = 0.7f;

            [Range(0f, 1f)]
            [Tooltip("고립된 부대를 얼마나 노릴지입니다.\n" +
                     "<b>이 값이 각개격파 성향을 결정합니다.</b>\n" +
                     "목표 자리의 아군 영향력이 낮다는 것은 도와줄 부대가 멀다는 뜻입니다.\n" +
                     "높이면 떨어져 나온 분대를 집요하게 물고, 0이면 가까운 쪽으로만 갑니다.")]
            public float IsolationWeight = 0.85f;

            [Range(0f, 1f)]
            [Tooltip("초크포인트를 얼마나 피할지입니다. 높이면 좁은 길을 우회합니다.")]
            public float OpenGroundWeight = 0.45f;

            [Min(0.02f)]
            [Tooltip("위협 영향력 맵을 다시 만드는 주기(초)입니다.\n" +
                     "갱신 비용이 격자 크기에 비례하므로 매 프레임 만들지 않습니다.")]
            public float InfluenceRefreshInterval = 0.4f;

            [Range(0.1f, 0.95f)]
            [Tooltip("위협이 한 칸 번질 때 남는 비율입니다.\n" +
                     "낮으면 위협이 유닛 주변에만 맺히고, 높으면 넓게 퍼져 섬 전체가 위험해 보입니다.")]
            public float InfluenceDecayPerTile = 0.72f;
        }

        /// <summary>
        /// 부대를 전장에 세우는 규칙입니다. <b>양측에 똑같이 적용됩니다.</b>
        /// </summary>
        [Serializable]
        public sealed class DeploymentTuning
        {
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
        }

        /// <summary>
        /// 시점 조작입니다.
        /// </summary>
        [Serializable]
        public sealed class CameraTuning
        {
            [Min(1f)]
            [Tooltip("WASD로 시점을 옮기는 속도입니다.\n" +
                     "느리면 섬을 훑어보기 답답하고, 빠르면 어디를 보고 있는지 놓칩니다.")]
            public float PanSpeed = 18f;

            [Min(0f)]
            [Tooltip("카메라가 육지 바깥으로 나갈 수 있는 여유 거리입니다.\n" +
                     "0이면 해안선에 딱 붙어 상륙정이 화면에 안 들어옵니다.")]
            public float BoundsMargin = 8f;
        }

        /// <summary>
        /// 방패입니다. <b>상황 방어</b>의 전부가 여기 있습니다.
        ///
        /// 갑옷(<see cref="ArmorMatchupTuning"/>)과 축이 다릅니다 —
        /// 이쪽은 어디서 오는지를 보고, 저쪽은 무엇으로 때렸는지를 봅니다.
        /// 그 둘을 가르는 것이 이 게임 전술의 뼈대라, 묶음도 따로 둡니다.
        /// </summary>
        [Serializable]
        public sealed class ShieldTuning
        {
            [Range(0f, 45f)]
            [Tooltip("방패의 상방 판정 여유각(도)입니다.\n" +
                     "평지 곡사보다 이만큼 더 가파르게 떨어져야 '위에서 내리꽂았다'로 봅니다.\n" +
                     "0에 가까우면 평지 사격까지 방패에 막히고, 크면 고지대 이점이 사라집니다.")]
            public float SteepBlockMarginDegrees = DefaultSteepBlockMarginDegrees;

            [Range(0f, 1f)]
            [Tooltip("<b>막아 냈을 때도 전달되는 충격량</b>의 비율입니다.\n" +
                     "1이면 피해는 막아도 넉백은 그대로 받습니다. 큰 적의 일격이 방패벽을 밀어 틈을 벌립니다.\n" +
                     "0이면 막은 공격은 밀리지도 않아 방패병이 요지부동이 됩니다.")]
            public float BlockedKnockbackRetention = 1f;

            [Range(0f, 1f)]
            [Tooltip("<b>이동 중</b> 방패가 유지하는 저항 비율입니다.\n" +
                     "뛰는 동안 방패가 흔들려 빈틈이 생기는 것을 표현합니다.\n" +
                     "1이면 뛰면서도 완벽히 막아 '멈춰서 막는다'는 판단이 사라집니다.")]
            public float MovingEffectiveness = 0.35f;

            [Min(0f)]
            [Tooltip("방패병이 몸을 돌릴 위협을 살피는 반경입니다.\n" +
                     "교전 반경보다 넓어야 합니다. 궁수는 사거리 밖에서 쏘기 때문입니다.")]
            public float ThreatRadius = 14f;
        }

        /// <summary>
        /// 창입니다. 사거리가 길고 안쪽이 비는 무기라 규칙이 따로 필요합니다.
        /// </summary>
        [Serializable]
        public sealed class PikeTuning
        {
            [Range(0f, 0.9f)]
            [Tooltip("자루 길이 대비 <b>안쪽 사각지대</b> 비율입니다.\n" +
                     "이 안쪽으로 파고든 적은 찌르기에 맞지 않습니다.\n" +
                     "0이면 품 안의 적도 찔러 창병에게 약점이 없어지고, 크면 파고들기만 하면 무력해집니다.")]
            public float InnerDeadZoneRatio = DefaultPikeInnerDeadZoneRatio;

            [Min(0f)]
            [Tooltip("창을 겨눌 때 더하는 준비 동작 보정(초)입니다.\n" +
                     "찌르기는 즉시 나가지 않으므로 그만큼 더 앞을 봐야 합니다.")]
            public float AimLeadSeconds = DefaultPikeAimLeadSeconds;

            [Min(0f)]
            [Tooltip("접근 예측 시간의 상한(초)입니다.\n" +
                     "거의 멈춰 선 적을 향해 도착 시간을 그대로 쓰면 예측 지점이 지평선까지 날아갑니다.")]
            public float MaxAimLeadSeconds = DefaultPikeMaxAimLeadSeconds;

            [Min(0f)]
            [Tooltip("한 번 겨눈 적을 놓지 않는 시간(초)입니다.\n" +
                     "0이면 더 가까운 적이 나타날 때마다 창끝이 돌아가 방어선에 구멍이 납니다.")]
            public float TargetLockSeconds = DefaultPikeTargetLockSeconds;

            [Min(0.1f)]
            [Tooltip("창 회전 스프링의 진동수입니다. 클수록 빠르게 따라붙습니다.")]
            public float TurnSpringFrequency = DefaultPikeTurnSpringFrequency;

            [Range(0.05f, 2f)]
            [Tooltip("창 회전 스프링의 감쇠입니다.\n" +
                     "1 미만이면 목표를 지나쳤다 돌아와 무게감이 생기고, 1을 넘으면 굼떠집니다.")]
            public float TurnSpringDamping = DefaultPikeTurnSpringDamping;

            [Min(0f)]
            [Tooltip("품 안을 내준 뒤 창을 다시 내리기까지의 시간(초)입니다.\n" +
                     "이 동안 창병은 공격하지 못하고 물러납니다. 길수록 진형 붕괴가 치명적입니다.")]
            public float BreakRecoverySeconds = DefaultPikeBreakRecoverySeconds;
        }

        /// <summary>
        /// 랭크와 숙련도가 병사에게 얹는 성장입니다.
        ///
        /// 두 축을 한 묶음에 두는 것이 중요합니다. 나눠 두면 "합쳐서 얼마나 세지는가"를
        /// 두 화면을 오가며 가늠하게 되는데, 이 둘은 <b>서로를 보고 정해야 하는 값</b>입니다.
        /// </summary>
        [Serializable]
        public sealed class GrowthTuning
        {
            [Range(0f, 0.5f)]
            [Tooltip("랭크 한 단계마다 오르는 최대 체력 비율입니다. 0.1이면 5랭크에서 +40%입니다.")]
            public float RankHealthGain = 0.10f;

            [Range(0f, 0.5f)]
            [Tooltip("랭크 한 단계마다 오르는 피해량 비율입니다.")]
            public float RankDamageGain = 0.08f;

            [Range(0f, 0.5f)]
            [Tooltip("랭크 한 단계마다 오르는 공격 속도 비율입니다. 공격 간격과 동작 시간이 함께 짧아집니다.")]
            public float RankAttackSpeedGain = 0.05f;

            [Range(0f, 0.5f)]
            [Tooltip("랭크 한 단계마다 오르는 이동 속도 비율입니다.\n" +
                     "다른 것보다 낮게 잡습니다. 이동 속도는 진형 유지와 직결되어 크게 흔들면 대열이 무너집니다.")]
            public float RankMoveSpeedGain = 0.02f;

            [Range(0f, 2f)]
            [Tooltip("숙련도가 가득 찼을 때 오르는 명중 비율입니다.\n" +
                     "숙련도의 주된 몫입니다. 랭크는 명중을 건드리지 않으므로 이 축은 여기가 단독으로 맡습니다.")]
            public float ProficiencyAccuracyGain = 0.6f;

            [Range(0f, 1f)]
            [Tooltip("숙련도가 가득 찼을 때 오르는 공격 속도 비율입니다. 손에 익으면 동작이 빨라집니다.")]
            public float ProficiencyAttackSpeedGain = 0.20f;

            [Range(0f, 1f)]
            [Tooltip("숙련도가 가득 찼을 때 오르는 피해 비율입니다.\n" +
                     "낮게 잡습니다. 숙련은 '잘 맞히는 것'이지 '세게 때리는 것'이 아닙니다 — " +
                     "세기는 랭크가 맡습니다.")]
            public float ProficiencyDamageGain = 0.15f;
        }

        /// <summary>
        /// 피해 성질과 갑옷의 상성표입니다. <b>상시 방어</b>의 전부가 여기 있습니다.
        ///
        /// <b>왜 표를 배열이 아니라 이름 붙은 값으로 두는가</b>
        ///
        /// 3×3 배열을 인스펙터에 노출하면 어느 칸이 무엇인지 알 수 없습니다.
        /// 밸런스를 잡는 사람이 "참격 대 중갑"을 찾으려고 인덱스를 세게 되고,
        /// 축이 셋씩이라 아홉 줄로 끝나므로 이름을 붙이는 편이 낫습니다.
        /// </summary>
        [Serializable]
        public sealed class ArmorMatchupTuning
        {
            [Header("참격")]
            [Range(0.2f, 2f)]
            [Tooltip("참격이 무갑에 주는 피해 배율입니다.")]
            public float SlashVsUnarmored = 1.0f;

            [Range(0.2f, 2f)]
            [Tooltip("참격이 경갑에 주는 피해 배율입니다. 가죽은 베는 것을 잘 막지 못합니다.")]
            public float SlashVsLight = 1.1f;

            [Range(0.2f, 2f)]
            [Tooltip("참격이 중갑에 주는 피해 배율입니다. 판금에서는 칼날이 미끄러집니다.")]
            public float SlashVsHeavy = 0.7f;

            [Header("자돌")]
            [Range(0.2f, 2f)]
            [Tooltip("자돌이 무갑에 주는 피해 배율입니다. 벨 면적이 없어 맨몸에는 오히려 덜 듭니다.")]
            public float PierceVsUnarmored = 0.9f;

            [Range(0.2f, 2f)]
            [Tooltip("자돌이 경갑에 주는 피해 배율입니다.")]
            public float PierceVsLight = 1.0f;

            [Range(0.2f, 2f)]
            [Tooltip("자돌이 중갑에 주는 피해 배율입니다. 갑옷의 틈을 파고듭니다.\n" +
                     "중갑 보병을 뚫는 두 해법 중 하나입니다. 나머지 하나는 측면을 잡아 방패를 무력화하는 것입니다.")]
            public float PierceVsHeavy = 1.3f;

            [Header("타격")]
            [Range(0.2f, 2f)]
            [Tooltip("타격이 무갑에 주는 피해 배율입니다.")]
            public float BluntVsUnarmored = 0.8f;

            [Range(0.2f, 2f)]
            [Tooltip("타격이 경갑에 주는 피해 배율입니다.")]
            public float BluntVsLight = 0.9f;

            [Range(0.2f, 2f)]
            [Tooltip("타격이 중갑에 주는 피해 배율입니다. 뚫지 않고 충격을 그대로 전달합니다.")]
            public float BluntVsHeavy = 1.2f;

            /// <summary>
            /// 이 타격이 저 갑옷에 얼마나 잘 드는지 구합니다.
            /// </summary>
            /// <param name="damage">때리는 성질입니다.</param>
            /// <param name="armor">맞는 쪽이 걸친 방어입니다.</param>
            /// <returns>피해량에 곱할 배율입니다. 1이면 상성이 없습니다.</returns>
            public float GetEffectiveness(DamageType damage, ArmorType armor)
            {
                return damage switch
                {
                    DamageType.Slash => armor switch
                    {
                        ArmorType.Light => SlashVsLight,
                        ArmorType.Heavy => SlashVsHeavy,
                        _ => SlashVsUnarmored,
                    },
                    DamageType.Pierce => armor switch
                    {
                        ArmorType.Light => PierceVsLight,
                        ArmorType.Heavy => PierceVsHeavy,
                        _ => PierceVsUnarmored,
                    },
                    DamageType.Blunt => armor switch
                    {
                        ArmorType.Light => BluntVsLight,
                        ArmorType.Heavy => BluntVsHeavy,
                        _ => BluntVsUnarmored,
                    },
                    _ => 1f,
                };
            }
        }
    }
}
