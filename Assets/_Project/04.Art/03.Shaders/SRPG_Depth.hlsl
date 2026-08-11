#ifndef SRPG_DEPTH_INCLUDED
#define SRPG_DEPTH_INCLUDED

// ====================================================================================================
// <b>투영에 따라 답이 갈리는 것들</b>을 모아 둔 자리입니다.
//
// 깊이와 시선 방향이 여기 함께 있는 이유는 <b>같은 함정</b>이기 때문입니다 —
// 원근에서 맞던 식이 직교에서 조용히 틀립니다. 오류는 나지 않고 화면만 이상해집니다.
// 실제로 세 번 겪었습니다: 물의 수심, 외곽선의 실루엣, 그리고 수면의 금속성 반사.
// 한곳에 모아 두면 네 번째를 찾을 때 여기부터 보게 됩니다.
//
// <b>왜 공용으로 떼어 냈는가</b>
//
// 물과 외곽선이 각자 LinearEyeDepth 를 부르고 있었습니다. 그 함수는 <b>원근 전용</b>이고,
// 전투 카메라를 직교로 옮기는 순간 둘 다 동시에 틀렸습니다.
// 증상은 서로 달라 보였습니다 — 한쪽은 바다 거품이 뚝 끊겼고 다른 쪽은 외곽선이 사라졌습니다.
// 원인이 하나인데 자리가 둘이면, 한 곳만 고치고 나머지를 몇 주 뒤에 다시 찾게 됩니다.
//
// <b>원근 경로를 남겨 둔 이유</b>
//
// 게임 카메라는 직교이지만 씬 뷰와 미리보기 카메라는 원근일 수 있습니다.
// 편집 중에만 화면이 깨지는 것은 찾기 가장 나쁜 종류의 고장입니다.
// ====================================================================================================

// 깊이 버퍼의 원시 값을 눈에서 잰 거리로 바꿉니다.
//
// <b>직교에서는 깊이 버퍼가 이미 선형입니다.</b> 근평면에서 원평면까지 고르게 늘어서 있으므로
// 근·원 사이를 그대로 보간하면 됩니다. 원근의 역수 공식을 그대로 쓰면
// 값이 통째로 어긋나고, 오류는 나지 않습니다.
//
// <c>unity_OrthoParams.w</c> 가 직교일 때 1 입니다. 분기 대신 섞는 것은
// 두 경로가 모두 유효한 수라 분기 예측을 흔들 이유가 없기 때문입니다.
float SrpgLinearEyeDepth(float rawDepth)
{
#if UNITY_REVERSED_Z
    // 뒤집힌 깊이에서는 근평면이 1 입니다. 0에서 1로 되돌려 보간에 씁니다.
    float ortho01 = 1.0 - rawDepth;
#else
    float ortho01 = rawDepth;
#endif

    float orthoEye = lerp(_ProjectionParams.y, _ProjectionParams.z, ortho01);
    float perspEye = LinearEyeDepth(rawDepth, _ZBufferParams);

    return lerp(perspEye, orthoEye, unity_OrthoParams.w);
}

// 이 월드 좌표가 눈에서 얼마나 떨어져 있는지입니다.
//
// <b>화면 좌표의 w 를 쓰면 안 됩니다.</b>
// 원근에서는 그것이 시점 깊이와 같지만, <b>직교에서는 클립 w 가 언제나 1</b> 입니다.
// 그래서 직교로 옮기는 순간 "수면이 카메라에서 1미터 앞에 있다"가 되어
// 수심이 전부 같은 값으로 뭉갭니다.
//
// 시점 공간으로 직접 옮기면 두 투영에서 모두 맞습니다.
// 유니티의 시점 공간은 카메라가 -Z 를 보므로 앞에 있는 것이 음수이고, 부호를 뒤집어 거리로 씁니다.
float SrpgEyeDepthFromWorld(float3 positionWS)
{
    return -TransformWorldToView(positionWS).z;
}

// 이 자리에서 <b>관객을 향하는</b> 방향입니다.
//
// <b>직교에서는 화면 전체가 같은 방향으로 보입니다.</b>
// 그런데 <c>normalize(_WorldSpaceCameraPos - positionWS)</c> 는 카메라 <b>점</b>에서
// 부챗살처럼 퍼지는 방향을 줍니다 — 원근의 거동입니다.
//
// 그것을 직교에서 쓰면 카메라 발밑을 중심으로 한 <b>둥근 얼룩</b>이 생깁니다.
// 수면에서는 그것이 하늘빛과 반짝임의 세기를 자리마다 바꿔,
// 카메라를 옮길 때마다 얼룩이 따라 미끄러집니다. <b>물이 아니라 닦아 놓은 금속</b>처럼 보입니다.
//
// <c>UNITY_MATRIX_V</c> 의 세 번째 행이 카메라의 뒤쪽 축, 곧 관객을 향하는 방향입니다.
float3 SrpgViewDirection(float3 positionWS)
{
    float3 persp = normalize(_WorldSpaceCameraPos - positionWS);
    float3 ortho = UNITY_MATRIX_V._m20_m21_m22;

    return normalize(lerp(persp, ortho, unity_OrthoParams.w));
}

#endif // SRPG_DEPTH_INCLUDED
