using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Units;
using SRPG.Gameplay.Weapons;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 병사를 세우는 데 필요한 최소 구현입니다. <b>테스트 픽스처이지 테스트가 아닙니다.</b>
    ///
    /// <b>왜 EditMode 어셈블리에 두는가</b>
    ///
    /// <c>10.Tests/Support</c> 는 일부러 <c>SRPG.Gameplay</c> 를 참조하지 않습니다.
    /// 거기 있는 <c>TestIsland</c> 는 지형 하나만 지어 주면 되고, 그 경계를 넓히면
    /// 순수 시스템 테스트가 게임 객체에 딸려 흔들리기 시작합니다.
    /// 병사가 필요한 픽스처는 병사를 아는 쪽, 즉 여기 있어야 합니다.
    ///
    /// 공간 질의는 전수 조사로 답합니다. 유닛이 몇 개뿐이고, 무엇보다
    /// <b>정답이 자명해야</b> 검증에 쓸 수 있습니다.
    /// (색인 자체의 정확성은 <c>SpatialGridTests</c> 가 따로 봅니다)
    ///
    /// <c>UnitContextTests</c> 는 이것을 쓰지 않고 자기 안에 같은 것을 들고 있습니다.
    /// 그 테스트의 논지가 <b>"이 파일이 컴파일된다는 사실 자체가 증명"</b> 이라,
    /// 가짜가 그 파일 안에 보이는 편이 읽는 사람에게 낫기 때문입니다.
    /// </summary>
    public sealed class TestUnitContext : IUnitContext
    {
        private readonly List<Unit> _registered = new List<Unit>();

        public TestUnitContext(IslandGrid grid, BattleTuning tuning)
        {
            Grid = grid;
            Tuning = tuning;
        }

        public IslandGrid Grid { get; }

        public BattleTuning Tuning { get; }

        public ProjectilePool ProjectilePool { get; } = new ProjectilePool();

        /// <summary>
        /// 소리를 내지 않습니다.
        ///
        /// 검사에서 소리가 나면 안 되기도 하지만, 그보다 <b>확인할 수 없기</b> 때문입니다.
        /// 소리가 났는지를 검사가 판단할 방법이 없으므로 여기서 세울 이유도 없습니다.
        /// </summary>
        public IBattleAudio Audio { get; } = SilentBattleAudio.Instance;

        /// <summary>등록된 유닛입니다. 검증용으로만 노출합니다.</summary>
        public IReadOnlyList<Unit> Registered => _registered;

        public void Register(Unit unit)
        {
            if (unit != null && !_registered.Contains(unit))
            {
                _registered.Add(unit);
            }
        }

        public void Unregister(Unit unit)
        {
            _registered.Remove(unit);
        }

        public Unit FindNearestEnemy(Vector3 position, Team myTeam, float maxDistance)
        {
            Unit best = null;
            float bestSqr = maxDistance * maxDistance;

            for (int i = 0; i < _registered.Count; i++)
            {
                var candidate = _registered[i];
                if (candidate == null || !candidate.IsAlive || candidate.Team == myTeam)
                {
                    continue;
                }

                float sqr = (candidate.Position - position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }

            return best;
        }

        public int QueryTeam(Vector3 position, float radius, Team team, Unit exclude, List<Unit> buffer)
        {
            buffer.Clear();

            float sqrRadius = radius * radius;

            for (int i = 0; i < _registered.Count; i++)
            {
                var candidate = _registered[i];
                if (candidate == null || candidate == exclude || !candidate.IsAlive || candidate.Team != team)
                {
                    continue;
                }

                if ((candidate.Position - position).sqrMagnitude <= sqrRadius)
                {
                    buffer.Add(candidate);
                }
            }

            return buffer.Count;
        }
    }
}
