# M7 — The remaining decoders and odds and ends on the software machine

**Status: built 2026-11-25, under D44 and D56, as group 12 of M8.** The host now decodes long polls
$06, $07, $21, $76, $7C, $7D and $A4, hears the ROM signature on whichever poll it arrives, and can
ask a machine whether it reports a cash-out limit. The software machine switches its note acceptor on
and off, computes a ROM signature, prints the ticket text a host sets, keeps custom transfer ticket
data, leaves $A4 unanswered as its platform does, and has an attendant and an operator menu.
Thirteen cases run that skipped before — 4.2, 4.28, 4.29, 10.3.11 to 10.3.14, 11.25, 21.5, 21.6 and
21.8 — eleven passing and two failing as labelled findings; the tree goes from 403/43/186 to 412/45/175,
every failure a finding (13.10's known cadence flake failed once in the full run and passes alone).

---

## The rule

D44 again: **the protocol half is the library's.** Each handler applies the library's tests in its
order and stays silent where it does. Where the library hands the decision to the application — the
signature itself, what the acceptor does, what a ticket prints, whether a cash-out limit exists — the
machine does what QSP does, in its simplest form, with a one-line "QSP" comment at each place. What
QSP lacks is either declared or an honest skip (D56).

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| $06 and $07: the general control category gate, the address, a bad check sequence refused, then acknowledged | `lp.cpp:348` (`lpXX_EnableDisable`) |
| $08: the denomination mask and the action byte handed to the application | `lp.cpp:411`, `:449` |
| $21: the address, a bad check sequence refused, the seed (most significant byte first) kept for the channel that asked, acknowledged | `lp.cpp:1460` |
| A pending signature answers the next general poll ahead of any exception: address, $21, two bytes, check sequence | `libsas_threads.cpp:3369`, `:3546` |
| With reporting on it takes an addressed long poll's window only while an exception is queued | `libsas_threads.cpp:3367` |
| Any message but a repeated general poll acknowledges a signature sent, and it stops; a loop break means it goes again | `libsas_threads.cpp:332`, `:423`, `:805`, `:129` |
| $7C: validation category gate, own address or 00, data codes $00–$02 up to 40 characters and $10, $20 up to 16; an unknown code or a line too long ends the poll unanswered with the flag cleared | `lp.cpp:7353` to `:7470` |
| $7C and $7D answer the status flag only when addressed, and clear it once answered | `lp.cpp:7482`, `:7663` |
| $7D: own address or 00; a length of 1 refused silently; a length of 2 keeps the identifier and clears the lines; three lines in order, each up to 40 | `lp.cpp:7519` to `:7660` |
| $76: AFT category gate, silent without transfers, own address or 00, a line as long as its buffer or an unknown code refused, $80 clears all; the reply lists every code held | `lp.cpp:6817` to `:6980` |
| $A4: information category gate, the address, silent beyond the game count, silent while no limit is configured; game and limit in two BCD bytes each | `lp.cpp:9465` |

## What the platform decides, and what this machine says

Each of these carries a "QSP" comment in the code.

* **The note acceptor.** $07 sets and $06 clears the platform's network disable bit, kept in its
  memory, and the acceptor is inhibited while it is set (`SasNetwork_Callbacks.hpp:486`); a note
  offered then, or one $08 has disabled, is handed back without a word. **The action byte of $08 is
  unused** (`SasNetwork_Callbacks.hpp:508`), so the acceptor never stops itself after a note: 21.8's
  finding.
* **The ROM signature.** QSP hashes its configured targets, chained from the seed, or hands the seed
  to the game, and registers what comes back (`SasNetwork_Callbacks.hpp:608`, `:758`). The software
  machine signs a fixed stand-in for its program with the protocol's own CRC-16, started from the
  seed, so a seed always gives one signature and two seeds give two; it is ready at once.
* **Ticket text.** A printed ticket carries $7C's line where $7C set one, else $7D's, else the
  operator's, which the platform leaves empty (`SasNetwork_Ticket.hpp:1229`,
  `TicketDataProvenance.h:116`). A memory clear empties both.
* **Custom transfer ticket data.** QSP keeps nothing of $76, as it prints no transfer tickets
  (`SasNetwork_Callbacks.hpp:159`); the library's half answers as it does.
* **The cash-out limit.** QSP never calls `SAS_SetCashoutLimits`, having no hopper, so $A4 is silent.
* **The menus.** The attendant key reports $82 on entering the audit screens and $83 on leaving
  (`SasNetwork_Conditions.hpp:943`); the operator mode condition reports $84 and $85
  (`SasNetwork_Conditions.hpp:95`).

## What the host does

* $06, $07, $21, $76, $7C, $7D and $A4 have layouts; $76, $7C and $7D may be sent to 00, which no
  machine answers.
* **The ROM signature** is recognised by its shape — the machine's address, $21, two bytes, a good
  check sequence — on a general poll (a new outcome) or in a long poll's window (a new outcome, not a
  success, as an event there is not). Either way it is reported to the run.
* `host expect rom signature within <d>` waits for the next signature heard on that channel from that
  machine, on whichever poll; `host expect no rom signature within <d>` passes on none. A signature
  sent twice to a repeated poll is read once.
* `machine.cashout_limit` is asked of the machine with $A4 for game zero, and holds the limit; silence
  leaves it absent.
* `egm menu attendant|operator enter|leave` needs `machine.audit_menu`, which the software and manual
  backends offer; the manual backend also offers `machine.rom_signature`, the manufacturer's figure.

## What it deliberately does not do

* **Memory faults the platform never reports** (4.15 to 4.19): D48 stands. QSP raises $32 and $36 and
  nothing else of $31 to $39, so those capabilities stay with the manual backend and the cases skip
  whole.
* **Chapter 17 and 21.13, 21.14 skip**, on `machine.custom_ticket_data` and `machine.cashout_limit`.
* **The menus take the machine out of nothing.** On the platform the audit key and operator mode are
  conditions that stop play; the software machine only reports them, as no case here plays inside one.
* **$7D's expiration** is stored by the library and not printed; the software machine's tickets carry
  no expiration.
* **5.10** still needs a broken check sequence (`host.bad_crc`). **11.2, 16.1, 21.2, AD-05c, AD-15 and
  VC-04** now meet `machine.audit_menu` but skip on other requirements or have no steps yet; writing
  those is the notes-only group's.
* A signature computed over minutes: the software machine's is immediate, so the waits a real machine
  needs are the cases' own.

## Script corrections made while bringing the cases up

None loosens an assertion.

* 21.13 and 21.14 also require `machine.cashout_limit`, so they skip for the true reason: the library
  answers $A4 only once a limit is configured.
* 21.7's note no longer says the software stimulus fails on a refused note; it hands the note back.
* The headers of 17.2 to 17.5 and 21.13, 21.14 and the note of 5.10 no longer say a decoder is missing.

## Findings against the documents

| Case | What the library or the platform does |
|---|---|
| 21.8 | The action byte of $08 reaches the application (`lp.cpp:449`) and the platform leaves it unused (QSP `SasNetwork_Callbacks.hpp:508`), so with action 0 the acceptor goes on taking notes. |
| 11.25 | With reporting on and nothing queued, a long poll is answered as itself; the library sends the signature in a long poll's window only while an exception is queued (`libsas_threads.cpp:3367`), so it waits for a general poll. |
