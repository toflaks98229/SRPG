using System.Collections.Generic;
using NUnit.Framework;
using SRPG.Gameplay.Visual;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 접지 그림자를 검증합니다.
    ///
    /// <b>왜 필요한가</b>
    ///
    /// 빌보드는 평면이라 지면과 닿는 면이 없습니다.
    /// 방향광 그림자만으로는 유닛이 어디에 서 있는지 읽히지 않고,
    /// 카메라를 돌리면 스프라이트가 배경 위에 떠 있는 것처럼 보입니다.
    ///
    /// 그림자가 <b>주인을 정확히 따라다니는지</b>, 그리고 <b>주인의 회전에 휘둘리지 않는지</b>가 핵심입니다.
    /// 둘 중 하나만 어긋나도 접지가 아니라 노이즈가 됩니다.
    /// </summary>
    public sealed class ContactShadowTests
    {
        // ====================================================================================================
        // 1. Setup
        // ====================================================================================================

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();
        }

        /// <summary>
        /// 주인과 그림자를 만듭니다. 셰이더가 없으면 테스트를 건너뜁니다.
        /// </summary>
        private ContactShadow CreateShadow(Vector3 ownerPosition, out Transform owner)
        {
            var ownerObject = new GameObject("Owner");
            _spawned.Add(ownerObject);

            ownerObject.transform.position = ownerPosition;
            owner = ownerObject.transform;

            var root = new GameObject("Shadows");
            _spawned.Add(root);

            // 격자는 넘기지 않습니다. 여기서 보려는 것은 지형 샘플링이 아니라 추종과 독립성입니다.
            var shadow = ContactShadow.Attach(owner, null, radius: 0.4f, parent: root.transform);

            if (shadow == null)
            {
                Assert.Ignore($"셰이더 '{PrototypeVisuals.ContactShadowShaderName}' 를 찾지 못했습니다.");
            }

            return shadow;
        }

        // ====================================================================================================
        // 2. 추종
        // ====================================================================================================

        [Test]
        public void 만들자마자_주인의_발밑에_놓인다()
        {
            var shadow = CreateShadow(new Vector3(3f, 1.5f, -2f), out _);

            Vector3 position = shadow.transform.position;

            // 첫 프레임부터 제자리여야 합니다. LateUpdate를 기다리면 원점에서 한 번 깜빡입니다.
            Assert.AreEqual(3f, position.x, 0.001f);
            Assert.AreEqual(-2f, position.z, 0.001f);
        }

        [Test]
        public void 주인이_움직이면_따라간다()
        {
            var shadow = CreateShadow(Vector3.zero, out var owner);

            owner.position = new Vector3(7f, 2f, 4f);
            shadow.SyncPosition();

            Assert.AreEqual(7f, shadow.transform.position.x, 0.001f);
            Assert.AreEqual(4f, shadow.transform.position.z, 0.001f);
        }

        /// <summary>
        /// 지면과 정확히 같은 높이면 z-파이팅으로 그림자가 지글거립니다.
        /// 반대로 너무 띄우면 계단에서 공중에 뜬 판이 보입니다.
        /// </summary>
        [Test]
        public void 지면에서_아주_조금만_띄운다()
        {
            var shadow = CreateShadow(new Vector3(0f, 5f, 0f), out _);

            float lift = shadow.transform.position.y - 5f;

            Assert.Greater(lift, 0f, "지면과 같은 높이입니다. z-파이팅이 납니다.");
            Assert.Less(lift, 0.1f, "너무 띄웠습니다. 그림자가 공중에 뜬 판으로 보입니다.");
        }

        // ====================================================================================================
        // 3. 주인으로부터의 독립
        // ====================================================================================================

        /// <summary>
        /// 유닛의 자식이면 유닛의 회전을 함께 받습니다.
        /// 타원이 유닛을 따라 돌면 접지가 아니라 회전하는 판때기로 보입니다.
        /// </summary>
        [Test]
        public void 주인의_자식이_아니다()
        {
            var shadow = CreateShadow(Vector3.zero, out var owner);

            Assert.AreNotSame(owner, shadow.transform.parent);
            Assert.IsFalse(shadow.transform.IsChildOf(owner), "그림자가 유닛의 자식입니다. 유닛과 함께 회전합니다.");
        }

        [Test]
        public void 주인이_돌아도_그림자는_눕힌_채로_있다()
        {
            var shadow = CreateShadow(Vector3.zero, out var owner);

            Quaternion before = shadow.transform.rotation;

            owner.rotation = Quaternion.Euler(0f, 137f, 0f);
            shadow.SyncPosition();

            Assert.AreEqual(0f, Quaternion.Angle(before, shadow.transform.rotation), 0.001f);
        }

        /// <summary>
        /// 그림자가 그림자를 드리우면 유닛 발밑이 두 겹으로 어두워집니다.
        /// </summary>
        [Test]
        public void 그림자는_그림자를_드리우지_않는다()
        {
            var shadow = CreateShadow(Vector3.zero, out _);
            var renderer = shadow.GetComponent<Renderer>();

            Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, renderer.shadowCastingMode);
            Assert.IsFalse(renderer.receiveShadows);
        }

        // ====================================================================================================
        // 4. 공유
        // ====================================================================================================

        /// <summary>
        /// 유닛마다 머티리얼을 새로 만들면 수백 개가 생겨 배칭이 통째로 깨집니다.
        /// 그림자는 전부 같은 모습이므로 하나로 충분합니다.
        /// </summary>
        [Test]
        public void 모든_그림자가_같은_머티리얼을_쓴다()
        {
            var first = CreateShadow(Vector3.zero, out _);
            var second = CreateShadow(Vector3.one, out _);

            Assert.AreSame(
                first.GetComponent<Renderer>().sharedMaterial,
                second.GetComponent<Renderer>().sharedMaterial);
        }

        // ====================================================================================================
        // 5. 예외
        // ====================================================================================================

        [Test]
        public void 주인이_없으면_만들지_않는다()
        {
            var root = new GameObject("Shadows");
            _spawned.Add(root);

            Assert.IsNull(ContactShadow.Attach(null, null, 0.4f, root.transform));
        }
    }
}
