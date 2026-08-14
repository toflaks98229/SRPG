using System.Reflection;
using NUnit.Framework;
using SRPG.Data;
using UnityEditor;
using UnityEngine;

namespace SRPG.Tests
{
    /// <summary>
    /// 전투 튜닝이 영역별로 나뉜 뒤에도 값이 실제로 닿는지 검증합니다.
    ///
    /// <b>중첩이 만든 새 실패 방식</b>
    ///
    /// 평평하던 시절에는 묶음이 없으니 <c>null</c> 이 될 것도 없었습니다.
    /// 지금은 묶음이 참조형이라, 에셋이 그 키 없이 로드되면 <b>묶음 자체가 비어</b>
    /// 첫 접근에서 터집니다. 그것도 전투 도중에 터집니다.
    ///
    /// 유니티 직렬화가 알아서 채워 주긴 하지만, 그것은 <b>규약이지 보장이 아닙니다</b> —
    /// 여기서 한 번 확인해 두면 나중에 묶음을 추가할 때 같은 것을 다시 생각하지 않아도 됩니다.
    /// </summary>
    public sealed class BattleTuningSchemaTests
    {
        // ====================================================================================================
        // 1. Fixture
        // ====================================================================================================

        /// <summary>구워져 있는 튜닝 에셋의 경로입니다.</summary>
        private const string AssetPath = "Assets/_Project/03.DataAssets/Configs/BattleTuning_Default.asset";

        // ====================================================================================================
        // 2. 묶음이 비어 있지 않다
        // ====================================================================================================

        /// <summary>
        /// 코드로 만든 기본 튜닝의 묶음이 하나도 비어 있지 않습니다.
        ///
        /// 반사로 훑습니다. 묶음을 하나 더할 때마다 이 검사에 줄을 더하게 두면
        /// 더하는 것을 잊는 순간 검사가 조용히 통과합니다.
        /// </summary>
        [Test]
        public void 기본_튜닝의_묶음이_전부_채워져_있다()
        {
            var tuning = BattleTuning.CreateDefault();

            try
            {
                foreach (var property in GroupProperties())
                {
                    Assert.IsNotNull(
                        property.GetValue(tuning),
                        $"{property.Name} 묶음이 비어 있습니다. 첫 접근에서 전투가 멈춥니다.");
                }
            }
            finally
            {
                Object.DestroyImmediate(tuning);
            }
        }

        /// <summary>
        /// 구워져 있는 에셋도 마찬가지입니다.
        ///
        /// <b>이쪽이 진짜 위험한 경로입니다.</b> 코드로 만든 것은 필드 초기값이 돌지만
        /// 에셋은 YAML 을 거쳐 오기 때문에, 옛 판으로 구워진 파일이 새 키 없이 로드됩니다.
        /// </summary>
        [Test]
        public void 구워진_튜닝_에셋의_묶음이_전부_채워져_있다()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<BattleTuning>(AssetPath);

            if (tuning == null)
            {
                Assert.Ignore($"튜닝 에셋이 없습니다: {AssetPath}");
                return;
            }

            foreach (var property in GroupProperties())
            {
                Assert.IsNotNull(property.GetValue(tuning), $"{property.Name} 묶음이 비어 있습니다.");
            }
        }

        // ====================================================================================================
        // 3. 값이 닿는다
        // ====================================================================================================

        /// <summary>
        /// 성장 계산이 묶음의 값을 실제로 읽습니다.
        ///
        /// 중첩하면서 <c>EvaluateRank</c> 가 보던 필드의 자리가 바뀌었습니다.
        /// 배선이 끊겨도 컴파일은 통과하고, 증상은 "랭크를 올려도 안 세진다" 하나뿐입니다.
        /// </summary>
        [Test]
        public void 성장_계산이_묶음의_값을_읽는다()
        {
            var tuning = BattleTuning.CreateDefault();

            try
            {
                tuning.Growth.RankHealthGain = 0.5f;

                var high = tuning.EvaluateRank(Common.CombatConstants.MaxRank);
                var low = tuning.EvaluateRank(Common.CombatConstants.MinRank);

                Assert.Greater(high.Health, low.Health, "랭크를 올려도 체력이 오르지 않습니다.");
            }
            finally
            {
                Object.DestroyImmediate(tuning);
            }
        }

