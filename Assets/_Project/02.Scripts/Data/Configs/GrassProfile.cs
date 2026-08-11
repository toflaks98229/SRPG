using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 한 종류의 식생이 어떻게 생겼는지입니다.
    ///
    /// <b>크기는 범위로 적습니다</b>
    ///
    /// 최소와 최대를 주면 그 사이에서 포기마다 다르게 뽑습니다.
    /// 폭이 좁으면 들판이 균질해져 인공물로 읽히고, 너무 넓으면 잡초밭처럼 어수선해집니다.
    /// 그 폭을 전장 전체에서 한 번에 조절하려면 <see cref="GrassProfile.SizeVariation"/> 을 쓰십시오.
    /// </summary>
    [System.Serializable]
    public struct GrassSpeciesProfile
    {
        [Header("크기")]
        [Tooltip("키의 범위(미터)입니다. x가 최소, y가 최대입니다.")]
        public Vector2 HeightRange;

        [Tooltip("폭의 범위(미터)입니다.")]
        public Vector2 WidthRange;

        [Header("색")]
        [Tooltip("밑동 쪽 기본 색입니다.")]
        public Color BaseColor;

        [Tooltip("끝동 색입니다. 위로 갈수록 이쪽으로 물듭니다.")]
        public Color TipColor;

        [Tooltip("고지대에서 마른 것처럼 보이게 하는 색입니다.")]
        public Color DryColor;

        [Tooltip("큰 얼룩의 색입니다. 들판을 몇 덩어리로 나눕니다.")]
        public Color PatchColorA;

        [Tooltip("작은 얼룩의 색입니다. 큰 덩어리 안에 결을 넣습니다.")]
        public Color PatchColorB;

        [Header("음영")]
        [Range(0f, 1f)]
        [Tooltip("끝동 색이 섞이는 정도입니다. 0이면 밑동 색 하나로 납작해집니다.")]
        public float TipBlend;

        [Range(0f, 1f)]
        [Tooltip("밑동을 눌러 지면에 앉히는 정도입니다.\n" +
                 "0으로 두면 풀이 땅에서 떠 보입니다 — 접지감을 만드는 가장 큰 요인입니다.")]
        public float RootShade;

        [Range(0f, 1f)]
        [Tooltip("고도에 따라 마른 색으로 가는 정도입니다.\n" +
                 "1로 두면 기복이 얕은 전장에서 들판 전체가 한 색으로 말라 얼룩이 묻힙니다.")]
        public float DryStrength;

        [Range(0f, 0.6f)]
        [Tooltip("포기마다의 밝기 흔들림입니다.\n" +
                 "낮게 잡으십시오. 포기마다 독립적으로 흔들면 흰 노이즈가 되어 들판이 자글자글해집니다.\n" +
                 "큰 몫은 아래 무리 흔들림이 맡습니다.")]
        public float ColorJitter;

        [Range(0.01f, 0.6f)]
        [Tooltip("무리 흔들림의 크기입니다. 작을수록 넓은 덩어리로 밝기가 갈립니다.")]
        public float ClusterScale;

        [Range(0f, 0.8f)]
        [Tooltip("무리 단위의 밝기 흔들림입니다.\n" +
                 "볕이 든 자리와 그늘진 자리가 여러 포기를 한꺼번에 덮는 것을 흉내 냅니다.")]
        public float ClusterJitter;

        [Range(0f, 1f)]
        [Tooltip("얼룩이 기준 색에서 색조로 얼마나 벌어질 수 있는지입니다.\n" +
                 "자연의 들판은 색조가 아니라 밝기와 채도로 갈립니다 — " +
                 "색조까지 벌어지면 다른 식물이 섞인 것처럼 보입니다.\n" +
                 "0이면 색조를 완전히 맞추고 밝기·채도 차이만 남깁니다.")]
        public float HueSpread;

        [Range(0f, 1f)]
        [Tooltip("모든 변주를 기준 색 쪽으로 당기는 정도입니다.\n" +
                 "얼룩을 지우지 않고 묶습니다. 색이 잘게 나뉘어 보일 때 가장 먼저 올릴 값입니다.")]
        public float ColorCohesion;

        [Header("잎 음영")]
        [Range(0f, 1f)]
        [Tooltip("잎을 둥글게 칠하는 정도입니다.\n" +
                 "0이면 노멀이 전 들판에서 같아져 모든 포기가 한 밝기가 되고, " +
                 "툰 밴딩이 걸리면 금속판처럼 보입니다.")]
        public float NormalRound;

        [Range(0f, 1f)]
        [Tooltip("끝으로 갈수록 노멀을 위로 세우는 정도입니다. 잎끝은 하늘을 봅니다.")]
        public float NormalTipUp;

        [Range(0f, 1f)]
        [Tooltip("포기마다 굴리는 방향을 흩뜨리는 정도입니다.\n" +
                 "0이면 전부 같은 방향으로 굴려 결국 한 밝기가 됩니다.")]
        public float NormalScatter;

        [Header("투과광")]
        [Range(0f, 1f)]
        [Tooltip("해를 등지고 볼 때 잎이 스스로 빛나는 세기입니다.\n" +
                 "이것이 없으면 식생이 플라스틱처럼 보입니다. 0.1~0.3이 자연스럽습니다.")]
        public float Translucency;

        [Tooltip("투과한 빛의 색입니다. 잎을 통과하며 노랗게 물듭니다.")]
        public Color TranslucencyColor;

        [Range(1f, 16f)]
        [Tooltip("클수록 해를 정확히 등졌을 때만 빛납니다.")]
        public float TranslucencyPower;

        [Range(0f, 1f)]
        [Tooltip("밑동이 빛을 통과시키는 비율입니다. 겹치고 두꺼워 끝동보다 어둡습니다.")]
        public float TranslucencyRoot;

        [Header("방향")]
        [Range(0f, 1f)]
        [Tooltip("잎이 무엇을 향해 돌지입니다.\n" +
                 "0이면 잎마다 카메라 자리를 향해 부채꼴로 펼쳐지고, 카메라를 옮기면 들판이 돌아갑니다.\n" +
                 "1이면 카메라가 보는 방향에 나란히 서서, 화면 어디에 있든 같은 각입니다.")]
        public float ViewAlign;

        [Range(0f, 90f)]
        [Tooltip("나란히 선 잎을 포기마다 좌우로 비트는 한계 각도입니다.\n" +
                 "0이면 전부 같은 각이라 판때기로 보이고, 크면 제각각이라 덤불처럼 보입니다.")]
        public float FacingNoise;

        [Range(0f, 1f)]
        [Tooltip("잎의 세로축을 카메라 쪽으로 얼마나 눕힐지입니다.\n" +
                 "0이면 잎이 언제나 월드 기준으로 곧게 섭니다. 내려다보는 각만큼 화면에서 짧아 보이고, " +
                 "카메라를 더 눕힐수록 납작해집니다.\n" +
                 "1이면 카메라를 정면으로 마주해 각도와 무관하게 온전한 키로 읽힙니다.\n\n" +
                 "위의 ViewAlign 이 좌우를 정한다면 이것은 상하입니다. 둘이 함께 1이면 완전히 카메라를 향합니다.")]
        public float PitchAlign;

        [Header("거동")]
        [Range(0f, 90f)]
        [Tooltip("바람에 눕는 각도입니다. 갈대는 크게, 마른 잡초는 거의 흔들리지 않아야 합니다.")]
        public float WindSwayAngle;

        [Range(0f, 0.3f)]
        [Tooltip("드물게 섞이는 다른 풀의 비율입니다. 규칙적인 들판은 인공물로 읽힙니다.")]
        public float AccentChance;
    }

    /// <summary>
    /// 전장의 들판이 어떻게 보일지 정하는 에셋입니다.
    ///
    /// <b>왜 에셋이어야 하는가</b>
    ///
    /// <c>GrassField</c> 는 런타임에 <c>AddComponent</c> 되므로, 거기 붙은 직렬화 필드는
    /// <b>인스펙터에 뜰 기회가 없습니다</b>. 재생 중에만 존재하는 오브젝트라 값을 바꾸려면
    /// 코드를 고치고 컴파일해야 했습니다. 밀도 하나 만져 보려고 그러는 것은 말이 되지 않습니다.
    ///
    /// 이 에셋이 그 자리를 대신합니다. 그리고 전장 종류마다 다른 들판을 줄 수 있게 됩니다 —
    /// 강가는 갈대가 무성하고, 구릉은 마른 잡초가 성기게.
    ///
    /// <b>없어도 됩니다.</b> 연결되지 않으면 <see cref="CreateDefault"/> 가 지금까지의 모습을
    /// 그대로 만들어 냅니다. 이 프로젝트의 다른 설정 에셋과 같은 약속입니다.
    /// </summary>
    [CreateAssetMenu(fileName = "GrassProfile_", menuName = "SRPG/들판 프로필", order = 41)]
    public sealed class GrassProfile : ScriptableObject
    {
        // ====================================================================================================
        // 0. Schema
        // ====================================================================================================

        /// <summary>
        /// 지금 코드가 기대하는 스키마 버전입니다. 종에 필드를 더할 때마다 올립니다.
        ///
        /// <b>이 장치가 없어서 실제로 사고가 났습니다.</b>
        ///
        /// 종의 설정은 <see cref="GrassSpeciesProfile"/> 이라는 <b>구조체</b>입니다.
        /// 구조체 필드는 초기값을 가질 수 없으므로, 에셋이 만들어진 <b>뒤에</b> 필드를 추가하면
        /// 그 필드는 YAML에 없어 전부 0으로 로드됩니다.
        ///
        /// 문제는 0이 대개 <b>틀린 값</b>이라는 것입니다 —
        /// <see cref="GrassSpeciesProfile.ViewAlign"/> 이 0이면 잎이 예전처럼 카메라 자리를 향하고,
        /// 색은 검게 되고, 크기는 0이 되어 아예 보이지 않습니다.
        /// 오류는 하나도 나지 않고 화면만 조용히 틀립니다.
        ///
        /// <c>UnitDefinition</c> 이 같은 이유로 같은 장치를 들고 있습니다.
        /// </summary>
        public const int CurrentSchemaVersion = 3;

        [HideInInspector]
        [Tooltip("이 에셋이 마지막으로 갱신된 스키마 버전입니다. 배선 도구가 관리합니다.")]
        public int SchemaVersion;

        // ====================================================================================================
        // 1. Inspector - 무성함
        // ====================================================================================================

        [Header("무성함")]
        [Range(0.5f, 40f)]
        [Tooltip("제곱미터당 포기 수입니다.\n" +
                 "그림 한 장이 여러 갈래짜리 한 포기라 낱장 잎보다 성기게 심습니다.\n" +
                 "올릴수록 무성해지지만 그린 개수가 그대로 늘어납니다.")]
        public float Density = 5f;

        [Range(0f, 3f)]
        [Tooltip("해수면 위 이 높이까지가 물가입니다. 갈대는 여기에만 자랍니다.")]
        public float ReedBand = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("물가에서 갈대가 자랄 확률입니다. 1로 두면 띠가 너무 균질해집니다.")]
        public float ReedChance = 0.7f;

        [Range(0.2f, 1f)]
        [Tooltip("등반 한계에 대한 비율입니다. 이보다 가파르면 마른 잡초가 자랍니다.")]
        public float WeedSlopeRatio = 0.55f;

        [Range(0f, 1f)]
        [Tooltip("비탈에서 마른 잡초가 자랄 확률입니다.")]
        public float WeedChance = 0.65f;

        // ====================================================================================================
        // 2. Inspector - 크기 노이즈
        // ====================================================================================================

        [Header("크기 노이즈")]
        [Range(0f, 2f)]
        [Tooltip("종마다 적어 둔 크기 범위를 얼마나 넓게 쓸지입니다.\n" +
                 "0이면 모든 포기가 같은 크기가 되어 들판이 균질해지고,\n" +
                 "1이면 적어 둔 그대로, 그보다 크면 편차가 과장됩니다.\n" +
                 "가운데 값은 그대로 두고 <b>폭만</b> 조절하므로 들판이 전체적으로 커지거나 작아지지 않습니다.")]
        public float SizeVariation = 1f;

        // ====================================================================================================
        // 3. Inspector - 거리 감쇠
        // ====================================================================================================

        [Header("거리 감쇠")]
        [Tooltip("이 거리까지는 심은 대로 전부 그립니다. 줄이면 가까운 곳만 촘촘해집니다.")]
        public float FullDensityDistance = 22f;

        [Tooltip("이 거리에서 밀도가 최소가 됩니다.")]
        public float ThinDistance = 70f;

        [Range(0.02f, 1f)]
        [Tooltip("가장 멀 때 남길 비율입니다. 0에 가까우면 멀리서 들판이 사라지는 것이 보입니다.")]
        public float MinimumDensityRatio = 0.22f;

        // ====================================================================================================
        // 4. Inspector - 종
        // ====================================================================================================

        [Header("종")]
        [Tooltip("평지에 자라는 기본 풀입니다.")]
        public GrassSpeciesProfile Grass;

        [Tooltip("물가에 자라는 갈대입니다.")]
        public GrassSpeciesProfile Reed;

        [Tooltip("비탈과 마른 고지에 자라는 잡초입니다.")]
        public GrassSpeciesProfile Weed;

        // ====================================================================================================
        // 5. Factory
        // ====================================================================================================

        /// <summary>
        /// 에셋 없이 코드로 기본 들판을 만듭니다.
        ///
        /// <b>여기 적힌 값이 지금까지의 모습입니다.</b>
        /// 예전에 <c>GrassField</c> 안에 숫자로 박혀 있던 것을 그대로 옮겨 왔으므로,
        /// 프로필을 연결하지 않으면 겉모습이 전혀 달라지지 않습니다.
        /// </summary>
        /// <returns>기본값이 채워진 프로필입니다. 에셋으로 저장되지 않습니다.</returns>
        public static GrassProfile CreateDefault()
        {
            var profile = CreateInstance<GrassProfile>();
            profile.name = "GrassProfile_Default";
            profile.SchemaVersion = CurrentSchemaVersion;

            // 그림 한 장이 여러 갈래짜리 한 포기라, 낱장 잎보다 넓고 성깁니다.
            profile.Grass = new GrassSpeciesProfile
            {
                HeightRange = new Vector2(0.30f, 0.52f),
                WidthRange = new Vector2(0.30f, 0.52f),
                BaseColor = new Color(0.36f, 0.52f, 0.26f),
                TipColor = new Color(0.62f, 0.72f, 0.36f),
                DryColor = new Color(0.56f, 0.56f, 0.34f),
                PatchColorA = new Color(0.24f, 0.42f, 0.19f),
                PatchColorB = new Color(0.63f, 0.62f, 0.30f),
                TipBlend = 0.65f,
                RootShade = 0.45f,
                DryStrength = 0.55f,
                ColorJitter = 0.08f,
                ClusterScale = 0.12f,
                ClusterJitter = 0.18f,
                HueSpread = 0.35f,
                ColorCohesion = 0.25f,
                NormalRound = 0.55f,
                NormalTipUp = 0.5f,
                NormalScatter = 0.8f,
                Translucency = 0.22f,
                TranslucencyColor = new Color(0.72f, 0.85f, 0.35f),
                TranslucencyPower = 4f,
                TranslucencyRoot = 0.15f,
                ViewAlign = 1f,
                PitchAlign = 1f,
                FacingNoise = 18f,
                WindSwayAngle = 28f,
                AccentChance = 0.06f,
            };

            // 키가 크고 짙습니다. 물빛을 받아 조금 푸릅니다.
            // 얼룩을 거의 끄는 이유는 한 무리로 읽혀야 '물가'라는 신호가 되기 때문입니다.
            profile.Reed = new GrassSpeciesProfile
            {
                HeightRange = new Vector2(0.60f, 1.00f),
                WidthRange = new Vector2(0.34f, 0.52f),
                BaseColor = new Color(0.22f, 0.41f, 0.30f),
                TipColor = new Color(0.52f, 0.60f, 0.38f),
                DryColor = new Color(0.44f, 0.48f, 0.32f),
                PatchColorA = new Color(0.20f, 0.38f, 0.28f),
                PatchColorB = new Color(0.28f, 0.45f, 0.31f),
                TipBlend = 0.65f,
                RootShade = 0.45f,
                DryStrength = 0.55f,
                ColorJitter = 0.06f,
                ClusterScale = 0.16f,
                ClusterJitter = 0.12f,

                // 갈대는 한 무리로 읽혀야 물가라는 신호가 됩니다. 색조를 가장 좁게 묶습니다.
                HueSpread = 0.15f,
                ColorCohesion = 0.40f,

                // 갈대는 곧고 두꺼워 덜 둥글고 덜 통과합니다.
                NormalRound = 0.40f,
                NormalTipUp = 0.65f,
                NormalScatter = 0.6f,
                Translucency = 0.16f,
                TranslucencyColor = new Color(0.62f, 0.80f, 0.42f),
                TranslucencyPower = 5f,
                TranslucencyRoot = 0.10f,

                ViewAlign = 1f,
                PitchAlign = 1f,

                // 갈대는 곧게 뻗는 것이 물가의 신호라 덜 비틉니다.
                FacingNoise = 10f,

                // 키가 큰 만큼 바람을 크게 받습니다. 물가에서 갈대만 크게 눕습니다.
                WindSwayAngle = 46f,

                // 물가에 서는 것 자체가 이미 예외입니다. 그 안에서 또 예외를 두지 않습니다.
                AccentChance = 0f,
            };

            // 마르고 뻣뻣합니다. 거의 흔들리지 않아야 비탈이 메말라 보입니다.
            profile.Weed = new GrassSpeciesProfile
            {
                HeightRange = new Vector2(0.16f, 0.28f),
                WidthRange = new Vector2(0.26f, 0.42f),
                BaseColor = new Color(0.55f, 0.51f, 0.31f),
                TipColor = new Color(0.70f, 0.66f, 0.42f),
                DryColor = new Color(0.62f, 0.57f, 0.35f),
                PatchColorA = new Color(0.48f, 0.45f, 0.28f),
                PatchColorB = new Color(0.63f, 0.58f, 0.34f),
                TipBlend = 0.65f,
                RootShade = 0.45f,
                DryStrength = 0.55f,
                ColorJitter = 0.10f,
                ClusterScale = 0.10f,
                ClusterJitter = 0.22f,
                HueSpread = 0.45f,
                ColorCohesion = 0.20f,

                // 마른 잡초는 가장 얇습니다. 볕을 등지면 가장 환하게 비칩니다.
                NormalRound = 0.60f,
                NormalTipUp = 0.35f,
                NormalScatter = 0.9f,
                Translucency = 0.28f,
                TranslucencyColor = new Color(0.86f, 0.80f, 0.45f),
                TranslucencyPower = 3f,
                TranslucencyRoot = 0.20f,

                ViewAlign = 1f,
                PitchAlign = 1f,

                // 마른 잡초는 제멋대로 자랍니다. 가장 크게 비틉니다.
                FacingNoise = 32f,

                WindSwayAngle = 14f,
                AccentChance = 0f,
            };

            return profile;
        }

        /// <summary>
        /// 크기 범위에 <see cref="SizeVariation"/> 을 적용합니다.
        ///
        /// <b>가운데를 붙잡고 폭만 늘리거나 줄입니다.</b>
        /// 범위 전체에 배율을 곱하면 들판이 통째로 커지거나 작아져,
        /// "편차를 조절한다"가 아니라 "크기를 바꾼다"가 되어 버립니다.
        /// </summary>
        /// <summary>
        /// 스키마가 낡은 에셋에 새로 생긴 필드를 채웁니다.
        ///
        /// <b>손으로 맞춰 둔 값은 건드리지 않습니다.</b>
        /// 다만 <b>한 번도 손대지 않은 종</b>은 통째로 기본값으로 채웁니다 —
        /// 기본 풀만 만지고 갈대·잡초를 비워 둔 에셋이 실제로 그렇게 되어 있었고,
        /// 그 상태의 갈대는 검게 칠해지고 잡초는 크기가 0이라 보이지 않습니다.
        /// 비어 있는 것과 "일부러 검게 칠한 것"을 가르는 기준은 <b>기본색이 완전한 검정인가</b>입니다.
        /// 검은 풀을 의도하는 경우는 없습니다.
        /// </summary>
        /// <returns>실제로 갱신했으면 true입니다.</returns>
        public bool MigrateToCurrentSchema()
        {
            if (SchemaVersion >= CurrentSchemaVersion)
            {
                return false;
            }

            var template = CreateDefault();

            try
            {
                MigrateSpecies(ref Grass, template.Grass);
                MigrateSpecies(ref Reed, template.Reed);
                MigrateSpecies(ref Weed, template.Weed);
            }
            finally
            {
                DestroyImmediate(template);
            }

            SchemaVersion = CurrentSchemaVersion;
            return true;
        }

        /// <summary>
        /// 한 종의 빠진 값을 기본값으로 채웁니다.
        /// </summary>
        /// <param name="species">채울 종입니다.</param>
        /// <param name="template">그 종의 기본값입니다.</param>
        private static void MigrateSpecies(ref GrassSpeciesProfile species, in GrassSpeciesProfile template)
        {
            // 한 번도 손대지 않은 종입니다. 통째로 기본값을 씁니다.
            if (species.BaseColor.maxColorComponent <= 0.001f)
            {
                species = template;
                return;
            }

            // 버전 1에서 방향과 색 결속이 추가되었습니다.
            // 이 값들이 0이면 잎이 예전처럼 카메라 자리를 향하고 색 묶기가 꺼진 상태입니다.
            species.ViewAlign = template.ViewAlign;
            species.FacingNoise = template.FacingNoise;
            species.PitchAlign = template.PitchAlign;
            species.ClusterScale = template.ClusterScale;
            species.ClusterJitter = template.ClusterJitter;
            species.HueSpread = template.HueSpread;
            species.ColorCohesion = template.ColorCohesion;

            // 버전 2에서 잎 음영과 투과광이 추가되었습니다.
            // 0이면 노멀이 전 들판에서 같아져 금속판처럼 보이고, 투과광이 없어 플라스틱처럼 보입니다.
            species.NormalRound = template.NormalRound;
            species.NormalTipUp = template.NormalTipUp;
            species.NormalScatter = template.NormalScatter;
            species.Translucency = template.Translucency;
            species.TranslucencyColor = template.TranslucencyColor;
            species.TranslucencyPower = template.TranslucencyPower;
            species.TranslucencyRoot = template.TranslucencyRoot;
        }

        /// <summary>
        /// 에셋을 새로 만들거나 인스펙터에서 초기화할 때 기본값으로 채웁니다.
        ///
        /// <b>구조체라서 필요합니다.</b> 종 설정은 구조체라 필드 초기값을 가질 수 없고,
        /// 그대로 두면 새 에셋이 검고 크기가 0인 풀로 태어납니다.
        /// </summary>
        private void Reset()
        {
            var template = CreateDefault();

            try
            {
                Density = template.Density;
                ReedBand = template.ReedBand;
                ReedChance = template.ReedChance;
                WeedSlopeRatio = template.WeedSlopeRatio;
                WeedChance = template.WeedChance;
                SizeVariation = template.SizeVariation;
                FullDensityDistance = template.FullDensityDistance;
                ThinDistance = template.ThinDistance;
                MinimumDensityRatio = template.MinimumDensityRatio;

                Grass = template.Grass;
                Reed = template.Reed;
                Weed = template.Weed;
            }
            finally
            {
                DestroyImmediate(template);
            }

            SchemaVersion = CurrentSchemaVersion;
        }

        /// <param name="range">종에 적힌 최소·최대 범위입니다.</param>
        /// <returns>편차가 조절된 범위입니다.</returns>
        public Vector2 ApplyVariation(Vector2 range)
        {
            float mid = (range.x + range.y) * 0.5f;
            float half = (range.y - range.x) * 0.5f * Mathf.Max(0f, SizeVariation);

            return new Vector2(Mathf.Max(0.01f, mid - half), Mathf.Max(0.01f, mid + half));
        }
    }
}
