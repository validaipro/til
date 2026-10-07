using System;
using System.Collections.Generic;
using System.Linq;

namespace WildCardPoker.Core.Game23
{
    public enum OutOfTurnFoldPolicy
    {
        /// <summary>Folding before your turn is refused (strict online-style).</summary>
        NotAllowed,

        /// <summary>Allowed and binding, no comment from the dealer.</summary>
        AllowedBinding,

        /// <summary>Allowed and binding; the dealer warns the player like in a live card room.</summary>
        AllowedWithWarning,

        /// <summary>Allowed and binding; after repeated warnings the player sits out a hand.</summary>
        AllowedWithPenalty,
    }

    public enum Game23Phase
    {
        NotStarted,
        Rolling,
        Betting,
        HandOver,
    }

    public sealed class Game23Settings
    {
        public long Ante = 5;
        public BetStructure Structure = BetStructure.FixedLimit;

        /// <summary>Fixed limit: bet size on the early rounds. No/Pot limit: minimum opening bet.</summary>
        public long SmallBet = 10;

        /// <summary>Fixed limit: bet size from the third betting round on.</summary>
        public long BigBet = 20;

        public int MaxRaisesPerRound = 4;

        public int InitialHoleCards = 3;

        /// <summary>How many cards are turned on EACH side before each roll. Must total 7 per side.</summary>
        public int[] FlipSchedule = { 2, 2, 2, 1 };

        /// <summary>After the last roll each player gets one more card that stays down in the hole.</summary>
        public bool FinalDownCard = true;

        /// <summary>Mandatory wilds for Game 23: deuces and treys.</summary>
        public WildCall MandatoryWilds = WildCallPresets.DeucesAndTreys();

        public WildCallOptions WildOptions = WildCallOptions.Default;
        public HandRules HandRules = HandRules.Default;

        public OutOfTurnFoldPolicy OutOfTurnFolds = OutOfTurnFoldPolicy.AllowedWithWarning;
        public int WarningsBeforePenalty = 3;

        public static Game23Settings Default => new Game23Settings();

        /// <summary>14 board cards plus 7 cards per player must fit in one 52-card deck.</summary>
        public int MaxPlayers => (52 - 2 * Game23Board.CardsPerSide) / (InitialHoleCards + FlipSchedule.Length - 1 + (FinalDownCard ? 1 : 0));

        public void Validate()
        {
            if (FlipSchedule.Sum() != Game23Board.CardsPerSide)
                throw new InvalidOperationException("The flip schedule must expose exactly seven cards per side.");
            if (InitialHoleCards < 1) throw new InvalidOperationException("Players need at least one starting card.");
        }
    }

    /// <summary>What the engine is waiting for.</summary>
    public sealed class PendingDecision
    {
        public Game23Phase Phase { get; }

        /// <summary>Seats that still have to choose a card to roll.</summary>
        public IReadOnlyList<int> RollSeats { get; }

        /// <summary>Seat that must act in the betting round, or -1.</summary>
        public int ToAct { get; }

        public LegalActions Legal { get; }

        public PendingDecision(Game23Phase phase, IReadOnlyList<int> rollSeats, int toAct, LegalActions legal)
        {
            Phase = phase;
            RollSeats = rollSeats;
            ToAct = toAct;
            Legal = legal;
        }
    }

    /// <summary>
    /// Rules engine for "23 (MJ)": a seven-card rolling game with a 14-card design board shaped like a 2 and a 3.
    ///
    /// Flow per hand: antes; the dealer lays out the board face down; each player gets three hole cards;
    /// then for each street the dealer turns cards on both sides (2,2,2,1 by default), every player rolls one of
    /// their hole cards face up (choices are revealed together), and a betting round follows. From the second
    /// street on each player is dealt one more hole card before the flip. After the fourth roll a final card is
    /// dealt down, there is a last betting round and a showdown. Everyone finishes with 4 up and 3 down.
    ///
    /// A hand is either 2 cards from the 2 side + 3 of the player's own cards, or 3 cards from the 3 side + 2 own
    /// cards. Deuces and treys are always wild; the dealer's call adds more.
    /// The engine accepts out-of-turn folds at any moment according to <see cref="Game23Settings.OutOfTurnFolds"/>.
    /// </summary>
    public sealed class Game23Engine
    {
        private readonly List<Seat> _seats;
        private readonly IRandomSource _random;
        private readonly List<GameEvent> _events = new List<GameEvent>();
        private readonly Dictionary<int, Card> _rollChoices = new Dictionary<int, Card>();
        private readonly HashSet<int> _rollPending = new HashSet<int>();
        private readonly BestHandFinder _finder;

