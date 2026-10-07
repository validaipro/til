# Wild Card Poker: Game Design Spec

This document records the rules as the code implements them. Lines marked **[ASSUMED]** are my reading of
something the designer has not spelled out yet; each one is a single setting in code and easy to change.
See [Open questions](#open-questions).

---

## 1. Wild card calling

Before each hand, the dealer **calls** the wild cards. Calling is creative: the dealer can combine any of the
methods below. Wild cards are worked out **for each player separately**, so the same card can be wild for one
player and not for another.

### Words used

| Term | Meaning |
|---|---|
| **In the hole / in the hand** | A player's face-down cards. **[ASSUMED]** Face-up rolled cards are *not* "in the hole" (setting: `WildCallOptions.HoleIncludesUpCards`). |
| **Up / rolled** | Cards a player has turned face up. |
| **On the board** | Shared cards in the middle (the design board in Game 23). |

### Calling methods

| # | Method | Example call | What becomes wild | Who it applies to |
|---|---|---|---|---|
| 1 | **Matching low** | "matching low" | Your lowest card in the hole, **and every card of that value** you can use (hole, rolled, board). | Each player separately |
| 2 | **Lowest/highest of a suit** | "lowest diamond in the hole" | Your lowest diamond in the hole. | Each player separately |
| 2b | ...called on the board | "lowest diamond on the board" | The lowest diamond on the board. | Everyone (it's shared) |
| 3 | **All like it** | "lowest club in the hole and all like it" | The picked card **plus every card of the same value** you can use. Example: player A's only club is the A♣, so all aces are wild for A. Player B's lowest club is the 3♣, so all treys are wild for B. | Each player separately |
| 4 | **Default (house rule #4)** | "highest club in the hole" | When a lowest/highest card is called, *all like it* applies **unless the caller says "only"** or "no matching low". | n/a |
| 5 | **"Only"** | "lowest spade in the hole only, and no matching low" | That one card and nothing else. | Each player separately |
| 6 | **Ranks in a location** | "queens or kings in the hole only" | Q and K in your hole (not on the board, not rolled). | Each player separately |
| 7 | **Ranks on the board** | "sevens and threes on the board only" | 7s and 3s on the board only. | Everyone |
| 8 | **Ranks anywhere** | "kings wild", "deuces" | Every card of that rank. | Everyone |
| 9 | **Named cards** | "one-eyed jacks", "suicide king", "jack of diamonds" | J♠ J♥ / K♥ / J♦ | Everyone |

* Calls can be combined with `;`, `+` or "plus": `kings wild; lowest heart in the hole and all like it`.
* **Ties:** if two cards tie for "lowest card in the hole" (two 5s), both are picked.
* **No match:** if you have no club in the hole, "lowest club in the hole" gives you no wild card.
* **Aces when picking "lowest":** aces count high. **[ASSUMED]** (setting: `WildCallOptions.AceLowWhenPicking`).
* **When wilds are decided:** wilds are worked out again whenever cards move, and are **final after all cards
  are dealt**, as the rules say. The table shows your current wilds with a gold glow as the hand develops.

### What a wild card can be
A wild card can stand for **any card, including a duplicate of a card already in the hand**, which is the common
home-game rule. That makes **Five of a Kind** possible. It ranks above a Royal Flush (setting:
`HandRules.AllowFiveOfAKind`). Hand ranking, highest first:

Five of a Kind → Straight Flush (Royal) → Four of a Kind → Full House → Flush → Straight → Three of a Kind →
Two Pair → One Pair → High Card.

**Natural vs. wild:** two equal hands **tie**, no matter how many wilds each used **[ASSUMED]**. The other common
rule, natural beats wild, is one setting away: `HandRules.NaturalBeatsWild`.

### The dealer's call in the app
* **The player who deals** types the call in plain English (the app understands phrases like the examples
  above and shows what it understood before dealing) or taps a preset.
* **Computer dealers** pick a call from the presets.
* The call is shown on the banner at the top of the table for the whole hand.

---

## 2. Game "23 (MJ)"

### Summary
A seven-card **rolling** game with a 14-card **design board** laid out as the numerals **2** and **3**.
**Deuces and treys are always wild** (mandatory), on top of whatever the dealer calls.
**Aces are high or low**, whichever helps (A-2-3-4-5 straights count).

### Final hand
Every player ends with **7 cards: 3 down in the hole and 4 rolled face up**. The best five-card hand must be one of:

* **2 cards from the "2" structure + 3 of your own cards**, or
* **3 cards from the "3" structure + 2 of your own cards.**

You must use the board, and you can't mix the two structures.

**[ASSUMED]** Any 2 of the 7 cards on the "2", or any 3 of the 7 on the "3", may be used. If the board should
only be used "a certain way" (for example, only cards that touch along the numeral's line), tell me the rule.
The code has a single place for it (`Game23Engine.ShapesFor`).

### How a hand is dealt
| Step | Dealer | Players |
|---|---|---|
| 1 | Collects antes. | Ante. |
| 2 | Lays out the design board: 7 cards face down in the shape of a **2**, 7 in the shape of a **3**. | n/a |
| 3 | Deals **3 cards** to each player, face down. | n/a |
| 4 | Turns **2 cards on the 2** and **2 cards on the 3**. | Each player **rolls** one hole card face up. Everyone picks at the same time, then all are revealed. **[ASSUMED]** |
| 5 | n/a | **Betting round.** |
| 6 | Deals **1 more card** to each player; turns **2 + 2** more. | Roll one card. Betting round. |
| 7 | Deals 1 card; turns **2 + 2** more. | Roll one card. Betting round. |
| 8 | Deals 1 card; turns the **last card on each side (1 + 1)**, so all 14 are up. | Roll the 4th card. Betting round. |
| 9 | Deals the **last card down**. It stays in the hole. | **Final betting round**, then showdown. |

Board cards turned per round: **2, 2, 2, 1** on each side = 7 per side. **[ASSUMED]** You said "two at a time"
and seven per side, so the last turn can only be one card. Setting: `Game23Settings.FlipSchedule`.

**Player limit:** 14 board cards + 7 per player must fit in one 52-card deck, so the limit is **5 players**.

### Betting
* Structures: **No-Limit, Pot-Limit or Fixed-Limit** (setting). Fixed-limit uses the small bet on rounds 1-2 and
  the big bet afterwards, with a cap of a bet plus three raises.
* **[ASSUMED]** Betting starts with the first active player **left of the dealer** each round. Stud games often
  start with the best face-up hand instead. Tell me which you want.
* All-ins and side pots follow standard card-room rules. A short all-in raise does not reopen the betting.

---

## 3. Folding out of turn (interactive)

**FOLD can be pressed at any moment** while you're in the hand, whether or not it's your turn, just like a real
table. The rest of the table sees it right away: the player's cards are mucked, a red "Folded out of turn!"
bubble appears, and computer players now count one opponent fewer.

What the dealer does about it is a table setting (`OutOfTurnFoldPolicy`), based on real card-room practice (an
out-of-turn fold is binding; dealers warn and floors can penalize repeat offenders):

| Policy | Effect |
|---|---|
| `NotAllowed` | Strict online style: "Hold on, it's not your turn." The fold is refused. |
| `AllowedBinding` | The fold stands, and the dealer says nothing. |
| `AllowedWithWarning` **(default)** | The fold stands; the dealer warns the player ("Please wait for your turn"), and warnings are counted. |
| `AllowedWithPenalty` | As above, and after N warnings the player **sits out the next hand**. |

Folding while everyone is still choosing which card to roll is **not** out of turn, since nobody has the action yet.

Computer players fold out of turn too. Impatient personalities (the Rookie and the Rock) sometimes fold weak
hands before the action reaches them, and sometimes apologize: "Oops, my bad."

There are also online-style **pre-action** boxes (*Check/Fold*, *Call any*) that act automatically when your
turn arrives.

---

## Open questions

Please confirm or correct each of these. Every one is a single setting or function.

1. **"In the hole"**: only face-down cards, or every card the player owns including rolled ones?
2. **"All like it" scope**: your example says the A♣ makes aces wild *for player A only*, but an earlier line
   says the matching card is wild "to you and the other players" if it's on the board. I implemented
   **player A only**. Should a board card matched by "all like it" be wild for everyone?
3. **Board "a certain way"**: any 2 of the 7 on the "2" and any 3 of the 7 on the "3", or must the cards be
   connected along the numeral's line (or follow some other pattern)?
4. **Flip schedule**: 2, 2, 2, 1 per side. Is that right?
5. **Rolling**: do players roll at the same time (current), or one by one in turn order?
6. **First to bet**: left of the dealer (current), or the best face-up hand?
7. **Natural vs. wild** ties: split pot (current) or the natural hand wins?
8. **Five of a Kind**: allowed and above the Royal Flush (current)?
9. **Aces when picking "lowest club"**: does an ace count low (1) or high (current)?
10. Should **"no matching low"** be the default instead of "all like it" (house rule #4 says "all like it" applies
    unless the caller says otherwise, so that is the current default)?
