using SRPG.Data;
using SRPG.Systems.Battlefield;
using SRPG.Systems.Grid;

namespace SRPG.Composition
{
    /// <summary>
    /// 이번 판을 열기 위해 <b>씬을 건드리기 전에</b> 확정할 수 있는 전부입니다.
    ///
    /// <b>왜 따로 두는가</b>
    ///
    /// 전투를 여는 일은 두 종류로 갈립니다.
    ///   · 무엇을 데리고 어디서 싸우는가 — 순수한 자료입니다. 게임 오브젝트가 하나도 필요 없습니다.
    ///   · 그것을 화면에 세우는 일 — 부모를 만들고 카메라를 잡고 병력을 찍어 냅니다.
    ///
    /// 앞의 것을 컨테이너 조립 시점에 끝내 두면, 뒤의 것을 맡은 진입점은
    /// <b>이미 검증된 계획</b>만 받습니다. 주문서가 비었는지, 지형이 만들어졌는지를
    /// 병력을 세우다 말고 확인할 일이 없어집니다.
    ///
    /// 그리고 "전투가 왜 시작되지 않았는가"를 씬을 재생하지 않고도 알 수 있게 됩니다 —
    /// <see cref="Problem"/> 에 이유가 문장으로 남습니다.
    /// </summary>
    public sealed class BattlePlan
    {
        // ====================================================================================================
        // 1. Properties
        // ====================================================================================================

        /// <summary>이번 판의 주문서입니다. 계획이 서지 못했으면 비어 있을 수 있습니다.</summary>
        public BattleRequest Request { get; }

        /// <summary>
        /// 이번 판에 만들어진 전장입니다.
        ///
        /// <b>비어 있을 수 있습니다.</b> 바깥이 격자만 꽂아 준 경우가 그렇습니다 —
        /// 자동 검사가 고정된 지형을 쓸 때 이 경로를 지납니다. 그리는 쪽이 이것을 확인해야 합니다.
        /// </summary>
        public Battlefield Battlefield { get; }

        /// <summary>싸울 땅입니다. 계획이 서지 못했으면 null입니다.</summary>
        public IslandGrid Grid { get; }

        /// <summary>계획이 서지 못한 이유입니다. 온전하면 null입니다.</summary>
        public string Problem { get; }

        /// <summary>이 계획으로 전투를 열 수 있는지 여부입니다.</summary>
        public bool IsReady => Grid != null && Problem == null;

        // ====================================================================================================
        // 2. Constructors
        // ====================================================================================================

        /// <summary>열 수 있는 계획을 만듭니다.</summary>
        /// <param name="request">이번 판의 주문서입니다.</param>
        /// <param name="battlefield">만들어진 전장입니다. 격자만 받은 경로에서는 null입니다.</param>
        /// <param name="grid">싸울 땅입니다.</param>
        public BattlePlan(BattleRequest request, Battlefield battlefield, IslandGrid grid)
        {
            Request = request;
            Battlefield = battlefield;
            Grid = grid;
        }

        /// <summary>열 수 없는 계획을 만듭니다.</summary>
        /// <param name="problem">열 수 없는 이유입니다.</param>
        public BattlePlan(string problem)
        {
            Problem = problem;
        }
    }
}
