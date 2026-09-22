# M7, first wave: reading the test-script documents

What the eleven GSA EGM test-script PDFs contain, how they were read, and which of their cases this
build can run. Written 2026-09-21, before any M7 case was written, because a case list invented
ahead of the documents would quietly become the definition of "complete".

**Nothing of theirs is reproduced here.** D4 applies: the documents decide what we test, and every
word shipped is our own. Section numbers and short descriptive names are used as identifiers, which
is what `PLAN.md` asks for so that "we pass section 10.3" is a sentence somebody can say. The
extracted case text lives only in the session scratchpad and is not committed.

---

## 1. What arrived

Eleven PDFs in `SAS test script/`, gitignored. They are one suite, at revision 1.00, released
March 2004, against SAS 6.01. An introduction plus ten content documents.

| | |
|---|---|
| documents | 11 (1 introduction, 10 content) |
| pages | 827 |
| chapters | 21, numbered **3 to 23 continuously across the documents** |
| **test cases** | **612** |
| test steps | 4,381 |
| pass criteria | 5,955 |

**The chapter numbering is continuous across the whole suite**, not per document — the introduction
is chapters 1–2, required accounting is 3–6, advanced accounting 7–9, and so on to legacy meters at
23. So a chapter number identifies its document without ambiguity, which is what makes a flat
directory per chapter work.

## 2. How a case is shaped

Every case has the same seven parts: the requirement it exercises, an EGM set-up, a HostSim set-up,
the host configuration as three or four side-by-side columns, a two-column **Test Steps | Pass
Criteria** table, and notes.

Two of those matter to us more than the rest:

* **The HostSim set-up and its columns describe the *host*.** That is this tool's own job. Read as
  demands on the machine they invent requirements no case ever had, which is a mistake worth naming
  because it is the one that most inflates a coverage claim.
* **A step and its criteria are not a row each.** A criterion cell is routinely taller than its
  step cell — on one page a two-line step carries seven lines of criteria — so the columns have no
  row correspondence to rely on.

## 3. The extraction, and the trap it was written against

`CLAUDE.md` constraint 5 says a line-based extractor shears these tables, and it does. The proof is
visible on the very first page examined: a step of two lines whose criteria run to seven, where
pairing line against line hangs the CRC description on the following step. The result looks
plausible and is wrong.

So `research/tools/ts_extract.py` reads by **word coordinates**, walking each page top to bottom as
a state machine, with the two columns as independent streams — a numbered step anchors a row on the
left, a bullet anchors one on the right, and a criterion is attached to the step whose row it falls
in. Three things were got wrong on the way and each was found by checking against the rendered page
rather than against the previous run:

1. **A page carries the tail of one case and the head of the next.** Treating a page as one unit
   put the previous case's steps into the new case. Fixed by ending a case where the next one's
   requirement line begins, wherever on the page that is.
2. **The column gutter is not a constant.** The criteria column starts anywhere from x=259 to
   x=349 across these tables. A fixed gutter put the criteria bullets into the step column on about
   a fifth of the tables — 453 cases with orphaned criteria.
3. **The bullets are sometimes a few points left of the label above them.** Reading the gutter off
   the "Pass Criteria" label alone still lost them; the leftmost of the label and the bullets is
   the boundary that holds.

**Verification.** Two pages were rendered to images and compared field by field against the
extraction, and both match exactly, including the sheared row and its nested sub-bullets. Across
the whole suite, **49 cases of 612 (8%) still have at least one criterion that could not be
attached to a step** — they are carried in the output as orphans rather than being silently hung on
the nearest step, so the residue is visible rather than wrong. The concentration is in the legacy
document (19 of its 65).

## 4. The inventory

`research/tools/ts_classify.py` reads what each case *demands* — the long polls and exceptions it
names, and what has to happen at the machine — and checks those against what this build provides
(`sasctl catalogue`: 85 of 124 long polls decodable, the 10 exception payloads, 40 meters readable
by name; and the stimulus backends).

Three buckets, as asked:

* **A — this build can run it today**, unattended, against the software machine.
* **B — it needs something that does not exist yet** but could be built: a decoder, a stimulus, or
  a behaviour of the software machine.
* **C — it cannot be automated here at all.** It needs a person at a machine: reading the EGM's own
  display to compare against the host's number, an attendant, a physical fault, or a signature only
  the manufacturer holds.

