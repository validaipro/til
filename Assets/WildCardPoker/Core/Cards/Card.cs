using System;
using System.Collections.Generic;

namespace WildCardPoker.Core
{
    public enum Suit : byte
    {
        Clubs = 0,
        Diamonds = 1,
        Hearts = 2,
        Spades = 3,
    }

    public enum Rank : byte
    {
        Two = 2,
        Three = 3,
        Four = 4,
        Five = 5,
        Six = 6,
        Seven = 7,
        Eight = 8,
        Nine = 9,
        Ten = 10,
        Jack = 11,
        Queen = 12,
        King = 13,
        Ace = 14,
    }

    /// <summary>An immutable playing card. Each of the 52 cards has a unique <see cref="Id"/> (0..51).</summary>
    public readonly struct Card : IEquatable<Card>, IComparable<Card>
    {
        public Rank Rank { get; }
        public Suit Suit { get; }

        public Card(Rank rank, Suit suit)
        {
            if (rank < Rank.Two || rank > Rank.Ace) throw new ArgumentOutOfRangeException(nameof(rank));
            if (suit > Suit.Spades) throw new ArgumentOutOfRangeException(nameof(suit));
            Rank = rank;
            Suit = suit;
        }

        public int Id => ((int)Rank - 2) * 4 + (int)Suit;

        public bool IsRed => Suit == Suit.Hearts || Suit == Suit.Diamonds;

        public static Card FromId(int id)
        {
            if (id < 0 || id > 51) throw new ArgumentOutOfRangeException(nameof(id));
            return new Card((Rank)(id / 4 + 2), (Suit)(id % 4));
        }

        /// <summary>Parses short notation such as "As", "Td", "10h", "2c".</summary>
        public static Card Parse(string text)
        {
            if (!TryParse(text, out var card)) throw new FormatException($"Not a card: '{text}'");
            return card;
        }

        public static bool TryParse(string text, out Card card)
        {
            card = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            if (text.Length < 2) return false;
            var rankPart = text.Substring(0, text.Length - 1);
            var suitChar = char.ToLowerInvariant(text[text.Length - 1]);
            if (!TryParseRank(rankPart, out var rank)) return false;
            Suit suit;
            switch (suitChar)
            {
                case 'c': case '♣': suit = Suit.Clubs; break;
                case 'd': case '♦': suit = Suit.Diamonds; break;
                case 'h': case '♥': suit = Suit.Hearts; break;
                case 's': case '♠': suit = Suit.Spades; break;
                default: return false;
            }
            card = new Card(rank, suit);
            return true;
        }

        /// <summary>Parses a space-separated list of cards, e.g. "As Kd 3c".</summary>
        public static Card[] ParseMany(string text)
        {
            var parts = text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var cards = new Card[parts.Length];
            for (int i = 0; i < parts.Length; i++) cards[i] = Parse(parts[i]);
            return cards;
        }

        public static bool TryParseRank(string text, out Rank rank)
        {
            rank = default;
            switch (text.Trim().ToUpperInvariant())
            {
                case "2": rank = Rank.Two; return true;
                case "3": rank = Rank.Three; return true;
                case "4": rank = Rank.Four; return true;
                case "5": rank = Rank.Five; return true;
                case "6": rank = Rank.Six; return true;
                case "7": rank = Rank.Seven; return true;
                case "8": rank = Rank.Eight; return true;
                case "9": rank = Rank.Nine; return true;
                case "T": case "10": rank = Rank.Ten; return true;
                case "J": rank = Rank.Jack; return true;
                case "Q": rank = Rank.Queen; return true;
                case "K": rank = Rank.King; return true;
                case "A": rank = Rank.Ace; return true;
                default: return false;
            }
        }

        public static string RankSymbol(Rank rank)
        {
            switch (rank)
            {
                case Rank.Ten: return "10";
                case Rank.Jack: return "J";
                case Rank.Queen: return "Q";
                case Rank.King: return "K";
                case Rank.Ace: return "A";
                default: return ((int)rank).ToString();
            }
        }

        public static string SuitSymbol(Suit suit)
        {
            switch (suit)
            {
                case Suit.Clubs: return "♣";
                case Suit.Diamonds: return "♦";
                case Suit.Hearts: return "♥";
                default: return "♠";
            }
        }

        public static string RankName(Rank rank, bool plural = false)
        {
            string name;
            switch (rank)
            {
                case Rank.Two: return plural ? "Deuces" : "Deuce";
                case Rank.Three: return plural ? "Treys" : "Trey";
                case Rank.Four: name = "Four"; break;
                case Rank.Five: name = "Five"; break;
                case Rank.Six: return plural ? "Sixes" : "Six";
                case Rank.Seven: name = "Seven"; break;
                case Rank.Eight: name = "Eight"; break;
                case Rank.Nine: name = "Nine"; break;
                case Rank.Ten: name = "Ten"; break;
                case Rank.Jack: name = "Jack"; break;
                case Rank.Queen: name = "Queen"; break;
                case Rank.King: name = "King"; break;
                default: name = "Ace"; break;
            }
            return plural ? name + "s" : name;
        }

        public static string SuitName(Suit suit, bool plural = true)
        {
            var name = suit.ToString();
            return plural ? name : name.Substring(0, name.Length - 1);
        }

        /// <summary>Compact notation, e.g. "Ts", "2c".</summary>
        public override string ToString()
        {
            var r = Rank == Rank.Ten ? "T" : RankSymbol(Rank);
            return r + "cdhs"[(int)Suit];
        }

        /// <summary>Display notation, e.g. "10♥".</summary>
        public string ToPrettyString() => RankSymbol(Rank) + SuitSymbol(Suit);

        public bool Equals(Card other) => Rank == other.Rank && Suit == other.Suit;
        public override bool Equals(object obj) => obj is Card other && Equals(other);
        public override int GetHashCode() => Id;
        public int CompareTo(Card other) => Id.CompareTo(other.Id);
        public static bool operator ==(Card a, Card b) => a.Equals(b);
        public static bool operator !=(Card a, Card b) => !a.Equals(b);

        public static IEnumerable<Card> FullDeck()
        {
            for (int id = 0; id < 52; id++) yield return FromId(id);
        }
    }
}
