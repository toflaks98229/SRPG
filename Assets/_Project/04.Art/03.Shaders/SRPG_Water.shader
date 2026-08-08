// 전장의 물입니다. 강과 물가를 표현합니다.
//
// <b>왜 단색 평면으로는 부족한가</b>
//
// 예전 물은 불투명한 파란 판이었습니다. 섬 둘레를 두르는 바다였을 때는 그것으로 충분했습니다 —
// 어차피 화면 가장자리였고, 아무도 그 위에서 싸우지 않았습니다.
//
// 야전의 강은 다릅니다. 전장 한가운데를 가르고, 그 위를 부대가 건너며,
// <b>어디가 얕은지가 곧 전술 정보</b>입니다. 단색 판은 그것을 하나도 말해 주지 않습니다.
//
// <b>깊이가 여울을 드러냅니다</b>
//
// 물 아래 지형까지의 거리로 색을 정하면, 얕은 곳은 저절로 밝고 맑아집니다.
// 여울을 따로 표시할 필요가 없습니다 — 얕기 때문에 밝게 보이는 것이고,
// 그래서 플레이어가 "저기로 건널 수 있겠다"를 규칙이 아니라 눈으로 읽습니다.
//
// 물가 거품선도 같은 값에서 나옵니다. 깊이가 거의 0인 띠를 밝게 칠하면
// 물과 땅이 만나는 경계가 살아나고, 강이 지형에 파고든 것처럼 보입니다.
//
// <b>깊이 텍스처가 없으면</b>
//
// 모바일 렌더 파이프라인은 깊이 텍스처를 끄고 있습니다. 그때는 깊이가 먼 값으로 들어와
// 전체가 깊은 물로 칠해집니다. 여울이 드러나지 않을 뿐 물로는 보이므로 그대로 둡니다.
Shader "SRPG/Water"
{
    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.35, 0.68, 0.75, 0.55)
        _DeepColor    ("Deep Color", Color)    = (0.09, 0.22, 0.38, 0.92)

        _DepthFade    ("Depth Fade", Range(0.05, 12)) = 2.2

        _ShoreColor   ("Shore Color", Color)         = (0.82, 0.93, 0.95, 1)
        _ShoreWidth   ("Shore Width", Range(0, 2))   = 0.35
        _ShorePower   ("Shore Sharpness", Range(1, 8)) = 3.0

        _RippleScale    ("Ripple Scale", Range(0.02, 2))    = 0.35
        _RippleSpeed    ("Ripple Speed", Range(0, 3))       = 0.6
        _RippleStrength ("Ripple Strength", Range(0, 0.4))  = 0.10

        _Fresnel      ("Sky Glint", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent"
        }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }

            // 물은 그림자를 드리우지도, 깊이를 쓰지도 않습니다.
            // 깊이를 쓰면 물 아래 지형을 스스로 가려 깊이 계산이 무너집니다.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float  _DepthFade;
                float4 _ShoreColor;
                float  _ShoreWidth;
                float  _ShorePower;
                float  _RippleScale;
                float  _RippleSpeed;
                float  _RippleStrength;
                float  _Fresnel;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float4 screenPos   : TEXCOORD1;
            };

            Varyings WaterVertex(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.screenPos  = ComputeScreenPos(output.positionCS);

                return output;
            }

            // 두 방향으로 흐르는 사인파를 겹칩니다.
            // 한 방향만 쓰면 줄무늬가 되고, 노이즈를 쓰면 이 추상화 수준에서 지저분해집니다.
            float Ripple(float2 p, float time)
            {
                float a = sin((p.x + p.y) * 3.1 + time * 1.7);
                float b = sin((p.x - p.y * 1.6) * 2.3 - time * 1.1);

                return (a + b) * 0.25 + 0.5;
            }

            half4 WaterFragment(Varyings input) : SV_Target
            {
                // 물 표면과 그 아래 지형 사이의 거리입니다.
                float2 uv = input.screenPos.xy / max(input.screenPos.w, 1e-5);

                float sceneDepth   = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float surfaceDepth = input.positionCS.w;

                // 물이 지형보다 앞에 있을 때만 의미가 있습니다.
                float waterDepth = max(0.0, sceneDepth - surfaceDepth);

                // 얕을수록 0, 깊을수록 1.
                float deep = saturate(waterDepth / max(_DepthFade, 1e-3));

                float4 water = lerp(_ShallowColor, _DeepColor, deep);

                // 물가 거품선 — 깊이가 거의 0인 띠입니다.
                // 강이 지형에 파고든 것처럼 보이게 하는 것이 이 한 줄입니다.
                float shore = 1.0 - saturate(waterDepth / max(_ShoreWidth, 1e-3));
                shore = pow(shore, _ShorePower);

                float2 flow = input.positionWS.xz * _RippleScale;
                float ripple = Ripple(flow, _TimeParameters.x * _RippleSpeed);

                float3 color = water.rgb + (ripple - 0.5) * _RippleStrength;

                // 비스듬히 볼수록 하늘빛을 더 받습니다. 평평한 판이 판으로 보이지 않게 합니다.
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float glint = pow(1.0 - saturate(viewDir.y), 4.0) * _Fresnel;

                color = lerp(color, _ShoreColor.rgb, shore);
                color += glint;

                // 물가는 불투명에 가깝게 두어 경계가 흐려지지 않게 합니다.
                float alpha = lerp(water.a, 1.0, shore);

                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
