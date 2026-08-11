using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using UnityEngine;

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

        /// <summary>
        /// 표적을 다시 고르는 최소 주기(초)입니다.
        ///
        /// <b>고정 시간이 0인 무기(검·활)도 이 주기로만 재평가합니다.</b>
        /// 매 프레임 다시 고르면 두 가지가 나빠집니다 —
        /// 살아 있는 표적을 든 병사마다 <b>프레임당 공간 질의가 하나씩 늘고</b>,
        /// 거리가 엇비슷한 두 적 사이에서 표적이 프레임 단위로 뒤집힙니다.
        ///
        /// 재평가가 늦어서 손해 보는 것은 "옆에 더 가까운 적이 왔는데 0.35초 늦게 안다" 정도이고,
        /// 그건 교전에서 눈에 띄지 않습니다.
        /// </summary>
        private const float RetargetInterval = 0.35f;

        /// <summary>
        /// 표적을 갈아탈 만하다고 보는 거리 비율입니다. 새 후보가 지금 표적보다 이만큼은 가까워야 옮깁니다.
        ///
        /// 마진이 없으면 나란히 선 두 적 사이에서 재평가 때마다 표적이 뒤집힙니다.
        /// 적 분대가 목표를 바꿀 때 <c>EnemyGoalSwitchMargin</c>을 두는 것과 같은 이유입니다 —
        /// 조금만 뒤집혀도 방향을 트는 판단은 갈팡질팡하는 것으로 보입니다.
        /// </summary>
        private const float RetargetDistanceMargin = 0.8f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        /// <summary>대기열을 쓰는 무기가 후보를 훑을 때 쓰는 재사용 버퍼입니다.</summary>
        private readonly List<Unit> _candidateBuffer = new List<Unit>(16);

        /// <summary>이 표적 판단을 소유한 병사입니다.</summary>
        private Unit _owner;
        /// <summary>주변 유닛을 찾는 공간 질의입니다.</summary>
        private ISpatialQuery _spatial;
        /// <summary>병과 정의입니다. 교전 반경과 피해량을 읽습니다.</summary>
        private UnitDefinition _definition;

        /// <summary>현재 교전 대상입니다.</summary>
        private Unit _target;
        /// <summary>표적을 다시 고르기까지 남은 시간입니다.</summary>
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
        /// <param name="owner">이 표적 판단을 소유한 병사입니다.</param>
        /// <param name="spatial">주변 유닛을 찾는 공간 질의입니다.</param>
        /// <param name="definition">병과 정의입니다. 교전 반경과 피해량을 읽습니다.</param>
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
        /// <param name="deltaTime">지난 시간입니다. 표적 고정 시간을 줄이는 데 씁니다.</param>
        public void Tick(float deltaTime)
        {
            _lockTimer -= deltaTime;
        }

        /// <summary>
        /// 표적을 갱신합니다. 죽거나 멀어진 대상을 놓고, 고정이 풀렸으면 <b>다시 고릅니다.</b>
        ///
        /// <b>한 프레임의 판단 순서</b>
        ///
        ///   1. 쓸 수 없게 된 표적을 놓는다 (사망·이탈)
        ///   2. 고정 시간이 남았으면 아무것도 다시 보지 않는다
        ///   3. 표적이 있으면 <b>재평가</b>한다 — 갈아탈 만한 상대가 있는가
        ///   4. 표적이 없으면 새로 잡는다
        ///
        /// 3번이 오래 비어 있었습니다. 표적이 있으면 무조건 유지하는 코드가 남아 있어서
        /// 고정 타이머가 실제로 막는 것이 없었고, 대기열이 꽉 찼을 때
        /// <see cref="BreakLock"/>으로 다음 적을 겨누는 경로도 함께 죽어 있었습니다.
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

            DropUnusableTarget();

            // 표적 고정: 한 번 잡으면 잠시 바꾸지 않습니다.
            // 더 가까운 적이 나타날 때마다 시선을 돌리면 창끝이 흩어지고 방어선에 구멍이 납니다.
            if (_target != null && _lockTimer > 0f)
            {
                return;
            }

            if (_target != null)
            {
                Reconsider(usesAttackQueue, targetLockSeconds, maxAttackers);
                return;
            }

            var found = FindCandidate(usesAttackQueue, maxAttackers);

            if (found == null)
            {
                return;
            }

            _target = found;
            ArmLock(targetLockSeconds);
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
        ///
        /// 다음 <see cref="Refresh"/>가 곧바로 재평가에 들어가고, 그 자리에서
        /// <b>자리가 없는 표적은 거리를 따지지 않고 놓습니다.</b>
        /// 이 한 줄이 "앞의 한 명에게 전원이 달라붙어 뒤가 뚫리는" 것을 막는 실행부입니다.
        /// </summary>
        public void BreakLock()
        {
            _lockTimer = 0f;
        }

        // ====================================================================================================
        // 7. Private Methods - Retarget
        // ====================================================================================================

        /// <summary>
        /// 쓸 수 없게 된 표적을 놓습니다. 쓰러졌거나 교전 반경 밖으로 멀어진 경우입니다.
        /// </summary>
        private void DropUnusableTarget()
        {
            if (_target == null)
            {
                return;
            }

            if (!_target.IsAlive)
            {
                Clear();
                return;
            }

            // 히스테리시스를 두어 대상이 매 프레임 바뀌며 떨리는 것을 막습니다.
            float dropRange = _owner.Stats.EngageRadius * DropRangeFactor;

            if ((_target.Position - _owner.Position).sqrMagnitude > dropRange * dropRange)
            {
                Clear();
            }
        }

        /// <summary>
        /// 지금 표적을 그대로 둘지, 더 나은 상대로 옮길지 정합니다.
        ///
        /// <b>옮기는 조건은 둘입니다.</b>
        ///
        ///   · 지금 표적에 <b>내 자리가 없다</b> — 거리를 따지지 않고 옮깁니다
        ///   · 새 후보가 <b>눈에 띄게 가깝다</b> — 마진을 넘겨야 옮깁니다
        ///
        /// 첫째가 오버킬 방지의 본체입니다. 자리가 찬 적을 계속 겨누고 서 있으면
        /// 그가 쓰러진 자리로 뒤따라오던 적들이 그대로 통과합니다.
        /// 둘째가 없으면 나란히 선 두 적 사이에서 표적이 계속 뒤집힙니다.
        /// </summary>
        private void Reconsider(bool usesAttackQueue, float targetLockSeconds, int maxAttackers)
        {
            var current = _target;

            bool currentHasRoom = !usesAttackQueue ||
                                  current.HasRoomForAttacker(_owner, _owner.Stats.AttackDamage, maxAttackers);

            var found = FindCandidate(usesAttackQueue, maxAttackers);

            // 대안이 없거나 지금 표적이 그대로 최선이면 유지합니다.
            // 다음 재평가까지의 시간을 다시 채워, 이 질의가 매 프레임 반복되지 않게 합니다.
            if (found == null || found == current)
            {
                ArmLock(targetLockSeconds);
                return;
            }

            if (currentHasRoom && !IsMeaningfullyCloser(found, current))
            {
                ArmLock(targetLockSeconds);
                return;
            }

            // <b>옮기기 전에 예약을 반드시 놓습니다.</b>
            // 빠뜨리면 떠나온 표적의 정원이 한 자리 줄어든 채로 영영 남고,
            // 그 적에게는 실제 인원보다 적은 수만 달라붙게 됩니다.
            current.ReleaseAttacker(_owner);

            _target = found;
            ArmLock(targetLockSeconds);
        }

        /// <summary>이번 재평가에서 고를 수 있는 최선의 상대입니다.</summary>
        private Unit FindCandidate(bool usesAttackQueue, int maxAttackers)
        {
            return usesAttackQueue
                ? FindUnclaimedEnemy(maxAttackers)
                : _spatial.FindNearestEnemy(_owner.Position, _owner.Team, _owner.Stats.EngageRadius);
        }

        /// <summary>새 후보가 지금 표적보다 마진을 넘겨 가까운지 봅니다.</summary>
        private bool IsMeaningfullyCloser(Unit candidate, Unit current)
        {
            float candidateSqr = (candidate.Position - _owner.Position).sqrMagnitude;
            float currentSqr = (current.Position - _owner.Position).sqrMagnitude;

            return candidateSqr < currentSqr * (RetargetDistanceMargin * RetargetDistanceMargin);
        }

        /// <summary>
        /// 다음 재평가까지의 시간을 채웁니다.
        ///
        /// 무기가 요구하는 고정 시간과 최소 재평가 주기 중 <b>긴 쪽</b>을 씁니다.
        /// 고정을 쓰지 않는 무기(0)도 최소 주기는 지켜야 질의가 프레임마다 늘지 않습니다.
        /// </summary>
        private void ArmLock(float targetLockSeconds)
        {
            _lockTimer = Mathf.Max(targetLockSeconds, RetargetInterval);
        }

        // ====================================================================================================
        // 8. Private Methods - Search
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
                _owner.Position, _owner.Stats.EngageRadius, enemyTeam, null, _candidateBuffer);

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
                if (!candidate.HasRoomForAttacker(_owner, _owner.Stats.AttackDamage, maxAttackers))
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
            return best ?? _spatial.FindNearestEnemy(_owner.Position, _owner.Team, _owner.Stats.EngageRadius);
        }
    }
}
