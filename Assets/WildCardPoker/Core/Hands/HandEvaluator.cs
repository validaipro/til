using System;
using System.Collections.Generic;

namespace WildCardPoker.Core
{
    /// <summary>
    /// Evaluates 5-card poker hands with any number of wild cards.
    ///
    /// A wild card may stand for any rank and suit, including a card already in the hand
    /// (the common home-game rule that allows five of a kind and "double-ace" flushes).
    /// For a given set of natural cards the best substitution only depends on the natural ranks
    /// and on whether all naturals share a suit, so results are memoized on exactly that key.
    /// Thread-safe.
    /// </summary>
    public sealed class HandEvaluator
    {
        private struct Solution
        {
            public int Strength;
            public int[] WildRanks; // what the wild cards play as, ascending
        }

        private readonly Dictionary<int, Solution> _memo = new Dictionary<int, Solution>();
        private readonly object _lock = new object();

        public HandRules Rules { get; }

        public HandEvaluator(HandRules rules = null)
        {
            Rules = rules ?? HandRules.Default;
        }

        /// <summary>Evaluates exactly five cards.</summary>
        public HandValue Evaluate(IReadOnlyList<Card> five, WildSet wilds)
        {
            var solution = Solve(five, wilds, out int wildCount, out _);
            return new HandValue(solution.Strength, wildCount);
        }

        /// <summary>Evaluates exactly five cards and explains what each wild card plays as.</summary>
        public EvaluatedHand Explain(IReadOnlyList<Card> five, WildSet wilds, string source = null)
        {
            var solution = Solve(five, wilds, out int wildCount, out int flushSuit);
            var value = new HandValue(solution.Strength, wildCount);
            var cards = new Card[5];
            var playsAs = new Card[5];
            var isWild = new bool[5];
            var taken = new HashSet<Card>();
            for (int i = 0; i < 5; i++)
            {
                cards[i] = five[i];
                isWild[i] = wilds.Contains(five[i]);
                if (!isWild[i]) { playsAs[i] = five[i]; taken.Add(five[i]); }
            }

            bool suited = value.Category == HandCategory.Flush || value.Category == HandCategory.StraightFlush;
            int w = 0;
            for (int i = 0; i < 5; i++)
            {
                if (!isWild[i]) continue;
                var rank = (Rank)solution.WildRanks[w++];
                Suit suit;
                if (suited) suit = (Suit)(flushSuit < 0 ? (int)Suit.Spades : flushSuit);
                else suit = PickUnusedSuit(rank, taken);
                playsAs[i] = new Card(rank, suit);
                taken.Add(playsAs[i]);
            }
            return new EvaluatedHand(value, cards, playsAs, isWild, source);
        }

        private static Suit PickUnusedSuit(Rank rank, HashSet<Card> taken)
        {
            for (int s = 3; s >= 0; s--)
            {
                var candidate = new Card(rank, (Suit)s);
                if (!taken.Contains(candidate)) return (Suit)s;
            }
            return Suit.Spades;
        }

        private Solution Solve(IReadOnlyList<Card> five, WildSet wilds, out int wildCount, out int flushSuit)
        {
            if (five.Count != 5) throw new ArgumentException("A poker hand has exactly five cards.", nameof(five));

            // Gather natural ranks (insertion-sorted ascending) and check for a common suit.
            Span5 naturals = default;
            int n = 0;
            flushSuit = -1;
            bool flushPossible = true;
            for (int i = 0; i < 5; i++)
            {
                var card = five[i];
                if (wilds.Contains(card)) continue;
                if (flushSuit < 0) flushSuit = (int)card.Suit;
                else if (flushSuit != (int)card.Suit) flushPossible = false;
                naturals.Insert(n++, (int)card.Rank);
            }
            wildCount = 5 - n;

            int key = n | (flushPossible ? 8 : 0);
            for (int i = 0; i < n; i++) key |= naturals[i] << (4 + 4 * i);

            lock (_lock)
            {
                if (_memo.TryGetValue(key, out var cached)) return cached;
            }

            var solution = SolveUncached(naturals, n, flushPossible);
            lock (_lock)
            {
                _memo[key] = solution;
            }
            return solution;
        }

