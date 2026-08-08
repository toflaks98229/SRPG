using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using SRPG.Gameplay.Units;
using SRPG.Gameplay.Weapons;
using UnityEditor;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 빌더가 구운 <b>실제 에셋</b>의 정합성을 검증합니다.
    ///
    /// 이 파일이 존재하는 이유는 실제로 놓친 버그 때문입니다.
    ///
    /// 무기 필드를 추가했을 때, 기존 <c>UnitDef_Archer</c> 에셋에는 그 필드가 없어
    /// <c>Style</c>이 C# 기본값 <c>MeleeSwing</c>으로 로드되었습니다.
    /// 궁수가 활을 들고도 화살을 한 발도 쏘지 않는 상태였는데,
    /// <b>테스트는 전부 통과했습니다.</b> PlayMode 테스트가 에셋이 아니라
    /// 코드 기본값(폴백) 경로를 타고 있었기 때문입니다.
    ///
    /// 폴백이 있는 설계에서는 "폴백이 잘 도는지"와 "에셋이 제대로 물렸는지"를 따로 검증해야 합니다.
    /// 여기서는 후자만 봅니다.
    ///
    /// 에셋이 아직 생성되지 않은 환경(클론 직후)에서는 테스트를 건너뜁니다.
    /// 에셋 없이도 프로젝트가 돌아가는 것이 설계 의도이므로, 없다고 실패시키면 안 됩니다.
    /// </summary>
    public sealed class BuiltAssetIntegrityTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        private const string BattleSetupPath = "Assets/_Project/03.DataAssets/Configs/BattleSetup_Prototype.asset";

        /// <summary>
        /// 전투 구성 에셋을 불러옵니다. 없으면 테스트를 건너뜁니다.
        /// </summary>
        private static BattleSetup LoadSetupOrIgnore()
        {
            var setup = AssetDatabase.LoadAssetAtPath<BattleSetup>(BattleSetupPath);

            if (setup == null)
            {
                Assert.Ignore(
                    $"{BattleSetupPath} 이 없어 건너뜁니다.\n" +
                    "메뉴 'SRPG → 프로토타입 에셋 생성'을 실행하면 만들어집니다.");
            }

            return setup;
        }

        // ====================================================================================================
        // 2. Setup Wiring
        // ====================================================================================================

        [Test]
        public void 전투_구성_에셋의_참조가_모두_연결되어_있다()
        {
            var setup = LoadSetupOrIgnore();

            Assert.IsTrue(setup.TerrainMaterials.IsComplete, "지형 머티리얼 5종 중 빠진 것이 있습니다.");
            Assert.IsNotNull(setup.SelectionMarkerPrefab, "선택 마커가 연결되지 않았습니다.");
            Assert.IsNotNull(setup.OrderMarkerPrefab, "명령 마커가 연결되지 않았습니다.");

            Assert.IsNotNull(setup.PlayerRoster, "아군 병과 목록이 없습니다.");
            Assert.Greater(setup.PlayerRoster.Length, 0, "아군 병과 목록이 비어 있습니다.");

            Assert.IsNotNull(setup.EnemyRoster, "적 병과 목록이 없습니다.");
            Assert.Greater(setup.EnemyRoster.Length, 0, "적 병과 목록이 비어 있습니다.");
        }


        // ====================================================================================================
        // 3. Definition Integrity
        // ====================================================================================================

        [Test]
        public void 모든_병과_정의의_스키마가_최신이다()
        {
            var setup = LoadSetupOrIgnore();

            foreach (var definition in AllDefinitions(setup))
            {
                Assert.GreaterOrEqual(
                    definition.SchemaVersion,
                    UnitDefinition.CurrentSchemaVersion,
                    $"{definition.name} 의 스키마가 v{definition.SchemaVersion} 입니다. " +
                    $"새로 추가된 필드가 기본값으로 남아 있어 병과 동작이 틀어집니다.");
            }
        }

        [Test]
        public void 정의의_공격_방식과_프리팹의_무기가_일치한다()
        {
            // 이것이 실제로 놓쳤던 버그를 잡는 검사입니다.
            // 값 하나만 보는 대신 두 곳이 서로 어긋나지 않는지를 봅니다.
            var setup = LoadSetupOrIgnore();

            foreach (var definition in AllDefinitions(setup))
            {
                Assert.IsNotNull(definition.Prefab, $"{definition.name} 에 프리팹이 없습니다.");

                var weapon = definition.Prefab.GetComponent<WeaponBase>();
                Assert.IsNotNull(weapon, $"{definition.name} 의 프리팹에 무기 컴포넌트가 없습니다.");

                System.Type expected = definition.Style switch
                {
                    AttackStyle.MeleeThrust => typeof(PikeWeapon),
                    AttackStyle.Projectile => typeof(BowWeapon),
                    _ => typeof(MeleeWeapon),
                };

                Assert.AreEqual(
                    expected,
                    weapon.GetType(),
                    $"{definition.name}: Style={definition.Style} 인데 프리팹 무기는 {weapon.GetType().Name} 입니다.");
            }
        }

        [Test]
        public void 투사체_병과에는_화살_프리팹이_있다()
        {
            var setup = LoadSetupOrIgnore();

            foreach (var definition in AllDefinitions(setup))
            {
                if (definition.Style != AttackStyle.Projectile)
                {
                    continue;
                }

                Assert.IsNotNull(
                    definition.ProjectilePrefab,
                    $"{definition.name} 은 투사체 병과인데 화살 프리팹이 없습니다. 조준만 하고 아무것도 쏘지 않습니다.");

                Assert.IsNotNull(
                    definition.ProjectilePrefab.GetComponent<Arrow>(),
                    $"{definition.name} 의 화살 프리팹 루트에 Arrow 컴포넌트가 없습니다.");
            }
        }

        [Test]
        public void 궁수는_투사체_방식이고_창병은_찌르기_방식이다()
        {
            // 병과의 정체성이 데이터에 제대로 남아 있는지 확인합니다.
            var setup = LoadSetupOrIgnore();

            foreach (var definition in AllDefinitions(setup))
            {
                switch (definition.Role)
                {
                    case UnitRole.Archer:
                        Assert.AreEqual(AttackStyle.Projectile, definition.Style, $"{definition.name} 궁수가 투사체 방식이 아닙니다.");

                        // IsRanged 는 Style 에서 파생되므로 어긋날 수 없습니다.
                        // 예전에는 별도 필드라 둘이 따로 놀 수 있었고, 그것을 여기서 막고 있었습니다.
                        Assert.IsTrue(definition.IsRanged, $"{definition.name} 궁수가 원거리로 읽히지 않습니다.");
                        break;

                    case UnitRole.Pike:
                        Assert.AreEqual(AttackStyle.MeleeThrust, definition.Style, $"{definition.name} 창병이 찌르기 방식이 아닙니다.");
                        Assert.IsFalse(
                            definition.CanAttackWhileMoving,
                            $"{definition.name} 창병이 이동 중 공격 가능으로 설정되어 있습니다. 자리 선점 압박이 사라집니다.");
                        Assert.Greater(
                            definition.WeaponLength,
                            1.5f,
                            $"{definition.name} 창의 길이가 너무 짧습니다. 사거리 우위가 물리적으로 성립하지 않습니다.");
                        break;
                }
            }
        }

        // ====================================================================================================
        // 4. Prefab Integrity
        // ====================================================================================================

        [Test]
        public void 유닛_프리팹에_Unit_컴포넌트와_콜라이더가_있다()
        {
            var setup = LoadSetupOrIgnore();

            foreach (var definition in AllDefinitions(setup))
            {
                var prefab = definition.Prefab;

                Assert.IsNotNull(prefab.GetComponent<Unit>(), $"{prefab.name} 루트에 Unit 컴포넌트가 없습니다.");

                // 콜라이더가 없으면 이 유닛은 무기 판정에 잡히지 않아 아무에게도 맞지 않습니다.
                Assert.IsNotNull(
                    prefab.GetComponentInChildren<Collider>(),
                    $"{prefab.name} 에 콜라이더가 없습니다. 물리 판정에 걸리지 않아 무적이 됩니다.");
            }
        }

        [Test]
        public void 지휘관_깃발은_프리팹에서_꺼져_있다()
        {
            // 켜진 채로 저장되면 모든 병사가 깃발을 들고 나옵니다.
            var setup = LoadSetupOrIgnore();

            foreach (var definition in AllDefinitions(setup))
            {
                var flag = definition.Prefab.transform.Find("CommanderFlag");
                Assert.IsNotNull(flag, $"{definition.Prefab.name} 에 CommanderFlag 자식이 없습니다.");
                Assert.IsFalse(
                    flag.gameObject.activeSelf,
                    $"{definition.Prefab.name} 의 CommanderFlag 가 켜진 채 저장되었습니다.");
            }
        }

        // ====================================================================================================
        // 5. Private Methods
        // ====================================================================================================

        /// <summary>
        /// 아군과 적 병과 정의를 모두 순회합니다.
        /// </summary>
        private static System.Collections.Generic.IEnumerable<UnitDefinition> AllDefinitions(BattleSetup setup)
        {
            foreach (var definition in setup.PlayerRoster)
            {
                if (definition != null)
                {
                    yield return definition;
                }
            }

            foreach (var definition in setup.EnemyRoster)
            {
                if (definition != null)
                {
                    yield return definition;
                }
            }
        }
    }
}
