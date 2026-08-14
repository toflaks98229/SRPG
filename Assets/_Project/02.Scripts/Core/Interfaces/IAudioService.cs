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
        /// <summary>
        /// 자리에서 나는 소리의 <b>감쇠 범위</b>를 정합니다.
        ///
        /// <b>왜 부르는 쪽이 정하는가</b>
        ///
        /// 감쇠는 <b>월드 거리</b>로 잽니다. 그런데 그 거리가 얼마나 먼 것인지는
        /// 전장의 크기와 카메라가 얼마나 물러나 있는지가 정합니다.
        /// 매니저에 절대값을 박아 두면, 전장이 넓어지거나 카메라를 뒤로 빼는 순간
        /// <b>화면에 보이는 싸움이 감쇠 구간 한복판에 들어갑니다</b> — 실제로 그랬습니다.
        /// 소리는 나는데 거의 들리지 않고, 그 증상은 "소리가 안 난다"와 구별되지 않습니다.
        ///
        /// 이 프로젝트가 <c>_DepthFade</c> · <c>_OpenSeaDepth</c> · <c>_WaveShoreFade</c> 에서
        /// 이미 세 번 내린 판단과 같습니다 — <b>절대 길이가 아니라 이 판에 대한 비율</b>이어야 합니다.
        /// </summary>
        /// <param name="minDistance">이 거리 안에서는 최대 음량입니다. 화면에 보이는 것이 여기 들어와야 합니다.</param>
        /// <param name="maxDistance">이 거리를 넘으면 들리지 않습니다.</param>
        void SetSfxDistances(float minDistance, float maxDistance);

        void SetMasterVolume(float volume);

        /// <summary>배경음 음량을 정합니다. 흐르고 있는 곡에 즉시 반영됩니다.</summary>
        /// <param name="volume">0에서 1 사이의 배율입니다.</param>
        void SetBgmVolume(float volume);

        /// <summary>효과음 음량을 정합니다. 이후에 나는 소리부터 적용됩니다.</summary>
        /// <param name="volume">0에서 1 사이의 배율입니다.</param>
        void SetSfxVolume(float volume);
    }
}
