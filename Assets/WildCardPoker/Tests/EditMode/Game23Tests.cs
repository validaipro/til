using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WildCardPoker.Core;
using WildCardPoker.Core.AI;
using WildCardPoker.Core.Game23;

namespace WildCardPoker.Tests
{
    public class Game23Tests
    {
        private static List<Seat> Seats(int count, long stack = 1000) =>
            Enumerable.Range(0, count).Select(i => new Seat(i, "P" + i, stack)).ToList();

        /// <summary>Plays the engine forward: everyone rolls their first hole card and checks/calls.</summary>
        private static void PlayPassively(Game23Engine engine)
        {
            int guard = 0;
            while (engine.Phase != Game23Phase.HandOver && guard++ < 500)
            {
                var pending = engine.Pending;
                if (pending.Phase == Game23Phase.Rolling)
                {
                    foreach (var seat in pending.RollSeats.ToList())
                        engine.SubmitRoll(seat, engine.Seats[seat].Hole[0]);
                }
                else if (pending.Phase == Game23Phase.Betting && pending.ToAct >= 0)
                {
                    engine.Act(pending.ToAct, pending.Legal.CanCheck ? PlayerAction.Check() : PlayerAction.Call());
                }
            }
            Assert.AreEqual(Game23Phase.HandOver, engine.Phase);
        }

        [Test]
        public void FullHandDealsFourUpThreeDownAndExposesTheBoard()
        {
            var seats = Seats(5);
            var engine = new Game23Engine(Game23Settings.Default, seats, new SeededRandom(7));
            engine.StartHand(WildCallPresets.MatchingLow());

            int checkedRolls = 0;
            int guard = 0;
            while (engine.Phase != Game23Phase.HandOver && guard++ < 500)
            {
                var pending = engine.Pending;
                if (pending.Phase == Game23Phase.Rolling)
                {
                    // Before each roll, each player holds 3 cards in the hole.
                    foreach (var s in pending.RollSeats) Assert.AreEqual(3, engine.Seats[s].Hole.Count);
                    checkedRolls++;
                    foreach (var s in pending.RollSeats.ToList()) engine.SubmitRoll(s, engine.Seats[s].Hole.Last());
                }
                else if (pending.ToAct >= 0)
                {
                    engine.Act(pending.ToAct, pending.Legal.CanCheck ? PlayerAction.Check() : PlayerAction.Call());
                }
            }

            Assert.AreEqual(4, checkedRolls);
            Assert.AreEqual(7, engine.Board.FlippedTwo);
            Assert.AreEqual(7, engine.Board.FlippedThree);
            foreach (var s in seats)
            {
                Assert.AreEqual(4, s.Up.Count, "four rolled cards");
                Assert.AreEqual(3, s.Hole.Count, "three cards in the hole");
            }
            Assert.AreEqual(5 * 1000, seats.Sum(s => s.Stack), "chips are conserved");
        }

        [Test]
        public void FlipScheduleIsTwoTwoTwoOne()
        {
            var engine = new Game23Engine(Game23Settings.Default, Seats(2), new SeededRandom(3));
            engine.StartHand(WildCall.None);
            var flips = new List<int>();
            int guard = 0;
            while (engine.Phase != Game23Phase.HandOver && guard++ < 200)
            {
                var pending = engine.Pending;
                if (pending.Phase == Game23Phase.Rolling)
                {
                    flips.Add(engine.Board.FlippedTwo);
                    Assert.AreEqual(engine.Board.FlippedTwo, engine.Board.FlippedThree);
                    foreach (var s in pending.RollSeats.ToList()) engine.SubmitRoll(s, engine.Seats[s].Hole[0]);
                }
                else engine.Act(pending.ToAct, PlayerAction.Check());
            }
            CollectionAssert.AreEqual(new[] { 2, 4, 6, 7 }, flips);
        }

