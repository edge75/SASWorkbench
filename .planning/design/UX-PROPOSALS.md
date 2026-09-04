# SasWorkbench — UX proposals (M1.5 design contract, UX half)

Written against the app as it stands on 2026-09-04: `MainWindow.axaml` (222 lines),
`WorkbenchViewModel.cs` (473 lines), `App.axaml` (4 style rules), `docs/M1-thin-vertical-slice.md`,
`.planning/DECISIONS.md`, `.planning/PLAN.md` (with M1.5 as inserted).

Everything proposed here is buildable with Avalonia 11 primitives. Where a proposal needs data the
ViewModel does not have today, it is called out under **New data required** and traced to where it
would come from.

---

## 0. What the current window actually is, in its own source

Four observations the proposals are all reacting to. Each is quoted, not asserted.

**1. Provenance for an interactive session and provenance for a run are shown in two different
places, and the run's is the quieter of the two.** Connecting writes to row 1:

```csharp
Provenance = caps.Caveat is null
    ? $"{_session.Description} · {caps.Fidelity.ToString().ToLowerInvariant()}"
    : $"{_session.Description} · {caps.Fidelity...} · {caps.Caveat}";
```

rendered as `Classes="label"` — `FontSize 11`, `Opacity 0.65`. But finishing a *run* writes to the
bottom status line instead:

```csharp
Status = string.Join("; ", report.Provenance.Select(p => p.ToString()));
```

rendered at `Opacity="0.7" FontSize="11"` in row 3. So the single most load-bearing fact in the
product — principle 4 — has two homes, both of them the smallest text on screen, and the run's is
the one furthest from the verdict it qualifies.

**2. Passing assertions are invisible.** The run view keeps only failures:

```csharp
foreach (StepResult step in test.Steps.Where(s => s.Verdict != Verdict.Pass))
```

A green test collapses to one line. You cannot see in the window that
`expect field base_percentage of lp 1F >= 7500` got `9210` — the evidence table in
`docs/M1-thin-vertical-slice.md` came from the CLI, not from here.

**3. The bytes behind a run are collected and then thrown away by the GUI.** `TestOutcome.Wire` is
`IReadOnlyList<WireRecord>` — *"every message that crossed the wire, so a failure can be read back
byte by byte"* — and `RunScriptAsync` flattens each test into `StepLine(Verdict, What, Where,
Detail)`, four strings. The bytes exist in Core and never reach the screen.

**4. A run tears down the interactive session.** By design:

```csharp
// A script run opens its own link, so the interactive session is closed first rather than
// having two hosts polling one machine and each seeing half the exceptions.
bool wasConnected = IsConnected;
Disconnect();
```

This is a protocol constraint, not a UI accident, and it is the hardest single fact for D5 (GUI
session recording) to live with. Every proposal has to answer it.

Two further facts constrain what can honestly be offered from the window today:

* The GUI hard-codes the run environment — `Stimulus = StimulusBackend.Software`,
  `Environment = SoftwareMachineEnvironment.For()` — and never sets `ScriptRunnerOptions.AskPerson`.
  Since `DefaultEnvironment.Create` only builds a `ManualStimulusBackend`
  `when options.AskPerson is not null`, **a `manual`-stimulus test cannot be run from the window at
  all today.** It is not that the prompt is ugly; there is no prompt.
* `SaveReportAsync` is a stub that says so out loud:
  `Status = "reports are written by the command line: sasctl run <path> --report html"`.
  The window cannot produce the D13 artefact.

---

## 1. Use cases

Four actors, per D3:

| Code | Actor |
|---|---|
| **PE** | The user — senior protocol engineer at Quixant. Reads hex, verifies claims. |
| **DEV** | Other QSP/libsas developers, debugging why their firmware answered wrong. |
| **QA** | QA engineers. Run suites, triage red, not protocol experts. |
| **CUS** | Customers and integrators. Read reports; may run a suite they did not write. |

Status key: **now** = works in the shipped window · **M2/M3/M4** = the milestone that supplies it ·
**unsched** = justified by the domain, on no milestone, listed because a design that forecloses it
is a worse design.

### A. Bring a link up and look at it

| ID | Actor | Trigger | Goal | Success condition | Status |
|---|---|---|---|---|---|
| UC-01 | PE | Machine booted, laptop beside it | Open a link and confirm the machine is alive | Link reads `up`, sync + general polls on the wire, counters climb | now |
| UC-02 | PE/DEV | A colleague is pressing buttons on the machine | Watch the wire while somebody else drives | Every TX/RX visible with its wakeup flag and note, in order, without touching anything | now |
| UC-03 | PE | Suspicion about one command | Send one long poll by hand and read the decoded reply | Field-by-field list with units and any constraint violation named | now |
| UC-04 | PE | The command is catalogued but has no layout | Explore an undecoded long poll by hand | Raw hex shown, labelled *known but undecoded*, with the same reason a test would be skipped for | now |
| UC-05 | PE/QA | Before or after doing something to the machine | Read every meter this build can read | 17 named meters with source command, value, unit; the other 179 accounted for, not hidden | now |
| UC-06 | PE/DEV | Machine queues an event | See exceptions arrive with our own plain-language meaning | Code, name, meaning, time since link start; nothing swallowed | now |
| UC-07 | PE | The machine says nothing | Tell apart wrong address / no peer / CRC reject / genuine silence | Silence distinguishable from a bad reply; the last TX visible next to the gap | partial — silences counted, not explained |
| UC-08 | PE/QA | Link hangs mid-run | Recover without losing what has been captured | Link state goes down visibly; captured wire and results survive; reconnect does not clear evidence | unsched |
| UC-09 | PE | Sanity-checking a new transport | Prove the wakeup bit is on the address byte and nothing else | `1.80` / `0.1F` rendering per symbol, both directions | now |
| UC-10 | PE/DEV | "Can this build even decode that?" | Browse the catalogue: 124 long polls, 21 layouts, 122 exceptions, 196 meters | Every entry states decoded / known-but-undecoded; searchable | M4 — data exists now, no view |
| UC-11 | PE | A serial adapter has been plugged in | Be told the serial link is unverified before trusting a green | Refusal on Linux; on Windows an `unverified` stamp that survives into every result | now, but weakly placed |

### B. Author a test

| ID | Actor | Trigger | Goal | Success condition | Status |
|---|---|---|---|---|---|
| UC-12 | PE | A new behaviour needs covering | Write a script from scratch and run it | No build step; a parse error names file, line and what was expected there | now, via an external editor |
| UC-13 | PE | Just poked at the machine and it was interesting | Turn what was just done into a script | The emitted script re-runs and reaches the same verdict | **M4** (D5) |
| UC-14 | PE | Looking at a field that should always hold | Promote a field reading into an assertion | `expect field base_percentage of lp 1F >= 7500` appears in the script, with the observed value as the reference | M4 |
| UC-15 | QA | The test needs a note acceptor we do not always have | Add a `requires` gate so it skips honestly elsewhere | An unmet requirement produces `SKIP` with the reason — never a fail, never a pass | now in the language; no UI |
| UC-16 | PE | A misspelled meter name | Find out before a link is opened | Refused at parse time, naming the line | now |
| UC-17 | CUS | Wants a variant of a shipped test | Copy a conformance test, change one value, run it | The copy runs; provenance still says what it ran over | now |

### C. Run

| ID | Actor | Trigger | Goal | Success condition | Status |
|---|---|---|---|---|---|
| UC-18 | QA | Nightly / pre-release | Run a whole suite unattended and walk away | One verdict, one exit code, one report; exit 3 if nothing was tested | now via CLI; the window has no report |
| UC-19 | DEV | Fixing firmware | Re-run one test repeatedly, fast | Single-test re-run; the previous result stays on screen for comparison | unsched |
| UC-20 | PE | D7 | Run the same script against both peers and diff the verdicts | Two runs, one diff; a disagreement is presented as a finding, not as one red run | unsched — done by hand today |
| UC-21 | QA | A `manual` stimulus step is reached | A human is prompted mid-run and the run continues | The prompt states exactly what to do, the poll loop keeps running, and an unanswered prompt becomes a SKIP with a reason rather than a hang | **M2** — and see §5: the prompt has no deadline |
| UC-22 | QA | Wrong suite started | Abort a run in progress | The run stops; partial results and their provenance are kept and labelled partial | unsched — `RunScriptAsync` takes no cancellation token |
| UC-23 | CUS | Evaluating the platform | Run a conformance suite they did not write | Can select and run a suite, understand SKIPs without protocol knowledge, and export the report | partly now; **report export missing from the window** |
| UC-24 | QA | Suite mixes destructive and safe tests | Run only the safe ones | Filter by tag: `@destructive`, `@slow`, `@channel2`, `@requires-ram-clear` | unsched — D18 tags exist in the language |
| UC-25 | PE | A window run must equal a CI run | Confirm the window and `sasctl` do the same thing | Same engine, same verdict, and the window states which options it used | same engine now, but the window's options are **hard-coded and unstated** |

### D. Triage

| ID | Actor | Trigger | Goal | Success condition | Status |
|---|---|---|---|---|---|
| UC-26 | QA | Red suite at 18:00, going home | Decide in five minutes whether this blocks the release | Failures grouped and ranked, each with one sentence of cause; environment ruled in or out; a handover artefact produced in one action | now: **poorly** — one flat `ListBox` of `StepLine` |
| UC-27 | PE | One assertion failed | Get to the bytes | Expected vs actual, the source line, and the exact frames around the step | Core has `TestOutcome.Wire`; **the window drops it** |
| UC-28 | QA | A red run | Tell apart *the machine is wrong* / *our decoder is wrong* / *the environment was wrong* | Three visually distinct outcomes: FAIL, known-but-undecoded, SKIP-with-reason | the language does this; the UI renders all three as plain text |
| UC-29 | DEV | A reply changed between builds | Bisect which libsas commit broke it | Two captures of the same reply compared byte by byte and field by field | unsched — M4 trace diff is the nearest thing |
| UC-30 | QA | Suite exited 3 | Understand that nothing was tested | The run reads as *not a success*, and each SKIP names its unmet capability — e.g. `egm.validation: this environment provides no 'egm.validation'` | now via CLI; the window shows the same text without emphasis |
| UC-31 | PE | M1 finding: exception `$18` arrives before the host asks anything | Recognise startup noise rather than chase it | Events before the first host action are marked pre-run, not test evidence | unsched |
| UC-32 | PE | M1 finding: the real machine returns an empty serial number | Distinguish *field absent* from *field empty* from *field wrong* | The length byte is visible and the decode says which of the three it is | partly — the decoder distinguishes; the field list flattens it |
| UC-33 | PE/QA | A stimulus moved more than was asserted on | See side effects nobody asserted on | Meter baseline diff after each stimulus, including unasserted movement | **M2** (D14) |
| UC-34 | QA | The same test passes and fails alternately | Identify a flaky test | Per-test history, or at minimum two runs side by side | unsched |
| UC-35 | DEV | A bug needs filing | Attach the evidence | One artefact with verdicts, provenance and raw wire; D13's HTML already embeds JSONL | now, CLI only |

### E. Evidence and honest labelling

| ID | Actor | Trigger | Goal | Success condition | Status |
|---|---|---|---|---|---|
| UC-36 | CUS/PE | A regulator or customer asks for proof | Prove a pass was on real hardware | The artefact states the link, `proven`/`unverified`, the stimulus backend and any caveat *before* the results, and cannot be separated from them | now in the HTML report; **not from the window** |
| UC-37 | PE | About to send a green suite to a customer | Notice, before sending, that it was loopback + software | The window makes proven-green and unverified-green unmissably different | now: **fails** — 11px, 65% opacity, one line |
| UC-38 | QA | Yesterday green, today red | Compare two runs | Two reports diffed by test, both provenances shown, so an environment change is visible | unsched |
| UC-39 | PE | M3 | Show a printed ticket to a human and assert on its JSON | Both present, and which one is the evidence is stated, not implied | **M3** (D12, principle 3) |
| UC-40 | CUS | Has the report, not the app | Read the result without installing anything | Self-contained HTML with the event log embedded | now (D13) |
| UC-41 | PE | The link is serial and unmeasured | Be prevented from over-claiming | `unverified` travels into every derived artefact; no path strips it | now in Core; the UI has no visual rule for it |

### F. Drive the machine (M2/M3)

| ID | Actor | Trigger | Goal | Success condition | Status |
|---|---|---|---|---|---|
| UC-42 | PE | Exploring | Insert a note, press collect, open a door, by hand from this app | The action happens and is recorded as an `egm` step on the same clock as the wire | **M2** |
| UC-43 | PE | Before a stimulus | Capture a meter baseline | Snapshot taken and timestamped; `+= 2000` is measured against it | **M2** |
| UC-44 | QA | The test is destructive | Snapshot / restore NVRAM; opt-in RAM clear | Explicit, opt-in, and recorded in the report | **M2** |
| UC-45 | PE | M3 | Print a ticket and re-insert it from the wallet | The print-to-redeem loop closes; the wallet ticket is the same JSON document | **M3** |
| UC-46 | QA | Negative cases | Import an externally-authored bad ticket — expired, wrong checksum, foreign machine, already redeemed | The bad ticket is visibly marked authored-elsewhere, not as something the machine printed | **M3** |

### G. Breadth (M4)

| ID | Actor | Trigger | Goal | Success condition | Status |
|---|---|---|---|---|---|
| UC-47 | PE | `sastest.ini` and GSA XML imported as overlays | Diff them against the header-derived catalogue | The diff is presented as a finding in its own right — 22 long polls in the INI with no decoder; `$72`/`$6E`/`$B0` absent from its command lists | **M4** |
| UC-48 | PE | A legacy `.trc` / `TXRXData.dat` capture exists | Import, replay and diff it against a live run | Replayed and live frames aligned; differences called out | **M4** |
| UC-49 | PE | D16 / GSA 10.1.4 | Run two channels at once and read both wires | Two live wires readable — side by side or interleaved-and-tagged — with per-channel exception buffers kept apart | **M4** |
| UC-50 | PE | D5 | Everything done by hand in the window emits a script | The emitted script is the language the parser already takes, and re-running it reproduces the session | **M4** |

### H. Living with the tool

| ID | Actor | Trigger | Goal | Success condition | Status |
|---|---|---|---|---|---|
| UC-51 | All | Second time opening the app | Not retype the endpoint, address and interval | Connection profiles remembered and named | unsched |
| UC-52 | All | Laptop at 125% scaling, 1180x760 | See the wire and the run at the same time | Neither is pushed off screen; nothing depends on a window wider than 900px | now, with two columns; at risk under any three-column proposal |
| UC-53 | All | Federal / state customer accessibility requirement | Operate by keyboard and screen reader; distinguish verdicts without colour | Full tab order, AA contrast, PASS/FAIL/SKIP/undecoded/UNKNOWN distinguishable in greyscale | **not met today** — verdict is a bold string with no glyph and no reader label |
| UC-54 | CUS/QA | No SAS spec to hand (D4) | Understand a command or exception from our own words | Every catalogue entry carries our description; nothing requires the IGT PDF | now |
| UC-55 | All | Long session | Not run out of memory or lose the start of the log | A bounded log with the bound stated — `WireLimit = 2000` — rather than lines silently dropped | now, but the bound is invisible |

**Coverage summary.** 20 of 55 work today, 5 are M2, 3 are M3, 6 are M4, and 21 are unscheduled but
justified. The unscheduled cases cluster hard in triage (UC-26…UC-38) and in living with the tool
(UC-51…UC-55) — which is exactly the ground a UX pass is for.

---

## 2. The four proposals

Before the proposals, one shared vocabulary they all use, because it is the part of the design that
must not vary between them.

### 2.0 Shared: five states, three families, no colour dependence

The product has three different kinds of status that a reader must never confuse. Today they are
all rendered as plain strings. Give each family a distinct **shape**, so the distinction survives
greyscale, a projector, and a colour-blind reader (UC-53).

**Family 1 — verdicts** (from `Verdict`: Pass, Fail, Skip, Error). Square chip, fixed-width glyph
column, uppercase word, and a left bar whose stroke differs:

```
  ▌ ✓ PASS      solid thin bar, check
  ▐ ✕ FAIL      solid heavy bar, cross
  ┆ ⊘ SKIP      dashed bar, slashed circle  + a mandatory second line: the reason
  ▐ ! ERROR     solid heavy bar, exclamation
