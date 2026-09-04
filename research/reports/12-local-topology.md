# 12 - All-local, unattended test topology

Every claim below is anchored to a file+line I read or a command I ran. Where I could not
establish something, it says UNKNOWN.

---

## 0. The target picture, corrected

The picture in the brief is achievable, but **not with any build configuration that exists today**.
The blocking fact is a single CMake switch:

```
QSP/CMakeLists.txt:41    if(HAL)  ->  SIMULATE_NVRAM=ON, SIMULATE_QXCOM=ON, SKIP_INTEGRITY_CHECKS
QSP/CMakeLists.txt:87    if(HAL)  ->  fetch qpm_simulator            (peripherals over shared memory)
libsas/CMakeLists.txt:67 if(NOT HAL) SRC_FILES=full  else SRC_FILES=src/hal/libsas.cpp   (STUB)
QSP/src/LibMarketSas/LibMarketSas.cmake:27  if(NOT HAL) link qxcom_static  else define HAL_MODE
QSP/src/LibMarketSas/SasNetwork.cpp:259     #ifndef HAL_MODE  ... sasSetSerialCallbacks ...
```

So:

| build | peripherals | SAS |
|---|---|---|
| `HAL=OFF` (Debug/Release preset) | real Quixant hardware required | real libsas, but `sasInit` calls `validateQuixantPlatform()` which opens `\\.\QX10` (libsas/src/libsas_windows.cpp:78) and fails on a laptop |
| `HAL=ON`  (HALDebug/HALRelease preset) | qpm_simulator over shared memory - works today | **libsas is a stub**: `src/hal/libsas.cpp:21 void sasSetSerialCallbacks(void*) { }`, `:25 sasInit(...) {*sasHandlePtr=(void*)0x01; return QXT_LIB_SUCCESS;}` - there is no wire at all |

The corrected picture, and what has to be built to get there:

```
  [ sasctl / SasWorkbench ]                          [ OpenGL_Game.exe  (QSP_DemoOpenGL) ]
   SAS HOST                                            +-- libmarketsas (SasNetwork.cpp)
   9-bit symbols as 16-bit words     <== TCP ==>        +-- libsas  (FULL sources, VALIDATION_DEBUG)
                                       (NEW)            +-- libqxcom-shaped TCP shim  (NEW)
        ^                                               +-- qpm (HAL shim)  ---+
        |                                                                      | file-backed
        |                                                                      | shared memory
        +----- orchestrator ------------------------------------------------->[ C:\ProgramData\shared_memory\
                (writes shared memory directly, in place of qpm_simulator.exe)   lsm_QpmSharedMemory.shm ]
```

Two peers, both driven by one orchestrator, no gaming hardware. The orchestrator replaces
`qpm_simulator.exe` rather than running beside it (reason in section 2).

---

## 1. The SAS link

### 1.1 What libsas actually asks of a transport

`libsas/include/libsas_db.h:488-508` defines `SAS_SERIAL_CALLBACKS`: 16 function pointers plus a
`callbacksVersion`. The 9-bit path is:

```c
typedef unsigned int(*readExtended_callback)(long long handle, unsigned short* buffer,
        unsigned int length, unsigned long long* timestamps, unsigned int* dataRead);
typedef unsigned int(*writeExtended_callback)(long long handle, const unsigned short* buffer,
        unsigned int length, unsigned int* dataWritten);
```

`libsas/include/libsas_wrapper.h:225-228`:

```c
SAS_API void sasSetSerialCallbacks(void* serialCallbacks);
```

which is a plain field-by-field copy into `sasContext.serialCallbacks` and sets
`sasContext.usingLegacySerialLibrary = FALSE` (`libsas/src/libsas_wrapper.cpp:897-919`).

Only these callbacks are exercised on the hot path. From `libsas/src/libsas_uart.cpp`:

* `openSasSerialDevice()` (line 107) calls, in order, `open(deviceName, &handle)`,
  `setState(handle, 19200, 9, UART_PAR_NONE, UART_STOP_ONE, FALSE)`,
  `setTimeouts(handle, 9, 5000, 1500)`. **The device-name string is passed through verbatim.**
* `readSasSerial()` (line 273) uses only `readExtended`, one unit at a time for the address byte,
  then in blocks. Bit 8 (`0x100`) is the wakeup bit; a non-first byte carrying it means "drop the
  message" (lines 214, 245). An empty read is legal - libsas spins until a 9 ms inter-byte deadline
  (`SAS_INTER_BYTE_DEADLINE`, line 205).
* `writeSasSerial()` (line 613) uses only `writeExtended`, one call per packet (chunking is disabled,
  `chunk_size = 0`, line 683). The wakeup bit is set on byte 0 only for the loop-break chirp
  (line 660).
