using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;

namespace SRPG.Gameplay.Units
{
    /// <summary>
    /// 병사 한 명의 <b>표적 상태</b>를 들고 있습니다. 누구를 보고 있는지, 언제까지 놓지 않을지.
    ///
    /// <b>왜 떼어 놓는가</b>
    ///
    /// 표적을 고르는 일에는 세 가지 규칙이 겹쳐 있습니다 —
    /// 히스테리시스(떨림 방지), 표적 고정(방어선 유지), 공격 대기열(오버킬 방지).
    /// 이 셋이 조향·넉백·회전과 같은 <c>Update</c> 안에 섞여 있으면
    /// "왜 저 창병이 엉뚱한 적을 보고 있는가"를 다른 모든 것과 함께 읽어야 합니다.
    ///
    /// MonoBehaviour가 아니므로 EditMode에서 유닛 몇 개만 세워 놓고 직접 검증할 수 있습니다.
    /// </summary>
    public sealed class UnitTargeting
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// 표적을 놓는 거리의 배수입니다. 교전 반경보다 <b>넓게</b> 잡아야 합니다.
        ///
        /// 잡는 거리와 놓는 거리가 같으면 경계에 선 적을 두고 매 프레임 잡았다 놓았다 합니다.
        /// </summary>
        private const float DropRangeFactor = 1.5f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly List<Unit> _candidateBuffer = new List<Unit>(16);

        private Unit _owner;
        private ISpatialQuery _spatial;
        private UnitDefinition _definition;

        private Unit _target;
        private float _lockTimer;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>현재 교전 대상입니다. 없으면 null입니다.</summary>
        public Unit Target => _target;

        /// <summary>살아 있는 교전 대상이 있는지 여부입니다.</summary>
        public bool HasLivingTarget => _target != null && _target.IsAlive;

        /// <summary>표적을 바꾸지 못하는 남은 시간입니다.</summary>
        public float LockRemaining => _lockTimer;

        // ====================================================================================================
        // 4. Public Methods - Setup
        // ====================================================================================================

        /// <summary>
        /// 판단에 필요한 것을 연결합니다. 유닛 초기화 때 한 번 부릅니다.
        ///
        /// <b>받는 것이 공간 질의 하나뿐입니다.</b>
        /// 표적을 고르는 데 필요한 것은 "내 주변에 누가 있는가"가 전부입니다.
        /// 지형도 경로 탐색기도 타일 점유도 여기서는 손댈 수 없습니다.
        /// </summary>
        public void Configure(Unit owner, ISpatialQuery spatial, UnitDefinition definition)
        {
            _owner = owner;
            _spatial = spatial;
            _definition = definition;

            _target = null;
            _lockTimer = 0f;
        }

        // ====================================================================================================
        // 5. Public Methods - Tick
        // ====================================================================================================

        /// <summary>시간을 흘려보냅니다.</summary>
        public void Tick(float deltaTime)
        {
            _lockTimer -= deltaTime;
        }

        /// <summary>
        /// 표적을 갱신합니다. 기존 대상이 죽거나 너무 멀어지면 새로 탐색합니다.
        /// </summary>
        /// <param name="usesAttackQueue">공격 대기열을 쓰는 무기인지 여부입니다.</param>
        /// <param name="targetLockSeconds">새로 잡은 표적을 놓지 않는 시간입니다.</param>
        /// <param name="maxAttackers">한 적에게 붙을 수 있는 인원의 상한입니다.</param>
        public void Refresh(bool usesAttackQueue, float targetLockSeconds, int maxAttackers)
        {
            if (_owner == null || _spatial == null || _definition == null)
            {
                return;
            }

            if (_target != null)
            {
                if (!_target.IsAlive)
                {
                    Clear();
                }
                else
                {
                    // 히스테리시스를 두어 대상이 매 프레임 바뀌며 떨리는 것을 막습니다.
                    float dropRange = _definition.EngageRadius * DropRangeFactor;

                    if ((_target.Position - _owner.Position).sqrMagnitude > dropRange * dropRange)
                    {
                        Clear();
                    }
                }
            }

            // 표적 고정: 한 번 잡으면 잠시 바꾸지 않습니다.
            // 더 가까운 적이 나타날 때마다 시선을 돌리면 창끝이 흩어지고 방어선에 구멍이 납니다.
            if (_target != null && _lockTimer > 0f)
            {
                return;
            }

            // 고정이 풀렸더라도 살아 있는 표적은 그대로 둡니다.
            //
            // ※ 위 조건과 합쳐 보면 결국 "표적이 있으면 언제나 유지"와 같습니다.
            //   즉 지금 구조에서 고정 타이머는 표적 교체를 막는 일을 실제로 하지 않고,
            //   대기열이 꽉 찼을 때 <see cref="BreakLock"/>으로 고정을 푸는 것도 효과가 없습니다.
            //   분해 과정에서 드러난 사실이지만, 이번 작업은 동작을 바꾸지 않는 것이 목적이라
            //   그대로 옮겨 둡니다. 표적 재평가를 실제로 켜는 것은 별도 변경입니다.
            if (_target != null)
            {
                return;
            }

            var found = usesAttackQueue
                ? FindUnclaimedEnemy(maxAttackers)
                : _spatial.FindNearestEnemy(_owner.Position, _owner.Team, _definition.EngageRadius);

            if (found == null)
            {
                return;
            }

            _target = found;
            _lockTimer = targetLockSeconds;
        }

        // ====================================================================================================
        // 6. Public Methods - Control
        // ====================================================================================================

        /// <summary>표적을 놓고 예약도 함께 반납합니다.</summary>
        public void Clear()
        {
            _target?.ReleaseAttacker(_owner);
            _target = null;
            _lockTimer = 0f;
        }

        /// <summary>
        /// 표적 고정을 즉시 풉니다. 대기열이 꽉 차 이번 표적을 칠 수 없을 때 부릅니다.
        /// </summary>
        public void BreakLock()
        {
            _lockTimer = 0f;
        }

        // ====================================================================================================
        // 7. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 아직 자리가 남은 적 중 가장 가까운 쪽을 고릅니다.
        ///
        /// <b>공격 대기열의 실행부입니다.</b> 다른 병사가 이미 맡은 적은 건너뛰고 다음으로 다가오는 적을 봅니다.
        /// 아무도 남지 않았으면(전부 자리가 찼으면) 가장 가까운 적으로 되돌아갑니다.
        /// 그러지 않으면 방어선 전체가 손을 놓고 서 있게 됩니다.
        /// </summary>
        private Unit FindUnclaimedEnemy(int maxAttackers)
        {
            var enemyTeam = _owner.Team == Team.Player ? Team.Enemy : Team.Player;

            int count = _spatial.QueryTeam(
                _owner.Position, _definition.EngageRadius, enemyTeam, null, _candidateBuffer);

            Unit best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var candidate = _candidateBuffer[i];
                if (candidate == null || !candidate.IsAlive)
                {
                    continue;
                }

                // 이미 정원이 찬 적은 건너뜁니다. 예약은 실제로 공격을 시작할 때 잡습니다.
                if (!candidate.HasRoomForAttacker(_owner, _definition.AttackDamage, maxAttackers))
                {
                    continue;
                }

                float sqr = (candidate.Position - _owner.Position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }

            // 전부 찼으면 평소대로 가장 가까운 적을 봅니다.
            return best ?? _spatial.FindNearestEnemy(_owner.Position, _owner.Team, _definition.EngageRadius);
        }
    }
}
