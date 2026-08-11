using System.Collections.Generic;
using SRPG.Data;
using SRPG.Gameplay.Visual;
using SRPG.Systems.Battlefield;
using UnityEngine;
using UnityEngine.Rendering;

namespace SRPG.Gameplay.Island
{
    /// <summary>
    /// 전장에 식생을 심고 매 프레임 인스턴싱으로 그립니다.
    ///
    /// <b>왜 게임오브젝트로 심지 않는가</b>
    ///
    /// 풀잎 하나를 오브젝트로 만들면 수만 개의 트랜스폼과 렌더러가 생깁니다.
    /// 계층 갱신만으로 프레임이 무너지고, 배칭도 나오지 않습니다.
    /// 여기서는 <b>뿌리 위치 행렬만</b> 들고 있다가 <see cref="Graphics.RenderMeshInstanced"/>로 넘깁니다.
    /// 색도 흔들림도 눌림도 셰이더가 월드 좌표에서 직접 계산하므로 넘길 것이 그것뿐입니다.
    ///
    /// <b>어디에 무엇이 자라는지가 곧 게임 규칙입니다</b>
    ///
    /// 물속과 절벽에는 심지 않습니다. 절벽의 기준은 <b>생성기가 절벽을 가르는 각도</b>이고,
    /// 지형 셰이더가 암반색을 칠하는 각도이기도 합니다.
    /// 셋이 같은 값을 쓰므로 <b>풀이 자란 곳은 걸을 수 있는 곳</b>이 됩니다.
    ///
    /// 종을 나누는 것도 같은 생각의 연장입니다.
    /// 갈대는 물가에만, 마른 잡초는 비탈과 마른 고지에만 자랍니다.
    /// 그래서 <b>무엇이 자랐는지가 그 땅이 어떤 땅인지를 말합니다</b>.
    ///
    /// <b>덩어리는 종마다 크기가 다릅니다</b>
    ///
    /// 모든 종을 같은 격자로 나누면, 드문 종은 거의 빈 덩어리마다 호출을 하나씩 뿌립니다.
    /// 스무 포기짜리 호출은 거의 전부가 부대 비용입니다.
    /// 덩어리 크기를 종의 개체 수에서 뽑으면 어느 종이든 한 호출이 꽉 찹니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GrassField : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>한 번의 인스턴싱 호출에 넘길 수 있는 최대 개수입니다.</summary>
        private const int BatchCapacity = 1023;

        /// <summary>
        /// 덩어리 하나가 목표로 하는 개체 수입니다.
        ///
        /// 한 호출의 한계에 절반쯤 채웁니다. 거리 감쇠로 솎아내도 호출이 여전히 알차고,
        /// 가까이서 전부 그릴 때도 한 덩어리가 두 호출로 쪼개지지 않습니다.
        /// </summary>
        private const int TargetPerChunk = 512;

        /// <summary>덩어리 격자의 한 변 최대 분할 수입니다. 너무 잘게 나누면 호출만 늘어납니다.</summary>
        private const int MaxChunksPerSide = 10;

        /// <summary>셰이더가 받아 줄 수 있는 눌림 지점의 최대 개수입니다.</summary>
        private const int TrampleCapacity = 32;

        /// <summary>해수면 위로 이만큼은 올라와야 식생이 자랍니다. 물가가 젖은 땅으로 남습니다.</summary>
        private const float WaterMargin = 0.15f;

        /// <summary>식생의 종 수입니다.</summary>
        private const int SpeciesCount = 3;

        /// <summary>기본 풀입니다.</summary>
        private const int SpeciesGrass = 0;
        /// <summary>물가의 갈대입니다.</summary>
        private const int SpeciesReed = 1;
        /// <summary>비탈과 마른 고지의 잡초입니다.</summary>
        private const int SpeciesWeed = 2;

        // ====================================================================================================
        // 2. Inspector
        // ====================================================================================================

        /// <summary>
        /// 이 들판의 생김새입니다. 밀도·크기 노이즈·음영이 전부 여기서 옵니다.
        ///
        /// <b>이 컴포넌트는 런타임에 붙습니다.</b>
        /// 그래서 여기에 직렬화 필드를 두어도 인스펙터에 뜰 기회가 없습니다 —
        /// 값을 바꾸려면 코드를 고쳐야 했고, 그것이 지금까지 들판을 손댈 수 없던 이유입니다.
        /// 전장을 세우는 쪽이 에셋을 넘겨 주고, 없으면 코드 기본값으로 채웁니다.
        /// </summary>
        private GrassProfile _profile;

