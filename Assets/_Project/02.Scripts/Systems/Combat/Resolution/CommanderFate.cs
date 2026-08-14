using SRPG.Data;
using UnityEngine;

namespace SRPG.Systems.Combat
{
    /// <summary>치명상을 입은 지휘관에게 일어나는 일입니다.</summary>
    public enum CommanderFateOutcome
    {
        /// <summary>쓰러지지 않고 부상으로 넘어갑니다. 체력이 조금 돌아오고 잠시 움직이지 못합니다.</summary>
        Wounded = 0,

        /// <summary>쓰러집니다. 분대가 영구히 사라집니다.</summary>
        Fallen,
    }

    /// <summary>
    /// 치명상을 맞은 순간의 지휘관 상태입니다.
    /// </summary>
    public readonly struct CommanderGuard
    {
        /// <summary>지휘관을 뺀, 아직 서 있는 병사 수입니다.</summary>
        public readonly int EscortsAlive;

        /// <summary>지휘관을 뺀, 이 분대가 처음 데리고 나온 병사 수입니다.</summary>
        public readonly int EscortsDeployed;

        /// <summary>지금까지 견딘 부상 수입니다.</summary>
        public readonly int WoundsTaken;

        /// <param name="escortsAlive">아직 서 있는 호위 수입니다.</param>
        /// <param name="escortsDeployed">처음 데리고 나온 호위 수입니다.</param>
        /// <param name="woundsTaken">지금까지 견딘 부상 수입니다.</param>
        public CommanderGuard(int escortsAlive, int escortsDeployed, int woundsTaken)
        {
            EscortsAlive = Mathf.Max(0, escortsAlive);
            EscortsDeployed = Mathf.Max(0, escortsDeployed);
            WoundsTaken = Mathf.Max(0, woundsTaken);
        }
    }

