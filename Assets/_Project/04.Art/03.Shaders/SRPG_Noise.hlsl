#ifndef SRPG_NOISE_INCLUDED
#define SRPG_NOISE_INCLUDED

// 물과 풀이 함께 쓰는 절차적 노이즈입니다.
//
// <b>왜 텍스처를 쓰지 않는가</b>
//
// 노이즈 텍스처를 두면 에셋이 하나 더 늘고, 프로젝트가
// "에셋 없이도 실행 가능"을 유지하지 못합니다. 해시 하나면 충분합니다.
//
// <b>왜 사인파가 아닌가</b>
//
// 사인파를 겹쳐 무늬를 만들면 반드시 주기가 남고, 눈은 그 주기를 격자로 읽습니다.
// 물에서 이미 한 번 겪었습니다 — 먼바다가 바둑판으로 깔렸습니다.
// 해시로 만든 값 노이즈에는 그런 주기가 없습니다.

float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);

    return frac(p.x * p.y);
}

// 값과 <b>기울기</b>를 함께 냅니다. x = 값, yz = 기울기.
//
// <b>왜 기울기를 함께 내는가</b>
//
// 물의 잔결은 노이즈를 색에 더하는 것이 아니라 <b>노멀을 기울이는</b> 데 씁니다.
// 그러려면 기울기가 필요한데, 유한 차분으로 구하면 표본이 서너 배로 늘어납니다.
// 값 노이즈의 도함수는 <b>이미 손에 든 네 모서리 값에서 곧바로</b> 나오므로 거의 공짜입니다.
//
// <b>왜 5차 보간인가</b>
//
// 예전에는 3차(<c>smoothstep</c>)를 썼습니다. 값은 매끄럽지만 <b>기울기가 셀 경계에서 꺾입니다.</b>
// 색으로만 쓸 때는 눈에 띄지 않았는데, 기울기를 노멀에 먹이고 그것을 다시
// 날카로운 반사광(<c>pow(..., 220)</c>)에 통과시키면 그 꺾임이 <b>축에 정렬된 격자</b>로 드러납니다.
// 실제로 그렇게 보였습니다 — 먼바다가 촘촘한 타일처럼 깔렸습니다.
// 5차는 도함수까지 이어지므로 경계가 사라집니다.
float3 ValueNoiseD(float2 p)
{
    float2 cell = floor(p);
    float2 f    = frac(p);

    // 5차 보간과 그 도함수입니다.
    float2 u  = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float2 du = 30.0 * f * f * (f * (f - 2.0) + 1.0);

    float a = Hash21(cell);
    float b = Hash21(cell + float2(1, 0));
    float c = Hash21(cell + float2(0, 1));
    float d = Hash21(cell + float2(1, 1));

    // 겹선형 보간을 다항식으로 펼쳐 두면 값과 도함수가 같은 계수를 나눠 씁니다.
    float k1 = b - a;
    float k2 = c - a;
    float k3 = a - b - c + d;

    float value = a + k1 * u.x + k2 * u.y + k3 * u.x * u.y;

    float2 gradient = du * float2(k1 + k3 * u.y,
                                  k2 + k3 * u.x);

    return float3(value, gradient);
}

float ValueNoise(float2 p)
{
    return ValueNoiseD(p).x;
}

