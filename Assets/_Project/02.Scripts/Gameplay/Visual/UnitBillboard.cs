using SRPG.Gameplay.Units;
using SRPG.Systems.Rendering;
using UnityEngine;

namespace SRPG.Gameplay.Visual
{
    /// <summary>
    /// 유닛의 몸을 2.5D 빌보드로 그립니다.
    ///
    /// <b>회전은 셰이더가, 방향 판독은 이 컴포넌트가</b>
    ///
    /// 스프라이트를 카메라 쪽으로 돌리는 일은 정점 셰이더가 합니다.
    /// 여기서 트랜스폼을 돌리면 수백 유닛의 계층 갱신이 매 프레임 발생합니다.
    ///
    /// 이 컴포넌트가 하는 일은 하나입니다 — <b>지금 어느 면을 보여 줄지</b> 정해서 넘깁니다.
    /// 유닛의 논리적 방향은 트랜스폼이 계속 들고 있고(무기 판정이 그걸 씁니다),
    /// 화면에 보일 그림은 그 방향과 카메라 방향의 차이로 정합니다.
    ///
    /// <b>몸만 빌보드입니다.</b>
    /// 창·활·방패처럼 방향이 게임 규칙인 것은 3D로 남습니다.
    /// 조사에서 확인한 대로, 비대칭 장비를 스프라이트로 뒤집으면 반대 손으로 옮겨 가 버립니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitBillboard : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>방향이 이만큼 바뀌어야 그림을 갈아 끼웁니다. 경계에서 깜빡이는 것을 막습니다.</summary>
        private const float DirectionHysteresisDegrees = 6f;

        // ====================================================================================================
        // 2. Inspector
        // ====================================================================================================

        [SerializeField]
        [Tooltip("빌보드로 그릴 렌더러입니다. 비우면 자식에서 찾습니다.")]
        private Renderer _renderer;

        [SerializeField]
        [Range(1, 16)]
        [Tooltip("스프라이트 시트가 담고 있는 방향 수입니다.")]
        private int _directionCount = BillboardDirection.DirectionCount;

        [SerializeField]
        [Tooltip("좌우 반전으로 절반의 그림만 쓰는지 여부입니다.\n" +
                 "방패나 활처럼 <b>한쪽에 쏠린 장비</b>가 있으면 켜면 안 됩니다. 반대 손으로 옮겨 갑니다.")]
        private bool _useMirroring;

        // ====================================================================================================
        // 3. Fields
        // ====================================================================================================

        /// <summary>빌보드 셰이더의 방향 칸 수 프로퍼티 식별자입니다.</summary>
        private static readonly int FrameCountId = Shader.PropertyToID("_FrameCount");
        /// <summary>빌보드 셰이더의 현재 방향 칸 프로퍼티 식별자입니다.</summary>
        private static readonly int FrameIndexId = Shader.PropertyToID("_FrameIndex");
        /// <summary>빌보드 셰이더의 좌우 반전 프로퍼티 식별자입니다.</summary>
        private static readonly int FlipXId = Shader.PropertyToID("_FlipX");

        /// <summary>머티리얼을 복제하지 않고 값만 바꾸기 위한 프로퍼티 블록입니다.</summary>
        private MaterialPropertyBlock _properties;
        /// <summary>자기 트랜스폼 캐시입니다.</summary>
        private Transform _transform;
        /// <summary>방향을 재는 기준이 되는 카메라입니다.</summary>
        private Camera _camera;

        /// <summary>지금 그리고 있는 방향 칸입니다. -1이면 아직 정해지지 않았습니다.</summary>
        private int _currentIndex = -1;
        /// <summary>마지막으로 계산한 상대 각도입니다. 변화가 없으면 갱신을 건너뜁니다.</summary>
        private float _lastAngle = -1f;

        // ====================================================================================================
        // 4. Unity Lifecycle
        // ====================================================================================================

        private void Awake()
        {
            _transform = transform;

            if (_renderer == null)
            {
                _renderer = GetComponentInChildren<Renderer>();
            }

            _properties = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            if (_renderer == null)
            {
                return;
            }

            var activeCamera = ResolveCamera();
            if (activeCamera == null)
            {
                return;
            }

            UpdateDirection(activeCamera);
        }

        // ====================================================================================================
        // 5. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 이 유닛이 쓸 스프라이트 구성을 지정합니다.
        /// </summary>
        /// <param name="directionCount">스프라이트 시트의 방향 수입니다.</param>
        /// <param name="useMirroring">좌우 반전으로 절반만 쓸지 여부입니다.</param>
        public void Configure(int directionCount, bool useMirroring)
        {
            _directionCount = Mathf.Clamp(directionCount, 1, 16);
            _useMirroring = useMirroring;

            // 다음 프레임에 반드시 다시 계산하게 합니다.
            _currentIndex = -1;
            _lastAngle = -1f;
        }

        // ====================================================================================================
        // 6. Private Methods
        // ====================================================================================================

        private Camera ResolveCamera()
        {
            // 카메라는 전투 중에 바뀌지 않으므로 한 번만 찾습니다.
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            return _camera;
        }

        /// <summary>
        /// 지금 보여 줄 면을 정해 셰이더에 넘깁니다.
        /// </summary>
        private void UpdateDirection(Camera activeCamera)
        {
            float angle = BillboardDirection.RelativeAngle(
                _transform.forward,
                activeCamera.transform.forward);

            // 구간 경계에서 각도가 미세하게 오가면 그림이 깜빡입니다.
            if (_lastAngle >= 0f && Mathf.Abs(Mathf.DeltaAngle(_lastAngle, angle)) < DirectionHysteresisDegrees)
            {
                return;
            }

            _lastAngle = angle;

            int index = BillboardDirection.ToIndex(angle, _directionCount);
            bool mirrored = false;

            if (_useMirroring)
            {
                index = BillboardDirection.ToMirroredIndex(index, _directionCount, out mirrored);
            }

            if (index == _currentIndex)
            {
                return;
            }

            _currentIndex = index;

            // MaterialPropertyBlock 을 쓰면 유닛마다 머티리얼을 복제하지 않아도 됩니다.
            // 복제하면 배칭이 통째로 깨집니다.
            _renderer.GetPropertyBlock(_properties);

            _properties.SetFloat(FrameCountId, _directionCount);
            _properties.SetFloat(FrameIndexId, index);
            _properties.SetFloat(FlipXId, mirrored ? 1f : 0f);

            _renderer.SetPropertyBlock(_properties);
        }
    }
}
