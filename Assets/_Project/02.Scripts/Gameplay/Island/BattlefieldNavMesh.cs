using System.Collections.Generic;
using SRPG.Systems.Battlefield;
using UnityEngine;
using UnityEngine.AI;

namespace SRPG.Gameplay.Island
{
    /// <summary>
    /// 전장이 다 서고 나면 그 위에 길을 굽습니다.
    ///
    /// <b>왜 실행 중에 굽는가</b>
    ///
    /// 전장은 판마다 새로 만들어집니다 — 씨앗이 다르면 지형이 다르고, 강줄기도 바위 자리도
    /// 그때 정해집니다. 미리 구워 둘 대상이 아예 없습니다.
    ///
    /// <b>왜 NavMeshSurface 를 쓰지 않는가</b>
    ///
    /// 그 컴포넌트는 반경·키·기울기를 <b>프로젝트 설정의 에이전트 타입</b>에서 가져옵니다.
    /// 실행 중에 그 값을 바꿀 방법이 없습니다 — <c>NavMesh.GetSettingsByID</c> 는 복사본을
    /// 돌려주므로 고쳐 봐야 아무 데도 반영되지 않습니다.
    ///
    /// 그런데 <b>오를 수 있는 기울기는 전장마다 다릅니다.</b> 격자가 쓰는 값과 어긋나면
    /// "길은 있는데 격자는 막혔다"(또는 그 반대)가 생기고, 그런 자리는 화면에 드러나지 않습니다.
    /// 빌더를 직접 부르면 굽는 값을 전부 여기서 정할 수 있습니다.
    ///
    /// <b>무엇을 길이 아닌 곳으로 도려내는가</b>
    ///
    /// <b>물</b> — 지형은 물 밑으로도 이어집니다(바다 바닥이 그렇게 만들어져 있습니다).
    /// 그대로 두면 길이 강바닥을 따라 이어져 병사가 물속을 걸어 건넙니다.
    /// 해수면 아래를 상자 하나로 통째로 도려냅니다. 여울은 수면 위로 나와 있으므로 살아남습니다 —
    /// <b>도하 지점이 지형에서 저절로 나오는</b> 셈이고, 따로 표시하지 않아도 됩니다.
    ///
    /// <b>바위</b> — 바위 꼭대기는 평평해서 길이 얹힐 수 있습니다. 표시를 걸어 도려냅니다.
    /// </summary>
    public static class BattlefieldNavMesh
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// 병사 한 사람의 반경입니다(월드 거리).
        ///
        /// 실제 몸보다 <b>작게</b> 잡습니다. 이 값은 "길이 벽에서 얼마나 떨어지는가"를 정하는데,
        /// 크게 잡으면 좁은 통로가 통째로 사라져 지나갈 수 있는 곳을 못 지나갑니다.
        /// 서로 부딪히지 않게 하는 것은 길이 아니라 조향의 분리 성분이 맡습니다.
        /// </summary>
        public const float AgentRadius = 0.3f;

        /// <summary>병사의 키입니다. 이 높이가 안 나오면 지나갈 수 없는 것으로 봅니다.</summary>
        public const float AgentHeight = 1.8f;

        /// <summary>그냥 넘어갈 수 있는 턱의 높이입니다.</summary>
        public const float AgentClimb = 0.4f;

        /// <summary>
        /// 복셀 한 칸의 크기입니다. 작을수록 길이 지형을 잘 따라가고 굽는 데 오래 걸립니다.
        /// 반경의 1/3 은 유니티가 권하는 비율입니다.
        /// 이보다 굵으면 여울처럼 <b>좁은 통로가 통째로 사라집니다</b>.
        /// </summary>
        private const float VoxelSize = AgentRadius / 3f;

        /// <summary>
        /// 이보다 작은 길 조각은 버립니다(제곱 월드 거리).
        ///
        /// 바위 사이나 물가에 손바닥만 한 섬이 생깁니다. 거기 올라선 병사는
        /// 어디로도 갈 수 없어 <b>정확히 예전의 갇힘이 재현됩니다</b>.
        /// </summary>
        private const float MinRegionArea = 4f;

