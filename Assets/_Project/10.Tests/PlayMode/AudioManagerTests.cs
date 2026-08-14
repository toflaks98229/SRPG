using System.Collections;
using NUnit.Framework;
using SRPG.Common;
using SRPG.Core.Managers;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using UnityEngine;
using UnityEngine.TestTools;

namespace SRPG.Tests.PlayMode
{
    /// <summary>
    /// 소리가 실제로 소스까지 닿는지 확인합니다.
    ///
    /// <b>왜 이 검사가 필요한가</b>
    ///
    /// "소리가 안 난다"는 원인이 여러 층에 걸쳐 있습니다 — 뱅크에 클립이 없거나,
    /// 창구가 연결되지 않았거나, 풀이 비었거나, 음량이 0이거나, 거리 감쇠에 묻혔거나.
    /// <b>어느 층에서 끊겼는지 귀로는 구분되지 않습니다.</b> 전부 같은 증상을 냅니다.
    ///
    /// 여기서는 소스의 상태를 직접 들여다봅니다. 클립이 꽂혔고 음량이 0보다 크면
    /// 매니저까지는 온 것이고, 그다음은 장치나 감쇠의 문제입니다.
    ///
    /// <b>재생 여부를 묻지 않습니다.</b> 배치 모드(<c>-nographics</c>)에는 소리 장치가 없어
    /// <c>isPlaying</c> 이 미덥지 않습니다. 소스에 무엇이 실렸는지는 장치와 무관하게 확인됩니다.
    /// </summary>
    public sealed class AudioManagerTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        private GameObject _host;
        private AudioManager _manager;
        private AudioClip _clip;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("AudioManagerUnderTest");
            _manager = _host.AddComponent<AudioManager>();

            _clip = AudioClip.Create("TestClip", 4410, 1, 44100, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }

