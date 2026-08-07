using SRPG.Systems.Formation;
using SRPG.Systems.Meshing;
using UnityEngine;

namespace SRPG.Systems.Props
{
    /// <summary>
    /// 지형지물의 형상을 만듭니다. 바위 하나부터 침식된 둔덕까지 <b>하나의 생성기</b>로 냅니다.
    ///
    /// <b>왜 하나인가</b>
    ///
    /// 바위 생성기와 둔덕 생성기를 따로 두면 둘이 서로 다른 세계에서 온 것처럼 보입니다.
    /// 실제 지형에서 바위와 둔덕은 <b>같은 암반이 얼마나 오래 깎였는가</b>의 차이일 뿐입니다.
    /// 그래서 여기서도 하나의 형상에 <see cref="Weathering"/> 축을 두고 그 위를 오갑니다.
    ///
    ///   · 풍화도 0 — 각지고 높고 거칠다. 갓 부서져 나온 바위.
    ///   · 풍화도 1 — 둥글고 낮고 매끈하다. 오래 깎인 둔덕.
    ///
    /// 둘이 한 섬에 섞여 있어야 지형이 <b>시간을 겪은 것</b>처럼 보입니다.
    ///
    /// <b>왜 저폴리인가</b>
    ///
    /// 면마다 정점을 새로 넣어 각진 음영이 나오게 합니다.
    /// 매끄럽게 만들려고 정점을 공유하면 지형과 룩이 갈라집니다.
    /// 부드러움은 음영이 아니라 <b>실루엣</b>으로 냅니다.
    /// </summary>
    public static class PropMeshBuilder
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>둘레를 나누는 수입니다. 홀수라 좌우 대칭이 눈에 띄지 않습니다.</summary>
        private const int Segments = 7;

        /// <summary>세로로 쌓는 단 수입니다.</summary>
        private const int Rings = 3;

        /// <summary>
        /// 밑동이 지면 아래로 들어가는 깊이입니다. 높이에 대한 비율입니다.
        ///
        /// <b>이것이 "놓인 것"과 "박힌 것"을 가릅니다.</b>
        /// 지면에 딱 맞춰 세우면 아무리 형태가 좋아도 스티커처럼 보입니다.
        /// 조금 묻어야 땅에서 솟은 것으로 읽힙니다.
        /// </summary>
        private const float BuryRatio = 0.22f;

        /// <summary>밑동의 음영입니다. 어두워야 지면 그늘에 녹아듭니다.</summary>
        private const float BaseShade = 0.18f;

        /// <summary>윗면의 음영입니다.</summary>
        private const float TopShade = 1f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 바위 하나를 버퍼에 쌓습니다.
        /// </summary>
        /// <param name="buffer">쌓을 대상입니다.</param>
        /// <param name="groundPosition">지면과 닿는 지점입니다.</param>
        /// <param name="rotation">기울기입니다. 살짝 기울어야 놓인 티가 사라집니다.</param>
        /// <param name="radius">밑동의 반경입니다.</param>
        /// <param name="height">지면 위로 솟는 높이입니다.</param>
        /// <param name="weathering">풍화도입니다. 0은 각진 바위, 1은 침식된 둔덕입니다.</param>
        /// <param name="seed">형상을 정하는 씨앗입니다. 같은 값이면 항상 같은 모양이 나옵니다.</param>
        public static void AddBoulder(
            MeshBuffer buffer,
            Vector3 groundPosition,
            Quaternion rotation,
            float radius,
            float height,
            float weathering,
            int seed)
        {
            if (buffer == null || radius <= 0f || height <= 0f)
            {
                return;
            }

            weathering = Mathf.Clamp01(weathering);

            // 침식은 높이를 깎습니다. 오래 깎인 것일수록 납작합니다.
            float actualHeight = height * Mathf.Lerp(1f, 0.55f, weathering);
            float buriedDepth = actualHeight * BuryRatio;

            // 거칠기도 침식이 줄입니다.
            float roughness = Mathf.Lerp(0.28f, 0.09f, weathering);

            var ringPoints = new Vector3[Rings + 1][];
            var ringShades = new float[Rings + 1];

            for (int r = 0; r <= Rings; r++)
            {
                float t = (float)r / Rings;

                ringPoints[r] = BuildRing(groundPosition, rotation, radius, actualHeight, buriedDepth, r, t, weathering, roughness, seed);
                ringShades[r] = ShadeAt(t);
            }

            for (int r = 0; r < Rings; r++)
            {
                AddRingBand(buffer, groundPosition, ringPoints[r], ringPoints[r + 1], ringShades[r], ringShades[r + 1]);
            }

            AddTopCap(buffer, ringPoints[Rings]);
        }

