// 섬 지형 셰이더입니다.
//
// Konsoll 2018 "Developing The Bad North Look" 의 두 원칙을 구현합니다.
//
//   1. 텍스처가 아닌 경계 (Borders, not textures)
//      면 안쪽을 반복 텍스처로 채우는 대신, 영역이 만나는 경계를 날카롭게 세웁니다.
//      여기서는 정점 컬러에 구운 "경계 음영"이 그 역할을 합니다.
//
//   2. 부드러운 그라디언트 + 날카로운 외곽선
//      면은 완만하게 칠하되 실루엣은 칼같이 남깁니다.
//      외곽선은 폴리곤을 뒤집어 확장해 한 번 더 그리는 고전적인 방식(inverted hull)입니다.
//
// 정점 컬러 채널의 뜻
//   · R : 접지 음영. 1이면 밝고 0이면 어둡습니다. 타일 측면 벽의 아래쪽이 0에 가깝습니다
//   · A : 사용하지 않습니다
Shader "SRPG/Terrain"
{
    Properties
    {
        _BaseColor      ("Base Color", Color)            = (0.44, 0.62, 0.36, 1)
        _ShadeColor     ("Shade Color", Color)           = (0.20, 0.28, 0.18, 1)
        _ShadeStrength  ("Shade Strength", Range(0, 1))  = 0.85

        _OutlineColor   ("Outline Color", Color)         = (0.08, 0.09, 0.11, 1)
        _OutlineWidth   ("Outline Width", Range(0, 0.5)) = 0.06

        _AmbientBoost   ("Ambient Boost", Range(0, 1))   = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        // ================================================================================================
        // Pass 1 — 외곽선
        //
        // 앞면을 버리고 뒷면만 그립니다. 정점을 노멀 방향으로 밀어내면
        // 물체보다 살짝 큰 껍데기가 되고, 그 껍데기의 뒷면만 보이므로 테두리처럼 남습니다.
        // 물체 자체가 앞에서 덮어 가리므로 안쪽은 보이지 않습니다.
        // ================================================================================================
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPUnlitShaderPass" }

            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float  _ShadeStrength;
                float4 _OutlineColor;
                float  _OutlineWidth;
                float  _AmbientBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings OutlineVertex(Attributes input)
            {
                Varyings output;

                // 노멀 방향으로 부풀립니다.
                // 지형은 면마다 노멀이 갈라져 있어(하드 노멀) 모서리에서 껍데기가 벌어지지만,
                // 카메라가 멀어 그 틈이 보이지 않고 오히려 모서리 선이 굵어져 잘 읽힙니다.
                float3 inflated = input.positionOS.xyz + input.normalOS * _OutlineWidth;

                output.positionCS = TransformObjectToHClip(inflated);
                return output;
            }

            half4 OutlineFragment(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        // ================================================================================================
        // Pass 2 — 본체
        //
        // 방향광 하나를 램버트로 받되, 명암을 그대로 쓰지 않고 눌러서 씁니다.
        // 사실적인 음영은 이 추상화 수준과 어울리지 않고, 무엇보다 어두운 면의 판독성을 해칩니다.
        // ================================================================================================
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex TerrainVertex
            #pragma fragment TerrainFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float  _ShadeStrength;
                float4 _OutlineColor;
                float  _OutlineWidth;
                float  _AmbientBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float4 color      : COLOR;
            };

            Varyings TerrainVertex(Attributes input)
            {
                Varyings output;

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.color      = input.color;

                return output;
            }

            half4 TerrainFragment(Varyings input) : SV_Target
            {
                Light mainLight = GetMainLight();

                float3 normal = normalize(input.normalWS);

                // 램버트를 0~1로 접은 뒤(half lambert) 다시 눌러 씁니다.
                // 그대로 쓰면 빛을 등진 면이 새까매져 지형이 안 읽힙니다.
                float lambert = dot(normal, mainLight.direction) * 0.5 + 0.5;
                float shading = lerp(_AmbientBoost, 1.0, lambert);

                // 정점 컬러의 R이 접지 음영입니다.
                // 타일 측면 벽의 아래쪽이 어두워져 고도 차가 눈에 들어옵니다.
                float contact = lerp(1.0 - _ShadeStrength, 1.0, input.color.r);

                float3 albedo = lerp(_ShadeColor.rgb, _BaseColor.rgb, contact);

                return half4(albedo * shading * mainLight.color, 1.0);
            }
            ENDHLSL
        }

        // 그림자를 드리우기 위한 패스입니다. 없으면 지형이 그림자를 만들지 않습니다.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _ShadeColor;
                float  _ShadeStrength;
                float4 _OutlineColor;
                float  _OutlineWidth;
                float  _AmbientBoost;
            CBUFFER_END

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVertex(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(input.normalOS);

                output.positionCS = ApplyShadowBias(positionWS, normalWS, _LightDirection);
                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