        // ====================================================================================================
        // 3. Fields
        // ====================================================================================================

        /// <summary>풀 셰이더의 눌림 지점 배열 식별자입니다.</summary>
        private static readonly int TramplePointsId = Shader.PropertyToID("_TramplePoints");
        /// <summary>풀 셰이더의 눌림 지점 개수 식별자입니다.</summary>
        private static readonly int TrampleCountId = Shader.PropertyToID("_TrampleCount");

        // 프로필이 입히는 셰이더 값들입니다. 문자열을 매번 해싱하지 않도록 미리 잡아 둡니다.
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TipColorId = Shader.PropertyToID("_TipColor");
        private static readonly int DryColorId = Shader.PropertyToID("_DryColor");
        private static readonly int PatchColorAId = Shader.PropertyToID("_PatchColorA");
        private static readonly int PatchColorBId = Shader.PropertyToID("_PatchColorB");
        private static readonly int TipBlendId = Shader.PropertyToID("_TipBlend");
        private static readonly int RootShadeId = Shader.PropertyToID("_RootShade");
        private static readonly int DryStrengthId = Shader.PropertyToID("_DryStrength");
        private static readonly int ColorJitterId = Shader.PropertyToID("_ColorJitter");
        private static readonly int WindSwayAngleId = Shader.PropertyToID("_WindSwayAngle");
        private static readonly int AccentChanceId = Shader.PropertyToID("_AccentChance");
        private static readonly int ViewAlignId = Shader.PropertyToID("_ViewAlign");
        private static readonly int FacingNoiseId = Shader.PropertyToID("_FacingNoise");
        /// <summary>잎의 세로축을 카메라 쪽으로 눕히는 정도입니다.</summary>
        private static readonly int PitchAlignId = Shader.PropertyToID("_PitchAlign");
        private static readonly int ClusterScaleId = Shader.PropertyToID("_ClusterScale");
        private static readonly int ClusterJitterId = Shader.PropertyToID("_ClusterJitter");
        private static readonly int HueSpreadId = Shader.PropertyToID("_HueSpread");
        private static readonly int ColorCohesionId = Shader.PropertyToID("_ColorCohesion");
        private static readonly int NormalRoundId = Shader.PropertyToID("_NormalRound");
        private static readonly int NormalTipUpId = Shader.PropertyToID("_NormalTipUp");
        private static readonly int NormalScatterId = Shader.PropertyToID("_NormalScatter");
        private static readonly int TranslucencyId = Shader.PropertyToID("_Translucency");
        private static readonly int TranslucencyColorId = Shader.PropertyToID("_TranslucencyColor");
        private static readonly int TranslucencyPowerId = Shader.PropertyToID("_TranslucencyPower");
        private static readonly int TranslucencyRootId = Shader.PropertyToID("_TranslucencyRoot");

        /// <summary>종별 들판입니다.</summary>
        private readonly Species[] _species = new Species[SpeciesCount];

        /// <summary>식생을 눕히는 주체들입니다. 보통 유닛입니다.</summary>
        private readonly List<Transform> _tramplers = new List<Transform>();

        /// <summary>셰이더에 넘길 눌림 지점 버퍼입니다. 매 프레임 새로 만들지 않습니다.</summary>
        private readonly Vector4[] _trampleBuffer = new Vector4[TrampleCapacity];

        /// <summary>이번 프레임에 그릴 카메라들입니다.</summary>
        private Camera[] _cameraBuffer = new Camera[4];

        /// <summary>
        /// 카메라별 절두체 평면입니다. <b>프레임당 한 번만</b> 만듭니다.
        ///
        /// 예전에는 덩어리를 검사할 때마다 다시 만들었습니다. 덩어리가 예순 개면
        /// 같은 카메라의 같은 평면 여섯 장을 프레임마다 예순 번 다시 계산한 셈입니다.
        /// 평면은 카메라가 정하지 덩어리가 정하지 않으므로, 덩어리 루프에 들어가기 전에 한 번이면 됩니다.
        /// </summary>
        private Plane[][] _cameraFrustums = new Plane[4][];

