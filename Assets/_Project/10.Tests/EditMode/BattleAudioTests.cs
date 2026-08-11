using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Core;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 전장의 소리 배선을 검증합니다.
    ///
    /// <b>무엇을 볼 수 있고 무엇을 볼 수 없는가</b>
    ///
    /// 소리가 실제로 <b>들리는지</b>는 검사가 알 수 없습니다. 스피커까지 가는 길은
    /// 유니티 오디오 엔진이고, 배치 실행에는 그것이 아예 없습니다.
    ///
    /// 대신 그 직전까지는 전부 볼 수 있습니다 — 어떤 클립을, 어느 자리에서,
    /// 얼마나 크게 내라고 <b>창구에 넘겼는가</b>입니다. 배선이 끊기면 여기가 먼저 무너집니다.
    ///
    /// 이 검사가 지키는 것은 하나입니다. <b>"연결했는데 안 들린다"의 원인을 가릴 수 있어야 한다.</b>
    /// 클립이 하나도 없는 상태에서도 소리 요청 자체는 나가야, 안 들리는 이유가
    /// 배선이 아니라 클립(또는 그 너머)이라고 말할 수 있습니다.
    /// </summary>
    public sealed class BattleAudioTests
    {
        // ====================================================================================================
        // 1. Fake
        // ====================================================================================================

        /// <summary>
        /// 넘어온 재생 요청을 그대로 적어 두는 가짜 창구입니다.
        ///
        /// 배경음과 음량 설정은 전장의 소리와 무관하므로 받기만 하고 버립니다.
        /// </summary>
        private sealed class RecordingAudioService : IAudioService
        {
            /// <summary>한 번의 재생 요청입니다.</summary>
            public readonly struct Call
            {
                public Call(AudioClip clip, Vector3 position, float volume, float pitch)
                {
                    Clip = clip;
                    Position = position;
                    Volume = volume;
                    Pitch = pitch;
                }

                /// <summary>재생하라고 넘어온 클립입니다.</summary>
                public AudioClip Clip { get; }

                /// <summary>소리가 날 자리입니다.</summary>
                public Vector3 Position { get; }

                /// <summary>넘어온 음량입니다.</summary>
                public float Volume { get; }

                /// <summary>넘어온 음높이입니다.</summary>
                public float Pitch { get; }
            }

            /// <summary>들어온 요청이 순서대로 쌓입니다.</summary>
            public List<Call> Calls { get; } = new List<Call>();

            public void PlayBgm(AudioClip clip, bool loop = true, float volume = 1f, float fadeDuration = 1f)
            {
            }

            public void StopBgm()
            {
            }

            public void PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f)
            {
                Calls.Add(new Call(clip, Vector3.zero, volume, pitch));
            }

            public void PlaySfxAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
            {
                Calls.Add(new Call(clip, position, volume, pitch));
            }

            public void SetMasterVolume(float volume)
            {
            }

            public void SetBgmVolume(float volume)
            {
            }

            public void SetSfxVolume(float volume)
            {
            }
        }

        // ====================================================================================================
        // 2. Fixture
        // ====================================================================================================

        private RecordingAudioService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new RecordingAudioService();
        }

        // ====================================================================================================
        // 3. Tests - Fallback
        // ====================================================================================================

        /// <summary>
        /// 뱅크가 아예 없어도 소리 요청은 나갑니다.
        ///
        /// 이것이 없으면 클립을 넣기 전까지 배선이 도는지 확인할 방법이 없습니다.
        /// </summary>
        [Test]
        public void 뱅크가_없어도_소리를_낸다()
        {
            var audio = new BattleAudio(_service, null);

            audio.PlayHit(DamageType.Slash, Vector3.zero);

            Assert.AreEqual(1, _service.Calls.Count);
            Assert.IsNotNull(_service.Calls[0].Clip, "합성음이 그 자리를 메워야 합니다.");
        }

        /// <summary>
        /// 뱅크는 있는데 <b>그 칸만</b> 비어 있어도 소리는 납니다.
        ///
        /// 실제 소리는 한 번에 다 들어오지 않습니다. 타격음만 먼저 넣은 상태가 정상이고,
        /// 그때 뱅크가 연결되었다는 이유로 나머지가 통째로 조용해지면
        /// "이것만 안 난다"가 다시 배선 문제로 보이기 시작합니다.
        /// </summary>
        [Test]
        public void 뱅크의_빈_칸은_합성음이_메운다()
        {
            var bank = ScriptableObject.CreateInstance<BattleAudioBank>();

            try
            {
                bank.Slash = MakeClip("실제_베기");

                var audio = new BattleAudio(_service, bank);

                audio.PlayHit(DamageType.Slash, Vector3.zero);
                audio.PlayDeath(Vector3.zero);

                Assert.AreEqual("실제_베기", _service.Calls[0].Clip.name, "꽂힌 클립이 우선입니다.");
                Assert.IsNotNull(_service.Calls[1].Clip, "비어 있는 칸도 소리가 나야 합니다.");
                Assert.AreNotEqual("실제_베기", _service.Calls[1].Clip.name);
            }
            finally
            {
                Object.DestroyImmediate(bank);
            }
        }

        // ====================================================================================================
        // 4. Tests - Selection
        // ====================================================================================================

        /// <summary>
        /// 피해 성질마다 다른 소리를 고릅니다.
        ///
        /// 이것이 이 클래스가 존재하는 이유입니다. 무기마다 자기 클립을 들고 있으면
        /// 성질과 소리의 대응이 무기 수만큼 흩어집니다.
        /// </summary>
        [Test]
        public void 피해_성질마다_다른_소리를_고른다()
        {
            var bank = ScriptableObject.CreateInstance<BattleAudioBank>();

            try
            {
                bank.Slash = MakeClip("베기");
                bank.Pierce = MakeClip("자돌");
                bank.Blunt = MakeClip("타격");

                var audio = new BattleAudio(_service, bank);

                audio.PlayHit(DamageType.Slash, Vector3.zero);
                audio.PlayHit(DamageType.Pierce, Vector3.zero);
                audio.PlayHit(DamageType.Blunt, Vector3.zero);

                Assert.AreEqual("베기", _service.Calls[0].Clip.name);
                Assert.AreEqual("자돌", _service.Calls[1].Clip.name);
                Assert.AreEqual("타격", _service.Calls[2].Clip.name);
            }
            finally
            {
                Object.DestroyImmediate(bank);
            }
        }

        /// <summary>
        /// 소리는 <b>그 자리에서</b> 납니다.
        ///
        /// 전장은 넓고 카메라는 그 위를 돕니다. 화면 전체에 같은 크기로 울리면
        /// 어디서 싸움이 벌어지는지 귀로 알 수 없습니다.
        /// </summary>
        [Test]
        public void 소리는_넘겨받은_자리에서_난다()
        {
            var audio = new BattleAudio(_service, null);
            var where = new Vector3(12f, 3f, -7f);

            audio.PlayShot(where);

            Assert.AreEqual(where, _service.Calls[0].Position);
        }

        // ====================================================================================================
        // 5. Tests - Jitter
        // ====================================================================================================

        /// <summary>
        /// 같은 소리를 반복해도 음높이가 매번 같지는 않습니다.
        ///
        /// 전장에서는 같은 소리가 초당 수십 번 겹칩니다. 그대로 겹치면 사람 귀에
        /// 한 번의 기계음으로 뭉칩니다.
        ///
        /// <b>모두 다르다고 주장하지 않습니다.</b> 난수는 같은 값을 두 번 낼 수 있습니다.
        /// 여기서 확인하는 것은 "흔들리고 있는가"이므로 서로 다른 값이 <b>여럿</b>이면 충분합니다.
        /// </summary>
        [Test]
        public void 같은_소리를_반복해도_음높이가_흔들린다()
        {
            var audio = new BattleAudio(_service, null);

            for (int i = 0; i < 16; i++)
            {
                audio.PlayHit(DamageType.Slash, Vector3.zero);
            }

            var pitches = new HashSet<float>();

            foreach (var call in _service.Calls)
            {
                pitches.Add(call.Pitch);
            }

            Assert.Greater(pitches.Count, 8, "음높이가 사실상 고정되어 있습니다.");
        }

        /// <summary>
        /// 흔들림에는 상한이 있습니다. 뱅크가 정한 폭을 벗어나면 소리의 정체가 바뀝니다.
        /// </summary>
        [Test]
        public void 음높이_흔들림은_뱅크가_정한_폭을_넘지_않는다()
        {
            var bank = ScriptableObject.CreateInstance<BattleAudioBank>();

            try
            {
                bank.PitchJitter = 0.1f;

                var audio = new BattleAudio(_service, bank);

                for (int i = 0; i < 64; i++)
                {
                    audio.PlayHit(DamageType.Slash, Vector3.zero);
                }

                foreach (var call in _service.Calls)
                {
                    Assert.GreaterOrEqual(call.Pitch, 0.9f);
                    Assert.LessOrEqual(call.Pitch, 1.1f);
                }
            }
            finally
            {
                Object.DestroyImmediate(bank);
            }
        }

        // ====================================================================================================
        // 6. Tests - Absence
        // ====================================================================================================

        /// <summary>
        /// 창구가 없어도 터지지 않습니다.
        ///
        /// 전투 씬만 따로 여는 경로(편집 중의 실행, 자동 검사)에는 위층이 없어
        /// 소리를 낼 창구 자체가 없습니다. 그때 조용한 것은 결함이 아닙니다.
        /// </summary>
        [Test]
        public void 창구가_없으면_조용히_넘어간다()
        {
            var audio = new BattleAudio(null, null);

            Assert.DoesNotThrow(() =>
            {
                audio.PlayHit(DamageType.Slash, Vector3.zero);
                audio.PlayShot(Vector3.zero);
                audio.PlayDeath(Vector3.zero);
            });
        }

        /// <summary>
        /// 아무것도 하지 않는 구현은 정말로 아무것도 하지 않습니다.
        /// </summary>
        [Test]
        public void 침묵_구현은_창구를_부르지_않는다()
        {
            IBattleAudio audio = SilentBattleAudio.Instance;

            audio.PlayHit(DamageType.Blunt, Vector3.one);
            audio.PlayShot(Vector3.one);
            audio.PlayDeath(Vector3.one);

            Assert.AreEqual(0, _service.Calls.Count);
        }

        // ====================================================================================================
        // 7. Helpers
        // ====================================================================================================

        /// <summary>
        /// 이름만 확인하면 되는 자리에 쓸 최소 클립을 만듭니다.
        /// </summary>
        /// <param name="clipName">붙일 이름입니다. 검증은 이 이름으로 합니다.</param>
        /// <returns>표본 하나짜리 클립입니다.</returns>
        private static AudioClip MakeClip(string clipName)
        {
            var clip = AudioClip.Create(clipName, 1, 1, 22050, false);
            clip.hideFlags = HideFlags.HideAndDontSave;

            return clip;
        }
    }
}
