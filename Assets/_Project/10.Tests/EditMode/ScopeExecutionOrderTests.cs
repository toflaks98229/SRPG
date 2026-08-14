using System;
using System.Reflection;
using NUnit.Framework;
using SRPG.Composition;
using UnityEngine;
using VContainer.Unity;

namespace SRPG.Tests
{
    /// <summary>
    /// 스코프가 깨어나는 순서를 확인합니다.
    ///
    /// <b>왜 순서를 검사로 묶어 두는가</b>
    ///
    /// 아래층 스코프는 부모의 컨테이너에서 서비스를 꺼냅니다. 그러려면 위층이
    /// <b>먼저</b> 깨어나 컨테이너를 조립해 두어야 합니다. 순서가 뒤집히면
    /// 부모를 찾지 못한 채로 조립이 진행되고, 부모에게서 받았어야 할 것이 전부 빕니다.
    ///
    /// <b>그런데 그것이 조용히 일어납니다.</b> VContainer 는 부모를 못 찾아도 예외를 던지지 않고
    /// 홀로 조립합니다. 게임은 멀쩡히 돌아가고, 부모가 주었어야 할 기능만 사라집니다.
    /// 실제로 그렇게 전장 전체가 무음이 되었고, 증상이 오디오 배선 문제로 보여
    /// 여러 층을 헛짚었습니다.
    ///
    /// <b>함정은 표기가 상속된다는 점입니다.</b>
    /// <c>DefaultExecutionOrder</c> 는 상속되므로, 자기 표기가 없는 스코프는
    /// VContainer <c>LifetimeScope</c> 의 값을 그대로 물려받습니다.
    /// 그 값보다 큰 숫자를 위층에 적으면 <b>앞당기려던 표기가 뒤로 미룹니다.</b>
    /// 숫자만 보면 음수라 앞선 것처럼 읽혀서, 눈으로는 거의 잡히지 않습니다.
    /// </summary>
    public sealed class ScopeExecutionOrderTests
    {
        // ====================================================================================================
        // 1. Helpers
        // ====================================================================================================

