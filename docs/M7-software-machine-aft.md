# M7 — Funds transfer on the software machine, phases 1 and 2

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
| A loop break on the channel that owns AFT ends the lock as it happens; other channels leave it | `libsas_threads.cpp:2542` |
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
* **A host cash-out the host's own transfer did not finish** - cancelled, failed, or leaving credits
  behind - is paid the usual way at once, as when the request runs out (QSP's `AftCashoutFallback.h`;
  the library leaves this to the platform).
* **A partial transfer from the machine** is `$01` when it moved less than it named, the all-available
  amount included, as the library's own black-box test expects (`test/blackbox/blackbox_test.cpp:1077`).
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
* A hand pay still goes to the head of the queue, while the AFT exceptions are ranked as the library ranks them.
* The transfer limit is a constant, not the configured maximum less the credits on the machine.
* A second collect press while a host cash-out is outstanding is not modelled.
* Some refusal checks cannot be reached with the transfers this machine offers (bonus, ticket, debit amounts).
* The library never writes the position byte of a `$C0` or `$C1` answer (`lp.cpp:5967` to `:5979`);
  this machine sends zero.

## Findings against the documents

| Case | What the library does |
|---|---|
| 16.24 | A lock naming no transfer condition is refused `$FF` (`lp.cpp:6505`); the document expects locked. |
| 16.30 | A lock with no timeout is taken (`$00`, not `$FF`) and then released at once on Windows (`libsas_windows.cpp:857`) or never on Linux (`libsas_linux.cpp:315`). |
| 16.32 | The platform offers transfer from the machine with nothing on it, so the lock stands. |
| 16.82, 16.83 | A transfer that needs a lock is refused in the reply and raises no `$69` (`lp.cpp:5373`, `:5515`). |

---

# Phase 2 — registration, bonus awards, receipts

**Status: built 2026-11-20, under D45.** The software machine answers long poll $73, takes bonus
awards of both kinds, holds non-restricted credit, and reports and prints transaction receipts; the
host decodes $73. Twenty-six more GSA cases run, 200 of 632: chapter 14 runs 10 of 11 and chapter
16 runs 43 of 90. All 15 failures in the suite are labelled findings; the regression tree stays
green at 50 of 50.

## What it models, and where each behaviour comes from

| Behaviour | libsas source |
|---|---|
| $73 NACKs an unknown code, and a code carrying data in fewer than $1D bytes | `lp.cpp:6146`, `:6156` |
| $73 is silent while AFT is off in the enabled features | `lp.cpp:6173` |
| Another asset number, or none, cancels whatever registration there was | `lp.cpp:6207` |
| Initialise: ready, and the key and point of sale are cleared only when sent as zero, never stored | `lp.cpp:6236` to `:6277` |
| Register: only from ready and with a non-zero key; stores key and point of sale | `lp.cpp:6279` |
| Acknowledgement request: pending, only from ready | `lp.cpp:6313` |
| Cancel, or any step out of order: not registered, $6E when that is a change | `lp.cpp:6328` |
| The reply carries what the machine holds, not what the host sent | `lp.cpp:6371` |
| A loop break on the AFT channel cancels a ready or pending registration | `libsas_threads.cpp:2519` |
| A new asset number cancels a registration that was complete | `libsas.cpp:2186` |
| $74 adds the registered bit to the AFT status from the live registration | `lp.cpp:6630` |
| The operator's action: $6C from not registered, $6D and ready from pending | `libsas_wrapper.cpp:3056` |
| $6C and $6D ranked 12 and 13 among the priority exceptions; $6E queued plainly | `libsas_internal.cpp:3835` |
| Registration governs debit transfers alone: $88, $89, $8A in that order | `libsas_internal.cpp:2971` |
| A transfer to a ticket of nothing is $82 | `lp.cpp:5356` |
| A receipt the printer cannot make is $90; an in-house receipt with no patron account, or a debit one with no card, $91; a bonus receipt $92 | `lp.cpp:5366`, `:5406`, `:5412` |
| A bonus of nothing still takes a history position | `lp.cpp:5497` |
| A bonus keeps the expiry and pool it carried | `lp.cpp:5609`, `:5612` |
| Bonus transfers move meters $AE to $B1 on success | `libsas_internal.cpp:3300` |
| A receipt settles from $20 to $00 later, reopening a transfer already read and raising $69 again | `libsas_internal.cpp:3543`, `:3654` |
| AFT bonus awards are bit 1 of the first feature byte, cleared on a channel without AFT | `libsas.h:187`, `libsas_utils.cpp:1240` |

