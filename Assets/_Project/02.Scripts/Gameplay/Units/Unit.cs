using System;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Weapons;
using SRPG.Systems.Combat;
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

        private readonly UnitTargeting _targeting = new UnitTargeting();
        private readonly UnitLocomotion _locomotion = new UnitLocomotion();
        private readonly UnitFacing _facing = new UnitFacing();
        private readonly AttackerSlots _attackerSlots = new AttackerSlots();

        private UnitDefinition _definition;
        private IUnitContext _context;
        private Team _team;
        private WeaponBase _weapon;
        private Transform _transform;

        private float _health;
        private float _attackTimer;
        private float _staggerTimer;
        private float _rootTimer;

        /// <summary>분대(또는 적 AI)가 배정한 진형 슬롯입니다.</summary>
        private Vector3 _slotTarget;

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

        // ====================================================================================================
        // 6. Unity Lifecycle
        // ====================================================================================================

        private void Awake()
        {
            _transform = transform;
        }

        /// <summary>
        /// 한 프레임의 판단 순서입니다. <b>이 순서 자체가 규칙입니다.</b>
        ///
        /// 표적을 먼저 정해야 어디로 갈지가 나오고, 이동 여부가 정해져야 창병이 찌를지가 나옵니다.
        /// 그래서 <see cref="IsMoving"/>은 조향 다음, 전투 앞에서 <b>한 번만</b> 확정됩니다.
        /// </summary>
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

            _targeting.Refresh(
                _weapon != null && _weapon.UsesAttackQueue,
                _weapon != null ? _weapon.TargetLockSeconds : 0f,
                _context.Tuning.MaxSimultaneousAttackers);

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

        private void OnDestroy()
        {
            _context?.Unregister(this);
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
        public void Initialize(UnitDefinition definition, Team team, IUnitContext context, bool isCommander = false)
        {
            _transform = transform;
            _definition = definition;
            _team = team;
            _context = context;
            IsCommander = isCommander;

            _health = definition.MaxHealth;
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

        /// <summary>
        /// 교전 대상이 없을 때 바라볼 방향을 분대가 지정합니다.
        /// </summary>
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
        public bool HasRoomForAttacker(Unit attacker, float damagePerHit, int maxAttackers)
        {
            if (!IsAlive)
            {
                return false;
            }

            return _attackerSlots.HasRoom(attacker, _health, damagePerHit, maxAttackers);
        }

        /// <summary>예약을 놓습니다. 표적을 바꾸거나 죽을 때 호출합니다.</summary>
        public void ReleaseAttacker(Unit attacker)
        {
            _attackerSlots.Release(attacker);
        }

        /// <summary>
        /// 지정 반경 안에서 가장 가까운 적을 찾습니다. 무기가 근접 위협을 살필 때 씁니다.
        /// </summary>
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

            _weapon.Initialize(this, _definition, _context.ProjectilePool, _context.Tuning);
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
                _definition.AttackRange));

            if (blocked != AttackBlock.None)
            {
                return;
            }

            // 공격 대기열: 찌르기 직전에 자리를 예약합니다.
            // 자리가 없으면 이번 공격을 보류합니다.
            if (_weapon.UsesAttackQueue &&
                !target.TryCommitAttacker(this, _definition.AttackDamage, _context.Tuning.MaxSimultaneousAttackers))
            {
                _targeting.BreakLock();
                return;
            }

            if (_weapon.TryBeginAttack(target))
            {
                _attackTimer = _definition.AttackInterval;
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
        /// 물에 빠져 죽습니다. 낙수 지점은 이미 수면 높이로 맞춰져 있습니다.
        /// </summary>
        private void Drown(Vector3 splashPosition)
        {
            _transform.position = splashPosition;
            Kill();
        }
    }
}
