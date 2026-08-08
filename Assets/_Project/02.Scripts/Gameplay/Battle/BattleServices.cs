using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Units;
using SRPG.Gameplay.Weapons;
using SRPG.Systems.AI;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Gameplay.Battle
{
    /// <summary>
    /// 공간 질의입니다. "내 주변에 누가 있는가"에 답합니다.
    ///
    /// <b>왜 인터페이스로 떼어 내는가</b>
    ///
    /// <see cref="BattleContext"/>는 싱글턴이 아니지만, 여덟 개 컴포넌트가 그것을 <b>통째로</b> 받고
    /// 각자 그중 일부만 씁니다. 그러면 컨텍스트에 필드를 하나 더할 때마다
    /// 그것을 쓸 이유가 없는 여덟 곳이 함께 접근 가능해집니다.
    ///
    /// 지금은 규율로 지켜지고 있지만 컴파일러가 막아 주지는 않습니다.
    /// 병사가 분대의 타일 점유를 만지거나 디버그 HUD가 경로 탐색기를 돌리는 코드가
    /// 들어와도 아무 일도 일어나지 않습니다. 팀이 커지면 가장 먼저 무너지는 지점입니다.
    ///
    /// <see cref="WeaponBase"/>가 컨텍스트 대신 투사체 풀과 튜닝만 받는 것이
    /// 이 문제를 이미 인지하고 있다는 증거입니다. 그 판단을 나머지에도 적용합니다.
    /// </summary>
    public interface ISpatialQuery
    {
        /// <summary>지정 위치에서 가장 가까운 적대 유닛을 찾습니다. 없으면 null입니다.</summary>
        Unit FindNearestEnemy(Vector3 position, Team myTeam, float maxDistance);

        /// <summary>지정 반경 안의 특정 진영 유닛을 <paramref name="buffer"/>에 채웁니다.</summary>
        int QueryTeam(Vector3 position, float radius, Team team, Unit exclude, List<Unit> buffer);
    }

    /// <summary>
    /// 유닛 등록부입니다. 태어나고 죽을 때만 부릅니다.
    ///
    /// 목록을 <b>읽는</b> 일과 나눠 둡니다(<see cref="IUnitRoster"/>).
    /// 병사는 자기를 등록할 뿐 전군의 명부를 훑을 이유가 없고,
    /// 디버그 HUD는 명부를 읽을 뿐 누군가를 등록할 이유가 없습니다.
    /// </summary>
    public interface IUnitRegistry
    {
        /// <summary>유닛을 등록합니다.</summary>
        void Register(Unit unit);

        /// <summary>유닛을 등록에서 제거합니다.</summary>
        void Unregister(Unit unit);
    }

    /// <summary>
    /// 유닛 명부입니다. 전황을 세는 쪽이 봅니다.
    /// </summary>
    public interface IUnitRoster
    {
        /// <summary>플레이어 유닛 목록입니다.</summary>
        IReadOnlyList<Unit> PlayerUnits { get; }

        /// <summary>적 유닛 목록입니다.</summary>
        IReadOnlyList<Unit> EnemyUnits { get; }

        /// <summary>지정 진영의 유닛 목록을 반환합니다.</summary>
        IReadOnlyList<Unit> GetUnits(Team team);
    }

    /// <summary>
    /// 위협 영향력 지도를 공급합니다. 적 AI의 판단과 그 판단을 그리는 오버레이가 씁니다.
    /// </summary>
    public interface IThreatProvider
    {
        /// <summary>지정 진영이 뿜는 위협 영향력 맵을 반환합니다. 주기적으로만 갱신됩니다.</summary>
        InfluenceMap GetThreatMap(Team team);
    }

    /// <summary>
    /// <b>병사 한 명이 볼 수 있는 전부</b>입니다.
    ///
    /// 여기 없는 것이 이 타입의 요점입니다.
    ///
    ///   · <b>경로 탐색기가 없습니다.</b> 길을 잡는 것은 분대의 일입니다.
    ///     병사는 앵커나 슬롯이 가리키는 곳으로 조향할 뿐 스스로 경로를 잡지 않습니다.
    ///     예전에 병사마다 경로를 잡던 구조를 걷어낸 이유가 그것이었습니다.
    ///   · <b>타일 점유가 없습니다.</b> 한 칸에 분대 하나라는 규칙은 분대 단위의 약속입니다.
    ///   · <b>전군 명부가 없습니다.</b> 병사는 자기 주변만 보면 됩니다.
    ///     명부를 훑는 것은 전황을 세는 쪽의 일입니다.
    ///
    /// 이 셋을 빼는 것만으로 "병사가 전투 전체를 주무르는" 코드가 컴파일되지 않게 됩니다.
    /// </summary>
    public interface IUnitContext : ISpatialQuery, IUnitRegistry
    {
        /// <summary>섬 지형입니다. 발 높이와 타일 크기를 여기서 얻습니다.</summary>
        IslandGrid Grid { get; }

        /// <summary>전투 튜닝 수치입니다. 절대 null이 아닙니다.</summary>
        BattleTuning Tuning { get; }

        /// <summary>화살 재사용 풀입니다. 궁수의 무기가 여기서 화살을 꺼내 씁니다.</summary>
        ProjectilePool ProjectilePool { get; }
    }
}
