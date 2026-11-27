# M9 — Tournaments

**Status: built 2026-11-26, under D44, D58 and D60, as group 16 of M9.** A case declares that the machine plays
host-controlled tournaments, and the software machine then sets the tournament bit, acknowledges $8C and keeps the
four tournament meters that $95 to $99 read. The host decodes those five replies and the $94 reply. Eight cases now
run in chapter 18, 18.8 among them, which group 14 left waiting on `machine.tournament`.

---

## The rule

D44 again: **the protocol half is the library's.** Here that half is the feature bit, the tests $8C and the meter polls
make before they answer, the acknowledgement, and the four counts per game the meter polls read. The library keeps
those counts but never moves them: only the application writes them (`SAS_WriteTournamentMeter`). **QSP has no
tournaments** — it never sets the bit (QSP `SasNetwork.cpp:285`) — so the application half follows the GSA document in
its simplest form and is built only when a case declares it (D58, D60).

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| The tournament bit is bit 3 of the first feature byte | `libsas.h:189`, `libsas_wrapper.cpp:277` |
| $8C: the address, then the check sequence, then a game past the last or not configured, then the game's bit, each silent | `lp.cpp:8768`, `:8772`, `:8786`, `:8791`, `:8809` |
| $8C is never answered busy: the library's test is commented out | `lp.cpp:8779` |
| $8C is acknowledged, then the game, minutes, seconds, credits and pulses go to the application | `lp.cpp:8812` to `:8828` |
| $95 to $98: the same tests, silent for a game without the bit | `lp.cpp:228`, `:232`, `:239`, `:244`, `:251` |
| $95 to $98 echo the game number and give one four-byte BCD count | `lp.cpp:274` |
| $99 gives all four, in the order of $95 to $98 | `lp.cpp:9224` to `:9270` |
| The counts are the application's to write; the library only stores them | `libsas.cpp:1477`, `:1518` |
| The channel arrangement leaves the tournament bit alone | `libsas_utils.cpp:1223` |

## What the platform lacks, and what this machine does

* **A session** (`machine tournament on`): $8C with a time or credits zeroes the named game's four meters at once,
  and the session is entered at the next game that starts while the machine is free — no game, door, tilt, hand pay,
  cash out or menu (GSA 18.8). Zero time and zero credits ends it; another $8C replaces it.
* **Session credits are not money.** A session with credits plays on them: the credit meter and the accounting
  meters stay still, and the session ends when they are spent or a bet is more than is left. A session with time only
  plays on the player's credits, as any game does.
* **The time runs from the session's first game** (GSA 18.3); a game starting after it has run out is the player's.
* **Game zero counts every game**; any other number counts only that game's rounds. Credits are accounting units.
* The pulses byte is acknowledged and changes nothing. A restart ends the session; a memory clear zeroes the meters.

## How the cases learn what the machine does

* `machine tournament on` is the declaration; only a software machine built with it claims `machine.tournament`, and
  the manual backend offers it for a person at a cabinet that has tournaments.
* `decoder.lp94` to `decoder.lp99` exist now. The software machine still leaves $94 unanswered, so 20.1 and 20.2 skip
  on `machine.remote_handpay_reset`, group 17's.

## What it deliberately does not model

* Tournament pulses, which the document leaves open (18.6).
* A progressive or a hand pay won on session credits: the win goes back to the session.
* The remote hand pay reset itself ($94), group 17's.

## Script work done while bringing the cases up

* 18.3 to 18.10 declare tournaments; 18.8 declares a hopper too, for its tilt. The cases that play on one game
  declare a one-dollar accounting unit, so a one-dollar game credit is one unit, as their amounts assume.
* 18.7 remembered its meters before starting each new session and so asserted that a new session keeps them, which
  18.8 contradicts and 18.5 leaves open; it now remembers them once the new session has been asked for, as 18.5 does.
* 18.8's three by-hand blocks — a game in progress, an empty hopper, the operator menu — are now steps written from
  its notes.
* 18.10 declares two one-cent games and selects game one before its session, as its note asked of a person.
* 18.1 and 18.2 are unchanged: they are the machine-without-tournaments half.

## Findings against the documents

None.
