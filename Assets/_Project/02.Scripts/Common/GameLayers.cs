using UnityEngine;

namespace SRPG.Common
{
    /// <summary>
    /// 물리 질의에 사용하는 레이어와 마스크를 한곳에서 관리합니다.
    ///
    /// 전투가 물리 기반이 되면서 레이어 분리가 필수가 되었습니다.
    ///   · 지형에만 클릭 레이캐스트가 닿아야 한다 (유닛이 클릭을 가로채면 명령이 씹힌다)
    ///   · 근접 무기 판정은 유닛만 훑어야 한다
    ///   · 화살은 유닛과 지형에 맞고 다른 화살은 무시해야 한다
    ///
    /// 레이어는 <c>SRPG/프로토타입 에셋 생성</c> 메뉴가 프로젝트 설정에 등록합니다.
    /// 등록 전이라도 게임이 죽지 않도록, 레이어가 없으면 안전한 폴백 마스크를 돌려줍니다.
    /// </summary>
    public static class GameLayers
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>지형 메시가 속하는 레이어 이름입니다.</summary>
        public const string TerrainName = "Terrain";

        /// <summary>유닛 몸체 콜라이더가 속하는 레이어 이름입니다.</summary>
        public const string UnitName = "Unit";

        /// <summary>화살 등 투사체가 속하는 레이어 이름입니다.</summary>
        public const string ProjectileName = "Projectile";

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        /// <summary>지형 레이어 인덱스 캐시입니다. -2는 아직 조회하지 않았다는 표시입니다.</summary>
        private static int s_terrain = -2;
        /// <summary>유닛 레이어 인덱스 캐시입니다. -2는 아직 조회하지 않았다는 표시입니다.</summary>
        private static int s_unit = -2;
        /// <summary>투사체 레이어 인덱스 캐시입니다. -2는 아직 조회하지 않았다는 표시입니다.</summary>
        private static int s_projectile = -2;
        /// <summary>레이어 미등록 경고를 이미 냈는지 여부입니다. 매 프레임 같은 경고가 쌓이는 것을 막습니다.</summary>
        private static bool s_warned;

        // ====================================================================================================
        // 3. Properties - Layer Index
        // ====================================================================================================

        /// <summary>지형 레이어 인덱스입니다. 등록되지 않았으면 -1입니다.</summary>
        public static int Terrain
        {
            get
            {
                if (s_terrain == -2)
                {
                    s_terrain = LayerMask.NameToLayer(TerrainName);
                }

                return s_terrain;
            }
        }

        /// <summary>유닛 레이어 인덱스입니다. 등록되지 않았으면 -1입니다.</summary>
        public static int Unit
        {
            get
            {
                if (s_unit == -2)
                {
                    s_unit = LayerMask.NameToLayer(UnitName);
                }

                return s_unit;
            }
        }

        /// <summary>투사체 레이어 인덱스입니다. 등록되지 않았으면 -1입니다.</summary>
        public static int Projectile
        {
            get
            {
                if (s_projectile == -2)
                {
                    s_projectile = LayerMask.NameToLayer(ProjectileName);
                }

                return s_projectile;
            }
        }

        // ====================================================================================================
        // 4. Properties - Mask
        // ====================================================================================================

        /// <summary>
        /// 지형만 포함하는 마스크입니다. 클릭 레이캐스트에 사용합니다.
        /// 레이어가 없으면 유닛과 투사체를 제외한 전부를 돌려줍니다.
        /// </summary>
        public static int TerrainMask => MaskOrFallback(Terrain, ~(SafeBit(Unit) | SafeBit(Projectile)));

        /// <summary>유닛만 포함하는 마스크입니다. 근접 무기 판정에 사용합니다.</summary>
        public static int UnitMask => MaskOrFallback(Unit, ~0);

        /// <summary>화살이 충돌할 대상 마스크입니다. 유닛과 지형이며, 다른 투사체는 제외합니다.</summary>
        public static int ProjectileHitMask
        {
            get
            {
                if (Unit >= 0 && Terrain >= 0)
                {
                    return SafeBit(Unit) | SafeBit(Terrain);
                }

                // 레이어가 없으면 투사체끼리만 서로 무시합니다.
                return ~SafeBit(Projectile);
            }
        }

        // ====================================================================================================
        // 5. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 오브젝트와 모든 자식의 레이어를 설정합니다. 레이어가 등록되지 않았으면 아무것도 하지 않습니다.
        /// </summary>
        /// <param name="target">레이어를 바꿀 오브젝트입니다. null이면 아무것도 하지 않습니다.</param>
        /// <param name="layer">설정할 레이어 인덱스입니다. 음수면 등록되지 않은 것으로 보고 넘어갑니다.</param>
        public static void ApplyRecursively(GameObject target, int layer)
        {
            if (target == null || layer < 0)
            {
                return;
            }

            target.layer = layer;

            var transforms = target.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < transforms.Length; i++)
            {
                transforms[i].gameObject.layer = layer;
            }
        }

        /// <summary>
        /// 캐시를 비웁니다. 에디터가 레이어를 새로 등록한 직후에 호출합니다.
        /// </summary>
        public static void InvalidateCache()
        {
            s_terrain = -2;
            s_unit = -2;
            s_projectile = -2;
            s_warned = false;
        }

        // ====================================================================================================
        // 6. Private Methods
        // ====================================================================================================

        private static int SafeBit(int layer) => layer >= 0 ? 1 << layer : 0;

        /// <summary>
        /// 레이어가 등록되어 있으면 해당 비트만, 아니면 폴백 마스크를 돌려줍니다.
        /// </summary>
        private static int MaskOrFallback(int layer, int fallback)
        {
            if (layer >= 0)
            {
                return 1 << layer;
            }

            WarnOnce();
            return fallback;
        }

        private static void WarnOnce()
        {
            if (s_warned)
            {
                return;
            }

            s_warned = true;
            Debug.LogWarning(
                $"[GameLayers] '{TerrainName}' / '{UnitName}' / '{ProjectileName}' 레이어가 등록되지 않았습니다.\n" +
                "메뉴 'SRPG → 프로토타입 에셋 생성'을 실행하면 자동으로 등록됩니다. " +
                "그때까지는 폴백 마스크로 동작하므로 클릭이나 타격 판정이 부정확할 수 있습니다.");
        }
    }
}