```

A second, load-bearing cue: **SKIP always renders a reason line, so a skipped row is physically
taller than a passing row.** From two metres away a suite of skips has a different rhythm from a
suite of passes. That is the cheapest honest-labelling device available and it costs nothing.

**Family 2 — data states**, angle-bracketed so they cannot be mistaken for verdicts:

```
  ⟨decoded⟩            21 layouts; a field list follows
  ⟨known, undecoded⟩   in the catalogue, no layout in this build — the third real state
  ⟨unknown⟩            not in the catalogue at all
  ⟨no reply⟩           the machine said nothing
  ⟨violates⟩           read fine, broke its constraint; value still reported
```

**Family 3 — provenance stamps**, double-bracketed and never abbreviated:

```
  [[ PROVEN ]]        exercised end to end against the machine library and captured
  [[ UNVERIFIED ]]    implemented to spec, never measured — hatched fill, italic
```

Avalonia note: `✓ ✕ ⊘ ▌ ▐ ┆ ⟨ ⟩` are all in DejaVu Sans (the Linux fallback) and Segoe UI Symbol.
Ship an ASCII fallback set behind one token — `[v] [x] [/] [!]`, `<known, undecoded>`,
`[[PROVEN]]` — selected by a font-probe at startup, so a missing glyph never becomes a blank box.
Every chip carries `AutomationProperties.Name` with the spelled-out word plus, for SKIP and FAIL,
the reason — so a screen reader gets the same information as the eye.

---

## Proposal A — **The Bench**

### Thesis

SasWorkbench is a bench instrument permanently clipped to one SAS link; every feature is a probe
you attach to that link, and the wire is never off screen.

### Information architecture

* **Header band, always visible, two lines, never below 12px.** Line 1 is the link and its
  provenance stamp. Line 2 is the machine's identity and what is driving it. This is where
  principle 4 lives, and it is the second-largest text in the window after the run verdict.
* **Instrument rail**, left, 168px, collapsible to a 48px icon strip: Wire · Poll · Meters ·
  Exceptions · Catalogue · Suite · Session · (M2) Stimulus · (M3) Tickets · (M4) Traces.
* **The spine**, centre, always the live wire. It is never replaced by anything. It can be
  *scoped* (to a step, to a channel, to a time window) but not hidden.
* **Instrument panel**, right, is the selected rail item. One at a time.
* **Footer**: counters, the log bound, and the tape transport (record / stop / export).

Always visible: header, spine, footer. On demand: everything else.

### Primary screen

```
┌─ SasWorkbench ────────────────────────────────────────────────────────────────────────────────┐
│  LINK   tcp 127.0.0.1:9760 (listen)   addr 01   ch 1   poll 200ms      [[ PROVEN ]]     ● up   │
│  EGM    game_id QX · sas 602 · serial ⟨none⟩          DRIVEN BY  none        [ Disconnect ]    │
├──────────┬──────────────────────────────────────────────┬─────────────────────────────────────┤
│ ▸ WIRE   │ WIRE  ch1              [x] Follow   [Clear]  │ POLL                                │
│   POLL   │  5927ms  TX  1.80        sync poll - addr 0  │ Command                             │
│   METERS │  5954ms  TX  1.81        general poll        │ [ $1F  send gaming machine ID   ▾ ] │
│   EXCEPT │  5985ms  RX  0.18        exception $18       │ Data (hex)  [                     ] │
│   CATALOG│  6126ms  TX  1.80        sync poll - addr 0  │                        [  Send  ]   │
│   SUITE  │  6157ms  TX  1.01 0.1F   long poll $1F       │                                     │
│   SESSION│  6189ms  RX  0.01 0.1F 0.51 0.58 0.30 0.30   │ REPLY  ⟨decoded⟩   30.7 ms          │
│ ─────────│          0.31 0.02 0.FF 0.00 0.00 0.00 0.30  │  game_id           QX               │
│   STIMULUS          0.30 0.31 0.00 0.00 0.00 0.39 0.32  │  additional_id     001              │
│     (M2) │          0.31 0.30 0.86 0.3F                 │  denomination      02               │
│   TICKETS│  6730ms  TX  1.80        sync poll - addr 0  │  max_bet           255              │
│     (M3) │  6761ms  TX  1.01 0.54   long poll $54       │  progressive_group 00               │
│          │  6791ms  RX  0.01 0.54 0.03 0.36 0.30 0.32   │  game_options      0000  (LE)       │
│          │          0.D0 0.8D                           │  paytable_id       001              │
│          │                                              │  base_percentage   9210   [+assert] │
├──────────┴──────────────────────────────────────────────┴─────────────────────────────────────┤
│  412 sync · 412 general · 2 long · 1 exception · 0 silences        log 118/2000   ● REC  ⏏     │
└───────────────────────────────────────────────────────────────────────────────────────────────┘
```

The frames are verbatim from `docs/M1-thin-vertical-slice.md`. `base_percentage 9210` and
`sas_version 602` are the values that run actually returned. The header's `serial ⟨none⟩` is the M1
finding — `$54` came back `01 54 03 36 30 32 D0 8D`, a length of three and nothing after it — shown
as a data state rather than as a blank.

### Secondary state 1 — a run in progress

The Suite instrument is selected. The header grows a third line, the run band, which carries the
run's own provenance next to the running verdict — not in the footer.

```
┌─ SasWorkbench ────────────────────────────────────────────────────────────────────────────────┐
│  LINK   tcp 127.0.0.1:9760 (listen)   addr 01   ch 1   poll 200ms      [[ PROVEN ]]     ● up   │
│  EGM    game_id QX · sas 602 · serial ⟨none⟩          DRIVEN BY  none        [ Disconnect ]    │
│  RUN    tests/regression   ▓▓▓▓▓▓▓▓░░░░  2 of 3    over tcp 127.0.0.1:9760 (listen), proven,   │
│                                                     stimulus none              [ Stop ]        │
├──────────┬──────────────────────────────────────────────┬─────────────────────────────────────┤
│   WIRE   │ WIRE  ch1   ⟨scoped to the running step⟩     │ SUITE   tests/regression            │
│   POLL   │  6157ms  TX  1.01 0.1F   long poll $1F       │                                     │
│   METERS │  6189ms  RX  0.01 0.1F 0.51 0.58 0.30 0.30   │ ▌✓ PASS  the link comes up and the  │
│   EXCEPT │          0.31 0.02 0.FF 0.00 0.00 0.00 0.30  │          machine identifies itself  │
│   CATALOG│          0.30 0.31 0.00 0.00 0.00 0.39 0.32  │          6.9s · 5 assert(s)         │
│ ▸ SUITE  │          0.31 0.30 0.86 0.3F                 │    ▌✓ expect link up within 5s      │
│   SESSION│  6761ms  TX  1.01 0.54   long poll $54       │    ▌✓ send lp 1F      30.7 ms       │
│          │  6791ms  RX  0.01 0.54 0.03 0.36 0.30 0.32   │    ▌✓ game_id == QX          QX     │
│          │          0.D0 0.8D                           │    ▌✓ base_percentage >= 7500 9210  │
│          │                                              │    ▌✓ sas_version == 602      602   │
│          │                                              │                                     │
│          │                                              │ ◐ RUNNING  a twenty raises its      │
│          │                                              │            exception and moves its  │
│          │                                              │            meters                   │
│          │                                              │    ▌✓ connect loopback address 01   │
│          │                                              │    ◐  expect exception              │
│          │                                              │       bill_accepted_20 within 5s    │
│          │                                              │       waiting  1.2s of 5.0s         │
│          │                                              │                                     │
│          │                                              │ ┆⊘ SKIP  10.1.1 standard validation │
│          │                                              │    ┆ egm.validation: this           │
│          │                                              │      environment provides no        │
│          │                                              │      'egm.validation'               │
└──────────┴──────────────────────────────────────────────┴─────────────────────────────────────┘
```

Passing assertions are shown, with the value that satisfied them — the direct fix to observation 2
in §0. The SKIP row is two lines taller than every PASS row and carries the capability name.

### Secondary state 2 — a failed assertion under investigation

The instrument panel splits: the step list stays, and a detail block opens beneath it. The spine
**scopes itself to the step**, showing only the frames between the step's start and its deadline —
possible only because the wire is the spine and never a tab.

*(Constructed from the real M1 finding "the real machine reports an empty serial number"; the
failing assertion below is a plausible test, not captured output. The reply bytes are real.)*

```
┌─ SasWorkbench ────────────────────────────────────────────────────────────────────────────────┐
│  LINK   tcp 127.0.0.1:9760 (listen)   addr 01   ch 1   poll 200ms      [[ PROVEN ]]     ● up   │
│  EGM    game_id QX · sas 602 · serial ⟨none⟩          DRIVEN BY  none                          │
│  RUN    ▐ ✕ FAIL   1 failed, 1 passed, 1 skipped in 8.1s                                       │
│         over tcp 127.0.0.1:9760 (listen), proven, stimulus none          [ Save report… ]      │
├──────────┬──────────────────────────────────────────────┬─────────────────────────────────────┤
│   WIRE   │ WIRE ⟨scoped: identity.sas:9, 6761–6791ms⟩   │ SUITE   tests/regression            │
│   POLL   │       [ show whole session ]                 │ ▐✕ FAIL  the machine identifies     │
│   METERS │                                              │          itself                     │
│   EXCEPT │  6761ms  TX  1.01 0.54   long poll $54       │    ▌✓ expect link up within 5s      │
│   CATALOG│  6791ms  RX  0.01 0.54 0.03 0.36 0.30 0.32   │    ▌✓ sas_version == 602      602   │
│ ▸ SUITE  │          0.D0 0.8D                           │  ▶ ▐✕ serial_number == "QX0000123"  │
│   SESSION│              └┬┘ └──────┬─────┘ └───┬──┘     │ ───────────────────────────────────│
│          │            length     "602"       CRC        │ ▐ ✕ FAIL                            │
│          │                                              │ expect field serial_number of lp 54 │
│          │  the reply carries three payload bytes.      │   expected  "QX0000123"             │
│          │  the layout places serial_number after       │   got       ⟨absent⟩                │
│          │  revision; the length byte says there are    │   where     identity.sas:9          │
│          │  none.                                       │   at        6.79s into the test     │
│          │                                              │                                     │
│          │                                              │ ⟨absent⟩ is not ⟨empty⟩ and not "".  │
│          │                                              │ The length byte 03 accounts for the │
│          │                                              │ revision only.                      │
│          │                                              │                                     │
│          │                                              │ [ copy frame ]  [ open in Poll ]    │
└──────────┴──────────────────────────────────────────────┴─────────────────────────────────────┘
```

This directly serves UC-27 and UC-32, and it needs no new capture: `TestOutcome.Wire` already holds
those frames and `StepResult.At` already holds the offset.

### Walkthroughs

**UC-26 — QA triages a red suite at 18:00.**
1. Opens the app; the Suite instrument is where they left it, with the last run still on screen.
2. The header's run band reads `▐ ✕ FAIL 1 failed, 1 passed, 1 skipped in 8.1s` with the provenance
   sentence directly beneath it. First question answered before scrolling: this ran over TCP
   against the real machine, `proven`, `stimulus none` — so it is not an environment excuse.
3. The step list is collapsed to test level. One `▐ ✕`, one `▌ ✓`, one `┆ ⊘`. The skip is two lines
   tall and names `egm.validation`; QA can dismiss it as known-unavailable without protocol
   knowledge.
4. Clicks the failing test. It expands; the failing step is the only one with a heavy bar. The
   detail block says expected `"QX0000123"`, got `⟨absent⟩`.
5. The spine has already scoped itself to that step. QA does not need to know what `$54` is to see
   that the reply is eight bytes and that a label points at the length byte.
6. `[ Save report… ]` in the run band writes the D13 self-contained HTML, provenance first. QA
   pastes the path into the ticket and goes home. Elapsed: under three minutes.

**UC-04 — PE explores an undecoded long poll by hand.**
1. Rail → Poll. The command combo lists all 124 entries; each row carries its data state, so
   `$74 ⟨known, undecoded⟩` is legible before selection.
2. Selects `$74`, types data, Send. The reply block shows the raw bytes and the chip
   `⟨known, undecoded⟩`, with the sentence the ViewModel already produces —
   `this build has no layout for the reply to $74`.
3. The spine shows the frame; the PE reads the bytes by hand.
4. `[ +assert ]` is disabled on this reply, with a tooltip saying why: there is no field to name.
   That is the same reason a test would be skipped, said in the same words — the runner and the
   window cannot drift.

**UC-37 — PE is about to send a green suite to a customer.**
1. The suite is green. The header's link line carries `[[ UNVERIFIED ]]` — hatched, italic, at the
   same size as the endpoint, not at 65% opacity in 11px.
2. The run band repeats the sentence in full: `over serial COM12, unverified, stimulus manual`.
3. `[ Save report… ]` is enabled but the button's adjacent text reads `report will be stamped
   UNVERIFIED`. There is no path that produces an unstamped artefact.
4. The PE re-runs over TCP instead. Nothing was prevented; the claim was simply impossible to make
   by accident.

### How provenance is expressed

Provenance is **chrome, not content**: it lives in the window's header band, which no view can
scroll away or replace, and it is repeated inside the run band that qualifies a verdict. The two
homes identified in §0 are merged into one component used twice with the same tokens. An
`unverified` link additionally applies a hatched top border to the whole window frame — a
persistent, peripheral, non-colour signal that this session is not evidence about hardware.

### Non-colour status

Family 1/2/3 chips from §2.0, plus: the instrument rail shows a per-instrument badge glyph (an
exception count, a fail count) rather than a coloured dot; the spine renders TX and RX with
different leading glyphs (`→` / `←`) as well as the `TX`/`RX` text.

### Where M2/M3/M4 land

| Surface | Slot |
|---|---|
| Stimulus controls (M2) | A rail instrument. Actions post into the spine as `EGM` lines interleaved with the wire, so cause sits above effect. |
| Manual stimulus prompt (M2) | A band that pushes down from under the header — never a modal, because the spine must keep scrolling and the poll loop must stay visible. Carries a countdown. |
| Meter baseline diff (M2) | The Meters instrument gains a *baseline* column and a *Δ since* column; unasserted movement is marked with a `▲` glyph. |
| Ticket + wallet + JSON (M3) | A rail instrument split top/bottom: the rendered ticket labelled `for a human`, the JSON labelled `this is the evidence`. |
| Catalogue diff (M4) | The Catalogue instrument gains an overlay selector and a third data state, `⟨in overlay only⟩`. |
| Trace replay (M4) | A rail instrument that *takes over the spine* — the spine gets a replay chrome and a scrub bar. This is the one place a spine takeover is honest, because there is no live link. |
| Session recording (M4) | The Session instrument plus the footer tape transport. |
| Second channel (M4) | **Weak point.** The spine is one column. Two channels become either interleaved with a channel gutter, or the spine splits vertically — at 1180px with a rail and a panel, a split spine is about 300px per channel. See trade-offs. |

### Session recording (D5)

The bench has a **tape**. Recording is a footer transport (`● REC` / `■` / `⏏ Export`) and is on by
default, because a recording you have to remember to start is a recording you do not have.

Mechanically: every instrument action must stop calling the session directly and instead construct
the `ScriptStep` that describes it, hand that to a recorder, and let the recorder execute it. Today
`SendLongPollAsync` calls `session.SendAndDecode(command, data)`; under this proposal it constructs
the equivalent step, appends it to the tape, and executes it. The tape is therefore a list of
`ScriptStep` — the same type the parser produces — so `⏏ Export` is a serialisation, not a
reconstruction, and the emitted script cannot express anything the language cannot parse.

Assertions are added by promotion: `[+assert]` on a decoded field row appends
`host expect field base_percentage of lp 1F >= 7500`, using the observed value as the reference and
letting the PE edit the comparison in the Session instrument before export.

The §0.4 problem — a run tears down the interactive session — is answered by making the tape
outlive the link. The tape is not the session; it is a record of intents. A run appends a
`run <path>` entry and its nested outcome, then the interactive link is restored (the ViewModel
already does this: `if (wasConnected) Connect();`). The tape has a visible seam at that point,
labelled, rather than a silent gap.

### New data required

* **Machine identity in the header** (`game_id QX · sas 602 · serial ⟨none⟩`). Derived, not new:
  cache the decode of `$1F` and `$54` taken at connect. Needs a small VM addition; no Core change.
* **A scoped wire view.** `TestOutcome.Wire` and `StepResult.At` already exist; the GUI must stop
  discarding them.
* **A recorder** that turns interactive actions into `ScriptStep` values. New, in the GUI layer,
  using existing Core types.
* **Report writing from the window.** `HtmlReportWriter` exists; `SaveReportAsync` must call it.

### Trade-offs

* **Bad at**: anything involving two of something. Two channels, two runs compared, live-versus-
  replay. The single spine is the whole idea and it is singular.
* **Serves worst**: CUS. A customer who wants to run a suite and read the result is handed an
  oscilloscope with a test runner attached to the side. The Suite instrument is a 480px panel; a
  200-test conformance corpus is uncomfortable in it.
* **Costs**: the rail plus a persistent spine is a real restructure of `MainWindow.axaml`, but the
  panels themselves are the existing four tab bodies, moved.

### Build cost

**≈1.0×** (this is the baseline for the comparison). Rail = `ListBox` with a `ControlTheme` plus a
`ContentControl` with `DataTemplates`. Spine = the existing virtualized `ListBox`, plus a filter
predicate for scoping. Header band = a `Grid`. The tape = a new `ObservableCollection<ScriptStep>`
and a serializer. No custom-drawn controls.

---

## Proposal B — **The Runner**

### Thesis

SasWorkbench is a test runner with a protocol debugger attached; the unit of work is a *test*, and
the wire is evidence bound to a step.

### Information architecture

Three columns, the shape of a test IDE.

* **Explorer**, left: the suite tree — `tests/conformance/<GSA id>/` and
  `tests/regression/<feature>/` per D18 — with each test's last verdict chip, its tags, and its
  `requires`. Plus one special node, **Scratch**, which is where interactive work lives.
* **Results**, centre: the current run as a two-level tree, tests over steps.
* **Evidence**, right (or docked bottom on a narrow window): tabs bound to the selected step —
  Wire slice · Fields · Meters · (M2) Baseline diff · (M3) Ticket · Report preview.
* **Run bar**, top: target, options, Run/Stop, and the provenance card.

Always visible: explorer, results, run bar. On demand: evidence (it is a pane, but it can be
collapsed).

### Primary screen

```
┌─ SasWorkbench ────────────────────────────────────────────────────────────────────────────────┐
│ [ ▶ Run ] [ ■ ] tests/regression         link  tcp 127.0.0.1:9760 ▾   stimulus  none ▾         │
│ PROVENANCE   over tcp 127.0.0.1:9760 (listen), [[ PROVEN ]], stimulus none                     │
├───────────────────────┬────────────────────────────────────────┬──────────────────────────────┤
│ SUITES         [find] │ RUN  ▌✓ PASS  2 passed, 1 skipped 8.1s │ EVIDENCE   step: send lp 1F  │
│                       │                                        │ ┌ Wire ─ Fields ─ Meters ─┐  │
│ ▾ conformance         │ ▌✓ the link comes up and the machine   │                              │
│   ▾ 10.1              │      identifies itself      6.9s  5a   │  6157ms TX 1.01 0.1F         │
│     ┆⊘ 10.1.1 standard│    ▌✓ expect link up within 5s         │  6189ms RX 0.01 0.1F 0.51    │
│        validation     │    ▌✓ send lp 1F            30.7 ms  ◀ │         0.58 0.30 0.30 0.31  │
│        @wire-only     │    ▌✓ game_id == QX             QX     │         0.02 0.FF 0.00 0.00  │
│     ·  10.1.3 cashout │    ▌✓ base_percentage >= 7500   9210   │         0.00 0.30 0.30 0.31  │
│        ticket 1       │    ▌✓ sas_version == 602        602    │         0.00 0.00 0.00 0.39  │
│        @needs-stimulus│                                        │         0.32 0.31 0.30 0.86  │
│ ▾ regression          │ ▌✓ a twenty raises its exception and   │         0.3F                 │
│   ▾ wire              │      moves its meters       2.6s  7a   │                              │
│     ▌✓ the link comes │    ▌✓ connect loopback address 01      │  ⟨decoded⟩  8 fields         │
│        up and the     │    ▌✓ egm bill insert 2000             │  answered in 30.7 ms         │
│        machine        │    ▌✓ exception bill_accepted_20       │                              │
│        identifies     │    ▌✓ meter total_bill_in += 2000      │                              │
│        itself         │    ▌✓ meter bills_20 += 1              │                              │
│   ▾ bill              │                                        │                              │
│     ▌✓ a twenty raises│ ┆⊘ 10.1.1 standard validation          │                              │
│        its exception  │    ┆ egm.validation: this environment  │                              │
│                       │      provides no 'egm.validation'      │                              │
│ ▸ SCRATCH  (3 steps)  │                                        │                              │
├───────────────────────┴────────────────────────────────────────┴──────────────────────────────┤
│ 412 sync · 412 general · 2 long · 1 exception · 0 silences          exit code 0                │
└───────────────────────────────────────────────────────────────────────────────────────────────┘
```

### Secondary state 1 — a run in progress

```
┌─ SasWorkbench ────────────────────────────────────────────────────────────────────────────────┐
│ [ ▶ Run ] [ ■ Stop ] tests/regression    ▓▓▓▓▓▓▓▓▓░░░ 2 of 3        elapsed 4.3s               │
│ PROVENANCE   over tcp 127.0.0.1:9760 (listen), [[ PROVEN ]], stimulus none                     │
├───────────────────────┬────────────────────────────────────────┬──────────────────────────────┤
│ SUITES         [find] │ RUN  ◐ running                         │ EVIDENCE  ⟨live⟩             │
│ ▾ regression          │                                        │ ┌ Wire ─ Fields ─ Meters ─┐  │
│   ▾ wire              │ ▌✓ the link comes up and the machine   │  6730ms TX 1.80  sync poll   │
│     ▌✓ the link ...   │      identifies itself      6.9s  5a   │  6761ms TX 1.01 0.54         │
│   ▾ bill              │                                        │  6791ms RX 0.01 0.54 0.03    │
│     ◐  a twenty ...   │ ◐ a twenty raises its exception and    │         0.36 0.30 0.32 0.D0  │
│                       │     moves its meters                   │         0.8D                 │
│ ▸ SCRATCH  (3 steps)  │    ▌✓ connect loopback address 01      │  7002ms TX 1.80  sync poll   │
│                       │    ▌✓ poll every 200ms                 │  7031ms TX 1.81  general     │
│                       │    ▌✓ egm bill insert 2000             │  7062ms RX 0.4A              │
│                       │    ◐  expect exception                 │            exception $4A     │
│                       │       bill_accepted_20 within 5s       │                              │
│                       │       ▓▓▓░░░░░░  1.2s of 5.0s          │  [x] follow                  │
└───────────────────────┴────────────────────────────────────────┴──────────────────────────────┘
```

### Secondary state 2 — a failed assertion under investigation

```
┌─ SasWorkbench ────────────────────────────────────────────────────────────────────────────────┐
│ [ ▶ Run ] [ ■ ]  tests/regression        ▐ ✕ FAIL  1 failed, 1 passed, 1 skipped in 8.1s       │
│ PROVENANCE   over tcp 127.0.0.1:9760 (listen), [[ PROVEN ]], stimulus none    [ Save report… ] │
├───────────────────────┬────────────────────────────────────────┬──────────────────────────────┤
│ SUITES         [find] │ ▐✕ the machine identifies itself       │ EVIDENCE  step: identity.sas:9│
│ ▾ regression          │    ▌✓ expect link up within 5s         │ ┌ Wire ─ Fields ─ Diff ───┐  │
│   ▾ wire              │    ▌✓ sas_version == 602        602    │                              │
│     ▐✕ the machine    │  ▶ ▐✕ serial_number == "QX0000123"     │  ▐ ✕ FAIL                    │
│        identifies     │       expected "QX0000123"             │  expect field serial_number  │
│        itself         │       got      ⟨absent⟩                │    of lp 54                  │
│        last green:    │       identity.sas:9                   │                              │
│        2 runs ago     │                                        │  expected  "QX0000123"       │
│   ▾ bill              │ ▌✓ a twenty raises its exception and   │  got       ⟨absent⟩          │
│     ▌✓ a twenty ...   │      moves its meters       2.6s  7a   │  where     identity.sas:9    │
│                       │                                        │  at        6.79s             │
│ ▸ SCRATCH  (3 steps)  │ ┆⊘ 10.1.1 standard validation          │ ─────────────────────────────│
│                       │    ┆ egm.validation: this environment  │  6761ms TX 1.01 0.54         │
│                       │      provides no 'egm.validation'      │  6791ms RX 0.01 0.54 0.03    │
│                       │                                        │         0.36 0.30 0.32 0.D0  │
│                       │                                        │         0.8D                 │
│                       │                                        │           ↑ length 03        │
│                       │                                        │  three payload bytes: the    │
│                       │                                        │  revision "602" and no more. │
└───────────────────────┴────────────────────────────────────────┴──────────────────────────────┘
```

### Walkthroughs

**UC-23 — a customer runs a conformance suite they did not write.**
1. Opens the app. The explorer is the first thing they see and it is a list of named tests, not a
   hex dump. `10.1.1 standard validation` is a readable name (UC-54: our words, D4).
2. Sets link and stimulus in the run bar. Both are explicit controls — which also removes the §0
   defect that the window hard-codes `StimulusBackend.Software` without saying so.
3. Runs. Results fill in the centre. Most are `┆ ⊘` with a reason naming a capability.
4. The status bar shows `exit code 3` and the run header reads *nothing was tested* rather than a
   count of zero failures. A customer cannot mistake this for a pass.
5. `[ Save report… ]` produces the D13 HTML with provenance first.

**UC-24 / UC-19 — a developer iterates on one test.**
1. Right-clicks the failing test in the explorer → Run this test.
2. The results tree keeps the previous run's outcome as a ghost row (`last green: 2 runs ago`) so
   the change is visible without a diff view.
3. Fixes firmware, presses the same command again. Two keystrokes per cycle.
4. Tag filters in the explorer keep `@destructive` and `@slow` out of the loop.

**UC-13 / UC-50 — session recording.** See below; under B it is the Scratch node.

### How provenance is expressed

A **provenance card** immediately under the run bar, above the results, in the same reading order
as the report — the run bar and the report header are the same component, so what is on screen is
literally what will be in the file. Additionally, every *test row* carries a compact environment
badge (`tcp/proven/none`), so sorting, filtering or copying a subset of rows can never separate a
verdict from what produced it. That per-row redundancy is B's distinctive answer: A protects
provenance by making it chrome; B protects it by making it *per record*.

### Non-colour status

Family 1/2/3 chips. Plus the explorer's tree carries the same chip in the same column position at
every depth, so a folder's aggregate verdict and a step's verdict are read the same way. Test rows
have a two-space glyph column that is never used for anything else.

### Where M2/M3/M4 land

| Surface | Slot |
|---|---|
| Stimulus controls (M2) | A palette in the Scratch node — pressing a stimulus button appends an `egm` step to Scratch and executes it. |
| Manual prompt (M2) | An inline row *in the results tree*, at the position of the blocking step, with the instruction and a countdown. The run's shape stays legible while it waits. |
| Baseline diff (M2) | An Evidence tab. Natural fit: it is per-step evidence. |
| Ticket + JSON (M3) | An Evidence tab with two stacked panes, labelled `for a human` and `this is the evidence`. |
| Catalogue diff (M4) | Poor fit — a catalogue is not a test. Needs a fourth top-level place, which is where B starts to strain. |
| Trace replay (M4) | Poor fit for the same reason; a replay is a session, not a suite. |
| Session recording (M4) | The Scratch node. |
| Second channel (M4) | Good fit: a channel column in the wire evidence tab, and a channel facet in the explorer for `@channel2`. |

### Session recording (D5)

**Scratch is the recording.** There is no record button and no mode. The Scratch node is a
persistent, unnamed test document; every interactive action — connect, poll, send, read meters,
(M2) stimulus — appends its `ScriptStep` to Scratch and executes it. The centre column, when
Scratch is selected, shows the script text in an editable buffer with the results interleaved.
`Save as…` promotes it into the tree under `tests/regression/<feature>/`.

This is the cheapest of the four answers to D5 because it needs no new concept: a recording is just
a test you happen to be writing by doing rather than typing. It is also the weakest at capturing
*observation* — under B, watching the machine for ten minutes and noticing something records
nothing, because nothing you did was an action. A PE who wants "what did the machine do while I
watched" is not served.

The §0.4 problem is answered structurally: the interactive link belongs to Scratch, and running
Scratch is the same operation as running any other test, which means the teardown is expected
rather than surprising.

### New data required

* **Last-verdict-per-test history** for the explorer chips and the ghost row. New: a small on-disk
  run history keyed by test name and location. Sourced from `RunReport`, which already carries
  everything needed.
* **Per-test provenance on the row.** `RunReport.Provenance` is a list at run level, not per test.
  For a single-environment run it can be projected down; for a run spanning environments it cannot,
  so either Core attaches provenance to `TestOutcome`, or the badge shows the run's list. Say which
  before building.
* **Run controls**: the run bar's link and stimulus selectors must reach
  `ScriptRunnerOptions.Stimulus` and `AskPerson`, neither of which the GUI sets today.
* **Cancellation.** `RunScriptAsync` has no token; `[ ■ Stop ]` cannot be built without one.
* Same report-writing and `TestOutcome.Wire` items as A.

### Trade-offs

* **Bad at**: being an instrument. The wire is a tab in the third column and can be scrolled away.
  UC-02 (watch while a colleague drives) and UC-09 (prove the wakeup bit) become second-class.
* **Serves worst**: PE. This is the proposal the primary user will like least, and the primary user
  is the one who verifies claims by reading hex.
* Three columns at 900px minimum is genuinely tight (UC-52). Evidence has to be dockable to the
  bottom below ~1100px, which is a second layout to build and test.
* Catalogue, traces and channels have no natural home; B grows a fourth region in M4, which is
  exactly the retrofitting M1.5 exists to avoid.

### Build cost

**≈1.4×.** Two `TreeDataGrid` hierarchies with different templates, an editable DSL text buffer
with the parser's error positions surfaced inline, a responsive dock for the evidence pane, and a
run-history store. `TreeDataGrid` is a separate package with its own template and virtualization
learning curve.

---

## Proposal C — **The Timeline**

### Thesis

SasWorkbench records a session: everything that happens — TX, RX, exception, stimulus, human
prompt, assertion, verdict — is one time-ordered stream on one clock (D11), and every panel is a
projection of that stream.

### Information architecture

* **Header**: link, provenance stamp, machine identity. As in A.
* **The stream**, occupying most of the window: one virtualized list, columns
  `time | lane | kind | detail`. Lanes are `HOST`, `EGM`, `TEST`. The existing wire is this list
  filtered to `HOST`/`EGM` TX/RX rows.
* **Filter bar** above the stream: lane toggles, kind toggles, channel toggles, a text filter, and
  a "keep only script-worthy events" toggle that previews the exported script.
* **Inspector**, right, ~360px: the selected event, fully expanded.
* **Command line**, bottom: type one line of the DSL and it executes — the same grammar
  `ScriptParser` already takes, so the window's most powerful control is the language itself.
* **State drawers**, on demand from the header: Meters, Catalogue, Suite. These are *state*, not
  events, and C is honest that they do not belong in the stream.

Always visible: header, stream, filter bar, command line. On demand: inspector, drawers.

### Primary screen

```
┌─ SasWorkbench ────────────────────────────────────────────────────────────────────────────────┐
│ LINK  tcp 127.0.0.1:9760 (listen)  addr 01  ch 1  200ms  [[ PROVEN ]]  ● up   DRIVEN BY  none  │
│ EGM   game_id QX · sas 602 · serial ⟨none⟩          [ Meters ] [ Catalogue ] [ Suite ] ● REC   │
├────────────────────────────────────────────────────────────────────┬──────────────────────────┤
│ LANES [x]HOST [x]EGM [x]TEST   KIND [x]poll [x]reply [x]exc [x]asrt│ INSPECTOR                │
│ CH    [x]1 [ ]2 [ ]3 [ ]4      find [                    ]  [only ▸script]                    │
├────────────────────────────────────────────────────────────────────┤  reply to $1F            │
│  5927ms  HOST  →  1.80              sync poll - address zero       │  at 6189ms  ·  30.7 ms    │
│  5954ms  HOST  →  1.81              general poll                   │  ⟨decoded⟩  8 fields      │
│  5985ms  EGM   ←  0.18              ⚑ exception $18  power reset   │                          │
│  6126ms  HOST  →  1.80              sync poll - address zero       │  game_id            QX    │
│  6157ms  HOST  →  1.01 0.1F         long poll $1F                  │  additional_id      001   │
│  6189ms  EGM   ←  0.01 0.1F 0.51…   ⟨decoded⟩ 8 fields          ◀  │  denomination       02    │
│  6730ms  HOST  →  1.80              sync poll - address zero       │  max_bet            255   │
│  6761ms  HOST  →  1.01 0.54         long poll $54                  │  progressive_group  00    │
│  6791ms  EGM   ←  0.01 0.54 0.03…   ⟨decoded⟩ 3 fields             │  game_options       0000  │
│  6792ms  TEST  ✓  expect field game_id == QX                  QX   │                    (LE)   │
│  6793ms  TEST  ✓  expect field base_percentage >= 7500      9210   │  paytable_id        001   │
│  6794ms  TEST  ✓  expect field sas_version == 602            602   │  base_percentage    9210  │
│  6795ms  TEST  ▌✓ PASS  the link comes up and the machine          │                 [+assert] │
│                         identifies itself      6.9s · 5 assert(s)  │                          │
│                                                                    │  raw                     │
│                                                                    │  0.01 0.1F 0.51 0.58     │
│                                                                    │  0.30 0.30 0.31 0.02     │
│                                                                    │  0.FF 0.00 0.00 0.00     │
├────────────────────────────────────────────────────────────────────┴──────────────────────────┤
│ ▸ host expect field                                        412 sync · 412 general · 2 long     │
└───────────────────────────────────────────────────────────────────────────────────────────────┘
```

The point: cause and effect are adjacent. `TX 1.01 0.1F` at 6157ms, the reply at 6189ms, and the
three assertions it satisfied at 6792–6794ms are four rows apart, in one list, on one clock. No
other proposal puts them in the same place.

### Secondary state 1 — a run in progress, with a manual stimulus prompt blocking

This is the state that is hardest under every other proposal and easiest under this one, because a
blocking human step is simply the next event in the stream.

```
┌─ SasWorkbench ────────────────────────────────────────────────────────────────────────────────┐
│ LINK  tcp 127.0.0.1:9760 (listen)  addr 01  ch 1  200ms  [[ PROVEN ]]  ● up  DRIVEN BY manual  │
│ RUN   tests/conformance/10.1  ◐ 1 of 4   over tcp 127.0.0.1:9760, proven, stimulus manual      │
├────────────────────────────────────────────────────────────────────┬──────────────────────────┤
│ 12318ms  HOST  →  1.80              sync poll - address zero       │ INSPECTOR                │
│ 12349ms  HOST  →  1.81              general poll                   │                          │
│ 12380ms  EGM   ←  0.00              no exception                   │ ⏸ WAITING FOR A PERSON   │
│ 12440ms  TEST  ▸  egm bill insert 2000                             │                          │
│ ┌──────────────────────────────────────────────────────────────┐   │ 10.1.3 cashout ticket 1  │
│ │ ⏸  WAITING FOR A PERSON            12440ms   1:47 remaining  │   │ step 6 of 14             │
│ │                                                              │   │ 10.1.3-cashout.sas:11    │
│ │    Please do this on the machine, then confirm:              │   │                          │
│ │    egm bill insert 2000                                      │   │ if nobody confirms, this │
│ │                                                              │   │ step becomes             │
│ │    [ Done ]   [ Cannot do it ]        no answer → ⊘ SKIP     │   │ ┆⊘ SKIP with the reason  │
│ └──────────────────────────────────────────────────────────────┘   │ "no one answered within  │
│ 12641ms  HOST  →  1.80              sync poll - address zero       │  two minutes"            │
│ 12672ms  HOST  →  1.81              general poll        (polling   │                          │
│ 12703ms  EGM   ←  0.00              no exception         continues)│ the poll loop has not     │
│ 12904ms  HOST  →  1.80              sync poll - address zero       │ stopped. 14 polls since  │
│                                                                    │ the prompt appeared.     │
└────────────────────────────────────────────────────────────────────┴──────────────────────────┘
```

The prompt is an inline card *in the stream*, not a modal. The wire keeps scrolling underneath it,
which is the visible proof that the link is still alive while a human walks to the machine — and it
makes the deadline behaviour explicit before it fires.

### Secondary state 2 — a failed assertion under investigation

Selecting the FAIL row brackets the causal window in the gutter: from the last HOST action that
could have caused it to the step's deadline.

```
┌─ SasWorkbench ────────────────────────────────────────────────────────────────────────────────┐
│ LINK  tcp 127.0.0.1:9760 (listen)  addr 01  ch 1  200ms  [[ PROVEN ]]  ● up   DRIVEN BY none   │
│ RUN   ▐✕ FAIL  1 failed, 1 passed, 1 skipped in 8.1s   over tcp …, proven, stimulus none       │
├────────────────────────────────────────────────────────────────────┬──────────────────────────┤
│  6730ms  HOST  →  1.80              sync poll - address zero       │ INSPECTOR                │
│ ┌6761ms  HOST  →  1.01 0.54         long poll $54                  │                          │
│ │6791ms  EGM   ←  0.01 0.54 0.03 0.36 0.30 0.32 0.D0 0.8D          │ ▐ ✕ FAIL                 │
│ │                   └┬┘ └────┬────┘ └──┬──┘                        │ expect field             │
│ │                 length   "602"      CRC                          │  serial_number of lp 54  │
│ │6792ms  TEST  ✓  expect field sas_version == 602            602   │                          │
│ └6793ms  TEST  ▐✕ expect field serial_number == "QX0000123"    ◀   │ expected "QX0000123"     │
│                    expected "QX0000123", got ⟨absent⟩              │ got      ⟨absent⟩        │
│                    identity.sas:9                                  │ where    identity.sas:9  │
│  6794ms  TEST  ▐✕ FAIL  the machine identifies itself              │ at       6.79s           │
│                                                                    │                          │
│  causal window: 6761 – 6793ms, 3 events    [ isolate ] [ export ]  │ ⟨absent⟩ is not ⟨empty⟩. │
│                                                                    │ The length byte 03       │
│  8102ms  TEST  ┆⊘ SKIP  10.1.1 standard validation configuration   │ accounts for the         │
│                 ┆ egm.validation: this environment provides no     │ revision only, so the    │
│                   'egm.validation'                                 │ field is not present in  │
│                                                                    │ the reply at all.        │
└────────────────────────────────────────────────────────────────────┴──────────────────────────┘
```

### Walkthroughs

**UC-21 — a run blocks on a manual stimulus step.** The wireframe above is the walkthrough. The
three things that must be true — the instruction is exact, the poll loop is visibly still running,
and an unanswered prompt degrades to a SKIP with a reason rather than hanging — are all visible in
one frame without a dialog. Under A this is a band; under B an inline row; under C it is simply the
next event, which is the least special-casing of the three.

**UC-31 — recognising startup noise.** The M1 finding: *"This run began with exception `$18` queued
before the host had asked for anything."* In the stream, `$18` at 5985ms sits above the first
`TEST` row, and the gutter marks everything before the first test event as pre-run. A PE scanning
for the cause of a failure can see at a glance that this exception predates the test rather than
being caused by it. No other proposal has anywhere to put that distinction, because no other
proposal keeps observation and assertion on one axis.

**UC-29 — bisecting which libsas commit broke a reply.** Load the previous session (recorded, since
recording is always on) into a second stream and align on the `$1F` request. The two streams are
the same control twice; the diff highlights the differing symbol. The whole feature is a second
instance of the primary view — which is why C is the cheapest home for the M4 trace-diff work even
though it is the most expensive shell.

### How provenance is expressed

Provenance is an **event in the stream** as well as a header. Connecting emits
`0ms HOST ⚑ link opened — tcp 127.0.0.1:9760 (listen), [[ PROVEN ]], stimulus none`, and changing
anything that affects it emits another. The consequence is strong: **provenance is timestamped and
cannot be retro-fitted**, so a session that started unverified and later reconnected proven shows
both, in order, and a reader can see which results fall under which. That is a claim neither A nor
B can make. Its weakness is equally clear: an event scrolls, so C still needs the header stamp as
the persistent copy, which slightly undercuts the "everything is a projection" thesis.

### Non-colour status

Family 1/2/3 chips, plus a lane column that is textual (`HOST` / `EGM` / `TEST`) and a direction
glyph (`→` / `←`) — so even with all styling stripped the stream reads correctly as plain text.
This is also the export format: the stream copies to the clipboard as exactly what is on screen,
which is what makes it paste-able into a ticket (UC-35).

### Where M2/M3/M4 land

| Surface | Slot |
|---|---|
| Stimulus controls (M2) | A palette in the command line's completion; actions appear as `EGM` lane events. Perfect fit. |
| Manual prompt (M2) | Inline card in the stream. Best of the four. |
| Baseline diff (M2) | Awkward. A diff is state-over-time, not an event. It becomes an event *carrying* a table (`12660ms TEST ⚖ baseline diff — 3 meters moved, 1 unasserted`), expanded in the inspector. Workable, not elegant. |
| Ticket + JSON (M3) | An event carrying a document; the inspector shows the render and the JSON stacked, labelled. Good fit — a ticket genuinely is a thing that happened at a time. |
| Catalogue diff (M4) | Bad fit. A drawer, outside the thesis. |
| Trace replay (M4) | Best fit of the four: a replay is a stream. Two streams side by side is one control twice. |
| Session recording (M4) | The identity of the app. See below. |
| Second channel (M4) | Best fit of the four: a channel column plus a channel filter, or two lanes. Interleaved-and-tagged is the default and costs nothing. |

### Session recording (D5)

Recording is not a feature; it is what the app is. The stream *is* the recording, always on, and
the script is a **projection**: filter the stream to intent events (HOST actions, EGM stimuli,
promoted assertions), drop observation, serialise. The filter bar's `only ▸script` toggle previews
exactly what would be exported, in place, in the same list — so what you see is what you get, and
there is no separate "recorded script" artefact to fall out of sync with what happened.

Curation is per-row: each intent event has a `▸` gutter toggle for "keep in script", and each
decoded field has `[+assert]` which inserts a `TEST` event into the stream at the current time and
into the projection.

The §0.4 problem is answered head on: the stream survives the link. A run appears as a nested span
in the same stream, bracketed and labelled, and the teardown/reconnect appears as two link events
with their own provenance. This is the only proposal where the disconnect is *shown* rather than
worked around.

### New data required — the largest of the four

* **A unified session event type.** Nothing in Core is the union of `WireRecord`, `StepResult`,
  `ExceptionReport` and a stimulus action. All four exist; the stream needs one sequence over all
  of them with a common clock and a monotonic sequence number. `WireRecord.Sequence` and
  `WireRecord.Elapsed` set the pattern; `StepResult.At` is "measured from the start of the test",
  not from the start of the engine, so **the two clocks must be reconciled** before this view is
  truthful. That is a Core change and it is the gating item for this proposal.
* **A live step feed.** Today `Steps` is populated only after `runner.RunAll(documents)` returns.
  A stream that shows a run as it happens needs `ScriptRunner` to raise per-step events.
* **A manual-prompt surface with a deadline.** See §5 — `ManualStimulusBackend` takes
  `Func<string, bool> ask` with no timeout.
* **Command-line execution** of a single DSL line against a live session. `ScriptParser` can parse
  a line; executing one outside a `ScriptRunner` is new plumbing.
* Report writing, cancellation, and machine identity as in A and B.

### Trade-offs

* **Bad at**: state. Meters, the catalogue and the suite are not events, and pushing them into
  drawers weakens the thesis to "mostly a stream". A PE asking "what is `total_bill_in` right now"
  is one click further away than in A.
* **Bad at**: scale. A 200-test conformance run produces tens of thousands of events. Filters make
  it usable; they do not make it a good way to answer "how many failed".
* **Serves worst**: CUS and QA. A customer opening this sees a firehose. Discoverability is the
  worst of the four — the command line is the most powerful control and the least self-evident.
* **Riskiest**: the clock reconciliation above. If step time and engine time cannot be put on one
  axis, the central premise does not render.

### Build cost

**≈1.3× in XAML, ≈2× including the Core work.** The stream itself is a virtualized `ListBox` with
per-kind `DataTemplates` — cheap. The gutter bracket for causal windows and the inline prompt card
are `Border`/`Path` inside item templates — cheap. What is not cheap is the unified event model,
the per-step live feed, and the two-clock reconciliation. Deliberately excluded from the estimate:
a scrub ruler. It is the obvious next thing to want and it is a custom-drawn control; defer it to
M4 with trace replay, where it earns its cost.

---

## Proposal D — **The Two-Ended Bench** (considered, presented at lower resolution)

### Thesis

SasWorkbench is a two-ended lab bench: the screen is the rig. What we send and assert is on the
left, what the machine is and what we did to it is on the right, and the wire is the seam between
them.

### Information architecture

Three vertical regions, mirrored about the wire.

```
┌─ SasWorkbench ────────────────────────────────────────────────────────────────────────────────┐
│ THE HOST — SasWorkbench          ══ the link ══           THE MACHINE — QSP + libsas          │
│ [[ PROVEN ]] tcp 127.0.0.1:9760       ● up                game_id QX · sas 602 · serial ⟨none⟩│
├────────────────────────────┬──────────────────────────────┬──────────────────────────────────┤
│ WHAT WE SEND               │  5927ms → 1.80  sync poll    │ WHAT IT IS                       │
│                            │  5954ms → 1.81  general poll │  METERS      17 of 196 readable  │
│ poll every        200ms    │  5985ms ← 0.18  exception $18│   total_bill_in       0          │
│ address           01       │  6126ms → 1.80  sync poll    │   games_played        0          │
│ channel           1        │  6157ms → 1.01 0.1F  lp $1F  │   bills_20            0          │
│                            │  6189ms ← 0.01 0.1F 0.51 …   │                                  │
│ [ $1F machine ID     ▾ ]   │  6730ms → 1.80  sync poll    │  EXCEPTIONS                      │
│ data  [            ] Send  │  6761ms → 1.01 0.54  lp $54  │   $18 power reset      5985ms    │
│                            │  6791ms ← 0.01 0.54 0.03 …   │                                  │
│ WHAT WE ASSERT             │                              │ WHAT WE DID TO IT   (M2)         │
│  ▌✓ game_id == QX     QX   │                              │  [ insert $20 ] [ press collect ]│
│  ▌✓ base_pct >= 7500  9210 │                              │  [ open slot door ]              │
│  ▌✓ sas_version == 602 602 │                              │  DRIVEN BY   none                │
│  ▐✕ serial_number ⟨absent⟩ │                              │  TICKETS     (M3)                │
└────────────────────────────┴──────────────────────────────┴──────────────────────────────────┘
```

### Walkthroughs (two)

**UC-02 — watching while a colleague drives the machine.** Excellent. The colleague's actions show
up on the right, the consequences cross the seam in the middle, and the assertions that care are on
the left. This is the clearest picture of SAS the four proposals produce, and for teaching QA what
the protocol is, nothing else comes close.

**UC-26 — triaging a red suite.** Poor. There is no region for a suite. A run either takes over the
left column (destroying the mirror) or opens in a fourth place (destroying the thesis).

### Provenance, non-colour status, session recording

Provenance is **split by construction**: link fidelity belongs to the seam, stimulus backend
belongs to the right-hand end. That is architecturally elegant and practically wrong — principle 4
requires one sentence a reader cannot miss, and D gives them two facts in two places. This is the
decisive objection.

Status chips are the shared families. Session recording has no natural home: the left column is
configuration, the right is state, and neither is a tape; it would arrive as a fourth region or a
dialog, which is the retrofit M1.5 exists to prevent.

### Trade-offs and cost

Three columns at a 900px minimum is unworkable for dense hex — the seam needs ~380px on its own to
show a 24-symbol reply without wrapping badly (the `$1F` reply is 24 symbols; at `0.XX ` per symbol
that is ~120 characters). The right-hand end is mostly empty until M2 and M3 land, so D ships as a
promise. **Cost ≈1.1×**, but with the worst density profile of the four at the stated default size.

**Not recommended.** One idea survives into the recommendation: *the wire belongs physically
between what we did and what came back.*

---

## 3. Comparison matrix

Scores are 1–5, 5 best. Build cost is scored 5 = cheapest.

| Criterion | A · Bench | B · Runner | C · Timeline | D · Two-ended |
|---|---|---|---|---|
| **Serves PE** (protocol engineer, reads hex) | **5** — the wire is never off screen; scoped wire on a failure | 3 — the wire is a tab in a third column | **5** — cause, reply and assertion four rows apart | 4 — the clearest picture of the protocol, but no suite |
| **Serves DEV** (why did my firmware answer wrong) | 4 — good, but comparing two builds is off-model | 4 — per-test re-run and a ghost of the last result | **5** — two streams diffed is one control twice | 3 — no run history, no comparison |
| **Serves QA** (run, triage, not a protocol expert) | 3 — a 200-test suite in a 480px panel | **5** — the results tree is the whole middle column | 2 — a firehose; "how many failed" is a filter away | 4 — legible, but nowhere to run a suite |
| **Serves CUS** (may run a suite they did not write) | 2 — an oscilloscope with a runner attached | **5** — opens on a list of named tests | 1 — opens on a hex stream and a command line | 3 — legible but half the window is empty until M3 |
| **Density** (dense technical content per screen) | 4 — rail costs 168px, spine and panel are both full | 4 — three columns, but the third is tabbed | **5** — one list, no chrome tax, everything on one axis | 2 — three columns at 900px; the seam wraps |
| **Discoverability** (can a new person find things) | 4 — a labelled rail is self-describing | **5** — a file tree and a Run button need no explanation | 2 — the most powerful control is a text prompt | **5** — the layout explains the domain |
| **Fit for session recording (D5)** | 4 — a tape of intents; needs a recorder abstraction | 3 — Scratch is cheap but records only actions, never observation | **5** — the recording *is* the app; export is a projection | 1 — no home; would arrive as a fourth region |
| **Fit for two channels (D16)** | 2 — one spine; split gives ~300px per channel | 4 — a channel column and an explorer facet | **5** — a lane or a column; interleaved-and-tagged is free | 2 — the seam is already the narrowest region |
| **Provenance / honest labelling (principle 4)** | **5** — persistent chrome, repeated in the run band, hatched window border when unverified | 4 — a card *and* a per-row badge, so a copied subset keeps it | 3 — timestamped and un-retrofittable, but it scrolls | 1 — split between the seam and the right end |
| **Build cost** (5 = cheapest) | **4** — ≈1.0×, no custom-drawn controls, panels are today's tab bodies moved | 2 — ≈1.4×, two TreeDataGrid hierarchies, an editable DSL buffer, a responsive dock | 2 — ≈1.3× XAML but ≈2× with the Core event-model and clock work | 3 — ≈1.1× but needs a second narrow-window layout |
| **Rework risk in M3/M4** (5 = least) | 3 — traces and a second channel strain the single spine | 4 — good until M4, then catalogue diff and replay need a fourth region | **4** — replay, channels and recording all land inside the thesis; catalogue does not | 1 — three of the four M4 surfaces have no home |
| **Total** (unweighted) | **40** | **43** | **39** | **29** |

The unweighted total is close between A, B and C and should not be read as the answer — the rows
are not equally important. Weight `Serves PE`, `Provenance` and `Fit for session recording` at
double, as the primary audience and two settled decisions demand, and the order becomes
**A 55 · C 53 · B 52 · D 34**. That the three are still within three points of each other is the
real finding: none of them is wrong, and the differences are about *what gets retrofitted later*.

---

## 4. Recommendation

**Build a named hybrid: "The Session Bench" — Proposal A's shell over Proposal C's substrate, with
Proposal B's suite explorer as one first-class instrument.**

Specifically:

1. **Take A's chrome.** The two-line header band with the provenance stamp, the instrument rail,
   the persistent spine, the footer. This is the cheapest structure to build, it puts principle 4
   in the only place that cannot be scrolled away, and it is the layout that respects the primary
   audience.
2. **Take C's substrate.** The spine is not a wire log; it is the **session stream** — one
   time-ordered list of TX, RX, exception, stimulus, prompt, assertion and verdict events, filtered
   by default to TX/RX so that on day one it looks exactly like today's WIRE panel. Turning the
   `TEST` lane on is a checkbox, not a new view.
3. **Take B's explorer**, as the Suite instrument: a tree of `tests/conformance/<GSA id>/` and
   `tests/regression/<feature>/` with verdict chips, tags and requires. And take B's **per-record
   provenance badge**, because a copied or filtered subset of rows must not be able to shed it.
4. **Take D's seam idea only**: within the Suite instrument's failure detail, the frames sit
   physically between "what we asserted" and "what came back".
5. **Drop**: C's scrub ruler (defer to M4 with trace replay), C's command line as the *primary*
   control (keep it, but as a footer affordance, not the main affordance), B's three-column layout,
   D entirely.

### Why this and not one of the pure proposals

**Why C's substrate rather than A's wire log.** Two settled decisions are structural, not cosmetic.
D11 says one script, one process, **one clock** — because "within 5 seconds" is meaningless across
two clocks. D5 says the GUI emits a script from what you did. Both of those are statements about a
*single ordered event stream*, and you can add a suite tree to a stream at any time, whereas you
cannot add a stream to four tabs later without rewriting all of them. This is the same argument
that carried channels into the poll engine from day one under D16, applied to the information
architecture — and `.planning/PLAN.md` M1.5 makes that argument explicitly.

**Why A's chrome rather than C's.** Provenance must be persistent, and C's own analysis concedes
that an event scrolls. A header band that no view can replace is the strongest available answer to
principle 4, and it is also the cheapest.

**Why B is not the base.** B scores best unweighted and serves customers and QA best, and it is the
proposal I would pick if the primary audience were QA. It is not. The user is a senior protocol
engineer who verifies claims by reading hex, and B demotes hex to a tab in a third column. B also
costs 1.4× and grows a fourth top-level region in M4 for catalogue diff and trace replay — the
exact retrofit this milestone exists to avoid. Its two best ideas (the explorer, per-record
provenance) transplant cleanly; its layout does not need to come with them.

**Why D loses.** It splits provenance across two regions by construction. Everything else about it
is recoverable; that is not.

### What this commits to, in order

| Step | What | Depends on |
|---|---|---|
| 1 | Token dictionary (`ThemeDictionaries`, light + dark), the three chip families with an ASCII fallback and `AutomationProperties`, and the header band component | nothing |
| 2 | Rail + spine + instrument `ContentControl`; today's four tab bodies moved in unchanged | 1 |
| 3 | Unified session event model in Core, and the clock reconciliation between `WireRecord.Elapsed` (engine start) and `StepResult.At` (test start) | — **gating; do this before 4** |
| 4 | Spine becomes the session stream with lane filters, defaulting to TX/RX only | 3 |
| 5 | Suite instrument: tree, per-test chips, per-record provenance badge, passing assertions shown, `TestOutcome.Wire` surfaced, scoped-wire on a failing step | 2, 4 |
| 6 | Report writing from the window (`HtmlReportWriter`), run cancellation, explicit stimulus/link selectors | Core: a cancellation token on the run |
| 7 | Slots only, not pixel-final: Stimulus instrument, manual-prompt inline card with a deadline, Tickets instrument, Traces instrument, channel filter | M2/M3/M4 |

Steps 1–2 and 5–6 are the M1.5 implementation scope named in `PLAN.md` ("*implement only the shell
and the surfaces that exist today*"). Step 3 is the item to argue about: it is Core work inside a
UI milestone. My position is that it belongs here, because step 4 is what makes D5 and the M4 trace
work cheap later, and doing it after the shell means rebuilding the spine twice.

---

## 5. Tensions with settled decisions

Seven. None of them asks for a decision to be reversed; five ask for something to be added, and two
are contradictions inside the current implementation rather than inside the decisions.

**T1 — D5 (GUI session recording) versus the run/interactive teardown.** The window closes the
interactive session before a run, for a stated protocol reason:
*"having two hosts polling one machine and each seeing half the exceptions."* That reason is sound
and I am not proposing to change it. But it means a recording cannot be a continuous link session;
it has to be a continuous *record of intents* that spans a teardown. Anything built as
"capture what the link did" will have a hole in it exactly where the user pressed Run. The
recommendation handles this by making the stream, not the link, the durable object — but it is
worth saying out loud, because it is the single design constraint most likely to be discovered late
and expensively.

**T2 — D10 (`manual` prompts a human) has no deadline, and the GUI cannot prompt at all.**
`ManualStimulusBackend` takes `Func<string, bool> ask` and `Apply` is synchronous with no timeout.
`WorkbenchViewModel.RunScriptAsync` never sets `ScriptRunnerOptions.AskPerson`, and
`DefaultEnvironment.Create` only builds the manual backend `when options.AskPerson is not null` —
so today a `manual` test simply cannot run from the window. Worse, once it can, an unattended run
that reaches a manual step with nobody at the desk hangs indefinitely, which is a *silent* failure
mode in a product whose first principle is that nothing fails silently. **Proposed resolution**
(needs Core work, so flagging rather than assuming): the prompt delegate gains a deadline, and an
expired prompt raises `StimulusUnavailableException` with the reason *"no one answered within N"*,
producing a `SKIP` — consistent with D10's rule that `none` skips rather than passes. Every
proposal above assumes this and shows the countdown; none of them can honestly ship it without the
Core change.

**T3 — D16 (channels first-class, ship 1, prove 2) versus what the window offers.** The connection
bar offers channels 1–4 (`public IReadOnlyList<int> Channels { get; } = [1, 2, 3, 4];`) while
`CapabilityProbe.Baseline()` advertises only `channel.1`, with the comment *"claiming them now
would be a claim about a link nobody has opened."* The UI therefore lets a user select a channel
the environment says it does not have, and nothing says so. This is a *"never silently degrade"*
violation in the shipped window. Resolution is cheap and belongs in M1.5: render channels 2–4 with
the `⟨unknown⟩` data state and a one-line explanation, rather than as ordinary choices.

**T4 — D13 (self-contained HTML report by default) versus the window's inability to write one.**
`SaveReportAsync` is a stub whose entire body sets `Status` to *"reports are written by the command
line."* D13 does not say CLI-only, and D3 admits customers who may run a suite. UC-23, UC-35 and
UC-36 all dead-end here: from the window, there is no way to produce the evidence artefact.
`HtmlReportWriter` already exists, so this is a wiring job, but it should be named as in scope
rather than assumed.

**T5 — principle 3 ("assert on data, never on pixels") versus M3's rendered ticket.** D12 requires
both a rendered ticket with a real barcode and a JSON document. The two must be on screen together,
and the design must make it impossible to mistake the picture for the evidence. Every proposal
above labels the panes `for a human` and `this is the evidence`. The sharper consequence, which I
recommend adopting as a rule now rather than discovering in M3: **a screenshot of the rendered
ticket must never be exportable on its own** — any ticket export carries the JSON, or it is not an
export. That is a product rule, not a layout choice, and it is easier to hold from the start.

**T6 — D3 (four audiences) pulls against the density mandate.** Customers and integrators want
guidance and defaults; the protocol engineer wants hex at full density. The obvious escape is two
modes, simple and expert. **I recommend against it**, and the reason is principle 4: two modes mean
a customer's screenshot and an engineer's screenshot of the same run do not look alike, and the
whole point of honest labelling is that one artefact reads the same to everyone who holds it.
Instead: one density, and the guidance is delivered through data the product already carries —
`SasExceptionInfo.Description`, the skip `Reason`, the decode `Failure`, the `Caveat`. Those are
already written in our own words per D4. What a customer needs is not less information; it is that
the sentence next to the code is a sentence.

**T7 — an implementation contradiction, not a decision conflict: the window's run options are
hard-coded and unstated.** `RunScriptAsync` constructs
`new ScriptRunnerOptions { Stimulus = StimulusBackend.Software, Environment =
SoftwareMachineEnvironment.For() }` regardless of what the connection bar says. The safety net
holds — a script that connects over TCP with `Stimulus = Software` gets a `RefusingStimulusBackend`
whose reason is printed — so nothing silently degrades. But the window is making a choice on the
user's behalf and not telling them, and D3's *"errors must name the line and say what was
expected"* spirit argues that the run's configuration should be visible before the run, not
inferrable from a refusal afterwards. Every proposal puts the stimulus backend and the link in the
run controls for this reason.

**Not a tension, recorded so it is not re-litigated:** D19's skip-with-reason and the interactive
"known but undecoded" state are already consistent. `SasLayouts.IsKnownButUndecoded(command)` gives
the window the same sentence the runner would skip on — *"this build has no layout for the reply to
$XX"* — so a person poking by hand and a test being skipped are told the same thing in the same
words. That is the behaviour the design should preserve and extend, not change.
