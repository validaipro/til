using System;
using System.Collections.Generic;
using System.Linq;
using WildCardPoker.Core.DiamondTurnOver;

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
    /// A computer player for Diamond Turn-over. It only reads what its seat could see at a real table: its own cards,
    /// everyone's rolled cards and the face-up board. Hand strength comes from simulating the unseen cards with the
    /// wild card rule applied, so it understands its own wild diamonds.
    /// </summary>
    public sealed class DiamondBot
    {
        private readonly IRandomSource _random;
        private (int hand, int round, int players, int upCards, string board) _cacheKey = (-1, -1, -1, -1, null);
        private double _cachedEquity;

        public PlayerProfile Profile { get; }
        public int SeatIndex { get; }

        /// <summary>0 = calm, 1 = full tilt. Rises after painful losses, decays each hand.</summary>
        public float Tilt { get; private set; }

        public int Samples { get; set; } = 80;

        public DiamondBot(int seatIndex, PlayerProfile profile, IRandomSource random)
        {
            SeatIndex = seatIndex;
            Profile = profile ?? new PlayerProfile();
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        private float Loose => Clamp01(Profile.Looseness + Tilt * 0.3f);
        private float Aggro => Clamp01(Profile.Aggression + Tilt * 0.3f);

        /// <summary>Chooses which hole card to roll face up.</summary>
        public Card ChooseRoll(DiamondTurnOverEngine engine)
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
                var wilds = engine.Settings.Wilds.Resolve(new WildContext(hole, up, engine.Board.AllFaceUp));
                var value = engine.Finder.BestValueOfShapes(personal, shapes, wilds);

                // Primary: strength under the resulting wilds (rolling a diamond changes the lowest diamond in the hole).
                double score = value.IsValid ? value.Strength : 0;
                // Keep diamonds and wilds hidden; showing a high card earns the turn-over pick.
                if (candidate.Suit == Suit.Diamonds) score -= 60_000;
                if (wilds.Contains(candidate)) score -= 50_000;
                score += (int)candidate.Rank * 400 * Aggro;
                score += _random.NextDouble() * (1 - Profile.Patience) * 20_000;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        }

        /// <summary>
        /// Picks a column for the dealer to turn over, or null to pass. Weighs how much each flip could help this
        /// bot's hand (averaging over the cards that might be hidden there) against keeping the board as it is.
        /// </summary>
        public int? ChooseTurnOver(DiamondTurnOverEngine engine)
        {
            var seat = engine.Seats[SeatIndex];
            var unseen = engine.UnseenBy(SeatIndex);
            var board = engine.Board;
            double current = Strength(engine, seat, board.FaceUpInRow(1), board.FaceUpInRow(2));

            int? bestColumn = null;
            double bestGain = 0.04 * (1 - Aggro) + 0.005; // cautious players need a clearer gain
            for (int col = 0; col < DiamondBoard.Columns; col++)
            {
                double total = 0;
                int samples = Math.Min(10, unseen.Count);
                for (int i = 0; i < samples; i++)
                {
                    var hidden = unseen[_random.Next(unseen.Count)];
                    BuildRowsAfterFlip(board, col, hidden, out var row1, out var row2);
                    total += Strength(engine, seat, row1, row2);
                }
                double gain = total / Math.Max(1, samples) - current;
                gain += (_random.NextDouble() - 0.5) * 0.02 * (1 - Profile.Patience);
                if (gain > bestGain)
                {
                    bestGain = gain;
                    bestColumn = col;
                }
            }
            return bestColumn;
        }

        /// <summary>Chooses a betting action for the bot's turn.</summary>
        public BotDecision DecideBet(DiamondTurnOverEngine engine, LegalActions legal)
        {
            int inHand = engine.Seats.Count(s => s.InHand);
            double equity = Equity(engine);
            double relative = equity * inHand;
            double potOdds = legal.ToCall == 0 ? 0 : legal.ToCall / (double)(legal.Pot + legal.ToCall);

            double valueThreshold = 1.55 - 0.55 * Aggro;
            double callNeed = potOdds * (1.15 - 0.7 * Loose);
            bool bluff = relative < 0.8 && _random.NextDouble() < Profile.BluffRate * (1 + Tilt);

            PlayerAction action;
            string chat = null;
            bool raising = legal.CanBetOrRaise && (relative >= valueThreshold || bluff);

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

        /// <summary>Impatient players with weak hands fold before the action reaches them.</summary>
        public bool WantsToFoldOutOfTurn(DiamondTurnOverEngine engine)
        {
            if (engine.Phase != Phase.Betting) return false;
            int toAct = engine.SeatToAct;
            if (toAct < 0 || toAct == SeatIndex) return false;
            if (!engine.Seats[SeatIndex].CanAct) return false;

            int inHand = engine.Seats.Count(s => s.InHand);
            double relative = Equity(engine) * inHand;
            if (relative > 0.45) return false;
            return _random.NextDouble() < (1 - Profile.Patience) * 0.35;
        }

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

        public double Equity(DiamondTurnOverEngine engine)
        {
            var key = (engine.HandNumber, engine.BettingRoundIndex, engine.Seats.Count(s => s.InHand),
                engine.Seats.Sum(s => s.Up.Count), string.Join(",", engine.Board.AllFaceUp));
            if (key == _cacheKey) return _cachedEquity;
            _cachedEquity = DiamondEquity.Estimate(engine, SeatIndex, Samples, _random);
            _cacheKey = key;
            return _cachedEquity;
        }

        /// <summary>A 0..1 measure of hand strength (category-based), used to compare turn-over options.</summary>
        private static double Strength(DiamondTurnOverEngine engine, Seat seat, IReadOnlyList<Card> row1, IReadOnlyList<Card> row2)
        {
            var shapes = DiamondTurnOverEngine.ShapesFor(row1, row2, engine.Settings.MaxBoardCards);
            var wilds = engine.Settings.Wilds.Resolve(new WildContext(seat.Hole, seat.Up, row1.Concat(row2).ToList()));
            var value = engine.Finder.BestValueOfShapes(seat.PersonalCards.ToList(), shapes, wilds);
            return value.IsValid ? value.Strength / (double)(10 << 20) : 0;
        }

        private static void BuildRowsAfterFlip(DiamondBoard board, int column, Card hidden, out List<Card> row1, out List<Card> row2)
        {
            row1 = new List<Card>();
            row2 = new List<Card>();
            for (int c = 0; c < DiamondBoard.Columns; c++)
            {
                bool topUp = board.IsFaceUp(1, c);
                if (c == column)
                {
                    // After the flip the hidden card is up in the other row.
                    if (topUp) row2.Add(hidden);
                    else row1.Add(hidden);
                    continue;
                }
                if (topUp) row1.Add(board.CardAt(1, c));
                else row2.Add(board.CardAt(2, c));
            }
        }

        private long RaiseSize(LegalActions legal, double relative)
        {
            if (legal.MinRaiseTo == legal.MaxRaiseTo) return legal.MinRaiseTo;
            double fraction = 0.45 + 0.6 * Aggro;
            if (Profile.Archetype == Archetype.Maniac && _random.NextDouble() < 0.25) fraction = 3;
            if (relative > 2.2 && _random.NextDouble() < Aggro * 0.3) fraction = 2;
            long target = legal.CurrentBet + (long)Math.Round((legal.Pot + legal.ToCall) * fraction);
            return Math.Max(legal.MinRaiseTo, Math.Min(legal.MaxRaiseTo, target));
        }

        private float ThinkTime(double relative, double closeness, PlayerAction action, ref string chat)
        {
            float baseTime = 0.6f + 1.6f * Profile.Patience;
            if (closeness < 0.08) baseTime += 1.2f;
            if (relative > 2.0)
            {
                if (_random.NextDouble() < Profile.SnapTell) return 0.25f + (float)_random.NextDouble() * 0.35f;
                if (_random.NextDouble() < Profile.Hollywood)
                {
                    chat = chat ?? TableTalk.Maybe(TableTalk.Moment.Tank, Profile, _random, 0.2f);
                    return 3.5f + (float)_random.NextDouble() * 3f;
                }
            }
            if (action.Type == ActionType.Fold && relative < 0.4) baseTime *= 0.4f;
            baseTime *= 1f - 0.4f * Tilt;
            float noise = 0.8f + (float)_random.NextDouble() * 0.4f;
            return Math.Max(0.25f, baseTime * noise);
        }

        private static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }

    public static class DiamondEquity
    {
        /// <summary>
        /// Monte Carlo estimate of a seat's pot share: deals the unseen cards (opponents' hole cards and future
        /// cards) at random and plays to showdown many times. The board is assumed to stay as it is now.
        /// </summary>
        public static double Estimate(DiamondTurnOverEngine engine, int seatIndex, int samples, IRandomSource random)
        {
            var me = engine.Seats[seatIndex];
            var opponents = engine.Seats.Where(s => s.InHand && s.Index != seatIndex).ToList();
            if (opponents.Count == 0) return 1.0;

            int perPlayer = engine.Settings.CardsPerPlayer;
            int myFuture = Math.Max(0, perPlayer - me.Hole.Count - me.Up.Count);
            var row1 = engine.Board.FaceUpInRow(1);
            var row2 = engine.Board.FaceUpInRow(2);
            var boardUp = engine.Board.AllFaceUp;
            var shapes = DiamondTurnOverEngine.ShapesFor(row1, row2, engine.Settings.MaxBoardCards);
            var finder = engine.Finder;
            var rules = engine.Settings.HandRules;
            var call = engine.Settings.Wilds;
            var pool = engine.UnseenBy(seatIndex).ToArray();
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
                    var wilds = call.Resolve(new WildContext(hole, up, boardUp));
                    return finder.BestValueOfShapes(hole.Concat(up).ToList(), shapes, wilds);
                }
            }
            return total / samples;
        }
    }
}
