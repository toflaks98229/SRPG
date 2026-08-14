using System.Collections.Generic;
using System.IO;
using SRPG.Gameplay.Visual;
using UnityEditor;
using UnityEngine;

namespace SRPG.Editor.Tools
{
    /// <summary>
    /// 유닛 프리팹의 몸체를 만듭니다.
    ///
    /// <b>왜 따로 있는가</b>
    ///
    /// 유닛의 몸을 만드는 코드가 두 곳에 있었습니다.
    ///   · <see cref="PrototypeAssetBuilder"/> — 프리팹을 굽습니다
    ///   · <see cref="SRPG.Gameplay.Visual.PrototypeVisuals"/> — 프리팹이 없을 때 런타임에 만듭니다
    ///
    /// 2.5D 빌보드로 바꿀 때 런타임 쪽만 고쳤습니다. 그래서 프리팹을 다시 구울 때마다
    /// 몸이 조용히 캡슐로 되돌아갔습니다. 실제로 그 일이 일어났습니다 —
    /// 메뉴 한 번에 빌보드 작업이 통째로 사라졌고, 아무 오류도 나지 않았습니다.
    ///
    /// 같은 개념을 두 곳에서 구현하면 반드시 갈라집니다.
    /// 프리팹 쪽 구현을 여기 하나로 모아, 굽든 고치든 같은 몸이 나오게 합니다.
    ///
    /// <b>왜 런타임 쪽과도 합치지 않는가</b>
    ///
    /// 프리팹은 디스크에 있는 메시만 참조할 수 있습니다.
    /// 런타임 쪽이 쓰는 메시는 <see cref="HideFlags.DontSave"/>라 프리팹에 넣을 수 없습니다.
    /// 형상 정의는 <see cref="PrototypeVisuals.GetGroundedQuadMesh"/> 하나를 따르되,
    /// 그것을 에셋으로 구워 두고 프리팹은 그 에셋을 봅니다.
    /// </summary>
    public static class UnitBodyBuilder
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>생성한 메시가 저장되는 폴더입니다.</summary>
        private const string MeshDirectory = "Assets/_Project/04.Art/02.Models";
        /// <summary>밑변이 원점에 오는 공유 쿼드 메시의 경로입니다.</summary>
        private const string GroundedQuadPath = MeshDirectory + "/SRPG_GroundedQuad.mesh";

        /// <summary>쿼드의 가로가 유닛 반경의 몇 배인지입니다.</summary>
        private const float WidthPerRadius = 2.2f;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 빌보드 몸체를 만들어 붙입니다. 이미 있으면 그것을 고칩니다.
        /// </summary>
        /// <param name="root">유닛 프리팹의 루트입니다.</param>
        /// <param name="radius">유닛의 반경입니다.</param>
        /// <param name="height">유닛의 높이입니다.</param>
        /// <param name="material">몸체 머티리얼입니다. 빌보드 셰이더로 바뀝니다.</param>
        /// <returns>만들어진 몸체입니다. 셰이더가 없으면 null입니다.</returns>
        public static GameObject Build(GameObject root, float radius, float height, Material material)
        {
            var shader = Shader.Find(PrototypeVisuals.BillboardShaderName);

            if (root == null || shader == null)
            {
                return null;
            }

            var body = root.transform.Find("Body")?.gameObject;

            if (body == null)
            {
                body = new GameObject("Body");
                body.transform.SetParent(root.transform, false);
            }

            // 쿼드의 원점이 밑변이라 발밑에 놓기만 하면 정확히 섭니다.
            // 캡슐은 원점이 가운데라 절반 높이만큼 띄워 두었습니다. 그 보정을 걷어냅니다.
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = new Vector3(radius * WidthPerRadius, height, 1f);

            var filter = body.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = body.AddComponent<MeshFilter>();
            }

            filter.sharedMesh = LoadOrCreateGroundedQuad();

            var renderer = body.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = body.AddComponent<MeshRenderer>();
            }

            renderer.sharedMaterial = RetargetToBillboard(material, shader);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            AttachDirectionReader(root, renderer);

            return body;
        }

        /// <summary>
        /// 이 몸체가 이미 빌보드인지 봅니다.
        /// </summary>
        /// <param name="root">검사할 유닛 프리팹의 루트입니다.</param>
        /// <returns>몸체가 빌보드 쿼드로 만들어져 있으면 true입니다.</returns>
        public static bool IsBillboard(GameObject root)
        {
            if (root == null || root.GetComponent<UnitBillboard>() == null)
            {
                return false;
            }

            var body = root.transform.Find("Body");
            var renderer = body != null ? body.GetComponent<MeshRenderer>() : null;

            return renderer != null
                && renderer.sharedMaterial != null
                && renderer.sharedMaterial.shader != null
                && renderer.sharedMaterial.shader.name == PrototypeVisuals.BillboardShaderName;
        }

        /// <summary>
        /// 프리팹이 참조할 수 있는 빌보드 쿼드를 에셋으로 만듭니다.
        ///
        /// 런타임 생성 메시는 <see cref="HideFlags.DontSave"/>라 프리팹이 참조할 수 없습니다.
        /// 형상은 런타임 쪽과 같은 정의에서 복사해 옵니다. 모양이 두 벌이면 반드시 갈라집니다.
        /// </summary>
        /// <returns>밑변이 원점에 오는 공유 쿼드 메시 에셋입니다. 없으면 만들어 저장합니다.</returns>
        public static Mesh LoadOrCreateGroundedQuad()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(GroundedQuadPath);
            if (existing != null)
            {
                return existing;
            }

            EnsureFolder(MeshDirectory);

            var source = PrototypeVisuals.GetGroundedQuadMesh();

            var asset = new Mesh { name = "SRPG_GroundedQuad" };
            asset.SetVertices(source.vertices);
            asset.SetUVs(0, new List<Vector2>(source.uv));
            asset.SetTriangles(source.triangles, 0);
            asset.RecalculateNormals();
            asset.RecalculateBounds();

            AssetDatabase.CreateAsset(asset, GroundedQuadPath);
            AssetDatabase.SaveAssets();

            return asset;
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 몸체 머티리얼을 빌보드 셰이더로 바꿉니다. 색은 유지합니다.
        ///
        /// 새로 만들지 않고 기존 것을 바꿉니다. 프리팹이 이미 참조하고 있으니
        /// 참조를 새로 이을 필요가 없고, <b>병종별로 하나</b>라는 성질도 유지됩니다.
        /// </summary>
        private static Material RetargetToBillboard(Material material, Shader shader)
        {
            if (material == null || material.shader == shader)
            {
                return material;
            }

            Color color = material.HasProperty("_BaseColor")
                ? material.GetColor("_BaseColor")
                : Color.white;

            material.shader = shader;
            material.SetColor("_BaseColor", color);

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 방향 판독기를 붙이고 렌더러를 물려 줍니다.
        ///
        /// 렌더러를 비워 두면 <c>GetComponentInChildren</c> 가
        /// 무기나 깃발의 렌더러를 집을 수 있습니다.
        /// </summary>
        private static void AttachDirectionReader(GameObject root, Renderer renderer)
        {
            var billboard = root.GetComponent<UnitBillboard>();

            if (billboard == null)
            {
                billboard = root.AddComponent<UnitBillboard>();
            }

            var serialized = new SerializedObject(billboard);
            serialized.FindProperty("_renderer").objectReferenceValue = renderer;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');

            if (!string.IsNullOrEmpty(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
