using System;
using System.Collections.Generic;
using System.Linq;

namespace WildCardPoker.Core
{
    public enum BetStructure
    {
        NoLimit,
        PotLimit,
        FixedLimit,
    }

    public enum ActionType
    {
        Fold,
        Check,
        Call,
        Bet,
        Raise,
    }

    public readonly struct PlayerAction
    {
        public ActionType Type { get; }

        /// <summary>For Bet/Raise: the total this player's round bet becomes ("raise to").</summary>
        public long Amount { get; }

        public PlayerAction(ActionType type, long amount = 0)
        {
            Type = type;
            Amount = amount;
        }

        public static PlayerAction Fold() => new PlayerAction(ActionType.Fold);
        public static PlayerAction Check() => new PlayerAction(ActionType.Check);
        public static PlayerAction Call() => new PlayerAction(ActionType.Call);
        public static PlayerAction BetTo(long amount) => new PlayerAction(ActionType.Bet, amount);
        public static PlayerAction RaiseTo(long amount) => new PlayerAction(ActionType.Raise, amount);

        public override string ToString() =>
            Type == ActionType.Bet || Type == ActionType.Raise ? $"{Type} to {Amount}" : Type.ToString();
    }

    public sealed class LegalActions
    {
        public int Seat { get; set; }
        public bool CanCheck { get; set; }

        /// <summary>Chips needed to call (already capped at the player's stack).</summary>
        public long ToCall { get; set; }

        public bool CanBetOrRaise { get; set; }

        /// <summary>True when there is no bet yet this round (the aggressive action is a "bet", not a "raise").</summary>
        public bool IsOpeningBet { get; set; }

        public long MinRaiseTo { get; set; }
        public long MaxRaiseTo { get; set; }
        public long CurrentBet { get; set; }
        public long Pot { get; set; }
        public long Stack { get; set; }
        public long RoundBet { get; set; }

        public bool CallIsAllIn => ToCall > 0 && ToCall >= Stack;

        public override string ToString() =>
            CanCheck ? $"check{(CanBetOrRaise ? $" / bet {MinRaiseTo}-{MaxRaiseTo}" : "")}"
                     : $"call {ToCall}{(CanBetOrRaise ? $" / raise {MinRaiseTo}-{MaxRaiseTo}" : "")} / fold";
    }

    /// <summary>One betting round. Supports all-ins, incomplete raises and out-of-turn folds.</summary>
    public sealed class BettingRound
    {
        private readonly IReadOnlyList<Seat> _seats;
        private readonly BetStructure _structure;
        private readonly long _betUnit;
        private readonly int _maxRaises;
        private readonly HashSet<int> _pending = new HashSet<int>();
        private readonly HashSet<int> _actedSinceFullRaise = new HashSet<int>();
        private int _aggressions;

        public long CurrentBet { get; private set; }
        public long LastFullRaise { get; private set; }

        /// <summary>Seat that must act now, or -1 when the round is complete.</summary>
        public int ToAct { get; private set; } = -1;

        public bool IsComplete => ToAct < 0;

        /// <param name="betUnit">Fixed limit: the bet size. No/Pot limit: the minimum opening bet.</param>
        /// <param name="maxRaises">Fixed limit cap on bet + raises (usually 4). Ignored otherwise.</param>
        public BettingRound(IReadOnlyList<Seat> seats, int firstToAct, BetStructure structure, long betUnit, int maxRaises = 4)
        {
            _seats = seats;
            _structure = structure;
            _betUnit = Math.Max(1, betUnit);
            _maxRaises = maxRaises;
            foreach (var s in seats) s.RoundBet = 0;

            foreach (var s in seats)
                if (s.CanAct) _pending.Add(s.Index);

            // Nobody to bet against: skip the round.
            if (_pending.Count < 2) _pending.Clear();
            ToAct = NextPending(firstToAct - 1);
        }

        public long PotTotal => _seats.Sum(s => s.TotalBet);

