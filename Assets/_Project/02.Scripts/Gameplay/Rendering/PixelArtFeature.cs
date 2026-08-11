using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using SRPG.Data;
using UnityEngine.Rendering.Universal;

namespace SRPG.Rendering
{
    /// <summary>
    /// 화면을 내부 해상도로 줄여 픽셀아트로 그리고, 그 격자 위에서 외곽선을 찾는 렌더러 피처입니다.
    ///
    /// <b>왜 URP 의 렌더 배율을 쓰지 않는가</b>
    ///
    /// 파이프라인 에셋의 Render Scale 은 <b>전체를 한꺼번에</b> 줄입니다.
    /// 그러면 외곽선을 얹을 자리가 없습니다 — 이미 줄어든 화면을 받아 선을 그으면
    /// 그 선은 이미 축소된 그림 위에 얹히는 것이라 두께를 통제할 수 없습니다.
    ///
    /// 여기서는 순서를 잡습니다. 원본을 내부 해상도로 표본하면서 <b>같은 자리의</b>
    /// 깊이와 노멀로 경계를 찾고, 찾은 선을 그 해상도에서 얹은 뒤 확대합니다.
    /// 이웃 표본이 곧 내부 픽셀 하나이므로 선이 정확히 한 픽셀이 됩니다.
    ///
    /// <b>세 패스로 끝냅니다</b>
    ///
    ///   1. 마스크  — 외곽선을 그릴 레이어만 흰색으로 칠합니다 (선택 사항)
    ///   2. 외곽선  — 축소하면서 경계를 찾아 얹습니다
    ///   3. 확대    — 점 표본으로 화면 크기까지 늘립니다
    ///
    /// 축소와 외곽선을 한 패스로 묶은 것은 중간 텍스처를 하나 줄이기 위해서입니다.
    /// 둘을 나누면 내부 해상도 버퍼가 둘이 되는데, 사이에 낄 일이 없습니다.
    /// </summary>
    [DisallowMultipleRendererFeature("SRPG Pixel Art")]
    public sealed class PixelArtFeature : ScriptableRendererFeature
    {
        // ====================================================================================================
        // 1. Settings
        // ====================================================================================================

        /// <summary>외곽선을 어떻게 그릴지 정하는 값들입니다.</summary>
        [System.Serializable]
        public sealed class Settings
        {
            [Header("실루엣 — 깊이가 끊기는 곳")]
            [Range(0.001f, 0.5f)]
            [Tooltip("이웃이 자기보다 이 비율만큼 멀면 윤곽으로 봅니다.\n" +
                     "비율이라 거리와 무관하게 같은 두께가 나옵니다. 낮추면 선이 많아집니다.")]
            public float DepthThreshold = 0.035f;

            [Tooltip("윤곽선의 색입니다. 알파가 섞이는 세기입니다.")]
            public Color SilhouetteColor = new Color(0.06f, 0.05f, 0.09f, 0.85f);

            [Header("크리스 — 면이 꺾이는 곳")]
            [Range(0f, 1f)]
            [Tooltip("이웃과 노멀이 이만큼 어긋나면 주름으로 봅니다. 0은 같은 방향, 1은 직각입니다.")]
            public float NormalThreshold = 0.35f;

            [Range(0f, 0.2f)]
            [Tooltip("휘어진 면을 걸러 내는 문턱입니다.\n" +
                     "공처럼 완만히 휘는 면은 두 축이 고르게 변하고, 꺾인 곳은 한 축만 뚜렷하게 변합니다.\n" +
                     "올리면 둥근 것에 줄무늬가 덜 생깁니다.")]
            public float CreaseContrast = 0.012f;

            [Tooltip("주름선의 색입니다.")]
            public Color CreaseColor = new Color(1f, 0.96f, 0.85f, 0.5f);

            [Range(0f, 1f)]
            [Tooltip("주름선의 세기입니다. 0으로 두면 윤곽만 그립니다.")]
            public float CreaseStrength = 0.6f;

