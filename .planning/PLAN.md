# Plan

Riskiest-first. Each milestone ends with something demonstrable.
Status is tracked in `PROGRESS.md` at the repo root (see the protocol in `CLAUDE.md`).

---

## M0 · Prove the wire  ← **START HERE**

**The whole plan rests on one unproven assumption**: that `QSP_DemoOpenGL` can run with
simulated peripherals *and* a real libsas SAS wire at the same time. Nobody has ever built
that path (`QSP/simulator/include/qxcom/` is headers-only). If it doesn't work, everything
downstream is theory — so it goes first, alone.

**Build**
1. `tools/qxcom-tcp/` — the missing `libqxcom` implementation. Satisfies the existing
   header contract (`QSP/simulator/include/qxcom/libqxcom*.h`) but reads/writes a TCP
   socket. Implements `assignCallbacks` plus the `QUIXANT_SERIAL_CALLBACKS` function set,
   with `readExtended`/`writeExtended` carrying `unsigned short` 9-bit words.
   Wire format: little-endian `uint16` per symbol, bit 8 = wakeup.
2. `CMakeUserPresets.json` for `QSP_DemoOpenGL` (**gitignored — not a tracked file**),
   inheriting `CustomSimulation`, linking libsas's `sas_static_testable`
   (full sources + `VALIDATION_DEBUG`, so the `\\.\QX10` gate compiles out) and our shim.
3. A throwaway socket listener that answers nothing, just to log bytes.

**Done when** `QSP_DemoOpenGL` boots with HAL peripherals, libsas initialises without a
Quixant board, and a general poll plus `01 1F` (Send gaming machine ID) arrives on the
socket with the wakeup bit set on the address byte only.

**Deliverable:** that hex dump, in the chat.

**If it fails:** report exactly where and why. Fallback order — (a) a hook inside the
existing `if(SIMULATE_QXCOM)` block in QSP's CMake, (b) a standalone `egm-bridge` linking
`sas_static_testable` directly without the demo, (c) the C# fake EGM alone while the
build problem is escalated. **Do not quietly edit QSP source.**

### In parallel with M0 (no dependency)
Mine `gsat.exe`'s **599 domain symbols** into a behaviour/assertion checklist
(D17). Each symbol name encodes a testable rule — e.g.
`_AFT_Reg_Move_To_Status_80_Issue_Exception_0x6E`,
`_AFT_0x72_Delay_Between_Consecutive_0x69_Exceptions`,
`_Are_0x6F_0xAF_response_meters_bcd`. Use it to find gaps in our protocol model and
assertion vocabulary. **Reference only — do not copy GSA's names into shipped code (D4).**
Symbols are recoverable via the COFF string table; extractor approach is in
`research/tools/`.

---

## M1 · Thin vertical slice

Narrow but complete, so every layer is exercised before anything is widened.

* `SasWorkbench.Core` — transports (`Loopback`, `Tcp`; `Serial` stubbed behind a capability
  probe), 9-bit framing, CRC, the poll loop on a dedicated thread with zero allocation on
  the hot path.
* Protocol model for ~15 long polls, **generated from libsas headers** (`lp.h`, `libsas.h`
  enums), not hand-written. Design the field-type system as an **assertion** model — see
  `BLOCKERS.md §7` for where GSA's display model falls short.
* Script parser (DSL + YAML), the assertion vocabulary, `requires` capability matching (D19).
* `sasctl run` with the self-contained HTML report (D13).
* Avalonia window: connection, live frame log (hex + decoded), meters, exceptions, Run.
* `SasWorkbench.Egm` — the C# fake EGM.
* CI: windows-latest + ubuntu-latest, build + unit tests (D20).

**Done when** `sasctl run tests/conformance/10.1/10.1.1-standard-validation.sas --tcp ...`
prints `PASS`, and the GUI shows the same run live.

---

## M1.5 · Design contract  ✅ done 2026-09-04

The window built in M1 is a protocol debugger with a Run tab stapled to it: four style rules, no
colour tokens, everything at 55% opacity, and one line of 11px grey text carrying the provenance
that D-principle 4 calls load-bearing. It is 222 lines of markup — the cheapest it will ever be to
restructure. M2, M3 and M4 each add panels (stimulus and manual prompts; printer, wallet and
ticket JSON; catalogue diff, trace replay, session recording, a second channel), so the same
retrofitting argument that carried channels into the poll engine from day one (D16) applies to the
information architecture.

