# M3 — The extended log, and the coverage number it produces

**Status: a run explains itself, and a run that has already happened can be explained from its own
record.** Both go through one renderer, and the two outputs are asserted byte-identical — which is
what makes the extended log a *rendering* of the record rather than a second capture. The fourth
timestamp mode landed with it.

`D34` asked for a log that explains itself: a description of each test case before it runs, and a
description of every SAS packet with its complete meaning, available from the command line and from
the window, obtainable both by asking for it before a run and by producing it from a log that
already exists.

---

## What runs today

```
sasctl run tests/conformance --stimulus software --report jsonl,extended
sasctl explain runs/20260907-095901/run.jsonl
```

and, in the window, an **Explain** button beside *Write report*, which fills an **Explained** tab
in the same well as the frames it is explaining.

## The half that decided the design

Being able to explain a run afterwards is not a convenience. It forces the explanation to be a
rendering of the record and nothing else, and that has two consequences worth stating plainly:

* **The raw record stays the only artefact that has to be right.** An explanation that turns out
  wrong is corrected and rendered again. Nothing is re-measured, and no machine is touched.
* **The live rendering can never say more than the recorded one.** If it could, somebody would come
  to depend on something that does not survive a reload.

The second is not left to discipline. A test renders a run, writes it as the machine-readable
record, reads that back, renders it again, and asserts the two strings are equal. A field added to
the renderer and forgotten in the record fails there rather than in six months. The same equality
was checked outside the tests, on a real run of the conformance tree:

```
sasctl run tests/conformance --stimulus software --report jsonl,extended --out out
sasctl explain out/run.jsonl --out out/explained.log
diff out/run.log out/explained.log        # identical
```

### What the record had to start carrying

Three things were already known when a run happened and were not being written down, so nothing
rendered from the record could say them. All three are now in it:

| | Why it is needed |
|---|---|
| what a case **declared it needs** | a description of a case is built from its name, tags, `requires` and step locations — there is nowhere else honest to get one |
| what each case **was carried over** | a per-case provenance line, so a case read on its own cannot shed the claim it was made under |
| **when each case began**, in the world | the anchor that puts a run's relative times on somebody's watch |

Nothing was captured to obtain them. They are recorded, not measured.

## Every packet gets a line

Each packet is explained from its own bytes. The kind of message, the address, the command, and —
where this build has established the layout — every field, with its value, its bytes, and one
sentence on what it carries, plus the library handler the layout was read from so the claim is
checkable in one step.

```
  #4     TX        248.7ms  1.01 0.A0 0.00 0.00 0.6C 0.13   [host's own: capability probe]
        long poll $A0 — Which optional features the machine has, for one game.
          game_number                  = 0                      0000
            Which game to ask about; zero means the machine as a whole.
        layout read from lpA0_SendEnabledFeatures
```

**A packet this build cannot decode says so and names the command.** That is the honest form of
"complete meaning", and it is the whole reason the log doubles as a measurement:

```
  #4     TX        245.7ms  1.01 0.51
        long poll $51 — Send number of games implemented, to the machine at $01
        ** known but not decoded: the gaming machine library names long poll $51 and this
           build has not established the layout of its bytes, so what they mean is not claimed
```

Four answers are possible and they are kept apart, because collapsing them would hide the thing
worth knowing: **explained in full**, **known but not decoded**, **layout did not fit** — a layout
was applied and the bytes did not match it, which is a finding rather than a gap — and **not named
at all**, for a command in no list this build holds.

### The findings that come free

Explaining a packet from its bytes means checking them, and three checks are worth having:

* **the frame check sequence**, on the messages that carry one — a message of two symbols or fewer
  carries none, which is the rule the machine itself applies when it decides whether to answer;
* **where the wakeup bit is** — missing from the first symbol means no machine would read the
  message as an address and none would answer; present on any other symbol means it is somewhere it
  does not belong; and present on *anything* coming back from a machine is always a fault, because
  only a host may set it;
* **the address a reply echoes**, against the address being polled.

### The one genuinely ambiguous packet, and what is done about it

A one-byte answer from a machine is an exception code, unless it is that machine acknowledging or
refusing — which is only knowable if the address is. The address is learned from the host's own
polls in the same record, so within any real session both readings are told apart. Where a record
never showed an address, **both readings are printed and neither is chosen**, and the packet counts
as named rather than explained. That case is rare and it is not guessed at.

## The packets no step asked for

A reader counting packets against steps comes up short, and the reason is that the host sends polls
of its own. Two kinds:

* the **capability probe** — the enabled-features poll `$A0`, sent before any step runs, for a case
  that declares a requirement only the machine can answer, such as `egm.validation`;
* a **meter baseline** — the read that establishes what `+= 2000` is measured against, taken before
  the first step and again before every stimulus step.

**How the log explains them.** The reason is written onto the record by the host as the poll is
sent, and read back when the log is rendered. Nothing is inferred from where a packet sits relative
to the steps — that would be a guess dressed as an explanation, and it would also be wrong, because
the baselines are interleaved with the steps rather than sitting before them. The packet carries a
margin note, the section header says how many there were and why, and the tally counts them:

```
WHAT CROSSED THE WIRE
  69 packet(s), timed from the moment the link opened in this case. 18 of them the host
  sent on its own behalf, with no step asking: capability probe, meter baseline. They are
  marked in the margin.
```

