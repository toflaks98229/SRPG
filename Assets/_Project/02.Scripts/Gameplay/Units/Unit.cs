using System;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Weapons;
using SRPG.Systems.Combat;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Gameplay.Units
{
    /// <summary>
    /// 개별 병사입니다. 이동과 교전이라는 두 가지 국소 행동만 스스로 판단합니다.
    ///
    /// 설계 원칙: 플레이어는 "분대를 어느 타일로 보낼지"만 지시하고, 나머지는 이 클래스가 처리합니다.
    ///
    /// 전투는 물리 기반입니다. 이 클래스는 <b>공격을 시작할지</b>만 정하고,
    /// 실제로 누가 맞는지는 무기(<see cref="WeaponBase"/>)가 콜라이더 질의로 판정합니다.
    ///
    /// <b>이 클래스는 조율만 합니다</b>
    ///
    /// 판단은 넷으로 나뉘어 있고, 각자 MonoBehaviour 밖에 있습니다.
    ///
    ///   · <see cref="UnitTargeting"/>  — 누구를 볼 것인가 (히스테리시스·표적 고정·공격 대기열)
    ///   · <see cref="UnitLocomotion"/> — 어디로 어떻게 갈 것인가 (도착·분리 조향, 넉백·도약, 지형)
    ///   · <see cref="UnitFacing"/>     — 어디를 향해 설 것인가 (표적·위협·진행·대기)
    ///   · <see cref="AttackerSlots"/>  — 나를 치기로 예약한 적들의 장부
    ///
    /// 여기 남은 것은 그 넷을 <b>정해진 순서로</b> 부르는 일과, 몸에 직접 붙는 것들
    /// (체력·콜라이더·무기 부착·사망)뿐입니다.
    ///
    /// 사망 원인은 둘입니다. 체력 소진과 <b>익사</b>입니다.
    /// 넉백으로 물에 밀려나면 즉사합니다. 조사에서 확인한 Bad North의 핵심 사망 규칙입니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Unit : MonoBehaviour, IDamageable
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>이동 중으로 판정하는 최소 속도입니다. 창병의 이동 중 공격 금지 판정에 사용합니다.</summary>
        private const float MovingSpeedThreshold = 0.25f;

        // ====================================================================================================
        // 2. Inspector
        // ====================================================================================================

        [Header("시각")]
        [SerializeField]
        [Tooltip("지휘관일 때만 켜지는 깃발 오브젝트입니다.\n" +
                 "병사와 지휘관의 프리팹을 따로 두지 않기 위해, 한 프리팹 안에서 꺼 두었다가 켭니다.")]
        private GameObject _commanderFlag;

        [SerializeField]
        [Tooltip("몸체 렌더러입니다. 진영별 색상 덮어쓰기에 사용합니다. 비워 두면 자식에서 찾습니다.")]
        private Renderer _bodyRenderer;

        [Header("물리")]
        [SerializeField]
        [Tooltip("무기 판정에 걸리는 몸체 콜라이더입니다. 비워 두면 초기화할 때 만듭니다.")]
        private CapsuleCollider _bodyCollider;

        // ====================================================================================================
        // 3. Fields
        // ====================================================================================================

        /// <summary>누구를 볼 것인가를 정하는 협력자입니다. 히스테리시스와 표적 고정을 들고 있습니다.</summary>
        private readonly UnitTargeting _targeting = new UnitTargeting();

        /// <summary>어디로 어떻게 갈 것인가를 정하는 협력자입니다. 조향과 외력을 합칩니다.</summary>
        private readonly UnitLocomotion _locomotion = new UnitLocomotion();

        /// <summary>어디를 향해 설 것인가를 정하는 협력자입니다.</summary>
        private readonly UnitFacing _facing = new UnitFacing();

        /// <summary>나를 치기로 예약한 적들의 장부입니다. 오버킬을 막습니다.</summary>
        private readonly AttackerSlots _attackerSlots = new AttackerSlots();

        /// <summary>병과 정의입니다. 체력·사거리·이동 속도 같은 수치의 출처입니다.</summary>
        private UnitDefinition _definition;

        /// <summary>병사가 볼 수 있는 전부입니다. 경로 탐색기도 타일 점유도 여기 없습니다.</summary>
        private IUnitContext _context;

        /// <summary>소속 진영입니다.</summary>
        private Team _team;

        /// <summary>부착된 무기입니다. 실제 타격 판정은 이쪽이 합니다.</summary>
        private WeaponBase _weapon;

        /// <summary>자기 트랜스폼 캐시입니다. 매 프레임 접근하므로 속성 조회를 피합니다.</summary>
        private Transform _transform;

        /// <summary>현재 체력입니다. 0 이하가 되면 즉시 쓰러집니다.</summary>
        private float _health;

        /// <summary>다음 공격까지 남은 시간입니다.</summary>
        private float _attackTimer;

        /// <summary>남은 경직 시간입니다. 0보다 크면 스스로 움직이지 못합니다.</summary>
        private float _staggerTimer;

        /// <summary>공격 동작에 붙잡혀 있는 남은 시간입니다.</summary>
        private float _rootTimer;

        /// <summary>분대(또는 적 AI)가 배정한 진형 슬롯입니다.</summary>
        private Vector3 _slotTarget;

        /// <summary>
        /// 특전이 거는 배율입니다. 분대가 꽂아 줍니다.
        ///
        /// <b>초기값이 Identity 여야 합니다.</b> 구조체의 기본값은 전부 0이고,
        /// 그대로 곱하면 체력도 피해도 0이 되어 병사가 태어나자마자 쓰러집니다.
        /// </summary>
        private UnitModifiers _perks = UnitModifiers.Identity;

        /// <summary>지금까지 견딘 부상 수입니다. 지휘관에게만 쌓입니다.</summary>
        private int _woundsTaken;

        /// <summary>아직 서 있는 호위 수입니다. 분대가 밀어 넣습니다.</summary>
        private int _escortsAlive;

        /// <summary>처음 데리고 나온 호위 수입니다. 분대가 밀어 넣습니다.</summary>
        private int _escortsDeployed;

        /// <summary>
        /// 이 병사가 속한 분대의 전과 장부입니다. 분대가 세우면서 꽂아 줍니다.
        ///
        /// 올려 보내기만 하고 되묻지는 못합니다. 병사가 분대의 사정을 알 이유가 없습니다.
        /// </summary>
        private ISquadTally _tally;

        // ====================================================================================================
        // 4. Properties
        // ====================================================================================================

        /// <summary>이 유닛의 정의 데이터입니다.</summary>
        public UnitDefinition Definition => _definition;

        /// <summary>소속 진영입니다.</summary>
        public Team Team => _team;

        /// <summary>생존 여부입니다.</summary>
        public bool IsAlive { get; private set; } = true;

        /// <summary>현재 월드 좌표입니다.</summary>
        public Vector3 Position => _transform != null ? _transform.position : transform.position;

        /// <summary>현재 체력입니다.</summary>
        public float Health => _health;

        /// <summary>최대 체력 대비 현재 체력 비율입니다.</summary>
        public float HealthRatio => Stats.MaxHealth > 0f ? _health / Stats.MaxHealth : 0f;

        /// <summary>
        /// 이 병사에게 <b>실제로 적용되는</b> 수치입니다.
        ///
        /// <b>정의 에셋 대신 이것을 읽으십시오.</b>
        /// 정의는 기본값이고, 여기에는 랭크와 숙련도가 이미 반영되어 있습니다.
        /// 정의를 직접 읽으면 그 병사가 얼마나 단련되었든 같은 수치로 싸웁니다.
        ///
        /// 몸 크기·무기 길이처럼 성장으로 변하지 않는 값은 여기 없고 정의에 그대로 있습니다.
        /// </summary>
        public UnitStats Stats { get; private set; }

        /// <summary>이 유닛이 분대 지휘관(깃발병)인지 여부입니다. 지휘관이 죽으면 분대가 영구 소멸합니다.</summary>
        public bool IsCommander { get; private set; }

        /// <summary>
        /// 숙련도 랭크입니다. 바뀌면 <see cref="Stats"/> 가 다시 계산됩니다.
        /// 분대가 초기화할 때 소속 병사 전원에게 같은 값을 넣습니다.
        /// </summary>
        public int Rank { get; private set; } = CombatConstants.MinRank;

        /// <summary>
        /// 이 병사가 무기 계열마다 쌓은 숙련도입니다. 바뀌면 <see cref="Stats"/> 가 다시 계산됩니다.
        ///
        /// 실제로 걸리는 것은 자기 무기의 동작(<c>Definition.Style</c>) 하나뿐입니다.
        /// 나머지를 함께 들고 있는 이유는 분대가 무기를 바꿔 들 수 있어야 하기 때문입니다.
        /// </summary>
        public WeaponProficiency Proficiency { get; private set; }

        /// <summary>현재 교전 중인지 여부입니다. 분대 상태 판정에 사용합니다.</summary>
        public bool IsEngaged => _targeting.HasLivingTarget;

        /// <summary>직전 프레임에 적용된 속도입니다. 예측 사격의 입력이기도 합니다.</summary>
        public Vector3 Velocity => _locomotion.Velocity;

        /// <summary>넉백으로 경직된 상태인지 여부입니다.</summary>
        public bool IsStaggered => _staggerTimer > 0f;

        /// <summary>
        /// 스스로 이동 중인지 여부입니다. 넉백에 밀려나는 것은 포함하지 않습니다.
        ///
        /// 창병의 "이동 중 공격 금지"와 무기의 운반 자세가 <b>같은 이 값</b>을 봅니다.
        /// </summary>
        public bool IsMoving { get; private set; }

        /// <summary>지금 나를 치기로 예약한 적의 수입니다. 디버그 표시와 오버킬 판정에 씁니다.</summary>
        public int CommittedAttackerCount => _attackerSlots.Count;

        /// <summary>이 병사의 무기가 횡대를 선호하는지 여부입니다. 분대가 진형 모양을 정할 때 묻습니다.</summary>
        public bool PrefersLineFormation => _weapon != null && _weapon.PrefersLineFormation;

        /// <summary>
        /// 이 병사에게 고정된 씨앗입니다. 태어날 때 한 번 정해지고 죽을 때까지 바뀌지 않습니다.
        ///
        /// 병사마다 달라야 하면서 <b>매 프레임 같아야 하는</b> 값들의 근거입니다.
        /// 진형 흐트러짐이 대표적입니다. 매번 새로 뽑으면 제자리에서 부들부들 떱니다.
        /// </summary>
        public int VisualSeed { get; private set; }

        // ====================================================================================================
        // 5. Events
        // ====================================================================================================

        /// <summary>사망했을 때 발생합니다. 분대가 지휘관 사망을 감지하는 경로입니다.</summary>
        public event Action<Unit> Died;

        /// <summary>
        /// 지휘관이 쓰러질 뻔했다가 부상으로 버텼을 때 발생합니다.
        ///
        /// <b>연출이 이것을 듣습니다.</b> 규칙은 아무 소리도 내지 않고 지나가므로,
        /// 플레이어에게는 "체력이 좀 깎였다"와 <b>구별되지 않습니다</b>.
        /// 영구 손실을 눈앞에서 면한 순간이 그렇게 지나가면 규칙이 없는 것과 같습니다.
        /// </summary>
        public event Action<Unit> Wounded;

        // ====================================================================================================
        // 6. Unity Lifecycle
        // ====================================================================================================

        private void Awake()
        {
            _transform = transform;
        }

        private void OnDestroy()
        {
            _context?.Unregister(this);
        }

        // ====================================================================================================
        // 6-1. Public Methods - Tick
        // ====================================================================================================

        /// <summary>
        /// 한 프레임의 판단 순서입니다. <b>이 순서 자체가 규칙입니다.</b>
        ///
        /// 표적을 먼저 정해야 어디로 갈지가 나오고, 이동 여부가 정해져야 창병이 찌를지가 나옵니다.
        /// 그래서 <see cref="IsMoving"/>은 조향 다음, 전투 앞에서 <b>한 번만</b> 확정됩니다.
        ///
        /// <b>유니티가 아니라 분대가 부릅니다</b>
        ///
        /// 예전에는 이것이 <c>Update</c> 였습니다. 그러면 병사 수만큼 관리되는 호출이 생기고,
        /// 그 비용은 안에서 무엇을 하든 똑같이 듭니다. 지금은 분대가 자기 병사를 훑습니다
        /// (<see cref="Squads.SquadMembers.Tick"/>).
        ///
        /// 덤으로 순서가 생겼습니다. 분대가 이번 프레임의 슬롯을 정한 <b>다음에</b> 여기가 돕니다 —
        /// 예전에는 유니티의 <c>Update</c> 순서가 정해져 있지 않아 한 프레임 늦은 자리를 향할 수 있었습니다.
        ///
        /// <b>분대에 속하지 않은 병사는 돌지 않습니다.</b> 지금 그런 병사는 없습니다 —
        /// 전장의 병사는 전부 분대가 만들어 명부에 올립니다. 병사 하나만 세워 시험하려면
        /// 세운 쪽이 이것을 직접 불러야 합니다.
        /// </summary>
        /// <param name="deltaTime">지난 시간입니다. 0 이하면 아무것도 하지 않습니다.</param>
        public void Tick(float deltaTime)
        {
            if (!IsAlive || _context == null || _definition == null)
            {
                return;
            }

            if (deltaTime <= 0f)
            {
                return;
            }

            TickTimers(deltaTime);

            _targeting.Refresh(
                _weapon != null && _weapon.UsesAttackQueue,
                _weapon != null ? _weapon.TargetLockSeconds : 0f,
                _context.Tuning.Unit.MaxSimultaneousAttackers);

            // 경직 중이거나 공격 동작에 붙잡힌 동안에는 스스로 움직이지 않습니다.
            // 넉백은 그와 무관하게 계속 적용됩니다. 맞고 밀려나는 도중에 버티면 넉백이 무의미해집니다.
            bool canSteer = _staggerTimer <= 0f && _rootTimer <= 0f;
            Vector3 steering = canSteer ? SolveSteering(deltaTime) : Vector3.zero;

            // 이동 여부를 여기서 한 번만 정합니다.
            // 전투 규칙(창병의 이동 중 공격 금지)과 무기의 자세가 같은 판단을 봐야 합니다.
            // 각자 계산하면 "창을 들고 걷는데 공격은 나가는" 어긋남이 생깁니다.
            //
            // <b>조향(=자리 옮김)만 셉니다. 회전은 이동이 아닙니다.</b>
            // 제자리에서 고개를 돌리는 것은 창을 세워 들 이유가 되지 않습니다.
            // 창병이 위협 쪽으로 몸을 틀면서도 계속 찌를 수 있어야 방어선이 성립합니다.
            IsMoving = steering.sqrMagnitude > MovingSpeedThreshold * MovingSpeedThreshold;

            TickCombat(deltaTime);
            ApplyMovement(steering, deltaTime);
        }

        // ====================================================================================================
        // 7. Public Methods - Setup
        // ====================================================================================================

        /// <summary>
        /// 유닛을 초기화합니다. 생성 직후 반드시 호출해야 합니다.
        ///
        /// <b>전투 컨텍스트 전체가 아니라 <see cref="IUnitContext"/>를 받습니다.</b>
        /// 경로 탐색기·타일 점유·전군 명부는 여기 들어오지 않습니다.
        /// 병사가 그것들을 만지는 코드는 아예 컴파일되지 않습니다.
        /// </summary>
        /// <param name="definition">병과 정의입니다. 체력과 전투 수치의 출처입니다.</param>
        /// <param name="team">소속 진영입니다.</param>
        /// <param name="context">병사가 볼 수 있는 것만 담긴 컨텍스트입니다.</param>
        /// <param name="isCommander">지휘관이면 true입니다. 깃발이 켜지고, 죽으면 분대가 소멸합니다.</param>
        public void Initialize(UnitDefinition definition, Team team, IUnitContext context, bool isCommander = false)
        {
            _transform = transform;
            _definition = definition;
            _team = team;
            _context = context;
            IsCommander = isCommander;

            // 체력을 채우기 전에 계산해야 합니다. 최대 체력 자체가 랭크에 따라 달라지므로,
            // 순서가 뒤바뀌면 병사가 자기 최대치보다 적은 체력으로 전장에 섭니다.
            RecalculateStats();

            _health = Stats.MaxHealth;
            IsAlive = true;
            _slotTarget = _transform.position;
            _staggerTimer = 0f;
            _rootTimer = 0f;

            // 협력자들은 각자 필요한 것만 받습니다. 병사가 받은 것보다 더 좁습니다.
            _targeting.Configure(this, context, definition);
            _locomotion.Configure(this, context.Grid, context, context.Tuning, definition);
            _facing.Configure(this, _transform, context, context.Tuning, definition);
            _attackerSlots.Clear();

            // 같은 프레임에 생성된 유닛들이 동시에 공격하지 않도록 첫 쿨다운을 흩뜨립니다.
            _attackTimer = UnityEngine.Random.Range(0f, definition.AttackInterval);

            // 이 병사만의 고정 씨앗입니다. 여기서 한 번만 정합니다.
            VisualSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

            // 프리팹은 깃발을 꺼 둔 상태로 저장되어 있습니다. 지휘관일 때만 켭니다.
            if (_commanderFlag != null)
            {
                _commanderFlag.SetActive(isCommander);
            }

            EnsureBodyCollider();
            EnsureWeapon();

            context.Register(this);
        }

        /// <summary>
        /// 전과를 올려 보낼 분대를 꽂습니다. 분대가 병사를 세우면서 부릅니다.
        /// </summary>
        /// <param name="tally">이 병사가 속한 분대의 전과 장부입니다.</param>
        public void AttachTally(ISquadTally tally)
        {
            _tally = tally;
        }

        /// <summary>
        /// 내가 때린 타격이 상대에게 닿았습니다.
        ///
        /// <b>맞은 쪽이 불러 줍니다.</b>
        /// 때린 쪽에서 세면 무기 종류마다 세는 자리가 따로 생기고, 새 무기를 붙일 때
        /// 그 한 줄을 빠뜨리면 조용히 집계에서 빠집니다.
        /// 피격은 <see cref="ReceiveHit"/> 한 곳으로 모이므로 거기서 한 번만 세면 됩니다.
        /// </summary>
        public void NotifyLandedHit()
        {
            _tally?.ReportHitLanded();
        }

        /// <summary>
        /// 단련도만 설정합니다. 무기 숙련도는 지금 값을 그대로 둡니다.
        /// </summary>
        /// <param name="rank">설정할 랭크입니다. 허용 범위를 벗어나면 잘립니다.</param>
        public void SetRank(int rank)
        {
            SetTraining(rank, Proficiency);
        }

        /// <summary>
        /// 단련도와 숙련도를 함께 설정합니다. 분대가 병사를 세우면서 부릅니다.
        ///
        /// <b>둘을 한 번에 받는 이유</b>
        ///
        /// 따로 두면 유효 수치를 두 번 계산하게 되고, 그 사이에 최대 체력이 한 번 흔들립니다.
        /// 체력은 최대치가 바뀔 때마다 비율을 맞춰 옮기므로, 중간 상태를 거치면
        /// 반올림이 두 번 쌓여 병사가 미묘하게 다친 채로 서게 됩니다.
        /// </summary>
        /// <param name="rank">부대 단련도입니다. 허용 범위를 벗어나면 잘립니다.</param>
        /// <param name="proficiency">무기 계열별 숙련도입니다.</param>
        public void SetTraining(int rank, in WeaponProficiency proficiency)
        {
            SetTraining(rank, proficiency, UnitModifiers.Identity);
        }

        /// <summary>
        /// 단련도·숙련도·특전을 <b>한 번에</b> 설정합니다.
        ///
        /// 셋을 따로 넣으면 유효 수치를 세 번 계산하게 되고, 그 사이에 최대 체력이 두 번 흔들립니다.
        /// 체력은 최대치가 바뀔 때마다 비율을 맞춰 옮기므로 반올림이 쌓여
        /// 병사가 미묘하게 다친 채로 서게 됩니다. <see cref="SetTraining(int, in WeaponProficiency)"/> 와
        /// 같은 이유이고, 특전이 늘어난 지금은 이유가 하나 더 늘었을 뿐입니다.
        /// </summary>
        /// <param name="rank">부대 단련도입니다. 허용 범위를 벗어나면 잘립니다.</param>
        /// <param name="proficiency">무기 계열별 숙련도입니다.</param>
        /// <param name="perks">
        /// 특전이 거는 배율입니다. <b>어떤 특전인지는 받지 않습니다</b> —
        /// 병사가 특전의 종류를 알면 여기에 특전별 분기가 생깁니다.
        /// </param>
        public void SetTraining(int rank, in WeaponProficiency proficiency, in UnitModifiers perks)
        {
            Rank = Mathf.Clamp(rank, CombatConstants.MinRank, CombatConstants.MaxRank);
            Proficiency = proficiency;
            _perks = perks;

            RecalculateStats();
        }

        /// <summary>
        /// 유효 수치를 다시 계산합니다. 정의나 랭크가 바뀔 때마다 불립니다.
        ///
        /// 성장 요소가 늘어나면 여기서 <see cref="UnitModifiers.Combine"/> 로 곱해 넣습니다.
        /// 소비자는 이미 <see cref="Stats"/> 를 읽고 있으므로 그때도 손댈 것이 없습니다.
        ///
        /// <b>최대 체력이 바뀌면 지금 체력을 같은 비율로 옮깁니다.</b>
        ///
        /// 분대는 병사를 다 만든 <b>뒤에</b> 랭크를 넣습니다. 그 사이에 최대 체력이 오르는데
        /// 지금 체력을 그대로 두면, 3랭크 분대의 병사가 처음부터 다친 채로 전장에 섭니다.
        /// 반대로 최대치까지 채워 버리면 전투 중 승급이 곧 완전 회복이 됩니다.
        /// 비율을 지키면 둘 다 아닙니다 — 멀쩡하던 병사는 멀쩡하고, 반쯤 다친 병사는 반쯤 다친 채입니다.
        ///
        /// <b>컨텍스트가 없어도 돕니다.</b>
        /// 유닛만 떼어 검사하는 경로에서는 튜닝이 없으므로 성장 없이 정의 그대로 씁니다.
        /// </summary>
        private void RecalculateStats()
        {
            float previousMax = Stats.MaxHealth;

            var growth = UnitModifiers.Identity;

            if (_context != null && _context.Tuning != null)
            {
                // 단련도와 숙련도는 출처가 다른 성장이라 곱해서 함께 겁니다.
                // 숙련도는 자기가 실제로 든 무기의 동작에 대한 것만 걸립니다 —
                // 활을 잘 쏘는 병사가 검을 들었다고 잘 베지는 않습니다.
                int forThisWeapon = _definition != null ? Proficiency.Get(_definition.Style) : 0;

                growth = _context.Tuning.EvaluateRank(Rank)
                    .Combine(_context.Tuning.EvaluateProficiency(forThisWeapon));
            }

            // 특전은 튜닝이 없어도 걸립니다. 단련도와 숙련도는 곡선이 튜닝에 있지만
            // 특전의 배율은 특전 자신이 들고 오므로, 에셋 없이 여는 경로에서도 그대로 유효합니다.
            growth = growth.Combine(_perks);

            Stats = new UnitStats(_definition, growth);

            // 처음 계산할 때는 이전 최대치가 없습니다. 그때는 부르는 쪽이 체력을 채웁니다.
            if (previousMax > 0f && Stats.MaxHealth > 0f)
            {
                _health = Mathf.Clamp(_health * (Stats.MaxHealth / previousMax), 0f, Stats.MaxHealth);
            }
        }

        /// <summary>
        /// 몸체 머티리얼을 덮어씁니다. 프리팹 없이 만든 임시 몸체에 색을 입힐 때 사용합니다.
        /// </summary>
        /// <param name="material">입힐 머티리얼입니다. null이면 아무것도 하지 않습니다.</param>
        public void OverrideBodyMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (_bodyRenderer == null)
            {
                _bodyRenderer = GetComponentInChildren<Renderer>();
            }

            if (_bodyRenderer != null)
            {
                _bodyRenderer.sharedMaterial = material;
            }
        }

        // ====================================================================================================
        // 8. Public Methods - Damage
        // ====================================================================================================

        /// <summary>
        /// 타격을 받습니다. <see cref="IDamageable"/> 구현입니다.
        ///
        /// <b>여기는 모든 피해가 지나는 유일한 길목입니다.</b>
        /// 무기는 "무엇을 어느 방향으로 때렸는지"만 넘기고 감쇠에는 관여하지 않습니다.
        ///
        /// 예전에는 무기가 피해와 넉백을 <b>따로</b> 호출했고, 화살만 자기가 감쇠를 계산한 뒤
        /// bool 인자로 "이미 깎았다"고 알렸습니다. 그래서 감쇠 규칙이 두 곳에 나뉘어 있었고,
        /// 넉백에 감쇠를 곱하는 것을 잊으면 <b>막아 낸 화살에 밀려나</b> 물에 빠지는 일이 생겼습니다.
        ///
        /// <b>여기서 하는 일과 하지 않는 일</b>
        ///
        /// 하는 일은 셋입니다 — 전과를 세고, 소리를 내고, 결과를 자기 몸에 적용합니다.
        /// 셋 다 <b>이 병사에게 일어난 일</b>이라 병사가 알아야 합니다.
        ///
        /// 얼마나 들어가는지를 <b>정하는</b> 것은 여기가 아닙니다(<see cref="DamageResolver"/>).
        /// 방패 각도와 갑옷 상성은 누가 맞았는지와 무관한 규칙이고,
        /// 그것이 이 클래스 안에 있으면 확인하는 데 씬이 필요해집니다.
        /// </summary>
        /// <param name="hit">무엇을 어느 방향으로 때렸는지가 담긴 타격 정보입니다.</param>
        public void ReceiveHit(in DamageInfo hit)
        {
            if (!IsAlive)
            {
                return;
            }

            // 닿았다는 사실 자체를 먼저 셉니다. 얼마나 막혔는지는 상대의 사정이고,
            // 숙련은 '잘 맞히는 것'이라 방패에 막혔어도 맞힌 것은 맞힌 것입니다.
            (hit.Source as Unit)?.NotifyLandedHit();

            // 소리도 같은 이유로 여기서 냅니다. 막혔든 뚫었든 <b>부딪힌 것은 부딪힌 것</b>이고,
            // 무엇보다 이곳이 모든 피해가 지나는 유일한 길목입니다.
            // 무기마다 소리를 내게 하면 새 무기를 만들 때마다 잊을 자리가 하나씩 늘어납니다.
            _context?.Audio.PlayHit(hit.Type, Position);

            var outcome = DamageResolver.Resolve(hit, BuildDefenderProfile(), _context?.Tuning);

            _health -= outcome.HealthLoss;

            if (_health <= 0f)
            {
                // 지휘관은 여기서 곧바로 쓰러지지 않습니다.
                // 호위가 남아 있으면 버티고, 그마저 무너진 뒤에야 확률이 개입합니다.
                if (IsCommander && TryEndureAsCommander())
                {
                    return;
                }

                Kill();
                return;
            }

            if (outcome.HasImpulse)
            {
                ApplyKnockback(outcome.Impulse, outcome.StaggerSeconds);
            }
        }

        /// <summary>
        /// 지휘관이 치명상을 견뎌 내는지 봅니다.
        ///
        /// <b>호위 수는 분대가 밀어 넣어 줍니다</b>(<see cref="SetEscortStrength"/>).
        /// 병사가 분대에 되묻는 길은 이 프로젝트에 없습니다 — 그 길을 열면
        /// "우리 분대가 무너졌는가"를 묻는 코드가 곧 생깁니다.
        /// 지휘관에게 필요한 것은 <b>숫자 둘</b>뿐이라 밀어 넣는 편이 좁습니다.
        ///
        /// 판정 자체는 <see cref="CommanderFate"/> 가 합니다. 씬 없이 검증되어야 하는 규칙이고,
        /// 무엇보다 "언제 죽는가"는 이 게임에서 가장 중요한 규칙이라 한곳에 있어야 합니다.
        /// </summary>
        /// <returns>버텨 냈으면 true입니다. false면 부르는 쪽이 쓰러뜨립니다.</returns>
        private bool TryEndureAsCommander()
        {
            var rules = _context?.Tuning?.Commander;

            var guard = new CommanderGuard(_escortsAlive, _escortsDeployed, _woundsTaken);

            var fate = CommanderFate.Resolve(guard, rules, UnityEngine.Random.value);

            if (fate == CommanderFateOutcome.Fallen)
            {
                return false;
            }

            // 이번 부상을 세기 <b>전</b>의 수로 회복량을 정합니다.
            // 먼저 세면 첫 부상부터 이미 깎인 몫을 받아 규칙이 한 칸씩 밀립니다.
            _health = CommanderFate.ResolveRecoveredHealth(Stats.MaxHealth, _woundsTaken, rules);
            _woundsTaken++;

            // 쓰러졌다 일어나는 틈입니다. 그대로 계속 싸우면 부상으로 보이지 않습니다.
            float stagger = rules != null ? rules.WoundStaggerSeconds : 0.8f;

            _staggerTimer = Mathf.Max(_staggerTimer, stagger);
            _weapon?.CancelAttack();

            _context?.Audio.PlayDeath(Position);

            Wounded?.Invoke(this);

            return true;
        }

        /// <summary>
        /// 이 지휘관을 지키고 있는 호위의 수를 알려 줍니다. 분대가 매 프레임 밀어 넣습니다.
        ///
        /// <b>왜 밀어 넣는가</b>
        ///
        /// 병사는 분대의 사정을 되물을 수 없습니다(<see cref="ISquadTally"/> 가 한 방향인 이유).
        /// 그런데 지휘관의 생사만은 분대의 상태가 정해야 합니다 —
        /// 호위가 멀쩡한데 지휘관만 사라지면 그것은 판단이 아니라 사고입니다.
        /// 필요한 것이 숫자 둘뿐이므로, 창을 여는 대신 그 둘만 밀어 넣습니다.
        /// </summary>
        /// <param name="alive">지휘관을 뺀, 아직 서 있는 병사 수입니다.</param>
        /// <param name="deployed">지휘관을 뺀, 처음 데리고 나온 병사 수입니다.</param>
        public void SetEscortStrength(int alive, int deployed)
        {
            _escortsAlive = alive;
            _escortsDeployed = deployed;
        }

        /// <summary>
        /// 지금 이 병사의 <b>맞는 쪽</b> 상태를 한 덩어리로 묶습니다.
        ///
        /// <b>정의가 없으면 방패도 갑옷도 없는 것으로 봅니다.</b>
        /// 병과 정의 없이 병사만 세워 두는 경로가 있고(자동 검사),
        /// 그때 방어 수치를 어림잡으면 검사가 보는 것이 흐려집니다.
        /// </summary>
        /// <returns>이번 판정에 쓸 방어 측 상태입니다.</returns>
        private DefenderProfile BuildDefenderProfile()
        {
            // 캐시해 둔 트랜스폼을 먼저 씁니다. Awake 전에 맞는 경우가 있어 폴백을 둡니다.
            Vector3 forward = _transform != null ? _transform.forward : transform.forward;

            if (_definition == null)
            {
                return new DefenderProfile(forward, 0f, ArmorType.Unarmored, IsMoving);
            }

            return new DefenderProfile(
                forward,
                Stats.ProjectileResistance,
                _definition.Armor,
                IsMoving);
        }

        /// <summary>
        /// 넉백과 경직을 적용합니다.
        ///
        /// 넉백은 단순한 연출이 아니라 주요 사망 수단입니다.
        /// 밀려난 자리가 물이면 그대로 익사합니다.
        /// </summary>
        /// <param name="impulse">수평 방향 밀어내기 속도입니다.</param>
        /// <param name="staggerSeconds">경직 시간입니다.</param>
        public void ApplyKnockback(Vector3 impulse, float staggerSeconds)
        {
            if (!IsAlive)
            {
                return;
            }

            _locomotion.AddKnockback(impulse);

            if (staggerSeconds > 0f)
            {
                _staggerTimer = Mathf.Max(_staggerTimer, staggerSeconds);
                _weapon?.CancelAttack();
            }
        }

        /// <summary>
        /// 스스로 앞으로 몸을 던집니다. 검병이 벨 때 파고드는 힘입니다.
        ///
        /// 넉백과 달리 <b>물로 밀려나지 않습니다.</b>
        /// 밀려서 빠지는 것은 사고지만, 달려들다 빠지는 것은 자살입니다.
        /// </summary>
        /// <param name="impulse">앞으로 몸을 던지는 속도입니다.</param>
        public void ApplyLunge(Vector3 impulse)
        {
            if (!IsAlive)
            {
                return;
            }

            _locomotion.AddLunge(impulse);
        }

        /// <summary>
        /// 유닛을 즉시 사망 처리합니다.
        /// </summary>
        public void Kill()
        {
            if (!IsAlive)
            {
                return;
            }

            IsAlive = false;
            _health = 0f;

            _weapon?.CancelAttack();

            DropFlag();

            // 오브젝트가 사라지기 전에 냅니다. 소리는 자리에 놓이는 것이지
            // 이 오브젝트에 붙는 것이 아니라, 시체가 없어져도 소리는 남습니다.
            _context?.Audio.PlayDeath(Position);

            Died?.Invoke(this);
            _context?.Unregister(this);

            Destroy(gameObject);
        }

        // ====================================================================================================
        // 9. Public Methods - Orders
        // ====================================================================================================

        /// <summary>
        /// 이 유닛이 향할 진형 슬롯을 지정합니다. 분대 또는 적 AI가 매 프레임 갱신합니다.
        /// </summary>
        /// <param name="worldPosition">향할 월드 좌표입니다. 행군 중에는 분대 앵커입니다.</param>
        public void SetSlotTarget(Vector3 worldPosition)
        {
            _slotTarget = worldPosition;
        }

        /// <summary>
        /// 교전 대상이 없을 때 바라볼 방향을 분대가 지정합니다.
        /// </summary>
        /// <param name="direction">바라볼 방향입니다. 정규화되지 않아도 됩니다.</param>
        public void SetIdleFacing(Vector3 direction)
        {
            _facing.SetIdleFacing(direction);
        }

        // ====================================================================================================
        // 10. Public Methods - Attack Queue
        // ====================================================================================================

        /// <summary>
        /// 나를 칠 자리를 하나 예약합니다. 이미 자리가 찼으면 거절합니다.
        ///
        /// <b>정원은 체력으로 정해집니다.</b> 규칙은 <see cref="AttackerSlots"/>가 들고 있습니다.
        /// </summary>
        /// <param name="attacker">예약하려는 쪽입니다.</param>
        /// <param name="damagePerHit">그 공격이 한 번에 주는 피해량입니다. 정원 계산의 분모입니다.</param>
        /// <param name="maxAttackers">정원의 상한입니다. 계산 결과가 아무리 커도 이를 넘지 않습니다.</param>
        /// <returns>자리를 잡았으면 true입니다.</returns>
        public bool TryCommitAttacker(Unit attacker, float damagePerHit, int maxAttackers)
        {
            if (attacker == null || !IsAlive)
            {
                return false;
            }

            return _attackerSlots.TryCommit(attacker, _health, damagePerHit, maxAttackers);
        }

        /// <summary>
        /// 예약을 <b>잡지 않고</b> 자리가 남았는지만 봅니다. 표적을 고를 때 씁니다.
        /// </summary>
        /// <param name="attacker">자리를 알아보는 쪽입니다. 이미 예약했으면 언제나 true입니다.</param>
        /// <param name="damagePerHit">그 공격이 한 번에 주는 피해량입니다. 정원 계산의 분모입니다.</param>
        /// <param name="maxAttackers">정원의 상한입니다.</param>
        /// <returns>자리가 남아 있으면 true입니다. 이미 쓰러졌으면 false입니다.</returns>
        public bool HasRoomForAttacker(Unit attacker, float damagePerHit, int maxAttackers)
        {
            if (!IsAlive)
            {
                return false;
            }

            return _attackerSlots.HasRoom(attacker, _health, damagePerHit, maxAttackers);
        }

        /// <summary>예약을 놓습니다. 표적을 바꾸거나 죽을 때 호출합니다.</summary>
        /// <param name="attacker">예약을 놓을 쪽입니다. 예약이 없어도 안전합니다.</param>
        public void ReleaseAttacker(Unit attacker)
        {
            _attackerSlots.Release(attacker);
        }

        /// <summary>
        /// 지정 반경 안에서 가장 가까운 적을 찾습니다. 무기가 근접 위협을 살필 때 씁니다.
        /// </summary>
        /// <param name="radius">살펴볼 반경입니다.</param>
        /// <returns>가장 가까운 적입니다. 반경 안에 없거나 컨텍스트가 없으면 null입니다.</returns>
        public Unit FindClosestEnemyWithin(float radius)
        {
            return _context != null ? _context.FindNearestEnemy(Position, _team, radius) : null;
        }

        // ====================================================================================================
        // 11. Private Methods - Setup
        // ====================================================================================================

        /// <summary>
        /// 무기 판정에 걸릴 콜라이더를 확보합니다.
        /// 콜라이더가 없으면 이 유닛은 아무에게도 맞지 않습니다.
        /// </summary>
        private void EnsureBodyCollider()
        {
            if (_bodyCollider == null)
            {
                _bodyCollider = GetComponent<CapsuleCollider>();
            }

            if (_bodyCollider == null)
            {
                _bodyCollider = gameObject.AddComponent<CapsuleCollider>();
            }

            _bodyCollider.radius = _definition.Radius;
            _bodyCollider.height = Mathf.Max(_definition.DebugHeight, _definition.Radius * 2f);
            _bodyCollider.center = new Vector3(0f, _definition.DebugHeight * 0.5f, 0f);

            // 트리거로 둡니다. 유닛끼리 물리로 밀어내면 분리 조향과 싸우게 됩니다.
            // 겹침 방지는 조향이 담당하고, 콜라이더는 판정 대상 역할만 합니다.
            _bodyCollider.isTrigger = true;

            GameLayers.ApplyRecursively(gameObject, GameLayers.Unit);
        }

        /// <summary>
        /// 공격 방식에 맞는 무기를 확보합니다.
        /// 프리팹이 미리 붙여 둔 무기가 있으면 그것을 쓰고, 없으면 코드로 붙입니다.
        /// </summary>
        private void EnsureWeapon()
        {
            _weapon = GetComponent<WeaponBase>();

            if (_weapon == null)
            {
                _weapon = _definition.Style switch
                {
                    AttackStyle.MeleeThrust => gameObject.AddComponent<PikeWeapon>(),
                    AttackStyle.Projectile => gameObject.AddComponent<BowWeapon>(),
                    _ => gameObject.AddComponent<MeleeWeapon>(),
                };
            }

            _weapon.Initialize(this, _definition, _context.ProjectilePool, _context.Tuning, _context.Audio);
        }

        // ====================================================================================================
        // 12. Private Methods - Tick
        // ====================================================================================================

        private void TickTimers(float deltaTime)
        {
            _attackTimer -= deltaTime;
            _staggerTimer -= deltaTime;
            _rootTimer -= deltaTime;

            _targeting.Tick(deltaTime);
            _facing.Tick(deltaTime);
            _locomotion.Decay(deltaTime);
        }

        /// <summary>
        /// 이번 프레임의 이동 지시를 모아 조향을 구합니다.
        ///
        /// 슬롯은 분대가, 리시와 후퇴는 무기가 정합니다.
        /// 여기서 그 셋을 한자리에 모아 넘기므로, 이동을 계산하는 쪽은 무기를 캐묻지 않아도 됩니다.
        /// </summary>
        private Vector3 SolveSteering(float deltaTime)
        {
            Vector3 retreatFrom = Vector3.zero;
            bool retreating = _weapon != null && _weapon.TryGetRetreatFrom(out retreatFrom);

            var target = _targeting.Target;
            bool hasTarget = _targeting.HasLivingTarget;

            var order = new SteeringOrder(
                _slotTarget,
                hasTarget,
                hasTarget ? target.Position : Vector3.zero,
                GetFormationBreakDistance(),
                retreating,
                retreatFrom);

            return _locomotion.Solve(order, Position, deltaTime);
        }

        /// <summary>
        /// 대열을 풀고 나갈 수 있는 월드 거리입니다.
        ///
        /// 무기는 <b>칸</b> 단위로 말하고, 여기서 타일 크기를 곱해 월드 거리로 바꿉니다.
        /// 칸으로 두는 이유는 이 게임의 공간 단위가 타일이기 때문입니다.
        /// "2칸까지 나간다"는 기획 의도가 타일 크기를 바꿔도 그대로 유지됩니다.
        /// </summary>
        private float GetFormationBreakDistance()
        {
            if (_weapon == null)
            {
                return 0f;
            }

            return _weapon.FormationBreakTiles * _context.Grid.CellSize;
        }

        // ====================================================================================================
        // 13. Private Methods - Combat
        // ====================================================================================================

        /// <summary>
        /// 무기 동작을 진행시키고, 조건이 맞으면 새 공격을 시작합니다.
        /// 실제 타격 판정은 무기가 수행합니다.
        /// </summary>
        private void TickCombat(float deltaTime)
        {
            if (_weapon == null)
            {
                return;
            }

            _weapon.Tick(deltaTime);

            var target = _targeting.Target;
            bool hasLivingTarget = _targeting.HasLivingTarget;

            var blocked = AttackGate.Evaluate(new AttackGateInput(
                _weapon.IsBusy,
                _staggerTimer,
                _attackTimer,
                hasLivingTarget,
                IsMoving,
                _definition.CanAttackWhileMoving,
                hasLivingTarget ? Vector3.Distance(Position, target.Position) : float.MaxValue,
                Stats.AttackRange));

            if (blocked != AttackBlock.None)
            {
                return;
            }

            // 공격 대기열: 찌르기 직전에 자리를 예약합니다.
            // 자리가 없으면 이번 공격을 보류합니다.
            if (_weapon.UsesAttackQueue &&
                !target.TryCommitAttacker(this, Stats.AttackDamage, _context.Tuning.Unit.MaxSimultaneousAttackers))
            {
                _targeting.BreakLock();
                return;
            }

            if (_weapon.TryBeginAttack(target))
            {
                _attackTimer = Stats.AttackInterval;
                _rootTimer = _weapon.RootDuration;
            }
        }

        // ====================================================================================================
        // 14. Private Methods - Movement
        // ====================================================================================================

        /// <summary>
        /// 조향과 외력을 합쳐 실제로 이동시키고, 방향을 갱신합니다.
        /// </summary>
        private void ApplyMovement(Vector3 steering, float deltaTime)
        {
            if (!_locomotion.TryStep(Position, steering, deltaTime, out Vector3 next))
            {
                Drown(next);
                return;
            }

            _transform.position = next;
            _facing.Apply(next, steering, IsMoving, _targeting.Target, _weapon, deltaTime);
        }

        /// <summary>
        /// 지휘관의 깃발을 그 자리에 남깁니다.
        ///
        /// <b>왜 남기는가</b>
        ///
        /// 지휘관을 잃으면 분대가 통째로 사라집니다. 그런데 화면에서는 병사 몇이
        /// 동시에 없어질 뿐이라, <b>무엇을 잃었는지가 그 자리에 남지 않습니다.</b>
        /// 이 게임에서 가장 중요한 사건이 가장 조용하게 지나가는 셈입니다.
        ///
        /// 깃발이 남으면 전투가 끝날 때까지 그 자리가 <b>여기서 분대를 잃었다</b>고 말합니다.
        /// 목록에서 줄 하나가 사라지는 것과는 다른 종류의 신호입니다.
        ///
        /// <b>씬 루트로 올립니다.</b> 깃발은 지금 지휘관의 자식이고, 지휘관은 곧 파괴됩니다.
        /// 분대에 매달면 분대도 함께 사라지므로 더 위로 올려야 하는데,
        /// 전투 루트를 알고 있는 것은 여기가 아닙니다. 루트로 올리면 씬이 바뀔 때 함께 사라집니다.
        /// </summary>
        private void DropFlag()
        {
            if (!IsCommander || _commanderFlag == null || !_commanderFlag.activeSelf)
            {
                return;
            }

            _commanderFlag.transform.SetParent(null, worldPositionStays: true);
            _commanderFlag.name = "FallenCommanderFlag";

            // 더 이상 이 병사의 것이 아닙니다. 다시 켜고 끄는 쪽이 없도록 참조를 놓습니다.
            _commanderFlag = null;
        }

        /// <summary>
        /// 물에 빠져 죽습니다. 낙수 지점은 이미 수면 높이로 맞춰져 있습니다.
        /// </summary>
        private void Drown(Vector3 splashPosition)
        {
            _transform.position = splashPosition;
            Kill();
        }
    }
}