* `flushRx`/`flushTx`/`close`/`cancelRx` are called on shutdown paths.
  `read`/`write` (8-bit) and `getVersion`/`getTimestamp` may be null - the black-box harness sets
  `cb.read = nullptr; cb.write = nullptr;` (`libsas/test/blackbox/sas_blackbox.h:212,215`).

**The reference implementation already exists**: `libsas/test/blackbox/sas_blackbox.h` is a complete,
working 9-bit transport (`egmOpen` .. `egmCancelRx`, `installEgmSerialCallbacks()` at line 200)
backed by two in-memory queues. Replacing those two queues with a TCP socket is the whole job.
Note the convention at line 187: `egmReadExtended` returns `0x80000000` when nothing was read - a
non-zero result on an empty read is what keeps libsas polling.

### 1.2 Where QSP wraps it, exactly

`QSP/src/LibMarketSas/SasNetwork.cpp:257-266` - this is the precise call site:

```cpp
#ifndef HAL_MODE
    #pragma message("SAS Serial callbacks enabled")
    QUIXANT_SERIAL_CALLBACKS serialCallbacks = {0};
    serialCallbacks.callbacksVersion = CALLBACKS_VERSION;
    assignCallbacks((void*)&serialCallbacks);        // <-- filled in by libqxcom
    sasSetSerialCallbacks((void*)&serialCallbacks);
#else
    #pragma message("SAS Serial callbacks disabled")
#endif
```

and 70 lines later, `SasNetwork.cpp:336`:

```cpp
res = sasInit((void **) &fSasHandle,
              json_handler->GetSerialPort(JsonHandler::SerialPort::eNetwork1),
              uint8_t(fDramPollAddress[0]), enabled_features, &SasNetwork::LibSasCallback);
```

(second channel: `SasNetwork.cpp:528`, `sasAddSasChannel(..., eNetwork2, ...)`).

`QUIXANT_SERIAL_CALLBACKS` (`libqxcom/include/libqxcom_callbacks.h:60-79`) is **field-for-field
identical** to libsas's `SAS_SERIAL_CALLBACKS`, and `assignCallbacks(void*)` is the single exported
entry point (line 81). QSP calls nothing else from qxcom in this file - I grepped: the only four
hits are lines 261-264.

`GetSerialPort` (`QSP/src/LibPlatform/JsonHandler.cpp:1264-1272`) returns
`fSerialPorts[port].c_str()`, parsed at `JsonHandler.cpp:252-275` from the market JSON:

```json
"Serial Ports": { "Bill Validator": "/dev/ttyS3", "Printer": "/dev/ttyS4",
                  "Network 1": "/dev/ttyS5", "Network 2": "/dev/ttyS2" }
```
(`QSP_DemoOpenGL/bin/HALDebug/x64/Resources/configuration.json:92-102`)

It is validated only as "a non-empty string". **It is therefore a free-form runtime knob**:
`"Network 1": "tcp://127.0.0.1:5010"` reaches the shim's `open()` callback unchanged.

### 1.3 Answer: build-time choice + a new shim, zero source edits to libsas or QSP

The clean route is **link-time substitution of libqxcom**, not a code change:

1. **New static library `qxcom_tcp`** (in the SasWorkbench repo, not in QSP or libsas). It exports
   one symbol with the existing signature:
   `QUIXANT_EXPORT void assignCallbacks(void *callbacksStruct);`
   It fills the struct with 16 TCP-backed callbacks modelled directly on
   `libsas/test/blackbox/sas_blackbox.h:138-217`. `open()` parses the device string; anything
   matching `tcp://host:port` connects a socket, anything else can fall through to the real qxcom
   (or fail). Each `unsigned short` goes on the wire as one 16-bit little-endian word, so bit 8
   (wakeup) survives - the whole point of `readExtended`/`writeExtended`.

2. **Header availability is already solved.** `QSP/CMakeLists.txt:50-52` adds
   `simulator/include/qxcom` to the include path whenever `SIMULATE_QXCOM` is on (which `HAL=ON`
   forces, line 42). `QSP/simulator/include/qxcom/libqxcom_callbacks.h` is byte-identical to the
   real one (I diffed them) and declares `assignCallbacks` at line 81. So
   `SasNetwork.cpp`'s `#include "libqxcom.h"` / `"libqxcom_callbacks.h"` compile fine in a HAL
   configuration.

3. **libsas must be the real one, without the hardware check.** libsas already builds exactly that
   target: `libsas/CMakeLists.txt:197-207`

   ```cmake
   add_library(${PROJECT_NAME}_static_testable STATIC ${FULL_SRC_FILES})
   target_compile_definitions(${PROJECT_NAME}_static_testable PRIVATE
           VALIDATION_DEBUG
           $<$<NOT:$<PLATFORM_ID:Windows>>:_DEBUG>)
   ```

   with the comment "This lets the black-box tests call `sasInit` on a non-Quixant CI runner
   WITHOUT modifying any library source file... It is built from the FULL sources regardless of the
   HAL flag". `VALIDATION_DEBUG` is what compiles out the `\\.\QX10` open in
   `libsas/src/libsas_windows.cpp:76-81`. It is built only when `BUILD_TESTING` is ON
   (`libsas/CMakeLists.txt:185`).

