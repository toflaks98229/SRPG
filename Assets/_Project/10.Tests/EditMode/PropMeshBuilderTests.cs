using NUnit.Framework;
using SRPG.Systems.Meshing;
using SRPG.Systems.Props;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 지형지물의 형상을 검증합니다.
    ///
    /// <b>풍화도 축이 실제로 동작하는가</b>가 핵심입니다.
    /// 바위와 둔덕을 하나의 생성기로 내기로 한 이상, 그 축이 죽어 있으면
    /// 매개변수만 다르고 결과는 같은 것이 잔뜩 나옵니다. 그건 최악입니다 —
    /// 다양해 보이려고 애쓴 흔적만 남고 실제로는 전부 같은 모양입니다.
    /// </summary>
    public sealed class PropMeshBuilderTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private static MeshBuffer Build(float weathering, float radius = 1f, float height = 1f, int seed = 7)
        {
            var buffer = new MeshBuffer();

            PropMeshBuilder.AddBoulder(
                buffer,
                Vector3.zero,
                Quaternion.identity,
                radius,
                height,
                weathering,
                seed);

            return buffer;
        }

        /// <summary>가장 높은 정점의 높이입니다.</summary>
        private static float TopOf(MeshBuffer buffer)
        {
            float top = float.MinValue;

            for (int i = 0; i < buffer.Vertices.Count; i++)
            {
                top = Mathf.Max(top, buffer.Vertices[i].y);
            }

            return top;
        }

        /// <summary>가장 낮은 정점의 높이입니다.</summary>
        private static float BottomOf(MeshBuffer buffer)
        {
            float bottom = float.MaxValue;

            for (int i = 0; i < buffer.Vertices.Count; i++)
            {
                bottom = Mathf.Min(bottom, buffer.Vertices[i].y);
            }

            return bottom;
        }

        /// <summary>지정한 높이 부근에서의 최대 수평 반경입니다.</summary>
        private static float RadiusNear(MeshBuffer buffer, float y, float tolerance)
        {
            float widest = 0f;

            for (int i = 0; i < buffer.Vertices.Count; i++)
            {
                var v = buffer.Vertices[i];

                if (Mathf.Abs(v.y - y) > tolerance)
                {
                    continue;
                }

                widest = Mathf.Max(widest, new Vector2(v.x, v.z).magnitude);
            }

            return widest;
        }

        // ====================================================================================================
        // 2. 기본
        // ====================================================================================================

        [Test]
        public void 형상이_실제로_만들어진다()
        {
            var buffer = Build(0.5f);

            Assert.Greater(buffer.Count, 0, "정점이 없습니다.");
            Assert.AreEqual(0, buffer.Triangles.Count % 3, "삼각형 인덱스가 3의 배수가 아닙니다.");
            Assert.AreEqual(buffer.Vertices.Count, buffer.Colors.Count, "정점과 컬러 수가 다릅니다.");
        }

        [Test]
        public void 크기가_0이면_아무것도_만들지_않는다()
        {
            Assert.AreEqual(0, Build(0.5f, radius: 0f).Count);
            Assert.AreEqual(0, Build(0.5f, height: 0f).Count);
        }

        [Test]
        public void 버퍼가_없어도_터지지_않는다()
        {
            Assert.DoesNotThrow(() =>
                PropMeshBuilder.AddBoulder(null, Vector3.zero, Quaternion.identity, 1f, 1f, 0.5f, 0));
        }

        // ====================================================================================================
        // 3. 접지
        // ====================================================================================================

        /// <summary>
        /// 지면에 딱 맞춰 세우면 스티커처럼 보입니다. 조금 묻어야 땅에서 솟은 것으로 읽힙니다.
        /// </summary>
        [Test]
        public void 밑동이_지면_아래로_들어간다()
        {
            var buffer = Build(0.5f);

            Assert.Less(BottomOf(buffer), 0f, "밑동이 지면 위에 떠 있습니다. 놓인 것처럼 보입니다.");
        }

        /// <summary>
        /// 접지 그늘은 닿는 자리에 몰려 있습니다. 위까지 고르게 어두우면 그늘이 아니라 검은 물체입니다.
        /// </summary>
        [Test]
        public void 밑동만_어둡고_위는_밝다()
        {
            var buffer = Build(0.5f);

            float lowest = float.MaxValue;
            float highest = 0f;

            for (int i = 0; i < buffer.Vertices.Count; i++)
            {
                float shade = buffer.Colors[i].r;

                if (buffer.Vertices[i].y < 0f)
                {
                    lowest = Mathf.Min(lowest, shade);
                }
                else if (buffer.Vertices[i].y > TopOf(buffer) * 0.8f)
                {
                    highest = Mathf.Max(highest, shade);
                }
            }

            Assert.Less(lowest, 0.3f, "밑동이 밝습니다. 지면에서 떠 보입니다.");
            Assert.Greater(highest, 0.9f, "꼭대기가 어둡습니다.");
        }

        // ====================================================================================================
        // 4. 풍화도 축
        // ====================================================================================================

        /// <summary>
        /// 침식은 높이를 깎습니다. 오래 깎인 것일수록 납작합니다.
        /// </summary>
        [Test]
        public void 침식될수록_납작해진다()
        {
            float angular = TopOf(Build(0f));
            float eroded = TopOf(Build(1f));

            Assert.Less(eroded, angular * 0.75f,
                $"각진 것 {angular:F2}, 침식된 것 {eroded:F2}. 침식이 높이를 깎지 않았습니다.");
        }

        /// <summary>
        /// 각진 바위는 옆면이 거의 곧아 윗면이 넓고, 침식된 둔덕은 위로 갈수록 둥글게 좁아집니다.
        /// 이 차이가 실루엣을 가릅니다.
        /// </summary>
        [Test]
        public void 침식될수록_꼭대기가_좁아진다()
        {
            var angular = Build(0f);
            var eroded = Build(1f);

            float angularRatio = RadiusNear(angular, TopOf(angular), 0.05f) / RadiusNear(angular, 0f, 0.3f);
            float erodedRatio = RadiusNear(eroded, TopOf(eroded), 0.05f) / RadiusNear(eroded, 0f, 0.3f);

            Assert.Less(erodedRatio, angularRatio,
                $"각진 것 {angularRatio:F2}, 침식된 것 {erodedRatio:F2}. 둘의 실루엣이 같습니다.");
        }

        /// <summary>
        /// 침식은 표면을 매끈하게 만듭니다. 각진 것은 위아래로도 들쭉날쭉해야 합니다.
        ///
        /// 같은 높이에서 둘레 반경의 편차로 잽니다.
        /// </summary>
        [Test]
        public void 침식될수록_표면이_매끈해진다()
        {
            Assert.Less(
                RadiusSpread(Build(1f)),
                RadiusSpread(Build(0f)),
                "침식된 것이 각진 것보다 거칠습니다.");
        }

        /// <summary>둘레 반경의 최대-최소 차이입니다. 클수록 거칩니다.</summary>
        private static float RadiusSpread(MeshBuffer buffer)
        {
            float min = float.MaxValue;
            float max = 0f;

            float sampleY = TopOf(buffer) * 0.34f;

            for (int i = 0; i < buffer.Vertices.Count; i++)
            {
                var v = buffer.Vertices[i];

                if (Mathf.Abs(v.y - sampleY) > TopOf(buffer) * 0.2f)
                {
                    continue;
                }

                float r = new Vector2(v.x, v.z).magnitude;

                min = Mathf.Min(min, r);
                max = Mathf.Max(max, r);
            }

            return max > 0f ? (max - min) / max : 0f;
        }

        // ====================================================================================================
        // 5. 결정론과 변화
        // ====================================================================================================

        [Test]
        public void 같은_씨앗이면_같은_형상이_나온다()
        {
            var first = Build(0.5f, seed: 42);
            var second = Build(0.5f, seed: 42);

            Assert.AreEqual(first.Vertices.Count, second.Vertices.Count);

            for (int i = 0; i < first.Vertices.Count; i++)
            {
                Assert.AreEqual(first.Vertices[i], second.Vertices[i], $"{i}번 정점이 다릅니다.");
            }
        }

        /// <summary>
        /// 씨앗이 다른데 모양이 같으면 복제된 티가 납니다.
        /// </summary>
        [Test]
        public void 다른_씨앗이면_다른_형상이_나온다()
        {
            var first = Build(0.5f, seed: 1);
            var second = Build(0.5f, seed: 999);

            bool identical = true;

            for (int i = 0; i < first.Vertices.Count; i++)
            {
                if (first.Vertices[i] != second.Vertices[i])
                {
                    identical = false;
                    break;
                }
            }

            Assert.IsFalse(identical, "씨앗이 다른데 형상이 같습니다.");
        }

        // ====================================================================================================
        // 6. 변환
        // ====================================================================================================

        [Test]
        public void 지정한_자리에_만들어진다()
        {
            var buffer = new MeshBuffer();
            var position = new Vector3(13f, 4f, -7f);

            PropMeshBuilder.AddBoulder(buffer, position, Quaternion.identity, 1f, 1f, 0.5f, 3);

            var center = Vector3.zero;
            for (int i = 0; i < buffer.Vertices.Count; i++)
            {
                center += buffer.Vertices[i];
            }

            center /= buffer.Vertices.Count;

            Assert.AreEqual(position.x, center.x, 0.5f);
            Assert.AreEqual(position.z, center.z, 0.5f);
        }

        [Test]
        public void 기울이면_실제로_기운다()
        {
            var upright = Build(0.3f);

            var tilted = new MeshBuffer();
            PropMeshBuilder.AddBoulder(
                tilted, Vector3.zero, Quaternion.Euler(0f, 0f, 25f), 1f, 1f, 0.3f, 7);

            bool moved = false;

            for (int i = 0; i < upright.Vertices.Count; i++)
            {
                if (Vector3.Distance(upright.Vertices[i], tilted.Vertices[i]) > 0.05f)
                {
                    moved = true;
                    break;
                }
            }

            Assert.IsTrue(moved, "기울였는데 정점이 그대로입니다.");
        }
    }
}
