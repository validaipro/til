using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WildCardPoker.Core;
using WildCardPoker.Core.AI;
using WildCardPoker.Core.DiamondTurnOver;

namespace WildCardPoker.Tests
{
    public class DiamondTurnOverTests
    {
        private static List<Seat> Seats(int count, long stack = 1000) =>
            Enumerable.Range(0, count).Select(i => new Seat(i, "P" + i, stack)).ToList();

        private static Card[] C(string text) => text.Length == 0 ? new Card[0] : Card.ParseMany(text);

        /// <summary>Plays forward passively: roll the first hole card, pass on turn-overs, check/call.</summary>
        private static void PlayPassively(DiamondTurnOverEngine engine)
        {
            int guard = 0;
            while (engine.Phase != Phase.HandOver && guard++ < 1000)
            {
                var p = engine.Pending;
                switch (p.Phase)
                {
                    case Phase.Rolling: engine.Roll(p.Seat, engine.Seats[p.Seat].Hole[0]); break;
                    case Phase.TurnOver: engine.TurnOver(p.Seat, null); break;
                    case Phase.Betting: engine.Act(p.Seat, p.Legal.CanCheck ? PlayerAction.Check() : PlayerAction.Call()); break;
                }
            }
            Assert.AreEqual(Phase.HandOver, engine.Phase);
        }

        private static void RollAll(DiamondTurnOverEngine engine)
        {
            while (engine.Phase == Phase.Rolling) engine.Roll(engine.SeatToAct, engine.Seats[engine.SeatToAct].Hole[0]);
        }

        // ------------------------------------------------------------------ board

        [Test]
        public void BoardIsSetUpAlternatingUpAndDown()
        {
            var board = new DiamondBoard();
            board.SetUp(C("Kh 8c Td 6s Ah"), C("3c 4h Js 9d 2h"));
            // Row 1: up, down, up, down, up. Row 2: down, up, down, up, down.
            CollectionAssert.AreEqual(new[] { true, false, true, false, true }, Enumerable.Range(0, 5).Select(c => board.IsFaceUp(1, c)));
            CollectionAssert.AreEqual(new[] { false, true, false, true, false }, Enumerable.Range(0, 5).Select(c => board.IsFaceUp(2, c)));
            CollectionAssert.AreEqual(C("Kh Td Ah"), board.FaceUpInRow(1));
            CollectionAssert.AreEqual(C("4h 9d"), board.FaceUpInRow(2));
        }

        [Test]
        public void TurningOverCardFourFlipsTheWholeColumn()
        {
            var board = new DiamondBoard();
            board.SetUp(C("Kh 8c Td 6s Ah"), C("3c 4h Js 9d 2h"));

            // Example 1 -> example 2: the player points at card 4 of row 1 (face down).
            var revealed = board.TurnOver(3);
            Assert.AreEqual(Card.Parse("6s"), revealed);
            Assert.IsTrue(board.IsFaceUp(1, 3), "row 1, card 4 is now up");
            Assert.IsFalse(board.IsFaceUp(2, 3), "row 2, card 4 is now down");
            CollectionAssert.AreEqual(C("Kh Td 6s Ah"), board.FaceUpInRow(1));
            CollectionAssert.AreEqual(C("4h"), board.FaceUpInRow(2));

            // Another player "comes behind" and turns the same column back, killing the 6s.
            board.TurnOver(3);
            CollectionAssert.AreEqual(C("Kh Td Ah"), board.FaceUpInRow(1));
            CollectionAssert.AreEqual(C("4h 9d"), board.FaceUpInRow(2));
        }

        // ------------------------------------------------------------------ hand rules

        private static DiamondTurnOverEngine Engine(DiamondSettings settings = null) =>
            new DiamondTurnOverEngine(settings ?? DiamondSettings.Default, Seats(2), new SeededRandom(1));

        [Test]
        public void HandCannotMixTheTopAndBottomRows()
        {
            var engine = Engine();
            // K-Q-J-10-9 of hearts needs Q J from row 1 and 10 9 from row 2: not allowed.
            var mixed = engine.BestHandFor(C("Kh 2c 7s"), C("8c 3s 4c 6c"), C("Qh Jh"), C("Th 9h"));
            Assert.AreNotEqual(HandCategory.StraightFlush, mixed.Value.Category);

            // With Q J 10 all in row 1 it is legal: 3 board cards + K 9 of my own.
            var oneRow = engine.BestHandFor(C("Kh 9h 7s"), C("8c 3s 4c 6c"), C("Qh Jh Th"), C("5d"));
            Assert.AreEqual("Straight Flush, King high", oneRow.Describe());
            Assert.AreEqual("row 1", oneRow.Source);
        }

