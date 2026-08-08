using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Squads;
using SRPG.Gameplay.Visual;
using SRPG.Systems.Grid;
using SRPG.Systems.Time;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SRPG.Gameplay.Selection
{
    /// <summary>
    /// 플레이어의 유일한 조작 창구입니다.
    ///
    /// 조작 규칙 (Bad North의 미니멀 조작을 따릅니다)
    ///   · 좌클릭으로 분대 근처를 찍으면 선택
    ///   · 분대가 선택된 상태에서 지형을 찍으면 그 타일로 이동 명령
    ///   · 우클릭 또는 Esc로 선택 해제
    ///   · 분대가 선택되어 있는 동안 시간이 느려짐
    ///
    /// 명령의 종류를 늘리지 않는 것이 설계의 핵심입니다.
    /// "어디로 보낼지"만 정하고 나머지 판단은 유닛 AI가 흡수합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SquadSelectionController : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>지형 레이캐스트의 최대 거리입니다.</summary>
        private const float RaycastDistance = 500f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        private readonly List<Squad> _squads = new List<Squad>(8);

        private IslandGrid _grid;
        private TacticalTimeController _clock;
        private BattleTuning _tuning;
        private Camera _camera;
        private Squad _selected;

        private GameObject _selectionMarkerPrefab;
        private GameObject _orderMarkerPrefab;
        private Transform _selectionMarker;
        private Transform _orderMarker;

        /// <summary>
        /// 슬로우모션이 걸려 있는지 여부입니다.
        /// 선택 여부와 따로 두는 이유는, 명령을 내린 뒤에는 분대가 선택된 채로도 시간이 정상으로 돌아가야 하기 때문입니다.
        /// </summary>
        private bool _slowMotionArmed;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>현재 선택된 분대입니다. 없으면 null입니다.</summary>
        public Squad Selected => _selected;

        /// <summary>등록된 분대 목록입니다.</summary>
        public IReadOnlyList<Squad> Squads => _squads;

        // ====================================================================================================
        // 4. Unity Lifecycle
        // ====================================================================================================

        private void Update()
        {
            if (_grid == null || _camera == null)
            {
                return;
            }

            PruneDestroyedSquads();
            ReadInput();
            UpdateMarkers();

            // 슬로우모션은 "분대를 골랐고 아직 명령을 안 내린" 동안에만 걸립니다.
            //
            // 선택 상태만으로 판단하면, 명령을 내린 뒤에도 분대가 선택된 채라 시간이 계속 느려집니다.
            // 그러면 판단이 끝났는데도 화면이 느리게 흘러 답답하고, 무엇보다
            // 아무것도 선택하지 않는 것이 가장 유리한 플레이가 되어 버립니다.
            // 명령을 내리는 순간 시간이 돌아와야, 결정에 대한 결과를 제 속도로 마주하게 됩니다.
            _clock?.SetSlowMotion(
                _slowMotionArmed && _selected != null && !_selected.IsDestroyed);
        }

        // ====================================================================================================
        // 5. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 컨트롤러를 초기화합니다.
        /// </summary>
        /// <param name="grid">클릭 지점을 타일로 옮기는 데 쓰는 지형입니다.</param>
        /// <param name="clock">명령 입력 중 시간을 늦추는 제어기입니다.</param>
        /// <param name="tuning">조작감 수치입니다. 분대를 고르는 클릭 반경이 여기 있습니다.</param>
        /// <param name="battleCamera">클릭 레이캐스트에 사용할 카메라입니다.</param>
        /// <param name="selectionMarkerPrefab">선택 표시 프리팹입니다. null이면 프리미티브로 대체합니다.</param>
        /// <param name="orderMarkerPrefab">명령 지점 표시 프리팹입니다. null이면 프리미티브로 대체합니다.</param>
        public void Initialize(
            IslandGrid grid,
            TacticalTimeController clock,
            BattleTuning tuning,
            Camera battleCamera,
            GameObject selectionMarkerPrefab = null,
            GameObject orderMarkerPrefab = null)
        {
            _grid = grid;
            _clock = clock;
            _tuning = tuning;
            _camera = battleCamera;
            _selectionMarkerPrefab = selectionMarkerPrefab;
            _orderMarkerPrefab = orderMarkerPrefab;

            CreateMarkers();
        }

        /// <summary>
        /// 조작 대상 분대를 등록합니다.
        /// </summary>
        public void RegisterSquad(Squad squad)
        {
            if (squad == null || _squads.Contains(squad))
            {
                return;
            }

            _squads.Add(squad);
            squad.SquadDestroyed += OnSquadDestroyed;
        }

        // ====================================================================================================
        // 6. Private Methods - Input
        // ====================================================================================================

        private void ReadInput()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Deselect();
                return;
            }

            if (mouse == null)
            {
                return;
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                Deselect();
                return;
            }

            if (!mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            if (!TryRaycastGround(mouse.position.ReadValue(), out Vector3 worldPoint))
            {
                return;
            }

            var clickedSquad = FindSquadNear(worldPoint);

            // 분대를 찍었으면 선택하고 슬로우모션을 겁니다.
            // 이미 선택된 분대를 다시 찍은 경우에도 다시 걸어, 명령을 취소하고 재고할 수 있게 합니다.
            if (clickedSquad != null)
            {
                _selected = clickedSquad;
                _slowMotionArmed = true;
                return;
            }

            // 선택된 분대가 있으면 그 지점으로 이동 명령을 내립니다.
            if (_selected != null && !_selected.IsDestroyed)
            {
                var coord = _grid.WorldToCoord(worldPoint);
                if (_selected.IssueMoveOrder(coord))
                {
                    // 명령이 나갔으니 시간을 정상으로 돌립니다.
                    _slowMotionArmed = false;
                }
                else
                {
                    // 갈 수 없는 곳입니다. 슬로우모션을 유지해 다시 찍을 기회를 줍니다.
                    Debug.Log($"[Selection] {coord}로 가는 경로가 없습니다.");
                }
            }
        }

        /// <summary>
        /// 선택을 해제하고 시간을 정상으로 되돌립니다.
        /// </summary>
        private void Deselect()
        {
            _selected = null;
            _slowMotionArmed = false;
        }

        /// <summary>
        /// 화면 좌표에서 지형으로 레이캐스트합니다.
        ///
        /// 반드시 지형 레이어만 훑어야 합니다.
        /// 물리 전투를 도입하면서 유닛에도 콜라이더가 생겼기 때문에,
        /// 마스크 없이 쏘면 병사 몸에 레이가 막혀 그 뒤 지형으로 가는 명령이 통째로 씹힙니다.
        /// </summary>
        private bool TryRaycastGround(Vector2 screenPosition, out Vector3 worldPoint)
        {
            worldPoint = Vector3.zero;

            var ray = _camera.ScreenPointToRay(screenPosition);
            if (Physics.Raycast(ray, out var hit, RaycastDistance, GameLayers.TerrainMask, QueryTriggerInteraction.Ignore))
            {
                worldPoint = hit.point;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 클릭 지점 근처의 분대를 찾습니다.
        /// </summary>
        private Squad FindSquadNear(Vector3 worldPoint)
        {
            float pickRadius = _tuning != null ? _tuning.SquadPickRadius : BattleTuning.DefaultSquadPickRadius;

            Squad best = null;
            float bestSqr = pickRadius * pickRadius;

            for (int i = 0; i < _squads.Count; i++)
            {
                var squad = _squads[i];
                if (squad == null || squad.IsDestroyed)
                {
                    continue;
                }

                Vector3 offset = squad.AnchorPosition - worldPoint;
                offset.y = 0f;

                float sqr = offset.sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = squad;
                }
            }

            return best;
        }

        // ====================================================================================================
        // 7. Private Methods - Markers
        // ====================================================================================================

        /// <summary>
        /// 선택 표시와 명령 지점 표시를 만듭니다.
        /// </summary>
        private void CreateMarkers()
        {
            _selectionMarker = CreateMarker(
                _selectionMarkerPrefab,
                "SelectionMarker",
                new Color(1f, 0.95f, 0.55f),
                new Vector3(1.7f, 0.06f, 1.7f));

            _orderMarker = CreateMarker(
                _orderMarkerPrefab,
                "OrderMarker",
                new Color(0.55f, 0.9f, 1f),
                new Vector3(0.7f, 0.06f, 0.7f));
        }

        /// <summary>
        /// 마커를 만듭니다. 프리팹이 있으면 그것을, 없으면 프리미티브를 씁니다.
        /// </summary>
        private Transform CreateMarker(GameObject prefab, string markerName, Color fallbackColor, Vector3 fallbackScale)
        {
            GameObject go;

            if (prefab != null)
            {
                go = Instantiate(prefab, transform);
                go.name = markerName;
            }
            else
            {
                go = new GameObject(markerName);
                go.transform.SetParent(transform, false);
                go.transform.localScale = fallbackScale;

                go.AddComponent<MeshFilter>().sharedMesh = PrototypeVisuals.GetCubeMesh();
                go.AddComponent<MeshRenderer>().sharedMaterial = PrototypeVisuals.CreateMaterial(fallbackColor);
            }

            go.SetActive(false);
            return go.transform;
        }

        /// <summary>
        /// 표시의 위치와 노출 여부를 갱신합니다.
        /// </summary>
        private void UpdateMarkers()
        {
            bool hasSelection = _selected != null && !_selected.IsDestroyed;

            if (_selectionMarker != null)
            {
                _selectionMarker.gameObject.SetActive(hasSelection);
                if (hasSelection)
                {
                    Vector3 position = _selected.AnchorPosition;
                    position.y += 0.12f;
                    _selectionMarker.position = position;
                }
            }

            if (_orderMarker != null)
            {
                bool showOrder = hasSelection && _selected.OrderedCoord.IsValid;
                _orderMarker.gameObject.SetActive(showOrder);

                if (showOrder)
                {
                    Vector3 position = _grid.CoordToWorld(_selected.OrderedCoord);
                    position.y += 0.14f;
                    _orderMarker.position = position;
                }
            }
        }

        // ====================================================================================================
        // 8. Private Methods - Lifecycle
        // ====================================================================================================

        private void OnSquadDestroyed(Squad squad)
        {
            squad.SquadDestroyed -= OnSquadDestroyed;
            _squads.Remove(squad);

            if (_selected == squad)
            {
                _selected = null;
            }
        }

        private void PruneDestroyedSquads()
        {
            for (int i = _squads.Count - 1; i >= 0; i--)
            {
                if (_squads[i] == null || _squads[i].IsDestroyed)
                {
                    _squads.RemoveAt(i);
                }
            }

            if (_selected != null && _selected.IsDestroyed)
            {
                _selected = null;
            }
        }
    }
}
