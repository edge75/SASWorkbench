# M7 — Component authentication on the software machine

**Status: built 2026-11-24, under D44 and D51, as group 6 of M8.** The host decodes long poll $6E,
and a case can declare `machine component authentication on` to get a software machine that takes
part: it lists two components, reads them by index or by name, authenticates them with CRC-16,
raises $8F when it has finished and every fifteen seconds after while the result is unread, and
raises $8E when a component is installed (`egm component change`). Chapter 19 runs 12 of its 16
cases, up from 1: seven pass and five fail as labelled findings against the library. The tree went
from 332 pass, 33 fail, 267 skip to 338, 38, 256 (raw 337, 39, 256: 10.6.4 failed once, a known
flake, and passed alone).

---

## The rule

D44 again: **the protocol half is the library's.** The $6E handler applies the library's tests in
the library's order and stays silent where it returns without sending. The application half has
nothing to copy: QSP starts the library with component authentication off (`SasNetwork.cpp:302`,
"TODO modify when LibQpm supports component authentication") and registers no component. So the
software machine takes no part by default, as the platform, and a case that needs it declares it
(D51); what the application then does is the simplest version this fixture can hold, said below.

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| $6E: channel category, then the address, then the feature bit; silent on each | `lp.cpp:3707`, `:3721`, `:3739` |
| An action past $03, an addressing mode past $01, or a name length past $7F is refused | `lp.cpp:3755`, `:3768`, `:3778` |
| Every reply starts with the list check sequence, least significant byte first, then the status | `lp.cpp:3748` |
| Action $00: status $01, an empty list name, a one-byte count and four zero bytes | `lp.cpp:3802` to `:3810`, `libsas_internal.cpp:6130` |
| Action $01: by index on the request's seventh byte, by name as a prefix of the zero-padded name; $80 when none | `lp.cpp:3820`, `:3827`, `:3880` |
| A found component answers status, name, size (as few bytes as it needs, least significant first) and methods | `lp.cpp:3836` to `:3845`, `libsas_internal.cpp:6150` |
| Running ($40): the method follows, and the authentication length byte is left out | `lp.cpp:3851`, `:3986`, `:4054` |
| Complete ($41): the method, the length and the result; $8F stops | `lp.cpp:3856`, `:3861` |
| Every reported outcome is cleared the moment it is sent | `lp.cpp:3867`, `:4075` |
| Action $02: seed over 32 is $82, no methods $C2, method not offered $C3, seed too long for the method $C4 | `lp.cpp:3914`, `:3924`, `:3929`, `:3934` |
| Only the first method byte is compared, and the offset is caught only when both halves exceed the size | `lp.cpp:3929`, `:3942` |
| Accepting aborts every running authentication ($C1) and cancels any $8F still owed | `lp.cpp:3952`, `:3965` |
| Action $03: the first component running or holding an outcome, in list order; $FF when none | `lp.cpp:4021`, `:4088` |
| Only action $03 stops $8F for a failure | `lp.cpp:4068` |
| The application's answer is refused for a component no longer running | `libsas_internal.cpp:6311` |
| $8F at the answer and every fifteen seconds after (76 ticks of the 200 ms timer) | `libsas_internal.cpp:6333`, `libsas_threads.cpp:2241` |
| The list check sequence is the SAS CRC over every name in list order, recomputed at each registration | `libsas_internal.cpp:6187`, `:6261` |
| A registration asked to report raises $8E; the start-up registrations do not | `libsas_internal.cpp:6201`, `libsas.cpp:2662` |
| The component state is in working memory: a restart forgets every status and the $8F timer | `libsas_db.h:765` |
| The feature bit is bit 4 of the second byte, cleared on a channel without the category | `libsas.h:203`, `libsas_utils.cpp:1249` |

## What the platform decides, and what this machine says

QSP decides none of this, so each is this machine's own choice, in its simplest form:

* **Two components**, `SWB-GAME` (65,536 bytes) and `SWB-PLATFORM` (131,072 bytes), with a fixed
  byte pattern so a result can be checked by hand.
* **CRC-16 only**, the method every chapter 19 request names: the SAS CRC started from the seed and
  run from the offset to the end, least significant byte first. Any other method, or an offset at or
  past the end, fails at $C0.
* **Five seconds** to authenticate (`AuthenticationTime`), long enough for a host to read the status
  while it runs.
* **A component change is an installation.** The library can only add a component, so `egm
  component change` registers `SWB-UPDATE-<n>` with $8E. A power cycle keeps it, as hardware stays
  installed.

## How the cases learn what the machine does

* `machine.component_authentication` is read off bit 4 of the second enabled-features byte;
  `machine component authentication on` builds the software machine taking part and asks any other.
* `machine.multiple_components` is read off the count in the $6E action $00 reply.
* `egm component change` needs `stimulus.component_change`, offered by the software and manual
  backends: a person installs or updates a component at a cabinet.
* `decoder.lp6E` is baseline now that the layout exists. The reply's tail (method, length, result)
  is read by what is left of the reply, a new `SasCondition.Remaining`, so a byte the machine leaves
  out reads as absent rather than as the next field.

## What it deliberately does not model

* A component whose link can be cut (`stimulus.component_link`), so 19.4 and 19.6 still skip.
* A component that cannot be authenticated now (`stimulus.component_busy`), so 19.14 skips. The
  library sets $C5 only from the application's answer after $40 (`libsas_internal.cpp:6315`), so an
  immediate $C5 in the reply to the request, which the document wants, is not something it produces.
* `host.timing`, so 19.13 skips. On this machine, as in the library, the $8F repeat does not survive
  a power cycle.
* The $8F timer counting only while the link is up; this machine counts always.
* CRC-32, MD5, SHA-1 and SHA-256, a list name, and removing or reordering a component.

## Script work done while bringing the cases up

None of these loosens an assertion.

* Every case after 19.1 declares `machine component authentication on` in place of requiring it.
* 19.3 and 19.16 had only notes and now have bodies: the list read, a component change, and the
  check sequence or the $8E event after it.
* 19.8's note on the offset says what the library does: the eight-byte offset is accepted at $40.

## Findings against the documents

| Case | What the library does |
|---|---|
| 19.7, 19.9, 19.10, 19.11 | A $40 reply writes the authentication length byte past its end and leaves it out of the length (`lp.cpp:3851`, `:3986`, `:4054`), so the reply ends after the method. |
| 19.9, 19.12 | A result is cleared the moment either action reports it (`lp.cpp:3867`, `:4075`), acknowledged or not, so a second read answers $FF or status 0. |
| 19.12 | A second authentication marks the first aborted (`lp.cpp:3956`), so the next read reports $C1 once, where the document wants $FF. |
| 19.8 (passes) | The offset check is almost never active: only when both halves of a sixteen-byte comparison exceed the size, and the empty-size test compares a pointer (`lp.cpp:3941`, `:3942`). The case passes because this machine then fails the authentication. |