        [Test]
        public void AtLeastOneAndAtMostFourBoardCardsAreUsed()
        {
            var engine = Engine();
            // Five spades of my own would be a flush, but the hand must use a board card.
            var row1 = C("Kc 7h");
            var row2 = C("4c");
            var hand = engine.BestHandFor(C("As Ks Qs"), C("9s 5s Jd 2c"), row1, row2);
            int fromBoard = hand.Cards.Count(c => row1.Contains(c) || row2.Contains(c));
            Assert.That(fromBoard, Is.InRange(1, 4));
            Assert.AreNotEqual(HandCategory.Flush, hand.Value.Category);

            // A five-card row can never be played as a whole: at most four board cards.
            var full = C("Ah Kh Qh Jh Th");
            var best = engine.BestHandFor(C("2c 3d 4s"), C("5c 6d 8s 9c"), full, C(""));
            Assert.AreEqual(4, best.Cards.Count(c => full.Contains(c)));
        }

        [Test]
        public void LowestDiamondInTheHoleAndAllLikeItIsWildForEachPlayer()
        {
            var call = DiamondSettings.Default.Wilds;
            var board = C("5h 9c Kd");
            // Player A: lowest diamond in the hole is the 5d, so every 5 (hand, rolled, board) is wild for A.
            var a = call.Resolve(new WildContext(C("5d 9d Kc"), C("5s 2h"), board));
            CollectionAssert.AreEquivalent(C("5d 5s 5h"), a.Cards);
            // Player B: lowest diamond is the 9d, so 9s are wild for B and 5s are not.
            var b = call.Resolve(new WildContext(C("9d Qd 4c"), C("5c"), board));
            CollectionAssert.AreEquivalent(C("9d 9c"), b.Cards);
            // A rolled diamond is not in the hole and does not count.
            var c = call.Resolve(new WildContext(C("7c Jh"), C("2d"), board));
            Assert.IsTrue(c.IsEmpty);
        }

        [Test]
        public void AceMayBeTheLowestDiamondAtThePlayersDiscretion()
        {
            // Hole A♦ 9♦. Ace high: the 9♦ is the lowest diamond, one wild -> three aces at best.
            // Ace low: the A♦ is the lowest diamond, so both aces are wild -> a king-high heart flush with the K♥ off the board.
            var hole = C("Ad 9d 2s");
            var up = C("Ac 3h 7h 5s");
            var row1 = C("Kh");
            var row2 = C("Jc");
            var aceHighOnly = Engine(new DiamondSettings { AcesEitherWay = false }).BestHandFor(hole, up, row1, row2);
            var eitherWay = Engine().BestHandFor(hole, up, row1, row2);
            Assert.AreEqual(HandCategory.ThreeOfAKind, aceHighOnly.Value.Category, aceHighOnly.ToString());
            Assert.AreEqual(HandCategory.Flush, eitherWay.Value.Category, eitherWay.ToString());
            Assert.Greater(HandRules.Default.Compare(eitherWay.Value, aceHighOnly.Value), 0);
        }

        // ------------------------------------------------------------------ flow

        [Test]
        public void FullHandEndsFourUpThreeDown()
        {
            var seats = Seats(6);
            var engine = new DiamondTurnOverEngine(DiamondSettings.Default, seats, new SeededRandom(7));
            Assert.AreEqual(6, DiamondSettings.Default.MaxPlayers);
            engine.StartHand();
            int turnOverOffers = 0;
            int guard = 0;
            while (engine.Phase != Phase.HandOver && guard++ < 1000)
            {
                var p = engine.Pending;
                if (p.Phase == Phase.Rolling)
                {
                    Assert.AreEqual(3, engine.Seats[p.Seat].Hole.Count, "three cards in hand before each roll");
                    engine.Roll(p.Seat, engine.Seats[p.Seat].Hole.Last());
                }
                else if (p.Phase == Phase.TurnOver)
                {
                    turnOverOffers++;
                    engine.TurnOver(p.Seat, null);
                }
                else engine.Act(p.Seat, p.Legal.CanCheck ? PlayerAction.Check() : PlayerAction.Call());
            }
            Assert.AreEqual(4 + 6, turnOverOffers, "one offer after each of four rolls, then every player");
            foreach (var s in seats)
            {
                Assert.AreEqual(4, s.Up.Count);
                Assert.AreEqual(3, s.Hole.Count, "last card stays down");
            }
            Assert.AreEqual(6000, seats.Sum(s => s.Stack));
        }

