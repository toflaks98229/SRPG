using System.Collections.Generic;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Battle;
using SRPG.Gameplay.Island;
using SRPG.Gameplay.Visual;
using UnityEngine;

namespace SRPG.Gameplay.Units
{
    /// <summary>
    /// 병사 하나를 만드는 일만 맡습니다. 분대와 전개기가 이것을 통해 병력을 세웁니다.
    ///
    /// <b>왜 조립 지점에서 떼어 냈는가</b>
    ///
    /// 이 일은 조립이 아닙니다. 조립 지점은 "무엇과 무엇을 잇는가"를 정하는 자리인데,
    /// 유닛 생성은 전투가 도는 내내 계속 불립니다 — 지원군이 올라올 때마다 다시 불립니다.
    /// 한 번 하는 일과 계속 하는 일을 같은 클래스에 두면, 그 클래스는
    /// 조립이 끝난 뒤에도 살아 있어야 하는 상태를 들게 됩니다.
    /// 실제로 그랬습니다 — 조립 지점이 머티리얼 캐시와 그림자 루트를 들고 있었습니다.
    ///
    /// <b>왜 컨테이너에 등록하지 않는가</b>
    ///
    /// 이것은 서비스가 아니라 <b>이번 판의 씬 구조에 매인 도구</b>입니다.
    /// 유닛이 붙을 부모와 그림자가 모일 부모, 그리고 눕힐 풀밭이 있어야 만들어지는데,
    /// 그 셋은 전장을 세운 뒤에야 존재합니다. 컨테이너에 올리려면 두 단계 초기화가 되고,
    /// 그러면 "아직 초기화되지 않은 팩토리"라는 상태가 생깁니다.
    /// 전장을 세운 쪽이 그 자리에서 만들어 넘기면 그런 상태가 아예 없습니다.
    /// </summary>
    public sealed class UnitFactory
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>병사가 볼 수 있는 것만 담긴 컨텍스트입니다. 만들어진 유닛에게 그대로 넘어갑니다.</summary>
        private readonly IUnitContext _context;

        /// <summary>만들어진 유닛이 붙을 부모입니다.</summary>
        private readonly Transform _unitRoot;

        /// <summary>접지 그림자가 모일 부모입니다.</summary>
        private readonly Transform _shadowRoot;

        /// <summary>유닛이 밟아 눕힐 풀밭입니다. 전장 없이 격자만 있는 경로에서는 비어 있습니다.</summary>
        private readonly GrassField _grass;

        /// <summary>
        /// 병과별 폴백 머티리얼입니다.
        ///
        /// 유닛마다 머티리얼을 새로 만들면 같은 병과끼리도 배칭이 깨집니다.
        /// 병과 수는 많아야 열 남짓이므로 캐시가 커질 걱정은 없습니다.
        /// </summary>
        private readonly Dictionary<UnitDefinition, Material> _materials = new Dictionary<UnitDefinition, Material>();

        // ====================================================================================================
        // 2. Constructor
        // ====================================================================================================

        /// <param name="context">병사가 볼 컨텍스트입니다.</param>
        /// <param name="unitRoot">만들어진 유닛이 붙을 부모입니다.</param>
        /// <param name="shadowRoot">접지 그림자가 모일 부모입니다.</param>
        /// <param name="grass">유닛이 눕힐 풀밭입니다. 없어도 됩니다.</param>
        public UnitFactory(IUnitContext context, Transform unitRoot, Transform shadowRoot, GrassField grass)
        {
            _context = context;
            _unitRoot = unitRoot;
            _shadowRoot = shadowRoot;
            _grass = grass;
        }

