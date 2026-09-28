# Blockers, traps and the unblocking strategy

Everything here was verified in source or measured from a binary during the
2026-09-03/04 reverse-engineering pass. Each claim cites its evidence. Full detail is in
`research/reports/`.

---

## 1. THE blocker — no build configuration reaches the goal today

```
HAL=ON    simulated peripherals ✓   but libsas is REPLACED by a 130-line do-nothing stub
          (runs on a laptop)         → QSP_DemoOpenGL/src/hal/libsas.cpp
                                       assignCallbacks() { }   sasInit() returns success

HAL=OFF   real libsas ✓             but sasInit() → validateQuixantPlatform() opens \\.\QX10
                                       and returns QXT_INVALID_PLATF_ERR_OFFSET on a laptop
                                       → libsas/src/libsas_windows.cpp:67-81
```

You can have **simulated peripherals** or **real SAS**, never both. This is why the goal
has never been testable locally.

### The QX10 gate is compile-time — this matters

```c
// libsas/src/libsas_windows.cpp:75-81
//#define VALIDATION_DEBUG
#if !defined(VALIDATION_DEBUG)
    coreHandler = CreateFile("\\\\.\\QX10", ...);
    if (coreHandler == INVALID_HANDLE_VALUE)
        return QXT_INVALID_PLATF_ERR_OFFSET;
```

Called from `libsas.cpp:100`, `libsas.cpp:197`, `libsas_wrapper.cpp:141`.
**No DLL substitution, config or runtime trick can bypass it.** libsas must be *rebuilt*
with `VALIDATION_DEBUG` — which is a build flag, not a source change.

libsas already ships the target for exactly this:
`add_library(${PROJECT_NAME}_static_testable STATIC ${FULL_SRC_FILES})` with
`VALIDATION_DEBUG` — `libsas/CMakeLists.txt:197,206`.

---

## 2. The unblocking strategy (satisfies "don't change QSP or the demo")

The mechanism **already exists in the codebase and was never finished**:

```
QSP/CMakeLists.txt:38    option(SIMULATE_QXCOM "Use QXCOM simulator instead of hardware" OFF)
QSP/CMakeLists.txt:50    if(SIMULATE_QXCOM) include_directories(simulator/include/qxcom)
QSP/CMakeLists.txt:41-45 if(HAL) forces SIMULATE_NVRAM + SIMULATE_QXCOM ON, adds SKIP_INTEGRITY_CHECKS

QSP/simulator/nvram/libnvram.cpp         ← NVRAM simulator: IMPLEMENTED
QSP/simulator/include/qxcom/*.h          ← QXCOM simulator: HEADERS ONLY, no .cpp
                                            ^^^ THE ENTIRE GAP

QSP_DemoOpenGL/CMakePresets.json         "CustomSimulation" → SIMULATE_NVRAM + SIMULATE_QXCOM, HAL off
QSP_DemoOpenGL/.gitignore:131            CMakeUserPresets.json    ← ALREADY IGNORED
```

Someone set all of this up. Only the implementation behind `SIMULATE_QXCOM` is missing.
The headers even declare `QUIXANT_SERIAL_DATABITS_9`, so the API speaks 9-bit natively.

**The seam is `assignCallbacks`, exported by libqxcom — not libsas:**

```c
// qxcom/include/libqxcom_callbacks.h:81
QUIXANT_EXPORT void assignCallbacks(void *callbacksStruct);

// the struct it takes (same file) — note the 9-bit "Extended" variants:
typedef unsigned int (*readExtended_callback) (long long h,       unsigned short *buf, unsigned int len,
                                               unsigned long long *timestamps, unsigned int *read);
typedef unsigned int (*writeExtended_callback)(long long h, const unsigned short *buf, unsigned int len,
                                               unsigned int *written);
```

Usage precedent: `libsas/examples/example_sas.cpp:5439` calls
`assignCallbacks((void*)&serialCallbacks)`.

### Therefore

1. **The missing `libqxcom` implementation lives in THIS repo** (`tools/qxcom-tcp/`),
   satisfying the existing header contract but reading/writing a TCP socket instead of
   `\\.\QXCOM`. Nothing is added to QSP.
2. **libsas is rebuilt, not edited** — link `sas_static_testable`.
3. **The wiring goes in `CMakeUserPresets.json`**, which is already gitignored, so it is
   literally not a tracked file in `QSP_DemoOpenGL`.

