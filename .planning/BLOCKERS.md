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
* **libsas is pinned to a branch, not a tag** (`IGS-226-accounting-denomination`,
  `QSP/CMakeLists.txt:126`), so EGM behaviour can change under a passing suite. Record
  versions in every run record.

---

## 5. Useful levers that already exist

* **Long poll `AA` (auto rebet)** already makes the demo play rounds by itself — a real
  automation lever, driven from the SAS side.
* **`lamps[32]`** is a read-back channel into EGM intent, so the orchestrator can observe
  what the EGM is trying to do.
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

**But GSA's assets are excellent and importable:** `SASTemplates.dtd` + `PollTemplates.xml`
+ `ResponseTemplates.xml` + `EventTemplates.xml` define **369 messages with zero
per-message code**, covering **119/120 long polls and 116/122 exceptions** vs libsas. Its
weakness — and our opportunity — is that it is a **display model, not an assertion model**:
no arrays, no conditional predicates, no byte-order attribute, no units, no constraints,
and four bespoke element types that exist only because those gaps could not be closed
generically.
