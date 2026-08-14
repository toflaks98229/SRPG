using System.Collections.Generic;
using UnityEngine;

namespace SRPG.Data
{
    /// <summary>
    /// 승급할 때 고를 수 있는 특전입니다.
    ///
    /// <b>여기에 장비는 없습니다</b>
    ///
    /// 특전은 <b>부대가 익힌 것</b>입니다 — 더 오래 버티고, 더 빨리 걷고, 더 정확히 쏩니다.
    /// 무기와 방패는 <b>사서 드는 것</b>이고 그쪽은 상점의 축입니다.
    /// 둘을 섞으면 승급이 곧 장비 보급이 되어, 상점에서 살 이유가 사라집니다.
    /// 성장의 축과 소비의 축은 서로를 대신할 수 없어야 각자 값어치를 갖습니다.
    ///
    /// 그래서 이 목록의 모든 항목은 <see cref="UnitModifiers"/> 의 배율로만 표현됩니다.
    /// 새 장비를 특전으로 넣고 싶어지면, 그것은 특전이 아니라 상점 품목입니다.
    /// </summary>
    public enum SquadPerkKind
    {
        /// <summary>특전이 없습니다. 배열의 빈자리를 뜻합니다.</summary>
        None = 0,

        /// <summary>단련 — 더 맞고도 버팁니다.</summary>
        Hardened,

        /// <summary>매서움 — 한 번의 타격이 무겁습니다.</summary>
        Deadly,

        /// <summary>속사 — 다음 공격이 빨리 나옵니다.</summary>
        Relentless,

        /// <summary>발빠름 — 자리를 먼저 잡습니다.</summary>
        Swift,

        /// <summary>명사수 — 겨눈 곳으로 갑니다.</summary>
        Marksman,

        /// <summary>완력 — 밀어내는 힘이 셉니다.</summary>
        Brawny,

        /// <summary>굳건함 — 날아오는 것에 강합니다.</summary>
        Bulwark,
    }

    /// <summary>
    /// 특전 하나의 내용입니다.
    /// </summary>
    public readonly struct SquadPerk
    {
        /// <summary>어떤 특전인지입니다.</summary>
        public readonly SquadPerkKind Kind;

        /// <summary>화면에 보일 이름입니다.</summary>
        public readonly string DisplayName;

        /// <summary>무엇이 달라지는지에 대한 한 줄입니다.</summary>
        public readonly string Description;

        /// <summary>이 특전이 거는 배율입니다.</summary>
        public readonly UnitModifiers Modifiers;

        /// <param name="kind">어떤 특전인지입니다.</param>
        /// <param name="displayName">화면에 보일 이름입니다.</param>
        /// <param name="description">무엇이 달라지는지입니다.</param>
        /// <param name="modifiers">이 특전이 거는 배율입니다.</param>
        public SquadPerk(SquadPerkKind kind, string displayName, string description, in UnitModifiers modifiers)
        {
            Kind = kind;
            DisplayName = displayName;
            Description = description;
            Modifiers = modifiers;
        }
    }

    /// <summary>
    /// 특전 목록과 그 조합 규칙입니다.
    ///
    /// <b>왜 에셋이 아닌가</b>
    ///
    /// 특전은 게임의 <b>규칙</b>이지 전장마다 바뀌는 값이 아닙니다.
    /// 에셋으로 두면 "연결하는 것을 잊어 승급해도 아무것도 고를 수 없는" 상태가 생기고,
    /// 그 상태는 오류가 아니라 <b>빈 목록</b>으로만 나타납니다.
    /// 코드에 두면 그런 상태가 아예 존재하지 않습니다 —
    /// 이 프로젝트가 <c>CampaignProgression</c> 을 에셋으로 만들지 않은 것과 같은 이유입니다.
    ///
    /// 수치를 만지고 싶으면 이 파일의 한곳만 고치면 되고, 그 변경은 컴파일러가 확인해 줍니다.
    /// </summary>
    public static class SquadPerks
    {
        // ====================================================================================================
        // 1. Catalogue
        // ====================================================================================================

        /// <summary>
        /// 고를 수 있는 특전 전부입니다.
        ///
        /// <b>배율은 한 축씩만 건드립니다.</b> 하나가 여러 축을 올리면 그 특전이 언제나 정답이 되고,
        /// 고르는 일이 곧 "가장 센 것 찾기"가 됩니다. 축이 하나면 <b>이 부대에 무엇이 부족한가</b>를
        /// 묻게 되고, 그것이 고르는 재미입니다.
        /// </summary>
        private static readonly SquadPerk[] Catalogue =
        {
            new SquadPerk(
                SquadPerkKind.Hardened, "단련",
                "체력 +18%",
                new UnitModifiers(1.18f, 1f, 1f, 1f, 1f, 1f, 1f)),

            new SquadPerk(
                SquadPerkKind.Deadly, "매서움",
                "피해 +15%",
                new UnitModifiers(1f, 1.15f, 1f, 1f, 1f, 1f, 1f)),

            new SquadPerk(
                SquadPerkKind.Relentless, "속사",
                "공격 속도 +12%",
                new UnitModifiers(1f, 1f, 1.12f, 1f, 1f, 1f, 1f)),

            new SquadPerk(
                SquadPerkKind.Swift, "발빠름",
                "이동 속도 +12%",
                new UnitModifiers(1f, 1f, 1f, 1.12f, 1f, 1f, 1f)),

            new SquadPerk(
                SquadPerkKind.Marksman, "명사수",
                "명중 +25% (산포가 줄어듭니다)",
                new UnitModifiers(1f, 1f, 1f, 1f, 1.25f, 1f, 1f)),

            new SquadPerk(
                SquadPerkKind.Brawny, "완력",
                "넉백 +20%",
                new UnitModifiers(1f, 1f, 1f, 1f, 1f, 1.2f, 1f)),

            new SquadPerk(
                SquadPerkKind.Bulwark, "굳건함",
                "투사체 저항 +20%",
                new UnitModifiers(1f, 1f, 1f, 1f, 1f, 1f, 1.2f)),
        };

