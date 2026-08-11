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

        /// <summary>물 셰이더의 이름입니다.</summary>
        public const string WaterShaderName = "SRPG/Water";

        /// <summary>2.5D 유닛 빌보드 셰이더의 이름입니다.</summary>
        public const string BillboardShaderName = "SRPG/Billboard";

        /// <summary>접지 그림자 셰이더의 이름입니다.</summary>
        public const string ContactShadowShaderName = "SRPG/ContactShadow";

        /// <summary>풀 셰이더의 이름입니다.</summary>
        public const string GrassShaderName = "SRPG/Grass";

        /// <summary>URP Lit 의 기본 색 프로퍼티 식별자입니다.</summary>
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        /// <summary>지형 셰이더의 해수면 프로퍼티 식별자입니다.</summary>
        private static readonly int SeaLevelId = Shader.PropertyToID("_SeaLevel");
        /// <summary>지형 셰이더의 고도폭 프로퍼티 식별자입니다.</summary>
        private static readonly int HeightRangeId = Shader.PropertyToID("_HeightRange");
        /// <summary>지형 셰이더의 등반 한계 프로퍼티 식별자입니다.</summary>
        private static readonly int ClimbLimitId = Shader.PropertyToID("_ClimbLimit");
        /// <summary>물 셰이더의 정지 수심 지도 식별자입니다.</summary>
        private static readonly int WaterDepthMapId = Shader.PropertyToID("_WaterDepthMap");
        /// <summary>물 셰이더의 수심 지도 범위 식별자입니다.</summary>
        private static readonly int WaterDepthAreaId = Shader.PropertyToID("_WaterDepthArea");
        /// <summary>물 셰이더의 파도 감쇠 폭 식별자입니다.</summary>
        private static readonly int WaveShoreFadeId = Shader.PropertyToID("_WaveShoreFade");
        /// <summary>물 셰이더의 물가 거품선 폭 식별자입니다.</summary>
        private static readonly int ShoreWidthId = Shader.PropertyToID("_ShoreWidth");
        /// <summary>물 셰이더의 수심 감쇠 폭 식별자입니다.</summary>
        private static readonly int DepthFadeId = Shader.PropertyToID("_DepthFade");
        /// <summary>풀 셰이더의 잎 그림 식별자입니다.</summary>
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        /// <summary>풀 셰이더의 강조풀 그림 식별자입니다.</summary>
        private static readonly int AccentMapId = Shader.PropertyToID("_AccentMap");

        /// <summary>
        /// 거품선이 지면에서 차지해야 할 폭입니다. 미터 단위입니다.
        ///
        /// 넓히면 얕은 여울이 통째로 하얘져 수심이 안 읽히고,
        /// 좁히면 화면에서 사라집니다. 화면으로 확인하며 고른 값입니다.
        /// </summary>
        private const float ShoreBandMeters = 0.9f;

        /// <summary>공유 캡슐 메시 캐시입니다.</summary>
        private static Mesh s_capsuleMesh;
        /// <summary>공유 큐브 메시 캐시입니다.</summary>
        private static Mesh s_cubeMesh;
        /// <summary>밑변이 원점에 오는 공유 쿼드 메시 캐시입니다.</summary>
        private static Mesh s_groundedQuadMesh;
        /// <summary>원점이 가운데인 공유 쿼드 메시 캐시입니다.</summary>
        private static Mesh s_centeredQuadMesh;
        /// <summary>파도가 설 수 있도록 잘게 나눈 수면 메시 캐시입니다.</summary>
        private static Mesh s_waterGridMesh;
        /// <summary>캐시된 수면 메시의 분할 수입니다.</summary>
        private static int s_waterGridSegments;
        /// <summary>기본 잎 그림 캐시입니다.</summary>
        private static Texture2D s_grassSprite;
        /// <summary>강조풀 그림 캐시입니다.</summary>
        private static Texture2D s_accentSprite;
        /// <summary>잎 그림 누락 경고를 이미 냈는지 여부입니다.</summary>
        private static bool s_warnedMissingSprite;
        /// <summary>모든 접지 그림자가 공유하는 머티리얼입니다.</summary>
        private static Material s_contactShadowMaterial;
        /// <summary>셰이더 누락 경고를 이미 냈는지 여부입니다.</summary>
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
        /// <param name="color">칠할 기본 색입니다.</param>
        /// <param name="smoothness">표면의 매끄러움입니다. 0에 가까울수록 무광입니다.</param>
        /// <returns>색과 매끄러움이 적용된 새 머티리얼입니다.</returns>
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
        /// 터레인용 머티리얼을 만듭니다.
        ///
        /// <b>왜 전장마다 새로 만드는가</b>
        ///
        /// 지형 셰이더는 해수면·고도폭·등반 한계를 알아야 색을 정합니다.
        /// 그런데 그 셋은 <b>전장마다 다릅니다</b> — 언덕 전장과 평야 전장의 고도폭이 같을 리 없습니다.
        /// 공유 에셋 하나에 써 넣으면 마지막에 만들어진 전장의 값이 남아
        /// 다음 전장에서 엉뚱한 높이에 물가 띠가 그려집니다.
        ///
        /// 그래서 에셋은 색과 취향만 들고, 전장에 종속된 세 숫자는 여기서 덮어씁니다.
        /// </summary>
        /// <param name="source">기준이 되는 에셋 머티리얼입니다. 비어 있으면 셰이더에서 새로 만듭니다.</param>
        /// <param name="seaLevel">해수면의 월드 높이입니다.</param>
        /// <param name="heightRange">해수면 위로 올라가는 높이의 폭입니다.</param>
        /// <param name="climbLimitDegrees">생성기가 절벽을 가른 기울기입니다.</param>
        /// <returns>이번 전장의 세 숫자가 덮어써진 새 머티리얼입니다.</returns>
        public static Material CreateTerrainMaterial(
            Material source,
            float seaLevel,
            float heightRange,
            float climbLimitDegrees)
        {
            var shader = Shader.Find(TerrainShaderName);

            if (shader == null)
            {
                WarnMissingShaderOnce(TerrainShaderName, "고도·경사에 따른 지형 색이 나오지 않습니다.");
                return null;
            }

            // 에셋이 있으면 그것을 복제해 색 취향을 물려받고, 없으면 셰이더 기본값으로 시작합니다.
            var material = source != null && source.shader == shader
                ? new Material(source)
                : new Material(shader);

            material.name = "Terrain_Runtime";
            material.hideFlags = HideFlags.DontSave;

            material.SetFloat(SeaLevelId, seaLevel);
            material.SetFloat(HeightRangeId, Mathf.Max(0.5f, heightRange));
            material.SetFloat(ClimbLimitId, climbLimitDegrees);

            return material;
        }

        /// <summary>
        /// 물 머티리얼을 만듭니다.
        ///
        /// 셰이더를 찾지 못하면 불투명한 파란 판으로 물러납니다.
        /// 수심도 여울도 보이지 않지만, 적어도 물이 있어야 할 자리에 물이 있습니다.
        /// </summary>
        /// <param name="fallbackColor">셰이더를 찾지 못했을 때 쓸 단색입니다.</param>
        /// <returns>물 머티리얼입니다. 셰이더가 없으면 불투명한 대체 머티리얼입니다.</returns>
        public static Material CreateWaterMaterial(Color fallbackColor)
        {
            return CreateWaterMaterial(null, null, Vector4.zero, 0f, 0f, fallbackColor);
        }

        /// <summary>
        /// 이번 전장의 수심 지도를 물고 있는 물 머티리얼을 만듭니다.
        ///
        /// <b>왜 전장마다 새로 만드는가</b>
        ///
        /// 지형 머티리얼과 같은 이유입니다. 물 셰이더는 파도를 물가에서 죽이려고
        /// <b>정지 상태의 수심</b>을 알아야 하는데, 그 지도는 전장마다 다릅니다.
        /// 공유 에셋에 써 넣으면 마지막 전장의 지도가 남아, 다음 전장에서는
        /// 뭍 위에 파도가 서거나 깊은 물이 잔잔해집니다.
        ///
        /// 그래서 에셋은 색과 취향만 물려주고, 전장에 종속된 값은 여기서 덮어씁니다.
        /// </summary>
        /// <param name="source">기준이 되는 에셋 머티리얼입니다. 비어 있으면 셰이더에서 새로 만듭니다.</param>
        /// <param name="restDepthMap">정지 수심 지도입니다. 붉은 채널이 미터 단위 수심입니다.</param>
        /// <param name="depthArea">(원점X, 원점Z, 월드→UV 배율, 반 텍셀 보정)입니다.</param>
        /// <param name="maxRestDepth">이 전장에서 가장 깊은 물의 깊이입니다. 0이면 폭을 덮어쓰지 않습니다.</param>
        /// <param name="shorelineSlope">물가에서 1미터 나아갈 때 깊어지는 정도입니다.</param>
        /// <param name="fallbackColor">셰이더를 찾지 못했을 때 쓸 단색입니다.</param>
        /// <returns>물 머티리얼입니다. 셰이더가 없으면 불투명한 대체 머티리얼입니다.</returns>
        public static Material CreateWaterMaterial(
            Material source,
            Texture restDepthMap,
            Vector4 depthArea,
            float maxRestDepth,
            float shorelineSlope,
            Color fallbackColor)
        {
            var shader = Shader.Find(WaterShaderName);

            if (shader == null)
            {
                WarnMissingShaderOnce(WaterShaderName, "수심에 따른 여울이 드러나지 않습니다.");
                return CreateMaterial(fallbackColor);
            }

            // 에셋이 있으면 그것을 복제해 색 취향을 물려받고, 없으면 셰이더 기본값으로 시작합니다.
            var material = source != null && source.shader == shader
                ? new Material(source)
                : new Material(shader);

            material.name = "Water_Runtime";
            material.hideFlags = HideFlags.DontSave;

            if (restDepthMap != null)
            {
                material.SetTexture(WaterDepthMapId, restDepthMap);
                material.SetVector(WaterDepthAreaId, depthArea);
            }

            // <b>폭을 이 전장의 수심 규모에 맞춥니다.</b>
            //
            // 파도가 잦아드는 폭과 거품선의 폭을 셰이더나 에셋에 고정해 두면,
            // 기복이 얕은 전장에서는 <b>전장 전체가 얕은 물로 판정</b>됩니다.
            // 그러면 지도 안쪽 물이 통째로 잔잔해져 지도 경계가 사각형으로 드러나고,
            // 거품선은 강 전체를 덮거나 아예 나오지 않습니다.
            //
            // 절대 길이가 아니라 이 전장의 가장 깊은 곳에 대한 비율이어야 합니다.
            if (maxRestDepth > 0f)
            {
                // 감쇠 폭은 <b>가장 깊은 곳보다 넉넉해야</b> 합니다.
                // 같게 두면 강의 가장 깊은 자리에서 감쇠가 포화되어 파도가 온전히 서고,
                // 무릎 깊이의 여울에 파도 마루와 거품이 뜹니다.
                material.SetFloat(WaveShoreFadeId, Mathf.Clamp(maxRestDepth * 2.5f, 0.3f, 8f));

                // 거품선의 폭은 <b>최대 수심이 아니라 물가의 경사</b>에서 나옵니다.
                //
                // 최대 수심에 비례시켰더니 얕은 강이 있는 전장에서 거품선이 강 전체를 덮어
                // 하얗게 만들었습니다. 가장 깊은 곳은 지도 가장자리의 먼바다여서
                // 강의 깊이와 아무 상관이 없었기 때문입니다.
                //
                // 거품선은 지면에서 늘 비슷한 <b>폭</b>으로 보여야 합니다.
                // 그 폭에 물가의 기울기를 곱하면 깊이 단위의 문턱이 나옵니다.
                if (shorelineSlope > 0f)
                {
                    material.SetFloat(ShoreWidthId, Mathf.Clamp(shorelineSlope * ShoreBandMeters, 0.02f, 2f));
                }

                // 얕은 색에서 깊은 색으로 넘어가는 폭입니다.
                //
                // 이것도 전장에 맞춰야 합니다. 고정해 두면 기복이 얕은 전장에서는
                // 강 전체가 '얕은 물' 한 색이 되어 <b>여울이 드러나지 않습니다</b> —
                // 어디로 건널지 눈으로 읽게 하려던 목적이 그대로 무너집니다.
                // 지형이 끝나는 자리에서 색이 튀는 것도 이 폭이 좁을수록 덜합니다.
                material.SetFloat(DepthFadeId, Mathf.Clamp(maxRestDepth * 1.6f, 0.3f, 12f));
            }

            return material;
        }

        /// <summary>
        /// 셰이더가 없다는 경고를 한 번만 냅니다.
        /// 프레임마다 나오면 콘솔이 잠겨 정작 중요한 오류를 못 봅니다.
        /// </summary>
        private static void WarnMissingShaderOnce(string shaderName, string consequence)
        {
            if (s_warnedMissingShader)
            {
                return;
            }

            s_warnedMissingShader = true;
            Debug.LogWarning(
                $"[PrototypeVisuals] 셰이더 '{shaderName}' 를 찾지 못해 기본 머티리얼로 대체합니다.\n" +
                consequence);
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
        /// <param name="definition">병과 정의입니다. 몸체 크기를 여기서 읽습니다.</param>
        /// <param name="team">소속 진영입니다. 임시 색을 가르는 기준입니다.</param>
        /// <param name="isCommander">지휘관이면 깃대를 답니다.</param>
        /// <param name="bodyMaterial">몸체에 입힐 머티리얼입니다.</param>
        /// <returns>아직 <c>Unit</c> 컴포넌트가 붙지 않은 임시 몸체 오브젝트입니다.</returns>
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
        /// <returns>공유 쿼드 메시입니다. 처음 호출할 때 만들어 캐시합니다.</returns>
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
        /// <returns>공유 쿼드 메시입니다. 처음 호출할 때 만들어 캐시합니다.</returns>
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
        /// 파도가 설 수 있도록 잘게 나눈 1×1 수면 메시입니다.
        ///
        /// <b>왜 쿼드 한 장으로는 안 되는가</b>
        ///
        /// 거스트너 파도는 <b>정점을 밀어 올려</b> 파형을 만듭니다.
        /// 유니티 기본 쿼드는 정점이 네 귀퉁이뿐이라 아무리 파도 셰이더를 붙여도
        /// 네 모서리만 오르내리고 가운데는 평평한 채로 남습니다.
        ///
        /// 분할 수는 <b>파장보다 촘촘해야</b> 합니다. 한 파장에 정점이 서너 개도 없으면
        /// 마루가 뭉개지고 각진 삼각형이 그대로 보입니다.
        /// </summary>
        /// <param name="segments">한 변의 분할 수입니다. 정점 수가 65535를 넘지 않도록 제한됩니다.</param>
        /// <returns>가운데가 원점인 공유 수면 메시입니다. 분할 수가 바뀌면 다시 만듭니다.</returns>
        public static Mesh GetWaterGridMesh(int segments)
        {
            // 16비트 인덱스로 다룰 수 있는 한계입니다. 넘기면 유니티가 메시를 통째로 버립니다.
            segments = Mathf.Clamp(segments, 1, 250);

            if (s_waterGridMesh != null && s_waterGridSegments == segments)
            {
                return s_waterGridMesh;
            }

            int side = segments + 1;
            int vertexCount = side * side;

            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var triangles = new int[segments * segments * 6];

            float step = 1f / segments;

            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++)
                {
                    int index = y * side + x;

                    vertices[index] = new Vector3(x * step - 0.5f, y * step - 0.5f, 0f);

                    // 파도의 노멀은 셰이더가 접선에서 직접 구합니다. 여기서는 평면의 노멀만 둡니다.
                    normals[index] = new Vector3(0f, 0f, -1f);
                }
            }

            int triangle = 0;

            for (int y = 0; y < segments; y++)
            {
                for (int x = 0; x < segments; x++)
                {
                    int bottomLeft = y * side + x;
                    int topLeft = bottomLeft + side;

                    triangles[triangle++] = bottomLeft;
                    triangles[triangle++] = topLeft;
                    triangles[triangle++] = topLeft + 1;

                    triangles[triangle++] = bottomLeft;
                    triangles[triangle++] = topLeft + 1;
                    triangles[triangle++] = bottomLeft + 1;
                }
            }

            if (s_waterGridMesh == null)
            {
                s_waterGridMesh = new Mesh
                {
                    name = "SRPG_WaterGrid",
                    hideFlags = HideFlags.DontSave,
                };
            }

            s_waterGridMesh.Clear();
            s_waterGridMesh.SetVertices(vertices);
            s_waterGridMesh.SetNormals(normals);
            s_waterGridMesh.SetTriangles(triangles, 0);

            // 파도가 정점을 밀어 올리므로 원래 평면보다 커집니다.
            // 경계를 넉넉히 잡지 않으면 화면 가장자리에서 수면이 통째로 컬링됩니다.
            s_waterGridMesh.RecalculateBounds();

            var bounds = s_waterGridMesh.bounds;
            bounds.Expand(new Vector3(0f, 0f, 0.5f));
            s_waterGridMesh.bounds = bounds;

            s_waterGridSegments = segments;
            return s_waterGridMesh;
        }

        /// <summary>
        /// 풀이 쓰는 잎 그림입니다.
        ///
        /// <b>왜 메시가 아니라 그림인가</b>
        ///
        /// 예전에는 잎의 윤곽을 정점에 구웠습니다. 텍스처가 없으니 형태를 메시가 들어야 했고,
        /// 그렇게 만들 수 있는 것은 결국 <b>대칭인 사다리꼴</b> 하나뿐이었습니다.
        /// 한 포기가 여러 갈래로 뻗은 모습, 끝이 꺾인 모습은 정점 몇 개로 나오지 않습니다.
        ///
        /// 24픽셀짜리 알파 그림 한 장이 그 일을 대신합니다. 파일 크기가 700바이트라
        /// "에셋 없이 실행"이라는 원칙이 지키려던 것 — 무거운 의존을 만들지 않는 것 —
        /// 은 그대로 지켜집니다.
        ///
        /// <b>그림은 흰 실루엣입니다.</b> 색은 하나도 들어 있지 않습니다.
        /// 고도·얼룩·종·강조는 전부 셰이더가 월드 좌표에서 정합니다.
        /// 그림이 색을 들면 그 색이 지형과 어긋나는 순간 손댈 방법이 없습니다.
        /// </summary>
        /// <returns>기본 잎 그림입니다. 없으면 null입니다.</returns>
        public static Texture2D GetGrassSprite()
        {
            // <b>??= 를 쓰면 안 됩니다.</b>
            // 널 병합은 유니티가 재정의한 == 를 무시하고 실제 참조만 봅니다.
            // 에셋이 해제되면 참조는 남으므로, 죽은 텍스처를 그대로 돌려주게 됩니다.
            if (s_grassSprite == null)
            {
                s_grassSprite = LoadGrassSprite("GrassLeaf");
            }

            return s_grassSprite;
        }

        /// <summary>
        /// 드물게 섞이는 강조풀의 그림입니다. 갈래가 많고 키가 큽니다.
        /// </summary>
        /// <returns>강조풀 그림입니다. 없으면 null입니다.</returns>
        public static Texture2D GetAccentSprite()
        {
            if (s_accentSprite == null)
            {
                s_accentSprite = LoadGrassSprite("AccentLeaf");
            }

            return s_accentSprite;
        }

        /// <summary>
        /// 잎 그림을 불러옵니다.
        ///
        /// 머티리얼을 코드가 만들므로 직렬화된 참조를 둘 곳이 없습니다.
        /// <see cref="Resources"/> 를 쓰는 것은 그래서입니다 — 두 장 합쳐 1.3KB 라
        /// 빌드에 항상 실리는 대가가 문제가 되지 않습니다.
        /// </summary>
        /// <param name="name">Resources/Grass 아래의 파일 이름입니다.</param>
        /// <returns>불러온 그림입니다. 없으면 null입니다.</returns>
        private static Texture2D LoadGrassSprite(string name)
        {
            var sprite = Resources.Load<Texture2D>($"Grass/{name}");

            if (sprite == null && !s_warnedMissingSprite)
            {
                s_warnedMissingSprite = true;

                Debug.LogWarning(
                    $"잎 그림 'Resources/Grass/{name}' 을 찾지 못했습니다. " +
                    "풀이 사각형으로 그려집니다.");
            }

            return sprite;
        }

        /// <summary>
        /// 풀 머티리얼을 만듭니다.
        ///
        /// <b>지형과 같은 숫자를 받습니다</b>
        ///
        /// 고도에 따라 풀이 마르는 정도는 지형이 저지와 고지를 가르는 것과
        /// <b>같은 해수면·고도폭</b>에서 나와야 합니다.
        /// 갈라지면 마른 땅 위에 새파란 풀이 자라, 지형과 풀이 서로 다른 말을 합니다.
        /// </summary>
        /// <param name="seaLevel">해수면의 월드 높이입니다.</param>
        /// <param name="heightRange">해수면 위로 올라가는 높이의 폭입니다.</param>
        /// <returns>풀 머티리얼입니다. 셰이더가 없으면 null입니다.</returns>
        public static Material CreateGrassMaterial(float seaLevel, float heightRange)
        {
            var shader = Shader.Find(GrassShaderName);

            if (shader == null)
            {
                WarnMissingShaderOnce(GrassShaderName, "전장에 풀이 자라지 않습니다.");
                return null;
            }

            var material = new Material(shader)
            {
                name = "Grass_Runtime",
                hideFlags = HideFlags.DontSave,
            };

            material.SetFloat(SeaLevelId, seaLevel);
            material.SetFloat(HeightRangeId, Mathf.Max(0.5f, heightRange));

            // 그림이 없으면 셰이더의 기본값인 흰 텍스처가 그대로 남습니다.
            // 알파가 1이라 잎이 사각형으로 보이지만, 그것 때문에 실행이 막히지는 않습니다.
            var sprite = GetGrassSprite();

            if (sprite != null)
            {
                material.SetTexture(BaseMapId, sprite);
            }

            var accent = GetAccentSprite();

            if (accent != null)
            {
                material.SetTexture(AccentMapId, accent);
            }

            // 인스턴싱으로 그리므로 반드시 켜져 있어야 합니다.
            material.enableInstancing = true;

            return material;
        }

        /// <summary>
        /// 접지 그림자가 공유하는 머티리얼입니다.
        ///
        /// 유닛마다 새로 만들면 수백 개의 머티리얼이 생기고 배칭이 통째로 깨집니다.
        /// 그림자는 전부 같은 모습이므로 하나로 충분합니다.
        /// </summary>
        /// <param name="shader">그림자 셰이더입니다. 처음 호출할 때만 쓰입니다.</param>
        /// <returns>모든 접지 그림자가 공유하는 머티리얼입니다.</returns>
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

        /// <summary>공유 캡슐 메시를 반환합니다.</summary>
        /// <returns>프리미티브에서 한 번 뽑아 캐시해 둔 캡슐 메시입니다.</returns>
        public static Mesh GetCapsuleMesh()
        {
            if (s_capsuleMesh == null)
            {
                s_capsuleMesh = ExtractPrimitiveMesh(PrimitiveType.Capsule);
            }

            return s_capsuleMesh;
        }

        /// <summary>공유 큐브 메시를 반환합니다.</summary>
        /// <returns>프리미티브에서 한 번 뽑아 캐시해 둔 큐브 메시입니다.</returns>
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