4. **The only real edit** is ~8 lines of CMake in `QSP/src/LibMarketSas/LibMarketSas.cmake`
   (lines 20-34), behind a new option, e.g. `-DSAS_TCP_HARNESS=ON`:

   ```cmake
   # today
   target_link_libraries(${MARKET_SAS} PRIVATE sas_static ...)
   if(NOT HAL)
       target_link_libraries(${MARKET_SAS} PRIVATE qxcom_static)
   else()
       target_compile_definitions(${MARKET_SAS} PRIVATE HAL_MODE)   # <-- kills the wire
   endif()
   ```
   becomes: link `sas_static_testable` and `qxcom_tcp`, and do **not** define `HAL_MODE`.
   `SasNetwork.cpp` is untouched; `libsas` is untouched.

   There is precedent for adding a translation unit to a QSP target from outside the QSP tree
   without editing QSP sources - `QSP_DemoOpenGL/src_keyboard_intrusion/keyboard_intrusion.cmake`
   does exactly that with `target_sources(qsp PRIVATE ...)` and says so in its header comment.

   There is also an unfinished hook that this fills: preset `CustomSimulation`
   (`QSP_DemoOpenGL/CMakePresets.json:69-78`) sets `SIMULATE_QXCOM=ON`, but **no qxcom simulator
   implementation exists anywhere in the tree** - only headers. The extension point was designed
   and left empty.

5. **Config change, no rebuild**: `"Network 1": "tcp://127.0.0.1:5010"` in
   `Resources/configuration.json`.

**Summary answer to the question asked:** it is a *build-time* choice of which library provides
`assignCallbacks` plus a *runtime* choice of device string. QSP does hide the callbacks behind
`HAL_MODE`, and that `#ifndef` at `SasNetwork.cpp:259` is the one thing that has to change state
(via the CMake option, not by editing the file).

### 1.4 A second, worse option, for completeness

Rather than a shim, the host could be put on the other side of a real serial pair (com0com null
modem). This does **not** work: com0com does not carry a 9th data bit / mark-space parity, and
`openSasSerialDevice` asks for `dataBits = 9` (`libsas_uart.cpp:124`). The wakeup bit is the SAS
address delimiter. Rejected.

---

## 2. The stimulus link (summary; a peer agent has the detail)

**Verified facts:**

* The segment is `QpmSharedMemory`, 1024 bytes, wrapped identically in two places:
  `qpm_simulator/include/QpmSharedMemory.h` and `QPM2/src/hal/QpmSharedMemory.h`. Both construct
  `lsm::Memory("QpmSharedMemory", 1024, /*persist=*/true)` and both do `open()` then `create()`,
  so start order does not matter.
* Layout (`qpm_simulator/src/Main.cpp:25-33`, identical at `QPM2/src/hal/libqpm.cpp:153-160`):
  `uint32_t hearbeat; intrusion_mask; dio_mask; lamps[32]; bna_mask; printer_mask;` = 4+4+4+128+4+4
  = 148 bytes at offset 0. (`hearbeat` is spelled that way in both.)
* **On Windows `persist=true` means it is a FILE, not a named mapping.** libsharedmemory v1.8.0
  (pinned in `QPM2/CMakeLists.txt:277` and `qpm_simulator/CMakeLists.txt:98`) builds the path
  `%PROGRAMDATA%\shared_memory\lsm_<name>.shm` and maps it with `CreateFileMappingA(fileHandle,
  ..., NULL /* unnamed */)`. I confirmed the file on this machine:
  `C:\ProgramData\shared_memory\lsm_QpmSharedMemory.shm`, exactly 1024 bytes.
  So a third process attaches with `MemoryMappedFile.CreateFromFile(...)` on that path - **not**
  `OpenExisting` by name. On Linux the same version uses `shm_open("/QpmSharedMemory")`.
* Direction of travel: the **game process** (QPM HAL) writes `hearbeat` (`libqpm.cpp:722`,
  `++state->hearbeat` every 125 ms) and `lamps[]` (`libqpm.cpp:682`); the **simulator** writes
  `intrusion_mask`, `dio_mask`, `bna_mask`, `printer_mask`. QPM edge-detects with XOR against its
  own previous copy and fires `D2C_DOOR_OPEN/CLOSED`, `D2C_GPIO_RISING_EDGE/FALLING_EDGE`, BV and
  printer callbacks (`libqpm.cpp:724-900`).

**Orchestrator options:**

