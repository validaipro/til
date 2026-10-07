using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WildCardPoker.Core
{
    public sealed class WildCallParseResult
    {
        public WildCall Call { get; }
        public IReadOnlyList<string> Warnings { get; }
        public bool Success => Call != null && Call.Clauses.Count > 0;

        public WildCallParseResult(WildCall call, IReadOnlyList<string> warnings)
        {
            Call = call;
            Warnings = warnings;
        }
    }

    /// <summary>
    /// Turns a dealer's spoken-style call into a <see cref="WildCall"/>. Examples:
    /// "highest club in the hole", "lowest heart in the hole and all like it", "queens or kings in the hole only",
    /// "sevens and threes on the board only", "matching low", "lowest spade in the hole only, and no matching low",
    /// "kings wild; lowest diamond on the board", "one-eyed jacks", "jack of diamonds".
    ///
    /// Clauses can be separated with ";", "+" or "plus". House rule #4: calling a lowest/highest card applies
    /// "all like it" unless the caller says "only" or "no matching low" (configurable via <see cref="DefaultAllLikeIt"/>).
    /// </summary>
    public sealed class WildCallParser
    {
        public bool DefaultAllLikeIt { get; set; } = true;

        private sealed class Draft
        {
            public enum Kind { Ranks, Picked, Specific }

            public Kind Type;
            public readonly List<Rank> Ranks = new List<Rank>();
            public readonly List<Card> Cards = new List<Card>();
            public string SpecificName;
            public Extreme Extreme;
            public Suit? Suit;
            public CardLocation? Location;
            public bool? AllLikeIt;
            public bool Only;
        }

        private static readonly HashSet<string> Filler = new HashSet<string>
        {
            "and", "or", "in", "on", "the", "wild", "wilds", "card", "cards", "are", "is", "a", "an", "with", "my",
            "your", "their", "his", "her", "player", "players", "each", "every", "all", "face", "be", "becomes",
            "become", "also", "of", "s", "then", "call", "calling", "game", "for", "to", "it", "its", "value",
        };

        public WildCallParseResult Parse(string text, string name = null)
        {
            var warnings = new List<string>();
            var drafts = new List<Draft>();
            if (string.IsNullOrWhiteSpace(text))
                return new WildCallParseResult(null, new[] { "Nothing was called." });

            var tokens = Tokenize(text);
            Draft current = null;

            void Finish()
            {
                if (current != null) drafts.Add(current);
                current = null;
            }

            for (int i = 0; i < tokens.Count; i++)
            {
                string t = tokens[i];
                bool Next(int offset, string expected) => i + offset < tokens.Count && tokens[i + offset] == expected;

                if (t == ";") { Finish(); continue; }

                // "matching low" / "matching high" / "low matching"
                if ((t == "matching" && (Next(1, "low") || Next(1, "high"))) || (t == "low" && Next(1, "matching")))
                {
                    Finish();
                    bool high = t == "matching" && Next(1, "high");
                    current = new Draft
                    {
                        Type = Draft.Kind.Picked,
                        Extreme = high ? Extreme.Highest : Extreme.Lowest,
                        AllLikeIt = true,
                    };
                    i++;
                    continue;
                }

                // "no matching (low)", "no all like it", "nothing else"
                if ((t == "no" && Next(1, "matching")) || (t == "no" && Next(1, "all")) || (t == "nothing" && Next(1, "else")))
                {
                    var targets = drafts.Where(d => d.Type == Draft.Kind.Picked).ToList();
                    if (current != null && current.Type == Draft.Kind.Picked) targets.Add(current);
                    foreach (var d in targets) d.AllLikeIt = false;
                    i++;
                    while (i + 1 < tokens.Count && (tokens[i + 1] == "low" || tokens[i + 1] == "high" ||
                                                    tokens[i + 1] == "like" || tokens[i + 1] == "it")) i++;
                    continue;
                }

                // "all like it"
                if ((t == "all" && Next(1, "like")) || t == "like")
                {
                    if (current != null && current.Type == Draft.Kind.Picked) current.AllLikeIt = true;
                    while (i + 1 < tokens.Count && (tokens[i + 1] == "like" || tokens[i + 1] == "it")) i++;
                    continue;
                }

                if (t == "lowest" || t == "low" || t == "highest" || t == "high")
                {
                    Finish();
                    current = new Draft
                    {
                        Type = Draft.Kind.Picked,
                        Extreme = t.StartsWith("low", StringComparison.Ordinal) ? Extreme.Lowest : Extreme.Highest,
                    };
                    continue;
                }

                // Named specials.
                if (t == "one" && Next(1, "eyed"))
                {
                    i += 2;
                    string what = i < tokens.Count ? tokens[i] : "";
                    if (what == "jacks" || what == "jack")
                        StartSpecific(new[] { new Card(Rank.Jack, Core.Suit.Spades), new Card(Rank.Jack, Core.Suit.Hearts) }, "One-eyed jacks");
                    else if (what == "kings" || what == "king")
                        StartSpecific(new[] { new Card(Rank.King, Core.Suit.Diamonds) }, "One-eyed king");
                    else warnings.Add("Unknown one-eyed card: '" + what + "'.");
                    continue;
                }
                if (t == "suicide" && Next(1, "king"))
                {
                    i++;
                    StartSpecific(new[] { new Card(Rank.King, Core.Suit.Hearts) }, "Suicide king");
                    continue;
                }

                if (TryLocation(t, out var location))
                {
                    var target = current ?? drafts.LastOrDefault();
                    if (target == null) warnings.Add("'" + t + "' does not follow any wild card.");
                    else target.Location = location;
                    continue;
                }

                if (t == "only")
                {
                    var target = current ?? drafts.LastOrDefault();
                    if (target != null) target.Only = true;
                    continue;
                }

                if (TrySuit(t, out var suit))
                {
                    if (current != null && current.Type == Draft.Kind.Picked && current.Suit == null) current.Suit = suit;
                    else warnings.Add("Suit '" + t + "' does not belong to a lowest/highest call.");
                    continue;
                }

                if (TryRankWord(t, out var rank))
                {
                    // "jack of diamonds" => a specific card
                    if (Next(1, "of") && i + 2 < tokens.Count && TrySuit(tokens[i + 2], out var ofSuit))
                    {
                        var card = new Card(rank, ofSuit);
                        StartSpecific(new[] { card }, Card.RankName(rank) + " of " + Card.SuitName(ofSuit));
                        i += 2;
                        continue;
                    }
                    if (current == null || current.Type != Draft.Kind.Ranks)
                    {
                        Finish();
                        current = new Draft { Type = Draft.Kind.Ranks };
                    }
                    if (!current.Ranks.Contains(rank)) current.Ranks.Add(rank);
                    continue;
                }

                if (Filler.Contains(t) || t == "no" || t == "eyed") continue;
                warnings.Add("Did not understand '" + t + "'.");
            }
            Finish();

            var clauses = new List<WildClause>();
            foreach (var d in drafts)
            {
                switch (d.Type)
                {
                    case Draft.Kind.Ranks:
                        if (d.Ranks.Count > 0) clauses.Add(new RankWildClause(d.Ranks, d.Location ?? CardLocation.Anywhere));
                        break;
                    case Draft.Kind.Specific:
                        clauses.Add(new SpecificCardsClause(d.Cards, d.Location ?? CardLocation.Anywhere, d.SpecificName));
                        break;
                    case Draft.Kind.Picked:
                        bool allLike = d.AllLikeIt ?? (!d.Only && DefaultAllLikeIt);
                        clauses.Add(new PickedCardWildClause(d.Extreme, d.Suit, d.Location ?? CardLocation.Hole, allLike));
                        break;
                }
            }

            if (clauses.Count == 0) warnings.Add("No wild card rule was recognised.");
            var call = clauses.Count == 0 ? null : new WildCall(name, clauses);
            return new WildCallParseResult(call, warnings);

            void StartSpecific(Card[] cards, string specificName)
            {
                Finish();
                current = new Draft { Type = Draft.Kind.Specific, SpecificName = specificName };
                current.Cards.AddRange(cards);
            }
        }

        private static List<string> Tokenize(string text)
        {
            var sb = new StringBuilder();
            foreach (char ch in text.ToLowerInvariant())
            {
                if (ch == ';' || ch == '+') sb.Append(" ; ");
                else if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else if (ch == '\'' || ch == '’') continue; // "2's" -> "2s", "king's" -> "kings"
                else sb.Append(' ');
            }
            var tokens = new List<string>();
            foreach (var raw in sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                tokens.Add(raw == "plus" ? ";" : raw);
            return tokens;
        }

        private static bool TryLocation(string t, out CardLocation location)
        {
            switch (t)
            {
                case "hole": case "down": case "hand": case "hidden": location = CardLocation.Hole; return true;
                case "board": case "community": case "middle": location = CardLocation.Board; return true;
                case "up": case "showing": case "rolled": case "exposed": location = CardLocation.Up; return true;
                case "anywhere": case "everywhere": location = CardLocation.Anywhere; return true;
                default: location = default; return false;
            }
        }

        private static bool TrySuit(string t, out Suit suit)
        {
            switch (t)
            {
                case "club": case "clubs": suit = Core.Suit.Clubs; return true;
                case "diamond": case "diamonds": suit = Core.Suit.Diamonds; return true;
                case "heart": case "hearts": suit = Core.Suit.Hearts; return true;
                case "spade": case "spades": suit = Core.Suit.Spades; return true;
                default: suit = default; return false;
            }
        }

        private static bool TryRankWord(string t, out Rank rank)
        {
            switch (t)
            {
                case "deuce": case "deuces": case "two": case "twos": case "2": case "2s": rank = Rank.Two; return true;
                case "trey": case "treys": case "tray": case "trays": case "three": case "threes": case "3": case "3s":
                    rank = Rank.Three; return true;
                case "four": case "fours": case "4": case "4s": rank = Rank.Four; return true;
                case "five": case "fives": case "5": case "5s": rank = Rank.Five; return true;
                case "six": case "sixes": case "6": case "6s": rank = Rank.Six; return true;
                case "seven": case "sevens": case "7": case "7s": rank = Rank.Seven; return true;
                case "eight": case "eights": case "8": case "8s": rank = Rank.Eight; return true;
                case "nine": case "nines": case "9": case "9s": rank = Rank.Nine; return true;
                case "ten": case "tens": case "10": case "10s": rank = Rank.Ten; return true;
                case "jack": case "jacks": rank = Rank.Jack; return true;
                case "queen": case "queens": rank = Rank.Queen; return true;
                case "king": case "kings": rank = Rank.King; return true;
                case "ace": case "aces": rank = Rank.Ace; return true;
                default: rank = default; return false;
            }
        }
    }
}
