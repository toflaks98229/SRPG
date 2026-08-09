using System.Collections.Generic;
using UnityEngine;

namespace SRPG.Systems.Spatial
{
    /// <summary>
    /// 고정 영역 위의 균일 공간 격자입니다. 반경 질의를 전수 조사 없이 처리합니다.
    ///
    /// <b>왜 해시가 아니라 조밀 배열인가</b>
    ///
    /// 흔히 쓰는 공간 해싱은 월드가 무한하거나 경계를 모를 때의 선택입니다.
    /// 이 게임의 전장은 섬 하나이고 크기가 생성 시점에 이미 확정됩니다.
    /// 그러면 셀 배열을 통째로 잡아 두는 편이 낫습니다.
    /// 궁수의 교전 반경(10m)처럼 넓은 질의는 수십 개 셀을 훑는데,
    /// 해시라면 그만큼 해시 계산과 버킷 탐색이 들지만 조밀 배열은 <b>인덱스 산술뿐</b>입니다.
    ///
    /// <b>왜 위치를 함께 저장하는가</b>
    ///
    /// 셀 단위로만 걸러 내면 결과가 "반경 근처"일 뿐 정확하지 않습니다.
    /// 호출부가 다시 거리를 재야 하고, 그러면 이 클래스를 쓴 의미가 절반으로 줄어듭니다.
    /// 색인 시점 위치를 함께 들고 있으면 <see cref="Query"/>가 정확한 반경 결과를 돌려줍니다.
    ///
    /// <b>한 프레임 스냅샷입니다</b>
    ///
    /// <see cref="Rebuild"/> 시점의 위치로 색인합니다. 그 뒤 대상이 움직여도 색인은 따라가지 않습니다.
    /// 초당 60프레임에서 유닛이 최대로 움직여야 6cm 남짓이라, AI 판단과 조향에는 영향이 없습니다.
    /// 매 프레임 다시 만드는 비용이 O(대상 수)라 그래도 됩니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 자료구조라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    /// <typeparam name="T">색인할 대상입니다. 이 클래스는 대상의 타입을 알 필요가 없습니다.</typeparam>
    public sealed class SpatialGrid<T> where T : class
    {
        // ====================================================================================================
        // 1. Types
        // ====================================================================================================

        /// <summary>색인된 대상 하나입니다. 색인 시점의 위치를 함께 들고 있습니다.</summary>
        private readonly struct Entry
        {
            public readonly Vector3 Position;
            public readonly T Item;

            public Entry(Vector3 position, T item)
            {
                Position = position;
                Item = item;
            }
        }

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        /// <summary>색인이 덮는 영역의 좌하단 월드 좌표입니다.</summary>
        private readonly Vector3 _origin;
        /// <summary>셀 한 칸의 월드 크기입니다.</summary>
        private readonly float _cellSize;
        /// <summary>셀 크기의 역수입니다. 나눗셈을 곱셈으로 바꾸기 위해 미리 구해 둡니다.</summary>
        private readonly float _inverseCellSize;
        /// <summary>색인의 가로 셀 수입니다.</summary>
        private readonly int _columns;
        /// <summary>색인의 세로 셀 수입니다.</summary>
        private readonly int _rows;

        /// <summary>셀별 항목 목록입니다. 조밀 배열이라 인덱스 산술만으로 접근합니다.</summary>
        private readonly List<Entry>[] _buckets;

        /// <summary>
        /// 이번 주기에 실제로 대상이 들어간 버킷 인덱스입니다.
        ///
        /// <see cref="Clear"/>가 전체 버킷을 훑지 않도록 하기 위한 것입니다.
        /// 이게 없으면 비우는 비용이 <b>맵 크기</b>에 비례하고, 있으면 <b>대상 수</b>에 비례합니다.
        /// 유닛이 20명이든 200명이든 맵은 그대로이므로, 적은 유닛에서 특히 차이가 큽니다.
        /// </summary>
        private readonly List<int> _occupied;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>현재 색인된 대상 수입니다.</summary>
        public int Count { get; private set; }

        /// <summary>격자의 열 수입니다.</summary>
        public int Columns => _columns;

        /// <summary>격자의 행 수입니다.</summary>
        public int Rows => _rows;

        // ====================================================================================================
        // 4. Constructor
        // ====================================================================================================

