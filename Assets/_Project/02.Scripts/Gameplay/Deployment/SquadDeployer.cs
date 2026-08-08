using System;
using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Systems.Deployment;
using SRPG.Systems.Formation;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Gameplay.Deployment
{
    /// <summary>
    /// 양측 부대를 전장에 세우고, 자리가 나면 지원군을 올려보냅니다.
    ///
    /// <b>상륙정과 웨이브 스포너를 대신합니다</b>
    ///
    /// 예전에는 적이 바다에서 왔습니다. 스포너가 시각에 맞춰 상륙정을 띄우고,
    /// 배가 해안에 닿아 병력을 한 명씩 내려놓았습니다.
    /// 방어자는 어느 해안이 위험한지 모른 채 전선을 나눠야 했고, 그것이 압박의 원천이었습니다.
    ///
    /// 야전은 다릅니다. 양측이 <b>이미 편성된 부대로</b> 마주 보고 들어섭니다.
    /// 어디서 오는지는 처음부터 알고, 문제는 그 다음입니다 —
    /// 전장이 좁아 전 병력이 한 번에 붙지 못하고, 앞이 무너져야 뒤가 올라옵니다.
    ///
    /// <b>양측을 같은 코드가 다룹니다</b>
    ///
    /// 규칙이 대칭이기 때문입니다. 상한도 간격도 진영을 가리지 않습니다.
    /// 한쪽만 특별하게 두면 "적은 왜 지원군이 빠른가" 같은 것이 규칙이 아니라 버그로 생깁니다.
    /// 다른 것은 <b>무엇을 만드느냐</b>뿐이라, 그 부분만 밖에서 꽂습니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SquadDeployer : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        private readonly List<Tile> _playerSlots = new List<Tile>(8);
        private readonly List<Tile> _enemySlots = new List<Tile>(8);
        private readonly List<SquadOrder> _batch = new List<SquadOrder>(8);

        private BattleContext _context;
        private ReinforcementPool _playerPool;
        private ReinforcementPool _enemyPool;

        private Action<SquadOrder, GridCoord> _playerFactory;
        private Action<SquadOrder, GridCoord> _enemyFactory;

        /// <summary>다음 지원군이 설 자리입니다. 진영마다 돌아가며 씁니다.</summary>
        private int _playerSlotCursor;
        private int _enemySlotCursor;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>더 내보낼 적 분대가 없는지 여부입니다. 승리 판정의 입력입니다.</summary>
        public bool EnemyReinforcementsExhausted => _enemyPool == null || _enemyPool.IsExhausted;

        /// <summary>아직 전장에 나가지 않은 아군 분대 수입니다. 패배 판정의 입력입니다.</summary>
        public int PlayerReserves => _playerPool != null ? _playerPool.Remaining : 0;

        /// <summary>아직 전장에 나가지 않은 적 분대 수입니다. HUD 표시에 씁니다.</summary>
        public int EnemyReserves => _enemyPool != null ? _enemyPool.Remaining : 0;

        /// <summary>다음 적 지원군까지 남은 시간입니다. HUD 표시에 씁니다.</summary>
        public float TimeUntilEnemyReinforcement => _enemyPool != null ? _enemyPool.TimeUntilNext : 0f;

        // ====================================================================================================
        // 3. Unity Lifecycle
        // ====================================================================================================

        private void Update()
        {
            if (_context == null)
            {
                return;
            }

            // 스케일된 시간으로 진행합니다. 슬로우모션 중에도 지원군이 그대로 밀려오면
            // 명령 입력이 곧 시간 벌기가 되어 버립니다.
            float deltaTime = UnityEngine.Time.deltaTime;

            _playerPool.Tick(deltaTime);
            _enemyPool.Tick(deltaTime);

            TryReinforce(Team.Player);
            TryReinforce(Team.Enemy);
        }

        // ====================================================================================================
        // 4. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 전개기를 준비하고 <b>양측을 즉시 전장에 세웁니다.</b>
        ///
        /// 초기 전개는 간격을 두지 않습니다. 전투가 열리면 이미 대치한 상태여야 합니다.
        /// </summary>
        /// <param name="context">전투 컨텍스트입니다.</param>
        /// <param name="request">양측의 전투 서열이 담긴 주문서입니다.</param>
        /// <param name="playerFactory">아군 분대를 만드는 함수입니다.</param>
        /// <param name="enemyFactory">적 분대를 만드는 함수입니다.</param>
        public void Initialize(
            BattleContext context,
            BattleRequest request,
            Action<SquadOrder, GridCoord> playerFactory,
            Action<SquadOrder, GridCoord> enemyFactory)
        {
            _context = context;
            _playerFactory = playerFactory;
            _enemyFactory = enemyFactory;

            var tuning = context.Tuning;

            _playerPool = new ReinforcementPool(
                request.PlayerSquads, tuning.FieldSquadCap, tuning.ReinforcementInterval);

            _enemyPool = new ReinforcementPool(
                request.EnemySquads, tuning.FieldSquadCap, tuning.ReinforcementInterval);

            ResolveSlots(tuning.FieldSquadCap);

            DeployInitial(Team.Player);
            DeployInitial(Team.Enemy);
        }

        // ====================================================================================================
        // 5. Private Methods - Slots
        // ====================================================================================================

        /// <summary>
        /// 진영마다 설 자리를 미리 골라 둡니다.
        ///
        /// 매번 다시 고르지 않는 이유는 지원군이 <b>같은 곳에서 올라와야</b> 하기 때문입니다.
        /// 전황에 따라 진입 지점이 옮겨 다니면 어디서 나올지 읽을 수 없습니다.
        /// </summary>
        private void ResolveSlots(int cap)
        {
            var grid = _context.Grid;
            Vector3 center = grid.WorldCenter;

            SpawnPlacement.SelectDeploymentTiles(grid.PlayerDeployment, center, cap, _playerSlots);
            SpawnPlacement.SelectDeploymentTiles(grid.EnemyDeployment, center, cap, _enemySlots);

            // 전개 구역이 비어 있으면(격자만 꽂아 준 테스트 경로) 통행 가능한 아무 자리나 씁니다.
            // 여기서 물러나면 전투가 아예 시작되지 않습니다.
            if (_playerSlots.Count == 0)
            {
                SpawnPlacement.SelectSquadTiles(grid, cap, _playerSlots);
            }

            if (_enemySlots.Count == 0)
            {
                SpawnPlacement.SelectSquadTiles(grid, cap, _enemySlots);
            }
        }

        private List<Tile> SlotsOf(Team team) => team == Team.Player ? _playerSlots : _enemySlots;

        /// <summary>
        /// 다음 분대가 설 자리를 하나 집습니다. 자리를 돌아가며 씁니다.
        /// </summary>
        private bool TryTakeSlot(Team team, out GridCoord coord)
        {
            var slots = SlotsOf(team);

            if (slots.Count == 0)
            {
                coord = GridCoord.Invalid;
                return false;
            }

            if (team == Team.Player)
            {
                coord = slots[_playerSlotCursor % slots.Count].Coord;
                _playerSlotCursor++;
            }
            else
            {
                coord = slots[_enemySlotCursor % slots.Count].Coord;
                _enemySlotCursor++;
            }

            return true;
        }

        // ====================================================================================================
        // 6. Private Methods - Deployment
        // ====================================================================================================

        /// <summary>전투 시작 시 상한까지 채웁니다.</summary>
        private void DeployInitial(Team team)
        {
            var pool = team == Team.Player ? _playerPool : _enemyPool;

            pool.FillField(0, _batch);

            for (int i = 0; i < _batch.Count; i++)
            {
                Deploy(team, _batch[i]);
            }
        }

        /// <summary>자리가 났으면 대기 중인 분대를 하나 올려보냅니다.</summary>
        private void TryReinforce(Team team)
        {
            var pool = team == Team.Player ? _playerPool : _enemyPool;

            int onField = _context.CountLivingSquads(team);

            if (pool.TryDeploy(onField, out var order))
            {
                Deploy(team, order);
            }
        }

        private void Deploy(Team team, SquadOrder order)
        {
            if (!TryTakeSlot(team, out var coord))
            {
                Debug.LogError($"[Deployer] {team} 진영이 설 자리가 없어 {order.Id}번 분대를 세우지 못했습니다.");
                return;
            }

            if (team == Team.Player)
            {
                _playerFactory?.Invoke(order, coord);
            }
            else
            {
                _enemyFactory?.Invoke(order, coord);
            }
        }
    }
}