        public LegalActions Legal(int seatIndex)
        {
            var seat = _seats[seatIndex];
            long toCall = Math.Max(0, CurrentBet - seat.RoundBet);
            var legal = new LegalActions
            {
                Seat = seatIndex,
                ToCall = Math.Min(toCall, seat.Stack),
                CanCheck = toCall == 0,
                IsOpeningBet = CurrentBet == 0,
                CurrentBet = CurrentBet,
                Pot = PotTotal,
                Stack = seat.Stack,
                RoundBet = seat.RoundBet,
            };

            long allInTo = seat.RoundBet + seat.Stack;
            bool othersCanRespond = _seats.Any(s => s.Index != seatIndex && s.CanAct);
            bool reopened = !_actedSinceFullRaise.Contains(seatIndex);
            bool capReached = _structure == BetStructure.FixedLimit && _aggressions >= _maxRaises;
            legal.CanBetOrRaise = seat.CanAct && allInTo > CurrentBet && othersCanRespond && reopened && !capReached;

            if (legal.CanBetOrRaise)
            {
                long increment = CurrentBet == 0 ? _betUnit : Math.Max(LastFullRaise, _betUnit);
                long minTo = CurrentBet + increment;
                long maxTo;
                switch (_structure)
                {
                    case BetStructure.FixedLimit:
                        maxTo = minTo;
                        break;
                    case BetStructure.PotLimit:
                        maxTo = CurrentBet + PotTotal + toCall;
                        break;
                    default:
                        maxTo = allInTo;
                        break;
                }
                legal.MinRaiseTo = Math.Min(minTo, allInTo);
                legal.MaxRaiseTo = Math.Max(legal.MinRaiseTo, Math.Min(maxTo, allInTo));
            }
            return legal;
        }

        /// <summary>Applies an in-turn action. Returns the chips committed by it.</summary>
        public long Apply(int seatIndex, PlayerAction action)
        {
            if (seatIndex != ToAct) throw new InvalidOperationException($"It is not seat {seatIndex}'s turn.");
            var seat = _seats[seatIndex];
            var legal = Legal(seatIndex);
            long committed = 0;

            switch (action.Type)
            {
                case ActionType.Fold:
                    seat.Status = SeatStatus.Folded;
                    break;
                case ActionType.Check:
                    if (!legal.CanCheck) throw new InvalidOperationException("Cannot check facing a bet.");
                    break;
                case ActionType.Call:
                    if (legal.CanCheck) break; // calling nothing is a check
                    committed = seat.Commit(legal.ToCall);
                    break;
                case ActionType.Bet:
                case ActionType.Raise:
                {
                    if (!legal.CanBetOrRaise) throw new InvalidOperationException("Betting or raising is not allowed now.");
                    long to = action.Amount;
                    long allInTo = seat.RoundBet + seat.Stack;
                    if (to < legal.MinRaiseTo || to > legal.MaxRaiseTo)
                        throw new InvalidOperationException($"Raise must be between {legal.MinRaiseTo} and {legal.MaxRaiseTo}.");
                    long increment = to - CurrentBet;
                    bool fullRaise = increment >= (CurrentBet == 0 ? _betUnit : Math.Max(LastFullRaise, _betUnit));
                    committed = seat.Commit(to - seat.RoundBet);
                    if (fullRaise)
                    {
                        LastFullRaise = increment;
                        _actedSinceFullRaise.Clear();
                        _aggressions++;
                    }
                    else if (to < allInTo)
                    {
                        throw new InvalidOperationException("Only an all-in may be smaller than a full raise.");
                    }
                    CurrentBet = to;
                    foreach (var other in _seats)
                        if (other.Index != seatIndex && other.CanAct && other.RoundBet < CurrentBet) _pending.Add(other.Index);
                    break;
                }
            }

            _actedSinceFullRaise.Add(seatIndex);
            _pending.Remove(seatIndex);
            Settle(seatIndex);
            return committed;
        }

