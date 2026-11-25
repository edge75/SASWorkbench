# Decisions

Twenty decisions taken with the user on 2026-09-03/04, before any code was written.
Each was put to them explicitly. **Do not silently revisit these.** If new evidence
contradicts one, say so and ask.

---

## The goal, in one sentence

**SasWorkbench turns the GSA SAS Toolkit conformance corpus into unattended regression
tests that run all-local on a laptop against QSP + libsas.** Replacing IGT's
`sastest.exe` is a side effect, not the objective.

The user's own words: *"Launch an unattended test that gives stimuli to both EGM and SAS
Host and see if the other part behaves well according to the SAS Specifications."*

A test drives **both ends**. The SAS host asserts; the EGM gets stimulated. A step like
"play 2 games" is explicitly *not* the SAS host's job.

---

## D1 — Test targets
TCP software EGM (primary) · real board over USB-serial · trace replay.
Explicitly **not** a real board wired to a CI runner.

## D2 — Simulator transport
**TCP carrying 9-bit SAS symbols**: little-endian `uint16` per symbol, bit 8 = wakeup,
bits 0–7 = data.

Rejected: a virtual COM pair (com0com). Reason: it transports 8-bit bytes, so the wakeup
bit is lost — and address-vs-data detection (`libsas_uart.cpp:231`) is the most
protocol-critical behaviour in SAS. Tests would pass while the real link was broken.

## D3 — Audience
All four: the user, other QSP/libsas devs, QA engineers, and customers/integrators.
Consequences: syntax must be readable without a manual; no build step to run a test;
errors must name the line and say what was expected.

## D4 — Spec IP
**Our own words; protocol model derived from libsas's own source.** The SAS 6.02 PDF is
stamped *"IGT … Confidential and Proprietary / Distributed to Quixant LTD"*, and the app
may ship to customers, so no third-party spec text is redistributed. The spec and the GSA
XML are internal cross-references only.

## D5 — Script form
**Purpose-built DSL + YAML + a .NET library**, all over one engine.
YAML for simple linear flows, DSL when timing and assertions get real, C# when a dev needs
actual logic. Plus **GUI session recording** that emits a script.

## D6 — Hardware spike
**Deferred.** No serial hardware is available. Build against the TCP simulator. The serial
backend still ships, behind an honest capability probe, labelled unverified until measured.

## D7 — EGM peers
**Both.** A C# fake EGM for fast unit tests, and a libsas-backed bridge for real
integration. **Run the same scripts against both and diff the verdicts** — disagreement is
a genuine finding in one of them.

## D8 — V1 shape
**Thin vertical slice, CLI and GUI together.** Narrow but complete, so every layer is
exercised before anything is widened.

## D9 — Home
`C:\Progetti\SasWorkbench`. CLI binary `sasctl`. GUI `SasWorkbench`. Org `quixantplc`.

## D10 — Stimulus
**Pluggable `stimulus` step with swappable backends**: `manual` (prompts a human), `qpm`
(drives the simulator, unattended), `none` (**skips**, reported as skipped — *never*
silently passes).

## D11 — Orchestration
**One script with `egm` and `host` steps, one process, one clock.**
Rationale: SAS assertions are about timing relative to EGM events. Two processes means two
clocks and "within 5 seconds" becomes guesswork.

## D12 — Ticket printer (in `qpm_simulator`)
Printer panel rendering the ticket with a real barcode · a **wallet** so printed tickets can
be re-inserted to close the print→redeem loop · **JSON per ticket so tests assert on fields,
never on pixels** · and **import of externally-authored JSON tickets** for negative cases
(expired, wrong checksum, foreign machine, already redeemed).

## D13 — Run report
**Self-contained HTML by default**, with the raw event log embedded inside it so it stays
one file but remains machine-readable. `junit`, `md` and `jsonl` all available and mixable:
`sasctl run tests/ --report html,junit`.

## D14 — State reset
**All four, composed.** Relative assertions are the *default discipline* (`+= 2`, never
`== 2`, because SAS meters only accumulate). Plus NVRAM snapshot/restore, opt-in RAM clear
for tests that declare it, and baseline-diff to catch side effects nobody asserted.

## D15 — Change scope
Approved: `QSP_DemoOpenGL` build wiring · `qpm_simulator` (printer, wallet, external-control
mode, race fix) · a golden `nvram.bin` fixture · a deterministic-RNG option.

**Superseded in part by D30**, which withdraws the branch permission entirely: nothing in
`QSP`, `libsas` or `QSP_DemoOpenGL` is edited at all. The rest of D15 stands —
`qpm_simulator` changes, the golden fixture and the deterministic-RNG option are still
approved.

**Constraint from the user, as it stood:** changes to `QSP_DemoOpenGL` live on a **branch**
and in a **dev-only preset** — no customer ever builds them. And: *"I don't want to change
the QSP repo at all."*

**How that is satisfied** — see `BLOCKERS.md`. Short version: the missing `libqxcom`
implementation lives in *this* repo; `CMakeUserPresets.json` is already gitignored
(`QSP_DemoOpenGL/.gitignore:131`) so the wiring is not a tracked file; and libsas is
*rebuilt* with an existing target, not edited.

## D16 — Channels
**First-class in the model, script syntax and reports from day one; ship 1, prove 2.**
libsas supports 4 (`SAS_CHANNEL_1..4`) with per-channel exception buffers and
`getAftControllingChannel()`. GSA 10.1.4 needs two. Retrofitting a channel dimension into a
poll engine built for one is painful, so carry it from the start even while unused.

## D17 — EGM role
**Mine `gsat.exe`'s 599 domain symbols into a behaviour/assertion checklist first**, then
decide. The codec is bidirectional anyway; the C# fake EGM stays an internal fixture —
libsas *is* the real EGM under test.

## D18 — Suite layout
`tests/conformance/<GSA id>/` for the official corpus (so "we pass 10.1.1–10.1.7" is a
sentence you can say) and `tests/regression/<feature>/` for our own. Tags cut across:
`@wire-only @needs-stimulus @destructive @slow @channel2 @requires-ram-clear`.

