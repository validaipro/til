using System;
using System.Collections.Generic;

namespace WildCardPoker.Core.DiamondTurnOver
{
    /// <summary>
    /// The Diamond Turn-over board: two rows of five. Row 1 starts up, down, up, down, up; row 2 is the opposite,
    /// so every column shows exactly one card. Turning over a column flips both of its cards: the hidden one comes
    /// up and the visible one goes down. Turning the same column again flips it back.
    /// </summary>
    public sealed class DiamondBoard
    {
        public const int Columns = 5;
        public const int Rows = 2;

        private readonly Card[] _top = new Card[Columns];
        private readonly Card[] _bottom = new Card[Columns];
        private readonly bool[] _topUp = new bool[Columns];

        public bool IsSet { get; private set; }

        /// <summary>Lays out the board from the deck: row 1 left to right, then row 2.</summary>
        public void SetUp(Deck deck)
        {
            var top = new Card[Columns];
            var bottom = new Card[Columns];
            for (int c = 0; c < Columns; c++) top[c] = deck.Draw();
            for (int c = 0; c < Columns; c++) bottom[c] = deck.Draw();
            SetUp(top, bottom);
        }

        /// <summary>Lays out a known board (tests, replays).</summary>
        public void SetUp(IReadOnlyList<Card> top, IReadOnlyList<Card> bottom)
        {
            if (top.Count != Columns || bottom.Count != Columns) throw new ArgumentException("Each row needs exactly five cards.");
            for (int c = 0; c < Columns; c++)
            {
                _top[c] = top[c];
                _bottom[c] = bottom[c];
                _topUp[c] = c % 2 == 0; // row 1: up, down, up, down, up
            }
            IsSet = true;
        }

        /// <summary>Is the card in <paramref name="row"/> (1 or 2) of <paramref name="column"/> (0..4) face up?</summary>
        public bool IsFaceUp(int row, int column) => row == 1 ? _topUp[column] : !_topUp[column];

        public Card CardAt(int row, int column) => row == 1 ? _top[column] : _bottom[column];

        /// <summary>The face-up card of a column and the row it is in.</summary>
        public Card Showing(int column, out int row)
        {
            row = _topUp[column] ? 1 : 2;
            return CardAt(row, column);
        }

        /// <summary>Turns a column over. Returns the card that just came face up.</summary>
        public Card TurnOver(int column)
        {
            if (column < 0 || column >= Columns) throw new ArgumentOutOfRangeException(nameof(column));
            _topUp[column] = !_topUp[column];
            return Showing(column, out _);
        }

        /// <summary>Face-up cards of one row, left to right.</summary>
        public IReadOnlyList<Card> FaceUpInRow(int row)
        {
            var list = new List<Card>(Columns);
            for (int c = 0; c < Columns; c++)
                if (IsFaceUp(row, c)) list.Add(CardAt(row, c));
            return list;
        }

        /// <summary>Columns (0..4) whose card in <paramref name="row"/> is face up.</summary>
        public IReadOnlyList<int> FaceUpColumns(int row)
        {
            var list = new List<int>(Columns);
            for (int c = 0; c < Columns; c++)
                if (IsFaceUp(row, c)) list.Add(c);
            return list;
        }

        /// <summary>All five face-up cards (one per column).</summary>
        public IReadOnlyList<Card> AllFaceUp
        {
            get
            {
                var list = new List<Card>(Columns);
                for (int c = 0; c < Columns; c++) list.Add(Showing(c, out _));
                return list;
            }
        }

        /// <summary>The five face-down cards (unknown to players).</summary>
        public IReadOnlyList<Card> AllFaceDown
        {
            get
            {
                var list = new List<Card>(Columns);
                for (int c = 0; c < Columns; c++) list.Add(_topUp[c] ? _bottom[c] : _top[c]);
                return list;
            }
        }
    }

    /// <summary>
    /// Ranks the face-up ("rolled") cards on the table to find who has the highest hand showing.
    /// Only what everyone can see counts: natural pairs, trips and quads, then high cards. Aces are high.
    /// Wild cards are ignored here because a player's wild depends on their hidden hole cards.
    /// </summary>
    public static class ShowingHand
    {
        public static int Score(IReadOnlyList<Card> upCards)
        {
            if (upCards.Count == 0) return 0;
            var counts = new int[15];
            foreach (var c in upCards) counts[(int)c.Rank]++;

            var ranks = new List<int>();
            int top = 0, second = 0;
            for (int n = 4; n >= 1; n--)
                for (int r = 14; r >= 2; r--)
                    if (counts[r] == n)
                    {
                        ranks.Add(r);
                        if (top == 0) top = n;
                        else if (second == 0) second = n;
                    }

            HandCategory category;
            if (top == 4) category = HandCategory.FourOfAKind;
            else if (top == 3) category = HandCategory.ThreeOfAKind;
            else if (top == 2 && second == 2) category = HandCategory.TwoPair;
            else if (top == 2) category = HandCategory.OnePair;
            else category = HandCategory.HighCard;
            return HandValue.Pack(category, ranks.ToArray());
        }

        public static string Describe(IReadOnlyList<Card> upCards)
        {
            int score = Score(upCards);
            if (score == 0) return "nothing showing";
            var value = new HandValue(score, 0);
            switch (value.Category)
            {
                case HandCategory.FourOfAKind: return "four " + Card.RankName((Rank)value.TieBreak(0), true).ToLowerInvariant();
                case HandCategory.ThreeOfAKind: return "three " + Card.RankName((Rank)value.TieBreak(0), true).ToLowerInvariant();
                case HandCategory.TwoPair: return "two pair";
                case HandCategory.OnePair: return "a pair of " + Card.RankName((Rank)value.TieBreak(0), true).ToLowerInvariant();
                default: return Card.RankName((Rank)value.TieBreak(0)).ToLowerInvariant() + " high";
            }
        }
    }
}
