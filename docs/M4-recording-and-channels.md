# M4 — Recording what a person did, and proving two channels

**Status: done, 2026-09-09.** The last two things `PLAN.md` asked of M4: a window that records what
somebody does by hand and emits a script that does it again, and a second channel, proven rather
than carried.

```
sasctl record --caveats                              what a recording cannot capture, and why
sasctl record <run.jsonl> --out <dir>                a script out of a session already on disk
sasctl catalogue channels                            the seven categories, and what each decides
sasctl nvram channels "1@01 2@02 validation=2"       a machine divided between two hosts
sasctl run tests/regression/channels --loopback --stimulus software
sasctl run tests/conformance/10.1 --loopback --stimulus software --tag finding
tools/qxcom-tcp/run-two-channel.ps1                  two hosts on QSP_DemoOpenGL
```

---

## The numbers, before and after

| | before | after |
|---|---|---|
| the catalogue, replies with a layout | 83 of 124 | 83 of 124 |
| the catalogue, meters readable by name | 40 | 40 |
| the catalogue, event payloads established | 10, and 2 never produced | 10, and 2 never produced |
| the catalogue, **long polls gated on a channel category** | — | **35 of 124** |
| the catalogue, **exceptions filtered by category** | — | **16 of 122** |
| our own suite, distinct long polls explained | 57 of 57 (100%) | 57 of 57 (100%) |
| our own suite, distinct events explained | 4 of 4 (100%) | 4 of 4 (100%) |
| our own suite, packets | 1,488 | 1,673 |
| our own suite, cases | 41 pass · 38 skip | **50 pass · 39 skip**, and one deliberate failure |
| `tests/regression`, cases | 33, no skips | **39, no skips** |
| the imported corpus, distinct long polls | 18 of 18 (100%) | 18 of 18 (100%) |
| the imported corpus, packets | 1,128,939 | 1,128,939 |
| a fresh sweep of the software machine | 76 of 83 (91%) | 76 of 83 (91%) |
| tests | 869 | **915** |

**No coverage figure of the catalogue moved and none could.** This wave establishes no reply
layout: it reads three tables out of the library that decide *who* may be told what, and it
records what a person did. The two new catalogue rows are those tables, and they are new
measurements rather than movements of an old one.

The suite's packet count went up because the two-channel cases put two hosts on one machine and
both of them poll.

### The numbers this wave does produce

| | |
|---|---|
| channel categories, from the library's own structure | 7 |
| long polls gated on one | 35 |
| exceptions filtered by one | 16 |
| enabled-feature bits masked per category | 5 categories, 6 runs of bits |
| gated handlers byte-identical between the generator's libsas and the machine's | **29 of 29** |
| two-channel cases green against the software machine | 4 of 4, no skips |
| two-channel cases green against `QSP_DemoOpenGL` | **3 of 3 that touch a wire**, 1 needs a person |
| GSA 10.1.4 against the software machine | 16 assertions held, **2 failed — the finding** |
| recorded scripts that run again unattended and pass | 2 of 2 |
| recorded script replayed against `QSP_DemoOpenGL` | **13 of 14 assertions held** |

---

## Part one: two channels

### What the machine says, and where it says it

Three tables decide which host is told what, and all three are now generated out of the gaming
machine library rather than written down here.

| table | read from | what it decides |
|---|---|---|
| the seven categories | the one-bit fields of `sChannelControl` | what a channel can be given |
| 35 commands → a category | the gate at the top of each handler | which host may ask |
| 16 exceptions → a category | the five `is…Exception` predicates | which host is told |
| 5 feature masks | the function that clears them | what a channel says of the machine |

The platform above the library lists the same seven categories in the same order, as seven
two-bit fields each holding one channel number. That is where D23's disjointness comes from and it
is worth restating: **a category cannot be on two channels because the field holds one value.** It
is a property of the machine, not a rule a host has to police.

The gate is silence, in the library's own words — *"Otherwise ignore the LP"* — and it returns
without sending anything. Silence and a refusal are not interchangeable: a refusal says the command
was understood and declined.

**Checked against the revision the machine actually runs, not only the checkout the generator
reads.** `BLOCKERS.md` Trap E says that has to be done per question. Twenty-nine gated handlers,
the five predicates and the mask function are **byte-identical** between `C:\Progetti\libsas` and
the copy `QSP_DemoOpenGL` builds from, so Trap E does not reach this question.

### The finding: the cashout ticket poll is gated on nothing

