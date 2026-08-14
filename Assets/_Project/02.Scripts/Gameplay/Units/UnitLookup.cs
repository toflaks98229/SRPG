using UnityEngine;

namespace SRPG.Gameplay.Units
{
    /// <summary>
    /// 콜라이더에서 <see cref="Unit"/>을 찾습니다.
    ///
    /// <b>왜 별도 함수인가</b>
    ///
    /// 물리 기반 전투에서 이 조회는 가장 뜨거운 자리에 있습니다.
    /// 근접 무기는 판정 구간 <b>매 프레임</b> 걸린 콜라이더 전부에 대해 이걸 부르고,
    /// 화살은 명중할 때마다 부릅니다. 전투가 격해질수록 호출 수가 제곱으로 늘어납니다.
    ///
    /// 예전에는 <c>GetComponentInParent</c> 를 직접 불렀습니다.
    /// 이 함수는 계층을 <b>위로 거슬러 올라가며</b> 각 단계에서 컴포넌트를 찾습니다.
    /// 그런데 이 프로젝트에서 몸체 콜라이더는 <b>항상 Unit과 같은 오브젝트</b>에 붙습니다.
    /// (<c>Unit.EnsureBodyCollider</c>가 그렇게 만들고, 프리팹 빌더도 루트에 답니다.
    ///  <c>BuiltAssetIntegrityTests.유닛_프리팹에_Unit_컴포넌트와_콜라이더가_있다</c>가 이를 고정합니다)
    /// 즉 거의 모든 호출이 0단계에서 끝나는데도 매번 계층 순회 경로를 탑니다.
    ///
    /// 그래서 같은 오브젝트를 먼저 봅니다(<c>TryGetComponent</c>, 할당 없음).
    /// 그래도 못 찾으면 예전 경로로 넘어갑니다. 나중에 별도 히트박스 자식을 쓰는 프리팹이 생겨도
    /// 조용히 안 맞는 일이 없도록 남겨 둡니다.
    ///
    /// 조회 결과를 사전에 캐시하는 방법도 있지만 쓰지 않았습니다.
    /// 유닛이 초당 수십 번 생성·소멸하므로 사전을 최신으로 유지하는 비용과
    /// 파괴된 항목이 남는 위험이, 여기서 아끼는 것보다 큽니다.
    /// </summary>
    public static class UnitLookup
    {
        /// <summary>
        /// 콜라이더가 속한 유닛을 찾습니다.
        /// </summary>
        /// <param name="collider">물리 질의에 걸린 콜라이더입니다. null이어도 됩니다.</param>
        /// <returns>콜라이더가 속한 유닛입니다. 유닛이 아니면(지형 등) null입니다.</returns>
        public static Unit FromCollider(Collider collider)
        {
            if (collider == null)
            {
                return null;
            }

            // 빠른 경로: 콜라이더와 Unit이 같은 오브젝트에 있습니다. 사실상 모든 경우가 여기서 끝납니다.
            if (collider.TryGetComponent(out Unit unit))
            {
                return unit;
            }

            // 폴백: 콜라이더가 자식에 달린 계층입니다.
            return collider.GetComponentInParent<Unit>();
        }
    }
}
