# M9 — Hand pay reset methods, the cash-out limit, poker and hand pay receipts

**Status: built 2026-11-27, under D44, D58 and D61, as group 17 of M9.** The host can now clear a hand pay with $94
and choose the credit meter for a jackpot with $A8, as the platform allows; a declared hopper machine reports its
cash-out limit on $A4; a declared draw poker game reports its hand, the cards held and the draw; and a declared
machine prints hand pay receipts. 20.1, 20.3, 21.13, 21.14, 11.19, 11.23, 15.13, 15.21, 15.25, 10.3.4 and 10.5.9
run; 20.1 fails on a finding, and 15.10 now fails on one.

---

## The rule

D44 again: **the protocol half is the library's.** Here that half is the tests and codes of $94 and $A8, the reset
ID long poll 1B reports, the cash-out limit $A4 reads, the hand $8E reads and the events $8B and $87. **Remote hand
pay reset and reset to the credit meter are the platform's**: the library leaves remote reset on and QSP never turns
it off, and QSP pays a jackpot onto the credit meter when the host asked for it and the win fits. So the software
machine does both without a declaration, as QSP does. **A hopper, poker and hand pay receipts the platform lacks**,
so they are built only when a case declares them (D58, D61), and follow the GSA document in their simplest form.

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| $94 and $A8 test the address first and are silent for another | `lp.cpp:9066`, `:9597` |
| Remote reset is on unless the application turns it off; QSP never does | `libsas_internal.cpp:329`, `lp.cpp:9073` |
| $94: no hand pay registered answers 02 | `lp.cpp:9097` |
| $94: busy for $94 answers 01; QSP never publishes that busy state | `lp.cpp:9102`, QSP `SasNetwork_AftTransfer.hpp:1525` |
| $94: otherwise the library resets the hand pay, answering 00, or 01 if it refuses | `lp.cpp:9109`, `:9117` |
| $A8: a method other than 00, 01 or 80 is refused | `lp.cpp:9625` |
| $A8: no hand pay answers 02; the credit meter needs the pending entry's reset ID 01 or 80, else 01 | `lp.cpp:9641`, `:9664` |
| $A8 standard or celebration answers 00 | `lp.cpp:9680` |
| The reset ID is 01 for a jackpot, a bonus's included, that fits under the credit limit and is below the upper limit, else 00 | QSP `SasNetwork_Conditions.hpp:784`, `HandpayResetRules.h` |
| The upper limit is the market's large win limit, a hundred thousand dollars by default | QSP `SasMarket.cpp:141`, `JsonHandler.cpp:35` |
| The platform acts on $A8 only for a jackpot, only if it answered 00 and the win still fits | QSP `SasNetwork_Callbacks.hpp:820` |
| $94 then pays as an attendant would, or onto the credit meter: credits, coin out and the machine-paid meter move, the jackpot meter does not | QSP `SasNetwork_Callbacks.hpp:864`, `:925` to `:1018` |
| The platform forgets the method once $94 has paid | QSP `SasNetwork_Callbacks.hpp:1029` |
| $A4 answers only once the application set a limit, for game zero to the last, silent past it | `lp.cpp:9518`, `:9522`, `:9546` |
| The library writes the limit as four BCD digits | `lp.cpp:9554` |
| $8E answers the hand the game registered, zeros before | `lp.cpp:8838`, `libsas.cpp:1345`, `libsas_internal.cpp:365` |
| $8B carries the byte the game gives | `libsas.cpp:1750` |
| The library has no call for $87, and neither has QSP's game interface | `libsas_internal.cpp:3876`, QSP `libqsp.h:366` |
| A receipt is validated with type 10 or 20 and announced with $3D; no receipt is 40 or 60 and $3E | `libsas_internal.cpp:5901` to `:5940` |
| The sound switches $03 to $05 are acknowledged and handed to the game | `lp.cpp:348`, QSP `SasNetwork_Callbacks.hpp:39` |

