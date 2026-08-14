using System.Collections;
using NUnit.Framework;
using SRPG.Gameplay.CameraControl;
using UnityEngine;
using UnityEngine.TestTools;

namespace SRPG.Tests.PlayMode
{
    /// <summary>
    /// 듣는 귀가 어디에 서는지 확인합니다.
    ///
    /// <b>왜 이것만 따로 검사하는가</b>
    ///
    /// 이 프로젝트에서 소리가 통째로 들리지 않았던 원인이 여기였습니다.
    ///
    /// 직교 투영에서는 카메라를 얼마나 물리든 화면이 달라지지 않습니다. 그래서 리그는
    /// 카메라를 초점에서 수십 미터 뒤에 고정해 둡니다 — 잘려 나가는 것을 막기 위한 값이고
    /// 보이는 그림과는 무관합니다. 그런데 <c>AudioListener</c> 가 그 카메라에 붙어 있으면
    /// <b>그 거리가 소리에는 그대로 남습니다.</b> 화면 한복판의 싸움이 귀에서는 수십 미터 밖입니다.
    ///
    /// 이 어긋남은 <b>화면에 전혀 드러나지 않습니다.</b> 그림은 멀쩡하고 소리만 사라지며,
    /// 증상은 "배선이 안 됐다"와 구별되지 않습니다. 눈으로 잡을 수 없으니 검사로 잡습니다.
    /// </summary>
    public sealed class BattleListenerTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        private GameObject _rigHost;
        private BattleCameraRig _rig;
        private Camera _camera;

        [SetUp]
        public void SetUp()
        {
            _rigHost = new GameObject("BattleCameraRigUnderTest");
            _rig = _rigHost.AddComponent<BattleCameraRig>();

            var cameraHost = new GameObject("BattleCamera");
            _camera = cameraHost.AddComponent<Camera>();
            _camera.orthographic = true;

            _rig.AttachCamera(_camera);
        }

        [TearDown]
        public void TearDown()
        {
            if (_rigHost != null)
            {
                Object.DestroyImmediate(_rigHost);
            }
        }

        /// <summary>리그가 세운 귀를 찾습니다.</summary>
        /// <returns>귀입니다. 없으면 null 입니다.</returns>
        private AudioListener FindListener()
        {
            return _rigHost.GetComponentInChildren<AudioListener>(includeInactive: true);
        }

        // ====================================================================================================
        // 2. Tests
        // ====================================================================================================

        /// <summary>
        /// 귀가 없어도 리그가 하나 세웁니다.
        ///
        /// 전투 씬만 따로 여는 경로에는 귀가 없을 수 있습니다.
        /// 그때 조용한 것은 씬 배선 문제로 보이지만 실제로는 아무도 듣고 있지 않은 것입니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 귀가_없으면_리그가_세운다()
        {
            yield return null;

            Assert.IsNotNull(FindListener(), "듣는 귀가 없어 아무 소리도 들리지 않습니다.");
        }

        /// <summary>
        /// <b>귀는 카메라가 아니라 초점에 섭니다.</b>
        ///
        /// 이것이 이 검사의 전부입니다. 여기가 무너지면 소리가 통째로 사라집니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 귀는_카메라가_아니라_초점에_선다()
        {
            _rigHost.transform.position = new Vector3(17f, 2f, -9f);

            yield return null;

            var listener = FindListener();

            Assert.IsNotNull(listener);
            Assert.AreEqual(
                _rigHost.transform.position,
                listener.transform.position,
                "귀가 초점에서 벗어났습니다. 카메라를 따라갔다면 소리가 수십 미터 밖에서 납니다.");
        }

        /// <summary>
        /// 카메라가 물러난 거리가 소리에 섞이지 않습니다.
        ///
        /// 앞의 검사와 같은 것을 반대편에서 봅니다 — <b>귀와 카메라가 떨어져 있어야</b>
        /// 고쳐진 상태입니다. 둘이 같은 자리면 물러난 거리가 그대로 소리에 실립니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 카메라가_물러난_거리가_소리에_섞이지_않는다()
        {
            yield return null;

            var listener = FindListener();

            Assert.IsNotNull(listener);

            float standoff = Vector3.Distance(_camera.transform.position, listener.transform.position);

            Assert.Greater(standoff, 1f, "귀가 카메라에 붙어 있습니다. 물러난 거리가 소리에 그대로 실립니다.");
        }

        /// <summary>
        /// 초점이 움직이면 귀도 따라갑니다.
        ///
        /// 한 번만 맞추고 마는 배선이면 전투가 시작된 자리에서만 들립니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 초점이_움직이면_귀도_따라간다()
        {
            yield return null;

            _rigHost.transform.position = new Vector3(-40f, 0f, 25f);

            yield return null;

            var listener = FindListener();

            Assert.IsNotNull(listener);
            Assert.AreEqual(new Vector3(-40f, 0f, 25f), listener.transform.position);
        }

        /// <summary>
        /// <b>자세는 카메라의 것을 씁니다.</b>
        ///
        /// 좌우 정위는 귀가 어디를 향하는지로 정해집니다. 자세를 두지 않으면
        /// 궤도를 돌려도 정위가 월드 축에 묶여, 화면 왼쪽의 싸움이 오른쪽에서 들립니다.
        /// </summary>
        [UnityTest]
        public IEnumerator 귀는_카메라와_같은_곳을_바라본다()
        {
            yield return null;

            var listener = FindListener();

            Assert.IsNotNull(listener);

            float alignment = Vector3.Dot(listener.transform.forward, _camera.transform.forward);

            Assert.Greater(alignment, 0.999f, "귀와 카메라가 다른 곳을 봅니다. 좌우 정위가 화면과 어긋납니다.");
        }
    }
}
