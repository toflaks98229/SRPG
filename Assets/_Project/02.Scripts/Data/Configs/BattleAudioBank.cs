using SRPG.Common;
using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 전투가 내는 소리를 모아 둔 에셋입니다.
    ///
    /// <b>왜 소리를 뱅크로 모으는가</b>
    ///
    /// 소리를 내는 자리는 무기·병사·화살로 흩어져 있습니다. 각자 클립을 들고 있으면
    /// "타격음을 바꾸자"가 프리팹 여러 개를 여는 일이 되고, 어느 하나를 빠뜨리면
    /// 무기 하나만 옛 소리를 냅니다.
    ///
    /// 한곳에 모으면 전장의 소리 전체를 한 에셋에서 봅니다.
    ///
    /// <b>클립이 없어도 됩니다</b>
    ///
    /// 이 프로젝트는 에셋이 비어 있어도 실행되어야 합니다.
    /// <see cref="CreateDefault"/> 가 코드로 파형을 합성해 채웁니다 —
    /// 좋은 소리는 아니지만 <b>배선이 실제로 도는지 귀로 확인할 수 있습니다</b>.
    /// 그게 없으면 "소리가 안 난다"의 원인이 배선인지 클립인지 가릴 수 없습니다.
    /// </summary>
    [CreateAssetMenu(fileName = "BattleAudio_", menuName = "SRPG/전투 사운드", order = 43)]
    public sealed class BattleAudioBank : ScriptableObject
    {
        // ====================================================================================================
        // 1. Inspector
        // ====================================================================================================

        [Header("타격 — 피해 성질별")]
        [Tooltip("베는 무기가 닿는 소리입니다.")]
        public AudioClip Slash;

        [Tooltip("찌르는 무기와 화살이 꽂히는 소리입니다.")]
        public AudioClip Pierce;

        [Tooltip("둔기가 닿는 소리입니다.")]
        public AudioClip Blunt;

        [Header("그 밖")]
        [Tooltip("활을 놓는 소리입니다.")]
        public AudioClip BowRelease;

        [Tooltip("병사가 쓰러지는 소리입니다.")]
        public AudioClip Death;

        [Header("음량")]
        [Range(0f, 1f)]
        [Tooltip("타격음의 기본 음량입니다. 전장에서는 여럿이 겹치므로 낮게 잡습니다.")]
        public float HitVolume = 0.5f;

        [Range(0f, 1f)]
        [Tooltip("발사음의 기본 음량입니다.")]
        public float ShotVolume = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("사망음의 기본 음량입니다.")]
        public float DeathVolume = 0.55f;

        [Range(0f, 0.5f)]
        [Tooltip("같은 소리를 반복할 때 음높이를 흔드는 폭입니다.\n" +
                 "0으로 두면 스무 명이 동시에 같은 음을 내 기계처럼 들립니다.")]
        public float PitchJitter = 0.12f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 이 성질의 타격에 쓸 소리를 고릅니다.
        /// </summary>
        /// <param name="damage">타격이 들어간 성질입니다.</param>
        /// <returns>쓸 클립입니다. 비어 있으면 null 입니다.</returns>
        public AudioClip ResolveHit(DamageType damage)
        {
            return damage switch
            {
                DamageType.Pierce => Pierce,
                DamageType.Blunt => Blunt,
                _ => Slash,
            };
        }

        // ====================================================================================================
        // 3. Factory
        // ====================================================================================================

        /// <summary>합성 뱅크의 하나뿐인 인스턴스입니다. 처음 필요할 때 만듭니다.</summary>
        private static BattleAudioBank _synthesized;

        /// <summary>
        /// 클립이 비었을 때 대신 쓰는 합성 뱅크입니다. <b>온 게임에 하나뿐입니다.</b>
        ///
        /// 판마다 새로 합성하면 짧은 클립이 판 수만큼 쌓입니다.
        /// 내용이 언제나 같으므로(씨앗이 이름에서 나옵니다) 나눠 쓰지 못할 이유가 없습니다.
        /// </summary>
        public static BattleAudioBank Synthesized
        {
            get
            {
                // 유니티의 null 비교는 파괴된 객체도 잡아냅니다.
                // 플레이 모드를 빠져나오며 사라졌으면 다음 요청에서 다시 만듭니다.
                if (_synthesized == null)
                {
                    _synthesized = CreateDefault();
                }

                return _synthesized;
            }
        }

        /// <summary>
        /// 클립을 코드로 합성한 임시 뱅크를 만듭니다.
        ///
        /// <b>임시입니다.</b> 실제 소리를 넣기 전까지 배선을 확인하기 위한 것입니다.
        /// 성질마다 다른 파형을 씁니다 — 베기는 잡음이 섞인 짧은 스침,
        /// 자돌은 높고 마른 딸깍, 타격은 낮고 둔한 쿵입니다.
        /// </summary>
        /// <returns>합성된 뱅크입니다. 에셋으로 저장되지 않습니다.</returns>
        public static BattleAudioBank CreateDefault()
        {
            var bank = CreateInstance<BattleAudioBank>();
            bank.name = "BattleAudio_Synth";

            // 씬이나 에셋에 딸려 저장되지 않게 합니다. 임시 파형이 프로젝트에 남으면
            // 실제 소리를 넣은 뒤에도 무엇이 진짜인지 헷갈립니다.
            bank.hideFlags = HideFlags.HideAndDontSave;

            bank.Slash = Synth.Noise("SFX_Slash", 0.14f, 1600f, 0.55f);
            bank.Pierce = Synth.Tone("SFX_Pierce", 0.10f, 880f, 0.35f);
            bank.Blunt = Synth.Tone("SFX_Blunt", 0.18f, 160f, 0.7f);
            bank.BowRelease = Synth.Noise("SFX_Bow", 0.09f, 2600f, 0.3f);
            bank.Death = Synth.Tone("SFX_Death", 0.32f, 110f, 0.9f);

            return bank;
        }

        // ====================================================================================================
        // 4. Nested Types
        // ====================================================================================================

        /// <summary>
        /// 아주 단순한 파형 합성기입니다. 실제 소리를 넣기 전까지만 씁니다.
        /// </summary>
        private static class Synth
        {
            /// <summary>합성 표본율입니다. 짧은 효과음이라 낮게 잡아도 됩니다.</summary>
            private const int SampleRate = 22050;

            /// <summary>
            /// 감쇠하는 사인파를 만듭니다. 자돌과 타격처럼 <b>음정이 있는</b> 소리에 씁니다.
            /// </summary>
            /// <param name="clipName">클립 이름입니다.</param>
            /// <param name="seconds">길이(초)입니다.</param>
            /// <param name="frequency">기본 주파수(Hz)입니다.</param>
            /// <param name="decay">감쇠 세기입니다. 클수록 빨리 잦아듭니다.</param>
            /// <returns>합성된 클립입니다.</returns>
            public static AudioClip Tone(string clipName, float seconds, float frequency, float decay)
            {
                return Build(clipName, seconds, (t, normalized) =>
                {
                    float envelope = Mathf.Exp(-normalized / Mathf.Max(0.01f, 1f - decay) * 4f);

                    return Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope;
                });
            }

            /// <summary>
            /// 감쇠하는 잡음을 만듭니다. 베기와 활처럼 <b>스치는</b> 소리에 씁니다.
            /// </summary>
            /// <param name="clipName">클립 이름입니다.</param>
            /// <param name="seconds">길이(초)입니다.</param>
            /// <param name="brightness">잡음을 얼마나 밝게 걸러 낼지입니다.</param>
            /// <param name="decay">감쇠 세기입니다.</param>
            /// <returns>합성된 클립입니다.</returns>
            public static AudioClip Noise(string clipName, float seconds, float brightness, float decay)
            {
                var random = new System.Random(clipName.GetHashCode());
                float previous = 0f;

                // 밝기가 높을수록 이전 표본을 덜 섞어 고음이 남습니다.
                float smoothing = Mathf.Clamp01(1f - brightness / SampleRate * 4f);

                return Build(clipName, seconds, (t, normalized) =>
                {
                    float white = (float)(random.NextDouble() * 2.0 - 1.0);

                    previous = Mathf.Lerp(white, previous, smoothing);

                    float envelope = Mathf.Exp(-normalized / Mathf.Max(0.01f, 1f - decay) * 4f);

                    return previous * envelope;
                });
            }

            /// <summary>
            /// 표본을 채워 클립을 만듭니다.
            /// </summary>
            /// <param name="clipName">클립 이름입니다.</param>
            /// <param name="seconds">길이(초)입니다.</param>
            /// <param name="sample">시각과 진행도를 받아 -1에서 1 사이를 돌려주는 함수입니다.</param>
            /// <returns>합성된 클립입니다.</returns>
            private static AudioClip Build(string clipName, float seconds, System.Func<float, float, float> sample)
            {
                int count = Mathf.Max(1, Mathf.RoundToInt(SampleRate * seconds));
                var data = new float[count];

                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)SampleRate;
                    float normalized = i / (float)count;

                    data[i] = Mathf.Clamp(sample(t, normalized), -1f, 1f);
                }

                var clip = AudioClip.Create(clipName, count, 1, SampleRate, false);
                clip.SetData(data, 0);

                // 뱅크와 같은 이유입니다. 클립은 뱅크의 자식이 아니라 별개의 객체라
                // 여기서 따로 지정하지 않으면 뱅크만 사라지고 클립이 남습니다.
                clip.hideFlags = HideFlags.HideAndDontSave;

                return clip;
            }
        }
    }
}