A case where every packet is accounted for says *that* instead, rather than saying nothing.

## The test-case description, and what it does when a script is thin

A description is assembled from what the script says about itself: the name, the labels, each
declared requirement with the line it was declared on, and the shape of the case counted off its own
steps — how many there are, which lines they span, and how many make a claim about the machine.

```
WHAT THIS CASE IS
  written at     tests/conformance/10.1/10.1.3-cashout-ticket-1.sas:26
  began          09:59:02.246 local  (2026-09-07 07:59:02Z)
  labelled       @needs-stimulus
  needs          egm.validation == standard   (declared at line 29)
                 decoder.lp3D   (declared at line 30)
                 stimulus.bill   (declared at line 31)
                 stimulus.button.collect   (declared at line 32)
  does           24 step(s), between lines 34 and 78, 14 of which make a claim about the machine
  carried over   loopback (host end), proven, stimulus software — …
```

A case that carries no labels and declares nothing it needs gets a thin description and is told so:

> This case says nothing about itself beyond its name and the line it is written on: it carries no
> labels and declares nothing it needs. That is a description of the script rather than of the case,
> and the way to a fuller one is to say in the script what the case assumes.

That is `D34`'s rule kept rather than softened. Filling the gap with prose would put words in the
author's mouth, and a log that invents a purpose for a case is worse than one that says the case
never stated one.

## The coverage number, and why there are two of them

```
WHAT THE WHOLE RUN EXPLAINED, COUNTED
  packets        78
    explained in full            78  (100%)
    known but not decoded         0  (0%)
    layout did not fit            0  (0%)
    not named at all              0  (0%)
    of which the host's own      20

  long polls     3 distinct command(s)
    explained in full             3  (100%)  $20 $3D $A0
    known but not decoded         0  (0%)
    not named at all              0  (0%)
```

**The second tally is the one that means something, and the first would be misleading alone.** A
poll loop is the overwhelming majority of any session's packets and every general poll is explained
in full, so a count over all packets reads as near-total coverage however little of the protocol
this build understands. The count over the distinct long polls a run exchanged does not flatter
anything. Both are printed, and which is which is said in the log.

A command counts as explained only when **every** packet carrying it was explained. A command whose
request decodes and whose reply does not is not an explained command — rounding that up would hide
the exact gap the tally exists to expose, since the interesting half of a long poll is almost always
the answer.

Anything in the second list is named underneath it, because that list is the direct input to the
breadth work of M4:

```
  Each command listed as known but not decoded is a layout this build has not established.
  They are what this log leaves unexplained, and therefore where breadth work is worth most.
    $21  Rom signature
    $51  Send number of games implemented
```

## Reading a record written by another build

A record is read strictly, and what cannot be read is reported before the log rather than after it:
a line that is not JSON, a packet belonging to a case the file does not describe, a verdict this
build does not know, a channel SAS does not define. The one worth naming is the link weight: an
unknown one is read as **unverified** and reported, never rounded up to *proven*. Honest labelling
applies to reading a record as much as to writing one — reading an unknown weight generously would
make somebody else's run look worth more than it is.

## The fourth timestamp mode — wall-clock time

`D28` named four timestamp modes and three were built in M1.5. The fourth landed here, and it
belongs here: it exists for matching a run against something outside it — another tool's log, a
machine's own log, or somebody's account of what they were doing — which is exactly what somebody
reading an extended log is doing.

It needs nothing added to a record. The session already carries the instant it began and every
record already carries its elapsed time, so wall-clock is the one plus the other, derived like the
other three and never written into the log.

Two things about it are deliberate:

* **It is local time, not universal**, because the thing it is compared against was read off a
  screen in the room the machine was in. The extended log prints both for a case's start.
* **Asking for it without the instant the session began is refused**, not answered from the current
  clock. A session recorded yesterday and read back today would otherwise be stamped with today's
  time and nothing would say so — a wrong answer indistinguishable from a right one.

A time of day is written as a time of day; the other three modes are durations and stay in
milliseconds. Both surfaces format through the same function, so a mode cannot read one way in the
window and another way in a report.

## Known limits, stated rather than discovered later

* **Steps and packets are shown as two sections per case, not interleaved.** They are recorded on
  two different clocks — a step measures from the start of its case, a packet from the moment the
  link opened inside it — and interleaving them would claim a precision the record does not hold.
  The window's session stream is where the two are genuinely on one clock, and that is where to look
  for the ordering between a stimulus and the exception it caused.
* **A packet's time is relative to the link opening in its own case.** The absolute anchor is the
  case's start, printed in its header. Adding the two is close but not exact, because the link opens
  a little after the case begins, and the log does not pretend otherwise.
* **The whole-run tally counts every case's packets, and a case that was skipped before its link
  opened contributes none.** A run that skipped everything reports no packets rather than perfect
  coverage of nothing.
* **A one-byte reply carries no command byte**, so it is never attributed to the long poll it
  answers in the command tally. That keeps the tally about what the bytes say; a refusal to `$51` is
  explained as a refusal, and `$51` is still counted as undecoded from the host's side.
* **The suite is still intermittently flaky under load.** Three of the five full-solution test runs
  made during this wave failed one timing-sensitive engine test, a different one each time, and one
  of those three was with this work reverted — so it is not from this wave. It is the same class
  `D33` addressed and it has not gone away. See `BLOCKERS.md §4`.
