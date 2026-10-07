using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WildCardPoker.Core;

namespace WildCardPoker.Tests
{
    public class HandEvaluatorTests
    {
        private static HandValue Eval(string cards, string wilds = "", HandRules rules = null)
        {
            var evaluator = new HandEvaluator(rules);
            var wildSet = wilds.Length == 0 ? WildSet.Empty : WildSet.Of(Card.ParseMany(wilds));
            return evaluator.Evaluate(Card.ParseMany(cards), wildSet);
        }

        [TestCase("As Ks Qs Js Ts", HandCategory.StraightFlush, "Royal Flush")]
        [TestCase("9h 8h 7h 6h 5h", HandCategory.StraightFlush, "Straight Flush, Nine high")]
        [TestCase("Kc Kd Kh Ks 2c", HandCategory.FourOfAKind, "Four Kings")]
        [TestCase("Kc Kd Kh 7s 7c", HandCategory.FullHouse, "Full House, Kings full of Sevens")]
        [TestCase("Ac 9c 7c 4c 2c", HandCategory.Flush, "Flush, Ace high")]
        [TestCase("Ac 2d 3h 4s 5c", HandCategory.Straight, "Straight, Five high")]
        [TestCase("Qc Qd Qh 4s 2c", HandCategory.ThreeOfAKind, "Three Queens")]
        [TestCase("Jc Jd 4h 4s 2c", HandCategory.TwoPair, "Two Pair, Jacks and Fours")]
        [TestCase("9c 9d Ah 4s 2c", HandCategory.OnePair, "Pair of Nines")]
        [TestCase("Ac Td 8h 4s 2c", HandCategory.HighCard, "Ace high")]
        public void NaturalHands(string cards, HandCategory category, string description)
        {
            var value = Eval(cards);
            Assert.AreEqual(category, value.Category);
            Assert.AreEqual(description, value.Describe());
        }

        [Test]
        public void RankingOrder()
        {
            var ordered = new[]
            {
                "Ac Td 8h 4s 2c", "9c 9d Ah 4s 2c", "Jc Jd 4h 4s 2c", "Qc Qd Qh 4s 2c", "Ac 2d 3h 4s 5c",
                "6c 7d 8h 9s Tc", "Ac 9c 7c 4c 2c", "Kc Kd Kh 7s 7c", "Kc Kd Kh Ks 2c", "9h 8h 7h 6h 5h",
                "As Ks Qs Js Ts",
            }.Select(h => Eval(h).Strength).ToList();
            for (int i = 1; i < ordered.Count; i++) Assert.Greater(ordered[i], ordered[i - 1]);
        }

        [Test]
        public void KickersBreakTies()
        {
            Assert.Greater(Eval("Ac Ad Kh 4s 3c").Strength, Eval("Ac Ad Qh Js Tc").Strength);
            Assert.AreEqual(Eval("Ac Ad Kh 4s 3c").Strength, Eval("Ah As Kd 4c 3d").Strength);
        }

        [Test]
        public void WildMakesFiveOfAKind()
        {
            var value = Eval("Ac Ad Ah As 2c", "2c");
            Assert.AreEqual(HandCategory.FiveOfAKind, value.Category);
            Assert.AreEqual("Five Aces", value.Describe());
            Assert.AreEqual(1, value.WildCount);
        }

        [Test]
        public void AllWildIsFiveAces()
        {
            Assert.AreEqual("Five Aces", Eval("2c 2d 3h 3s 2h", "2c 2d 3h 3s 2h").Describe());
        }

        [Test]
        public void WithoutFiveOfAKindAllWildIsRoyalFlush()
        {
            var rules = new HandRules { AllowFiveOfAKind = false };
            Assert.AreEqual("Royal Flush", Eval("2c 2d 3h 3s 2h", "2c 2d 3h 3s 2h", rules).Describe());
            Assert.AreEqual("Four Aces", Eval("Ac Ad Ah As 2c", "2c", rules).Describe());
        }

        [Test]
        public void WildCompletesStraightFlushOverQuads()
        {
            // 9h 8h 6h 5h + wild => straight flush (7h), beats four nines.
            Assert.AreEqual(HandCategory.StraightFlush, Eval("9h 8h 6h 5h 3c", "3c").Category);
        }

        [Test]
        public void WildFillsGutshotStraight()
        {
            Assert.AreEqual("Straight, Ace high", Eval("Ac Kd Qh Ts 3c", "3c").Describe());
        }

        [Test]
        public void NaturalBeatsWildHouseRule()
        {
            var natural = Eval("Kc Kd Kh 9s 4c");
            var wild = Eval("Kc Kd 3h 9s 4c", "3h");
            Assert.AreEqual(natural.Strength, wild.Strength);
            Assert.AreEqual(0, HandRules.Default.Compare(natural, wild));
            Assert.Greater(new HandRules { NaturalBeatsWild = true }.Compare(natural, wild), 0);
        }

        [Test]
        public void ExplainShowsWhatWildPlaysAs()
        {
            var evaluator = new HandEvaluator();
            var hand = evaluator.Explain(Card.ParseMany("9h 8h 6h 5h 3c"), WildSet.Of(new[] { Card.Parse("3c") }));
            int wildIndex = 4;
            Assert.IsTrue(hand.IsWild[wildIndex]);
            Assert.AreEqual(Card.Parse("7h"), hand.PlaysAs[wildIndex]);
        }

        /// <summary>Cross-check the fast evaluator against brute-force substitution of every real card.</summary>
        [Test]
        public void MatchesBruteForceOnRandomHands()
        {
            var random = new SeededRandom(1234);
            var evaluator = new HandEvaluator();
            for (int trial = 0; trial < 400; trial++)
            {
                var deck = new Deck();
                deck.Shuffle(random);
                var five = Enumerable.Range(0, 5).Select(_ => deck.Draw()).ToArray();
                int wildCount = 1 + random.Next(2);
                var wilds = WildSet.Of(five.Take(wildCount));
                var fast = evaluator.Evaluate(five, wilds).Strength;
                Assert.AreEqual(BruteForce(five, wildCount), fast, string.Join(" ", five.Select(c => c.ToString())));
            }
        }

        private static int BruteForce(Card[] five, int wildCount)
        {
            var naturals = five.Skip(wildCount).ToList();
            var all = Card.FullDeck().ToList();
            int best = -1;
            IEnumerable<List<Card>> Assign(int remaining)
            {
                if (remaining == 0) { yield return new List<Card>(); yield break; }
                foreach (var c in all)
                    foreach (var rest in Assign(remaining - 1))
                    {
                        rest.Add(c);
                        yield return rest;
                    }
            }
            foreach (var substitute in Assign(wildCount))
            {
                var hand = naturals.Concat(substitute).ToArray();
                // Duplicate cards are allowed for wilds; score ranks + suit directly.
                var ranks = hand.Select(c => (int)c.Rank).ToArray();
                bool flush = hand.All(c => c.Suit == hand[0].Suit);
                best = System.Math.Max(best, HandEvaluator.Score(ranks, flush, HandRules.Default));
            }
            return best;
        }
    }
}
