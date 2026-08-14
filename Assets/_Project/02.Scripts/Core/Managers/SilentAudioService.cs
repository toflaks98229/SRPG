using UnityEngine;

namespace SRPG.Core.Managers
{
    /// <summary>
    /// 아무 소리도 내지 않는 <see cref="IAudioService"/> 입니다.
    ///
    /// <b>왜 이런 것이 필요한가</b>
    ///
    /// 전투 스코프는 루트 스코프의 자식으로 서는 것이 정상이지만,
    /// 루트 없이 전투만 여는 경로가 둘 있습니다 — 자동 검사와, 전투 씬만 열어 보는 편집 중의 실행입니다.
    ///
    /// 그때 <c>IAudioService</c> 해석이 실패하면 <b>컨테이너 조립 전체가 무너집니다.</b>
    /// 소리가 안 나는 정도로 끝날 일이 전투가 아예 시작되지 않는 것으로 번집니다.
    ///
    /// 소비자 쪽에 "오디오가 없을 수도 있다"는 분기를 심는 것도 방법이지만,
    /// 그러면 소리를 내는 모든 자리에 null 검사가 하나씩 붙습니다.
    /// 아무것도 하지 않는 구현을 하나 두는 편이 소비자를 단순하게 둡니다.
    /// </summary>
    public sealed class SilentAudioService : IAudioService
    {
        /// <inheritdoc />
        public void PlayBgm(AudioClip clip, bool loop = true, float volume = 1f, float fadeDuration = 1f)
        {
        }

        /// <inheritdoc />
        public void StopBgm()
        {
        }

        /// <inheritdoc />
        public void PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
        }

        /// <inheritdoc />
        public void PlaySfxAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
        {
        }

        /// <inheritdoc />
        public void SetSfxDistances(float minDistance, float maxDistance)
        {
        }

        /// <inheritdoc />
        public void SetMasterVolume(float volume)
        {
        }

        /// <inheritdoc />
        public void SetBgmVolume(float volume)
        {
        }

        /// <inheritdoc />
        public void SetSfxVolume(float volume)
        {
        }
    }
}
