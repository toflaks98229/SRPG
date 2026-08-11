using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SRPG.Core.Managers
{
    /// <summary>
    /// 소리를 실제로 내는 매니저입니다. 앱이 살아 있는 동안 하나만 존재합니다.
    ///
    /// <b>믹서 에셋이 없어도 돌아갑니다</b>
    ///
    /// 믹서 그룹을 비워 두면 소스가 기본 출력으로 나가고, 음량은 소스에 직접 걸립니다.
    /// 이 프로젝트는 "에셋이 아직 없어도 재생 버튼만으로 게임이 보여야 한다"를 지켜 왔고,
    /// 오디오도 예외로 두지 않습니다. 나중에 믹서를 붙여도 이 클래스의 공개 API는 그대로입니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AudioManager : MonoBehaviour, IAudioService
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>배경음 소스 오브젝트의 이름입니다.</summary>
        private const string BgmSourceName = "Bgm";

        /// <summary>효과음 소스가 모이는 부모의 이름입니다.</summary>
        private const string SfxRootName = "SfxPool";

        // ====================================================================================================
        // 2. Inspector
        // ====================================================================================================

        [Header("믹서")]
        [SerializeField]
        [Tooltip("배경음이 나갈 믹서 그룹입니다. 비워 두면 기본 출력으로 나갑니다.")]
        private AudioMixerGroup _bgmGroup;

        [SerializeField]
        [Tooltip("효과음이 나갈 믹서 그룹입니다. 비워 두면 기본 출력으로 나갑니다.")]
        private AudioMixerGroup _sfxGroup;

        [Header("효과음 풀")]
        [SerializeField]
        [Range(4, 128)]
        [Tooltip("미리 만들어 둘 효과음 소스 수입니다. 전장에서는 활·근접·피격이 겹치므로 넉넉히 잡습니다.")]
        private int _initialPoolSize = 24;

        [SerializeField]
        [Range(8, 256)]
        [Tooltip("동시에 울릴 수 있는 최대 소리 수입니다. 넘으면 가장 오래된 소리를 끄고 그 자리를 씁니다.")]
        private int _maxVoices = 64;

        [Header("거리 감쇠")]
        [SerializeField]
        [Tooltip("이 거리 안에서는 최대 음량으로 들립니다.")]
        private float _sfxMinDistance = 3f;

        [SerializeField]
        [Tooltip("이 거리를 넘으면 들리지 않습니다. 전장 폭을 생각해 잡습니다.")]
        private float _sfxMaxDistance = 60f;

        // ====================================================================================================
        // 3. Fields
        // ====================================================================================================

        /// <summary>지금 흐르고 있는 배경음 소스입니다. 곡을 넘길 때 보조와 교대합니다.</summary>
        private AudioSource _bgmSource;

        /// <summary>곡을 겹쳐 넘길 때 새 곡이 올라오는 보조 소스입니다.</summary>
        private AudioSource _bgmFadeSource;

        /// <summary>진행 중인 곡 전환입니다. 새 요청이 오면 중단됩니다.</summary>
        private Coroutine _bgmFade;

        /// <summary>지금 곡이 요청한 기본 음량입니다.</summary>
        private float _bgmTrackVolume = 1f;

        /// <summary>설정이 정한 배경음 배율입니다.</summary>
        private float _bgmScale = 1f;

        /// <summary>설정이 정한 효과음 배율입니다.</summary>
        private float _sfxScale = 1f;

        /// <summary>쉬고 있는 효과음 소스입니다.</summary>
        private readonly Queue<AudioSource> _idle = new Queue<AudioSource>();

        /// <summary>울리고 있는 효과음입니다. 오래된 것이 앞에 있습니다.</summary>
        private readonly List<Voice> _voices = new List<Voice>(64);

        /// <summary>효과음 소스가 모이는 부모입니다.</summary>
        private Transform _sfxRoot;

        /// <summary>소스 오브젝트 이름을 겹치지 않게 하는 일련번호입니다.</summary>
        private int _sourceCounter;

        // ====================================================================================================
        // 4. Unity Lifecycle
        // ====================================================================================================

        private void Awake()
        {
            _bgmSource = CreateBgmSource(BgmSourceName);
            _bgmFadeSource = CreateBgmSource(BgmSourceName + "_Fade");

            var rootObject = new GameObject(SfxRootName);
            rootObject.transform.SetParent(transform, false);
            _sfxRoot = rootObject.transform;

            for (int i = 0; i < _initialPoolSize; i++)
            {
                _idle.Enqueue(CreateSfxSource());
            }
        }

        /// <summary>
        /// 다 울린 소리를 거둬들입니다.
        ///
        /// <b>왜 코루틴이 아닌가</b>
        ///
        /// 재생마다 코루틴을 띄우면 <c>WaitForSeconds</c> 객체가 매번 새로 생깁니다.
        /// 전투에서는 이것이 초당 수십 개씩 쌓입니다.
        ///
        /// 더 중요한 것은 <b>시간의 종류</b>입니다. 이 게임은 명령을 내릴 때 시간을 늦춥니다.
        /// 그런데 오디오는 타임스케일을 따르지 않고 실제 시간대로 끝까지 재생됩니다.
        /// 코루틴이 스케일된 시간으로 기다리면, 0.2배속에서 1초짜리 소리가 5초 동안 자리를 잡고 있습니다.
        /// 그러면 슬로우모션에 들어가는 순간 풀이 말라붙습니다 — 정작 소리가 가장 몰리는 때입니다.
        /// </summary>
        private void Update()
        {
            float now = Time.unscaledTime;

            for (int i = _voices.Count - 1; i >= 0; i--)
            {
                if (now < _voices[i].ReleaseTime)
                {
                    continue;
                }

                Recycle(_voices[i].Source);
                _voices.RemoveAt(i);
            }
        }

        // ====================================================================================================
        // 5. Public Methods - BGM
        // ====================================================================================================

        /// <inheritdoc />
        public void PlayBgm(AudioClip clip, bool loop = true, float volume = 1f, float fadeDuration = 1f)
        {
            if (clip == null)
            {
                return;
            }

            // 같은 곡을 다시 요청하는 것은 흔합니다. 그때마다 처음부터 다시 틀면 소리가 끊깁니다.
            if (_bgmSource.clip == clip && _bgmSource.isPlaying)
            {
                return;
            }

            _bgmTrackVolume = Mathf.Clamp01(volume);

            if (_bgmFade != null)
            {
                StopCoroutine(_bgmFade);
                _bgmFade = null;
            }

            if (fadeDuration <= 0f)
            {
                _bgmFadeSource.Stop();

                _bgmSource.clip = clip;
                _bgmSource.loop = loop;
                _bgmSource.volume = _bgmTrackVolume * _bgmScale;
                _bgmSource.Play();

                return;
            }

            _bgmFadeSource.clip = clip;
            _bgmFadeSource.loop = loop;
            _bgmFadeSource.volume = 0f;
            _bgmFadeSource.Play();

            _bgmFade = StartCoroutine(Crossfade(fadeDuration));
        }

        /// <inheritdoc />
        public void StopBgm()
        {
            if (_bgmFade != null)
            {
                StopCoroutine(_bgmFade);
                _bgmFade = null;
            }

            _bgmSource.Stop();
            _bgmFadeSource.Stop();
        }

        // ====================================================================================================
        // 6. Public Methods - SFX
        // ====================================================================================================

        /// <inheritdoc />
        public void PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            Play(clip, Vector3.zero, volume, pitch, false);
        }

        /// <inheritdoc />
        public void PlaySfxAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            Play(clip, position, volume, pitch, true);
        }

        // ====================================================================================================
        // 7. Public Methods - Volume
        // ====================================================================================================

        /// <inheritdoc />
        public void SetMasterVolume(float volume)
        {
            AudioListener.volume = Mathf.Clamp01(volume);
        }

        /// <inheritdoc />
        public void SetBgmVolume(float volume)
        {
            _bgmScale = Mathf.Clamp01(volume);

            // 전환 중이면 코루틴이 매 프레임 목표 음량을 다시 계산하므로 건드리지 않습니다.
            if (_bgmFade == null && _bgmSource != null)
            {
                _bgmSource.volume = _bgmTrackVolume * _bgmScale;
            }
        }

        /// <inheritdoc />
        public void SetSfxVolume(float volume)
        {
            _sfxScale = Mathf.Clamp01(volume);
        }

        // ====================================================================================================
        // 8. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 앞 곡을 내리면서 새 곡을 올리고, 끝나면 두 소스의 역할을 맞바꿉니다.
        /// 일시정지와 슬로우모션 중에도 곡은 제 속도로 넘어가야 하므로 스케일되지 않은 시간을 씁니다.
        /// </summary>
        /// <param name="duration">겹치는 시간입니다.</param>
        private IEnumerator Crossfade(float duration)
        {
            var from = _bgmSource;
            var to = _bgmFadeSource;

            float fromStart = from.volume;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                from.volume = Mathf.Lerp(fromStart, 0f, t);
                to.volume = Mathf.Lerp(0f, _bgmTrackVolume * _bgmScale, t);

                yield return null;
            }

            from.Stop();
            from.clip = null;
            to.volume = _bgmTrackVolume * _bgmScale;

            _bgmSource = to;
            _bgmFadeSource = from;
            _bgmFade = null;
        }

        /// <summary>
        /// 효과음 하나를 실제로 냅니다.
        /// </summary>
        /// <param name="clip">낼 소리입니다.</param>
        /// <param name="position">3차원 재생일 때의 월드 좌표입니다.</param>
        /// <param name="volume">음량입니다.</param>
        /// <param name="pitch">음높이입니다.</param>
        /// <param name="spatial">거리에 따라 작아지게 할지 여부입니다.</param>
        private void Play(AudioClip clip, Vector3 position, float volume, float pitch, bool spatial)
        {
            if (clip == null)
            {
                return;
            }

            var source = Rent();

            if (source == null)
            {
                return;
            }

            source.transform.position = position;
            source.spatialBlend = spatial ? 1f : 0f;
            source.clip = clip;
            source.volume = volume * _sfxScale;
            source.pitch = pitch;

            if (spatial)
            {
                source.minDistance = _sfxMinDistance;
                source.maxDistance = _sfxMaxDistance;
                source.rolloffMode = AudioRolloffMode.Linear;
            }

            source.Play();

            // 음높이를 바꾸면 실제 재생 시간도 그만큼 달라집니다.
            float length = clip.length / Mathf.Max(0.01f, Mathf.Abs(pitch));

            _voices.Add(new Voice
            {
                Source = source,
                ReleaseTime = Time.unscaledTime + length,
            });
        }

        /// <summary>
        /// 쉬고 있는 소스를 빌려 옵니다.
        ///
        /// 풀이 비었으면 상한까지는 새로 만들고, 상한에 닿으면 <b>가장 오래된 소리를 끊습니다.</b>
        /// 무한히 늘리지 않는 이유는 전장에서 소리가 몰리는 순간이 곧 프레임이 가장 빠듯한 순간이기 때문입니다.
        /// 그리고 예순 개가 동시에 울리는 상황에서 가장 먼저 시작한 소리는 이미 거의 끝나 갑니다.
        /// </summary>
        /// <returns>쓸 수 있는 소스입니다.</returns>
        private AudioSource Rent()
        {
            if (_idle.Count > 0)
            {
                var pooled = _idle.Dequeue();
                pooled.gameObject.SetActive(true);
                return pooled;
            }

            if (_voices.Count < _maxVoices)
            {
                var created = CreateSfxSource();
                created.gameObject.SetActive(true);
                return created;
            }

            // 가장 오래된 것을 끊어 자리를 냅니다.
            var oldest = _voices[0].Source;
            _voices.RemoveAt(0);

            oldest.Stop();
            oldest.clip = null;

            return oldest;
        }

        /// <summary>소스를 풀에 돌려놓습니다.</summary>
        /// <param name="source">돌려놓을 소스입니다.</param>
        private void Recycle(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            source.Stop();
            source.clip = null;
            source.gameObject.SetActive(false);

            _idle.Enqueue(source);
        }

        /// <summary>배경음 소스를 하나 만듭니다.</summary>
        /// <param name="objectName">만들 오브젝트의 이름입니다.</param>
        /// <returns>준비된 소스입니다.</returns>
        private AudioSource CreateBgmSource(string objectName)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);

            var source = go.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = _bgmGroup;
            source.playOnAwake = false;
            source.spatialBlend = 0f;

            return source;
        }

        /// <summary>효과음 소스를 하나 만듭니다. 만든 직후에는 꺼진 상태입니다.</summary>
        /// <returns>준비된 소스입니다.</returns>
        private AudioSource CreateSfxSource()
        {
            var go = new GameObject($"Sfx_{_sourceCounter++}");
            go.transform.SetParent(_sfxRoot, false);

            var source = go.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = _sfxGroup;
            source.playOnAwake = false;

            go.SetActive(false);

            return source;
        }

        // ====================================================================================================
        // 9. Nested Types
        // ====================================================================================================

        /// <summary>울리고 있는 소리 하나입니다.</summary>
        private struct Voice
        {
            /// <summary>이 소리를 내고 있는 소스입니다.</summary>
            public AudioSource Source;

            /// <summary>이 시각이 지나면 거둬들입니다. 스케일되지 않은 시간입니다.</summary>
            public float ReleaseTime;
        }
    }
}