**Extended on 2026-09-05 with a third tree**, `tests/validation/<document>/`, for suites written
from a machine-validation document. It exists because the regression tree carries an invariant the
other two cannot: everything under it runs green against the software machine with **no skips at
all**. A document-driven suite is mostly cases no build can run yet, so putting one there would
either destroy that invariant or force the suite to be trimmed to the convenient cases. The two
original trees are unchanged. This is the shape M7 needs.

## D19 — Capability requirements
**Each test declares what it needs; the runner probes the environment and skips-with-reason
when unmet.** Never fail a test for an environmental reason — that trains people to ignore
red. Requirements are checked, so unlike tags they cannot go stale.

```
test "10.1.3 Cashout Ticket 1" {
  requires stimulus.bill
  requires stimulus.button.collect
  requires printer
  requires egm.validation == secure_enhanced
}
```

## D20 — Linux
**SasWorkbench builds and its tests run on Linux CI from M1** (matrix: windows-latest +
ubuntu-latest) so portability never rots. Deferred: the Linux serial backend (must use
native `termios`+`CMSPAR`, or **refuse** — `System.IO.Ports` silently no-ops MARK/SPACE
parity on Linux with no error), and the full rig on Linux (needs `qpm_simulator` headless).

---

## Cross-cutting principles the user endorsed

1. **Never silently pass.** An unavailable capability is a `SKIP` with a reason.
2. **Never silently degrade.** If the wakeup bit cannot be transmitted, refuse — do not
   drop it and carry on looking healthy.
3. **Assert on data, never on pixels.** Tickets are JSON for tests, images for humans.
4. **Honest labelling.** A green suite on unverified hardware must not look identical to a
   green suite on good hardware.

---

# Decisions 21–29

Taken with the user on 2026-09-04, at the end of the M1.5 design phase. Same standing as the
first twenty: **do not silently revisit.**

## D21 — UX direction: **The Session Bench**

Proposal A's shell over Proposal C's substrate, with Proposal B's suite explorer as one
instrument. Chosen from `.planning/design/UX-PROPOSALS.md`.

The load-bearing part is the substrate: the centre panel is **one time-ordered session stream**
(TX, RX, exception, stimulus, prompt, assertion, verdict) on one clock, filtered by default to
TX/RX so it looks like today's wire panel on day one. The shell is A's — a header band carrying
provenance that no view can scroll away, an instrument rail, a persistent spine, a footer.

## D22 — UI direction: **Bench**, with two grafts

From `.planning/design/UI-PROPOSALS.md`. Four fixed bands, a recessed trace well, readouts in
drawers rather than tabs, achromatic chrome so all chroma belongs to data and state.

Grafts: **selection drives the decode** (select a received row, the decode drawer fills — from
Console) and a **report mode** rendered from the same run report the HTML output uses (from
Dossier). See D24: the report mode is demoted, not dropped.

Binding, and not to be quietly dropped later: **the word, the failure-only solid fill, and the
vector glyph.** Pass and fail cannot be separated by colour under deuteranopia in any palette —
measured at 1.00:1 to 1.19:1 across all eight variants — so those three cues are the whole
mitigation. Making chips icon-only, or outlining failure for visual consistency, silently breaks
the accessibility contract and makes the contrast tables untrue.

## D23 — One log, many channels; categories cannot overlap because the EGM will not let them

**One recording per session, whatever the channel count.** The stream carries a channel lane, so
two channels interleave in one log rather than producing two logs that have to be aligned later.

The reason two hosts do not contend is **structural in the machine, not a rule the host has to
police**. QSP assigns each of seven categories to exactly one channel, as seven 2-bit fields
(`QSP/src/LibMarketSas/SasConfigurationFlags.h:35-41`): General Control · Advanced Funds
Transfer · Progressive · Legacy Bonus · Validation · Component Authentication · Information.
A category cannot be on two channels — the field holds one value — and libsas keeps a separate
exception buffer per channel (`libsas.h:2263`, `SAS_CHANNEL_1..4`). So disjointness is
guaranteed by the machine's own configuration.

Two consequences:

1. **The category→channel map becomes part of provenance.** "Which host should have sent this
   poll" and "which host owns this exception" are only answerable if the map is known, and the
   map lives in the machine, not in us. It is recorded with the run, next to the link and the
   stimulus backend.
2. **A category pointed at a channel with no poll address is unreachable.** The audit menu
   refuses to assign one, but nothing stops a config arriving that way. Our software machine
   must refuse it with a reason rather than accept a poll nothing will answer.

## D24 — Audience narrowed: personal use, developers first

The four-audience premise (D3) is superseded. This is being built for the user's own use, with
other QSP/libsas developers as the plausible second reader. Customers, integrators and
regulators are no longer a design constraint.

Consequences, stated so they are not re-argued: density and hex win every tie; the **report mode
of D22 is demoted below everything the engineer uses daily**, because the audience it was grafted
in for is no longer primary; the self-contained HTML report (D13) survives on a different
justification — shipping a result to somebody else and keeping a record (see D26). The
recommendation against a simple/expert mode split stands and is now moot.

## D25 — Tickets: JSON only in SasWorkbench

The rendered ticket and its real barcode belong to `qpm_simulator`, which is where a human looks
at one. SasWorkbench holds **only the JSON** — machine-readable, parsable, searchable. This
narrows D12: the rendering half of it is the simulator's job, not the host's.

## D26 — Log export: a block-compressed container with a readable header

A session log is long and large — a 200 ms poll loop is ~10 records/second per channel, and the
casino simulation of D29 is two orders of magnitude worse. So a session exports as a single
container file:

* an **uncompressed header** — provenance, the category→channel map, catalogue version, clock
  base — so a tool can read what a run was worth without decompressing the body;
* the body as **independently-decompressible blocks** of the same JSON-lines records, so the file
  is seekable by time rather than only readable from the start;
