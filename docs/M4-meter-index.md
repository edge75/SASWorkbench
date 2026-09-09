# M4 — Widening the meter index

**Status: done, 2026-09-09.** A script could read sixteen meters by name; the machine keeps 196,
so sixteen was the ceiling on anything an `expect meter` could say. It reads forty now, and not
one of the sixteen changed the command it comes from.

```
sasctl catalogue readable-meters                  every name, its command, and the ones it is not
sasctl catalogue meters                           all 196, and which are readable
sasctl run tests/regression --loopback --stimulus software
```

---

## The numbers, before and after

| | before | after |
|---|---|---|
| **meters a script can read by name** | **16** | **40** |
| the catalogue, replies with a layout | 83 of 124 | 83 of 124 |
| our own suite, distinct long polls explained | 37 of 37 (100%) | **57 of 57 (100%)** |
| our own suite, distinct events explained | 4 of 4 (100%) | 4 of 4 (100%) |
| our own suite, packets | 870 | **1,482** |
| tests under `tests/regression` | 28, no skips | **33, no skips** |
| the imported corpus, distinct long polls | 18 of 18 (100%) | 18 of 18 (100%) |
| a sweep of the software machine | 76 of 83 (91%) | 76 of 83 (91%) |
| a sweep of a real machine | 76 of 83 (91%) | 76 of 83 (91%) |

818 tests pass, forty-one of them new.

**The catalogue figure could not move and did not.** No layout was added: what changed is which
fields of the layouts already here are offered under a name, and which command each name is read
from. The corpus figure could not move either — that traffic was recorded before any of this and
carries none of the commands involved.

**The row that moved by itself is the fourth.** Our own suite now exercises fifty-seven distinct
long polls rather than thirty-seven, because a case that reads a name sends the command behind it.

---

## The reason three waves deferred this, and what it turned out to be

Every wave since the catalogue sweep put this off with the same sentence: *several commands report
the same library meter through handlers that scale it differently, so a name moved to the wrong
command quietly changes what an existing test measures.* That was true and it was worth stating.
It was also never taken any further than a sentence.

Taken further, the machine library reads a meter in two places.

| | where | what it does |
|---|---|---|
| **the shared single-meter helper** | `lpXX_SendMeter` | divides the meter by the accounting denomination in cents, when the machine is in either of its two default meter modes and the meter is not one the library calls multigame |
| **every other handler** | `lp0F`, `lp18`, `lp19`, `lp1C`, `lp1E`, `lp2F`, `lp6F`, … | reads the meter and writes it as it stands |

Thirty-eight commands go through the first. Forty-seven commands report a meter at all, and which
meters each reports, and which of the two paths it takes, are now **generated from the library's
own handlers** rather than written down here — as is the list of fourteen meters the helper leaves
alone, which comes from the predicate that decides it. A layout naming a meter its handler does not
read is a failing test.

### The rule that decides which command a name is read from

The library says why the division exists, in a comment beside it: meters expressed in credit should
be divided by the denomination. So the division is a conversion, and a conversion applies to some
of these numbers and not others:

* **A meter counted in credits** is read from the command that scales it, because that is the
  conversion into the unit the reply is supposed to be in.
* **A meter counted in anything else** — notes, games, door openings — is read from the command
  that does not, because dividing a number of notes by a currency unit converts nothing. The
  library applies the division to those meters too, which is exactly where the two commands part
  company.
* **Where nothing separates them**, the reply carrying one number wins over the reply carrying six.

Applied to the sixteen names that already existed, that rule picks **the same sixteen commands they
were already read from**. A test asserts it name by name, because a rule that quietly moved one of
them would change what a passing case measures — which is the thing this was deferred to avoid.

### The disagreement, produced on a wire

Reasoning about it was what the last three waves did. This wave made the software machine model the
library's rule — it takes a meter mode, and is in full manual by default because that is what the
machine under test is in — and then measured it. One two-unit note, on two machines differing in
nothing but their meter mode:

```
full manual    $1E  011E 00000000 00000000 00000000 00000001 00000000 00000000 DFFD    bills_20 = 1
               $35  0135 00000001 27E7                                                 bills_20 = 1

default auto   $1E  011E 00000000 00000000 00000000 00000001 00000000 00000000 DFFD    bills_20 = 1
               $35  0135 00000000 AEF6                                                 bills_20 = 0
```