        /// <summary>
        /// 순서 값이 담긴 필드입니다. <b>공개되어 있지 않아 리플렉션으로 꺼냅니다.</b>
        ///
        /// <c>DefaultExecutionOrder</c> 는 값을 읽는 통로를 열어 두지 않았습니다.
        /// 유니티가 내부에서만 쓰기 때문인데, 여기서는 그 값 자체가 검사 대상입니다.
        /// </summary>
        private static readonly FieldInfo OrderField = typeof(DefaultExecutionOrder).GetField(
            "m_Order",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>
        /// 이 타입이 실제로 적용받는 실행 순서를 읽습니다.
        ///
        /// <b>상속을 포함해서 읽습니다.</b> 유니티가 그렇게 해석하기 때문입니다 —
        /// 자기 표기가 없으면 조상의 표기가 그대로 쓰입니다. 여기서 <c>inherit: false</c> 로 읽으면
        /// 검사만 통과하고 실제 게임에서는 다른 순서로 도는 상태가 만들어집니다.
        /// </summary>
        /// <param name="type">읽을 타입입니다.</param>
        /// <returns>적용되는 실행 순서입니다. 표기가 없으면 0입니다.</returns>
        private static int OrderOf(Type type)
        {
            // 필드 이름이 바뀌면 여기서 멈춰야 합니다. 조용히 0을 돌려주면
            // 모든 비교가 0끼리의 비교가 되어 검사 전체가 아무것도 지키지 않게 됩니다.
            Assert.IsNotNull(OrderField, "DefaultExecutionOrder 의 순서 필드를 찾지 못했습니다.");

            var attribute = type.GetCustomAttribute<DefaultExecutionOrder>(inherit: true);

            return attribute != null ? (int)OrderField.GetValue(attribute) : 0;
        }

        // ====================================================================================================
        // 2. Tests
        // ====================================================================================================

        /// <summary>
        /// <b>위층이 아래층보다 먼저 깨어납니다.</b>
        ///
        /// 이 검사가 서 있는 이유가 곧 이 프로젝트가 겪은 고장입니다.
        /// 전투가 루트보다 먼저 깨어나 <c>RootLifetimeScope.Live</c> 가 null 인 상태로
        /// 부모를 찾았고, 부모 없이 조립되었습니다.
        /// </summary>
        [Test]
        public void 루트가_전투보다_먼저_깨어난다()
        {
            int root = OrderOf(typeof(RootLifetimeScope));
            int battle = OrderOf(typeof(BattleBootstrap));

            Assert.Less(
                root,
                battle,
                $"루트({root})가 전투({battle})보다 늦게 깨어납니다. " +
                "전투가 부모를 찾지 못한 채로 조립되어, 부모가 주었어야 할 것이 전부 빕니다.");
        }

        /// <summary>
        /// 캠페인도 전투보다 먼저 깨어납니다.
        ///
        /// 전투는 캠페인이 있으면 캠페인을 부모로 삼습니다. 캠페인이 늦으면
        /// 부대 명부와 주문서를 꺼내지 못한 채로 판이 시작됩니다.
        /// </summary>
        [Test]
        public void 캠페인이_전투보다_먼저_깨어난다()
        {
            int campaign = OrderOf(typeof(CampaignLifetimeScope));
            int battle = OrderOf(typeof(BattleBootstrap));

            Assert.Less(campaign, battle, "캠페인이 전투보다 늦게 깨어납니다.");
        }

        /// <summary>
        /// 루트가 캠페인보다 먼저 깨어납니다. 캠페인의 부모가 루트이기 때문입니다.
        /// </summary>
        [Test]
        public void 루트가_캠페인보다_먼저_깨어난다()
        {
            int root = OrderOf(typeof(RootLifetimeScope));
            int campaign = OrderOf(typeof(CampaignLifetimeScope));

            Assert.Less(root, campaign, "루트가 캠페인보다 늦게 깨어납니다.");
        }

        /// <summary>
        /// <b>위층은 VContainer 가 정한 기본 순서보다 앞서야 합니다.</b>
        ///
        /// 앞의 세 검사는 지금 있는 스코프끼리만 비교합니다. 그런데 진짜 기준선은
        /// <c>LifetimeScope</c> 자신의 표기입니다 — 새로 만드는 스코프가 표기를 생략하면
        /// 그 값을 물려받기 때문입니다.
        ///
        /// 여기를 지켜 두면 <b>아직 없는 스코프</b>에 대해서도 순서가 보장됩니다.
        /// 표기를 깜빡한 새 스코프가 조용히 위층보다 먼저 깨어나는 일이 이 검사로 막힙니다.
        /// </summary>
        [Test]
        public void 위층은_VContainer_기본_순서보다_앞선다()
        {
            int inherited = OrderOf(typeof(LifetimeScope));

            Assert.Less(
                OrderOf(typeof(RootLifetimeScope)),
                inherited,
                $"루트가 LifetimeScope 의 기본값({inherited})보다 늦습니다. " +
                "표기를 생략한 스코프가 루트보다 먼저 깨어나게 됩니다.");

            Assert.Less(
                OrderOf(typeof(CampaignLifetimeScope)),
                inherited,
                $"캠페인이 LifetimeScope 의 기본값({inherited})보다 늦습니다.");
        }

        /// <summary>
        /// 전투는 자기 표기를 두지 않습니다.
        ///
        /// 두어도 되지만 두지 않는 편이 낫습니다. 기준선이 한 곳에만 있어야
        /// 위층 값을 옮길 때 함께 옮겨야 할 곳을 빠뜨리지 않습니다.
        ///
        /// <b>물려받은 값이 정확히 얼마인지는 묻지 않습니다.</b>
        /// 유니티가 조상의 표기를 적용하는 규칙과 C# 리플렉션의 상속 규칙이
        /// 반드시 같다는 보장이 없어서, 같다고 못 박으면 실제 순서와 무관하게 깨질 수 있습니다.
        /// 지켜야 할 것은 값이 아니라 <b>위층이 먼저</b>이고, 그것은 앞의 검사들이 봅니다.
        /// </summary>
        [Test]
        public void 전투는_자기_실행_순서를_두지_않는다()
        {
            Assert.IsNull(
                typeof(BattleBootstrap).GetCustomAttribute<DefaultExecutionOrder>(inherit: false),
                "전투가 자기 실행 순서를 두었습니다. 기준선이 두 곳으로 갈라집니다.");
        }

        /// <summary>
        /// <b>위층은 0보다도 앞섭니다.</b>
        ///
        /// 앞의 검사는 물려받은 값(-5000)을 기준으로 봅니다. 그런데 유니티가 조상의 표기를
        /// 적용하지 않는다면 표기 없는 스코프의 순서는 0입니다.
        /// <b>어느 쪽이든 위층이 먼저여야 합니다.</b> 두 해석을 모두 덮어 두면
        /// 이 불변식이 유니티 버전의 해석 차이에 흔들리지 않습니다.
        /// </summary>
        [Test]
        public void 위층은_표기가_없는_스코프보다_앞선다()
        {
            Assert.Less(OrderOf(typeof(RootLifetimeScope)), 0, "루트가 표기 없는 스코프보다 늦습니다.");
            Assert.Less(OrderOf(typeof(CampaignLifetimeScope)), 0, "캠페인이 표기 없는 스코프보다 늦습니다.");
        }
    }
}
