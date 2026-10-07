# Wild Card Poker

A Unity poker game built around **dealer's-choice wild card calling** and the original rolling game **"23 (MJ)"**:
a seven-card game played over a 14-card design board shaped like the numerals **2** and **3**.

* **Rules:** [docs/GAME_DESIGN.md](docs/GAME_DESIGN.md) (wild card calling, Game 23, folding out of turn, open questions)
* **Research:** [docs/RESEARCH.md](docs/RESEARCH.md) (real player behavior, popular apps, roadmap)

## Features
* **Wild card calling engine.** Matching low, lowest/highest of a suit, *all like it*, *only*, ranks in the hole /
  on the board, named cards (one-eyed jacks, suicide king), combined calls. Wilds are worked out for each player
  separately. The dealer can **type a call in plain English** and see what it means before dealing.
* **Hand evaluator** that handles any number of wild cards, including Five of a Kind, plus house rules (natural
  beats wild, five of a kind allowed). It is checked against a brute-force solver.
* **Game 23 engine:** design board, rolling cards, 2-side/3-side hand rule, mandatory deuces and treys,
  No-Limit / Pot-Limit / Fixed-Limit betting, side pots.
* **Fold out of turn:** press FOLD any time. The dealer warns or penalizes according to a table setting, and
  computer players do it too.
* **Computer players with personalities** (Shark, Rookie, Maniac, Rock, Calling Station, Wildcat). They
  estimate hand strength by simulation with the wild call applied, and have timing tells, tilt and table talk.
* **Polished table built entirely in code**: rail, felt, card faces and backs, chips, gold wild glow, deal, flip
  and roll animations, speech bubbles, dealer log, smart bet controls and pre-action boxes. No imported assets
  are needed, and all art is centralized in `Theme` so commissioned art can replace it.

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
    Games/Game23/       The 23 (MJ) board and engine
    AI/                 Player profiles, bots, Monte Carlo equity
  Runtime/              Unity presentation (WildCardPoker.Runtime.asmdef)
    Art/                Procedural sprites + Theme
    Table/              TableController, CardView, SeatView, BoardLayout
    UI/                 ActionBar, WildCallPanel, uGUI helpers
  Tests/EditMode/       NUnit tests (Unity Test Runner + dotnet)
tools/CoreTests/        dotnet test project for the engine
docs/                   Design spec and research
```
