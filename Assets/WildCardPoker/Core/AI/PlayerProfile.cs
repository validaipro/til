using System;
using System.Collections.Generic;

namespace WildCardPoker.Core.AI
{
    /// <summary>Classic player archetypes (see docs/RESEARCH.md).</summary>
    public enum Archetype
    {
        /// <summary>Tight-passive "nit": plays very few hands, rarely bluffs.</summary>
        Rock,

        /// <summary>Tight-aggressive: selective, but bets and raises when in.</summary>
        Shark,

        /// <summary>Loose-aggressive: many hands, lots of pressure.</summary>
        Wildcat,

        /// <summary>Loose-passive "calling station": calls too much, rarely raises.</summary>
        CallingStation,

        /// <summary>Hyper-aggressive: raises and re-raises regardless of cards.</summary>
        Maniac,

        /// <summary>A beginner: inconsistent, leaks timing tells, often acts out of turn.</summary>
        Rookie,
    }

    /// <summary>
    /// Personality knobs for a computer player. All values are 0..1.
    /// They drive decisions, timing (tells), chat and etiquette mistakes such as folding out of turn.
    /// </summary>
    public sealed class PlayerProfile
    {
        public string Name = "Bot";
        public Archetype Archetype = Archetype.Shark;

        /// <summary>How many hands the player is willing to continue with.</summary>
        public float Looseness = 0.4f;

        /// <summary>Preference for betting/raising over checking/calling.</summary>
        public float Aggression = 0.5f;

        /// <summary>Chance to bet or raise with a weak hand.</summary>
        public float BluffRate = 0.08f;

        /// <summary>How strongly losses push the player into tilt (looser, more aggressive, faster).</summary>
        public float TiltProneness = 0.3f;

        /// <summary>Low patience: thinks quickly and tends to fold out of turn with bad cards.</summary>
        public float Patience = 0.7f;

        /// <summary>Chance a strong hand produces an instant "snap" action (a timing tell).</summary>
        public float SnapTell = 0.3f;

        /// <summary>Chance a strong hand produces a long, theatrical think before a raise ("Hollywooding").</summary>
        public float Hollywood = 0.2f;

        /// <summary>How often the player talks at the table.</summary>
        public float Chattiness = 0.3f;

        public PlayerProfile Clone() => (PlayerProfile)MemberwiseClone();

        public static PlayerProfile Preset(Archetype archetype, string name = null)
        {
            var p = new PlayerProfile { Archetype = archetype, Name = name ?? archetype.ToString() };
            switch (archetype)
            {
                case Archetype.Rock:
                    p.Looseness = 0.15f; p.Aggression = 0.3f; p.BluffRate = 0.02f; p.TiltProneness = 0.1f;
                    p.Patience = 0.35f; p.SnapTell = 0.6f; p.Hollywood = 0.05f; p.Chattiness = 0.1f;
                    break;
                case Archetype.Shark:
                    p.Looseness = 0.3f; p.Aggression = 0.7f; p.BluffRate = 0.1f; p.TiltProneness = 0.15f;
                    p.Patience = 0.9f; p.SnapTell = 0.15f; p.Hollywood = 0.3f; p.Chattiness = 0.2f;
                    break;
                case Archetype.Wildcat:
                    p.Looseness = 0.6f; p.Aggression = 0.8f; p.BluffRate = 0.22f; p.TiltProneness = 0.4f;
                    p.Patience = 0.75f; p.SnapTell = 0.3f; p.Hollywood = 0.25f; p.Chattiness = 0.5f;
                    break;
                case Archetype.CallingStation:
                    p.Looseness = 0.85f; p.Aggression = 0.15f; p.BluffRate = 0.03f; p.TiltProneness = 0.3f;
                    p.Patience = 0.6f; p.SnapTell = 0.5f; p.Hollywood = 0.05f; p.Chattiness = 0.4f;
                    break;
                case Archetype.Maniac:
                    p.Looseness = 0.9f; p.Aggression = 0.95f; p.BluffRate = 0.35f; p.TiltProneness = 0.6f;
                    p.Patience = 0.45f; p.SnapTell = 0.45f; p.Hollywood = 0.1f; p.Chattiness = 0.8f;
                    break;
                case Archetype.Rookie:
                    p.Looseness = 0.55f; p.Aggression = 0.35f; p.BluffRate = 0.1f; p.TiltProneness = 0.5f;
                    p.Patience = 0.2f; p.SnapTell = 0.85f; p.Hollywood = 0.05f; p.Chattiness = 0.6f;
                    break;
            }
            return p;
        }

        public static IReadOnlyList<PlayerProfile> DefaultLineup() => new[]
        {
            Preset(Archetype.Shark, "Lena \"The Shark\""),
            Preset(Archetype.Rookie, "Jamie"),
            Preset(Archetype.Maniac, "Big Tony"),
            Preset(Archetype.Rock, "Doc"),
            Preset(Archetype.CallingStation, "Rosa"),
            Preset(Archetype.Wildcat, "Kai"),
        };
    }

    /// <summary>Short table-talk lines per situation and archetype.</summary>
    public static class TableTalk
    {
        public enum Moment
        {
            BigRaise,
            Fold,
            OutOfTurnFold,
            Tank,
            WinPot,
            LoseBigPot,
            Tilted,
        }

        private static readonly Dictionary<Moment, string[]> Lines = new Dictionary<Moment, string[]>
        {
            [Moment.BigRaise] = new[] { "Let's make it interesting.", "Raise it up!", "You don't want to call this.", "Gamble gamble!" },
            [Moment.Fold] = new[] { "I'm out.", "Not this time.", "Too rich for me." },
            [Moment.OutOfTurnFold] = new[] { "Oops, my bad.", "Sorry, sorry!", "I wasn't going to play that anyway." },
            [Moment.Tank] = new[] { "Hmm...", "Let me think about this one.", "What did you roll there?" },
            [Moment.WinPot] = new[] { "Ship it!", "Thank you, thank you.", "Wild cards love me tonight." },
            [Moment.LoseBigPot] = new[] { "Unbelievable.", "Every time!", "Nice hand... I guess." },
            [Moment.Tilted] = new[] { "Deal the cards already!", "I'm calling everything now.", "This deck hates me." },
        };

        public static string Pick(Moment moment, IRandomSource random) =>
            Lines.TryGetValue(moment, out var lines) ? lines[random.Next(lines.Length)] : null;

        /// <summary>Returns a line with probability based on chattiness, else null.</summary>
        public static string Maybe(Moment moment, PlayerProfile profile, IRandomSource random, float boost = 0f)
        {
            float chance = Math.Min(1f, profile.Chattiness * 0.6f + boost);
            return random.NextDouble() < chance ? Pick(moment, random) : null;
        }
    }
}
