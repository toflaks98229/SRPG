using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Units;
using UnityEngine;

namespace SRPG.Gameplay.Weapons
{
    /// <summary>
    /// 무기의 공통 골격입니다.
    ///
    /// 이 계층이 생긴 이유: 피해를 "사거리 안이면 즉시 적용"에서 "무기가 실제로 닿은 곳에서 적용"으로 바꿨습니다.
    /// 그러면 공격은 순간이 아니라 <b>시간을 갖는 동작</b>이 되고, 그 동작을 관리할 주체가 필요합니다.
    ///
    /// 한 번의 공격은 세 구간으로 나뉩니다.
    ///   준비(windup) → 판정(active) → 회수(recovery)
    /// 판정 구간에서만 물리 질의를 돌리므로, 휘두르는 도중에 들어온 적도 맞고
    /// 이미 맞은 적은 같은 동작에서 두 번 맞지 않습니다.
    /// </summary>
    public abstract class WeaponBase : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>이 무기를 든 유닛입니다.</summary>
        protected Unit Owner { get; private set; }

        /// <summary>소유자의 정의 데이터입니다.</summary>
        protected UnitDefinition Definition { get; private set; }

        /// <summary>
        /// 투사체 재사용 풀입니다. 투사체를 쓰지 않는 무기에는 null일 수 있습니다.
        ///
        /// 전투 컨텍스트 전체가 아니라 <b>풀만</b> 받습니다.
        /// 무기가 지형이나 유닛 레지스트리에 손댈 이유가 없고, 받을 수 있게 두면 언젠가 손댑니다.
        /// </summary>
        protected ProjectilePool ProjectilePool { get; private set; }

        /// <summary>전투 튜닝 수치입니다. 무기별 감각을 재컴파일 없이 조정하는 통로입니다.</summary>
        protected BattleTuning Tuning { get; private set; }

        /// <summary>
        /// 전장의 소리 창구입니다. 절대 null이 아닙니다.
        ///
        /// <b>무기가 내는 소리는 하나뿐입니다</b> — 활을 놓는 소리입니다.
        /// 맞는 소리는 맞은 병사가 냅니다(<c>Unit.ReceiveHit</c>). 때린 쪽은 자기 타격이
        /// 닿았는지조차 모르기 때문입니다. 발사는 반대입니다 — 화살이 어디에 꽂히든
        /// 시위를 놓는 순간은 확실히 일어나고, 그것을 아는 것은 무기뿐입니다.
        /// </summary>
        protected IBattleAudio Audio { get; private set; } = SilentBattleAudio.Instance;

        /// <summary>무기 모델이 붙는 트랜스폼입니다. 프리팹에서 연결하거나 런타임에 만듭니다.</summary>
        [SerializeField]
        [Tooltip("무기 모델의 루트입니다. 공격 동작에서 이 트랜스폼을 움직입니다.")]
        protected Transform WeaponPivot;

        /// <summary>진행 중인 공격 동작의 남은 시간입니다. 0보다 크면 동작 중입니다.</summary>
        private float _actionTimer;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>공격 동작이 진행 중인지 여부입니다.</summary>
        public bool IsBusy => _actionTimer > 0f;

        /// <summary>동작 전체 길이 대비 현재 진행도입니다. 0이면 시작, 1이면 끝입니다.</summary>
        protected float Progress
        {
            get
            {
                float duration = Owner != null ? Owner.Stats.AttackDuration : 0.4f;
                return duration <= 0.0001f ? 1f : 1f - Mathf.Clamp01(_actionTimer / duration);
            }
        }

        /// <summary>공격을 시작할 때 소유자를 제자리에 붙잡는 시간입니다.</summary>
        public virtual float RootDuration => Definition != null ? Owner.Stats.AttackRootDuration : 0f;

        /// <summary>
        /// 대열을 풀고 적에게 다가갈 수 있는 최대 <b>격자 거리</b>입니다. 0이면 자리를 지킵니다.
        ///
        /// <b>왜 무기가 정하는가</b>
        ///
        /// 대열을 풀어도 되는지는 무기의 성격입니다.
        /// 검을 든 병사는 한두 걸음 나가 베는 것이 자연스럽지만,
        /// 창병이 대열을 풀면 그 순간 창병이 아니게 되고, 궁수가 앞으로 나가면 궁수가 아니게 됩니다.
        ///
        /// 기본값은 0입니다. <b>기본은 자리를 지키는 것</b>이고, 나서는 쪽이 예외입니다.
        /// 반대로 두면 새 무기를 추가할 때마다 "얘도 대열을 풀면 안 되는데"를 매번 기억해야 합니다.
        /// </summary>
        public virtual float FormationBreakTiles => 0f;