        /// <summary>한 번 승급할 때 보여 주는 선택지 수입니다.</summary>
        public const int OfferSize = 3;

        // ====================================================================================================
        // 2. Public Methods
        // ====================================================================================================

        /// <summary>고를 수 있는 특전의 총수입니다.</summary>
        public static int Count => Catalogue.Length;

        /// <summary>지정한 특전의 내용을 꺼냅니다.</summary>
        /// <param name="kind">꺼낼 특전입니다.</param>
        /// <param name="perk">찾은 내용입니다. 없으면 기본값입니다.</param>
        /// <returns>목록에 있으면 true입니다.</returns>
        public static bool TryGet(SquadPerkKind kind, out SquadPerk perk)
        {
            for (int i = 0; i < Catalogue.Length; i++)
            {
                if (Catalogue[i].Kind == kind)
                {
                    perk = Catalogue[i];
                    return true;
                }
            }

            perk = default;
            return false;
        }

        /// <summary>
        /// 이미 가진 특전을 뺀 나머지에서 <see cref="OfferSize"/> 개를 골라 줍니다.
        ///
        /// <b>같은 승급은 언제나 같은 선택지를 냅니다.</b>
        /// 씨앗을 밖에서 받아 그것으로만 섞으므로, 화면을 다시 열거나 저장했다 불러와도
        /// 목록이 바뀌지 않습니다. 매번 달라지면 <b>마음에 들 때까지 다시 여는</b> 놀이가 되고,
        /// 고르는 일이 선택이 아니라 뽑기가 됩니다.
        ///
        /// 남은 것이 <see cref="OfferSize"/> 보다 적으면 있는 만큼만 냅니다.
        /// 전부 가졌으면 빈 목록입니다 — 부르는 쪽이 그때는 승급만 시키고 선택을 건너뜁니다.
        /// </summary>
        /// <param name="owned">이미 가진 특전입니다. null이어도 됩니다.</param>
        /// <param name="seed">이 승급의 씨앗입니다. 같은 값은 같은 목록을 냅니다.</param>
        /// <param name="offer">고를 수 있는 특전이 채워집니다. 호출 시 비워집니다.</param>
        /// <returns>채워진 선택지 수입니다.</returns>
        public static int BuildOffer(IReadOnlyList<SquadPerkKind> owned, int seed, List<SquadPerkKind> offer)
        {
            offer.Clear();

            var pool = new List<SquadPerkKind>(Catalogue.Length);

            for (int i = 0; i < Catalogue.Length; i++)
            {
                if (!Contains(owned, Catalogue[i].Kind))
                {
                    pool.Add(Catalogue[i].Kind);
                }
            }

            // 씨앗으로만 섞습니다. 유니티의 전역 난수를 쓰면 같은 승급이 매번 달라집니다.
            var random = new System.Random(seed);

            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);

                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            int take = Mathf.Min(OfferSize, pool.Count);

            for (int i = 0; i < take; i++)
            {
                offer.Add(pool[i]);
            }

            return offer.Count;
        }

        /// <summary>
        /// 가진 특전을 하나의 배율로 모읍니다.
        ///
        /// 곱해서 모으므로 <b>순서가 결과를 바꾸지 않습니다.</b>
        /// 목록이 비었으면 <see cref="UnitModifiers.Identity"/> 입니다 — 아무것도 바뀌지 않습니다.
        /// </summary>
        /// <param name="owned">가진 특전입니다. null이어도 됩니다.</param>
        /// <returns>합쳐진 배율입니다.</returns>
        public static UnitModifiers Combine(IReadOnlyList<SquadPerkKind> owned)
        {
            var combined = UnitModifiers.Identity;

            if (owned == null)
            {
                return combined;
            }

            for (int i = 0; i < owned.Count; i++)
            {
                if (TryGet(owned[i], out var perk))
                {
                    combined = combined.Combine(perk.Modifiers);
                }
            }

            return combined;
        }

        // ====================================================================================================
        // 3. Private Methods
        // ====================================================================================================

        private static bool Contains(IReadOnlyList<SquadPerkKind> owned, SquadPerkKind kind)
        {
            if (owned == null)
            {
                return false;
            }

            for (int i = 0; i < owned.Count; i++)
            {
                if (owned[i] == kind)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
