using SRPG.Common;
using SRPG.Data;
using UnityEngine;

namespace SRPG.Gameplay.Visual
{
    /// <summary>
    /// 프로토타입 단계의 임시 시각 표현을 만듭니다.
    /// 아트 에셋 없이 코드만으로 실행 가능한 상태를 유지하기 위한 장치이며, 아트가 들어오면 통째로 교체됩니다.
    /// </summary>
    public static class PrototypeVisuals
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        private static Mesh s_capsuleMesh;
        private static Mesh s_cubeMesh;

        // ====================================================================================================
        // 2. Public Methods - Material
        // ====================================================================================================

        /// <summary>
        /// 단색 머티리얼을 만듭니다.
        /// URP Lit은 색상 프로퍼티가 <c>_BaseColor</c>이므로 <c>Material.color</c>만으로는 색이 적용되지 않습니다.
        /// </summary>
        public static Material CreateMaterial(Color color, float smoothness = 0.05f)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            var material = new Material(shader)
            {
                name = $"Proto_{ColorUtility.ToHtmlStringRGB(color)}",
                hideFlags = HideFlags.DontSave,
            };

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", smoothness);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }

            return material;
        }

        // ====================================================================================================
        // 3. Public Methods - Unit Visual
        // ====================================================================================================

        /// <summary>
        /// 유닛의 임시 몸체를 만듭니다. 지휘관은 깃대를 달아 한눈에 구분되게 합니다.
        /// </summary>
        public static GameObject CreateUnitVisual(UnitDefinition definition, Team team, bool isCommander, Material bodyMaterial)
        {
            var root = new GameObject(isCommander ? $"{definition.DisplayName}(지휘관)" : definition.DisplayName);

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, definition.DebugHeight * 0.5f, 0f);
            body.transform.localScale = new Vector3(
                definition.Radius * 2f,
                definition.DebugHeight * 0.5f,
                definition.Radius * 2f);

            var bodyFilter = body.AddComponent<MeshFilter>();
            bodyFilter.sharedMesh = GetCapsuleMesh();

            var bodyRenderer = body.AddComponent<MeshRenderer>();
            bodyRenderer.sharedMaterial = bodyMaterial;
            bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            if (isCommander)
            {
                AttachCommanderFlag(root.transform, definition, team);
            }

            return root;
        }

        // ====================================================================================================
        // 4. Public Methods - Shared Meshes
        // ====================================================================================================

        /// <summary>공유 캡슐 메시입니다.</summary>
        public static Mesh GetCapsuleMesh()
        {
            if (s_capsuleMesh == null)
            {
                s_capsuleMesh = ExtractPrimitiveMesh(PrimitiveType.Capsule);
            }

            return s_capsuleMesh;
        }

        /// <summary>공유 큐브 메시입니다.</summary>
        public static Mesh GetCubeMesh()
        {
            if (s_cubeMesh == null)
            {
                s_cubeMesh = ExtractPrimitiveMesh(PrimitiveType.Cube);
            }

            return s_cubeMesh;
        }

        // ====================================================================================================
        // 5. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 지휘관 깃발을 붙입니다. 깃대와 깃폭 두 조각으로 실루엣만 만듭니다.
        /// </summary>
        private static void AttachCommanderFlag(Transform parent, UnitDefinition definition, Team team)
        {
            float poleHeight = definition.DebugHeight * 1.6f;

            var pole = new GameObject("FlagPole");
            pole.transform.SetParent(parent, false);
            pole.transform.localPosition = new Vector3(0f, poleHeight * 0.5f, 0f);
            pole.transform.localScale = new Vector3(0.07f, poleHeight, 0.07f);
            pole.AddComponent<MeshFilter>().sharedMesh = GetCubeMesh();
            pole.AddComponent<MeshRenderer>().sharedMaterial = CreateMaterial(new Color(0.25f, 0.2f, 0.16f));

            var flag = new GameObject("FlagCloth");
            flag.transform.SetParent(parent, false);
            flag.transform.localPosition = new Vector3(0.22f, poleHeight * 0.86f, 0f);
            flag.transform.localScale = new Vector3(0.42f, 0.28f, 0.05f);
            flag.AddComponent<MeshFilter>().sharedMesh = GetCubeMesh();

            Color flagColor = team == Team.Player
                ? new Color(0.95f, 0.92f, 0.85f)
                : new Color(0.35f, 0.1f, 0.12f);

            flag.AddComponent<MeshRenderer>().sharedMaterial = CreateMaterial(flagColor);
        }

        /// <summary>
        /// 프리미티브를 임시 생성해 메시만 뽑아내고 즉시 파괴합니다.
        /// 런타임에 메시를 직접 만드는 것보다 짧고, 프로토타입 용도로 충분합니다.
        /// </summary>
        private static Mesh ExtractPrimitiveMesh(PrimitiveType type)
        {
            var temp = GameObject.CreatePrimitive(type);
            var mesh = temp.GetComponent<MeshFilter>().sharedMesh;

            if (Application.isPlaying)
            {
                Object.Destroy(temp);
            }
            else
            {
                Object.DestroyImmediate(temp);
            }

            return mesh;
        }
    }
}