    /// <summary>
    /// 지휘관이 치명상을 입었을 때 쓰러지는지 부상으로 넘어가는지를 정합니다.
    ///
    /// <b>왜 즉사를 두지 않는가</b>
    ///
    /// 지휘관의 죽음은 이 게임에서 유일하게 되돌릴 수 없는 손실입니다(조사 보고서 §2.5).
    /// 그런데 병사 하나와 같은 방식으로 처리하면, 온전한 분대의 지휘관이
    /// 빗나간 화살 하나에 사라질 수 있습니다. 그러면 영구 손실이
    /// <b>플레이어의 판단이 아니라 사고</b>가 되고, "물러날 것인가 버틸 것인가"라는
    /// 이 게임의 핵심 판단이 성립하지 않습니다 — 애초에 판단할 틈이 없었으니까요.
    ///
    /// <b>순서가 규칙입니다</b>
    ///
    ///   1. 부상 한도를 넘겼으면 → <b>판정 없이</b> 쓰러집니다
    ///   2. 호위가 아직 충분하면 → <b>판정 없이</b> 부상입니다  ← 안전장치
    ///   3. 그 외에는 확률이 정합니다
    ///
    /// 1번이 2번보다 <b>앞이어야 합니다.</b> 뒤에 두면 호위를 충원하는 것만으로
    /// 부상 한도가 무의미해집니다 — 지휘관이 몇 번이든 살아나고, 영구 손실이 사라집니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 판단이라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public static class CommanderFate
    {
        // ====================================================================================================
        // 1. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 이번 치명상의 결말을 정합니다.
        /// </summary>
        /// <param name="guard">치명상을 맞은 순간의 지휘관 상태입니다.</param>
        /// <param name="rules">지휘관 규칙입니다. null이면 코드 기본값과 같은 값으로 봅니다.</param>
        /// <param name="roll">
        /// 0~1 무작위 값입니다. <b>밖에서 받습니다.</b>
        /// 안에서 뽑으면 같은 상황이 매번 다른 답을 내어 검사를 쓸 수 없습니다.
        /// </param>
        /// <returns>쓰러지는지 부상으로 넘어가는지입니다.</returns>
        public static CommanderFateOutcome Resolve(
            in CommanderGuard guard, BattleTuning.CommanderTuning rules, float roll)
        {
            int maxWounds = rules != null ? rules.MaxWounds : 2;

            // 1. 견딜 만큼 견뎠습니다. 여기서는 호위가 몇이든 소용없습니다.
            if (guard.WoundsTaken >= Mathf.Max(0, maxWounds))
            {
                return CommanderFateOutcome.Fallen;
            }

            // 2. 안전장치 — 호위가 아직 남아 있으면 쓰러지지 않습니다.
            if (!IsExposed(guard, rules))
            {
                return CommanderFateOutcome.Wounded;
            }

            // 3. 이제야 확률입니다.
            float fallChance = rules != null ? rules.FallChance : 0.35f;

            return roll < Mathf.Clamp01(fallChance)
                ? CommanderFateOutcome.Fallen
                : CommanderFateOutcome.Wounded;
        }

        /// <summary>
        /// 호위가 무너져 지휘관이 위험해졌는지 봅니다.
        ///
        /// <b>비율로 잽니다.</b> "둘 이상 죽으면" 같은 절대 수로 두면 여섯 명짜리 분대와
        /// 두 명짜리 분대가 같은 조건을 갖게 되어, 작은 분대의 지휘관이 훨씬 쉽게 죽습니다.
        ///
        /// <b>처음 인원을 모르면 지금 인원으로 봅니다.</b> 그때는 아무도 잃지 않은 상태이므로
        /// 안전장치가 그대로 걸립니다 — 모르는 값 때문에 지휘관이 죽는 쪽으로 기울지 않습니다.
        /// </summary>
        /// <param name="guard">지휘관 상태입니다.</param>
        /// <param name="rules">지휘관 규칙입니다.</param>
        /// <returns>호위가 무너져 확률 판정에 들어가야 하면 true입니다.</returns>
        public static bool IsExposed(in CommanderGuard guard, BattleTuning.CommanderTuning rules)
        {
            float requiredRatio = rules != null ? rules.FallenEscortRatio : 0.6f;

            int deployed = guard.EscortsDeployed > 0 ? guard.EscortsDeployed : guard.EscortsAlive;

            // 애초에 호위가 없었으면 지킬 것도 없습니다. 곧바로 위험합니다.
            if (deployed <= 0)
            {
                return true;
            }

            int fallen = Mathf.Max(0, deployed - guard.EscortsAlive);

            // 올림입니다. 내림으로 두면 비율이 1이어도 마지막 한 명이 남은 채 위험해집니다.
            int required = Mathf.CeilToInt(deployed * Mathf.Clamp01(requiredRatio));

            return fallen >= Mathf.Max(1, required);
        }

        /// <summary>
        /// 부상에서 일어날 때 되찾는 체력입니다.
        ///
        /// <b>부상이 쌓일수록 줄어듭니다.</b> 매번 같은 만큼 되찾으면 지휘관이
        /// 점점 위태로워지는 것이 아니라 그냥 체력이 많은 병사가 됩니다.
        /// 절반씩 줄이면 두 번째 부상부터는 눈에 띄게 위태로워집니다.
        /// </summary>
        /// <param name="maxHealth">지휘관의 최대 체력입니다.</param>
        /// <param name="woundsTaken">이번 부상을 <b>세기 전</b>의 부상 수입니다.</param>
        /// <param name="rules">지휘관 규칙입니다.</param>
        /// <returns>부상 직후의 체력입니다. 최소 1은 남습니다.</returns>
        public static float ResolveRecoveredHealth(
            float maxHealth, int woundsTaken, BattleTuning.CommanderTuning rules)
        {
            float ratio = rules != null ? rules.WoundRecoveryRatio : 0.35f;

            float scaled = Mathf.Clamp01(ratio) / Mathf.Pow(2f, Mathf.Max(0, woundsTaken));

            return Mathf.Max(1f, maxHealth * scaled);
        }
    }
}
