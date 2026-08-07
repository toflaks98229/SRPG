using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Gameplay.Weapons;
using UnityEngine;
using UnityEngine.TestTools;

namespace SRPG.Tests
{
    /// <summary>
    /// 화살 풀을 검증합니다.
    ///
    /// 풀의 존재 이유는 <b>재사용</b>입니다. 그런데 재사용은 눈에 보이지 않습니다.
    /// 풀이 실제로는 매번 새로 만들고 있어도 게임은 똑같이 굴러가고, 다만 GC가 계속 돌 뿐입니다.
    /// 그래서 "같은 인스턴스가 돌아오는가"와 "몇 개나 만들었는가"를 직접 확인합니다.
    ///
    /// 프리팹별 분리도 함께 봅니다. 키를 무시하고 한 덩어리로 묶으면
    /// 궁수가 다른 병과의 투사체를 쏘는데, 그 증상은 한참 뒤에야 눈에 띕니다.
    /// </summary>
    public sealed class ProjectilePoolTests
    {
        // ====================================================================================================
        // 1. Setup / Teardown
        // ====================================================================================================

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private ProjectilePool _pool;
        private Transform _root;

        [SetUp]
        public void SetUp()
        {
            _pool = new ProjectilePool();

            // 풀이 씬 루트에 오브젝트를 만들지 않도록 미리 부모를 줍니다.
            // (아직 루트가 없는 상태에서 부르므로 Destroy 경로를 타지 않습니다)
            _root = Track(new GameObject("TestArrowRoot")).transform;
            _pool.SetRoot(_root);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();
        }

        private GameObject Track(GameObject go)
        {
            _spawned.Add(go);
            return go;
        }

        /// <summary>화살 프리팹 역할을 할 오브젝트를 만듭니다.</summary>
        private GameObject CreateArrowPrefab(string prefabName)
        {
            var go = Track(new GameObject(prefabName));
            go.AddComponent<Arrow>();
            go.SetActive(false);
            return go;
        }

        // ====================================================================================================
        // 2. 기본 대여
        // ====================================================================================================

        [Test]
        public void 빌린_화살은_활성_상태이고_지정한_위치에_있다()
        {
            var prefab = CreateArrowPrefab("Arrow_Test");
            var position = new Vector3(1f, 2f, 3f);

            var arrow = _pool.Rent(prefab, position, Quaternion.identity);
            Track(arrow.gameObject);

            Assert.IsNotNull(arrow);
            Assert.IsTrue(arrow.gameObject.activeSelf, "빌린 화살이 꺼져 있습니다.");
            Assert.AreEqual(position, arrow.transform.position);
        }

        [Test]
        public void 프리팹이_null이면_null을_돌려준다()
        {
            Assert.IsNull(_pool.Rent(null, Vector3.zero, Quaternion.identity));
        }

        [Test]
        public void 화살_컴포넌트가_없는_프리팹은_null을_돌려주고_소리를_낸다()
        {
            var broken = Track(new GameObject("Broken"));
            broken.SetActive(false);

            // 잘못된 프리팹을 만든 인스턴스는 풀이 Object.Destroy 로 치웁니다.
            // 런타임에서는 옳지만 EditMode에서는 유니티가 별도 에러를 남기므로,
            // 확인하려는 메시지만 명시하고 나머지 잡음은 무시합니다.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Arrow 컴포넌트가 없습니다"));
            LogAssert.ignoreFailingMessages = true;

            Assert.IsNull(_pool.Rent(broken, Vector3.zero, Quaternion.identity));
        }

        // ====================================================================================================
        // 3. 재사용
        // ====================================================================================================

        /// <summary><b>이 테스트가 풀의 존재 이유입니다.</b></summary>
        [Test]
        public void 반납한_화살을_다시_빌리면_같은_인스턴스다()
        {
            var prefab = CreateArrowPrefab("Arrow_Test");

            var first = _pool.Rent(prefab, Vector3.zero, Quaternion.identity);
            Track(first.gameObject);

            _pool.Return(first);

            Assert.IsFalse(first.gameObject.activeSelf, "반납한 화살이 켜진 채입니다.");

            var second = _pool.Rent(prefab, Vector3.one, Quaternion.identity);

            Assert.AreSame(first, second, "반납한 화살이 재사용되지 않았습니다.");
            Assert.IsTrue(second.gameObject.activeSelf, "재사용한 화살이 켜지지 않았습니다.");
            Assert.AreEqual(1, _pool.CreatedCount, "재사용 가능한 화살이 있는데 새로 만들었습니다.");
        }

        [Test]
        public void 반납하지_않은_화살은_재사용되지_않는다()
        {
            var prefab = CreateArrowPrefab("Arrow_Test");

            var first = Track(_pool.Rent(prefab, Vector3.zero, Quaternion.identity).gameObject);
            var second = Track(_pool.Rent(prefab, Vector3.zero, Quaternion.identity).gameObject);

            Assert.AreNotSame(first, second, "동시에 쓰이는 화살이 같은 인스턴스입니다.");
            Assert.AreEqual(2, _pool.CreatedCount);
        }

        /// <summary>
        /// 풀이 안정되면 더 이상 만들지 않아야 합니다.
        /// 최고 동시 사용량이 3이면, 그 뒤로는 몇 번을 쏘든 3개로 돌려막습니다.
        /// </summary>
        [Test]
        public void 최고_동시_사용량만큼만_만든다()
        {
            var prefab = CreateArrowPrefab("Arrow_Test");
            var inFlight = new List<Arrow>();

            for (int volley = 0; volley < 10; volley++)
            {
                for (int i = 0; i < 3; i++)
                {
                    var arrow = _pool.Rent(prefab, Vector3.zero, Quaternion.identity);
                    Track(arrow.gameObject);
                    inFlight.Add(arrow);
                }

                for (int i = 0; i < inFlight.Count; i++)
                {
                    _pool.Return(inFlight[i]);
                }

                inFlight.Clear();
            }

            Assert.AreEqual(3, _pool.CreatedCount, "풀이 안정된 뒤에도 새 화살을 만들고 있습니다.");
            Assert.AreEqual(3, _pool.IdleCount);
        }

        // ====================================================================================================
        // 4. 프리팹 분리
        // ====================================================================================================

        [Test]
        public void 프리팹이_다르면_서로_섞이지_않는다()
        {
            var arrowPrefab = CreateArrowPrefab("Arrow_Normal");
            var boltPrefab = CreateArrowPrefab("Arrow_Bolt");

            var arrow = _pool.Rent(arrowPrefab, Vector3.zero, Quaternion.identity);
            Track(arrow.gameObject);
            _pool.Return(arrow);

            var bolt = _pool.Rent(boltPrefab, Vector3.zero, Quaternion.identity);
            Track(bolt.gameObject);

            Assert.AreNotSame(arrow, bolt, "다른 프리팹의 화살이 재사용되었습니다.");
            Assert.AreEqual(2, _pool.CreatedCount);

            // 원래 프리팹으로 다시 빌리면 반납해 둔 것이 나와야 합니다.
            var arrowAgain = _pool.Rent(arrowPrefab, Vector3.zero, Quaternion.identity);
            Assert.AreSame(arrow, arrowAgain);
        }

        // ====================================================================================================
        // 5. 방어적 동작
        // ====================================================================================================

        [Test]
        public void null을_반납해도_터지지_않는다()
        {
            Assert.DoesNotThrow(() => _pool.Return(null));
        }
    }
}
