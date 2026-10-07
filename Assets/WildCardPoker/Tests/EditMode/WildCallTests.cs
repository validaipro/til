using System.Linq;
using NUnit.Framework;
using WildCardPoker.Core;

namespace WildCardPoker.Tests
{
    /// <summary>Each test mirrors an example from the game designer's wild card calling rules.</summary>
    public class WildCallTests
    {
        private static WildContext Ctx(string hole, string up = "", string board = "", WildCallOptions options = null) =>
            new WildContext(Cards(hole), Cards(up), Cards(board), options);

        private static Card[] Cards(string text) => text.Length == 0 ? new Card[0] : Card.ParseMany(text);

        private static string Wilds(WildCall call, WildContext ctx) =>
            string.Join(" ", call.Resolve(ctx).Cards.OrderBy(c => c.Id).Select(c => c.ToString()));

        private static WildCall Parse(string text)
        {
            var result = new WildCallParser().Parse(text);
            Assert.IsTrue(result.Success, string.Join("; ", result.Warnings));
            Assert.IsEmpty(result.Warnings, string.Join("; ", result.Warnings));
            return result.Call;
        }

        [Test]
        public void MatchingLow_LowestHoleCardAndEveryCardOfThatValue()
        {
            // Lowest card in the hole is the 4; every 4 the player can use is wild for them.
            var ctx = Ctx(hole: "4c 9d Ks", up: "4h Qc", board: "4s 7d 8h");
            Assert.AreEqual("4c 4h 4s", Wilds(WildCallPresets.MatchingLow(), ctx));
        }

        [Test]
        public void MatchingLow_EachPlayersOwnLowCardMatchesOnTheBoardAndInTheirHand()
        {
            // Designer's example: if a player's low card is a deuce, every deuce on the board and in that
            // player's hand is wild for them. Another player's low card is a 4, so 4s are wild for them instead.
            var call = Parse("matching low");
            const string board = "2s 4d 9c";
            var playerA = Ctx(hole: "2c 9d Kh", up: "2h 7s", board: board);
            var playerB = Ctx(hole: "4c 8d Qh", up: "Jh", board: board);
            Assert.AreEqual("2c 2h 2s", Wilds(call, playerA));
            Assert.AreEqual("4c 4d", Wilds(call, playerB));
        }

        [Test]
        public void InTheHoleMeansTheCardsInTheHandNotRolledCards()
        {
            // The rolled 4c is on the table, not in the hand, so the lowest club in the hole is the 9c.
            var ctx = Ctx(hole: "9c Kd", up: "4c");
            Assert.AreEqual("9c", Wilds(Parse("lowest club in the hole only"), ctx));
        }

        [Test]
        public void LowestDiamondInTheHole_OnlyThatCard()
        {
            var ctx = Ctx(hole: "9d 5d Ac", up: "5h", board: "5s");
            Assert.AreEqual("5d", Wilds(Parse("lowest diamond in the hole only"), ctx));
        }

        [Test]
        public void LowestDiamondOnTheBoard_SharedByEveryone()
        {
            var call = Parse("lowest diamond on the board only");
            Assert.AreEqual("6d", Wilds(call, Ctx(hole: "2d", board: "Jd 6d Kc")));
            Assert.AreEqual("6d", Wilds(call, Ctx(hole: "3d", board: "Jd 6d Kc")));
            Assert.IsFalse(call.Clauses[0].IsPersonal);
        }

        [Test]
        public void LowestClubAndAllLikeIt_IsPersonalToEachPlayer()
        {
            // Player A's only club in the hole is the ace: all aces A can use are wild for A.
            // Player B's lowest club is the 3: all treys are wild for B, aces are not.
            var call = Parse("lowest club in the hole and all like it");
            const string board = "Ah 3d 9s";
            var playerA = Ctx(hole: "Ac Kd 7h", up: "As 3s", board: board);
            var playerB = Ctx(hole: "3c 8c Qh", up: "Ad", board: board);
            Assert.AreEqual("Ac Ah As", Wilds(call, playerA));
            Assert.AreEqual("3c 3d", Wilds(call, playerB));
        }