        /// <summary>유니티가 정해 둔 "갈 수 없음" 영역 번호입니다.</summary>
        private const int NotWalkableArea = 1;

        /// <summary>기본으로 걸어 다닐 수 있는 영역 번호입니다.</summary>
        private const int WalkableArea = 0;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        /// <summary>지금 깔려 있는 길입니다. 다음 판을 굽기 전에 거둡니다.</summary>
        private static NavMeshDataInstance _instance;

        /// <summary>모으는 자리를 재사용합니다. 판마다 새로 할당할 이유가 없습니다.</summary>
        private static readonly List<NavMeshBuildSource> Sources = new List<NavMeshBuildSource>(256);

        /// <summary>표시(도려낼 것) 목록입니다.</summary>
        private static readonly List<NavMeshBuildMarkup> Markups = new List<NavMeshBuildMarkup>(4);

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 이번에 굽는 데 쓰는 설정입니다.
        ///
        /// <b>기울기는 전장이 정합니다.</b> 격자가 쓰는 값과 같아야 통행 규칙이 한 벌로 남습니다.
        /// 검사가 이 값을 들여다볼 수 있어야 해서 밖으로 냅니다 —
        /// 구워진 뒤에는 유니티에 되물을 방법이 없습니다.
        /// </summary>
        /// <param name="battlefield">기준이 되는 전장입니다.</param>
        /// <returns>굽는 데 쓸 설정입니다.</returns>
        public static NavMeshBuildSettings SettingsFor(Battlefield battlefield)
        {
            // 0번 타입의 설정을 밑바탕으로 삼습니다. 돌려받는 것이 복사본이라
            // 여기서 고쳐도 프로젝트 설정은 그대로이고, 굽는 데만 쓰입니다.
            var settings = NavMesh.GetSettingsByID(0);

            settings.agentRadius = AgentRadius;
            settings.agentHeight = AgentHeight;
            settings.agentClimb = AgentClimb;
            settings.agentSlope = battlefield != null ? battlefield.ClimbLimitDegrees : settings.agentSlope;

            settings.overrideVoxelSize = true;
            settings.voxelSize = VoxelSize;

            settings.minRegionArea = MinRegionArea;

            return settings;
        }

        /// <summary>
        /// 전장 위에 길을 굽습니다.
        ///
        /// <b>지형·물·바위가 모두 선 뒤에 불러야 합니다.</b>
        /// 길은 그 자리에 있는 것을 보고 구워지므로, 하나라도 늦게 서면 그것이 빠진 길이 됩니다.
        /// </summary>
        /// <param name="root">전장이 매달린 오브젝트입니다.</param>
        /// <param name="battlefield">길을 구울 전장입니다.</param>
        /// <returns>구웠으면 true 입니다.</returns>
        public static bool Bake(Transform root, Battlefield battlefield)
        {
            if (root == null || battlefield == null)
            {
                return false;
            }

            Clear();

            var bounds = WorldBounds(battlefield);

            CollectGeometry(root, bounds);
            AddWaterCarve(battlefield);

            var data = NavMeshBuilder.BuildNavMeshData(
                SettingsFor(battlefield),
                Sources,
                bounds,
                Vector3.zero,
                Quaternion.identity);

            if (data == null)
            {
                Debug.LogWarning(
                    $"[Nav] 길을 굽지 못했습니다. 모은 것 {Sources.Count}개, 범위 {bounds}.");

                return false;
            }

            data.name = "BattlefieldNavMesh";

            _instance = NavMesh.AddNavMeshData(data);

            Report(bounds);

            return _instance.valid;
        }

