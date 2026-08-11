// 전장의 풀입니다.
//
// <b>왜 풀을 심는가</b>
//
// 지형은 고도·경사·물가로 색이 갈리지만, 그것만으로는 표면이 여전히 매끈합니다.
// 부대가 서 있는 자리에 결이 없으면 전장이 지도처럼 보입니다.
// 풀은 그 결을 만들고, 무엇보다 <b>바람이 지나가는 방향</b>을 눈에 보이게 합니다.
//
// <b>왜 인스턴싱인가</b>
//
// 풀잎 하나하나가 게임오브젝트이면 수만 개의 트랜스폼이 생깁니다.
// 여기서는 뿌리 위치만 행렬로 넘기고, <b>나머지는 전부 자리에서 계산</b>합니다 —
// 색도, 흔들림도, 눌림도 월드 좌표에서 나옵니다. 인스턴스마다 넘길 데이터가 없습니다.
//
// <b>왜 판이 아니라 그림인가</b>
//
// 예전에는 잎의 윤곽을 메시에 구웠습니다. 텍스처가 없으니 형태를 정점이 들어야 했습니다.
// 그러나 정점으로 만들 수 있는 윤곽은 결국 <b>대칭인 사다리꼴</b>입니다.
// 한 포기가 여러 갈래로 뻗은 모습은 정점 몇 개로 나오지 않습니다.
// 24픽셀짜리 알파 그림 한 장이 그 일을 대신하고, 알파 컷아웃 한 번이면 그려집니다.
//
// <b>왜 프레임을 끊어 흔드는가</b>
//
// 매끄럽게 흔들리는 풀은 3D 로 보입니다. 이 게임의 유닛은 픽셀 빌보드입니다.
// 배경만 초당 60번 매끄럽게 움직이면 <b>유닛이 배경 위에 얹힌 스티커처럼</b> 떠 보입니다.
// 풀의 갱신을 초당 몇 번으로 끊으면 둘이 같은 시간 위에 놓입니다.
// 포기마다 끊는 박자를 어긋내는 것이 핵심입니다 — 한 박자로 끊으면 그냥 렉으로 보입니다.
//
// <b>휘는 계산은 한 곳에만 있습니다</b>
//
// 본체·깊이·노멀 세 패스가 모두 같은 <c>BladeWorldPosition</c> 을 부릅니다.
// 패스마다 따로 쓰면 언젠가 하나만 고쳐지고, 그때부터 잎이 서 있는 자리와
// 깊이 버퍼가 말하는 자리가 어긋납니다.
//
// 기법 출처: Dylearn 3D Pixel Art Grass Demo (MIT).
// 잎 그림 출처: 같은 저장소 (CC BY 4.0). 09.Docs/ATTRIBUTION.md 를 보십시오.
Shader "SRPG/Grass"
{
    Properties
    {
        [Header(Sprite)]
        [NoScaleOffset] _BaseMap   ("Blade Sprite", 2D)  = "white" {}
        [NoScaleOffset] _AccentMap ("Accent Sprite", 2D) = "white" {}

        // 알파를 자르는 기준입니다. 그림이 흑백 실루엣이라 가운데면 충분합니다.
        _Cutoff ("Alpha Cutoff", Range(0.05, 0.95)) = 0.5

        [Header(Color)]
        _BaseColor  ("Base Color", Color)  = (0.36, 0.52, 0.26, 1)
        _TipColor   ("Tip Color", Color)   = (0.62, 0.72, 0.36, 1)
        _TipBlend   ("Tip Blend", Range(0, 1)) = 0.65

        // 밑동을 눌러 지면에 앉힙니다. 없으면 풀이 땅에서 떠 보입니다.
        _RootShade  ("Root Shade", Range(0, 1)) = 0.45

        [Header(Accent)]
        // 드물게 섞이는 다른 풀입니다.
        //
        // <b>왜 자리가 아니라 확률로 고르는가</b>
        //
        // 종은 땅이 정합니다 — 갈대는 물가, 잡초는 비탈입니다. 그것은 규칙입니다.
        // 강조풀은 규칙이 아니라 <b>예외</b>입니다. 같은 땅에 같은 것만 자라면
        // 들판이 규칙적으로 보이고, 규칙적인 들판은 인공물로 읽힙니다.
        _AccentColor  ("Accent Color", Color)          = (0.68, 0.66, 0.34, 1)
        _AccentChance ("Accent Chance", Range(0, 0.3)) = 0.06
        _AccentScale  ("Accent Scale", Range(0.5, 2.5)) = 1.35

        [Header(Noise Patches)]
        // 큰 얼룩입니다. 들판을 몇 덩어리로 나눕니다.
        //
        // <b>배율은 전장 크기에 맞춰야 합니다.</b> 너무 작게 잡으면 섬 전체가
        // 노이즈 한 칸 안에 들어가 얼룩이 하나도 보이지 않습니다.
        // 0.075 는 대략 13 월드 단위짜리 덩어리라, 64 단위 전장에 네댓 개가 앉습니다.
        _PatchColorA     ("Patch A Color", Color)             = (0.24, 0.42, 0.19, 1)
        _PatchScaleA     ("Patch A Scale", Range(0.005, 0.5)) = 0.075
        _PatchThresholdA ("Patch A Threshold", Range(0, 1))   = 0.42
        _PatchBlendA     ("Patch A Edge", Range(0.01, 0.4))   = 0.18

        // 작은 얼룩입니다. 큰 덩어리 안에 결을 넣습니다.
        _PatchColorB     ("Patch B Color", Color)             = (0.63, 0.62, 0.30, 1)
        _PatchScaleB     ("Patch B Scale", Range(0.005, 0.5)) = 0.26
        _PatchThresholdB ("Patch B Threshold", Range(0, 1))   = 0.50
        _PatchBlendB     ("Patch B Edge", Range(0.01, 0.4))   = 0.14

        // 잎마다의 밝기 흔들림입니다.
        //
        // <b>낮게 잡습니다.</b> 포기마다 독립적으로 흔들면 흰 노이즈가 되어
        // 들판이 자글자글하게 갈라집니다. 자연의 들판은 포기가 아니라 <b>무리</b> 단위로 밝기가 갈립니다.
        // 큰 몫은 아래 덩어리 흔들림이 맡습니다.
        _ColorJitter ("Per Blade Jitter", Range(0, 0.6)) = 0.08

        // 무리 단위의 밝기 흔들림입니다. 여러 포기가 함께 밝아지고 함께 어두워집니다.
        _ClusterScale  ("Cluster Scale", Range(0.01, 0.6))  = 0.12
        _ClusterJitter ("Cluster Jitter", Range(0, 0.8))    = 0.18

        [Header(Color Cohesion)]
        // 얼룩이 기준 색에서 <b>색조로</b> 얼마나 벌어질 수 있는가입니다.
        //
        // 자연의 들판은 색조가 아니라 밝기와 채도로 갈립니다.
        // 색조까지 벌어지면 다른 식물이 섞여 자란 것처럼 보이고, 그것이
        // "색이 잘게 나뉘어 보인다"의 가장 큰 원인입니다.
        // 0 이면 색조를 완전히 기준에 맞추고 밝기·채도만 남습니다.
        _HueSpread ("Hue Spread", Range(0, 1)) = 0.35

        // 모든 변주를 기준 색 쪽으로 당기는 정도입니다.
        // 얼룩을 지우지 않고 <b>묶습니다</b>. 잘게 나뉜 느낌을 한 손잡이로 줄일 때 씁니다.
        _ColorCohesion ("Color Cohesion", Range(0, 1)) = 0.25

        [Header(Altitude)]
        // <b>지형 셰이더와 같은 값</b>이어야 합니다. C# 이 전장마다 넣어 줍니다.
        _DryColor    ("Highland Dry Color", Color)    = (0.56, 0.56, 0.34, 1)
        _SeaLevel    ("Sea Level", Float)             = 0
        _HeightRange ("Height Range", Range(0.5, 40)) = 6

        // 고도가 색을 <b>전부</b> 정하게 두면 안 됩니다.
        // 기복이 얕은 전장에서는 섬 대부분이 고지로 판정되어 들판이 한 색으로 마르고,
        // 애써 만든 노이즈 얼룩이 그 밑에 묻힙니다.
        _DryStrength ("Highland Dryness", Range(0, 1)) = 0.55

        [Header(Facing)]
        // 잎이 무엇을 향해 도는가입니다.
        //
        // 0 이면 잎마다 카메라 <b>자리</b>를 향합니다 — 카메라를 중심으로 부채꼴이 생기고,
        // 카메라를 옮기면 들판이 소용돌이처럼 돌아갑니다.
        // 1 이면 카메라가 <b>보는 방향</b>에 나란히 섭니다. 화면 어디에 있든 같은 각입니다.
        _ViewAlign ("View Aligned", Range(0, 1)) = 1

        // 나란히 선 잎을 포기마다 좌우로 비트는 한계 각도입니다.
        // 0 이면 전부 같은 각이라 판때기로 보이고, 크면 제각각이라 덤불처럼 보입니다.
        _FacingNoise ("Facing Noise", Range(0, 90)) = 18

        [Header(Stepped Time)]
        // 흔들림을 갱신하는 초당 횟수입니다.
        // 유닛 스프라이트의 프레임 수와 가까워야 둘이 같은 시간 위에 놓입니다.
        _Framerate ("Sway Framerate", Range(1, 60)) = 8

        // 끄면 매끄럽게 흔들립니다. 끊는 편이 이 게임의 그림체에 맞습니다.
        //
        // <b>ToggleUI 여야 합니다.</b> [Toggle] 은 셰이더 키워드를 함께 켜고 끄는데,
        // 여기서는 값을 그대로 읽으므로 선언한 적 없는 키워드만 남습니다.
        [ToggleUI] _Quantised ("Step The Framerate", Float) = 1

        [Header(Wind)]
        _WindScale     ("Wind Scale", Range(0.005, 0.5)) = 0.055
        _WindSpeed     ("Wind Speed", Range(0, 3))       = 0.55
        _WindDirection ("Wind Direction (xz)", Vector)   = (1, 0, 0.35, 0)

        // 바람이 잎을 눕히는 최대 각도입니다. 도 단위입니다.
        _WindSwayAngle ("Wind Sway Angle", Range(0, 90)) = 34

        // 바람 노이즈의 밝기입니다. 올리면 들판이 더 자주 눕습니다.
        _WindBias ("Wind Bias", Range(-1, 1)) = 0.1

        // 두 겹의 바람 노이즈가 갈라지는 각도입니다. 0 이면 함께 흘러 주기가 드러납니다.
        _WindDiverge ("Wind Diverge (deg)", Range(0, 45)) = 10

        [Header(Idle Sway)]
        // 바람과 별개로 잎이 제자리에서 까딱이는 움직임입니다.
        // 바람이 잔잔할 때 들판이 완전히 멈춰 버리면 죽은 그림이 됩니다.
        _IdleSwaySpeed ("Idle Sway Speed", Range(0, 5))  = 0.35
        _IdleSwayAngle ("Idle Sway Angle", Range(0, 45)) = 7

        [Header(Fake Perspective)]
        // 잎이 카메라 쪽으로 눕거나 멀어질 때 밑동의 폭을 줄여 원근을 흉내 냅니다.
        //
        // 빌보드는 언제나 카메라를 정면으로 봅니다. 그래서 앞뒤로 눕혀도
        // <b>기울었다는 것이 안 보입니다</b>. 폭을 줄여야 비로소 누운 것으로 읽힙니다.
        _FakePerspective ("Fake Perspective", Range(0, 1)) = 0.35

        [Header(Trample)]
        // 부대가 지나간 자리는 눕습니다. C# 이 유닛 위치를 넣어 줍니다.
        _TrampleStrength ("Trample Strength", Range(0, 2))  = 1.0

        // 눌림이 가장자리로 갈수록 잦아드는 급함입니다.
        // 1 이면 곧게 잦아들고, 크게 잡으면 발밑만 눕고 둘레는 서 있습니다.
        _TrampleFalloff  ("Trample Falloff", Range(0.2, 6)) = 1.6

        [Header(Foliage)]
        // <b>잎을 둥글게 칠합니다.</b>
        //
        // 잎은 실제로는 평면 사각형입니다. 그래서 노멀을 그대로 쓰면 들판의 모든 포기가
        // <b>완전히 같은 밝기</b>가 되고, 그 위에 툰 밴딩이 걸리면 들판이 한 장의 금속판처럼 읽힙니다.
        // 폭 방향으로 노멀을 굴려 주면 한 포기 안에서도 명암이 돌아, 비로소 풀로 보입니다.
        _NormalRound ("Blade Roundness", Range(0, 1)) = 0.55

        // 끝으로 갈수록 노멀을 위로 세웁니다. 잎끝은 하늘을 봅니다.
        _NormalTipUp ("Tip Points Up", Range(0, 1)) = 0.5

        // 포기마다 <b>다른 방향</b>으로 굴립니다.
        //
        // 굴리기만 하고 방향이 같으면 여전히 온 들판이 같은 밝기입니다.
        // 방향은 뿌리 자리에서 뽑으므로 카메라를 돌려도 밝기가 출렁이지 않습니다.
        _NormalScatter ("Normal Scatter", Range(0, 1)) = 0.8

        [Header(Translucency)]
        // <b>잎은 얇아서 빛이 통과합니다.</b>
        //
        // 이것이 없으면 해를 등진 풀이 그늘 속 풀과 똑같이 어둡습니다.
        // 실제로는 뒤에서 빛을 받은 잎이 스스로 밝게 빛나고, 그 대비가 식물을 식물로 보이게 합니다.
        // 투과가 없는 것이 식생이 플라스틱처럼 보이는 가장 큰 이유입니다.
        _Translucency      ("Translucency", Range(0, 1))       = 0.22
        _TranslucencyColor ("Translucency Color", Color)       = (0.72, 0.85, 0.35, 1)

        // 클수록 해를 정확히 등졌을 때만 빛납니다.
        _TranslucencyPower ("Translucency Focus", Range(1, 16)) = 4

        // 밑동은 겹치고 두꺼워 빛이 덜 통과합니다.
        _TranslucencyRoot  ("Root Thickness", Range(0, 1))      = 0.15

        [Header(Lighting)]
        // 지형 셰이더와 <b>같은 값</b>이어야 합니다. 갈라지면 풀만 다른 조명을 받습니다.
        _AmbientBoost ("Ambient Boost", Range(0, 1)) = 0.35
        _ShadowDepth  ("Shadow Depth", Range(0, 1))  = 0.55
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

        // RgbToHsv / HsvToRgb 를 씁니다. 색조를 좁히려면 색조를 다룰 수 있어야 합니다.
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        // 물·지형과 <b>같은</b> 노이즈이고 <b>같은</b> 명암입니다.
        #include "SRPG_Noise.hlsl"
        #include "SRPG_Toon.hlsl"

        // 풀을 눕히는 지점의 최대 개수입니다.
        // 늘리면 상수 버퍼가 커지고 정점마다 도는 반복이 길어집니다.
        #define TRAMPLE_CAPACITY 32

        // 잎이 누울 수 있는 최대 각도입니다. 라디안입니다.
        //
        // 바람과 눌림이 겹치면 각도가 그냥 더해집니다. 막지 않으면 잎이
        // 90도를 넘겨 <b>땅속으로 뒤집혀</b> 들어갑니다.
        #define MAX_BEND 1.4

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        TEXTURE2D(_AccentMap);
        SAMPLER(sampler_AccentMap);

        CBUFFER_START(UnityPerMaterial)
            float  _ViewAlign;
            float  _FacingNoise;

            float  _ClusterScale;
            float  _ClusterJitter;
            float  _HueSpread;
            float  _ColorCohesion;

            float  _NormalRound;
            float  _NormalTipUp;
            float  _NormalScatter;

            float  _Translucency;
            float4 _TranslucencyColor;
            float  _TranslucencyPower;
            float  _TranslucencyRoot;

            float  _Cutoff;

            float4 _BaseColor;
            float4 _TipColor;
            float  _TipBlend;
            float  _RootShade;

            float4 _AccentColor;
            float  _AccentChance;
            float  _AccentScale;

            float4 _PatchColorA;
            float  _PatchScaleA;
            float  _PatchThresholdA;
            float  _PatchBlendA;

            float4 _PatchColorB;
            float  _PatchScaleB;
            float  _PatchThresholdB;
            float  _PatchBlendB;

            float  _ColorJitter;

            float4 _DryColor;
            float  _SeaLevel;
            float  _HeightRange;
            float  _DryStrength;

            float  _Framerate;
            float  _Quantised;

            float  _WindScale;
            float  _WindSpeed;
            float4 _WindDirection;
            float  _WindSwayAngle;
            float  _WindBias;
            float  _WindDiverge;

            float  _IdleSwaySpeed;
            float  _IdleSwayAngle;

            float  _FakePerspective;

            float  _TrampleStrength;
            float  _TrampleFalloff;

            float  _AmbientBoost;
            float  _ShadowDepth;
        CBUFFER_END

        // 전역입니다. 머티리얼이 아니라 <b>이번 프레임의 부대 위치</b>이므로
        // UnityPerMaterial 밖에 있어야 합니다.
        float4 _TramplePoints[TRAMPLE_CAPACITY];   // xyz = 위치, w = 반경
        int    _TrampleCount;

        // 잎 하나가 놓이는 자리를 푸는 데 필요한 모든 것입니다.
        //
        // 정점 셋이 각자 다시 구하면 같은 잎의 정점이 서로 다른 바람을 맞습니다.
        // 잎마다 <b>한 번만</b> 풀고 정점은 그 결과를 씁니다.
        struct BladeState
        {
            float3 rootWS;      // 뿌리의 월드 좌표
            float3 right;       // 화면 가로 방향. 잎의 폭이 놓이는 축
            float3 bendAxis;    // 잎이 눕는 수평 방향
            float  bendSin;     // 눕는 각도의 사인
            float  bendCos;     // 눕는 각도의 코사인
            float  roll;        // 화면 안에서 좌우로 까딱이는 각도
            float  perspective; // 밑동의 폭을 줄이는 정도
            float  accent;      // 1 이면 강조풀입니다
        };

        // 인스턴스 행렬이 놓아 준 뿌리의 월드 좌표입니다.
        float3 BladeRootWS()
        {
            return TransformObjectToWorld(float3(0, 0, 0));
        }

        // 인스턴스 행렬에 담긴 폭과 키입니다. 포기마다 다릅니다.
        float2 BladeScale()
        {
            return float2(
                length(GetObjectToWorldMatrix()._m00_m10_m20),
                length(GetObjectToWorldMatrix()._m01_m11_m21));
        }

        // 잎이 Y축으로만 돌아 관객을 향합니다.
        //
        // <b>완전한 빌보드는 쓰지 않습니다.</b>
        //
        // 이 게임의 카메라는 47도로 내려다봅니다. 완전한 빌보드로 만들면
        // 잎이 그만큼 뒤로 누워 <b>들판 전체가 바닥에 깔린 카펫</b>이 됩니다.
        // Y축으로만 돌면 잎은 언제나 서 있고, 눕는 것은 바람과 발이 눕힐 때뿐입니다.
        //
        // <b>카메라의 '자리'가 아니라 '보는 방향'을 향합니다</b>
        //
        // 예전에는 잎마다 카메라 좌표를 향해 돌았습니다. 그러면 잎이 카메라를 중심으로
        // <b>부채꼴로 펼쳐집니다</b> — 화면 가운데의 풀과 가장자리의 풀이 서로 다른 각으로 서고,
        // 카메라를 움직이면 들판이 통째로 소용돌이처럼 돌아갑니다.
        // 시선이 내려다보는 각일수록 이 회전이 눈에 띕니다.
        //
        // 시선 방향을 쓰면 모든 잎이 <b>서로 나란히</b> 섭니다. 화면 어디에 있든 같은 각이고,
        // 카메라가 평행 이동해도 들판은 가만히 있습니다.
        // <c>UNITY_MATRIX_V</c> 의 세 번째 행이 카메라의 뒤쪽 축, 즉 관객을 향하는 방향입니다.
        //
        // <b>다만 나란하기만 하면 판때기처럼 보입니다.</b>
        // 그래서 포기마다 뿌리 자리에서 뽑은 각만큼 비틀어 둡니다.
        // 자리에서 뽑으므로 같은 포기는 언제나 같은 각이고, 그래서 깜빡이지 않습니다.
        void BladeBasis(float3 rootWS, out float3 right, out float3 toCamera)
        {
            // 예전 방식 — 잎에서 카메라 자리로.
            float3 toPoint = _WorldSpaceCameraPos - rootWS;
            toPoint.y = 0;

            float pointLengthSq = dot(toPoint, toPoint);
            toPoint = pointLengthSq > 1e-6 ? toPoint * rsqrt(pointLengthSq) : float3(0, 0, 1);

            // 새 방식 — 시선 평면에 나란히. 잎의 자리와 무관하게 언제나 같은 방향입니다.
            float3 toView = UNITY_MATRIX_V._m20_m21_m22;
            toView.y = 0;

            float viewLengthSq = dot(toView, toView);
            toView = viewLengthSq > 1e-6 ? toView * rsqrt(viewLengthSq) : toPoint;

            // 위에서 거의 수직으로 내려다보면 시선의 수평 성분이 사라집니다.
            // 그때는 방향을 정할 근거가 없으므로 예전 방식으로 물러납니다.
            float3 facing = normalize(lerp(toPoint, toView, _ViewAlign * saturate(viewLengthSq * 64.0)));

            // 포기마다 좌우로 비틉니다. -1에서 1 사이를 각도 한계에 곱합니다.
            float twist = (Hash21(rootWS.xz * 7.31) * 2.0 - 1.0) * radians(_FacingNoise);

            float twistSin;
            float twistCos;
            sincos(twist, twistSin, twistCos);

            // Y축 회전입니다. 잎은 계속 서 있고 바라보는 방향만 돌아갑니다.
            toCamera = float3(
                facing.x * twistCos - facing.z * twistSin,
                0,
                facing.x * twistSin + facing.z * twistCos);

            right = normalize(cross(float3(0, 1, 0), toCamera));
        }

        // 흔들림이 갱신되는 시각입니다.
        //
        // <b>왜 포기마다 박자를 어긋내는가</b>
        //
        // 들판 전체를 같은 순간에 끊으면 초당 여덟 번 화면이 통째로 덜컥입니다.
        // 그것은 그림체가 아니라 그냥 <b>렉</b>으로 보입니다.
        // 뿌리 자리에서 뽑은 위상만큼 밀어 두면 포기마다 끊기는 순간이 달라지고,
        // 들판은 끊기면서도 전체로는 이어져 흐릅니다.
        float SteppedTime(float seed)
        {
            float time = _TimeParameters.x;

            if (_Quantised < 0.5)
            {
                return time;
            }

            float framerate = max(_Framerate, 1.0);
            float frametime = 1.0 / framerate;

            float phase = fmod(seed, frametime);

            return round((time + phase) * framerate) / framerate;
        }

        // 부대가 지나간 자리를 눕힙니다.
        //
        // 미는 방향은 <b>부대에서 잎을 향한</b> 수평 방향입니다.
        // 그래야 밟힌 자리를 중심으로 풀이 바깥으로 쓰러집니다.
        //
        // 돌려주는 값은 방향이자 크기입니다 — 길이가 곧 눕는 각도(라디안)입니다.
        float3 TrampleLean(float3 rootWS)
        {
            float3 lean = 0;

            for (int i = 0; i < _TrampleCount; i++)
            {
                // 'point' 는 HLSL 이 예약해 둔 낱말입니다. 쓰면 컴파일이 막힙니다.
                float3 center = _TramplePoints[i].xyz;
                float  radius = max(_TramplePoints[i].w, 1e-3);

                float3 away = rootWS - center;
                away.y = 0;

                float distance = length(away);

                // 가운데일수록 강하게, 가장자리에서 0이 됩니다.
                float falloff = pow(saturate(1.0 - distance / radius), _TrampleFalloff);

                if (falloff > 0.0)
                {
                    float3 direction = distance > 1e-4 ? away / distance : float3(1, 0, 0);
                    lean += direction * falloff;
                }
            }

            return lean * _TrampleStrength;
        }

        // 이 잎이 이번 프레임에 어떻게 서 있는지를 한 번에 풉니다.
        BladeState ResolveBlade()
        {
            BladeState blade;

            float3 rootWS = BladeRootWS();

            // 구조체 멤버를 out 인자로 바로 넘기지 않습니다.
            // 컴파일러에 따라 거부하거나 조용히 임시본에 써 버립니다.
            float3 right;
            float3 toCamera;
            BladeBasis(rootWS, right, toCamera);

            blade.rootWS = rootWS;
            blade.right = right;

            // 자리에서 뽑은 씨앗입니다. 같은 자리는 언제나 같은 값이라 깜빡이지 않습니다.
            float seed = Hash21(rootWS.xz) * 10.0;

            blade.accent = step(Hash21(rootWS.xz * 3.77), _AccentChance);

            float time = SteppedTime(seed);

            // --- 바람 ---
            //
            // 어긋나게 흐르는 두 겹입니다. 구름과 같은 함수를 부릅니다.
            // 노이즈이므로 들판 전체가 한 박자로 흔들리지 않고 물결처럼 지나갑니다.
            float2 windDirection = normalize(_WindDirection.xz + 1e-5);

            // 흐른 거리를 <b>끊긴 시각</b>에서 셉니다.
            // 매끄러운 시각을 넘기면 잎은 끊겨도 바람만 매끄럽게 흘러 어긋납니다.
            float gust = DivergentNoise(
                blade.rootWS.xz,
                windDirection,
                _WindScale,
                time * _WindSpeed,
                _WindDiverge);

            // 0~1 을 -1~1 로 폅니다. 바람은 양쪽으로 붑니다.
            gust = saturate(gust + _WindBias);
            gust = (gust - 0.5) * 2.0;

            float windAngle = gust * radians(_WindSwayAngle);

            // --- 눕는 방향과 각도 ---
            //
            // <b>바람과 눌림을 한 벡터로 더합니다.</b>
            //
            // 따로 돌리면 회전이 겹쳐 잎이 비틀립니다. 방향과 크기를 함께 담은
            // 벡터로 더한 뒤 <b>한 번만</b> 돌리면, 발밑에서는 바람을 거슬러
            // 바깥으로 눕고 멀리서는 바람대로 눕는 것이 저절로 나옵니다.
            float3 lean = float3(windDirection.x, 0, windDirection.y) * windAngle
                        + TrampleLean(rootWS);

            float bendAngle = length(lean);

            float3 bendAxis = bendAngle > 1e-5 ? lean / bendAngle : float3(1, 0, 0);

            bendAngle = min(bendAngle, MAX_BEND);

            float bendSin;
            float bendCos;
            sincos(bendAngle, bendSin, bendCos);

            blade.bendAxis = bendAxis;
            blade.bendSin = bendSin;
            blade.bendCos = bendCos;

            // --- 제자리 까딱임 ---
            blade.roll = sin((time + seed) * _IdleSwaySpeed * 6.2831853) * radians(_IdleSwayAngle);

            // --- 가짜 원근 ---
            //
            // 잎이 카메라 쪽으로 눕는 정도만 셉니다. 옆으로 눕는 것은 이미 보입니다.
            blade.perspective = dot(bendAxis, toCamera) * bendSin * _FakePerspective;

            return blade;
        }

        // 잎의 한 정점이 실제로 놓이는 자리입니다.
        //
        // <b>세 패스가 전부 이 함수를 부릅니다.</b> 본체가 서 있는 자리와
        // 깊이·노멀 버퍼가 말하는 자리가 갈라지면 안 됩니다.
        float3 BladeWorldPosition(float3 positionOS, BladeState blade)
        {
            float2 scale = BladeScale();

            // 강조풀은 조금 더 큽니다.
            scale *= lerp(1.0, _AccentScale, blade.accent);

            float2 local = float2(positionOS.x * scale.x, positionOS.y * scale.y);

            // --- 화면 안에서 까딱임 ---
            // 밑동을 축으로 돕니다. 가운데를 축으로 돌리면 뿌리가 땅에서 떨어집니다.
            float rollSin;
            float rollCos;
            sincos(blade.roll, rollSin, rollCos);

            local = float2(local.x * rollCos - local.y * rollSin,
                           local.x * rollSin + local.y * rollCos);

            // --- 눕기 ---
            // 강체 회전입니다. 키를 눕는 각도만큼 앞으로 내주고 그만큼 낮아집니다.
            return blade.rootWS
                 + blade.right * local.x
                 + float3(0, local.y * blade.bendCos, 0)
                 + blade.bendAxis * (local.y * blade.bendSin);
        }

        // 그림에서 뽑아 낼 자리입니다.
        //
        // 밑동 쪽의 가로 폭을 줄여 잎이 앞뒤로 기운 것처럼 보이게 합니다.
        // 잎끝(v=1)은 건드리지 않습니다 — 기운 잎의 끝은 원래 제자리에 있습니다.
        float2 BladeUV(float2 uv, float perspective)
        {
            uv.x -= 0.5;
            uv.x *= (1.0 - uv.y) * perspective + 1.0;
            uv.x += 0.5;

            // 그림 밖으로 나가면 반대쪽이 딸려 옵니다. 잘라 냅니다.
            return float2(clamp(uv.x, 0.0, 1.0), uv.y);
        }

        // 잎의 알파입니다. 세 패스가 같은 값으로 잘라야 합니다.
        float BladeAlpha(float2 uv, float accent)
        {
            float sprite = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a;
            float accentSprite = SAMPLE_TEXTURE2D(_AccentMap, sampler_AccentMap, uv).a;

            return lerp(sprite, accentSprite, accent);
        }
        ENDHLSL

        // ================================================================================================
        // Pass 1 — 본체
        // ================================================================================================
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            // 잎은 양면입니다. 카메라를 향해 돌려도 뒤집힌 면이 보일 수 있습니다.
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex GrassVertex
            #pragma fragment GrassFragment

            #pragma multi_compile_instancing

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;

                // 잎 하나가 한 가지 색이어야 하므로 <b>뿌리</b> 좌표를 넘깁니다.
                // 정점 좌표로 색을 정하면 한 잎 안에서 색이 갈라집니다.
                float3 rootWS     : TEXCOORD2;

                // x = 강조풀 여부, y = 가짜 원근의 세기
                float2 blade      : TEXCOORD3;
            };

            Varyings GrassVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);

                BladeState blade = ResolveBlade();

                float3 positionWS = BladeWorldPosition(input.positionOS.xyz, blade);

                output.positionWS = positionWS;
                output.rootWS = blade.rootWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                output.blade = float2(blade.accent, blade.perspective);

                return output;
            }

            // 뿌리 자리의 노이즈로 색 얼룩을 고릅니다.
            //
            // <b>배율이 다른 두 겹</b>이어야 합니다. 같은 배율로 두 번 뽑으면
            // 두 얼룩이 같은 크기로 겹쳐 결국 한 겹과 다르지 않습니다.
            float3 PatchColor(float2 rootXZ, float3 baseColor)
            {
                float noiseA = Fbm(rootXZ * _PatchScaleA);
                float noiseB = Fbm(rootXZ * _PatchScaleB);

                float3 color = baseColor;

                color = lerp(color, _PatchColorA.rgb,
                    smoothstep(_PatchThresholdA - _PatchBlendA, _PatchThresholdA + _PatchBlendA, noiseA));

                color = lerp(color, _PatchColorB.rgb,
                    smoothstep(_PatchThresholdB - _PatchBlendB, _PatchThresholdB + _PatchBlendB, noiseB));

                return color;
            }

            // 색조를 원형으로 보간합니다.
            //
            // 색조는 0 과 1 이 같은 자리인 고리입니다. 그냥 lerp 하면 빨강에서 보라로 갈 때
            // 고리를 <b>먼 쪽으로</b> 한 바퀴 돌아 초록·파랑을 지나갑니다.
            // 짧은 쪽 호를 골라야 합니다.
            float LerpHue(float from, float to, float t)
            {
                float delta = frac(to - from + 0.5) - 0.5;

                return frac(from + delta * t);
            }

            // 색을 기준 색의 <b>색조 쪽으로</b> 좁힙니다. 밝기와 채도는 건드리지 않습니다.
            //
            // <b>왜 색조만 좁히는가</b>
            //
            // 자연의 들판이 균질해 보이지 않는 것은 밝기와 채도가 자리마다 다르기 때문이지,
            // 색조가 다르기 때문이 아닙니다. 한 들판의 풀은 대체로 같은 식물이고, 같은 식물은
            // 같은 색소를 씁니다. 색조까지 벌어지면 <b>다른 식물이 섞여 자란 것</b>으로 읽히고,
            // 그것이 "색이 잘게 나뉘어 보인다"는 인상의 정체입니다.
            //
            // spread 가 1 이면 얼룩이 적어 둔 색조를 그대로 쓰고, 0 이면 기준 색조로 모읍니다.
            float3 ConstrainHue(float3 color, float3 reference, float spread)
            {
                float3 hsv = RgbToHsv(color);
                float3 referenceHsv = RgbToHsv(reference);

                hsv.x = LerpHue(referenceHsv.x, hsv.x, saturate(spread));

                return HsvToRgb(hsv);
            }

            half4 GrassFragment(Varyings input) : SV_Target
            {
                float accent = input.blade.x;

                float2 uv = BladeUV(input.uv, input.blade.y);

                // --- 0. 잎의 윤곽 ---
                // 그림은 흰 실루엣입니다. 색은 전부 아래에서 정해집니다.
                clip(BladeAlpha(uv, accent) - _Cutoff);

                float2 rootXZ = input.rootWS.xz;

                // --- 1. 고도 ---
                // 지형과 같은 식입니다. 고지대의 풀은 마릅니다.
                // 다만 <b>끝까지 마르게 두지는 않습니다</b> — 아래에서 얹을 얼룩이 묻힙니다.
                float altitude = saturate((input.rootWS.y - _SeaLevel) / max(_HeightRange, 1e-3));
                float dryness = smoothstep(0.35, 0.95, altitude) * _DryStrength;

                // 이 자리의 <b>기준 색</b>입니다. 아래의 모든 변주가 이것을 흔든 결과여야 합니다.
                float3 baseTone = lerp(_BaseColor.rgb, _DryColor.rgb, dryness);

                // --- 2. 노이즈 얼룩 ---
                float3 albedo = PatchColor(rootXZ, baseTone);

                // 얼룩이 벌려 놓은 색조를 기준 쪽으로 좁힙니다.
                // 밝기와 채도의 차이는 그대로 남으므로 얼룩 자체는 사라지지 않습니다.
                albedo = ConstrainHue(albedo, baseTone, _HueSpread);

                // 남은 차이를 기준 색 쪽으로 당깁니다.
                // 얼룩을 지우지 않고 <b>묶는</b> 한 개의 손잡이입니다.
                albedo = lerp(albedo, baseTone, _ColorCohesion);

                // --- 3. 밝기 흔들림 ---
                //
                // <b>무리가 주, 포기가 종입니다.</b>
                //
                // 예전에는 포기마다 독립적인 해시 하나로만 흔들었습니다. 그것은 흰 노이즈라
                // 이웃한 포기가 서로 무관하게 밝고 어두워지고, 들판이 자글자글하게 갈라집니다.
                // 자연의 들판은 포기가 아니라 <b>무리</b> 단위로 밝기가 갈립니다 —
                // 볕이 든 자리와 그늘진 자리가 여러 포기를 한꺼번에 덮습니다.
                //
                // 큰 몫을 저주파 노이즈에 주고 포기 단위는 결을 넣는 정도로만 남깁니다.
                float cluster = Fbm(rootXZ * _ClusterScale) - 0.5;
                float speckle = Hash21(rootXZ * 7.31) - 0.5;

                albedo *= 1.0 + cluster * _ClusterJitter + speckle * _ColorJitter;

                // --- 4. 잎끝 ---
                // 끝으로 갈수록 밝습니다. 빛을 더 받는 자리이기도 하고,
                // 이것이 있어야 잎이 낱장으로 읽힙니다.
                albedo = lerp(albedo, _TipColor.rgb, input.uv.y * _TipBlend);

                // --- 5. 강조풀 ---
                // 얼룩도 고도도 건너뜁니다. 예외는 예외로 보여야 합니다.
                albedo = lerp(albedo, _AccentColor.rgb, accent);

                // --- 6. 밑동 ---
                // 발치를 눌러 지면에 앉힙니다. 복셀 주변 차폐가 만들어 줄 그늘의 대역입니다.
                float grounding = lerp(1.0 - _RootShade, 1.0, saturate(input.uv.y * 2.2));

                // --- 7. 조명 ---
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // <b>잎을 둥근 것처럼 칠합니다.</b>
                //
                // 예전에는 위쪽 하나를 노멀로 썼습니다. 시점과 무관하다는 장점은 있었지만,
                // 그 값은 들판 전체에서 <b>완전히 같습니다</b> — 모든 포기가 한 밝기가 되고,
                // 그 위에 툰 밴딩이 걸리면 들판이 한 장의 금속판처럼 읽힙니다.
                //
                // 실제 잎은 평면이 아니라 살짝 말린 띠입니다. 폭 방향으로 노멀을 굴려 주면
                // 한 포기 안에서 명암이 돌고, 포기마다 굴리는 방향이 다르면 들판에 결이 생깁니다.
                //
                // <b>굴리는 방향은 뿌리에서 뽑습니다.</b>
                // 잎의 실제 가로축은 빌보드라 카메라를 따라 돕니다. 그것으로 칠하면
                // 카메라를 돌릴 때마다 들판 전체의 밝기가 출렁입니다.
                // 자리에서 뽑은 방향은 월드에 고정되어 있어 시점이 바뀌어도 명암이 가만히 있습니다.
                // 기하는 빌보드로 돌되 <b>칠하기는 제자리에 선 잎인 척</b>하는 것입니다.
                float bladeYaw = Hash21(rootXZ * 3.17) * 6.2831853;

                float3 bladeSide = float3(cos(bladeYaw), 0.0, sin(bladeYaw)) * _NormalScatter;

                // uv.x 가 가운데에서 멀수록 옆을 봅니다. 이것이 잎을 둥글게 보이게 합니다.
                float across = (input.uv.x - 0.5) * 2.0;

                float3 normalWS = float3(0.0, 1.0, 0.0) + bladeSide * (across * _NormalRound);

                // 끝으로 갈수록 다시 위를 봅니다. 잎끝은 하늘을 향합니다.
                normalWS = normalize(lerp(normalWS, float3(0.0, 1.0, 0.0), input.uv.y * _NormalTipUp));

                float lambert = dot(normalWS, mainLight.direction) * 0.5 + 0.5;

                // <b>뿌리 자리</b>로 구름 그늘을 잽니다.
                // 정점 위치로 재면 한 포기 안에서 그늘이 갈라지고, 바람에 휠 때마다 깜빡입니다.
                float clouds = CloudShadow(input.rootWS, mainLight.direction);

                float shading = ToonLight(
                    lambert,
                    mainLight.shadowAttenuation,
                    clouds,
                    _AmbientBoost,
                    _ShadowDepth);

                float3 lit = albedo * shading * grounding * mainLight.color;

                // --- 8. 투과광 ---
                //
                // <b>잎은 얇아서 빛이 뒤로 새어 나옵니다.</b>
                //
                // 이것이 없으면 해를 등진 풀이 그늘 속 풀과 똑같이 어둡습니다.
                // 실제로는 뒤에서 빛을 받은 잎이 스스로 밝게 빛나고, 앞뒤의 그 대비가
                // 식물을 <b>식물로</b> 보이게 합니다. 투과가 없는 것이 식생을
                // 플라스틱이나 금속처럼 보이게 하는 가장 큰 이유입니다.
                //
                // 보는 방향이 빛의 진행 방향과 가까울수록, 즉 해를 등지고 볼수록 세게 빛납니다.
                float3 viewDir = normalize(_WorldSpaceCameraPos - input.positionWS);

                float backlight = saturate(dot(viewDir, -mainLight.direction));
                float transmission = pow(backlight, _TranslucencyPower) * _Translucency;

                // 밑동은 포기가 겹치고 두꺼워 빛이 덜 통과합니다. 끝동이 가장 환합니다.
                transmission *= lerp(_TranslucencyRoot, 1.0, input.uv.y);

                // 그늘에 들면 통과할 빛 자체가 없습니다. 구름 그늘도 마찬가지입니다.
                transmission *= mainLight.shadowAttenuation * clouds;

                lit += _TranslucencyColor.rgb * transmission * mainLight.color;

                return half4(lit, 1.0);
            }
            ENDHLSL
        }

        // ================================================================================================
        // Pass 2 — 깊이와 노멀
        //
        // 없으면 화면 공간 주변 차폐가 풀밭을 건너뛰고, 폴백이 대신 채워 주면
        // <b>휘지 않은 곧은 판</b>의 깊이가 들어가 잎이 선 자리와 어긋납니다.
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

            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 blade      : TEXCOORD1;
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);

                BladeState blade = ResolveBlade();

                output.positionCS = TransformWorldToHClip(BladeWorldPosition(input.positionOS.xyz, blade));
                output.uv = input.uv;
                output.blade = float2(blade.accent, blade.perspective);

                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                // 본체 패스와 <b>같은 자리를 같은 기준으로</b> 잘라야 합니다.
                // 여기서만 통째로 그리면 깊이 버퍼에 잎이 아니라 사각형이 남습니다.
                float2 uv = BladeUV(input.uv, input.blade.y);
                clip(BladeAlpha(uv, input.blade.x) - _Cutoff);

                // 본체 패스와 같이 <b>위</b>를 씁니다. 풀과 그 아래 땅이 같은 차폐를 받아야 합니다.
                return half4(0.0, 1.0, 0.0, 0.0);
            }
            ENDHLSL
        }

        // ================================================================================================
        // Pass 3 — 깊이만
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

            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 blade      : TEXCOORD1;
            };

            Varyings DepthVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);

                BladeState blade = ResolveBlade();

                output.positionCS = TransformWorldToHClip(BladeWorldPosition(input.positionOS.xyz, blade));
                output.uv = input.uv;
                output.blade = float2(blade.accent, blade.perspective);

                return output;
            }

            half4 DepthFragment(Varyings input) : SV_Target
            {
                float2 uv = BladeUV(input.uv, input.blade.y);
                clip(BladeAlpha(uv, input.blade.x) - _Cutoff);

                return 0;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