        /// <summary>카메라별 위치입니다. 거리 감쇠에 씁니다. 프레임당 한 번만 읽습니다.</summary>
        private Vector3[] _cameraPositions = new Vector3[4];

        /// <summary>이번 프레임에 실제로 검사할 카메라 수입니다.</summary>
        private int _activeCameraCount;

        /// <summary>눌림 지점을 가까운 순으로 세울 때 기준이 되는 눈의 자리입니다.</summary>
        private Vector3 _sortEye;

        /// <summary>
        /// 눌림 주체를 가까운 순으로 세우는 비교자입니다.
        ///
        /// <b>미리 만들어 둡니다.</b> 람다가 바깥 변수를 붙잡으면 프레임마다
        /// 클로저와 델리게이트가 새로 생깁니다. 전투 내내 매 프레임이면 적지 않은 쓰레기입니다.
        /// 기준점을 필드로 옮기면 붙잡을 것이 없어져 한 번 만든 것을 계속 씁니다.
        /// </summary>
        private System.Comparison<Transform> _byDistanceToEye;

        /// <summary>식생을 눕히는 반경입니다. 유닛 몸집에서 나옵니다.</summary>
        private float _trampleRadius = 0.9f;

        /// <summary>덩어리 안의 순서를 섞는 난수입니다. 같은 전장은 늘 같은 순서여야 합니다.</summary>
        private System.Random _shuffle;

        /// <summary>심긴 식생의 총 개수입니다. 진단용입니다.</summary>
        public int BladeCount { get; private set; }

        /// <summary>덩어리의 총 개수입니다. 진단용입니다.</summary>
        public int ChunkCount { get; private set; }

        /// <summary>이번 프레임에 실제로 그린 개수입니다. 컬링과 거리 감쇠가 적용된 값입니다.</summary>
        public int DrawnBladeCount { get; private set; }

        /// <summary>이번 프레임에 나간 인스턴싱 호출 수입니다. 진단용입니다.</summary>
        public int DrawCallCount { get; private set; }

        /// <summary>이번 프레임에 화면 밖이라 건너뛴 덩어리 수입니다. 진단용입니다.</summary>
        public int CulledChunkCount { get; private set; }

        /// <summary>이번 프레임에 실제로 셰이더까지 간 눌림 지점의 수입니다. 진단용입니다.</summary>
        public int ActiveTrampleCount { get; private set; }

        /// <summary>종별로 심긴 개수입니다. 진단용입니다.</summary>
        public int[] SpeciesCounts { get; } = new int[SpeciesCount];

        /// <summary>한 덩어리 몫의 식생입니다.</summary>
        private sealed class Chunk
        {
            /// <summary>이 덩어리의 뿌리 행렬입니다.</summary>
            public Matrix4x4[] Matrices;
            /// <summary>컬링에 쓰는 경계입니다. 바람에 휘는 폭까지 넉넉히 잡습니다.</summary>
            public Bounds Bounds;
        }

        /// <summary>한 종의 들판입니다. 종마다 자기 격자와 자기 머티리얼을 듭니다.</summary>
        private sealed class Species
        {
            public Mesh Mesh;
            public Material Material;
            public Vector2 HeightRange;
            public Vector2 WidthRange;

            /// <summary>이 종의 덩어리들입니다.</summary>
            public readonly List<Chunk> Chunks = new List<Chunk>();

            /// <summary>매 프레임 새로 만들지 않기 위해 들고 있는 그리기 인자입니다.</summary>
            public RenderParams Parameters;
        }

        // ====================================================================================================
        // 4. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 전장에 식생을 심습니다. 이미 심겨 있으면 지우고 다시 심습니다.
        /// </summary>
        /// <param name="battlefield">식생을 심을 전장입니다. 높이와 등반 한계를 여기서 읽습니다.</param>
        /// <param name="profile">들판의 생김새입니다. 비우면 코드 기본값을 씁니다.</param>
        public void Build(Battlefield battlefield, GrassProfile profile = null)
        {
            // 프로필이 없으면 지금까지의 모습 그대로를 만들어 씁니다.
            _profile = profile != null ? profile : GrassProfile.CreateDefault();

            BladeCount = 0;
            ChunkCount = 0;
            System.Array.Clear(SpeciesCounts, 0, SpeciesCounts.Length);

            for (int i = 0; i < SpeciesCount; i++)
            {
                _species[i]?.Chunks.Clear();
            }

            if (battlefield == null)
            {
                return;
            }

            if (!CreateSpecies(battlefield))
            {
                // 셰이더가 없으면 식생 없이 진행합니다. 셰이더 하나 때문에 실행이 막히면 안 됩니다.
                return;
            }

            Scatter(battlefield);
        }

