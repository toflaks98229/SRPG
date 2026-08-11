using SRPG.Common;
using SRPG.Core;
using SRPG.Data;
using UnityEngine;

namespace SRPG.Gameplay.Battle
{
    /// <summary>
    /// 전장이 소리를 내는 창구입니다.
    ///
    /// <b>왜 <see cref="IAudioService"/> 를 직접 부르지 않는가</b>
    ///
    /// 저쪽은 "이 클립을 여기서 틀어라"만 압니다. 그러면 소리를 내는 자리마다
    /// <b>어느 클립인지</b>와 <b>얼마나 크게인지</b>를 각자 알아야 합니다.
    /// 무기 셋이 각자 뱅크를 뒤지고 각자 음량을 정하면, 밸런스를 맞출 자리가 셋이 됩니다.
    ///
    /// 여기서 한 번 풀어 두면 부르는 쪽은 "베였다"만 말하면 됩니다.
    ///
    /// <b>음높이를 흔듭니다</b>
    ///
    /// 전장에서는 같은 소리가 초당 수십 번 겹칩니다. 그대로 겹치면 사람 귀에
    /// <b>한 번의 기계음</b>으로 뭉칩니다. 자리마다 음높이를 조금씩 흔들면
    /// 같은 클립인데도 여럿이 각자 내는 소리로 들립니다.
    /// </summary>
    public interface IBattleAudio
    {
        /// <summary>타격이 닿은 소리를 냅니다.</summary>
        /// <param name="damage">타격이 들어간 성질입니다.</param>
        /// <param name="position">닿은 자리입니다.</param>
        void PlayHit(DamageType damage, Vector3 position);

        /// <summary>활을 놓는 소리를 냅니다.</summary>
        /// <param name="position">쏜 자리입니다.</param>
        void PlayShot(Vector3 position);

        /// <summary>병사가 쓰러지는 소리를 냅니다.</summary>
        /// <param name="position">쓰러진 자리입니다.</param>
        void PlayDeath(Vector3 position);
    }

    /// <summary>
    /// <see cref="IBattleAudio"/> 의 기본 구현입니다. 뱅크에서 클립을 고르고 창구에 넘깁니다.
    ///
    /// <b>빈 칸은 합성음이 메웁니다</b>
    ///
    /// 뱅크가 통째로 비었는지가 아니라 <b>칸마다</b> 봅니다.
    /// 실제 소리는 한 번에 다 들어오지 않습니다 — 타격음만 먼저 구해 넣는 상태가 정상이고,
    /// 그때 "뱅크가 연결되었으니 나머지도 뱅크를 따른다"로 처리하면 나머지가 통째로 조용해집니다.
    /// 그러면 "이것만 소리가 안 난다"가 배선 문제인지 클립 문제인지 다시 가릴 수 없게 됩니다.
    /// </summary>
    public sealed class BattleAudio : IBattleAudio
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>소리를 실제로 내는 창구입니다.</summary>
        private readonly IAudioService _audio;

        /// <summary>전장의 소리 모음입니다. 절대 null 이 아닙니다.</summary>
        private readonly BattleAudioBank _bank;

        /// <summary>
        /// 음높이를 흔드는 난수입니다.
        ///
        /// 씨앗을 고정합니다. 같은 판을 다시 열면 같은 소리가 나야
        /// "이 소리가 원래 이랬나"를 비교할 수 있습니다.
        /// </summary>
        private readonly System.Random _jitter = new System.Random(7919);

        // ====================================================================================================
        // 2. Constructor
        // ====================================================================================================

        /// <param name="audio">소리를 낼 창구입니다. null 이면 아무 소리도 나지 않습니다.</param>
        /// <param name="bank">쓸 소리 모음입니다. null 이면 합성 뱅크를 씁니다.</param>
        public BattleAudio(IAudioService audio, BattleAudioBank bank)
        {
            _audio = audio;
            _bank = bank != null ? bank : BattleAudioBank.Synthesized;
        }

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <inheritdoc />
        public void PlayHit(DamageType damage, Vector3 position)
        {
            var clip = _bank.ResolveHit(damage);

            // 합성 뱅크는 <b>비었을 때만</b> 건드립니다. 먼저 꺼내 두면 클립이 다 꽂힌
            // 프로젝트에서도 첫 타격에 파형 다섯 개를 합성하게 됩니다.
            if (clip == null)
            {
                clip = BattleAudioBank.Synthesized.ResolveHit(damage);
            }

            Play(clip, position, _bank.HitVolume);
        }

        /// <inheritdoc />
        public void PlayShot(Vector3 position)
        {
            var clip = _bank.BowRelease;

            if (clip == null)
            {
                clip = BattleAudioBank.Synthesized.BowRelease;
            }

            Play(clip, position, _bank.ShotVolume);
        }

        /// <inheritdoc />
        public void PlayDeath(Vector3 position)
        {
            var clip = _bank.Death;

            if (clip == null)
            {
                clip = BattleAudioBank.Synthesized.Death;
            }

            Play(clip, position, _bank.DeathVolume);
        }

        // ====================================================================================================
        // 4. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 소리 하나를 그 자리에서 냅니다.
        ///
        /// <b>자리에서 냅니다.</b> 전장은 넓고 카메라는 그 위를 돕니다.
        /// 화면 전체에 같은 크기로 울리면 어디서 싸움이 벌어지는지 귀로 알 수 없습니다.
        /// </summary>
        /// <param name="clip">낼 소리입니다. 비어 있으면 아무것도 하지 않습니다.</param>
        /// <param name="position">소리가 나는 월드 좌표입니다.</param>
        /// <param name="volume">기본 음량입니다.</param>
        private void Play(AudioClip clip, Vector3 position, float volume)
        {
            if (_audio == null || clip == null || volume <= 0f)
            {
                return;
            }

            float spread = Mathf.Clamp01(_bank.PitchJitter);
            float pitch = 1f + ((float)_jitter.NextDouble() * 2f - 1f) * spread;

            _audio.PlaySfxAt(clip, position, volume, pitch);
        }
    }

    /// <summary>
    /// 아무 소리도 내지 않는 <see cref="IBattleAudio"/> 입니다.
    ///
    /// 전투만 따로 여는 경로(자동 검사)에서 소비자마다 null 검사를 심지 않기 위한 것입니다.
    /// </summary>
    public sealed class SilentBattleAudio : IBattleAudio
    {
        /// <summary>어디서나 쓸 수 있는 하나뿐인 인스턴스입니다. 상태가 없어 나눠 써도 됩니다.</summary>
        public static readonly SilentBattleAudio Instance = new SilentBattleAudio();

        /// <inheritdoc />
        public void PlayHit(DamageType damage, Vector3 position)
        {
        }

        /// <inheritdoc />
        public void PlayShot(Vector3 position)
        {
        }

        /// <inheritdoc />
        public void PlayDeath(Vector3 position)
        {
        }
    }
}