        private Deck _deck;
        private BettingRound _round;
        private int _street;
        private bool _finalCardDealt;

        public Game23Settings Settings { get; }
        public IReadOnlyList<Seat> Seats => _seats;
        public Game23Board Board { get; } = new Game23Board();
        public Game23Phase Phase { get; private set; } = Game23Phase.NotStarted;
        public int DealerSeat { get; private set; } = -1;
        public int HandNumber { get; private set; }
        public WildCall CurrentCall { get; private set; } = WildCall.None;

        /// <summary>The dealer's call plus the mandatory deuces and treys.</summary>
        public WildCall EffectiveCall { get; private set; } = WildCall.None;

        /// <summary>0-based street (roll) index; 4 means the final down-card round.</summary>
        public int Street => _finalCardDealt ? Settings.FlipSchedule.Length : _street;

        public BestHandFinder Finder => _finder;

        public Game23Engine(Game23Settings settings, IEnumerable<Seat> seats, IRandomSource random)
        {
            Settings = settings ?? Game23Settings.Default;
            Settings.Validate();
            _seats = seats.ToList();
            for (int i = 0; i < _seats.Count; i++)
                if (_seats[i].Index != i) throw new ArgumentException("Seat indexes must be 0..n-1 in order.");
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _finder = new BestHandFinder(new HandEvaluator(Settings.HandRules));
        }

        public long PotTotal => _seats.Sum(s => s.TotalBet);

        /// <summary>True when events are waiting to be drained.</summary>
        public bool HasEvents => _events.Count > 0;

        /// <summary>Who will deal the next hand (so the UI can ask that player for a wild card call first).</summary>
        public int NextDealerSeat() => NextSeat(DealerSeat, s => s.Stack > 0 && !s.SitOutNextHand);

        public IReadOnlyList<GameEvent> DrainEvents()
        {
            var copy = _events.ToArray();
            _events.Clear();
            return copy;
        }

        public PendingDecision Pending
        {
            get
            {
                switch (Phase)
                {
                    case Game23Phase.Rolling:
                        return new PendingDecision(Phase, _rollPending.OrderBy(x => x).ToArray(), -1, null);
                    case Game23Phase.Betting:
                        return new PendingDecision(Phase, Array.Empty<int>(), _round.ToAct,
                            _round.ToAct >= 0 ? _round.Legal(_round.ToAct) : null);
                    default:
                        return new PendingDecision(Phase, Array.Empty<int>(), -1, null);
                }
            }
        }

