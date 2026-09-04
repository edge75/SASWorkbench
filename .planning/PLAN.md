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

## Deliberately deferred

Real serial hardware validation (D6) · Linux serial backend and the full rig on Linux (D20)
· the EGM role as a product (D17) · real printer and bill-validator protocol emulation
(roadmap: exercise QSP's actual device drivers, all families).
