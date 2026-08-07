using System.Collections.Generic;
using UnityEngine;

namespace SRPG.Systems.Meshing
{
    /// <summary>
    /// 메시를 쌓아 올리는 버퍼입니다.
    ///
    /// <b>정점 컬러를 함께 담습니다.</b>
    /// 셰이더가 R 채널을 <b>접지 음영</b>으로 읽습니다.
    /// 텍스처 없이 경계를 세우는 것이 이 게임 룩의 핵심이라, 그 정보는 지오메트리와
    /// 같은 자리에서 같이 만들어져야 합니다. 나중에 칠하려 들면 반드시 어긋납니다.
    ///
    /// <b>정점을 공유하지 않습니다.</b>
    /// 면마다 정점을 새로 넣습니다. 그래야 <see cref="Mesh.RecalculateNormals"/>가
    /// 면 단위 법선을 만들어 각진 저폴리 음영이 나옵니다.
    /// 정점을 공유하면 법선이 평균 나서 뭉개지고, 이 룩에서는 그게 손해입니다.
    /// </summary>
    public sealed class MeshBuffer
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        public readonly List<Vector3> Vertices = new List<Vector3>(1024);
        public readonly List<int> Triangles = new List<int>(2048);
        public readonly List<Color> Colors = new List<Color>(1024);

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>쌓인 정점 수입니다.</summary>
        public int Count => Vertices.Count;

        /// <summary>아무것도 쌓이지 않았는지 여부입니다.</summary>
        public bool IsEmpty => Vertices.Count == 0;

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        public void Clear()
        {
            Vertices.Clear();
            Triangles.Clear();
            Colors.Clear();
        }

        /// <summary>
        /// 삼각형을 추가합니다. 세 꼭짓점의 음영을 각각 받습니다.
        ///
        /// 감기 순서를 손으로 맞추는 대신, 만들어진 법선이 원하는 방향과 반대면 뒤집습니다.
        /// 면 방향마다 순서를 유도하다 생기는 실수를 원천 차단하기 위한 방식입니다.
        /// </summary>
        public void AddTriangle(
            Vector3 a, Vector3 b, Vector3 c,
            Vector3 desiredNormal,
            float shadeA, float shadeB, float shadeC)
        {
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), desiredNormal) < 0f;

            int baseIndex = Vertices.Count;

            Vertices.Add(a);
            Vertices.Add(b);
            Vertices.Add(c);

            Colors.Add(new Color(shadeA, 0f, 0f, 1f));
            Colors.Add(new Color(shadeB, 0f, 0f, 1f));
            Colors.Add(new Color(shadeC, 0f, 0f, 1f));

            if (flip)
            {
                Triangles.Add(baseIndex + 0);
                Triangles.Add(baseIndex + 2);
                Triangles.Add(baseIndex + 1);
            }
            else
            {
                Triangles.Add(baseIndex + 0);
                Triangles.Add(baseIndex + 1);
                Triangles.Add(baseIndex + 2);
            }
        }

        /// <summary>세 꼭짓점의 음영이 같은 삼각형입니다.</summary>
        public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 desiredNormal, float shade)
        {
            AddTriangle(a, b, c, desiredNormal, shade, shade, shade);
        }

        /// <summary>
        /// 사각형을 추가합니다. 네 꼭짓점의 음영을 각각 받습니다.
        /// </summary>
        public void AddQuad(
            Vector3 a, Vector3 b, Vector3 c, Vector3 d,
            Vector3 desiredNormal,
            float shadeA, float shadeB, float shadeC, float shadeD)
        {
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), desiredNormal) < 0f;

            int baseIndex = Vertices.Count;

            Vertices.Add(a);
            Vertices.Add(b);
            Vertices.Add(c);
            Vertices.Add(d);

            Colors.Add(new Color(shadeA, 0f, 0f, 1f));
            Colors.Add(new Color(shadeB, 0f, 0f, 1f));
            Colors.Add(new Color(shadeC, 0f, 0f, 1f));
            Colors.Add(new Color(shadeD, 0f, 0f, 1f));

            if (flip)
            {
                Triangles.Add(baseIndex + 0);
                Triangles.Add(baseIndex + 3);
                Triangles.Add(baseIndex + 2);
                Triangles.Add(baseIndex + 0);
                Triangles.Add(baseIndex + 2);
                Triangles.Add(baseIndex + 1);
            }
            else
            {
                Triangles.Add(baseIndex + 0);
                Triangles.Add(baseIndex + 1);
                Triangles.Add(baseIndex + 2);
                Triangles.Add(baseIndex + 0);
                Triangles.Add(baseIndex + 2);
                Triangles.Add(baseIndex + 3);
            }
        }

        /// <summary>네 꼭짓점의 음영이 같은 사각형입니다.</summary>
        public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 desiredNormal, float shade)
        {
            AddQuad(a, b, c, d, desiredNormal, shade, shade, shade, shade);
        }

        /// <summary>
        /// 쌓인 내용을 메시로 굽습니다.
        /// </summary>
        /// <param name="meshName">메시 이름입니다.</param>
        public Mesh ToMesh(string meshName)
        {
            var mesh = new Mesh
            {
                name = meshName,

                // 65000 정점을 넘으면 16비트 인덱스로는 담을 수 없습니다.
                // 프롭이 붙으면 지형 하나가 이 선을 쉽게 넘습니다.
                indexFormat = Vertices.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };

            mesh.SetVertices(Vertices);
            mesh.SetTriangles(Triangles, 0);
            mesh.SetColors(Colors);

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
