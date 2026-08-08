using System;
using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Units;
using SRPG.Gameplay.Visual;
using SRPG.Systems.Grid;
using SRPG.Systems.Spawning;
using UnityEngine;

namespace SRPG.Gameplay.Enemies
{
    /// <summary>
    /// 웨이브 스케줄에 맞춰 상륙정을 띄웁니다.
    ///
    /// 상륙 구역을 서로 다른 곳으로 고르는 것이 이 클래스의 핵심입니다.
    /// 같은 해안에 몰아서 보내면 방어가 쉬워지고, 흩어 보내면 분대 4개로 전선을 감당할 수 없게 됩니다.
    /// 조사에서 확인한 Bad North의 압박 구조가 여기서 만들어집니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySpawner : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>상륙정이 등장하는 해안으로부터의 거리입니다.</summary>
        private const float OffshoreDistance = 9f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly List<EnemyShip> _activeShips = new List<EnemyShip>(8);
        private readonly List<int> _zoneOrder = new List<int>(8);

        private BattleContext _context;
        private WaveScheduler _scheduler;
        private UnitDefinition[] _roster;
        private Func<UnitDefinition, Team, bool, Vector3, Unit> _unitFactory;
        private GameObject _shipPrefab;
        private Material _fallbackHullMaterial;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>웨이브 스케줄러입니다. HUD가 남은 시간을 읽어 갑니다.</summary>
        public WaveScheduler Scheduler => _scheduler;

        /// <summary>현재 바다 위에 있는 상륙정 수입니다.</summary>
        public int ActiveShipCount
        {
            get
            {
                _activeShips.RemoveAll(ship => ship == null);
                return _activeShips.Count;
            }
        }

        // ====================================================================================================
        // 4. Unity Lifecycle
        // ====================================================================================================

        private void Update()
        {
            // 스케일된 시간으로 진행합니다. 슬로우모션 중에도 웨이브가 그대로 밀려오면
            // 명령 입력이 곧 시간 벌기가 되어 버립니다.
            _scheduler?.Tick(UnityEngine.Time.deltaTime);
        }

        // ====================================================================================================
        // 5. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 스포너를 초기화합니다.
        /// </summary>
        public void Initialize(
            BattleContext context,
            WaveDefinition waveDefinition,
            UnitDefinition[] roster,
            Func<UnitDefinition, Team, bool, Vector3, Unit> unitFactory,
            GameObject shipPrefab = null)
        {
            _context = context;
            _roster = roster;
            _unitFactory = unitFactory;
            _shipPrefab = shipPrefab;

            // 프리팹이 없을 때만 쓰이는 폴백 머티리얼입니다.
            if (_shipPrefab == null)
            {
                _fallbackHullMaterial = PrototypeVisuals.CreateMaterial(new Color(0.3f, 0.22f, 0.18f));
            }

            _scheduler = new WaveScheduler(waveDefinition);
            _scheduler.WaveTriggered += OnWaveTriggered;
        }

        // ====================================================================================================
        // 6. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 웨이브가 시작되면 상륙 구역을 골라 상륙정을 띄웁니다.
        /// </summary>
        private void OnWaveTriggered(int waveIndex, WaveEntry entry)
        {
            var zones = _context.Grid.LandingZones;
            if (zones.Count == 0)
            {
                Debug.LogWarning("[EnemySpawner] 상륙 구역이 없어 웨이브를 건너뜁니다. 섬 생성 설정을 확인하세요.");
                return;
            }

            BuildShuffledZoneOrder(zones.Count);

            int shipCount = Mathf.Max(1, entry.ShipCount);
            for (int i = 0; i < shipCount; i++)
            {
                // 구역 수보다 배가 많으면 순환하며 재사용합니다.
                int zoneIndex = _zoneOrder[i % _zoneOrder.Count];
                SpawnShip(zones[zoneIndex], entry.UnitsPerShip);
            }
        }

        /// <summary>
        /// 상륙 구역 인덱스를 섞습니다. 매 웨이브마다 다른 해안이 위협받게 하기 위함입니다.
        /// </summary>
        private void BuildShuffledZoneOrder(int zoneCount)
        {
            _zoneOrder.Clear();
            for (int i = 0; i < zoneCount; i++)
            {
                _zoneOrder.Add(i);
            }

            for (int i = _zoneOrder.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (_zoneOrder[i], _zoneOrder[j]) = (_zoneOrder[j], _zoneOrder[i]);
            }
        }

        /// <summary>
        /// 한 구역에 상륙정 하나를 띄웁니다.
        /// </summary>
        private void SpawnShip(List<Tile> zone, int unitsPerShip)
        {
            if (zone == null || zone.Count == 0)
            {
                return;
            }

            var landingTile = zone[UnityEngine.Random.Range(0, zone.Count)];

            // 섬 중심에서 해안을 향하는 방향의 연장선에 등장 지점을 잡습니다.
            Vector3 outward = landingTile.WorldCenter - _context.Grid.WorldCenter;
            outward.y = 0f;

            if (outward.sqrMagnitude < 0.001f)
            {
                outward = Vector3.forward;
            }

            outward.Normalize();

            Vector3 spawnPosition = landingTile.WorldCenter + outward * OffshoreDistance;

            var ship = EnemyShip.Spawn(_shipPrefab, transform, _fallbackHullMaterial);
            ship.Initialize(_context, landingTile, spawnPosition, unitsPerShip, _roster, _unitFactory);

            _activeShips.Add(ship);
        }
    }
}
