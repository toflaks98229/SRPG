using UnityEngine;

namespace SRPG.Systems.Landform
{
    /// <summary>
    /// 지형을 다질 때 쓰는 감쇠 곡선입니다.
    ///
    /// <b>왜 이것만 남았는가</b>
    ///
    /// 예전에는 여기에 절토·성토와 단구화가 함께 있었습니다.
    /// 타일의 고도 단계가 먼저 정해져 있고, 그 안에서 흔들 수 있는 기복을
    /// 손으로 밀고 당기는 일이었기 때문입니다.
    ///
    /// 순서를 뒤집으면서 그 일이 사라졌습니다. 이제는 물과 중력이 만든 지형을
    /// <see cref="TerrainFlattening"/>이 경사에 따라 다지고, 고도 단계는 그 결과를
    /// 읽어 낼 뿐입니다. 밀고 당길 대상이 없습니다.
    ///
    /// 남은 것은 <b>어떻게 풀어 줄 것인가</b>뿐입니다.
    /// 다진 자리와 손대지 않은 자리 사이는 반드시 매끄럽게 이어져야 합니다.
    /// </summary>
    public static class TerrainSculptor
    {
        /// <summary>
        /// 거리에 따른 영향 세기입니다.
        ///
        /// 안쪽 반경까지는 1, 바깥 반경에서 0, 그 사이는 매끄럽게 이어집니다.
        ///
        /// <b>선형으로 떨어뜨리면 안 됩니다.</b>
        /// 바깥 반경에 기울기가 꺾이는 자리가 남아, 다진 자리 둘레에
        /// 동그란 접힘선이 생깁니다. 지형에 그런 선은 없습니다.
        /// smoothstep 은 양 끝에서 기울기가 0이라 그 선이 생기지 않습니다.
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

            return 1f - (t * t * (3f - 2f * t));
        }
    }
}
