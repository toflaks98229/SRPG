using System.Collections.Generic;
using UnityEngine;

namespace SRPG.Systems.Meshing
{
    /// <summary>
    /// 계단식 대지를 굽습니다. <b>평평한 상판과 가파른 절벽면</b>이 쌓인 형태입니다.
    ///
    /// <b>왜 마칭 스퀘어인가</b>
    ///
    /// 단 경계를 셀 변을 따라 그리면 윤곽이 <b>축에 정렬된 계단</b>이 됩니다.
    /// 셀을 아무리 잘게 나눠도 계단은 계단입니다 — 눈은 그 규칙을 즉시 찾아냅니다.
    /// 지형을 침식으로 만들어 놓고도 사각형으로 보이던 이유가 이것이었습니다.
    ///
    /// 마칭 스퀘어는 경계를 <b>셀 안쪽으로 가로질러</b> 긋습니다.
    /// 네 모서리 중 높은 것과 낮은 것이 갈리는 변의 <b>중점</b>을 이어서, 45도 대각선이 나옵니다.
    /// 그 대각선들이 이어지면 축과 무관한 불규칙 다각형이 됩니다.
    ///
    /// <b>수직면은 없애지 않습니다</b>
    ///
    /// 이 게임의 룩은 쌓인 판입니다. 절벽면이 비탈이 되면 판으로 안 보입니다.
    /// 대신 면을 살짝 뒤로 눕혀(<see cref="BatterRatio"/>) 밑동이 바닥과 만나는 자리를
    /// 정확한 90도에서 비켜 놓습니다. 실제 암벽도 완전한 수직은 드뭅니다.
    /// </summary>
    public static class PlateauMesher
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// 절벽면이 뒤로 눕는 정도입니다. 벽 높이에 대한 비율입니다.
        ///
        /// 0이면 완벽한 수직이라 밑동이 정확히 90도로 꺾입니다.
        /// 살짝 눕히면 그 선이 부드러워지면서도 <b>판이라는 인상은 유지</b>됩니다.
        /// </summary>
        private const float BatterRatio = 0.16f;

        /// <summary>상판의 접지 음영입니다.</summary>
        private const float TopShade = 1f;

        /// <summary>절벽면 위쪽의 음영입니다.</summary>
        private const float WallTopShade = 0.74f;

        /// <summary>절벽면 아래쪽의 음영입니다. 여기가 어두워야 고도 차가 읽힙니다.</summary>
        private const float WallBottomShade = 0.16f;

        // ====================================================================================================
        // 2. Nested Types
        // ====================================================================================================

        /// <summary>
        /// 셀 하나를 그리는 데 필요한 정보입니다.
        /// </summary>
        public struct Cell
        {
            /// <summary>네 모서리의 단입니다. 좌하 → 좌상 → 우상 → 우하 순서입니다.</summary>
            public int Corner00;
            public int Corner01;
            public int Corner11;
            public int Corner10;

            /// <summary>셀의 좌하단 월드 좌표입니다.</summary>
            public float MinX;
            public float MinZ;

            /// <summary>셀 한 변의 크기입니다.</summary>
            public float Size;

            /// <summary>고도 한 단의 높이입니다.</summary>
            public float HeightStep;
        }

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 셀 하나의 상판과 절벽면을 굽습니다.
        /// </summary>
        /// <param name="top">상판이 쌓일 버퍼입니다.</param>
        /// <param name="wall">절벽면이 쌓일 버퍼입니다.</param>
        /// <param name="cell">셀 정보입니다.</param>
        public static void AddCell(MeshBuffer top, MeshBuffer wall, in Cell cell)
        {
            int low = Mathf.Min(Mathf.Min(cell.Corner00, cell.Corner01), Mathf.Min(cell.Corner11, cell.Corner10));
            int high = Mathf.Max(Mathf.Max(cell.Corner00, cell.Corner01), Mathf.Max(cell.Corner11, cell.Corner10));

            // 네 모서리가 같은 단이면 평평한 사각형 하나입니다.
            if (low == high)
            {
                AddFlatQuad(top, cell, low);
                return;
            }

            // 모서리 좌표를 좌하 → 좌상 → 우상 → 우하 순서로 둡니다.
            var corners = new[]
            {
                new Vector2(cell.MinX, cell.MinZ),
                new Vector2(cell.MinX, cell.MinZ + cell.Size),
                new Vector2(cell.MinX + cell.Size, cell.MinZ + cell.Size),
                new Vector2(cell.MinX + cell.Size, cell.MinZ),
            };

            var levels = new[] { cell.Corner00, cell.Corner01, cell.Corner11, cell.Corner10 };

            var highPolygon = new List<Vector2>(8);
            var lowPolygon = new List<Vector2>(8);
            var crossings = new List<Vector2>(4);

            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;

                bool isHigh = levels[i] == high;
                bool nextIsHigh = levels[next] == high;

                if (isHigh)
                {
                    highPolygon.Add(corners[i]);
                }
                else
                {
                    lowPolygon.Add(corners[i]);
                }

                if (isHigh == nextIsHigh)
                {
                    continue;
                }

                // 변의 중점에서 경계가 지나갑니다. 이것이 45도 대각선을 만듭니다.
                var crossing = (corners[i] + corners[next]) * 0.5f;

                highPolygon.Add(crossing);
                lowPolygon.Add(crossing);
                crossings.Add(crossing);
            }