        /// <summary>
        /// 표적을 한 번 잡으면 이 시간(초) 동안은 바꾸지 않습니다.
        ///
        /// 방어선을 유지하는 무기에 필요합니다. 더 가까운 적이 나타날 때마다 시선을 돌리면
        /// 창끝이 이리저리 흩어지고, 결국 아무도 막지 않는 구멍이 생깁니다.
        ///
        /// <b>0은 "매 프레임 자유롭게"가 아닙니다.</b>
        /// 고정을 걸지 않겠다는 뜻이고, 그때도 재평가는 <c>UnitTargeting</c>의
        /// 최소 주기로만 일어납니다. 프레임마다 다시 고르면 병사마다 공간 질의가
        /// 하나씩 늘고, 거리가 엇비슷한 두 적 사이에서 표적이 떨립니다.
        /// </summary>
        public virtual float TargetLockSeconds => 0f;

        /// <summary>
        /// 이 무기가 <b>공격 대기열</b>을 쓰는지 여부입니다.
        ///
        /// 켜면 같은 적을 이미 다른 병사가 맡았을 때 물러나 다음 적을 겨눕니다.
        /// 방어선을 세우는 무기에만 의미가 있습니다. 난전에서는 오히려 공격이 뜸해집니다.
        /// </summary>
        public virtual bool UsesAttackQueue => false;

        /// <summary>
        /// 회전에 <b>무게</b>가 있는지 여부입니다. 켜면 소유자가 스프링으로 방향을 바꿉니다.
        /// </summary>
        public virtual bool UsesSpringTurn => false;

        /// <summary>
        /// 공격을 시작할 때 <b>몸을 앞으로 던지는</b> 힘입니다. 0이면 제자리에서 휘두릅니다.
        ///
        /// 도약은 두 가지를 합니다.
        ///   · 무기의 짧은 사거리를 몸으로 메웁니다 — 닿을락 말락 한 거리에서 파고들 수 있습니다
        ///   · 타격 순간의 넉백과 맞물려 <b>부딪히는 느낌</b>을 만듭니다
        ///
        /// 제자리에서 허공에 칼을 휘두르면 아무리 판정이 정확해도 답답해 보입니다.
        /// 버티는 것이 일인 무기(창)는 0으로 둡니다. 창병이 뛰어들면 그 순간 창병이 아니게 됩니다.
        /// </summary>
        public virtual float LungeForce => 0f;

        /// <summary>
        /// 분대가 <b>가로로 넓게 퍼지는</b> 전열을 선호하는지 여부입니다.
        ///
        /// 창은 정면 좁은 각도만 위험하므로 옆으로 늘어서야 방어선이 됩니다.
        /// 검과 활은 사방에서 오는 위협에 대응해야 하므로 동심원이 낫습니다.
        /// </summary>
        public virtual bool PrefersLineFormation => false;

        /// <summary>회전 스프링의 진동수입니다. 클수록 빠르게 따라붙습니다.</summary>
        public virtual float TurnSpringFrequency => 1.6f;

        /// <summary>회전 스프링의 감쇠입니다. 1 미만이면 목표를 지나쳤다 돌아옵니다.</summary>
        public virtual float TurnSpringDamping => 0.55f;

        // ====================================================================================================
        // 3-1. Hooks - Reaction
        // ====================================================================================================

        /// <summary>
        /// 지금 공격을 시작해도 되는지 무기가 판단합니다.
        ///
        /// 사거리·재사용 대기 같은 공통 조건은 소유자가 이미 확인했습니다.
        /// 여기서는 <b>무기 고유의 사정</b>만 봅니다. 창병이 품 안을 내주어 무너진 상태가 그렇습니다.
        /// </summary>
        /// <param name="target">치려는 대상입니다.</param>
        /// <returns>무기 쪽 사정으로 막히는 것이 없으면 true입니다. 기본 구현은 언제나 true입니다.</returns>
        public virtual bool CanBeginAttack(Unit target) => true;

        /// <summary>
        /// 지금 물러나야 할 위협이 있으면 그 위치를 알려 줍니다.
        ///
        /// 긴 무기가 품 안을 내주면 싸울 방법이 없습니다. 그 자리에 서 있는 것은 선택지가 아니고,
        /// 창을 세워 들고 물러나는 것이 유일한 반응입니다.
        /// </summary>
        /// <param name="threatPosition">물러날 기준이 되는 위협의 위치입니다.</param>
        /// <returns>물러나야 하면 true입니다. 기본 구현은 언제나 false입니다.</returns>
        public virtual bool TryGetRetreatFrom(out Vector3 threatPosition)
        {
            threatPosition = Vector3.zero;
            return false;
        }

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 무기를 초기화합니다. 유닛 초기화 직후 호출됩니다.
        /// </summary>
        /// <param name="owner">이 무기를 든 유닛입니다.</param>
        /// <param name="definition">소유자의 정의 데이터입니다.</param>
        /// <param name="projectilePool">투사체 재사용 풀입니다. 없어도 동작합니다.</param>
        /// <param name="tuning">전투 튜닝입니다. 없으면 무기가 코드 기본값으로 동작합니다.</param>
        /// <param name="audio">전장의 소리 창구입니다. 없으면 소리를 내지 않습니다.</param>
        public virtual void Initialize(
            Unit owner,
            UnitDefinition definition,
            ProjectilePool projectilePool = null,
            BattleTuning tuning = null,
            IBattleAudio audio = null)
        {
            Owner = owner;
            Definition = definition;
            ProjectilePool = projectilePool;
            Tuning = tuning;
            Audio = audio ?? SilentBattleAudio.Instance;

            if (WeaponPivot == null)
            {
                WeaponPivot = BuildDefaultPivot();
            }

            CacheRestPose();
        }