        [Test]
        public void HighestHandShowingGetsTheTurnOver()
        {
            var engine = new DiamondTurnOverEngine(DiamondSettings.Default, Seats(5), new SeededRandom(31));
            engine.StartHand();
            RollAll(engine);
            Assert.AreEqual(Phase.TurnOver, engine.Phase);
            int chooser = engine.SeatToAct;
            int best = engine.Seats.Where(s => s.InHand).Max(s => engine.ShowingScore(s.Index));
            Assert.AreEqual(best, engine.ShowingScore(chooser));
        }

        [Test]
        public void PlayerPicksAColumnAndTheDealerTurnsItOver()
        {
            var engine = new DiamondTurnOverEngine(DiamondSettings.Default, Seats(3), new SeededRandom(5));
            engine.StartHand();
            RollAll(engine);
            int chooser = engine.SeatToAct;
            bool wasUp = engine.Board.IsFaceUp(1, 3);
            engine.DrainEvents();
            engine.TurnOver(chooser, 3);
            Assert.AreEqual(!wasUp, engine.Board.IsFaceUp(1, 3));
            var ev = engine.DrainEvents().OfType<ColumnTurnedEvent>().Single();
            Assert.AreEqual(3, ev.Column);
            Assert.AreEqual(Phase.Betting, engine.Phase, "betting follows the turn-over");
        }

        [Test]
        public void FinalRoundGoesFromHighestShowingToLowestAndCanUndoATurnOver()
        {
            var engine = new DiamondTurnOverEngine(DiamondSettings.Default, Seats(4), new SeededRandom(17));
            engine.StartHand();
            int guard = 0;
            while (!(engine.Phase == Phase.TurnOver && engine.IsFinalTurnOverRound) && guard++ < 500)
            {
                var p = engine.Pending;
                if (p.Phase == Phase.Rolling) engine.Roll(p.Seat, engine.Seats[p.Seat].Hole[0]);
                else if (p.Phase == Phase.TurnOver) engine.TurnOver(p.Seat, null);
                else engine.Act(p.Seat, PlayerAction.Check());
            }
            Assert.AreEqual(3, engine.Seats.Sum(s => s.Hole.Count) / 4, "the last card has been dealt down");

            var scores = new List<int>();
            bool firstFlip = true;
            bool topUpBefore = engine.Board.IsFaceUp(1, 2);
            while (engine.Phase == Phase.TurnOver)
            {
                int seat = engine.SeatToAct;
                scores.Add(engine.ShowingScore(seat));
                // The first two players turn over the same column: the second undoes the first.
                engine.TurnOver(seat, scores.Count <= 2 ? 2 : (int?)null);
                if (firstFlip) { Assert.AreNotEqual(topUpBefore, engine.Board.IsFaceUp(1, 2)); firstFlip = false; }
            }
            Assert.AreEqual(4, scores.Count, "every player gets a turn-over chance");
            CollectionAssert.IsOrdered(scores.AsEnumerable().Reverse());
            Assert.AreEqual(topUpBefore, engine.Board.IsFaceUp(1, 2), "turned over twice = back as it was");
            Assert.AreEqual(Phase.Betting, engine.Phase);
        }

        [Test]
        public void OnlyTheChooserCanTurnOver()
        {
            var engine = new DiamondTurnOverEngine(DiamondSettings.Default, Seats(3), new SeededRandom(9));
            engine.StartHand();
            RollAll(engine);
            int other = (engine.SeatToAct + 1) % 3;
            Assert.Throws<System.InvalidOperationException>(() => engine.TurnOver(other, 0));
        }