**Produce, not implement:** a design contract — use cases, information architecture, interaction
model, colour and type tokens, a component inventory, and named slots for the M2–M4 surfaces.
Three or more whole-application UX directions and four or more layout-plus-colour UI directions,
compared honestly, with a recommendation the user chooses from.

**Implement only** the shell and the surfaces that exist today. The ticket and stimulus panels are
specified as slots and refined in M3 against the real thing, because designing them pixel-final now
would be designing against a milestone that has not run yet.

**Done when** the user has picked a UX direction and a UI direction, the tokens are a real
`ThemeDictionaries` resource that light and dark both resolve against, the M1 surfaces are rebuilt
on it, and PASS / FAIL / SKIP / known-but-undecoded / UNKNOWN are distinguishable without colour.

See `docs/M1.5-design-contract.md` for what was built, the five defects it found, and the five
deviations from the contract.

### Chosen, 2026-09-04 — see D21–D28

**UX: The Session Bench.** A's shell, C's session-stream substrate, B's suite explorer as one
instrument. **UI: Bench**, with selection-driven decode grafted from Console and a report mode
grafted from Dossier — the report mode demoted below everything the engineer uses daily, because
D24 narrowed the audience to personal use with developers second.

Build order, gating item marked:

| | | |
|---|---|---|
| 1 | Token dictionary, light and dark; the three chip families with an ASCII fallback and automation names; the header band | — |
| 2 | Rail, spine, instrument host; today's four tab bodies moved in unchanged | 1 |
| 3 | **Unified session event model in Core, and the clock reconciliation** between engine-relative wire time and test-relative step time | **gating — before 4** |
| 4 | The spine becomes the session stream, lane filters defaulting to TX/RX | 3 |
| 5 | Suite instrument: tree, per-test chips, per-record provenance, passing assertions shown, captured frames surfaced, scoped wire on a failure | 2, 4 |
| 6 | Report writing from the window, run cancellation, explicit stimulus and link selectors | a cancellation token on the run |

Also in this milestone, because all three touch the wire row that step 4 rewrites anyway:

* **The wakeup-bit display setting** (D27) — flag digit, or colour only, which is the default. This
  needs the handoff change a wire row already requires: its symbols become a list carrying a
  per-symbol wakeup flag, because a joined string cannot highlight bit 8.
* **Timestamp modes** (D28) — since test start, since previous poll, since previous message.
  Selectable per view, and *not* baked into the record, so the raw log is unaffected.
  **A fourth, wall-clock time, was added to D28 on 2026-09-05 and landed on 2026-09-07** with the
  extended log — see M3 and `docs/M3-extended-log.md`.
* **Channels 2–4 stop being offered as ordinary choices** — the probe advertises one, so the other
  three render as `⟨unknown⟩` with the reason. This is a "never silently degrade" violation
  currently shipping in the window.

Four defects found while reading the source, fixed on the way through: a test needing a person
cannot run from the window at all (the prompt delegate is only ever set by the command line); the
window discards every assertion that passed; the channel offer above; and a manual prompt with no
deadline, which hangs an unattended run forever and produces no verdict.

---

## M2 · EGM stimulus — the two-ended test  ✅ done 2026-09-05

A single script inserts a twenty-dollar note into a real gaming machine and asserts both
exception `$4A` and the bill meters over the wire, unattended, with no person and no
peripheral simulator window. Doors and buttons are driven from the machine's own declared
configuration, never from the simulator's labels. See `docs/M2-egm-stimulus.md`, which also
records two corrections the machine forced: money meters are in credits rather than cents,
and long poll `$08` is not what a note acceptor was waiting for.

The simulator's window is **replaced** for unattended runs rather than changed, so
`qpm_simulator` is untouched; its external-control mode moves to M3, where that repository is
being changed anyway.