GSA 10.1.4 asks a machine to print a ticket while the host is on the validating channel, then to be
asked for that ticket from a channel that owns no validation. Its pass criterion: *the EGM ignores
the poll, or answers it with a zero validation ID and a zero amount.*

The library does neither. `lp3D_SendCashoutTicketInformation` carries no channel gate at all, and
reads the machine-wide validation buffer; its only refusal is section 15.5's, for a machine
performing secure enhanced or system validation. So a host that was never given validation is
answered with **the ticket's real validation number and its real amount**.

Measured twice. Against the software machine, which models the library:

```
tests/conformance/10.1/10.1.4…:85  expect field validation_number of lp 3D Equal 0
  expected  0
  actual    64226492
tests/conformance/10.1/10.1.4…:86  expect field ticket_amount of lp 3D Equal 0
  expected  0
  actual    2000
```

And against `QSP_DemoOpenGL` with two channels really open, where the poll is answered on the
channel that owns no validation — the machine having printed no ticket, so the number is zero
because nothing was printed rather than because the channel was refused (case VC-03).

Steps 2, 3, 4 and 6 of the document all hold. Step 4 is worth its own line, because it holds for a
reason: `$3D` is in **none** of the five exception predicates, so it reaches every open channel's
buffer, which is exactly what the document requires. The same absence that satisfies step 4 breaks
step 5.

**The case is left failing.** It carries `tag @finding`, and two gates use that label: the suite
gate runs `--skip-tag finding` and still expects a tree in which nothing could be tested, and a
second gate runs `--tag finding` and expects it to **fail**. So a green suite does not hide the
finding, and the day the library gates that poll the second gate goes red and somebody comes and
takes the label off.

### Two channels on a real machine, with no money at all

The stronger result, and it was not expected to be available: `QSP_DemoOpenGL` opened a second SAS
channel and answered both hosts.

Nothing in QSP or QSP_DemoOpenGL was edited. Three things had to line up and all three already
existed:

* the demo's own configuration declares two network serial ports, `Network 1` and `Network 2`;
* the TCP transport already reads a **per-port** endpoint, so the two ports reach two different
  hosts rather than both dialling one;
* the poll address of the second channel and the category map live in the machine's memory, and
  `sasctl nvram channels` writes them — which is the one configuration a person **cannot** reach
  through the machine's own audit menu, because that menu skips a channel with no poll address
  while cycling and so can never point a category at one.

The platform's own trace is the evidence that it worked, and it is the machine talking about itself:

```
internalQueueException: exception 0x18 on channel 0 count : 12
internalQueueException: exception 0x18 on channel 1 count : 1
threadTimers: Channel Link Up …          (twice)
threadDecode: sendHostPacket with exceptioncode:18 on channel:0
threadDecode: sendHostPacket with exceptioncode:18 on channel:1
```

Two channels, each with its own exception buffer and its own count. On the wire, `$1F` answered
`011F5158…` on channel 1 and `021F5158…` on channel 2 — one machine, two addresses, the same
identity bytes after the echoed address.

`tests/validation/two-channel/` holds it. Three cases run and pass; the fourth is two notes about
what no host can read for itself, and it skips saying so.

**A real machine was proven and the software machine was not the only peer that could do it.** What
a real machine could *not* do is GSA 10.1.4 itself: printing a ticket needs credits, and this
machine has no licence and returns every note before its money handler sees one — `BLOCKERS.md`
Trap D, measured again here with `AsyncEnableBillValidator(0)` in its own log. So 10.1.4 is
established against the software machine only, and the half of its step 5 that needs no money is
established against the real one.

### What a host cannot read, and is therefore declared

No long poll reports how a machine divides itself. So the arrangement on a run's record is either a
reading or a declaration, and the report says which:

```
over loopback …, channels 1@01 2@02 · general_control=1 … validation=2 (read from the machine)
over tcp 127.0.0.1:9781 …, channels 1@01 2@02 · validation=2 (declared by the case)
```

Against a machine this build made, it is a reading. Against anybody else's, it is what the case
said it would be — and the cases are what check the machine against it.

For the same reason **the arrangement is deliberately not a capability requirement.** Nothing can
answer "does this machine open two channels" before the link opens, so opening the second channel
is the measurement: a machine with one makes that step a skip naming the channel, which is what GSA
10.1.4 asks for in words. A channel that could not be opened or moved to then **blocks every step
that follows** rather than leaving them on the channel they were already on — running the second
half of a two-channel case against the first channel would pass, and would mean nothing.

### The configuration that has to be refused

