# M4 — The check sequence, and a catalogue swept against a real machine

**Status: done, 2026-09-08.** Two waves. The first fixed the defect the overlay diff found and
measured the fix on the wire. The second turned the diff's backlog of eighty-six commands into
bytes by asking a real gaming machine for them, and forty-seven of those bytes became layouts.

```
sasctl sweep --list                                # what would be asked, and what would not
sasctl sweep --tcp 127.0.0.1:9760                  # ask, and write down what came back
sasctl sweep --tcp 127.0.0.1:9760 --framing        # measure the check-sequence rule instead
```

---

## The numbers, before and after

| | before | after |
|---|---|---|
| the catalogue, replies with a layout | 36 of 124 | **83 of 124** |
| our own suite, distinct long polls explained | 10 of 10 (100%) | 10 of 10 (100%) |
| the imported corpus, distinct long polls explained | 18 of 18 (100%) | 18 of 18 (100%) |
| **a sweep of a real machine**, distinct long polls explained | — | **76 of 83 (91%)** |
| meters a script can read by name | 16 | 16 |
| the overlay diff's backlog | 86 commands | **39 commands** |

696 tests pass, fifty-nine of them new.

**The first two figures could not move and did not.** Neither body of traffic carries any of the
forty-seven commands — that was the whole finding of the last wave. The third row is the point:
the sweep is a body of traffic that does move, because what it contains is chosen rather than
found, and it can be produced again on demand.

The meter row is deliberate and is not an oversight — see *What was deliberately not done*.

---

## Wave one: the check sequence is a property of the command

The last wave found this and did not fix it. Both other descriptions of SAS say the host's own
`$01` and `$02` messages end in a frame check sequence; this build said they do not, deciding from
the shape of the message rather than from the command. **The software machine decided the same
way, so the two peers agreed with each other and both disagreed with a real machine** — the one
kind of agreement D7 says proves nothing.

### The library states the rule twice, in two files, and the two agree

The previous wave read it out of one handler. There is a second and better statement of it.

| | where | what it says |
|---|---|---|
| the handlers | `lp.cpp` | a handler that calls `crcCheck` itself refuses the message when the check fails, and on a two-byte message that check reads the address and the command byte *as* the sequence and can never pass |
| **the receiver** | `libsas_uart.cpp` | a table of how many bytes each host message is. The receive path reads the address and the command byte, looks the command up, and then stops, or reads exactly that many more bytes, or reads a length byte and follows it |

The generator now reads **both** and reports any disagreement rather than preferring one.
**Over every command where both speak there are none** — 124 commands, zero conflicts. That is
worth more than either reading on its own, and it is checked on every regeneration rather than
asserted here.

The receiver's table says more than the handlers can, in two ways that both mattered:

* **It covers two commands the dispatcher answers nowhere.** `$0C` and `$0D` are framed at eight
  bytes each, so their host message is described even though nothing will answer them. The last
  wave listed both among the three commands nothing here could establish anything about.
* **It covers eighteen commands the long poll enumeration does not carry at all** — `$1D`,
  `$22`–`$29`, `$62`–`$67`, `$69`, `$6A`, `$6B`, the legacy funds-transfer family. The last wave
  reported those as existing only in somebody else's model. They are in the library's receiver,
  which means how long the host's own message is, and whether it ends in a check sequence, are
  answerable for all eighteen; what comes back is still not. `sasctl catalogue diff` now says so.

### Measured on the wire, and the machine did something else

The last wave's reading predicted that a real machine *refuses* a two-byte `$01`. It does not.

```
  cmd  framed as        outcome        reply
  $01  two bytes only   Silent         -
  $01  with a sequence  Acknowledged   01
  $02  two bytes only   Silent         -
  $02  with a sequence  Acknowledged   01
```

Against `QSP_DemoOpenGL`, from the machine's own side of the transport:

```
rx 1.01 0.01                  bare $01           -- nothing sent back
rx 1.01 0.01 0.51 0.08        $01 with a check   tx 0.01   acknowledged
rx 1.01 0.02                  bare $02           -- nothing sent back
rx 1.01 0.02 0.CA 0.3A        $02 with a check   tx 0.01   acknowledged
```

**Silence, not a refusal**, and the receiver's table is why: it is still waiting for two more
bytes, the inter-byte deadline expires, and the message is dropped as incomplete before any
handler sees it. So the handler that would have sent the refusal is never reached. Both readings
agree that the host's message has no effect, which is the defect either way; what the measurement
corrects is the machine's manner of refusing, and `M4-overlays.md` has been corrected to match.