        private Solution SolveUncached(Span5 naturals, int n, bool flushPossible)
        {
            int wilds = 5 - n;
            var ranks = new int[5];
            for (int i = 0; i < n; i++) ranks[i] = naturals[i];

            var best = new Solution { Strength = -1, WildRanks = new int[wilds] };
            var current = new int[wilds];
            Search(0, 2);
            if (best.Strength < 0) throw new InvalidOperationException("No legal hand could be formed.");
            return best;

            // Enumerate wild rank assignments as non-decreasing sequences (multisets).
            void Search(int index, int minRank)
            {
                if (index == wilds)
                {
                    for (int i = 0; i < wilds; i++) ranks[n + i] = current[i];
                    int score = Score(ranks, flushPossible, Rules);
                    if (score > best.Strength)
                    {
                        best.Strength = score;
                        Array.Copy(current, best.WildRanks, wilds);
                    }
                    return;
                }
                for (int r = minRank; r <= 14; r++)
                {
                    current[index] = r;
                    Search(index + 1, r);
                }
            }
        }

        /// <summary>Scores five concrete ranks. Returns -1 if the combination is illegal under the rules.</summary>
        public static int Score(int[] ranks, bool flush, HandRules rules)
        {
            var counts = new int[15];
            for (int i = 0; i < 5; i++) counts[ranks[i]]++;

            // Groups ordered by count desc, then rank desc.
            var groupRank = new int[5];
            var groupCount = new int[5];
            int groups = 0;
            for (int c = 5; c >= 1; c--)
                for (int r = 14; r >= 2; r--)
                    if (counts[r] == c) { groupRank[groups] = r; groupCount[groups] = c; groups++; }

            if (groupCount[0] == 5)
                return rules.AllowFiveOfAKind ? HandValue.Pack(HandCategory.FiveOfAKind, groupRank[0]) : -1;

            int straightHigh = 0;
            if (groups == 5)
            {
                if (groupRank[0] - groupRank[4] == 4) straightHigh = groupRank[0];
                else if (rules.AceLowStraights && groupRank[0] == 14 && groupRank[1] == 5) straightHigh = 5;
            }

            if (straightHigh > 0 && flush) return HandValue.Pack(HandCategory.StraightFlush, straightHigh);
            if (groupCount[0] == 4) return HandValue.Pack(HandCategory.FourOfAKind, groupRank[0], groupRank[1]);
            if (groupCount[0] == 3 && groupCount[1] == 2)
                return HandValue.Pack(HandCategory.FullHouse, groupRank[0], groupRank[1]);
            if (flush)
            {
                var sorted = (int[])ranks.Clone();
                Array.Sort(sorted);
                return HandValue.Pack(HandCategory.Flush, sorted[4], sorted[3], sorted[2], sorted[1], sorted[0]);
            }
            if (straightHigh > 0) return HandValue.Pack(HandCategory.Straight, straightHigh);
            if (groupCount[0] == 3)
                return HandValue.Pack(HandCategory.ThreeOfAKind, groupRank[0], groupRank[1], groupRank[2]);
            if (groupCount[0] == 2 && groupCount[1] == 2)
                return HandValue.Pack(HandCategory.TwoPair, groupRank[0], groupRank[1], groupRank[2]);
            if (groupCount[0] == 2)
                return HandValue.Pack(HandCategory.OnePair, groupRank[0], groupRank[1], groupRank[2], groupRank[3]);
            return HandValue.Pack(HandCategory.HighCard, groupRank[0], groupRank[1], groupRank[2], groupRank[3], groupRank[4]);
        }

        /// <summary>A tiny fixed-size sorted buffer that avoids allocations on the hot path.</summary>
        private struct Span5
        {
            private int _a, _b, _c, _d, _e;

            public int this[int i]
            {
                get
                {
                    switch (i)
                    {
                        case 0: return _a;
                        case 1: return _b;
                        case 2: return _c;
                        case 3: return _d;
                        default: return _e;
                    }
                }
                set
                {
                    switch (i)
                    {
                        case 0: _a = value; break;
                        case 1: _b = value; break;
                        case 2: _c = value; break;
                        case 3: _d = value; break;
                        default: _e = value; break;
                    }
                }
            }

            /// <summary>Inserts into the first <paramref name="count"/> slots keeping ascending order.</summary>
            public void Insert(int count, int value)
            {
                int i = count;
                while (i > 0 && this[i - 1] > value)
                {
                    this[i] = this[i - 1];
                    i--;
                }
                this[i] = value;
            }
        }
    }
}