        [Test]
        public void HandMustUseTwoFromTheTwoSideOrThreeFromTheThreeSide()
        {
            var finder = new BestHandFinder(new HandEvaluator());
            // The 2 side holds two aces; the 3 side holds three kings. Own cards: two aces + junk.
            var two = Card.ParseMany("Ad Ac 9s 8s 7d 6d 5h");
            var three = Card.ParseMany("Kd Kc Kh 9h 8h 7c 6c");
            var personal = Card.ParseMany("Ah As 4c Td Jc Qs 4d");

            var hand = finder.BestOfShapes(personal, Game23Engine.ShapesFor(two, three), WildSet.Empty);
            // 2 side + 3 own: Ad Ac + Ah As + kicker = four aces.
            Assert.AreEqual("Four Aces", hand.Describe());
            Assert.AreEqual("the 2 side", hand.Source);
            Assert.AreEqual(2, hand.Cards.Count(c => two.Contains(c)));
            Assert.AreEqual(3, hand.Cards.Count(c => personal.Contains(c)));
        }

        [Test]
        public void ThreeSideCannotBeMixedWithTwoSide()
        {
            var finder = new BestHandFinder(new HandEvaluator());
            // A flush is only possible by mixing sides (one heart on each side) - not allowed.
            var two = Card.ParseMany("Kh 9s 8s 7d 6d 5c 4c");
            var three = Card.ParseMany("Qh 9c 8c 7c 6s 5s 4s");
            var personal = Card.ParseMany("Ah Jh 9h Tc Th Ts 4d");
            var hand = finder.BestOfShapes(personal, Game23Engine.ShapesFor(two, three), WildSet.Empty);
            Assert.AreNotEqual(HandCategory.Flush, hand.Value.Category);
        }

        [Test]
        public void DeucesAndTreysAreAlwaysWild()
        {
            var engine = new Game23Engine(Game23Settings.Default, Seats(2), new SeededRandom(11));
            engine.StartHand(WildCall.None);
            var seat = engine.Seats[0];
            var wilds = engine.WildsFor(0);
            foreach (var c in seat.Hole)
                Assert.AreEqual(c.Rank == Rank.Two || c.Rank == Rank.Three, wilds.Contains(c));
        }

        [Test]
        public void FoldOutOfTurnIsAcceptedWithWarning()
        {
            var engine = new Game23Engine(Game23Settings.Default, Seats(4), new SeededRandom(5));
            engine.StartHand(WildCall.None);
            foreach (var s in engine.Pending.RollSeats.ToList()) engine.SubmitRoll(s, engine.Seats[s].Hole[0]);
            engine.DrainEvents();

            var pending = engine.Pending;
            Assert.AreEqual(Game23Phase.Betting, pending.Phase);
            int toAct = pending.ToAct;
            int folder = (toAct + 2) % 4;

            Assert.IsTrue(engine.Fold(folder));
            Assert.AreEqual(SeatStatus.Folded, engine.Seats[folder].Status);
            Assert.AreEqual(toAct, engine.Pending.ToAct, "the action does not move");
            var ev = engine.DrainEvents().OfType<OutOfTurnFoldEvent>().Single();
            Assert.AreEqual(folder, ev.Seat);
            Assert.AreEqual(1, ev.Warnings);
            StringAssert.Contains("out of turn", ev.Narration);

            PlayPassively(engine);
            Assert.AreEqual(4000, engine.Seats.Sum(s => s.Stack));
        }

        [Test]
        public void FoldOutOfTurnCanBeDisallowed()
        {
            var settings = new Game23Settings { OutOfTurnFolds = OutOfTurnFoldPolicy.NotAllowed };
            var engine = new Game23Engine(settings, Seats(3), new SeededRandom(5));
            engine.StartHand(WildCall.None);
            foreach (var s in engine.Pending.RollSeats.ToList()) engine.SubmitRoll(s, engine.Seats[s].Hole[0]);
            int folder = (engine.Pending.ToAct + 1) % 3;
            Assert.IsFalse(engine.Fold(folder));
            Assert.AreEqual(SeatStatus.Active, engine.Seats[folder].Status);
        }