* a footer index of block offsets with each block's first and last timestamp.

JSON lines stays the canonical record — the container is a framing and compression layer over it,
and `sasctl` can turn one back into plain text. Compression uses what ships in the framework, so
there is no native dependency on either platform.

## D27 — The wakeup bit is a display setting

Today every symbol renders as flag-dot-byte (`1.80`, `0.1F`). Add a setting: **flag digit**, or
**colour only** — the byte alone, with a wakeup symbol drawn in the wakeup colour. Colour-only is
the user's preference and the default.

This does not weaken the refusals in D2 or the principle behind them: a transport that cannot
carry the bit is still refused, and a received symbol carrying it still fails the poll. What
changes is only how a symbol is drawn.

## D28 — Four timestamp modes

Selectable per view and *not* baked into the record, so the raw log is unaffected:

| Mode | Zero is | What it shows |
|---|---|---|
| **since test start** | the start of the test (today's behaviour) | where in the test something happened |
| **since previous poll** | the previous host poll on that channel | the poll period — 200 ms, and every stall in it |
| **since previous message** | the previous record of any kind | turnaround latency — the 30.7 ms a reply took |
| **wall-clock time** | midnight | when it happened in the world, for matching a run against another tool's log, a machine's own log, or somebody's account of what they were doing |

The distinction between the middle two is the point: in a general-poll loop, *since previous
poll* reads ~200 ms while *since previous message* reads ~30 ms, and the two answer different
questions.

**The fourth was added on 2026-09-05 and is not yet built.** It needs no change to a record:
the session already carries a clock base (D26), so wall-clock is that base plus the elapsed
time a record already holds. Every other mode stays derived the same way, which is what keeps
the raw log the only thing that has to be right.

## D29 — Casino simulation, as the last milestone

100 software EGMs against one or two hosts, with the hosts partitioned by category per D23 — for
example Advanced Funds Transfer and Legacy Bonus on one, Validation and Information on the other.
Scheduled as M5, after breadth. See `PLAN.md`.

---

# Decisions 30–31

Taken with the user on 2026-09-05, at the end of M2. Same standing as the rest.

## D30 — Nothing in QSP, libsas or QSP_DemoOpenGL is edited, branch or not

Taken 2026-09-05. Tightens D15, which had allowed changes to `QSP_DemoOpenGL` on a branch in a
dev-only preset. The user's words: *"I don't want to change QSP / libsas / QSP_DemoOpenGL repos.
The CMakeUserPreset is ok because it is gitignored and it remains only on my PC."*

So the only file this project may add to any of those three repositories is
`QSP_DemoOpenGL/CMakeUserPresets.json`, and only because it is gitignored and never leaves the
user's machine. Everything a build needs is injected from *this* repository through CMake's own
`CMAKE_PROJECT_<name>_INCLUDE` hooks, which is what M0 established.

The branch `qsp-new-callbacks` that M0 created in `QSP_DemoOpenGL` carries no tracked change and
exists only to hold that ignored file's working directory. Nothing further goes near it.

**If something cannot be done within that, stop and say so.** A change made to their source
because it was the easy path is the one outcome this decision exists to prevent.

## D31 — A milestone for whole-EGM conformance, driven by the user's test-case documents

Taken 2026-09-05. The user holds several PDFs enumerating the test cases that validate an entire
gaming machine across every SAS feature, and will supply them when that milestone starts — not
before, so the scripts are written against the real documents rather than against a guess at
them. Scheduled as M7 in `PLAN.md`, after the breadth work that gives it a full catalogue.

The same rule as D4 applies to those documents: their content informs what we test, and every
word we ship is our own.

**The documents arrived on 2026-09-13.** Eleven GSA EGM test-script PDFs, in
`SAS test script/` — an intro plus required accounting, advanced accounting, ticketing, real-time
events, progressives, bonusing, cashless, tournament, authentication, and miscellaneous/legacy.
The directory is gitignored (`.gitignore:23`), so they never leave the user's machine, and D4's
rule stands: they decide what we test and not one word of what we ship.

---

# Decisions 32–33

Taken with the user on 2026-09-05, before starting M3.

## D32 — QPM2 is substituted, never edited

The ticket-in path needs the HAL peripheral manager to raise a ticket-pending event, and that code
lives in the QPM2 checkout. QPM2 is not one of the three repositories D30 protects, but it was not
on D15's approved list either, so it was put to the user.

**Chosen: substitute, do not edit.** QPM2's tracked files stay as they are; its HAL source is
swapped for our own copy at configure time, the same injection M0 established for the serial
transport and for libsas's platform check, with the wiring in the gitignored preset. The cost is a
maintenance seam if QPM2 moves, and that is accepted in exchange for a fourth repository staying
untouched.

Rejected: editing QPM2 on a local checkout reached through `FETCHCONTENT_SOURCE_DIR_QPM`, which
QSP's own CMake documents at `QSP/CMakeLists.txt:70`. Cleaner code, but it would make QPM2 a fourth
repository this project changes.

## D33 — A flaky suite is fixed before more is built on it

The intermittent failure that only appeared when all four test projects ran at once was chased
before M3 rather than alongside it or after it. The reasoning the user endorsed: a suite that fails
one run in ten cannot tell you whether the next milestone works, and the bug gets harder to isolate
as the suite grows.

It was worth doing. What looked like flakiness was two real defects, both of which silently
returned one message's answer under another message's name — see
`docs/one-behind.md`.

---

# Decisions 34–35

Taken with the user on 2026-09-05, after M2 was merged.

## D34 — An extended log, asked for before a run or produced from one afterwards

The user wants a log that explains itself: **a description of each test case before it runs, and a
description of every single SAS packet with its complete meaning.** Available from the command line
and from the window, and — this is the load-bearing half — obtainable **both** by asking for it
before a run *and* by producing it from a log that already exists.

The second half decides the design. It means the extended log is a **rendering of the recorded
session, never a second thing captured alongside it**. Nothing may go into it that was not already
in the record, which is the same rule D28 applied to timestamp modes and for the same reason: the
raw log stays the only artefact that has to be right, and anything derived from it can be
regenerated, corrected and re-rendered without re-running anything.

Consequences, so they are not re-argued:

* **Every packet gets a line, and a packet this build cannot decode says so.** "Complete meaning"
  is a promise, and the honest form of it on an undecoded reply is `known but not decoded` with the
  command named — never a plausible guess. This is principle 1 and 2 restated for prose.
* **The extended log is therefore also a coverage measurement.** How many packets were explained in
  full, how many only named, is the same number M4 is trying to move. A run that could explain a
  third of its packets must not read like one that explained all of them.
* **Breadth is the ceiling.** The full long-poll catalogue and its layouts are M4, so an extended
  log written today explains what this build decodes and names the rest. That is worth having now
  rather than after M4, because it is what shows where M4 should go first.
* **The test-case description comes from the script**, which already carries a name, tags,
  `requires` and per-step locations. A case whose script says nothing about itself produces a thin
  description, which is a prompt to write a better script rather than a reason to invent prose.

## D35 — The simulator's ticket surface: a popup, and JSON in both directions by hand

Elaborates D12, which already approved the printer panel, the wallet and JSON in both directions.
What the user added is the shape of it in `qpm_simulator`, which is an ImGui application:

* a **popup showing printed tickets**, so a person can see what came out;
* **buttons to export a ticket as JSON**, alongside the per-ticket JSON tests already assert on;
* **buttons to load a JSON ticket**, which is how ticket insertion gets simulated by hand.

The by-hand path and the automated one are the same path: a ticket a person loads through a button
and a ticket a script injects are the same JSON going to the same place. If they diverge, the thing
a person demonstrates is not the thing the suite tests.

`qpm_simulator` is approved for change (D15) and needs no version bump in the platform repository —
see the note in `PLAN.md` M3.

---

# Decision 36

Taken with the user on 2026-09-05, after three unit defects were found by measuring the machine.

## D36 — A script says what accounting denomination the machine is in, and can also change it

Two forms, both wanted, **both after the ticket path of M3**.

* **The declaration** — a case states the configuration it assumes, before the link opens. Against
  the software machine it builds one in that configuration and runs. Against a real machine it
  configures nothing: it reads the identification reply and either matches, and runs, or skips
  naming what the machine is actually in force at. Never a silent reconfigure and never a failure
  for an environmental reason, which is D19 applied to a unit rather than a capability.
* **The setter** — a step that changes the denomination during a run.

**The recommendation was the declaration alone**, on the grounds that section 16 makes the unit a
property of the machine that a memory clear establishes, so no real machine can honour a change
mid-session and a script written against one could not be run against the machine it describes.
The user asked for both anyway. The way that is kept honest: the setter is a stimulus like any
other, so on a machine that cannot be reconfigured it refuses with a reason and the case skips —
the same treatment a door nobody has wired gets.

**What it is worth, counted rather than claimed:** the declaration unblocks 7 of the 44 cases in
`tests/validation/accounting-denomination/` — the pure wire-semantics ones — and only against the
software peer, because six of them need games played, which a real machine refuses. The other 37
need a memory clear, a power cycle, the configuration file, the audit pages, a handpay, tickets,
transfers, progressives, a bonus, or a second game. The suite does not go green because of this;
it starts measuring something.

**Built 2026-09-07, and the number measured was 6, not 7.** AD-37 needs a wager of twenty-five
centavos, which needs a game credit worth twenty-five centavos, and nothing here can say what a
game credit is worth — so it skips naming that rather than asserting a truncation it never caused.
The declaration turned out to need a construct of its own rather than a `requires`, because a
requirement is checked after the environment is built and cannot configure anything; the reasoning
and the verification of which reply carries the value are in `docs/M3-accounting-denomination.md`.

---

# Decisions 37–38

Taken with the user on 2026-09-05, before the ticket work started.

## D37 — A ticket has one model and two encodings

Revises D12 and D25, which said JSON. A ticket is written and read as **either** a key/value text
file in the shape of a classic `.ini` **or** JSON: the encoding is chosen by file extension when
reading and by a flag when writing, and either is accepted anywhere a ticket is accepted.

The user's reason for wanting the text form is the one that matters: a person hand-authoring the
negative cases — expired, wrong check digits, foreign machine, already redeemed — edits a
key/value list far more comfortably than JSON, and those cases are the whole point of importing a
ticket at all.

What this must not become is two ticket models. **There is one model**; the encodings are two
renderings of it, and a ticket converted from one to the other and back is the same ticket. That
is D35's rule — the by-hand path and the scripted path are the same path — restated for a format
choice, and it is the thing to check first if the two ever start to drift.

## D38 — `qpm_simulator` changes go on a branch

The simulator is approved for change (D15), but its work belongs on a branch rather than on
`main`, which is where it sits today. The printer panel, the wallet, the ticket popup and the
mask-zeroing race all land there.


---

# Decisions 39–41

Taken with the user on 2026-09-21, after the M7 documents were read and before any case was
written. Same standing as the rest.

## D39 — M7 lives in `tests/validation/egm-test-scripts-v1.00/`, with the revision in the path

The eleven documents are revision **1.00** of the EGM test scripts. The three cases already in
`tests/conformance/10.1/` come from the GSA toolkit corpus, which is the **same suite at revision
1.2**, and the section numbers moved between the two: revision 1.2's 10.1 *Standard Cashout
Validation* is 10.2 in these documents, and its 10.2 *Secure Enhanced Validation* is 10.5 here. So
"we pass 10.1" names two different things unless the tree says which revision it means.

M7 therefore goes in D18's third tree, one directory per chapter, numbered as the documents number
them (3 to 23, continuous across the suite). Two reasons rather than one: the revision becomes part
of the path, and the regression and conformance trees keep their no-skip property — a
document-driven suite is mostly cases no build can run yet, and putting one in a tree whose
invariant is "no skips at all" would either destroy that invariant or trim the suite to the
convenient cases.

`tests/conformance/10.1/` is left exactly as it is.

## D40 — A case that compares the wire against the machine's own display is written as two halves

142 of the 612 cases assert a value read over the wire **and** that it matches the same meter on
the machine's own display; 56 of those are chapter 6 alone. The wire half is assertable and the
comparison never is — nothing here can read a machine's screen.

Each becomes a wire assertion that runs, plus a note carrying the comparison for a person. The
alternative shapes were both rejected: one case each would make the largest chapter contribute 56
skips and almost nothing that runs, and dropping the comparison would silently stop covering
something the document requires.

## D41 — CI keeps running the hundred-machine case

`ci.yml` is left unfiltered, so D29's case runs on every push on both platforms at about 76 seconds
a run. It is the only thing exercising both axes at scale, and that is cheap against a floor defect
found by a person instead. The `@casino` tag stays in place for running the tree without it by hand.

# Decisions 42–44

Taken with the user on 2026-09-22, after chapters 3, 4 and 11 were written and the two findings in
`docs/M7-reading-the-documents.md` §10 were put to them. Same standing as the rest.

## D42 — The chirp is not built on its own; it goes with the scripted line

Six cases name `host.chirp` — 3.4, 3.5.1, 3.6, 3.23, 11.8 and 11.13 — but it would unblock only
**two of them alone**, 3.4 and 11.8. The rest also need `host.poll_sequence`, a power cycle or a
service button.

Chirping needs two halves that are useless apart: the software machine must send its address every
200 ms after five seconds of silence, and the host must watch a line while no poll is outstanding.
The second is a new mode in the poll engine, and it is the same mode `host.poll_sequence` needs —
one where the script drives the line instead of the timer. `host.poll_sequence` blocks twenty
cases. So the chirp waits and is built with it, rather than building the idle-line watcher twice
for two cases.

Nothing is wrong meanwhile: the six cases skip naming the capability, which is the honest state.

## D43 — A long poll answered with an event is kept, named, and not retried

The machine library answers a **long poll** with a real-time event, rather than with the long
poll's own reply, when reporting is on and an exception is queued (`libsas_threads.cpp:3333`; the
branch that dispatches long polls by command is never reached). The documents require the same
thing — chapter 11's loops carry no general polls at all.

