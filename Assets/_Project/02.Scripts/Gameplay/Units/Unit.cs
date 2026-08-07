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

        /// <summary>이 속도 이상으로 밀려나는 중이면 물 위로도 밀려납니다(= 익사할 수 있습니다).</summary>
        private const float DrownKnockbackThreshold = 1.2f;

        /// <summary>평상시 회전 속도입니다.</summary>
        private const float TurnSpeed = 12f;

        /// <summary>교전 중 회전 속도입니다. 무기 판정이 정면 기준이라 빠르게 돌아야 합니다.</summary>
        private const float CombatTurnSpeed = 22f;

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
        private Transform _transform;

        private readonly List<Unit> _neighborBuffer = new List<Unit>(16);

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

            TickCombat(deltaTime, steering);
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

            ApplyKnockback(
                push.normalized * (hit.KnockbackForce * mitigation),
                hit.StaggerSeconds * mitigation);
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

            return ShieldSolver.ComputeBlockFactor(
                hit.Direction,
                _transform != null ? _transform.forward : transform.forward,
                _definition.ProjectileResistance,
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

            _weapon.Initialize(this, _definition, _context.ProjectilePool);
        }

        // ====================================================================================================
        // 11. Private Methods - Decision
        // ====================================================================================================

        private void TickTimers(float deltaTime)
        {
            _attackTimer -= deltaTime;
            _staggerTimer -= deltaTime;
            _rootTimer -= deltaTime;

            _knockbackVelocity = Vector3.MoveTowards(
                _knockbackVelocity,
                Vector3.zero,
                KnockbackDecay * deltaTime);
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
                    _target = null;
                }
                else
                {
                    // 히스테리시스를 두어 대상이 매 프레임 바뀌며 떨리는 것을 막습니다.
                    float sqrDistance = (_target.Position - Position).sqrMagnitude;
                    float dropRange = _definition.EngageRadius * 1.5f;
                    if (sqrDistance > dropRange * dropRange)
                    {
                        _target = null;
                    }
                }
            }

            if (_target == null)
            {
                _target = _context.FindNearestEnemy(Position, _team, _definition.EngageRadius);
            }
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

            // 슬롯에 도착했는데 적이 사거리 밖이면, 리시 범위 안에서만 접근합니다.
            if (atSlot && _target != null && _target.IsAlive)
            {
                float targetDistance = Vector3.Distance(position, _target.Position);
                if (targetDistance > _definition.AttackRange)
                {
                    float targetToSlot = Vector3.Distance(_target.Position, _slotTarget);
                    if (targetToSlot <= _context.Tuning.ChaseLeash)
                    {
                        destination = _target.Position;
                    }
                }
                else
                {
                    // 사거리 안이면 멈춰서 싸웁니다.
                    destination = position;
                }
            }

            Vector3 velocity = SteeringSolver.Arrive(position, destination, _definition.MoveSpeed, ArriveSlowRadius);

            // 아군끼리 겹치지 않도록 밀어냅니다.
            float separationRadius = _definition.Radius * 2.4f;
            int neighborCount = _context.QueryTeam(position, separationRadius, _team, this, _neighborBuffer);
            if (neighborCount > 0)
            {
                Vector3 separation = Vector3.zero;
                for (int i = 0; i < neighborCount; i++)
                {
                    separation += SteeringSolver.SeparationFrom(position, _neighborBuffer[i].Position, separationRadius);
                }

                velocity += separation * (_definition.MoveSpeed * SeparationWeight);
            }

            // 최대 속도를 넘지 않게 제한합니다.
            float maxSpeed = _definition.MoveSpeed;
            if (velocity.sqrMagnitude > maxSpeed * maxSpeed)
            {
                velocity = velocity.normalized * maxSpeed;
            }

            return velocity;
        }

        // ====================================================================================================
        // 12. Private Methods - Combat
        // ====================================================================================================

        /// <summary>
        /// 무기 동작을 진행시키고, 조건이 맞으면 새 공격을 시작합니다.
        /// 실제 타격 판정은 무기가 수행합니다.
        /// </summary>
        private void TickCombat(float deltaTime, Vector3 steering)
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
            bool isMoving = steering.magnitude > MovingSpeedThreshold;
            if (isMoving && !_definition.CanAttackWhileMoving)
            {
                return;
            }

            if (Vector3.Distance(Position, _target.Position) > _definition.AttackRange)
            {
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

            Vector3 total = steering + _knockbackVelocity;
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

            Vector3 facing = hasTarget ? _target.Position - position : steering;
            facing.y = 0f;

            if (facing.sqrMagnitude <= 0.0001f)
            {
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
