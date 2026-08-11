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

        // 지형·풀·물과 <b>같은</b> 구름 그림자입니다. 유닛만 볕을 받으면 오려 붙인 것처럼 보입니다.
        #include "SRPG_Noise.hlsl"

        // 지형·물·풀과 <b>같은</b> 계단식 명암입니다.
        #include "SRPG_Toon.hlsl"

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

        // 오브젝트 원점의 월드 위치입니다. 회전의 중심이자 유닛이 실제로 서 있는 자리입니다.
        float3 BillboardOriginWS()
        {
            return TransformObjectToWorld(float3(0, 0, 0));
        }

        // 향할 방향을 수평으로 눕혀 정규화합니다.
        //
        // 상대가 유닛 바로 위에 있으면 수평 성분이 사라집니다. 그때는 기본 축을 씁니다.
        float3 BillboardForward(float3 facingWS)
        {
            float3 toward = float3(facingWS.x, 0.0, facingWS.z);

            float lengthSq = dot(toward, toward);
            return lengthSq > 1e-6 ? toward * rsqrt(lengthSq) : float3(0, 0, 1);
        }

        // 오브젝트 원점을 기준으로 <b>주어진 방향</b>을 향해 Y축으로만 돌립니다.
        //
        // 정점을 월드로 옮긴 뒤 원점 기준 오프셋만 회전시킵니다.
        // 오프셋만 돌리므로 유닛의 실제 위치는 그대로이고, 그림만 상대를 향합니다.
        //
        // <b>향할 방향을 밖에서 받는 이유는 그림자입니다</b>
        //
        // 본체와 외곽선은 카메라를 향하지만, 그림자 패스는 <b>빛</b>을 향해야 합니다.
        // 카메라 쪽으로 돌린 채 빛이 옆에서 들어오면 빛은 이 평면을 모서리로 봅니다 —
        // 그림자가 실 한 가닥으로 무너집니다.
        // 빛을 향해 돌리면 빛은 늘 스프라이트를 정면으로 보고, 그림자에 온전한 실루엣이 남습니다.
        float3 BillboardWorldPosition(float3 positionOS, float3 facingWS, float expand)
        {
            float3 originWS = BillboardOriginWS();

            float3 forward = BillboardForward(facingWS);
            float3 right   = normalize(cross(float3(0, 1, 0), forward));

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
        //
        // <b>반드시 본체보다 뒤에 있어야 합니다</b>
        //
        // 키운 쿼드는 본체와 <b>같은 평면</b>에 있습니다. 정점 위치만 다를 뿐 면은 하나이므로,
        // 두 패스가 같은 픽셀에 대해 마지막 비트만 다른 깊이를 씁니다.
        // 그대로 두면 픽셀마다 어느 쪽이 이길지가 갈려 유닛이 <b>검은 얼룩으로 뒤덮이고</b>,
        // 그 무늬가 프레임마다 뒤집혀 떨립니다. 실제로 그렇게 됐습니다 —
        // 화면 픽셀의 37%가 정지 화면에서도 프레임마다 달랐습니다.
        //
        // 깊이를 한 단위 뒤로 밀면 승부가 확정됩니다.
        // 본체가 자기 자리를 늘 차지하고, 외곽선은 본체 바깥의 테두리에만 남습니다.
        // ================================================================================================
        Pass
        {
            Name "Outline"

            // URP 가 인식하는 이름은 <b>SRPDefaultUnlit</b> 하나뿐입니다.
            // 다른 이름을 쓰면 컴파일도 되고 경고도 없이 필터에서 조용히 탈락합니다 —
            // DrawObjectsPass 가 찾는 태그는 SRPDefaultUnlit / UniversalForward / UniversalForwardOnly 셋입니다.
            // 이 셋 중 첫 번째라 본체(UniversalForward)보다 먼저 그려지는 순서까지 의도대로 맞습니다.
            Tags { "LightMode" = "SRPDefaultUnlit" }

            // 양수는 카메라에서 <b>멀어지는</b> 쪽입니다. 접지 그림자가 음수로 당겨 쓰는 것과 반대입니다.
            Offset 1, 1

            Cull Off
            ZWrite On

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

                float3 positionWS = BillboardWorldPosition(
                    input.positionOS.xyz,
                    _WorldSpaceCameraPos - BillboardOriginWS(),
                    _OutlineWidth);

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

            // AlphaToMask 를 걸지 않습니다.
            //
            // 이 프래그먼트는 알파를 늘 1.0 으로 돌려주므로 커버리지가 항상 가득 찹니다.
            // 게다가 렌더 파이프라인 에셋의 MSAA 가 꺼져 있어 표본이 하나뿐입니다.
            // 잘라내기는 전적으로 clip() 이 하고 있고, AlphaToMask 는 켜 두면
            // 무엇이 실루엣을 만드는지만 흐립니다.
            Cull Off
            ZWrite On

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

                // 구름 그늘은 <b>발밑</b>에서 잽니다. 정점마다 재면 한 유닛 안에서 그늘이 갈라집니다.
                float3 rootWS     : TEXCOORD1;
            };

            Varyings BillboardVertex(Attributes input)
            {
                Varyings output;

                float3 rootWS = BillboardOriginWS();

                float3 positionWS = BillboardWorldPosition(
                    input.positionOS.xyz,
                    _WorldSpaceCameraPos - rootWS,
                    0.0);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.rootWS = rootWS;
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

                // 발밑이 구름 그늘에 들면 유닛도 함께 어두워집니다.
                float clouds = CloudShadow(input.rootWS, mainLight.direction);

                // 지형과 <b>같은</b> 계단으로 끊습니다.
                //
                // 이 셰이더는 그림자를 받지 않습니다(<c>GetMainLight</c> 를 그림자 좌표 없이 부릅니다).
                // 그래서 감쇠는 1, 깊이는 0 을 넘깁니다 — 넘기는 값이 달라도 <b>계단은 같아야</b>
                // 평지에 선 유닛이 발밑의 땅과 같은 단에 놓입니다.
                float shading = ToonLight(lambert, 1.0, clouds, _AmbientBoost, 0.0);

                // 발치를 어둡게 눌러 지면에 앉힙니다.
                // 복셀 주변 차폐가 만들어 줄 그늘을, 세로 그러데이션 하나로 흉내 냅니다.
                float grounding = lerp(1.0 - _GroundShade, 1.0, saturate(input.uv.y * 3.0));

                half3 albedo = sampled.rgb * _BaseColor.rgb;

                return half4(albedo * shading * grounding * mainLight.color, 1.0);
            }
            ENDHLSL
        }

        // ================================================================================================
        // Pass 3 — 그림자 드리우기
        //
        // <b>왜 직접 써야 하는가</b>
        //
        // 이 패스가 없으면 폴백(URP/Unlit)이 대신 채워 줍니다. 그런데 그 패스에는
        // 빌보드 회전도, 프레임 잘라내기도, 알파 컷아웃도 없습니다.
        // 결과는 <b>돌아가지 않은 원본 방향의 직사각형 통짜 그림자</b>입니다 —
        // 유닛은 카메라를 향해 도는데 그림자만 제자리에서 네모로 남습니다.
        //
        // 여기서는 본체와 같은 회전 함수, 같은 프레임 UV, 같은 컷오프를 씁니다.
        // 셋 중 하나라도 갈라지면 그림자와 그림의 실루엣이 어긋납니다.
        //
        // 방향광 하나만 다룹니다. 이 게임의 조명이 그렇고, 지형 셰이더도 같은 전제입니다.
        // ================================================================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            // 빛을 향해 돌린 평면입니다. 어느 면이 빛을 보는지는 그때그때 다릅니다.
            Cull Off

            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // 표면에서 광원으로 향하는 방향입니다. URP 가 그림자 패스마다 넣어 줍니다.
            float3 _LightDirection;

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

                float3 positionWS = BillboardWorldPosition(input.positionOS.xyz, _LightDirection, 0.0);

                // 돌아간 쿼드가 바라보는 방향이 곧 이 면의 노멀입니다.
                // 그림자 바이어스가 표면을 안쪽으로 밀 때 이 값을 씁니다.
                float3 normalWS = BillboardForward(_LightDirection);

                // ApplyShadowBias 는 <b>월드 좌표</b>를 돌려줍니다. 클립 공간이 아닙니다.
                // 그대로 positionCS 에 넣으면 컴파일도 되지 않고, 통과시켜도 그림자가 엉뚱한 데 맺힙니다.
                output.positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));

                output.uv = input.uv;

                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                // 본체와 <b>같은</b> 프레임 UV, 같은 컷오프여야 실루엣이 일치합니다.
                half4 sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, FrameUV(input.uv));
                clip(sampled.a - _Cutoff);

                return 0;
            }
            ENDHLSL
        }

        // ================================================================================================
        // Pass 4 — 깊이와 노멀
        //
        // <b>왜 직접 써야 하는가</b>
        //
        // 폴백(URP/Unlit)이 채워 주는 패스에는 빌보드 회전도 컷아웃도 없습니다.
        // 그 결과 깊이 버퍼와 노멀 버퍼에는 <b>돌아가지 않은 네모난 판</b>이 남습니다.
        // 화면 공간 주변 차폐가 유닛 주위에 네모난 그늘을 그리고,
        // 물 셰이더가 그 깊이로 수심을 재면 유닛 주변의 물이 엉뚱한 색이 됩니다.
        //
        // 노멀은 본체 패스와 같이 <b>위</b>를 씁니다. 평면에는 쓸 만한 노멀이 없고,
        // 위쪽 노멀이라야 유닛과 그 발밑의 땅이 같은 차폐를 받습니다.
        // ================================================================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

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

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output;

                float3 positionWS = BillboardWorldPosition(
                    input.positionOS.xyz,
                    _WorldSpaceCameraPos - BillboardOriginWS(),
                    0.0);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;

                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                half4 sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, FrameUV(input.uv));
                clip(sampled.a - _Cutoff);

                return half4(0.0, 1.0, 0.0, 0.0);
            }
            ENDHLSL
        }

        // ================================================================================================
        // Pass 5 — 깊이만
        //
        // 깊이 선행 패스가 도는 설정에서 이 패스가 없으면, 폴백이 회전하지 않은 판의 깊이를
        // 카메라 깊이 텍스처에 써 넣습니다. <b>물 셰이더가 그 값으로 수심을 잽니다.</b>
        // ================================================================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Off
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment

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

            Varyings DepthVertex(Attributes input)
            {
                Varyings output;

                float3 positionWS = BillboardWorldPosition(
                    input.positionOS.xyz,
                    _WorldSpaceCameraPos - BillboardOriginWS(),
                    0.0);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;

                return output;
            }

            half4 DepthFragment(Varyings input) : SV_Target
            {
                half4 sampled = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, FrameUV(input.uv));
                clip(sampled.a - _Cutoff);

                return 0;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