**One note, divided by five because the machine counts in nickels, is nought.** A host reading the
twenty-unit note count from `$35` on such a machine is told the machine has taken none. That is the
number a test would have begun measuring had the index been widened by preferring the dedicated
command, which is what the index's own rule used to say to do.

The same measurement on a credit meter, where the division is the correction the library meant:
credits dropped in reads 400 through `$0F` and 80 through `$13`.

### And on the machine this project tests against, it cannot happen at all

`QSP/src/LibMarketSas/SasNetwork.cpp` calls `sasSetImmediateModeMeter(fSasHandle,
eMeterMode::FULL_MANUAL)` at startup and the library writes that to the machine's memory. In full
manual the helper divides nothing. So on `QSP_DemoOpenGL` the single-meter commands and the bulk
replies answer the same number at every accounting denomination — checked in the copy of QSP the
demo under test is actually built from, not only in the checkout.

**`BLOCKERS.md` Trap E does not apply here.** Every meter handler named above is byte-identical
between the libsas checkout the generator reads and the libsas revision the machine runs, so for
this the two do not describe different machines.

---

## Read off a real machine, and what that does and does not settle

`QSP_DemoOpenGL` was swept again on 2026-09-09, and asked separately for three meters by code
through the selected-meter poll. **Every meter more than one command reports agrees** — twelve
names, twenty-three comparisons, all in `MeterAgreementTests` with the bytes copied verbatim.

```
$0F  010F 00000000 00000000 00000000 00000000 00000000 00000000 E9F3
$10  0110 00000000 6BB0        total_cancelled_credits, both nought
$1E  011E 00000000 00000000 00000000 00000000 00000000 00000000 22B0
$35  0135 00000000 AEF6        bills_20, both nought
$18  0118 0000 0000 4BEA       games since power up and since door closure, both nought
$2F  012F 07 0000 25 00000000 FD97     the same meter asked for by code, nought
```

**They agree, and they agree at zero.** That machine has never taken money and never played a game
— its licence is gone and it returns every note (`BLOCKERS.md` Trap D) — so nought divided by
anything is nought. The comparison establishes that both replies put the meter where this build
reads it. It establishes nothing about the division, and this document does not pretend otherwise:
what settles the division is the full-manual finding above and the software machine's bytes.

The two meters that machine does hold are its slot door openings (23) and its power resets (18),
and exactly one command reports either: the library's selected-meter handler skips both by name.

**A note on the power reset figure.** The sweep of 2026-09-08 recorded 21 and this one records 18.
That is not a meter going down. The machine's memory was put back from the committed fixture in
between, and the fixture was saved when the count stood at 18: it is one meter in two states of one
machine.

---

## The twenty-four names, one by one

| name | read from | why that command | also reported by |
|---|---|---|---|
| `bills_2` | `$32` | the only command that reports it; scaled, and said to be | — |
| `bills_25` | `$3B` | the only command that reports it; scaled | — |
| `bills_200` | `$3A` | the only command that reports it; scaled | — |
| `bills_250` | `$45` | the only command that reports it; scaled | — |
| `bills_500` | `$38` | the only command that reports it; scaled | — |
| `bills_1000` | `$39` | the only command that reports it; scaled | — |
| `bills_2000` | `$3C` | the only command that reports it; scaled | — |
| `bills_2500` | `$3E` | the only command that reports it; scaled | — |
| `bills_5000` | `$3F` | the only command that reports it; scaled | — |
| `bills_10000` | `$40` | the only command that reports it; scaled | — |
| `bills_20000` | `$41` | the only command that reports it; scaled | — |
| `bills_25000` | `$42` | the only command that reports it; scaled | — |
| `bills_50000` | `$43` | the only command that reports it; scaled | — |
| `bills_100000` | `$44` | the only command that reports it; scaled | — |
| `bills_accepted_credits` | `$46` | credits, and the scaled reading is the converted one | — |
| `bills_in_stacker` | `$49` | the only command that reports it; scaled | — |
| `stacker_credits` | `$4A` | credits, and the scaled reading is the converted one | — |
| `true_coin_in` | `$2A` | credits, and the scaled reading is the converted one | `$47`, the same meter |
| `true_coin_out` | `$2B` | credits, and the scaled reading is the converted one | — |
| `external_coin_acceptor_credits` | `$47` | credits; the library reads the same meter as `$2A` | `$2A`, the same meter |
| `games_since_power_up` | `$18` | a count, and `$18` does not scale it | — |
| `games_since_door_closure` | `$18` | a count, and `$18` does not scale it | — |
| `slot_door_opened` | `$1C` | the only command that reports it, and it does not scale it | — |
| `power_reset` | `$1C` | the only command that reports it, and it does not scale it | — |

