using NUnit.Framework;
using SRPG.Data;
using SRPG.Gameplay.Visual;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// C#이 셰이더에 넘기는 값들이 실제로 도착하는지 검증합니다.
    ///
    /// <b>왜 테스트하는가</b>
    ///
    /// <see cref="Material.SetFloat(string, float)"/>는 <b>없는 프로퍼티에 써도 아무 말이 없습니다</b>.
    /// 예외도, 경고도, 로그도 없습니다. 그냥 조용히 사라집니다.
    ///
    /// 그래서 셰이더에서 프로퍼티 이름을 하나 바꾸면 C# 쪽은 계속 컴파일되고 계속 돌아가는데,
    /// 화면만 기본값으로 나옵니다. 해수면이 0으로 고정돼 물가 띠가 엉뚱한 높이에 그려져도
    /// 아무도 오류를 보지 못합니다.
    ///
    /// 여기서 그 침묵을 깨뜨립니다.
    /// </summary>
    public sealed class BattleShaderContractTests
    {
        // ====================================================================================================
        // 1. 지형
        // ====================================================================================================

        /// <summary>
        /// 이 테스트가 이 파일의 핵심입니다.
        ///
        /// 지형 셰이더는 해수면·고도폭·등반 한계를 <b>전장마다 다르게</b> 받아야 합니다.
        /// 셋 중 하나라도 이름이 어긋나면 그 값만 셰이더 기본값으로 굳습니다.
        /// </summary>
        [Test]
        public void 지형_머티리얼이_전장의_숫자를_받는다()
        {
            var material = PrototypeVisuals.CreateTerrainMaterial(null, 12.5f, 30f, 41f);

            if (material == null)
            {
                Assert.Ignore($"셰이더 '{PrototypeVisuals.TerrainShaderName}' 를 찾지 못했습니다.");
            }

            try
            {
                Assert.AreEqual(12.5f, material.GetFloat("_SeaLevel"), 0.001f,
                    "해수면이 도착하지 않았습니다. 물가 띠가 엉뚱한 높이에 그려집니다.");

                Assert.AreEqual(30f, material.GetFloat("_HeightRange"), 0.001f,
                    "고도폭이 도착하지 않았습니다. 저지와 고지가 색으로 갈리지 않습니다.");

                Assert.AreEqual(41f, material.GetFloat("_ClimbLimit"), 0.001f,
                    "등반 한계가 도착하지 않았습니다. 보이는 바위와 막히는 곳이 어긋납니다.");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        /// <summary>
        /// 고도폭이 0이면 셰이더가 0으로 나눕니다. 지형 전체가 한 색으로 뭉갭니다.
        /// </summary>
        [Test]
        public void 고도폭이_0이어도_나눗셈이_터지지_않는다()
        {
            var material = PrototypeVisuals.CreateTerrainMaterial(null, 0f, 0f, 34f);

            if (material == null)
            {
                Assert.Ignore($"셰이더 '{PrototypeVisuals.TerrainShaderName}' 를 찾지 못했습니다.");
            }

            try
            {
                Assert.Greater(material.GetFloat("_HeightRange"), 0f,
                    "고도폭이 0으로 들어갔습니다. 셰이더가 0으로 나눕니다.");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        /// <summary>
        /// 셰이더의 기본 등반 한계는 프로필의 기본값과 같아야 합니다.
        ///
        /// 어긋나면 <b>프로필이 연결되지 않은 전장</b>에서만 조용히 틀립니다.
        /// 걸어 올라갈 수 있는 바위색 비탈이 생기거나, 풀밭인데 막히는 자리가 생깁니다.
        /// </summary>
        [Test]
        public void 셰이더의_기본_등반_한계가_프로필_기본값과_같다()
        {
            var shader = Shader.Find(PrototypeVisuals.TerrainShaderName);

            if (shader == null)
            {
                Assert.Ignore($"셰이더 '{PrototypeVisuals.TerrainShaderName}' 를 찾지 못했습니다.");
            }

            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            var profile = BattlefieldProfile.CreateDefault();

            try
            {
                Assert.AreEqual(profile.ClimbLimitDegrees, material.GetFloat("_ClimbLimit"), 0.001f,
                    "셰이더가 절벽을 그리는 각도와 생성기가 절벽을 만드는 각도가 다릅니다.");
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(profile);
            }
        }

        // ====================================================================================================
        // 2. 물
        // ====================================================================================================

        /// <summary>
        /// 물 셰이더는 수심으로 색을 정합니다. 그 두 폭이 0이면 전부 얕은 물이 됩니다.
        /// </summary>
        [Test]
        public void 물_셰이더가_수심_폭을_들고_있다()
        {
            var shader = Shader.Find(PrototypeVisuals.WaterShaderName);

            if (shader == null)
            {
                Assert.Ignore($"셰이더 '{PrototypeVisuals.WaterShaderName}' 를 찾지 못했습니다.");
            }

            var material = new Material(shader) { hideFlags = HideFlags.DontSave };

            try
            {
                Assert.Greater(material.GetFloat("_DepthFade"), 0f,
                    "수심 감쇠 폭이 0입니다. 깊은 물과 얕은 물이 구분되지 않습니다.");

                Assert.Greater(material.GetFloat("_ShoreWidth"), 0f,
                    "물가 띠 폭이 0입니다. 물과 땅의 경계가 드러나지 않습니다.");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        /// <summary>
        /// 물은 반투명해야 강바닥이 비칩니다. 불투명하면 여울이 보일 리가 없습니다.
        /// </summary>
        [Test]
        public void 얕은_물이_깊은_물보다_투명하다()
        {
            var shader = Shader.Find(PrototypeVisuals.WaterShaderName);

            if (shader == null)
            {
                Assert.Ignore($"셰이더 '{PrototypeVisuals.WaterShaderName}' 를 찾지 못했습니다.");
            }

            var material = new Material(shader) { hideFlags = HideFlags.DontSave };

            try
            {
                float shallow = material.GetColor("_ShallowColor").a;
                float deep = material.GetColor("_DeepColor").a;

                Assert.Less(shallow, deep, "얕은 물이 깊은 물보다 불투명합니다. 여울이 드러나지 않습니다.");
                Assert.Less(shallow, 0.9f, "얕은 물이 거의 불투명합니다. 강바닥이 비치지 않습니다.");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        /// <summary>
        /// 셰이더가 없어도 물은 있어야 합니다. 없으면 강 자리가 구멍으로 보입니다.
        /// </summary>
        [Test]
        public void 셰이더가_없어도_물_머티리얼은_나온다()
        {
            var material = PrototypeVisuals.CreateWaterMaterial(Color.blue);

            try
            {
                Assert.IsNotNull(material, "물 머티리얼이 비어 있습니다.");
            }
            finally
            {
                if (material != null)
                {
                    Object.DestroyImmediate(material);
                }
            }
        }
    }
}
