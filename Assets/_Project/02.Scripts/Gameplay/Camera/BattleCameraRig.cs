using UnityEngine;
using UnityEngine.InputSystem;

namespace SRPG.Gameplay.CameraControl
{
    /// <summary>
    /// 전투 카메라입니다. 섬 하나가 한 화면에 들어오는 것을 전제로 한 궤도형 카메라입니다.
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

        private const float MinDistance = 14f;
        private const float MaxDistance = 52f;
        private const float RotationSpeed = 90f;
        private const float ZoomSpeed = 6f;
        private const float SmoothSpeed = 10f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        [Header("궤도")]
        [SerializeField]
        [Tooltip("카메라가 내려다보는 각도입니다.")]
        private float _pitch = 47f;

        [SerializeField]
        [Tooltip("카메라의 초기 수평 회전값입니다.")]
        private float _yaw = 35f;

        [SerializeField]
        [Tooltip("초점으로부터의 거리입니다.")]
        private float _distance = 34f;

        private Vector3 _focusPoint;
        private float _targetDistance;
        private float _targetYaw;

        // ====================================================================================================
        // 3. Unity Lifecycle
        // ====================================================================================================

        private void Awake()
        {
            _targetDistance = _distance;
            _targetYaw = _yaw;
        }

        private void LateUpdate()
        {
            float deltaTime = UnityEngine.Time.unscaledDeltaTime;

            ReadInput(deltaTime);

            _yaw = Mathf.LerpAngle(_yaw, _targetYaw, SmoothSpeed * deltaTime);
            _distance = Mathf.Lerp(_distance, _targetDistance, SmoothSpeed * deltaTime);

            var rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.SetPositionAndRotation(
                _focusPoint - rotation * Vector3.forward * _distance,
                rotation);
        }

        // ====================================================================================================
        // 4. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 카메라가 바라볼 초점을 지정합니다. 보통 섬의 중심입니다.
        /// </summary>
        public void SetFocus(Vector3 focusPoint)
        {
            _focusPoint = focusPoint;
        }

        /// <summary>
        /// 섬 크기에 맞춰 초기 거리를 잡습니다.
        /// </summary>
        public void FrameArea(float areaSize)
        {
            _targetDistance = Mathf.Clamp(areaSize * 0.85f, MinDistance, MaxDistance);
            _distance = _targetDistance;
        }

        // ====================================================================================================
        // 5. Private Methods
        // ====================================================================================================

        /// <summary>
        /// Q/E로 회전하고 마우스 휠로 확대·축소합니다.
        /// </summary>
        private void ReadInput(float deltaTime)
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
                    _targetDistance = Mathf.Clamp(
                        _targetDistance - Mathf.Sign(scroll) * ZoomSpeed,
                        MinDistance,
                        MaxDistance);
                }
            }
        }
    }
}
