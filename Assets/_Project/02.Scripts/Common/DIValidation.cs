using UnityEngine;

namespace SRPG.Common
{
    /// <summary>
    /// 인스펙터로 주입받는 필수 참조가 비어 있는지를 <b>기동 시점에</b> 확인하는 헬퍼입니다.
    ///
    /// <b>왜 필요한가</b>
    ///
    /// 컨테이너에 등록할 컴포넌트를 인스펙터에서 연결하는 방식은, 연결을 빠뜨려도 컴파일이 통과합니다.
    /// 그 결과는 두 가지 중 하나입니다 — 컨테이너 조립이 알 수 없는 예외로 실패하거나,
    /// 조용히 조립된 뒤 한참 지나 엉뚱한 자리에서 NullReference가 납니다.
    /// 둘 다 원인에서 멀리 떨어진 곳에서 증상이 나타납니다.
    ///
    /// 여기서 한 번 훑어 두면 "어느 설치자의 어느 필드가 비었는가"가 첫 줄에 나옵니다.
    /// </summary>
    public static class DIValidation
    {
        /// <summary>
        /// 필수 참조가 비어 있으면 오류를 남기고 false를 돌려줍니다.
        ///
        /// 조립을 <b>중단시키지는 않습니다.</b> 참조 하나가 비었다고 게임 전체를 세우면
        /// 나머지가 함께 비었는지를 한 번에 알 수 없어, 고치고 실행하기를 반복하게 됩니다.
        /// 비어 있는 것을 전부 나열한 뒤 실패하는 편이 한 번에 고치기 쉽습니다.
        /// </summary>
        /// <param name="owner">이 참조를 들고 있는 설치자입니다. 로그를 클릭하면 이것이 선택됩니다.</param>
        /// <param name="reference">확인할 참조입니다.</param>
        /// <param name="fieldName">필드 이름입니다. <c>nameof(field)</c>를 넘기십시오.</param>
        /// <returns>참조가 살아 있으면 true입니다.</returns>
        public static bool RequireRef(Object owner, Object reference, string fieldName)
        {
            if (reference != null)
            {
                return true;
            }

            string ownerName = owner != null ? owner.GetType().Name : "UnknownInstaller";

            Debug.LogError(
                $"[{ownerName}] 필수 참조 '{fieldName}'가 인스펙터에 연결되지 않았습니다. " +
                "이대로는 컨테이너 조립이 실패하거나 런타임에 NullReference가 납니다.", owner);

            return false;
        }
    }
}
