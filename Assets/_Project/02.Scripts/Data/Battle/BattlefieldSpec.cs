namespace SRPG.Data
{
    /// <summary>
    /// 이 전투가 <b>어디서</b> 벌어지는지에 대한 지시입니다.
    ///
    /// <b>월드맵이 붙을 자리입니다</b>
    ///
    /// 지금은 주문서가 직접 채우지만, 나중에는 이렇게 흐릅니다.
    ///
    ///   월드맵 좌표 → 그 자리의 지형 종류 → 프로필 → 이 지시 → 전장
    ///
    /// 좌표와 전장 사이를 <b>지형 종류</b>가 갈라 놓는 것이 핵심입니다.
    /// 생성기는 좌표를 모르고, 월드맵은 생성기가 어떻게 만드는지 모릅니다.
    /// 그래서 한쪽이 바뀌어도 다른 쪽이 흔들리지 않습니다.
    ///
    /// <b>왜 크기를 여기 두는가</b>
    ///
    /// 같은 숲이라도 소규모 매복과 회전은 전장 크기가 달라야 합니다.
    /// 지형 종류는 <b>성격</b>을, 이 지시는 <b>이번 판</b>을 정합니다.
    /// </summary>
    [System.Serializable]
    public struct BattlefieldSpec
    {
        /// <summary>같은 값이면 같은 전장이 나옵니다. 0이면 매번 달라집니다.</summary>
        public int Seed;

        /// <summary>지형 종류입니다. 월드맵 좌표가 정하게 됩니다.</summary>
        public TerrainKind Terrain;

        /// <summary>가로 칸 수입니다.</summary>
        public int Width;

        /// <summary>세로 칸 수입니다.</summary>
        public int Depth;

        /// <summary>
        /// 기본 크기의 전장입니다.
        ///
        /// 32칸은 셀 크기 2 기준 64미터입니다. 부대 대여섯이 기동하기에 넉넉하고,
        /// 카메라 한 화면에 대체로 들어옵니다.
        /// </summary>
        public static BattlefieldSpec CreateDefault(TerrainKind terrain = TerrainKind.Plains, int seed = 0)
        {
            return new BattlefieldSpec
            {
                Seed = seed,
                Terrain = terrain,
                Width = 32,
                Depth = 32,
            };
        }

        /// <summary>크기가 지정되지 않았으면 기본값으로 채웁니다.</summary>
        public BattlefieldSpec WithDefaults()
        {
            var filled = this;

            if (filled.Width <= 0)
            {
                filled.Width = 32;
            }

            if (filled.Depth <= 0)
            {
                filled.Depth = 32;
            }

            return filled;
        }
    }
}
