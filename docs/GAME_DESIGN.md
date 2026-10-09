# Diamond Turn-over: Game Design Spec

This records the rules as the code implements them (`Assets/WildCardPoker/Core/Games/DiamondTurnOver`).
**[CONFIRMED]** = stated by the designer. **[ASSUMED]** = my reading; each one is a single setting or function,
listed again under [Open questions](#open-questions).

---

## At a glance

| | |
|---|---|
| Players | 2–6 (10 board cards + 7 per player fit one deck), plus a **house dealer** |
| Cards per player | 7: **4 rolled face up, 3 in the hole**. The last card stays down. **[CONFIRMED]** |
| Wild cards | **Lowest diamond in the hole and all like it**, worked out for each player. **[CONFIRMED]** |
| Board | Two rows of five, alternating face up and face down |
| Special move | **Turn-over**: have the dealer turn over a board card |
| Making a hand | **1, 2, 3 or 4 face-up cards from ONE row**, plus your own cards. Using the board is mandatory. **[CONFIRMED]** |
| Aces | High or low, player's discretion. **[CONFIRMED]** |

## 1. The board

The dealer sets up two rows of five **[CONFIRMED]**:

```
          1      2      3      4      5
ROW 1    UP    down    UP    down    UP
ROW 2   down    UP    down    UP    down
```

Every column has exactly one card up and one card down, so five board cards are always showing.

### Turn-over
When a player points at a card, **the dealer turns over that whole column**. The face-down card comes up and the
face-up card in the other row goes down. In the designer's example 1 → example 2, the player picks card #4 of row 1.
Row 1 #4 turns face up and row 2 #4 turns face down. **[ASSUMED: this is my reading of "card number four
underneath... this card gets turned over".]**

That is also why "coming behind" works: a later player can turn over **the same column** again. It flips back
and the card the first player wanted is dead. **[CONFIRMED]**

## 2. Dealing a hand

| Step | Dealer | Players |
|---|---|---|
| 1 | Collects antes; sets up the board | n/a |
| 2 | Deals **3 cards** to each player | Each player **rolls** one card face up, one by one from the dealer's left **[ASSUMED: carried over from the previous game]** |
| 3 | Turns over the column picked, if any | The player with the **highest hand showing** may pick a board card to turn over, or **pass** **[CONFIRMED]** |
| 4 | n/a | Betting round **[ASSUMED]** |
| 5 | Deals **1 card** to each player | Roll another card, then repeat steps 3–4 |
| 6 | Repeats until each player has 4 rolled cards | n/a |
| 7 | Deals the **last card face down**; it stays in the hole | Hands are not shown yet **[CONFIRMED]** |
| 8 | Turns over each column picked | **Every player** may turn over a card, from the **highest hand showing down to the lowest**. A player may re-turn a column someone else just turned. **[CONFIRMED]** |
| 9 | n/a | Final betting round **[ASSUMED]**, then showdown: players call their best hand |

**Highest hand showing:** only the face-up rolled cards count: four of a kind > three of a kind > two pair >
pair > high card. Aces count high. Wilds are ignored, because each player's wild depends on their hidden hole
cards. Ties go to the player nearest the dealer's left. **[ASSUMED]**

## 3. Making a hand

* Five cards: **1 to 4 face-up cards from ROW 1, or 1 to 4 from ROW 2**, plus the rest from your own 7 cards.
  **[CONFIRMED]**
* You **cannot** combine cards from both rows. **[CONFIRMED]**
* At least one board card is mandatory, so a hand made only from your own cards doesn't count. **[CONFIRMED]**
* The board cards don't have to sit next to each other in the row. **[ASSUMED]**
* Face-down board cards can't be used.

## 4. Wild cards

**Lowest diamond in the hole and all like it.** Your lowest face-down diamond is wild, and so is every card of the
same rank in your hand, your rolled cards and the board. This is worked out for each player separately.
*Example:* your lowest hole diamond is the 4♦, so 4♦, your rolled 4♠ and the 4♥ on the board are all wild for you.
Another player's lowest hole diamond is the 9♦, so their 9s are wild and the 4s are not.

* No diamond in your hole means no wild cards for you.
* **Aces either way:** the ace may also count as your **lowest** diamond (the A♦ becomes wild, with all aces). The game
  picks whichever way gives you the better hand. **[ASSUMED: extends "aces high or low, player's discretion"]**
* A wild can be any card, so **Five of a Kind** is possible and ranks above a Royal Flush.

## 5. Folding out of turn
FOLD works at any moment. Folding while another player must roll, pick a turn-over or bet counts as out of turn,
and the dealer warns the player (setting: `OutOfTurnFoldPolicy`: not allowed, binding, warning, or a penalty
sit-out).

## 6. Flashing cards (table UI)
| Flash | Meaning |
|---|---|
| **Gold**, quick pulse, "WILD" tag | Your wild cards: in your hand, rolled, and on the board |
| **Blue** outline, slow pulse | The five cards that make up your best hand right now, including the board cards it uses |
| **White** + flashing **HIGH HAND** tag | The up cards of the player with the highest hand showing (who gets the turn-over) |

At showdown each player's winning five flash blue.

---

## Open questions
1. **Turn-over = whole column flips?** That's how I read your example 1 → 2. Confirm.
2. **"Four, three, two or one card in a row"**: does it mean *from one row* (current), or must the cards sit
   **next to each other** in the row?
3. **Betting**: a round after each turn-over and one final round before showdown (current). Is that right, or
   do you want betting at different points?
4. **Who rolls first**: one by one from the dealer's left (current), or all at the same time?
5. **Highest hand showing**: pairs and trips of the face-up cards, ties to the dealer's left (current). OK?
6. **Ace as the lowest diamond**: allowed at the player's discretion (current)?
7. **Natural vs. wild ties** and **Five of a Kind**: currently split pot and allowed.
