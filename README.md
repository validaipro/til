# Wild Card Poker: Diamond Turn-over

A Unity poker game. The current game is **Diamond Turn-over**: a seven-card game with a two-row board where
players can have the dealer **turn over** board cards. The lowest diamond in each player's hole, and all like it,
is wild.

* **Rules:** [docs/GAME_DESIGN.md](docs/GAME_DESIGN.md) (board, turn-over, making a hand, wild cards, open questions)
* **Research:** [docs/RESEARCH.md](docs/RESEARCH.md) (real player behavior, popular apps, roadmap)

## Features
* **Diamond Turn-over engine:**
  * a 2×5 board (up/down/up/down/up over down/up/down/up/down);
  * turn-overs that flip a whole column;
  * the highest hand showing picks after each roll, and everyone picks in the final round;
  * hands use 1–4 cards from ONE row;
  * the last card stays down.
* **Wild card engine:** lowest diamond in the hole and all like it, worked out for each player; the ace can count as
  the lowest diamond at the player's discretion. The general wild-calling system (matching low, "all like it",
  "only", calls typed in plain English) remains available for future games.
* **Hand evaluator** for any number of wild cards, including Five of a Kind, checked against a brute-force solver.
* **Betting:** No-Limit, Pot-Limit or Fixed-Limit, with side pots and online-style pre-action boxes.
* **Fold out of turn** at any moment, with the dealer's warning or penalty policy.
* **Computer players with personalities.** They:
  * decide turn-overs;
  * estimate hand strength by simulation;
  * give away timing tells, go on tilt and talk at the table.
* **Flashing cards:** gold for your wild cards, blue for your best hand, white plus a "HIGH HAND" tag for the
  highest hand showing.
* **Table built entirely in code:**
  * a house dealer with a chip rack and shoe, and six seats;
  * the board with row and column labels;
  * deal, roll and turn-over animations.
* **Design mockup:** a matching table layout on a claude.ai design canvas.

## Run it
1. Install **Unity 6** (6000.0 LTS) via Unity Hub.
2. *Open* this folder as a project. Unity will create `Library/` and the `.meta` files on first import.
3. Open any scene (or create an empty one) and press **Play**. The table creates itself.
   To customize (players, stacks, betting structure, out-of-turn policy, animation speed), add a
   `TableController` component to a GameObject in your scene and edit it in the Inspector.

> The UI uses the classic Input Manager. If your project is set to the *new Input System only*, set
> **Project Settings → Player → Active Input Handling** to *Both*.

## Tests
The rules engine (`Assets/WildCardPoker/Core`) has no Unity dependency, so it runs anywhere:

```bash
cd tools/CoreTests
dotnet test        # .NET 8 SDK
```

The same tests appear in Unity under **Window → General → Test Runner → EditMode**.

## Layout
```
Assets/WildCardPoker/
  Core/                 Rules engine, pure C#, no UnityEngine (WildCardPoker.Core.asmdef)
    Cards/              Card, Deck, crypto/seeded shuffling
    Hands/              Wild-aware evaluator, best-hand search
    Wild/               Wild card calls, per-player resolution, plain-English parser
    Table/              Seats, betting rounds, side pots, pre-actions, game events
    Games/DiamondTurnOver/  Diamond Turn-over board and engine
    AI/                 Player profiles, bots, Monte Carlo equity
  Runtime/              Unity presentation (WildCardPoker.Runtime.asmdef)
    Art/                Procedural sprites + Theme
    Table/              TableController, CardView, SeatView, BoardLayout
    UI/                 ActionBar, uGUI helpers
  Tests/EditMode/       NUnit tests (Unity Test Runner + dotnet)
tools/CoreTests/        dotnet test project for the engine
docs/                   Design spec and research
```
