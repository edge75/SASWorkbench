# M9 — A game played on a chosen game

**Status: built 2026-11-26, under D44, D58 and D59, as group 15 of M9.** The player chooses the game played and each
game's meters move on their own; a case adds those movements up and compares them with the machine's. The machine
recalls a past game, answers busy where the platform tells the library it is, keeps each kind of ticket on its own
meters, and, when a case declares one, has a bet-one button and waits for the player. Thirty-eight cases now run in
chapters 5, 7, 9, 10, 11, 12, 15 and 22; one fails as a labelled finding against the platform.

---

## The rule

D44 again: **the protocol half is the library's.** Here that half is the exception queue and its event-only codes,
the busy reply, the per-game meter reads of $6F, $AF, $83 and $9A, and the receiver's framing of a poll that carries
its own length. **The application half is the platform's** for the recall screen, the busy states, the ticket meters
and the legacy bonus meters, each with a "QSP" citation. The bet-one button and the wait for the player have no
platform counterpart, so they follow the GSA document in its simplest form and are built only when a case declares
them (D58, D59).

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| A recalled game is reported with $8A: the game's number and the index, each two BCD digits | `libsas.cpp:1808`, QSP `SasNetwork.cpp:1701` |
| The platform passes the replayed game and its replay index less one | QSP `SasNetwork.cpp:1705` |
| $87, $88, $89 and $8B are not queued at all while reporting is off | `libsas_internal.cpp:3962` |
| The busy reply is the address followed by 00 | `libsas_threads_sync.cpp:375` |
| $8A is answered busy while the platform says so: a door open, a tilt, the host's shutdown | `lp.cpp:8650`, QSP `SasNetwork_AftTransfer.hpp:1525` |
| A poll whose length is in the message is framed from its length byte, so one that matches it reaches its handler | `libsas_threads.cpp:1669` |
| $83 reads the two progressive meters for the game asked about | `lp.cpp:8033`, `:8041` |
| $9A reads the game's own legacy bonus meters | `lp.cpp:9354` |
| In the full manual meter mode an award moves no game's legacy meter | `libsas.cpp:2617` |
| The platform writes only the machine's legacy bonus meters | QSP `SasNetwork_AftTransfer.hpp:1745` |
| Total won is coin out and jackpot together | QSP `SasNetwork_Meters.hpp:617` |
| Each kind of ticket in moves its own family; a non-restricted promotional ticket joins the cashable totals, not the regular ones | QSP `SasNetwork_Meters.hpp:483` to `:499`, `:658` to `:682`, `:740` to `:757`, `:879` to `:914` |
| A printed cashable ticket also moves the cashable ticket out totals 002C and 0038 | QSP `SasNetwork_Meters.hpp:683`, `:758` |

## What the platform lacks, and what this machine does

* **A bet-one button** (`machine bet button on`): each press commits one credit, while there are credits for it, up
  to a maximum bet of five, and is reported with $89; the machine then reports $1F, waiting for the player, on every
  general poll with nothing queued until the game starts (GSA 11.4, 11.21).
* **Multiplied jackpots**, which the platform lacks too (D52), write the selected game's legacy bonus meter beside
  the machine's, as the GSA document expects of an application (22.22).

## How the cases learn what the machine does

* `stimulus.play.game`, `machine.game_recall`, `stimulus.game_recall` and `machine.game_busy` are offered by the
  software backend and the manual one; the play names its game with `egm select game`.
* `host.game_meter_delta`, `host.length_prefixed_silence` and `host.busy_reply` are the host's own and claimed always.
* `machine bet button on` is the declaration; only a software machine built with it claims `stimulus.bet_button` and
  `machine.player_input_state`, which the manual backend already offered.
* New steps: `egm press bet`, `egm game recall <index>` and `host expect busy reply to lp <hh> [data <hex>]`.

## What it deliberately does not model

* Game recall as a busy state for a bonus (15.8's last note): the platform does not list it.
* A single-game machine's recall number, which the document has read zero; the cases declare two games.
* Restricted and debit tickets printed by the machine: 10.4.1 to 10.4.3, 10.4.5 and 10.4.6 now skip on
  `machine.restricted_tickets` or `machine.debit_credits`, which are group 17's.
* 9.22, which still skips on its meter.

## Script work done while bringing the cases up

* 5.1, 7.12 to 7.24, 10.4.4, 10.7.9 to 10.7.15, 11.4, 11.6, 11.21, 11.22, 12.23, 12.24, 15.16, 15.23, 15.24, 15.27,
  15.33, 15.36 and 22.22 to 22.24 had notes only and now have bodies written from them.
* 12.22 and 9.24 gained their declarations and the steps their notes described; 15.8 asserts the busy reply its note
  described.
* The per-game cases declare two games and say so in a note: the language has no loops, so a real machine is
  enabled with exactly two, and the case is repeated for each further pair.
* 11.6 asserts the queue in the order of its own steps; the document lists $8A before the door events it opened
  first.

## Findings against the documents

| Case | What the platform does |
|---|---|
| 15.27 | The platform writes only the machine's legacy bonus meters (QSP `SasNetwork_AftTransfer.hpp:1745`), and in the full manual meter mode it sets the library moves no game's meter on an award (`libsas.cpp:2617`), so $9A for a game always answers zero. |
