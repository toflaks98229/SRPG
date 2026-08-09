using System.Collections.Generic;
using SRPG.Common;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Systems.AI
{
    /// <summary>
    /// 격자 위에 "무엇이 어디에 얼마나 영향을 미치는가"를 펼쳐 놓은 지도입니다.
    ///
    /// <b>왜 필요한가</b>
    ///
    /// 규칙 기반 AI는 "가장 가까운 적에게 간다" 같은 판단만 할 수 있습니다.
    /// 그러면 적은 방어가 두꺼운 정면으로 곧장 걸어 들어가고, 플레이어는 한 곳만 막으면 됩니다.
    /// 조사 보고서에서 Fire Emblem식 규칙 기반의 한계로 지적한 바로 그 지점입니다.
    ///
    /// 영향력 맵은 "어디가 위험한가"를 <b>공간 전체에 대해</b> 알려 줍니다.
    /// 그러면 "가옥으로 가되 방어가 얇은 쪽으로"라는 판단이 가능해지고,
    /// 그 결과가 곧 측면 우회입니다. 우회 규칙을 따로 쓰지 않아도 나옵니다.
    ///
    /// <b>퍼짐 방식</b>
    ///
    /// 발생원에서 시작해 통행 가능한 이웃으로 너비 우선으로 번지며, 한 칸 갈 때마다 감쇠합니다.
    /// 벽을 통과하지 않으므로 절벽 너머의 위협이 이쪽으로 새어 오지 않습니다.
    /// <b>이것이 유클리드 거리 대신 BFS를 쓰는 이유입니다.</b> 절벽 하나를 사이에 둔 두 지점은
    /// 직선 거리로는 가깝지만 실제로는 한참 돌아가야 하고, 위협도 그만큼 늦게 도달합니다.
    ///
    /// 겹치는 발생원은 <b>더해지고</b>(같은 칸에 병사가 많으면 더 위험하다),
    /// 번지는 값은 <b>더 큰 쪽이 이깁니다</b>(가까운 위협이 먼 위협에 묻히지 않는다).
    ///
    /// MonoBehaviour에 의존하지 않는 순수 자료구조라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public sealed class InfluenceMap
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>이보다 작은 값은 없는 것으로 봅니다. 번짐이 무한히 이어지지 않게 합니다.</summary>
        private const float Epsilon = 0.001f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        /// <summary>값을 펼칠 지형입니다. 번지는 경로가 통행 가능 여부를 따릅니다.</summary>
        private readonly IslandGrid _grid;

        /// <summary>칸별 영향력 값입니다. 격자와 같은 행 우선 순서입니다.</summary>
        private readonly float[] _values;

        /// <summary>너비 우선 확산의 대기열입니다. 셀 인덱스를 담습니다.</summary>
        private readonly Queue<int> _frontier;

        /// <summary>값이 들어간 칸의 인덱스입니다. 비울 때 격자 전체를 훑지 않기 위한 것입니다.</summary>
        private readonly List<int> _touched;

        /// <summary>이웃 조회용 재사용 버퍼입니다. 주기적으로 도는 갱신에서 할당을 만들지 않기 위함입니다.</summary>
        private readonly Tile[] _neighborBuffer = new Tile[4];

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>지도에서 가장 큰 값입니다. 0~1로 정규화할 때의 분모입니다.</summary>
        public float MaxValue { get; private set; }

        /// <summary>값이 하나라도 있는지 여부입니다.</summary>
        public bool IsEmpty => _touched.Count == 0;

        // ====================================================================================================
        // 4. Constructor
        // ====================================================================================================

        /// <summary>지형 크기에 맞춰 지도를 만듭니다.</summary>
        /// <param name="grid">값을 펼칠 지형입니다. 격자 크기가 그대로 지도 크기가 됩니다.</param>
        public InfluenceMap(IslandGrid grid)
        {
            _grid = grid;
            _values = new float[grid.Width * grid.Depth];
            _frontier = new Queue<int>(_values.Length);
            _touched = new List<int>(256);
        }

        // ====================================================================================================
        // 5. Public Methods - Build
        // ====================================================================================================

        /// <summary>
        /// 지도를 비웁니다. 값이 들어갔던 칸만 훑으므로 비용이 격자 크기가 아니라 발생원 규모에 비례합니다.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _touched.Count; i++)
            {
                _values[_touched[i]] = 0f;
            }

            _touched.Clear();
            MaxValue = 0f;
        }

        /// <summary>
        /// 발생원을 더합니다. 같은 칸에 여러 번 더하면 누적됩니다.
        /// </summary>
        /// <param name="coord">발생 위치입니다.</param>
        /// <param name="strength">세기입니다. 0 이하면 무시합니다.</param>
        public void AddSource(GridCoord coord, float strength)
        {
            if (strength <= 0f || !_grid.IsInside(coord))
            {
                return;
            }

            int index = ToIndex(coord);

            if (_values[index] <= 0f)
            {
                _touched.Add(index);
            }

            _values[index] += strength;
        }

        /// <summary>
        /// 월드 좌표의 발생원을 더합니다.
        /// </summary>
        /// <param name="world">발생 위치의 월드 좌표입니다. 격자 좌표로 변환해 더합니다.</param>
        /// <param name="strength">세기입니다. 0 이하면 무시합니다.</param>
        public void AddSourceWorld(Vector3 world, float strength)
        {
            AddSource(_grid.WorldToCoord(world), strength);
        }

        /// <summary>
        /// 발생원에서 바깥으로 번지게 합니다. 한 칸 갈 때마다 <paramref name="decayPerStep"/>이 곱해집니다.
        ///
        /// 통행 가능한 칸으로만 번집니다. 절벽과 바다가 자연스러운 차폐가 됩니다.
        /// </summary>
        /// <param name="decayPerStep">칸당 감쇠 비율(0~1)입니다. 0.6이면 한 칸 갈 때 60%가 남습니다.</param>
        public void Propagate(float decayPerStep)
        {
            float decay = Mathf.Clamp(decayPerStep, 0f, 0.999f);

            _frontier.Clear();
            for (int i = 0; i < _touched.Count; i++)
            {
                _frontier.Enqueue(_touched[i]);
            }

            while (_frontier.Count > 0)
            {
                int current = _frontier.Dequeue();
                float spread = _values[current] * decay;

                if (spread <= Epsilon)
                {
                    continue;
                }

                var coord = ToCoord(current);
                int count = _grid.GetNeighbors4(coord, _neighborBuffer);

                for (int n = 0; n < count; n++)
                {
                    var neighbor = _neighborBuffer[n];
                    if (!neighbor.IsWalkable)
                    {
                        continue;
                    }

                    int index = ToIndex(neighbor.Coord);

                    // 더 강한 값이 이깁니다. 가까운 위협이 먼 위협에 묻히지 않게 하기 위함입니다.
                    if (_values[index] >= spread)
                    {
                        continue;
                    }

                    if (_values[index] <= 0f)
                    {
                        _touched.Add(index);
                    }

                    _values[index] = spread;
                    _frontier.Enqueue(index);
                }
            }

            RecomputeMax();
        }

        // ====================================================================================================
        // 6. Public Methods - Sample
        // ====================================================================================================

        /// <summary>지정 칸의 값입니다. 격자 밖이면 0입니다.</summary>
        public float this[GridCoord coord]
        {
            get { return _grid.IsInside(coord) ? _values[ToIndex(coord)] : 0f; }
        }

        /// <summary>월드 좌표의 영향력 값을 읽습니다.</summary>
        /// <param name="world">읽을 위치의 월드 좌표입니다.</param>
        /// <returns>그 자리의 영향력 값입니다. 격자 밖이면 0입니다.</returns>
        public float SampleWorld(Vector3 world)
        {
            return this[_grid.WorldToCoord(world)];
        }

        /// <summary>
        /// 지정 칸의 값을 0~1로 정규화해 반환합니다. 유틸리티 평가의 입력으로 바로 쓸 수 있습니다.
        /// 지도가 비어 있으면 0입니다.
        /// </summary>
        /// <param name="coord">읽을 격자 좌표입니다.</param>
        /// <returns><see cref="MaxValue"/>로 나눈 0~1 값입니다. 지도가 비었거나 격자 밖이면 0입니다.</returns>
        public float SampleNormalized(GridCoord coord)
        {
            return MaxValue > Epsilon ? Mathf.Clamp01(this[coord] / MaxValue) : 0f;
        }

        // ====================================================================================================
        // 7. Private Methods
        // ====================================================================================================

        private int ToIndex(GridCoord coord) => coord.Y * _grid.Width + coord.X;

        private GridCoord ToCoord(int index) => new GridCoord(index % _grid.Width, index / _grid.Width);

        private void RecomputeMax()
        {
            float max = 0f;
            for (int i = 0; i < _touched.Count; i++)
            {
                float value = _values[_touched[i]];
                if (value > max)
                {
                    max = value;
                }
            }

            MaxValue = max;
        }
    }
}
