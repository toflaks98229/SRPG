using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 전장의 성격입니다. 나중에 <b>월드맵의 좌표가 이것을 고릅니다.</b>
    ///
    /// 지형이 다르면 싸우는 법이 달라야 합니다 — 숲은 시야가 막히고 우회가 쉬우며,
    /// 벌판은 궁수가 유리하고 언덕은 고지 다툼이 됩니다.
    /// 그 차이를 값 몇 개로 표현합니다.
    /// </summary>
    public enum TerrainKind
    {
        /// <summary>벌판. 거의 평평하고 장애물이 드뭅니다.</summary>
        Plains = 0,

        /// <summary>숲. 장애물이 빽빽해 시야와 진로가 막힙니다.</summary>
        Forest = 1,

        /// <summary>구릉. 고저가 뚜렷해 고지가 의미를 갖습니다.</summary>
        Hills = 2,

        /// <summary>바위땅. 장애물이 크고 통로가 좁습니다.</summary>
        Rocky = 3,
    }

    /// <summary>
    /// 한 종류의 전장을 어떻게 만들지 정한 값 묶음입니다.
    ///
    /// <b>왜 에셋으로 두는가</b>
    ///
    /// 월드맵이 붙으면 좌표마다 지형이 달라지고, 그 차이를 조율하는 것은
    /// 코드가 아니라 기획입니다. "숲이 너무 답답하다"를 고치려고 컴파일하면 안 됩니다.
    ///
    /// <b>왜 이 값들인가</b>
    ///
    /// 배너로드의 전장은 복잡하지 않습니다 — 대체로 걸을 수 있는 땅에
    /// 완만한 기복이 있고 장애물이 흩어져 있습니다. 그 셋이면 성격이 갈립니다.
    /// 섬을 만들 때처럼 침식과 절벽 윤곽까지 갈 이유가 없습니다.
    /// </summary>
    [CreateAssetMenu(fileName = "Battlefield_", menuName = "SRPG/Configs/Battlefield Profile")]
    public sealed class BattlefieldProfile : ScriptableObject
    {
        // ====================================================================================================
        // 1. Identity
        // ====================================================================================================

        [Header("정체")]
        [Tooltip("이 프로필이 담당하는 지형 종류입니다. 월드맵이 이 값으로 프로필을 고릅니다.")]
        public TerrainKind Kind = TerrainKind.Plains;

        // ====================================================================================================
        // 2. Elevation
        // ====================================================================================================

        [Header("기복")]
        [Range(0f, 12f)]
        [Tooltip("가장 높은 곳의 해발입니다. 월드 단위입니다.\n\n" +
                 "전장의 고저는 장벽이 아니라 수정자입니다. " +
                 "너무 높이면 지형이 길을 막기 시작해 배너로드보다 배드노스에 가까워집니다.")]
        public float MaxElevation = 3f;

        [Range(0.02f, 0.4f)]
        [Tooltip("언덕의 잘기입니다. 작을수록 넓고 완만한 기복이 됩니다.")]
        public float HillScale = 0.09f;

        [Range(15f, 60f)]
        [Tooltip("이 각도를 넘는 비탈은 오를 수 없습니다.\n\n" +
                 "터레인은 연속면이라 어디든 걸을 수 있어 보입니다. " +
                 "그래서 기울기가 통행을 정합니다 — 보이는 것과 같은 기준이라야 " +
                 "\"올라갈 수 있을 줄 알았는데\"가 생기지 않습니다.")]
        public float ClimbLimitDegrees = 34f;

        // ====================================================================================================
        // 3. Obstacles
        // ====================================================================================================

        [Header("장애물")]
        [Range(0f, 0.4f)]
        [Tooltip("통행 불가 칸의 비율입니다.\n\n" +
                 "이것이 지형의 성격을 가장 크게 가릅니다. 벌판은 거의 0, 숲은 빽빽합니다. " +
                 "다만 너무 높이면 부대가 지나갈 길이 없어집니다.")]
        public float ObstacleDensity = 0.04f;

        [Range(1, 5)]
        [Tooltip("장애물 한 덩이의 크기입니다. 클수록 큰 바위·수풀이 되어 시야를 막습니다.\n\n" +
                 "같은 밀도라도 잘게 흩으면 성가시기만 하고, 뭉치면 우회로가 생겨 전술이 됩니다.")]
        public int ObstacleClumpSize = 2;

        // ====================================================================================================
        // 5. Factory
        // ====================================================================================================

        /// <summary>
        /// 에셋 없이 코드로 기본 프로필을 만듭니다.
        ///
        /// 에셋이 아직 없어도 전투가 돌아가야 합니다.
        /// 프로필 하나 때문에 프로젝트가 실행조차 안 되면 안 됩니다.
        /// </summary>
        public static BattlefieldProfile CreateDefault(TerrainKind kind = TerrainKind.Plains)
        {
            var profile = CreateInstance<BattlefieldProfile>();
            profile.name = $"Battlefield_{kind}";
            profile.Kind = kind;

            switch (kind)
            {
                case TerrainKind.Forest:
                    // 빽빽하고 평평합니다. 시야가 막혀 접근을 못 보는 것이 특징입니다.
                    profile.MaxElevation = 1.5f;
                    profile.HillScale = 0.07f;
                    profile.ObstacleDensity = 0.2f;
                    profile.ObstacleClumpSize = 2;
                    break;

                case TerrainKind.Hills:
                    // 고저가 뚜렷합니다. 장애물은 적어 고지 자체가 자원이 됩니다.
                    profile.MaxElevation = 6f;
                    profile.HillScale = 0.06f;
                    profile.ObstacleDensity = 0.03f;
                    profile.ObstacleClumpSize = 2;
                    break;

                case TerrainKind.Rocky:
                    // 큰 바위가 통로를 좁힙니다. 초크포인트가 생기는 유일한 지형입니다.
                    profile.MaxElevation = 4f;
                    profile.HillScale = 0.11f;
                    profile.ObstacleDensity = 0.14f;
                    profile.ObstacleClumpSize = 4;
                    break;

                default:
                    // 벌판. 거의 아무것도 없습니다 — 순수한 부대 운용 싸움입니다.
                    profile.MaxElevation = 2.5f;
                    profile.HillScale = 0.09f;
                    profile.ObstacleDensity = 0.03f;
                    profile.ObstacleClumpSize = 2;
                    break;
            }

            return profile;
        }
    }
}