            [Header("대상 고르기")]
            [Tooltip("켜면 아래 레이어에만 외곽선이 붙습니다.\n" +
                     "끄면 화면 전체에 붙어 지형과 풀에도 선이 그어집니다.")]
            public bool UseMask;

            [Tooltip("외곽선을 그릴 레이어입니다.")]
            public LayerMask OutlineLayers = ~0;

            [Header("진단")]
            [Tooltip("켜면 실루엣을 파랑, 크리스를 빨강으로 칠합니다.\n" +
                     "문턱을 맞출 때 어느 쪽이 잡히고 있는지 눈으로 보기 위한 것입니다.")]
            public bool DebugEdges;

            [Header("주입 지점")]
            [Tooltip("후처리 뒤에 넣습니다. 앞에 넣으면 블룸이 픽셀 경계를 다시 번지게 합니다.")]
            public RenderPassEvent Injection = RenderPassEvent.AfterRenderingPostProcessing;
        }

        // ====================================================================================================
        // 2. Inspector
        // ====================================================================================================

        [SerializeField]
        private Settings _settings = new Settings();

        [SerializeField]
        [Tooltip("픽셀 격자 설정입니다. 카메라의 PixelSnapCamera 와 같은 에셋을 꽂아야 합니다.\n" +
                 "비우면 코드 기본값을 쓰는데, 그때는 카메라 쪽도 비어 있어야 합니다.")]
        private PixelGridSettings _grid;

        [SerializeField]
        [Tooltip("SRPG/PixelOutline 셰이더입니다. 비우면 이름으로 찾습니다.")]
        private Shader _outlineShader;

        [SerializeField]
        [Tooltip("SRPG/OutlineMask 셰이더입니다. 마스크를 쓸 때만 필요합니다.")]
        private Shader _maskShader;

        // ====================================================================================================
        // 3. Fields
        // ====================================================================================================

        private Material _outlineMaterial;
        private Material _maskMaterial;
        private PixelArtPass _pass;
        private PixelGridSettings _fallbackGrid;

        // ====================================================================================================
        // 4. Lifecycle
        // ====================================================================================================

        /// <inheritdoc />
        public override void Create()
        {
            _pass = new PixelArtPass(_settings)
            {
                renderPassEvent = _settings.Injection,
            };
        }

        /// <inheritdoc />
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!EnsureMaterials())
            {
                return;
            }

            // 깊이와 노멀을 요청합니다. 이것이 없으면 URP 가 노멀 프리패스를 돌지 않아
            // _CameraNormalsTexture 가 비고, 크리스가 전부 사라집니다.
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            _pass.Setup(_outlineMaterial, _maskMaterial, ResolveGrid());

