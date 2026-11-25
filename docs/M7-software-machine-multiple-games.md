# M7 — Multiple games and denominations on the software machine

**Status: built 2026-11-24, under D44 and D54, as group 10 of M8.** A case can declare the games the software
machine holds, each at its own player denomination (`machine games 1c 2c 5c 10c`). The machine then answers the
per-game polls ($51 to $56, $2F, $6F, $AF, $B5), the player denomination polls ($B1, $B2), the multi-denomination
preamble ($B0) and the game switch ($09) as the library does, reports a change of game with $8C, and keeps the
platform's per-game meters. The player chooses a game or a denomination, and the operator switches a game on or
off, with three new steps. Without the declaration the machine keeps its single game and nothing it answered
before changes. The tree runs 436 cases, up from 405: chapter 7 runs 11 of 23, chapter 9 runs 19 of 24, chapter 13
runs 37 of 40, and 21.15.1 and 21.15.2 fail as a labelled finding.

---

## The rule

D44 again: **the protocol half is the library's.** Every handler applies the library's tests in the library's order
and is silent where the library is silent. Where the library hands a decision to the application — which games
exist, at what denominations, which are on, what a meter holds for one game or one denomination — the machine does
what QSP does with it, in its simplest form, and says so in a one-line comment at each place.

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| $09: game zero and a game the machine lacks are refused with the address and bit 7, not ignored | `lp.cpp:523` |
| $09: any non-zero byte enables; the game status is set | `lp.cpp:563` |
| $51: the number of games configured | `lp.cpp:2964` |
| $52, $53, $6F, $AF: a game past the last is silent | `lp.cpp:3046`, `:3139`, `:4213` |
| $53: the accounting denomination for every game; game zero's bet and payback from the reachable games | `lp.cpp:3163`, `libsas_internal.cpp:1080` |
| $1F: the largest bet (saturating at $FF) and the integer average payback of the games a player can reach | `lp.cpp:1320`, `libsas_internal.cpp:1087` |
| $55: the selected game, zero in the game menu | `lp.cpp:3239` |
| $56: games whose status and denomination are on, at the selected game's denomination; every one in the menu | `lp.cpp:3359`, `libsas_internal.h:584` to `:635` |
| $6F, $AF, $2F: a meter the library does not keep per game is left out for a game other than zero | `lp.cpp:4274`, `:1909` |
| $2F: more than ten meters is not answered | `lp.cpp:1867` |
| $B1: the selected game's denomination | `lp.cpp:10228` |
| $B2: every enabled denomination of every game, once, in game order | `libsas_internal.cpp:1246` |
| $B5: game zero's largest bet is the largest of every game, at the denomination asked behind the preamble | `lp.cpp:10687` |
| $B0: silent without the multi-denomination extensions bit | `lp.cpp:9843` |
| $B0: error 03 for a poll the library lists as not per denomination, 01 for one it does not know | `lp.cpp:9897` to `:10010` |
| $B0 around $6F, $AF, $2F: error 04 for a game other than zero or a denomination game zero lacks; per-denomination meters only | `lp.cpp:4203`, `:4225`, `:4274` |
| $B0 around $09: error 05 for game zero, a missing game or a denomination it is not played at; otherwise the address | `lp.cpp:10015` |
| $B0 around $B5, $11 to $17: error 05 for a denomination the machine does not offer | `lp.cpp:10144`, `:9799` |
| $8C carries the game number as two BCD digits | `libsas.cpp:1777` |
| Game zero's denominations are the union of its games' | `libsas_internal.cpp:1468` |

## What the platform decides, and what this machine says

Each of these carries a "QSP" comment in the code.

* **Each game has one denomination**, a maximum bet in its own credits and a payback, as the platform configures the
  library (`SasNetwork.cpp:1243`). The games alternate 500 and 250 credits and 92.00 and 92.50 per cent, the demo's
  bet factors and variations (`QSP_DemoOpenGL GameManager.cpp:161`, `:175`).
