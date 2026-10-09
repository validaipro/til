using System;
using System.Collections.Generic;
using System.Linq;

namespace WildCardPoker.Core.DiamondTurnOver
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

    public enum Phase
    {
        NotStarted,
        Rolling,
        TurnOver,
        Betting,
        HandOver,
    }

    public sealed class DiamondSettings
    {
        public long Ante = 5;
        public BetStructure Structure = BetStructure.FixedLimit;

        /// <summary>Fixed limit: bet size on the early rounds. No/Pot limit: minimum opening bet.</summary>
        public long SmallBet = 10;

        /// <summary>Fixed limit: bet size from the third betting round on.</summary>
        public long BigBet = 20;

        public int MaxRaisesPerRound = 4;

        public int InitialHoleCards = 3;

        /// <summary>Rolls per hand. Each player ends with this many cards face up.</summary>
        public int Rolls = 4;

        /// <summary>Seven-card game: the last card stays down.</summary>
        public bool FinalDownCard = true;

        /// <summary>At most this many board cards (from one row) go into a hand; at least one is mandatory.</summary>
        public int MaxBoardCards = 4;

        /// <summary>The game's wild card: lowest diamond in the hole and all like it, for each player.</summary>
        public WildCall Wilds = new WildCall("Lowest diamond in the hole and all like it",
            new PickedCardWildClause(Extreme.Lowest, Suit.Diamonds, CardLocation.Hole, allLikeIt: true));

        /// <summary>Aces high or low, player's discretion: for straights and for picking the lowest diamond.</summary>
        public bool AcesEitherWay = true;

        public HandRules HandRules = HandRules.Default;

        public OutOfTurnFoldPolicy OutOfTurnFolds = OutOfTurnFoldPolicy.AllowedWithWarning;
        public int WarningsBeforePenalty = 3;

        public static DiamondSettings Default => new DiamondSettings();

        public int CardsPerPlayer => InitialHoleCards + Rolls - 1 + (FinalDownCard ? 1 : 0);

        /// <summary>10 board cards plus each player's cards must fit in one 52-card deck.</summary>
        public int MaxPlayers => (52 - DiamondBoard.Columns * DiamondBoard.Rows) / CardsPerPlayer;
    }

    /// <summary>What the engine is waiting for.</summary>
    public sealed class Pending
    {
        public Phase Phase { get; }

        /// <summary>The seat that must act now (roll, turn over or bet), or -1.</summary>
        public int Seat { get; }

        /// <summary>Betting options when <see cref="Phase"/> is Betting.</summary>
        public LegalActions Legal { get; }

        public Pending(Phase phase, int seat, LegalActions legal)
        {
            Phase = phase;
            Seat = seat;
            Legal = legal;
        }
    }

    /// <summary>
    /// Rules engine for <b>Diamond Turn-over</b>, a seven-card game.
    ///
    /// The dealer lays out two rows of five (row 1 up/down/up/down/up, row 2 the opposite) and deals three cards
    /// to each player. Then, four times: every player rolls one hole card face up (one by one from the dealer's
    /// left); the player with the highest hand showing may have the dealer turn over a board column, or pass;
    /// a betting round follows; the dealer deals one more card. The last card stays down. Then every player, from
    /// the highest hand showing down to the lowest, may turn over a column (even one already turned, undoing it),
    /// a final betting round is played, and the best hand wins.
    ///
    /// A hand uses 1-4 face-up cards from ONE row of the board (at least one is mandatory) plus the player's own
    /// cards. The lowest diamond in each player's hole, and all cards like it, are wild for that player.
    /// Aces are high or low, whichever is better for the player. Folding is accepted at any moment.
    /// </summary>
    public sealed class DiamondTurnOverEngine
    {
        private readonly List<Seat> _seats;
        private readonly IRandomSource _random;
        private readonly List<GameEvent> _events = new List<GameEvent>();
        private readonly List<int> _rollQueue = new List<int>();
        private readonly List<int> _turnOverQueue = new List<int>();
        private readonly BestHandFinder _finder;

        private Deck _deck;
        private BettingRound _round;
        private int _roll;
        private bool _finalCardDealt;

        public DiamondSettings Settings { get; }
        public IReadOnlyList<Seat> Seats => _seats;
        public DiamondBoard Board { get; } = new DiamondBoard();
        public Phase Phase { get; private set; } = Phase.NotStarted;
        public int DealerSeat { get; private set; } = -1;
        public int HandNumber { get; private set; }
        public BestHandFinder Finder => _finder;

        /// <summary>Betting round index: 0-3 after each roll, 4 for the final round.</summary>
        public int BettingRoundIndex => _finalCardDealt ? Settings.Rolls : _roll;

        /// <summary>True during the last turn-over round, where every player gets a chance.</summary>
        public bool IsFinalTurnOverRound => _finalCardDealt;

        public DiamondTurnOverEngine(DiamondSettings settings, IEnumerable<Seat> seats, IRandomSource random)
        {
            Settings = settings ?? DiamondSettings.Default;
            _seats = seats.ToList();
            for (int i = 0; i < _seats.Count; i++)
                if (_seats[i].Index != i) throw new ArgumentException("Seat indexes must be 0..n-1 in order.");
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _finder = new BestHandFinder(new HandEvaluator(Settings.HandRules));
        }

        public long PotTotal => _seats.Sum(s => s.TotalBet);
        public bool HasEvents => _events.Count > 0;

        public int NextDealerSeat() => NextSeat(DealerSeat, s => s.Stack > 0 && !s.SitOutNextHand);

        public IReadOnlyList<GameEvent> DrainEvents()
        {
            var copy = _events.ToArray();
            _events.Clear();
            return copy;
        }

        /// <summary>The seat whose turn it is in the current phase, or -1.</summary>
        public int SeatToAct
        {
            get
            {
                switch (Phase)
                {
                    case Phase.Rolling: return _rollQueue.Count > 0 ? _rollQueue[0] : -1;
                    case Phase.TurnOver: return _turnOverQueue.Count > 0 ? _turnOverQueue[0] : -1;
                    case Phase.Betting: return _round?.ToAct ?? -1;
                    default: return -1;
                }
            }
        }

        public Pending Pending
        {
            get
            {
                int seat = SeatToAct;
                var legal = Phase == Phase.Betting && seat >= 0 ? _round.Legal(seat) : null;
                return new Pending(Phase, seat, legal);
            }
        }

        public void StartHand(Deck presetDeck = null)
        {
            if (Phase != Phase.NotStarted && Phase != Phase.HandOver)
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
                throw new InvalidOperationException($"Diamond Turn-over supports at most {Settings.MaxPlayers} players with one deck.");

            HandNumber++;
            DealerSeat = NextSeat(DealerSeat, s => s.Status == SeatStatus.Active);
            _roll = 0;
            _finalCardDealt = false;
            _round = null;
            _rollQueue.Clear();
            _turnOverQueue.Clear();

            _deck = presetDeck ?? new Deck();
            if (presetDeck == null) _deck.Shuffle(_random);

            _events.Add(new HandStartedEvent(HandNumber, DealerSeat, Settings.Wilds));
            foreach (var s in DealOrder())
            {
                long ante = s.Commit(Math.Min(Settings.Ante, s.Stack));
                if (ante > 0) _events.Add(new AntePostedEvent(s.Index, ante));
            }

            Board.SetUp(_deck);
            _events.Add(new BoardSetEvent(VisibleRow(1), VisibleRow(2)));

            for (int i = 0; i < Settings.InitialHoleCards; i++)
                foreach (var s in DealOrder()) DealDown(s);

            BeginRolling();
            Continue();
        }

        /// <summary>The player whose turn it is rolls one of their hole cards face up.</summary>
        public void Roll(int seatIndex, Card card)
        {
            if (Phase != Phase.Rolling) throw new InvalidOperationException("Not in the rolling phase.");
            if (SeatToAct != seatIndex) throw new InvalidOperationException($"It is seat {SeatToAct}'s turn to roll, not seat {seatIndex}'s.");
            var seat = _seats[seatIndex];
            if (!seat.Hole.Contains(card)) throw new InvalidOperationException($"{card} is not in seat {seatIndex}'s hole.");
            _rollQueue.RemoveAt(0);
            seat.Hole.Remove(card);
            seat.Up.Add(card);
            _events.Add(new CardRolledEvent(seatIndex, card));
            if (_rollQueue.Count > 0) _events.Add(new RollRequestedEvent(new[] { _rollQueue[0] }));
            Continue();
        }

        /// <summary>
        /// The player offered a turn-over picks a board column (0..4) for the dealer to turn over,
        /// or null to pass. Pointing at either card of a column turns the whole column over.
        /// </summary>
        public void TurnOver(int seatIndex, int? column)
        {
            if (Phase != Phase.TurnOver) throw new InvalidOperationException("No turn-over is being offered.");
            if (SeatToAct != seatIndex) throw new InvalidOperationException($"It is seat {SeatToAct}'s turn-over, not seat {seatIndex}'s.");
            _turnOverQueue.RemoveAt(0);
            if (column.HasValue)
            {
                var revealed = Board.TurnOver(column.Value);
                Board.Showing(column.Value, out int row);
                _events.Add(new ColumnTurnedEvent(seatIndex, column.Value, revealed, row));
            }
            else
            {
                _events.Add(new TurnOverPassedEvent(seatIndex));
            }
            OfferNextTurnOver();
            Continue();
        }

        /// <summary>An in-turn betting action.</summary>
        public void Act(int seatIndex, PlayerAction action)
        {
            if (Phase != Phase.Betting || _round == null) throw new InvalidOperationException("Not in a betting round.");
            var seat = _seats[seatIndex];
            long committed = _round.Apply(seatIndex, action);
            _events.Add(new ActionEvent(seatIndex, action, committed, seat.Status == SeatStatus.AllIn));
            Continue();
        }

        /// <summary>
        /// Folds a player at any time. Folding while someone else must roll, turn over or bet is out of turn and
        /// handled by <see cref="DiamondSettings.OutOfTurnFolds"/>. Returns false if the fold was refused.
        /// </summary>
        public bool Fold(int seatIndex)
        {
            if (Phase == Phase.NotStarted || Phase == Phase.HandOver) return false;
            var seat = _seats[seatIndex];
            if (!seat.InHand) return false;

            if (Phase == Phase.Betting && _round != null && _round.ToAct == seatIndex)
            {
                Act(seatIndex, PlayerAction.Fold());
                return true;
            }

            int expected = SeatToAct;
            bool outOfTurn = expected >= 0 && expected != seatIndex;
            if (outOfTurn && Settings.OutOfTurnFolds == OutOfTurnFoldPolicy.NotAllowed)
            {
                _events.Add(new OutOfTurnRejectedEvent(seatIndex));
                return false;
            }

            bool hadTheTurn = expected == seatIndex;
            seat.Status = SeatStatus.Folded;
            _rollQueue.Remove(seatIndex);
            _turnOverQueue.Remove(seatIndex);
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

            if (hadTheTurn && _seats.Count(s => s.InHand) > 1)
            {
                if (Phase == Phase.Rolling && _rollQueue.Count > 0) _events.Add(new RollRequestedEvent(new[] { _rollQueue[0] }));
                if (Phase == Phase.TurnOver) OfferNextTurnOver();
            }
            Continue();
            return true;
        }

        // ------------------------------------------------------------------ hands and wilds

        /// <summary>Cards wild for a player (lowest diamond in the hole and all like it).</summary>
        public WildSet WildsFor(int seatIndex, bool aceLow = false)
        {
            var seat = _seats[seatIndex];
            var options = new WildCallOptions { AceLowWhenPicking = aceLow };
            return Settings.Wilds.Resolve(new WildContext(seat.Hole, seat.Up, Board.AllFaceUp, options));
        }

        /// <summary>Legal ways to use the board right now: 1..4 face-up cards from row 1 or from row 2.</summary>
        public IReadOnlyList<HandShape> Shapes() => ShapesFor(Board.FaceUpInRow(1), Board.FaceUpInRow(2), Settings.MaxBoardCards);

        public static IReadOnlyList<HandShape> ShapesFor(IReadOnlyList<Card> row1, IReadOnlyList<Card> row2, int maxBoardCards = 4)
        {
            var shapes = new List<HandShape>();
            AddRow("row 1", row1);
            AddRow("row 2", row2);
            return shapes;

            void AddRow(string name, IReadOnlyList<Card> row)
            {
                for (int k = 1; k <= Math.Min(maxBoardCards, row.Count); k++)
                    shapes.Add(new HandShape(name, 5 - k, row, k));
            }
        }

        /// <summary>A player's best hand with the board as it is now. Aces count high or low, whichever is better.</summary>
        public EvaluatedHand BestHand(int seatIndex)
        {
            var seat = _seats[seatIndex];
            return BestHandFor(seat.Hole, seat.Up, Board.FaceUpInRow(1), Board.FaceUpInRow(2));
        }

        /// <summary>
        /// Best hand from given cards: 1..4 face-up cards from one row plus own cards, with the lowest diamond in the
        /// hole and all like it wild. With aces either way, an ace may also count as the LOWEST diamond.
        /// </summary>
        public EvaluatedHand BestHandFor(IReadOnlyList<Card> hole, IReadOnlyList<Card> up, IReadOnlyList<Card> row1, IReadOnlyList<Card> row2)
        {
            var personal = hole.Concat(up).ToList();
            var shapes = ShapesFor(row1, row2, Settings.MaxBoardCards);
            var board = row1.Concat(row2).ToList();
            EvaluatedHand best = null;
            foreach (bool aceLow in Settings.AcesEitherWay ? new[] { false, true } : new[] { false })
            {
                var options = new WildCallOptions { AceLowWhenPicking = aceLow };
                var wilds = Settings.Wilds.Resolve(new WildContext(hole, up, board, options));
                var hand = _finder.BestOfShapes(personal, shapes, wilds);
                if (hand != null && (best == null || Settings.HandRules.Compare(hand.Value, best.Value) > 0)) best = hand;
            }
            return best;
        }

        /// <summary>Score of the face-up cards in front of a player (who has the highest hand showing).</summary>
        public int ShowingScore(int seatIndex) => ShowingHand.Score(_seats[seatIndex].Up);

        /// <summary>Players still in, from the highest hand showing to the lowest; ties go to the dealer's left first.</summary>
        public List<int> RankByShowingHand() => DealOrder()
            .Select((s, order) => (s.Index, score: ShowingHand.Score(s.Up), order))
            .OrderByDescending(x => x.score).ThenBy(x => x.order)
            .Select(x => x.Index).ToList();

        /// <summary>The seat with the highest hand showing, or -1 when nobody has rolled yet.</summary>
        public int HighestShowingSeat()
        {
            var ranked = RankByShowingHand();
            return ranked.Count > 0 && _seats[ranked[0]].Up.Count > 0 ? ranked[0] : -1;
        }

        /// <summary>Cards a player has not seen: the deck, other players' hole cards and the face-down board cards.</summary>
        public List<Card> UnseenBy(int seatIndex)
        {
            var seen = new HashSet<Card>(Board.AllFaceUp);
            foreach (var s in _seats)
            {
                foreach (var c in s.Up) seen.Add(c);
                if (s.Index == seatIndex) foreach (var c in s.Hole) seen.Add(c);
            }
            return Card.FullDeck().Where(c => !seen.Contains(c)).ToList();
        }

        // ------------------------------------------------------------------ flow

        private void Continue()
        {
            while (true)
            {
                if (Phase == Phase.HandOver) return;
                if (_seats.Count(s => s.InHand) == 1)
                {
                    AwardUncontested();
                    return;
                }

                switch (Phase)
                {
                    case Phase.Rolling:
                        if (_rollQueue.Count > 0) return;
                        BeginTurnOvers(everyone: false);
                        continue;

                    case Phase.TurnOver:
                        if (_turnOverQueue.Count > 0) return;
                        BeginBetting();
                        continue;

                    case Phase.Betting:
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
                        if (_roll < Settings.Rolls - 1)
                        {
                            _roll++;
                            foreach (var s in DealOrder()) DealDown(s);
                            BeginRolling();
                            continue;
                        }
                        // The last card stays down; then everyone gets a turn-over chance.
                        _finalCardDealt = true;
                        if (Settings.FinalDownCard) foreach (var s in DealOrder()) DealDown(s);
                        BeginTurnOvers(everyone: true);
                        continue;

                    default:
                        return;
                }
            }
        }

        private void BeginRolling()
        {
            Phase = Phase.Rolling;
            _rollQueue.Clear();
            foreach (var s in DealOrder())
                if (s.Hole.Count > 0) _rollQueue.Add(s.Index);
            if (_rollQueue.Count > 0) _events.Add(new RollRequestedEvent(new[] { _rollQueue[0] }));
        }

        /// <summary>
        /// After a roll: only the highest hand showing gets a turn-over. In the final round: every player,
        /// from the highest hand showing down to the lowest. Ties go to the player closest to the dealer's left.
        /// </summary>
        private void BeginTurnOvers(bool everyone)
        {
            Phase = Phase.TurnOver;
            _turnOverQueue.Clear();
            var ranked = RankByShowingHand();
            if (ranked.Count == 0) return;
            if (everyone) _turnOverQueue.AddRange(ranked);
            else _turnOverQueue.Add(ranked[0]);
            OfferNextTurnOver();
        }

        private void OfferNextTurnOver()
        {
            if (_turnOverQueue.Count == 0) return;
            int seat = _turnOverQueue[0];
            string reason = IsFinalTurnOverRound
                ? "has " + ShowingHand.Describe(_seats[seat].Up) + " showing"
                : "has the highest hand showing (" + ShowingHand.Describe(_seats[seat].Up) + ")";
            _events.Add(new TurnOverOfferedEvent(seat, reason));
        }

        private void BeginBetting()
        {
            Phase = Phase.Betting;
            long unit = Settings.Structure == BetStructure.FixedLimit && BettingRoundIndex >= 2 ? Settings.BigBet : Settings.SmallBet;
            int first = NextSeat(DealerSeat, s => s.CanAct);
            _round = new BettingRound(_seats, first < 0 ? 0 : first, Settings.Structure, unit, Settings.MaxRaisesPerRound);
        }

        private void DealDown(Seat seat)
        {
            var card = _deck.Draw();
            seat.Hole.Add(card);
            _events.Add(new CardDealtEvent(seat.Index, card));
        }

        private Card?[] VisibleRow(int row)
        {
            var cards = new Card?[DiamondBoard.Columns];
            for (int c = 0; c < DiamondBoard.Columns; c++)
                cards[c] = Board.IsFaceUp(row, c) ? Board.CardAt(row, c) : (Card?)null;
            return cards;
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
                var hand = BestHand(s.Index);
                hands[s.Index] = hand;
                _events.Add(new ShowdownEvent(s.Index, hand, WildsFor(s.Index)));
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
                _events.Add(new PotAwardedEvent(p, pot.Amount, ordered, contenders.Count > 1 ? hands[ordered[0]].Describe() : null));
            }
            EndHand();
        }

        private void EndHand()
        {
            Phase = Phase.HandOver;
            _round = null;
            _rollQueue.Clear();
            _turnOverQueue.Clear();
            _events.Add(new HandEndedEvent());
        }
    }
}