This host discarded such a message: it cannot answer the command, so it was counted as answering
some earlier poll, and the long poll was reported `Silent`. **The event was lost**, which is the
silent degradation this project exists to refuse, and it happens against a real machine in a
configuration a customer would use.

So the host now admits the event format in a long poll's window, reports the exception to the
stream it would have reached on a general poll, and gives the long poll a new outcome of its own —
`AnsweredWithEvent`, which is deliberately **not** a success, because the caller did not get the
reply it asked for. A script step that sent the poll fails, with a reason telling its author to
drain the queue first.

**The host does not ask again**, and that is the decision rather than an omission. Repeating the
same poll is an implied NACK (section 3.20): the machine would read it as the event having been
missed and send the same event again, and a third time is final. A host that retried would spin on
one event and never reach its reply while anything was queued. Draining correctly means
interleaving a *different* poll to acknowledge before re-asking, which is the poll engine's control
flow and is not bought here. It is worth building when something actually needs long polls to work
with reporting on.

One bounded exception, because it already exists and already retried: `sasctl events` insists on
its auto-rebet command up to three times while the machine stays silent, and that retry now covers
an event-preempted poll too. It is a local insistence on one command, not a general policy.

## D44 — The software machine mirrors the library, after the host can hear it

The fixture answered every long poll with the command's own reply and only ever put an event on a
general poll, so nothing here could reproduce D43 at all. It now does what the library does: while
reporting is on and an exception is queued for that link, a long poll addressed to the machine is
answered with the event and the command is not dispatched.