        /// <summary>Starts a hand. <paramref name="dealerCall"/> is the dealer's wild card call (mandatory 2s and 3s are added).</summary>
        public void StartHand(WildCall dealerCall, Deck presetDeck = null)
        {
            if (Phase != Game23Phase.NotStarted && Phase != Game23Phase.HandOver)
                throw new InvalidOperationException("A hand is already in progress.");

            foreach (var s in _seats)
            {
                bool penalty = s.SitOutNextHand;
                s.ResetForHand();
                s.SitOutNextHand = false;
                if (!penalty && s.Stack > 0) s.Status = SeatStatus.Active;
            }

            int players = _seats.Count(s => s.Status == SeatStatus.Active);
            if (players < 2) throw new InvalidOperationException("At least two players with chips are needed.");
            if (players > Settings.MaxPlayers)
                throw new InvalidOperationException($"Game 23 supports at most {Settings.MaxPlayers} players with one deck.");

            HandNumber++;
            DealerSeat = NextSeat(DealerSeat, s => s.Status == SeatStatus.Active);
            CurrentCall = dealerCall ?? WildCall.None;
            EffectiveCall = Settings.MandatoryWilds == null
                ? CurrentCall
                : new WildCall(CurrentCall.Clauses.Count == 0 ? Settings.MandatoryWilds.Name : Settings.MandatoryWilds.Name + " + " + CurrentCall.Name,
                    Settings.MandatoryWilds.Clauses.Concat(CurrentCall.Clauses));
            _street = 0;
            _finalCardDealt = false;
            _round = null;
            _rollChoices.Clear();
            _rollPending.Clear();

            _deck = presetDeck ?? new Deck();
            if (presetDeck == null) _deck.Shuffle(_random);

            _events.Add(new HandStartedEvent(HandNumber, DealerSeat, CurrentCall));

            foreach (var s in DealOrder())
            {
                long ante = s.Commit(Math.Min(Settings.Ante, s.Stack));
                if (ante > 0) _events.Add(new AntePostedEvent(s.Index, ante));
            }

            Board.SetUp(_deck);
            _events.Add(new BoardSetEvent());

            for (int round = 0; round < Settings.InitialHoleCards; round++)
                foreach (var s in DealOrder()) DealDown(s);

            FlipForStreet(0);
            BeginRolling();
            Continue();
        }

        /// <summary>A player chooses which hole card to roll face up. Choices are revealed together.</summary>
        public void SubmitRoll(int seatIndex, Card card)
        {
            if (Phase != Game23Phase.Rolling) throw new InvalidOperationException("Not in the rolling phase.");
            if (!_rollPending.Contains(seatIndex)) throw new InvalidOperationException($"Seat {seatIndex} has nothing to roll.");
            var seat = _seats[seatIndex];
            if (!seat.Hole.Contains(card)) throw new InvalidOperationException($"{card} is not in seat {seatIndex}'s hole.");
            _rollChoices[seatIndex] = card;
            _rollPending.Remove(seatIndex);
            Continue();
        }

        /// <summary>An in-turn betting action.</summary>
        public void Act(int seatIndex, PlayerAction action)
        {
            if (Phase != Game23Phase.Betting || _round == null) throw new InvalidOperationException("Not in a betting round.");
            var seat = _seats[seatIndex];
            long committed = _round.Apply(seatIndex, action);
            _events.Add(new ActionEvent(seatIndex, action, committed, seat.Status == SeatStatus.AllIn));
            Continue();
        }

        /// <summary>
        /// Folds a player at any time: in turn, out of turn, or while others are still choosing their rolls.
        /// Returns false if the fold was refused.
        /// </summary>
        public bool Fold(int seatIndex)
        {
            if (Phase == Game23Phase.NotStarted || Phase == Game23Phase.HandOver) return false;
            var seat = _seats[seatIndex];
            if (!seat.InHand) return false;

            bool inTurn = Phase == Game23Phase.Betting && _round != null && _round.ToAct == seatIndex;
            if (inTurn)
            {
                Act(seatIndex, PlayerAction.Fold());
                return true;
            }

            int expected = Phase == Game23Phase.Betting && _round != null ? _round.ToAct : -1;
            // Folding while everyone is choosing a roll is not "out of turn" - nobody has the action.
            bool outOfTurn = expected >= 0;
            if (outOfTurn && Settings.OutOfTurnFolds == OutOfTurnFoldPolicy.NotAllowed)
            {
                _events.Add(new OutOfTurnRejectedEvent(seatIndex));
                return false;
            }

            seat.Status = SeatStatus.Folded;
            _rollPending.Remove(seatIndex);
            _rollChoices.Remove(seatIndex);
            _round?.RemoveFolded(seatIndex);

            if (outOfTurn)
            {
                bool warn = Settings.OutOfTurnFolds == OutOfTurnFoldPolicy.AllowedWithWarning ||
                            Settings.OutOfTurnFolds == OutOfTurnFoldPolicy.AllowedWithPenalty;
                if (warn) seat.OutOfTurnWarnings++;
                bool penalized = Settings.OutOfTurnFolds == OutOfTurnFoldPolicy.AllowedWithPenalty &&
                                 seat.OutOfTurnWarnings >= Settings.WarningsBeforePenalty;
                if (penalized)
                {
                    seat.SitOutNextHand = true;
                    seat.OutOfTurnWarnings = 0;
                }
                _events.Add(new OutOfTurnFoldEvent(seatIndex, expected, warn ? seat.OutOfTurnWarnings : 0, penalized));
            }
            else
            {
                _events.Add(new ActionEvent(seatIndex, PlayerAction.Fold(), 0, false));
            }

            Continue();
            return true;
        }

