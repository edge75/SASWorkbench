# M7 — The hand pay queue on the software machine

**Status: built 2026-11-24, under D44 and D53, as group 9 of M8.** The software machine now keeps the
library's hand pay queue on every channel: a hand pay reset before the host read it stays queued behind the
next one, the readings come out oldest first, $51 is announced again every fifteen seconds while the front
hand pay awaits its reading, a full queue of sixteen loses its oldest, and a power cycle announces an unread
hand pay again. A case can declare `machine handpay legacy` for the legacy reporting of SAS 6.02 7.8.2, and
`machine handpay validation on` for a machine that validates hand pays. Eleven cases that skipped now run: ten
pass and 5.8.2 fails as a labelled finding. The tree went from 356 pass, 38 fail, 238 skip to 366, 39, 227;
every failure is a labelled finding.

---

## The rule

D44 again: **the protocol half is the library's.** Register, the $51 and $52 acknowledgements, long poll 1B,
the dequeue its acknowledgement runs and the attendant's reset are ported check for check, with their state
names, in the library's order, and silent where it is silent. Where the library hands the decision to the
application, the machine does what QSP does: one lockup at a time, the standard reset ID, and the reporting
mode chosen at a memory reset. QSP performs no hand pay validation, so that half is on only when a case
declares it (D53).

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| A hand pay is refused until the last one is reset; a reset one stays queued | `libsas_internal.cpp:4834`, `:4840` |
| Sixteen per channel, the oldest overwritten when full | `libsas_db.h:72`, `libsas_internal.cpp:4855` |
| The amount in accounting units unless a progressive contributed, then in cents | `libsas_internal.cpp:4895` |
| An acknowledged $51 marks the front announced and starts the timer; otherwise every waiting entry | `libsas_internal.cpp:4981` to `:5005` |
| An acknowledged $52 dequeues the entry it reported | `libsas_internal.cpp:5014` |
| The dequeue: a read entry is reset ($52) if the attendant already reset it, else read; a reset entry leaves; the next is announced ($51) | `libsas_internal.cpp:5068` to `:5165` |
| A $52 is dropped while the front entry waits behind others | `libsas_internal.cpp:3993` |
| 1B reports the first entry still owed, zeros for a reset or read one, and a read one again in legacy mode | `lp.cpp:1022` to `:1037` |
| A reading moves the entry on, takes queued $51 back and leaves the dequeue to its acknowledgement | `lp.cpp:1080` to `:1110` |
| The reset is counted, and $52 is sent at once when the reading was acknowledged, or in legacy mode whenever the front is still owed | `libsas_internal.cpp:5276` to `:5335` |
| $51 every thirtieth half-second tick while the front awaits its reading, never in legacy mode | `libsas_threads.cpp:3014` to `:3075` |
| At power up an entry awaiting its reading is registered again and $51 is written, in either mode | `libsas_internal.cpp:887` |
| A validated hand pay with no receipt is $3E on a secure enhanced machine, with its own meters | `libsas_internal.cpp:5928` to `:5940`, `:690` |

## What the platform decides, and what this machine says

* **One lockup at a time.** QSP locks up for one hand pay and calls `sasHandpayStart` once
  (SasNetwork_Conditions.hpp:794); a win the platform adds to the lockup changes the queue's newest entry
  in place, as before.
* **The standard reset.** The reset ID is 00 (SasNetwork_Conditions.hpp:770).
* **Queue by default, legacy when declared.** QSP's flag defaults to the queue and can be changed only
  straight after a memory reset (SasNetwork.cpp:605, :862). No poll reports it, so the software machine
  claims `machine.handpay_queue` (and `machine.handpay_queue_size`, 16) or `machine.legacy_handpay`.
* **Hand pay validation only when declared (D53).** The attendant's reset, while the host has 7B bit 2 on,
  writes a validation record of type $40 or $60 through the buffer tickets use; 7B then reports bit 2 as
  supported. Secure enhanced only.

## How the cases learn what the machine does

* `machine handpay legacy` and `machine handpay validation on` are declarations, requirements only a
  machine built that way meets. No new stimulus: `egm handpay`, `egm handpay jackpot` and `egm handpay reset`
  were already there.

## What it deliberately does not model

* A system-validated hand pay, and hand pay receipts (`machine.handpay_receipts`), so 10.3.4 and 10.5.9 skip.
* Partial pays and a hopper (D46), so 5.3.2, 5.4.2 and 4.22, 4.24, 4.25 skip.
* A player cancelling a hand pay ($55).
* 5.6.2 stays notes: its broadcast sequences turn on the library's implied-NACK state machine, which is not
  modelled beyond a repeat of the same long poll.

## Script work done while bringing the cases up

None of these loosens an assertion.

* 5.5.1, 5.5.2, 5.6.1, 5.7, 5.8.1 and 5.8.2 had only notes and now have bodies; the legacy ones declare
  `machine handpay legacy`, and all declare the five-cent unit so the amounts are exact.
* 12.6 and 13.11 had a person win the progressive; they now declare the level and win it with
  `egm progressive win`, as their neighbours do.
* 10.3.5 and 10.5.7 declare `machine handpay validation on` in place of requiring it alone.

## Findings against the documents

* **5.8.2:** the library announces a hand pay still awaiting its reading again at power up whatever the
  reporting mode (`libsas_internal.cpp:887`, `libsas_wrapper.cpp:946`), so a legacy machine sends $51 after a
  power cycle where the document wants none.