The gate sits **after the check sequence and address tests and before the channel-category gate**,
so a malformed poll is still refused and an event never leaves on a channel that owns no category.

Order matters and is part of the decision: the host change lands first. A fixture that sent events
on long polls before the host could accept one would make our own suite lose them.

The cost is named rather than discovered later: **any `host send lp` issued while an exception is
still queued now gets an event instead of a reply, and the step fails.** Two cases were relying on
the preceding assertions having happened to drain the queue — 11.1 and 11.14, both disabling
reporting after a stimulus — and each gains a `host forget exceptions` first, which waits for three
poll intervals of quiet before clearing and so drains as well as forgets. 11.7 sends long polls
with reporting on and no stimulus before them, and carries a comment saying that is why it is safe.

---

# Decision 45

Taken with the user on 2026-11-20, when the AFT validation cases were the largest block left
skipping on `machine.aft` and `decoder.lp72` alone. Same standing as the rest.

## D45 — The software machine takes funds transfers, in two phases, mirrored from the library

The software machine gains AFT the way D44 says it gains anything: **the protocol half is the
machine library's**, check for check and in the library's order (`lp72_TransferFunds`,
`lp74_LockAndStatusRequest`, `internalUpdateAftTransfer`, the lock and $69 timers), so a refusal
carries the code the library would choose. Where the library hands a decision to the application —
which transfers are available, whether a lock is granted, when the money actually moves — the
machine answers the way the platform above the library does, reduced to the simplest version this
fixture can hold, and says so where it does.

**Phase 1, built now:** long poll $72 decoded on the host; the lock and its timeout, its loss to a
door, a hand pay, a disable or a loop break; game locked; in-house transfers to the machine and to
the host, pending until the platform finishes them and final once read back; the 127-entry history
and its wrap; the two-byte refusals; transfer complete and its reissue; cash-out to the host,
switched on and off by the host. AFT is **on by default**, as on the machine it mirrors, and is
read off the machine's own enabled-features reply rather than claimed by the stimulus backend, so
`machine.aft` means the same thing against both peers. A case states the asset number its transfers
are addressed to with a setter step, which only a machine this build made can honour.

