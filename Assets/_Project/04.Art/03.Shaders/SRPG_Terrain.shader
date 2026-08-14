// 전장 지형 셰이더입니다. <b>유니티 터레인</b>에 물립니다.
//
// <b>왜 다시 썼는가</b>
//
// 예전 셰이더는 정점 컬러에 구운 접지 음영을 읽었습니다. 타일마다 사각 발판을 굽던 시절,
// 발판 측면 아래쪽을 어둡게 칠해 고도 차를 드러내던 방식입니다.
//
// 지형이 유니티 터레인으로 바뀌면서 그 전제가 사라졌습니다. 터레인 메시에는 정점 컬러가 없고,
// 그래서 접지 음영이 통째로 무의미해졌습니다. 외곽선 패스도 마찬가지입니다 —
// 터레인은 하나의 거대한 연속면이라 뒤집힌 껍질을 씌워도 실루엣이 나오지 않습니다.
//
// <b>무엇이 지형을 읽히게 하는가</b>
//
// 정점 컬러가 없으면 색을 정할 근거는 지형 자신뿐입니다. 셋을 씁니다.
//
//   1. 높이  — 저지대는 풀, 고지대는 마른 땅. 고저가 색으로 읽힙니다
//   2. 경사  — 가파른 면은 암반. <b>길찾기가 막는 곳과 같은 기준</b>이라
//              "올라갈 수 있을 줄 알았는데"가 생기지 않습니다
//   3. 물가  — 해수면 근처의 젖은 띠. 강이 지형에 파고든 자리가 드러납니다
//
// 특히 2번이 중요합니다. 생성기는 경사가 등반 한계를 넘으면 그 칸을 절벽으로 봅니다.
// 같은 각도에서 색이 바뀌면 <b>보이는 것과 갈 수 있는 곳이 일치</b>합니다.
Shader "SRPG/Terrain"
{
    Properties
    {
        _LowColor    ("Lowland Color", Color)  = (0.42, 0.58, 0.33, 1)
        _HighColor   ("Highland Color", Color) = (0.60, 0.62, 0.42, 1)
        _RockColor   ("Rock Color", Color)     = (0.44, 0.42, 0.43, 1)
        _WetColor    ("Waterline Color", Color) = (0.32, 0.40, 0.31, 1)

        _SeaLevel    ("Sea Level", Float)             = 0
        _HeightRange ("Height Range", Range(0.5, 40)) = 6

        _WetBand     ("Waterline Band", Range(0, 4))  = 0.9

        // 생성기의 등반 한계와 같은 값을 넣어야 합니다.
        // 다르면 색이 바뀌는 선과 실제로 막히는 선이 어긋납니다.
        _ClimbLimit  ("Climb Limit (deg)", Range(10, 70)) = 34
        _RockBlend   ("Rock Blend (deg)", Range(1, 20))   = 7

        _AmbientBoost ("Ambient Boost", Range(0, 1)) = 0.35
        _ShadowDepth  ("Shadow Depth", Range(0, 1))  = 0.55
    }

    SubShader
    {
        Tags
        {
            "RenderType"        = "Opaque"
            "RenderPipeline"    = "UniversalPipeline"
            "Queue"             = "Geometry"
            "TerrainCompatible" = "True"
        }

        // ================================================================================================
        // Pass 1 — 본체
        //
        // 방향광 하나를 램버트로 받되 명암을 눌러 씁니다.
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

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 물·풀과 <b>같은</b> 구름 그림자입니다. 갈라지면 지형만 다른 하늘 아래 놓입니다.
            #include "SRPG_Noise.hlsl"

            // 물·풀·유닛과 <b>같은</b> 계단식 명암입니다.
            #include "SRPG_Toon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _LowColor;
                float4 _HighColor;
                float4 _RockColor;
                float4 _WetColor;
                float  _SeaLevel;
                float  _HeightRange;
                float  _WetBand;
                float  _ClimbLimit;
                float  _RockBlend;
                float  _AmbientBoost;
                float  _ShadowDepth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings TerrainVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);

                return output;
            }

            half4 TerrainFragment(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);

                // --- 1. 높이 ---
                // 해수면을 0으로 두고 정규화합니다. 저지는 풀, 고지는 마른 땅입니다.
                float altitude = saturate((input.positionWS.y - _SeaLevel) / max(_HeightRange, 1e-3));
                float3 albedo = lerp(_LowColor.rgb, _HighColor.rgb, smoothstep(0.1, 0.9, altitude));

                // --- 2. 경사 ---
                // 생성기가 등반 한계로 절벽을 가르는 그 각도에서 암반이 드러납니다.
                float slopeDegrees = degrees(acos(saturate(normal.y)));
                float rock = smoothstep(_ClimbLimit - _RockBlend, _ClimbLimit + _RockBlend, slopeDegrees);

                albedo = lerp(albedo, _RockColor.rgb, rock);

                // --- 3. 물가 ---
                // 해수면 바로 위의 젖은 띠입니다. 강이 파고든 자리가 눈에 들어옵니다.
                float wet = 1.0 - saturate((input.positionWS.y - _SeaLevel) / max(_WetBand, 1e-3));
                albedo = lerp(albedo, _WetColor.rgb, saturate(wet) * (1.0 - rock));

                // --- 음영 ---
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // 램버트를 0~1로 접습니다(half lambert).
                // 그대로 쓰면 빛을 등진 면이 새까매져 지형이 안 읽힙니다.
                float lambert = dot(normal, mainLight.direction) * 0.5 + 0.5;

                // 지나가는 구름의 그늘입니다. 네 셰이더가 모두 같은 함수를 부릅니다.
                float clouds = CloudShadow(input.positionWS, mainLight.direction);

                // 명암·그림자·구름을 한 번에 접어 계단으로 끊습니다.
                //
                // <b>지형이 이 계단을 가장 크게 씁니다.</b>
                // 완만한 비탈은 램버트가 아주 천천히 변해 어디가 능선인지 보이지 않습니다.
                // 계단으로 끊으면 그 경계가 등고선처럼 드러나고, 고도가 형태로 읽힙니다.
                float shading = ToonLight(
                    lambert,
                    mainLight.shadowAttenuation,
                    clouds,
                    _AmbientBoost,
                    _ShadowDepth);

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
                float4 _LowColor;
                float4 _HighColor;
                float4 _RockColor;
                float4 _WetColor;
                float  _SeaLevel;
                float  _HeightRange;
                float  _WetBand;
                float  _ClimbLimit;
                float  _RockBlend;
                float  _AmbientBoost;
                float  _ShadowDepth;
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

                // ApplyShadowBias 가 돌려주는 것은 <b>밀어낸 월드 좌표</b>이지 클립 좌표가 아닙니다.
                // 반환값을 positionCS 에 바로 넣으면 float3 를 float4 에 넣는 셈이라 컴파일이 막히고,
                // 이 패스가 죽으면 <b>지형이 그림자를 드리우지 않습니다</b>.
                // 밀어낸 뒤에 반드시 클립 공간으로 옮겨야 합니다.
                output.positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));

                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ================================================================================================
        // 깊이와 노멀을 함께 그리는 패스입니다.
        //
        // <b>없으면 화면 공간 주변 차폐가 지형을 통째로 건너뜁니다.</b>
        //
        // 렌더러의 SSAO 는 노멀 원본을 DepthNormals 로 잡고 있습니다.
        // 이 패스가 없는 물체는 노멀 버퍼에 아무것도 남기지 않으므로,
        // 지형 위에 차폐가 생기지 않고 바위 밑동도 어두워지지 않습니다.
        // 컴파일도 되고 경고도 없이 효과만 사라지는 종류의 누락입니다.
        // ================================================================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _LowColor;
                float4 _HighColor;
                float4 _RockColor;
                float4 _WetColor;
                float  _SeaLevel;
                float  _HeightRange;
                float  _WetBand;
                float  _ClimbLimit;
                float  _RockBlend;
                float  _AmbientBoost;
                float  _ShadowDepth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output;

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                // 전방 렌더링에서는 월드 노멀을 그대로 씁니다.
                // 지연 렌더링으로 옮기면 팔면체 부호화가 필요합니다.
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }

        // 깊이만 그리는 패스입니다.
        // <b>물 셰이더가 이 값으로 수심을 잽니다.</b> 없으면 여울이 드러나지 않습니다.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _LowColor;
                float4 _HighColor;
                float4 _RockColor;
                float4 _WetColor;
                float  _SeaLevel;
                float  _HeightRange;
                float  _WetBand;
                float  _ClimbLimit;
                float  _RockBlend;
                float  _AmbientBoost;
                float  _ShadowDepth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Terrain/Lit"
}
