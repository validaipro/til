using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WildCardPoker.Core;

namespace WildCardPoker.Tests
{
    public class BettingTests
    {
        private static List<Seat> Seats(params long[] stacks)
        {
            var seats = stacks.Select((s, i) => new Seat(i, "P" + i, s)).ToList();
            foreach (var s in seats) s.Status = SeatStatus.Active;
            return seats;
        }

        [Test]
        public void NoLimitMinRaiseIsLastRaiseSize()
        {
            var seats = Seats(1000, 1000, 1000);
            var round = new BettingRound(seats, 0, BetStructure.NoLimit, 10);
            round.Apply(0, PlayerAction.BetTo(50));
            var legal = round.Legal(1);
            Assert.AreEqual(50, legal.ToCall);
            Assert.AreEqual(100, legal.MinRaiseTo);
            Assert.AreEqual(1000, legal.MaxRaiseTo);
        }

        [Test]
        public void ShortAllInDoesNotReopenBetting()
        {
            var seats = Seats(1000, 70, 1000);
            var round = new BettingRound(seats, 0, BetStructure.NoLimit, 10);
            round.Apply(0, PlayerAction.BetTo(50));
            round.Apply(1, PlayerAction.RaiseTo(70)); // all-in, only +20: not a full raise
            round.Apply(2, PlayerAction.Call());
            Assert.AreEqual(0, round.ToAct);
            var legal = round.Legal(0);
            Assert.AreEqual(20, legal.ToCall);
            Assert.IsFalse(legal.CanBetOrRaise, "The original bettor may only call or fold.");
        }

        [Test]
        public void PotLimitMaximum()
        {
            var seats = Seats(1000, 1000, 1000);
            foreach (var s in seats) s.Commit(10); // antes: pot 30
            var round = new BettingRound(seats, 0, BetStructure.PotLimit, 10);
            Assert.AreEqual(30, round.Legal(0).MaxRaiseTo);
            round.Apply(0, PlayerAction.BetTo(30)); // pot now 60
            // Call 30 makes pot 90, raise by 90 more: to 120.
            Assert.AreEqual(120, round.Legal(1).MaxRaiseTo);
        }

        [Test]
        public void FixedLimitCapsAtFourBets()
        {
            var seats = Seats(1000, 1000);
            var round = new BettingRound(seats, 0, BetStructure.FixedLimit, 10, maxRaises: 4);
            round.Apply(0, PlayerAction.BetTo(10));
            round.Apply(1, PlayerAction.RaiseTo(20));
            round.Apply(0, PlayerAction.RaiseTo(30));
            round.Apply(1, PlayerAction.RaiseTo(40));
            Assert.IsFalse(round.Legal(0).CanBetOrRaise);
            round.Apply(0, PlayerAction.Call());
            Assert.IsTrue(round.IsComplete);
        }

        [Test]
        public void RoundCompletesWhenAllCheck()
        {
            var seats = Seats(100, 100, 100);
            var round = new BettingRound(seats, 1, BetStructure.NoLimit, 10);
            Assert.AreEqual(1, round.ToAct);
            round.Apply(1, PlayerAction.Check());
            round.Apply(2, PlayerAction.Check());
            round.Apply(0, PlayerAction.Check());
            Assert.IsTrue(round.IsComplete);
        }

        [Test]
        public void OutOfTurnFoldIsSkipped()
        {
            var seats = Seats(100, 100, 100);
            var round = new BettingRound(seats, 0, BetStructure.NoLimit, 10);
            round.Apply(0, PlayerAction.BetTo(20));
            seats[2].Status = SeatStatus.Folded;
            round.RemoveFolded(2);
            Assert.AreEqual(1, round.ToAct);
            round.Apply(1, PlayerAction.Call());
            Assert.IsTrue(round.IsComplete);
        }

        [Test]
        public void SidePots()
        {
            var seats = Seats(0, 0, 0, 0);
            seats[0].TotalBet = 50; seats[0].Status = SeatStatus.AllIn;
            seats[1].TotalBet = 200; seats[1].Status = SeatStatus.Active;
            seats[2].TotalBet = 200; seats[2].Status = SeatStatus.Active;
            seats[3].TotalBet = 30; seats[3].Status = SeatStatus.Folded;
            var pots = PotCalculator.Build(seats);
            Assert.AreEqual(2, pots.Count);
            Assert.AreEqual(180, pots[0].Amount); // 50*3 + 30 dead
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, pots[0].Eligible);
            Assert.AreEqual(300, pots[1].Amount);
            CollectionAssert.AreEqual(new[] { 1, 2 }, pots[1].Eligible);
            Assert.AreEqual(480, pots.Sum(p => p.Amount));
        }

        [Test]
        public void PreActionButtonsResolveOnTurn()
        {
            var facingBet = new LegalActions { ToCall = 20, CanCheck = false };
            var free = new LegalActions { ToCall = 0, CanCheck = true };
            Assert.AreEqual(ActionType.Fold, PreActions.Resolve(PreAction.CheckFold, facingBet)?.Type);
            Assert.AreEqual(ActionType.Check, PreActions.Resolve(PreAction.CheckFold, free)?.Type);
            Assert.AreEqual(ActionType.Call, PreActions.Resolve(PreAction.CallAny, facingBet)?.Type);
            Assert.IsNull(PreActions.Resolve(PreAction.Check, facingBet));
        }
    }
}