**Why that distinction is worth the paragraph.** A refusal is a machine saying no; silence is a
machine that never heard a message. A host that treats the two the same cannot tell "this command
is not honoured here" from "my framing is wrong", and those need different fixes.

### What changed, and where

* **`SasCommandFraming`** answers, per command, whether the host's message must end in a check
  sequence, how long the whole message is, and how many request bytes that leaves. All of it comes
  out of the generated catalogue, which comes out of the two library sources.
* **`HostMessage.WriteLongPoll`** takes the requirement rather than inferring it. The default is
  the protocol's own — data decides — and it is documented as the default and never as a stand-in
  for not having looked.
* **`PollEngine`** looks the rule up in one place, so every caller gets it: the script runner, the
  capability probe, the session, the sweep. There is no second decision anywhere.
* **The software machine** now has two refusals, because a real machine has two: a bad check
  sequence on any message longer than two bytes, and a command whose handler checks one asked as a
  bare two-byte poll. The second is the one it was missing.
* **The extended log** reports a host message that carries no check sequence where its command
  requires one, over anybody's traffic. Over the imported corpus it fires zero times: nobody in
  that corpus ever sent one, which is worth stating rather than leaving as an untested path.

**Fifty-seven of the 124 commands require a check sequence and 63 do not**, with four codes the
library neither frames nor answers. Nine of the fifty-seven carry no data at all, and those nine
are what the defect was about: `$01`–`$07`, `$0A`, `$0B` — and `$94`, remote handpay reset, which
would have arrived with the same fault the moment it got a layout.

---

## Wave two: ask the machine

The diff's backlog was eighty-six commands no traffic here had ever carried. A coverage figure
taken over traffic can only ever name the commands somebody happened to send, and both bodies of
traffic were spent. The only way left to produce bytes is to ask a machine for them — which is
also the only way that verifies a layout instead of asserting one, because the reply either fits
or it does not.

### What the sweep refuses to send, and why

**Classified first, and printed without opening a link.** `sasctl sweep --list` prints the whole
classification. The rule is one sentence: **a command the gaming machine library answers in a
handler named for sending something is asked; every other command is withheld, by name, with what
it would have changed.**

That rule is applied rather than documented. A command that is neither named in the withheld table
nor answered by such a handler is withheld with a reason saying it has not been classified — and a
test walks the whole generated catalogue and fails if any command is in that position. So a setter
added to the library arrives as a failing test, not as a command the sweep quietly sends to
somebody's machine. A second test names the eleven that move money and asserts each is withheld.

**41 of 124 withheld.** The reasons are in the tool's own output; the ones worth reading twice:

* **`$0E`, real-time event reporting.** Not a money command. It changes how the machine answers
  *every general poll after it*, which would change the meaning of the rest of the sweep — so
  sending it would corrupt the measurement rather than the machine.
* **`$21`, ROM verification.** Makes the machine compute a signature over its own code and report
  it later as an exception. Withheld because the sweep would walk away leaving that in flight.
* **`$B0`, the multi-denomination preamble.** It carries another command inside it, so what it
  changes is whatever it is given. There is no safe thing to give it unattended.
* **`$4D` is *not* withheld, and that took a decision.** Function code zero reads the current
  validation entry and advances the machine past it, which would consume a record nobody has seen.
  Function code `$FF` reads the same entry and leaves it where it was. The sweep sends `$FF`, and
  the plan says so in as many words rather than leaving it to the ladder, which would have sent
  zero first.
* **Where the judgement was uncertain the command is withheld.** That costs coverage and cannot
  cost anything else.

### What it asks, and how it knows what to ask

Almost every command is asked exactly once, and the request is derived rather than guessed:

1. **A command this build has a layout for** is asked in the shape that layout says, built through
   the encoder so its length byte is right.
2. **Otherwise the receiver's own table** says how long the whole message is, so how many request
   bytes it takes is a fact this build can read.
3. **Only a command neither describes** is worked up from the shortest form — nothing, one byte,
   then two — with every form it sent on the record. Shortest first for a reason: a machine that
   answers a bare poll would also answer one carrying bytes it ignores, so the longer form first
   would establish nothing.

A test asserts that where both a layout and the receiver's table speak, the two agree about the
request's length. **They agree everywhere.** That is not decoration: it is where a disagreement
between the two would become a failing test rather than a message the sweep sends in one shape
while the machine frames it in another.

