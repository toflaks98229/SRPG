using SRPG.Data;
using SRPG.Rendering;
using UnityEngine;

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
    /// <b>두 걸음으로 풉니다</b>
    ///
    ///   1. 카메라를 텍셀 격자에 <b>붙입니다</b>. 그러면 월드의 한 점이 언제나 같은 텍셀에 떨어져,
    ///      저해상도 그림 자체가 프레임 사이에 흔들리지 않습니다.
    ///   2. 붙이느라 버린 <b>나머지를 화면에서 되돌립니다</b>. 확대 패스가 UV 를 그만큼 밀어
    ///      그림은 격자에 붙어 있는데 화면은 매끄럽게 흐릅니다.
    ///
    /// 둘 중 하나만 하면 반쪽입니다. 1만 하면 움직임이 한 픽셀씩 뚝뚝 끊기고,
    /// 2만 하면 그림이 격자에 붙지 않아 애초에 기어다니는 것을 막지 못합니다.
    ///
    /// <b>예전에는 투영 행렬만 밀었습니다</b>
    ///
    /// 그 방식은 카메라를 격자에 붙이지 않고 <b>어긋난 만큼 화면을 미는 것만</b> 했습니다.
    /// 회전할 때 떨리던 문제(카메라 자리를 회전하는 축으로 끊던 시절)는 그것으로 사라졌지만,
    /// 저해상도 그림이 매 프레임 다른 자리에서 표본되는 것은 그대로 남아 있었습니다.
    /// 지금은 두 걸음을 다 밟습니다. <b>투영은 건드리지 않습니다</b> —
    /// 함께 하면 어긋남이 두 번 걸려 오히려 흔들립니다.
    ///
    /// <b>직교라야 성립합니다</b>
    ///
    /// 텍셀 하나가 덮는 월드 길이가 화면 전체에서 같아야 격자를 붙잡을 수 있습니다.
    /// 원근에서는 깊이마다 달라 한 평면에서만 맞습니다. 전투 카메라를 직교로 옮긴 이유가 이것입니다.
    ///
    /// <b>회전 자체의 지글거림은 이것으로 없앨 수 없습니다</b>
    ///
    /// 카메라가 돌면 월드와 픽셀 격자의 대응 자체가 돌아갑니다.
    /// 그것은 어긋남이 아니라 회전이 정말로 일어나고 있다는 뜻입니다.
    /// 없애려면 회전을 <b>단계로 끊어야</b> 합니다(<c>BattleCameraRig</c> 의 야우 단계).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(100)]
    public sealed class PixelSnapCamera : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>확대 패스가 읽는 전역 보정값의 이름입니다.</summary>
        private static readonly int PixelPanOffsetId = Shader.PropertyToID("_PixelPanOffset");

        // ====================================================================================================
        // 2. Inspector
        // ====================================================================================================

        [SerializeField]
        [Tooltip("픽셀 격자를 붙잡을지 여부입니다. 끄면 저해상도에서 화면이 기어다닙니다.")]
        private bool _snap = true;

        [SerializeField]
        [Tooltip("픽셀 격자 설정입니다. 렌더러 피처의 PixelArtFeature 와 같은 에셋을 꽂아야 합니다.\n" +
                 "어긋나면 카메라는 A 격자에 맞춰 서는데 화면은 B 격자로 잘려, " +
                 "붙잡는 시늉만 하고 화면은 그대로 기어다닙니다.")]
        private PixelGridSettings _grid;

        // ====================================================================================================
        // 3. Fields
        // ====================================================================================================

        /// <summary>이 컴포넌트가 붙은 카메라입니다.</summary>
        private Camera _camera;

        /// <summary>격자 에셋이 연결되지 않았을 때 쓰는 코드 기본값입니다.</summary>
        private PixelGridSettings _fallbackGrid;

        // ====================================================================================================
        // 4. Properties
        // ====================================================================================================

        /// <summary>이번 프레임에 한 픽셀이 덮은 월드 길이입니다. 진단용입니다.</summary>
        public float WorldUnitsPerPixel { get; private set; }

        /// <summary>이번 프레임에 확대 패스로 넘긴 보정값입니다. 진단용입니다.</summary>
        public Vector2 PanOffset { get; private set; }

        // ====================================================================================================
        // 5. Unity Lifecycle
        // ====================================================================================================

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        /// <summary>
        /// 보정값을 켜 둔 채로 남겨 두지 않습니다.
        ///
        /// <c>_PixelPanOffset</c> 은 전역이라 이 컴포넌트를 꺼도 마지막 값이 그대로 남습니다.
        /// 화면이 반 픽셀 밀린 채 굳어 있으면 원인을 찾기 어렵습니다.
        /// </summary>
        private void OnDisable()
        {
            Shader.SetGlobalVector(PixelPanOffsetId, Vector4.zero);
            PanOffset = Vector2.zero;
        }

        /// <summary>
        /// 카메라 리그가 자리를 잡은 <b>뒤에</b> 격자에 붙입니다.
        ///
        /// 실행 순서를 뒤로 미뤄 둔 것이 그 때문입니다. 리그보다 먼저 돌면
        /// 리그가 자리를 다시 잡아 붙여 둔 것이 곧바로 풀립니다.
        /// </summary>
        private void LateUpdate()
        {
            if (_camera == null)
            {
                return;
            }

            if (!_snap)
            {
                Publish(Vector2.zero);
                return;
            }

            float extent = PixelGrid.ResolveViewExtent(_camera);
            int internalHeight = ResolveGrid().ResolveHeight(Screen.height, extent);
            float texel = PixelGrid.TexelSize(extent, internalHeight);

            WorldUnitsPerPixel = texel;

            if (texel <= 0f)
            {
                Publish(Vector2.zero);
                return;
            }

            SnapToGrid(texel, extent);
        }

        // ====================================================================================================
        // 6. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 카메라를 텍셀 격자에 붙이고, 버린 나머지를 확대 패스에 넘깁니다.
        ///
        /// <b>카메라 자신의 가로·세로축으로 잽니다.</b> 격자는 화면의 것이므로
        /// 월드 축으로 재면 카메라가 돌아간 만큼 격자가 비스듬해집니다.
        ///
        /// <b>부호</b> — 카메라를 나머지만큼 <b>뒤로</b> 옮겼으므로, 그려진 그림에서는 월드가
        /// 그만큼 <b>앞으로</b> 밀려 보입니다. 표본 UV 를 같은 부호로 밀면 그림 내용이 반대로 움직여
        /// 원래 있어야 할 자리로 돌아옵니다. 그래서 보정값은 나머지와 <b>같은 부호</b>입니다.
        /// </summary>
        /// <param name="texel">픽셀 하나가 덮는 월드 길이입니다.</param>
        /// <param name="extent">화면에 담기는 월드 높이의 절반입니다.</param>
        private void SnapToGrid(float texel, float extent)
        {
            var cameraTransform = _camera.transform;

            Vector3 right = cameraTransform.right;
            Vector3 up = cameraTransform.up;
            Vector3 position = cameraTransform.position;

            float offsetRight = PixelGrid.SubTexelOffset(Vector3.Dot(position, right), texel);
            float offsetUp = PixelGrid.SubTexelOffset(Vector3.Dot(position, up), texel);

            cameraTransform.position = position - right * offsetRight - up * offsetUp;

            float visibleHeight = extent * 2f;
            float visibleWidth = Mathf.Max(0.0001f, visibleHeight * _camera.aspect);

            Publish(new Vector2(offsetRight / visibleWidth, offsetUp / visibleHeight));
        }

        /// <summary>
        /// 보정값을 전역으로 넘깁니다.
        ///
        /// <b>전역인 이유</b>는 이 값을 읽는 쪽이 후처리 머티리얼이기 때문입니다.
        /// 렌더러 피처가 그 머티리얼을 소유하지만, 값을 아는 것은 카메라입니다.
        /// 피처를 거쳐 넘기면 카메라 → 피처 → 머티리얼로 배선이 한 단 늘어나고,
        /// 그 사이 어느 한 곳이 <b>0을 덮어쓰면</b> 조용히 반쪽이 됩니다. 실제로 한 번 그랬습니다.
        /// </summary>
        /// <param name="offset">확대할 때 UV 를 밀 양입니다.</param>
        private void Publish(Vector2 offset)
        {
            PanOffset = offset;

            Shader.SetGlobalVector(PixelPanOffsetId, new Vector4(offset.x, offset.y, 0f, 0f));
        }

        /// <summary>
        /// 쓸 격자 설정을 정합니다. 연결되지 않았으면 코드 기본값을 만들어 들고 있습니다.
        /// </summary>
        /// <returns>격자 설정입니다. 절대 null 이 아닙니다.</returns>
        private PixelGridSettings ResolveGrid()
        {
            if (_grid != null)
            {
                return _grid;
            }

            if (_fallbackGrid == null)
            {
                _fallbackGrid = PixelGridSettings.CreateDefault();
                _fallbackGrid.hideFlags = HideFlags.HideAndDontSave;
            }

            return _fallbackGrid;
        }
    }
}