// 옥타브마다 <b>회전</b>시켜 겹칩니다.
// 배율만 키우면 옥타브들이 같은 축에 정렬돼 결국 격자가 다시 드러납니다.
//
// 기울기도 함께 누적합니다. 옥타브마다 좌표를 돌렸으므로,
// 그 옥타브의 기울기는 <b>돌아간 좌표계의 것</b>입니다.
// 원래 좌표계로 되돌리려면 지금까지 곱해 온 회전을 그대로 다시 곱해 줘야 합니다(연쇄 법칙).
// 이것을 빠뜨리면 기울기가 옥타브마다 다른 방향을 가리켜, 노멀이 엉뚱하게 흔들립니다.
float3 FbmD(float2 p)
{
    const float2x2 rot = float2x2(1.6, 1.2, -1.2, 1.6);

    float2x2 accumulated = float2x2(1, 0, 0, 1);

    float  sum      = 0.0;
    float2 gradient = 0.0;
    float  amp      = 0.5;

    for (int i = 0; i < 3; i++)
    {
        float3 n = ValueNoiseD(p);

        sum      += n.x * amp;
        gradient += mul(n.yz, accumulated) * amp;

        p           = mul(rot, p);
        accumulated = mul(accumulated, rot);
        amp        *= 0.5;
    }

    return float3(sum, gradient);
}

float Fbm(float2 p)
{
    return FbmD(p).x;
}

// 방향 벡터를 각도(도)만큼 돌립니다.
//
// <b>이름을 조심해야 합니다.</b>
// <c>vector</c> 는 HLSL 의 예약된 자료형이고 <c>degrees</c>·<c>radians</c> 는 내장 함수입니다.
// 인자 이름으로 쓰면 "unexpected token" 이라는, 원인을 알아보기 어려운 오류가 납니다.
// 이 파일의 <c>point</c> 주석과 같은 이야기입니다.
float2 RotateVec2(float2 direction, float angleDegrees)
{
    float angle = angleDegrees * (3.14159265 / 180.0);

    float c = cos(angle);
    float s = sin(angle);

    return float2(direction.x * c - direction.y * s,
                  direction.x * s + direction.y * c);
}

// ====================================================================================================
// 어긋나게 흐르는 두 겹 노이즈
//
// <b>왜 두 겹을 곱하는가</b>
//
// 한 겹만 흘리면 무늬가 통째로 한 방향으로 미끄러지는 것이 눈에 띕니다.
// 배율이 다른 두 겹을 곱하면 밝은 자리는 <b>둘 다 밝을 때만</b> 밝아지므로,
// 어느 겹의 주기와도 다른 주기의 무늬가 생깁니다.
//
// <b>왜 방향까지 어긋내는가</b>
//
// 두 겹을 같은 방향으로 흘리면 배율만 다를 뿐 결국 함께 미끄러집니다.
// 나아가는 방향을 좌우로 조금씩 벌려 두면 두 겹이 서로 <b>스쳐 지나가고</b>,
// 그때 무늬가 생겼다 사라지는 것처럼 보입니다. 구름과 바람이 둘 다 이것을 씁니다.
//
// 출처: Dylearn 3D Pixel Art Grass Demo (MIT). 09.Docs/ATTRIBUTION.md 를 보십시오.
// ====================================================================================================

/// <param name="position">노이즈를 뽑을 평면 좌표입니다. 보통 월드의 xz 입니다.</param>
/// <param name="direction">무늬가 흘러가는 방향입니다. 정규화하지 않아도 됩니다.</param>
/// <param name="scale">노이즈의 배율입니다. 작을수록 무늬가 커집니다.</param>
/// <param name="drift">지금까지 흘러온 거리입니다. 보통 시각 × 속도입니다.</param>
/// <param name="divergeDegrees">두 겹이 갈라지는 각도입니다. 0 이면 함께 흐릅니다.</param>
/// <returns>0~1 근처의 값입니다. 곱이라 가운데로 몰립니다.</returns>
///
/// <remarks>
/// <b>시각을 안에서 읽지 않고 밖에서 받습니다.</b>
///
/// 풀은 흔들림을 초당 몇 번으로 <b>끊어서</b> 씁니다. 이 함수가 안에서
/// <c>_TimeParameters</c> 를 읽으면 바람만 매끄럽게 흘러, 애써 끊어 놓은 박자가
/// 바람에서만 풀립니다. 부르는 쪽이 자기 시각을 정해 넘기게 둡니다.
/// </remarks>
float DivergentNoise(float2 position, float2 direction, float scale, float drift, float divergeDegrees)
{
    float2 flow = normalize(direction + 1e-5);

    float2 flowA = RotateVec2(flow, divergeDegrees);
    float2 flowB = RotateVec2(flow, -divergeDegrees);

    // 두 번째 겹은 배율과 속도를 함께 어긋냅니다.
    // 한쪽만 어긋내면 언젠가 두 겹의 주기가 맞아떨어져 무늬가 겹칩니다.
    float layerA = Fbm(position * scale + flowA * drift);
    float layerB = Fbm(position * (scale * 0.8) + flowB * (drift * 0.93));

    return layerA * layerB * 2.6;
}