        /// <summary>Cards that are currently wild for a player (wilds can change as cards are rolled and flipped).</summary>
        public WildSet WildsFor(int seatIndex)
        {
            var seat = _seats[seatIndex];
            return EffectiveCall.Resolve(new WildContext(seat.Hole, seat.Up, Board.AllExposed, Settings.WildOptions));
        }

        /// <summary>The two legal ways to use the board.</summary>
        public IReadOnlyList<HandShape> Shapes() => ShapesFor(Board.ExposedTwo, Board.ExposedThree);

        public static IReadOnlyList<HandShape> ShapesFor(IReadOnlyList<Card> twoSide, IReadOnlyList<Card> threeSide) => new[]
        {
            new HandShape("the 2 side", 3, twoSide, 2),
            new HandShape("the 3 side", 2, threeSide, 3),
        };

        /// <summary>Best hand a player can make right now from exposed board cards. Null if none is possible yet.</summary>
        public EvaluatedHand BestHand(int seatIndex)
        {
            var seat = _seats[seatIndex];
            return _finder.BestOfShapes(seat.PersonalCards.ToList(), Shapes(), WildsFor(seatIndex));
        }

        /// <summary>Cards a given player has not seen: the deck plus opponents' hole cards plus unflipped board.</summary>
        public List<Card> UnseenBy(int seatIndex)
        {
            var seen = new HashSet<Card>(Board.AllExposed);
            foreach (var s in _seats)
            {
                foreach (var c in s.Up) seen.Add(c);
                if (s.Index == seatIndex) foreach (var c in s.Hole) seen.Add(c);
            }
            return Card.FullDeck().Where(c => !seen.Contains(c)).ToList();
        }

        private void Continue()
        {
            while (true)
            {
                if (Phase == Game23Phase.HandOver) return;

                if (_seats.Count(s => s.InHand) == 1)
                {
                    AwardUncontested();
                    return;
                }

                if (Phase == Game23Phase.Rolling)
                {
                    if (_rollPending.Count > 0) return;
                    RevealRolls();
                    BeginBetting();
                    continue;
                }

                if (Phase == Game23Phase.Betting)
                {
                    if (!_round.IsComplete)
                    {
                        _events.Add(new TurnEvent(_round.ToAct, _round.Legal(_round.ToAct)));
                        return;
                    }
                    _events.Add(new BettingRoundEndedEvent(PotTotal));
                    _round = null;

                    if (_finalCardDealt)
                    {
                        Showdown();
                        return;
                    }
                    if (_street < Settings.FlipSchedule.Length - 1)
                    {
                        _street++;
                        foreach (var s in DealOrder()) DealDown(s);
                        FlipForStreet(_street);
                        BeginRolling();
                        continue;
                    }
                    if (Settings.FinalDownCard)
                    {
                        _finalCardDealt = true;
                        foreach (var s in DealOrder()) DealDown(s);
                        BeginBetting();
                        continue;
                    }
                    Showdown();
                    return;
                }
                return;
            }
        }

        private void BeginRolling()
        {
            Phase = Game23Phase.Rolling;
            _rollChoices.Clear();
            _rollPending.Clear();
            foreach (var s in _seats)
                if (s.InHand && s.Hole.Count > 0) _rollPending.Add(s.Index);
            _events.Add(new RollRequestedEvent(_rollPending.OrderBy(x => x).ToArray()));
        }