        // ====================================================================================================
        // 3. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 유닛 하나를 만듭니다.
        ///
        /// 정의에 프리팹이 있으면 그것을 찍고, 없으면 프리미티브로 임시 몸체를 만듭니다.
        /// 두 경로 모두 마지막에 <see cref="Unit.Initialize"/> 를 지나므로 이후 동작은 완전히 같습니다.
        /// </summary>
        /// <param name="definition">병과입니다.</param>
        /// <param name="team">진영입니다.</param>
        /// <param name="isCommander">지휘관인지 여부입니다.</param>
        /// <param name="position">설 자리입니다. 높이는 지면에 맞춰집니다.</param>
        /// <returns>만들어진 유닛입니다. 프리팹이 온전하지 않으면 null입니다.</returns>
        public Unit Create(UnitDefinition definition, Team team, bool isCommander, Vector3 position)
        {
            position.y = _context.Grid.SampleGroundHeight(position);

            Unit unit = definition.Prefab != null
                ? InstantiateFromPrefab(definition, position)
                : CreatePrimitive(definition, team, isCommander, position);

            if (unit == null)
            {
                return null;
            }

            unit.Initialize(definition, team, _context, isCommander);
            AttachGroundContact(unit, definition);

            return unit;
        }

        // ====================================================================================================
        // 4. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 유닛이 지면에 닿아 있다는 사실을 화면에 남깁니다.
        ///
        /// 접지 그림자와 눕는 풀은 같은 사실의 두 표현이라 한자리에서 함께 겁니다.
        ///
        /// <b>왜 그림자가 필요한가</b>
        /// 빌보드는 평면이라 지면과 닿는 면이 없습니다. 방향광 그림자만으로는
        /// 유닛이 어디에 서 있는지 읽히지 않고, 카메라를 돌리면 떠 보입니다.
        /// </summary>
        /// <param name="unit">방금 만든 유닛입니다.</param>
        /// <param name="definition">그 유닛의 병과입니다.</param>
        private void AttachGroundContact(Unit unit, UnitDefinition definition)
        {
            // 발자국보다 조금 넓어야 그림자로 읽힙니다. 딱 맞으면 유닛에 가려 안 보입니다.
            ContactShadow.Attach(unit.transform, _context.Grid, definition.Radius * 1.4f, _shadowRoot);

            _grass?.RegisterTrampler(unit.transform, definition.Radius * 2.2f);
        }

        /// <summary>프리팹으로 유닛을 만듭니다.</summary>
        /// <param name="definition">병과입니다.</param>
        /// <param name="position">설 자리입니다.</param>
        /// <returns>만들어진 유닛입니다. 프리팹 루트에 <see cref="Unit"/> 이 없으면 null입니다.</returns>
        private Unit InstantiateFromPrefab(UnitDefinition definition, Vector3 position)
        {
            var instance = Object.Instantiate(definition.Prefab, position, Quaternion.identity, _unitRoot);
            var unit = instance.GetComponent<Unit>();

            if (unit != null)
            {
                return unit;
            }

            Debug.LogError(
                $"[UnitFactory] 프리팹 '{definition.Prefab.name}' 루트에 Unit 컴포넌트가 없습니다. ({definition.name})");

            Object.Destroy(instance);
            return null;
        }

        /// <summary>프리팹 없이 프리미티브로 유닛을 만듭니다.</summary>
        /// <param name="definition">병과입니다.</param>
        /// <param name="team">진영입니다.</param>
        /// <param name="isCommander">지휘관인지 여부입니다.</param>
        /// <param name="position">설 자리입니다.</param>
        /// <returns>만들어진 유닛입니다.</returns>
        private Unit CreatePrimitive(UnitDefinition definition, Team team, bool isCommander, Vector3 position)
        {
            var visual = PrototypeVisuals.CreateUnitVisual(definition, team, isCommander, GetMaterial(definition));

            visual.transform.SetParent(_unitRoot, false);
            visual.transform.position = position;

            return visual.AddComponent<Unit>();
        }

        /// <summary>병과별 폴백 머티리얼을 얻습니다. 없으면 만들어 캐시합니다.</summary>
        /// <param name="definition">병과입니다.</param>
        /// <returns>그 병과가 공유하는 머티리얼입니다.</returns>
        private Material GetMaterial(UnitDefinition definition)
        {
            if (_materials.TryGetValue(definition, out var cached))
            {
                return cached;
            }

            var created = PrototypeVisuals.CreateMaterial(definition.DebugColor);
            _materials[definition] = created;

            return created;
        }
    }
}