            AddPolygon(top, highPolygon, high * cell.HeightStep);
            AddPolygon(top, lowPolygon, low * cell.HeightStep);

            // 경계를 지나는 두 점을 이으면 그것이 절벽면의 밑선입니다.
            // 대각으로 갈린 경우(네 점)는 두 조각이 따로 있는 것이라 순서대로 짝지어 잇습니다.
            for (int i = 0; i + 1 < crossings.Count; i += 2)
            {
                AddWall(wall, crossings[i], crossings[i + 1], low * cell.HeightStep, high * cell.HeightStep);
            }
        }

        // ====================================================================================================
        // 4. Private Methods
        // ====================================================================================================

        private static void AddFlatQuad(MeshBuffer buffer, in Cell cell, int level)
        {
            float y = level * cell.HeightStep;

            float x0 = cell.MinX;
            float z0 = cell.MinZ;
            float x1 = x0 + cell.Size;
            float z1 = z0 + cell.Size;

            buffer.AddQuad(
                new Vector3(x0, y, z0),
                new Vector3(x0, y, z1),
                new Vector3(x1, y, z1),
                new Vector3(x1, y, z0),
                Vector3.up,
                TopShade);
        }

        /// <summary>
        /// 다각형을 부채꼴로 나눠 채웁니다. 셀 안에서 잘린 조각은 언제나 볼록합니다.
        /// </summary>
        private static void AddPolygon(MeshBuffer buffer, List<Vector2> polygon, float y)
        {
            if (polygon.Count < 3)
            {
                return;
            }

            for (int i = 1; i + 1 < polygon.Count; i++)
            {
                buffer.AddTriangle(
                    new Vector3(polygon[0].x, y, polygon[0].y),
                    new Vector3(polygon[i].x, y, polygon[i].y),
                    new Vector3(polygon[i + 1].x, y, polygon[i + 1].y),
                    Vector3.up,
                    TopShade);
            }
        }

        /// <summary>
        /// 절벽면 한 조각입니다. 윗변을 안쪽으로 살짝 당겨 면을 뒤로 눕힙니다.
        /// </summary>
        private static void AddWall(MeshBuffer buffer, Vector2 a, Vector2 b, float bottom, float top)
        {
            var direction = b - a;

            if (direction.sqrMagnitude < 1e-8f)
            {
                return;
            }

            // 밑선에 수직인 방향으로 윗변을 밀어 면을 눕힙니다.
            // 어느 쪽이 위인지는 호출자가 알지만, 여기서는 높은 쪽이 언제나
            // 밑선의 왼쪽에 오도록 다각형을 구성했으므로 그 방향으로 당깁니다.
            var normal = new Vector2(-direction.y, direction.x).normalized;
            float batter = (top - bottom) * BatterRatio;

            var topA = a + normal * batter;
            var topB = b + normal * batter;

            var outward = new Vector3(-normal.x, 0f, -normal.y);

            buffer.AddQuad(
                new Vector3(topA.x, top, topA.y),
                new Vector3(topB.x, top, topB.y),
                new Vector3(b.x, bottom, b.y),
                new Vector3(a.x, bottom, a.y),
                outward,
                WallTopShade, WallTopShade, WallBottomShade, WallBottomShade);
        }
    }
}
