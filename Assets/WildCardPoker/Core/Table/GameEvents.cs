using System.Collections.Generic;

namespace WildCardPoker.Core
{
    /// <summary>
    /// Everything that happens at the table is published as an event, in order. The Unity layer plays them
    /// back as animations and sounds; bots use them to read the table (timing, out-of-turn folds, rolls).
    /// </summary>
    public abstract class GameEvent
    {
        /// <summary>Plain-language line for the dealer log.</summary>
        public abstract string Narration { get; }

        public override string ToString() => Narration;
    }

    public sealed class HandStartedEvent : GameEvent
    {
        public int HandNumber { get; }
        public int DealerSeat { get; }
        public WildCall Call { get; }

        public HandStartedEvent(int handNumber, int dealerSeat, WildCall call)
        {
            HandNumber = handNumber;
            DealerSeat = dealerSeat;
            Call = call;
        }

        public override string Narration => $"Hand #{HandNumber}. Dealer calls: {Call.Describe()}.";
    }

    public sealed class AntePostedEvent : GameEvent
    {
        public int Seat { get; }
        public long Amount { get; }

        public AntePostedEvent(int seat, long amount)
        {
            Seat = seat;
            Amount = amount;
        }

        public override string Narration => $"Seat {Seat} antes {Amount}.";
    }

    /// <summary>
    /// The dealer lays out the board: two rows of five. Each entry is the card when it is dealt face up,
    /// or null when it is face down (players must not learn hidden cards).
    /// </summary>
    public sealed class BoardSetEvent : GameEvent
    {
        public IReadOnlyList<Card?> Row1 { get; }
        public IReadOnlyList<Card?> Row2 { get; }

        public BoardSetEvent(IReadOnlyList<Card?> row1, IReadOnlyList<Card?> row2)
        {
            Row1 = row1;
            Row2 = row2;
        }

        public override string Narration => "The dealer sets up the board: two rows of five, alternating up and down.";
    }

    public sealed class CardDealtEvent : GameEvent
    {
        public int Seat { get; }
        public Card Card { get; }

        public CardDealtEvent(int seat, Card card)
        {
            Seat = seat;
            Card = card;
        }

        public override string Narration => $"Seat {Seat} is dealt a card.";
    }

    /// <summary>A player may ask the dealer to turn over a board column, or pass.</summary>
    public sealed class TurnOverOfferedEvent : GameEvent
    {
        public int Seat { get; }
        public string Reason { get; }

        public TurnOverOfferedEvent(int seat, string reason)
        {
            Seat = seat;
            Reason = reason;
        }

        public override string Narration => $"Seat {Seat} {Reason} and may turn over a card.";
    }

    /// <summary>The dealer turned a column over: one card came up, the other went down.</summary>
    public sealed class ColumnTurnedEvent : GameEvent
    {
        public int Seat { get; }

        /// <summary>0-based column.</summary>
        public int Column { get; }

        /// <summary>The card now face up in this column.</summary>
        public Card Revealed { get; }

        /// <summary>Row (1 or 2) of the revealed card. The other row's card is now face down.</summary>
        public int RevealedRow { get; }

        public ColumnTurnedEvent(int seat, int column, Card revealed, int revealedRow)
        {
            Seat = seat;
            Column = column;
            Revealed = revealed;
            RevealedRow = revealedRow;
        }

        public override string Narration =>
            $"Seat {Seat} turns over column {Column + 1}: {Revealed.ToPrettyString()} is up in row {RevealedRow}.";
    }

    public sealed class TurnOverPassedEvent : GameEvent
    {
        public int Seat { get; }
        public TurnOverPassedEvent(int seat) { Seat = seat; }
        public override string Narration => $"Seat {Seat} passes on the turn-over.";
    }

    public sealed class RollRequestedEvent : GameEvent
    {
        public IReadOnlyList<int> Seats { get; }
        public RollRequestedEvent(IReadOnlyList<int> seats) { Seats = seats; }
        public override string Narration =>
            Seats.Count == 1 ? $"Seat {Seats[0]} to roll." : "Players, choose a card to roll.";
    }

    public sealed class CardRolledEvent : GameEvent
    {
        public int Seat { get; }
        public Card Card { get; }