        /// <summary>
        /// 식생을 눕히는 주체를 등록합니다. 보통 유닛의 트랜스폼입니다.
        /// </summary>
        /// <param name="trampler">등록할 트랜스폼입니다.</param>
        /// <param name="radius">이 주체가 식생을 눕히는 반경입니다.</param>
        public void RegisterTrampler(Transform trampler, float radius)
        {
            if (trampler == null || _tramplers.Contains(trampler))
            {
                return;
            }

            _tramplers.Add(trampler);
            _trampleRadius = Mathf.Max(_trampleRadius, radius);
        }

        // ====================================================================================================
        // 5. Unity Lifecycle
        // ====================================================================================================

        private void LateUpdate()
        {
            if (_species[SpeciesGrass] == null || _species[SpeciesGrass].Material == null)
            {
                return;
            }

            UpdateTramplePoints();
            Draw();
        }

        // ====================================================================================================
        // 6. Private Methods - Species
        // ====================================================================================================

        /// <summary>
        /// 종별 메시와 머티리얼을 만듭니다.
        ///
        /// 셋 다 같은 셰이더를 씁니다 — 조명도 바람도 눌림도 구름도 같아야 하기 때문입니다.
        /// 다른 것은 잎의 모양과 색뿐입니다.
        /// </summary>
        /// <returns>셰이더를 찾아 준비를 마쳤으면 참입니다.</returns>
        private bool CreateSpecies(Battlefield battlefield)
        {
            float heightSpan = battlefield.Heightmap.MaxElevation - battlefield.Heightmap.SeaLevel;

            for (int index = 0; index < SpeciesCount; index++)
            {
                var material = PrototypeVisuals.CreateGrassMaterial(battlefield.SeaLevel, heightSpan);

                if (material == null)
                {
                    return false;
                }

                _species[index] ??= new Species();
                _species[index].Material = material;
            }

            // 세 종이 <b>같은 사각형</b>을 씁니다.
            //
            // 잎의 윤곽은 이제 그림이 듭니다. 종을 가르는 것은 그림·색·크기뿐이라
            // 메시를 종마다 따로 둘 이유가 사라졌습니다.
            var quad = PrototypeVisuals.GetGroundedQuadMesh();

            for (int index = 0; index < SpeciesCount; index++)
            {
                _species[index].Mesh = quad;
            }

            // 갈대는 <b>강조풀 그림</b>을 기본으로 씁니다.
            // 갈래가 여럿이고 곧게 뻗어, 키를 늘이면 그대로 갈대로 읽힙니다.
            var accentSprite = PrototypeVisuals.GetAccentSprite();

            if (accentSprite != null)
            {
                _species[SpeciesReed].Material.SetTexture(BaseMapId, accentSprite);
            }

            _species[SpeciesGrass].Material.name = "Grass_Runtime";
            _species[SpeciesReed].Material.name = "Reed_Runtime";
            _species[SpeciesWeed].Material.name = "Weed_Runtime";

            ApplySpecies(_species[SpeciesGrass], _profile.Grass);
            ApplySpecies(_species[SpeciesReed], _profile.Reed);
            ApplySpecies(_species[SpeciesWeed], _profile.Weed);

            for (int index = 0; index < SpeciesCount; index++)
            {
                // 그리기 인자는 덩어리마다 경계만 바뀝니다. 나머지는 여기서 한 번 세웁니다.
                _species[index].Parameters = new RenderParams(_species[index].Material)
                {
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = gameObject.layer,
                    camera = null,
                };
            }

            return true;
        }