**Honest caveat:** items 1 and 2 are certain from the code. Whether item 3 composes with
*zero* tracked edits depends on QSP's CMake accepting an externally-supplied qxcom target.
If a hook is needed it goes inside the existing `if(SIMULATE_QXCOM)` block — a pre-existing,
off-by-default option customers never take. **Tell the user plainly if that happens.**

---

## 3. qpm_simulator — IPC facts and three traps

### Shared memory is a FILE-BACKED section, not a named kernel object

```
Windows: C:\ProgramData\shared_memory\lsm_QpmSharedMemory.shm   (exactly 1024 bytes)
         CreateFileMappingA(fileHandle, NULL, PAGE_READWRITE, ...)  ← name arg is NULL,
         so OpenFileMapping is UNUSABLE. Memory-map the FILE.
Linux:   shm_open("/QpmSharedMemory")  →  /dev/shm/QpmSharedMemory
```
Evidence: `libsharedmemory.hpp` v1.8.0 `persistence_file_path()`; `QpmSharedMemory.h:16`
passes `persist=true`, which selects the file-backed branch.

### Layout — a plain 148-byte struct, no header or framing

| Offset | Size | Field | Written by |
|---|---|---|---|
| `0x000` | 4 | `heartbeat` (u32) | EGM |
| `0x004` | 4 | `intrusion_mask` (u32) | GUI / orchestrator |
| `0x008` | 4 | `dio_mask` (u32) | GUI / orchestrator |
| `0x00C` | 128 | `lamps[32]` (u32 each) | EGM — **read-back channel into EGM intent** |
| `0x08C` | 4 | `bna_mask` (u32) | GUI / orchestrator |
| `0x090` | 4 | `printer_mask` (u32) | GUI / orchestrator |
| `0x094`+ | — | unused, zero | |

Declared identically in `qpm_simulator/src/Main.cpp:25-33` and `QPM2/src/hal/libqpm.cpp:152-161`.
Lamp states: `0`=off, `1`=on, `2`=blink, `4`=blink-alternate.

No mutex, event, semaphore or sequence counter. Safety comes from **disjoint field
ownership**; aligned 32-bit writes cannot tear on x86-64.

### Trap A — the GUI clobbers at 60 Hz
`ProcessSharedMemory()` is the first statement of `Render()`, runs every vsynced frame, and
unconditionally rewrites all four stimulus masks from its own local booleans
(`Main.cpp:139-172,196`). **A third party's writes are erased within ~16 ms.** The C#
orchestrator must *replace* the GUI, or `qpm_simulator` needs an external-control mode.

### Trap B — a real race that fabricates SAS events  ✅ fixed 2026-09-05

**Fixed on `qpm_simulator` branch `ticket-printer-and-mask-race` (D38), commit `4103b51`,
version 2.0.0.5.** Each mask is now built in a local and published in a single aligned store.
Not yet merged to that repository's `main`. Measured before and after — see below.

What the defect was, corrected against the source rather than the original note:

* **The exposure was larger than "a zero window".** `ProcessSharedMemory` did not clear once and
  restore once. `state->intrusion_mask |= (1 << i)` is a read-modify-write **to shared memory per
  set bit**, so the machine's 125 ms poll could observe zero *or any prefix* of the mask — up to
  17 distinct values per frame for intrusion, at 60 Hz. Doors closing and reopening together was
  the worst case, not the only one.
* **`dio_mask` was published twice per frame.** The heartbeat-clocked buttons were ORed in after
  the loop had already stored a mask, leaving a second window in which a queued press was missing.
  Buttons are the one stimulus that is already edge-sensitive, so this site dropped presses rather
  than inventing events.
* **The heartbeat was read twice**, and the drain decision was taken on the first read while
  `sLastHeartbeat` was set from the second — a beat arriving between them consumed the edge that
  would have delivered the pending buttons. Same class, outside the original note; fixed in the
  same pass at the user's direction.
* Line numbers in the original note (`Main.cpp:141-146`) were stale; the code was at `150-195`.

### Trap B, measured — 2026-09-06

Both binaries were built from the same preset and sampled with four doors ticked and the window
left alone. The probe proves a writer is present before and after each run by writing a sentinel
into the mask and waiting for the simulator's own 60 Hz rewrite to erase it — without that, a
**closed** simulator is indistinguishable from a well-behaved one, because `persist=true` leaves
the last value in the file. One run was discarded for exactly that reason before the check existed.

