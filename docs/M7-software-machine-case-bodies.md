# M7 — The notes-only cases on the software machine

**Status: built 2026-11-25, under D44 and D57, as group 13 of M8.** The cases that held only notes
although every capability they name now exists on the software machine get bodies, written from the
document's steps as their notes describe them. To write them the host gains one assertion — which
exception a poll placed in a sequence drew, and in which format — and the software machine gains
the library's implied NACK on a repeated long poll and on a real-time event. Twenty-one cases run that
skipped before — 3.6, 4.20, 11.5, 11.8, 11.9 to 11.13, 11.15, 12.26, 13.33 to 13.35, 15.37, 16.1, 16.56,
16.70, 21.2, 22.25 and 22.26 — twenty passing and 16.70 failing as a labelled finding; the tree goes from
411/46/175 to 432/46/154 (raw 426/52/154, the difference being 16.36, corrected after the run, and the
known flakes 10.6.2 to 10.6.4, 16.60 and 23.13.2, each passing alone), every failure a finding.

---

## The rule

D44 again: **the protocol half is the library's.** Where a case needed the machine to do something it
did not, the behaviour comes from the library's handler, check for check; where the library hands the
decision to the application, the machine does what QSP does, in its simplest form, with a one-line
"QSP" comment. A case that needs something nobody offers keeps its notes and names the capability it
waits on (D57).

## What the machine now does, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| A real-time event is held until the host's next message acknowledges it, as a bare exception is; it is dequeued only then | `libsas_threads.cpp:3467`, `:3520` |
| A long poll identical to the one before it, command and check sequence alike, is an implied NACK and acknowledges nothing | `libsas_threads.cpp:964`, `:1165` |
| With reporting on, a refused event is still due, so the repeated long poll is answered with it again | `libsas_threads.cpp:3364` |
| The third identical poll is final; the exception goes back to the head of the queue, as the general poll already did here | `libsas_threads.cpp:369`, `libsas_db.h:96` |
| A jackpot limit or a credit limit saved by the operator reports $3C | QSP `SasNetwork.cpp:747`, `SasRules.h:391` |

## What the host does

* `host send lp <code> [data ...] draws event <exception> [or silence]` sends a long poll in a sequence
  and passes only when it is answered with that event in its window. Outside a sequence it fails, as a
  general poll placed by hand does.
* `host send gp draws event|bare <exception>` also asserts the format: the event format, or the single
  byte a general poll carries with reporting off. `draws any` passes on any exception and `or silence`
  admits no answer, for the final refusal the document allows either way.
* `egm power cycle without sync` skips the synchronisation the runner otherwise sends after a power cycle
  in a sequence, so 3.6 can poll the machine as it booted.

## What it deliberately does not do

* **Memory found invalid at start-up** (3.9): the platform then stays at address 0 and starts no link
  (QSP `SasNetwork.cpp:309`); the software machine raises its memory error while running.
  `machine.memory_fault.at_start_up`.
* **Restricted tickets** (10.3.6, 10.3.7, 10.4.1, 10.7.6, 10.7.7): the platform's option is off by
  default, as on this machine (QSP `SasNetwork.cpp:617`). `machine.restricted_tickets`.
* **Clearing the period meters alone** (4.27) and **the game recall screen** (11.6):
  `stimulus.period_meter_clear`, `stimulus.game_recall`.
* **Reading a page of the machine's menu** (AD-05c, AD-15, VC-04): the software machine's menus report
  entering and leaving only. `machine.audit_screens`, offered by the manual backend.
* **10.5.5 and 10.5.6** stay notes: their notes do not say enough of the two-host secure enhanced
  arrangement to write the steps from.
* **11.2** still needs coins, a hopper and reels (D46); **22.18.1** a multiplied progressive win (D52),
  `machine.multiplied_progressive_win`.
* **16.14** is a cross-reference to 16.8 to 16.11 with nothing of its own; **16.17** (16.8 to 16.11 again
  with reporting on) and **21.16** ($09 inside $B0) are left for a later pass and still skip as notes.

## Script corrections made while bringing the cases up

None loosens an assertion.

* 11.12 turns reporting on again after each power cycle: the document's later rounds ask the
  identification poll to carry an event, which a machine that has just powered up, with reporting off
  (11.5), cannot do.
* 13.33 broadcasts its level every second, as 13.32 does, rather than once.
* 16.70 sets the host cash-out flags on the transfer that loads the credit rather than on a separate
  zero transfer.
* 16.55 reads the completion and the doors with long polls that differ from each other: three general
  polls in a row are a refusal and a final one, which the machine now honours with reporting on.
* 12.26 no longer requires `meter.attendant_paid_progressive_win`, which no machine offers; the case reads
  0020 itself, as 12.21 and 13.35 do.
* 16.36 reads game locked and the doors with differing long polls, for the reason 16.55 does.
* 11.8 alternates the two addresses three times rather than for thirty seconds; only what follows is
  asserted.

## Findings against the documents

| Case | What the library or the platform does |
|---|---|
| 16.70 | No $6B: the library offers a win to the host only when the platform asks (`libsas_wrapper.cpp:3285`), and QSP hands a win past the credit limit to an attendant, as in 16.71. |