        [Test]
        public void LowestSpadeOnly_NoMatchingLow()
        {
            var call = Parse("Lowest spade in the hole only, and no matching low");
            var ctx = Ctx(hole: "6s Js 2h", up: "6d", board: "6c");
            Assert.AreEqual("6s", Wilds(call, ctx));
            Assert.IsFalse(((PickedCardWildClause)call.Clauses[0]).AllLikeIt);
        }

        [Test]
        public void HighestClubInTheHole_DefaultsToAllLikeIt()
        {
            // House rule #4: unless the caller says otherwise, "all like it" applies.
            var call = Parse("highest club in the hole");
            var ctx = Ctx(hole: "4c Tc 2h", up: "Td", board: "Ts 4d");
            Assert.AreEqual("Tc Td Ts", Wilds(call, ctx));
        }

        [Test]
        public void HighestClubInTheHoleOnly()
        {
            var call = Parse("highest club in the hole only");
            Assert.AreEqual("Tc", Wilds(call, Ctx(hole: "4c Tc 2h", up: "Td", board: "Ts")));
        }

        [Test]
        public void QueensOrKingsInTheHoleOnly()
        {
            var call = Parse("Queens or kings in the hole only");
            var ctx = Ctx(hole: "Qc Kd 5h", up: "Qs", board: "Kh");
            Assert.AreEqual("Qc Kd", Wilds(call, ctx));
        }

        [Test]
        public void SevensAndThreesOnTheBoardOnly()
        {
            var call = Parse("Sevens and threes on the board only");
            var ctx = Ctx(hole: "7c 3d", up: "7h", board: "7s 3h 9c");
            Assert.AreEqual("3h 7s", Wilds(call, ctx));
        }

        [Test]
        public void HoleCanIncludeRolledCards()
        {
            var options = new WildCallOptions { HoleIncludesUpCards = true };
            var ctx = Ctx(hole: "9c Kd", up: "4c", board: "", options: options);
            Assert.AreEqual("4c", Wilds(Parse("lowest club in the hole only"), ctx));
        }

        [Test]
        public void TiedLowestCardsAreAllWild()
        {
            var ctx = Ctx(hole: "5c 5d 9h");
            Assert.AreEqual("5c 5d", Wilds(Parse("lowest card in the hole only"), ctx));
        }

        [Test]
        public void NoMatchingCardMeansNoWild()
        {
            var ctx = Ctx(hole: "5c 6d 9h");
            Assert.AreEqual("", Wilds(Parse("lowest spade in the hole"), ctx));
        }

        [Test]
        public void MandatoryDeucesAndTreysCombineWithCall()
        {
            var call = WildCallPresets.DeucesAndTreys().Plus(Parse("one-eyed jacks"));
            var ctx = Ctx(hole: "2c Jh Jd", up: "3s", board: "Js");
            Assert.AreEqual("2c 3s Jh Js", Wilds(call, ctx));
        }

        [TestCase("matching low", "Matching low (lowest card in the hole and all like it)")]
        [TestCase("kings wild; lowest heart in the hole and all like it",
            "Kings wild; Lowest heart in the hole and all like it")]
        [TestCase("deuces plus suicide king", "Deuces wild; Suicide king wild")]
        [TestCase("jack of diamonds", "Jack of Diamonds wild")]
        [TestCase("low hole card", "Matching low (lowest card in the hole and all like it)")]
        public void ParserDescriptions(string text, string expected)
        {
            Assert.AreEqual(expected, Parse(text).Describe());
        }

        [Test]
        public void ParserReportsUnknownWords()
        {
            var result = new WildCallParser().Parse("purple dragons wild");
            Assert.IsFalse(result.Success);
            Assert.IsNotEmpty(result.Warnings);
        }
    }
}
