# M5 — The casino simulation

**Status: started 2026-09-13.** 100 software machines against one or two hosts partitioned by
category rather than by machine (D29). This document records what `PLAN.md` said had to exist
first, checked against the code rather than against the plan, and the one number the milestone was
not allowed to start without.

---

## What has to exist first, checked against the code

`PLAN.md` names four things. Two exist, one exists in the half that a person reads and not in the
half a program reads, and one does not exist at all.

| what PLAN.md asks for | verdict | where that was checked |
|---|---|---|
| the category→channel map in our software machine | **exists** | `SasChannelPlan` · `FakeEgm` |
| the map recorded as provenance | **half** — printed, not recorded | `JsonLinesReportWriter` |
| the container log format of D26 | **does not exist** | nothing writes one |
| address allocation across machines | **does not exist** | `PollEngine` holds one address |

### 1. The category→channel map — exists, and does what D23 said it would

Read rather than taken on trust, because the plan describes the intention and the code is what
runs. All four halves of the rule are in place:

* the map itself is a value on the machine, not on a host — `FakeEgm` settles it at construction
  and exposes it, so every reading of it comes off the wire or off the machine;
* a long poll whose category the answering channel does not own is met with **silence**, which is
  what the gaming machine library does — the machine consults the map before it answers;
* an exception whose category a channel does not own never reaches that channel's buffer, and each
  open channel has its own buffer with its own count;
* the enabled-features reply is masked per channel, so a channel with no validation does not tell
  its host the machine redeems tickets.

The refusal D23 required is enforced in three places rather than one: the arrangement itself
refuses a category pointed at a channel with no poll address, the machine refuses to open such a
channel, and `sasctl nvram channels` refuses to write one. A script can still say it, which is what
makes the refusal testable from a script.

**What this does not yet do is scale.** The arrangement is four channels of one machine. A hundred
machines is a second dimension that nothing in it carries.

### 2. The map recorded as provenance — printed for a person, missing from the record

This is the finding of the audit, and it is small and load-bearing. The run's provenance carries the
arrangement and prints it:

```
over loopback (host end), proven, stimulus software, channels 1@01 2@02 ·
general_control=1 … validation=2 (read from the machine)
```

but the JSON-lines record — the canonical artefact, the thing D34 says everything else is a
rendering of — projects only the transport, the fidelity, the stimulus, the caveat and whether the
wakeup bit was captured. The arrangement is dropped, at both the run level and the per-test level,
so the reader that parses a run back cannot recover it.

The consequence is exactly the one D23 warned about: *which host should have sent this poll* and
*which host owns this exception* are unanswerable from a saved run. Today that is invisible, because
a two-channel run is short enough to read. Across 100 machines it is the difference between a log
and a pile of bytes.

### 3. The container log of D26 — not built

Nothing writes an uncompressed header, independently-decompressible blocks or a footer index. The
only compression anywhere in the product is the saved machine memory. Runs are written as plain
JSON lines.

Measured rather than estimated, from the baseline run below: **2,018 records for 41 cases in 94.7
seconds**, of which 1,473 are wire records. A hundred machines polled at 200 ms produce about 500
wire records a second before a single test step is counted, so an hour of floor traffic is on the
order of two million records. That is the scale D26 was written for, and it is still theory.

### 4. Address allocation — not built, and it is the milestone's real work

SAS addresses 1–127 are modelled and range-checked, so 100 machines fit. What does not exist is
anything that gives them out or polls more than one.

A host in this build is one poll engine bound to **one address**: the address is read from its
options in the poll it writes, in the address check on every reply, and in the test that tells an
acknowledgement from an exception of the same value. The thing that holds several hosts —
`ChannelHosts` — is documented as, and is, *the hosts a case has open on one machine, one per
channel*. Two channels is two hosts on one machine. A hundred machines is one host on a hundred
machines, which is the other axis entirely and has no representation anywhere.

So the milestone's first real piece of work is a host that polls a round of addresses rather than
one, and a way to name a hundred machines in a script without writing a hundred lines.

---

## The poll cycle, measured before anything was optimised

`PLAN.md` says *"a poll cycle across 100 machines at 40 ms each is four seconds per round"*. The
40 ms was an estimate. This is the measurement, and it lands close to the estimate for a reason
nobody had identified.

Taken from the committed regression suite over the in-process link — 41 cases, 1,473 wire records,
489 host-message-to-machine-reply pairs:

| | ms |
|---|---|
| minimum | 29.34 |
| **median** | **30.90** |
| 95th percentile | 31.98 |
| 99th percentile | 46.30 |
| maximum | 46.95 |

The distribution is not a spread, it is two spikes: 478 of 489 pairs land between 30 and 32 ms, and
9 land between 45 and 47 ms. A general poll and a long poll cost the same — 30.88 ms against
30.91 ms — which is the first sign that what is being measured is not the message.

**It is not the machine and it is not the wire.** The link is in-process, and the software machine's
answer delay is zero by default. The cost is the host's own end-of-message rule: once a reply has
started, the link layer keeps reading until a read returns nothing, waiting the quiet gap each time,
so **every reply is followed by one full quiet-gap wait**. The quiet gap is 20 ms.

The remaining 11 ms is the platform, measured on this machine rather than assumed:

```
Monitor.Wait(20): min 26.76 ms, median 30.39 ms, max 31.72 ms
```

A 20 ms wait is served in 30.39 ms here, and the wire measures 30.90 ms. The difference of about
half a millisecond is the whole of the framing, the transport and the machine's answer.

### What that makes the round

| | |
|---|---|
| host cost per machine per poll, measured | **≈ 31 ms** |
| 100 machines, one host, one round | **≈ 3.1 s** |
| the same round if the quiet gap were served at its nominal 20 ms | ≈ 2.0 s |
| `PLAN.md`'s estimate | 4 s |

**This is stated as a measurement and not yet as a bottleneck.** It is the cost of a host polling
one machine at a time in sequence, which is one of several shapes a floor controller can have and
is not yet known to be the shape this one will take. What the measurement does establish is where
the time goes, so that if the round has to be shortened the work goes to the right place: the 31 ms
is one timer wait per machine, not a hundred machines' worth of traffic.

The number to re-measure, once a host can poll a round, is the round itself. A per-poll figure
multiplied by a hundred is an estimate like any other.

> **Superseded by the measurement in W2 below.** The round of a hundred was measured at **6.27 s**,
> not 3.1 s. The 31 ms above is a host-message-to-reply pair, and a poll cycle sends two host
> messages for one such pair — so the synchronisation poll's own 20 ms was outside every sample.
> The table above is kept as it was written; the corrected figure is the measured one.

---

## W1 — the map into the record

The audit's finding, fixed. The arrangement travelled to the terminal and was dropped from the
canonical artefact, so a saved run could not say which host should have sent a poll.

Before changing the shape, every writer and reader was found rather than the first one:

| what | where | state before |
|---|---|---|
| constructs provenance | `ScriptRunner` | set the map |
| | `ReplayCommand` · `SweepCommand` · `EventsCommand` | did not |
| | `CaptureImporter` | did not, correctly — an import has no map |
| | `JsonLinesReader` | did not — **the gap** |
| decides the record's shape | `JsonLinesReportWriter`, run level and test level | dropped three fields |
| renders it | extended log · HTML · Markdown · JUnit · the command line · two view models | — |

Two things fell out that the audit had not named, and both are the same defect in more places:

* `RunProvenance.ToString()` never rendered the refusal, so **a machine that would not take the
  arrangement it was given read exactly like one that did**. It says so now.
* HTML, Markdown and JUnit each build their own field list, so they would have kept dropping the
  map after the record carried it. All three carry it now.

The arrangement is written as the machine describes it and read back by the same parser that
writes it, so a run's provenance line can be pasted back in to rebuild the arrangement. A record
that names one but does not say it was read from the machine is read as **declared**, never as
read: claiming a reading the file never made would turn what a case asserted into what a machine
confirmed. An arrangement this build cannot parse is reported rather than passed through.

The property is guarded where it cannot be forgotten. The arrangement went into the sample run
that the live-versus-recorded equality test renders both ways, so a field dropped from the record
fails there rather than in a test somebody has to remember to write.

---

## W2 — a host that polls a round

### The design, and why it is not a preference

SAS is RS-485 multi-drop: one host port, one pair, up to 127 machines, the address byte choosing
who speaks. Two machines answering together collide, so **one transaction at a time is all the wire
allows**. That settles the question the milestone opened with:

| shape | round | what it measures |
|---|---|---|
| **one engine cycling N addresses, one line** | **N × per-machine, serial** | a floor round |
| N engines, N links | ≈ one machine's cost, whatever N | how many links a process can drive — each engine still polls one address |

A round is therefore one engine cycling addresses. The N-engine shape is not a faster round; it is
a different measurement, and reporting it as the round would put 31 ms against a hundred machines.

