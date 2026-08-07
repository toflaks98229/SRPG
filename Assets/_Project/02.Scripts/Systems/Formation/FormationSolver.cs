using System.Collections.Generic;
using UnityEngine;

namespace SRPG.Systems.Formation
{
    /// <summary>
    /// 분대 진형의 슬롯 위치를 계산합니다.
    /// 분대는 "앵커 한 점"만 이동시키고 각 병사는 자기 슬롯을 향해 조향하므로,
    /// 진형 계산이 곧 분대 이동의 형태를 결정합니다.
    ///
    /// 현재 쓰는 것은 <see cref="SolveRings"/>입니다.
    /// <see cref="SolveGrid"/>는 방향을 받는 옛 방식으로, 전열을 세워야 하는 진형이 생기면 다시 쓸 수 있게 남겨 둡니다.
    /// </summary>
    public static class FormationSolver
    {
        // ====================================================================================================
        // 1. Public Methods - Rings (방향 무관)
        // ====================================================================================================

        /// <summary>
        /// 앵커를 중심으로 한 동심원 진형의 슬롯 위치를 계산합니다.
        ///
        /// <b>방향을 받지 않습니다.</b> 진형을 어느 쪽으로 세울지 정하지 않는다는 뜻이고, 이유는 두 가지입니다.
        ///   · 실시간에서 위협은 사방에서 옵니다. 한 방향으로 전열을 세우면 나머지 세 방향이 항상 약점이 됩니다
        ///   · 진형이 회전하면 병사들이 제자리에서 빙빙 도는 것처럼 보입니다. 방향이 없으면 그 문제가 사라집니다
        ///
        /// 슬롯 0번은 항상 중심입니다. 분대의 지휘관 자리이며, 사방에서 가장 안쪽이라 가장 안전합니다.
        /// 그 바깥으로 반지름이 커지는 고리를 만들고, 고리 둘레가 감당할 수 있는 만큼 인원을 배분합니다.
        /// </summary>
        /// <param name="anchor">진형의 중심 월드 좌표입니다.</param>
        /// <param name="count">배치할 인원 수입니다.</param>
        /// <param name="spacing">고리 간격이자 인원 간 최소 간격입니다.</param>
        /// <param name="result">계산된 슬롯 위치가 채워집니다. 호출 시 비워집니다.</param>
        public static void SolveRings(Vector3 anchor, int count, float spacing, List<Vector3> result)
        {
            result.Clear();
            if (count <= 0)
            {
                return;
            }

            // 중심 한 자리
            result.Add(anchor);

            int placed = 1;
            int ring = 1;

            while (placed < count)
            {
                float radius = ring * spacing;

                // 이 고리 둘레에 간격을 지키며 들어갈 수 있는 최대 인원입니다.
                int capacity = Mathf.Max(1, Mathf.FloorToInt(2f * Mathf.PI * radius / spacing));
                int inThisRing = Mathf.Min(capacity, count - placed);

                // 고리마다 시작 각도를 어긋나게 해 안쪽 고리와 방사상으로 겹쳐 보이지 않게 합니다.
                float angleOffset = ring * 0.5f;

                for (int i = 0; i < inThisRing; i++)
                {
                    float angle = angleOffset + i * (2f * Mathf.PI / inThisRing);
                    result.Add(anchor + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
                }

                placed += inThisRing;
                ring++;

                // 간격 설정이 병적인 경우에도 무한 루프에 빠지지 않도록 막습니다.
                if (ring > count + 2)
                {
                    break;
                }
            }
        }

        // ====================================================================================================
        // 2. Public Methods - Grid (방향 기반, 현재 미사용)
        // ====================================================================================================

        /// <summary>
        /// 앵커를 중심으로 한 격자 진형의 슬롯 위치를 계산합니다.
        /// </summary>
        /// <param name="anchor">진형의 중심 월드 좌표입니다.</param>
        /// <param name="forward">진형이 바라보는 방향입니다. 정규화되지 않아도 됩니다.</param>
        /// <param name="count">배치할 인원 수입니다.</param>
        /// <param name="spacing">슬롯 간 간격입니다.</param>
        /// <param name="result">계산된 슬롯 위치가 채워집니다. 호출 시 비워집니다.</param>
        public static void SolveGrid(Vector3 anchor, Vector3 forward, int count, float spacing, List<Vector3> result)
        {
            result.Clear();
            if (count <= 0)
            {
                return;
            }

            Vector3 fwd = forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f)
            {
                fwd = Vector3.forward;
            }

            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            // 전열이 넓어지도록 열 수를 행 수보다 크게 잡습니다.
            int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count * 1.6f)));
            int rows = Mathf.CeilToInt((float)count / columns);

            for (int i = 0; i < count; i++)
            {
                int row = i / columns;
                int column = i % columns;

                // 마지막 행이 덜 찼을 때도 가운데 정렬이 되도록 행마다 실제 인원으로 오프셋을 계산합니다.
                int unitsInThisRow = Mathf.Min(columns, count - row * columns);
                float columnOffset = (column - (unitsInThisRow - 1) * 0.5f) * spacing;
                float rowOffset = (row - (rows - 1) * 0.5f) * spacing;

                result.Add(anchor + right * columnOffset - fwd * rowOffset);
            }
        }
    }
}
