using NUnit.Framework;
using SRPG.Common;
using SRPG.Data;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 피해 성질과 갑옷의 상성을 검증합니다.
    ///
    /// <b>왜 표의 모양이 아니라 관계를 검사하는가</b>
    ///
    /// 구체적인 숫자는 밸런스라 계속 바뀝니다. 여기서 0.7 같은 값을 못박으면
    /// 수치를 조정할 때마다 검사가 깨지고, 결국 검사 쪽을 따라 고치게 되어 아무것도 지키지 못합니다.
    ///
    /// 대신 <b>바뀌면 안 되는 관계</b>를 지킵니다 — 자돌은 중갑에 강하고 참격은 약하다,
    /// 같은 것들입니다. 이것이 무너지면 무기를 고를 이유 자체가 사라집니다.
    /// </summary>
    public sealed class ArmorEffectivenessTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        /// <summary>검사에 쓰는 튜닝입니다.</summary>
        private BattleTuning _tuning;

        [SetUp]
        public void SetUp()
        {
            _tuning = BattleTuning.CreateDefault();
        }

        [TearDown]
        public void TearDown()
        {
            if (_tuning != null)
            {
                Object.DestroyImmediate(_tuning);
            }
        }

        // ====================================================================================================
        // 2. 상성의 방향
        // ====================================================================================================

        [Test]
        public void 참격은_중갑에_약하고_경갑에_강하다()
        {
            float vsHeavy = _tuning.GetArmorEffectiveness(DamageType.Slash, ArmorType.Heavy);
            float vsLight = _tuning.GetArmorEffectiveness(DamageType.Slash, ArmorType.Light);

            Assert.Less(vsHeavy, 1f, "판금에서 칼날이 미끄러지지 않습니다.");
            Assert.Greater(vsLight, vsHeavy);
        }

        [Test]
        public void 자돌은_중갑에_강하다()
        {
            float pierce = _tuning.GetArmorEffectiveness(DamageType.Pierce, ArmorType.Heavy);
            float slash = _tuning.GetArmorEffectiveness(DamageType.Slash, ArmorType.Heavy);

            Assert.Greater(pierce, 1f, "자돌이 중갑을 파고들지 못하면 창을 들 이유가 없습니다.");
            Assert.Greater(pierce, slash, "중갑 상대로 자돌이 참격보다 낫지 않습니다.");
        }

        [Test]
        public void 타격은_중갑에_강하고_무갑에_약하다()
        {
            float vsHeavy = _tuning.GetArmorEffectiveness(DamageType.Blunt, ArmorType.Heavy);
            float vsNone = _tuning.GetArmorEffectiveness(DamageType.Blunt, ArmorType.Unarmored);

            Assert.Greater(vsHeavy, 1f);
            Assert.Less(vsNone, 1f, "둔기가 맨몸에도 잘 들면 다른 무기를 들 이유가 없습니다.");
        }

        [Test]
        public void 무갑은_어느_성질에도_크게_유리하지_않다()
        {
            // 맨몸이 특정 무기에 강하면 "갑옷을 벗는 것이 유리한" 상황이 생깁니다.
            foreach (DamageType type in System.Enum.GetValues(typeof(DamageType)))
            {
                Assert.LessOrEqual(
                    _tuning.GetArmorEffectiveness(type, ArmorType.Unarmored),
                    1.0f,
                    $"{type} 가 무갑에게 1배를 넘습니다. 갑옷을 벗는 것이 유리해집니다.");
            }
        }

        [Test]
        public void 모든_조합이_유효한_배율을_돌려준다()
        {
            foreach (DamageType type in System.Enum.GetValues(typeof(DamageType)))
            {
                foreach (ArmorType armor in System.Enum.GetValues(typeof(ArmorType)))
                {
                    float value = _tuning.GetArmorEffectiveness(type, armor);

                    Assert.Greater(value, 0f, $"{type} 대 {armor} 배율이 0 이하입니다.");
                    Assert.IsFalse(float.IsNaN(value));
                }
            }
        }

        // ====================================================================================================
        // 3. 병과 기본값
        // ====================================================================================================

        [Test]
        public void 병과마다_무기_성질이_다르게_붙는다()
        {
            var infantry = UnitDefinition.CreateDefault(UnitRole.Infantry);
            var archer = UnitDefinition.CreateDefault(UnitRole.Archer);
            var pike = UnitDefinition.CreateDefault(UnitRole.Pike);
            var militia = UnitDefinition.CreateDefault(UnitRole.Militia);

            try
            {
                Assert.AreEqual(DamageType.Slash, infantry.Damage);
                Assert.AreEqual(DamageType.Pierce, archer.Damage, "화살은 자돌이어야 합니다.");
                Assert.AreEqual(DamageType.Pierce, pike.Damage);
                Assert.AreEqual(DamageType.Blunt, militia.Damage);

                Assert.AreEqual(ArmorType.Heavy, infantry.Armor);
                Assert.AreEqual(ArmorType.Unarmored, militia.Armor);
            }
            finally
            {
                Object.DestroyImmediate(infantry);
                Object.DestroyImmediate(archer);
                Object.DestroyImmediate(pike);
                Object.DestroyImmediate(militia);
            }
        }

        [Test]
        public void 궁수가_중갑_보병을_상대로_창병과_같은_이점을_갖는다()
        {
            var archer = UnitDefinition.CreateDefault(UnitRole.Archer);
            var infantry = UnitDefinition.CreateDefault(UnitRole.Infantry);

            try
            {
                float effect = _tuning.GetArmorEffectiveness(archer.Damage, infantry.Armor);

                // 중갑 보병을 뚫는 길이 둘이라는 설계의 한쪽입니다.
                // 다만 방패가 정면 사격을 대부분 막으므로, 실제로 이 이점을 보려면 측면을 잡아야 합니다.
                Assert.Greater(effect, 1f, "자돌 계열이 중갑에 이점이 없으면 중갑 보병이 벽이 됩니다.");
                Assert.Greater(infantry.ProjectileResistance, 0f, "방패가 없으면 측면 기동의 의미가 사라집니다.");
            }
            finally
            {
                Object.DestroyImmediate(archer);
                Object.DestroyImmediate(infantry);
            }
        }

        // ====================================================================================================
        // 4. 타격 정보
        // ====================================================================================================

        [Test]
        public void 타격_정보가_성질을_싣는다()
        {
            var melee = DamageInfo.Melee(10f, Vector3.forward, 1f, 0.1f, null, DamageType.Blunt);
            var arrow = DamageInfo.Projectile(7f, Vector3.forward, 1f, 0.1f, 40f, null, DamageType.Pierce);

            Assert.AreEqual(DamageType.Blunt, melee.Type);
            Assert.AreEqual(DamageKind.Melee, melee.Kind);

            Assert.AreEqual(DamageType.Pierce, arrow.Type);
            Assert.AreEqual(DamageKind.Projectile, arrow.Kind, "경로와 성질은 별개의 축이어야 합니다.");
        }
    }
}