### What it took

* **A multi-drop line.** `MultidropLoopbackBus`: one host end, N machine ends, everything the host
  writes reaching every machine. The software machine needed no change — it already drops a general
  poll and a long poll addressed elsewhere, which is the behaviour that makes a round mean anything.
* **The address became the transaction's, not the engine's.** Six sites, not the five the first
  reading found: the general poll, the rejection test, the real-time event address check, the long
  poll's frame, `CouldAnswer`, and — the one that was missed — `Interpret`, where a reply from a
  named machine was read as **coming from the wrong address**. It failed as a test rather than as a
  run, which is the argument for writing the test first.
* **A long poll on a round names its machine, or is refused.** Sent without a name it would land on
  whichever machine the cycle had reached, and that machine would answer it correctly — a case that
  passes while addressing the wrong machine is worse than one that does not run.

### The round, measured

`sasctl floor --machines <n> --rounds <n>`, timed between two polls of the same machine so
everything the host did in between is inside the number. Poll interval zero, because a floor host
with a hundred machines cannot keep 200 ms per machine — that is twenty seconds a round.

| machines | round, as this build polls | per machine | round, no sync polls | per machine |
|---|---|---|---|---|
| 4 | 248.7 ms | 62.17 ms | 124.7 ms | 31.17 ms |
| 8 | 498.1 ms | 62.26 ms | 248.4 ms | 31.05 ms |
| **100** | **6.27 s** | 62.73 ms | **3.15 s** | 31.49 ms |

**The hundred is measured, not extrapolated.** All 100 machines answered, nothing was dropped on
the line, and the per-machine cost is flat from 4 to 100 — which is the serial model confirmed
rather than assumed.

### The prediction was wrong by a factor of two, and here is why

W0 predicted **3.1 s** for a hundred. The measured round is **6.27 s**.

The 31 ms of W0 was measured over *host-message-to-machine-reply pairs*. A poll cycle sends **two**
host messages and produces **one** pair: a synchronisation poll of address zero, a 20 ms sleep to
keep the two apart, then the general poll and its reply with its own quiet-gap wait. The pair
measurement saw the second half and never the first, so the sync poll's own 20 ms was outside every
sample. Two quiet gaps per machine, not one — 62 ms, and 6.27 s for a hundred.

This is what measuring the round rather than multiplying the per-poll figure was for.

### The 20 ms gap is untouched, and the halving is offered rather than taken

`--no-sync-polls` halves the round, and it is **not** the default.

The reasoning is sound: a synchronisation poll exists because a host that polls one address never
shows a machine another address, and in a round the next machine's poll *is* that other address.
The reason it is not the default is that this was checked rather than assumed, and **neither peer
enforces the rule it leans on**. Our software machine models no such state at all. The gaming
machine library resets its loop-break timer on *any* received address byte, so it does not require
a different one either. So the halving is unverified against a real machine, and a real machine is
the only thing that can settle it.

The quiet gap itself stays at 20 ms. It is what decides a message has ended, and nothing here
touched it.

---

## W3 — the machine on the record, and a round in one line

### Every writer and reader of a wire record, before the shape changed

| | where |
|---|---|
| **writers, 5 in `src/`** | `WireLog.Snapshot` (the only live producer) · `Capture.ToWire` · `SessionRecorder.Wire` · `JsonLinesReader` · `ChannelHosts.Wire` (rewrites time and sequence when merging two hosts) |
| **the upstream signatures** | `WireLog.Record`, called 3× in the poll engine · the `WireObserver` delegate, invoked 3× and subscribed twice |
| **readers, 13** | `PacketExplainer` · `ExtendedLogWriter` · `JsonLinesReportWriter` · `HtmlReportWriter` · `TransactionSplitter` · `SasExchange` · `ReplayFinding` · `CatalogueSweep` · `ScriptEmitter` · `RecordCommand` · `RecordedSessions` · `SuiteViewModel` · `RoundMeasurement` |
| **tests** | 10 more construction sites |

### The address is recorded, because it cannot be derived

A host's own message carries the address in its first byte. A reply to a long poll carries it too.
But **a reply to a general poll is a bare exception code with no address in it at all** — one byte,
`$11`, and nothing else. Across a round of a hundred machines that is most of the traffic, and
which machine sent it is answerable only from the poll it answered.

`TransactionSplitter` was already the standing evidence: it learns addresses from the host's polls
and already reports the ambiguity that leaves behind.