1. **Replace qpm_simulator (recommended).** The orchestrator maps the .shm file itself and drives
   the four input masks. Do **not** run `qpm_simulator.exe` at the same time: its
   `ProcessSharedMemory()` (`Main.cpp:147-194`) unconditionally does `state->dio_mask = 0;` then
   rebuilds it from its own checkbox array every rendered frame, so a third party's bits are wiped
   within ~16 ms. The two writers fight.
2. **Drive qpm_simulator's GUI** with UI automation. Fragile, needs a desktop, no.
3. **Momentary presses**: qpm_simulator models them with a heartbeat-synchronised queue
   (`Main.cpp:181-193`). An orchestrator that owns the segment can do it more simply: set the bit,
   wait for `hearbeat` to advance at least twice (>=250 ms), clear the bit. QPM samples at 125 ms,
   so anything shorter can be missed.

Mapping needed for the worked example, from `Resources/game_configuration.json:71-86` and
`Resources/configuration.json:36-70`:
`dio_mask` bit 0 = Collect, bit 2 = Service, bit 3 = Reset/JKey keyswitch, bit 4 = Play,
bit 5 = Audit keyswitch, bits 7-11 = Bet x1..x5.
`intrusion_mask` bit 2 = main door, 4 = logic, 5 = stacker.
`bna_mask` bits 0-6 = note denominations, bits 8-11 = BV states (Disconnected/Jam/StackerFull/
StackerRemoved, `Main.cpp:39`), `printer_mask` bits 0-3 = Disconnected/Jam/PaperOut/PaperLow
(`Main.cpp:40`, consumed at `libqpm.cpp:841-900`).

---

## 3. Determinism - what a re-runnable regression suite has to control

### 3.1 The one lever that fixes most of it: a golden NVRAM image

The NVRAM simulator (`QSP/simulator/nvram/libnvram.cpp:17,44-57`) stores everything in a single
8 MB file: `C:\ProgramData\quixant\launchpad\nvram.bin` (confirmed present, 8388608 bytes).
`GetSystemStorageDirectory()` is `QSP/simulator/include/utils.h:191-216`.
Writes are `fflush`ed on every block (`libnvram.cpp:246`), so it is durable per-write.
A missing or wrong-sized file is recreated filled with `0xdeadbeef` (`libnvram.cpp:77-79`), which
forces a master reset on the next start.

**Restoring a byte-for-byte snapshot before each test resets, in one action:**
all Appendix C-7 meters, the ticket-out log ring and `mTicketNumber`
(`SasNetwork_Ticket.hpp:597-601`), the AFT transaction history, the exception/event buffers, the
SAS configuration (poll address, validation style, printer protocol), and the game RNG state word
at vendor offset `4096-8` (`QSP_DemoOpenGL/src/GameApplication/Rng.cpp:19`).

That is the single most important thing to build, and it is cheap.

### 3.2 Source-by-source