**One command needed that test to be right.** Long poll `$2F` is length-prefixed, so the shortest
well-formed request asks for no meters at all — three bytes. The receiver frames `$2F` at eight
bytes exactly. So a `$2F` asking for no meters is dropped as incomplete, and **so is one asking
for two**: on this library exactly one meter may be asked for per message. The sweep fills the
group to the framed length, and the machine answered. Before that, it was silent and the reason
looked like the machine's.

### What came back

Against `QSP_DemoOpenGL` with the standard-validation memory image, on 2026-09-08:

| | count |
|---|---|
| answered with data | **74** |
| acknowledged, no data | 0 |
| refused | 0 |
| **answered nothing** | **9** |
| withheld, not sent | 41 |

**Nothing was refused, which is itself a result**: every request the sweep built was one the
machine's receiver could frame and its dispatcher could read.

**The nine silences are recorded as silences and not skipped.** A machine's silence is a fact
about the machine, and dropping it would leave the sweep looking more complete than it is. What
they are:

* **`$95`–`$99`, the tournament meters.** The library's own handler returns without answering
  unless the game advertises SAS tournament mode, which this machine does not. That is the
  library behaving correctly, and it is the reason those five layouts cannot be established here.
* **`$6E`, authentication information**, and **`$A4`, the cashout limit** — asked in the shape
  both the receiver and the handler describe, and answered with nothing. No layout follows from
  silence.
* **`$4D` and `$70`** — the validation buffer and the ticket the machine wants validated. Both are
  empty on a machine that has printed nothing, and both already have layouts.

### The forty-seven layouts

Every one was read out of the library's own handler and then checked against the bytes that
machine answered with. The bytes are in `SweepLayoutTests`, copied verbatim with their check
sequences, so a layout that shifts a field by one byte fails there rather than in a report
somebody has to read carefully.

| | commands | what the shape is |
|---|---|---|
| the rest of the dedicated bill counts | `$31`–`$34`, `$38`–`$45` — 17 | four bytes of binary coded decimal, through the shared meter handler |
| the other shared single meters | `$2A $2B $46 $47 $49 $4A` | the same four bytes |
| fixed multi-field replies | `$18 $19 $1C $2D $48 $50 $52 $53 $55 $57 $83 $84 $85 $8E $8F $90 $9A $B3` | offsets fixed by the handler and by the reply's total length |
| length-prefixed replies | `$4F $56 $87 $B2 $B4 $B5` | the length byte is the only thing that says where the message ends |

**`$31` to `$34` are the four the last wave left undecoded on purpose**, because no capture here
carried one and writing them from the shape of their neighbours would have been a guess that
happened to be right. The sweep carries all four. The reason for withholding them then still
stands, and what changed is not the reasoning but the evidence.

Four of these are worth reading in detail.

#### `$8F`, the physical reel stops — a disagreement closed twice

The overlay diff found the legacy tester describing this reply at seven bytes and the conformance
toolkit at eleven, took it to the library's handler, and closed it at eleven — nine reel positions
and not five. **The machine answered thirteen bytes**: an address, a command, nine positions and a
check sequence. That is the same answer from the machine itself, which is the half neither of those
two files could supply.

#### `$B5`, extended game information — two length-prefixed strings in one reply

```
01 B5 21 00 00 05 00 00 00 00 00 0E "Multigame Pack" 06 "PT_001" 00 00 7E 4D
      ^^ ^^^^^ ^^^^^ ^^ ^^^^^^^^^^^ ^^ ^^^^^^^^^^^^^^ ^^ ^^^^^^^^ ^^^^^
      |  game  max   |  progressive |  the game's own |  paytable  wager
      |        bet   |  levels      |  name           |  name      categories
      |              progressive     name length 14   paytable length 6
      length 33      group
```

The two names are what make the offsets checkable: a layout one byte out would read the length of
the name as part of the number before it and produce a string of the wrong length, which fails
rather than decoding into something plausible. The machine that answered calls itself
`Multigame Pack` and its paytable `PT_001`.

The library can also append a list of player denominations after the wager categories. Its own
comment calls that an off-by-default extension for legacy hosts and a deviation from the
specification's table, so the layout ends where the conforming reply ends and a machine with it
turned on produces bytes the log reports as an unmodelled trailer rather than a reply that does
not fit.

#### `$4F`, the hopper status — a field that is there only sometimes