| | pre-fix `4db296b` | fixed `4103b51` |
|---|---|---|
| duration, steady state | 10 s | 20 s |
| samples | 15,584,692,000 | 50,313,778,000 |
| transitions observed | **10** | **0** |
| distinct values | **3** | **1** |
| excursions from `0x000F` | `0x0000` ×3, `0x0007` ×2 | none |

**`0x0007` is the finding.** Bits 0–2 set with bit 3 not yet written is a *partial* mask, not the
zero window — the "any prefix" correction above, observed rather than argued. A machine reading
that byte sees one door still open and three shut, which is not a state any door was ever in.

Telling a glitch from a fingertip needs no judgement call: a person holds a checkbox value for
hundreds of milliseconds, and these intermediate values were held for well under a microsecond —
six orders of magnitude apart.

**What this does not establish is a rate.** The probe under-observes badly: the writer holds the
cache line while it makes its seventeen stores, so most of the reader's loads are serviced only
after the burst, and it caught a bad value in about 5 of ~600 frames. The true exposure is at
least the 4 ppm measured and plausibly far higher. The measurement establishes that the
intermediate states are real and observable from another process, which is what the fix removes;
it does not support a claim of "one spurious door event every N hours".

**There is still no regression test, and there cannot be an unattended one today.** The masks are
all zero until a person ticks a checkbox — nothing else drives `sIntrusion[]` — so this check needs
a human. `qpm_simulator` also has **no test harness at all**: `CMakeLists.txt:158` leaves
`include(test/test.cmake)` commented out and the `test/test.cpp` that file names does not exist,
though GoogleTest is fetched and built.

Our own side never had this defect: `QpmSharedRegion.Write` is a single `Volatile.Write` and
`QpmMachine` builds each mask in a field before writing it.

### Trap C — two label/meaning mismatches
* **Denominations.** The GUI labels BNA bits from `qpm_simulator_config.json` `"Bills"`
  `[100,200,500,1000,2000,5000,10000]`, but the EGM **hardcodes**
  `{100,200,500,1000,2000,5000,10000,20000,50000,100000}` cents (`libqpm.cpp:766`).
  Bits 0–6 agree; bit 7 is **$200** to the EGM and renders as `##7` in the GUI.
* **Intrusions.** Simulator labels vs `QSP_DemoOpenGL/bin/.../qsp-config.json`
  `Hardware.Doors[]` disagree on bits 3/4/5 (GUI: Logic/Stacker/Dropbox; EGM:
  topbox=3→`$11`, logic=4→`$15`, stacker=5→`$19`). Bits 6-15 are unmapped on the demo.

**Address both by index from the EGM's config, never by the simulator's label.**

### Trap D — the machine has no licence, so it takes no money  ✅ **cleared 2026-09-13**

**It was the build, not the licence, and the diagnosis below was right about everything except the
last step.** The machine's secure storage did hold a valid, machine-bound key the whole time. What
rejected it was `LicenseHandler::RetrieveRequiredFeatures`, which adds `F_QSP_LICENSE_DEMO` to the
required features **`#ifndef NDEBUG`** — deliberately, so a debug build cannot run on production
hardware even by accident. The key carries `SAS` only, the `SasWorkbenchM0` preset inherited
`CMAKE_BUILD_TYPE=Debug`, and so a good key was refused, the lockup was raised at powerup and never
removed, and the platform disabled the bill validator on every evaluation.

Building the preset `Release` — in `CMakeUserPresets.json`, which is gitignored and is the one file
in that repository this project may write — validates the same key:

```
[CONDITION] RemoveCondition(Quixant License)
[INFO]      AsyncEnableBillValidator(1)
PASS  a twenty raises its exception and moves its meters (8.3s, 7 assert(s))
```

**What the reconfigure needed, and would need again.** Changing the build type forces a fresh
configure, which turned up two things the incremental build had been hiding:

* the demo pins its platform dependency to a branch that has since been **renamed upstream**, and
  its libsas and peripheral-manager dependencies likewise. `FETCHCONTENT_UPDATES_DISCONNECTED=ON`
  plus local refs at the revisions already on disk keeps the machine under test at exactly the
  revisions every earlier measurement used, so the build type stays the only thing that changed.
* a `Release` build applies reproducible-build flags at `qx_version()` and **refuses to configure**
  if the top-level directory already holds a target. This project's TCP transport was being created
  there; it is now added as a subdirectory instead, which is the honest fix rather than relaxing
  the check.

**What it was costing, and no longer does:** the four real-time event payloads. A run of
`sasctl events` on 2026-09-13 produced **5 of 10**, against 0 of 10 on 2026-09-08. See
`docs/M4-catalogue-breadth.md`.

