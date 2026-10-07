using System;
using System.Collections.Generic;
using System.Linq;

namespace WildCardPoker.Core
{
    /// <summary>Where a card sits from one player's point of view.</summary>
    public enum CardLocation
    {
        /// <summary>Any card the player can use: hole, up (rolled) and board.</summary>
        Anywhere,

        /// <summary>The player's face-down cards ("in the hole", "in the hand").</summary>
        Hole,

        /// <summary>The player's face-up (rolled) cards.</summary>
        Up,

        /// <summary>Shared cards in the middle of the table.</summary>
        Board,

        /// <summary>All of the player's own cards, face down or face up.</summary>
        Personal,
    }

    public enum Extreme
    {
        Lowest,
        Highest,
    }

    public sealed class WildCallOptions
    {
        /// <summary>
        /// When true, "in the hole" also covers the player's face-up rolled cards (every card the player owns).
        /// When false (default), "in the hole" means face-down cards only.
        /// </summary>
        public bool HoleIncludesUpCards = false;

        /// <summary>When picking the "lowest" card, count an ace as low (below the deuce).</summary>
        public bool AceLowWhenPicking = false;

        public static WildCallOptions Default => new WildCallOptions();
    }

    /// <summary>Everything one player can see and use when deciding which cards are wild for them.</summary>
    public sealed class WildContext
    {
        public IReadOnlyList<Card> Hole { get; }
        public IReadOnlyList<Card> Up { get; }
        public IReadOnlyList<Card> Board { get; }
        public WildCallOptions Options { get; }

        public WildContext(IReadOnlyList<Card> hole, IReadOnlyList<Card> up, IReadOnlyList<Card> board,
            WildCallOptions options = null)
        {
            Hole = hole ?? Array.Empty<Card>();
            Up = up ?? Array.Empty<Card>();
            Board = board ?? Array.Empty<Card>();
            Options = options ?? WildCallOptions.Default;
        }

        public IEnumerable<Card> In(CardLocation location)
        {
            switch (location)
            {
                case CardLocation.Hole:
                    return Options.HoleIncludesUpCards ? Hole.Concat(Up) : Hole;
                case CardLocation.Up:
                    return Up;
                case CardLocation.Board:
                    return Board;
                case CardLocation.Personal:
                    return Hole.Concat(Up);
                default:
                    return Hole.Concat(Up).Concat(Board);
            }
        }

        public int PickValue(Rank rank) =>
            Options.AceLowWhenPicking && rank == Rank.Ace ? 1 : (int)rank;
    }

    /// <summary>One rule inside a wild card call.</summary>
    public abstract class WildClause
    {
        /// <summary>Cards made wild for the player described by <paramref name="context"/>.</summary>
        public abstract WildSet Apply(WildContext context);

        public abstract string Describe();

        /// <summary>True when the result can differ between players (depends on their own cards).</summary>
        public abstract bool IsPersonal { get; }

        public override string ToString() => Describe();

        internal static string LocationPhrase(CardLocation location)
        {
            switch (location)
            {
                case CardLocation.Hole: return "in the hole";
                case CardLocation.Up: return "face up";
                case CardLocation.Board: return "on the board";
                case CardLocation.Personal: return "in your own cards";
                default: return "";
            }
        }
    }

    /// <summary>"Deuces and treys", "Queens or kings in the hole only", "Sevens and threes on the board only".</summary>
    public sealed class RankWildClause : WildClause
    {
        public IReadOnlyList<Rank> Ranks { get; }
        public CardLocation Where { get; }

        public RankWildClause(IEnumerable<Rank> ranks, CardLocation where = CardLocation.Anywhere)
        {
            Ranks = ranks.Distinct().OrderBy(r => r).ToArray();
            if (Ranks.Count == 0) throw new ArgumentException("At least one rank is required.", nameof(ranks));
            Where = where;
        }

        public override bool IsPersonal => Where == CardLocation.Hole || Where == CardLocation.Up || Where == CardLocation.Personal;