So the field is carried, and it is nullable with a hard rule: **null means *not known*, never
*machine one*.** An import whose recorder kept no address, and a run written by an older build,
both read as null. Filling it in would attribute a whole floor's traffic to whichever machine
happened to be first.

Added as an `init` property rather than a positional one, so all fifteen construction sites
compile unchanged and only the producer sets it.

### What belongs to nobody stays belonging to nobody

The engine attributes each record to the transaction in flight, which is **not** always the machine
being polled:

* a **synchronisation poll** is addressed to address zero and belongs to no machine;
* **symbols drained off the line** before a poll goes out answered some *earlier* transaction.

Attributing either to the machine about to be polled would put one machine's traffic under
another's name. Both are recorded as belonging to nobody, and the extended log leaves the column
blank:

```
  #0     TX             50.6ms  1.80                    ← the sync poll, nobody's
  #1     TX    $01      83.1ms  1.81
  #2     RX    $01     115.4ms  0.00
  #7     TX    $04     487.0ms  1.04 0.1F
  #8     RX    $04     518.4ms  0.04 0.1F 0.51 …
```

### A round in one line

```
host connect tcp 127.0.0.1:9760 addresses 01-64 channel 1    # a hundred machines
host connect loopback addresses 01,05,09-0C                  # a list, a range, or both
host machine 05                                              # the steps that follow talk to $05
host send lp 1F                                              #   … so this one goes to $05
host send lp 1F to 07                                        # or name it on the step itself
```

`address <hh>` is untouched, so every case written before rounds existed is unaffected.

The same reader serves the command line and the script language, because two readers of one
notation drift and the first sign of it is a case that polls a different floor than the one its
author wrote down. A round printed the short way can be pasted back in — `01,05,09-0B,7F` in and
out — which is the rule the channel arrangement already follows.