        /// <summary>
        /// 구운 결과를 한 줄로 남깁니다.
        ///
        /// <b>왜 로그가 필요한가</b>
        ///
        /// 길은 눈에 보이지 않습니다. 씬 뷰의 표시를 켜야 겨우 보이고, 실행 중에 구워지므로
        /// 편집기에서 미리 볼 수도 없습니다. 잘못 구워졌을 때 드러나는 방식은 하나뿐입니다 —
        /// <b>병사가 이상하게 움직입니다.</b> 그리고 그 증상은 "길이 안 구워졌다",
        /// "길은 구워졌는데 안 따라간다", "따라가는데 길이 틀렸다"를 구별해 주지 않습니다.
        ///
        /// 세 갈래를 가르는 데 필요한 것은 <b>실제로 몇 개가 구워졌는가</b> 한 줄입니다.
        /// </summary>
        /// <param name="bounds">이번에 구운 범위입니다.</param>
        private static void Report(Bounds bounds)
        {
            var triangulation = NavMesh.CalculateTriangulation();

            int triangles = triangulation.indices.Length / 3;

            if (!_instance.valid || triangles == 0)
            {
                Debug.LogWarning(
                    $"[Nav] 길이 비었습니다 — 모은 것 {Sources.Count}개, 삼각형 {triangles}개, 범위 {bounds}.\n" +
                    "지형이 수집되지 않았거나 전부 도려내졌습니다. 병사는 예전처럼 직선으로 움직입니다.");

                return;
            }

            Debug.Log($"[Nav] 길을 구웠습니다 — 모은 것 {Sources.Count}개, 삼각형 {triangles}개, 범위 {bounds.size}.");
        }

        /// <summary>
        /// 깔아 둔 길을 거둡니다.
        ///
        /// 길은 씬이 아니라 <b>전역</b>에 남습니다. 거두지 않으면 다음 판이
        /// 지난 판의 지형 위에 겹쳐 깔린 길을 함께 보게 됩니다.
        /// </summary>
        public static void Clear()
        {
            if (_instance.valid)
            {
                NavMesh.RemoveNavMeshData(_instance);
            }

            _instance = default;
        }

        // ====================================================================================================
        // 4. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 길을 구울 월드 범위를 구합니다.
        ///
        /// <b>노는 땅만 봅니다.</b> 지형은 앞바다 바닥까지 이어져 있어서 전체를 훑으면
        /// 대부분을 물속이라고 버리면서 굽는 시간만 넉 배로 씁니다.
        /// </summary>
        /// <param name="battlefield">기준이 되는 전장입니다.</param>
        /// <returns>구울 범위입니다.</returns>
        private static Bounds WorldBounds(Battlefield battlefield)
        {
            float extent = battlefield.PlayWorldSize;
            float height = battlefield.Heightmap.MaxElevation;

            var center = new Vector3(
                battlefield.PlayOrigin.x + extent * 0.5f,
                battlefield.Origin.y + height * 0.5f,
                battlefield.PlayOrigin.z + extent * 0.5f);

            // 높이는 넉넉히 잡습니다. 여기서 잘리면 산마루나 골짜기가 통째로 빠집니다.
            return new Bounds(center, new Vector3(extent, height * 2f + AgentHeight * 2f, extent));
        }

        /// <summary>
        /// 지형과 바위를 모읍니다.
        ///
        /// <b>콜라이더가 아니라 그리는 메시를 봅니다.</b>
        /// 바위에는 콜라이더가 없습니다 — 통행은 격자가, 클릭은 지형 레이어가 맡아 왔기 때문입니다.
        /// 콜라이더를 기준으로 모으면 바위가 길을 막지 못합니다.
        /// </summary>
        /// <param name="root">전장이 매달린 오브젝트입니다.</param>
        /// <param name="bounds">모을 범위입니다.</param>
        private static void CollectGeometry(Transform root, Bounds bounds)
        {
            Markups.Clear();

            // 바위는 꼭대기가 평평해서 길이 얹힐 수 있습니다. 갈 수 없는 곳으로 표시합니다.
            //
            // 바위는 1.6미터로 넘어갈 수 있는 턱보다 훨씬 높아, 아래 지면과 한 덩어리로
            // 묶이지 않습니다. 그래서 표시로 도려내도 바위가 <b>선 자리만</b> 사라집니다.
            AddCarveMarkup(root.Find("Obstacles"));

            Sources.Clear();

            NavMeshBuilder.CollectSources(
                bounds,
                ~0,
                NavMeshCollectGeometry.RenderMeshes,
                WalkableArea,
                Markups,
                Sources);

            Exclude(root.Find("Water"));
            Exclude(root.Find("Grass"));
        }

