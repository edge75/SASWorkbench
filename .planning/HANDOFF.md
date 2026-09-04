# Handoff — resuming in a new session

Written 2026-09-04, at the end of the design session that produced this folder.
Nothing has been built yet. The next action is **M0**.

---

## The prompt to paste into the new session

Start Claude Code in `C:\Progetti\SasWorkbench`, then paste:

> Read `CLAUDE.md`, then `.planning/DECISIONS.md`, `.planning/BLOCKERS.md` and
> `.planning/PLAN.md`. They are the output of a long design session and the twenty
> decisions in them are settled — don't reopen them unless you find evidence they're wrong.
>
> Then start **M0 (Prove the wire)**. Build `tools/qxcom-tcp/` — the missing `libqxcom`
> implementation that satisfies the existing header contract in
> `C:\Progetti\QSP\simulator\include\qxcom\` but reads and writes a TCP socket carrying
> 9-bit SAS symbols — plus a gitignored `CMakeUserPresets.json` for `QSP_DemoOpenGL` that
> inherits `CustomSimulation` and links libsas's `sas_static_testable` target. Success is
> one thing: `QSP_DemoOpenGL` boots with HAL peripherals, libsas initialises with no
> Quixant board, and a general poll plus `01 1F` arrives on the socket with the wakeup bit
> set on the address byte only. Show me that hex dump.
>
> Remember the hard constraints: never touch `libsas/utils/sashost`, never edit tracked
> source in `QSP`, and anything in `QSP_DemoOpenGL` goes on a branch. If M0 turns out to
> need a change I'm not allowed to make, stop and tell me rather than making it.

---

## Where things stand

**Done:** all reverse engineering and all design decisions. Seven research agents on
`sastest.exe` + libsas + QSP, three on the GSA toolkit, three on the EGM control surface —
14 reports in `research/reports/`, ~700 KB, every claim cited to a file or a command.
Twenty decisions taken with the user, recorded in `DECISIONS.md`.

**Not done:** any code at all. No `src/`, no solution file, no `PROGRESS.md`.

**Next:** M0, and in parallel the `gsat.exe` symbol → behaviour checklist (D17).

---

## Context a fresh session cannot infer

* **The user is sharp and verifies things.** They spotted that `libsas/docs/sas-spec.txt`
  was badly extracted from intuition alone — and they were right; every message-layout
  table in it is sheared. They also correctly challenged whether `QSP_DemoOpenGL` needed
  changing at all, which led to the much better `CMakeUserPresets.json` strategy. Bring
  evidence, state confidence honestly, and expect to be checked.
* **Scope was corrected twice, firmly.** First "don't change sashost", then "don't even
  read how sashost performs — I only want to build a better application than the one in
  `C:\IGT3`". Respect that boundary; it is in `CLAUDE.md` as constraint 1.
* **The real objective arrived late and reframed everything.** It is *not* "replace
  `sastest.exe`". It is: *"Launch an unattended test that gives stimuli to both EGM and SAS
  Host and see if the other part behaves well according to the SAS Specifications"* — all
  local, on a laptop, re-runnable any time. A test drives **both ends**; "play 2 games" is
  explicitly not the SAS host's job.
* **The user answers questions with amendments, not just picks.** Several answers added a
  requirement rather than choosing an option (YAML alongside the DSL; GUI recording;
  imported JSON tickets; all four report formats). Offer options, but read the notes.
* **They asked to be guided.** *"Sometimes it's too difficult for me to understand what you
  intend for one of the solutions."* Explain the *why* and the trade-off before the choice,
  and give a recommendation.

---

## The five most load-bearing facts

1. **No build reaches the goal today.** `HAL=ON` stubs libsas out entirely; `HAL=OFF` fails
   the `\\.\QX10` check on a laptop. The gate is compile-time (`libsas_windows.cpp:77`), so
   only a rebuild with `VALIDATION_DEBUG` clears it — and libsas already ships
   `sas_static_testable` for exactly that. `BLOCKERS.md §1-2`.
2. **The seam is `assignCallbacks`, exported by libqxcom, not libsas.** `SIMULATE_QXCOM` is
   an existing QSP option whose implementation was never written —
   `QSP/simulator/include/qxcom/` is headers only, while `simulator/nvram/libnvram.cpp`
   exists. That gap is M0.
3. **The QPM shared memory is a file-backed section**, not a named kernel object, so
   `OpenFileMapping` is useless — memory-map
   `C:\ProgramData\shared_memory\lsm_QpmSharedMemory.shm`. And the GUI **clobbers all
   stimulus masks at 60 Hz**, so an external writer must replace it. `BLOCKERS.md §3`.
4. **A golden `nvram.bin` is a hard prerequisite for every ticket and note test** — printer
   and bill-validator protocols live in NVRAM and default to disabled, settable only through
   the audit-menu framebuffer.
5. **GSA's XML model is the right shape but a display model, not an assertion model.** Ours
   needs arrays, conditional predicates, byte order, units and constraints — the four
   bespoke element types in `SASTemplates.dtd` exist precisely because those were missing.

---

## Two things promised to the user

* **M0 may fail**, and if QSP's CMake won't accept an externally-supplied qxcom target, say
  so plainly rather than quietly editing their repo.
* **The `qpm_simulator` mask-zeroing race is a real bug** that can fabricate door
  open/close events on the SAS wire, worth fixing independently of this project.
  `BLOCKERS.md §3` Trap B.

---

## Research index

| Report | Subject |
|---|---|
| `00-REQUIREMENTS.md` | Consolidated spec (116 KB) — feature inventory, requirements, first script design |
| `01-binary-re.md` | `sastest.exe` reverse engineering — Delphi forms, 118 long polls, no automation |
| `02-ini-spec.md` | `sastest.ini` fully decoded — the decoder DSL, and why `[Script]` can't test |
| `03-docs-and-formats.md` | `SASTEST.HLP`, `SASINI.doc`, `TXRXData.dat` recovered |
| `03-libsas-map.md` | Early libsas map (superseded by 04, kept for reference) |
| `04-libsas-surface.md` | libsas: 122 long polls, 196 meters, 122 exceptions, the blackbox harness |
| `05-serial-diagnosis.md` | Why USB serial fails; .NET wakeup-bit feasibility; the Linux parity trap |
| `06-qsp-test-needs.md` | QSP's SAS surface and 57 candidate regression scenarios |
| `07-gsa-binary.md` | GSA HostSim + `gsat.exe` + all `.dat`/`.trc`/`.log` formats, 8 decoders |
| `08-gsa-protocol-model.md` | The GSA XML model, its coverage vs libsas, and its limits |
| `09-gsa-test-corpus.md` | The 427-test conformance corpus; what's automatable |
| `10-qpm-control.md` | QPM shared memory layout, traps, and third-process feasibility |
| `11-egm-app.md` | `QSP_DemoOpenGL` — how it's driven, and every blocker |
| `12-local-topology.md` | The all-local two-peer topology and what's missing |
