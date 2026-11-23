# M7 — Peripherals on the software machine

**Status: built 2026-11-23, under D44 and D46, as group 1 of M8.** The software machine has a
service button, and its note acceptor and printer can be put into a fault and taken out of it. The
service button alone lets the chapter 3 and 4 acknowledgement and queue cases run; the faults bring
up the chapter 4 note and printer cases and two chapter 16 transfer cases. The machine has no coin
acceptor, hopper or reels, because the platform it mirrors has none (D46).
Twenty-two cases now run: six in chapter 3, fourteen in chapter 4 and two in chapter 16; three of
them fail as labelled findings. The tree went from 209 pass, 25 fail, 398 skip to 228, 28, 376.

---

## The rule

D44 again: **the protocol half is the library's.** For a peripheral that half is only the
exception queue: the library writes nothing about a note acceptor, a printer or a lamp of its own
accord, and queues what the application gives it. So every code below is the platform's choice,
taken from its condition table, and every behaviour carries a one-line "QSP" comment in the code.

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| The service button toggles the service request: the first press raises it, the next clears it | QSP_DemoOpenGL `GameManager.cpp:788`, QSP `libqsp.cpp:2140` |
| The service request raises $71 on and $72 off | QSP `SasNetwork_Conditions.hpp:87` |
| Stacker full $27, jam $28, disconnected or malfunction $29, counterfeit $2C; none has a cleared code | QSP `SasNetwork_Conditions.hpp:70` to `:75`, `:96` |
| A returned note is not a condition: $2B is written directly, and never for a counterfeit | QSP `SasNetwork_Meters.hpp:110` to `:115` |
| Printer disconnected or malfunctioning $75, back $76; jam $78; paper out $61; paper low $74 | QSP `SasNetwork_Conditions.hpp:77` to `:82` |
| A condition already active raises nothing the second time | QSP `ConditionHandler.cpp:263` |
| A cleared code is raised only where the table has one | QSP `SasNetwork_Conditions.hpp:394` |
| Every fault but low paper interrupts the game | QSP `ConditionDefaults.h:67` to `:93` |
| An interrupted game refuses or withdraws the transfer lock, as an open door does | QSP `SasNetwork_AftTransfer.hpp:1339`; `lp.cpp:6588` |
| A fault makes a legacy bonus busy | QSP `SasGameRules.h:255` |
| The exception queue, its channels and its order are the library's, unchanged | `libsas.cpp` exception queue, D44 |

## What the platform decides, and what this machine says

* **Clearing** is one step per device, `egm note fault cleared` and `egm printer fault cleared`:
  reconnected, refilled, freed and reset by an attendant, which on the platform is the reset key for
  most faults and the device coming back for a disconnection (`ConditionHandler.cpp:895`).
  `egm settle` clears every fault too, as its description always said it would.
* **Which printer fault a disconnection is.** The platform reports a disconnected printer as powered
  off, $75, and never raises $60. 4.48 asserts $60 and fails as a labelled finding.
* **Excessive rejects** lock the platform after a run of returned notes; this machine never counts
  them, so every rejected note is one $2B and nothing more.

## How the cases learn what the machine does

* `stimulus.button.service`, `stimulus.note_fault` and `stimulus.printer_fault` are offered by the
  software backend, and by the manual backend, where a person does the same at the cabinet.
* `stimulus.note_fault.reversed` and `stimulus.printer_fault.ribbon` are offered by the manual
  backend only. 4.43 needs the first, so it skips on the software machine rather than failing an
  assertion after a step that could not happen.
* `stimulus.coin`, `stimulus.hopper`, `stimulus.reel`, `machine.partial_pay` and
  `machine.external_coin_acceptor` are offered by nothing in this build.

## What it deliberately does not model

* A coin acceptor, a hopper, a diverter and reels (D46): 4.21.2, 4.25, 4.34 to 4.39, 4.46, 4.47,
  4.53, 5.3.2, 5.4.2, 6.9 to 6.12, 11.20 and 15.22 keep skipping.
* A note pulled back and a worn ribbon, which the platform has no report for.
* The cash out a faulted printer cannot take becoming a hand pay, and restricted tickets, so 16.89
  stays notes only.
* Notes refused while the acceptor is faulted; a case that wants that inserts no note.
* The low backup battery, which is not a peripheral and has its own capability.

## Script work done while bringing the cases up

* 4.40 to 4.44.2 and 4.48 to 4.51 had notes only and now have bodies written from them. 4.51 runs
  its jam half; the ribbon half stays a note, owed only by a printer with a ribbon.
* 16.25 replaced its two notes with `egm printer fault disconnected` and `egm set aft bonus off`, and
  clears the printer after its step so the later refusals are the door's and the debit's, not the
  printer's.
* 16.87 had notes only and now has a body, using 16.62's transfer with the printer disconnected.
* 11.9 to 11.11 are still notes only: their sequences need a long poll that draws an event to be
  asserted as such, which the host has no step for yet.

## Findings against the documents

| Case | What the platform does |
|---|---|
| 4.48 | A disconnected printer is reported as powered off, $75, and nothing raises $60 (QSP `SasNetwork_Conditions.hpp:77`). |
| 16.25 | The first lock is answered locked at once, $00 not $40, on a playable machine with no game running (QSP `SasNetwork_AftTransfer.hpp:1119`); the debit transfer is refused $88 on an unregistered machine (`libsas_internal.cpp:2978`), so the lock after it is granted. |
| 16.87 | Refused at once with $87: a faulted printer takes the machine out of play, so the library refuses the transfer as one it cannot take now (`lp.cpp:5333`) before the receipt; with only receipts unavailable it refuses $90 at once (`lp.cpp:5367`), never pending first. |