        /// <summary>
        /// 프로필에 적힌 생김새를 한 종에 입힙니다.
        ///
        /// <b>크기 범위에는 편차 배율이 함께 걸립니다.</b>
        /// 종마다 적어 둔 범위가 기준이고, 전장 단위로 그 폭을 넓히거나 좁힐 수 있어야
        /// "이 전장은 유난히 고르다" 같은 것을 에셋만으로 만들 수 있습니다.
        /// </summary>
        /// <param name="species">값을 받을 종입니다.</param>
        /// <param name="profile">그 종의 생김새입니다.</param>
        private void ApplySpecies(Species species, in GrassSpeciesProfile profile)
        {
            species.HeightRange = _profile.ApplyVariation(profile.HeightRange);
            species.WidthRange = _profile.ApplyVariation(profile.WidthRange);

            var material = species.Material;

            material.SetColor(BaseColorId, profile.BaseColor);
            material.SetColor(TipColorId, profile.TipColor);
            material.SetColor(DryColorId, profile.DryColor);
            material.SetColor(PatchColorAId, profile.PatchColorA);
            material.SetColor(PatchColorBId, profile.PatchColorB);

            material.SetFloat(TipBlendId, profile.TipBlend);
            material.SetFloat(RootShadeId, profile.RootShade);
            material.SetFloat(DryStrengthId, profile.DryStrength);
            material.SetFloat(ColorJitterId, profile.ColorJitter);
            material.SetFloat(WindSwayAngleId, profile.WindSwayAngle);
            material.SetFloat(AccentChanceId, profile.AccentChance);
            material.SetFloat(ViewAlignId, profile.ViewAlign);
            material.SetFloat(FacingNoiseId, profile.FacingNoise);
            material.SetFloat(PitchAlignId, profile.PitchAlign);
            material.SetFloat(ClusterScaleId, profile.ClusterScale);
            material.SetFloat(ClusterJitterId, profile.ClusterJitter);
            material.SetFloat(HueSpreadId, profile.HueSpread);
            material.SetFloat(ColorCohesionId, profile.ColorCohesion);
            material.SetFloat(NormalRoundId, profile.NormalRound);
            material.SetFloat(NormalTipUpId, profile.NormalTipUp);
            material.SetFloat(NormalScatterId, profile.NormalScatter);
            material.SetFloat(TranslucencyId, profile.Translucency);
            material.SetColor(TranslucencyColorId, profile.TranslucencyColor);
            material.SetFloat(TranslucencyPowerId, profile.TranslucencyPower);
            material.SetFloat(TranslucencyRootId, profile.TranslucencyRoot);
        }

        /// <summary>
        /// 이 자리에 무엇이 자랄지 정합니다.
        ///
        /// <b>땅이 종을 정합니다.</b> 그 반대가 아닙니다.
        /// 확률을 섞는 것은 경계가 자로 그은 듯 갈리지 않게 하기 위해서입니다.
        /// </summary>
        private int PickSpecies(float heightAboveSea, float slopeDegrees, float climbLimit, double roll)
        {
            if (heightAboveSea < _profile.ReedBand)
            {
                return roll < _profile.ReedChance ? SpeciesReed : SpeciesGrass;
            }

            if (slopeDegrees > climbLimit * _profile.WeedSlopeRatio)
            {
                return roll < _profile.WeedChance ? SpeciesWeed : SpeciesGrass;
            }

            return SpeciesGrass;
        }

        // ====================================================================================================
        // 7. Private Methods - Scatter
        // ====================================================================================================

