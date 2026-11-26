# M9 — Coin acceptor, hopper and reels on the software machine

**Status: built 2026-11-26, under D44 and D58, as group 14 of M9.** A case can declare a coin acceptor, a hopper,
reels or a partial pay, and the software machine is built with them; the platform it mirrors has none of them, so
D46 had left their cases skipping. The cashbox, which the platform does report, comes without a declaration.
Twenty-nine cases now run in chapters 4, 5, 6, 11 and 15; two fail as labelled findings against the library.

---

## The rule

D44 again: **the protocol half is the library's.** For these devices that half is the exception queue, the hopper
status the application registers and long polls $4F and $2C read back, the reel stops it registers and $8F reads
back, the partial pay field of $1B and the hand pay refusal that writes $55. **The application half has no QSP
counterpart** for anything but the cashbox and the reel stops, so it is the simplest form of what the GSA document
expects, and each choice names the case it serves.

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| A hopper status is one of nine codes; the library stores the level only when the percentage is $FF | `libsas.cpp:1304`, `:1325` |
| $4F: information channel, address; status, percentage, and the level only when the percentage is not $FF | `lp.cpp:2752`, `:2773`, `:2777` |
| $2C: silent when the percentage is $FF, otherwise the stored level | `lp.cpp:1589` |
| $8F: information channel, address; the nine positions the library holds, zero until a game registers them | `lp.cpp:8912`, `:8930`, `libsas_internal.cpp:366` |
| A game's reels are registered with $FF for the reels it lacks, and $88 is raised for each new stop | `libsas.cpp:1387`, QSP `SasNetwork.cpp:1740`, `:1752` |
| $88 is sent only in the event format; with reporting off the poll that would carry it goes unanswered | `libsas_utils.cpp:903` |
| A cancelled hand pay behind another is dropped silently; otherwise it is counted like a reset and $55 is written | `libsas_internal.cpp:5401`, `:5448`, `:5477` |
| An acknowledged $55 dequeues the hand pay as an acknowledged $52 does | `libsas_internal.cpp:4968`, `:5017` |
| A cancellation is refused once the hand pay has been reset | `libsas_internal.cpp:5384` |
| The partial pay is reported in accounting units beside the amount owed | `libsas_internal.cpp:4764` |
| A removed cashbox raises $1B, its return $1C, and interrupts play | QSP `SasNetwork_Conditions.hpp:74`, `ConditionDefaults.h:71` |

## What the platform lacks, and what this machine does

* **A coin is one accounting unit.** Coins at the internal acceptor go to the hopper while it has room and to the
  drop after; all count as coin acceptor credits (GSA 6.9, 6.12). An external acceptor's go to the drop and to its
  own meter as well (6.10).
* **Coin faults** (4.34 to 4.39): a coin-in tilt $21 and a diverter malfunction $25 interrupt play until cleared; a
  coin pulled back $2D and a coin past the lockout $79 are reported once, in any state.
* **Hopper faults** (4.34, 4.37, 4.38, 4.53, 5.11): empty $23, a runaway $24, and every other fault a coin-out tilt
  $22, each with its $4F status and each interrupting play; full $80 and low $81 do not.
* **A hopper machine pays cash outs from the hopper**, never a ticket, up to its hopper limit (ten dollars unless set),
  counting them as paid from the hopper and as cancelled credits; above the limit the hopper pays the partial pay
  and the attendant the rest (4.21.2, 5.4.2). An empty hopper pays what it has, leaves the rest on the meter and
  raises $23 (4.37.1).
* **Wins go to the credit meter**, unless the operator chose the hopper with `egm set win payout hopper` (4.37.2).
* **A partial pay is ten coins**, paid towards a jackpot hand pay and towards a cash out over the limit (5.3.2,
  5.4.2, 15.12). The hopper starts half full, at five hundred coins of a thousand.
* **A cash out waiting for an attendant can be cancelled** by the player: the credits return and the library refuses
  the hand pay (4.25).
* **Reels**: five, stopping at positions the machine chooses after every game; their tilts are $41 to $45, $40 when
  the reel cannot be named, and $46 for a mechanism disconnected, each interrupting play (4.46, 4.47).

## How the cases learn what the machine does

* `machine coin acceptor on|external`, `machine hopper on`, `machine reels on` and `machine partial pay on` are
  declarations. Only a software machine built with them claims `machine.coin_acceptor`,
  `machine.external_coin_acceptor`, `machine.hopper`, `machine.physical_reels` and `machine.partial_pay`, with
  `stimulus.coin`, `stimulus.hopper`, `stimulus.hopper_fault`, `stimulus.reel` and `stimulus.handpay.cancel`. The
  manual backend offers all of them, for a person at a cabinet that has them.
* `stimulus.cashbox` is offered by the software and manual backends without a declaration.

## What it deliberately does not model

* A rejected coin, coins wagered straight to the drop and tokens of another value.
* Wins and bonuses paid in coins apart from a game win: a legacy bonus and a progressive still go to the credit meter,
  so the hopper halves of 12.13 and 15.29 stay notes and 12.13 keeps skipping on `stimulus.hopper`.
* A cash-out limit long poll $A4 for the hopper limit, left to group 17 with the other cash-out limit cases.
* The answer of a machine without a hopper or reels: $4F still reports an unknown status and $8F stays silent, as
  pinned in `tests/regression`, though the library would answer both with zeros.
* 3.11, which needs every reply timed within twenty milliseconds; it stays notes only.
* The second half of 11.2, which needs a host that stops its general polls (D40).

## Script work done while bringing the cases up

* 4.21.2, 4.22, 4.24, 4.25, 4.32, 4.34 to 4.39, 4.46, 4.47, 4.53, 5.3.2, 5.4.2, 5.11, 5.12, 6.9 to 6.12, 11.2,
  11.20, 15.12 and 15.22 had notes only and now have bodies written from them; 15.14 and 15.29 gained their declarations
  and 15.14 checks the padding of $8F.
* 4.53 runs at address 02: at address 01 the hopper-low code $81 is the same byte as a refused general poll, which
  this host reads as one.
* 6.11 asserts that a cash out over the hopper limit pays exactly the partial pay from the hopper: on a machine whose
  partial pay applies to cash outs, as 5.4.2 has it, "leaves 0009 alone" means nothing beyond it.

## Findings against the documents

| Case | What the library does |
|---|---|
| 4.24 | At power-up the library writes $51 again for a hand pay awaiting its reading in legacy reporting too, where the document expects no more (`libsas_internal.cpp:887`). |
| 5.12 | `SAS_RegisterHopperStatus` stores the coin count only when the percentage is $FF, the one case in which $4F leaves it out, so a six-byte reply always counts zero (`libsas.cpp:1325`, `lp.cpp:2777`). |