        /// <summary>
        /// 격자를 만듭니다. 영역 밖의 대상도 안전하게 처리되지만, 가장자리 셀에 몰리므로
        /// 실제로 대상이 존재하는 범위를 넉넉히 덮도록 잡는 편이 좋습니다.
        /// </summary>
        /// <param name="origin">영역의 좌하단(XZ 최소) 월드 좌표입니다.</param>
        /// <param name="width">영역의 X 방향 크기입니다.</param>
        /// <param name="depth">영역의 Z 방향 크기입니다.</param>
        /// <param name="cellSize">셀 한 변의 크기입니다. 흔한 질의 반경과 비슷하게 잡습니다.</param>
        public SpatialGrid(Vector3 origin, float width, float depth, float cellSize)
        {
            _origin = origin;
            _cellSize = Mathf.Max(0.01f, cellSize);
            _inverseCellSize = 1f / _cellSize;

            _columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0f, width) * _inverseCellSize));
            _rows = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0f, depth) * _inverseCellSize));

            _buckets = new List<Entry>[_columns * _rows];
            for (int i = 0; i < _buckets.Length; i++)
            {
                _buckets[i] = new List<Entry>(4);
            }

            _occupied = new List<int>(Mathf.Min(_buckets.Length, 128));
        }

        // ====================================================================================================
        // 5. Public Methods - Build
        // ====================================================================================================

        /// <summary>
        /// 색인을 비웁니다. 채워졌던 버킷만 훑으므로 비용이 대상 수에 비례합니다.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _occupied.Count; i++)
            {
                _buckets[_occupied[i]].Clear();
            }

            _occupied.Clear();
            Count = 0;
        }

        /// <summary>
        /// 대상 하나를 색인에 넣습니다. 영역 밖이면 가장 가까운 가장자리 셀에 들어갑니다.
        /// </summary>
        /// <param name="position">항목의 월드 좌표입니다. 결과와 함께 저장됩니다.</param>
        /// <param name="item">색인에 넣을 항목입니다.</param>
        public void Insert(Vector3 position, T item)
        {
            if (item == null)
            {
                return;
            }

            int index = IndexOf(position);
            var bucket = _buckets[index];

            if (bucket.Count == 0)
            {
                _occupied.Add(index);
            }

            bucket.Add(new Entry(position, item));
            Count++;
        }

        // ====================================================================================================
        // 6. Public Methods - Query
        // ====================================================================================================

        /// <summary>
        /// 반경 안의 대상을 <paramref name="buffer"/>에 채웁니다.
        ///
        /// 결과는 <b>정확합니다.</b> 셀 단위로 후보를 좁힌 뒤 색인 시점 위치로 거리를 다시 확인합니다.
        /// </summary>
        /// <param name="center">질의 중심입니다.</param>
        /// <param name="radius">질의 반경입니다. 0 이하면 아무것도 담기지 않습니다.</param>
        /// <param name="buffer">결과가 채워집니다. 호출 시 비워집니다.</param>
        /// <returns>담긴 대상 수입니다.</returns>
        public int Query(Vector3 center, float radius, List<T> buffer)
        {
            buffer.Clear();

            if (radius <= 0f || Count == 0)
            {
                return 0;
            }

            int minX = ClampColumn(ColumnOf(center.x - radius));
            int maxX = ClampColumn(ColumnOf(center.x + radius));
            int minZ = ClampRow(RowOf(center.z - radius));
            int maxZ = ClampRow(RowOf(center.z + radius));

            float sqrRadius = radius * radius;

            for (int z = minZ; z <= maxZ; z++)
            {
                int rowOffset = z * _columns;

                for (int x = minX; x <= maxX; x++)
                {
                    var bucket = _buckets[rowOffset + x];

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var entry = bucket[i];
                        if ((entry.Position - center).sqrMagnitude <= sqrRadius)
                        {
                            buffer.Add(entry.Item);
                        }
                    }
                }
            }

            return buffer.Count;
        }

        // ====================================================================================================
        // 7. Private Methods
        // ====================================================================================================

        private int ColumnOf(float worldX) => Mathf.FloorToInt((worldX - _origin.x) * _inverseCellSize);

        private int RowOf(float worldZ) => Mathf.FloorToInt((worldZ - _origin.z) * _inverseCellSize);

        private int ClampColumn(int column) => Mathf.Clamp(column, 0, _columns - 1);

        private int ClampRow(int row) => Mathf.Clamp(row, 0, _rows - 1);

        /// <summary>
        /// 월드 좌표가 속한 버킷 인덱스입니다. 영역 밖이면 가장 가까운 가장자리로 붙입니다.
        ///
        /// 밖에 있는 대상을 버리지 않고 붙잡아 두는 이유는, 버리면 <b>결과가 조용히 누락</b>되기 때문입니다.
        /// 가장자리에 몰리면 그 셀의 질의가 조금 느려질 뿐이고, 거리 확인은 어차피 다시 하므로 결과는 정확합니다.
        /// </summary>
        private int IndexOf(Vector3 position)
        {
            int column = ClampColumn(ColumnOf(position.x));
            int row = ClampRow(RowOf(position.z));
            return row * _columns + column;
        }
    }
}
