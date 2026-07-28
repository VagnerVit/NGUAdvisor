using System;
using System.Collections.Generic;
using System.Linq;
using static NGUAdvisor.Main;

namespace NGUAdvisor.Managers
{
    public static class CardManager
    {
        private static readonly Character _character = Main.Character;
        private static readonly CardsController _cc = _character.cardsController;
        private static readonly IDictionary<cardBonus, float> _cardValues = new Dictionary<cardBonus, float>();
        public static readonly string[] sortList;
        public static readonly int[] costList = new int[]
            { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35 };

        public static readonly Dictionary<int, string> rarityList = new Dictionary<int, string>
        {
            { -1, "Don't trash" },
            { 0, "Crappy" },
            { 1, "Bad" },
            { 2, "Meh" },
            { 3, "Okay" },
            { 4, "Good" },
            { 5, "Great" },
            { 6, "Hot Damn" },
            { 7, "BIG CHONKER" }
        };

        private static List<Card> Cards => _character.cards.cards;

        private static List<Mana> Manas => _character.cards.manas;

        static CardManager()
        {
            try
            {
                foreach (cardBonus bonus in typeof(cardBonus).GetEnumValues())
                {
                    float bonusValue = _cc.generateCardEffect(bonus, 6, 1, 1, false);
                    _cardValues.Add(bonus, bonusValue);
                }

                var temp = new List<string>();
                string[] cardBonusTypes = typeof(cardBonus).GetEnumNames().Where(x => x != "none").ToArray();
                var cardSortOptions = new List<string> { "RARITY", "TIER", "COST", "PROTECTED", "CHANGE", "VALUE", "NORMALVALUE" };
                foreach (string sortOption in cardSortOptions)
                {
                    temp.Add(sortOption);
                    temp.Add($"{sortOption}-ASC");
                }
                foreach (string bonus in cardBonusTypes)
                {
                    temp.Add($"TYPE:{bonus}");
                    temp.Add($"TYPE-ASC:{bonus}");
                }
                sortList = temp.ToArray();
            }
            catch (Exception e)
            {
                LogDebug(e.Message);
                LogDebug(e.StackTrace);
            }
        }

        public static bool WouldBeTrashed(Card card, out string reason)
        {
            reason = "";
            if (card.bonusType == cardBonus.none)
                return false;
            if ((int)card.cardRarity <= Settings.CardRarities[(int)card.bonusType - 1])
            {
                reason = "Rarity";
                return true;
            }
            if (card.manaCosts.Sum() <= Settings.CardCosts[(int)card.bonusType - 1])
            {
                reason = "Cost";
                return true;
            }
            return false;
        }

        public static void CheckManas()
        {
            try
            {
                var needMayo = false;
                int[] target = new int[Manas.Count];
                foreach (var card in Cards)
                {
                    // Don't save Mayo for protected cards if the flaf is not set
                    if (card.isProtected && !Settings.CastProtectedCards)
                        continue;
                    for (int i = 0; i < Manas.Count; i++)
                    {
                        target[i] += card.manaCosts[i];
                        if (target[i] > Manas[i].amount)
                            needMayo = true;
                    }
                    if (needMayo)
                        break;
                }

                if (needMayo)
                {
                    for (int i = 0; i < Manas.Count && _cc.curManaToggleCount() > 0; i++)
                    {
                        if (target[i] <= Manas[i].amount && Manas[i].running)
                            _cc.toggleManaGen(i);
                    }
                    for (int i = 0; i < Manas.Count && _cc.curManaToggleCount() < _cc.maxManaGenSize(); i++)
                    {
                        if (target[i] > Manas[i].amount && !Manas[i].running)
                            _cc.toggleManaGen(i);
                    }
                }
                else
                {
                    var min = Manas.AllMinBy(x => x.amount + x.progress).First();
                    for (int i = 0; i < Manas.Count; i++)
                    {
                        var mana = Manas[i];
                        if (mana == min)
                            continue;
                        if (mana.running)
                            _cc.toggleManaGen(i);
                    }
                    if (!min.running)
                        _cc.toggleManaGen(Manas.IndexOf(min));
                }
            }
            catch (Exception e)
            {
                LogDebug(e.Message);
                LogDebug(e.StackTrace);
            }
        }

        public static void TrashCard(int index)
        {
            Cards[index].isProtected = false;
            _cc.trashCard(index);
        }

