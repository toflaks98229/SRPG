// 유닛 발밑의 접지 그림자입니다.
//
// <b>왜 필요한가</b>
//
// 빌보드는 평면이라 지면과 닿는 면이 없습니다.
// 방향광 그림자만으로는 유닛이 어디에 서 있는지 읽히지 않고, 특히 카메라를 돌릴 때
// 스프라이트가 배경 위에 떠 있는 것처럼 보입니다.
//
// 발밑에 어두운 타원 하나를 깔면 그 문제가 사라집니다.
// 값싸고 확실하며, 나중에 복셀 기반 주변 차폐로 넘어가더라도 이 자리는 그대로 쓸 수 있습니다.
//
// <b>왜 텍스처를 안 쓰는가</b>
//
// 중심에서 가장자리로 부드럽게 사라지는 원은 UV만으로 계산됩니다.
// 텍스처를 두면 에셋이 하나 더 늘고, 프로젝트가 "에셋 없이도 실행 가능"을 유지하지 못합니다.
Shader "SRPG/ContactShadow"
{
    Properties
    {
        _ShadowColor ("Shadow Color", Color)        = (0, 0, 0, 1)
        _Strength    ("Strength", Range(0, 1))      = 0.45
        _Softness    ("Softness", Range(0.01, 1))   = 0.55
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent-10"
        }

        Pass
        {
            Name "ContactShadow"
            Tags { "LightMode" = "UniversalForward" }

            // 지형 위에 얹히되 지형을 밀어내지 않습니다.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            // 지면과 같은 깊이라 z-파이팅이 납니다. 살짝 앞으로 당겨 그립니다.
            Offset -1, -1

            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShadowColor;
                float  _Strength;
                float  _Softness;
            CBUFFER_END

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

            Varyings ShadowVertex(Attributes input)
            {
                Varyings output;

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;

                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                // UV 중심에서의 거리로 원을 그립니다.
                float2 centered = input.uv * 2.0 - 1.0;
                float distance = length(centered);

                // 가장자리에서 부드럽게 사라집니다.
                float alpha = 1.0 - smoothstep(1.0 - _Softness, 1.0, distance);

                return half4(_ShadowColor.rgb, alpha * _Strength);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
