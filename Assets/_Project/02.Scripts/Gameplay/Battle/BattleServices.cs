using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Enemies;
using SRPG.Gameplay.Squads;
using SRPG.Gameplay.Units;
using SRPG.Gameplay.Weapons;
using SRPG.Systems.AI;
using SRPG.Systems.Grid;
using SRPG.Systems.Pathfinding;
using SRPG.Systems.Spatial;
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
    /// 전장에 서 있는 분대의 명부입니다.
    ///
    /// <b>왜 레지스트리가 필요한가</b>
    ///
    /// 디버그 HUD 와 AI 오버레이가 <c>FindObjectsByType</c> 으로 씬 전체를 훑고 있었습니다.
    /// 그 자체도 싸지 않지만, <b>부르는 자리가 나빴습니다</b> —
    /// <c>OnGUI</c> 는 한 프레임에 레이아웃과 리페인트로 두 번 이상 돕니다.
    ///
    /// 야전으로 오면서 이유가 하나 더 붙었습니다. <b>지원군이 자리를 보고 들어옵니다.</b>
    /// "지금 전장에 우리 분대가 몇인가"를 매 프레임 물어야 하는데,
    /// 그때마다 씬을 훑을 수는 없습니다.
    /// </summary>
    public interface ISquadRoster
    {
        /// <summary>전장에 있는 플레이어 분대입니다. 무너진 분대가 잠시 남아 있을 수 있습니다.</summary>
        IReadOnlyList<Squad> PlayerSquads { get; }

        /// <summary>전장에 있는 적 분대입니다. 해산한 분대가 잠시 남아 있을 수 있습니다.</summary>
        IReadOnlyList<EnemySquad> EnemySquads { get; }

        /// <summary>지정 진영에서 <b>아직 싸우고 있는</b> 분대 수입니다. 지원군 투입 판단의 입력입니다.</summary>
        int CountLivingSquads(Team team);
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

        /// <summary>
        /// 전장의 소리 창구입니다. 절대 null이 아닙니다.
        ///
        /// <b>왜 병사가 이것을 보는가</b>
        ///
        /// 위의 셋을 빼 둔 원칙과 충돌하는 것처럼 보이지만 그렇지 않습니다.
        /// 빼 둔 것은 <b>병사가 전투 전체를 주무를 수 있게 하는 것</b>들입니다.
        /// 소리는 그 반대입니다 — 자기 몸에 일어난 일을 알릴 뿐 아무것도 바꾸지 않습니다.
        ///
        /// 그리고 여기 있어야 합니다. 소리가 나야 할 자리는 <b>맞은 순간</b>인데,
        /// 그 순간을 아는 것은 맞은 병사뿐입니다. 때린 쪽은 자기 타격이 닿았는지
        /// (막혔는지, 빗나갔는지) 모릅니다. 공유 자원을 병사에게 넘기는 것은
        /// 이미 <see cref="ProjectilePool"/> 로 한 판단이고, 같은 이유입니다.
        /// </summary>
        IBattleAudio Audio { get; }
    }

    /// <summary>
    /// <b>분대 하나가 볼 수 있는 전부</b>입니다. 양측이 같은 것을 봅니다.
    ///
    /// <b>왜 제네릭인가</b>
    ///
    /// 점유 장부와 명부는 진영마다 <b>별도 인스턴스</b>입니다.
    /// 하나로 합치면 적이 플레이어가 선 칸을 목적지로 삼을 수 없게 되는데,
    /// 그건 "공격하지 말라"는 뜻이 되어 버립니다(<see cref="TileOccupancy{TOwner}"/>).
    ///
    /// 그렇다고 양쪽 장부를 다 넘기면 <b>아군 분대가 적의 점유를 만질 수 있게</b> 됩니다.
    /// 타입 인자로 가르면 자기 진영의 것만 손에 닿고, 반대편을 만지는 코드는
    /// 아예 컴파일되지 않습니다. 규율이 아니라 컴파일러가 지킵니다.
    ///
    /// 그러면서 규칙이 대칭이라는 사실도 타입으로 남습니다 —
    /// 한쪽만 특별하게 두면 "적은 왜 지원군이 빠른가" 같은 것이 규칙이 아니라 버그로 생깁니다.
    ///
    /// <b>여기 없는 것</b>
    ///
    ///   · <b>전군 명부가 없습니다.</b> 분대는 부대를 보지 병사 하나하나를 세지 않습니다
    ///   · <b>투사체 풀이 없습니다.</b> 쏘는 것은 병사의 무기입니다
    ///   · <b>반대 진영의 점유와 명부가 없습니다.</b> 위의 이유입니다
    /// </summary>
    /// <typeparam name="TSquad">이 컨텍스트를 받는 분대의 타입입니다.</typeparam>
    public interface ISquadContext<TSquad> : ISpatialQuery
        where TSquad : class
    {
        /// <summary>지형입니다. 좌표 변환·통행 판정·지면 높이를 여기서 얻습니다.</summary>
        IslandGrid Grid { get; }

        /// <summary>전투 튜닝 수치입니다. 절대 null이 아닙니다.</summary>
        BattleTuning Tuning { get; }

        /// <summary>
        /// 공유 경로 탐색기입니다. <b>길을 잡는 것은 분대의 일입니다</b> —
        /// 병사는 앵커나 슬롯이 가리키는 곳으로 조향할 뿐입니다.
        /// </summary>
        GridPathfinder Pathfinder { get; }

        /// <summary>내 진영의 타일 점유 장부입니다. 한 칸에 분대 하나를 강제합니다.</summary>
        TileOccupancy<TSquad> Occupancy { get; }

        /// <summary>전장에 서면서 스스로 명부에 오릅니다.</summary>
        void RegisterSquad(TSquad squad);

        /// <summary>무너지거나 사라지면서 스스로 명부에서 빠집니다.</summary>
        void UnregisterSquad(TSquad squad);
    }

    /// <summary>
    /// 적 분대가 <b>추가로</b> 보는 것입니다. 판단의 재료입니다.
    ///
    /// 플레이어 분대는 사람이 명령을 내리므로 스스로 판단하지 않습니다.
    /// 적 분대만 목표를 고르고, 그러려면 두 가지가 더 필요합니다 —
    /// 어디가 위험한가(<see cref="IThreatProvider"/>)와 칠 부대가 어디 있는가(<see cref="ISquadRoster"/>).
    ///
    /// <b>부대 명부이지 병사 명부가 아닙니다.</b>
    /// 판단의 단위가 부대이므로 후보도 부대여야 합니다.
    /// </summary>
    public interface IEnemySquadContext : ISquadContext<EnemySquad>, IThreatProvider, ISquadRoster
    {
    }

    /// <summary>
    /// <b>전개기가 볼 수 있는 전부</b>입니다.
    ///
    /// 자리를 고르고(<see cref="Grid"/>), 상한과 간격을 읽고(<see cref="Tuning"/>),
    /// 지금 몇 부대가 서 있는지 셉니다(<see cref="ISquadRoster.CountLivingSquads"/>).
    /// 그게 전부입니다 — 전개기는 분대를 <b>만들지 않습니다.</b>
    /// 무엇을 어떻게 만들지는 밖에서 꽂아 준 함수가 압니다.
    ///
    /// 경로 탐색기도 점유 장부도 없습니다. 자리를 정하는 것과 그 자리로 가는 것은 다른 일입니다.
    /// </summary>
    public interface IDeploymentContext : ISquadRoster
    {
        /// <summary>지형입니다. 전개 구역을 여기서 얻습니다.</summary>
        IslandGrid Grid { get; }

        /// <summary>전투 튜닝 수치입니다. 동시 전개 상한과 지원군 간격이 여기 있습니다.</summary>
        BattleTuning Tuning { get; }
    }
}
