using System;
using System.Collections.Generic;
using System.Linq;
using WildCardPoker.Core.Game23;

namespace WildCardPoker.Core.AI
{
    public sealed class BotDecision
    {
        public PlayerAction Action { get; }

        /// <summary>How long the bot "thinks" before acting. Varies with personality and hand strength (timing tells).</summary>
        public float ThinkSeconds { get; }

        /// <summary>Optional table talk, or null.</summary>
        public string Chat { get; }

        public BotDecision(PlayerAction action, float thinkSeconds, string chat)
        {
            Action = action;
            ThinkSeconds = thinkSeconds;
            Chat = chat;
        }

        public override string ToString() => $"{Action} after {ThinkSeconds:0.0}s{(Chat != null ? " \"" + Chat + "\"" : "")}";
    }

    /// <summary>
    /// A computer player for Game 23. It only reads information its seat could see at a real table:
    /// its own cards, everyone's rolled cards and the exposed board. Hand strength comes from Monte Carlo simulation
    /// of the unseen cards with the hand's wild card call applied, so bots understand personal wilds.
    /// </summary>
    public sealed class Game23Bot
    {
        private readonly IRandomSource _random;
        private (int hand, int street, int players, int upCards) _cacheKey = (-1, -1, -1, -1);
        private double _cachedEquity;

        public PlayerProfile Profile { get; }
        public int SeatIndex { get; }

        /// <summary>0 = calm, 1 = full tilt. Rises after painful losses, decays each hand.</summary>
        public float Tilt { get; private set; }

        public int Samples { get; set; } = 80;

        public Game23Bot(int seatIndex, PlayerProfile profile, IRandomSource random)
        {
            SeatIndex = seatIndex;
            Profile = profile ?? new PlayerProfile();
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        private float Loose => Clamp01(Profile.Looseness + Tilt * 0.3f);
        private float Aggro => Clamp01(Profile.Aggression + Tilt * 0.3f);

        /// <summary>Chooses which hole card to roll face up.</summary>
        public Card ChooseRoll(Game23Engine engine)
        {
            var seat = engine.Seats[SeatIndex];
            var personal = seat.PersonalCards.ToList();
            var shapes = engine.Shapes();
            Card best = seat.Hole[0];
            double bestScore = double.MinValue;

            foreach (var candidate in seat.Hole)
            {
                var hole = seat.Hole.Where(c => c != candidate).ToList();
                var up = seat.Up.Concat(new[] { candidate }).ToList();
                var wilds = engine.EffectiveCall.Resolve(new WildContext(hole, up, engine.Board.AllExposed, engine.Settings.WildOptions));
                var value = engine.Finder.BestValueOfShapes(personal, shapes, wilds);

                // Primary: hand strength under the resulting wilds (rolling can change "in the hole" wilds).
                double score = value.IsValid ? value.Strength : 0;
                // Secondary: conceal strength - prefer showing low, non-wild cards.
                score += wilds.Contains(candidate) ? -50_000 : 0;
                score -= (int)candidate.Rank * 1_000;
                // Rookies are a bit random.
                score += _random.NextDouble() * (1 - Profile.Patience) * 20_000;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        }

        /// <summary>Chooses a betting action for the bot's turn.</summary>
        public BotDecision DecideBet(Game23Engine engine, LegalActions legal)
        {
            int inHand = engine.Seats.Count(s => s.InHand);
            double equity = Equity(engine);
            double relative = equity * inHand; // 1.0 = an average share of the pot
            double potOdds = legal.ToCall == 0 ? 0 : legal.ToCall / (double)(legal.Pot + legal.ToCall);

            double valueThreshold = 1.55 - 0.55 * Aggro;
            double callNeed = potOdds * (1.15 - 0.7 * Loose);
            bool bluff = relative < 0.8 && _random.NextDouble() < Profile.BluffRate * (1 + Tilt);

            PlayerAction action;
            string chat = null;
            bool strong = relative >= valueThreshold;
            bool raising = legal.CanBetOrRaise && (strong || bluff);

            if (raising)
            {
                long target = RaiseSize(legal, relative);
                action = legal.IsOpeningBet ? PlayerAction.BetTo(target) : PlayerAction.RaiseTo(target);
                if (target >= legal.MaxRaiseTo && legal.MaxRaiseTo == legal.RoundBet + legal.Stack)
                    chat = TableTalk.Maybe(TableTalk.Moment.BigRaise, Profile, _random, 0.3f);
            }
            else if (legal.CanCheck)
            {
                action = PlayerAction.Check();
            }
            else if (equity >= callNeed || (Profile.Archetype == Archetype.CallingStation && relative > 0.6))
            {
                action = PlayerAction.Call();
            }
            else
            {
                action = PlayerAction.Fold();
                chat = TableTalk.Maybe(TableTalk.Moment.Fold, Profile, _random);
            }

            float think = ThinkTime(relative, Math.Abs(equity - potOdds), action, ref chat);
            if (Tilt > 0.6f && chat == null) chat = TableTalk.Maybe(TableTalk.Moment.Tilted, Profile, _random);
            return new BotDecision(action, think, chat);
        }

        /// <summary>
        /// Real players with weak hands and little patience often fold before the action reaches them.
        /// Call this while someone else has the action.
        /// </summary>
        public bool WantsToFoldOutOfTurn(Game23Engine engine)
        {
            if (engine.Phase != Game23Phase.Betting) return false;
            var pending = engine.Pending;
            if (pending.ToAct < 0 || pending.ToAct == SeatIndex) return false;
            var seat = engine.Seats[SeatIndex];
            if (!seat.CanAct) return false;

            int inHand = engine.Seats.Count(s => s.InHand);
            double relative = Equity(engine) * inHand;
            if (relative > 0.45) return false;
            double impatience = 1 - Profile.Patience;
            return _random.NextDouble() < impatience * 0.35;
        }

        /// <summary>Update the emotional state after a hand.</summary>
        public void OnHandEnded(long chipsWonOrLost, long startingStack)
        {
            Tilt *= 0.8f;
            if (chipsWonOrLost < 0 && startingStack > 0)
            {
                float pain = (float)Math.Min(1.0, -chipsWonOrLost / (double)startingStack);
                if (pain > 0.2f) Tilt = Clamp01(Tilt + Profile.TiltProneness * pain * 1.5f);
            }
            else if (chipsWonOrLost > 0)
            {
                Tilt *= 0.6f;
            }
        }

        /// <summary>Estimated share of the pot this seat wins against the players still in.</summary>
        public double Equity(Game23Engine engine)
        {
            var key = (engine.HandNumber, engine.Street, engine.Seats.Count(s => s.InHand),
                engine.Seats.Sum(s => s.Up.Count));
            if (key == _cacheKey) return _cachedEquity;
            _cachedEquity = Game23Equity.Estimate(engine, SeatIndex, Samples, _random);
            _cacheKey = key;
            return _cachedEquity;
        }

        private long RaiseSize(LegalActions legal, double relative)
        {
            if (legal.MinRaiseTo == legal.MaxRaiseTo) return legal.MinRaiseTo;
            double fraction = 0.45 + 0.6 * Aggro;
            if (Profile.Archetype == Archetype.Maniac && _random.NextDouble() < 0.25) fraction = 3; // overbet / shove
            if (relative > 2.2 && _random.NextDouble() < Aggro * 0.3) fraction = 2;
            long target = legal.CurrentBet + (long)Math.Round((legal.Pot + legal.ToCall) * fraction);
            return Math.Max(legal.MinRaiseTo, Math.Min(legal.MaxRaiseTo, target));
        }

        private float ThinkTime(double relative, double closeness, PlayerAction action, ref string chat)
        {
            float baseTime = 0.6f + 1.6f * Profile.Patience;
            if (closeness < 0.08) baseTime += 1.2f; // close decisions take longer

            if (relative > 2.0)
            {
                if (_random.NextDouble() < Profile.SnapTell) return 0.25f + (float)_random.NextDouble() * 0.35f;
                if (_random.NextDouble() < Profile.Hollywood)
                {
                    chat = chat ?? TableTalk.Maybe(TableTalk.Moment.Tank, Profile, _random, 0.2f);
                    return 3.5f + (float)_random.NextDouble() * 3f;
                }
            }
            if (action.Type == ActionType.Fold && relative < 0.4) baseTime *= 0.4f; // trash folds are fast
            baseTime *= 1f - 0.4f * Tilt;                                           // tilted players act fast
            float noise = 0.8f + (float)_random.NextDouble() * 0.4f;
            return Math.Max(0.25f, baseTime * noise);
        }

        private static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }

    public static class Game23Equity
    {
        /// <summary>
        /// Monte Carlo estimate of a seat's pot share. Deals out every unseen card at random (unflipped board,
        /// opponents' hole cards, future cards) and plays the hand to showdown, many times.
        /// Future cards are treated as hole cards (an approximation for "in the hole" wild calls).
        /// </summary>
        public static double Estimate(Game23Engine engine, int seatIndex, int samples, IRandomSource random)
        {
            var me = engine.Seats[seatIndex];
            var opponents = engine.Seats.Where(s => s.InHand && s.Index != seatIndex).ToList();
            if (opponents.Count == 0) return 1.0;

            int perPlayer = engine.Settings.InitialHoleCards + engine.Settings.FlipSchedule.Length - 1 +
                            (engine.Settings.FinalDownCard ? 1 : 0);
            var unseen = engine.UnseenBy(seatIndex);
            int needTwo = Game23Board.CardsPerSide - engine.Board.FlippedTwo;
            int needThree = Game23Board.CardsPerSide - engine.Board.FlippedThree;
            int myFuture = Math.Max(0, perPlayer - me.Hole.Count - me.Up.Count);

            var finder = engine.Finder;
            var rules = engine.Settings.HandRules;
            var call = engine.EffectiveCall;
            var options = engine.Settings.WildOptions;
            var pool = unseen.ToArray();
            double total = 0;

            for (int sample = 0; sample < samples; sample++)
            {
                int next = 0;
                Card Draw()
                {
                    int j = next + random.Next(pool.Length - next);
                    var tmp = pool[next];
                    pool[next] = pool[j];
                    pool[j] = tmp;
                    return pool[next++];
                }

                var two = engine.Board.ExposedTwo.ToList();
                var three = engine.Board.ExposedThree.ToList();
                for (int i = 0; i < needTwo; i++) two.Add(Draw());
                for (int i = 0; i < needThree; i++) three.Add(Draw());
                var board = two.Concat(three).ToList();
                var shapes = Game23Engine.ShapesFor(two, three);

                var myHole = me.Hole.ToList();
                for (int i = 0; i < myFuture; i++) myHole.Add(Draw());
                var myValue = Value(myHole, me.Up);

                int ties = 1;
                bool lost = false;
                foreach (var opp in opponents)
                {
                    int hidden = perPlayer - opp.Up.Count;
                    var oppHole = new List<Card>(hidden);
                    for (int i = 0; i < hidden; i++) oppHole.Add(Draw());
                    int cmp = rules.Compare(Value(oppHole, opp.Up), myValue);
                    if (cmp > 0) { lost = true; break; }
                    if (cmp == 0) ties++;
                }
                if (!lost) total += 1.0 / ties;

                HandValue Value(List<Card> hole, List<Card> up)
                {
                    var wilds = call.Resolve(new WildContext(hole, up, board, options));
                    var personal = hole.Concat(up).ToList();
                    return finder.BestValueOfShapes(personal, shapes, wilds);
                }
            }
            return total / samples;
        }
    }
}