        /// <summary>
        /// 심을 자리를 정합니다.
        ///
        /// <b>두 번에 나눠 합니다.</b> 먼저 종별로 자리를 모두 모으고,
        /// 그 개수를 안 뒤에 종마다 격자를 정합니다.
        /// 개수를 모른 채 격자를 먼저 정하면 드문 종이 빈 칸을 잔뜩 만듭니다.
        /// </summary>
        private void Scatter(Battlefield battlefield)
        {
            var heightmap = battlefield.Heightmap;

            Vector3 origin = battlefield.Origin;
            float worldSize = heightmap.WorldSize;

            float seaLevel = battlefield.SeaLevel;
            float climbLimit = battlefield.ClimbLimitDegrees;

            float spacing = 1f / Mathf.Sqrt(Mathf.Max(_profile.Density, 0.01f));
            int steps = Mathf.Max(1, Mathf.RoundToInt(worldSize / spacing));

            // 같은 전장은 늘 같은 들판이어야 합니다.
            int seed = battlefield.Grid.AllTiles.Count * 7919 + steps;

            var random = new System.Random(seed);
            _shuffle = new System.Random(seed + 104729);

            var collected = new List<Matrix4x4>[SpeciesCount];

            for (int index = 0; index < SpeciesCount; index++)
            {
                collected[index] = new List<Matrix4x4>(4096);
            }

            for (int gz = 0; gz < steps; gz++)
            {
                for (int gx = 0; gx < steps; gx++)
                {
                    float jitterX = (float)random.NextDouble() - 0.5f;
                    float jitterZ = (float)random.NextDouble() - 0.5f;

                    float worldX = origin.x + (gx + 0.5f + jitterX) * spacing;
                    float worldZ = origin.z + (gz + 0.5f + jitterZ) * spacing;

                    float groundY = heightmap.SampleHeight(worldX, worldZ, origin);

                    // 물속에는 심지 않습니다.
                    if (groundY < seaLevel + WaterMargin)
                    {
                        continue;
                    }

                    float slope = heightmap.SampleSlopeDegrees(worldX, worldZ, origin);

                    // 절벽에는 심지 않습니다. 생성기가 절벽을 가르는 각도와 같아야 합니다.
                    if (slope > climbLimit)
                    {
                        continue;
                    }

                    int index = PickSpecies(groundY - seaLevel, slope, climbLimit, random.NextDouble());

                    var species = _species[index];

                    float height = Mathf.Lerp(species.HeightRange.x, species.HeightRange.y, (float)random.NextDouble());
                    float width = Mathf.Lerp(species.WidthRange.x, species.WidthRange.y, (float)random.NextDouble());

                    collected[index].Add(Matrix4x4.TRS(
                        new Vector3(worldX, groundY, worldZ),
                        Quaternion.identity,
                        new Vector3(width, height, 1f)));

                    SpeciesCounts[index]++;
                }
            }

            for (int index = 0; index < SpeciesCount; index++)
            {
                BuildChunks(_species[index], collected[index], origin, worldSize);
                BladeCount += collected[index].Count;
                ChunkCount += _species[index].Chunks.Count;
            }
        }