| ch | chapter | A | B | C | total |
|---:|---|---:|---:|---:|---:|
| 3 | Communications | 12 | 8 | 4 | 24 |
| 4 | Required exceptions | 23 | 23 | 14 | 60 |
| 5 | Required long polls | 2 | 22 | 3 | 27 |
| 6 | Required meters | 16 | 0 | 56 | 72 |
| 7 | Multi-game | 1 | 20 | 2 | 23 |
| 8 | Weighted average theoretical payback | 0 | 1 | 0 | 1 |
| 9 | Multi-denomination | 0 | 20 | 2 | 22 |
| 10 | Tickets | 1 | 49 | 24 | 74 |
| 11 | Real-time events | 11 | 9 | 5 | 25 |
| 12 | Non-SAS progressives | 0 | 17 | 13 | 30 |
| 13 | SAS-controlled progressives | 0 | 28 | 12 | 40 |
| 14 | AFT bonusing | 0 | 9 | 2 | 11 |
| 15 | Legacy bonusing | 0 | 17 | 10 | 27 |
| 16 | AFT transfers | 0 | 74 | 6 | 80 |
| 17 | Custom AFT ticket data | 0 | 5 | 0 | 5 |
| 18 | Tournament | 0 | 10 | 0 | 10 |
| 19 | Authentication | 0 | 16 | 0 | 16 |
| 20 | Hand pay reset methods | 0 | 2 | 1 | 3 |
| 21 | Miscellaneous EGM control and legacy | 0 | 12 | 5 | 17 |
| 22 | MJT | 0 | 17 | 11 | 28 |
| 23 | Legacy meters | 3 | 5 | 9 | 17 |
| | **total** | **69** | **364** | **179** | **612** |

**Against a real machine the same split is 69 / 64 / 479**, because configuring a machine, clearing
its memory and cycling its power are buildable against the software peer and are a person's job on
a real one. That difference is the honest shape of this milestone: the software machine is where
the suite can be made to run, and a real machine is where most of it needs somebody standing there.

### What blocks the most cases

| cases blocked | what is missing |
|---:|---|
| 279 | configuring the machine into the state a case assumes |
| 168 | forcing a hand pay |
| 142 | comparing the host's number against the machine's own display — a person, always |
| 121 | moving funds |
| 110 | a progressive award |
| 81 | a hopper |
| 67 | decoding the funds-transfer reply |
| 70 / 55 | a multi-game / multi-denomination machine |
| 52 | cycling power |
| 45 | a legacy bonus |
| 28 / 27 | decoding the two legacy bonus commands |

The decoder gaps are a short list and a known quantity — the funds-transfer, registration,
authentication and legacy-bonus commands. The stimulus gaps are the milestone's real cost.

### How much to trust the split

It is a first pass, and it errs in one direction: **it over-counts A.** Every sample checked by
hand found cases called runnable that in fact begin by configuring the machine, because the
documents say that a dozen different ways and no keyword list catches them all. Three rounds of
correction moved A from 135 to 69 and it has not converged.

That is a reason to treat the table as the input to writing cases rather than the answer. **The
answer is the runner's own coverage line**: every case declares its `requires`, the runner probes
the environment, and what ran versus what skipped and why is then a measurement that moves on its
own as the build grows. That is D19, and it is why the split does not need to be hand-maintained
prose.

## 5. Two things found on the way that need a decision

### The existing conformance tree is a different revision of these same documents

`tests/conformance/10.1/` holds three cases numbered 10.1.1, 10.1.3 and 10.1.4, taken from the GSA
toolkit corpus. That corpus is the **same suite at revision 1.2**, and the section numbers moved
between revisions: what revision 1.2 calls 10.1 *Standard Cashout Validation* is 10.2 in the
documents supplied here, and its 10.2 *Secure Enhanced Validation* is 10.5 here.

So "section 10.1" names two different things depending on which revision is meant, and a tree that
does not say which will be read wrong by whoever comes next. Options in the note below.

### Chapter 6 is 72 cases and 56 of them need a person

Required meters is the largest single chapter and almost all of it compares a meter read over the
wire against the same meter on the machine's own display. The wire half is assertable and the
comparison is not. Written as one case each they would be 56 skips; written as two halves they
would be 56 assertions that run and 56 notes for a person. That is a shape decision worth taking
before the chapter is written rather than after.

---

## 6. What the tools are

| | |
|---|---|
| `research/tools/ts_extract.py` | word-coordinate extraction of the cases, one JSON record each |
| `research/tools/ts_classify.py` | what each case demands, against what this build provides |