        public override WildSet Apply(WildContext context)
        {
            var set = WildSet.Empty;
            foreach (var card in context.In(Where))
                if (Ranks.Contains(card.Rank)) set = set.With(card);
            return set;
        }

        public override string Describe()
        {
            var names = Ranks.Select(r => Card.RankName(r, plural: true)).ToList();
            string list = names.Count == 1 ? names[0]
                : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];
            var where = LocationPhrase(Where);
            return where.Length == 0 ? list + " wild" : list + " " + where + " only";
        }
    }

    /// <summary>Named cards such as one-eyed jacks (J♠ J♥) or the suicide king (K♥).</summary>
    public sealed class SpecificCardsClause : WildClause
    {
        public IReadOnlyList<Card> Cards { get; }
        public CardLocation Where { get; }
        public string Name { get; }

        public SpecificCardsClause(IEnumerable<Card> cards, CardLocation where = CardLocation.Anywhere, string name = null)
        {
            Cards = cards.Distinct().ToArray();
            Where = where;
            Name = name ?? string.Join(" ", Cards.Select(c => c.ToPrettyString()));
        }

        public override bool IsPersonal => Where == CardLocation.Hole || Where == CardLocation.Up || Where == CardLocation.Personal;

        public override WildSet Apply(WildContext context)
        {
            var set = WildSet.Empty;
            foreach (var card in context.In(Where))
                if (Cards.Contains(card)) set = set.With(card);
            return set;
        }

        public override string Describe()
        {
            var where = LocationPhrase(Where);
            return where.Length == 0 ? Name + " wild" : Name + " " + where + " only";
        }
    }

    /// <summary>
    /// "Lowest diamond in the hole", "Highest club in the hole", "Matching low" (lowest card in the hole and all like it).
    /// The picked card is wild. With <see cref="AllLikeIt"/>, every card of the same rank the player can use
    /// (hole, rolled, board) is wild for that player too.
    /// </summary>
    public sealed class PickedCardWildClause : WildClause
    {
        public Extreme Extreme { get; }
        public Suit? Suit { get; }
        public CardLocation PickFrom { get; }
        public bool AllLikeIt { get; }

        public PickedCardWildClause(Extreme extreme, Suit? suit = null, CardLocation pickFrom = CardLocation.Hole,
            bool allLikeIt = true)
        {
            Extreme = extreme;
            Suit = suit;
            PickFrom = pickFrom;
            AllLikeIt = allLikeIt;
        }

        /// <summary>"Matching low": the lowest card in the hole and every card of that value.</summary>
        public static PickedCardWildClause MatchingLow() => new PickedCardWildClause(Extreme.Lowest, null, CardLocation.Hole, true);

        public override bool IsPersonal => PickFrom != CardLocation.Board;

        /// <summary>The picked cards (several only if tied, e.g. two 3s when "lowest card" is called).</summary>
        public IReadOnlyList<Card> Pick(WildContext context)
        {
            var candidates = context.In(PickFrom).Where(c => Suit == null || c.Suit == Suit.Value).ToList();
            if (candidates.Count == 0) return Array.Empty<Card>();
            int target = Extreme == Extreme.Lowest
                ? candidates.Min(c => context.PickValue(c.Rank))
                : candidates.Max(c => context.PickValue(c.Rank));
            return candidates.Where(c => context.PickValue(c.Rank) == target).ToArray();
        }

        public override WildSet Apply(WildContext context)
        {
            var picked = Pick(context);
            var set = WildSet.Of(picked);
            if (AllLikeIt && picked.Count > 0)
            {
                var rank = picked[0].Rank;
                foreach (var card in context.In(CardLocation.Anywhere))
                    if (card.Rank == rank) set = set.With(card);
            }
            return set;
        }

        public override string Describe()
        {
            if (Extreme == Extreme.Lowest && Suit == null && PickFrom == CardLocation.Hole && AllLikeIt)
                return "Matching low (lowest card in the hole and all like it)";
            var what = Suit == null ? "card" : Card.SuitName(Suit.Value, plural: false).ToLowerInvariant();
            var text = (Extreme == Extreme.Lowest ? "Lowest " : "Highest ") + what + " " + LocationPhrase(PickFrom);
            return text + (AllLikeIt ? " and all like it" : " only");
        }
    }

    /// <summary>A complete wild card call: the clauses a dealer announces for one hand.</summary>
    public sealed class WildCall
    {
        public string Name { get; }
        public IReadOnlyList<WildClause> Clauses { get; }

        public WildCall(string name, IEnumerable<WildClause> clauses)
        {
            Clauses = clauses.ToArray();
            Name = string.IsNullOrWhiteSpace(name) ? DescribeClauses(Clauses) : name;
        }

        public WildCall(string name, params WildClause[] clauses) : this(name, (IEnumerable<WildClause>)clauses) { }

        public static WildCall None => new WildCall("No wild cards", Array.Empty<WildClause>());

        public WildCall Plus(WildCall other) =>
            new WildCall(Name + " + " + other.Name, Clauses.Concat(other.Clauses));

        public WildSet Resolve(WildContext context)
        {
            var set = WildSet.Empty;
            foreach (var clause in Clauses) set = set.Union(clause.Apply(context));
            return set;
        }

        public string Describe() => DescribeClauses(Clauses);

        private static string DescribeClauses(IReadOnlyList<WildClause> clauses) =>
            clauses.Count == 0 ? "No wild cards" : string.Join("; ", clauses.Select(c => c.Describe()));

        public override string ToString() => Name;
    }

    /// <summary>Ready-made calls for the dealer's menu.</summary>
    public static class WildCallPresets
    {
        public static WildCall DeucesAndTreys() =>
            new WildCall("Deuces and treys", new RankWildClause(new[] { Rank.Two, Rank.Three }));

        public static WildCall MatchingLow() => new WildCall("Matching low", PickedCardWildClause.MatchingLow());

        public static WildCall HighestClubInTheHole() =>
            new WildCall("Highest club in the hole", new PickedCardWildClause(Extreme.Highest, Suit.Clubs));

        public static WildCall LowestDiamondInTheHole() =>
            new WildCall("Lowest diamond in the hole", new PickedCardWildClause(Extreme.Lowest, Suit.Diamonds));

        public static WildCall LowestHeartAndAllLikeIt() =>
            new WildCall("Lowest heart in the hole and all like it",
                new PickedCardWildClause(Extreme.Lowest, Suit.Hearts, CardLocation.Hole, allLikeIt: true));

        public static WildCall LowestSpadeOnly() =>
            new WildCall("Lowest spade in the hole only",
                new PickedCardWildClause(Extreme.Lowest, Suit.Spades, CardLocation.Hole, allLikeIt: false));

        public static WildCall QueensOrKingsInTheHole() =>
            new WildCall("Queens or kings in the hole only",
                new RankWildClause(new[] { Rank.Queen, Rank.King }, CardLocation.Hole));

        public static WildCall SevensAndThreesOnTheBoard() =>
            new WildCall("Sevens and threes on the board only",
                new RankWildClause(new[] { Rank.Seven, Rank.Three }, CardLocation.Board));

        public static WildCall OneEyedJacks() =>
            new WildCall("One-eyed jacks",
                new SpecificCardsClause(new[] { new Card(Rank.Jack, Suit.Spades), new Card(Rank.Jack, Suit.Hearts) },
                    CardLocation.Anywhere, "One-eyed jacks"));

        public static WildCall SuicideKing() =>
            new WildCall("Suicide king",
                new SpecificCardsClause(new[] { new Card(Rank.King, Suit.Hearts) }, CardLocation.Anywhere, "Suicide king"));

        public static IReadOnlyList<WildCall> All() => new[]
        {
            MatchingLow(),
            HighestClubInTheHole(),
            LowestDiamondInTheHole(),
            LowestHeartAndAllLikeIt(),
            LowestSpadeOnly(),
            QueensOrKingsInTheHole(),
            SevensAndThreesOnTheBoard(),
            OneEyedJacks(),
            SuicideKing(),
        };
    }
}