        [Test]
        public void OutOfTurnFoldPenaltySitsThePlayerOutNextHand()
        {
            var settings = new Game23Settings { OutOfTurnFolds = OutOfTurnFoldPolicy.AllowedWithPenalty, WarningsBeforePenalty = 1 };
            var engine = new Game23Engine(settings, Seats(3), new SeededRandom(9));
            engine.StartHand(WildCall.None);
            foreach (var s in engine.Pending.RollSeats.ToList()) engine.SubmitRoll(s, engine.Seats[s].Hole[0]);
            int folder = (engine.Pending.ToAct + 1) % 3;
            engine.Fold(folder);
            Assert.IsTrue(engine.DrainEvents().OfType<OutOfTurnFoldEvent>().Single().Penalized);
            PlayPassively(engine);

            engine.StartHand(WildCall.None);
            Assert.AreEqual(SeatStatus.SittingOut, engine.Seats[folder].Status);
            Assert.AreEqual(0, engine.Seats[folder].Hole.Count);
        }

        [Test]
        public void FoldingToOnePlayerEndsTheHandImmediately()
        {
            var engine = new Game23Engine(Game23Settings.Default, Seats(3), new SeededRandom(2));
            engine.StartHand(WildCall.None);
            engine.Fold(1); // during rolling
            engine.Fold(2);
            Assert.AreEqual(Game23Phase.HandOver, engine.Phase);
            Assert.AreEqual(3000, engine.Seats.Sum(s => s.Stack));
            Assert.AreEqual(1000 + 2 * Game23Settings.Default.Ante, engine.Seats[0].Stack);
        }

        [Test]
        public void SixPlayersDoNotFitInOneDeck()
        {
            Assert.AreEqual(5, Game23Settings.Default.MaxPlayers);
            var engine = new Game23Engine(Game23Settings.Default, Seats(6), new SeededRandom(1));
            Assert.Throws<System.InvalidOperationException>(() => engine.StartHand(WildCall.None));
        }

        [Test]
        public void BotsPlayManyHandsWithoutLosingChips()
        {
            var random = new SeededRandom(42);
            var profiles = PlayerProfile.DefaultLineup().Take(5).ToList();
            var seats = Seats(5, 500);
            var settings = new Game23Settings { Structure = BetStructure.NoLimit };
            var engine = new Game23Engine(settings, seats, random);
            var bots = profiles.Select((p, i) => new Game23Bot(i, p, new SeededRandom(100 + i)) { Samples = 12 }).ToList();
            var calls = WildCallPresets.All();
            int outOfTurnFolds = 0;

            for (int hand = 0; hand < 25 && seats.Count(s => s.Stack > 0) >= 2; hand++)
            {
                engine.StartHand(calls[hand % calls.Count]);
                int guard = 0;
                while (engine.Phase != Game23Phase.HandOver && guard++ < 1000)
                {
                    var pending = engine.Pending;
                    if (pending.Phase == Game23Phase.Rolling)
                    {
                        foreach (var s in pending.RollSeats.ToList()) engine.SubmitRoll(s, bots[s].ChooseRoll(engine));
                        continue;
                    }
                    // Give impatient bots a chance to fold before their turn.
                    foreach (var bot in bots)
                        if (engine.Phase == Game23Phase.Betting && bot.WantsToFoldOutOfTurn(engine) && engine.Fold(bot.SeatIndex))
                            outOfTurnFolds++;
                    if (engine.Phase != Game23Phase.Betting) continue;
                    var p = engine.Pending;
                    var decision = bots[p.ToAct].DecideBet(engine, p.Legal);
                    engine.Act(p.ToAct, decision.Action);
                }
                Assert.AreEqual(Game23Phase.HandOver, engine.Phase);
                Assert.AreEqual(2500, seats.Sum(s => s.Stack), $"chips conserved after hand {hand}");
            }
            TestContext.WriteLine($"Out-of-turn folds by bots: {outOfTurnFolds}");
        }
    }
}
