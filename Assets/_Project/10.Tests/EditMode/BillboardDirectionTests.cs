using NUnit.Framework;
using SRPG.Systems.Rendering;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 빌보드 방향 판독을 검증합니다.
    ///
    /// <b>2.5D에서 방향은 연출이 아니라 규칙입니다.</b>
    /// 창은 정면 좁은 각도만 위험하고 방패는 정면에서 온 화살만 막습니다.
    /// 플레이어가 "저 창병이 어디를 보고 있는가"를 못 읽으면 그 규칙들이 전부 무의미해집니다.
    ///
    /// 빌보드는 언제나 카메라를 향하므로 그림 자체로는 방향을 말할 수 없습니다.
    /// 유닛 방향과 카메라 방향의 <b>차이</b>가 그 역할을 대신하고, 그 계산이 여기 있습니다.
    /// </summary>
    public sealed class BillboardDirectionTests
    {
        // ====================================================================================================
        // 1. 상대 각도
        // ====================================================================================================

        [Test]
        public void 카메라와_같은_쪽을_보면_0도다()
        {
            // 유닛이 카메라를 등지고 있습니다. 뒷모습입니다.
            float angle = BillboardDirection.RelativeAngle(Vector3.forward, Vector3.forward);

            Assert.AreEqual(0f, angle, 0.01f);
        }

        [Test]
        public void 카메라를_마주_보면_180도다()
        {
            float angle = BillboardDirection.RelativeAngle(Vector3.back, Vector3.forward);

            Assert.AreEqual(180f, angle, 0.01f);
        }

        [Test]
        public void 결과는_항상_0에서_360_사이다()
        {
            for (int deg = -720; deg <= 720; deg += 7)
            {
                Vector3 facing = Quaternion.Euler(0f, deg, 0f) * Vector3.forward;
                float angle = BillboardDirection.RelativeAngle(facing, Vector3.forward);

                Assert.GreaterOrEqual(angle, 0f, $"deg={deg}");
                Assert.Less(angle, 360.01f, $"deg={deg}");
            }
        }

        /// <summary>
        /// 카메라를 돌리면 같은 유닛이 다른 면을 보여야 합니다. 그것이 곧 방향의 표현입니다.
        /// </summary>
        [Test]
        public void 카메라를_돌리면_보이는_면이_바뀐다()
        {
            Vector3 unitFacing = Vector3.forward;

            float before = BillboardDirection.RelativeAngle(unitFacing, Vector3.forward);
            float after = BillboardDirection.RelativeAngle(unitFacing, Vector3.right);

            float difference = Mathf.Abs(Mathf.DeltaAngle(before, after));

            Assert.Greater(difference, 80f, "카메라를 90도 돌렸는데 보이는 면이 그만큼 안 바뀌었습니다.");
        }

        /// <summary>
        /// 카메라가 47도로 내려다봅니다. 그 아래 성분이 방향 판정에 섞이면 안 됩니다.
        /// </summary>
        [Test]
        public void 카메라의_내려다보는_각도는_영향을_주지_않는다()
        {
            Vector3 flat = Vector3.forward;
            Vector3 pitched = (Vector3.forward + Vector3.down * 1.2f).normalized;

            float withFlat = BillboardDirection.RelativeAngle(Vector3.right, flat);
            float withPitch = BillboardDirection.RelativeAngle(Vector3.right, pitched);

            Assert.AreEqual(withFlat, withPitch, 0.01f);
        }

        [Test]
        public void 영벡터가_들어와도_터지지_않는다()
        {
            Assert.DoesNotThrow(() => BillboardDirection.RelativeAngle(Vector3.zero, Vector3.forward));
            Assert.DoesNotThrow(() => BillboardDirection.RelativeAngle(Vector3.forward, Vector3.zero));
            Assert.DoesNotThrow(() => BillboardDirection.RelativeAngle(Vector3.up, Vector3.up));
        }

        // ====================================================================================================
        // 2. 방향 번호
        // ====================================================================================================

        /// <summary>
        /// 각 구간의 <b>가운데</b>가 대표 각도여야 합니다.
        /// 반올림 없이 잘라 버리면 정면을 보는데도 비스듬한 그림이 나옵니다.
        /// </summary>
        [Test]
        public void 구간의_대표_각도가_그_번호를_낸다()
        {
            for (int i = 0; i < 8; i++)
            {
                float representative = i * 45f;

                Assert.AreEqual(i, BillboardDirection.ToIndex(representative, 8), $"{representative}도");
            }
        }

        [Test]
        public void 구간_안에서는_같은_번호가_나온다()
        {
            // 45도 구간의 ±20도는 전부 1번이어야 합니다.
            Assert.AreEqual(1, BillboardDirection.ToIndex(45f - 20f, 8));
            Assert.AreEqual(1, BillboardDirection.ToIndex(45f, 8));
            Assert.AreEqual(1, BillboardDirection.ToIndex(45f + 20f, 8));
        }

        [Test]
        public void 한_바퀴_돌면_0번으로_돌아온다()
        {
            Assert.AreEqual(0, BillboardDirection.ToIndex(360f, 8));
            Assert.AreEqual(0, BillboardDirection.ToIndex(359f, 8));
        }

        [Test]
        public void 번호는_항상_범위_안이다()
        {
            for (int count = 1; count <= 16; count++)
            {
                for (float angle = -400f; angle <= 760f; angle += 3f)
                {
                    int index = BillboardDirection.ToIndex(angle, count);

                    Assert.GreaterOrEqual(index, 0, $"count={count} angle={angle}");
                    Assert.Less(index, count, $"count={count} angle={angle}");
                }
            }
        }

        // ====================================================================================================
        // 3. 좌우 반전
        // ====================================================================================================

        [Test]
        public void 앞쪽_절반은_뒤집지_않는다()
        {
            for (int i = 0; i <= 4; i++)
            {
                int result = BillboardDirection.ToMirroredIndex(i, 8, out bool mirrored);

                Assert.AreEqual(i, result, $"i={i}");
                Assert.IsFalse(mirrored, $"i={i} 를 뒤집었습니다.");
            }
        }

        [Test]
        public void 뒤쪽_절반은_거울에_비친_짝을_쓴다()
        {
            // 5번은 3번을 뒤집은 것, 6번은 2번, 7번은 1번입니다.
            for (int i = 5; i < 8; i++)
            {
                int result = BillboardDirection.ToMirroredIndex(i, 8, out bool mirrored);

                Assert.AreEqual(8 - i, result, $"i={i}");
                Assert.IsTrue(mirrored, $"i={i} 를 뒤집지 않았습니다.");
            }
        }

        /// <summary>
        /// 반전을 쓰면 절반의 그림으로 전부 표현되어야 합니다. 그것이 반전을 쓰는 이유입니다.
        /// </summary>
        [Test]
        public void 반전을_쓰면_절반의_그림만_필요하다()
        {
            var used = new System.Collections.Generic.HashSet<int>();

            for (int i = 0; i < 8; i++)
            {
                used.Add(BillboardDirection.ToMirroredIndex(i, 8, out _));
            }

            Assert.LessOrEqual(used.Count, 5, $"필요한 그림이 {used.Count}장입니다. 절반으로 줄지 않았습니다.");
        }

        [Test]
        public void 반전_결과도_항상_범위_안이다()
        {
            for (int count = 1; count <= 16; count++)
            {
                for (int i = -20; i < 40; i++)
                {
                    int result = BillboardDirection.ToMirroredIndex(i, count, out _);

                    Assert.GreaterOrEqual(result, 0, $"count={count} i={i}");
                    Assert.Less(result, count, $"count={count} i={i}");
                }
            }
        }
    }
}
