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
* **Three timestamp modes** (D28) — since test start, since previous poll, since previous message.
  Selectable per view, and *not* baked into the record, so the raw log is unaffected.
* **Channels 2–4 stop being offered as ordinary choices** — the probe advertises one, so the other
  three render as `⟨unknown⟩` with the reason. This is a "never silently degrade" violation
  currently shipping in the window.

Four defects found while reading the source, fixed on the way through: a test needing a person
cannot run from the window at all (the prompt delegate is only ever set by the command line); the
window discards every assertion that passed; the channel offer above; and a manual prompt with no
deadline, which hangs an unattended run forever and produces no verdict.

---

## M2 · EGM stimulus — the two-ended test

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

* `qpm_simulator`: printer panel with a real barcode, wallet, JSON per ticket, JSON import
  for negative cases (D12). Tagged release + QSP bump (it is pinned by `GIT_TAG`).
* Fix the mask-zeroing race (Trap B) — it fabricates door events on the SAS wire.
* Ticket-in path so redemption can be stimulated at all.

**Done when** GSA **10.1.3 Cashout Ticket 1** runs unattended, end to end.

---

## M4 · Breadth

Full long-poll catalogue · `sastest.ini` and GSA XML imported as overlays and **diffed**
against the header-derived catalogue (the diff is itself a finding: 22 long polls in the
INI have no decoder, and `$72`/`$6E`/`$B0` are absent from its command lists) · `.trc` and
`TXRXData.dat` import, replay and diff · GUI session recording (D5) · second channel to
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

## Deliberately deferred

Real serial hardware validation (D6) · Linux serial backend and the full rig on Linux (D20)
· the EGM role as a product (D17) · real printer and bill-validator protocol emulation
(roadmap: exercise QSP's actual device drivers, all families).