        /// <summary>
        /// 공격 동작을 시작합니다.
        /// </summary>
        /// <param name="target">칠 대상입니다. null이거나 이미 쓰러졌으면 시작하지 않습니다.</param>
        /// <returns>실제로 시작했으면 true입니다. 이미 동작 중이면 false입니다.</returns>
        public bool TryBeginAttack(Unit target)
        {
            if (IsBusy || Owner == null || Definition == null || target == null || !target.IsAlive)
            {
                return false;
            }

            if (!CanBeginAttack(target))
            {
                return false;
            }

            _actionTimer = Mathf.Max(0.05f, Owner.Stats.AttackDuration);
            OnAttackBegan(target);
            return true;
        }

        /// <summary>
        /// 매 프레임 호출됩니다. 동작 진행과 판정을 처리합니다.
        /// </summary>
        /// <param name="deltaTime">지난 시간입니다.</param>
        public void Tick(float deltaTime)
        {
            if (_actionTimer <= 0f)
            {
                OnIdleTick(deltaTime);
                return;
            }

            _actionTimer -= deltaTime;
            OnActionTick(deltaTime, Progress);

            if (_actionTimer <= 0f)
            {
                _actionTimer = 0f;
                OnAttackEnded();
            }
        }

        /// <summary>
        /// 진행 중인 동작을 즉시 취소합니다. 경직되거나 죽었을 때 호출됩니다.
        /// </summary>
        public void CancelAttack()
        {
            if (_actionTimer <= 0f)
            {
                return;
            }

            _actionTimer = 0f;
            OnAttackEnded();
        }

        // ====================================================================================================
        // 4. Template Methods
        // ====================================================================================================

        /// <summary>공격이 시작될 때 호출됩니다.</summary>
        protected virtual void OnAttackBegan(Unit target)
        {
        }

        /// <summary>공격이 진행되는 동안 매 프레임 호출됩니다.</summary>
        /// <param name="deltaTime">경과 시간입니다.</param>
        /// <param name="progress">동작 진행도(0~1)입니다.</param>
        protected virtual void OnActionTick(float deltaTime, float progress)
        {
        }

        /// <summary>공격이 끝날 때 호출됩니다. 무기를 대기 자세로 되돌리는 자리입니다.</summary>
        protected virtual void OnAttackEnded()
        {
            ReturnToRestPose();
        }

        /// <summary>공격하지 않는 동안 매 프레임 호출됩니다.</summary>
        protected virtual void OnIdleTick(float deltaTime)
        {
        }

        // ====================================================================================================
        // 5. Aiming
        // ====================================================================================================

        /// <summary>
        /// 이 무기가 겨누고 싶은 지점을 알려 줍니다.
        ///
        /// 기본값은 "따로 없음"입니다. 그러면 소유자는 대상의 현재 위치를 그대로 바라봅니다.
        /// 창처럼 <b>적이 올 자리를 미리 겨눠야</b> 하는 무기가 이걸 재정의합니다.
        ///
        /// 이 갈래가 무기 쪽에 있는 이유가 있습니다.
        /// 겨누는 방식은 무기의 성격이지 유닛의 성격이 아닙니다.
        /// <c>Unit</c>에 병과별 분기를 넣기 시작하면 무기를 늘릴 때마다 액터가 두꺼워집니다.
        /// </summary>
        /// <param name="target">현재 교전 대상입니다.</param>
        /// <param name="aimPoint">겨눌 월드 좌표입니다.</param>
        /// <returns>겨눌 지점이 따로 있으면 true입니다.</returns>
        public virtual bool TryGetAimPoint(Unit target, out Vector3 aimPoint)
        {
            aimPoint = Vector3.zero;
            return false;
        }

        /// <summary>무기 모델이 없을 때 기본 형상을 만듭니다.</summary>
        protected abstract Transform BuildDefaultPivot();

        /// <summary>대기 자세를 기억합니다.</summary>
        protected abstract void CacheRestPose();

        /// <summary>대기 자세로 되돌립니다.</summary>
        protected abstract void ReturnToRestPose();
    }
}