        public CardRolledEvent(int seat, Card card)
        {
            Seat = seat;
            Card = card;
        }

        public override string Narration => $"Seat {Seat} rolls {Card.ToPrettyString()}.";
    }

    public sealed class TurnEvent : GameEvent
    {
        public int Seat { get; }
        public LegalActions Legal { get; }

        public TurnEvent(int seat, LegalActions legal)
        {
            Seat = seat;
            Legal = legal;
        }

        public override string Narration => $"Action is on seat {Seat}: {Legal}.";
    }

    public sealed class ActionEvent : GameEvent
    {
        public int Seat { get; }
        public PlayerAction Action { get; }
        public long Committed { get; }
        public bool AllIn { get; }

        public ActionEvent(int seat, PlayerAction action, long committed, bool allIn)
        {
            Seat = seat;
            Action = action;
            Committed = committed;
            AllIn = allIn;
        }

        public override string Narration
        {
            get
            {
                string text;
                switch (Action.Type)
                {
                    case ActionType.Fold: text = "folds"; break;
                    case ActionType.Check: text = "checks"; break;
                    case ActionType.Call: text = $"calls {Committed}"; break;
                    case ActionType.Bet: text = $"bets {Action.Amount}"; break;
                    default: text = $"raises to {Action.Amount}"; break;
                }
                return $"Seat {Seat} {text}{(AllIn ? " and is all-in" : "")}.";
            }
        }
    }

    public sealed class OutOfTurnFoldEvent : GameEvent
    {
        public int Seat { get; }
        public int ExpectedSeat { get; }
        public int Warnings { get; }
        public bool Penalized { get; }

        public OutOfTurnFoldEvent(int seat, int expectedSeat, int warnings, bool penalized)
        {
            Seat = seat;
            ExpectedSeat = expectedSeat;
            Warnings = warnings;
            Penalized = penalized;
        }

        public override string Narration
        {
            get
            {
                var line = $"Seat {Seat} folds out of turn";
                if (ExpectedSeat >= 0) line += $" (action was on seat {ExpectedSeat})";
                if (Penalized) return line + ". Penalty: sits out the next hand.";
                return Warnings > 0 ? line + $". Dealer: \"Please wait for your turn.\" (warning {Warnings})" : line + ".";
            }
        }
    }

    public sealed class OutOfTurnRejectedEvent : GameEvent
    {
        public int Seat { get; }
        public OutOfTurnRejectedEvent(int seat) { Seat = seat; }
        public override string Narration => $"Seat {Seat} tried to fold out of turn. Dealer: \"Hold on, it's not your turn.\"";
    }

    public sealed class BettingRoundEndedEvent : GameEvent
    {
        public long Pot { get; }
        public BettingRoundEndedEvent(long pot) { Pot = pot; }
        public override string Narration => $"Betting is complete. Pot: {Pot}.";
    }

    public sealed class ShowdownEvent : GameEvent
    {
        public int Seat { get; }
        public EvaluatedHand Hand { get; }
        public WildSet Wilds { get; }

        public ShowdownEvent(int seat, EvaluatedHand hand, WildSet wilds)
        {
            Seat = seat;
            Hand = hand;
            Wilds = wilds;
        }

        public override string Narration => $"Seat {Seat} shows {Hand}.";
    }

    public sealed class PotAwardedEvent : GameEvent
    {
        public int PotIndex { get; }
        public long Amount { get; }
        public IReadOnlyList<int> Winners { get; }
        public string HandDescription { get; }

        public PotAwardedEvent(int potIndex, long amount, IReadOnlyList<int> winners, string handDescription)
        {
            PotIndex = potIndex;
            Amount = amount;
            Winners = winners;
            HandDescription = handDescription;
        }

        public override string Narration
        {
            get
            {
                var pot = PotIndex == 0 ? "the main pot" : $"side pot {PotIndex}";
                var who = Winners.Count == 1 ? $"Seat {Winners[0]} wins" : $"Seats {string.Join(" & ", Winners)} split";
                return $"{who} {pot} ({Amount}){(string.IsNullOrEmpty(HandDescription) ? "" : " with " + HandDescription)}.";
            }
        }
    }

    public sealed class HandEndedEvent : GameEvent
    {
        public override string Narration => "The hand is over.";
    }
}
