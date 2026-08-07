using System.Collections.Generic;
using SRPG.Common;
using UnityEngine;

namespace SRPG.Gameplay.Weapons
{
    /// <summary>
    /// 화살을 재사용합니다.
    ///
    /// <b>왜 필요한가</b>
    ///
    /// 궁수는 사거리 안에 적이 있는 한 계속 쏩니다. 100명이 1.4초 간격이면 초당 70발이고,
    /// 화살마다 <c>Instantiate</c> 와 <c>Destroy</c> 가 한 번씩 일어납니다.
    /// 둘 다 관리 힙에 쓰레기를 남기므로, 전투가 격해질수록 GC가 정확히 <b>가장 바쁜 순간에</b> 돕니다.
    /// 초당 수십 번의 생성·파괴는 풀링이 가장 잘 듣는 형태입니다.
    ///
    /// <b>프리팹별로 나눠 담습니다</b>
    ///
    /// 병과마다 다른 화살 프리팹을 쓸 수 있으므로 프리팹을 키로 삼습니다.
    /// 키를 무시하고 한 덩어리로 묶으면 궁수가 석궁 볼트를 쏘게 됩니다.
    ///
    /// 풀에 없으면 만들고, 있으면 꺼내 씁니다. 사용량이 최고치에 도달한 뒤에는 더 만들지 않습니다.
    /// </summary>
    public sealed class ProjectilePool
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        private readonly Dictionary<GameObject, Stack<Arrow>> _idle =
            new Dictionary<GameObject, Stack<Arrow>>(4);

        private Transform _root;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>지금까지 만든 화살의 총 개수입니다. 풀이 안정되었는지 확인하는 용도입니다.</summary>
        public int CreatedCount { get; private set; }

        /// <summary>현재 대기 중인(쓰이지 않는) 화살 수입니다.</summary>
        public int IdleCount
        {
            get
            {
                int total = 0;
                foreach (var stack in _idle.Values)
                {
                    total += stack.Count;
                }

                return total;
            }
        }

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 대기 중인 화살을 담아 둘 부모를 지정합니다. 부트스트랩이 전투 루트 아래를 넘겨줍니다.
        ///
        /// 지정하지 않으면 풀이 씬 루트에 오브젝트를 하나 만듭니다. 동작에는 문제가 없지만,
        /// 전투가 끝나도 남아 하이라키에 쌓입니다. 전투와 수명을 같이하게 두는 편이 낫습니다.
        /// </summary>
        public void SetRoot(Transform root)
        {
            if (root == null || _root == root)
            {
                return;
            }

            // 이미 만들어 둔 화살이 있으면 새 부모로 옮깁니다.
            if (_root != null)
            {
                for (int i = _root.childCount - 1; i >= 0; i--)
                {
                    _root.GetChild(i).SetParent(root, worldPositionStays: false);
                }

                Object.Destroy(_root.gameObject);
            }

            _root = root;
        }

        /// <summary>
        /// 화살 하나를 꺼내 옵니다. 대기 중인 것이 없으면 새로 만듭니다.
        /// </summary>
        /// <param name="prefab">루트에 <see cref="Arrow"/>가 있는 프리팹입니다.</param>
        /// <param name="position">생성 위치입니다.</param>
        /// <param name="rotation">생성 회전입니다.</param>
        /// <returns>바로 <see cref="Arrow.Launch"/>를 부를 수 있는 화살입니다. 프리팹이 잘못되면 null입니다.</returns>
        public Arrow Rent(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null)
            {
                return null;
            }

            if (!_idle.TryGetValue(prefab, out var stack))
            {
                stack = new Stack<Arrow>(16);
                _idle[prefab] = stack;
            }

            Arrow arrow = null;

            // 씬 전환 등으로 사라진 항목이 남아 있을 수 있으므로 살아 있는 것이 나올 때까지 꺼냅니다.
            while (stack.Count > 0 && arrow == null)
            {
                arrow = stack.Pop();
            }

            if (arrow == null)
            {
                arrow = Create(prefab);
                if (arrow == null)
                {
                    return null;
                }
            }

            var arrowTransform = arrow.transform;
            arrowTransform.SetPositionAndRotation(position, rotation);
            arrow.gameObject.SetActive(true);

            return arrow;
        }

        /// <summary>
        /// 다 쓴 화살을 돌려받습니다. <see cref="Arrow"/>가 스스로 호출합니다.
        /// </summary>
        public void Return(Arrow arrow)
        {
            if (arrow == null)
            {
                return;
            }

            arrow.gameObject.SetActive(false);

            if (arrow.PrefabKey == null || !_idle.TryGetValue(arrow.PrefabKey, out var stack))
            {
                // 이 풀이 만든 화살이 아닙니다. 되돌릴 자리가 없으므로 그냥 없앱니다.
                Object.Destroy(arrow.gameObject);
                return;
            }

            arrow.transform.SetParent(EnsureRoot(), worldPositionStays: false);
            stack.Push(arrow);
        }

        // ====================================================================================================
        // 4. Private Methods
        // ====================================================================================================

        private Arrow Create(GameObject prefab)
        {
            var instance = Object.Instantiate(prefab, EnsureRoot());

            var arrow = instance.GetComponent<Arrow>();
            if (arrow == null)
            {
                Debug.LogError($"[ProjectilePool] 화살 프리팹 '{prefab.name}' 루트에 Arrow 컴포넌트가 없습니다.");
                Object.Destroy(instance);
                return null;
            }

            // 레이어는 인스턴스마다 한 번만 칠하면 됩니다. 재사용할 때는 이미 칠해져 있습니다.
            GameLayers.ApplyRecursively(instance, GameLayers.Projectile);

            arrow.BindPool(this, prefab);
            CreatedCount++;

            return arrow;
        }

        /// <summary>
        /// 대기 중인 화살을 담아 둘 부모입니다. 하이라키가 화살로 뒤덮이지 않게 한곳에 모읍니다.
        /// </summary>
        private Transform EnsureRoot()
        {
            if (_root == null)
            {
                _root = new GameObject("ArrowPool").transform;
            }

            return _root;
        }
    }
}
