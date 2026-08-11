// 전장의 물입니다. 강과 물가, 그리고 먼바다를 표현합니다.
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
// <b>왜 사인 두 개로는 안 되는가</b>
//
// 처음에는 교차하는 사인파 두 개를 색에 더해 잔물결을 흉내 냈습니다.
// 화면으로 확인해 보니 <b>먼바다가 규칙적인 바둑판</b>으로 깔렸습니다 —
// 고정 주파수의 사인 둘이 교차하면 격자가 생기고, 눈은 그 주기를 즉시 찾아냅니다.
// 물이 아니라 타일링이 깨진 텍스처처럼 보입니다.
//
// 그래서 둘을 바꿨습니다.
//
//   1. <b>형태</b>는 거스트너 파도가 만듭니다. 색에 더하는 것이 아니라 수면을 실제로 밀어 올립니다.
//      마루가 서고 골이 파이므로 빛이 닿는 각도가 달라지고, 평면이 평면으로 보이지 않습니다.
//   2. <b>잔결</b>은 값 노이즈로 만듭니다. 옥타브마다 회전시켜 겹치므로 축에 정렬된 주기가 남지 않습니다.
//
// 노이즈는 해시로 만듭니다. 텍스처를 두면 에셋이 하나 더 늘고,
// 프로젝트가 "에셋 없이도 실행 가능"을 유지하지 못합니다.
//
// <b>파도가 물가에서 잦아드는 이유</b>
//
// 해수면은 이 게임에서 장식이 아니라 <b>게임플레이 상수</b>입니다.
// 생성기가 그 높이를 기준으로 물 타일을 가르고, 지형 셰이더가 같은 값에서 물가 띠를 그립니다.
// 수면이 파고만큼 오르내리면 <b>보이는 물가와 실제로 건널 수 있는 경계가 어긋납니다</b> —
// 지형 셰이더가 경계하는 바로 그 문제입니다.
//
// 그래서 정지 상태의 수심을 미리 재어 텍스처로 넘겨받고, 얕은 곳일수록 파고를 죽입니다.
// 먼바다는 넘실대고 물가는 잔잔합니다. 실제 파도가 그렇기도 합니다.
Shader "SRPG/Water"
{
    Properties
    {
        [Header(Depth)]
        _ShallowColor ("Shallow Color", Color) = (0.35, 0.68, 0.75, 0.55)
        _DeepColor    ("Deep Color", Color)    = (0.09, 0.22, 0.38, 0.92)

        _DepthFade    ("Depth Fade", Range(0.05, 12)) = 2.2

        [Header(Shore)]
        _ShoreColor         ("Shore Color", Color)              = (0.82, 0.93, 0.95, 1)
        _ShoreWidth         ("Shore Width", Range(0, 4))        = 0.9
        _ShorePower         ("Shore Sharpness", Range(1, 8))    = 2.0
        _ShoreNoiseScale    ("Shore Noise Scale", Range(0.05, 3))  = 0.55
        _ShoreNoiseStrength ("Shore Noise Strength", Range(0, 1))  = 0.55
        _ShoreSpeed         ("Shore Speed", Range(0, 2))           = 0.35

        [Header(Waves)]
        // xy = 진행 방향, z = 가파름, w = 파장.
        // 방향을 서로 어긋난 각도로 두어야 마루가 한 줄로 정렬되지 않습니다.
        _WaveA ("Wave A (dirX, dirZ, steepness, length)", Vector) = (1, 0.31, 0.075, 17.3)
        _WaveB ("Wave B (dirX, dirZ, steepness, length)", Vector) = (-0.57, 1, 0.055, 9.1)
        _WaveC ("Wave C (dirX, dirZ, steepness, length)", Vector) = (0.42, -0.86, 0.04, 5.3)

        _WaveSpeed     ("Wave Speed", Range(0, 3))        = 1.0
        _WaveShoreFade ("Wave Shore Fade (m)", Range(0.05, 8)) = 1.6

        // 마루를 흐트러뜨립니다. 이것이 없으면 파도 셋의 간섭무늬가 그대로 격자로 보입니다.
        _WaveWarpScale    ("Wave Warp Scale", Range(0.005, 0.4)) = 0.045
        _WaveWarpStrength ("Wave Warp Strength", Range(0, 12))   = 5.5

        [Header(Detail)]
        _DetailScale        ("Detail Scale", Range(0.02, 2))     = 0.42
        _DetailSpeed        ("Detail Speed", Range(0, 2))        = 0.22
        _DetailStrength     ("Detail Strength", Range(0, 1))     = 0.35
        _DetailFadeDistance ("Detail Fade Distance", Range(5, 300)) = 90

        [Header(Crest)]
        _CrestColor      ("Crest Color", Color)                = (0.88, 0.95, 0.97, 1)
        _CrestThreshold  ("Crest Threshold", Range(0, 1))      = 0.86
        _CrestStrength   ("Crest Strength", Range(0, 1))       = 0.12
        _CrestNoiseScale ("Crest Noise Scale", Range(0.05, 3)) = 1.1

        [Header(Lighting)]
        // 지형 셰이더와 <b>같은 값</b>이어야 합니다. 갈라지면 물만 다른 조명을 받는 것처럼 보입니다.
        _AmbientBoost ("Ambient Boost", Range(0, 1)) = 0.35
        _SpecStrength ("Sun Glitter", Range(0, 3))   = 0.45
        _SpecSharpness("Sun Glitter Sharpness", Range(8, 512)) = 220

        // 비스듬히 볼수록 하늘빛을 받습니다. 흰색을 더하면 먼바다가 통째로 바래므로
        // 하늘색 쪽으로 <b>섞습니다</b>.
        _SkyColor ("Sky Tint", Color)            = (0.42, 0.55, 0.70, 1)
        _Fresnel  ("Sky Glint", Range(0, 1))     = 0.35

        [Header(Depth Map)]
        // 정지 상태의 수심입니다. 미터 단위로, C# 이 하이트맵에서 구워 넣습니다.
        // 연결되지 않으면 흰색(1m)이 들어와 파도가 적당히 잦아든 상태가 됩니다.
        [NoScaleOffset] _WaterDepthMap ("Rest Depth Map", 2D) = "white" {}

        // (원점X, 원점Z, 월드→UV 배율, 半텍셀 보정)
        _WaterDepthArea ("Depth Map Area", Vector) = (0, 0, 0, 0)

        // 지도 바깥은 먼바다입니다. 파도가 온전히 섭니다.
        _OpenSeaDepth ("Open Sea Depth (m)", Float) = 40
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

            // 파도가 정점을 밀어 올리므로 정점 수가 많습니다. 대상 수준을 올려 둡니다.
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // 풀 셰이더와 <b>같은</b> 노이즈입니다. 갈라지면 물결과 풀밭의 결이 서로 다른 세계가 됩니다.
            #include "SRPG_Noise.hlsl"

            // 지형·풀·유닛과 <b>같은</b> 계단식 명암입니다.
            #include "SRPG_Toon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float  _DepthFade;

                float4 _ShoreColor;
                float  _ShoreWidth;
                float  _ShorePower;
                float  _ShoreNoiseScale;
                float  _ShoreNoiseStrength;
                float  _ShoreSpeed;

                float4 _WaveA;
                float4 _WaveB;
                float4 _WaveC;
                float  _WaveSpeed;
                float  _WaveShoreFade;
                float  _WaveWarpScale;
                float  _WaveWarpStrength;

                float  _DetailScale;
                float  _DetailSpeed;
                float  _DetailStrength;
                float  _DetailFadeDistance;

                float4 _CrestColor;
                float  _CrestThreshold;
                float  _CrestStrength;
                float  _CrestNoiseScale;

                float  _AmbientBoost;
                float  _SpecStrength;
                float  _SpecSharpness;
                float4 _SkyColor;
                float  _Fresnel;

                float4 _WaterDepthArea;
                float  _OpenSeaDepth;
            CBUFFER_END

            TEXTURE2D(_WaterDepthMap);
            SAMPLER(sampler_WaterDepthMap);

            // ========================================================================================
            // 정지 수심
            // ========================================================================================

            // 파도가 없을 때 이 자리의 수심입니다. 미터 단위입니다.
            //
            // 화면 깊이가 아니라 <b>지형에서 미리 구운 값</b>을 씁니다.
            // 정점 셰이더에는 화면 깊이가 없고, 무엇보다 파도가 스스로 만든 깊이 변화에
            // 파도가 반응하면 되먹임이 생겨 수면이 요동칩니다.
            float RestDepth(float3 positionWS)
            {
                float2 uv = (positionWS.xz - _WaterDepthArea.xy) * _WaterDepthArea.z + _WaterDepthArea.w;

                float sampled = SAMPLE_TEXTURE2D_LOD(_WaterDepthMap, sampler_WaterDepthMap, saturate(uv), 0).r;

                // 지도 바깥은 먼바다입니다.
                //
                // <b>아주 넓게 걸쳐 넘겨야 합니다.</b> 좁게 넘기면 값이 부드럽게 변해도
                // 등고선이 지도의 사각형을 그대로 따라가, 바다 위에 네모난 테두리가 보입니다.
                // 넓게 펴면 섬에서 멀어질수록 서서히 거칠어지는 것으로만 읽힙니다.
                float2 outside = max(-uv, uv - 1.0);
                float  beyond  = saturate(max(outside.x, outside.y) * 1.2);

                return lerp(sampled, _OpenSeaDepth, beyond);
            }

            // ========================================================================================
            // 거스트너 파도
            //
            // 사인파는 수면을 위아래로만 흔들어 부풀린 이불처럼 보입니다.
            // 거스트너는 정점을 마루 쪽으로 <b>끌어당깁니다</b> — 마루가 뾰족해지고 골이 넓어져
            // 물처럼 보입니다. 접선도 해석적으로 나오므로 노멀을 따로 굽지 않아도 됩니다.
            // ========================================================================================

            // 이 파도의 진폭입니다. 가파름은 파장에 비례해 진폭이 됩니다.
            float WaveAmplitude(float4 wave)
            {
                return wave.z * max(wave.w, 0.05) / 6.28318530718;
            }

            float3 GerstnerWave(float4 wave, float damp, float phaseWarp, float3 positionWS, float time,
                                inout float3 tangent, inout float3 binormal)
            {
                float steepness = wave.z * damp;
                float wavelength = max(wave.w, 0.05);

                float k = 6.28318530718 / wavelength;

                // 심해파의 위상 속도입니다. 파장이 길수록 빠릅니다 — 이래야 여러 파도가 섞여 보입니다.
                float c = sqrt(9.8 / k);

                float2 d = normalize(wave.xy + 1e-5);

                // <b>위상을 노이즈로 흔듭니다.</b>
                //
                // 사인파 셋을 그대로 겹치면 간섭무늬가 짧은 주기로 반복되고,
                // 눈은 그 주기를 즉시 격자로 읽습니다. 자리마다 위상을 어긋나게 하면
                // 마루가 한 줄로 서지 못하고 흩어집니다.
                float f = k * (dot(d, positionWS.xz) - c * time) + phaseWarp;

                float amplitude = steepness / k;

                float sinF = sin(f);
                float cosF = cos(f);

                tangent += float3(
                    -d.x * d.x * (steepness * sinF),
                     d.x * (steepness * cosF),
                    -d.x * d.y * (steepness * sinF));

                binormal += float3(
                    -d.x * d.y * (steepness * sinF),
                     d.y * (steepness * cosF),
                    -d.y * d.y * (steepness * sinF));

                return float3(
                    d.x * (amplitude * cosF),
                    amplitude * sinF,
                    d.y * (amplitude * cosF));
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;

                // x = 마루 높이(0~1), y = 이 자리의 정지 수심
                float2 surface    : TEXCOORD3;
            };

            Varyings WaterVertex(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                float time = _TimeParameters.x * _WaveSpeed;

                // 얕은 곳일수록 파도를 죽입니다. 물가에서 해수면이 흔들리면
                // 보이는 물가와 건널 수 있는 경계가 어긋납니다.
                float restDepth = RestDepth(positionWS);
                float damp = smoothstep(0.0, max(_WaveShoreFade, 1e-3), restDepth);

                float3 tangent  = float3(1, 0, 0);
                float3 binormal = float3(0, 0, 1);

                // 자리마다 다른 위상 어긋남입니다. 파도 셋에 서로 다른 배수로 먹여
                // 셋이 같은 방식으로 흔들리지 않게 합니다.
                float warp = (Fbm(positionWS.xz * _WaveWarpScale) - 0.5) * _WaveWarpStrength;

                float3 offset = 0;
                offset += GerstnerWave(_WaveA, damp, warp,        positionWS, time, tangent, binormal);
                offset += GerstnerWave(_WaveB, damp, warp * 1.73, positionWS, time, tangent, binormal);
                offset += GerstnerWave(_WaveC, damp, warp * 2.41, positionWS, time, tangent, binormal);

                positionWS += offset;

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.screenPos  = ComputeScreenPos(output.positionCS);
                output.normalWS   = normalize(cross(binormal, tangent));

                // 마루일수록 1에 가깝습니다. 거품을 얹을 자리를 프래그먼트에 알려 줍니다.
                //
                // <b>감쇠로 나누면 안 됩니다.</b>
                //
                // 물가에서는 파도가 죽어 offset.y 가 거의 0인데, 같은 비율로 작아진 값으로 나누면
                // 비율은 그대로 1에 닿습니다. 눈에 보이지도 않는 잔파도가 마루로 판정되어
                // 얕은 강바닥에 거품 덩어리가 떠다닙니다.
                // 파도가 <b>온전히 섰을 때</b>의 높이로 나눠야 물가가 잔잔해집니다.
                float amplitudeSum = WaveAmplitude(_WaveA) + WaveAmplitude(_WaveB) + WaveAmplitude(_WaveC);

                output.surface = float2(
                    saturate(offset.y / max(amplitudeSum * 0.75, 1e-4) * 0.5 + 0.5),
                    restDepth);

                return output;
            }

            half4 WaterFragment(Varyings input) : SV_Target
            {
                // ---------------------------------------------------------------------------
                // 1. 수심 — 물 표면과 그 아래 지형 사이의 거리입니다.
                // ---------------------------------------------------------------------------
                float2 uv = input.screenPos.xy / max(input.screenPos.w, 1e-5);

                float sceneDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);

                // 반드시 screenPos.w 여야 합니다.
                //
                // 프래그먼트 단계의 SV_POSITION.w 는 클립 w 가 아니라 <b>1/w</b> 입니다 —
                // 래스터라이저가 원근 보간을 위해 덮어씁니다.
                // 그것을 수심으로 쓰면 카메라 거리 25에서 0.04 가 들어와
                // 수심이 곧 카메라 거리가 되고, 전 수면이 깊은 물 한 색으로 칠해집니다.
                // ComputeScreenPos 가 넘겨준 w 만이 시점 공간 깊이를 그대로 들고 있습니다.
                float surfaceDepth = input.screenPos.w;

                // 물이 지형보다 앞에 있을 때만 의미가 있습니다.
                float waterDepth = max(0.0, sceneDepth - surfaceDepth);

                // 얕을수록 0, 깊을수록 1.
                float deep = saturate(waterDepth / max(_DepthFade, 1e-3));

                float4 water = lerp(_ShallowColor, _DeepColor, deep);

                // ---------------------------------------------------------------------------
                // 2. 노멀 — 파도의 형태에 잔결을 얹습니다.
                // ---------------------------------------------------------------------------
                float3 normal = normalize(input.normalWS);

                float2 detailUV = input.positionWS.xz * _DetailScale;
                float  detailTime = _TimeParameters.x * _DetailSpeed;

                // 두 겹을 서로 다른 방향으로 흘립니다. 한 겹만 쓰면 흐르는 방향이 눈에 띕니다.
                float n1 = Fbm(detailUV + float2(detailTime, detailTime * 0.6));
                float n2 = Fbm(detailUV * 1.9 - float2(detailTime * 0.7, -detailTime));

                // 유한 차분 대신, 두 겹의 차이를 기울기처럼 씁니다. 값싸고 이 추상화 수준에 충분합니다.
                float2 slope = float2(n1 - n2, n2 - n1);

                // 멀리서는 잔결이 지글거리기만 합니다. 거리로 죽입니다.
                float distanceFade = saturate(1.0 - length(input.positionWS - _WorldSpaceCameraPos) / _DetailFadeDistance);

                // 물가에서는 파도와 함께 잔결도 잦아듭니다.
                float calm = smoothstep(0.0, max(_WaveShoreFade, 1e-3), input.surface.y);

                normal = normalize(normal + float3(slope.x, 0, slope.y) * _DetailStrength * distanceFade * calm);

                // ---------------------------------------------------------------------------
                // 3. 물가 거품선 — 깊이가 거의 0인 띠입니다.
                // 강이 지형에 파고든 것처럼 보이게 하는 것이 이 대목입니다.
                // ---------------------------------------------------------------------------
                float shore = 1.0 - saturate(waterDepth / max(_ShoreWidth, 1e-3));

                // 깔끔한 등고선은 물가처럼 보이지 않습니다. 가장자리를 노이즈로 흩뜨립니다.
                float foamNoise = Fbm(input.positionWS.xz * _ShoreNoiseScale
                                    + float2(0, _TimeParameters.x * _ShoreSpeed));

                shore = saturate(shore - (foamNoise - 0.5) * _ShoreNoiseStrength);
                shore = pow(shore, _ShorePower);

                // ---------------------------------------------------------------------------
                // 4. 마루 거품 — 파도가 선 자리에만 옅게 얹습니다.
                //
                // 높이만으로 자르면 거품이 파도의 <b>등고선</b>을 그대로 따라가고,
                // 파도가 주기적이므로 그 등고선이 곧 격자가 됩니다.
                // 노이즈로 문턱을 자리마다 흔들어 띠를 끊습니다.
                // ---------------------------------------------------------------------------
                float crestMask = Fbm(input.positionWS.xz * _CrestNoiseScale
                                    + float2(_TimeParameters.x * 0.05, 0));

                // 얕은 물에서는 거품이 아예 뜨지 않아야 합니다.
                // 마루 판정은 파도 높이의 <b>비율</b>이라 잔파도에서도 1에 닿을 수 있으므로,
                // 파도가 실제로 살아 있는 정도(calm)로 한 번 더 눌러 줍니다.
                float crest = smoothstep(_CrestThreshold, 1.0, input.surface.x * (0.55 + crestMask))
                            * _CrestStrength * calm;

                // ---------------------------------------------------------------------------
                // 5. 조명
                // ---------------------------------------------------------------------------
                Light mainLight = GetMainLight();

                // 지형과 같은 half lambert 곡선입니다. 여기서 식이 갈라지면
                // 물만 다른 세계의 조명을 받는 것처럼 보입니다.
                float lambert = dot(normal, mainLight.direction) * 0.5 + 0.5;

                // 구름 그늘은 물에도 집니다. 뭍만 어두워지면 섬이 오려 붙인 것처럼 보입니다.
                float clouds = CloudShadow(input.positionWS, mainLight.direction);

                // 지형과 <b>같은</b> 계단으로 끊습니다.
                // 물은 그림자를 받지 않으므로 감쇠는 1, 깊이는 0 입니다.
                float shading = ToonLight(lambert, 1.0, clouds, _AmbientBoost, 0.0);

                float3 viewDir = normalize(_WorldSpaceCameraPos - input.positionWS);

                // 마루에서 튀는 햇빛입니다. 수면이 살아 있다는 인상의 대부분이 여기서 나옵니다.
                float3 halfDir = normalize(mainLight.direction + viewDir);
                float  glitter = pow(saturate(dot(normal, halfDir)), _SpecSharpness) * _SpecStrength;

                // 비스듬히 볼수록 하늘빛을 더 받습니다. 평평한 판이 판으로 보이지 않게 합니다.
                float glint = pow(1.0 - saturate(viewDir.y), 4.0) * _Fresnel;

                // ---------------------------------------------------------------------------
                // 6. 합성
                // ---------------------------------------------------------------------------
                float3 color = water.rgb;

                color = lerp(color, _CrestColor.rgb, crest);
                color = lerp(color, _ShoreColor.rgb, shore);

                // 하늘빛은 더하지 않고 섞습니다. 더하면 먼바다가 통째로 바래
                // 수심으로 애써 만든 색 차이가 사라집니다.
                color = lerp(color, _SkyColor.rgb, glint);

                // <b>명암을 마지막에 겁니다.</b>
                //
                // 예전에는 물빛에만 명암을 걸고 거품과 하늘빛은 그 뒤에 섞었습니다.
                // 구름이 지날 때 물은 어두워지는데 <b>물마루의 거품만 밝게 남아</b>
                // 떠 보였습니다. 다 섞은 뒤에 한 번 걸면 수면 전체가 한 덩어리로 그늘에 듭니다.
                color *= shading * mainLight.color;

                // 반짝임도 그늘에서는 죽습니다. 구름 아래에서 햇빛이 튈 리가 없습니다.
                color += glitter * mainLight.color * clouds;

                // 물가는 불투명에 가깝게 두어 경계가 흐려지지 않게 합니다.
                float alpha = lerp(water.a, 1.0, max(shore, crest * 0.5));

                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