| source | where | control |
|---|---|---|
| **SAS meters accumulate** | all of Appendix C-7; `AsyncPrintComplete` bumps 12 of them, `SasNetwork_Ticket.hpp:790-801` | Restore golden `nvram.bin` -> absolute assertions become valid. Belt and braces: assert on **deltas** (read LP 6F / AF before and after the stimulus). Do both; the delta form survives a stale image. |
| **Ticket number** | `mTicketNumber`, `++` then wrap at 10000, `SasNetwork_Ticket.hpp:597-601` | In the golden image. Deterministic once restored. |
| **Standard validation number** | `internalCalculateStandardValidationId`, `libsas/src/libsas_internal.cpp:5112-5145`: BCD add of `ticketAmount` and **BCD hh:mm:ss** from `internalGetLocalTimeBcd`, then base-16-as-BCD, `% 1e8` | **Wall-clock dependent.** Either (a) do not assert the value, only that LP 3D's 4 BCD bytes equal the digits printed by the QPM HAL printer stub (`libqpm.cpp:1639`) - a real conformance property; or (b) pin the clock for the run. |
| **Secure-enhanced validation number** | `internalCalculateSecureEnhancedValidationId`, same file ~5150 | Sequence-based, seeded in libsas NVRAM. Deterministic after a golden restore. Prefer this style for tests that must assert an exact number. |
| **Ticket timestamp** | `ticket_log.mTimestamp = time(nullptr)` (`SasNetwork_Ticket.hpp:586`); printed date/time `PrinterHandler.cpp:922` | Assert a range, not a value, unless the clock is pinned. |
| **AFT transaction ids** | `sAft.transactions[AFT_TRANSACTION_HISTORY]` in libsas NVRAM (`libsas_db.h:465`) | In the golden image. |
| **Game outcome RNG** | `QSP_DemoOpenGL/src/GameApplication/Rng.cpp` | **Irreducibly nondeterministic today.** The primary mt19937_64 is seeded from NVRAM (lines 33-36, deterministic after restore) but is then advanced by `GenerateEntropyAdvanceState()` a count of 13..107 drawn from `fEngineSecondary`, which is seeded from `std::random_device` (line 29). Worse, `RngThread` (lines 81-96) keeps calling `GenerateUint32` on the **primary** engine at 100-200 ms intervals for the life of the process. So the engine's position at spin time depends on wall time and on a fresh entropy source. **A test may not assert a win amount.** Assert instead: games-played meter +N, coin-in meter +bet*N, ordering of RTE game start/end, credit meter conservation. |
| **Wall clock / date rollover** | `time(nullptr)` in QSP, `internalGetLocalTimeBcd` in libsas, plus LP 7E (set date/time) | libsas has a black-box test for this (`bb_systemtime`, `libsas/test/blackbox/CMakeLists.txt:170`). Pin the OS clock, or accept ranges. Never run a suite across local midnight without expecting the "games since door closed"/date-stamped assertions to move. |
| **Shared-memory state leaks between runs** | `lsm_QpmSharedMemory.shm` is `persist=true`, and `Memory::destroy()` only deletes the file when `!_persist` (libsharedmemory.hpp:425-436, 456-464). **The file is never deleted.** | Zero the 1 KB (or delete the file) in setup. Otherwise a door left open or a bill left set by a crashed run is still open in the next one. |
| **QPM key storage** | `C:\ProgramData\quixant\launchpad\qpm_storage.bin`, 512 bytes (`QPM2/src/hal/libqpm.cpp:60,587`) | Snapshot with the NVRAM image. |
| **libsas thread timing** | 9 ms inter-byte deadline (`libsas_uart.cpp:205`), 30 s link-down timer (`bb_linkdown30s`), 800 ms AFT timer (`libsas_db.h:461`) | A TCP loopback bridge adds ~0.1-1 ms; well inside the budget. But a Windows scheduler stall under load can exceed 9 ms and cause a dropped message. Retry-on-drop is legitimate SAS host behaviour; the harness must not treat one dropped poll as a failure. |
| **Poll cadence** | the host is the timing master | Fix the host's poll period per test and record it in the artifact. |

---

## 4. Startup and teardown

### 4.1 Cold start sequence

```
0. Orchestrator
   - kill any OpenGL_Game.exe / qpm_simulator.exe left over
   - restore  C:\ProgramData\quixant\launchpad\nvram.bin        <- golden image
   - restore  C:\ProgramData\quixant\launchpad\qpm_storage.bin  <- golden image
   - zero or delete C:\ProgramData\shared_memory\lsm_QpmSharedMemory.shm
   - delete logsas.txt and logs\ in the game's working directory
   - write Resources\configuration.json with "Network 1": "tcp://127.0.0.1:<port>"

1. Orchestrator opens the shared-memory mapping (creates the 1 KB file if absent), zeroed.
   READY when the mapping exists.

2. SasWorkbench starts LISTENING on tcp 127.0.0.1:<port>. It must listen first: the shim's
   open() is called from inside sasInit and a refused connect fails the whole SAS start
   (SasNetwork.cpp:339-347 raises SasCondition::eSerialPort on QXT_LIB_SAS_COM_OPEN_ERR).
   READY when the socket is bound.

3. Orchestrator launches OpenGL_Game.exe.
   READY signals, best to worst:
     a. the TCP accept fires  -> libsas opened the port, i.e. sasInit got past
        validateQuixantPlatform and openSasSerialDevice.
     b. the EGM answers a General Poll: host sends 0x80 then 0x80|addr, EGM replies with an
        exception code byte or nothing. Answering at all proves the whole stack is up.
        This is the real handshake - use it.
     c. hearbeat in shared memory advances (proves QPM's 125 ms thread is running,
        libqpm.cpp:713-722).
     d. log lines: with -DSAS_LOGGING, libsas writes logsas.txt (SasNetwork.cpp:387).
        QSP traces "AsyncStart() PollAddress#1(%u)" (SasNetwork.cpp:249).
   Use (a)+(c) as liveness and (b) as readiness. Do not parse a GUI.

4. Orchestrator asserts the machine is out of powerup: poll until the exception queue drains
   and LP 1A (send current credits) answers.
```

Note on step 0: after a master reset `PostNvramReset` writes poll address **1**
(`SasNetwork.cpp:1019`) and the demo's market config has `"start network": "immediately"`
(`configuration.json:417`), and `IsSasStartWithheld` returns false for `eImmediately`
(`SasStartupState.h:85-87`). So a memory-cleared machine does come up polling at address 1.
**But** `mPrinterProtocol` defaults to 0 on a fresh NVRAM, and it is only ever written from the
audit-menu configuration path (`NvramHandler.cpp:1285`) - there is no public API for it
(`QSP/include/libqsp.h` exposes `qspSetGameConfiguration`, `qspNvramWriteVendorData`, etc., but
nothing for the hardware/network/printer pages). With `mPrinterProtocol == 0` the machine has no
cash-out device and **test 10.1.3 cannot run at all**. Hence: the golden image is not an
optimisation, it is a prerequisite.

