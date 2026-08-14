using SRPG.Systems.Grid;
using UnityEngine;

namespace SRPG.Gameplay.Visual
{
    /// <summary>
    /// 유닛 발밑에 접지 그림자를 깔고 지면에 붙여 둡니다.
    ///
    /// <b>왜 필요한가</b>
    ///
    /// 빌보드는 평면이라 지면과 닿는 면이 없습니다.
    /// 방향광 그림자만으로는 유닛이 어디에 서 있는지 읽히지 않고,
    /// 카메라를 돌릴 때 스프라이트가 배경 위에 떠 있는 것처럼 보입니다.
    ///
    /// 발밑에 어두운 타원 하나면 그 문제가 사라집니다.
    /// 나중에 복셀 기반 주변 차폐로 넘어가더라도 이 자리는 그대로 씁니다.
    ///
    /// <b>지면 높이를 따로 재는 이유</b>
    ///
    /// 유닛의 발 위치를 그대로 쓰면 계단 경계에서 그림자가 벽면에 파고듭니다.
    /// 그림자는 <b>지금 서 있는 타일의 표면</b>에 놓여야 하고, 그 값은 격자가 알고 있습니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ContactShadow : MonoBehaviour
    {
        // ====================================================================================================
        // 1. Constants
        // ====================================================================================================

        /// <summary>지면에서 살짝 띄우는 높이입니다. 같은 평면이면 z-파이팅이 납니다.</summary>
        private const float GroundOffset = 0.02f;

        // ====================================================================================================
        // 2. Fields
        // ====================================================================================================

        /// <summary>그림자를 붙일 지면 높이를 읽을 지형입니다.</summary>
        private IslandGrid _grid;
        /// <summary>그림자 자신의 트랜스폼입니다.</summary>
        private Transform _transform;
        /// <summary>그림자가 따라다닐 유닛의 트랜스폼입니다.</summary>
        private Transform _owner;

        // ====================================================================================================
        // 3. Unity Lifecycle
        // ====================================================================================================

        private void LateUpdate()
        {
            if (_owner == null)
            {
                // 주인이 사라졌습니다. 그림자만 남아 떠 있으면 안 됩니다.
                Destroy(gameObject);
                return;
            }

            SyncPosition();
        }

        // ====================================================================================================
        // 4. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 그림자를 주인의 발밑, 지면 위로 옮깁니다.
        ///
        /// <b>지면 높이를 따로 재는 이유</b>
        /// 유닛의 y를 그대로 쓰면 계단 경계에서 그림자가 벽면에 파고듭니다.
        /// 그림자는 지금 서 있는 타일의 표면에 놓여야 하고, 그 값은 격자가 알고 있습니다.
        /// </summary>
        public void SyncPosition()
        {
            if (_owner == null)
            {
                return;
            }

            Vector3 position = _owner.position;

            position.y = _grid != null
                ? _grid.SampleGroundHeight(position) + GroundOffset
                : position.y + GroundOffset;

            _transform.position = position;
        }

        /// <summary>
        /// 유닛 발밑에 접지 그림자를 만들어 붙입니다.
        ///
        /// <b>유닛의 자식으로 두지 않습니다.</b> 자식이면 유닛의 회전을 함께 받아
        /// 타원이 같이 돌고, 빌보드 회전과 겹쳐 이상하게 보입니다.
        /// </summary>
        /// <param name="owner">그림자를 드리울 유닛입니다.</param>
        /// <param name="grid">지형입니다. 지면 높이를 재는 데 씁니다.</param>
        /// <param name="radius">그림자의 반경입니다.</param>
        /// <param name="parent">그림자 오브젝트가 붙을 부모입니다.</param>
        /// <returns>만들어진 그림자입니다. 셰이더가 없으면 null입니다.</returns>
        public static ContactShadow Attach(Transform owner, IslandGrid grid, float radius, Transform parent)
        {
            if (owner == null)
            {
                return null;
            }

            var shader = Shader.Find(PrototypeVisuals.ContactShadowShaderName);
            if (shader == null)
            {
                return null;
            }

            var go = new GameObject($"Shadow_{owner.name}");
            go.transform.SetParent(parent, worldPositionStays: false);

            // 지면에 눕힌 쿼드입니다.
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);

            go.AddComponent<MeshFilter>().sharedMesh = PrototypeVisuals.GetCenteredQuadMesh();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = PrototypeVisuals.GetSharedContactShadowMaterial(shader);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var shadow = go.AddComponent<ContactShadow>();
            shadow._owner = owner;
            shadow._grid = grid;
            shadow._transform = go.transform;

            // 첫 프레임부터 제자리에 있어야 합니다. LateUpdate를 기다리면 원점에서 한 번 깜빡입니다.
            shadow.SyncPosition();

            return shadow;
        }
    }
}
