# M9 — Line faults, memory faults, the machine's own conditions and restricted tickets

**Status: built 2026-11-27, under D44, D58 and D62, as group 18 of M9.** The host can now break a long poll's check
sequence on purpose. The software machine raises the general tilt, takes itself out of service, reports a low battery
and starts silent at address 0 on memory found invalid, as the platform does; it prints restricted tickets when its
operator allows them and keeps the terms of a redeemed restricted ticket; and a case can declare memory devices that
report every fault, period meters and a credit-meter key, which the platform lacks. 3.9, 4.13, 4.15 to 4.19, 4.27,
4.30.1, 4.33, 4.45, 5.10, 10.3.6, 10.3.7, 10.4.3, 10.4.5, 10.5.14 to 10.5.17, 10.7.6, 10.7.7, 16.17 and 20.2 run; 20.2
fails on a finding.

---

## The rule

D44 again: **the protocol half is the library's** — here the exception queue, the check-sequence refusal, the
validation status bits and the expiration and pool it keeps from $71. **The general tilt, out of service, a low battery,
the address lost with the memory and restricted tickets are the platform's**, so the software machine does them without
a declaration, as QSP does. **Memory devices that report $31, $33 to $35 and $37 to $39, period meters and a
credit-meter key the platform lacks**, so they are built only when a case declares them (D58, D62), and follow the GSA
document in their simplest form.

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| A poll whose check sequence fails is refused when addressed to the machine | `libsas_threads.cpp:1641`, `:1680` |
| A hardware error raises the general tilt $20 and stops play; nothing when it clears | QSP `SasNetwork_Conditions.hpp:93`, `ConditionDefaults.h:90`, `AttachableDeviceHandler.cpp:279` |
| The attendant's maintenance mode raises $86 and stops play; the host's shutdown does not | QSP `SasNetwork_Conditions.hpp:86`, `libqsp.cpp:2124`, `SasNetwork_Callbacks.hpp:453` |
| A low backup battery is read while the platform starts and reported once with $3B | QSP `SasNetwork_Meters.hpp:132` |
| Memory found invalid at start-up leaves the poll address at 0 and starts no library | QSP `SasNetwork.cpp:155`, `:309`, `:336` |
| Setting an address restarts the library there and reports $3C | QSP `SasNetwork.cpp:753` |
| The library's exception queue holds sixty-four entries | `libsas_db.h:148` |
| The overflow $70 goes next unless the head of the queue outranks it, so a hand pay pending goes first | `libsas_internal.cpp:4365` |
| $7B bit 3 follows the operator's restricted ticket option, and bit 4 follows bit 3 without the ticket-in-part toggle | QSP `SasNetwork.cpp:624` |
| A restricted ticket needs the operator's option and the host's bit 3; without the toggle, bit 4 too, else none of it may leave | QSP `CashoutPrinterRules.h:256`, `:306` |
| A restricted ticket moves the restricted ticket-out meters | QSP `SasNetwork_Ticket.hpp:1161` to `:1170` |
| $71 keeps a restricted ticket's expiration and pool only with validation extensions, zero when left out | `lp.cpp:4625`, `:4651` |
| A redeemed restricted ticket lands as restricted credit with those terms | QSP `SasNetwork_Ticket.hpp:598` |
| $A8 standard records the method and leaves the reset ID the hand pay was registered with | `lp.cpp:9681` |

## What the platform lacks, and what this machine does

* **Memory devices reporting every fault** (`machine memory faults on`): a recovered CMOS error raises $31 once at the
  next start and keeps the meters; a bad CMOS device and the two EEPROM faults raise $33, $34 and $35 while running;
  a bad program checksum and the two partitioned program faults raise $37, $38 and $39 at the start that finds them.
  Every one but $31 holds the machine out of play until a memory clear and is raised again at each start, as the
  platform's own memory error is.
* **Period meters** (`machine period meters on`): clearing them raises $7B, and so does a memory clear, which zeroes
  the bill totals with everything else (GSA 4.27).