            renderer.EnqueuePass(_pass);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_outlineMaterial);
            CoreUtils.Destroy(_maskMaterial);
            CoreUtils.Destroy(_fallbackGrid);

            _outlineMaterial = null;
            _maskMaterial = null;
            _fallbackGrid = null;
        }

        /// <summary>
        /// 쓸 격자 설정을 정합니다. 연결되지 않았으면 코드 기본값을 만들어 들고 있습니다.
        /// </summary>
        /// <returns>격자 설정입니다. 절대 null 이 아닙니다.</returns>
        private PixelGridSettings ResolveGrid()
        {
            if (_grid != null)
            {
                return _grid;
            }

            if (_fallbackGrid == null)
            {
                _fallbackGrid = PixelGridSettings.CreateDefault();
                _fallbackGrid.hideFlags = HideFlags.HideAndDontSave;
            }

            return _fallbackGrid;
        }

        /// <summary>
        /// 머티리얼을 준비합니다. 셰이더를 찾지 못하면 이 피처는 조용히 물러납니다.
        /// </summary>
        /// <returns>그릴 준비가 되었으면 true입니다.</returns>
        private bool EnsureMaterials()
        {
            if (_outlineMaterial == null)
            {
                var shader = _outlineShader != null ? _outlineShader : Shader.Find("SRPG/PixelOutline");

                if (shader == null)
                {
                    return false;
                }

                _outlineMaterial = CoreUtils.CreateEngineMaterial(shader);
            }

            if (_settings.UseMask && _maskMaterial == null)
            {
                var shader = _maskShader != null ? _maskShader : Shader.Find("SRPG/OutlineMask");

                if (shader != null)
                {
                    _maskMaterial = CoreUtils.CreateEngineMaterial(shader);
                }
            }

            return true;
        }

        // ====================================================================================================
        // 5. Pass
        // ====================================================================================================

        /// <summary>
        /// 축소·외곽선·확대를 한 묶음으로 기록하는 패스입니다.
        /// </summary>
        private sealed class PixelArtPass : ScriptableRenderPass
        {
            private static readonly int OutlineParamsId = Shader.PropertyToID("_OutlineParams");
            private static readonly int OutlineTexelId = Shader.PropertyToID("_OutlineTexel");
            private static readonly int SilhouetteColorId = Shader.PropertyToID("_SilhouetteColor");
            private static readonly int CreaseColorId = Shader.PropertyToID("_CreaseColor");
            private static readonly int CreaseStrengthId = Shader.PropertyToID("_CreaseStrength");
            private static readonly int DebugModeId = Shader.PropertyToID("_DebugMode");

            /// <summary>깊이 차를 나눌 기준입니다. 0이면 셰이더가 자기 깊이로 나눕니다.</summary>
            private static readonly int OutlineDepthScaleId = Shader.PropertyToID("_OutlineDepthScale");
            private static readonly int OutlineMaskId = Shader.PropertyToID("_OutlineMask");

            /// <summary>마스크를 그릴 때 쓸 패스 태그입니다. 셰이더의 LightMode 와 같아야 합니다.</summary>
            private static readonly List<ShaderTagId> MaskTags = new List<ShaderTagId>
            {
                new ShaderTagId("SRPGOutlineMask"),
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("SRPDefaultUnlit"),
            };

            private readonly Settings _settings;

            private Material _outlineMaterial;
            private Material _maskMaterial;
            private PixelGridSettings _grid;

            public PixelArtPass(Settings settings)
            {
                _settings = settings;
                profilingSampler = new ProfilingSampler("SRPG Pixel Art");

                // <b>이것이 없으면 아무것도 그려지지 않습니다.</b>
                //
                // 후처리 뒤에 넣으면 그 시점의 컬러 타깃은 이미 백버퍼입니다.
                // 백버퍼는 읽으면서 동시에 쓸 수 없어, 화면을 받아 가공하는 패스는 성립하지 않습니다.
                // 이 값을 켜면 URP 가 중간 텍스처를 하나 두고 프레임을 거기로 그립니다.
                requiresIntermediateTexture = true;
            }

            /// <summary>같은 경고를 프레임마다 쏟지 않기 위한 표식입니다.</summary>
            private bool _warned;

            /// <summary>한 번만 경고합니다. 조용히 물러나면 원인을 찾을 수 없습니다.</summary>
            /// <param name="message">남길 말입니다.</param>
            private void WarnOnce(string message)
            {
                if (_warned)
                {
                    return;
                }

                _warned = true;
                Debug.LogWarning($"[PixelArtFeature] {message}");
            }

            /// <summary>이번 프레임에 쓸 머티리얼을 받습니다.</summary>
            /// <param name="outlineMaterial">외곽선·확대 머티리얼입니다.</param>
            /// <param name="maskMaterial">마스크 머티리얼입니다. 마스크를 끄면 null 입니다.</param>
            /// <param name="grid">픽셀 격자 설정입니다.</param>
            public void Setup(Material outlineMaterial, Material maskMaterial, PixelGridSettings grid)
            {
                _outlineMaterial = outlineMaterial;
                _maskMaterial = maskMaterial;
                _grid = grid;
            }

            /// <summary>마스크 패스가 주고받는 것입니다.</summary>
            private sealed class MaskPassData
            {
                public RendererListHandle Renderers;
            }

            /// <summary>전체 화면 패스가 주고받는 것입니다.</summary>
            private sealed class BlitPassData
            {
                public TextureHandle Source;
                public TextureHandle Mask;
                public Material Material;
                public int ShaderPass;
                public bool UseMask;
            }

            /// <summary>
            /// 전체 화면 패스를 하나 기록합니다.
            ///
            /// <b>왜 AddBlitPass 를 쓰지 않는가</b>
            ///
            /// 그쪽은 원본 하나만 묶어 줍니다. 마스크처럼 <b>추가로 읽을 텍스처</b>가 있으면
            /// 렌더 그래프가 그 의존을 알지 못해, 아직 그려지지 않은 것을 읽거나
            /// 자원을 미리 버립니다. 직접 기록하면서 읽을 것을 전부 밝힙니다.
            /// </summary>
            /// <param name="renderGraph">기록할 그래프입니다.</param>
            /// <param name="passName">프로파일러에 남을 이름입니다.</param>
            /// <param name="source">읽을 텍스처입니다.</param>
            /// <param name="destination">그릴 대상입니다.</param>
            /// <param name="shaderPass">셰이더의 몇 번째 패스인지입니다.</param>
            /// <param name="useMask">마스크를 함께 묶을지 여부입니다.</param>
            /// <param name="mask">마스크 텍스처입니다.</param>
            private void RecordBlit(
                RenderGraph renderGraph,
                string passName,
                TextureHandle source,
                TextureHandle destination,
                int shaderPass,
                bool useMask,
                TextureHandle mask)
            {
                using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>(passName, out var passData))
                {
                    passData.Source = source;
                    passData.Mask = mask;
                    passData.Material = _outlineMaterial;
                    passData.ShaderPass = shaderPass;
                    passData.UseMask = useMask;

                    builder.UseTexture(source, AccessFlags.Read);

                    if (useMask)
                    {
                        builder.UseTexture(mask, AccessFlags.Read);

                        // 마스크는 전역 이름으로 묶습니다.
                        // 래스터 패스는 기본적으로 전역 상태를 못 건드리므로 먼저 허락을 받아야 합니다.
                        builder.AllowGlobalStateModification(true);
                    }

                    builder.SetRenderAttachment(destination, 0);

                    builder.SetRenderFunc(
                        (BlitPassData data, RasterGraphContext context) =>
                        {
                            if (data.UseMask)
                            {
                                context.cmd.SetGlobalTexture(OutlineMaskId, data.Mask);
                            }

                            Blitter.BlitTexture(
                                context.cmd,
                                (RTHandle)data.Source,
                                new Vector4(1f, 1f, 0f, 0f),
                                data.Material,
                                data.ShaderPass);
                        });
                }
            }

            /// <inheritdoc />
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();

                if (_outlineMaterial == null)
                {
                    WarnOnce("외곽선 머티리얼이 없습니다. SRPG/PixelOutline 셰이더를 찾지 못했습니다.");
                    return;
                }

                if (resources.isActiveTargetBackBuffer)
                {
                    // requiresIntermediateTexture 를 켜 두었으므로 여기 오면 안 됩니다.
                    // 오면 파이프라인 쪽에서 중간 텍스처를 막고 있다는 뜻입니다.
                    WarnOnce("컬러 타깃이 백버퍼라 화면을 읽을 수 없습니다. 중간 텍스처가 꺼져 있습니다.");
                    return;
                }

                var camera = cameraData.camera;

                // 화면 미리보기·반사 카메라까지 픽셀화하면 편집이 어려워집니다.
                if (camera.cameraType != CameraType.Game && camera.cameraType != CameraType.SceneView)
                {
                    return;
                }

                int internalHeight = ResolveInternalHeight(camera, cameraData.scaledHeight);
                int internalWidth = Mathf.Max(16, Mathf.RoundToInt(internalHeight * camera.aspect));

                var source = resources.activeColorTexture;

                // --- 1. 마스크 ---
                var mask = TextureHandle.nullHandle;

                bool useMask = _settings.UseMask && _maskMaterial != null;

                if (useMask)
                {
                    mask = RecordMask(renderGraph, frameData, resources, cameraData);
                }

                // --- 2. 축소하며 외곽선 ---
                var internalDesc = renderGraph.GetTextureDesc(source);

                internalDesc.name = "_SRPGPixelInternal";
                internalDesc.width = internalWidth;
                internalDesc.height = internalHeight;
                internalDesc.clearBuffer = false;
                internalDesc.filterMode = FilterMode.Point;
                internalDesc.wrapMode = TextureWrapMode.Clamp;
                internalDesc.msaaSamples = MSAASamples.None;

                var internalTexture = renderGraph.CreateTexture(internalDesc);

                ApplyMaterialValues(internalWidth, internalHeight, useMask, camera);

                RecordBlit(renderGraph, "SRPG Pixel Outline", source, internalTexture, 0, useMask, mask);

                // --- 3. 확대 ---
                RecordBlit(renderGraph, "SRPG Pixel Upscale", internalTexture, source, 1, false, TextureHandle.nullHandle);
            }

            /// <summary>
            /// 외곽선을 그릴 레이어만 전체 해상도 마스크에 칠합니다.
            ///
            /// <b>전체 해상도로 그립니다.</b> 카메라의 깊이로 시험해야 가려진 것이 빠지는데,
            /// 깊이 버퍼는 전체 해상도라 크기를 맞춰야 하기 때문입니다.
            /// 읽을 때는 내부 해상도 UV 로 표본하므로 결과는 같습니다.
            /// </summary>
            /// <returns>칠해진 마스크 텍스처입니다.</returns>
            private TextureHandle RecordMask(
                RenderGraph renderGraph,
                ContextContainer frameData,
                UniversalResourceData resources,
                UniversalCameraData cameraData)
            {
                var renderingData = frameData.Get<UniversalRenderingData>();
                var lightData = frameData.Get<UniversalLightData>();

                var maskDesc = renderGraph.GetTextureDesc(resources.activeColorTexture);

                maskDesc.name = "_SRPGOutlineMask";
                maskDesc.format = GraphicsFormat.R8_UNorm;
                maskDesc.clearBuffer = true;
                maskDesc.clearColor = Color.clear;
                maskDesc.msaaSamples = MSAASamples.None;

                var mask = renderGraph.CreateTexture(maskDesc);

                using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>(
                           "SRPG Outline Mask", out var passData))
                {
                    var drawSettings = RenderingUtils.CreateDrawingSettings(
                        MaskTags, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);

                    drawSettings.overrideMaterial = _maskMaterial;
                    drawSettings.overrideMaterialPassIndex = 0;

                    var filterSettings = new FilteringSettings(
                        RenderQueueRange.opaque, _settings.OutlineLayers);

                    passData.Renderers = renderGraph.CreateRendererList(
                        new RendererListParams(renderingData.cullResults, drawSettings, filterSettings));

                    builder.UseRendererList(passData.Renderers);
                    builder.SetRenderAttachment(mask, 0);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);

                    builder.SetRenderFunc(
                        (MaskPassData data, RasterGraphContext context) =>
                            context.cmd.DrawRendererList(data.Renderers));
                }

                return mask;
            }

            /// <summary>
            /// 이번 프레임에 쓸 내부 세로 해상도를 정합니다.
            ///
            /// <b>줌을 켜면 계단으로 변합니다.</b>
            /// 화면 높이를 정수로 나눌 수 있는 값만 쓰므로 540 · 360 · 270 … 처럼 뜁니다.
            /// 부드럽게 변하지 않는 것이 목적입니다 — 나누어떨어지지 않으면 내부 픽셀이
            /// 어떤 것은 세 칸 어떤 것은 네 칸을 덮어, 그 들쭉날쭉함이 줌할 때마다 흘러갑니다.
            ///
            /// <b>렌더 타깃을 매 프레임 새로 잡지 않습니다.</b>
            /// 계단이라 실제로 크기가 바뀌는 것은 줌이 단을 넘을 때뿐이고,
            /// 렌더 그래프가 크기별로 텍스처를 재사용하므로 몇 개만 오갑니다.
            /// </summary>
            /// <param name="camera">그리는 카메라입니다.</param>
            /// <param name="screenHeight">이번 프레임의 화면 세로 픽셀 수입니다.</param>
            /// <returns>내부 세로 픽셀 수입니다.</returns>
            private int ResolveInternalHeight(Camera camera, int screenHeight)
            {
                return _grid.ResolveHeight(screenHeight, PixelGrid.ResolveViewExtent(camera));
            }

            /// <summary>
            /// 이번 프레임의 값을 머티리얼에 넣습니다.
            /// </summary>
            /// <param name="width">내부 해상도의 가로 픽셀 수입니다.</param>
            /// <param name="height">내부 해상도의 세로 픽셀 수입니다.</param>
            /// <param name="useMask">마스크를 쓰는지 여부입니다.</param>
            /// <param name="camera">그리는 카메라입니다. 깊이 차를 나눌 기준을 여기서 얻습니다.</param>
            private void ApplyMaterialValues(int width, int height, bool useMask, Camera camera)
            {
                _outlineMaterial.SetVector(
                    OutlineParamsId,
                    new Vector4(
                        _settings.DepthThreshold,
                        _settings.NormalThreshold,
                        _settings.CreaseContrast,
                        useMask ? 1f : 0f));

                // xy 는 내부 픽셀 하나의 UV 크기, zw 는 픽셀 수입니다.
                // 이웃을 고르는 간격이 이것이라 선의 두께가 여기서 정해집니다.
                _outlineMaterial.SetVector(
                    OutlineTexelId,
                    new Vector4(1f / width, 1f / height, width, height));

                _outlineMaterial.SetColor(SilhouetteColorId, _settings.SilhouetteColor);
                _outlineMaterial.SetColor(CreaseColorId, _settings.CreaseColor);
                _outlineMaterial.SetFloat(CreaseStrengthId, _settings.CreaseStrength);
                _outlineMaterial.SetFloat(DebugModeId, _settings.DebugEdges ? 1f : 0f);

                // <b>깊이 차를 무엇으로 나눌지</b>를 정해 넘깁니다.
                //
                // 직교에서는 시점 거리에 "카메라를 얼마나 물려 두었는가"가 통째로 들어 있어,
                // 그것으로 나누면 리그의 고정 거리를 바꾸는 것만으로 외곽선 굵기가 달라집니다.
                // 화면의 축척을 정하는 것은 담기는 월드 높이이므로 그것을 넘깁니다.
                //
                // 원근에서는 0을 넘겨 셰이더가 자기 깊이로 나누게 둡니다 —
                // 그쪽은 멀수록 같은 간격이 작게 보이는 것이 맞습니다.
                _outlineMaterial.SetFloat(
                    OutlineDepthScaleId,
                    camera.orthographic ? camera.orthographicSize * 2f : 0f);

                // <b>_PixelPanOffset 은 여기서 건드리지 않습니다.</b>
                //
                // 그 값을 아는 것은 카메라입니다(PixelSnapCamera). 격자에 붙이느라 버린 나머지가
                // 곧 확대할 때 되돌려야 할 양이고, 카메라가 그것을 전역으로 넘깁니다.
                //
                // 예전에는 여기서 0을 넣었습니다. 머티리얼에 직접 넣은 값은 전역보다 <b>세므로</b>,
                // 카메라가 아무리 제대로 넘겨도 이 한 줄이 매 프레임 덮어썼습니다.
                // 셰이더에 배선이 다 있는데도 보정이 통째로 죽어 있던 것이 그 때문입니다.
            }
        }
    }
}
