// 2.5D 유닛 빌보드 셰이더입니다.
//
// <b>왜 정점 셰이더에서 도는가</b>
//
// 유닛마다 Transform 을 매 프레임 카메라 쪽으로 돌리면, 수백 명일 때
// 그만큼의 트랜스폼 갱신과 계층 전파가 발생합니다.
// 정점 셰이더에서 돌리면 CPU 는 아무것도 하지 않고, 배칭도 깨지지 않습니다.
//
// <b>왜 Y축만 도는가 (원통형 빌보드)</b>
//
// 카메라가 47도로 내려다봅니다. 완전한 빌보드(구면)로 만들면 스프라이트가
// 카메라를 정면으로 마주하려고 뒤로 눕습니다. 발밑이 땅에서 떨어지고
// 위에서 내려다보는 느낌이 사라집니다.
// Y축만 돌리면 스프라이트는 늘 곧게 서 있고, 카메라를 따라 옆으로만 돕니다.
//
// <b>피벗은 발밑입니다</b>
//
// 쿼드의 원점이 하단 중앙이어야 지면에 발이 닿습니다.
// 가운데를 원점으로 두면 유닛이 땅에 반쯤 박히거나 떠 보입니다.
Shader "SRPG/Billboard"
{
    Properties
    {
        _BaseMap        ("Sprite", 2D)                    = "white" {}
        _BaseColor      ("Tint", Color)                   = (1, 1, 1, 1)

        _OutlineColor   ("Outline Color", Color)          = (0.08, 0.09, 0.11, 1)
        _OutlineWidth   ("Outline Width", Range(0, 0.3))  = 0.04

        // 지형 셰이더와 <b>같은 값</b>이어야 합니다. 갈라지면 유닛만 다른 조명을 받는 것처럼 보입니다.
        _AmbientBoost   ("Ambient Boost", Range(0, 1))    = 0.35

        // 발치를 어둡게 눌러 지면에 앉힙니다.
        _GroundShade    ("Ground Shade", Range(0, 1))     = 0.35

        _Cutoff         ("Alpha Cutoff", Range(0, 1))     = 0.5

        // 스프라이트 시트에서 이 유닛이 쓸 칸입니다.
        // 방향 번호를 BillboardDirection 이 계산해 넣어 줍니다.
        _FrameCount     ("Frame Count", Float)            = 1
        _FrameIndex     ("Frame Index", Float)            = 0
        _FlipX          ("Flip X", Float)                 = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "AlphaTest"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float4 _OutlineColor;
            float  _OutlineWidth;
            float  _AmbientBoost;
            float  _GroundShade;
            float  _Cutoff;
            float  _FrameCount;
            float  _FrameIndex;
            float  _FlipX;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        // 오브젝트 원점을 기준으로 카메라를 향해 Y축으로만 돌립니다.
        //
        // 정점을 월드로 옮긴 뒤 원점 기준 오프셋만 회전시킵니다.
        // 오프셋만 돌리므로 유닛의 실제 위치는 그대로이고, 그림만 카메라를 향합니다.
        float3 BillboardWorldPosition(float3 positionOS, float expand)
        {
            float3 originWS = TransformObjectToWorld(float3(0, 0, 0));

            // 카메라에서 유닛으로 향하는 수평 방향입니다.
            float3 toCamera = _WorldSpaceCameraPos - originWS;
            toCamera.y = 0;

            // 카메라가 유닛 바로 위에 있으면 방향이 사라집니다. 그때는 기본 축을 씁니다.
            float lengthSq = dot(toCamera, toCamera);
            float3 forward = lengthSq > 1e-6 ? toCamera * rsqrt(lengthSq) : float3(0, 0, 1);

            float3 right = normalize(cross(float3(0, 1, 0), forward));

            // 스케일은 오브젝트 공간 크기를 그대로 씁니다.
            float3 scale = float3(
                length(GetObjectToWorldMatrix()._m00_m10_m20),
                length(GetObjectToWorldMatrix()._m01_m11_m21),
                length(GetObjectToWorldMatrix()._m02_m12_m22));

            float2 local = positionOS.xy * scale.xy;

            // 외곽선용으로 바깥으로 조금 부풀립니다.
            local += sign(local) * expand;

            return originWS + right * local.x + float3(0, local.y, 0);
        }

        // 스프라이트 시트에서 이번 방향의 칸을 잘라 냅니다.
        float2 FrameUV(float2 uv)
        {
            float count = max(1.0, _FrameCount);
            float index = clamp(_FrameIndex, 0.0, count - 1.0);

            // 좌우 반전. 대칭인 유닛만 켭니다.
            float u = _FlipX > 0.5 ? (1.0 - uv.x) : uv.x;

            return float2((u + index) / count, uv.y);
        }
        ENDHLSL

        // ================================================================================================
        // Pass 1 — 외곽선
        //
        // 빌보드는 평면이라 노멀 방향으로 부풀리는 방식이 통하지 않습니다(전부 같은 방향을 봅니다).
        // 대신 쿼드를 사방으로 조금 키운 뒤 알파를 외곽선 색으로 칠합니다.
        // ================================================================================================
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPUnlitShaderPass" }

            Cull Off
            ZWrite On
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings OutlineVertex(Attributes input)
            {
                Varyings output;

                float3 positionWS = BillboardWorldPosition(input.positionOS.xyz, _OutlineWidth);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;

                return output;
            }

            half4 OutlineFragment(Varyings input) : SV_Target
            {
                half4 sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, FrameUV(input.uv));
                clip(sampled.a - _Cutoff);

                return half4(_OutlineColor.rgb, 1.0);
            }
            ENDHLSL
        }

        // ================================================================================================
        // Pass 2 — 본체
        // ================================================================================================
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex BillboardVertex
            #pragma fragment BillboardFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings BillboardVertex(Attributes input)
            {
                Varyings output;

                float3 positionWS = BillboardWorldPosition(input.positionOS.xyz, 0.0);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;

                return output;
            }

            half4 BillboardFragment(Varyings input) : SV_Target
            {
                half4 sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, FrameUV(input.uv));
                clip(sampled.a - _Cutoff);

                Light mainLight = GetMainLight();

                // 빌보드에는 쓸 만한 노멀이 없습니다. 그래서 <b>위</b>를 씁니다.
                //
                // 카메라를 향한 노멀을 쓰면 카메라를 돌릴 때마다 유닛의 밝기가 출렁입니다.
                // 위쪽 노멀은 시점과 무관하고, 무엇보다 <b>평지의 지형면과 같은 노멀</b>입니다.
                // 그래서 평지에 선 유닛은 발밑의 땅과 정확히 같은 밝기가 됩니다.
                //
                // 지형과 같은 half lambert 곡선을 그대로 씁니다. 여기서 식이 갈라지면
                // 유닛만 다른 세계의 조명을 받는 것처럼 보입니다.
                float lambert = dot(float3(0.0, 1.0, 0.0), mainLight.direction) * 0.5 + 0.5;
                float shading = lerp(_AmbientBoost, 1.0, lambert);

                // 발치를 어둡게 눌러 지면에 앉힙니다.
                // 복셀 주변 차폐가 만들어 줄 그늘을, 세로 그러데이션 하나로 흉내 냅니다.
                float grounding = lerp(1.0 - _GroundShade, 1.0, saturate(input.uv.y * 3.0));

                half3 albedo = sampled.rgb * _BaseColor.rgb;

                return half4(albedo * shading * grounding * mainLight.color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
