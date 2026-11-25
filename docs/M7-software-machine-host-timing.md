# M7 — Host timing and line behaviour on the software machine

**Status: built 2026-11-25, under D44 and D55, as group 11 of M8.** The software machine now chirps after
a loop break, takes the clock the host sends ($7F), and moves to whatever poll address the operator
saves; the host can listen to a quiet line for the chirp, send a poll without its wakeup bit, follow
the machine to a new address, and time how often an exception comes again. Ten cases run that skipped
before — 3.2, 3.4, 3.7, 3.8, 3.19, 5.16, 16.40, 16.53, 16.66 and 19.13 — eight passing and two
failing as labelled findings; the tree goes from 395/41/196 to 403/43/186, every failure a finding.

---

## The rule

D44 again: **the protocol half is the library's.** The chirp, the address tests and $7F apply the
library's tests in its order and stay silent where it does. Where the library hands a decision to
the application — the address itself, and what a clock change does — the machine does what QSP
does, in its simplest form, with a one-line "QSP" comment at each place.

The host half is this build's, and each step does the thing it names on the wire or fails saying
why. Nothing is offered that the host cannot honestly do (D2): a collision and a gap between two
bytes of one reply stay skipping, below.

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| A channel that has heard no message for five seconds is in loop break | `libsas_threads.cpp:2410` |
| In loop break it sends its own address every 200 ms timer tick, with the wakeup bit, the only transmission that carries it | `libsas_threads.cpp:2548`, `libsas_uart.cpp:669` |
| A channel at address 0 never chirps | `libsas_threads.cpp:2548` (`address != 0`) |
| Any message on the line, whoever it is addressed to, resets the loop-break timer and ends the chirp | `libsas_threads.cpp:1614` |
| Bytes without the wakeup bit before an address byte are skipped and reset nothing | `libsas_uart.cpp:488` to `:500` |
| A data poll to address 00 is dropped by its handler (every handler but $7F, $80 and $86) | `lp.cpp:1299` |
| $7F: own address or 00, else silent; a bad check sequence refused | `lp.cpp:7752`, `:7756` |
| $7F: month 1–12, day 1–31, year from 1900, hour, minute, second in range, else refused | `lp.cpp:7772` |
| $7F: acknowledged only when addressed to the machine; a broadcast is taken silently | `lp.cpp:7790` |
| $7E answers from the machine's own clock | `lp.cpp:7700` |
| A library started at an address waits for a synchronisation poll before answering its own polls | `libsas_threads.cpp:1518` |
| Reporting in the event format survives a restart only at the same address | `libsas_internal.cpp:386` |

## What the platform decides, and what this machine says

Each of these carries a "QSP" comment in the code.

* **The poll address is the operator's.** `egm set address <aa>` saves a new first-channel address.
  Saving the one already set restarts nothing (`SasRules.h:412`); a changed one restarts the library
  there, the queue kept with nothing in it sent, as at a power-up, and $3C reported once it is up
  (`SasNetwork.cpp:753`, `:855`).
* **Address 0 starts no library** (`SasNetwork.cpp:331`), so the machine hears nothing — no poll,
  no broadcast — never chirps and reports no $3C until an address is set again.
* **The clock** is the machine's own local time; a date its calendar cannot place, such as February
  30, is acknowledged by the library and then left alone by the platform
  (`SasNetwork_Callbacks.hpp:799`).

## What the host does

* `host expect chirp within <d>` listens to the quiet line of a sequence and passes on three chirps
  of the machine's address, each 100 to 300 ms after the one before; anything else heard fails it.
  `host expect no chirp for <d>` passes on a silent line. On a line the round is polling, both fail
  rather than hear nothing.
* A chirp that lands in a poll's reply window — one wakeup byte that is the machine's own address —
  is recorded and passed over, not read as the reply: it is no answer, and a real machine can send
  one a moment after the host's drain.
* `host send lp <hh> ... without wakeup` sends the poll with its address byte's wakeup bit clear and
  passes on silence or a refusal. Every transport this build opens carries the bit symbol by symbol,
  so leaving it off is deliberate and never a degradation (D2).
* `host poll address <aa>` points a host bound to one machine at the machine's new address, as a
  site would repoint its host. Polls to the old address are then polls to a stranger.
* `host expect exception <name> every <period>` waits for the next report of that exception and
  checks it came a period after the last one, within a quarter of the period or two poll intervals,
  whichever is larger: times are when the host read each one off the wire.
* `host.chirp`, `host.poll_without_wakeup` and `host.timing` are therefore the build's, in the
  baseline; `machine.address_configuration` is offered by the software and manual backends.

## What it deliberately does not do

* **A collision** (3.14). A transport hands a reply over whole, so the host has no moment part way
  through one at which to send; `host.collision` stays absent.
* **A gap between two bytes of one reply** (3.12). No transport this build opens times each symbol,
  so 3.12 now also names `host.byte_timing`, which nothing offers, rather than passing a check that
  cannot fail. 3.11 also needs a hopper (D46).
* **3.6**, a cold start ignoring polls until a synchronisation poll: in a sequence the runner
  resynchronises a machine after every restart, so the silence the case asks for cannot be had
  without turning that off. Left as notes.
* **11.8 and 11.13**: their point is that events keep the event format across a loop break, and no
  step yet asserts the format a general poll's answer came in; 11.13 also needs long polls answered
  with events (D43). Left as notes.
* **16.56**, the event repeat: with reporting on, the status read meets the queued event and fails
  its step (D43).
* **3.9**, the address reset by a memory fault, stays notes only: QSP starts no link when it finds
  its memory invalid at power-up (`SasNetwork.cpp:309`), which is the silence the case asks for, but
  writing it is the notes-only group's.
* More than the first channel's address, and the configuration screen's range check.

## Script corrections made while bringing the cases up

None loosens an assertion.

* 3.12 also requires `host.byte_timing`, so it skips for the true reason rather than for "no steps".
* 3.5.1 listens for the chirp in each of its silences instead of only waiting six seconds.
* 3.13 asserts the chirp past the top of its range, and the host's next poll ending it.
* 5.16 sends its first clock as the broadcast the document describes, and reads it back.
* 19.13 times its two repeats before the power cycle at fifteen seconds instead of "within twenty".
* 16.40 states the asset number its transfers are addressed to, waits for each transfer's $69 before
  reading it, and declares the one-cent unit its one-cent transfer needs; it had never run before.
* 3.4 uses the synchronisation poll for the document's poll to address 00, as 3.5.1 does, since a
  data long poll may not be sent to 00 from a script.
* The software machine no longer answers a data long poll sent to 00, which it did and the library
  never does (`lp.cpp:1299`).

## Findings against the documents

| Case | What the library does |
|---|---|
| 16.40 | A transfer reusing an earlier identifier is refused $81 only when that identifier is the most recent transfer's (`lp.cpp:5426`); the dollar's identifier sent after the cent's is taken as a new transfer. |
| 19.13 | The component state and the $8F timer are kept outside the library's non-volatile image (`libsas_db.h:1121`; the image is `sNvramData` at `:855`), so after a power cycle no $8F comes and the result reads $FF. |
