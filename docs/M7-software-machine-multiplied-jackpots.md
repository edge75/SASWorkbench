# M7 — Multiplied jackpots on the software machine

**Status: built 2026-11-24, under D44 and D52, as group 8 of M8.** A case can declare `machine multiplied
jackpot on` to get a software machine that takes long poll $8B: it multiplies a game's win inside the
host's range, pays the extra part as a legacy bonus and reports it with $7C, holds a change sent during a
game until the next one, and forgets the setting when the link is lost or the power cycles. Chapter 22
runs 19 of its 28 cases, up from 1, and all 19 pass. The tree went from 338 pass, 38 fail, 256 skip to
356, 38, 238; the 38 failures are the same labelled findings as before.

---

## The rule

D44 again: **the protocol half is the library's.** The $8B handler applies the library's tests in the
library's order and stays silent where it returns without sending; the award is built as
`SAS_LegacyBonusAward` builds it. The application half has nothing to copy: QSP never gives the library the
jackpot multiplier bit and drops $8B unhandled. So the software machine has no multiplied jackpots by
default, as the platform, and a case that needs them declares them (D52); what the application then does
is the simplest version the document describes, said below.

## What it models, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| $8B: the legacy bonus bit, then the jackpot multiplier bit, then the channel category, then the address; silent on each | `lp.cpp:8689`, `:8691`, `:8700`, `:8708` |
| A bad check sequence is refused (here on arrival, before the feature tests, as for every command on this machine), and there is no busy response (the test is commented out) | `lp.cpp:8712`, `:8719` |
| Acknowledged, then the range, multiplier, enable and wager type handed to the application unchecked | `lp.cpp:8725` to `:8737` |
| The award: multiplier, extra part, tax status and bonus stored for $90 and queued as $7C | `libsas.cpp:2594`, `:2634` to `:2643` |
| A multiplier the award accepts is 00 to 0A or 81 to 8A | `libsas.cpp:2566` |
| The multiplier bit is bit 0 of the first feature byte, cleared with the legacy bonus bit on a channel without the category | `libsas.h:186`, `libsas_utils.cpp:1236` |
| The library tells the application the link is down after 25 ticks of the 200 ms timer without a poll | `libsas_threads.cpp:2443`, `:2456` |

## What the platform decides, and what this machine says

QSP decides none of this (`SasNetwork.cpp:286`, `SasNetwork_Callbacks.hpp:198`), so each is this machine's
own choice, in its simplest form:

* **The bit goes with legacy bonusing.** The multiplier bit is reported only while legacy bonusing is on,
  since the award is a legacy bonus; switching legacy bonusing off clears both, as 22.1 expects.
* **A command it cannot apply changes nothing.** A multiplier outside one to ten (bit 7 aside), an enable or
  wager byte past 01, or a minimum above the maximum is acknowledged, as the library does, and ignored.
  Enable 01 switches the mode off whatever the other fields say.
* **A game multiplies by the setting in force when it started.** A change sent while a game runs waits for
  the next game. Only the paytable win counts: inside the inclusive range, in accounting credits, and with
  the max-bet restriction only at the machine's maximum bet.
* **The extra part is the win times one less than the multiplier.** It is paid to the credit meter, moves
  coin out and the machine-paid external bonus meter (001E), and lands on the deductible legacy meter, or
  the non-deductible one when bit 7 of the multiplier is set. Its $7C carries the multiplier byte as sent,
  the extra part and tax status 00.
* **A bonus held for the game end shares the award.** A legacy bonus sent during the game that is paid at
  its end, with nothing else holding it, joins the multiplied award: one $7C, the bonus with its own tax
  status beside the extra part.
* **A win paid by hand takes the extra part with it.** The hand pay is the game's win plus the extra part;
  the attendant's reset meters the extra part as an attendant-paid external bonus (0021), and its $7C
  follows the $52.
* **The setting is in working memory.** The channel that owns legacy bonusing going quiet past the loop
  break switches it off, as does a power cycle or a memory clear; the host enables it again.

## How the cases learn what the machine does

* `machine.multiplied_jackpot` is read off bit 0 of the first enabled-features byte, as before;
  `machine multiplied jackpot on` builds the software machine multiplying and asks any other.
* No new stimulus: games are played and held open with the steps groups 1 and 5 built.

## What it deliberately does not model

* A progressive's part of a win is not multiplied, so 22.18.1 and 22.18.2 stay notes; 22.18.3 needs a wide
  area progressive (`machine.wap_progressives`).
* The extra part is not weighed against the jackpot limit or the credit limit; only the game's win is (D50).
* `machine.max_bet` is not claimed, as no poll reports it, so 22.6 skips.
* A multi-game or multi-denomination machine, so 22.22 to 22.26 skip.

## Script work done while bringing the cases up

None of these loosens an assertion.

* Every case after 22.1 declares `machine multiplied jackpot on` in place of requiring it.
* Every case that plays declares `machine accounting denomination 100c`, as 15.28 does: the figures are in
  credits and assume a game credit is one accounting credit.
* 22.7 to 22.10 and 22.19 had only notes and now have bodies, a game held open with the change or the
  bonus sent before it ends.
* 22.16 and 22.17 set the jackpot limit their note asked a person to set (`egm set jackpot limit 100000`).
* 22.11, 22.13 and 22.17 drain the events of one game before the next poll or the next game, so an
  expectation cannot be met by an earlier game's event and a long poll is not answered with one (D44).

## Findings against the documents

None. Every case that runs passes. Two things the library does are worth knowing, though no case
observes them:

* `lp8B` checks none of the fields it hands on (`lp.cpp:8729` to `:8737`): a multiplier of 00 or past 0A, an enable or
  wager byte past 01 and a minimum above the maximum are all acknowledged, and refusing them is left to the
  application.
* `SAS_LegacyBonusAward` adds only the bonus to the legacy meter (`libsas.cpp:2621`, `:2625`), so an application
  that left the meters to the library would never meter the extra part on $9A; this machine meters it as
  the application, as QSP does for its own bonuses.
