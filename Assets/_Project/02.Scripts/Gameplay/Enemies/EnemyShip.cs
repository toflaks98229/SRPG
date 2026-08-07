using System;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Units;
using SRPG.Gameplay.Visual;
using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Gameplay.Enemies
{
    /// <summary>
    /// 적 상륙정입니다. 해안으로 접근해 병력을 내려놓고 물러납니다.
    ///
    /// 상륙정을 별도 액터로 둔 이유는 조사에서 확인한 두 가지 때문입니다.
    ///   · 접근 과정이 곧 "의도의 예고"가 됩니다. 어느 해안이 위험한지 플레이어가 미리 읽을 수 있습니다.
    ///     (Into the Breach의 텔레그래핑을 실시간에 옮긴 형태입니다)
    ///   · 궁수가 상륙 전에 배 위의 적을 때릴 수 있어야 병과별 역할이 성립합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyShip : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Types
        // ====================================================================================================

        private enum ShipState
        {
            Approaching,
            Unloading,
            Leaving,
        }

        // ====================================================================================================
        // 2. Constants
        // ====================================================================================================

        /// <summary>정박 지점으로 판정하는 거리입니다.</summary>
        private const float DockThreshold = 0.35f;

        /// <summary>상륙정이 떠 있는 높이입니다.</summary>
        private const float SeaLevel = -0.1f;

        // ====================================================================================================
        // 3. Fields
        // ====================================================================================================

        private BattleContext _context;
        private Tile _landingTile;
        private Vector3 _dockPosition;
        private Vector3 _retreatPosition;
        private UnitDefinition[] _roster;
        private Func<UnitDefinition, Team, bool, Vector3, Unit> _unitFactory;

        private ShipState _state = ShipState.Approaching;
        private int _remainingUnits;
        private float _unloadTimer;

        /// <summary>
        /// 이 배가 내려놓는 병력이 이루는 분대입니다.
        ///
        /// <b>배 한 척이 곧 분대 하나입니다.</b> 같은 배에서 내린 병력은 같은 판단으로 함께 움직입니다.
        /// 그래야 상륙정의 접근이 "저 해안에 분대 하나가 온다"는 예고로 읽힙니다.
        /// </summary>
        private EnemySquad _squad;

        // ====================================================================================================
        // 4. Properties
        // ====================================================================================================

        /// <summary>이 상륙정이 향하는 해안 타일입니다.</summary>
        public Tile LandingTile => _landingTile;

        /// <summary>아직 내려놓지 않은 병력 수입니다.</summary>
        public int RemainingUnits => _remainingUnits;

        // ====================================================================================================
        // 5. Events
        // ====================================================================================================

        /// <summary>병력을 모두 내려놓았을 때 발생합니다.</summary>
        public event Action<EnemyShip> UnloadFinished;

        // ====================================================================================================
        // 6. Unity Lifecycle
        // ====================================================================================================

        private void Update()
        {
            if (_context == null)
            {
                return;
            }

            float deltaTime = UnityEngine.Time.deltaTime;

            switch (_state)
            {
                case ShipState.Approaching:
                    TickApproach(deltaTime);
                    break;

                case ShipState.Unloading:
                    TickUnload(deltaTime);
                    break;

                case ShipState.Leaving:
                    TickLeave(deltaTime);
                    break;
            }
        }

        // ====================================================================================================
        // 7. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 상륙정을 초기화합니다.
        /// </summary>
        /// <param name="context">전투 컨텍스트입니다.</param>
        /// <param name="landingTile">상륙할 해안 타일입니다.</param>
        /// <param name="spawnPosition">먼바다의 등장 위치입니다.</param>
        /// <param name="unitCount">내려놓을 병력 수입니다.</param>
        /// <param name="roster">이 상륙정이 실어 온 병과 후보입니다.</param>
        /// <param name="unitFactory">유닛 생성 함수입니다.</param>
        public void Initialize(
            BattleContext context,
            Tile landingTile,
            Vector3 spawnPosition,
            int unitCount,
            UnitDefinition[] roster,
            Func<UnitDefinition, Team, bool, Vector3, Unit> unitFactory)
        {
            _context = context;
            _landingTile = landingTile;
            _roster = roster;
            _unitFactory = unitFactory;
            _remainingUnits = Mathf.Max(1, unitCount);

            spawnPosition.y = SeaLevel;
            transform.position = spawnPosition;

            // 해안 타일 바로 앞 물 위에 정박합니다.
            Vector3 toShore = landingTile.WorldCenter - spawnPosition;
            toShore.y = 0f;

            Vector3 offshoreDirection = toShore.sqrMagnitude > 0.001f ? -toShore.normalized : Vector3.forward;

            _dockPosition = landingTile.WorldCenter + offshoreDirection * (context.Grid.CellSize * 0.85f);
            _dockPosition.y = SeaLevel;

            _retreatPosition = spawnPosition;

            FaceTowards(_dockPosition);
        }

        // ====================================================================================================
        // 8. Private Methods
        // ====================================================================================================

        private void TickApproach(float deltaTime)
        {
            Vector3 position = transform.position;
            Vector3 next = Vector3.MoveTowards(position, _dockPosition, _context.Tuning.ShipSpeed * deltaTime);
            next.y = SeaLevel;
            transform.position = next;

            FaceTowards(_dockPosition);

            if (Vector3.Distance(next, _dockPosition) <= DockThreshold)
            {
                _state = ShipState.Unloading;
                _unloadTimer = 0f;
            }
        }

        private void TickUnload(float deltaTime)
        {
            _unloadTimer -= deltaTime;
            if (_unloadTimer > 0f)
            {
                return;
            }

            _unloadTimer = _context.Tuning.ShipUnloadInterval;
            SpawnOneUnit();

            _remainingUnits--;
            if (_remainingUnits <= 0)
            {
                _state = ShipState.Leaving;
                UnloadFinished?.Invoke(this);
            }
        }

        private void TickLeave(float deltaTime)
        {
            Vector3 next = Vector3.MoveTowards(transform.position, _retreatPosition, _context.Tuning.ShipSpeed * deltaTime);
            next.y = SeaLevel;
            transform.position = next;

            FaceTowards(_retreatPosition);

            if (Vector3.Distance(next, _retreatPosition) <= DockThreshold)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 병사 한 명을 해안에 내려놓고 이동 AI를 붙입니다.
        /// </summary>
        private void SpawnOneUnit()
        {
            if (_roster == null || _roster.Length == 0 || _unitFactory == null)
            {
                return;
            }

            var definition = _roster[UnityEngine.Random.Range(0, _roster.Length)];

            // 같은 지점에 겹쳐 나오지 않도록 해안 타일 안에서 살짝 흩뜨립니다.
            Vector2 jitter = UnityEngine.Random.insideUnitCircle * (_context.Grid.CellSize * 0.3f);
            Vector3 spawnPosition = _landingTile.WorldCenter + new Vector3(jitter.x, 0f, jitter.y);

            var unit = _unitFactory(definition, Team.Enemy, false, spawnPosition);
            if (unit == null)
            {
                return;
            }

            EnsureSquad().AddUnit(unit);
        }

        /// <summary>
        /// 이 배의 분대를 확보합니다. 첫 병력이 내릴 때 만듭니다.
        /// 배가 떠난 뒤에도 분대는 남아야 하므로 배의 자식으로 두지 않습니다.
        /// </summary>
        private EnemySquad EnsureSquad()
        {
            if (_squad != null)
            {
                return _squad;
            }

            var squadObject = new GameObject($"EnemySquad_{_landingTile.Coord}");
            squadObject.transform.SetParent(transform.parent, worldPositionStays: false);

            _squad = squadObject.AddComponent<EnemySquad>();
            _squad.Initialize(_context, _landingTile.WorldCenter);

            return _squad;
        }

        private void FaceTowards(Vector3 target)
        {
            Vector3 direction = target - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }

        // ====================================================================================================
        // 9. Static Factory
        // ====================================================================================================

        /// <summary>
        /// 프리팹이 있으면 그것으로, 없으면 프리미티브로 상륙정을 만듭니다.
        /// </summary>
        /// <param name="prefab">루트에 <see cref="EnemyShip"/>이 있는 프리팹입니다. null이면 폴백 경로를 씁니다.</param>
        /// <param name="parent">부모 트랜스폼입니다.</param>
        /// <param name="fallbackHullMaterial">폴백 경로에서 선체에 입힐 머티리얼입니다.</param>
        public static EnemyShip Spawn(GameObject prefab, Transform parent, Material fallbackHullMaterial)
        {
            if (prefab != null)
            {
                var instance = Instantiate(prefab, parent);
                var component = instance.GetComponent<EnemyShip>();

                if (component != null)
                {
                    return component;
                }

                Debug.LogWarning($"[EnemyShip] 프리팹 '{prefab.name}' 루트에 EnemyShip 컴포넌트가 없어 폴백으로 생성합니다.");
                Destroy(instance);
            }

            return CreatePrimitive(parent, fallbackHullMaterial);
        }

        /// <summary>
        /// 프리팹 없이 프리미티브로 상륙정을 만듭니다. 에셋 빌더가 프리팹을 구울 때도 이 형태를 씁니다.
        /// </summary>
        public static EnemyShip CreatePrimitive(Transform parent, Material hullMaterial)
        {
            var root = new GameObject("EnemyShip");
            root.transform.SetParent(parent, false);

            var hull = new GameObject("Hull");
            hull.transform.SetParent(root.transform, false);
            hull.transform.localScale = new Vector3(0.9f, 0.35f, 2.1f);
            hull.AddComponent<MeshFilter>().sharedMesh = PrototypeVisuals.GetCubeMesh();
            hull.AddComponent<MeshRenderer>().sharedMaterial = hullMaterial;

            var mast = new GameObject("Mast");
            mast.transform.SetParent(root.transform, false);
            mast.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            mast.transform.localScale = new Vector3(0.08f, 1.3f, 0.08f);
            mast.AddComponent<MeshFilter>().sharedMesh = PrototypeVisuals.GetCubeMesh();
            mast.AddComponent<MeshRenderer>().sharedMaterial = hullMaterial;

            return root.AddComponent<EnemyShip>();
        }
    }
}
