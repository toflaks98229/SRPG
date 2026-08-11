#ifndef SRPG_TOON_INCLUDED
#define SRPG_TOON_INCLUDED

// 지형·물·풀·유닛이 함께 쓰는 계단식 명암입니다.
//
// <b>왜 계단으로 끊는가</b>
//
// 매끄러운 램버트는 밝기를 연속으로 깝니다. 눈은 연속된 밝기에서 <b>경계를 못 읽습니다</b> —
// 어디까지가 볕이고 어디부터 그늘인지가 흐릿합니다.
// 명암을 서너 단으로 끊으면 그 경계가 선으로 드러나고, 지형의 굴곡이 형태로 읽힙니다.
// 이것이 그림체의 문제이기 이전에 <b>판독의 문제</b>입니다.
//
// <b>왜 완전한 계단이 아닌가</b>
//
// 순수한 계단은 경계에서 픽셀이 딱 갈립니다. 지형처럼 넓고 완만한 면에서는
// 그 선이 화면을 가로지르며 <b>카메라가 움직일 때마다 출렁입니다</b>.
// 경계에만 좁은 그라디언트를 두면 계단은 계단대로 보이면서 그 출렁임이 사라집니다.
// 원본이 hybrid toon shading 이라 부르는 것이 이것입니다.
//
// <b>왜 전역인가</b>
//
// 명암의 단 수는 머티리얼의 성질이 아니라 <b>화면 전체의 그림체</b>입니다.
// 구름과 같은 이유입니다 — 머티리얼마다 값을 두면 언젠가 하나가 어긋나고,
// 그때부터 지형은 세 단인데 그 위의 풀만 다섯 단이 됩니다.
// 한 곳에서 올리고 네 셰이더가 같은 함수를 부릅니다.
//
// 출처: Dylearn 3D Pixel Art Grass Demo (MIT). 09.Docs/ATTRIBUTION.md 를 보십시오.

// x = 계단 수, y = 치우침, z = 기울기, w = 경계 그라디언트의 폭
float4 _ToonParams;

// 빛의 양을 계단으로 끊습니다. 들어오고 나가는 값 모두 0~1 입니다.
float ToonRamp(float lightAmount)
{
    float cuts = _ToonParams.x;

    // 전역이 아직 안 올라왔으면 예전처럼 매끄럽게 둡니다.
    // 머티리얼 미리보기나 전장 없는 씬에서 화면이 새까매지면 안 됩니다.
    if (cuts < 1.0)
    {
        return saturate(lightAmount);
    }

    // 치우침은 명암의 중심을 옮기고, 기울기는 계단이 몰리는 정도를 정합니다.
    float amount = saturate((lightAmount + _ToonParams.y) * _ToonParams.z);

    float cut = 1.0 / cuts;

    // 다음 계단으로 올립니다.
    float stepped = saturate(ceil(amount * cuts) * cut);

    float band = _ToonParams.w;

    if (band <= 0.0)
    {
        return stepped;
    }

    // --- 경계에만 그라디언트를 둡니다 ---
    //
    // 지금 값에서 <b>가장 가까운</b> 계단 경계를 찾고, 그 둘레에서만 부드럽게 넘깁니다.
    // 경계에서 먼 자리는 blend 가 0 이나 1 로 붙어 계단 그대로 남습니다.
    float nearest = floor(amount / cut + 0.5);
    float threshold = nearest * cut;

    float halfWidth = 0.5 * cut * band;

    float low = max(0.0, threshold - halfWidth);
    float high = min(1.0, threshold + halfWidth);

    // 경계가 0 이나 1 에 붙어 폭이 사라지면 그냥 끊습니다.
    float blend = high > low
        ? smoothstep(low, high, amount)
        : step(threshold, amount);

    return saturate(lerp(threshold, min(threshold + cut, 1.0), blend));
}

// 한 자리가 실제로 받는 밝기입니다.
//
// <b>왜 곱하지 않고 한 번에 접는가</b>
//
// 예전에는 명암·그림자·구름을 각각 구해 곱했습니다. 곱하면 계단이 <b>세 번</b> 생깁니다 —
// 그늘 속의 구름 밑에서 계단이 겹쳐 보이지 않는 단이 생기고, 어디가 무엇 때문에
// 어두운지 읽을 수 없게 됩니다.
// 셋을 먼저 하나의 "빛의 양"으로 접은 뒤에 한 번만 끊으면 계단이 한 벌만 남습니다.
//
// <param name="lambert">위쪽으로 접은 램버트입니다. 0~1 입니다.</param>
// <param name="shadowAttenuation">그림자 감쇠입니다. 1 이 볕, 0 이 그늘입니다.</param>
// <param name="cloud">구름 그늘입니다. <c>CloudShadow</c> 가 준 1 이하의 값입니다.</param>
// <param name="ambientBoost">빛을 등진 면의 밝기 하한입니다.</param>
// <param name="shadowDepth">그림자가 빛을 덜어 내는 깊이입니다.</param>
float ToonLight(float lambert, float shadowAttenuation, float cloud, float ambientBoost, float shadowDepth)
{
    // 그림자는 빛의 양에서 덜어 냅니다.
    float amount = lambert - shadowDepth * (1.0 - shadowAttenuation);

    // 구름도 <b>같은 방식으로</b> 덜어 냅니다. 구름 그늘과 드리운 그림자는
    // 물리적으로 같은 것이기 때문입니다 — 해를 가린 것뿐입니다.
    //
    // <b>예전에는 상한이었습니다.</b> min(amount, cloud) 로 두면 구름 밑에서
    // 밝기의 천장이 내려옵니다. 가장자리가 또렷해지는 장점이 있어 그렇게 두었는데,
    // <b>천장보다 어두운 면에는 아무 일도 일어나지 않는다</b>는 것을 놓쳤습니다.
    //
    // 깊이 0.28 이면 천장이 0.72 입니다. 해를 정면으로 받는 비탈만 그 위에 있고,
    // 전장의 대부분은 그보다 어두워서 구름이 지나가도 화면이 그대로였습니다.
    // "구름이 있긴 한가"로 보이던 이유가 이것입니다.
    //
    // 덜어 내면 모든 면이 같은 만큼 어두워지고, 계단은 아래의 ToonRamp 가 끊습니다 —
    // 또렷함을 상한이 아니라 <b>양자화</b>가 만들게 두는 편이 맞습니다.
    amount -= (1.0 - cloud);

    // 하한 위로 폅니다. 빛을 등져도 여기보다 어두워지지 않습니다 —
    // 그늘에 든 부대가 안 보이면 연출을 얻고 게임을 잃습니다.
    return lerp(ambientBoost, 1.0, ToonRamp(amount));
}

#endif
