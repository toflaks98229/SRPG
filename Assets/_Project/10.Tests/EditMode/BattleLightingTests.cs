using NUnit.Framework;
using SRPG.Gameplay.Visual;
using UnityEngine;
using UnityEngine.Rendering;

namespace SRPG.Tests
{
    /// <summary>
    /// 조명이 한 곳에서만 정해지는지 검증합니다.
    ///
    /// <b>왜 테스트하는가</b>
    ///
    /// 같은 숫자가 여러 곳에 적혀 있으면 한쪽만 고쳐도 아무 오류가 나지 않습니다.
    /// 컴파일도 되고 다른 테스트도 전부 통과하는데 화면만 달라집니다.
    /// 사람이 눈으로 비교하지 않으면 못 찾는, 가장 오래 살아남는 종류의 버그입니다.
    ///
    /// 특히 셰이더의 기본값은 C# 쪽 상수와 아무 연결이 없어서 조용히 어긋나기 딱 좋습니다.
    /// 여기서 그 연결을 강제합니다.
    /// </summary>
    public sealed class BattleLightingTests
    {
        // ====================================================================================================
        // 1. Setup
        // ====================================================================================================

        private AmbientMode _savedMode;
        private Color _savedSky;
        private Color _savedEquator;
        private Color _savedGround;
        private Material _savedSkybox;
        private bool _savedFog;
        private Vector4 _savedToonParams;

        /// <summary>계단식 명암의 전역 식별자입니다.</summary>
        private static readonly int ToonParamsId = Shader.PropertyToID("_ToonParams");

        [SetUp]
        public void SetUp()
        {
            // RenderSettings는 전역입니다. 건드린 채로 두면 다른 테스트에 새어 나갑니다.
            _savedMode = RenderSettings.ambientMode;
            _savedSky = RenderSettings.ambientSkyColor;
            _savedEquator = RenderSettings.ambientEquatorColor;
            _savedGround = RenderSettings.ambientGroundColor;
            _savedSkybox = RenderSettings.skybox;
            _savedFog = RenderSettings.fog;

            // 셰이더 전역도 마찬가지입니다.
            _savedToonParams = Shader.GetGlobalVector(ToonParamsId);
        }

        [TearDown]
        public void TearDown()
        {
            RenderSettings.ambientMode = _savedMode;
            RenderSettings.ambientSkyColor = _savedSky;
            RenderSettings.ambientEquatorColor = _savedEquator;
            RenderSettings.ambientGroundColor = _savedGround;
            RenderSettings.skybox = _savedSkybox;
            RenderSettings.fog = _savedFog;

            Shader.SetGlobalVector(ToonParamsId, _savedToonParams);
        }

        // ====================================================================================================
        // 2. 셰이더와의 약속
        // ====================================================================================================

        /// <summary>
        /// 이 테스트가 이 단계의 핵심입니다.
        ///
        /// 지형과 유닛이 다른 밝기 하한을 쓰면, 절벽 그늘에 선 유닛만 혼자 밝게 떠 보입니다.
        /// 실제로 그런 상태였습니다 — 지형 0.35, 빌보드 0.45.
        /// </summary>
        [Test]
        public void 지형과_빌보드는_같은_환경광_하한을_쓴다()
        {
            float terrain = ReadShaderFloat(PrototypeVisuals.TerrainShaderName, "_AmbientBoost");
            float billboard = ReadShaderFloat(PrototypeVisuals.BillboardShaderName, "_AmbientBoost");

            Assert.AreEqual(terrain, billboard, 0.001f,
                "지형과 유닛이 다른 밝기 하한을 씁니다. 유닛만 다른 조명을 받는 것처럼 보입니다.");
        }

        [Test]
        public void 셰이더의_환경광_하한이_코드의_기준값과_같다()
        {
            float terrain = ReadShaderFloat(PrototypeVisuals.TerrainShaderName, "_AmbientBoost");

            Assert.AreEqual(BattleLighting.AmbientBoost, terrain, 0.001f,
                $"{nameof(BattleLighting)}.{nameof(BattleLighting.AmbientBoost)} 와 셰이더 기본값이 어긋났습니다.");
        }

        // ====================================================================================================
        // 3. 방향광
        // ====================================================================================================

