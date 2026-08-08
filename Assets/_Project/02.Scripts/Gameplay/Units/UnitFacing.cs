using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Weapons;
using SRPG.Systems.Combat;
using UnityEngine;

namespace SRPG.Gameplay.Units
{
    /// <summary>
    /// 병사가 <b>어디를 보고 있는지</b>를 담당합니다.
    ///
    /// <b>왜 이것만으로 한 덩이가 되는가</b>
    ///
    /// 이 게임에서 시선은 연출이 아니라 규칙입니다.
    ///   · 무기 판정이 정면 기준이라, 늦게 돌면 헛칩니다
    ///   · 방패는 정면만 막으므로 <b>어디를 보고 서 있느냐가 곧 방어력</b>입니다
    ///   · 창은 무게가 있어 표적을 옮길 때 관성으로 지나쳤다 되돌아와야 합니다
    ///
    /// 그래서 "무엇을 볼지"(<see cref="FacingSolver"/>)와 "얼마나 빨리 도는지"(스프링 대 보간)가
    /// 각각 판단이고, 둘 다 이동이나 전투와는 다른 축입니다.
    /// </summary>
    public sealed class UnitFacing
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>평상시 회전 속도입니다.</summary>
        public const float TurnSpeed = 12f;

        /// <summary>교전 중 회전 속도입니다. 무기 판정이 정면 기준이라 빠르게 돌아야 합니다.</summary>
        public const float CombatTurnSpeed = 22f;

        /// <summary>방패병이 위협 방향을 다시 살피는 주기(초)입니다. 매 프레임 질의하지 않기 위한 것입니다.</summary>
        public const float ThreatScanInterval = 0.3f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private Unit _owner;
        private Transform _transform;
        private ISpatialQuery _spatial;
        private BattleTuning _tuning;
        private UnitDefinition _definition;

        /// <summary>방패병이 몸을 돌릴 위협 대상입니다. 공격 대상과는 별개입니다.</summary>
        private Unit _threatSource;
        private float _threatScanTimer;

        /// <summary>분대가 지정한 대기 방향입니다. 교전 대상이 없을 때 이쪽을 봅니다.</summary>
        private Vector3 _idleFacing;
        private bool _hasIdleFacing;

        /// <summary>회전 스프링의 각속도입니다. 무기가 무게를 가질 때만 씁니다.</summary>
        private float _yawVelocity;

        // ====================================================================================================
        // 3. Public Methods - Setup
        // ====================================================================================================

        /// <summary>
        /// 필요한 것을 연결합니다. 유닛 초기화 때 부릅니다.
        ///
        /// 위협을 찾는 공간 질의와, 얼마나 넓게 살필지를 정하는 튜닝뿐입니다.
        /// </summary>
        public void Configure(
            Unit owner,
            Transform ownerTransform,
            ISpatialQuery spatial,
            BattleTuning tuning,
            UnitDefinition definition)
        {
            _owner = owner;
            _transform = ownerTransform;
            _spatial = spatial;
            _tuning = tuning;
            _definition = definition;

            _threatSource = null;
            _threatScanTimer = 0f;
            _yawVelocity = 0f;
        }

        // ====================================================================================================
        // 4. Public Methods - Orders
        // ====================================================================================================