**Phase 2, named and deferred:** registration ($73) and debit transfers, AFT bonusing
(`machine.aft_bonus`), transaction receipts (`machine.aft_receipts`), transfers to a ticket, and
restricted and non-restricted credits with their pools. Until then those cases keep skipping on the
capability they name, and a transfer carrying such an amount fails at the platform rather than
being half modelled. The design note is `docs/M7-software-machine-aft.md`.

**Phase 2, built 2026-11-20:** $73 registration on both peers, debit transfers as far as the
library takes them (the platform then fails them, as QSP performs none), bonus awards of both kinds,
non-restricted credit, and transaction receipts. Restricted credit and transfers to a ticket stay
out. Three choices were taken here:

* `machine.aft_bonus` and `machine.aft_receipts` are **asked of the machine**, off bit 1 of the first
  enabled-features byte and bit 0 of the $74 AFT status, like `machine.aft`.
* The attendant's side is two **new stimulus steps**, `egm aft register` (one action: request from
  not registered, acknowledge from pending, as QSP has it) and `egm set aft bonus on|off`, needing
  `stimulus.aft_registration` and `stimulus.aft_bonus`. The software and manual backends offer both;
  `machine.aft_operator_registration` is claimed by the software backend, whose machine has the menu.
* Non-restricted credit **is modelled** after all, because half the bonus cases carry it: held in
  the credit meter, played first, cashed out on the cashable ticket. Restricted credit still fails
  at the platform with $9F.

**Addendum, 2026-11-21, taken with the user:** restricted credit is now modelled, together with a
credit limit and transfers turned off at the machine, so the chapter 16 cases that skipped on them
run. Restricted credit is held in the credit meter with the pool and expiration the library keeps
(one pool at a time, refused `$8F` otherwise; expirations combined as `calculateExpirationDate` does;
both cleared when the balance empties), wagered before non-restricted and cashable credit, taken by
transfers to the host, and never paid on a cash-out ticket, so it stays on the machine as on a
platform with restricted tickets off. `machine.transfer_limit` is read off the `$74` status, like
`machine.aft_receipts`; `machine.credit_limit`, `machine.restricted_credits` and
`machine.configuration_options` are claimed by the software backend, as no poll reports them, and
the setter steps `egm set credit limit` and `egm set aft on|off` need `stimulus.credit_limit` and
`stimulus.aft_configuration`.

---

# Decision 46

Taken on 2026-11-23 while building the peripherals group of M8, without the user (the brief says to
take the reasonable choice and list it). Open to review like any other.

## D46 — The software machine has only the peripherals the platform has

The protocol half of a peripheral fault is the library's exception queue and nothing more; every
code is the platform's choice, written from its condition table. So the software machine gains
exactly what the platform reports: the **service button** (change lamp $71/$72), **note acceptor
faults** (stacker full $27, jam $28, malfunction $29, a returned note $2B, counterfeit $2C) and
**printer faults** (disconnected or failed $75/$76, jam $78, paper out $61, paper low $74), each
raised once while active and cleared by `egm <device> fault cleared` or `egm settle`. Every fault but
low paper takes the machine out of play, as the platform's lockup does.

It does **not** gain a coin acceptor, a hopper or reels, because the platform has none: it never
writes $21 to $25, $2D, $79, $80, $81 or $40 to $46, and its hopper meters are marked unsupported.
`stimulus.coin`, `stimulus.hopper`, `stimulus.reel`, `machine.partial_pay` and
`machine.external_coin_acceptor` therefore stay absent, and the cases needing them keep skipping on a
software run; a person at a cabinet that has them can still run them. A note pulled back ($2A) and a
worn ribbon ($77) have no platform report either, so each is a capability of its own
(`stimulus.note_fault.reversed`, `stimulus.printer_fault.ribbon`) that only the manual backend offers.
The design note is `docs/M7-software-machine-peripherals.md`.

---

# Decision 47

Taken on 2026-11-23 while building the validation styles group of M8, without the user (the brief
says to take the reasonable choice and list it). Open to review like any other.

## D47 — A case declares the validation style, and the software machine performs it

The style a machine performs is set at the machine and no poll can change it, so a case chooses it
the way it chooses an accounting denomination: **`machine validation standard|secure_enhanced|system`
is a declaration** (D36). The software machine is built performing it; any other machine is asked
`egm.validation` over the link and the case skips when it performs another. Every chapter 10 case
that required an enhanced style now declares it. There is **no `none`**: a machine with no host
validation reports the standard style on the wire, so 10.5.1 became the secure enhanced machine
with no identifier, which is what raises $3F.

The machine's half follows D44: the library's buffer, identifier, exchange and timers check for
check, and QSP's choices of when to print, when to pay by hand and what the machine can do. Three
smaller choices go with it:

* `host.validation_sequence` is baseline, true of this build once $58 has a layout.
* `machine.buffer_depth` (31) is claimed by the software backend, since no poll reports it.
* `machine.handpay_validation` and `machine.handpay_receipts` stay absent, because the platform
  validates no hand pay and prints no receipt (QSP `SasNetwork.cpp:617`).

The design note is `docs/M7-software-machine-validation-styles.md`.

---

# Decision 48

Taken on 2026-11-23 while building the life cycle group of M8, without the user (the brief says to
take the reasonable choice and list it). Open to review like any other.

## D48 — The software machine is power cycled, faulted and cleared as its platform is

A power cycle keeps exactly what the library's image keeps (`sNvramData`, libsas_db.h:855) and what
the platform keeps in its own memory, and starts everything else again: the link waits for a
synchronisation poll, real-time reporting is off, the transfer lock is gone. The power exceptions
are the platform's: $18 on every start whether or not anything noticed the power go, the conditions
it restores, then $17 (QSP SasNetwork.cpp:159, SasNetwork_Conditions.hpp:477). The machine is dead
to the line for half a second while off.

Three choices go with it:

* **A memory fault is the one the platform finds while running.** `egm memory fault cmos-lost`
  raises $32 with the link up and takes the machine out of play until a memory clear. Memory found
  invalid at power-up starts no link at all on the platform (QSP SasNetwork.cpp:309), so the host
  would hear nothing; that path is not modelled.