### 4.2 Teardown

```
1. Host stops polling; wait for the 30 s link-down behaviour only if the test needs it.
2. Signal the game to exit cleanly (window close). SasNetwork::AsyncStop calls sasShutdown
   (SasNetwork.cpp:181-186), which closes channels and calls serialCallbacks.close.
3. Orchestrator closes the listener, unmaps shared memory.
4. Collect artifacts: logsas.txt, the host's own wire log, nvram.bin (post-run snapshot),
   the shared-memory image.
```

### 4.3 What is held, and what leaks on a crash

| resource | leaks? |
|---|---|
| TCP listener port | No - OS reclaims. Beware `TIME_WAIT` if the port is reused within ~30 s; bind with SO_REUSEADDR or rotate ports per test. |
| `lsm_QpmSharedMemory.shm` | **Yes, always.** `persist=true` means `destroy()` is never called. Stale door/bill/printer state carries into the next run. Must be cleared in setup. |
| `nvram.bin` | Persists by design, and is `fflush`ed per write, so a killed process leaves a *consistent but mid-transaction* NVRAM: a ticket log in `ePrinting`, a pending collect, held exceptions. Always restore the golden image; never "just rerun". |
| `qpm_storage.bin` | Persists. |
| libsas threads | Internal to the process; die with it. `AsyncRestartLibSas` even sleeps 100 ms because libsas gives no join (`SasNetwork.cpp:204`). |
| `logsas.txt`, `logs\access_audit_*.log` | Append/grow (I found a 3.8 MB one). Rotate or delete per run. |
| GPU/window | `OpenGL_Game.exe` is GLFW+OpenGL and needs a desktop session. Unattended != headless: the laptop must be logged in, or use a session that keeps a desktop. On Linux this needs Xvfb. |

---

## 5. What is missing, and who should build it

Ordered by size. "Biggest gap" is called out.

| # | Missing piece | Size | Best owner |
|---|---|---|---|
| 1 | **`qxcom_tcp` shim**: a static lib exporting `assignCallbacks`, 16 callbacks over TCP, 16-bit LE words. Direct port of `sas_blackbox.h:138-217`. | ~250 lines C++ + ~20 lines CMake. **1-2 days.** | SasWorkbench author (it belongs in the harness repo) |
| 2 | **CMake option in QSP** (`SAS_TCP_HARNESS`) to link `sas_static_testable` + `qxcom_tcp` and suppress `HAL_MODE`. | ~10 lines in `LibMarketSas.cmake`, ~5 in `QSP/CMakeLists.txt`. **half a day**, plus review because it touches a shipped repo. | QSP owner |
| 3 | **SAS host** in SasWorkbench: 9-bit framing, CRC (`sas_blackbox.h:57-70`), general poll / long poll state machine, exception decode, RTE framing, per-LP decoders. | The wire layer is small; the ~200 long polls are the bulk. **Wire+framing 1 week; a useful LP subset (0F-20, 1A, 2F, 3D, 4D, 50, 54-58, 6F, 74, 7B-7D, 8x, AF, B0) 3-4 weeks.** | SasWorkbench author |
| 4 | **`QpmStimulus` module**: map the .shm file, typed accessors for the six fields, `PressButton(bit, ms)` synchronised on `hearbeat`, `SetDoor`, `InsertNote`, `SetPrinterState`. | ~200 lines C#. **2 days.** | SasWorkbench author |
| 5 | **Orchestrator**: process lifecycle, golden-image restore, readiness handshake, correlated timeline of (host wire, EGM `logsas.txt`, shared-memory writes), assertion DSL, artifacts. | **2-3 weeks.** | SasWorkbench author |
| 6 | **A golden NVRAM image + the recipe that produced it** (configure once through the audit menu, snapshot). Plus a documented rebuild procedure, because a QSP NVRAM layout bump invalidates it (`NvramHandler::MigrateLayoutIfRequired`, `NvramHandler.h:55`). | **1 day to make, ongoing to maintain.** | QSP/EGM engineer who knows the audit menu |
| 7 | **A deterministic RNG mode in the demo game.** `Rng.cpp` as written cannot be made reproducible from outside. Options: an env var / vendor-NVRAM flag that (a) seeds `fEngineSecondary` from the same NVRAM word and (b) stops `RngThread`. | ~30 lines in `QSP_DemoOpenGL/src/GameApplication/Rng.cpp`. **1 day** - but it changes a GLI-relevant file, so it must be a test-only build variant, exactly like `KEYBOARD_INTRUSION`. | Demo game owner |
| 8 | **A stimulus for "play 2 games" that is robust.** Today: press Bet, press Play via `dio_mask`, then wait. There is no "game finished" signal outside SAS itself. The RTE game-start/game-end exceptions (or the games-played meter) are the only observable completion, so the orchestrator's EGM-side and host-side views are not independent for this step. | Design work. **UNKNOWN, 3-5 days to prototype.** | SasWorkbench author + demo game owner |