**What is still true and still worth knowing.** Nothing on the SAS wire says a machine is locked
up: the licence condition maps to the no-activity exception, and the fault light the platform
commands does not reach the shared region's lamp words either — checked from the raw bytes over ten
samples. **An unattended run still cannot tell a locked-up machine from a broken stimulus.** That is
what made this cost two sessions, and it is a finding rather than something fixed here.
`SASWB_QPM_TRACE=1` remains the way to make the machine explain itself.

---

<details>
<summary>The diagnosis as it stood on 2026-09-08, kept because what it got wrong is the useful half</summary>

### Trap D — the machine has no licence, so it takes no money  (diagnosed 2026-09-08)

**The cause is the machine's, not ours, and it cannot be fixed from here.** The M2 case that
established the note path, `tests/regression/meters/note-moves-bill-meters.sas`, fails against
`QSP_DemoOpenGL` with *"nothing in 5000ms"* and both bill meters flat. The chain, measured link by
link with the peripheral boundary and the platform's own tracing turned on:

1. The peripheral manager reports the note to the platform. Measured — the note reaches the
   machine as `note in escrow (id 14, argument 2000)`, so nothing on this side is at fault.
2. The platform answers *"return what is in escrow"*, and says why: **`[BillValidatorHandler]
   Pending banknote while BV disabled`**.
3. Its bill validator is disabled because `ConditionHandler::UpdateMoneyInputDevices` refuses money
   while any active condition has the interrupt-game flag, and it calls
   `AsyncEnableBillValidator(0)` on **every** evaluation of this machine — never once `(1)`, in any
   run, including one with no door touched, no audit cycle and an otherwise empty condition set.
4. The condition is **`Quixant License`**, raised by the platform's licence handler at powerup and
   never removed. Its condition-defaults entry has the interrupt-game flag set.
5. The licence key lives in the peripheral manager's own secure storage file — not in NVRAM — so
   restoring `golden.nvram.gz` cannot restore it. `C:\ProgramData\quixant\launchpad\qpm_storage.bin`
   was last written **2026-09-07 16:32**, which is when the master reset noted below happened, and
   the platform's own comment says a master reset clears the licence key and the machine secret.
   *(The file is encrypted and was not read: the timestamp and the code path agree, which is
   evidence and not a reading.)*

**The master reset was the cause, not a red herring.** The earlier note here listed it among the
things ruled out — "one boot did master-reset; every run after it restored `golden.nvram.gz` first
and the machine booted clean". The memory image was indeed clean. The licence is not in the memory
image.

**How to clear it, and only a person can.** The machine's audit menu, Licensing → Quixant License →
*Enter New Key*, with a key from Quixant. The platform clears the lockup only when a stored key
validates. No long poll does it: `$06` and `$08` were both sent and both acknowledged, and the
machine still answered `AsyncEnableBillValidator(0)`.

**A host cannot see any of this, and neither can the shared region.** The licence condition maps to
`SasExceptionCode::NoActivity`, so nothing goes out on the wire — and the fault light the platform
commands for a lockup does not arrive either: with the condition active and the platform tracing
`UpdateCandles() fault dout(14) state(1)`, every one of the shared region's thirty-two lamp words
reads zero, checked from the raw bytes and over ten samples. So `BLOCKERS §5`'s "lamps are a
read-back channel into EGM intent" does not hold for the candles on this build. **An unattended run
has no way to tell a locked-up machine from a broken stimulus**, which is why this cost a session,
and it is a finding rather than something to fix here.

**What was ruled out on the way**, all measured rather than reasoned: a second demo left running ·
the memory image (the machine's memory is byte-identical to `golden.nvram.gz`) · the note being
offered into the exception burst a cold start makes · the machine sitting on its game select screen
· the host having disabled the acceptor (`$06` re-enables it and changes nothing) · the host having
narrowed the denominations (`$08` with every bit set changes nothing) · the note's value (a dollar,
five and twenty are all returned) · a door left open (the condition ledger balances) · our own door
sweep (a boot with no sweep at all behaves the same).

**What it costs:** four of the ten real-time event payloads still cannot be produced — see
`docs/M4-events.md` — and every test that needs money on this machine fails against it. A run of
`sasctl events` on 2026-09-08 after the diagnosis produced **0 of 10** payloads, and
`ConfirmedByCapture` is still false on all ten.

