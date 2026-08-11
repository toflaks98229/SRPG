using SRPG.Data;
using SRPG.Systems.Grid;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SRPG.Gameplay.CameraControl
{
    /// <summary>
    /// 전투 카메라의 <b>피벗</b>입니다. 이 컴포넌트가 붙은 오브젝트가 카메라의 부모이자 초점입니다.
    ///
    /// <b>구조</b>
    /// <code>
    /// CameraPivot   ← 이 컴포넌트. WASD로 섬 위를 훑고 다닙니다
    ///   └ Camera    ← 자식. 피벗을 바라보며 그 둘레를 돕니다
    /// </code>
    ///
    /// <b>왜 피벗과 카메라를 나누는가</b>
    ///
    /// 예전에는 이 컴포넌트가 카메라 자신에게 붙어 있었고, 초점은 <c>_focusPoint</c> 라는 필드였습니다.
    /// 초점이 씬에 실체가 없으니 눈으로 볼 수도, 손으로 끌어 볼 수도, 다른 것을 붙일 수도 없었습니다.
    /// 초점을 실제 오브젝트로 만들면 그 자리에 마커나 미니맵 기준점을 붙일 수 있고,
    /// 무엇보다 <b>"카메라를 움직인다"가 아니라 "보고 있는 지점을 옮긴다"</b>가 되어 조작이 자연스러워집니다.
    ///
    /// <b>피벗은 회전하지 않습니다.</b> 이동만 합니다.
    /// 회전은 자식 카메라의 로컬 변환이 전담하므로, 피벗의 월드 좌표가 곧 화면 중앙이 봅니다.
    ///
    /// 모든 입력과 보간에 <c>unscaledDeltaTime</c>을 씁니다.
    /// 슬로우모션 중에 카메라까지 느려지면 조작감이 무너지기 때문입니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BattleCameraRig : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>
        /// 카메라가 피벗에서 물러나 서는 <b>고정</b> 거리입니다.
        ///
        /// <b>직교에서 거리는 줌이 아닙니다.</b> 뒤로 물려도 화면에 담기는 것은 그대로이고,
        /// 달라지는 것은 무엇이 근평면에 잘리는가뿐입니다.
        /// 그래서 지형과 화살이 잘리지 않을 만큼만 넉넉히 물려 두고 고정합니다.
        /// </summary>
        private const float CameraStandoff = 60f;

        /// <summary>가장 가까이 당겼을 때 화면에 담기는 월드 높이의 절반입니다.</summary>
        private const float MinZoom = 8f;

        /// <summary>가장 멀리 밀었을 때 화면에 담기는 월드 높이의 절반입니다.</summary>
        private const float MaxZoom = 30f;

        /// <summary>궤도 회전 속도입니다. 초당 각도입니다.</summary>
        private const float RotationSpeed = 90f;

        /// <summary>휠 한 칸이 바꾸는 줌입니다.</summary>
        private const float ZoomSpeed = 3.5f;
        /// <summary>회전·확대가 목표값을 따라가는 속도입니다.</summary>
        private const float SmoothSpeed = 10f;

        /// <summary>피벗이 지면 높이를 따라가는 속도입니다. 계단형 지형에서 튀지 않게 부드럽게 붙습니다.</summary>
        private const float GroundFollowSpeed = 6f;

        /// <summary>이동 입력이 이보다 작으면 없는 것으로 봅니다.</summary>
        private const float InputDeadZone = 0.01f;

        // ====================================================================================================
        // 2. Inspector
        // ====================================================================================================

        [Header("카메라")]
        [SerializeField]
        [Tooltip("이 피벗을 바라볼 자식 카메라입니다. 비워 두면 자식에서 찾습니다.")]
        private Transform _cameraTransform;

        [Header("궤도")]
        [SerializeField]
        [Tooltip("카메라가 내려다보는 각도입니다.")]
        private float _pitch = 47f;

        [SerializeField]
        [Tooltip("카메라의 초기 수평 회전값입니다.")]
        private float _yaw = 35f;

        [SerializeField]
        [Range(MinZoom, MaxZoom)]
        [Tooltip("화면에 담기는 월드 높이의 절반입니다. 직교 카메라의 Orthographic Size 와 같습니다.\n\n" +
                 "<b>거리가 아닙니다.</b> 직교에서는 카메라를 뒤로 물려도 화면이 그대로이므로, " +
                 "줌은 이 값 하나로 정해집니다.")]
        private float _zoom = 19.5f;

        [SerializeField]
        [Range(0, 32)]
        [Tooltip("수평 회전을 몇 개의 자세로 끊을지입니다. 0이면 자유롭게 돕니다.\n\n" +
                 "저해상도로 그릴 때 카메라가 도는 동안 그림이 픽셀 격자 위에서 계속 다시 표본을 뽑아 " +
                 "지글거립니다. 픽셀 격자를 붙잡아도 이것만은 남습니다 — 회전이 정말로 일어나고 있기 때문입니다.\n" +
                 "자세를 끊으면 넘어가는 순간에만 다시 뽑히고, 머무는 동안에는 완전히 고정된 그림이 됩니다.\n\n" +
                 "8이면 45도마다, 16이면 22.5도마다 섭니다. 조작감이 크게 달라지므로 켜기 전에 시험해 보십시오.")]
        private int _yawSteps;

        [Header("이동")]
        [SerializeField]
        [Min(1f)]
        [Tooltip("WASD 이동 속도입니다. 부트스트랩이 전투 튜닝 값으로 덮어씁니다.")]
        private float _panSpeed = 18f;

        // ====================================================================================================
        // 3. Fields
        // ====================================================================================================

        /// <summary>이동 범위를 정할 때 기준이 되는 지형입니다.</summary>
        private IslandGrid _grid;

        /// <summary>줌을 실제로 적용할 카메라입니다. 트랜스폼만으로는 직교 크기를 만질 수 없습니다.</summary>
        private Camera _camera;

        /// <summary>확대·축소가 수렴할 목표 줌입니다.</summary>
        private float _targetZoom;
        /// <summary>궤도 회전이 수렴할 목표 각도입니다.</summary>
        private float _targetYaw;

        // 피벗이 벗어날 수 없는 XZ 범위입니다.
        /// <summary>이동 범위가 계산됐는지 여부입니다. 지형이 없으면 범위를 두지 않습니다.</summary>
        private bool _hasBounds;
        /// <summary>피벗이 갈 수 있는 최소 X입니다.</summary>
        private float _minX;
        /// <summary>피벗이 갈 수 있는 최대 X입니다.</summary>
        private float _maxX;
        /// <summary>피벗이 갈 수 있는 최소 Z입니다.</summary>
        private float _minZ;
        /// <summary>피벗이 갈 수 있는 최대 Z입니다.</summary>
        private float _maxZ;

        // ====================================================================================================
        // 4. Properties
        // ====================================================================================================

        /// <summary>피벗의 현재 월드 좌표입니다. 화면 중앙이 보는 지점입니다.</summary>
        public Vector3 FocusPoint => transform.position;

        // ====================================================================================================
        // 5. Unity Lifecycle
        // ====================================================================================================

        private void Awake()
        {
            _targetZoom = _zoom;
            _targetYaw = _yaw;

            if (_cameraTransform == null)
            {
                var childCamera = GetComponentInChildren<Camera>();
                if (childCamera != null)
                {
                    _cameraTransform = childCamera.transform;
                }
            }

            CacheCamera();
        }

        private void LateUpdate()
        {
            float deltaTime = UnityEngine.Time.unscaledDeltaTime;

            ReadOrbitInput(deltaTime);
            ReadPanInput(deltaTime);

            _yaw = Mathf.LerpAngle(_yaw, ResolveTargetYaw(), SmoothSpeed * deltaTime);
            _zoom = Mathf.Lerp(_zoom, _targetZoom, SmoothSpeed * deltaTime);

            FollowGround(deltaTime);
            PlaceCamera();
        }

        // ====================================================================================================
        // 6. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 카메라를 이 피벗의 자식으로 붙입니다.
        ///
        /// 피벗의 회전은 항상 기본값으로 되돌립니다. 회전은 자식 카메라가 전담해야
        /// 피벗의 월드 좌표가 곧 초점이라는 관계가 유지됩니다.
        /// </summary>
        /// <param name="battleCamera">피벗의 자식으로 붙일 카메라입니다.</param>
        public void AttachCamera(Camera battleCamera)
        {
            if (battleCamera == null)
            {
                return;
            }

            transform.rotation = Quaternion.identity;

            _cameraTransform = battleCamera.transform;
            _cameraTransform.SetParent(transform, worldPositionStays: false);

            _camera = battleCamera;

            // 직교가 아니면 픽셀 격자를 붙잡을 수 없습니다. 텍셀이 덮는 월드 길이가
            // 깊이마다 달라져 한 평면에서만 맞기 때문입니다. 씬이 원근으로 구워져 있어도 여기서 되돌립니다.
            _camera.orthographic = true;

            // 즉시 배치합니다. LateUpdate를 기다리면 첫 프레임 동안 카메라가
            // 피벗과 같은 자리, 즉 지형 속에 박힌 채로 한 장이 그려집니다.
            PlaceCamera();
        }

        /// <summary>
        /// 지형을 연결하고 이동 범위를 잡습니다.
        /// </summary>
        /// <param name="grid">섬 지형입니다. 이동 범위와 지면 높이의 근거가 됩니다.</param>
        /// <param name="tuning">전투 튜닝입니다. null이면 인스펙터 값을 그대로 씁니다.</param>
        public void Configure(IslandGrid grid, BattleTuning tuning)
        {
            _grid = grid;

            float margin = 6f;

            if (tuning != null)
            {
                _panSpeed = tuning.Camera.PanSpeed;
                margin = tuning.Camera.BoundsMargin;
            }

            ComputeBounds(grid, margin);
        }

        /// <summary>
        /// 피벗을 지정 위치로 즉시 옮깁니다. 초기 배치에 씁니다.
        /// </summary>
        /// <param name="world">피벗을 옮길 월드 좌표입니다. 높이는 지형을 따라 보정됩니다.</param>
        public void MoveTo(Vector3 world)
        {
            Vector3 clamped = ClampToBounds(world);
            clamped.y = _grid != null ? _grid.SampleGroundHeight(clamped) : world.y;

            transform.position = clamped;
            PlaceCamera();
        }

        /// <summary>
        /// 섬 크기에 맞춰 초기 줌을 잡습니다.
        /// </summary>
        /// <param name="areaSize">화면에 담고 싶은 영역의 한 변 길이입니다.</param>
        public void FrameArea(float areaSize)
        {
            // 절반이 화면 높이의 절반에 대응합니다. 비스듬히 내려다보므로 지면은 그보다 넓게 잡히고,
            // 그 여유를 계수로 조금 덜어 냅니다.
            _targetZoom = Mathf.Clamp(areaSize * 0.5f, MinZoom, MaxZoom);
            _zoom = _targetZoom;

            PlaceCamera();
        }

        // ====================================================================================================
        // 7. Private Methods - Input
        // ====================================================================================================

        /// <summary>
        /// Q/E로 회전하고 마우스 휠로 확대·축소합니다.
        /// </summary>
        private void ReadOrbitInput(float deltaTime)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.qKey.isPressed)
                {
                    _targetYaw -= RotationSpeed * deltaTime;
                }

                if (keyboard.eKey.isPressed)
                {
                    _targetYaw += RotationSpeed * deltaTime;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    // 휠 값의 크기는 플랫폼마다 다르므로 부호만 사용합니다.
                    _targetZoom = Mathf.Clamp(
                        _targetZoom - Mathf.Sign(scroll) * ZoomSpeed,
                        MinZoom,
                        MaxZoom);
                }
            }
        }

        /// <summary>
        /// WASD(또는 방향키)로 피벗을 옮깁니다.
        ///
        /// 이동 방향은 <b>월드 축이 아니라 카메라가 보는 방향</b> 기준입니다.
        /// W를 누르면 항상 "화면 위쪽"으로 갑니다. 월드 축을 쓰면 카메라를 돌린 뒤
        /// W가 옆으로 가 버려서, 회전과 이동을 함께 쓸 수가 없습니다.
        /// </summary>
        private void ReadPanInput(float deltaTime)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            float x = 0f;
            float z = 0f;

            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) z -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) z += 1f;

            Vector3 input = new Vector3(x, 0f, z);
            if (input.sqrMagnitude < InputDeadZone)
            {
                return;
            }

            // 현재 화면에 보이는 방향을 기준으로 삼습니다.
            // 목표 각도가 아니라 보간된 현재 각도를 쓰는 이유는, 회전이 끝나기 전에 눌러도
            // 눈에 보이는 대로 움직여야 하기 때문입니다.
            Vector3 direction = Quaternion.Euler(0f, _yaw, 0f) * input.normalized;

            Vector3 next = transform.position + direction * (_panSpeed * deltaTime);
            transform.position = ClampToBounds(next);
        }

        // ====================================================================================================
        // 8. Private Methods - Placement
        // ====================================================================================================

        /// <summary>
        /// 피벗을 지면 높이에 부드럽게 붙입니다.
        ///
        /// 즉시 맞추지 않고 보간하는 이유는 지형이 계단형이기 때문입니다.
        /// 고도가 한 단계 바뀌는 경계를 지날 때마다 화면이 툭툭 튀어 오르면 훑어보기가 불편합니다.
        /// </summary>
        private void FollowGround(float deltaTime)
        {
            if (_grid == null)
            {
                return;
            }

            Vector3 position = transform.position;
            float targetY = _grid.SampleGroundHeight(position);

            position.y = Mathf.Lerp(position.y, targetY, GroundFollowSpeed * deltaTime);
            transform.position = position;
        }

        /// <summary>
        /// 자식 카메라를 궤도 위에 놓고 피벗을 바라보게 합니다.
        ///
        /// 피벗이 회전하지 않으므로 로컬 방향이 곧 월드 방향입니다.
        /// 카메라를 피벗 뒤쪽으로 <c>distance</c>만큼 밀고 같은 회전을 주면,
        /// 시선이 정확히 피벗의 원점을 지납니다.
        /// </summary>
        /// <summary>
        /// 이번 프레임에 향할 야우입니다. 단계가 정해져 있으면 그 눈금으로 끊습니다.
        ///
        /// <b>왜 회전을 끊는가</b>
        ///
        /// 저해상도로 그리면 카메라가 도는 동안 그림이 픽셀 격자 위에서 계속 다시 표본을 뽑습니다.
        /// 픽셀 격자를 아무리 붙잡아도 이것은 남습니다 — 어긋난 것이 아니라
        /// <b>회전이 정말로 일어나고 있는</b> 것이기 때문입니다.
        ///
        /// 3D 픽셀아트가 이 문제를 다루는 방법은 하나뿐입니다. 회전을 <b>몇 개의 자세로</b> 끊는 것입니다.
        /// 자세 사이를 넘어갈 때만 다시 뽑히고, 머물러 있는 동안에는 완전히 고정된 그림이 됩니다.
        ///
        /// <b>기본은 꺼져 있습니다.</b> 자유 회전을 끊는 것은 그림의 문제가 아니라 조작감의 문제이고,
        /// 그 판단은 코드가 아니라 사람이 해야 합니다.
        /// </summary>
        /// <returns>보간이 향할 야우입니다.</returns>
        private float ResolveTargetYaw()
        {
            if (_yawSteps <= 0)
            {
                return _targetYaw;
            }

            float stepDegrees = 360f / _yawSteps;

            return Mathf.Round(_targetYaw / stepDegrees) * stepDegrees;
        }

        private void PlaceCamera()
        {
            if (_cameraTransform == null)
            {
                return;
            }

            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            // 거리는 고정입니다. 직교에서 화면 크기를 정하는 것은 아래의 orthographicSize 뿐입니다.
            _cameraTransform.SetLocalPositionAndRotation(
                -(rotation * Vector3.forward) * CameraStandoff,
                rotation);

            if (CacheCamera())
            {
                _camera.orthographicSize = _zoom;
            }
        }

        /// <summary>
        /// 자식 카메라를 찾아 들고 있습니다. 직교 크기를 만지려면 트랜스폼이 아니라 카메라가 필요합니다.
        /// </summary>
        /// <returns>쓸 수 있는 카메라를 들고 있으면 true입니다.</returns>
        private bool CacheCamera()
        {
            if (_camera != null)
            {
                return true;
            }

            if (_cameraTransform != null)
            {
                _camera = _cameraTransform.GetComponent<Camera>();
            }

            if (_camera == null)
            {
                return false;
            }

            _camera.orthographic = true;

            return true;
        }

        // ====================================================================================================
        // 9. Private Methods - Bounds
        // ====================================================================================================

        /// <summary>
        /// 통행 가능한 타일의 외곽에 여유를 더해 이동 범위를 잡습니다.
        ///
        /// 격자 전체가 아니라 <b>육지 기준</b>인 것이 핵심입니다.
        /// 격자에는 섬을 둘러싼 바다가 넓게 포함되어 있어서, 그 기준으로 잡으면
        /// 카메라가 아무것도 없는 빈 바다까지 한참 나갈 수 있습니다.
        ///
        /// 사각형으로 자르는 이유는 부드럽기 때문입니다. 실제 해안선 모양대로 자르면
        /// 들쭉날쭉한 경계에 걸려 이동이 턱턱 끊깁니다.
        /// </summary>
        private void ComputeBounds(IslandGrid grid, float margin)
        {
            _hasBounds = false;

            if (grid == null || grid.WalkableTiles.Count == 0)
            {
                return;
            }

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;

            for (int i = 0; i < grid.WalkableTiles.Count; i++)
            {
                Vector3 center = grid.WalkableTiles[i].WorldCenter;

                if (center.x < minX) minX = center.x;
                if (center.x > maxX) maxX = center.x;
                if (center.z < minZ) minZ = center.z;
                if (center.z > maxZ) maxZ = center.z;
            }

            _minX = minX - margin;
            _maxX = maxX + margin;
            _minZ = minZ - margin;
            _maxZ = maxZ + margin;
            _hasBounds = true;
        }

        /// <summary>
        /// 이동 범위 안으로 자릅니다. 범위가 없으면 그대로 돌려줍니다.
        /// </summary>
        private Vector3 ClampToBounds(Vector3 world)
        {
            if (!_hasBounds)
            {
                return world;
            }

            world.x = Mathf.Clamp(world.x, _minX, _maxX);
            world.z = Mathf.Clamp(world.z, _minZ, _maxZ);

            return world;
        }
    }
}