**The biggest gap is #3 - the SAS host itself.** Everything else is plumbing measured in days;
a conformance-grade host that can *assert* per spec (not merely poll) is the multi-week item, and
it is the part with no existing code to port from inside the allowed scope. Second biggest is #7
combined with #8: the EGM under test is a demo game whose outcomes are deliberately
unpredictable, so "play 2 games and assert" is currently only assertable at meter granularity.

---

## 6. Worked example: GSA 10.1.3 "Cashout Ticket 1"

Preconditions: golden NVRAM with poll address 1, `mPrinterProtocol != 0`, validation style
**Secure Enhanced** (chosen so the validation number is sequence-based and assertable - see 3.2),
ticket-out enabled, credit meter 0.

Naming: `SM` = shared memory (`lsm_QpmSharedMemory.shm`), `HOST` = SasWorkbench.

| # | End | Action | Assertion | Status |
|---|---|---|---|---|
| 1 | HOST | Bring the link up: wakeup pulses (`hostWakeup`), then General Polls at a fixed period. | EGM answers `0x00` (no exception) within 3 polls. | OK |
| 2 | HOST | LP `1A` `LP_SENDCURRENTCREDITS` (libsas.h:1588). | credits == 0. Records the baseline. | OK |
| 3 | HOST | LP `6F` `LP_SENDEXTENDEDMETERS_N` (libsas.h:1640) for meter codes `0x000E` `TotalCashableTicketOut_Cents`, `0x0038` `TotalCashableTicketOut_Qty`, `0x0016` `TotalTicketOut_Crd`, `0x002C` `TotalCashableTicketOut_Crd` (all from libsas.h:790-960). | Record M0 for each. | OK |
| 4 | SM | Money in: set `bna_mask` bit for the 1000-cent note, hold >=250 ms, clear. QPM raises the BV callback (`libqpm.cpp:758-773`). | - | OK |
| 5 | HOST | Poll. | EGM reports the configured bill-accepted exception, then LP `1A` reports 1000 cents (units per the accounting denomination, `configuration.json:444-470`). | OK |
| 6 | SM | Press Collect: set `dio_mask` bit **0**, wait 2 heartbeats, clear. QPM emits `D2C_GPIO_RISING_EDGE` arg 0 (`libqpm.cpp:739-756`); the demo maps DIO 0 -> `Button::eCollect` (`game_configuration.json:71`) -> `qspCollectButtonPressed()` (`GameSelect.cpp:120-122`). | - | OK |
| 7 | HOST | Poll. | EGM reports exception `0x3C` **Cashout button pressed** - `SasNetwork.cpp:1429` `SendException(SasExceptionCode::CashoutButtonPressed)`. | OK |
| 8 | EGM (internal) | `StartTicketOut()` (`SasNetwork_Ticket.hpp:564`) allocates the ticket number, builds `sCashoutHandpayTicket` with `validationType = VT_CASHABLE_TICKET_FROM_CASHOUT_OR_WIN` and calls `sasStartCashout(fSasHandle, sas_ticket, &validation_code_temp)` (line 623). Secure-enhanced -> prints immediately (lines 651-663) via `PrinterHandler::AsyncPrintTicket`. | - | OK |
| 9 | EGM stub | `qpmPrinterPrint` (`QPM2/src/hal/libqpm.cpp:1633-1647`) prints to **stdout**: template, `Validation Number: <Barcode>`, `Ticket Number`, `Date/Time`, `Amount`, `Expiration`, and returns `Q_SUCCESS`. | Orchestrator captures the game's stdout and parses these six lines. **This is the EGM-side ground truth for the ticket.** | Works, but **stdout scraping is the only channel**. There is no file, no callback, no shared-memory field for a printed ticket. See "cannot be done today" below. |
| 10 | EGM (internal) | `AsyncPrintComplete(true)` (`SasNetwork_Ticket.hpp:786`) -> `CashTicketOut()`, then `UpdateSasMeter()` x12, then `sasCompleteCashout(fSasHandle)` (line 804). | - | OK |
| 11 | HOST | Poll. | EGM reports exception **`0x3D` `CashoutTicketPrinted`** (`libsas/include/libsas.h:410`; queued from `libsas/src/libsas_pending.cpp:121` / `libsas_threads.cpp:2320`). Assert it arrives **after** `0x3C` and within N polls. | OK |
| 12 | HOST | LP **`3D`** `LP_SENDCASHOUTTICKETINFO` (libsas.h:1613). Response is built at `libsas/src/lp.cpp:2120-2131`: `[addr][3D][validation 4 BCD][amount 5 BCD][crc][crc]`, total 13 bytes. | (a) amount BCD == 1000 (cents); (b) validation BCD == the low 8 digits of the `Validation Number` scraped in step 9. That equality is the actual conformance property - the number on the paper must be the number the cage is told. libsas comments this at `SasNetwork_Ticket.hpp:632-644`. | OK |
| 13 | HOST | LP `4D` `LP_SENDENHANCEDVALIDATIONINFO` (libsas.h:1628) with function code 0 (current). | Returns the enhanced validation record: type `VT_CASHABLE_TICKET_FROM_CASHOUT_OR_WIN`, amount 1000, the same validation number, ticket number == the one scraped in step 9. | OK |
| 14 | HOST | LP `6F` again for the same four meter codes. | `TotalCashableTicketOut_Cents` == M0 + 1000; `TotalCashableTicketOut_Qty` == M0 + 1; `TotalTicketOut_Crd` and `TotalCashableTicketOut_Crd` incremented by 1000/denomination. Assert **deltas**, and (with the golden image) also absolutes. | OK |
| 15 | HOST | LP `1A`. | credits == 0. | OK |
| 16 | HOST | LP `50` `LP_SENDVALIDATIONMETERES` (libsas.h:1630) for the cashable-ticket-from-cashout type. | count +1, cumulative amount +1000. | OK |
| 17 | Orchestrator | Correlate: `logsas.txt` (EGM's own view of the same wire, enabled by `-DSAS_LOGGING`, `SasNetwork.cpp:387`) against the host's log. | Byte-identical frame sequence. Catches transport bugs in the new shim rather than blaming the EGM. | OK once #1 exists |

### Steps that cannot be done today, and why

* **Step 9 has no machine-readable channel.** The HAL printer is `printf` to stdout
  (`QPM2/src/hal/libqpm.cpp:1636-1645`). The orchestrator must capture and parse the child
  process's stdout, which is brittle (interleaved with QSP traces) and impossible if the game is
  launched in a way that discards stdout. **Wanted:** the HAL printer stub should also write a
  JSON line per ticket to a file, or a ticket record into the shared segment. ~20 lines in
  `libqpm.cpp`, owned by the QPM2 owner. Until then this step is "parse stdout, best effort".
* **Every step is blocked on section 5 items #1 and #2** - with today's `HAL=ON` build,
  `sasInit` is `{*sasHandlePtr=(void*)0x01; return QXT_LIB_SUCCESS;}` and steps 1-3 and 7-16
  do not exist. This is the whole test, blocked on one CMake option and one shim.
* **Step 4/6 assume the orchestrator owns the shared segment.** If `qpm_simulator.exe` is also
  running, its per-frame `dio_mask = 0` wipes the press (`qpm_simulator/src/Main.cpp:154-159`).
* **Step 12's assertion is only strong under Secure Enhanced validation.** Under Standard
  validation the number is `f(amount, hh:mm:ss)` (`libsas_internal.cpp:5112-5145`), so the host
  cannot predict it; the equality against the printed number still holds and is still worth
  asserting, but the value is not reproducible across runs.
* **A "play 2 games" precondition** (which 10.1.x variants often carry) cannot be asserted on
  outcome, only on meters - see section 5 item #7.

---

## 7. Open questions / UNKNOWN

* Whether `sas_static_testable` links cleanly into `libmarketsas` (it is declared inside
  `if(BUILD_TESTING)` and is not installed; `libsas/CMakeLists.txt:185-207`). Needs a trial
  configure. UNKNOWN.
* Whether the `HAL=ON` forced `SKIP_INTEGRITY_CHECKS` (`QSP/CMakeLists.txt:44`) interacts with
  anything SAS asserts on (e.g. LP `21` ROM signature, `GetRomSignatureTargets`,
  `JsonHandler.h`). Not investigated. UNKNOWN.
* Whether the shipped `qpm.dll` in `QSP_DemoOpenGL/bin/HALDebug/x64/` was built against
  libsharedmemory v1.8.0 (file-backed) or an earlier named-mapping version. The `.shm` file on
  this machine is dated Feb 2026, so at least one build used the file-backed form. Verify before
  writing the stimulus module. UNKNOWN.
* The exact bill-accepted exception code the demo reports for a 1000-cent note (step 5) - depends
  on country code / denomination table in the market config. Not chased. UNKNOWN.
* Whether the `dist/` prebuilt `R_APP_OPENGL_GAME_WIN_2.0.4.3` can be reused, or the harness must
  build QSP_DemoOpenGL from source (it must, for section 5 items #1/#2). Assumed: build from
  source.
