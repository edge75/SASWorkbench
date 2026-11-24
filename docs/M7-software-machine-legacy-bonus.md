# M7 — Legacy bonusing on the software machine

**Status: built 2026-11-22, under D44 and D45, for the group the user chose on 2026-11-21.** The
software machine takes legacy bonuses ($8A), holds them in escrow while it must, delays the next
game ($2E), reports the award ($7C, $90) and keeps the three legacy bonus meters ($9A). Chapter 15
runs 13 of its 37 cases, up from 4: twelve pass and one fails as a labelled finding. Chapter 22
still runs 1 of 28, because the platform the machine mirrors offers no multiplied jackpots.

---

## The rule

D44 again: **the protocol half is the library's.** Every handler applies the library's tests in the
library's order, and stays silent where the library returns without sending. Where the library hands
the bonus to the application, the machine does what QSP does with it, in its simplest form, and
says so in a one-line comment at each place.

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| $8A: channel, then the legacy bonus feature bit, then the address; silent on any of them | `lp.cpp:8621`, `:8628` |
| $8A: the busy response, address then `00`, in place of the acknowledgement | `lp.cpp:8652`, `libsas_threads_sync.cpp:374` |
| $8A: four BCD bytes of accounting credits and a tax status byte, acknowledged before the platform sees them | `lp.cpp:8656` to `:8663` |
| $2E: feature bit, channel, address; two bytes of binary, least significant first, in tenths of a second | `lp.cpp:1716`, `:1725`, `:1755` |
| $8B: silent unless the jackpot multiplier bit is set as well, which this machine never sets | `lp.cpp:8689`, `:8691` |
| $90: channel and feature bit; multiplier, multiplied win, tax status and bonus | `lp.cpp:8974`, `:8981`, `:9000` |
| $90: the award is cleared once the reply is acknowledged, never by a repeat, and a loop break leaves it | `lp.cpp:9022`, `libsas_pending.cpp:250` |
| $9A: feature bit only, no channel test; a game past the last is silent | `lp.cpp:9308`, `:9330` |
| $9A: the three legacy meters, deductible, non-deductible and wager match | `lp.cpp:9354` to `:9358` |
| $7C carries multiplier, multiplied win, tax status and bonus, and is queued as an ordinary exception | `libsas.cpp:2634` to `:2643` |
| The award is what $90 reads, and moves the legacy meter for its tax status | `libsas.cpp:2594`, `:2621` |
| The two feature bits are bit 2 and bit 0 of the first byte, both cleared on a channel without the legacy bonus category | `libsas.h:186`, `:188`, `libsas_utils.cpp:1235` |

## What the platform decides, and what this machine says

Each of these carries a "QSP" comment in the code.

* **Legacy bonusing is on** by default, as QSP stores it after a memory clear (`SasRules.h:442`,
  written by `PostNvramReset` at `SasNetwork.cpp:1206`), and
  the operator can turn it off with `egm set legacy bonus off`.
* **Multiplied jackpots are not offered.** QSP never gives the library the jackpot multiplier bit
  (the `sasInit` features at `SasNetwork.cpp:286` leave it clear) and has no handler for $8B, so the
  library answers $8B with silence and `machine.multiplied_jackpot` is absent.
* **Busy** while a door is open or the host has shut the machine down (`SasGameRules.h:255`). A hand
  pay is not busy: the bonus is escrowed.
* **The escrow** holds one amount per tax status. A bonus is paid at once when nothing holds it, and
  otherwise when the hand pay is reset, the door shut, the machine started up or the delay run out
  (`SasNetwork_AftTransfer.hpp:1669`, `SasNetwork_Conditions.hpp:404`). Two bonuses of one tax status
  held together are paid as one award, and each tax status as an award of its own.
* **A tax status past wager match** is acknowledged and dropped, and a bonus of nothing pays nothing
  (`SasNetwork_AftTransfer.hpp:1648`, `:1699`).
* **The payment** is a machine-paid external bonus: the credit meter, total coin out and meter `001E`
  move, as the AFT coin-out bonus already moves them (QSP `BonusIn`, `MoneyInHandler.cpp:635`).
  Multiplier and multiplied win are zero (`SasNetwork_AftTransfer.hpp:1738`).
* **The game delay** starts at game end and holds both the next game and the escrow; a delay sent
  while one runs replaces it, and zero ends it (`SasNetwork_Conditions.hpp:691`,
  `SasNetwork_Callbacks.hpp:775`). `egm play` waits out a running delay, as a player would.

## How the cases learn what the machine does

* `machine.legacy_bonus` and `machine.multiplied_jackpot` are read off bits 2 and 0 of the first
  enabled-features byte, the same question asked of either peer.
* `egm set legacy bonus on|off` needs `stimulus.legacy_bonus`, which the software and manual backends
  offer: a person works the same menu on a real machine. 15.1 and 22.1 use it to switch bonusing off.

## What it deliberately does not model

* Multiplied jackpots, so the 27 chapter 22 cases that need one skip on `machine.multiplied_jackpot`.
* A jackpot limit: a bonus above it would be a hand pay (`SasNetwork_AftTransfer.hpp:1708`), so the
  eight chapter 15 cases that need one still skip on `machine.jackpot_limit`. *Built since, in group
  5 of M8: see `M7-software-machine-game-in-progress.md`.*
* A game or a cash out held open, maintenance mode, game recall and tilts, so a bonus during a game
  or a cash out is never seen; 15.5 skips on `stimulus.cashout_in_progress`. *The game and the cash
  out were built in group 5 of M8; maintenance, recall and tilts were not.*
* The platform's limits on money in, so a bonus is never refused for size.
* The electronic-transfer meters QSP refreshes after an award.
* Persistence: the escrow and the award live only as long as the machine.
* What `machine.legacy_bonus` means on a real board: bit 2 says the machine takes legacy bonuses and
  nothing about QSP's busy states or its hand-pay limits there, which a case still meets as it runs.

## Script corrections made while bringing the cases up

None of these loosens an assertion; the last item adds some.

* The game delay is binary, least significant byte first, so five seconds is `3200`; the cases sent
  `0050`, which the library reads as 2048 seconds.
* 15.28 and 15.32 named their meters in `$6F` and `$AF` with a byte out of place, so one code read
  as `0C00`. 15.28 also states the one-dollar accounting unit its one-credit wager assumes.
* 15.6 forgets the first award before asserting the second, as an event field expectation needs.
* 15.5 keeps its document sequence and now also needs `stimulus.cashout_in_progress`: the software
  machine's cash out is over before a poll can follow the button, so it skips rather than send its
  second bonus after the cash out instead of during it.
* 15.1 and 15.26 now also assert the switched-on bonus and the `$9A` meters.

## Findings against the documents

| Case | What the library does |
|---|---|
| 15.26 | `$6F` codes D7, D8 and D9 are the library's own validation meters (`libsas.h:1489` to `:1497`, read at `lp.cpp:4281`); the legacy bonus meters are kept apart and only `$9A` reads them (`lp.cpp:9354`). |