* C# QPM shared-memory driver: memory-map the **file** (`C:\ProgramData\shared_memory\
  lsm_QpmSharedMemory.shm`; Linux `/dev/shm/QpmSharedMemory`), heartbeat-clocked buttons,
  the layout in `BLOCKERS.md §3`.
* `egm` script steps; `manual` / `qpm` / `none` stimulus backends (D10).
* Either replace the simulator GUI for unattended runs, or add its external-control mode —
  the GUI clobbers at 60 Hz (Trap A).
* Golden `nvram.bin` fixture — produced once by hand through the audit menu, then committed.
* Resolve the denomination and intrusion label traps by reading the **EGM's** config.

**Done when** a single script inserts a $20 note and asserts both exception `$4A` **and**
`total_bill_in += 2000`, unattended.

---

## M3 · Tickets — the first genuinely impossible test

* `qpm_simulator`, **on a branch** (D38): printer panel with a real barcode, wallet, one ticket per
  file, and import for negative cases (D12). A ticket is written and read in two encodings from one
  model — a key/value text file or JSON, chosen by extension on read and by a flag on write (D37). **No QSP bump is needed after all** — the `GIT_TAG` pin at
  `QSP/CMakeLists.txt:92` sits inside `if(HAL)`, and the SasWorkbench preset builds with `HAL` off
  and turns it on for QPM2's scope only, so the simulator is never fetched by this build. It is run
  as its own executable against the shared region. If a local checkout ever does need to be in the
  build, CMake's own `FETCHCONTENT_SOURCE_DIR_<name>` override goes in the gitignored preset —
  QSP's CMake documents exactly that at line 70. Either way nothing in QSP is edited (D30).
* Ticket-in needs the HAL peripheral manager to raise a ticket-pending event, which lives in QPM2.
  **QPM2 is substituted, never edited** (D32): its HAL source is swapped at configure time the way
  M0 swapped the serial transport and libsas's platform check.
* Fix the mask-zeroing race (Trap B) — it fabricates door events on the SAS wire.
* Ticket-in path so redemption can be stimulated at all.
* **The fourth timestamp mode, wall-clock time** (D28), outstanding from M1.5. ✅ **done
  2026-09-07**, with the extended log rather than in a wave of its own — the mode exists for
  matching a run against another tool's log, which is what somebody reading an extended log is
  doing. It needed no new field. Asking for it without the instant the session began is refused
  rather than answered from the current clock.
* **The ticket surface in `qpm_simulator`** (D35), which is an ImGui application: a popup showing
  printed tickets, buttons to export one as JSON, and buttons to load a JSON ticket so an insertion
  can be simulated by hand. The by-hand path and the scripted one must be the same path.
* **The extended log** (D34) — ✅ **done 2026-09-07**, see `docs/M3-extended-log.md`. Available as
  `run --report extended`, as `sasctl explain <run.jsonl>`, and from the window; a test asserts the
  two renderings are byte-identical, which is what makes it a rendering of the record rather than a
  second capture. A packet this build cannot decode is named and marked `known but not decoded`, and
  the tally reports coverage twice — over all packets, and over the distinct long polls a run
  exchanged, which is the number M4 moves.

* **The accounting denomination in a script** (D36) — ✅ **done 2026-09-07**, see
  `docs/M3-accounting-denomination.md`. A case states the unit it assumes before the link opens;
  against the software machine that builds one, against any other it is read back off the
  identification reply and a mismatch skips naming what the machine is in force at. The setter is a
  stimulus, so a machine that cannot be reconfigured refuses it and the case skips. **Six** of the
  forty-four validation cases now run, not the seven estimated: the seventh needs a wager smaller
  than one game credit, and nothing here can say what a game credit is worth.

**Done when** GSA **10.1.3 Cashout Ticket 1** runs unattended, end to end.
✅ **it does, against the software machine, on 2026-09-06** — see `docs/M3-tickets.md`. The
cashout ticket information poll is modelled from the library's handler and answered by the
software machine, which is silent under the enhanced styles the way section 15.5 asks; a wire
field can be compared against the ticket the printer filed, which is the only way to say the host
was told about that ticket rather than about a plausible number; and the suite's honesty check
now asks each conformance case to run or to say what it needs, with at least one running.

✅ **and a conformance case runs against a real machine on 2026-09-06** — see
`docs/M3-golden-memory.md`. Two memory images are committed, because a machine performs one
validation style at a time, and the enabled-features reply now decodes, so a case's declared style
is answered by the machine over the link rather than by our own fixture's configuration. `10.1.1`
passes against `QSP_DemoOpenGL` and asserts what the corpus asks for rather than sending the
command that sets the style it is about the absence of.

### Done ahead of M3, on 2026-09-05

* **Two one-behind defects fixed** — a late reply being read as the next reply, and an abandoned
  command's answer being returned to the next caller. Both reported success while returning the
  wrong message. See `docs/one-behind.md`. This is D33.
* **Long polls 6F and AF decode**, which needed two general additions rather than a decoder written
  by hand for one command: a field whose length comes from an earlier field, and a group that
  repeats to the end of a reply whose elements are not all the same size.
* **The accounting denomination is modelled** — the Table C-4 values, the divisibility rule and the
  refusals, and a per-meter unit derived from the library's own meter table. The software machine
  counts credit values in a configured denomination and answers the selected-meter polls.
* **`tests/validation/accounting-denomination/`** — forty-four cases from the user's test plan, in
  the third tree D18 was extended with.
* **A session can be searched** from the window and from the command line, by long poll, exception
  or kind of poll, at either end of the link.
* **Three unit defects, found by measuring the machine rather than reasoning about it.** A field
  assertion after a long poll carrying data was answering a second, dataless poll rather than the
  reply that poll obtained. The bill-value meter was modelled in credits where the specification
  fixes it in whole currency units. And the software machine published one accounting denomination
  while counting in another, so every credit meter disagreed with the real machine by a factor of
  twenty — a disagreement between peers that says nothing, which is the only kind D7 cannot use.

---

## M4 · Breadth  — the import landed 2026-09-07

**The extended log of D34 is the measurement this milestone moves, and it exists now.** Every
packet it can only name rather than explain is a layout M4 has not established yet, so the honest
place to start is whatever the extended log of a real run leaves undecoded — the log names those
commands under its own tally, so the starting list is produced rather than guessed at:

```
sasctl run tests/conformance --tcp 127.0.0.1:9760 --report jsonl
sasctl explain runs/<timestamp>/run.jsonl        # the 'known but not decoded' list is the backlog
```

✅ **The reader is built and the number is measured** — see `docs/M4-import.md`. `sasctl import`
reads the toolkit's traces and its session log and the legacy tester's serial log into the same
record a run writes, so `sasctl explain` renders an import unchanged and the two renderings stay
byte-identical. Over our own suite the tally reads **10 of 10 distinct long polls (100%)**; over the
imported corpus, **9 of 21 (43%)** — the same build, changing nothing but whose traffic it is. The
backlog is `$1F $20 $2F $35 $36 $37 $4C $4D $51 $54 $86` known but not decoded, and `$30` in no list
this build holds.

Three findings worth carrying forward. A trace's brackets are the **ninth bit**, established by
counting rather than assumed — and the tracer breaks a printed line at every byte carrying one, so
a reply with a spurious bit in it arrives as fragments, which is why `$1F` and `$54` decode in our
own suite and not over the corpus. The session log's **direction field was documented wrong**: 2 is
the machine, 0 never occurs. And **the 39 MB `TXRXData.dat` is worthless for breadth** — 1,124,493
messages, two distinct values, zero replies, so it is a tester polling something that never
answered. The 4 KB one beside `sastest.exe` carries nine distinct long polls.

Still to do in this milestone: the full long-poll catalogue and the layouts on the backlog above ·
`sastest.ini` and GSA XML imported as overlays and **diffed** against the header-derived catalogue
(the diff is itself a finding: 22 long polls in the INI have no decoder, and `$72`/`$6E`/`$B0` are
absent from its command lists) · replay of a capture against a machine and a diff of the verdicts ·
splitting a capture into the transactions inside it · GUI session recording (D5) · second channel to
prove D16 against GSA 10.1.4.

---

## M5 · Casino simulation — the many-EGM, many-category test

100 software EGMs against one or two hosts, with the hosts partitioned by **category** rather than
by machine (D29). For example Advanced Funds Transfer and Legacy Bonus on one host, Validation and
Information on the other.

**Why it is last and why it is worth doing.** It is the only configuration that exercises three
things the rest of the plan carries but never proves: the channel dimension D16 insisted on from
day one, the single-log-many-channels rule of D23, and the log volume that forces D26's container
format. It is also the first test where the *host* is under load rather than the machine — a poll
cycle across 100 machines at 40 ms each is four seconds per round, and that number, not the
protocol, is what a floor controller lives or dies by.

**What has to exist first**

* **The category→channel map in our software machine.** QSP holds seven categories, each a 2-bit
  field selecting one of four channels (`QSP/src/LibMarketSas/SasConfigurationFlags.h:35-41`):
  General Control · Advanced Funds Transfer · Progressive · Legacy Bonus · Validation · Component
  Authentication · Information. A category cannot be on two channels — the field holds one value —
  so **non-overlap is structural, not a rule the host polices**. Our fake EGM must model the same
  map and must refuse a category pointed at a channel with no poll address, rather than accepting a
  poll nothing will answer.
* **The map recorded as provenance.** Which host owns a poll, and which host owns an exception, is
  only answerable if the map is known, and the map lives in the machine.
* **The container log format** of D26. At 200 ms across 100 machines a session is roughly two
  orders of magnitude larger than anything M1–M4 produces.
* **Address allocation.** SAS addresses run 1–127, so 100 machines fit — but each needs an address
  per channel it answers on.

**Done when** one script brings up 100 machines behind two category-partitioned hosts, moves funds
on one host and redeems a ticket on the other, and the single session log attributes every poll and
every exception to the right host — with the poll cycle time reported as a measurement rather than
assumed.

---

## M6 · Release pipeline  ✅ done 2026-09-04

Every push to `main` packages the command line and the window for Windows and Linux, and publishes
a release tagged `v<version>` with the version read from the projects rather than written in the
workflow. The same workflow runs locally under `act`.

Both platforms are cross-published from one Linux job, which is what makes the local run possible —
`act` cannot start Windows containers. Windows-native testing stays in `build.yml`, which is the
workflow that decides whether the code works; this one only decides what ships.

See `docs/releasing.md`.

---

## M7 · Whole-EGM conformance from the test-case documents

**The milestone the rest of the plan is for.** Everything before it proves that one feature can
be tested; this one turns that into a suite that validates an entire gaming machine across
**every** SAS feature, from the test-case documents the user holds.

**The documents arrive when this milestone starts, not before.** The user has several PDFs
enumerating the cases. Writing scripts against a guess at them and reconciling later costs more
than waiting, and a case list invented here would quietly become the definition of "complete".
So the first task is to read them, and the second is to say plainly which cases this build can
run, which need something that does not exist yet, and which cannot be automated at all.

**What has to exist first**

* **The full long-poll catalogue and the layouts to assert on it** — M4. A case whose reply this
  build cannot decode is a `SKIP`, not a pass, so breadth of decoding is the ceiling on breadth
  of testing.
* **Tickets** — M3. A large part of any machine-validation document is the money path.
* **Whatever the documents need that M2's stimulus cannot do.** Expect gaps: handpay and
  jackpot reset, AFT transfers, progressives, component authentication. Each gap is either a new
  stimulus, a new host command, or an honest `SKIP` with a reason — and never a silent pass.

**How it is built**

* One directory per document section, named as the document names it, so "we pass section 4.3"
  is a sentence somebody can say — the same argument as D18.
* Every case declares its `requires`, so a machine or a build that cannot run one skips it by
  name rather than failing it.
* Coverage is reported as a measurement, not a claim: how many cases exist, how many ran, how
  many were skipped and why. A suite that skips half its cases must not look like one that
  passed them.

**Done when** the suite runs end to end against a real machine unattended, and its report
accounts for every case in the documents — passed, failed, or skipped with a reason.

**D4 applies to those documents as it applies to the spec.** Their content decides what we test;
every word this product ships is our own.

---

## Deliberately deferred

Real serial hardware validation (D6) · Linux serial backend and the full rig on Linux (D20)
· the EGM role as a product (D17) · real printer and bill-validator protocol emulation
(roadmap: exercise QSP's actual device drivers, all families).