* **The faults the platform never reports have capabilities of their own**, so a case needing
  $31, $33, $34, $35, $37, $38 or $39 skips on the software machine rather than failing after a
  step that could not happen: `machine.memory_fault.cmos_recovered`, `.cmos_bad_device`, `.eeprom`,
  `.eprom_checksum` and `.partitioned_eprom`, offered by the manual backend only. A case stays one
  test: 4.15 to 4.19 need them all and skip whole.
* **A memory clear is `egm nvram clear`**, also reached by `egm memory fault clear`, and includes
  the attendant setting the poll address again; the platform would come back at address 1 with
  asset number 1, and the asset number does. It reports $3A, $7A and then $3C as the configuration
  completes (QSP SasNetwork.cpp:1219, SasNetwork_Conditions.hpp:63).

The design note is `docs/M7-software-machine-lifecycle.md`.

---

# Decision 49

Taken on 2026-11-23 while building the progressives group of M8, without the user (the brief says to
take the reasonable choice and list it). Open to review like any other.

## D49 — A case declares the progressive levels, and the host can keep them broadcast

The group is set in the operator's menu and which levels a host feeds is the game's, so no poll can
change either; a case chooses them the way it chooses a validation style (D47):
**`machine progressive sas group <g> levels <list>`** and **`machine progressive non_sas levels
<list>`** are declarations. The software machine is built with them. Any other machine is asked its
group off $1F (`machine.sas_progressives`, valued with the group) and the case skips on another. A
non-SAS level reports group zero on every poll, so nothing can tell one is there: only the software
machine built with one claims `machine.progressives`, and on any other machine those cases skip.

The machine's half follows D44: the library's handlers check for check, and QSP's choices of when a
win is announced and paid and that a linked level not being broadcast locks the machine up. Three
smaller choices go with it:

* The software machine now answers $80 to $87 as the library does on every machine, zeros included,
  instead of keeping silent; only $8E and $8F stay without machinery.
* **`host broadcast lp 80|86 ... every <duration>`** keeps a broadcast going to 00 in the background
  until `host broadcast stop`, never during a sequence; `host.progressive_broadcast` is baseline.
* **`egm progressive win level=<n>[,<n>...]`** plays one game that wins those levels, paid by the
  machine to its credit meter; a stand-alone level is worth $10.00. A win paid by hand waits for the
  jackpot limit of group 5.

The design note is `docs/M7-software-machine-progressives.md`.

---

# Decision 50

Taken on 2026-11-23 while building the game-in-progress group of M8, without the user (the brief
says to take the reasonable choice and list it). Open to review like any other.

## D50 — A case holds a game or a cash out open, and sets one jackpot limit

No poll reports a jackpot limit, and no poll can open a game, so all three are the case's to work at
the machine, the way a credit limit is (D45): **`egm set jackpot limit <cents>`**,
**`egm game start [bet=n]` / `egm game end [win=n]`** and **`egm press collect lasting <duration>`**,
needing `stimulus.jackpot_limit`, `stimulus.game_in_progress` and `stimulus.cashout_in_progress`,
which the software and manual backends offer. `machine.jackpot_limit` is claimed by the software
backend. What each does is the platform's, in its simplest form (D44):

* **One limit for every award.** The platform keeps a game win limit and a progressive win limit;
  this machine keeps one figure and weighs a game win, a progressive (by the round's progressive
  total), a legacy bonus and an AFT coin-out bonus against it, strictly greater, as the market does.
  Zero is no limit and the default, so nothing written before changes.
* **One hand pay at a time**, where the library queues them: a progressive won with a jackpot joins
  it, and a legacy bonus over the limit waits until the reset before it has been reported.
* **Poker is left out.** The platform hands the library a dealt hand and held cards but raises no
  draw request, so two of the five poker cases could never run, and the other three would need a
  second game model; `machine.poker` stays absent.

The design note is `docs/M7-software-machine-game-in-progress.md`.

---

# Decision 51

Taken on 2026-11-24 while building the authentication group of M8, without the user (the brief says
to take the reasonable choice and list it). Open to review like any other.

## D51 — A case declares component authentication, which the platform does not perform

QSP starts the library with component authentication off and registers no component
(SasNetwork.cpp:302, "TODO modify when LibQpm supports component authentication"), so a machine
like the platform answers $6E with silence and every chapter 19 case after 19.1 would skip. D46
would leave it there. It is built anyway because the protocol half is wholly the library's, the
library is under test, and its $6E handler differs from the document in three ways that only a
running machine shows. The default stays the platform's:

* **`machine component authentication on` is a declaration** (D47). The software machine is built
  taking part; any other machine is asked off bit 4 of its second enabled-features byte
  (`machine.component_authentication`) and the case skips when it is clear. There is no `off`, as
  taking no part is the default.
* **The application half is this build's own, said as such:** two components, CRC-16 only (seeded,
  from the offset), five seconds to finish, and `egm component change` installing a third with $8E,
  the one change the library supports (`stimulus.component_change`, software and manual backends).
* `machine.multiple_components` is read off the count in the $6E list reply.

The design note is `docs/M7-software-machine-authentication.md`.

---

# Decision 52

Taken on 2026-11-24 while building the multiplied jackpot group of M8. The user decided that multiplied
jackpots are on only when a case declares them, as D51 does for component authentication; the
application half below is this build's own choice, open to review like any other.

## D52 — A case declares multiplied jackpots, which the platform does not perform

QSP starts the library without the jackpot multiplier bit (SasNetwork.cpp:286; the one place that sets it,
SasNetwork.cpp:491, fills a structure that never reaches the library) and drops $8B in its default branch
(SasNetwork_Callbacks.hpp:198), so a machine like the platform answers $8B with silence and every chapter 22
case after 22.1 would skip. It is built anyway because the protocol half is the library's: `lp8B` and
`SAS_LegacyBonusAward`. The default stays the platform's:

* **`machine multiplied jackpot on` is a declaration** (D47). The software machine is built multiplying and
  reports bit 0 of the first enabled-features byte while legacy bonusing is on; any other machine is asked
  off that bit (`machine.multiplied_jackpot`) and the case skips when it is clear. There is no `off`.
* **The application half is the simplest that the document describes:** a game multiplies by the setting in
  force when it started; only a paytable win inside the inclusive range, at the maximum bet when asked; the
  extra part (win times one less than the multiplier) is a machine-paid external bonus on the credit meter,
  on the legacy meter bit 7 chooses, reported by $7C with tax status 00, sharing one $7C with a bonus held
  for the game end; a win paid by hand takes the extra part into its hand pay (attendant-paid external
  bonus) and its $7C follows the $52; a lost link or a restart switches it off; a command it cannot apply
  is acknowledged and changes nothing.
* **Left out:** a progressive's part of a win is not multiplied, and the extra part is not weighed against
  the jackpot or credit limit, so 22.18.1 and 22.18.2 stay notes.

The design note is `docs/M7-software-machine-multiplied-jackpots.md`.

---

# Decision 53

Taken on 2026-11-24 while building the hand pay queue group of M8, without the user (the brief says to
take the reasonable choice and list it). Open to review like any other.

## D53 — A case declares hand pay validation, which the platform does not perform

QSP clears hand pay validation and hand pay receipts in the library's status word (SasNetwork.cpp:617), so
long poll 7B never reports bit 2 and no hand pay is ever validated; D47 left `machine.handpay_validation`
absent on that ground. Of the two patterns, this takes **"on only when declared"** (D51, D52) rather than an
honest skip (D46), because the protocol half is wholly the library's and already mirrored for tickets: the
validation buffer, the no-receipt types $40 and $60, the meters, and the choice of $3E over $3D on a secure
enhanced machine (`libsas_internal.cpp:5928`, `:690`). D46 keeps out what the library cannot do without a
device the platform lacks; here only the platform's call is missing. The default stays the platform's:

* **`machine handpay validation on` is a declaration** (D47). No poll can say whether a machine validates
  hand pays without the host first switching it on, so only the software machine built that way claims
  `machine.handpay_validation`, as only it claims a non-SAS progressive (D49).
* **The application half is the simplest the document describes:** the attendant's reset, while the host has
  bit 2 on, starts and completes a cash-out of the hand pay with the no-receipt type. Secure enhanced only; a
  system-validated hand pay would wait for $58, and no case asks for one. Receipts stay absent.

Two smaller choices go with it:

* **D50's one hand pay at a time is lifted.** The platform still locks up for one hand pay at a time, and the
  library still refuses a second until the first is reset, but a hand pay reset before the host read it stays
  in the library's queue of sixteen, on each channel, and the next one queues behind it.
* **Legacy reporting is a declaration, `machine handpay legacy`.** QSP offers it, chosen only straight after a
  memory reset (SasNetwork.cpp:605, :862), and no poll reports it; the software machine built that way claims
  `machine.legacy_handpay`, and one built without claims `machine.handpay_queue` and its size.

The design note is `docs/M7-software-machine-handpay-queue.md`.

---

# Decision 54

Taken on 2026-11-24 while building the multiple games group of M8, without the user (the brief says to take the
reasonable choice and list it). Open to review like any other.

## D54 — A case declares the machine's games, and the platform's choices fill them in

QSP supports several games and denominations: it configures each game in the library (SasNetwork.cpp:1243), sets
the multi-denomination extensions bit on every machine (SasNetwork.cpp:304), and its demo holds four games at
different denominations. So this is neither "on only when declared" because the platform lacks it (D51 to D53) nor
an honest skip (D46): it is the platform's behaviour, and only **which** games a machine holds is the case's to say.

* **`machine games <cents>c [<cents>c ...]` is a declaration** (D36), one denomination per game. The software machine
  is built with those games and, unless the case declares an accounting denomination, counts in cents as the
  platform does. Any other machine is asked off $51 and $B2 (`machine.multiple_games`,
  `machine.multiple_denominations`). Without the declaration the software machine keeps its single game and the
  extensions bit clear, so nothing written before changes.
* **The player and the operator are stimuli**: `egm select game <n>`, `egm select denomination <c>c` and
  `egm set game <n> on|off`, offered by the software and manual backends.
* **The platform never reports $89 or $1F**, so `stimulus.bet_button` and `machine.player_input_state` are offered
  by the manual backend only, and 11.4, 11.21, 15.16 and 15.23 skip on a software run (D46).
* **Left out:** a game at more than one denomination, total won credits per game, and the once-a-second limit on
  reporting a selection.

The design note is `docs/M7-software-machine-multiple-games.md`.

---

# Decision 55

Taken on 2026-11-25 while building the host timing and line group of M8, without the user (the brief says to take
the reasonable choice and list it). Open to review like any other.

## D55 — The host does on the line only what it can honestly do there, and the machine's address is the operator's

* **Three host capabilities are the build's**, in the baseline: `host.chirp` (listening to the quiet line of a
  sequence), `host.poll_without_wakeup` (every transport carries the bit per symbol, so leaving it off is
  deliberate, never a degradation under D2) and `host.timing` (how long until an exception comes again, read off
  the host's own clock with a tolerance of a quarter period or two poll intervals).
* **Two stay absent and their cases skip:** `host.collision`, because a transport hands a reply over whole, and the
  new `host.byte_timing`, because no transport times each symbol; 3.12 names it rather than passing a gap check
  that cannot fail.
* **A chirp in a reply window is passed over, not read as the reply**, and recorded as a chirp.
* **The poll address is a stimulus, `egm set address <aa>`**, as QSP's network menu has it: a changed address
  restarts the link there and reports $3C, and address 00 starts no link at all. `host poll address <aa>` points
  the host at the new address. A data poll to 00 is now silent on the software machine, as every library handler
  but $7F, $80 and $86 makes it.

The design note is `docs/M7-software-machine-host-timing.md`.
