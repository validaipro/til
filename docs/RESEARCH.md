# Research: Real Player Behavior and Popular Poker Apps

What the research found, and how each finding turns into a feature. ✅ = in the game now, 🔜 = on the roadmap.

## 1. How real players behave

### Player types
Poker strategy writing describes a small set of player types, each defined by how many hands they play
(tight/loose) and how they play them (aggressive/passive)[^types][^types2]:

| Type | Real-world description | In game ✅ |
|---|---|---|
| **TAG** (tight-aggressive) | Selective; raises more than calls; keeps firing when the board suits the story. | "Lena *The Shark*" |
| **LAG** (loose-aggressive) | Many hands; relentless aggression keeps opponents guessing. | "Kai" (Wildcat) |
| **Maniac** | Hyper-loose; raises and re-raises regardless of hand strength; huge pots from nothing. | "Big Tony" |
| **Nit / Rock** (tight-passive) | Very few hands, played passively. | "Doc" |
| **Calling station** (loose-passive) | Likes most hands; calls a lot, rarely raises. | "Rosa" |
| **Beginner** | Inconsistent; gives off tells; breaks etiquette. | "Jamie" (Rookie) |

Each bot has a `PlayerProfile` with looseness, aggression, bluff rate, tilt-proneness, patience, timing tells,
"Hollywooding" and chattiness, so new personalities are just new numbers.

### Timing tells
* Timing is the most reliable online tell: acting unusually fast or slow compared with a player's normal pace
  reveals information[^timing].
* An **instant bet often means strength**; a **long pause followed by a big raise often means uncertainty or a
  bluff**[^timing].
* **Very fast folds** usually mean a weak hand. **Slow folds** mean a close decision[^timing].

✅ Bots think for a time that depends on personality and hand strength: they *snap*-bet strong hands
(`SnapTell`), sometimes stall theatrically before raising a monster (`Hollywood`), muck trash fast, and take
longer on close decisions. Attentive players can learn to read them.

### Bet-sizing tells
* The useful signal is a change from a player's usual sizing pattern[^timing].

✅ Bet size varies by personality and hand strength (maniacs overbet and shove).

### Tilt and emotion
* After losing a big pot, players often go quiet or terse, a sign of frustration[^timing].

✅ Bots carry a `Tilt` level: big losses raise it, wins calm it, and it fades each hand. A tilted bot plays looser
and more aggressively, acts faster, and grumbles ("This deck hates me.").

### Etiquette: acting out of turn
* Acting out of turn gives free information to the players who should have acted first[^oot1].
* Dealers and floors respond with a verbal warning, a forced sit-out, or removal for repeat offenders[^oot1].
* Under TDA rules an out-of-turn action can be binding; out-of-turn folds are binding and can be penalized[^oot2].

✅ Implemented as the `OutOfTurnFoldPolicy` setting (see GAME_DESIGN.md §3), and bots break this etiquette
realistically.

### Home-game wild card culture
* "Low hole card wild", with the lowest hole card wild **for that player alone**, is a classic home game[^home1][^home2].
* *Follow the Queen*, where the card after an exposed queen becomes wild, is a dealer's-choice staple[^ftq].
* Dealer's choice games often use "two or three rows of community cards"[^home2], which is the same family as
  Diamond Turn-over's two-row board.

✅ The wild card calling system works per player, so hole-card wilds count for one player only. 🔜 Follow the
Queen is a natural next calling method (the wild changes as cards are dealt).

## 2. What the big poker apps do

