using SRPG.Common;
using SRPG.Gameplay.Units;
using UnityEngine;

namespace SRPG.Gameplay.Weapons
{
    /// <summary>
    /// 실제로 날아가는 화살입니다.
    ///
    /// 궁수는 사거리 안의 적에게 피해를 "전달"하지 않습니다. 화살을 쏘고, 그 화살이 무언가에 맞으면 피해가 납니다.
    /// 그래서 다음이 규칙 없이 자연히 성립합니다.
    ///   · 빗나간다 (그리고 빗나간 화살이 뒤에 선 다른 적에게 맞는다)
    ///   · 아군이나 지형이 사선을 막으면 못 맞힌다
    ///   · 날아가는 동안 대상이 움직이면 놓친다 → 그래서 예측 사격이 필요해진다
    ///
    /// <b>Rigidbody를 쓰지 않고 직접 적분합니다.</b>
    /// 화살은 작고 빠른 물체라 Rigidbody로는 프레임 사이를 건너뛰어 통과(터널링)합니다.
    /// 매 프레임 "직전 위치 → 현재 위치" 선분을 레이캐스트로 훑으면 아무리 빨라도 놓치지 않고,
    /// 충돌 대상도 우리가 원하는 마스크로 정확히 통제할 수 있습니다.
    /// 판정 자체는 실제 콜라이더에 대한 물리 질의입니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Arrow : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>화살이 사라지기까지의 최대 수명(초)입니다.</summary>
        private const float MaxLifetime = 6f;

        /// <summary>지형에 꽂힌 화살이 남아 있는 시간(초)입니다.</summary>
        private const float StuckLifetime = 2.5f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        /// <summary>이 화살을 쏜 병사입니다. 자기 자신은 맞지 않습니다.</summary>
        private Unit _shooter;
        /// <summary>쏜 쪽의 진영입니다. 아군 오사를 가릅니다.</summary>
        private Team _shooterTeam;
        /// <summary>현재 비행 속도입니다. 매 프레임 중력이 더해집니다.</summary>
        private Vector3 _velocity;
        /// <summary>이 화살에 적용할 중력 배율입니다.</summary>
        private float _gravityScale;
        /// <summary>명중했을 때 줄 피해량입니다. 감쇠 이전 값입니다.</summary>
        private float _damage;
        /// <summary>명중했을 때 밀어낼 세기입니다.</summary>
        private float _knockback;
        /// <summary>명중했을 때 줄 경직 시간입니다.</summary>
        private float _stagger;
        /// <summary>발사각입니다. 방패의 상방 판정 기준선이 여기서 파생됩니다.</summary>
        private float _arcAngleDegrees;

        /// <summary>이 화살이 들어가는 성질입니다. 발사 시점에 정해집니다.</summary>
        private DamageType _damageType = DamageType.Pierce;
        /// <summary>날아다닌 시간입니다. 너무 오래되면 스스로 회수됩니다.</summary>
        private float _lifetime;
        /// <summary>이미 어딘가에 박혔는지 여부입니다. 박힌 뒤에는 판정하지 않습니다.</summary>
        private bool _isStuck;

        /// <summary>회수될 풀입니다. 없으면 그냥 파괴됩니다.</summary>
        private ProjectilePool _pool;

        // ====================================================================================================
        // 2-1. Pooling
        // ====================================================================================================

        /// <summary>이 화살을 만들어 낸 프리팹입니다. 풀이 되돌릴 자리를 찾는 키입니다.</summary>
        internal GameObject PrefabKey { get; private set; }

        /// <summary>
        /// 이 화살을 풀에 묶습니다. 풀이 인스턴스를 만든 직후 한 번만 호출합니다.
        /// </summary>
        internal void BindPool(ProjectilePool pool, GameObject prefabKey)
        {
            _pool = pool;
            PrefabKey = prefabKey;
        }

        // ====================================================================================================
        // 3. Unity Lifecycle
        // ====================================================================================================

        private void Update()
        {
            float deltaTime = UnityEngine.Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            _lifetime -= deltaTime;
            if (_lifetime <= 0f)
            {
                Release();
                return;
            }

            if (_isStuck)
            {
                return;
            }

            Integrate(deltaTime);
        }

