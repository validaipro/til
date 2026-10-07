using System;
using System.Collections.Generic;

namespace WildCardPoker.Core.Game23
{
    /// <summary>
    /// The "design board" for Game 23: seven cards laid out in the shape of a 2 and seven in the shape of a 3.
    /// Cards are flipped in slot order (following the stroke of each number).
    /// </summary>
    public sealed class Game23Board
    {
        public const int CardsPerSide = 7;

        private readonly Card[] _two = new Card[CardsPerSide];
        private readonly Card[] _three = new Card[CardsPerSide];

        public int FlippedTwo { get; private set; }
        public int FlippedThree { get; private set; }
        public bool IsSet { get; private set; }

        public void SetUp(Deck deck)
        {
            for (int i = 0; i < CardsPerSide; i++) _two[i] = deck.Draw();
            for (int i = 0; i < CardsPerSide; i++) _three[i] = deck.Draw();
            FlippedTwo = 0;
            FlippedThree = 0;
            IsSet = true;
        }

        /// <summary>Test helper: lay out a known board.</summary>
        public void SetUp(IReadOnlyList<Card> twoSide, IReadOnlyList<Card> threeSide)
        {
            if (twoSide.Count != CardsPerSide || threeSide.Count != CardsPerSide)
                throw new ArgumentException("Each side needs exactly seven cards.");
            for (int i = 0; i < CardsPerSide; i++)
            {
                _two[i] = twoSide[i];
                _three[i] = threeSide[i];
            }
            FlippedTwo = 0;
            FlippedThree = 0;
            IsSet = true;
        }

        /// <summary>Flips the next card on side 2 or 3. Returns the slot index.</summary>
        public int FlipNext(int side, out Card card)
        {
            if (side == 2)
            {
                if (FlippedTwo >= CardsPerSide) throw new InvalidOperationException("The 2 side is fully exposed.");
                card = _two[FlippedTwo];
                return FlippedTwo++;
            }
            if (side == 3)
            {
                if (FlippedThree >= CardsPerSide) throw new InvalidOperationException("The 3 side is fully exposed.");
                card = _three[FlippedThree];
                return FlippedThree++;
            }
            throw new ArgumentOutOfRangeException(nameof(side), "Side must be 2 or 3.");
        }

        public IReadOnlyList<Card> ExposedTwo => Slice(_two, FlippedTwo);
        public IReadOnlyList<Card> ExposedThree => Slice(_three, FlippedThree);

        public IReadOnlyList<Card> AllExposed
        {
            get
            {
                var list = new List<Card>(FlippedTwo + FlippedThree);
                list.AddRange(ExposedTwo);
                list.AddRange(ExposedThree);
                return list;
            }
        }

        /// <summary>Card in a slot if it has been flipped.</summary>
        public Card? Peek(int side, int slot)
        {
            if (side == 2) return slot < FlippedTwo ? _two[slot] : (Card?)null;
            return slot < FlippedThree ? _three[slot] : (Card?)null;
        }

        private static Card[] Slice(Card[] source, int count)
        {
            var result = new Card[count];
            Array.Copy(source, result, count);
            return result;
        }
    }
}
