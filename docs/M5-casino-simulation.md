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