            if (_clip != null)
            {
                Object.DestroyImmediate(_clip);
            }
        }

        /// <summary>풀에서 실제로 무언가를 실은 소스를 찾습니다.</summary>
        /// <returns>클립이 실린 소스입니다. 없으면 null입니다.</returns>
        private AudioSource FindLoadedSource()
        {
            foreach (var source in _host.GetComponentsInChildren<AudioSource>(includeInactive: true))
            {
                if (source.clip != null)
                {
                    return source;
                }
            }

            return null;
        }

        // ====================================================================================================
        // 2. Tests - 소리가 소스까지 닿는다
        // ====================================================================================================

        /// <summary>
        /// 효과음 요청이 소스에 실립니다.
        ///
        /// 여기서 실패하면 매니저 안쪽(풀·음량)의 문제이고, 통과하면 그 바깥(장치·감쇠)의 문제입니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 효과음이_소스에_실린다()
        {
            yield return null;

            _manager.PlaySfxAt(_clip, Vector3.zero);

            var source = FindLoadedSource();

            Assert.IsNotNull(source, "어떤 소스에도 클립이 실리지 않았습니다. 풀이 비었거나 대여가 실패했습니다.");
            Assert.AreSame(_clip, source.clip);
            Assert.Greater(source.volume, 0f, "소스에 실렸지만 음량이 0입니다.");
            Assert.IsTrue(source.gameObject.activeInHierarchy, "소스 오브젝트가 꺼져 있어 소리가 나지 않습니다.");
        }

        /// <summary>
        /// 자리에서 나는 소리는 <b>3D</b> 여야 하고, 감쇠 범위가 잡혀 있어야 합니다.
        ///
        /// <c>spatialBlend</c> 가 0이면 어디서 싸우든 같은 크기로 들려
        /// "귀로 전황을 읽는다"가 성립하지 않습니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 자리에서_나는_소리는_3D_로_설정된다()
        {
            yield return null;

            _manager.PlaySfxAt(_clip, new Vector3(5f, 0f, 5f));

            var source = FindLoadedSource();

            Assert.IsNotNull(source);
            Assert.AreEqual(1f, source.spatialBlend, 1e-3f, "자리에서 나는 소리가 2D 로 설정되었습니다.");
            Assert.Greater(source.maxDistance, source.minDistance, "감쇠 범위가 뒤집혀 있습니다.");
            Assert.AreEqual(new Vector3(5f, 0f, 5f), source.transform.position);
        }

        /// <summary>
        /// 화면 전체에 울리는 소리는 <b>2D</b> 여야 합니다. 거리에 묻히면 안 되는 소리입니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 자리가_없는_소리는_2D_로_설정된다()
        {
            yield return null;

            _manager.PlaySfx(_clip);

            var source = FindLoadedSource();

            Assert.IsNotNull(source);
            Assert.AreEqual(0f, source.spatialBlend, 1e-3f, "화면 소리가 3D 로 설정되어 거리에 묻힙니다.");
        }

        // ====================================================================================================
        // 3. Tests - 전투 창구를 거쳐도 닿는다
        // ====================================================================================================

        /// <summary>
        /// <see cref="BattleAudio"/> 를 거친 요청도 소스까지 닿습니다.
        ///
        /// 뱅크의 칸이 비어 있어도 합성음이 메우므로 <b>언제나</b> 소리가 실려야 합니다.
        /// 여기서 실패하면 뱅크가 아니라 창구 배선이 끊긴 것입니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 전투_창구를_거친_타격음이_소스에_실린다()
        {
            yield return null;

            var battleAudio = new BattleAudio(_manager, bank: null);

            battleAudio.PlayHit(DamageType.Slash, Vector3.zero);

            var source = FindLoadedSource();

            Assert.IsNotNull(source, "전투 창구를 거친 소리가 소스에 실리지 않았습니다.");
            Assert.Greater(source.volume, 0f);
        }

        /// <summary>
        /// 뱅크에 실제 클립이 꽂혀 있으면 <b>그 클립</b>이 실립니다.
        ///
        /// 합성음이 대신 나오면 배선이 된 것처럼 보이면서 실제로는 안 된 상태가 됩니다 —
        /// 소리는 나므로 귀로는 구분되지 않습니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 뱅크에_꽂힌_클립이_그대로_쓰인다()
        {
            yield return null;

            var bank = ScriptableObject.CreateInstance<BattleAudioBank>();
            bank.hideFlags = HideFlags.HideAndDontSave;
            bank.Slash = _clip;

            try
            {
                var battleAudio = new BattleAudio(_manager, bank);

                battleAudio.PlayHit(DamageType.Slash, Vector3.zero);

                var source = FindLoadedSource();

                Assert.IsNotNull(source);
                Assert.AreSame(_clip, source.clip, "뱅크에 꽂은 클립 대신 다른 소리가 났습니다.");
            }
            finally
            {
                Object.DestroyImmediate(bank);
            }
        }

        // ====================================================================================================
        // 4. Tests - 거리 감쇠
        //
        // <b>여기가 실제로 소리를 삼켰던 자리입니다.</b>
        // 기본값이 "3미터 안은 최대 음량, 60미터 밖은 무음"이었는데 카메라는 8~30미터 물러나 있어서,
        // 화면 한복판의 싸움이 이미 감쇠 구간이었습니다. 실효 음량 14~26% 는
        // "소리가 안 난다"와 귀로 구분되지 않습니다.
        // ====================================================================================================

        /// <summary>알려 준 감쇠 범위가 소스에 걸립니다.</summary>
        [UnityTest]
        public IEnumerator 알려_준_감쇠_범위가_소스에_걸린다()
        {
            yield return null;

            _manager.SetSfxDistances(40f, 160f);
            _manager.PlaySfxAt(_clip, Vector3.zero);

            var source = FindLoadedSource();

            Assert.IsNotNull(source);
            Assert.AreEqual(40f, source.minDistance, 1e-3f);
            Assert.AreEqual(160f, source.maxDistance, 1e-3f);
        }

        /// <summary>
        /// 범위가 뒤집혀 들어와도 최소보다 크게 유지합니다.
        ///
        /// 뒤집히면 유니티가 거리를 무시해 어디서 나든 같은 크기로 들립니다 —
        /// 자리에서 내는 의미가 통째로 사라지는데, 그것도 조용히 일어납니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 감쇠_범위가_뒤집히지_않는다()
        {
            yield return null;

            _manager.SetSfxDistances(80f, 10f);
            _manager.PlaySfxAt(_clip, Vector3.zero);

            var source = FindLoadedSource();

            Assert.IsNotNull(source);
            Assert.Greater(source.maxDistance, source.minDistance);
        }

        /// <summary>
        /// <b>이미 울리고 있는 소리에도 겁니다.</b>
        ///
        /// 다음 소리부터 적용하면 전투가 열린 직후 몇 초가 옛 범위로 나는데,
        /// 하필 그때가 첫인상입니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 울리는_중인_소리에도_새_범위가_걸린다()
        {
            yield return null;

            _manager.PlaySfxAt(_clip, Vector3.zero);
            _manager.SetSfxDistances(50f, 200f);

            var source = FindLoadedSource();

            Assert.IsNotNull(source);
            Assert.AreEqual(50f, source.minDistance, 1e-3f, "이미 울리던 소리가 옛 범위 그대로입니다.");
        }

        // ====================================================================================================
        // 5. Tests - 음량
        // ====================================================================================================

        /// <summary>
        /// 효과음 배율이 소스 음량에 걸립니다. 0으로 두면 조용해집니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 효과음_배율이_음량에_걸린다()
        {
            yield return null;

            _manager.SetSfxVolume(0f);
            _manager.PlaySfxAt(_clip, Vector3.zero);

            var muted = FindLoadedSource();

            Assert.IsNotNull(muted);
            Assert.AreEqual(0f, muted.volume, 1e-3f, "배율을 0으로 두었는데 음량이 남아 있습니다.");
        }

        /// <summary>
        /// <b>기본 배율은 1이어야 합니다.</b>
        ///
        /// 아무도 설정을 건드리지 않은 상태가 정상 경로입니다.
        /// 여기가 0이면 프로젝트 전체가 조용한데 원인은 아무 데도 드러나지 않습니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 아무것도_설정하지_않으면_소리가_난다()
        {
            yield return null;

            _manager.PlaySfxAt(_clip, Vector3.zero, volume: 1f);

            var source = FindLoadedSource();

            Assert.IsNotNull(source);
            Assert.AreEqual(1f, source.volume, 1e-3f, "기본 상태에서 효과음 배율이 1이 아닙니다.");
        }
    }
}
