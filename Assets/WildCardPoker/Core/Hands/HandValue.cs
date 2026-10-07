using System;
using System.Collections.Generic;

namespace WildCardPoker.Core
{
    public enum HandCategory
    {
        HighCard = 0,
        OnePair = 1,
        TwoPair = 2,
        ThreeOfAKind = 3,
        Straight = 4,
        Flush = 5,
        FullHouse = 6,
        FourOfAKind = 7,
        StraightFlush = 8,
        FiveOfAKind = 9,
    }

    /// <summary>House rules that change how hands are ranked.</summary>
    public sealed class HandRules
    {
        /// <summary>Five of a kind (only possible with wilds) ranks above a straight flush.</summary>
        public bool AllowFiveOfAKind = true;

        /// <summary>When two hands are otherwise equal, the one using fewer wild cards wins.</summary>
        public bool NaturalBeatsWild = false;

        /// <summary>Aces may play low in A-2-3-4-5 ("the wheel"). Game 23: aces high or low, player's discretion.</summary>
        public bool AceLowStraights = true;

        public static HandRules Default => new HandRules();

        public int Compare(HandValue a, HandValue b)
        {
            int c = a.Strength.CompareTo(b.Strength);
            if (c != 0 || !NaturalBeatsWild) return c;
            return b.WildCount.CompareTo(a.WildCount);
        }
    }

    /// <summary>
    /// A comparable 5-card hand value. <see cref="Strength"/> packs the category and up to five
    /// tie-break ranks (4 bits each) so a plain integer comparison orders hands correctly.
    /// </summary>
    public readonly struct HandValue
    {
        public int Strength { get; }
        public int WildCount { get; }

        public HandValue(int strength, int wildCount)
        {
            Strength = strength;
            WildCount = wildCount;
        }

        public HandCategory Category => (HandCategory)(Strength >> 20);

        public bool IsValid => Strength > 0;

        /// <summary>Tie-break rank at position 0..4 (most significant first); 0 when unused.</summary>
        public int TieBreak(int index) => (Strength >> (16 - 4 * index)) & 0xF;

        public static int Pack(HandCategory category, params int[] ranks)
        {
            int value = (int)category << 20;
            for (int i = 0; i < ranks.Length && i < 5; i++) value |= ranks[i] << (16 - 4 * i);
            return value;
        }

        public string Describe()
        {
            if (!IsValid) return "No hand";
            var self = this;
            string R(int i, bool plural = false) => Card.RankName((Rank)self.TieBreak(i), plural);
            switch (Category)
            {
                case HandCategory.FiveOfAKind: return "Five " + R(0, true);
                case HandCategory.StraightFlush:
                    return TieBreak(0) == (int)Rank.Ace ? "Royal Flush" : "Straight Flush, " + R(0) + " high";
                case HandCategory.FourOfAKind: return "Four " + R(0, true);
                case HandCategory.FullHouse: return "Full House, " + R(0, true) + " full of " + R(1, true);
                case HandCategory.Flush: return "Flush, " + R(0) + " high";
                case HandCategory.Straight: return "Straight, " + R(0) + " high";
                case HandCategory.ThreeOfAKind: return "Three " + R(0, true);
                case HandCategory.TwoPair: return "Two Pair, " + R(0, true) + " and " + R(1, true);
                case HandCategory.OnePair: return "Pair of " + R(0, true);
                default: return R(0) + " high";
            }
        }

        public override string ToString() => Describe() + (WildCount > 0 ? $" ({WildCount} wild)" : "");
    }

    /// <summary>A fully explained hand: which real cards were used and what each one plays as.</summary>
    public sealed class EvaluatedHand
    {
        public HandValue Value { get; }
        public IReadOnlyList<Card> Cards { get; }
        public IReadOnlyList<Card> PlaysAs { get; }
        public IReadOnlyList<bool> IsWild { get; }

        /// <summary>Which part of the layout the hand came from, e.g. "Two side" in Game 23.</summary>
        public string Source { get; }

        public EvaluatedHand(HandValue value, IReadOnlyList<Card> cards, IReadOnlyList<Card> playsAs,
            IReadOnlyList<bool> isWild, string source)
        {
            Value = value;
            Cards = cards;
            PlaysAs = playsAs;
            IsWild = isWild;
            Source = source ?? "";
        }

        public string Describe() => Value.Describe();

        public override string ToString()
        {
            var parts = new List<string>();
            for (int i = 0; i < Cards.Count; i++)
                parts.Add(IsWild[i] ? Cards[i].ToPrettyString() + "→" + PlaysAs[i].ToPrettyString() : Cards[i].ToPrettyString());
            return Value + " [" + string.Join(" ", parts) + "]" + (Source.Length > 0 ? " from " + Source : "");
        }
    }
}
