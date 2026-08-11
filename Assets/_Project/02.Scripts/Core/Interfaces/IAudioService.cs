using UnityEngine;

namespace SRPG.Core
{
    /// <summary>
    /// 소리를 내는 유일한 창구입니다.
    ///
    /// <b>왜 인터페이스인가</b>
    ///
    /// 소리를 내는 쪽은 무기·유닛·UI처럼 게임 전역에 흩어져 있습니다.
    /// 그들이 <c>AudioManager</c> 라는 <b>구현</b>을 알게 되면, 오디오를 손볼 때마다
    /// 그 전부가 함께 흔들립니다. 그리고 테스트에서 소리를 끄려면 실제 매니저를 씬에 세워야 합니다.
    ///
    /// 소비자는 "재생해 달라"는 것만 알면 됩니다. 그것이 이 인터페이스입니다.
    /// </summary>
    public interface IAudioService
    {
        /// <summary>배경음을 틉니다. 같은 곡이 이미 흐르고 있으면 아무것도 하지 않습니다.</summary>
        /// <param name="clip">틀 곡입니다. null이면 아무것도 하지 않습니다.</param>
        /// <param name="loop">끝나면 다시 처음부터 틀지 여부입니다.</param>
        /// <param name="volume">이 곡의 기본 음량입니다. 설정 음량과 곱해집니다.</param>
        /// <param name="fadeDuration">앞 곡과 겹쳐 넘기는 시간입니다. 0이면 즉시 바뀝니다.</param>
        void PlayBgm(AudioClip clip, bool loop = true, float volume = 1f, float fadeDuration = 1f);

        /// <summary>배경음을 멈춥니다.</summary>
        void StopBgm();

        /// <summary>화면 전체에 들리는 효과음을 냅니다. UI와 알림에 씁니다.</summary>
        /// <param name="clip">낼 소리입니다. null이면 아무것도 하지 않습니다.</param>
        /// <param name="volume">음량입니다.</param>
        /// <param name="pitch">음높이입니다. 같은 소리를 반복할 때 조금씩 흔들면 덜 기계적입니다.</param>
        void PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f);

        /// <summary>
        /// 전장의 한 지점에서 효과음을 냅니다. 거리에 따라 작아집니다.
        ///
        /// 전투의 소리는 대부분 이쪽입니다 — 칼이 부딪히는 자리, 화살이 꽂히는 자리가 정해져 있고,
        /// 카메라가 그곳에서 멀면 작게 들려야 전장의 넓이가 읽힙니다.
        /// </summary>
        /// <param name="clip">낼 소리입니다. null이면 아무것도 하지 않습니다.</param>
        /// <param name="position">소리가 나는 월드 좌표입니다.</param>
        /// <param name="volume">음량입니다.</param>
        /// <param name="pitch">음높이입니다.</param>
        void PlaySfxAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f);

        /// <summary>전체 음량을 정합니다.</summary>
        /// <param name="volume">0에서 1 사이의 배율입니다.</param>
        void SetMasterVolume(float volume);

        /// <summary>배경음 음량을 정합니다. 흐르고 있는 곡에 즉시 반영됩니다.</summary>
        /// <param name="volume">0에서 1 사이의 배율입니다.</param>
        void SetBgmVolume(float volume);

        /// <summary>효과음 음량을 정합니다. 이후에 나는 소리부터 적용됩니다.</summary>
        /// <param name="volume">0에서 1 사이의 배율입니다.</param>
        void SetSfxVolume(float volume);
    }
}