        /// <summary>
        /// 이 가지에서 나온 것을 모은 것에서 아예 빼 버립니다.
        ///
        /// <b>왜 "갈 수 없음"으로 표시하지 않는가</b>
        ///
        /// 표시는 그 표면을 도려낼 뿐 아니라, <b>바로 아래위의 지면까지 함께 삼킵니다.</b>
        /// 복셀화는 넘어갈 수 있는 턱(<see cref="AgentClimb"/>) 안에 있는 면들을
        /// 한 덩어리로 묶는데, 그 덩어리에 "갈 수 없음"이 하나라도 섞이면 전부 사라집니다.
        ///
        /// 물 판이 정확히 그런 경우였습니다. 그 판은 전장 <b>전체</b>를 덮는 한 장이고
        /// 해수면 높이에 있습니다. 여울은 수면보다 겨우 조금 높아서 한 덩어리로 묶였고,
        /// <b>여울이 통째로 사라졌습니다</b> — 강을 건널 방법이 없어졌습니다.
        /// 적이 오지 못하니 전투가 시작되지도 끝나지도 않습니다.
        ///
        /// 아예 빼면 그 판은 없는 것이 됩니다. 물이 길이 아닌 것은 해수면 아래를
        /// 도려내는 상자가 이미 보장합니다 — 규칙이 한 곳에만 있습니다.
        /// </summary>
        /// <param name="branch">뺄 가지입니다. 없으면 아무것도 하지 않습니다.</param>
        private static void Exclude(Transform branch)
        {
            if (branch == null)
            {
                return;
            }

            Sources.RemoveAll(source =>
                source.component != null && source.component.transform.IsChildOf(branch));
        }

        /// <summary>
        /// 이 가지 아래의 모든 것을 갈 수 없는 곳으로 표시합니다.
        /// </summary>
        /// <param name="branch">표시할 가지입니다. 없으면 아무것도 하지 않습니다.</param>
        private static void AddCarveMarkup(Transform branch)
        {
            if (branch == null)
            {
                return;
            }

            Markups.Add(new NavMeshBuildMarkup
            {
                root = branch,
                overrideArea = true,
                area = NotWalkableArea,
                applyToChildren = true,
            });
        }

        /// <summary>
        /// 해수면 아래를 길이 아닌 곳으로 표시하는 상자를 넣습니다.
        ///
        /// 상자 하나로 전장 전체를 덮습니다. 강줄기를 따로 그릴 필요가 없습니다 —
        /// 물에 잠긴 곳은 정의상 해수면 아래이기 때문입니다.
        /// </summary>
        /// <param name="battlefield">기준이 되는 전장입니다.</param>
        private static void AddWaterCarve(Battlefield battlefield)
        {
            float extent = battlefield.PlayWorldSize;
            float depth = battlefield.Heightmap.MaxElevation * 2f;

            // 상자의 <b>윗면</b>이 해수면에 닿아야 합니다. 중심을 반 깊이만큼 내립니다.
            var center = new Vector3(
                battlefield.PlayOrigin.x + extent * 0.5f,
                battlefield.SeaLevel - depth * 0.5f,
                battlefield.PlayOrigin.z + extent * 0.5f);

            Sources.Add(new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.ModifierBox,
                size = new Vector3(extent, depth, extent),
                transform = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one),
                area = NotWalkableArea,
            });
        }
    }
}
