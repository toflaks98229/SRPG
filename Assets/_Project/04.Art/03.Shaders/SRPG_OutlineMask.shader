// 외곽선을 그릴 대상만 흰색으로 칠하는 마스크 셰이더입니다.
//
// <b>왜 별도의 그리기 패스가 필요한가</b>
//
// 외곽선은 전체 화면 패스라 "이 픽셀이 어느 오브젝트인지"를 알지 못합니다.
// 깊이와 노멀만으로는 지형과 병사를 구분할 수 없습니다.
//
// 고를 대상만 한 번 더 그려 두면 그 자리가 곧 대답이 됩니다.
// 스텐실을 쓰는 방법도 있지만, 그러려면 <b>대상의 머티리얼을 전부 고쳐야</b> 합니다.
// 레이어로 고르면 오브젝트에 손대지 않고 렌더러 피처 안에서 끝납니다.
//
// 카메라의 깊이로 시험하므로 가려진 것은 칠해지지 않습니다.
Shader "SRPG/OutlineMask"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Opaque"
        }

        Pass
        {
            Name "OutlineMask"
            Tags { "LightMode" = "SRPGOutlineMask" }

            ZWrite Off
            ZTest LEqual
            Cull Back
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            // 풀은 인스턴싱으로 그려집니다. 이것이 없으면 마스크에서 한 포기만 나옵니다.
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                return half4(1.0, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