**Fourteen of the twenty-four are read from a command that scales them and have no alternative.**
Their bill counts are numbers of notes, so on a machine in a default meter mode at any denomination
above one cent those readings are divided by a currency unit and are wrong — the library's, not
ours. Every one is marked, and `sasctl catalogue readable-meters` prints it against the name rather
than leaving it in a document:

```
bills_500    lp $38  Count  meter $004A  scaled by the accounting denomination
bills_20     lp $1E  Count  meter $0044
                            also reported by lp $35 (scaled)
```

**Two names, one meter.** Long polls `$2A` and `$47` both read `TotalCreditsFromCoinAcceptor`, so
this machine's coin acceptor and its external coin acceptor cannot disagree, and a case written
against one of them is written against both. Recorded rather than modelled around, and asserted, so
a library that split them later shows up as a failing test.

---

## What changed for names that already existed

**Nothing, and that is asserted rather than claimed.** All sixteen read from the same command as
before. Three of them — `total_cancelled_credits`, `total_drop` and `current_credits` — are read
from a command the library scales, which they always were; that is now visible in the tool's output
instead of being a property nobody could see.

Two things were corrected, and both were this build being wrong about the library:

* **The two counts long poll `$18` answers were not meters here, and they are.** The code said in
  as many words that the protocol keeps no code for either. It keeps two — `GamesSinceLastReset`
  and `GamesSinceSlotDoorClosure` — and the library's own handler reads them by code. They are
  meters of the software machine now, so a selected-meter poll asking for either gets the same
  number the `$18` reply gives, and a real machine answered both when asked that way.
* **The readable column of `sasctl catalogue meters` was wrong for eleven of the sixteen.** It
  compared a name against the library's spelling of the meter, and only five of the sixteen are
  spelled the same way, so eleven readable meters were printed as unreadable. It answers by meter
  code now.

---

## What is in the build

* **A field says which meter it reports**, separately from whether a script may read it by name.
  The two are different questions: five per-game replies report a meter and offer no name, because
  which number comes back depends on the game the host asked about; and one field is readable and
  reports no meter, because the machine computes it.
* **`$20` is not a meter read at all.** Its handler adds up twenty-four bill counts, each
  multiplied by its face value. `total_bill_value` is therefore a name with no meter code behind
  it, and the model now says so instead of implying one.
* **The generator reads two more facts out of the library** — which meters each command reports and
  in what order, and which meters the shared helper leaves alone — so both are checked on every
  regeneration rather than asserted here.
* **The index refuses a reply that has to be asked for in a shape.** A meter reading sends the
  command bare, so a per-game or per-validation-type reply cannot be behind a name. Those fields
  are still assertable on their own reply.
* **The software machine takes a meter mode**, defaulting to the one the machine under test is in.

---

## What was deliberately not done

* **No layout was added or changed.** This wave establishes no new decoding; it names what was
  already decodable.
* **The five tournament meters, `$6E` and `$A4` still have no layout**, so nothing they report can
  be named. That is unchanged and needs a machine advertising tournament mode.
* **No script construct for the meter mode.** No reply carries it, so a case declaring one could
  not be checked against a real machine — it would have to skip there, and a declaration that can
  only ever be honoured by the software peer is worth less than a unit test that measures the same
  thing. The measurement is in `MeterScalingTests`.
* **The library's own storage units are not modelled.** The software machine keeps one number per
  meter and applies the library's divisor to it; whether the library's stored value is in cents or
  in accounting units is a question about the platform above it that no reply answers. What the
  measurement shows is that two commands answer two numbers, which is what the choice of command
  turns on.
* **The per-game meters are still not readable by name**, and the reason is the same as the reason
  they are marked: `$52` answered for game zero and `$52` answered for game one are different
  numbers under one name.

## Known limits, stated rather than discovered later

* **Fourteen names have one command behind them and it is one the library scales.** On a machine
  in a default meter mode those readings are divided by the accounting denomination. Nothing here
  can offer a better source, because none exists; the name is marked and the tool prints the mark.
* **The agreement measured on a real machine is an agreement at zero.** Stated above and worth
  repeating: it confirms the offsets, not the arithmetic.
* **The full-manual finding is about this platform, not about SAS.** Another platform that leaves
  the library in a default mode would see the disagreement on its own machine, and every mark this
  wave added exists for that reader.