        /// <summary>
        /// 교전 대상이 없을 때 바라볼 방향을 분대가 지정합니다.
        ///
        /// 이게 없으면 대기 중인 병사는 <b>마지막으로 걷던 방향</b>을 그대로 보고 서 있습니다.
        /// 분대가 해안을 등지고 도착하면 전원이 섬 안쪽을 보고 선 채로 상륙을 맞이합니다.
        /// </summary>
        public void SetIdleFacing(Vector3 direction)
        {
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.0001f)
            {
                _idleFacing = direction.normalized;
                _hasIdleFacing = true;
            }
        }

        // ====================================================================================================
        // 5. Public Methods - Tick
        // ====================================================================================================

        /// <summary>시간을 흘려보냅니다.</summary>
        public void Tick(float deltaTime)
        {
            _threatScanTimer -= deltaTime;
        }

        /// <summary>
        /// 바라보는 방향을 갱신합니다.
        /// </summary>
        /// <param name="position">이번 프레임에 확정된 위치입니다.</param>
        /// <param name="steering">이번 프레임의 조향 속도입니다.</param>
        /// <param name="isMoving">스스로 이동 중인지 여부입니다.</param>
        /// <param name="target">교전 대상입니다. 없으면 null입니다.</param>
        /// <param name="weapon">겨눌 자리와 회전 감각을 정하는 무기입니다.</param>
        /// <param name="deltaTime">경과 시간입니다.</param>
        public void Apply(
            Vector3 position, Vector3 steering, bool isMoving, Unit target, WeaponBase weapon, float deltaTime)
        {
            if (_transform == null)
            {
                return;
            }

            bool hasTarget = target != null && target.IsAlive;

            // 무기가 겨눌 자리를 따로 가지고 있으면 그쪽을 봅니다.
            // 창은 적이 지금 있는 곳이 아니라 올 자리를 미리 겨눕니다.
            Vector3 aimPoint = Vector3.zero;
            if (hasTarget)
            {
                aimPoint = weapon != null && weapon.TryGetAimPoint(target, out var weaponAim)
                    ? weaponAim
                    : target.Position;
            }

            // 위협 탐색은 표적이 없을 때만 합니다.
            // 싸우는 중에도 살피게 하면 방패병마다 주기적인 공간 질의가 하나씩 더 붙는데,
            // 정작 그 결과는 표적에 밀려 쓰이지 않습니다.
            Vector3 threatPosition = Vector3.zero;
            bool hasThreat = !hasTarget && TryScanThreat(position, out threatPosition);

            var request = new FacingRequest(
                position,
                hasTarget,
                aimPoint,
                hasThreat,
                threatPosition,
                isMoving,
                steering,
                _hasIdleFacing,
                _idleFacing);

            if (FacingSolver.Resolve(request, out Vector3 facing) == FacingSource.None)
            {
                return;
            }

            Rotate(facing, hasTarget, weapon, deltaTime);
        }

        // ====================================================================================================
        // 6. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 방패를 든 병사가 몸을 돌려야 할 위협을 찾습니다.
        ///
        /// 교전 반경보다 넓게 봅니다. 궁수는 사거리 밖에서 쏘므로,
        /// 교전 반경만 보면 정작 나를 쏘는 상대를 영영 인지하지 못합니다.
        /// 매 프레임 질의하지 않고 <see cref="ThreatScanInterval"/>마다 한 번만 갱신합니다.
        /// </summary>
        private bool TryScanThreat(Vector3 position, out Vector3 threatPosition)
        {
            threatPosition = Vector3.zero;

            if (_definition == null || _definition.ProjectileResistance <= 0f || _spatial == null)
            {
                return false;
            }

            if (_threatScanTimer <= 0f)
            {
                _threatScanTimer = ThreatScanInterval;

                float radius = _tuning.ShieldThreatRadius;
                _threatSource = radius > 0f
                    ? _spatial.FindNearestEnemy(position, _owner.Team, radius)
                    : null;
            }

            if (_threatSource == null || !_threatSource.IsAlive)
            {
                return false;
            }

            threatPosition = _threatSource.Position;
            return true;
        }

        /// <summary>
        /// 실제로 몸을 돌립니다.
        ///
        /// 무게가 있는 무기는 스프링으로 돕니다.
        /// 보간은 언제나 목표를 향해 곧바로 줄어들어 관성이 없습니다.
        /// 그래서 표적을 옮길 때마다 창이 로봇처럼 휙 꺾입니다.
        /// </summary>
        private void Rotate(Vector3 facing, bool hasTarget, WeaponBase weapon, float deltaTime)
        {
            var lookRotation = Quaternion.LookRotation(facing, Vector3.up);

            if (weapon != null && weapon.UsesSpringTurn)
            {
                float yaw = SpringDamper.StepAngle(
                    _transform.eulerAngles.y,
                    lookRotation.eulerAngles.y,
                    ref _yawVelocity,
                    weapon.TurnSpringFrequency,
                    weapon.TurnSpringDamping,
                    deltaTime);

                _transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                return;
            }

            float turnSpeed = hasTarget ? CombatTurnSpeed : TurnSpeed;

            _transform.rotation = Quaternion.Slerp(_transform.rotation, lookRotation, turnSpeed * deltaTime);
        }
    }
}