        public static void TrashCards()
        {
            try
            {
                if (!Settings.TrashCards)
                    return;

                var hasEndPiece = _character.inventory.inventory.Exists(c => c.id == 492);
                var hasEndCard = false;

                for (int index = Cards.Count - 1; index >= 0; index--)
                {
                    Card card = Cards[index];

                    // Don't trash protected cards unless the trash protected flag is set
                    if (card.isProtected && !Settings.TrashProtectedCards)
                        continue;

                    if (card.type == cardType.end)
                    {
                        if (hasEndPiece)
                        {
                            LogCard($"Trashed Card: Bonus Type: END, due to already having the END piece");
                            TrashCard(index);
                        }
                        else if (hasEndCard)
                        {
                            LogCard($"Trashed Card: Bonus Type: END, due to already having one in the cards list");
                            TrashCard(index);
                        }
                        else
                        {
                            hasEndCard = true;
                        }
                    }
                    else if (WouldBeTrashed(card, out var reason))
                    {
                        LogCard($"Trashed Card: Cost: {card.manaCosts.Sum()}, Rarity: {card.cardRarity}, Bonus Type: {card.bonusType}, due to {reason} settings");
                        TrashCard(index);
                    }
                }
            }
            catch (Exception e)
            {
                LogDebug(e.Message);
                LogDebug(e.StackTrace);
            }
        }

        public static void CastCards()
        {
            if (Settings.TrashCards)
                TrashCards(); // Make sure all cards in inventory are ones that should be cast

            var index = 0;
            var count = Cards.Count;
            var reservedMayo = new bool[6] { false, false, false, false, false, false };
            while (index < count)
            {
                Card card = Cards[index];
                // Don't cast protected cards if the flag is not set
                if (card.isProtected && !Settings.CastProtectedCards)
                {
                    index++;
                    continue;
                }
                var enoughMayo = true;
                for (int i = 0; i < card.manaCosts.Count; i++)
                {
                    if (Manas[i].amount > 0 && reservedMayo[i])
                    {
                        enoughMayo = false;
                        break;
                    }
                    if (Manas[i].amount < card.manaCosts[i])
                    {
                        reservedMayo[i] = true;
                        enoughMayo = false;
                        break;
                    }
                }
                if (!enoughMayo)
                {
                    index++;
                    continue;
                }
                LogCard($"Cast Card: Cost: {card.manaCosts.Sum()}, Rarity: {card.cardRarity}, Bonus Type: {card.bonusType}");
                card.isProtected = false;
                _cc.tryConsumeCard(index);
                count--;
            }
        }

        public static void SortCards()
        {
            try
            {
                Cards.Sort(CompareCards);
                for (var i = 0; i < Cards.Count; i++)
                    _cc.updateDeckCard(i);
            }
            catch (Exception e)
            {
                LogDebug(e.Message);
                LogDebug(e.StackTrace);
            }
        }

        private static float GetCardChange(Card card) => (_cc.getBonus(card.bonusType) + card.effectAmount) / _cc.getBonus(card.bonusType);

        private static float GetCardValue(Card card) => (GetCardChange(card) - 1) / card.manaCosts.Sum();

        private static float GetCardNormalValue(Card card) => GetCardValue(card) / _cardValues[card.bonusType];

        private static int CompareByPriority(string priority, Card c1, Card c2)
        {
            string[] temp = priority.Split(':');
            string bonusType = temp.Length > 1 ? temp[1] : "";
            temp[0] = temp[0].ToUpper();
            bool sortAsc = temp[0].EndsWith("-ASC");
            if (sortAsc)
                temp[0] = temp[0].Substring(0, temp[0].Length - 4);

            priority = temp[0];

            if (priority == "RARITY")
                return DirectionalCompareTo(c1.cardRarity, c2.cardRarity, sortAsc);

            if (priority == "TIER")
                return DirectionalCompareTo(c1.tier, c2.tier, sortAsc);

            if (priority == "COST")
                return DirectionalCompareTo(c1.manaCosts.Sum(), c2.manaCosts.Sum(), sortAsc);

            if (priority == "TYPE")
            {
                if (string.IsNullOrWhiteSpace(bonusType))
                    return 0;

                if (c1.bonusType.ToString() == c2.bonusType.ToString())
                    return 0;
                else if (c1.bonusType.ToString() == bonusType)
                    return sortAsc ? 1 : -1;
                else if (c2.bonusType.ToString() == bonusType)
                    return sortAsc ? -1 : 1;
                else
                    return 0;
            }

            if (priority == "PROTECTED")
                return DirectionalCompareTo(c1.isProtected, c2.isProtected, sortAsc);

            if (priority == "CHANGE")
                return DirectionalCompareTo(GetCardChange(c1), GetCardChange(c2), sortAsc);

            if (priority == "VALUE")
                return DirectionalCompareTo(GetCardValue(c1), GetCardValue(c2), sortAsc);

            if (priority == "NORMALVALUE")
                return DirectionalCompareTo(GetCardNormalValue(c1), GetCardNormalValue(c2), sortAsc);

            return 0;
        }

        private static int DirectionalCompareTo<T>(T value1, T value2, bool sortAsc) where T : IComparable
        {
            if (sortAsc)
                return value1.CompareTo(value2);
            else
                return value2.CompareTo(value1);
        }

        private static int CompareCards(Card c1, Card c2)
        {
            foreach (string priority in Settings.CardSortOrder)
            {
                int index = CompareByPriority(priority, c1, c2);

                if (index != 0)
                    return index;
            }

            return c1.cardName.CompareTo(c2.cardName);
        }
    }
}