        private void RevealRolls()
        {
            foreach (var s in DealOrder())
            {
                if (!_rollChoices.TryGetValue(s.Index, out var card)) continue;
                s.Hole.Remove(card);
                s.Up.Add(card);
                _events.Add(new CardRolledEvent(s.Index, card));
            }
            _rollChoices.Clear();
        }

        private void BeginBetting()
        {
            Phase = Game23Phase.Betting;
            long unit = Settings.Structure == BetStructure.FixedLimit && Street >= 2 ? Settings.BigBet : Settings.SmallBet;
            int first = NextSeat(DealerSeat, s => s.CanAct);
            _round = new BettingRound(_seats, first < 0 ? 0 : first, Settings.Structure, unit, Settings.MaxRaisesPerRound);
        }

        private void FlipForStreet(int street)
        {
            int count = Settings.FlipSchedule[street];
            for (int i = 0; i < count; i++)
            {
                int slot = Board.FlipNext(2, out var c2);
                _events.Add(new BoardCardFlippedEvent(2, slot, c2));
            }
            for (int i = 0; i < count; i++)
            {
                int slot = Board.FlipNext(3, out var c3);
                _events.Add(new BoardCardFlippedEvent(3, slot, c3));
            }
        }

        private void DealDown(Seat seat)
        {
            var card = _deck.Draw();
            seat.Hole.Add(card);
            _events.Add(new CardDealtEvent(seat.Index, card));
        }

        /// <summary>Seats still in the hand, starting left of the dealer.</summary>
        private IEnumerable<Seat> DealOrder()
        {
            int n = _seats.Count;
            for (int step = 1; step <= n; step++)
            {
                var s = _seats[(DealerSeat + step) % n];
                if (s.InHand) yield return s;
            }
        }

        private int NextSeat(int after, Func<Seat, bool> predicate)
        {
            int n = _seats.Count;
            for (int step = 1; step <= n; step++)
            {
                int idx = ((after + step) % n + n) % n;
                if (predicate(_seats[idx])) return idx;
            }
            return -1;
        }

        private void AwardUncontested()
        {
            var winner = _seats.First(s => s.InHand);
            long pot = PotTotal;
            winner.Stack += pot;
            _events.Add(new PotAwardedEvent(0, pot, new[] { winner.Index }, null));
            EndHand();
        }

        private void Showdown()
        {
            var hands = new Dictionary<int, EvaluatedHand>();
            foreach (var s in DealOrder())
            {
                var wilds = WildsFor(s.Index);
                var hand = _finder.BestOfShapes(s.PersonalCards.ToList(), Shapes(), wilds);
                hands[s.Index] = hand;
                _events.Add(new ShowdownEvent(s.Index, hand, wilds));
            }

            var rules = Settings.HandRules;
            var pots = PotCalculator.Build(_seats);
            for (int p = 0; p < pots.Count; p++)
            {
                var pot = pots[p];
                var contenders = pot.Eligible.Where(hands.ContainsKey).ToList();
                if (contenders.Count == 0) contenders = pot.Eligible.ToList();
                var best = contenders.Select(i => hands[i].Value).Aggregate((a, b) => rules.Compare(a, b) >= 0 ? a : b);
                var winners = contenders.Where(i => rules.Compare(hands[i].Value, best) == 0).ToList();

                // Odd chips go to the first winner left of the dealer.
                var ordered = DealOrder().Select(s => s.Index).Where(winners.Contains).ToList();
                long share = pot.Amount / ordered.Count;
                long odd = pot.Amount - share * ordered.Count;
                foreach (var w in ordered) _seats[w].Stack += share;
                _seats[ordered[0]].Stack += odd;
                _events.Add(new PotAwardedEvent(p, pot.Amount, ordered,
                    contenders.Count > 1 ? hands[ordered[0]].Describe() : null));
            }
            EndHand();
        }

        private void EndHand()
        {
            Phase = Game23Phase.HandOver;
            _round = null;
            _rollPending.Clear();
            _rollChoices.Clear();
            _events.Add(new HandEndedEvent());
        }
    }
}