        [Test]
        public void 방향광을_기준값대로_설정한다()
        {
            var go = new GameObject("TestLight");

            try
            {
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;

                BattleLighting.ApplyDirectional(light);

                Assert.AreEqual(LightType.Directional, light.type);
                Assert.AreEqual(BattleLighting.DirectionalIntensity, light.intensity, 0.001f);
                Assert.AreEqual(LightShadows.Soft, light.shadows, "그림자가 꺼져 있으면 고도 차가 안 읽힙니다.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 빛이 아래를 향해야 지면이 밝습니다. 위를 향하면 모든 윗면이 하한 밝기로 죽습니다.
        /// </summary>
        [Test]
        public void 방향광은_아래를_향한다()
        {
            var go = new GameObject("TestLight");

            try
            {
                BattleLighting.ApplyDirectional(go.AddComponent<Light>());

                Assert.Less(go.transform.forward.y, 0f, "조명이 하늘을 향하고 있습니다.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 셰이더는 위쪽 노멀에 half lambert 를 겁니다.
        /// 그 결과가 하한에 붙어 버리면 평지가 그늘과 구분되지 않습니다.
        /// </summary>
        [Test]
        public void 평지는_하한보다_확실히_밝다()
        {
            var go = new GameObject("TestLight");

            try
            {
                BattleLighting.ApplyDirectional(go.AddComponent<Light>());

                // 셰이더가 쓰는 mainLight.direction 은 빛이 <b>오는</b> 방향입니다.
                Vector3 toLight = -go.transform.forward;

                float lambert = Vector3.Dot(Vector3.up, toLight) * 0.5f + 0.5f;
                float shading = Mathf.Lerp(BattleLighting.AmbientBoost, 1f, lambert);

                Assert.Greater(shading, BattleLighting.AmbientBoost + 0.3f,
                    "평지의 밝기가 그늘과 거의 같습니다. 지형의 고도가 안 읽힙니다.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void 조명이_없어도_터지지_않는다()
        {
            Assert.DoesNotThrow(() => BattleLighting.ApplyDirectional(null));
        }

        // ====================================================================================================
        // 4. 환경광
        // ====================================================================================================

        /// <summary>
        /// 기본 스카이박스를 그대로 두면 절벽 그늘이 새까맣게 죽어 지형이 안 읽힙니다.
        /// </summary>
        [Test]
        public void 환경광은_삼색이고_스카이박스는_비운다()
        {
            BattleLighting.ApplyAmbient();

            Assert.AreEqual(AmbientMode.Trilight, RenderSettings.ambientMode);
            Assert.IsNull(RenderSettings.skybox);
            Assert.IsFalse(RenderSettings.fog, "안개가 켜져 있으면 색상 대비가 뭉개집니다.");
        }

        /// <summary>
        /// 하늘이 가장 밝고 땅이 가장 어두워야 위아래가 구분됩니다. 뒤집히면 지형이 뒤집혀 보입니다.
        /// </summary>
        [Test]
        public void 환경광은_위에서_아래로_어두워진다()
        {
            BattleLighting.ApplyAmbient();

            float sky = Luminance(RenderSettings.ambientSkyColor);
            float equator = Luminance(RenderSettings.ambientEquatorColor);
            float ground = Luminance(RenderSettings.ambientGroundColor);

            Assert.Greater(sky, equator, "하늘이 수평보다 어둡습니다.");
            Assert.Greater(equator, ground, "수평이 땅보다 어둡습니다.");
        }

        // ====================================================================================================
        // 5. 계단식 명암
        // ====================================================================================================

        /// <summary>
        /// 명암의 단 수는 셰이더가 전역으로 받습니다.
        ///
        /// 전역이 안 올라가면 셰이더는 <b>조용히</b> 예전처럼 매끄럽게 칠합니다.
        /// 오류도 안 나고 다른 테스트도 전부 통과하는데 화면만 달라집니다.
        /// 그래서 올리는 일을 <see cref="BattleLighting.ApplyAmbient"/> 안에 묶었고,
        /// 여기서 그 묶음이 풀리지 않았는지 봅니다.
        /// </summary>
        [Test]
        public void 환경광을_적용하면_계단식_명암도_함께_올라간다()
        {
            Shader.SetGlobalVector(ToonParamsId, Vector4.zero);

            BattleLighting.ApplyAmbient();

            Vector4 published = Shader.GetGlobalVector(ToonParamsId);

            Assert.AreEqual(BattleLighting.ToonCuts, published.x, 0.001f,
                "계단 수가 안 올라갔습니다. 화면이 예전처럼 매끄럽게 칠해집니다.");
            Assert.AreEqual(BattleLighting.ToonWrap, published.y, 0.001f);
            Assert.AreEqual(BattleLighting.ToonSteepness, published.z, 0.001f);
            Assert.AreEqual(BattleLighting.ToonGradient, published.w, 0.001f);
        }

        /// <summary>
        /// 단이 하나면 명암이 통째로 사라집니다. 셰이더는 그 값도 받아 그리므로
        /// 아무 오류 없이 지형이 평평한 색 한 장이 됩니다.
        /// </summary>
        [Test]
        public void 계단은_최소_두_단_이상이다()
        {
            Assert.GreaterOrEqual(BattleLighting.ToonCuts, 2,
                "단이 하나면 지형의 굴곡이 통째로 사라집니다.");
        }

        /// <summary>
        /// 경계에 그라디언트가 없으면 넓고 완만한 지형에서 계단선이 화면을 가로지르며
        /// 카메라가 움직일 때마다 출렁입니다.
        /// </summary>
        [Test]
        public void 계단_경계에는_그라디언트가_있다()
        {
            Assert.Greater(BattleLighting.ToonGradient, 0f,
                "경계가 딱 갈리면 완만한 비탈에서 계단선이 출렁입니다.");
        }

        // ====================================================================================================
        // 6. Helpers
        // ====================================================================================================

        /// <summary>
        /// 셰이더의 프로퍼티 기본값을 읽습니다. 셰이더가 없으면 테스트를 건너뜁니다.
        /// </summary>
        private static float ReadShaderFloat(string shaderName, string propertyName)
        {
            var shader = Shader.Find(shaderName);

            if (shader == null)
            {
                Assert.Ignore($"셰이더 '{shaderName}' 를 찾지 못했습니다.");
            }

            var material = new Material(shader) { hideFlags = HideFlags.DontSave };

            try
            {
                Assert.IsTrue(material.HasProperty(propertyName),
                    $"셰이더 '{shaderName}' 에 '{propertyName}' 가 없습니다.");

                return material.GetFloat(propertyName);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        private static float Luminance(Color color)
        {
            return (color.r * 0.299f) + (color.g * 0.587f) + (color.b * 0.114f);
        }
    }
}
