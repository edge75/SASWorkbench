# M7 — Funds transfer on the software machine, phase 1

**Status: built 2026-11-20, under D45.** The software machine takes AFT locks and in-house
transfers the way the machine library does, and the host decodes long poll $72. The twenty-six
validation cases that skipped on `machine.aft` or `decoder.lp72` alone now run: twenty-one pass and
five fail as labelled findings. Over the whole GSA tree that is 164 passed, 10 failed (all findings)
and 458 skipped, against 145, 5 and 482 before.

---

## The rule

D44 again: **the protocol half is the library's.** Each refusal is the code `lp72_TransferFunds`
picks, checked in its order; the lock, the history and the exceptions follow the library's state
machine and timers. Where the library leaves a decision to the application, the machine answers as
the platform above it does (QSP's `AftAvailability.h` and its lock provider), reduced to what this
machine has. Every such place carries a one-line comment saying it is the platform's choice.

## What it models, and where each behaviour comes from

| Behaviour | libsas source |
|---|---|
| $74: read, lock, cancel; a condition the machine does not offer refuses and withdraws any lock | `lp.cpp:6385` `lp74_LockAndStatusRequest`, refusal at `:6505` |
| The lock runs in five-second steps, re-raising $6F each step, and ends when the timeout runs out | `internalLockGame` `libsas_internal.cpp:2261`, lock timer `libsas_threads.cpp:2560` |
| $6F only on the way into a lock; withdrawn from the queue when the lock ends | same, and `internalDequeueSpecificException` |
| A loop break ends the lock | `libsas_threads.cpp:2542` |
| $72 request parse and NACKs (identifier length, receipt fields) | `lp.cpp:4986` to `:5183` |
| The refusal chain, 82 to 95, in order | `lp.cpp:5242` to `:5473` |
| Pending reply: requested amounts, zero date, meters of size 0 | `lp.cpp:6000`, `:5618` |
| A refusal is final at once, stamped and with meters, and at position 0 | `lp.cpp:5623` to `:5638` |
| $C0 during a cycle, $C1 for an unknown code, each with position and status alone | `lp.cpp:5665`, `:5964`, `:5974` |
| Completion moves the cents and count meters, unlocks, raises $69, reissues it every 15 s | `internalUpdateAftTransfer` `libsas_internal.cpp:3073`, `:2909` |
| Reading the final status closes the cycle, advances the 127-slot history and stops $69 | `lp.cpp:5889` to `:5935` |
| History positions, relative positions $81 to $FF, the empty answer of three bytes | `lp.cpp:5746` to `:5875`, `:5985` |
| Priority exceptions in the library's rank, never queued twice | `internalGetExceptionPriority` `libsas_internal.cpp:3796`, `internalQueueException` `:3931` |
| The host's cash-out bits take effect when the transfer carrying them succeeds | `libsas_internal.cpp:3234` |
| Collect with cash-out to the host on: $6A every 800 ms for eight seconds, a $74 holds it back, a transfer from the machine ends it | `sasAftGmToHost` `libsas_wrapper.cpp:3272`, timer `libsas_threads.cpp:2634`, `lp.cpp:5213`, `:6480` |

## What the platform decides, and what this machine says

* **Available transfers** are to and from the machine whenever it is playable, none while a door is
  open, a hand pay is owed or the host has disabled it, and only from the machine while a host
  cash-out is outstanding. AFT status is in-house, partial to host, and AFT on (`$92`).
* **A lock is granted at once** when the machine is playable, because the platform's lock provider
  does that and no game is ever left running here. So a lock answers `$00`, never `$40`.
* **The money moves** after `AftCompletionDelay` (one second by default, so a host reading back at
  once still finds the transfer pending). Cashable credits only.
* **The asset number** is the option's, unless a case sets one with `egm set asset number <n>`.
* `machine.aft` is read off the AFT bit of the machine's enabled-features reply, the same question
  asked of either peer.

## What it deliberately does not model

* Registration and debit transfers ($73): every debit transfer is refused `$88`.
* Bonus, ticket and receipt transfers: the configuration offers none, so the library's own checks
  refuse them.
* Restricted and non-restricted credits: a transfer carrying them fails at the platform with `$9F`.
* A zero cash-out from the machine holds its reply until it completes in the library; here it
  completes before the reply goes, which is the one answer a host sees either way.
* The library never writes the position byte of a `$C0` or `$C1` answer (`lp.cpp:5967` to `:5979`);
  this machine sends zero.

## Findings against the documents

| Case | What the library does |
|---|---|
| 16.24 | A lock naming no transfer condition is refused `$FF` (`lp.cpp:6505`); the document expects locked. |
| 16.30 | A lock with no timeout is taken (`$00`, not `$FF`) and then released at once on Windows (`libsas_windows.cpp:857`) or never on Linux (`libsas_linux.cpp:315`). |
| 16.32 | The platform offers transfer from the machine with nothing on it, so the lock stands. |
| 16.82, 16.83 | A transfer that needs a lock is refused in the reply and raises no `$69` (`lp.cpp:5373`, `:5515`). |