        // ====================================================================================================
        // 3. Private Methods - Geometry
        // ====================================================================================================

        /// <summary>
        /// 한 단의 둘레 점들을 만듭니다.
        /// </summary>
        private static Vector3[] BuildRing(
            Vector3 groundPosition,
            Quaternion rotation,
            float radius,
            float height,
            float buriedDepth,
            int ringIndex,
            float t,
            float weathering,
            float roughness,
            int seed)
        {
            var points = new Vector3[Segments];

            float profile = Profile(t, weathering);
            float y = Mathf.Lerp(-buriedDepth, height, t);

            for (int s = 0; s < Segments; s++)
            {
                float angle = (float)s / Segments * Mathf.PI * 2f;

                // 각진 바위는 위아래로도 들쭉날쭉합니다.
                // 침식된 것은 세로로 매끈하게 흘러내립니다 — 물이 훑고 지나간 자국입니다.
                float jagged = Noise(seed, s, ringIndex);
                float smooth = Noise(seed, s, 0);
                float displacement = Mathf.Lerp(jagged, smooth, weathering);

                float scaledRadius = radius * profile * (1f + (displacement - 0.5f) * 2f * roughness);

                var local = new Vector3(
                    Mathf.Cos(angle) * scaledRadius,
                    y,
                    Mathf.Sin(angle) * scaledRadius);

                points[s] = groundPosition + rotation * local;
            }

            return points;
        }

        /// <summary>
        /// 두 단 사이를 옆면으로 잇습니다.
        /// </summary>
        private static void AddRingBand(
            MeshBuffer buffer,
            Vector3 center,
            Vector3[] lower,
            Vector3[] upper,
            float lowerShade,
            float upperShade)
        {
            for (int s = 0; s < Segments; s++)
            {
                int next = (s + 1) % Segments;

                // 바깥을 향해야 합니다. 중심에서 면으로 나가는 방향을 기준으로 잡습니다.
                Vector3 outward = (lower[s] + lower[next] + upper[s] + upper[next]) * 0.25f - center;
                outward.y = 0f;

                if (outward.sqrMagnitude < 1e-6f)
                {
                    outward = Vector3.forward;
                }

                buffer.AddQuad(
                    lower[s], lower[next], upper[next], upper[s],
                    outward,
                    lowerShade, lowerShade, upperShade, upperShade);
            }
        }

        /// <summary>
        /// 꼭대기를 덮습니다.
        /// </summary>
        private static void AddTopCap(MeshBuffer buffer, Vector3[] top)
        {
            Vector3 apex = Vector3.zero;
            for (int s = 0; s < Segments; s++)
            {
                apex += top[s];
            }

            apex /= Segments;

            for (int s = 0; s < Segments; s++)
            {
                int next = (s + 1) % Segments;
                buffer.AddTriangle(apex, top[s], top[next], Vector3.up, TopShade);
            }
        }

        // ====================================================================================================
        // 4. Private Methods - Shape
        // ====================================================================================================

        /// <summary>
        /// 높이에 따른 반경 비율입니다. 이것이 실루엣을 정합니다.
        ///
        ///   · 각진 바위 — 옆면이 거의 곧고 윗면이 넓습니다. 덩어리로 읽힙니다.
        ///   · 침식된 둔덕 — 밑동이 넓게 퍼지고 위로 갈수록 둥글게 좁아집니다.
        ///
        /// 어느 쪽이든 꼭대기가 0이 되지는 않습니다. 뾰족한 원뿔은 바위로 보이지 않습니다.
        /// </summary>
        private static float Profile(float t, float weathering)
        {
            float angular = 1f - t * 0.45f;
            float eroded = Mathf.Cos(t * Mathf.PI * 0.5f * 0.86f);

            return Mathf.Lerp(angular, eroded, weathering);
        }

        /// <summary>
        /// 높이에 따른 음영입니다.
        ///
        /// 밑동만 어둡고 금세 밝아집니다. 접지 그늘은 닿는 자리에 몰려 있습니다.
        /// 위까지 고르게 어두우면 그늘이 아니라 그냥 어두운 물체가 됩니다.
        /// </summary>
        private static float ShadeAt(float t)
        {
            return Mathf.Lerp(BaseShade, TopShade, Mathf.Sqrt(Mathf.Clamp01(t)));
        }

        /// <summary>
        /// 형상용 잡음입니다. 씨앗과 위치가 같으면 항상 같은 값이 나옵니다.
        /// </summary>
        private static float Noise(int seed, int s, int r)
        {
            unchecked
            {
                int mixed = seed * 73856093 ^ s * 19349663 ^ r * 83492791;
                return FormationScatter.Hash01(mixed, 0xC2B2AE35u);
            }
        }
    }
}
