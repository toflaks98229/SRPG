namespace SRPG.Common
{
    /// <summary>
    /// 전투 전반에서 공유하는 상수입니다.
    /// </summary>
    public static class CombatConstants
    {
        /// <summary>
        /// 분대가 도달할 수 있는 최고 랭크입니다.
        /// 궁수의 조준 산포가 이 값을 기준으로 보간되므로, 랭크 상한을 바꾸면 명중률 곡선 전체가 바뀝니다.
        /// </summary>
        public const int MaxRank = 5;

        /// <summary>분대의 시작 랭크입니다.</summary>
        public const int MinRank = 1;
    }
}
