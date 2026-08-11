using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SRPG.Gameplay.CameraControl
{
    /// <summary>
    /// 저해상도로 그릴 때 화면이 기어다니지 않도록 픽셀 격자를 붙잡아 둡니다.
    ///
    /// <b>왜 필요한가</b>
    ///
    /// 저해상도로 그린 뒤 점 필터로 확대하면 한 픽셀이 화면에서 여러 픽셀을 덮습니다.
    /// 그 상태에서 카메라가 부드럽게 움직이면 월드의 한 점이 저해상도 격자의 칸을
    /// <b>조금씩 넘나듭니다</b>. 넘어가는 순간 그 점이 통째로 한 칸 튀고,
    /// 화면 전체에서 그것이 제각기 일어나면 들판이 지글지글 기어다닙니다.
    ///
    /// <b>카메라를 옮기지 않습니다</b>
    ///
    /// 처음에는 카메라 자리를 격자에 끊어 붙였습니다. 그것이 <b>회전할 때 화면을 떨게 만들었습니다.</b>
    ///
    /// 끊는 기준이 카메라 자신의 가로·세로축인데, 카메라가 돌면 그 축도 함께 돕니다.
    /// 같은 월드 좌표를 회전하는 축으로 분해하면 값이 계속 달라지고,
    /// 반올림한 결과를 다시 자리로 되돌리면 카메라가 <b>반 픽셀 반경으로 원을 그리며 흔들립니다</b>.
    /// 회전이 매끄러울수록(리그가 각도를 보간합니다) 그 흔들림이 매 프레임 이어집니다.
    ///
    /// 지금은 카메라를 그대로 두고 <b>투영 행렬만</b> 어긋난 만큼 밀어 둡니다.
    /// 자리가 바뀌지 않으니 흔들릴 것이 없고, 그리는 결과만 격자에 얹힙니다.
    /// 이것이 3D 픽셀아트에서 쓰는 표준 방식입니다.
    ///
    /// <b>회전 자체의 지글거림은 이것으로 없앨 수 없습니다</b>
    ///
    /// 카메라가 돌면 월드와 픽셀 격자의 대응 자체가 돌아갑니다.
    /// 격자를 붙잡아도 그림이 격자 위에서 실제로 회전하므로 다시 표본이 뽑힙니다 —
    /// 그것은 어긋남이 아니라 회전이 정말로 일어나고 있다는 뜻입니다.
    /// 그것까지 없애려면 회전을 <b>단계로 끊어야</b> 합니다(<c>BattleCameraRig</c> 의 야우 단계).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(100)]
    public sealed class PixelSnapCamera : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Inspector
        // ====================================================================================================

        [SerializeField]
        [Tooltip("픽셀 격자를 붙잡을지 여부입니다. 끄면 저해상도에서 화면이 기어다닙니다.")]
        private bool _snap = true;

        [SerializeField]
        [Tooltip("초점이 놓인 평면입니다. 비우면 부모를 씁니다.\n" +
                 "카메라 리그에서는 피벗이 곧 부대가 선 지면입니다.")]
        private Transform _focus;

        [SerializeField]
        [Range(0.05f, 1f)]
        [Tooltip("렌더 배율을 읽지 못했을 때 쓸 값입니다. 파이프라인 에셋의 Render Scale 과 같게 두십시오.")]
        private float _fallbackRenderScale = 0.25f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        /// <summary>이 컴포넌트가 붙은 카메라입니다.</summary>
        private Camera _camera;

        // ====================================================================================================
        // 3. Properties
        // ====================================================================================================

        /// <summary>이번 프레임에 한 픽셀이 덮은 월드 길이입니다. 진단용입니다.</summary>
        public float WorldUnitsPerPixel { get; private set; }

        // ====================================================================================================
        // 4. Unity Lifecycle
        // ====================================================================================================

        private void Awake()
        {
            _camera = GetComponent<Camera>();

            if (_focus == null)
            {
                _focus = transform.parent;
            }
        }

        /// <summary>
        /// 투영 행렬을 덮어쓴 채로 남겨 두지 않습니다.
        ///
        /// 한 번 지정한 투영 행렬은 계속 유지됩니다. 이 컴포넌트를 끄거나 지웠을 때
        /// 마지막으로 밀어 둔 화면이 그대로 굳어 있으면 원인을 찾기 어렵습니다.
        /// </summary>
        private void OnDisable()
        {
            if (_camera != null)
            {
                _camera.ResetProjectionMatrix();
            }
        }

        /// <summary>
        /// 카메라 리그가 자리를 잡은 <b>뒤에</b> 어긋난 만큼을 투영으로 되돌립니다.
        ///
        /// 실행 순서를 뒤로 미뤄 둔 것이 그 때문입니다. 리그보다 먼저 돌면
        /// 리그가 자리를 다시 잡아 계산이 한 프레임 늦은 값이 됩니다.
        /// </summary>
        private void LateUpdate()
        {
            if (_camera == null)
            {
                return;
            }

            // 매 프레임 원래 투영으로 되돌린 뒤 다시 밉니다.
            // 그러지 않으면 어긋남이 프레임마다 누적되어 화면이 흘러갑니다.
            _camera.ResetProjectionMatrix();

            if (!_snap)
            {
                return;
            }

            float worldPerPixel = ResolveWorldUnitsPerPixel(out float renderWidth, out float renderHeight);

            WorldUnitsPerPixel = worldPerPixel;

            if (worldPerPixel <= 0f)
            {
                return;
            }

            var position = transform.position;

            // 카메라가 격자에서 몇 픽셀만큼 벗어나 있는지를 구합니다. -0.5 에서 0.5 사이입니다.
            float offsetX = SubPixelOffset(Vector3.Dot(position, transform.right), worldPerPixel);
            float offsetY = SubPixelOffset(Vector3.Dot(position, transform.up), worldPerPixel);

            // 화면 좌표계는 가로 세로 모두 -1 에서 1 이라, 픽셀 하나가 2/해상도 입니다.
            var projection = _camera.projectionMatrix;

            projection.m02 += offsetX * 2f / renderWidth;
            projection.m12 += offsetY * 2f / renderHeight;

            _camera.projectionMatrix = projection;
        }

        // ====================================================================================================
        // 5. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 격자에서 벗어난 만큼을 픽셀 단위로 구합니다.
        /// </summary>
        /// <param name="along">카메라 축 방향의 좌표입니다.</param>
        /// <param name="worldPerPixel">픽셀 하나가 덮는 월드 길이입니다.</param>
        /// <returns>-0.5 에서 0.5 사이의 어긋남입니다.</returns>
        private static float SubPixelOffset(float along, float worldPerPixel)
        {
            float inPixels = along / worldPerPixel;

            return inPixels - Mathf.Round(inPixels);
        }

        /// <summary>
        /// 초점 평면에서 저해상도 픽셀 하나가 덮는 월드 길이를 구합니다.
        /// </summary>
        /// <param name="renderWidth">저해상도 렌더의 가로 픽셀 수입니다.</param>
        /// <param name="renderHeight">저해상도 렌더의 세로 픽셀 수입니다.</param>
        /// <returns>월드 길이입니다. 구할 수 없으면 0입니다.</returns>
        private float ResolveWorldUnitsPerPixel(out float renderWidth, out float renderHeight)
        {
            float scale = ResolveRenderScale();

            renderWidth = Mathf.Max(1f, Screen.width * scale);
            renderHeight = Mathf.Max(1f, Screen.height * scale);

            if (_camera.orthographic)
            {
                // 직교에서는 거리와 무관하게 화면 높이가 곧 월드 높이입니다.
                return _camera.orthographicSize * 2f / renderHeight;
            }

            // 원근에서는 한 픽셀이 덮는 월드 크기가 거리마다 다릅니다.
            // 부대가 서 있는 평면, 즉 피벗이 놓인 자리를 기준으로 잡습니다.
            float distance = _focus != null
                ? Vector3.Distance(transform.position, _focus.position)
                : Mathf.Max(1f, _camera.nearClipPlane);

            if (distance <= 0.0001f)
            {
                return 0f;
            }

            float visibleHeight = 2f * distance * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            return visibleHeight / renderHeight;
        }

        /// <summary>
        /// 지금 파이프라인이 쓰는 렌더 배율입니다.
        ///
        /// <b>읽기만 합니다.</b> 파이프라인 에셋은 스크립터블 오브젝트라,
        /// 런타임에 고치면 에디터에서 그 값이 에셋 파일에 그대로 박힙니다.
        /// </summary>
        /// <returns>렌더 배율입니다. 읽지 못하면 인스펙터의 대체값입니다.</returns>
        private float ResolveRenderScale()
        {
            var asset = UniversalRenderPipeline.asset;

            return asset != null ? asset.renderScale : _fallbackRenderScale;
        }
    }
}
