using NUnit.Framework;
using SRPG.Data;
using SRPG.Rendering;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 픽셀 격자 계산을 검증합니다.
    ///
    /// <b>왜 이제야 검사가 붙는가</b>
    ///
    /// 원근이던 시절에는 이 계산이 "초점까지의 거리"에 기대고 있었고,
    /// 거리는 카메라와 부모의 관계라서 물으려면 트랜스폼 둘을 세워야 했습니다.
    /// 직교로 옮기면서 기준이 <b>화면에 담기는 월드 높이</b> 하나가 되었고,
    /// 그것은 그냥 수입니다. 검사가 붙을 수 있게 된 것 자체가 이번 전환의 부산물입니다.
    /// </summary>
    public sealed class PixelGridTests
    {
        // ====================================================================================================
        // 1. 정수 배율
        // ====================================================================================================

        /// <summary>
        /// 내부 해상도는 화면 높이를 정수로 나눈 값이어야 합니다.
        ///
        /// 나누어떨어지지 않으면 내부 픽셀이 어떤 것은 세 칸 어떤 것은 네 칸을 덮고,
        /// 그 들쭉날쭉함이 줌할 때마다 줄무늬처럼 흘러갑니다.
        /// </summary>
        [Test]
        public void 내부_해상도가_화면_높이를_정수로_나눈다()
        {
            const int screen = 1080;

            foreach (int desired in new[] { 540, 400, 360, 270, 200, 180 })
            {
                int height = PixelGrid.SnapToIntegerScale(screen, desired);

                Assert.AreEqual(
                    0,
                    screen % height,
                    $"{desired} 을 요청했더니 {height} 이 나왔는데 {screen} 을 나누지 못합니다.");
            }
        }

        [Test]
        public void 화면_높이를_모르면_요청한_값을_그대로_돌려준다()
        {
            Assert.AreEqual(270, PixelGrid.SnapToIntegerScale(0, 270f));
        }

        // ====================================================================================================
        // 2. 줌 연동
        // ====================================================================================================

        /// <summary>
        /// 줌아웃하면 내부 해상도가 올라갑니다.
        ///
        /// 목적은 <b>픽셀 하나가 덮는 월드 크기를 일정하게 두는 것</b>입니다.
        /// 반대로 움직이면 줌인할 때 병사가 자기 스프라이트보다 촘촘해 보입니다.
        /// </summary>
        [Test]
        public void 줌아웃하면_내부_해상도가_올라간다()
        {
            int near = PixelGrid.ResolveHeight(1080, 270, 19.5f, 10f, 140, 540);
            int far = PixelGrid.ResolveHeight(1080, 270, 19.5f, 30f, 140, 540);

            Assert.Less(near, far, "줌아웃했는데 내부 해상도가 올라가지 않았습니다.");
        }

        [Test]
        public void 기준_줌에서는_기준_해상도_근처가_나온다()
        {
            int height = PixelGrid.ResolveHeight(1080, 270, 19.5f, 19.5f, 140, 540);

            Assert.AreEqual(270, height);
        }

        [Test]
        public void 상한과_하한을_벗어나지_않는다()
        {
            int tiny = PixelGrid.ResolveHeight(1080, 270, 19.5f, 0.5f, 140, 540);
            int huge = PixelGrid.ResolveHeight(1080, 270, 19.5f, 500f, 140, 540);

            Assert.GreaterOrEqual(tiny, 100, "하한 아래로 내려갔습니다.");
            Assert.LessOrEqual(huge, 540, "상한 위로 올라갔습니다.");
        }

        [Test]
        public void 줌을_알_수_없으면_기준_해상도를_쓴다()
        {
            Assert.AreEqual(
                PixelGrid.SnapToIntegerScale(1080, 270),
                PixelGrid.ResolveHeight(1080, 270, 19.5f, 0f, 140, 540));
        }

        // ====================================================================================================
        // 3. 텍셀
        // ====================================================================================================

        /// <summary>
        /// 텍셀 하나가 덮는 월드 길이는 화면 높이를 내부 해상도로 나눈 값입니다.
        /// </summary>
        [Test]
        public void 텍셀_크기가_화면_높이를_해상도로_나눈_값이다()
        {
            // 높이의 절반이 19.5 이면 화면 전체는 39. 그것을 260칸으로 나누면 0.15.
            Assert.AreEqual(0.15f, PixelGrid.TexelSize(19.5f, 260), 0.0001f);
        }

        [Test]
        public void 잴_수_없으면_텍셀이_0이다()
        {
            Assert.AreEqual(0f, PixelGrid.TexelSize(0f, 270));
            Assert.AreEqual(0f, PixelGrid.TexelSize(19.5f, 0));
        }

        /// <summary>
        /// 격자에서 벗어난 길이는 언제나 텍셀 절반 안쪽입니다.
        ///
        /// 가장 가까운 격자점을 기준으로 재기 때문입니다.
        /// 이 성질이 깨지면 카메라를 붙이는 순간 한 칸 이상 튑니다.
        /// </summary>
        [Test]
        public void 격자에서_벗어난_길이는_텍셀_절반을_넘지_않는다()
        {
            const float texel = 0.15f;

            for (float along = -3f; along <= 3f; along += 0.017f)
            {
                float offset = PixelGrid.SubTexelOffset(along, texel);

                Assert.LessOrEqual(
                    Mathf.Abs(offset),
                    texel * 0.5f + 0.0001f,
                    $"{along} 에서 어긋남이 {offset} 이라 텍셀 절반을 넘습니다.");
            }
        }

        [Test]
        public void 격자에_정확히_선_자리는_어긋남이_없다()
        {
            Assert.AreEqual(0f, PixelGrid.SubTexelOffset(0.45f, 0.15f), 0.0001f);
            Assert.AreEqual(0f, PixelGrid.SubTexelOffset(-0.6f, 0.15f), 0.0001f);
        }

        [Test]
        public void 텍셀이_0이면_어긋남도_0이다()
        {
            Assert.AreEqual(0f, PixelGrid.SubTexelOffset(1.234f, 0f));
        }

        // ====================================================================================================
        // 4. 카메라에서 줌 읽기
        // ====================================================================================================

        /// <summary>
        /// 직교 카메라의 줌은 <c>orthographicSize</c> 그대로입니다.
        /// </summary>
        [Test]
        public void 직교_카메라의_줌은_직교_크기다()
        {
            var go = new GameObject("TestCamera");

            try
            {
                var camera = go.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 12.5f;

                Assert.AreEqual(12.5f, PixelGrid.ResolveViewExtent(camera), 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// 카메라가 없으면 0입니다. 줌을 알 수 없다는 뜻이고, 그때는 기준 해상도가 쓰입니다.
        /// </summary>
        [Test]
        public void 카메라가_없으면_줌이_0이다()
        {
            Assert.AreEqual(0f, PixelGrid.ResolveViewExtent(null));
        }

        // ====================================================================================================
        // 5. 설정 에셋의 이관
        // ====================================================================================================

        /// <summary>
        /// 옛 에셋의 <b>거리</b>가 새 단위인 <b>화면 높이의 절반</b>으로 환산됩니다.
        ///
        /// <b>이름만 이어받아서는 안 됩니다.</b> 같은 34가 새 단위에서는 전혀 다른 줌을 뜻합니다 —
        /// 그대로 두면 기준 해상도가 적용되는 지점이 통째로 어긋나고 오류는 나지 않습니다.
        /// </summary>
        [Test]
        public void 옛_거리값이_화면_높이로_환산된다()
        {
            var settings = ScriptableObject.CreateInstance<PixelGridSettings>();

            try
            {
                // 옛 에셋이 로드된 직후의 상태입니다 — 판을 모르고, 값은 카메라 거리입니다.
                settings.SchemaVersion = 0;
                settings.ReferenceExtent = 34f;

                Assert.IsTrue(settings.MigrateToCurrentSchema(), "옛 판인데 이관이 돌지 않았습니다.");

                // 시야각 60도에서 tan(30°) ≈ 0.5774. 34 × 0.5774 ≈ 19.63.
                Assert.AreEqual(19.63f, settings.ReferenceExtent, 0.05f);
                Assert.AreEqual(PixelGridSettings.CurrentSchemaVersion, settings.SchemaVersion);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void 이관은_여러_번_돌려도_결과가_같다()
        {
            var settings = ScriptableObject.CreateInstance<PixelGridSettings>();

            try
            {
                settings.SchemaVersion = 0;
                settings.ReferenceExtent = 34f;

                settings.MigrateToCurrentSchema();
                float once = settings.ReferenceExtent;

                Assert.IsFalse(settings.MigrateToCurrentSchema(), "두 번 돌았습니다.");
                Assert.AreEqual(once, settings.ReferenceExtent, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void 코드로_만든_설정은_최신_판이다()
        {
            var settings = PixelGridSettings.CreateDefault();

            try
            {
                Assert.AreEqual(PixelGridSettings.CurrentSchemaVersion, settings.SchemaVersion);
                Assert.IsFalse(settings.MigrateToCurrentSchema());
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