        /// <summary>
        /// 한 종의 자리들을 덩어리로 묶습니다.
        ///
        /// <b>격자는 개체 수에서 나옵니다.</b>
        ///
        /// 모든 종에 같은 격자를 쓰면, 육백 포기짜리 종도 예순 칸에 흩어져
        /// 열 포기짜리 호출을 예순 번 냅니다. 거의 전부가 부대 비용입니다.
        /// 개체 수를 목표치로 나눠 격자를 정하면 어느 종이든 호출이 꽉 찹니다.
        /// </summary>
        private void BuildChunks(Species species, List<Matrix4x4> matrices, Vector3 origin, float worldSize)
        {
            species.Chunks.Clear();

            if (matrices.Count == 0)
            {
                return;
            }

            int desiredChunks = Mathf.Max(1, Mathf.CeilToInt(matrices.Count / (float)TargetPerChunk));
            int chunksPerSide = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(desiredChunks)), 1, MaxChunksPerSide);

            float chunkSize = worldSize / chunksPerSide;

            var buckets = new List<Matrix4x4>[chunksPerSide * chunksPerSide];

            for (int i = 0; i < matrices.Count; i++)
            {
                Vector3 position = matrices[i].GetColumn(3);

                int chunkX = Mathf.Clamp((int)((position.x - origin.x) / chunkSize), 0, chunksPerSide - 1);
                int chunkZ = Mathf.Clamp((int)((position.z - origin.z) / chunkSize), 0, chunksPerSide - 1);

                int bucket = chunkZ * chunksPerSide + chunkX;

                buckets[bucket] ??= new List<Matrix4x4>(TargetPerChunk);
                buckets[bucket].Add(matrices[i]);
            }

            float tallest = species.HeightRange.y;

            for (int index = 0; index < buckets.Length; index++)
            {
                var bucket = buckets[index];

                if (bucket == null || bucket.Count == 0)
                {
                    continue;
                }

                // <b>순서를 섞습니다.</b>
                // 거리 감쇠는 앞에서부터 일부만 그리는 방식으로 솎아냅니다.
                // 심은 순서는 격자 순서라, 섞지 않고 잘라 내면 한쪽 띠가 통째로 비어 경계가 보입니다.
                for (int i = bucket.Count - 1; i > 0; i--)
                {
                    int swap = _shuffle.Next(i + 1);
                    (bucket[i], bucket[swap]) = (bucket[swap], bucket[i]);
                }

                var bounds = new Bounds(bucket[0].GetColumn(3), Vector3.zero);

                for (int i = 1; i < bucket.Count; i++)
                {
                    bounds.Encapsulate(bucket[i].GetColumn(3));
                }

                // 식생은 위로 자라고 바람에 옆으로 휩니다.
                // 뿌리 위치만으로 경계를 잡으면 화면 가장자리에서 덩어리가 통째로 사라집니다.
                bounds.Expand(new Vector3(1.5f, tallest * 2f, 1.5f));

                species.Chunks.Add(new Chunk
                {
                    Matrices = bucket.ToArray(),
                    Bounds = bounds,
                });
            }
        }

        // ====================================================================================================
        // 8. Private Methods - Draw
        // ====================================================================================================

        /// <summary>
        /// 이번 프레임에 식생을 눕힐 지점을 셰이더에 넘깁니다.
        ///
        /// 종마다 머티리얼이 다르므로 <b>전부에게</b> 넘겨야 합니다.
        /// 하나만 넘기면 갈대는 눕는데 그 옆의 풀은 서 있습니다.
        /// </summary>
        private void UpdateTramplePoints()
        {
            // 쓰러진 병사의 트랜스폼은 파괴됩니다. 걷어 내지 않으면 목록이 전투 내내 불어나고,
            // 무엇보다 <b>정렬이 터집니다</b> — 파괴된 항목을 0으로 비교하면 비교자가
            // 일관성을 잃고, List.Sort 가 그것을 감지해 예외를 던집니다.
            _tramplers.RemoveAll(trampler => trampler == null);

            var viewer = Camera.main;

            if (viewer != null && _tramplers.Count > 1)
            {
                _sortEye = viewer.transform.position;
                _byDistanceToEye ??= CompareByDistanceToEye;

                // 가까운 것부터 채웁니다. 먼 곳의 눌림은 화면에서 읽히지 않습니다.
                _tramplers.Sort(_byDistanceToEye);
            }

            int count = Mathf.Min(_tramplers.Count, TrampleCapacity);

            for (int i = 0; i < count; i++)
            {
                Vector3 position = _tramplers[i].position;
                _trampleBuffer[i] = new Vector4(position.x, position.y, position.z, _trampleRadius);
            }

            // 남은 칸은 비웁니다. 개수만큼만 돌지만 옛 값이 남으면 유령이 생깁니다.
            for (int i = count; i < TrampleCapacity; i++)
            {
                _trampleBuffer[i] = Vector4.zero;
            }

            // <b>전역으로 한 번만 올립니다.</b>
            // 셰이더가 이 둘을 UnityPerMaterial 밖에 선언해 둔 것이 그 뜻입니다 —
            // 머티리얼의 성질이 아니라 이번 프레임의 부대 위치이기 때문입니다.
            // 종마다 올리면 같은 배열을 종 수만큼 반복해서 밀어 넣게 됩니다.
            Shader.SetGlobalVectorArray(TramplePointsId, _trampleBuffer);
            Shader.SetGlobalInteger(TrampleCountId, count);

            ActiveTrampleCount = count;
        }

        /// <summary>
        /// 두 눌림 주체를 눈에서 가까운 순으로 견줍니다.
        /// </summary>
        /// <param name="a">왼쪽 주체입니다.</param>
        /// <param name="b">오른쪽 주체입니다.</param>
        /// <returns>가까운 쪽이 앞서도록 하는 비교 결과입니다.</returns>
        private int CompareByDistanceToEye(Transform a, Transform b)
        {
            float left = (a.position - _sortEye).sqrMagnitude;
            float right = (b.position - _sortEye).sqrMagnitude;

            return left.CompareTo(right);
        }

        /// <summary>
        /// 보이는 덩어리만 그립니다.
        ///
        /// <b>왜 직접 걸러 내는가</b>
        ///
        /// <see cref="RenderParams.worldBounds"/>도 컬링을 하지만, 그것은 호출이 나간 <b>뒤</b>입니다.
        /// 화면 밖 덩어리도 호출 비용은 그대로 냅니다.
        /// 여기서 미리 걸러 내면 그 비용이 아예 발생하지 않습니다.
        ///
        /// <b>왜 카메라 하나가 아니라 전부인가</b>
        ///
        /// 예전에 <see cref="Camera.main"/> 하나로 걸렀다가, 부 카메라로 찍을 때
        /// 들판이 통째로 사라졌습니다. 어느 카메라 하나에라도 보이면 넘깁니다 —
        /// 최종 판정은 여전히 유니티가 카메라마다 worldBounds 로 합니다.
        /// </summary>
        private void Draw()
        {
            DrawnBladeCount = 0;
            DrawCallCount = 0;
            CulledChunkCount = 0;

            // 카메라의 절두체와 자리를 먼저 확정합니다.
            // 이것들은 덩어리마다 달라지지 않으므로 루프 밖에서 한 번이면 충분합니다.
            PrepareCameras();

            if (_activeCameraCount == 0)
            {
                return;
            }

            for (int index = 0; index < SpeciesCount; index++)
            {
                var species = _species[index];

                if (species == null || species.Chunks.Count == 0)
                {
                    continue;
                }

                for (int c = 0; c < species.Chunks.Count; c++)
                {
                    var chunk = species.Chunks[c];

                    float ratio = Visibility(chunk);

                    if (ratio <= 0f)
                    {
                        CulledChunkCount++;
                        continue;
                    }

                    int drawCount = Mathf.CeilToInt(chunk.Matrices.Length * ratio);

                    if (drawCount <= 0)
                    {
                        continue;
                    }

                    DrawnBladeCount += drawCount;

                    species.Parameters.worldBounds = chunk.Bounds;

                    // 한 번에 넘길 수 있는 수가 정해져 있으므로 나눠 보냅니다.
                    for (int start = 0; start < drawCount; start += BatchCapacity)
                    {
                        int count = Mathf.Min(BatchCapacity, drawCount - start);

                        Graphics.RenderMeshInstanced(species.Parameters, species.Mesh, 0, chunk.Matrices, count, start);
                        DrawCallCount++;
                    }
                }
            }
        }

        /// <summary>
        /// 이번 프레임에 그림을 받을 카메라들의 절두체와 자리를 확정합니다.
        ///
        /// <b>덩어리 루프에 들어가기 전에 한 번만 부릅니다.</b>
        /// 절두체는 카메라가 정하는 것이라 덩어리마다 다시 만들 이유가 없습니다.
        /// </summary>
        private void PrepareCameras()
        {
            int count = Camera.allCamerasCount;

            if (count > _cameraBuffer.Length)
            {
                _cameraBuffer = new Camera[count];
                _cameraFrustums = new Plane[count][];
                _cameraPositions = new Vector3[count];
            }

            count = Camera.GetAllCameras(_cameraBuffer);
            _activeCameraCount = 0;

            for (int i = 0; i < count; i++)
            {
                var camera = _cameraBuffer[i];

                if (camera == null)
                {
                    continue;
                }

                _cameraFrustums[_activeCameraCount] ??= new Plane[6];

                GeometryUtility.CalculateFrustumPlanes(camera, _cameraFrustums[_activeCameraCount]);
                _cameraPositions[_activeCameraCount] = camera.transform.position;

                _activeCameraCount++;
            }
        }

        /// <summary>
        /// 이 덩어리를 얼마나 촘촘히 그릴지 정합니다. 어느 카메라에도 보이지 않으면 0입니다.
        ///
        /// 여러 카메라에 보이면 <b>가장 가까운</b> 카메라 기준으로 정합니다.
        /// 먼 카메라에 맞추면 가까운 화면에서 들판이 성기게 보입니다.
        /// </summary>
        private float Visibility(Chunk chunk)
        {
            // 감쇠 구간은 카메라와 무관합니다. 루프 안에서 다시 구할 이유가 없습니다.
            float near = Mathf.Min(_profile.FullDensityDistance, _profile.ThinDistance - 0.01f);

            float best = 0f;

            for (int i = 0; i < _activeCameraCount; i++)
            {
                if (!GeometryUtility.TestPlanesAABB(_cameraFrustums[i], chunk.Bounds))
                {
                    continue;
                }

                float distance = Vector3.Distance(chunk.Bounds.center, _cameraPositions[i]);
                float t = Mathf.InverseLerp(near, _profile.ThinDistance, distance);

                best = Mathf.Max(best, Mathf.Lerp(1f, _profile.MinimumDensityRatio, t));

                // 가장 가까운 카메라 기준이면 충분한데, 1은 그보다 좋아질 수 없습니다.
                if (best >= 1f)
                {
                    break;
                }
            }

            return best;
        }
    }
}