The level is in the reply only when the hopper can measure it. A machine that cannot answers five
bytes rather than nine and its length byte says so, so the field carries a condition on the byte
before it — `percent_full` not `$FF`. This is the construct the diff's own table says neither other
model can express: the toolkit can mark a group optional and cannot say *when* it is there.

#### `$B4`, the wager category — a meter as wide as the machine says

```
01 B4 0D 00 00 00 00 "9210" 04 00 00 00 00 F3 6B
      ^^ ^^^^^ ^^^^^ ^^^^^^ ^^ ^^^^^^^^^^^
      |  game  wager payback |  credits wagered, four bytes because the byte
      length   category      meter size   before them says four
```

The payback percentage is four characters of text and not a number: read as a number it comes out
as 943,265,584. And the coin-in meter takes its width from the byte before it, which is the same
general shape long poll `$6F` needed a wave ago — a field whose length an earlier field states.

### One thing the layouts broke, and it was a finding

Adding a layout for `$B4` unblocked a validation case that had been skipping on
`requires decoder.lpB4` — and the case turned out **never to have been written**. It carried a note
and no steps, so the runner errored on it rather than passing it, which is the runner behaving
correctly: a case that claims to run and tests nothing must not look like a pass.

What it needs is a machine that reports a wager category, and the software machine reports none —
it refuses a command it has no values for. So the case now says `requires machine.wager_categories`
and skips naming that. **Two cases had guessed at field names**, having been written before any
layout existed to check against: one wanted `denomination` on `$53` and one wanted `power_reset` on
`$1C`. Both guesses were right, and both are the library's own words — the library's description of
meter `$C1` is `Power reset` — so the layouts use the names the cases already had rather than the
other way round.

---

## What was deliberately not done

* **No new meter a script can read by name.** The index stayed at sixteen. Several of the
  forty-seven report the same library meter as a command that is already the answer to a meter
  name, through a handler that scales it differently: the shared meter handler divides by the
  accounting denomination under two of the machine's meter modes and the bulk commands do not.
  Quietly changing what an existing test measures is the one outcome that must not happen while
  adding coverage, so every new field is assertable on the reply it belongs to and none is offered
  as a meter. Widening the index is a wave of its own, and it needs the two commands' answers
  compared on a machine rather than reasoned about.
* **`$47` and `$2A` report the same library meter as each other** — `TotalCreditsFromCoinAcceptor`
  — so on this library the external coin acceptor and the coin acceptor can never disagree. That
  is recorded rather than modelled around, because a test written against one of them is really
  written against both.
* **The nine silent commands have no layouts.** Five of them cannot have one from this machine at
  all: it does not advertise tournament mode, so its own library declines to answer.
* **The eighteen legacy funds-transfer commands still have no reply layout.** The receiver frames
  the host's message for all eighteen, which is new; the dispatcher answers none of them, which is
  not. Nothing here should write a reply layout from somebody else's display model.
* **The real-time event format is still undecoded**, and the eight exceptions that carry data
  still carry it unread.
* **Nothing was replayed.** `PLAN.md`'s M4 also asks for a capture replayed against a machine with
  the verdicts diffed, and for a capture split into its transactions. Neither is done.

## Known limits, stated rather than discovered later

* **A sweep is one machine in one configuration.** These forty-seven layouts fit the bytes
  `QSP_DemoOpenGL` answered with the standard-validation image installed on 2026-09-08. A machine
  in another configuration may fill fields this one left at zero, and where a field was zero in
  the capture the layout's offsets rest on the handler alone. The replies where that is *not* true
  are the ones worth trusting most: `$53`, `$56`, `$B2`, `$B4`, `$B5` and `$1C` all came back with
  real values in most of their fields.
* **The classification is a safety judgement and it is ours.** The rule is stated and enforced, and
  every departure from it is named with a reason, but "this command only answers" is read off a
  handler's name. A handler misnamed in the library would be classified wrongly here.
* **The sweep leaves the machine as it found it, and does not prove that.** Nothing it sends
  changes state, by the classification above; there is no before-and-after comparison of the
  machine's memory to demonstrate it. The baseline-diff idea in D14 is what would.
* **`--framing` is the exception and says so.** It sends `$01` and `$02`, which the sweep itself
  withholds, and it sends `$02` last so the machine is left playing. That is a measurement a
  person asks for by name, not something the ordinary sweep does.
* **The machine's configured memory was replaced to run this.** The image installed is
  `fixtures/nvram/golden.nvram.gz`; what was there is kept beside it as
  `nvram.bin.replaced-20260908T054952Z` and can be put back with `sasctl nvram restore`.
