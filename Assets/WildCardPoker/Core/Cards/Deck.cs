using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace WildCardPoker.Core
{
    /// <summary>Source of randomness. Swap implementations for fair play (crypto) or reproducible tests (seeded).</summary>
    public interface IRandomSource
    {
        /// <summary>Returns a uniformly distributed integer in [0, maxExclusive).</summary>
        int Next(int maxExclusive);

        /// <summary>Returns a uniformly distributed double in [0, 1).</summary>
        double NextDouble();
    }

    /// <summary>Deterministic randomness for tests, replays and bots.</summary>
    public sealed class SeededRandom : IRandomSource
    {
        private readonly Random _random;
        public SeededRandom(int seed) { _random = new Random(seed); }
        public int Next(int maxExclusive) => _random.Next(maxExclusive);
        public double NextDouble() => _random.NextDouble();
    }

    /// <summary>Cryptographically secure randomness for shuffling real games.</summary>
    public sealed class CryptoRandom : IRandomSource, IDisposable
    {
        private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
        private readonly byte[] _buffer = new byte[4];

        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            // Rejection sampling removes modulo bias.
            uint limit = uint.MaxValue - (uint.MaxValue % (uint)maxExclusive);
            uint value;
            do
            {
                _rng.GetBytes(_buffer);
                value = BitConverter.ToUInt32(_buffer, 0);
            } while (value >= limit);
            return (int)(value % (uint)maxExclusive);
        }

        public double NextDouble()
        {
            _rng.GetBytes(_buffer);
            return (BitConverter.ToUInt32(_buffer, 0) >> 1) / (double)(1u << 31);
        }

        public void Dispose() => _rng.Dispose();
    }

    public sealed class Deck
    {
        private readonly List<Card> _cards;

        public Deck() : this(Card.FullDeck()) { }

        public Deck(IEnumerable<Card> cards) { _cards = new List<Card>(cards); }

        public int Count => _cards.Count;

        public IReadOnlyList<Card> Remaining => _cards;

        /// <summary>Fisher-Yates shuffle.</summary>
        public void Shuffle(IRandomSource random)
        {
            for (int i = _cards.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                var tmp = _cards[i];
                _cards[i] = _cards[j];
                _cards[j] = tmp;
            }
        }

        /// <summary>Draws from the top of the deck (the end of the list).</summary>
        public Card Draw()
        {
            if (_cards.Count == 0) throw new InvalidOperationException("The deck is empty.");
            var card = _cards[_cards.Count - 1];
            _cards.RemoveAt(_cards.Count - 1);
            return card;
        }

        public bool Remove(Card card) => _cards.Remove(card);
    }
}