D23's second consequence, now enforced: a category pointed at a channel with no poll address
reaches no host at all. `SasChannelPlan` refuses it with the category and the channel named, the
software machine falls back to its one channel and carries the refusal on the record, and
`sasctl nvram channels` refuses to write one. A script can still *say* it — `machine channel 2
address 00 owns validation`, because zero is how the machine itself stores "no address" — which is
what makes the refusal testable from a script rather than only from C#.

### Four things this half had to correct in itself

Each was wrong first and measured second.

**1. An assertion matched the wrong host's exceptions.** The runner kept one list of exceptions and
one set of consumed sequence numbers. Two engines number from zero, so reading an exception on one
channel marked another channel's unrelated exception as read; and an assertion matched any
channel's copy, so "the non-validating host was told about the ticket" would have passed on the
other host's copy. Both are now keyed on the channel, in one small class whose whole purpose is
that the rule cannot be forgotten at a call site.

**2. A second link was waited for before it was opened.** The wait for a machine to dial in was
placed before the engine started — and starting is what opens the socket. So the first run against
the real machine reported a second channel that machine *does* have as absent, after waiting ninety
seconds on a socket nothing could have dialled.

**3. Two cases asserted a silence that was only the framing.** `host expect no reply to lp 4D`
passed on both peers, and it proved nothing: the machine's receiver frames `$4D` at five bytes and
drops a shorter message as incomplete before any handler sees it. Of the nine validation commands,
only `$57` and `$70` can be asked with nothing after the command byte — so only their silence is
the gate. The runner now **refuses** to assert a silence that would only be the framing, and says
so with the length the receiver expects.

**4. `sasAddSasChannel` is always given the second port.** Read on the way past and not fixed here,
because nothing in QSP is edited: the platform's loop over channels 2 to 4 passes
`SerialPort::eNetwork2` whatever the index, so a machine configured for three or four channels would
give them all one port. It does not affect two channels, which is what this wave needed.

---

## Part two: recording what a person did

### The two streams, and why only one of them can become a script

A recording holds two things and they are not interchangeable.

* The **actions** are what the person did. Exact, reproducible, and the only thing a script can be
  built out of.
* The **wire** is what the machine answered. Not reproducible at all, and useful only for deciding
  what may honestly be asserted.

Building a script out of the second is the mistake the whole design is arranged against: a script
that replays a machine's answers asserts its way to a pass without ever doing anything.

So the window feeds the recorder its own intents — the same `ScriptStep` a hand-written script
would carry, not a label somebody has to translate — and the machine's replies are read afterwards
to decide what may be claimed.

### What is refused rather than written down

`sasctl record --caveats` prints all five. The three that matter:

**A stimulus nobody wired.** A person may press a button the backend cannot honour. The action is
kept with the backend's own refusal and **no step is written**: a step would drive a machine nobody
drove, and every assertion after it would describe a machine in another state. The window offers
every stimulus the language has a step for whatever the backend can do, deliberately — a button
hidden because the backend cannot honour it leaves a person wondering whether the machine has no
door or the build has no button, whereas one that refuses says which.

**A value the machine chose.** Which fields are the machine's settings and which are its condition
is not guessed at: it is the table the replay classifier established, where a name in neither list
is reported rather than assumed harmless. A configuration field becomes a literal; a meter, a clock
or a condition becomes a line in the caveats with the values it was seen holding:

```
#   a value the machine chose rather than the person: …
#     - total_bill_value of lp $20 = 0, 20, 30
```

The one machine-chosen value that *can* be pinned down is a ticket's own number, because the
language compares a reply against the ticket the printer filed:

```
host expect field validation_number of lp 3D == ticket.validation_number
```

and where the script prints no ticket of its own, that comparison has nothing to compare against
and becomes a caveat instead of an assertion.

**A person's hesitation.** No `host wait` is ever written. Where the person was plainly waiting for
the machine, an expectation with a window is written instead — what was measured, times three,
floored at five seconds — and the script's own header says the number is the tool's and not the
person's.

### A step per episode, and the ambiguity that comes with it

The unit is the episode from `sasctl transactions`, not the packet: a session's wire is hundreds of
exchanges of which most are the poll loop waiting. Because the episodes come from that splitter,
the places where an episode's boundary is a guess are already named, and a step written over one
carries the same warning:

```
# note accepted: a second note accepted episode opened while one was still open, and nothing
# in these replies says which of the two any of them is about
host send lp 3D
```

### What came out

