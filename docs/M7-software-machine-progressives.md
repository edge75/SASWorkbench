# M7 — Progressives on the software machine

**Status: built 2026-11-23, under D44 and D49, as group 4 of M8.** The software machine can be
configured with the progressive levels a case declares, takes the host's level broadcasts ($80,
$86) and watches them for five seconds ($53), wins a level and pays it to its credit meter
(`egm progressive win`), and reports the win the library's three ways ($84, $85, $87) with $54,
$56 and meter 001D. The host can keep a broadcast going in the background (`host broadcast`).
Thirty-two more cases run: thirteen in chapter 12 and nineteen in chapter 13; 12.12 and 13.8 fail
as labelled findings. The tree went from 274 pass, 29 fail, 329 skip to 304, 31, 297 (raw 299, 36,
297: 3.21, 6.2.1, 10.6.3, 10.6.7 and 10.6.8 failed once and pass alone), and AD-28 runs too.

---

## The rule

D44 again: **the protocol half is the library's.** Every handler applies the library's tests in the
library's order and stays silent where it is silent. Which levels a host feeds, what a level is
worth, when a win is announced and when it is paid, and what the machine does while a level is not
being broadcast are QSP's, each with a one-line "QSP" comment in the code.

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| $80: category, check sequence, level 1 to 32, then the group, 00 included; NACK at the own address, silence at 00 | `lp.cpp:7833`, `:7878`, `:7903` |
| $80 and $86 store the amount and set the watch counting; only a machine in a group starts the timers, only for SAS levels | `lp.cpp:7919`, `:7945`, `:7955`, `:8440` |
| $86: a length past $C1 is refused; a group mismatch is silent even at the own address | `lp.cpp:8369`, `:8395` |
| $86: a bad level is refused after the levels before it were taken | `lp.cpp:8411` |
| $53 once a loss, from the master timer or any level's own window; the next broadcast re-arms the latch | `libsas_internal.cpp:2375`, `libsas_threads.cpp:3090` |
| $83: own address, a game past the last is silent; 001D and 0020 added together | `lp.cpp:8018`, `:8032` |
| $84: the most recent win from its own slot, not consumed; group zero for a non-SAS level and for a channel without the category | `lp.cpp:8109`, `:8120`, `libsas_utils.cpp:1276` |
| $85: the oldest SAS win or zeros; dequeued when the reply is acknowledged, never on a repeat; $56 at once for a win left behind | `lp.cpp:8224`, `:8251`, `libsas_pending.cpp:368` |
| $87: up to 32 SAS wins with their count, an empty answer in no group; all of them dequeued on acknowledgement | `lp.cpp:8534`, `libsas_pending.cpp:312`, `:340` |
| A win queues an entry (33 at most, the oldest dropped) and fills the most-recent slot; $56 for a SAS level, and its 15-second reissue | `libsas_wrapper.cpp:3760`, `:3874`, `libsas_threads.cpp:3148` |
| A restart keeps the queue and raises $56 for what is in it; a memory clear empties everything | `libsas_internal.cpp:876`, `:756` |
| $B5 reports the SAS levels, zeroed in no group | `lp.cpp:10701`, `:10709` |

## What the platform decides, and what this machine says

* **The levels are the game's and the group the operator's**: a linked level is a SAS level, a stand-alone
  one a non-SAS level (QSP `SasNetwork.cpp:1302` to `:1321`). A case declares both (D49).
* **A SAS win is announced at the win and paid at the award**: queued with $56 and no payment, then
  001D, coin out and the credit meter move and $54 alone follows (QSP `SasNetwork_Progressives.hpp:104`,
  `:425`, `:448`). A non-SAS win is queued at the award with $54 (`:437`).
* **A stand-alone level is worth $10.00** on this machine; a SAS level is worth what the host last
  broadcast, and a level standing at nothing is not won (QSP `ProgressiveLevelHitRules.h:62`).
* **The machine locks up while a linked level waits for its broadcast**, from power-up until the
  first one and again after a loss (QSP `SasNetwork_Progressives.hpp:74`, `SasNetwork_Callbacks.hpp:340`).
  No level can be won and no transfer lock is granted meanwhile.
* **The multiple-wins bit** of the third enabled-features byte is set on every machine (QSP
  `SasNetwork.cpp:306`) and cleared on a channel without the progressive category.
* **The game end reports the progressive with the game's own win** (QSP `ProgressiveWonInRound`,
  `SasNetwork_Conditions.hpp:682`); the game start carries the group (`:653`).

## How the cases learn what the machine does

* `machine progressive sas group <g> levels <list>` and `machine progressive non_sas levels <list>`
  are declarations (D49). The SAS half is checked against `machine.sas_progressives`, read off the
  group in $1F on either peer; the non-SAS half reports group zero everywhere, so only the software
  machine built with it claims `machine.progressives`.
* `egm progressive win level=<n>[,<n>...] [bet=<credits>]` plays one game that wins those levels,
  needing `stimulus.progressive`, offered by the software and manual backends.
* `host broadcast lp 80|86 ... every <duration>` sends a broadcast to 00 in the background until
  `host broadcast stop` or the end of the case, and never while the case owns the line with a
  sequence; `host.progressive_broadcast` is baseline.
* `host expect answer to lp <hh> [data <hex>]` passes on a reply, an acknowledgement or a refusal,
  for where the documents accept either.

## What it deliberately does not model

* A progressive paid by an attendant: it needs a jackpot limit (group 5), so 12.5 to 12.9, 12.13,
  12.14, 12.17, 12.18, 12.21, 12.27, 13.10 to 13.14, 13.22, 13.23, 13.26, 13.27, 13.30, 13.33 and
  13.37 keep skipping on `machine.jackpot_limit` or `machine.progressives`.
* Several games or denominations: 12.4, 12.22 to 12.26, 13.4, 13.15, 13.31 to 13.35 skip on
  `machine.multiple_games` or `machine.multiple_denominations`.
* A win cumulated across one game cycle in $84 (the addenda's total of a cycle), the platform's win
  held to the round end, and wins held while the link is down.
* The group by channel in $1F, $53 and $B5 on a second host; $84 and $87 do mask it.
* QSP marking every level stale when the progressive channel's link goes down.

## Script work done while bringing the cases up

None of these loosens an assertion.

* Every case declares its levels, puts a note in to stake the winning game, and wins with
  `egm progressive win` where a person's step was; the SAS cases keep their levels fed with
  `host broadcast` where the document has the host keep broadcasting.
* 12.19 and 12.22 asked $6F for 001D and 0020 with a byte too many (`060000001D002000`); the length
  six is a game number and two codes, `0600001D002000`.
* 13.6 and 13.8 now assert the answer to the other groups, which the document requires.
* 13.19 lets its empty $85 reading be acknowledged before the next win: the dequeue it leaves pending
  would otherwise take that win.
* AD-23 now also requires `machine.jackpot_limit`: its progressive is paid by hand. AD-28 had notes
  only and now has a body.
* 13.38 to 13.40 drain the queued events before turning reporting off, as any long poll draws an event
  while it is on; 13.40 forgets the losing game before asserting the winning one.

## Findings against the documents

| Case | What the library does |
|---|---|
| 12.12 | $84 reports the level that was hit with group zero for a non-SAS win (`lp.cpp:8128`, `:8138`); the document expects level 00. QSP hands the library the hit level (`SasNetwork_Progressives.hpp:176`). |
| 13.8 | $86 for another group at the machine's own address is silent: the NACK is commented out (`lp.cpp:8395`, "REET-13"); the document wants an acknowledgement or a refusal. |
