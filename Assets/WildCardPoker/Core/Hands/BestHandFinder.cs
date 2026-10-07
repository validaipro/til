using System;
using System.Collections.Generic;

namespace WildCardPoker.Core
{
    /// <summary>Index combinations "n choose k", cached because the same shapes are used every hand.</summary>
    public static class Combinations
    {
        private static readonly Dictionary<int, int[][]> Cache = new Dictionary<int, int[][]>();
        private static readonly object Lock = new object();

        public static int[][] Choose(int n, int k)
        {
            if (k < 0 || n < 0) throw new ArgumentOutOfRangeException();
            int key = n * 64 + k;
            lock (Lock)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
            }

            var result = new List<int[]>();
            if (k <= n)
            {
                var idx = new int[k];
                for (int i = 0; i < k; i++) idx[i] = i;
                while (true)
                {
                    result.Add((int[])idx.Clone());
                    int p = k - 1;
                    while (p >= 0 && idx[p] == n - k + p) p--;
                    if (p < 0) break;
                    idx[p]++;
                    for (int i = p + 1; i < k; i++) idx[i] = idx[i - 1] + 1;
                }
            }
            var array = result.ToArray();
            lock (Lock)
            {
                Cache[key] = array;
            }
            return array;
        }
    }

    /// <summary>
    /// One way of building a 5-card hand: take exactly <see cref="PersonalCount"/> of the player's own cards
    /// and exactly <see cref="SharedCount"/> cards from one shared group (e.g. one side of the Game 23 board).
    /// </summary>
    public sealed class HandShape
    {
        public string Name { get; }
        public int PersonalCount { get; }
        public int SharedCount { get; }
        public IReadOnlyList<Card> Shared { get; }

        public HandShape(string name, int personalCount, IReadOnlyList<Card> shared, int sharedCount)
        {
            if (personalCount + sharedCount != 5) throw new ArgumentException("A hand shape must total five cards.");
            Name = name;
            PersonalCount = personalCount;
            Shared = shared ?? Array.Empty<Card>();
            SharedCount = sharedCount;
        }
    }

    public sealed class BestHandFinder
    {
        private readonly HandEvaluator _evaluator;

        public BestHandFinder(HandEvaluator evaluator) { _evaluator = evaluator; }

        public HandEvaluator Evaluator => _evaluator;

        /// <summary>Best five of any cards (Stud / Draw style).</summary>
        public EvaluatedHand BestOfAny(IReadOnlyList<Card> cards, WildSet wilds)
        {
            return BestOfShapes(cards, new[] { new HandShape("", 5, null, 0) }, wilds);
        }

        /// <summary>
        /// Best hand over several allowed shapes. Returns null when no shape can be completed
        /// (for example, not enough shared cards have been exposed yet).
        /// </summary>
        public EvaluatedHand BestOfShapes(IReadOnlyList<Card> personal, IReadOnlyList<HandShape> shapes, WildSet wilds)
        {
            var bestCards = new Card[5];
            if (!Search(personal, shapes, wilds, out _, bestCards, out var source)) return null;
            return _evaluator.Explain(bestCards, wilds, source);
        }

        /// <summary>Fast path when only the value matters (bots, equity simulation). Invalid value if no shape fits.</summary>
        public HandValue BestValueOfShapes(IReadOnlyList<Card> personal, IReadOnlyList<HandShape> shapes, WildSet wilds)
        {
            Search(personal, shapes, wilds, out var value, null, out _);
            return value;
        }

        private bool Search(IReadOnlyList<Card> personal, IReadOnlyList<HandShape> shapes, WildSet wilds,
            out HandValue bestValue, Card[] bestCards, out string bestSource)
        {
            bestValue = default;
            bestSource = null;
            bool found = false;
            var buffer = new Card[5];
            var rules = _evaluator.Rules;

            foreach (var shape in shapes)
            {
                if (personal.Count < shape.PersonalCount || shape.Shared.Count < shape.SharedCount) continue;
                var personalCombos = Combinations.Choose(personal.Count, shape.PersonalCount);
                var sharedCombos = Combinations.Choose(shape.Shared.Count, shape.SharedCount);
                foreach (var pc in personalCombos)
                {
                    for (int i = 0; i < pc.Length; i++) buffer[i] = personal[pc[i]];
                    foreach (var sc in sharedCombos)
                    {
                        for (int i = 0; i < sc.Length; i++) buffer[pc.Length + i] = shape.Shared[sc[i]];
                        var value = _evaluator.Evaluate(buffer, wilds);
                        if (found && rules.Compare(value, bestValue) <= 0) continue;
                        found = true;
                        bestValue = value;
                        bestSource = shape.Name;
                        if (bestCards != null) Array.Copy(buffer, bestCards, 5);
                    }
                }
            }
            return found;
        }
    }
}