        // ====================================================================================================
        // 4. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 화살을 발사합니다.
        /// </summary>
        /// <param name="shooter">쏜 유닛입니다. 자기 팀은 맞지 않습니다.</param>
        /// <param name="launchVelocity">초기 속도입니다. 방향과 속력을 함께 담습니다.</param>
        /// <param name="damage">명중 시 피해량입니다.</param>
        /// <param name="knockback">명중 시 넉백 세기입니다.</param>
        /// <param name="stagger">명중 시 경직 시간입니다.</param>
        /// <param name="gravityScale">중력 배율입니다.</param>
        /// <param name="arcAngleDegrees">
        /// 이 화살을 쏜 발사각(도)입니다. 곡사는 대칭 포물선이라 평지에서는 이 값이 곧 하강각입니다.
        ///
        /// 화살 자신은 이 값을 쓰지 않고 <see cref="DamageInfo"/>에 실어 피격자에게 넘깁니다.
        /// 방패의 상방 판정 기준선이 여기서 파생되므로, 발사각을 바꾸면 기준도 함께 따라옵니다.
        /// 예전에는 이 기준이 화살 쪽 상수로 박혀 있어, 발사각만 바꾸면 조용히 어긋났습니다.
        /// </param>
        /// <param name="damageType">
        /// 이 화살이 입히는 피해의 종류입니다. 갑옷 상성이 여기서 갈립니다.
        /// 기본값이 <see cref="DamageType.Pierce"/> 인 것은 화살이 꿰뚫는 무기이기 때문입니다 —
        /// 중갑에 강하고 무갑에 약한 그 상성이 궁수의 성격입니다.
        /// </param>
        public void Launch(
            Unit shooter,
            Vector3 launchVelocity,
            float damage,
            float knockback,
            float stagger,
            float gravityScale,
            float arcAngleDegrees,
            DamageType damageType = DamageType.Pierce)
        {
            _shooter = shooter;
            _shooterTeam = shooter != null ? shooter.Team : Team.Player;
            _velocity = launchVelocity;
            _damage = damage;
            _knockback = knockback;
            _stagger = stagger;
            _gravityScale = gravityScale;
            _arcAngleDegrees = arcAngleDegrees;

            // 성질도 발사 시점에 붙잡아 둡니다. 피해·넉백과 같은 이유입니다 —
            // 화살이 날아가는 동안 쏜 사람이 쓰러질 수 있고, 그때 정의를 다시 물으면
            // 파괴된 오브젝트를 건드리게 됩니다.
            _damageType = damageType;
            _lifetime = MaxLifetime;
            _isStuck = false;

            AlignToVelocity();
        }

        // ====================================================================================================
        // 5. Private Methods - Motion
        // ====================================================================================================

        /// <summary>
        /// 한 프레임만큼 화살을 전진시키고, 지나간 선분을 물리 질의합니다.
        /// </summary>
        private void Integrate(float deltaTime)
        {
            Vector3 previous = transform.position;

            _velocity += Physics.gravity * (_gravityScale * deltaTime);

            Vector3 next = previous + _velocity * deltaTime;
            Vector3 delta = next - previous;
            float distance = delta.magnitude;

            if (distance > 0.0001f &&
                Physics.Raycast(
                    previous,
                    delta / distance,
                    out var hit,
                    distance,
                    GameLayers.ProjectileHitMask,
                    QueryTriggerInteraction.Collide))
            {
                ResolveHit(hit, delta / distance);
                return;
            }

            transform.position = next;
            AlignToVelocity();
        }

        private void AlignToVelocity()
        {
            if (_velocity.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(_velocity.normalized, Vector3.up);
            }
        }

        // ====================================================================================================
        // 6. Private Methods - Hit Resolution
        // ====================================================================================================

        private void ResolveHit(RaycastHit hit, Vector3 direction)
        {
            var victim = UnitLookup.FromCollider(hit.collider);

            if (victim != null)
            {
                // 자기 팀과 자기 자신은 통과시킵니다.
                // 아군 오사까지 넣으면 궁수가 사실상 못 쏘게 되어 병과가 성립하지 않습니다.
                if (victim == _shooter || victim.Team == _shooterTeam || !victim.IsAlive)
                {
                    transform.position = hit.point;
                    return;
                }

                ApplyHitToUnit(victim, direction);
                Release();
                return;
            }

            StickToTerrain(hit);
        }

        /// <summary>
        /// 화살의 쓸모가 끝났습니다. 풀이 있으면 돌려주고, 없으면 파괴합니다.
        ///
        /// 풀 없이도 동작해야 합니다. 무기 하나를 떼어 내 시험할 때 풀까지 준비해야 한다면
        /// 그 자체가 결합이고, 테스트가 번거로워집니다.
        /// </summary>
        private void Release()
        {
            if (_pool != null)
            {
                _pool.Return(this);
                return;
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// 유닛에게 타격을 전달합니다.
        ///
        /// <b>화살은 자기가 얼마나 막혔는지 계산하지 않습니다.</b>
        /// 어디서 어느 각도로 날아왔는지만 넘기고, 방패 판정은 피격자가 자기 방어 수단으로 수행합니다.
        /// 감쇠 규칙이 한 곳에만 있으면, 피해에는 감쇠를 곱하고 넉백에는 잊는 종류의 어긋남이 생기지 않습니다.
        /// </summary>
        private void ApplyHitToUnit(Unit victim, Vector3 direction)
        {
            victim.ReceiveHit(DamageInfo.Projectile(
                _damage,
                direction,
                _knockback,
                _stagger,
                _arcAngleDegrees,
                _shooter,
                _damageType));
        }

        /// <summary>
        /// 지형에 꽂힙니다. 잠시 남았다가 사라집니다.
        /// 어디로 쏟아졌는지가 눈에 남아야 궁수의 산포를 플레이어가 읽을 수 있습니다.
        /// </summary>
        private void StickToTerrain(RaycastHit hit)
        {
            transform.position = hit.point;
            _isStuck = true;
            _velocity = Vector3.zero;
            _lifetime = Mathf.Min(_lifetime, StuckLifetime);
        }
    }
}