Neither carries any text from the documents. The extracted cases are written to the session
scratchpad and are deliberately not committed.

---

## 7. The first defect the documents found

Writing chapter 3 turned up a real one in our own software machine, before any case had been
written against a real machine.

Sections 3.3 and 5.2 both say an **unsupported poll is ignored** — the refusal in 3.3 is for a bad
check sequence on a *defined* poll, which is a different thing. Our fixture refused instead. The
machine library was read rather than assumed: an undefined command has no entry in its framing
table, so its receiver waits for a length byte that never arrives and drops the packet, and the
nine commands that need machinery this fixture lacks are gated on a feature bit and return without
sending. **Not one of them ever refuses.** A machine answers or it says nothing.

So the fixture was the only one of the three peers doing something no real machine does, and a case
asserting the silence the documents ask for failed against our own peer while passing against a
real machine — the D7 disagreement, found the way D7 says it will be.

What changed, with the call sites shown before anything was edited:

* a command with **no layout** — undefined, or defined but not modelled — is now ignored;
* the **nine commands this fixture has no machinery for** (progressives, legacy bonus, cards,
  reels) are declared as a property of the machine and ignored, rather than falling through to a
  fallback;
* the "could not build the values" fallback **stays a refusal**, because it now means only one
  thing: the layout said answer and this fixture could not. That is our bug and should be loud.

The refusal was originally there to stop a test depending on a fabricated zero. Silence serves that
equally, so nothing was lost — and the gap is still counted, so a run can still say what it asked
for in vain.

---

# Three chapters written — 2026-09-22

Chapters 3, 4 and 11: 109 cases, every one of them present whether or not anything can run it.
They were chosen because the first reading put 46 of the corpus's 69 runnable cases in them.

## 8. What the chapters actually measure

| ch | chapter | cases | run | skip |
|---:|---|---:|---:|---:|
| 3 | Communications | 24 | 3 | 21 |
| 4 | Required exceptions | 60 | 4 | 56 |
| 11 | Real-time events | 25 | 7 | 18 |
| | **total** | **109** | **14** | **95** |

Against the estimate of 46 that is a quarter, and the shortfall is the over-count section 4 warned
about rather than a surprise. Where it went, chapter by chapter:

**Chapter 3 — 3 run, 21 skip.** Eleven of the twenty-one wait on one thing: the ability to put an
exact sequence of named general and long polls on the line with nothing else between them. Every
implied acknowledgement and implied NACK case is that shape, and this host cannot produce it for
two reasons at once — it runs a round on a timer, and it sends a synchronisation poll ahead of
every message, which section 3.19 says is itself an acknowledgement. Two more wait on watching for
the machine's chirp, two on measuring a duration, two on setting the machine's address at the
machine, and one each on a hand pay, a power cycle, a memory fault, a collision and a poll sent
without the wakeup bit. What runs is the framing, the unsupported poll, and the poll-rate sweep
from 4800 ms down to 200 ms.

**Chapter 4 — 4 run, 56 skip.** The largest single block is hardware nobody has: ten cases need a
hand pay, six need a bill acceptor that can be made to jam or reject, five need a printer fault,
five a memory fault, four a coin path and four a hopper. Four more need the exact poll sequence,
three a power cycle, two the machine's own exception queue depth. What runs is no activity, the
four door pairs, the seven note denominations, and the host taking the machine out of service.

**Chapter 11 — 7 run, 18 skip.** The best return of the three, because real-time event reporting is
one of the few whole features the software machine implements. What runs is the mode itself, a
backlog of ten draining in the event format, the poll-rate sweep down to 40 ms, notes in both
formats, what a note event carries, and what a game start and a game end carry. Five of the skips
are the same acknowledgement sequences as chapter 3, three need a power cycle, and the rest need a
machine this one is not — a poker game, reels, more than one game, a game recall screen, a bet
button.

Run it with:

    sasctl run tests/validation/egm-test-scripts-v1.00 --loopback --stimulus software

## 9. Two capabilities would be worth more than anything else

`host.poll_sequence` blocks twenty cases across the two chapters and is the single largest lever in
the corpus so far. It is not a decoder and not a stimulus: it is a mode in which the host stops
polling on a timer and sends exactly the messages a case names, in order, to the addresses it
names, with the synchronisation poll under the case's control rather than the engine's. Everything
about implied acknowledgement and implied NACK — in this suite and in the machine library's own
state machine — is unreachable without it.