        [Test]
        public void RollingIsOneByOneFromTheDealersLeft()
        {
            var engine = new DiamondTurnOverEngine(DiamondSettings.Default, Seats(4), new SeededRandom(21));
            engine.StartHand();
            int dealer = engine.DealerSeat;
            var order = new List<int>();
            while (engine.Phase == Phase.Rolling)
            {
                int seat = engine.SeatToAct;
                order.Add(seat);
                engine.Roll(seat, engine.Seats[seat].Hole[0]);
            }
            CollectionAssert.AreEqual(new[] { (dealer + 1) % 4, (dealer + 2) % 4, (dealer + 3) % 4, dealer }, order);
        }

        [Test]
        public void FoldingWhileAnotherPlayerChoosesATurnOverIsOutOfTurn()
        {
            var engine = new DiamondTurnOverEngine(DiamondSettings.Default, Seats(4), new SeededRandom(13));
            engine.StartHand();
            RollAll(engine);
            int chooser = engine.SeatToAct;
            int folder = (chooser + 1) % 4;
            engine.DrainEvents();
            Assert.IsTrue(engine.Fold(folder));
            var ev = engine.DrainEvents().OfType<OutOfTurnFoldEvent>().Single();
            Assert.AreEqual(chooser, ev.ExpectedSeat);
            Assert.AreEqual(chooser, engine.SeatToAct);
            PlayPassively(engine);
            Assert.AreEqual(4000, engine.Seats.Sum(s => s.Stack));
        }

        [Test]
        public void BettingStartsLeftOfTheDealer()
        {
            var engine = new DiamondTurnOverEngine(DiamondSettings.Default, Seats(4), new SeededRandom(8));
            engine.StartHand();
            RollAll(engine);
            engine.TurnOver(engine.SeatToAct, null);
            Assert.AreEqual(Phase.Betting, engine.Phase);
            Assert.AreEqual((engine.DealerSeat + 1) % 4, engine.SeatToAct);
        }

        [Test]
        public void SevenPlayersDoNotFitInOneDeck()
        {
            var engine = new DiamondTurnOverEngine(DiamondSettings.Default, Seats(7), new SeededRandom(1));
            Assert.Throws<System.InvalidOperationException>(() => engine.StartHand());
        }

        [Test]
        public void BotsPlayManyHandsWithoutLosingChips()
        {
            var seats = Seats(6, 500);
            var settings = new DiamondSettings { Structure = BetStructure.NoLimit };
            var engine = new DiamondTurnOverEngine(settings, seats, new SeededRandom(42));
            var bots = PlayerProfile.DefaultLineup().Select((p, i) => new DiamondBot(i, p, new SeededRandom(100 + i)) { Samples = 10 }).ToList();
            int turnOvers = 0, outOfTurnFolds = 0;

            for (int hand = 0; hand < 20 && seats.Count(s => s.Stack > 0) >= 2; hand++)
            {
                engine.StartHand();
                int guard = 0;
                while (engine.Phase != Phase.HandOver && guard++ < 1000)
                {
                    var p = engine.Pending;
                    if (p.Phase == Phase.Rolling) { engine.Roll(p.Seat, bots[p.Seat].ChooseRoll(engine)); continue; }
                    if (p.Phase == Phase.TurnOver)
                    {
                        var col = bots[p.Seat].ChooseTurnOver(engine);
                        if (col.HasValue) turnOvers++;
                        engine.TurnOver(p.Seat, col);
                        continue;
                    }
                    foreach (var bot in bots)
                        if (engine.Phase == Phase.Betting && bot.WantsToFoldOutOfTurn(engine) && engine.Fold(bot.SeatIndex))
                            outOfTurnFolds++;
                    if (engine.Phase != Phase.Betting) continue;
                    var q = engine.Pending;
                    engine.Act(q.Seat, bots[q.Seat].DecideBet(engine, q.Legal).Action);
                }
                Assert.AreEqual(Phase.HandOver, engine.Phase);
                Assert.AreEqual(3000, seats.Sum(s => s.Stack), $"chips conserved after hand {hand}");
            }
            TestContext.WriteLine($"Bot turn-overs: {turnOvers}, out-of-turn folds: {outOfTurnFolds}");
            Assert.Greater(turnOvers, 0, "bots use the turn-over");
        }
    }
}
