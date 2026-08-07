using System.Collections.Generic;
using SRPG.Common;
using UnityEngine;

namespace SRPG.Systems.Landform
{
    /// <summary>
    /// 2단계 — 인공적 지형 변형. 길을 다지고 건물 터를 깎습니다.
    ///
    /// <b>사람이 땅을 건드린 흔적</b>
    ///
    /// 자연 지형만 있으면 무인도로 보입니다. 사람이 살았다는 인상은 건물이 아니라
    /// <b>땅에 남은 자국</b>에서 옵니다 — 다져진 길, 깎아 만든 평평한 터.
    /// 그 자국은 자연이 만들 수 없는 모양이라 즉시 인공물로 읽힙니다.
    ///
    /// <b>절토와 성토</b>
    ///
    /// 길을 내려면 높은 쪽은 깎고(cut) 낮은 쪽은 메웁니다(fill).
    /// 여기서는 경로 위 높이를 목표로 잡고 주변을 그쪽으로 끌어당깁니다.
    /// 끌어당기는 세기는 거리에 따라 <b>스플라인 곡선</b>으로 떨어집니다.
    ///
    /// 선형으로 떨어뜨리면 영향 반경 끝에 각진 접힘선이 남습니다.
    /// smoothstep 은 양 끝에서 기울기가 0이라 그 선이 생기지 않습니다.
    ///
    /// <b>이 단계는 일부러 부자연스럽습니다</b>
    ///
    /// 여기서 나온 절단면은 가파른 수직 흙벽입니다. 그대로 두면 조각칼 자국처럼 보입니다.
    /// 그것을 무너뜨리는 것이 3단계의 일입니다. 순서가 바뀌면 안 됩니다.
    /// </summary>
    public static class TerrainSculptor
    {
        // ====================================================================================================
        // 1. Public Methods - Cut and Fill
        // ====================================================================================================

        /// <summary>
        /// 경로 주변을 평탄화합니다.
        /// </summary>
        /// <param name="field">지형입니다.</param>
        /// <param name="path">표본 좌표로 된 경로입니다.</param>
        /// <param name="roadRadius">완전히 평탄해지는 반경입니다. 표본 단위입니다.</param>
        /// <param name="shoulderRadius">영향이 사라지는 반경입니다. 표본 단위입니다.</param>
        public static void CutAndFill(
            HeightField field,
            IReadOnlyList<GridCoord> path,
            float roadRadius,
            float shoulderRadius)
        {
            if (field == null || path == null || path.Count == 0)
            {
                return;
            }

            shoulderRadius = Mathf.Max(shoulderRadius, roadRadius + 0.01f);

            int reach = Mathf.CeilToInt(shoulderRadius);

            // 여러 경로가 겹칠 때 나중 것이 앞 것을 덮어쓰지 않도록,
            // 가장 강한 영향 하나만 남겼다가 마지막에 한 번에 적용합니다.
            var targetHeight = new Dictionary<int, float>();
            var strength = new Dictionary<int, float>();

            for (int p = 0; p < path.Count; p++)
            {
                var center = path[p];

                // 경로 위의 목표 기복입니다. 이 값으로 주변을 끌어당깁니다.
                float target = field.GetRelief(center.X, center.Y);

                for (int oy = -reach; oy <= reach; oy++)
                {
                    for (int ox = -reach; ox <= reach; ox++)
                    {
                        int sx = center.X + ox;
                        int sy = center.Y + oy;

                        if (!field.IsLand(sx, sy))
                        {
                            continue;
                        }

                        float distance = Mathf.Sqrt(ox * ox + oy * oy);
                        if (distance > shoulderRadius)
                        {
                            continue;
                        }

                        float weight = Falloff(distance, roadRadius, shoulderRadius);
                        if (weight <= 0f)
                        {
                            continue;
                        }

                        int index = field.Index(sx, sy);

                        if (!strength.TryGetValue(index, out float existing) || weight > existing)
                        {
                            strength[index] = weight;
                            targetHeight[index] = target;
                        }
                    }
                }
            }

            foreach (var pair in strength)
            {
                int index = pair.Key;
                int sx = index % field.SamplesX;
                int sy = index / field.SamplesX;

                float current = field.GetRelief(sx, sy);
                field.SetRelief(sx, sy, Mathf.Lerp(current, targetHeight[index], pair.Value));
            }
        }

        // ====================================================================================================
        // 2. Public Methods - Terracing
        // ====================================================================================================

        /// <summary>
        /// 한 지점 주변을 계단식으로 다집니다. 건물이 앉을 터를 만듭니다.
        ///
        /// <b>왜 단순 평탄화가 아닌가</b>
        ///
        /// 그냥 평평하게 밀면 주변과의 경계가 하나의 큰 단차가 됩니다.
        /// 실제 산비탈의 집터는 여러 단으로 나뉩니다 — 한 번에 다 깎는 것보다
        /// 나눠 깎는 편이 흙을 훨씬 덜 옮기기 때문입니다.
        ///
        /// 그래서 높이를 <see cref="stepCount"/>단으로 <b>양자화</b>합니다.
        /// 결과가 계단이 되고, 그 계단이 사람 손을 탄 인상을 냅니다.
        /// </summary>
        /// <param name="field">지형입니다.</param>
        /// <param name="center">터의 중심 표본입니다.</param>
        /// <param name="radius">완전히 다져지는 반경입니다. 표본 단위입니다.</param>
        /// <param name="blendRadius">영향이 사라지는 반경입니다.</param>
        /// <param name="stepCount">나눌 단의 수입니다.</param>
        public static void Terrace(
            HeightField field,
            GridCoord center,
            float radius,
            float blendRadius,
            int stepCount = 3)
        {
            if (field == null || stepCount < 1)
            {
                return;
            }

            blendRadius = Mathf.Max(blendRadius, radius + 0.01f);

            int reach = Mathf.CeilToInt(blendRadius);
            float stepSize = field.ReliefLimit * 2f / stepCount;

            for (int oy = -reach; oy <= reach; oy++)
            {
                for (int ox = -reach; ox <= reach; ox++)
                {
                    int sx = center.X + ox;
                    int sy = center.Y + oy;

                    if (!field.IsLand(sx, sy))
                    {
                        continue;
                    }

                    float distance = Mathf.Sqrt(ox * ox + oy * oy);
                    if (distance > blendRadius)
                    {
                        continue;
                    }

                    float weight = Falloff(distance, radius, blendRadius);
                    if (weight <= 0f)
                    {
                        continue;
                    }

                    float current = field.GetRelief(sx, sy);
                    float quantized = Mathf.Round(current / stepSize) * stepSize;

                    field.SetRelief(sx, sy, Mathf.Lerp(current, quantized, weight));
                }
            }
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 거리에 따른 영향 세기입니다.
        ///
        /// 안쪽 반경까지는 1, 바깥 반경에서 0, 그 사이는 매끄럽게 이어집니다.
        /// 선형으로 떨어뜨리면 바깥 반경에 각진 접힘선이 남습니다.
        /// </summary>
        public static float Falloff(float distance, float innerRadius, float outerRadius)
        {
            if (distance <= innerRadius)
            {
                return 1f;
            }

            if (distance >= outerRadius)
            {
                return 0f;
            }

            float t = (distance - innerRadius) / (outerRadius - innerRadius);

            // smoothstep 을 뒤집습니다. 양 끝에서 기울기가 0입니다.
            return 1f - (t * t * (3f - 2f * t));
        }
    }
}
