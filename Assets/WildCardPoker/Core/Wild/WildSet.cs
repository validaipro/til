using System.Collections.Generic;

namespace WildCardPoker.Core
{
    /// <summary>An immutable set of cards (a 52-bit mask). Used for "which cards are wild for this player".</summary>
    public readonly struct WildSet
    {
        public ulong Mask { get; }

        public WildSet(ulong mask) { Mask = mask; }

        public static WildSet Empty => new WildSet(0);

        public static WildSet Of(IEnumerable<Card> cards)
        {
            ulong mask = 0;
            foreach (var c in cards) mask |= 1UL << c.Id;
            return new WildSet(mask);
        }

        public bool Contains(Card card) => (Mask & (1UL << card.Id)) != 0;

        public WildSet With(Card card) => new WildSet(Mask | (1UL << card.Id));

        public WildSet Union(WildSet other) => new WildSet(Mask | other.Mask);

        public bool IsEmpty => Mask == 0;

        public int Count
        {
            get
            {
                int n = 0;
                ulong m = Mask;
                while (m != 0) { m &= m - 1; n++; }
                return n;
            }
        }

        public IEnumerable<Card> Cards
        {
            get
            {
                for (int id = 0; id < 52; id++)
                    if ((Mask & (1UL << id)) != 0) yield return Card.FromId(id);
            }
        }

        public override string ToString()
        {
            var names = new List<string>();
            foreach (var c in Cards) names.Add(c.ToPrettyString());
            return names.Count == 0 ? "(none)" : string.Join(" ", names);
        }
    }
}
