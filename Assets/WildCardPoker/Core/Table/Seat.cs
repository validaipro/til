using System.Collections.Generic;

namespace WildCardPoker.Core
{
    public enum SeatStatus
    {
        /// <summary>Not dealt in this hand (no chips, or sitting out as a penalty).</summary>
        SittingOut,
        Active,
        Folded,
        AllIn,
    }

    public sealed class Seat
    {
        public int Index { get; }
        public string Name { get; set; }
        public bool IsHuman { get; set; }
        public long Stack { get; set; }
        public SeatStatus Status { get; set; } = SeatStatus.SittingOut;

        /// <summary>Face-down cards ("in the hole").</summary>
        public List<Card> Hole { get; } = new List<Card>();

        /// <summary>Face-up cards the player has rolled.</summary>
        public List<Card> Up { get; } = new List<Card>();

        /// <summary>Chips committed in the current betting round.</summary>
        public long RoundBet { get; set; }

        /// <summary>Chips committed during the whole hand (antes included).</summary>
        public long TotalBet { get; set; }

        /// <summary>How many times this player has folded out of turn (table etiquette tracking).</summary>
        public int OutOfTurnWarnings { get; set; }

        /// <summary>Penalty: the player will be skipped for the next hand.</summary>
        public bool SitOutNextHand { get; set; }

        public Seat(int index, string name, long stack, bool isHuman = false)
        {
            Index = index;
            Name = name;
            Stack = stack;
            IsHuman = isHuman;
        }

        /// <summary>Still contesting the pot.</summary>
        public bool InHand => Status == SeatStatus.Active || Status == SeatStatus.AllIn;

        /// <summary>Can still make betting decisions.</summary>
        public bool CanAct => Status == SeatStatus.Active;

        public IEnumerable<Card> PersonalCards
        {
            get
            {
                foreach (var c in Hole) yield return c;
                foreach (var c in Up) yield return c;
            }
        }

        /// <summary>Moves chips from the stack into the pot. Returns the amount actually committed.</summary>
        public long Commit(long amount)
        {
            if (amount > Stack) amount = Stack;
            Stack -= amount;
            RoundBet += amount;
            TotalBet += amount;
            if (Stack == 0 && Status == SeatStatus.Active) Status = SeatStatus.AllIn;
            return amount;
        }

        public void ResetForHand()
        {
            Hole.Clear();
            Up.Clear();
            RoundBet = 0;
            TotalBet = 0;
            Status = SeatStatus.SittingOut;
        }

        public override string ToString() => $"{Name} ({Stack})";
    }
}