## What the platform decides, and what this machine says

Each of these carries a "platform's choice" comment in the code.

* **Configuration** is QSP's: transfers in and out and bonus awards ($13), in-house, bonus,
  less-than-full and any-AFT, plus receipts because this machine has a printer ($B3). AFT bonusing
  is on by default and can be turned off with `egm set aft bonus off`, which, being a change of AFT
  configuration, cancels any registration as QSP's `AftConfigurationChanged` does.
* **A coin-out bonus** is a win the machine pays onto the credit meter, moving total coin out and
  machine-paid external bonus as well as the library's bonus meters.
* **A jackpot bonus** is always a hand pay, even of nothing (QSP `ShouldPerformBonusAward`). The
  transfer completes as the machine locks up; the attendant's reset moves total jackpot and
  attendant-paid external bonus.
* **Non-restricted credit** is held as part of the credit meter and reported in $74. It is wagered
  before cashable credit and counted as non-restricted played; a cash-out takes it on the same
  cashable ticket.
* **A debit transfer** the library accepts fails at the platform with $82, because QSP performs
  none (`AftTransferTypePerformed`).
* **The receipt status** of a completed transfer is $20 for a non-zero success that asked for one
  and may carry one, $FF otherwise (QSP `ResolveAftTransactionReceipt`). The receipt prints after
  `AftReceiptPrintDelay`, one second by default, into `receipts/` beside the wallet, so no ticket
  reader takes it for a ticket. Its text is this build's own.
* **The operator's register action** is `egm aft register`. It is one action, as QSP has it: a
  request to register from not registered, an acknowledgement from pending, refused otherwise.

## How the cases learn what the machine does

* `machine.aft_bonus` is read off bit 1 of the first enabled-features byte, the same question asked
  of either peer.
* `machine.aft_receipts` is read off bit 0 of the AFT status a $74 read reports. A machine that
  cannot take a transfer at that moment reports zero there, and the case skips saying so.
* `machine.aft_operator_registration` is claimed by the software backend, because the software
  machine has the menu. The two new steps, `egm aft register` and `egm set aft bonus on|off`,
  need `stimulus.aft_registration` and `stimulus.aft_bonus`, which the software and manual backends
  both offer: a person works the same menu on a real machine.

## What phase 2 deliberately does not model

* Restricted credits and their pools: a transfer carrying them still fails at the platform with
  `$9F`.
* Transfers to a ticket: the platform performs none, so the library refuses them `$82`.
* Credit and jackpot limits, so 14.7 still skips.
* Persistence: the machine is rebuilt for every case, so a registration surviving a power cycle is
  not something it can show.
* Bonus transfers are not counted in the electronic-transfer credit meters ($17 and its family).
* `$1F` when idle with bonusing on: QSP never raises it, so 14.11 holds with bonusing on as well.

## Findings against the documents, phase 2

| Case | What the library does |
|---|---|
| 14.2 | A jackpot bonus carrying a registration key is taken, not refused `$88`: the key is checked for debit transfers only (`libsas_internal.cpp:2974`). |
| 14.8 | A bonus keeps the expiry and pool it carried and reports them back (`lp.cpp:5609`, `:5612`); the document expects zeros. |
| 16.41, 16.46 | A receipt for an in-house transfer needs a patron account in the receipt data, so the document's request is refused `$91` (`lp.cpp:5406`). |
| 16.63 | A transfer to a ticket of nothing is refused `$82` at once, with no `$69`; the document expects it pending first (`lp.cpp:5356`). |
