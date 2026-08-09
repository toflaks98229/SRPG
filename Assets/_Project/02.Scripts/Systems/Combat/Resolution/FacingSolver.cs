using UnityEngine;

namespace SRPG.Systems.Combat
{
    /// <summary>
    /// 병사가 어디를 보게 되었는지입니다.
    /// </summary>
    public enum FacingSource
    {
        /// <summary>돌릴 방향이 없습니다. 이번 프레임에는 회전하지 않습니다.</summary>
        None = 0,

        /// <summary>교전 대상(또는 무기가 겨눈 자리)을 봅니다.</summary>
        Target,

        /// <summary>방패를 든 채 위협 쪽으로 몸을 돌립니다.</summary>
        Threat,

        /// <summary>걷는 방향을 봅니다.</summary>
        Movement,

        /// <summary>분대가 지정한 대기 방향을 봅니다. 보통 해안선입니다.</summary>
        Idle,
    }

    /// <summary>
    /// 병사가 바라볼 방향을 정합니다.
    ///
    /// <b>왜 우선순위가 이 순서인가</b>
    ///
    ///   1. <b>교전 대상</b> — 무기 판정이 정면 기준이라, 보지 않는 쪽은 치지 못합니다
    ///   2. <b>위협</b> — 방패는 정면만 막습니다. 서 있는 동안 어디를 보느냐가 곧 방어력입니다
    ///   3. <b>진행 방향</b> — 걷는 사람은 가는 쪽을 봅니다
    ///   4. <b>대기 방향</b> — 분대가 지정합니다. 없으면 마지막 조향 방향을 씁니다
    ///
    /// 4번이 있어야 하는 이유가 있습니다. 이게 없으면 대기 중인 병사는 <b>마지막으로 걷던 방향</b>을
    /// 그대로 보고 서 있습니다. 분대가 해안을 등지고 도착하면 전원이 섬 안쪽을 보고 선 채로 상륙을 맞이합니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 판단이라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public static class FacingSolver
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>이보다 짧은 방향 벡터는 방향으로 치지 않습니다.</summary>
        private const float MinimumSquaredLength = 0.0001f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 바라볼 방향을 정합니다. <paramref name="facing"/>은 정규화된 수평 방향입니다.
        ///
        /// <b>대상과 위협의 처리가 일부러 다릅니다.</b>
        ///
        /// 겨눌 자리가 지금 서 있는 자리와 겹치면(=방향을 만들 수 없으면) <see cref="FacingSource.None"/>을
        /// 돌려주고 <b>위협으로 내려가지 않습니다.</b> 칼을 휘두르는 도중에 몸이 홱 돌아가는 것을 막기 위함입니다.
        /// 반면 위협이 겹쳐 있을 때는 다음 후보로 내려갑니다 — 그쪽은 "돌아볼 여유가 있는" 상태이기 때문입니다.
        /// </summary>
        /// <param name="request">판단에 필요한 관측값입니다.</param>
        /// <param name="facing">정규화된 수평 방향입니다. 반환값이 <see cref="FacingSource.None"/>이면 0입니다.</param>
        /// <returns>방향을 무엇에서 얻었는지입니다. 그 값이 곧 우선순위 중 어디서 걸렸는지를 말해 줍니다.</returns>
        public static FacingSource Resolve(in FacingRequest request, out Vector3 facing)
        {
            // 1. 교전 대상 — 무기가 겨눌 자리를 따로 가지고 있으면 그쪽입니다.
            //    창은 적이 지금 있는 곳이 아니라 올 자리를 미리 겨눕니다.
            if (request.HasTarget)
            {
                return TryFlatten(request.AimPoint - request.Position, out facing)
                    ? FacingSource.Target
                    : FacingSource.None;
            }

            // 2. 위협 — 방패를 든 채 서 있는 병사가 몸을 돌릴 쪽입니다.
            if (request.HasThreat && TryFlatten(request.ThreatPosition - request.Position, out facing))
            {
                return FacingSource.Threat;
            }

            // 3. 진행 방향
            if (request.IsMoving)
            {
                return TryFlatten(request.Steering, out facing)
                    ? FacingSource.Movement
                    : FacingSource.None;
            }

            // 4. 대기 방향. 지정된 것이 없으면 마지막 조향 방향으로 버팁니다.
            if (request.HasIdleFacing)
            {
                return TryFlatten(request.IdleFacing, out facing)
                    ? FacingSource.Idle
                    : FacingSource.None;
            }

            return TryFlatten(request.Steering, out facing)
                ? FacingSource.Movement
                : FacingSource.None;
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 수평 성분만 남기고 정규화합니다. 방향이라 부를 수 없을 만큼 짧으면 false입니다.
        /// </summary>
        private static bool TryFlatten(Vector3 direction, out Vector3 facing)
        {
            direction.y = 0f;

            if (direction.sqrMagnitude <= MinimumSquaredLength)
            {
                facing = Vector3.zero;
                return false;
            }

            facing = direction.normalized;
            return true;
        }
    }

    /// <summary>
    /// 시선 판단에 필요한 관측값입니다.
    /// </summary>
    public readonly struct FacingRequest
    {
        /// <summary>병사의 현재 위치입니다.</summary>
        public readonly Vector3 Position;

        /// <summary>살아 있는 교전 대상이 있는지 여부입니다.</summary>
        public readonly bool HasTarget;

        /// <summary>겨눌 월드 좌표입니다. 무기가 예측 지점을 내놓으면 그쪽이 들어옵니다.</summary>
        public readonly Vector3 AimPoint;

        /// <summary>몸을 돌릴 위협이 있는지 여부입니다.</summary>
        public readonly bool HasThreat;

        /// <summary>위협의 위치입니다.</summary>
        public readonly Vector3 ThreatPosition;

        /// <summary>스스로 이동 중인지 여부입니다.</summary>
        public readonly bool IsMoving;

        /// <summary>이번 프레임의 조향 속도입니다.</summary>
        public readonly Vector3 Steering;

        /// <summary>분대가 대기 방향을 지정했는지 여부입니다.</summary>
        public readonly bool HasIdleFacing;

        /// <summary>분대가 지정한 대기 방향입니다.</summary>
        public readonly Vector3 IdleFacing;

        public FacingRequest(
            Vector3 position,
            bool hasTarget,
            Vector3 aimPoint,
            bool hasThreat,
            Vector3 threatPosition,
            bool isMoving,
            Vector3 steering,
            bool hasIdleFacing,
            Vector3 idleFacing)
        {
            Position = position;
            HasTarget = hasTarget;
            AimPoint = aimPoint;
            HasThreat = hasThreat;
            ThreatPosition = threatPosition;
            IsMoving = isMoving;
            Steering = steering;
            HasIdleFacing = hasIdleFacing;
            IdleFacing = idleFacing;
        }
    }
}
