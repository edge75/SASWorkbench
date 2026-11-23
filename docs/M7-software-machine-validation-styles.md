# M7 — Validation styles on the software machine

**Status: built 2026-11-23, under D44 and D47, as group 2 of M8.** A case chooses the validation
style with `machine validation standard|secure_enhanced|system`; the software machine is built
performing it, and any other machine is asked over the link and the case skips when it performs
another. The machine keeps the library's validation buffer, its identifier and sequence number, the
system validation exchange and the timers behind $3D, $3F and $57, and prints, refuses or pays by
hand as the platform above the library does. Thirty more chapter 10 cases now run, all passing, and 23.13.2 runs under system validation as its document has it. The tree went from 228 pass, 28 fail, 376 skip to 258, 28, 346.

---

## The rule

D44 again: **the protocol half is the library's.** Every handler applies the library's tests in the
library's order, and stays silent where the library returns without sending. Where the library hands
the cash-out to the application — whether to print, what to do with a refusal, a denial or a timeout
— the machine does what QSP does, in its simplest form, with a one-line "QSP" comment at each place.

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| $4C: silent unless secure enhanced; a non-zero identifier restarts the sequence only when the identifier, the base or a ticket printed since says so | `lp.cpp:2454`, `:2479` |
| $4C: identifier and sequence number reported three bytes each, least significant first | `lp.cpp:2512` |
| $3F at power-up and every fifteen seconds while the identifier is unset; $4C stops it | `libsas.cpp:1999`, `libsas_threads.cpp:3213`, `lp.cpp:2497` |
| The secure enhanced number: sequence advanced first, three check sequences, two check digits | `libsas_internal.cpp:5630` |
| sasStartCashout: refused while a validation is in progress or the identifier is unset; write index wraps from 31 to 1 | `libsas_wrapper.cpp:2231`, `:2242`, `:2261` |
| internalCompleteCashout: record completed, counted unread (at most 31), $3D only for the first unread | `libsas_internal.cpp:5800`, `:5978`, `:5989` |
| $3D unanswered under secure enhanced and system | `lp.cpp:2143` |
| $4D: silent under standard; 00 and FF read the oldest unread, any other code is a slot, past 31 every field zero | `lp.cpp:2582`, `:2604`, `:2675` |
| The read index, skipping a record whose cash-out has not completed | `libsas_internal.cpp:5714` |
| $4D 00 retires a record on the implied acknowledgement; the same poll again is a NACK and retires nothing more | `lp.cpp:2720`, `libsas_pending.cpp:63`, `libsas_threads.cpp:678` |
| $3D reissued every fifteen seconds while records are unread, and once more after each retirement while any remain | `libsas_threads.cpp:2338`, `libsas_pending.cpp:94` |
| Ten seconds unread is announced to the platform | `libsas_internal.cpp:2468`, `libsas_threads.cpp:2365` |
| $57 every 800 ms until $57 is read; ten seconds to wait for $58 | `libsas_wrapper.cpp:2327`, `libsas_threads.cpp:2791` |
| $57: reading it moves a started cash-out on; type and amount of the newest record, or $80 and zero | `lp.cpp:3442`, `:3461` |
| $58: non-BCD system ID read as zero; zero or no $57 first is a denial that cancels the cash-out; statuses 00, 80, 81 | `lp.cpp:3571`, `:3580`, `:3660`, `libsas_internal.cpp:2501` |
| A cancelled cash-out puts the overwritten slot back and steps the write index back | `libsas_internal.cpp:5845` |
| $7B without the automatic reply: masked bits set within $803F or cleared; reply is the host's word and the machine's | `lp.cpp:7071`, `:7236` |
| $7B bit 15 cleared cancels the identifier, reports $3F and restores the default expirations | `lp.cpp:7243` |

## What the platform decides, and what this machine says

Each of these carries a "QSP" comment in the code.

* **The style** is the operator's; QSP has no fourth "no validation" style on the wire, so a machine
  with no host validation is a standard one (`CashoutPrinterRules.h:191`). The enhanced styles set
  the extensions and redemption bits (`SasNetwork.cpp:290`).
* **What the machine can do**, the library's machine status word, is $8021: QSP clears handpay
  receipts and handpay validation (`SasNetwork.cpp:617`), and this machine keeps restricted credit
  on the meter, so the two restricted ticket bits are clear.
* **When it may print**: not while the host has withdrawn the printer ($7B bit 0), and under the
  enhanced styles not while the link is down or a record has been unread ten seconds
  (`CashoutPrinterRules.h:65`). Otherwise, and when the library refuses to start the cash-out, the
  cash-out is a cancelled-credit hand pay (`SasNetwork_Ticket.hpp:756`, `:1040`).
* **Ticket numbers** wrap at ten thousand and are taken before the library is asked
  (`SasNetwork_Ticket.hpp:288`). The expiration reported is the one printed, as days.
* **System validation** prints only once $58 grants it, once per cash-out; a denial or the ten-second
  timeout is paid by hand (`SasNetwork_Ticket.hpp:1093`, `SasNetwork_Callbacks.hpp:329`).
* **Redemption withdrawn** with $7B bit 5 returns an inserted ticket unasked
  (`SasNetwork_Ticket.hpp:286`).
* **A full buffer** is an inhibiting lockup (`SasNetwork_Callbacks.hpp:367`).

## How the cases learn what the machine does

* `machine validation <style>` is a declaration, like `machine accounting denomination`: it builds
  the software machine in that style and is asked of any other machine as `egm.validation`.
* `host.validation_sequence` is true of this build: the host forms and decodes $4C, $4D, $57 and the
  new $58 layout, step by step.
* `machine.buffer_depth` (31) is claimed by the software backend, since no poll reports it.
* `machine.handpay_validation` and `machine.handpay_receipts` stay absent: the platform validates no
  hand pay and prints no receipt.

## What it deliberately does not model

* Hand pay validation and receipts (the platform has neither), so 10.3.4, 10.3.5, 10.5.7 and 10.5.9
  keep skipping on them, and $3E never arises.
* Restricted and debit tickets, ticket data ($7C, $7D) and the validation meters by name.
* A second host that owns no validation (10.5.5, 10.5.6).
* The secure enhanced condition on the platform's screen, and persistence across a power cycle.
* The standard style's refusal of tickets: the platform turns a ticket away under standard
  validation, and this machine still takes one there, as cases in chapters 10 and 23 rely on.

## Script work done while bringing the cases up

* Every enhanced-style case now declares its style rather than requiring it.
* 10.5.1 required a style named `none`, which no machine reports; it is now the secure enhanced
  machine with no identifier, which raises $3F and pays a cash-out by hand.
* 10.5.12, 10.6.1 to 10.6.10 and 10.7.19 had notes only and now have bodies; 23.13.2 declares system validation, as its document asks, and answers the request with $57 and $58. 10.6.1 checks the style
  the machine reports, as no long poll configures system validation; 10.6.7 reads the host's number
  through $4D, as 10.5.3 does, since the style leaves $3D unanswered.

## Findings against the documents

None. One library behaviour is mirrored and worth knowing: a $58 that arrives with no cash-out
waiting zeroes the number of the newest record (`lp.cpp:3584`), whatever the style.