        /// <summary>
        /// 상성 질의가 묶음의 표를 실제로 읽습니다.
        /// </summary>
        [Test]
        public void 상성_질의가_묶음의_표를_읽는다()
        {
            var tuning = BattleTuning.CreateDefault();

            try
            {
                tuning.Matchup.SlashVsHeavy = 0.25f;

                Assert.AreEqual(
                    0.25f,
                    tuning.GetArmorEffectiveness(Common.DamageType.Slash, Common.ArmorType.Heavy),
                    0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(tuning);
            }
        }

        // ====================================================================================================
        // 4. 스키마 판
        // ====================================================================================================

        /// <summary>
        /// 코드로 만든 튜닝은 처음부터 최신 판입니다. 이관이 헛돌 이유가 없습니다.
        /// </summary>
        [Test]
        public void 코드로_만든_튜닝은_최신_판이다()
        {
            var tuning = BattleTuning.CreateDefault();

            try
            {
                Assert.AreEqual(BattleTuning.CurrentSchemaVersion, tuning.SchemaVersion);
                Assert.IsFalse(tuning.MigrateToCurrentSchema(), "이미 최신인데 이관이 돌았습니다.");
            }
            finally
            {
                Object.DestroyImmediate(tuning);
            }
        }

        /// <summary>
        /// 판을 모르는 에셋은 이관 대상이 됩니다.
        ///
        /// <b>판 번호를 손으로 0에 두는 이유</b>
        ///
        /// 실제로 이 상태가 되는 것은 <b>디스크의 옛 에셋</b>입니다 —
        /// 그 파일에는 <c>SchemaVersion</c> 키가 아예 없어 필드 초기값(0)으로 로드됩니다.
        ///
        /// 그것을 코드에서 그대로 재현할 수는 없습니다. 에디터는 <c>CreateInstance</c> 에서
        /// <c>Reset</c> 을 부르므로 갓 만든 인스턴스는 이미 최신 판입니다.
        /// 그래서 로드 결과만 흉내 내어 이관이 실제로 도는지를 봅니다.
        /// </summary>
        [Test]
        public void 판을_모르는_에셋은_이관_대상이다()
        {
            var tuning = ScriptableObject.CreateInstance<BattleTuning>();

            try
            {
                // 옛 에셋이 로드된 직후의 상태입니다.
                tuning.SchemaVersion = 0;

                Assert.IsTrue(tuning.MigrateToCurrentSchema(), "판을 모르는데 이관이 돌지 않습니다.");
                Assert.AreEqual(BattleTuning.CurrentSchemaVersion, tuning.SchemaVersion);
                Assert.IsFalse(tuning.MigrateToCurrentSchema(), "두 번 돌았습니다. 여러 번 실행해도 결과가 같아야 합니다.");
            }
            finally
            {
                Object.DestroyImmediate(tuning);
            }
        }

        // ====================================================================================================
        // 5. Helpers
        // ====================================================================================================

        /// <summary>
        /// 튜닝이 노출하는 묶음 속성을 전부 찾습니다.
        ///
        /// 중첩 타입 여부로 가릅니다 — 묶음은 전부 <see cref="BattleTuning"/> 안에 선언되어 있고,
        /// 그렇지 않은 속성(<c>SchemaVersion</c>, <c>name</c> 따위)은 묶음이 아닙니다.
        /// </summary>
        /// <returns>묶음 속성들입니다.</returns>
        private static PropertyInfo[] GroupProperties()
        {
            var properties = typeof(BattleTuning).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var groups = new System.Collections.Generic.List<PropertyInfo>();

            foreach (var property in properties)
            {
                if (property.PropertyType.DeclaringType == typeof(BattleTuning))
                {
                    groups.Add(property);
                }
            }

            Assert.Greater(groups.Count, 5, "묶음을 하나도 찾지 못했습니다. 검사가 아무것도 지키지 않습니다.");

            return groups.ToArray();
        }
    }
}