**What was built out of it**, and it is the part that is ours: `SASWB_QPM_TRACE=1` makes the
substituted peripheral manager print every message crossing into the machine and every escrow
decision coming back, and asks the platform to raise its own trace level so its account of a refusal
reaches the log. Without both, this failure is a five-second timeout and nothing else.

</details>

### The machine really does open a second channel, and one thing about how it does is wrong

Measured 2026-09-09 against `QSP_DemoOpenGL`, with nothing in its repository edited. Three things
had to line up and all three already existed: the demo's own configuration declares `Network 1` and
`Network 2`; the TCP transport reads a **per-port** endpoint
(`SASWB_QXCOM_ENDPOINT__DEV_TTYS5` and `..._DEV_TTYS2`), so the two ports reach two different hosts
rather than both dialling one; and the second channel's poll address and the category map live in
NVRAM, where `sasctl nvram channels` writes them. The platform's own trace shows two link-ups and
two exception buffers, and `$1F` came back as `011F…` on channel 1 and `021F…` on channel 2.

**The one configuration a person cannot set by hand** is a category pointed at a channel with no
poll address: the audit menu skips such a channel while cycling, so the menu can never produce one.
A memory written any other way can, and it is refused rather than served — see D23's second
consequence, now enforced in `SasChannelPlan`.

**Read on the way past and not fixed, because nothing in QSP is edited.**
`SasNetwork.cpp`'s loop over channels 2 to 4 passes `SerialPort::eNetwork2` whatever the index, so a
machine configured for three or four channels would give them all one port. Two channels are
unaffected, which is what this project needs today.

**Only `$57` and `$70` can be asked with nothing after the command byte**, of the nine commands the
library gates on validation. The receiver frames `$4C` at ten bytes and `$4D` at five and drops a
shorter message as incomplete before any handler sees it — so a case asserting the silence of one
of those is asserting the framing and reading it as the channel gate. Two cases written on
2026-09-09 did exactly that and passed on both peers while proving nothing. The runner now refuses
such an assertion with the length the receiver expects.

### Trap E — the generator reads one libsas and the machine runs another

`tools/protogen/protogen.py` reads `C:\Progetti\libsas`, which is **24 commits ahead** of the
revision QSP fetches and the demo is built from. Measured on 2026-09-08: with reporting on and its
queue empty, the machine answers a general poll with nothing at all, while the checkout's
`d6fe417 IGS-271 answer a general poll with no activity in real time event mode` makes it answer in
the event format. Every layout this project has established comes from the checkout; every byte one
has been checked against comes from a machine behind it. Where they disagree, neither reading is
wrong — they describe different machines, and a run has to say which.

**Measured again on 2026-09-09, for the channel gates, and there they agree.** Twenty-nine handlers
carry a channel-category gate, and all twenty-nine are byte-identical between the checkout the
generator reads and the copy `QSP_DemoOpenGL` builds from — as are the five predicates that decide
which channel hears which exception and the function that masks the enabled-features reply. So the
whole of the channel model is safe from this trap, checked rather than assumed.

**Measured on 2026-09-13, for the receiver's framing table, and there they do not — and it showed
up on the wire.** The checkout frames long poll `$2F` by a length byte (`long_poll_size[0x2f] = -1`,
with a comment saying the old fixed size read a wider request short and refused it); the copy
`QSP_DemoOpenGL` builds from still frames it at eight. Regenerating the catalogue moved the value,
the sweep began asking `$2F` for no meters at all, and a command that had answered for two
milestones went silent against the machine. Neither reading is wrong. The sweep now asks a
length-prefixed command **twice** — for nothing and for one entry — and records which form the
machine answered, so the disagreement is reported rather than resolved by picking a number. It is
also the reason to run `python tools/protogen/protogen.py --check` before trusting a coverage
figure: the committed catalogue had been stale.

**Measured again on 2026-09-09, for the meter handlers, and there they agree.** Every handler that
reads a meter - the shared single-meter helper, the six bulk replies, the twenty dedicated bill
counts and the selected-meter polls - is byte-identical between the checkout the generator reads and
the revision the machine runs. So Trap E is real and is not universal: it has to be checked per
question rather than assumed either way.

### The machine under test is in full manual, so its meters cannot disagree with themselves

Measured 2026-09-09, in the copy of QSP the demo is actually built from and not only in the
checkout. `SasNetwork.cpp` calls `sasSetImmediateModeMeter(fSasHandle, eMeterMode::FULL_MANUAL)` at
startup and the library writes that to the machine's memory.

