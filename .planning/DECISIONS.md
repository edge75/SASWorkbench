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

