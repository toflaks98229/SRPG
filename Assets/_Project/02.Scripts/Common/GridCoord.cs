using System;

namespace SRPG.Common
{
    /// <summary>
    /// 섬 그리드 상의 정수 좌표입니다.
    /// 월드 좌표(Vector3)와의 변환은 <c>IslandGrid</c>가 담당하며, 이 구조체는 순수한 격자 좌표만 표현합니다.
    /// </summary>
    [Serializable]
    public struct GridCoord : IEquatable<GridCoord>
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>격자의 열 인덱스입니다.</summary>
        public int X;

        /// <summary>격자의 행 인덱스입니다.</summary>
        public int Y;

        // ====================================================================================================
        // 2. Constants
        // ====================================================================================================

        /// <summary>유효하지 않은 좌표를 나타내는 값입니다. 경로 탐색 실패 등을 표현할 때 사용합니다.</summary>
        public static readonly GridCoord Invalid = new GridCoord(int.MinValue, int.MinValue);

        /// <summary>
        /// 대각선 한 걸음의 비용입니다. √2 입니다.
        ///
        /// 대각선을 1로 두면 안 됩니다. 그러면 대각선이 공짜가 되어
        /// 경로 탐색이 직선으로 갈 수 있는 곳도 지그재그로 돌아갑니다.
        /// </summary>
        public const float DiagonalStepCost = 1.41421356f;

        /// <summary>상하좌우 4방향 오프셋입니다. 이동 가능 방향의 기본 집합입니다.</summary>
        public static readonly GridCoord[] Neighbors4 =
        {
            new GridCoord(1, 0),
            new GridCoord(-1, 0),
            new GridCoord(0, 1),
            new GridCoord(0, -1),
        };

        /// <summary>대각선을 포함한 8방향 오프셋입니다.</summary>
        public static readonly GridCoord[] Neighbors8 =
        {
            new GridCoord(1, 0),
            new GridCoord(-1, 0),
            new GridCoord(0, 1),
            new GridCoord(0, -1),
            new GridCoord(1, 1),
            new GridCoord(1, -1),
            new GridCoord(-1, 1),
            new GridCoord(-1, -1),
        };

        // ====================================================================================================
        // 3. Constructor
        // ====================================================================================================

        public GridCoord(int x, int y)
        {
            X = x;
            Y = y;
        }

        // ====================================================================================================
        // 4. Properties
        // ====================================================================================================

        /// <summary>유효한 좌표인지 여부입니다. <see cref="Invalid"/>와 비교합니다.</summary>
        public bool IsValid => X != int.MinValue && Y != int.MinValue;

        // ====================================================================================================
        // 5. Public Methods
        // ====================================================================================================

        /// <summary>맨해튼 거리(4방향 이동 기준 거리)를 반환합니다.</summary>
        public static int ManhattanDistance(GridCoord a, GridCoord b)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        }

        /// <summary>체비셰프 거리(8방향 이동 기준 거리)를 반환합니다.</summary>
        public static int ChebyshevDistance(GridCoord a, GridCoord b)
        {
            return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        /// <summary>
        /// 옥타일 거리입니다. 직선 1, 대각선 √2 비용으로 8방향 이동할 때의 최단 거리입니다.
        ///
        /// <b>8방향 A*의 휴리스틱은 반드시 이것이어야 합니다.</b>
        /// 맨해튼 거리는 대각선으로 갈 수 있는 구간을 두 걸음으로 세므로 실제 비용보다 크게 봅니다.
        /// 휴리스틱이 실제보다 크면(비허용적) A*는 최적 경로를 보장하지 않고,
        /// 대각선으로 질러갈 수 있는데도 돌아가는 경로를 내놓습니다.
        /// </summary>
        public static float OctileDistance(GridCoord a, GridCoord b)
        {
            int dx = Math.Abs(a.X - b.X);
            int dy = Math.Abs(a.Y - b.Y);

            // 겹치는 구간은 대각선으로, 나머지는 직선으로 갑니다.
            return (dx + dy) + (DiagonalStepCost - 2f) * Math.Min(dx, dy);
        }

        // ====================================================================================================
        // 6. Operators / Equality
        // ====================================================================================================

        public static GridCoord operator +(GridCoord a, GridCoord b) => new GridCoord(a.X + b.X, a.Y + b.Y);

        public static GridCoord operator -(GridCoord a, GridCoord b) => new GridCoord(a.X - b.X, a.Y - b.Y);

        public static bool operator ==(GridCoord a, GridCoord b) => a.X == b.X && a.Y == b.Y;

        public static bool operator !=(GridCoord a, GridCoord b) => !(a == b);

        public bool Equals(GridCoord other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is GridCoord other && Equals(other);

        /// <summary>
        /// X와 Y를 조합한 해시입니다. 그리드 폭이 수천 단위를 넘지 않는 프로토타입 범위에서 충돌이 거의 없습니다.
        /// </summary>
        public override int GetHashCode() => unchecked((X * 397) ^ Y);

        public override string ToString() => $"({X}, {Y})";
    }
}