* **A credit-meter key** (`machine credit key on`, `egm handpay reset credits`): it pays a jackpot hand pay onto the
  credit meter when the host chose that with $A8 and the win still fits, and does nothing otherwise (GSA 20.2).

## Choices taken here

* **Cashable credit leaves first.** A collect with cashable credit on the machine prints the cashable ticket and
  leaves restricted credit where it was, as before; the next collect, with only restricted credit left, prints the
  restricted ticket. Restricted tickets are printed on secure enhanced validation only; on system validation the
  restricted credit stays, as there it would need its own validation number from the host.
* **A redeemed restricted ticket is not refused for its pool.** The platform's rules for a ticket from another pool
  than the credit held are not modelled; the ticket's pool replaces the one held, as the funds-transfer path does.
* **`stimulus.battery` restarts the machine**, because the platform only reads its batteries as it starts.

## How the cases learn what the machine does

* `host.bad_crc` is the host's own (`host send lp <hh> ... with bad crc`), for every transport.
* `machine.general_tilt`, `stimulus.tilt`, `machine.out_of_service`, `stimulus.battery`,
  `machine.memory_fault.at_start_up`, `machine.restricted_tickets` and `stimulus.restricted_tickets` are offered by
  the software and manual backends: no poll can say so without raising the condition.
* `machine.exception_queue_depth` is 64 on the software machine, read from the library it mirrors.
* The memory fault, period meter and credit-key capabilities are claimed only by a software machine built with them,
  and offered by the manual backend for a cabinet that has them.

## What it deliberately does not model

* **3.11, 3.12 and 3.14**: a reply's time, the gap between its bytes and a collision part way through it are the
  wire's, and every transport here hands a reply over whole; on the software machine's in-process link a time would be
  the computer's scheduling, not a machine's.
* **Debit tickets** (10.4.1, 10.4.2, 10.4.6): the platform performs no debit transfer, so no debit credit exists.
* **Custom transfer ticket data** (17.2 to 17.5) and the standing receipt text (16.90): the platform prints no
  transfer ticket, and $75 has no layout here.
* **20.2's last round**, remote and host-controlled reset turned off at the machine part way through.
* **The notes-only cases left**: 16.14 is a cross-reference to 16.8 to 16.11; 10.5.5 and 10.5.6 need validation on a
  second channel; 21.10 and 21.11 need a person to hear the sound; 21.16 needs the base command's status byte read out
  of the multi-denomination reply.

## Script work done while bringing the cases up

* 10.4.1 now also requires `machine.debit_credits`: its notes need a debit ticket, which nothing here can make.
* 10.7.6 and 10.7.7 no longer require `machine.restricted_tickets`: redeeming a restricted ticket does not need the
  option to print one.
* 3.9, 4.13, 4.27, 4.30.1, 4.33, 4.45, 5.10, 10.3.6, 10.3.7, 10.4.3, 10.4.5, 10.5.14 to 10.5.17, 10.7.6, 10.7.7,
  16.17 and 20.2 are written from their notes; 4.15 to 4.19 declare memory devices.
* 10.5.14 to 10.5.17 required capabilities named after their meters, which nothing offers; they now declare hand pay
  validation, and receipts where the meter counts them, as 10.5.9 does.
* The software machine sent the overflow $70 ahead of a hand pay pending; it now ranks it as the library does.

## Findings against the documents

| Case | What the machine does | Where |
|---|---|---|
| 20.2 | After $A8 asks for a standard reset, the hand pay information still says reset ID 01 where the document wants 00: the library records the method and leaves the reset ID the platform gave the hand pay | libsas `lp.cpp:9681`, QSP `SasNetwork_Conditions.hpp:784` |
| 4.27 | On the platform itself a memory clear reports $3A and $7A but not $7B, and it has no period meters; the case runs on a declared machine | QSP `SasNetwork.cpp:1220` |