It matters because the library reads a meter two ways. Its shared single-meter helper divides a
meter it does not call multigame by the accounting denomination, **in the two default meter modes
only**; every other handler writes the meter as it stands. In full manual it divides nothing, so on
this machine long poll `$35` and long poll `$1E` answer the same number for the same note at any
denomination - and a host that reads a bill count from either gets the same answer.

**On a platform that leaves the library in a default mode they differ**, and a number of notes is
then divided by a currency unit, which converts nothing: one note on a nickel machine reads as
nought. Produced on a wire against the software machine and recorded in `docs/M4-meter-index.md`.
Every meter name read from a command that scales it is marked, and `sasctl catalogue
readable-meters` prints the mark.

### Other behaviours the orchestrator must reproduce
* **Buttons are heartbeat-clocked**, not wall-clock: set the bit, wait `heartbeat+2`, clear,
  wait `heartbeat+4` (one 125 ms EGM poll gives one rising + one falling edge,
  `Main.cpp:174-186`, `libqpm.cpp:715-722`). **If the EGM isn't running, buttons are inert.**
* The EGM **zeroes all 1024 bytes at startup** (`libqpm.cpp:557`), and `persist=true` means
  stale state survives reboots. Doors already open at boot take the `eStartupIntrusion*`
  path → `NoActivity` → **no SAS exception**. Sequence: start EGM → wait for heartbeat →
  zero → inject.
* Buttons are swallowed while an inhibiting condition (an open door) is active
  (`ButtonHandler.cpp` `AreInhibitingConditionsActive()`).
* `qpm_simulator` **cannot run headless**: unconditional `glfwInit()`, OpenGL 3.3 Core,
  `assert(fWindow)`, and a missing `qpm_simulator_config.json` pops a **blocking modal**.
  It has no command-line options at all. Needs `qpm_simulator_config.json` + `gentium.ttf`
  beside the exe (resolved via `GetModuleFileNameA`, not CWD).
* **QSP pins `qpm_simulator` v2.0.0.4 by FetchContent `GIT_TAG`** — any change needs a
  tagged release plus a QSP bump.

---

## 4. Other gates on the primary objective

* **NVRAM holds the config that matters.** ✅ solved 2026-09-06, and the shape of the solution
  matters more than the fact of it. SAS poll address, AFT asset number, feature flags,
  **printer protocol**, **bill-validator protocol** and the **cashout validation style** all live
  in NVRAM, and the only shipped way to set them is the platform's audit-menu framebuffer, which
  needs a person clicking. On fresh NVRAM `mPrinterProtocol = 0`, so no ticket can print and no
  note can be accepted; and `validation_type = 0`, so a real machine returns every ticket.

  Two fixtures are committed, and there are two because a machine performs one validation style
  at a time: `fixtures/nvram/golden.nvram.gz` (standard, which chapter 10.1 is written against)
  and `fixtures/nvram/secure-enhanced.nvram.gz` (the style redemption needs). The second was made
  from the first by rewriting four bytes rather than by walking the menu a second time, which is
  possible because the memory's own integrity rule is simple enough to maintain from outside — see
  `docs/M3-golden-memory.md` for the layout, the rule, the guards, and what was measured on a real
  machine. `qspSetNetworkConfiguration` is still exported and still never called by the demo.

  **The trap that comes with it:** the memory records the arrangement its records were laid out in,
  and a machine master-resets any memory not written in the arrangement it was built for. The
  committed fixtures are arrangement 7, which is the QSP the M0 preset fetches; the QSP checkout at
  `C:\Progetti\QSP` is arrangement 6 and the two are not interchangeable in either direction.
  Rebuilding against a QSP whose arrangement differs means remaking the fixtures by hand.
* **Ticket-in / redeem cannot be stimulated today**: the HAL QPM never emits a ticket-pending
  event (`fPendingTicket` is only ever false; `bna_mask` carries notes only). Needs new code
  in qpm's HAL or an injected `qspTestTicketIn`.
* **A simulated print never completes, and one cashout then stops the machine taking money.**
  ✅ fixed 2026-09-06 in the substituted HAL. The platform clears its in-flight print only on
  `D2C_PRINTER_PRINT_COMPLETE` (`QSP/src/LibPlatform/PrinterHandler.cpp:644-653`); on success the
  print call itself clears nothing. QPM2's simulated printer returns success and raises no such
  event, so the machine believes the printer is still busy for ever. **Measured, not argued:** the
  first cashout printed correctly, and every note offered afterwards was refused — the bill meters
  did not move and no `$4A` arrived — until the event was delivered. There is a watchdog
  (`PrinterStatusGating.h:119`) that releases the busy mark, but the queued print-complete callback
  is still never called, so the machine does not recover on its own.