`host.chirp` is named by six cases — 3.4, 3.5.1, 3.6, 3.23, 11.8 and 11.13 — but it would unblock
only **two of them on its own**, 3.4 and 11.8. The other four also need `host.poll_sequence`, a
power cycle, or a service button, so they stay skipped whatever is done about the chirp. After five
seconds of silence the machine sends its own address with the wakeup bit every 200 ms, and nothing
here watches the line when no poll is outstanding.

## 10. Two findings, neither of them fixed

Both were found the way section 7's was: our software machine disagrees with the documents, and the
machine library was read before deciding which of them was wrong. Neither has been changed — the
call sites are listed so the work can be agreed first.

### The software machine never chirps

Sections 3.4, 3.5, 3.6, 3.13, 3.23, 11.8 and 11.13 all turn on it. The machine library has the
behaviour: `libsas_threads.cpp` keeps a per-channel `syncSas` state with `SAS_CHIRP` as one of its
values, and `ianChirp()` enters it, resets the consecutive-poll counters and the last-received
command, and discards every action queued against the reply that was never acknowledged.

Our fixture has no such state and no such timer. It is a gap rather than a disagreement — nothing
in the suite passes because of it, because the host cannot watch for a chirp either — so the honest
form is the two capabilities above rather than a defect. Both halves are needed together: a machine
that chirps and a host that can see one.

### A long poll answered with an event — the machine does not send one, and the host would throw it away

This is the one worth deciding about. With real-time event reporting on and exceptions queued, the
machine library answers a **long poll** with the event rather than with the long poll's own reply:

    libsas_threads.cpp:3333
    if ((rxPacket.length == 1 && rxPacket.data[0] == (sc->address + 0x80))
     || (rxPacket.length > 1 && sc->isRteEnabled && exceptionsCount > 0 && rxPacket.data[0] == sc->address))

The branch that follows sends the event, and the `else` that dispatches long polls by command is
never reached, so the long poll goes unanswered. The documents say the same thing in several
places — 11.1's polling loop carries no general polls at all, and its criterion is that the game
start arrives in answer to a long poll.

Two things follow, and the second is a defect in this build rather than in the fixture:

* **Our software machine only ever puts an event on a general poll.** A test written against it
  cannot tell the two deliveries apart, which is why the chapter 11 cases that run say so in a
  note rather than claiming the criterion.
* **Our host would discard the event if a real machine sent one.** `CouldAnswer` admits the event
  format only for a general poll; for a long poll it requires the second byte to echo the command.
  An event arriving in a long poll's window is therefore counted as "this cannot be an answer to
  long poll $xx", recorded as discarded, and the long poll is reported `Silent`. The event is lost
  — not misreported, but lost — which is exactly the failure the one-behind work was about, in a
  case that work did not cover.

The call sites, all of them, before anything is edited:

| file | line | what it is |
|---|---:|---|
| `src/SasWorkbench.Core/Polling/PollEngine.cs` | 706 | `CouldAnswer` — the long-poll branch admits only an acknowledgement, a rejection, a busy response or an echoed command |
| `src/SasWorkbench.Core/Polling/PollEngine.cs` | 668 | `ReceiveAnswer` — discards and counts anything `CouldAnswer` rejects |
| `src/SasWorkbench.Core/Polling/PollEngine.cs` | 505 | `RunPendingLongPoll` — would have to route an event frame to the exception stream and decide what the long poll's own outcome then is |
| `src/SasWorkbench.Core/Polling/PollEngine.cs` | 571 | `Interpret` — classifies what came back; an event frame has no outcome today |
| `src/SasWorkbench.Core/Polling/PollEngine.cs` | 455 | `ReportRealTimeEvent` — reachable only from the general-poll path |
| `src/SasWorkbench.Egm/FakeEgm.cs` | 990 | `HandleLongPoll` — would have to answer with a queued event ahead of the command's own reply while reporting is on |
| `src/SasWorkbench.Egm/FakeEgm.cs` | 967 | `SendRealTimeEvent` — the sender, reused unchanged |
| `src/SasWorkbench.Egm/FakeEgm.cs` | 884 | `HandlePoll` — the general-poll path, unchanged |

A third, smaller, noted rather than raised as a finding: the fixture's exception queue is a plain
first-in-first-out with no bound, so it has no priority order and can never overflow. The library
has both — an overflow state per channel and a priority order the document lists in 4.11. Nothing
in these three chapters can reach either, because every case that would needs a hand pay or the
machine's own queue depth, so it is recorded here rather than turned into a case nobody asked for.