Three things are refused rather than smoothed over: a machine **named twice** (a round that polls
one machine twice costs more than it reads, and removing the repeat is a decision taken on the
author's behalf), a range written **backwards**, and **address zero**, which no machine answers to.

### What happens when a long poll names no machine

The rule the poll engine already enforced, lifted to the script so it arrives as a step that
skipped rather than as a poll that was never sent:

| the round | a send with no machine named |
|---|---|
| one machine | goes to it — unambiguous, and what every case does today |
| more than one, no `host machine` yet | **refused**, with the round named |
| names a machine outside the round | **refused**, with the round named |

Not sent to the head of the round. That machine would answer it correctly and the case would pass
having addressed a machine it never named — the worst outcome available, because nothing in the
report would look wrong.

The refusal is sticky, the way a channel that could not be opened is: every step after it skips
too, rather than quietly running the rest of the case against one arbitrary machine.

Both refusals are asserted in the unit suite rather than in `tests/regression/round/`. Expressed as
cases they would be skips, and everything under `tests/regression/` runs green with **no skips at
all** — a suite carrying two deliberate ones cannot be read at a glance for the accidental one.

### What a floor of software machines deliberately does not do

A script naming a round gets a floor on one multi-drop line, and that floor has **no stimulus**. A
floor has no "the machine" to drive, and driving the first because it is first is the silent choice
this project refuses everywhere else — a case would pass having stimulated a machine it never
named. A step needing stimulus skips with that reason.

It also refuses to be both axes at once. A case that names a round *and* divides one machine
between channels is asking for something nothing here builds yet, and is told so rather than given
one of the two.

### The defect the change found

Making the address the transaction's rather than the engine's touched **six** sites, not the five
a first reading found. The sixth was `Interpret`, where a reply from a machine named on a round
came back as **`WrongAddress`** — the host reading a correct reply as a fault. It failed as a test
rather than as a run, which is the argument for writing the test first.

---

## W4 — the container log

### First, the volume premise was wrong, and the round measurement is what corrects it

The figure this wave was started on was **1.2 million records an hour**. That came from W0's
reading, which modelled a hundred machines each polled every 200 ms — about 500 records a second.

A serial round cannot do that. One multi-drop line carries one transaction at a time, and the
measured round of a hundred machines is 6.27 s. So:

| | |
|---|---|
| rounds in an hour | 3600 / 6.27 = **574** |
| records per round | 100 machines × (sync poll + general poll + reply) = **300** |
| **records in an hour** | **≈ 172,000** |
| records per second | **≈ 48** |

**An order of magnitude less than the premise.** The round measurement did not only halve the
expected round time's accuracy — it cut the expected log volume by a factor of seven, because a
hundred machines on one line cannot each be polled five times a second. The 200 ms figure is what a
*machine* sees between its own polls when it is the only machine; it is not what a floor does.

That does not make the container unnecessary. It makes it the right size of problem: an hour is a
file somebody wants to seek into, not a file that cannot be written.

### The format

```
SASWB1\n                        the mark, so the file is recognisable from the front
<header JSON>\n                 uncompressed: provenance, arrangement, round, clock base, catalogue
<block><block><block>…          each one a complete Brotli stream of its own
<footer JSON>\n                 every block's offset, length, record count, first and last timestamp
<footer offset, 8 bytes LE>     so the index is reachable from the end
SASWB1                          the mark again, so truncation is detectable
```

Brotli because it ships in the framework on both platforms, so the file needs no native dependency
to read. The codec is **named in the header rather than assumed**: a file written by a build that
chose differently is refused by name rather than decompressed into bytes that are not the run.

### What each part buys, and the test that proves it

* **The header is plain text at the front.** `sasctl log header` prints what a run was worth — the
  link, the arrangement, the round, the catalogue it was read against — having decompressed
  nothing.
* **Blocks stand alone.** A single stream over the whole body would compress better and would make
  the middle of an hour unreachable without decompressing the half-hour in front of it.
* **The footer carries each block's span of time.** That is what makes `sasctl log slice` a seek
  rather than a scan: a block whose last timestamp is before the window, or whose first is after
  it, is never read off disk. The command prints how many of the file's blocks it touched, because
  a slice that read every block would mean the file is merely compressed rather than seekable.

**The load-bearing property is that what comes out is what went in.** JSON lines stays the
canonical record; this is a framing over exactly those lines. A compression layer that is not
exactly reversible would be a second record, and then there would be two artefacts that have to be
right instead of one.

Verified on a real run rather than only in a unit test: a 2,015-record regression run packed and
unpacked back to the same SHA-256.

### It is not a second kind of run

`JsonLinesReader.ReadFile` decides by what is in the file, not by its name. So `sasctl explain`,
`sasctl replay`, `sasctl record`, `sasctl transactions` and `sasctl find` all take a container
without any of them knowing the format exists — checked by rendering the extended log from a
container and from the plain lines it was packed from, and diffing the two.

### What it refuses

* a file that is not a container — **said**, not decompressed hopefully;
* a body in a codec this build does not read — refused **by name**;
* a container **cut short while it was being written** — which is the failure a long floor session
  actually has, when the machine is stopped or the disk fills halfway through an hour. The header
  is still readable and still true; the index is gone, and nothing here guesses at where the blocks
  were.

### The real file, measured both ways

Ten genuine minutes of the hundred-machine floor, recorded rather than synthesised: **95 rounds,
28,804 records, 601.5 seconds**, at a measured round of 6.257 s. The file was then packed and
unpacked, and both sizes are the files on disk rather than a ratio anybody guessed at.

| | |
|---|---|
| plain JSON lines | **5,424,226 bytes** |
| container | **316,654 bytes** in 6 blocks |
| **ratio** | **17.13× smaller** |
| bytes per record, plain | 188.3 |
| records per second | 47.9 |
| unpacking it again | 0.50 s, and **byte-identical** — same SHA-256 |

Scaled by the measured round, which is arithmetic on a measurement and is said to be:

| | plain | container |
|---|---|---|
| one hour (≈172,600 records) | 32.5 MB | **1.9 MB** |
| a floor running all day | 780 MB | **45.5 MB** |

### The seek, on that file

Thirty seconds out of the middle of the ten minutes:

```
sasctl log slice round.saslog --from 300 --to 330
  1,434 record(s) from 1 of 6 block(s). The rest of the file was never decompressed.
```

Counted independently against the plain file, the window holds **1,434 records** — the same number.
One block of six was touched, which is the claim the format makes and the reason the footer carries
timestamps at all.

### What the ten minutes also confirmed about the round

The floor ran unattended for ten minutes at a hundred machines, and the round held:

| | |
|---|---|
| complete rounds timed | 95 |
| fastest / **median** / slowest | 6199.5 / **6257.0** / 6508.6 ms |
| per machine | 62.57 ms |
| machines that answered | **100 of 100** |
| silences · symbols lost on the line | **0** · **0** |

The spread across ten minutes is 5%, and the median is within 0.1% of the five-round measurement
taken earlier. The round is a stable number, not a lucky one.