* **Game outcomes are nondeterministic**: `QSP_DemoOpenGL/src/GameApplication/Rng.cpp` is
  seeded by `std::random_device` and advanced by a free-running background thread.
* **`KeyboardIntrusion` preset appears stale** — missing `DemoApplication::FrameResized`,
  and `default: assert(0);` with no case for three newer QSP callbacks. Expect a link failure.
* **The libsas revision is pinned, and the pin lives here** — ✅ 2026-09-28. The earlier note on
  this line was stale: `QSP/CMakeLists.txt` names the tag `v5.4.4.57`, not a branch. The pin this
  project measures against is now recorded in `fixtures/pinned-versions.json` and held by
  `FETCHCONTENT_SOURCE_DIR_SAS` in the M0 preset, because the declaration in QSP is not ours to
  edit (D30). Check it with `git -C C:/Progetti/libsas rev-parse HEAD` before a validation run;
  a moved checkout changes EGM behaviour under a passing suite.

### The suite was intermittently flaky under load — ✅ fixed 2026-09-07

Two one-behind defects were found and fixed before M3 (`docs/one-behind.md`, D33), and they were
real. They were not all of it. Running all four test projects at once still fails one
timing-sensitive engine test in some runs:

| run | what failed |
|---|---|
| 1 | one meter-index test: several meters answered `$00`, "neither an acknowledgement nor a rejection" |
| 2 | three tests in the same project, same shape |
| 3 (**with the extended-log work reverted**) | a different test, in a different project: an acknowledgement mistaken for a payload |
| 4, 5 | 544 tests, all green |

**Three of five runs, and it happens with the wave's changes reverted**, so it is not from the
extended log. Every one of these tests passes on its own. The shape is the same in each case: a
reply arriving under the wrong command's name when the machine is slow to answer because the machine
is competing for a core with three other test processes.

**What this costs.** Exactly what D33 said it costs: a suite that fails one run in three cannot tell
you whether the next milestone works. It is worth chasing before M4 widens the surface, and the
measurement above is the starting point — the failures are reproducible by running
`dotnet test SasWorkbench.slnx` a few times, and they are not reproducible per project.

**Chased, and it was a third real defect of the same family.** See `docs/one-behind.md`. Short
version: draining the line before a poll goes out cannot catch an answer that arrives after the
drain, and the engine attributed a message to a transaction by *when it arrived*. A message is now
attributed by what it says — a reply echoes the command it answers — or it is discarded, counted and
recorded, and the listening continues within the same window. The same fault ran the other way and
was worse: a stale long poll reply in a general poll's window had its first byte read as an
exception code, inventing a machine event.

**What was left after that was not a defect.** Both software machines waited for a *timed* silence
before answering, so their turnaround was ten milliseconds plus whatever a loaded scheduler added,
against a host that waits two hundred. They now answer as soon as they hold the message. The
fixtures that are not about the host's patience were given a patient host; the ones that are about a
clock keep their own.

**Rates, at stated loads — and the load is the variable, not the suite.** Four assemblies at once on
an idle box was 0 in 8 before any of this work and 4 in 10 when the box happened to be busy, so any
claim about this suite's flakiness has to name the load it was measured at.

| | before | after |
|---|---|---|
| four assemblies, idle box | 4 of 10 failed (box busy) · 0 of 8 (box idle) | **0 of 10 failed** |
| four assemblies + eight spinner processes | 7 of 8 failed | **1 of 8 failed** |

The one remaining failure at eight spinners is honest: *the poll engine did not reach this command
in time*, after three seconds of waiting for a thread on a box with twelve runnable processes. That
is the scheduler, and it says so. **Nothing reports one command's answer under another command's
name any more, at any load** — which is the property the suite needed before M4 widened anything.

---

## 5. Useful levers that already exist

* **Long poll `AA` (auto rebet)** already makes the demo play rounds by itself — a real
  automation lever, driven from the SAS side.
* **`lamps[32]`** is a read-back channel into EGM intent, so the orchestrator can observe
  what the EGM is trying to do — **but not the light towers, measured 2026-09-08.** With a
  lockup active and the platform tracing that it commanded the fault candle to blink, all
  thirty-two lamp words read zero, from the raw bytes and over ten samples. See Trap D: it
  means an unattended run cannot see that a machine is locked up.
