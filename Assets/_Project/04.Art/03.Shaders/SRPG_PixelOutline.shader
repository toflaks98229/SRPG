// 저해상도 픽셀 그리드 위에서 외곽선을 그리는 전체 화면 셰이더입니다.
//
// <b>왜 저해상도에서 찾는가</b>
//
// 전체 해상도에서 외곽선을 찾아 축소하면 선이 픽셀 사이에 걸쳐 흐려집니다.
// 내부 해상도에서 찾으면 이웃 표본이 곧 픽셀 하나이므로 <b>선이 정확히 한 픽셀</b>이 됩니다.
// 이것이 3D 를 픽셀아트로 읽히게 하는 결정적인 조건입니다.
//
// <b>왜 색이 아니라 깊이와 노멀인가</b>
//
// 색으로 경계를 찾으면 무늬와 명암도 경계로 잡힙니다. 풀밭처럼 색이 잘게 나뉜 곳에서는
// 화면이 통째로 선으로 덮입니다. 깊이와 노멀은 <b>형태</b>만 말하므로 무늬에 반응하지 않습니다.
//
// 두 가지를 따로 찾습니다.
//   · 실루엣 — 깊이가 끊기는 곳. 물체의 바깥 윤곽입니다
//   · 크리스 — 깊이는 이어지는데 면이 꺾이는 곳. 물체 안쪽의 주름입니다
Shader "SRPG/PixelOutline"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // <b>경로에 주의하십시오.</b> 이 파일은 ShaderLibrary 가 아니라 Runtime/Utilities 에 있습니다.
        // Attributes·Varyings·Vert 와 _BlitTexture, 그리고 전역 샘플러가 여기서 옵니다.
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
        #include "SRPG_Depth.hlsl"

        // 외곽선을 그릴 대상만 1인 마스크입니다. 전체 해상도로 그려집니다.
        TEXTURE2D(_OutlineMask);
        SAMPLER(sampler_OutlineMask);

        float4 _OutlineParams;      // x=깊이 문턱, y=노멀 문턱, z=크리스 대비 문턱, w=마스크 사용 여부
        float4 _OutlineTexel;       // xy=내부 해상도 한 픽셀의 UV 크기, zw=내부 해상도 픽셀 수
        float4 _SilhouetteColor;
        float4 _CreaseColor;
        float  _CreaseStrength;
        float  _DebugMode;          // 0=끔, 1=실루엣 파랑 / 크리스 빨강

        // 깊이 차를 나눌 기준입니다. 0이면 자기 깊이로 나눕니다(원근).
        //
        // <b>직교에서는 자기 깊이로 나누면 안 됩니다.</b> 시점 거리에 카메라를 물려 둔 몫이
        // 통째로 들어 있어서, 카메라를 뒤로 물리는 것만으로 모든 깊이 차가 작아집니다.
        // 실제로 그랬습니다 — 직교로 옮기며 물린 거리가 34에서 60이 되자
        // 문턱을 넘던 윤곽이 넘지 못해 <b>외곽선이 군데군데 사라졌습니다</b>.
        // 직교에서 화면의 축척을 정하는 것은 거리가 아니라 담기는 월드 높이이므로 그것을 받습니다.
        float  _OutlineDepthScale;

        // 눈에서 잰 거리입니다.
        //
        // <b>투영을 가립니다.</b> 원근에서는 원시 깊이가 비선형이라 되돌려야 하고,
        // 직교에서는 이미 선형이라 근·원 사이를 보간해야 합니다.
        // 한쪽 공식을 양쪽에 쓰면 값이 통째로 어긋나면서 오류는 나지 않습니다.
        float EyeDepthAt(float2 uv)
        {
            return SrpgLinearEyeDepth(SampleSceneDepth(uv));
        }

        float3 NormalAt(float2 uv)
        {
            return SampleSceneNormals(uv);
        }

        // 이 자리에 외곽선을 그려도 되는지입니다.
        //
        // 마스크를 쓰지 않으면 전부 1입니다. 씬의 모든 메시에 선이 붙으면
        // 화면이 지저분해지므로, 골라 낼 수 있어야 합니다.
        float MaskAt(float2 uv)
        {
            if (_OutlineParams.w < 0.5)
            {
                return 1.0;
            }

            return SAMPLE_TEXTURE2D(_OutlineMask, sampler_OutlineMask, uv).r;
        }
        ENDHLSL

        // ================================================================================================
        // Pass 0 — 축소하며 외곽선을 찾아 얹습니다.
        //
        // 원본 색을 내부 해상도로 표본하고, <b>같은 자리에서</b> 깊이와 노멀도 표본합니다.
        // 이웃을 고르는 간격이 내부 해상도의 한 픽셀이므로 선의 두께가 한 픽셀로 고정됩니다.
        // ================================================================================================
        Pass
        {
            Name "PixelOutline"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragOutline
            #pragma target 3.0

            // 상하좌우 넷만 봅니다.
            //
            // 대각까지 여덟을 보면 선이 두 픽셀로 번집니다.
            // 십자 모양 표본이 <b>한 픽셀 두께</b>를 보장하는 가장 단순한 조건입니다.
            static const float2 kOffsets[4] =
            {
                float2( 1.0,  0.0),
                float2(-1.0,  0.0),
                float2( 0.0,  1.0),
                float2( 0.0, -1.0),
            };

            half4 FragOutline(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float2 texel = _OutlineTexel.xy;

                half4 source = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0);

                float centerDepth = EyeDepthAt(uv);
                float3 centerNormal = NormalAt(uv);

                // --- 실루엣: 깊이가 끊기는 곳 ---
                //
                // <b>차이를 화면의 축척으로 나눕니다.</b>
                // 절대 차이로 재면 줌에 따라 선이 생겼다 사라졌다 합니다.
                // 나누어 두면 "화면 높이의 3%만큼 끊겼다"가 줌과 무관하게 같은 뜻이 됩니다.
                //
                // 원근에서는 그 축척이 곧 자기 깊이입니다(멀수록 같은 간격이 작게 보임).
                // 직교에서는 깊이가 축척과 무관하므로 담기는 월드 높이를 CPU 가 넘겨 줍니다.
                float normalizer = _OutlineDepthScale > 0.0
                    ? _OutlineDepthScale
                    : max(centerDepth, 1e-4);

                float depthEdge = 0.0;

                // --- 크리스: 면이 꺾이는 곳 ---
                float normalEdge = 0.0;

                float depthDelta[4];

                UNITY_UNROLL
                for (int i = 0; i < 4; i++)
                {
                    float2 sampleUv = uv + kOffsets[i] * texel;

                    float neighbourDepth = EyeDepthAt(sampleUv);

                    // 이웃이 <b>더 먼</b> 경우만 셉니다.
                    // 양쪽을 다 세면 물체의 안쪽과 바깥쪽 모두에 선이 그려져 두 픽셀이 됩니다.
                    float relative = (neighbourDepth - centerDepth) / normalizer;

                    depthDelta[i] = relative;
                    depthEdge = max(depthEdge, relative);

                    // 노멀은 내적으로 잽니다. 1이면 같은 방향, 낮을수록 크게 꺾인 것입니다.
                    float alignment = dot(centerNormal, NormalAt(sampleUv));

                    normalEdge = max(normalEdge, 1.0 - alignment);
                }

                float silhouette = step(_OutlineParams.x, depthEdge);

                // <b>휘어진 면과 꺾인 면을 가릅니다.</b>
                //
                // 공처럼 완만히 휘는 면도 이웃과 노멀이 다릅니다. 그것까지 선으로 그리면
                // 둥근 것이 전부 줄무늬가 됩니다.
                // 꺾인 곳은 한 축의 변화가 반대 축보다 <b>뚜렷하게</b> 큽니다 —
                // 휘는 면은 두 축이 고르게 변합니다. 그 대비로 가려냅니다.
                float verticalContrast = abs(depthDelta[2] - depthDelta[3]);
                float horizontalContrast = abs(depthDelta[0] - depthDelta[1]);

                float directional = max(verticalContrast, horizontalContrast);

                float crease = step(_OutlineParams.y, normalEdge) * step(_OutlineParams.z, directional);

                // <b>실루엣이 우선입니다.</b>
                // 물체의 윤곽과 안쪽 주름이 겹치면 윤곽이 보여야 형태가 읽힙니다.
                crease *= 1.0 - silhouette;

                float mask = MaskAt(uv);

                silhouette *= mask;
                crease *= mask;

                if (_DebugMode > 0.5)
                {
                    // 검사용입니다. 실루엣은 파랑, 크리스는 빨강으로 칠합니다.
                    half3 debug = lerp(source.rgb * 0.15, half3(0.0, 0.35, 1.0), silhouette);

                    debug = lerp(debug, half3(1.0, 0.15, 0.1), crease);

                    return half4(debug, source.a);
                }

                half3 color = lerp(source.rgb, _SilhouetteColor.rgb, silhouette * _SilhouetteColor.a);

                color = lerp(color, _CreaseColor.rgb, crease * _CreaseColor.a * _CreaseStrength);

                return half4(color, source.a);
            }
            ENDHLSL
        }

        // ================================================================================================
        // Pass 1 — 내부 해상도를 화면으로 확대합니다.
        //
        // <b>점 표본이어야 합니다.</b> 선형으로 늘이면 애써 한 픽셀로 만든 선이
        // 여러 픽셀에 번져 픽셀아트가 아니라 흐린 저해상도 그림이 됩니다.
        // ================================================================================================
        Pass
        {
            Name "PixelUpscale"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragUpscale
            #pragma target 3.0

            // 화면을 훑는 동안 내부 격자에서 어긋난 만큼입니다.
            // 카메라가 격자 사이에 서 있을 때 그 몫을 여기서 되돌립니다.
            float2 _PixelPanOffset;

            half4 FragUpscale(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord + _PixelPanOffset;

                // 내부 픽셀의 <b>가운데</b>를 찍습니다.
                // 가장자리를 찍으면 반올림이 흔들려 확대 결과가 한 줄씩 어긋납니다.
                float2 grid = _OutlineTexel.zw;

                uv = (floor(uv * grid) + 0.5) / grid;

                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