        /// <summary>Removes a player who folded out of turn. Does not change whose turn it is.</summary>
        public void RemoveFolded(int seatIndex)
        {
            _pending.Remove(seatIndex);
            if (ToAct == seatIndex) Settle(seatIndex);
            else if (_seats.Count(s => s.InHand) < 2) { _pending.Clear(); ToAct = -1; }
        }

        private void Settle(int fromSeat)
        {
            if (_seats.Count(s => s.InHand) < 2) _pending.Clear();
            // A lone remaining actor who already matched the bet has nothing to do.
            if (_pending.Count == 1)
            {
                var only = _seats[_pending.First()];
                bool othersCanAct = _seats.Any(s => s.Index != only.Index && s.CanAct);
                if (!othersCanAct && only.RoundBet >= CurrentBet) _pending.Clear();
            }
            ToAct = NextPending(fromSeat);
        }

        private int NextPending(int afterSeat)
        {
            if (_pending.Count == 0) return -1;
            int n = _seats.Count;
            for (int step = 1; step <= n; step++)
            {
                int idx = ((afterSeat + step) % n + n) % n;
                if (_pending.Contains(idx)) return idx;
            }
            return -1;
        }
    }

    public sealed class Pot
    {
        public long Amount { get; }
        public IReadOnlyList<int> Eligible { get; }

        public Pot(long amount, IReadOnlyList<int> eligible)
        {
            Amount = amount;
            Eligible = eligible;
        }

        public override string ToString() => $"{Amount} [{string.Join(",", Eligible)}]";
    }

    public static class PotCalculator
    {
        /// <summary>Splits everything committed this hand into a main pot and side pots.</summary>
        public static List<Pot> Build(IReadOnlyList<Seat> seats)
        {
            var levels = seats.Where(s => s.InHand && s.TotalBet > 0).Select(s => s.TotalBet).Distinct().OrderBy(x => x).ToList();
            var pots = new List<Pot>();
            long previous = 0;
            foreach (var level in levels)
            {
                long amount = 0;
                foreach (var s in seats) amount += Math.Max(0, Math.Min(s.TotalBet, level) - previous);
                var eligible = seats.Where(s => s.InHand && s.TotalBet >= level).Select(s => s.Index).ToList();
                if (amount > 0)
                {
                    if (pots.Count > 0 && pots[pots.Count - 1].Eligible.SequenceEqual(eligible))
                        pots[pots.Count - 1] = new Pot(pots[pots.Count - 1].Amount + amount, eligible);
                    else
                        pots.Add(new Pot(amount, eligible));
                }
                previous = level;
            }

            // Dead money above the highest live contribution (from folded players) joins the last pot.
            long leftover = seats.Sum(s => Math.Max(0, s.TotalBet - previous));
            if (leftover > 0)
            {
                if (pots.Count == 0)
                {
                    var live = seats.Where(s => s.InHand).Select(s => s.Index).ToList();
                    pots.Add(new Pot(leftover, live));
                }
                else
                {
                    var last = pots[pots.Count - 1];
                    pots[pots.Count - 1] = new Pot(last.Amount + leftover, last.Eligible);
                }
            }
            return pots;
        }
    }

    /// <summary>Online-style pre-action buttons ("Fold", "Check/Fold", "Call any") that fire when the turn arrives.</summary>
    public enum PreAction
    {
        None,
        Fold,
        CheckFold,
        Check,
        CallAny,
    }

    public static class PreActions
    {
        /// <summary>The action a pre-selected button resolves to, or null if it no longer applies.</summary>
        public static PlayerAction? Resolve(PreAction preAction, LegalActions legal)
        {
            switch (preAction)
            {
                case PreAction.Fold: return PlayerAction.Fold();
                case PreAction.CheckFold: return legal.CanCheck ? PlayerAction.Check() : PlayerAction.Fold();
                case PreAction.Check: return legal.CanCheck ? PlayerAction.Check() : (PlayerAction?)null;
                case PreAction.CallAny: return legal.CanCheck ? PlayerAction.Check() : PlayerAction.Call();
                default: return null;
            }
        }
    }
}