// ====================================================================================================
// 구름 그림자
//
// <b>왜 전역인가</b>
//
// 구름은 머티리얼의 성질이 아니라 <b>장면의 성질</b>입니다.
// 머티리얼마다 값을 두면 언젠가 하나가 어긋나고, 그때부터 지형은 그늘인데
// 그 위의 풀은 볕을 받습니다. 값을 한 곳에 두고 모두가 같은 함수를 부릅니다.
//
// <b>왜 그냥 XZ에 칠하지 않는가</b>
//
// 그림자는 구름이 빛을 가려 생깁니다. 그러니 지면의 한 점에서 <b>광원 쪽으로 쏘아</b>
// 구름 높이에 닿은 자리의 구름 농도를 봐야 합니다.
// XZ에 그대로 칠하면 언덕 위와 골짜기가 같은 그늘을 받고, 해가 기울어도 그림자가 안 늘어납니다.
// ====================================================================================================

// x = 노이즈 배율, y = 흐르는 속도, z = 그늘의 깊이, w = 구름이 떠 있는 높이
float4 _CloudParams;

// xy = 흐르는 방향, z = 구름이 덮는 정도, w = 가장자리의 부드러움
float4 _CloudFlow;

// 이 자리가 구름 그늘에 얼마나 들었는지입니다. 1이 볕, 작을수록 그늘입니다.
//
// 빛의 방향은 셰이더가 쓰는 <see cref="Light"/>에서 그대로 받습니다.
// 전역으로 또 두면 조명과 그림자가 서로 다른 해를 볼 수 있습니다.
float CloudShadow(float3 positionWS, float3 lightDirectionWS)
{
    // 구름이 꺼져 있으면 아무 일도 하지 않습니다.
    if (_CloudParams.z <= 0.0)
    {
        return 1.0;
    }

    // 해가 지평선에 가까우면 나눗셈이 발산합니다. 그때는 그림자가 무한히 늘어지는 대신 멈춥니다.
    float upward = max(lightDirectionWS.y, 0.15);

    float distance = (_CloudParams.w - positionWS.y) / upward;
    float3 hit = positionWS + lightDirectionWS * distance;

    // 구름이 갈라지는 각도입니다.
    //
    // 미술 손잡이가 아니라 <b>주기를 감추기 위한 수</b>라서 상수로 둡니다.
    // 너무 크게 벌리면 두 겹이 각자 다른 방향으로 흘러가는 것이 오히려 눈에 띕니다.
    const float divergeDegrees = 14.0;

    float drift = _TimeParameters.x * _CloudParams.y;

    float density = DivergentNoise(hit.xz, _CloudFlow.xy, _CloudParams.x, drift, divergeDegrees);

    float shade = smoothstep(
        _CloudFlow.z - _CloudFlow.w,
        _CloudFlow.z + _CloudFlow.w,
        density);

    // <b>완전히 어둡게 두지 않습니다.</b>
    // 지형은 고도·경사·물가로 갈 수 있는 곳을 색으로 말하고 있습니다.
    // 지나가는 구름이 그 판독을 덮으면 연출을 얻고 게임을 잃습니다.
    return lerp(1.0, 1.0 - _CloudParams.z, shade);
}

#endif