| App | Notable features[^apps][^gg][^zynga][^gop3][^zoom] | What we take ✅/🔜 |
|---|---|---|
| **PokerStars** | Most mature mobile app; Zoom fast-fold; pre-action buttons. | ✅ Pre-action boxes (Check/Fold, Call any). 🔜 Fast-fold option for practice mode. |
| **GGPoker** | Mobile-first portrait mode; smart betting controls; Smart HUD stats; PokerCraft hand history and review; Rush & Cash fast-fold; Spin & Gold multiplier wheel; **run it twice/three times** (free, reduces variance and tilt). | ✅ Smart bet controls (½ pot / pot / all-in, slider). 🔜 Hand history and replay, opponent stats HUD, run it twice, portrait layout. |
| **WSOP** | Multi-table on mobile, portrait one-handed play, quick seat. | 🔜 Portrait mode, quick seat. |
| **Zynga Poker** | Emotes; leaderboards; gifts and drinks; throwing tomatoes; chat. | ✅ Speech bubbles and table talk. 🔜 Emotes, gifts, leaderboards. |
| **Governor of Poker 3** | Avatars, emotes, table backdrops and animations; teams; missions and challenges. | ✅ Avatars and table animation. 🔜 Unlockable table themes, missions. |

Industry-wide, the apps players like have **fast, readable tables, one-thumb controls, satisfying card and chip
animation, and social flavor**. The table here was built around those four.

## 3. Roadmap suggestions
1. **Sound and haptics**: card snaps, chip clinks, a dealer voice for wild calls.
2. **Hand history and replay**: the engine already emits every event in order, so replay is mostly UI.
3. **Portrait/mobile layout** with one-thumb controls.
4. **More dealer's-choice games**: Follow the Queen, Baseball, Low Hole Card Wild, and Hold'em/Stud with any wild call.
5. **Online multiplayer**: the engine is deterministic and UI-free, so it can run on a server as-is.
6. **Commissioned art pass**: every sprite comes from `Theme`, so art can be swapped in piece by piece.

## Sources
[^types]: Pokerology, "Types of Poker Players": https://www.pokerology.com/poker/strategy/playing-styles/
[^types2]: PokerStrategy, "Five player types": https://www.pokerstrategy.com/strategy/bss/five-player-types/
[^timing]: PokerSkill, "Timing Tell": https://www.pokerskill.com/poker-glossary/timing-tell/ ; DeucesCracked, online tells: https://www.deucescracked.com/how-to-spot-online-poker-tells-advanced-techniques/
[^oot1]: VIP-Grinders, "Poker etiquette: dos and don'ts": https://www.vip-grinders.com/the-dos-and-donts-poker-etiquette
[^oot2]: Poker TDA forum, folding in turn / out of turn: https://www.pokertda.com/forum/index.php?msg=1773 ; CardPlayer, "When you can fold": https://www.cardplayer.com/cardplayer-poker-magazines/76-san-manuel-indian-bingo-casino-16-22/articles/13591-when-you-can-fold
[^home1]: PokerNews, "Fun Home Poker Game Rules: 7-card Stud, low hole card wild": https://www.pokernews.com/news/2007/01/fun-home-poker-rules-low-hole-card-3494.htm
[^home2]: Wikipedia, "List of poker variants": https://en.wikipedia.org/wiki/List_of_poker_variants
[^ftq]: PokerNews, "Follow the Queen": https://www.pokernews.com/news/2006/11/fun-home-poker-rules-follow-queen.htm
[^apps]: PokerNews, "Best Real Money Poker Apps 2026": https://www.pokernews.com/best-poker-apps.htm ; DeucesCracked, "Best Poker Apps": https://www.deucescracked.com/poker/best-poker-apps
[^gg]: VIP-Grinders, "GGPoker's unique features": https://www.vip-grinders.com/how-to-make-the-most-of-ggpoker-games-unique-features
[^zynga]: Zynga Poker on the App Store: https://apps.apple.com/us/app/-/id354902315 ; Wikipedia, "Zynga Poker": https://wikipedia.com/wiki/Zynga_Poker
[^gop3]: Governor of Poker 3 on the App Store: https://apps.apple.com/app/apple-store/id877638937
[^zoom]: PokerNews, "A Review of PokerStars' Zoom Poker": https://www.pokernews.com/news/2012/03/a-review-of-pokerstars-zoom-poker-12168.htm
