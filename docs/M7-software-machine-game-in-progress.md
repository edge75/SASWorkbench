# M7 — A game or a cash out held open, and the jackpot limit, on the software machine

**Status: built 2026-11-23, under D44 and D50, as group 5 of M8.** A case can now start a game and
leave it running (`egm game start`, `egm game end`), keep a cash out printing long enough to be
polled (`egm press collect lasting <duration>`), and set the machine's jackpot limit (`egm set
jackpot limit <cents>`). Above the limit a game win, a progressive, a legacy bonus and an AFT
coin-out bonus are paid by an attendant, as the platform pays them. Thirty more cases run: nine in chapter 12, ten in chapter 13, one in chapter 14, seven in chapter 15 and three in chapter 16, and AD-23; 15.11 and 16.19 fail as labelled findings. The tree went from 304 pass, 31 fail, 297 skip to 332, 33, 267 (raw 331, 34, 267: 10.6.8 failed once, a known flake).

---

## The rule

D44 again, but nothing here is the library's to decide: the library is told that a game started
and ended and that a hand pay began, and the platform chooses when. So every behaviour below is
QSP's, in its simplest form, with a one-line "QSP" comment in the code; the library's own part is
how it reports what it is handed (`sasHandpayStart`, `sasSetProgressiveLevelWin`).

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| A game in progress holds a legacy bonus in escrow; the game end pays it, after the game delay | QSP `SasNetwork_AftTransfer.hpp:1669`, `SasNetwork_Conditions.hpp:691`, `:697` |
| A game in progress holds a transfer to the machine (types 00, 10, 11), answered pending until the game ends | QSP `AftAvailability.h:310`, `SasNetwork_AftTransfer.hpp:427` |
| A lock asked for during a game is answered pending ($40) and taken when the game ends; a cancel answers $FF | QSP `AftAvailability.h:53`, `SasNetwork_AftTransfer.hpp:1089` |
| A game in progress does not stop play, so every configured transfer stays available | QSP `ConditionDefaults.h:61`, `AftAvailability.h:186` |
| A ticket being printed interrupts play: a bonus waits for it, and no transfer or lock is offered | QSP `ConditionDefaults.h:65`, `SasNetwork_Conditions.hpp:404` |
| A win is over the limit when strictly greater than it | QSP `SasMarket.cpp:479` |
| A game win over the limit locks the machine up as a jackpot hand pay | QSP `NvramHandler.cpp:1316` |
| A progressive is weighed by everything the round has won on its levels | QSP `NvramHandler.cpp:1516` to `:1535` |
| A level the machine runs, paid by hand, is queued with no $54; a SAS level was announced with $56 at the win | `libsas_wrapper.cpp:3867`, QSP `SasNetwork_Progressives.hpp:448` |
| The hand pay reports the progressive's group (zero for a level the machine runs) and level, and its amount in cents | `libsas_wrapper.cpp:3438` to `:3471`, `libsas_internal.cpp:4899` |
| A legacy bonus over the limit is an external bonus hand pay, its award reported by $7C at once | QSP `SasNetwork_AftTransfer.hpp:1708`, `:1753` |
| A second external bonus is added to the one still owed | QSP `MoneyInHandler.cpp:625` |
| An AFT coin-out bonus over the limit is a hand pay, as a jackpot bonus always is | QSP `SasNetwork_AftTransfer.hpp:629` |
| At the reset: jackpot meter, then the attendant-paid progressive, paytable or external bonus meter for each part | QSP `SasNetwork_Conditions.hpp:842` |

## What the platform decides, and what this machine says

* **One limit.** The platform keeps a game win limit and a progressive win limit
  (`NvramHandler.h:487`); this machine keeps one figure, set by the case, and weighs all four kinds
  of award against it. It starts at zero, which means no limit, so no case written before this group
  changes.
* **One hand pay at a time.** The library queues hand pays; the platform raises a second while the
  first's $52 still waits for its reading. This machine keeps one: a progressive won in the same
  game as a jackpot joins it, and a legacy bonus over the limit waits in escrow until the reset of
  the one before has been reported.
* **The cash out lasts as long as the case says**, and ends by printing the ticket as `egm press
  collect` does; the game lasts until `egm game end`.

## How the cases learn what the machine does

* `machine.jackpot_limit` is claimed by the software backend, as no poll reports a jackpot limit.
  `egm set jackpot limit <cents>` needs `stimulus.jackpot_limit`, offered by the software and
  manual backends.
* `egm game start [bet=<credits>]` and `egm game end [win=<credits>]` need
  `stimulus.game_in_progress`; `egm press collect lasting <duration>` needs
  `stimulus.cashout_in_progress`. Both backends offer them: a person can leave a game in a bonus
  round or before the draw, and a printer takes its time.
* `egm progressive win ... win=<credits>` wins from the paytable in the same game, for the cases
  where a progressive and a machine jackpot are paid as one.

## What it deliberately does not model

* **Poker** (`machine.poker`): the platform passes a dealt hand and held cards to the library
  (QSP `SasNetwork.cpp:1776`, `:1794`) but raises no draw request, so 11.19 and 15.21 could never
  run, and a card game would be a second game model for 11.23, 15.13 and 15.25. Left out.
* A hand pay queue (`machine.handpay_queue`), partial pay, a hopper and several games or
  denominations, so 12.6, 12.13, 13.11, 15.12, 15.29, 15.36 and 15.37 keep skipping.
* The attendant and audit menus (`machine.audit_menu`), so 21.2 and the menu half of 16.19 stay notes.
* A game held across a power cycle, the platform's recovery of it, and the reset of a hand pay to
  the credit meter.
* A progressive past the credit limit, and the taxation limit over the game cycle.
* Multiplied jackpots (chapter 22), out by the user's choice.

## Script work done while bringing the cases up

None of these loosens an assertion.

* Every progressive hand pay case declares its levels, sets the jackpot limit below the level's
  value, puts a note in and wins with `egm progressive win` where a person's step was; the SAS cases
  keep their levels broadcast with `host broadcast`.
* 12.27 and 13.37 drain the queued events before reading the hand pay, as any long poll draws an
  event while reporting is on.
* 15.4, 15.20, 16.19, 16.26 and 16.57 hold a game open where the document's person did; 15.5
  presses collect with a cash out that lasts three seconds.
* 12.5 puts a long poll between general polls that expect different answers, as a second general
  poll in a row is an implied NACK; 13.10 keeps its level broadcast to 00 through its fifteen quiet
  seconds, or the machine also reports losing it.
* 14.7 now sets the credit and jackpot limits its note asks for. AD-23 wins its progressive.

## Findings against the documents

| Case | What the platform or the library does |
|---|---|
| 15.11 | Bonuses of one tax status that arrive during a hand pay are held as one amount (QSP `SasNetwork_AftTransfer.hpp:1664`) and paid as one award after the reset, so the second 500 and the 5 come back as one hand pay of 505. |
| 16.19 | Mid-game the platform still reports every configured transfer available, as a game in progress does not stop play (QSP `ConditionDefaults.h:61`, `AftAvailability.h:186`); the document wants none. |