* **The multi-denomination extensions bit is set**, as the platform always sets it (`SasNetwork.cpp:304`), but only
  on a machine built with games; the single-game machine keeps the bit clear, as before.
* **The meters count in cents** unless the case declares an accounting denomination: the platform's game zero record
  is one cent (`SasNetwork.cpp:1258`). A game credit is worth the selected game's denomination.
* **A game's meters are the platform's per-game block**, charged while it is the selected game; game zero at a
  denomination is the sum of every game at it (`SasNetwork_Meters.hpp:1345`). The meters kept per game are the
  platform's list (`SasNetwork_Meters.hpp:350`).
* **A game is enabled** when the operator offers it and the host has not switched it off, which the platform tells
  the library as the denomination being enabled (`SasNetwork.cpp:1407`). $09 also switches it for the host, at every
  denomination or at the one the preamble names (`SasNetwork_Callbacks.hpp:559`).
* **A selection is reported once, when it changes** (`ExceptionReporting.h:150`, `SasNetwork.cpp:1491`); the
  once-a-second limit on a player moving through the menu is not modelled.
* **The wager type** carries bit 6 on a machine of several games (`SasNetwork_Conditions.hpp:635`).
* **The game menu stands at one cent** for $B1, the platform's game zero denomination.

## How the cases learn what the machine does

* `machine.multiple_games` is read off $51 (two or more games) and `machine.multiple_denominations` off $B2 (two or
  more denominations), the same questions asked of either peer. `machine games` requires each where it applies.
* `egm select game <n>` (0 opens the game menu) needs `stimulus.select_game`; `egm select denomination <c>c`, which
  chooses the first game offered at that denomination, needs `stimulus.select_denomination`; `egm set game <n> on|off`
  needs `machine.enable_games`. The software and manual backends offer all three.
* `stimulus.bet_button` and `machine.player_input_state` are offered by the manual backend only: the platform never
  reports $89 or $1F (D54).
* A preamble reply is decoded twice: its own header, then the base reply under the base command's field names, with
  its length as `base_length`. The parser accepts a base field after a `host send lp B0`.

## What it deliberately does not model

* A game played at more than one denomination, so 21.16 stays notes and skips.
* Total won credits (0022) and an attendant-paid progressive at a denomination, so 9.22 and 9.24 skip on their
  meters.
* The per-game meter sums of 7.12 to 7.24, 12.22 to 12.24, 15.27, 15.33, 15.36 and 22.22 to 22.24, which still need
  a play that names its game and arithmetic over many replies (`stimulus.play.game`, `host.game_meter_delta`).
* Wager categories, game recall, tournaments, and the busy response the library can give $09.
* The cases left as notes: 12.26, 13.33 to 13.35, 15.37, 22.25 and 22.26, which need a hand pay at a denomination or
  a multiplied win written as steps.

## Script corrections made while bringing the cases up

None of these loosens an assertion.

* 9.18, 9.19, 9.21 and 12.25 no longer require a `meter.*` capability: the meter is read by code through $6F, which
  the machine answers. 9.24 keeps its requirement because 0022 is not kept.
* "Choose one cent on the machine" became `egm select denomination 1c`, and the person's progressive win in 9.19,
  12.25, 13.31 and 13.32 became `egm progressive win level=1` on a declared progressive.
* 13.31 and 13.32 keep their level broadcast going with `host broadcast`, as a SAS level must be fed (D49).
* Bodies were written for 7.6, 7.9, 11.24, 13.4, 15.34, AD-35 and AD-36 from their notes, and assertions added to
  9.5 to 9.11 and 12.4 where the notes stated them.

## Findings against the documents

| Case | What the library does |
|---|---|
| 21.15.1, 21.15.2 | $09 for game zero, or for a game past the last, is refused with the address and bit 7 (`lp.cpp:523`), where the document asks for no answer. |