## What the platform lacks, and what this machine does

* **A hopper's cash-out limit** (`machine hopper on`): the hopper limit is every game's cash-out limit, in
  accounting units, capped at the 9999 the reply can carry.
* **A draw poker game** (`machine poker on`): every game deals the same five cards, so a case can name them. The
  player holds and releases cards (`egm poker hold|release <1-5>`, $8B with the top bit for held and the place from 0
  on the left) and draws (`egm poker draw`, $87, the cards not held replaced). A hand not drawn is final at the
  game's end. $87 goes out through the library's plain exception write, as the document asks of a poker machine.
* **Hand pay receipts** (`machine handpay receipts on`): long poll 7B bit 1 is offered, and while the host has it on
  the attendant's reset, or a standard reset from the host, files a receipt in the wallet. On a secure enhanced
  machine with hand pay validation also on, the receipt carries the validation number and the record is announced
  with $3D (GSA 10.3.4).
* **The upper limit for reset to the credit meter is the platform's default**, a hundred thousand dollars, which
  the operator cannot change; a host's bonus is exempt, as QSP exempts a funds transfer bonus.

## How the cases learn what the machine does

* `machine.remote_handpay_reset` and `machine.handpay_reset_to_credits` are offered by the software and manual
  backends; no poll can say so without clearing a hand pay.
* `machine.poker` and `stimulus.poker`, `machine.handpay_receipts` and the hopper are claimed only by a software
  machine built with them, and offered by the manual backend for a cabinet that has them (receipts excepted, as hand
  pay validation is).
* `decoder.lpA8`, `decoder.lp03`, `decoder.lp04` and `decoder.lp05` exist now. The card held event's field is
  `card`, one byte, where it had been read as one bit per card.

## What it deliberately does not model

* **Restricted and debit tickets** (`machine.restricted_tickets`, `machine.debit_credits`): QSP's restricted cash-out
  has its own rules for pools, foreign amounts and part payments, and every case waiting on it is a note for a
  person. Left for a group of its own.
* **Custom transfer ticket data** (17.2 to 17.5): the platform prints no transfer ticket to put it on.
* **The credit-meter key** (20.2): the platform has none.
* **Turning remote reset off, and lowering the upper limit for reset to the credit meter**, each a mid-case
  reconfiguration the platform's operator cannot make.
* **The sound cases 21.10 and 21.11**, which a person has to hear.

## Script work done while bringing the cases up

* 20.1 no longer asserts the jackpot's reset ID: the document gives 00 there and 01 in 20.3 for the same jackpot on
  the same set-up, and 20.3, where the round is about it, keeps the assertion.
* 20.3 gains the document's low credit limit round: reset ID 00, $A8 refused with 01, and $94 paying the jackpot
  meter.
* 10.5.9 expected $3E after a hand pay that printed a receipt; the document announces a receipt with $3D (10.3.4
  step 5), as the library does.
* 21.13 and 21.14 declare a hopper and set its limit, so the figure is known; 21.14 declares two games and ends with
  the silence for the third.
* 11.19, 11.23, 15.21 and 15.25 are written from their notes; 15.13 now finishes its game and reads the final hand.

## Findings against the documents

| Case | What the machine does | Where |
|---|---|---|
| 20.1 | A hand pay is cleared from the host with the main door open, where the document wants code 01: the library answers 01 only when told the machine is busy for $94, and QSP never says so | QSP `SasNetwork_AftTransfer.hpp:1525`, libsas `lp.cpp:9102` |
| 15.10, 15.11 | A jackpot hand pay, a legacy bonus's included, reports reset ID 01 where the document wants 00 on a machine not set up for reset to the credit meter: the platform has no such setting | QSP `SasNetwork_Conditions.hpp:784` |
| 11.19 | Passes on the declared machine only; QSP's game interface has no way for a poker game to report $87 | QSP `libqsp.h:366` |
