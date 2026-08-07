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

        /// <summary>지형 셰이더의 이름입니다.</summary>
        public const string TerrainShaderName = "SRPG/Terrain";

        /// <summary>2.5D 유닛 빌보드 셰이더의 이름입니다.</summary>
        public const string BillboardShaderName = "SRPG/Billboard";

        /// <summary>접지 그림자 셰이더의 이름입니다.</summary>
        public const string ContactShadowShaderName = "SRPG/ContactShadow";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ShadeColorId = Shader.PropertyToID("_ShadeColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

        private static Mesh s_capsuleMesh;
        private static Mesh s_cubeMesh;
        private static Mesh s_groundedQuadMesh;
        private static Mesh s_centeredQuadMesh;
        private static Material s_contactShadowMaterial;
        private static bool s_warnedMissingShader;

        /// <summary>
        /// 병종별 빌보드 머티리얼입니다.
        ///
        /// 유닛마다 새로 만들면 수백 개가 생겨 배칭이 통째로 깨집니다.
        /// 같은 병종은 같은 그림을 쓰고, 방향과 프레임은 <see cref="MaterialPropertyBlock"/>이 개별로 넘깁니다.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<UnitDefinition, Material> s_billboardMaterials
            = new System.Collections.Generic.Dictionary<UnitDefinition, Material>();

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

        /// <summary>
        /// 지형용 머티리얼을 만듭니다. 외곽선과 접지 음영이 붙은 전용 셰이더를 씁니다.
        ///
        /// 셰이더를 찾지 못하면 조용히 기본 머티리얼로 물러납니다.
        /// 셰이더 하나 때문에 게임이 실행조차 안 되는 것보다는, 밋밋하게라도 돌아가는 편이 낫습니다.
        /// </summary>
        /// <param name="color">기본 색상입니다.</param>
        /// <param name="outlineWidth">외곽선 두께입니다. 0이면 외곽선이 사실상 사라집니다.</param>
        public static Material CreateTerrainMaterial(Color color, float outlineWidth = 0.06f)
        {
            var shader = Shader.Find(TerrainShaderName);
            if (shader == null)
            {
                WarnMissingShaderOnce();
                return CreateMaterial(color);
            }

            var material = new Material(shader)
            {
                name = $"Terrain_{ColorUtility.ToHtmlStringRGB(color)}",
                hideFlags = HideFlags.DontSave,
            };

            material.SetColor(BaseColorId, color);

            // 그늘색은 기본색을 어둡게 민 것입니다.
            // 별도로 지정하게 두면 지형마다 색 조합을 손으로 맞춰야 하고, 어긋나기 쉽습니다.
            material.SetColor(ShadeColorId, color * 0.42f);
            material.SetFloat(OutlineWidthId, outlineWidth);

            return material;
        }

        /// <summary>
        /// 지형 셰이더가 없다는 경고를 한 번만 냅니다.
        /// </summary>
        private static void WarnMissingShaderOnce()
        {
            if (s_warnedMissingShader)
            {
                return;
            }

            s_warnedMissingShader = true;
            Debug.LogWarning(
                $"[PrototypeVisuals] 셰이더 '{TerrainShaderName}' 를 찾지 못해 기본 머티리얼로 대체합니다.\n" +
                "외곽선과 접지 음영이 표시되지 않습니다.");
        }

        // ====================================================================================================
        // 3. Public Methods - Unit Visual
        // ====================================================================================================

        /// <summary>
        /// 유닛의 임시 몸체를 만듭니다. 지휘관은 깃대를 달아 한눈에 구분되게 합니다.
        ///
        /// <b>몸은 빌보드 쿼드입니다.</b> 이 게임은 2.5D로 갈 예정이라, 임시 표현도 같은 형태여야
        /// 나중에 스프라이트로 갈아 끼울 때 인상이 달라지지 않습니다.
        /// 캡슐로 만들어 두면 "프로토타입은 그럴듯했는데 아트를 넣으니 이상하다"가 됩니다.
        ///
        /// 빌보드 셰이더가 없으면 캡슐로 물러납니다. 셰이더 하나 때문에 실행이 막히면 안 됩니다.
        /// </summary>
        public static GameObject CreateUnitVisual(UnitDefinition definition, Team team, bool isCommander, Material bodyMaterial)
        {
            var root = new GameObject(isCommander ? $"{definition.DisplayName}(지휘관)" : definition.DisplayName);

            var billboardShader = Shader.Find(BillboardShaderName);

            if (billboardShader != null)
            {
                CreateBillboardBody(root.transform, definition, billboardShader);
            }
            else
            {
                CreateCapsuleBody(root.transform, definition, bodyMaterial);
            }

            if (isCommander)
            {
                AttachCommanderFlag(root.transform, definition, team);
            }

            return root;
        }

        /// <summary>
        /// 빌보드 몸체를 만듭니다. 쿼드의 밑변이 원점에 오도록 만들어 발이 지면에 닿게 합니다.
        /// </summary>
        private static void CreateBillboardBody(Transform parent, UnitDefinition definition, Shader shader)
        {
            var body = new GameObject("Body");
            body.transform.SetParent(parent, false);

            float width = definition.Radius * 2.2f;
            float height = definition.DebugHeight;

            body.AddComponent<MeshFilter>().sharedMesh = GetGroundedQuadMesh();
            body.transform.localScale = new Vector3(width, height, 1f);

            var renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = GetBillboardMaterial(definition, shader);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            // 방향 판독은 이 컴포넌트가 맡습니다. 회전 자체는 셰이더가 합니다.
            parent.gameObject.AddComponent<UnitBillboard>();
        }

        /// <summary>
        /// 병종이 공유하는 빌보드 머티리얼을 가져옵니다. 없으면 만들어 담아 둡니다.
        /// </summary>
        private static Material GetBillboardMaterial(UnitDefinition definition, Shader shader)
        {
            // 씬을 다시 열면 머티리얼만 파괴되고 항목은 남습니다. 그때는 새로 만듭니다.
            if (s_billboardMaterials.TryGetValue(definition, out var cached) && cached != null)
            {
                return cached;
            }

            var material = new Material(shader)
            {
                name = $"Billboard_{definition.name}",
                hideFlags = HideFlags.DontSave,
            };

            // 아직 스프라이트가 없습니다. 단색 실루엣만으로도 형태와 외곽선은 읽힙니다.
            material.SetColor(BaseColorId, definition.DebugColor);

            s_billboardMaterials[definition] = material;
            return material;
        }

        /// <summary>
        /// 캡슐 몸체를 만듭니다. 빌보드 셰이더가 없을 때의 폴백입니다.
        /// </summary>
        private static void CreateCapsuleBody(Transform parent, UnitDefinition definition, Material bodyMaterial)
        {
            var body = new GameObject("Body");
            body.transform.SetParent(parent, false);
            body.transform.localPosition = new Vector3(0f, definition.DebugHeight * 0.5f, 0f);
            body.transform.localScale = new Vector3(
                definition.Radius * 2f,
                definition.DebugHeight * 0.5f,
                definition.Radius * 2f);

            body.AddComponent<MeshFilter>().sharedMesh = GetCapsuleMesh();

            var renderer = body.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = bodyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        // ====================================================================================================
        // 4. Public Methods - Shared Meshes
        // ====================================================================================================

        /// <summary>
        /// 밑변이 원점에 오는 1×1 쿼드입니다. 빌보드 몸체가 씁니다.
        ///
        /// 유니티 기본 쿼드는 원점이 가운데라 그대로 쓰면 유닛이 땅에 반쯤 박힙니다.
        /// 발밑을 원점으로 두면 지면에 세우기만 하면 정확히 서 있습니다.
        /// </summary>
        public static Mesh GetGroundedQuadMesh()
        {
            if (s_groundedQuadMesh != null)
            {
                return s_groundedQuadMesh;
            }

            var mesh = new Mesh
            {
                name = "SRPG_GroundedQuad",
                hideFlags = HideFlags.DontSave,
            };

            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, 0f, 0f),
                new Vector3(-0.5f, 1f, 0f),
                new Vector3(0.5f, 1f, 0f),
                new Vector3(0.5f, 0f, 0f),
            });

            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
            });

            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            s_groundedQuadMesh = mesh;
            return s_groundedQuadMesh;
        }

        /// <summary>
        /// 원점이 가운데인 1×1 쿼드입니다. 접지 그림자가 씁니다.
        /// </summary>
        public static Mesh GetCenteredQuadMesh()
        {
            if (s_centeredQuadMesh != null)
            {
                return s_centeredQuadMesh;
            }

            var mesh = new Mesh
            {
                name = "SRPG_CenteredQuad",
                hideFlags = HideFlags.DontSave,
            };

            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
            });

            mesh.SetUVs(0, new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
            });

            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            s_centeredQuadMesh = mesh;
            return s_centeredQuadMesh;
        }

        /// <summary>
        /// 접지 그림자가 공유하는 머티리얼입니다.
        ///
        /// 유닛마다 새로 만들면 수백 개의 머티리얼이 생기고 배칭이 통째로 깨집니다.
        /// 그림자는 전부 같은 모습이므로 하나로 충분합니다.
        /// </summary>
        public static Material GetSharedContactShadowMaterial(Shader shader)
        {
            if (s_contactShadowMaterial != null)
            {
                return s_contactShadowMaterial;
            }

            s_contactShadowMaterial = new Material(shader)
            {
                name = "SRPG_ContactShadow",
                hideFlags = HideFlags.DontSave,
            };

            return s_contactShadowMaterial;
        }

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