Two recordings, driven through the window's own commands — connect, the stimulus panel, the long
poll panel, the meter read — and both run again unattended and pass. They are committed under
`tests/regression/recorded/`, which means they carry that tree's invariant: green against the
software machine with **no skips at all**.

```
test "money: a note, a cashout, and the host reading the ticket" {
  requires printer.wallet · stimulus.bill · stimulus.button.collect · stimulus.settle · decoder.lp1E · decoder.lp3D

  host connect loopback address 01
  host poll every 200ms
  egm settle
  egm bill insert 2000
  host expect exception usd_20_bill_accepted within 5s
  host send lp 1E
  egm press collect
  host expect exception cashout_ticket_printed within 5s
  egm expect ticket printed within 20s
  host send lp 3D
  host expect field validation_number of lp 3D == ticket.validation_number
  host expect field ticket_amount of lp 3D == ticket.amount_cents
}
```

Nobody wrote the `requires` lines, the exception names, the windows or the ticket comparison. What a
person did was click.

### Replayed against a real machine

The money-free recording, replayed against `QSP_DemoOpenGL` through the peripheral simulator:
**13 of its 14 assertions held.** The door opened and shut and both exceptions arrived; every field
of the identification reply matched. The one that did not is `serial_number`, and the demo answers
an empty one.

`sasctl replay` over the same recorded session says the same thing in its own words — 1 agrees,
1 **configuration**, nothing unexplained — against a control of 2 agreeing when the recording is
replayed against a fresh copy of the machine that produced it. The recorded script's one failure
and the replay's one classified difference are the same difference, which is the two halves of this
project agreeing about a machine.

The recording that needs money fails against that machine, and fails on the money: no note
accepted, no ticket, `AsyncEnableBillValidator(0)` in its log. Trap D again.

### What a recording still cannot do, and what had to be corrected

**A recording stops before the machine has finished answering.** A machine reports one exception
per poll, so what the last click caused is still inside the machine when somebody lets go of the
mouse. The first version stopped at that instant and produced a script that opened a door and
asserted nothing about it. Stopping now waits for the machine to go quiet — the same rule the
runner's own `forget exceptions` uses.

**Which exception a stimulus caused is not decided by timing.** It was, at first, and a window
measured between one click and the next attributed a door's exception to whatever was clicked
after the door. It now comes out of the library's own symbol names, through the same derived
tables the splitter uses: which codes say a door of that name opened, which say a note was taken.
A stimulus this build cannot map that way falls back to adjacency and says so.

**The connection is a fact about a recording, not an action inside it.** A person connects,
watches for a while and only then presses Record. The link goes in as the recording's preamble;
without it the script had no `host connect` and could not run at all. A recording holding only its
preamble is reported as one in which nothing was done, rather than as a script that connects to a
machine and stops.

**A window is measured, not required.** One of the emitted windows is 27 seconds, because the
machine's door-closed exception arrived nine seconds after the door was shut — the host having
spent those nine seconds reading forty meters. The number is honest and generous and it is the
tool's, which the header says; it is not a claim that any machine may take 27 seconds.

**Nobody's mouse was involved.** The two recordings were produced by driving the window's own
commands — the same commands a click invokes, in the same order — rather than by a person
clicking. What a real mouse adds beyond that is the mouse. The window itself was rendered
off-screen with Avalonia's binding diagnostics on, both new instruments included, and reported
**no binding errors**; the specimen run now renders every instrument rather than the two it used
to, because a tab control realises only the selected tab and an instrument nothing renders is one
whose bindings nothing checks.

---

## What was not done, and why

* **No recorded case under `tests/conformance`.** A recording is evidence about one session on one
  machine; a conformance case is a claim about what any machine must do. Promoting one to the
  other is a person's judgement and this build should not make it look automatic.
* **`sasctl record` cannot recover a stimulus.** Nothing in a recording says a person opened a
  door, only that the machine reported one. Every episode that opened on a machine event comes back
  as an action that cannot be reproduced, with that sentence attached, rather than as a step
  guessed from the exception after it.
* **Nothing was recorded over serial or on two channels at once.** No adapter is attached, and the
  window drives one link at a time; the emitter writes `host channel N` where a recording carries
  one, and nothing here has produced one.
* **The suite is still load-sensitive.** One run of the whole suite failed one timing-sensitive case
  — long poll `$B4` silent at 199.8 ms against a 200 ms deadline — while three MSBuild nodes were
  finishing behind it, and passed on every run afterwards. Same shape as the residual
  `BLOCKERS.md` already records, at a load worth naming.