* **`sasSetSerialCallbacks()`** is a public libsas export, and
  `libsas/test/blackbox/sas_blackbox.h` already runs libsas as a complete software EGM over
  an in-memory 9-bit mock bus — in-process only today.
* **`SAS_CHANNELS_SUPPORTED` = 4** channels, each with its own serial handle, exception
  buffer and link state (`libsas.h:2265-2277`, `libsas_db.h:1152`).

---

## 6. Serial reality (deferred, but do not lose)

* .NET **can** do the wakeup bit on Windows: `Parity.Mark`/`Parity.Space` map to
  `DCB.Parity = 3/4`; switching on an open port costs ~38 µs; `BaseStream.Flush()` maps to
  `FlushFileBuffers`, which for a comm handle returns only once pending writes are
  transmitted. **That drain is exactly what `sastest.exe` omits** — its `Wakeup Delay = 2`
  is a `Sleep(2)` where the drain belongs.
* **COM10+ works** if you pass the plain name `"COM12"`; `SerialPort` builds `\\?\COM12`
  itself and *rejects* hand-built `\\.\COM12`.
* **Linux: `System.IO.Ports` silently disables parity** for Mark/Space (`pal_termios.c`
  `default:` branch) — no exception, no warning. The wakeup bit would just never be sent.
  **Use native `termios`+`CMSPAR` or refuse. Never no-op.**
* Confidence, honestly stated: naive per-byte switching over USB breaks ≈90%;
  drain-then-switch fixes it ≈75%; **not measured — no adapter was attached.**
* Contingency ladder: QXCOM true 9-bit (`IOCTL_COM_EXTEND_DATABITS`) → genuine FTDI with
  `LatencyTimer=1` → FTDI D2XX → wakeup-less mode (EGM tolerance UNKNOWN) → honest labelling.

---

## 7. Why the legacy tools fail (settled, for context)

**`sastest.exe`** — IGT "SAS Simulator" v2.0.2.1, Borland C++Builder, 2005. Opens ports from
a hard-coded 7-entry table `"COM1:".."COM8:"` at VA `0x004B81FC`, indexed 1-based and
unchecked. `\\.\` appears **zero times** in the 985 KB binary, so COM10+ is structurally
unreachable — and the user's `Port = 11` reads past the end of that array. A source typo makes
entry 4 the single string `"COM4:,COM5:"`. **No automation entry point exists**: no argv, no
switches, no usage text. Its `[Script]` section is `<hex frame>[ = label]` with no loops,
waits, assertions, variables or verdicts. Its own help file says: *"a prototype test
program… never meant for release… There are going to be errors."*

**GSA SAS Host Simulator v1.1** — MFC GUI (2006) that does **not** speak SAS at all. The
protocol is spoken by `SAS Engine/gsat.exe`, a DJGPP DOS binary driving UARTs at I/O-port
level with its own IRQ handlers, over a *second* serial link using a private "GEP"
(GUI-Engine Protocol). **That is why DOSBox and the COM-pair PowerShell scripts exist, and
why it is unfixable.** Also no automation entry point. `SASHostSim.RPT` is an
`ACCESS_VIOLATION` crash dump, not a report.

**The traces are filtered, and the session log holds no replies** — measured 2026-09-09 while
building the replay, and both matter to anyone reading that corpus. The `.trc` tracer writes only
the polls its trace filters name, so a file holds machine answers to polls that are not in it:
`10.1/10.1.3/step10.trc` carries thirteen such answers, and the blocks it is written in are
separated by a rule with the first message of each carrying no timestamp. So **silence in a trace
is not a machine having answered nothing**, and neither is an unanswered poll — the traces also
poll a machine at `$7F` that never answers alongside the one at `$01` that does. The binary
session log is worse for this purpose: every machine message in it is exactly one byte, so it holds
no data reply at all and is **useless for replay**, the way the 39 MB legacy capture turned out to
be useless for breadth. The `.trc` traces and our own runs are the only recordings here a replay
can use.

**But GSA's assets are excellent and importable:** `SASTemplates.dtd` + `PollTemplates.xml`
+ `ResponseTemplates.xml` + `EventTemplates.xml` define **369 messages with zero
per-message code**, covering **119/120 long polls and 116/122 exceptions** vs libsas. Its
weakness — and our opportunity — is that it is a **display model, not an assertion model**:
no arrays, no conditional predicates, no byte-order attribute, no units, no constraints,
and four bespoke element types that exist only because those gaps could not be closed
generically.
