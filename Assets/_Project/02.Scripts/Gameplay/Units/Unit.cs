using System;
using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Weapons;
using SRPG.Systems.Combat;
using SRPG.Systems.Pathfinding;
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
    /// 공격 시작 조건
    ///   · 무기가 이전 동작을 끝냈고 재사용 대기가 지났다
    ///   · 대상이 사거리 안에 있다
    ///   · 이동 중이 아니거나, 이동 중 공격이 허용된 병과다 (창병은 불가)
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

        /// <summary>슬롯에 도착했다고 판단하는 거리입니다.</summary>
        private const float SlotArrivalThreshold = 0.45f;

        /// <summary>도착 감속이 시작되는 거리입니다.</summary>
        private const float ArriveSlowRadius = 1.2f;

        /// <summary>분리 조향의 가중치입니다. 값이 크면 서로 더 강하게 밀어냅니다.</summary>
        private const float SeparationWeight = 1.6f;

        /// <summary>이동 중으로 판정하는 최소 속도입니다. 창병의 이동 중 공격 금지 판정에 사용합니다.</summary>
        private const float MovingSpeedThreshold = 0.25f;

        /// <summary>넉백 속도가 초당 얼마나 줄어드는지입니다.</summary>
        private const float KnockbackDecay = 11f;

        /// <summary>도약 속도가 초당 얼마나 줄어드는지입니다. 넉백보다 빨리 잦아듭니다.</summary>
        private const float LungeDecay = 20f;

        /// <summary>이 속도 이상으로 밀려나는 중이면 물 위로도 밀려납니다(= 익사할 수 있습니다).</summary>
        private const float DrownKnockbackThreshold = 1.2f;

        /// <summary>평상시 회전 속도입니다.</summary>
        private const float TurnSpeed = 12f;

        /// <summary>교전 중 회전 속도입니다. 무기 판정이 정면 기준이라 빠르게 돌아야 합니다.</summary>
        private const float CombatTurnSpeed = 22f;

        /// <summary>방패병이 위협 방향을 다시 살피는 주기(초)입니다. 매 프레임 질의하지 않기 위한 것입니다.</summary>
        private const float ThreatScanInterval = 0.3f;

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

        private UnitDefinition _definition;
        private BattleContext _context;
        private Team _team;
        private WeaponBase _weapon;

        private float _health;
        private float _attackTimer;
        private float _staggerTimer;
        private float _rootTimer;
        private Unit _target;
        private Vector3 _slotTarget;
        private Vector3 _lastVelocity;
        private Vector3 _knockbackVelocity;

        /// <summary>
        /// 스스로 앞으로 던진 도약 속도입니다. 넉백과 <b>따로</b> 둡니다.
        ///
        /// 같은 채널에 넣으면 물가의 적에게 달려들 때마다 스스로 물에 뛰어들어 익사합니다.
        /// 밀려나는 것과 뛰어드는 것은 결과가 달라야 합니다.
        /// </summary>
        private Vector3 _lungeVelocity;
        private Transform _transform;

        private readonly List<Unit> _neighborBuffer = new List<Unit>(16);
        private readonly List<Unit> _candidateBuffer = new List<Unit>(16);

        /// <summary>
        /// 지금 나를 치기로 <b>예약한</b> 적들입니다.
        ///
        /// 오버킬을 막기 위한 장치입니다. 창병 여럿이 최전방의 한 명만 동시에 찌르면
        /// 그가 죽은 자리로 뒤따라오던 적들이 그대로 통과합니다.
        /// 예약 인원에 상한을 두면 남는 창병이 자연히 다음 적을 겨눕니다.
        /// </summary>
        private readonly HashSet<Unit> _committedAttackers = new HashSet<Unit>();

        /// <summary>표적을 바꾸지 못하는 남은 시간입니다.</summary>
        private float _targetLockTimer;

        /// <summary>회전 스프링의 각속도입니다. 무기가 무게를 가질 때만 씁니다.</summary>
        private float _yawVelocity;

        /// <summary>방패병이 몸을 돌릴 위협 대상입니다. 공격 대상과는 별개입니다.</summary>
        private Unit _threatSource;

        private float _threatScanTimer;

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
        public float HealthRatio => _definition != null && _definition.MaxHealth > 0f ? _health / _definition.MaxHealth : 0f;

        /// <summary>이 유닛이 분대 지휘관(깃발병)인지 여부입니다. 지휘관이 죽으면 분대가 영구 소멸합니다.</summary>
        public bool IsCommander { get; private set; }

        /// <summary>
        /// 숙련도 랭크입니다. 궁수의 조준 산포가 이 값에 반비례합니다.
        /// 분대가 초기화할 때 소속 병사 전원에게 같은 값을 넣습니다.
        /// </summary>
        public int Rank { get; private set; } = CombatConstants.MinRank;

        /// <summary>현재 교전 중인지 여부입니다. 분대 상태 판정에 사용합니다.</summary>
        public bool IsEngaged => _target != null && _target.IsAlive;

        /// <summary>직전 프레임에 적용된 속도입니다. 예측 사격의 입력이기도 합니다.</summary>
        public Vector3 Velocity => _lastVelocity;

        /// <summary>넉백으로 경직된 상태인지 여부입니다.</summary>
        public bool IsStaggered => _staggerTimer > 0f;

        /// <summary>
        /// 스스로 이동 중인지 여부입니다. 넉백에 밀려나는 것은 포함하지 않습니다.
        ///
        /// 창병의 "이동 중 공격 금지"와 무기의 운반 자세가 <b>같은 이 값</b>을 봅니다.
        /// </summary>
        public bool IsMoving { get; private set; }

        /// <summary>지금 나를 치기로 예약한 적의 수입니다. 디버그 표시와 오버킬 판정에 씁니다.</summary>
        public int CommittedAttackerCount => _committedAttackers.Count;

        /// <summary>이 병사의 무기가 횡대를 선호하는지 여부입니다. 분대가 진형 모양을 정할 때 묻습니다.</summary>
        public bool PrefersLineFormation => _weapon != null && _weapon.PrefersLineFormation;

        // ====================================================================================================
        // 5. Events
        // ====================================================================================================

        /// <summary>사망했을 때 발생합니다. 분대가 지휘관 사망을 감지하는 경로입니다.</summary>
        public event Action<Unit> Died;

        // ====================================================================================================
        // 6. Unity Lifecycle
        // ====================================================================================================

        private void Awake()
        {
            _transform = transform;
        }

        private void Update()
        {
            if (!IsAlive || _context == null || _definition == null)
            {
                return;
            }

            float deltaTime = UnityEngine.Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            TickTimers(deltaTime);
            AcquireTarget();

            // 경직 중이거나 공격 동작에 붙잡힌 동안에는 스스로 움직이지 않습니다.
            // 넉백은 그와 무관하게 계속 적용됩니다. 맞고 밀려나는 도중에 버티면 넉백이 무의미해집니다.
            bool canSteer = _staggerTimer <= 0f && _rootTimer <= 0f;
            Vector3 steering = canSteer ? ComputeSteering() : Vector3.zero;

            // 이동 여부를 여기서 한 번만 정합니다.
            // 전투 규칙(창병의 이동 중 공격 금지)과 무기의 자세가 같은 판단을 봐야 합니다.
            // 각자 계산하면 "창을 들고 걷는데 공격은 나가는" 어긋남이 생깁니다.
            IsMoving = steering.sqrMagnitude > MovingSpeedThreshold * MovingSpeedThreshold;

            TickCombat(deltaTime);
            ApplyMovement(steering, deltaTime);
        }

        private void OnDestroy()
        {
            _context?.Unregister(this);
        }

        // ====================================================================================================
        // 7. Public Methods - Setup
        // ====================================================================================================

        /// <summary>
        /// 유닛을 초기화합니다. 생성 직후 반드시 호출해야 합니다.
        /// </summary>
        public void Initialize(UnitDefinition definition, Team team, BattleContext context, bool isCommander = false)
        {
            _transform = transform;
            _definition = definition;
            _team = team;
            _context = context;
            IsCommander = isCommander;

            _health = definition.MaxHealth;
            IsAlive = true;
            _slotTarget = _transform.position;
            _knockbackVelocity = Vector3.zero;
            _lungeVelocity = Vector3.zero;
            _staggerTimer = 0f;
            _rootTimer = 0f;

            // 같은 프레임에 생성된 유닛들이 동시에 공격하지 않도록 첫 쿨다운을 흩뜨립니다.
            _attackTimer = UnityEngine.Random.Range(0f, definition.AttackInterval);

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
        /// 숙련도 랭크를 설정합니다.
        /// </summary>
        public void SetRank(int rank)
        {
            Rank = Mathf.Clamp(rank, CombatConstants.MinRank, CombatConstants.MaxRank);
        }

        /// <summary>
        /// 몸체 머티리얼을 덮어씁니다. 프리팹 없이 만든 임시 몸체에 색을 입힐 때 사용합니다.
        /// </summary>
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
        /// 피해 → 감쇠 → 넉백 → 경직이 여기 한 곳에서 순서대로 처리됩니다.
        /// 무기는 "무엇을 어느 방향으로 때렸는지"만 넘기고 감쇠에는 관여하지 않습니다.
        ///
        /// 예전에는 무기가 피해와 넉백을 <b>따로</b> 호출했고, 화살만 자기가 감쇠를 계산한 뒤
        /// bool 인자로 "이미 깎았다"고 알렸습니다. 그래서 감쇠 규칙이 두 곳에 나뉘어 있었고,
        /// 넉백에 감쇠를 곱하는 것을 잊으면 <b>막아 낸 화살에 밀려나</b> 물에 빠지는 일이 생겼습니다.
        /// </summary>
        public void ReceiveHit(in DamageInfo hit)
        {
            if (!IsAlive)
            {
                return;
            }

            float mitigation = ComputeMitigation(hit);

            _health -= hit.Amount * mitigation;

            if (_health <= 0f)
            {
                Kill();
                return;
            }

            // 넉백은 수평 성분만 씁니다. 위에서 내리꽂힌 화살이 유닛을 땅으로 박을 수는 없습니다.
            Vector3 push = hit.Direction;
            push.y = 0f;

            if (push.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            // <b>막아 내도 운동 에너지는 전달됩니다.</b>
            // 방패는 날붙이를 막을 뿐 충격량까지 없애지는 못합니다.
            // 이 덕분에 큰 적의 일격은 피해가 막혀도 방패벽을 뒤로 밀어 틈을 벌립니다.
            float impulseRetention = _context != null
                ? Mathf.Clamp01(_context.Tuning.BlockedKnockbackRetention)
                : 1f;

            float knockbackScale = Mathf.Lerp(mitigation, 1f, impulseRetention);

            ApplyKnockback(
                push.normalized * (hit.KnockbackForce * knockbackScale),
                hit.StaggerSeconds * knockbackScale);
        }

        /// <summary>
        /// 이 타격이 얼마나 통하는지 구합니다. 1이면 그대로 들어가고, 0에 가까우면 거의 막힙니다.
        ///
        /// 방패는 투사체만 막습니다. 그리고 <b>맞은 방향</b>으로 판정합니다.
        /// 조사에서 확인한 "측면에서 쏠 때 가장 잘 밀리고, 고지대에서 쏘면 잘 안 통한다"가
        /// 이 판정 하나에서 나옵니다.
        /// </summary>
        private float ComputeMitigation(in DamageInfo hit)
        {
            if (hit.Kind != DamageKind.Projectile)
            {
                return 1f;
            }

            if (_definition == null || _definition.ProjectileResistance <= 0f)
            {
                return 1f;
            }

            float steepMargin = _context != null
                ? _context.Tuning.ShieldSteepBlockMarginDegrees
                : BattleTuning.DefaultSteepBlockMarginDegrees;

            // 이동 중에는 방패가 제 몫을 못 합니다.
            // 뛰는 동안 방패가 위아래로 흔들려 물리적인 빈틈이 생기기 때문입니다.
            // 그래서 "궁수 앞에서 뛰어다니지 마라"가 규칙이 아니라 결과로 나옵니다.
            float resistance = _definition.ProjectileResistance;

            if (IsMoving && _context != null)
            {
                resistance *= Mathf.Clamp01(_context.Tuning.ShieldMovingEffectiveness);
            }

            return ShieldSolver.ComputeBlockFactor(
                hit.Direction,
                _transform != null ? _transform.forward : transform.forward,
                resistance,
                hit.ArcAngleDegrees,
                steepMargin);
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

            impulse.y = 0f;
            _knockbackVelocity += impulse;

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
        public void ApplyLunge(Vector3 impulse)
        {
            if (!IsAlive)
            {
                return;
            }

            impulse.y = 0f;
            _lungeVelocity += impulse;
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
        public void SetSlotTarget(Vector3 worldPosition)
        {
            _slotTarget = worldPosition;
        }

        // ====================================================================================================
        // 9-1. Public Methods - Attack Queue
        // ====================================================================================================

        /// <summary>
        /// 나를 칠 자리를 하나 예약합니다. 이미 자리가 찼으면 거절합니다.
        ///
        /// <b>정원은 체력으로 정해집니다.</b> 한 방에 죽는 적에게는 한 명만 붙고,
        /// 여러 대를 맞아야 하는 거구에게는 여러 명이 함께 붙습니다.
        /// 이 한 줄이 "덩치 큰 적에게는 유동적으로 여럿이 달라붙는다"를 규칙 없이 만들어 냅니다.
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

            // 이미 잡아 둔 자리는 그대로 유지합니다.
            if (_committedAttackers.Contains(attacker))
            {
                return true;
            }

            PruneCommittedAttackers();

            int capacity = ComputeAttackerCapacity(damagePerHit, maxAttackers);

            if (_committedAttackers.Count >= capacity)
            {
                return false;
            }

            _committedAttackers.Add(attacker);
            return true;
        }

        /// <summary>
        /// 예약을 <b>잡지 않고</b> 자리가 남았는지만 봅니다. 표적을 고를 때 씁니다.
        /// </summary>
        public bool HasRoomForAttacker(Unit attacker, float damagePerHit, int maxAttackers)
        {
            if (!IsAlive)
            {
                return false;
            }

            if (attacker != null && _committedAttackers.Contains(attacker))
            {
                return true;
            }

            PruneCommittedAttackers();

            return _committedAttackers.Count < ComputeAttackerCapacity(damagePerHit, maxAttackers);
        }

        /// <summary>
        /// 지정 반경 안에서 가장 가까운 적을 찾습니다. 무기가 근접 위협을 살필 때 씁니다.
        /// </summary>
        public Unit FindClosestEnemyWithin(float radius)
        {
            return _context != null ? _context.FindNearestEnemy(Position, _team, radius) : null;
        }

        /// <summary>예약을 놓습니다. 표적을 바꾸거나 죽을 때 호출합니다.</summary>
        public void ReleaseAttacker(Unit attacker)
        {
            if (attacker != null)
            {
                _committedAttackers.Remove(attacker);
            }
        }

        /// <summary>
        /// 동시에 붙을 수 있는 인원입니다. 지금 체력을 한 방 피해로 나눈 값입니다.
        /// </summary>
        private int ComputeAttackerCapacity(float damagePerHit, int maxAttackers)
        {
            if (damagePerHit <= 0.01f)
            {
                return Mathf.Max(1, maxAttackers);
            }

            int needed = Mathf.CeilToInt(_health / damagePerHit);
            return Mathf.Clamp(needed, 1, Mathf.Max(1, maxAttackers));
        }

        /// <summary>죽었거나 사라진 예약자를 정리합니다.</summary>
        private void PruneCommittedAttackers()
        {
            _committedAttackers.RemoveWhere(attacker => attacker == null || !attacker.IsAlive);
        }

        // ====================================================================================================
        // 10. Private Methods - Setup
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

            _weapon.Initialize(this, _definition, _context.ProjectilePool, _context.Tuning);
        }

        // ====================================================================================================
        // 11. Private Methods - Decision
        // ====================================================================================================

        private void TickTimers(float deltaTime)
        {
            _attackTimer -= deltaTime;
            _staggerTimer -= deltaTime;
            _rootTimer -= deltaTime;
            _targetLockTimer -= deltaTime;
            _threatScanTimer -= deltaTime;

            _knockbackVelocity = Vector3.MoveTowards(
                _knockbackVelocity,
                Vector3.zero,
                KnockbackDecay * deltaTime);

            // 도약은 넉백보다 빨리 잦아듭니다. 한 걸음 파고드는 것이지 미끄러지는 것이 아닙니다.
            _lungeVelocity = Vector3.MoveTowards(
                _lungeVelocity,
                Vector3.zero,
                LungeDecay * deltaTime);
        }

        /// <summary>
        /// 교전 대상을 갱신합니다. 기존 대상이 죽거나 너무 멀어지면 새로 탐색합니다.
        /// </summary>
        private void AcquireTarget()
        {
            if (_target != null)
            {
                if (!_target.IsAlive)
                {
                    ClearTarget();
                }
                else
                {
                    // 히스테리시스를 두어 대상이 매 프레임 바뀌며 떨리는 것을 막습니다.
                    float sqrDistance = (_target.Position - Position).sqrMagnitude;
                    float dropRange = _definition.EngageRadius * 1.5f;
                    if (sqrDistance > dropRange * dropRange)
                    {
                        ClearTarget();
                    }
                }
            }

            // 표적 고정: 한 번 잡으면 잠시 바꾸지 않습니다.
            // 더 가까운 적이 나타날 때마다 시선을 돌리면 창끝이 흩어지고 방어선에 구멍이 납니다.
            if (_target != null && _targetLockTimer > 0f)
            {
                return;
            }

            if (_target != null)
            {
                return;
            }

            var found = _weapon != null && _weapon.UsesAttackQueue
                ? FindUnclaimedEnemy()
                : _context.FindNearestEnemy(Position, _team, _definition.EngageRadius);

            if (found == null)
            {
                return;
            }

            _target = found;
            _targetLockTimer = _weapon != null ? _weapon.TargetLockSeconds : 0f;
        }

        /// <summary>
        /// 아직 자리가 남은 적 중 가장 가까운 쪽을 고릅니다.
        ///
        /// <b>공격 대기열의 실행부입니다.</b> 다른 병사가 이미 맡은 적은 건너뛰고 다음으로 다가오는 적을 봅니다.
        /// 아무도 남지 않았으면(전부 자리가 찼으면) 가장 가까운 적으로 되돌아갑니다.
        /// 그러지 않으면 방어선 전체가 손을 놓고 서 있게 됩니다.
        /// </summary>
        private Unit FindUnclaimedEnemy()
        {
            var enemyTeam = _team == Team.Player ? Team.Enemy : Team.Player;
            int count = _context.QueryTeam(Position, _definition.EngageRadius, enemyTeam, null, _candidateBuffer);

            Unit best = null;
            float bestSqr = float.MaxValue;

            int maxAttackers = _context.Tuning.MaxSimultaneousAttackers;

            for (int i = 0; i < count; i++)
            {
                var candidate = _candidateBuffer[i];
                if (candidate == null || !candidate.IsAlive)
                {
                    continue;
                }

                // 이미 정원이 찬 적은 건너뜁니다. 예약은 실제로 공격을 시작할 때 잡습니다.
                if (!candidate.HasRoomForAttacker(this, _definition.AttackDamage, maxAttackers))
                {
                    continue;
                }

                float sqr = (candidate.Position - Position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }

            // 전부 찼으면 평소대로 가장 가까운 적을 봅니다.
            return best ?? _context.FindNearestEnemy(Position, _team, _definition.EngageRadius);
        }

        /// <summary>표적을 놓고 예약도 함께 반납합니다.</summary>
        private void ClearTarget()
        {
            _target?.ReleaseAttacker(this);
            _target = null;
            _targetLockTimer = 0f;
        }

        /// <summary>
        /// 이번 프레임의 목표 속도를 계산합니다. 도착 조향과 분리 조향의 합입니다.
        /// </summary>
        private Vector3 ComputeSteering()
        {
            Vector3 position = Position;
            Vector3 destination = _slotTarget;

            float slotDistance = Vector3.Distance(
                new Vector3(position.x, 0f, position.z),
                new Vector3(_slotTarget.x, 0f, _slotTarget.z));

            bool atSlot = slotDistance <= SlotArrivalThreshold;

            // 진형 붕괴: 품 안을 내준 무기는 싸울 방법이 없습니다. 물러나는 것이 유일한 반응입니다.
            if (_weapon != null && _weapon.TryGetRetreatFrom(out Vector3 threat))
            {
                Vector3 away = position - threat;
                away.y = 0f;

                if (away.sqrMagnitude > 0.0001f)
                {
                    return away.normalized * _definition.MoveSpeed;
                }
            }

            // 얼마나 나설 수 있는지는 무기가 정합니다. 창병과 궁수는 0이라 자리를 지킵니다.
            float leash = GetFormationBreakDistance();

            // 대열을 풀 수 있는 병과는 <b>행군 중에도</b> 눈앞의 적에게 붙습니다.
            // 자리를 잡은 뒤에만 나서게 하면 행군로에 선 적을 그냥 지나쳐 가 버립니다.
            bool mayEngage = atSlot || leash > 0f;

            if (mayEngage && _target != null && _target.IsAlive)
            {
                // 리시는 자기 자리(행군 중이면 앵커)에서 잽니다.
                // 분대가 지나가 버리면 거리가 벌어져 자연히 교전을 접고 따라붙습니다.
                // 별도의 "복귀" 규칙 없이 이 한 줄이 이탈 시간을 스스로 제한합니다.
                bool withinLeash = leash > 0f
                                   && Vector3.Distance(_target.Position, _slotTarget) <= leash;

                float targetDistance = Vector3.Distance(position, _target.Position);

                if (targetDistance <= _definition.AttackRange)
                {
                    // 사거리 안입니다. 자리를 지키는 병과이거나 아직 리시 안이면 멈춰 싸웁니다.
                    if (atSlot || withinLeash)
                    {
                        destination = position;
                    }
                }
                else if (withinLeash)
                {
                    destination = _target.Position;
                }
            }

            Vector3 velocity = SteeringSolver.Arrive(position, destination, _definition.MoveSpeed, ArriveSlowRadius);

            // 아군끼리 겹치지 않도록 밀어냅니다.
            float separationRadius = _definition.Radius * 2.4f;
            velocity += ComputeSeparation(position, separationRadius, _team, this, SeparationWeight);

            // 적과도 밀어냅니다. 다만 <b>약하게</b>입니다.
            //
            // 안 밀어내면 난전에서 몸이 그대로 겹쳐 어느 쪽이 어디 있는지 알 수 없게 됩니다.
            // 그렇다고 아군만큼 세게 밀면 서로 다가가지 못해 영영 칼이 닿지 않습니다.
            // 부딪히되 뚫고 지나가지는 않는 정도가 필요합니다.
            var enemyTeam = _team == Team.Player ? Team.Enemy : Team.Player;
            velocity += ComputeSeparation(
                position,
                separationRadius,
                enemyTeam,
                null,
                _context.Tuning.EnemySeparationWeight);

            // 최대 속도를 넘지 않게 제한합니다.
            float maxSpeed = _definition.MoveSpeed;
            if (velocity.sqrMagnitude > maxSpeed * maxSpeed)
            {
                velocity = velocity.normalized * maxSpeed;
            }

            return velocity;
        }

        /// <summary>
        /// 방패를 든 병사가 몸을 돌려야 할 위협 방향입니다.
        ///
        /// 방패는 정면만 막으므로, 서 있는 동안 어디를 보느냐가 곧 방어력입니다.
        /// 뒤에서 날아오는 화살에 등을 보이고 있으면 저항이 통째로 무의미해집니다.
        ///
        /// 교전 반경보다 넓게 봅니다. 궁수는 사거리 밖에서 쏘므로,
        /// 교전 반경만 보면 정작 나를 쏘는 상대를 영영 인지하지 못합니다.
        /// 매 프레임 질의하지 않고 <see cref="ThreatScanInterval"/>마다 한 번만 갱신합니다.
        /// </summary>
        private bool TryGetThreatFacing(Vector3 position, out Vector3 facing)
        {
            facing = Vector3.zero;

            if (_definition.ProjectileResistance <= 0f || _context == null)
            {
                return false;
            }

            if (_threatScanTimer <= 0f)
            {
                _threatScanTimer = ThreatScanInterval;

                float radius = _context.Tuning.ShieldThreatRadius;
                _threatSource = radius > 0f
                    ? _context.FindNearestEnemy(position, _team, radius)
                    : null;
            }

            if (_threatSource == null || !_threatSource.IsAlive)
            {
                return false;
            }

            facing = _threatSource.Position - position;
            return facing.sqrMagnitude > 0.0001f;
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

        /// <summary>
        /// 지정 진영의 이웃에게서 밀려나는 속도를 구합니다.
        /// </summary>
        private Vector3 ComputeSeparation(Vector3 position, float radius, Team team, Unit exclude, float weight)
        {
            if (weight <= 0f)
            {
                return Vector3.zero;
            }

            int count = _context.QueryTeam(position, radius, team, exclude, _neighborBuffer);
            if (count == 0)
            {
                return Vector3.zero;
            }

            Vector3 separation = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                separation += SteeringSolver.SeparationFrom(position, _neighborBuffer[i].Position, radius);
            }

            return separation * (_definition.MoveSpeed * weight);
        }

        // ====================================================================================================
        // 12. Private Methods - Combat
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

            if (_weapon.IsBusy || _staggerTimer > 0f || _attackTimer > 0f)
            {
                return;
            }

            if (_target == null || !_target.IsAlive)
            {
                return;
            }

            // 창병 규칙: 이동 중에는 공격하지 않습니다. 자리를 먼저 잡도록 강제하는 장치입니다.
            if (IsMoving && !_definition.CanAttackWhileMoving)
            {
                return;
            }

            if (Vector3.Distance(Position, _target.Position) > _definition.AttackRange)
            {
                return;
            }

            // 공격 대기열: 찌르기 직전에 자리를 예약합니다.
            // 자리가 없으면 이번 공격을 보류합니다. 다음 표적 탐색에서 다른 적으로 옮겨 갑니다.
            if (_weapon.UsesAttackQueue &&
                !_target.TryCommitAttacker(this, _definition.AttackDamage, _context.Tuning.MaxSimultaneousAttackers))
            {
                // 고정이 풀리면 다른 적을 보게 됩니다.
                _targetLockTimer = 0f;
                return;
            }

            if (_weapon.TryBeginAttack(_target))
            {
                _attackTimer = _definition.AttackInterval;
                _rootTimer = _weapon.RootDuration;
            }
        }

        // ====================================================================================================
        // 13. Private Methods - Movement
        // ====================================================================================================

        /// <summary>
        /// 조향과 넉백을 합쳐 실제로 이동시키고, 지면 높이에 맞춰 스냅합니다.
        /// </summary>
        private void ApplyMovement(Vector3 steering, float deltaTime)
        {
            _lastVelocity = steering;

            Vector3 total = steering + _knockbackVelocity + _lungeVelocity;
            Vector3 position = Position;
            Vector3 next = position + total * deltaTime;

            if (!ResolveGround(position, ref next))
            {
                return; // 익사 처리됨
            }

            _transform.position = next;
            UpdateFacing(next, steering, deltaTime);
        }

        /// <summary>
        /// 도착 지점의 지형을 판정합니다.
        ///
        /// 평상시에는 갈 수 없는 곳으로 밀려나지 않습니다.
        /// 그러나 <b>넉백 중에는 물 위로 밀려날 수 있고, 그러면 익사합니다.</b>
        /// 이 예외가 없으면 넉백은 그냥 밀치기 연출이 되고, 물이 위험 요소가 되지 않습니다.
        /// </summary>
        /// <returns>계속 진행하면 true, 익사해 처리가 끝났으면 false입니다.</returns>
        private bool ResolveGround(Vector3 position, ref Vector3 next)
        {
            var grid = _context.Grid;
            var nextTile = grid.GetTile(grid.WorldToCoord(next));

            bool isWater = nextTile == null || nextTile.IsWater;
            bool beingKnockedBack = _knockbackVelocity.sqrMagnitude > DrownKnockbackThreshold * DrownKnockbackThreshold;

            if (isWater)
            {
                if (beingKnockedBack)
                {
                    Drown(next);
                    return false;
                }

                // 스스로는 물에 들어가지 않습니다.
                next = position;
                return true;
            }

            if (!nextTile.IsWalkable)
            {
                // 절벽·바위는 넉백으로도 통과할 수 없습니다.
                next = position;
                return true;
            }

            next.y = nextTile.WorldCenter.y;
            return true;
        }

        /// <summary>
        /// 물에 빠져 죽습니다.
        /// </summary>
        private void Drown(Vector3 splashPosition)
        {
            _transform.position = new Vector3(splashPosition.x, 0f, splashPosition.z);
            Kill();
        }

        /// <summary>
        /// 바라보는 방향을 갱신합니다.
        /// 교전 중에는 더 빠르게 돕니다. 무기 판정이 정면 기준이라 늦게 돌면 헛칩니다.
        /// </summary>
        private void UpdateFacing(Vector3 position, Vector3 steering, float deltaTime)
        {
            bool hasTarget = _target != null && _target.IsAlive;

            Vector3 facing;

            if (hasTarget)
            {
                // 무기가 겨눌 자리를 따로 가지고 있으면 그쪽을 봅니다.
                // 창은 적이 지금 있는 곳이 아니라 <b>올 자리</b>를 미리 겨눕니다.
                Vector3 lookTarget = _weapon != null && _weapon.TryGetAimPoint(_target, out var aimPoint)
                    ? aimPoint
                    : _target.Position;

                facing = lookTarget - position;
            }
            else
            {
                // 교전 대상이 없어도, 방패를 든 채 서 있는 병사는 위협 쪽으로 몸을 돌립니다.
                // 방패는 정면만 막으므로 어디를 보고 서 있느냐가 곧 방어력입니다.
                facing = TryGetThreatFacing(position, out var threatFacing) ? threatFacing : steering;
            }

            facing.y = 0f;

            if (facing.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            float targetYaw = Quaternion.LookRotation(facing.normalized, Vector3.up).eulerAngles.y;

            // 무게가 있는 무기는 스프링으로 돕니다.
            // 보간은 언제나 목표를 향해 곧바로 줄어들어 관성이 없습니다.
            // 그래서 표적을 옮길 때마다 창이 로봇처럼 휙 꺾입니다.
            if (_weapon != null && _weapon.UsesSpringTurn)
            {
                float yaw = SpringDamper.StepAngle(
                    _transform.eulerAngles.y,
                    targetYaw,
                    ref _yawVelocity,
                    _weapon.TurnSpringFrequency,
                    _weapon.TurnSpringDamping,
                    deltaTime);

                _transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                return;
            }

            float turnSpeed = hasTarget ? CombatTurnSpeed : TurnSpeed;

            _transform.rotation = Quaternion.Slerp(
                _transform.rotation,
                Quaternion.LookRotation(facing.normalized, Vector3.up),
                turnSpeed * deltaTime);
        }
    }
}
