using UnityEngine;

namespace SRPG.Systems.Motion
{
    /// <summary>
    /// 병사에게 걸린 <b>외력</b>을 채널별로 들고 있습니다.
    ///
    /// <b>넉백과 도약을 왜 나누는가</b>
    ///
    /// 둘 다 "스스로 걷는 것이 아닌 이동"이지만 결과가 달라야 합니다.
    ///
    ///   · <b>넉백</b>은 맞아서 밀려나는 것입니다. 밀려난 자리가 물이면 <b>익사합니다.</b>
    ///     이 게임의 주요 사망 수단이고, 그래서 물가가 위험 요소가 됩니다.
    ///   · <b>도약</b>은 스스로 앞으로 몸을 던지는 것입니다. 검병이 벨 때 파고드는 힘입니다.
    ///     이쪽은 물로 밀려나지 않습니다.
    ///
    /// 같은 채널에 넣으면 물가의 적에게 달려들 때마다 스스로 물에 뛰어들어 익사합니다.
    /// 밀려서 빠지는 것은 사고지만, 달려들다 빠지는 것은 자살입니다.
    ///
    /// 분리 성분을 함께 두는 이유는 그것 역시 <b>조향과 따로 부드럽게 따라가야 하는</b> 값이기 때문입니다.
    /// 계산된 분리를 그대로 더하면 이웃이 반경에 드나들 때마다 속도가 계단처럼 튑니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 상태라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public sealed class ImpulseState
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>맞아서 밀려나는 속도입니다. 익사 판정이 이 값만 봅니다.</summary>
        private Vector3 _knockback;

        /// <summary>스스로 앞으로 던진 속도입니다. 익사 판정에서 제외됩니다.</summary>
        private Vector3 _lunge;

        /// <summary>부드럽게 따라가는 분리 성분입니다. 목표값을 지수 보간으로 좇습니다.</summary>
        private Vector3 _separation;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>맞아서 밀려나는 속도입니다. 물 위로 밀려나면 익사합니다.</summary>
        public Vector3 Knockback => _knockback;

        /// <summary>스스로 앞으로 던진 속도입니다. 물로 밀려나지 않습니다.</summary>
        public Vector3 Lunge => _lunge;

        /// <summary>부드럽게 따라가는 분리 성분입니다.</summary>
        public Vector3 Separation => _separation;

        // ====================================================================================================
        // 3. Public Methods - Apply
        // ====================================================================================================

        /// <summary>넉백을 더합니다. 수평 성분만 씁니다.</summary>
        /// <param name="impulse">밀어내는 속도입니다. 높이 성분은 버려집니다.</param>
        public void AddKnockback(Vector3 impulse)
        {
            impulse.y = 0f;
            _knockback += impulse;
        }

        /// <summary>도약을 더합니다. 수평 성분만 씁니다.</summary>
        /// <param name="impulse">앞으로 몸을 던지는 속도입니다. 높이 성분은 버려집니다.</param>
        public void AddLunge(Vector3 impulse)
        {
            impulse.y = 0f;
            _lunge += impulse;
        }

        // ====================================================================================================
        // 4. Public Methods - Tick
        // ====================================================================================================

        /// <summary>
        /// 외력을 감쇠시킵니다.
        ///
        /// 도약은 넉백보다 빨리 잦아듭니다. 한 걸음 파고드는 것이지 미끄러지는 것이 아닙니다.
        /// </summary>
        /// <param name="deltaTime">지난 시간입니다.</param>
        /// <param name="knockbackDecay">넉백이 초당 줄어드는 속도입니다.</param>
        /// <param name="lungeDecay">도약이 초당 줄어드는 속도입니다. 넉백보다 커야 합니다.</param>
        public void Decay(float deltaTime, float knockbackDecay, float lungeDecay)
        {
            _knockback = Vector3.MoveTowards(_knockback, Vector3.zero, knockbackDecay * deltaTime);
            _lunge = Vector3.MoveTowards(_lunge, Vector3.zero, lungeDecay * deltaTime);
        }

        /// <summary>
        /// 분리 성분을 목표값으로 부드럽게 따라가게 합니다.
        ///
        /// 지수 보간이라 프레임률이 달라져도 수렴 속도가 같습니다.
        /// <paramref name="smoothing"/>이 작을수록 부드럽고, 너무 낮추면 반응이 늦어
        /// 병사들이 잠깐 겹쳤다 떨어집니다.
        /// </summary>
        /// <param name="target">이번 프레임에 계산된 분리 속도입니다.</param>
        /// <param name="smoothing">따라가는 속도입니다. 작을수록 부드럽습니다.</param>
        /// <param name="deltaTime">지난 시간입니다.</param>
        public void FollowSeparation(Vector3 target, float smoothing, float deltaTime)
        {
            float rate = Mathf.Max(0.01f, smoothing);

            _separation = Vector3.Lerp(_separation, target, 1f - Mathf.Exp(-rate * deltaTime));
        }

        // ====================================================================================================
        // 5. Public Methods - Query
        // ====================================================================================================

        /// <summary>
        /// 이 속도보다 세게 밀려나는 중인지 여부입니다. 익사 판정의 조건입니다.
        ///
        /// 문턱을 두는 이유는, 잔여 넉백이 거의 0에 가까울 때까지 익사 판정을 켜 두면
        /// 물가에 선 병사가 스치듯 맞기만 해도 빠져 죽기 때문입니다.
        /// </summary>
        /// <param name="speed">비교할 속도 문턱입니다.</param>
        /// <returns>넉백 속도가 문턱을 넘으면 true입니다. 도약은 세지 않습니다.</returns>
        public bool IsPushedFasterThan(float speed)
        {
            return _knockback.sqrMagnitude > speed * speed;
        }

        /// <summary>스스로 내는 조향에 외력을 얹습니다.</summary>
        /// <param name="steering">스스로 내는 조향 속도입니다.</param>
        /// <returns>조향에 넉백과 도약을 더한 최종 속도입니다. 분리는 조향에 이미 포함돼 있습니다.</returns>
        public Vector3 CombineWith(Vector3 steering)
        {
            return steering + _knockback + _lunge;
        }

        // ====================================================================================================
        // 6. Public Methods - Lifecycle
        // ====================================================================================================

        /// <summary>모든 외력을 지웁니다. 유닛을 초기화할 때 부릅니다.</summary>
        public void Reset()
        {
            _knockback = Vector3.zero;
            _lunge = Vector3.zero;
            _separation = Vector3.zero;
        }
    }
}
