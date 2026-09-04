# libsas — repository map and host-side prior-art audit

Research target: `C:\Progetti\libsas`
Date of survey: 2026-09-03
Git: branch `IGS-226-accounting-denomination`, HEAD `1f41480` ("Give a HAL build every entry point the header declares"), working tree clean.
CodeGraph: **absent** — `C:\Progetti\libsas\.codegraph` does not exist, so everything below comes from reading files directly.

Every claim is anchored to a file+line or a command that was run. Anything not established is marked **UNKNOWN**.

---

## 0. One-paragraph orientation

`libsas` is Quixant's **EGM-side** implementation of the SAS 6.02 protocol: it is the code that runs *inside* a gaming machine and answers a casino host's polls. It ships as both a SHARED and a STATIC library with a stable `extern "C"` ABI. The repo contains **four host-side utilities** under `utils/`, of which three were written in-house (`sashost`, `poller`, `hwconformance`) and one is a third-party legacy binary (`igt/sastest.exe`). `utils/sashost` is by far the closest prior art to a new host tool — it is a real SAS host with a scenario scripting language, expectations, exit codes and an ImGui front end — but its wire surface is only five operations (general poll, sync poll, LP 1F, LP 4C, LP 80 progressive broadcast) out of the ~124 long polls the library implements. The reusable assets are: the scenario language and runner design, the QXCOM 9-bit link layer, the SAS CRC, the in-memory mock serial bus in `test/blackbox/sas_blackbox.h`, and the `HOST-n / EGM-n` timestamped trace format the library's own logger emits (which is what `utils/POLLING.txt` is).

---

## 1. Public API

### 1.1 Which side of the wire?

**EGM-side. Unambiguously.** The library *is* the gaming machine; it receives long polls and answers them.

Evidence:
- `include/lp.h` declares 124 handlers, every one of the shape `void lpXX_Name(sChannelContext* channel, sPacket* rxBuffer)` — i.e. "a packet arrived, produce the machine's response". e.g. `lp.h:73` `lp1F_SendGamingMachineIdAndInformation`, `lp.h:145` `lp80_SingleLevelProgressive`, `lp.h:127` `lp71_RedeemTicket`.
- `src/lp.cpp:3149` `lp54_SendSasVersionIdAndGamingMachineSerialNumber` builds `txBuffer` and calls `sendHostPacket(channel, txPacket)` — the library *replies to* a host.
- The logging tags are written from the machine's point of view: `src/libsas_uart.cpp:480` `snprintf(strLogger, ..., "HOST-%u", channel->id + 1)` on the **receive** path, `src/libsas_uart.cpp:684` `"EGM-%u"` on the **transmit** path.
- `include/libsas_wrapper.h` is entirely EGM-facing verbs: `sasHandpayStart`, `sasStartCashout`, `sasWriteMeter`, `sasAftGmToHost`, `sasSetProgressiveLevelWin`.

There is **no host-side API** in the library. Every host-side capability in this repo is hand-rolled in `utils/` against raw QXCOM, not against libsas.

### 1.2 C vs C++ surface, and the C ABI

`include/libsas.h:2595-2609` defines the export macro:

```c
#ifndef SAS_API
#if defined(_WIN32)
#ifdef __cplusplus
#define SAS_API extern "C" __declspec(dllexport)
#else
#define SAS_API __declspec(dllexport)
#endif
#else
#ifdef __cplusplus
#define SAS_API extern "C"
#else
#define SAS_API
#endif
#endif
#endif
```

So: **yes, there is a stable C ABI**. Every public entry point is `SAS_API`, which on Windows is `extern "C" __declspec(dllexport)`. Structures are `#pragma pack(push, 8)` (`libsas_wrapper.h:47`, and a matching pack block around the struct region of `libsas.h`) — the header says "Quixant libraries are built with 8-bytes boundary alignment", which a .NET `StructLayout(Pack = 8)` must mirror.

Notably there is **no `dllimport` branch** — the macro always says `dllexport`. A consumer compiling against this header gets `dllexport` too, which works for linking against the import lib but is not the conventional pattern.

`libsas_db.h` is C++ (`<atomic>`, `<mutex>`, `<semaphore>`, `<unordered_map>`, `libsas_db.h:49-57`) and is an **internal** header — it is not in the install list. Only `include/libsas.h` and `include/libsas_wrapper.h` are installed (`CMakeLists.txt:75, 247`).

`include/lp56_enabled_games.h` is a pure-logic header of three `inline` predicates (`lp56CurrentPlayerDenomination`, `lp56DenominationToReport`, `lp56ReportsGame`) deliberately kept "free of library state ... so that it can be exercised without a bus" (`lp56_enabled_games.h:52-54`). It is unit-tested directly by `test/whitebox/lp56_enabled_games_test.cpp` (10 cases). It is **not** in the install set. It is a good model for how to factor testable protocol rules.

### 1.3 Version

- `include/libsas.h:46-50`: `SAS_MAJOR 5 / SAS_MINOR 4 / SAS_PATCH 4 / SAS_BUILD 32` → library version 5.4.4.32.
- `include/libsas_internal.h:218`: `#define SAS_VERSION "602"` — the three ASCII bytes the machine reports in its LP 54 response (`src/lp.cpp:3182` `memcpy(&txBuffer[3], SAS_VERSION, 3)`). **SAS 6.02.**

### 1.4 API groups

**Modern wrapper API** (`include/libsas_wrapper.h`) — the surface an integrator actually uses:

| Group | Functions |
|---|---|
| Init / lifecycle | `sasInit`, `sasShutdown`, `sasChannelsShutdown`, `sasAddSasChannel`, `sasGetChannelControl`, `sasSetChannelControl`, `sasGetChannelStatus` |
| Transport / callbacks | `sasSetSerialCallbacks`, `sasSetLoggerCallbacks`, `sasSetMetersProvider`, `sasSetAftLockStatusProvider` |
| Logging | `sasLoggerConfig`, `sasEnableDisableLogger`, `sasGetEnableDisableLogger` |
| Configuration | `sasSetConfigurationParameter` / `sasGetConfigurationParameter` (over `eSasEnumConf`, 17 switches, `libsas_wrapper.h:62-92`), `sasSetFeature`, `sasSetBusyResponse`/`sasGetBusyResponse`, `sasSetTicketExpiration`/`sasGetTicketExpiration`, `sasSetEnableDenomination` |
| Custom / unparsed polls | `sasRegisterCustomPoll`, `sasGetCustomPoll`, `sasCustomPollResponse` |
| NVRAM | `sasNvramDataSizeof`, `sasNvramReadVersion`, `sasNvramWriteVersion` |
| Meters | `sasReadMeter`, `sasWriteMeter`, `sasIncrementMeter`, `sasReadLegacyMeter`, `sasWriteLegacyMeter`, `sasSetImmediateModeMeter`/`sasGetImmediateModeMeter` (`eMeterMode`: DEFAULT_AUTO / IMMEDIATE_AUTO / DEFAULT_MANUAL / IMMEDIATE_MANUAL / FULL_MANUAL) |
| TITO / validation | `sasStartCashout`, `sasCompleteCashout`, `sasCancelCashout`, `sasStartTicketRedemption`, `sasCompleteTicketRedemption`, `sasRefuseTicketRedemption`, `sasSetExtendedValidationStatus` |
| Multi-denom | `sasSetAccountingDenom`/`sasGetAccountingDenom`, `sasSelectGameDenomination` |
| AFT | `sasSetAftRegistrationStatus`/`sasGetAftRegistrationStatus`, `sasSetAftTransferLimit`, `sasUpdateAftTransfer`, `sasAftGmToHost`, `sasAftCancelGmToHost`, `sasSetCurrentAmounts`, `sasGetCustomAftTicketData` |
| Handpay | `sasHandpayStart`, `sasHandpayReset`, `sasHandpayRefuse`, `sasHandpayConfigureRemoteReset`, `sasHandpayConfigureLegacyReporting` |
| Progressives | `sasSetProgressiveLevelWin` (with `eProgressiveException`: `LEVEL_HIT_AND_CASHOUT_PAID` / `HANDPAY_PENDING` / `INHIBITS_JACKPOT_CASHOUT_PAID`) |
| Wager categories | `sasCreateWagerCategory`, `sasWriteWageredCredit`, `sasGetNumberOfWagerCategories`, `sasGetWagerCategoryInfo`, `sasGetCreditWagered` |
| Component authentication | `sasGetComponentAuthentication`, `sasRegisterComponent`, `sasReplyAuthentication`, `sasRegisterROMSignature` |
| Event introspection | `sasGetStructInfo(SASDATA*, eSasStructId, index, sOneException*)` |

**Legacy API** (`include/libsas.h:2611-2666`) — 45 `SAS_`-prefixed functions taking `void** hCommPort`, of which 11 are marked `QXT_SAS_DEPRECATED` (`SAS_Setup`, `SAS_SetupStr`, `SAS_Shutdown`, `SAS_ReadMeter`, `SAS_WriteMeter`, `SAS_AFTUpdateTransfer`, `SAS_AFTUpdatePartialTransfer`, `SAS_SetCurrentAmounts`, `SAS_ReplyAuthentication`, `SAS_RegisterComponent`). The real-time-event group is here: `SAS_RTE_GameStart`, `SAS_RTE_GameEnd`, `SAS_RTE_ReelStopped`, `SAS_RTE_CardHeld`, `SAS_RTE_GameSelected`, `SAS_RTE_GameRecallEntry`, `SAS_RTE_SessionStart`, `SAS_RTE_SessionEnd`.

**Events / exceptions**: `enum SasExceptionCode` (`libsas.h:233-728`) — **122 named exception codes** with doc comments, from `NoActivity = 0x0` and `SlotDoorOpened = 0x11` through `MeterChangePending = 0xA0`, `EnabledGamesDenomsChanged = 0xA2`, `RealTimeEventMessageIdentifier = 0xFF`. Delivery to the application is via one callback: `typedef void(*callbackHandler)(sSASData* params)` (`libsas.h:2587`).

**Long polls**: `enum SasLongPollCode` (`libsas.h:1561-1688`) — 124 entries. `enum SasMeterCode` at `libsas.h:730`. Plus `SasTournamentMeterCode` (1522), `SasLegacyBonusMeterCode` (1530).

**Error model**: `QRESULT` = `unsigned int`; `QXT_SUCCESS(x)` is `((x) & 0x80000000) == 0` (`libsas.h:69-73`). Library-specific errors from `QXT_LIB_SAS_ERR 0x88000000` (`libsas.h:99`).

---

## 2. `utils/sashost/` — the closest prior art

10 files + 10 scenarios. Read in full.

### 2.1 Architecture

Four translation units in `namespace sashost`, plus an optional GUI:

```
sashost.cpp (main)
   ├─ HostEngine   (host_engine.h/.cpp)  — owns one worker thread + the link
   │     └─ SasLink (sas_link.h/.cpp)    — QXCOM 9-bit transport + SAS framing
   ├─ ScriptRunner (script.h/.cpp)       — parses & executes a .sas scenario
   └─ RunGui       (gui.h/.cpp)          — ImGui/GLFW/OpenGL3, optional
```

**Threading model** (three threads at most):

1. **Engine worker** — `HostEngine::Worker()` (`host_engine.cpp:89-265`). A single loop that owns the link exclusively: it releases each level's broadcast when that level's interval elapsed, sends the poll, drains the read, services the alive-probe and validation-id requests, refreshes the "seconds since last broadcast" display values, then `sleep_for(2ms)` (`host_engine.cpp:263`). The comment at `host_engine.cpp:85-87` states the design reason: "Keeping both in a single thread means a level's cadence is never disturbed by contention with another one."
2. **Script thread** — `ScriptRunner::Run(engine)` blocks for the whole scenario, so `gui.cpp:310` launches it as `std::thread([&runner, &engine]() { runner.Run(engine); })`. Headless mode runs it on the main thread (`sashost.cpp:239`).
3. **Main/GUI thread** — `RunGui` renders at vsync and reads `engine.Take()` once per frame (`gui.cpp:409`).

**State sharing**: all engine state lives behind one `mutable std::mutex fMutex` and is published as an immutable `Snapshot` value (`host_engine.h:66-84`, `HostEngine::Take()` at `host_engine.cpp:352`). Three flags are `std::atomic<bool>` outside the mutex: `fRunning`, `fProbeRequested`, `fValidationIdRequested`. The header states the intent (`host_engine.h:6-9`): "the script runner and the GUI observe exactly the same values while the worker thread keeps running: whatever a script changes at runtime is visible in the GUI on its next frame." That is confirmed in `gui.cpp` — every widget reads from the snapshot and writes back through an engine setter; there is no shadow copy.

**Levels**: `kMaxLevels = 32`, arrays sized `[kMaxLevels + 1]` so the level number indexes directly, index 0 unused (`host_engine.h:34, 78`).

### 2.2 The serial link — exact configuration

`utils/sashost/sas_link.cpp:20-23`:

```cpp
constexpr auto kBaudRate = QXCOM_CBR_19200;
constexpr auto kDataBits = QXCOM_DATABITS_9;
constexpr auto kParity   = QXCOM_PARITY_SPACE;
constexpr auto kStopBits = QXCOM_STOPBITS_ONE;
```

**19200 baud, 9 data bits, space parity, one stop bit.** Timeouts (`sas_link.cpp:27-29`):

```cpp
constexpr unsigned int kIntervalTimeoutMs = 9;
constexpr unsigned int kReadTimeoutMs     = 20;
constexpr unsigned int kWriteTimeoutMs    = 1500;
```

The read timeout is deliberately short — "Kept short so the polling loop stays responsive: a general poll that the EGM does not answer must not stall the scheduler that owns the broadcast cadence" (`sas_link.cpp:25-26`). (`utils/poller` and `hwconformance` both use `ReadTimeout = 5000` instead.)

**It is QXCOM, not Win32.** `SasLink::Open` (`sas_link.cpp:90-133`) calls `qxComOpen` / `qxComSetCommParameters` / `qxComSetCommTimeouts`. The reason is the ninth bit — `sas_link.h:34-35`: "QXCOM carries the ninth 'wakeup' bit that SAS uses to mark an address byte, which is why the port is opened with nine data bits."

Port naming is a **bare name**, not the `\\.\COMn` form:

```cpp
// sashost.cpp:29-33
#if defined(_WIN32)
constexpr const char* kDefaultPort = "com3";
#else
constexpr const char* kDefaultPort = "/dev/ttyS2";
#endif
```

The string is passed to `qxComOpen` verbatim after being copied into a mutable buffer (`sas_link.cpp:94-98`). Whatever `\\.\` prefixing happens is inside libqxcom, not here. Note the library's *own* device node is different again: `include/libsas_uart.h:52-56` defines `DEV_NODE "\\\\.\\QXCOM"` on Windows and `"/dev/ttyQ"` on Linux.

**USB CDC adapters: not handled.** There is no mention of CDC, FTDI, or a plain-COM fallback anywhere in `sashost`. The whole design assumes a QXCOM port that can carry the 9th bit. `utils/hwconformance/README.md` mentions "USB-to-serial adapters" as a wiring option for its two ports, and `hwconformance.cpp:864-866` adds a 5×400 ms open retry "on some systems (virtual COM port pairs, USB adapters) the port may not be ready immediately" — but that is still `qxComOpen`. **UNKNOWN** whether libqxcom itself can drive a generic USB CDC device.

**Framing.** `SasLink::Write` (`sas_link.cpp:151-182`) builds one `QXCOM_PACKET` per byte and sets the wakeup bit on the first byte only:

```cpp
packets[i].Data.u.Payload = bytes[i];
packets[i].Data.u.Wakeup  = (i == 0 && firstByteIsAddress) ? 1 : 0;
```

**SAS CRC** — `CrcCalc` at `sas_link.cpp:51-64`, nibble at a time, low byte first, with the multiplier written in octal:

```cpp
unsigned int q = (value ^ c) & 017;
value = (value >> 4) ^ (q * 010201);
```

`010201` octal = `0x1081`. `test/blackbox/sas_blackbox.h:sasCrc` uses `0x1081u` — **the same polynomial**, so the two implementations agree. `sas_link.h:26-28` says the algorithm is duplicated from libsas' `crcCalc()` on purpose "so this tool does not have to include libsas internal headers", and claims it "is verified against captured live packets by the unit test" — **I found no such unit test**: `sashost` is not referenced from `test/`, and `utils/sashost/CMakeLists.txt` registers no `add_test`. That comment appears to be stale.

### 2.3 Wire operations sashost can perform

Exactly five, all in `sas_link.h`:

| Method | Bytes on the wire | Note |
|---|---|---|
| `SendGeneralPoll(address)` | `0x80 \| address`, wakeup set | one byte |
| `SendSyncPoll()` | `0x80`, wakeup set | one byte |
| `SendIdRequest(address)` | `address, 0x1F` | **no CRC** — LP 1F is type R |
| `SendValidationId(addr, id, seq)` | `addr, 0x4C, id[3 LSB-first], seq[3 LSB-first], CRC[2]` | `sas_link.cpp:205-223` |
| `SendProgressiveBroadcast(group, level, cents)` | `0x00, 0x80, group, level, amount[5 BCD], CRC[2]` | broadcast address; `sas_link.cpp:240-260` |

`SasLink::Read` (`sas_link.cpp:268-293`) drains up to 64 packets and returns raw payload bytes. **There is no response parsing, no CRC validation of what comes back, no framing.**

### 2.4 The poll alternation — an important design insight to carry forward

`host_engine.cpp:145-149` and `README.md`'s "Polling" section:

> "SAS acknowledges an exception implicitly, by the poll value changing: a machine that sees the same general poll twice reads it as 'my answer did not get through' and reports the same exception again. Alternating the general poll with a sync poll is what acknowledges one and lets the queue advance — polling with 0x81 alone makes every exception repeat forever."

Implementation: `fSyncPollNext` toggles each poll tick (`host_engine.cpp:166-168`). This is a real, non-obvious protocol requirement and any replacement must do it.

### 2.5 How received bytes become "exceptions" — and the honest limitation

`host_engine.cpp:179-203`: everything read is counted into `fBytesReceived`, and then **every non-zero byte** is recorded as an exception code — unless a long-poll reply is expected. `host_engine.h:158-162` documents why:

> "A long poll is answered with a packet, not with an exception byte. Recording those bytes as exceptions would invent them out of whatever the reply happens to contain — a validation id holding 0x53 would read as a progressive timeout — so replies are counted for liveness and otherwise ignored for a short window after the poll goes out."

The window is `kLongPollReplyWindow = 150ms` (`host_engine.cpp:25`), set by `fReplyExpectedUntil` when the alive probe or the validation id goes out. This is a **heuristic, not a parser**. It is the single biggest thing a replacement must improve.

Exception history is a `std::deque` capped at `kMaxExceptionHistory = 512`, newest at the front (`host_engine.cpp:28`, `280-282`).

Auto-answer: when code `0x3F` (`kExceptionValidationIdNotConfigured`) arrives and a validation id is configured, `RecordException` sets `fValidationIdRequested` so the id is re-sent (`host_engine.cpp:269-274`). The rationale is in `README.md`: "Sending only at startup is easily too early — the link may not be up yet — and would leave a machine reset mid-run unserved."

### 2.6 The scenario language — complete specification

Parsing (`script.cpp:108-149`): one command per line; `#` starts a comment and truncates the rest of the line; whitespace tokenises; blank lines skipped; **an empty command list is a load error** ("script contains no commands"). Syntax is validated only at the verb level at parse time — argument errors surface at execution and fail that step.

Arguments may be **bare or `key=value`** (`ValueOf`, `script.cpp:72-84`): `expect-exception 0x53 within=6s` and `expect-exception code=0x53 within=6s` both parse; a bare token is accepted at any position, and a `key=value` token is only accepted if the key matches the one expected at that index.

Durations (`ParseDuration`, `script.cpp:37-56`): digits followed by `ms`, `s`, `m`, or nothing (= milliseconds). No `h`, no fractions.

**All 15 verbs** (14 are documented in the README table; `expect-alive` is implemented but **missing from the README**):

| Verb | Args | Semantics | Source |
|---|---|---|---|
| `log <text...>` | free text | joins args with spaces into the step's result; always passes | `script.cpp:213-220` |
| `wait <duration>` | | sleeps in 20 ms slices, interruptible by `Stop()` | `script.cpp:222-235` |
| `set-group <n>` | 0..255 | group placed in every broadcast | `script.cpp:237-248` |
| `set-poll <duration>\|off` | | poll interval; `off` (or `0`) stops polling entirely | `script.cpp:304-323` |
| `set-validation-id <id> [sequence]` | decimal only | sets **and immediately sends** LP 4C | `script.cpp:281-302` |
| `expect-alive [within=<duration>]` | default 2000 ms | repeats LP 1F every 200 ms until `BytesReceived()` increases; fails with "it is not running, or not on this port or address" | `script.cpp:250-279` |
| `enable-level <n>` | 1..32 | starts broadcasting; resets `fNextBroadcast` to now | `script.cpp:337-342` |
| `disable-level <n>` | 1..32 | stops broadcasting that level | `script.cpp:344-349` |
| `set-interval <n> <duration>` | duration > 0 | per-level cadence | `script.cpp:351-362` |
| `set-amount <n> <cents>` | | current jackpot value | `script.cpp:364-379` |
| `set-increment <n> <cents>` | | added on every broadcast | `script.cpp:364-379` |
| `drop <n> [count]` | default 1 | skips the next N broadcasts of that level; the schedule keeps running, the packet just never goes out | `script.cpp:381-392`, `host_engine.cpp:113-120` |
| `expect-exception <code> within=<d>` | | passes **as soon as** it arrives; result reports elapsed | `script.cpp:446-464` |
| `expect-no-exception <code> during=<d>` | | watches the **whole** window; passes iff count == 0 | `script.cpp:472-481` |
| `expect-exception-count <code> <n> during=<d>` | | watches the whole window; passes iff count == n | `script.cpp:483-489` |

An unrecognised verb fails the step with `"unknown command: <verb>"` (`script.cpp:492`).

**Exception codes** may be decimal or `0x`-prefixed hex: `ParseUnsigned` rejects non-digits, then a fallback parses `0x…` base 16 (`script.cpp:400-413`). Any code > 255 is rejected.

**Expectation windows** are measured from the moment the command starts: `since = WallClock::now()` and `deadline = Clock::now() + window` (`script.cpp:439-440`), then `engine.CountExceptions(code, since)` counts matching events at or after `since` (`host_engine.cpp:381-389`). Polling granularity while waiting is 20 ms (`script.cpp:24`).

`expect-exception-count <code> 0 during=<d>` is the idiom the scenarios use for "and *not again* for the rest of the episode" — see `spec_10_2.sas` line "And it must be reported once for the episode, not every five seconds."

**What the language deliberately excludes.** `script.h:6-8`: "Only what the host can actually observe may be asserted on, which is the exceptions the EGM sends. The lockup an EGM raises internally is invisible from this side of the wire, so it is deliberately not part of the language."

### 2.7 Pass/fail and exit codes

`ScriptProgress` (`script.h:36-44`) carries per-step `started/finished/failed/result` plus `running/finished/passed/failures`. `Run()` executes every step even after a failure — it does not abort — and sets `passed = (failures == 0) && !fStop` (`script.cpp:200`). A scenario stopped early is **not** a pass.

Headless report (`sashost.cpp:146-161`) prints one line per finished step:

```
  ok    <step text, left-padded to 46>  <result>
  FAIL  <step text, left-padded to 46>  <result>

PASSED (0 failure(s))
```

Exit codes (`sashost.cpp` and README agree):

| Code | Meaning | Where |
|---|---|---|
| 0 | every expectation passed, or no scenario was run | `ReportScript` returns 0; `RunGui` returns 0 |
| 1 | at least one expectation failed | `ReportScript` returns 1; `gui.cpp:476-477` returns 1 only if the script was actually started and finished failing |
| 2 | setup problem — bad option, cannot open the port, cannot load the script, cannot init GLFW/create window | `sashost.cpp:175, 187, 216`; `gui.cpp:365, 386` |

`stdout`/`stderr` are set unbuffered at `sashost.cpp:170-171` — "A diagnostic tool whose output only appears once a 4KB buffer fills is worse than useless when the thing being diagnosed is a link that has gone quiet."

With no scenario, headless mode broadcasts forever, printing each new exception as `HH:MM:SS.mmm  exception 0xNN` plus a status line every 20 ticks (5 s) with poll count and per-level sent/amount (`sashost.cpp:243-287`).

### 2.8 CLI

`sashost.cpp:54-71` (`--help`) plus two undocumented options parsed at `sashost.cpp:103-109`:

```
--port <name>          serial port (default com3 / /dev/ttyS2)
--address <n>          EGM SAS address (default 1)
--group <n>            progressive group (default 1)
--levels <a,b,...>     levels to start broadcasting immediately
--interval <ms>        broadcast interval for those levels (default 2000)
--amount <cents>       starting amount (default 100000)
--increment <cents>    added on every broadcast (default 1)
--script <file>        scenario to run
--headless             run without opening the GUI
--validation-id <n>    (NOT in --help)  default 0x010000; 0 disables the startup send
--validation-seq <n>   (NOT in --help)  default 0
--help
```

An unknown or value-less option prints to stderr and returns 2 (`sashost.cpp:120-124`).

### 2.9 The GUI

ImGui 1.90.9 on GLFW 3.4 + OpenGL 3 (GL 3.0/GLSL 130, or 3.2 core on macOS), both fetched at configure time (`CMakeLists.txt:47-77`). One full-viewport window with a tab bar: **General poll** (rate radio buttons off/20/40/80/200 ms, polls-sent and bytes-back counters, exception list), **Progressives** (group drag, per-level table: on/level/interval/amount/increment/sent/dropped/"drop 1"/"x3" buttons), **Validation** (id + sequence inputs, Send button, a warning when 0x3F has been seen). Below the tabs, the **Scenario** panel with Run/Stop and a live per-step list (`>>` current, `ok`, `FAIL`, greyed pending) with each step's result appended.

`ImGui::GetIO().IniFilename = nullptr` — no layout file next to the executable (`gui.cpp:393`).

Build switch: `-DSASHOST_GUI=OFF` builds headless-only and is auto-disabled when `find_package(OpenGL)` fails (`CMakeLists.txt:30-38`). The tool defines `SASHOST_WITH_GUI` 1/0 accordingly.

### 2.10 Build integration

- `CMakeLists.txt:274-277` (root): `option(SASHOST "..." OFF)` — **off by default**; `-DSASHOST=ON` adds the subdirectory.
- `utils/sashost/CMakeLists.txt:20-23`: bails out with a warning if `qxcom_static` does not exist.
- Links **only** `qxcom_static` + `Threads::Threads` (+ `winmm` on Windows, `glfw`/`OpenGL::GL` with the GUI). **It does not link libsas at all.**
- Scenarios are copied next to the executable POST_BUILD (`CMakeLists.txt:117-123`).
- **No `add_test`** — sashost is not part of ctest.

### 2.11 The 10 scenarios

All in `utils/sashost/scenarios/*.sas`. Every one opens with `expect-alive within=5s` and a settling `wait` before its first assertion, because "assertions about what was *not* reported are meaningless against one that is switched off" and because a machine left without broadcasts is already in timeout with an exception waiting for the first poll.

| Scenario | What it proves |
|---|---|
| `spec_10_2.sas` | The reference case for SAS 6.02 §10.2. Baseline clean; one level quiet while the other keeps arriving (the case a single global timer misses); reported once per episode not every 5 s; recovery re-arms; total blackout still reports once; 1 lost packet at 2 s (4 s gap) tolerated; 2 lost (6 s gap) reported. |
| `three_levels.sas` | Three levels so the quiet one is neither first nor last — "A machine that tracks one shared window sees an unbroken stream and reports nothing." Also: two of three quiet at once is one episode, not two. |
| `cadence_1s/2s/3s/4s.sas` | Sweep: at each cadence a clean run must produce no 0x53 for 60 s. |
| `drop_tolerance.sas` | Quantifies the margin: consecutive losses tolerated = `floor(5s / interval) - 1`. Checks 1 and 2 drops at 2 s, then 2 and 5 drops at 1 s. |
| `group_filtering.sas` | A machine on group 1 fed on group 1, starved on group 77, fed again on group 1. |
| `group_zero.sas` | Group 0 is **not** a wildcard — SAS 6.02 §10.1.1 reserves it for non-SAS progressives, so a machine on group 1 must time out on group 0 exactly as if nothing were sent. Also checks group 9. |
| `poll_rates.sas` | The poll rate changes only *how quickly* an exception arrives, not *whether*: 200 ms, 20 ms, then `set-poll off` (nothing reaches the host for 12 s however long the machine is in timeout), then resuming the poll delivers what the machine was holding within 3 s. |

Note: `spec_10_2.sas` carries a **stale comment** (lines 15-17) claiming "Group 0 reaches a machine whatever group it is configured for" — flatly contradicted by `group_zero.sas` and by the README. The scenario itself does `set-group 1`, so the behaviour is right and only the comment is wrong.

### 2.12 SAS surface covered vs. the full command set

**Covered by sashost (5 operations):** general poll `0x80|addr`, sync poll `0x80`, LP `1F` (Send Gaming Machine ID & Information — used only as a liveness probe, the response is never parsed), LP `4C` (Set Secure Enhanced Validation ID), LP `80` (Receive Progressive Amount, one level per message, broadcast address).

**Not covered — the other ~119 long polls `lp.h` implements.** Concretely, and grouped:

- **Meters**: `0F, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 1A, 1C, 1E, 20, 2A, 2B, 2C, 2D, 2F, 31-3C, 3E-4A, 50, 52, 6F, AF` — the entire accounting surface.
- **Machine/game info**: `1F` (parsed), `21` ROM signature, `51, 53, 54, 55, 56, B1, B2, B3, B4, B5, B7`.
- **Control**: `01` shutdown, `02` startup, `03/04` sound, `05` reel-spin sounds, `06/07` bill acceptor, `08` bill denominations, `09` enable/disable game N, `0A/0B` maintenance mode, `0C/0D` sound, `0E` **enable/disable RTE**, `2E` delay game, `A8` handpay reset method, `AA` auto rebet.
- **Handpay**: `1B` Send Handpay Information, `94` Remote Handpay Reset. (`hwconformance` does LP 1B; sashost does not.)
- **Tickets / validation**: `3D, 4D, 4F, 57, 58, 70, 71, 7B, 7C, 7D` — sashost sends `4C` and nothing else in this family.
- **AFT**: `72, 73, 74, 75, 76` — none. (`hwconformance` does 72; `blackbox` does 72/73/74.)
- **Progressives beyond LP 80**: `83` cumulative wins, `84`, `85`, `86` **multi-level progressive broadcast**, `87`. LP 86 in particular is the multi-level form of what sashost only does one level at a time.
- **Legacy bonus / tournament**: `8A, 8B, 8C, 90, 95, 96, 97, 98, 99, 9A`.
- **Component authentication**: `6E`.
- **Date/time**: `7E, 7F`.
- **Features / limits**: `A0` enabled features, `A4` cashout limit, `B0` multi-denom preamble, `B6` meter collect status.
- **Framing**: **no RTE support at all** — sashost never sends LP `0E` and cannot decode the `[addr][FF][code][data][crc][crc]` RTE frame (SAS 6.02 Table 12.5). Both `test/blackbox` and `hwconformance` do handle RTE.
- **Link bring-up**: no chirp detection, no wakeup pulse sequence. `utils/poller` has both; sashost just starts polling.

### 2.13 Reusable as-is vs. needs generalising

**Reusable essentially as-is**

| Asset | Where | Note |
|---|---|---|
| `SasLink::Open` line settings + timeouts | `sas_link.cpp:20-29, 90-133` | The exact DCB/timeout values a SAS host needs on QXCOM. |
| `Write` with wakeup-on-first-byte | `sas_link.cpp:151-182` | The 9-bit framing rule, three lines. |
| `CrcCalc` | `sas_link.cpp:51-64` | Verified identical to `sas_blackbox.h::sasCrc` and to the library's `crcCalc`. |
| `EncodeBcd` | `sas_link.cpp:71-78` | Packed BCD, MSD first, truncating — matches SAS. |
| General/sync poll alternation | `host_engine.cpp:145-175` | The implicit-ack rule; must be preserved. |
| Snapshot-under-mutex state publishing | `host_engine.h:66-84`, `.cpp:352-373` | Clean pattern for "one worker, many observers"; scales to any UI. |
| Scenario language shape | `script.cpp` | Line-oriented, `#` comments, bare-or-`key=value`, `ms/s/m` durations, three expectation forms with within/during semantics. |
| Per-step live progress model | `script.h:26-44` | Drives both the headless report and the GUI list from one structure. |
| Exit-code contract 0/1/2 | `sashost.cpp` | Directly CI-usable. |
| The 10 scenarios | `scenarios/` | Real, spec-cited regression content for §10.1.1 / §10.2. |

**Needs generalising**

| Gap | Why it matters |
|---|---|
| The engine is hard-wired to progressive broadcasting | `LevelState`/`fNextBroadcast` are the only schedulable thing. A general host needs a generic "scheduled transmission" concept and a generic long-poll send. |
| No response parser | Every non-zero RX byte becomes an "exception". Needs real framing: address, command, length, payload, CRC verify, and RTE `[addr][FF][code][...]`. |
| `fReplyExpectedUntil` 150 ms heuristic | Correlating a reply to its request by a time window is fragile; a request/response correlator keyed on the command byte is the right shape. |
| No trace/log output | Only stdout step lines. Nothing writes the `HOST-n/EGM-n` trace format (see §4). |
| Expectation vocabulary is exceptions-only | Cannot assert on a long-poll response field, on a meter value, on a timing bound, or on silence-on-the-wire (the `tournament` blackbox case needed exactly that). |
| No parametrisation | Scenarios are fixed text; `cadence_1s..4s.sas` are four near-identical files differing in one number. Compare `test/blackbox/CMakeLists.txt` which parametrises via environment (`SAS_POLL_MS`, `SAS_ROUNDS`, `SAS_RTE`, `SAS_BURST`) and generates cases from a CMake function. |
| No structured/machine-readable result | No JUnit XML, no JSON, no ctest registration. |
| Transport is QXCOM-only, single channel | No plain serial, no USB CDC, no mock/offline transport, no multi-channel. |
| No link bring-up | No chirp wait, no wakeup pulses (`poller` has both). |
| `set-validation-id` takes decimal only | `ParseUnsigned` rejects `0x...` for ids; only exception codes get the hex fallback. |
| `expect-alive` undocumented | Present in `script.cpp:250`, absent from `README.md`'s command table. |

---

## 3. `utils/poller/` and `utils/hwconformance/`

### 3.1 `utils/poller/` — a raw host bring-up probe

`poller.cpp` (517 lines), `README.md`, `CMakeLists.txt`. **Not built** — `CMakeLists.txt:267` in the root has `# add_subdirectory(utils/poller)` commented out.

What it does: opens a QXCOM port at the same 19200/9/space/1 settings (`poller.cpp:50-53`, `initializeSerialPort` at :448 — timeouts 9/5000/1500), then runs a four-phase state machine (`poller.cpp:36`):

`WAITING_FOR_CHIRP` → `CONNECTED` → `CONNECTION_LOST`, with phases WAKEUP → INITIAL → CONTINUOUS inside CONNECTED.

- **Chirp detection**: counts bytes equal to `0x01` or `0xFF`; threshold 3 within `CHIRP_WINDOW` (`poller.cpp:44-46`; `INTERVAL` is 40 ms in the source, though the README says 200 ms and that the window is `INTERVAL*5` — the source says `INTERVAL*100`. **The README is out of date on both counts.**)
- **Wakeup**: 4 × `0x80` (`wakeupSequence`, `poller.cpp:56`).
- **Initial sequence**: a hardcoded replay of the HOST side of `POLLING.txt` (`initialSequence`, `poller.cpp:65`) — machine info, ticket data, validation status, startup, delay game, features, version, time.
- **Continuous**: alternates two hardcoded extended-meter requests, `01 6F …` and `01 AF …` (`continuousSequence`, `poller.cpp:94`).
- **Loss detection**: no RX for 5 s → `CONNECTION_LOST`; another 5 s → back to `WAITING_FOR_CHIRP`.
- Two threads: `receiveThread` (:242) and `hostPollingThread` (:291), plus per-second stats (`printStats`, :428). SIGINT shutdown.

**Its own README's "Limitations" section is the honest summary**: "Does not parse SAS responses (only raw byte dumps)… No CRC validation or framing checks beyond raw write/read… No dynamic port enumeration."

Value to a replacement: the **chirp-then-wakeup link bring-up** and the connection-loss/recovery state machine are the two things `sashost` lacks. The hardcoded byte sequences are a useful record of a real host's opening handshake.

### 3.2 `utils/hwconformance/` — a real conformance harness

`hwconformance.cpp` (2114 lines), `README.md` (466 lines), `CMakeLists.txt`. Built when `BUILD_TESTING` is on (root `CMakeLists.txt:217`).

**What it is**: a real-hardware re-run of the `test/blackbox` scenarios. It drives **both sides**: the EGM side runs the *real libsas library* on one COM port; the host side talks raw QXCOM on a second COM port. **The two ports must be physically wired together.** It links `qxcom_static` **and `sas_static_testable`** — so it is the one utility that drives the library, not just hardware.

**Architecture — three processes, deliberately.** `hwconformance.cpp:14-29`:

> "Each side runs in its OWN OS process rather than a thread of a shared process: an early two-thread-in-one-process version of this tool crashed intermittently (access violation) under concurrent use of two independent QXCOM channels from two threads of the same process — the QXCOM driver does not document being safe for that pattern."

The orchestrator (`runOrchestrator`, :1616) spawns itself twice via `POPEN` with `--egm-worker` / `--host-worker`, relays both children's stdout live line-by-line (`relayPipe`, :1585), passes a shared `--epoch` in microseconds so all three processes share a time base (`main`, :2018-2024), and then merges the children's small event files into one report. **This is a significant, hard-won constraint: two QXCOM channels from one process is not safe.**

**Test registry** — `struct TestCase { int pollMs; int rounds; bool rte; bool burst; const char* label; }` and `kAllCases[]` (`hwconformance.cpp:143-175`), 16 entries:

| poll ms | RTE | pacing | rounds |
|---|---|---|---|
| 40 / 200 / 1000 / 2000 | off | paced | 25 / 15 / 6 / 4 |
| 40 / 200 / 1000 / 2000 | on | paced | 20 / 12 / 5 / 3 |
| 40 / 200 / 1000 / 2000 | off | burst | 25 / 15 / 6 / 4 |
| 40 / 200 / 1000 / 2000 | on | burst | 20 / 12 / 5 / 3 |

Rounds are scaled down for slow polls and RTE because one queued exception drains per poll (RTE ~2 polls each).

**Modes** (`main`, :2034-2113; priority order `--handpay-queue` > `--handpay` > `--aft` > `--all`):

| Mode | What it checks |
|---|---|
| default / `--all` | handpay lifecycle + handpay queue + AFT GM-cashout + the 16-case gameorder matrix, then a consolidated summary (`runAllTests`, :1911) |
| `--single` | one gameorder configuration |
| `--handpay` | 0x51 HandpayIsPending → LP 0x1B → 0x52 HandpayWasReset, then an 8 s watch window for a spurious 0x51 |
| `--handpay-queue` | two handpays stacked; host must see exactly `[0x51(A), 0x52(A), 0x51(B), 0x52(B)]` in order, each LP 1B amount matching |
| `--aft` | `sasAftGmToHost` → 0x6A → host LP72 transfer-to-host → `sasUpdateAftTransfer` → 0x69 → LP72 interrogation → watch window |

**It drives the library through real hardware.** The EGM workers call real API: `sasInit`, `sasHandpayStart`/`sasHandpayReset`, `sasAftGmToHost`, `sasSetCurrentAmounts`, `sasUpdateAftTransfer`. The host workers build raw frames: `hp_buildLp1B` (:341), `aft_buildLp72Transfer` (:396), `aft_buildLp72Interrogation` (:436), with its own `sasCrc` (:301) and `bcdToDec`/`decToBcd` (:332, :382).

**Reporting format — worth adopting.** Three layers:

1. **Live log**, `[+  12.345s][HOST] …` / `[EGM ] …`, relative to a shared epoch (`logLine`, :197; `elapsedSeconds`, :191).
2. **Per-event correlation table** (`printReport`, :1504) built by parsing two plain-text event files with lines `SENT <code> <round> <t>` and `RECV <code> <t>` (`parseEventFile`, :1477, `recordSent`/`recordReceived`, :285/:292):

```
============================================================
  HWCONFORMANCE REPORT
  EGM port: com3   HOST port: com4
  Poll interval: 200 ms   Rounds: 20   Mode: non-RTE   Pacing: paced
============================================================
#    code            sent@(s)      recv@(s)      latency(ms)   status
1    7e GameStarted  1.234         1.251         17            ok
...
------------------------------------------------------------
  Sent: 40   Received: 40   Mismatches: 0   Not received: 0   Unexpected extras: 0
============================================================
  VERDICT: PASS -- every GameStarted/GameEnded was received exactly once, ...
============================================================
```

Pass condition: `mismatches == 0 && missing == 0 && extras == 0 && !sent.empty()` (`:1578`) — note the deliberate `!sent.empty()`, so a run that sent nothing is a FAIL, not a vacuous pass.

3. **Consolidated suite summary** (`runAllTests`, :1961-1993): `#  configuration  rounds  verdict` rows, then `PASSED: n / m   FAILED: k / m` and an `OVERALL: PASS/FAIL` line.

Exit code: 0 if all configurations passed, 1 otherwise. Single-scenario modes report a single PASS/FAIL taken from the host worker's own exit code, since a one-off lifecycle does not fit a per-event table.

**The README also carries "Interpreting failures" tables** mapping each symptom (loss / duplication / reorder; "HandpayIsPending never observed"; "Transferred amount mismatch") to a likely cause. That diagnostic layer is unusually good and is exactly what a replacement should carry forward — a verdict without a hypothesis wastes the operator's next hour.

**Host-side wire details** (`hostOpen`, :860): same 19200/9-bit/space/1; timeouts 9 / **5000** / 1500 (the comment at :911 says "DIAGNOSTIC: matching utils/poller's known-good value"); **5 open retries with 400 ms back-off** for virtual/USB ports. `hostSendGeneralPoll` (:951) sends `0x80` then `0x80|address` 2 ms apart — the same sync/general alternation as sashost. `hostEnableRte` (:958) sends LP 0x0E with CRC and warns if no ACK comes back.

---

## 4. `utils/POLLING.txt` — the trace format

330 lines, 24,440 bytes. **This is not a bespoke file: it is exactly what libsas' own logger writes.** Confirmed against `src/libsas_logger.cpp:134-160`:

```c
fprintf(handle, "%-10s - %02hhu:%02hhu:%02hhu.%03u - ",
        str, preciseTime.hour, preciseTime.minutes, preciseTime.seconds,
        preciseTime.microseconds / 1000);
if (tsBuffer)
    fprintf(handle, "{hw %llu} ", (unsigned long long)tsBuffer[0]);
for (i = 0; i < length; i++)
    fprintf(handle, "%02X ", serialBuffer[i].u.Payload);
loggerParser(dataBuffer, length, description);
fprintf(handle, "\n%-27s %s\n\n", "", description);
```

### 4.1 Exact line format

Each record is **three physical lines**: a header line, a description line, and a blank line.

```
<TAG padded to 10> - <HH:MM:SS.mmm> - <HEX BYTE> <HEX BYTE> ... <trailing space>
<27 spaces><description>
<blank>
```

Verbatim example (POLLING.txt lines 5-6):

```
HOST-1     - 15:34:46.551 - 01 1F
                            Send gaming machine ID & information
```

- **Tag**: `HOST-%u` or `EGM-%u`, where `%u` is `channel->id + 1` — the 1-based SAS channel number, **not** the SAS address. Built at `src/libsas_uart.cpp:480` (RX path → `HOST-n`) and `:684` (TX path → `EGM-n`). Left-justified in 10 columns, so `HOST-1` is followed by five spaces.
- **Direction semantics**: the file is written *by the EGM*. `HOST-1` = bytes the EGM **received from** the host. `EGM-1` = bytes the EGM **transmitted**. In this capture: 57 `HOST-1` records, 53 `EGM-1` records.
- **Timestamp**: local wall clock, `HH:MM:SS.mmm`, millisecond resolution (microseconds/1000). **No date.** Source is `internalGetLocalTimePrecise`.
- **Optional hardware timestamp**: current code emits `{hw <u64>} ` between the wall clock and the hex when a timestamp buffer is present (`libsas_logger.cpp:143-144`). The comment explains the point: "the gap between the two localises how much of a response turnaround was spent below this library." **POLLING.txt contains none** — it predates the feature or was captured with `EnableTimestamps = 0`.
- **Hex**: uppercase, space-separated, one byte per unit, **with a trailing space** after the last byte. Includes the CRC bytes. Long lines are not wrapped — the longest records in this capture run several hundred characters.
- **Description**: produced by `loggerParser` (`libsas_logger.cpp:~178`). Written on its own line, indented to column 28 (`"%-27s %s"` with an empty first arg).

### 4.2 Description vocabulary observed in this capture

19 distinct strings. In frequency order:

`Send extended meters for game 0000` (68) · `Send legacy bonus meters for game 0000` (6) · `Unparsed LP` (5) · `Send gaming machine ID & information` (4) · `ACK` (4) · `Set ticket data` (2) · `Send extended meters for game 0004/0003/0002` (2 each) · `Send enabled features` (2) · `Send SAS version ID and gaming machine serial number` (2) · `Extended validation status` (2) · `Delay game` (2) · `AFT game lock and status request (Lock code = interrogate current status only)` (2) · `AFT game lock and status request (Game lock status = game not locked. Current cashable amount = 0000000000. Current restricted amount = 0000000000. Current non-restricted amount = 0000000000)` (2) · `Startup` (1) · `Receive date and time` (1) · `Chirping: 0x01` (1)

Two things to note:
- **Request and response carry the same description** — the `HOST-1` and `EGM-1` records for one exchange both say e.g. "Send enabled features". Direction is carried only by the tag.
- The parser **decodes fields into the description** for some polls (the LP 74 example above spells out lock status and all three amounts). `Unparsed LP` is the fallback for commands it does not know (LP 7C appears as `Unparsed LP` here).

### 4.3 What the capture shows

A complete host bring-up: EGM chirps `01`; host repeats LP 1F three times, then a bare `80`; then `7D` set ticket data, `7B` extended validation status, `7C`, `02` startup (ACKed with a bare `01`), `7C`, `2E` delay game, `A0` enabled features, `54` version/serial, `7F` receive date and time, LP 74 AFT lock interrogation; then settles into continuous alternation of `6F` and `AF` extended-meter requests roughly every 120 ms. This is precisely the sequence `utils/poller` hardcodes.

### 4.4 Assessment as a trace format for a new tool

**Strengths**: human-readable at a glance; direction and channel in one token; millisecond timestamps; the decoded description sits right under the bytes so the eye can check the parse against the raw; already the format every libsas customer's log is in, so a tool that emits it produces artifacts an engineer can diff against a machine-side log.

**Weaknesses**: three lines per record makes it 3× larger than needed and awkward for `grep`; no date, so a run crossing midnight is ambiguous and correlation with other logs is manual; no sequence numbers; the description carries no structure (a parsed field is prose, not a key/value); no CRC-valid flag; `%-27s` indentation is fragile; and it is write-only — nothing in the repo parses it back.

**Recommendation**: emit this format as the *human* view (it is the lingua franca here), and emit a structured record (NDJSON: iso8601 timestamp, direction, channel, address, command, hex, decoded fields, crcOk, seq) as the *machine* view. `hwconformance` already proves the value of a machine-readable side channel — its `SENT`/`RECV` files are what make the correlation table possible.

---

## 5. Tests

### 5.1 Structure

`test/CMakeLists.txt` fetches **GoogleTest, pinned to `release-1.12.1`** (`test/CMakeLists.txt:9-17`), with `gtest_force_shared_crt OFF`, and adds two subdirectories.

There is a subtle and deliberate pinning trick in the root `CMakeLists.txt:134-146`: libnvram's own CMakeLists declares an **unpinned** googletest at `GIT_TAG main`, and `FetchContent_Declare` state is a global property where the first declaration wins — so libsas declares its pinned googletest *before* fetching nvram, guaranteeing the pin holds.

Both suites link **`sas_static_testable`**, a test-only target built from the FULL sources regardless of the HAL flag, with `VALIDATION_DEBUG` (Windows) and `_DEBUG` (Linux) defined (root `CMakeLists.txt:197-207`). See §6.3 — this is what lets `sasInit` run on a non-Quixant machine.

### 5.2 White-box — `test/whitebox/`

GoogleTest, exercising library internals directly (`sasContext`, `internalInitContext`, `internalQueueException`, …). Five translation units, **371 test cases** total (counted by `grep -cE '^(TEST|TEST_F|TEST_P)\('`):

| File | Cases |
|---|---|
| `whitebox_test.cpp` (380,693 bytes) | 343 |
| `accounting_denom_test.cpp` | 10 |
| `lp56_enabled_games_test.cpp` | 10 |
| `rte_implied_ack_test.cpp` | 6 |
| `provider_lock_test.cpp` | 2 |

Registered as **one** ctest entry, `whitebox_tests`, label `whitebox`.

Largest suites by case count: `AftTransferTest` (36), `AftRegistrationTest` (24), `HandpayTest` (21), `AftLockTest` (21), `SyncStateMachineTest` (18), `AftUpdateTransferTest` (18), `FeatureGatedPollTest` (14), `AftGmToHostTest` (10), `EnabledFeaturesPerChannel` (9), `RedeemTicketRepeatTest` (8), `ChannelReturnToLinkUp` (8), `AggregateMaxBetTest` (8). Also present: `ProgressiveBroadcastTimeoutTest` (5), `ProgressiveTimerTest` (3), `ExceptionQueueTest` (3), `TxRingCapacityTest` (5), `PowerUpExceptionOrderTest` (5), `ImpliedAckWatchdogTest` (4). The centre of gravity is clearly **AFT** (≈115 cases across the AFT suites).

`whitebox/CMakeLists.txt:5-9` warns explicitly that these must **not** link the shipped `sas_static`, which in a HAL build is only the thin `src/hal` shim.

### 5.3 Black-box — `test/blackbox/`

**Yes, there is an in-memory mock serial bus, and the CIRS45 AFT-timer regression uses it.** `test/blackbox/sas_blackbox.h` (11,963 bytes) is a single header giving:

- **`MockPort`** — a `std::deque<uint16_t>` behind a mutex + condvar, holding 9-bit SAS units (payload in bits 0-7, wakeup in bit 8, matching the `QXCOM_DATA` layout). Two instances: `gToEgm` (host→EGM) and `gToHost` (EGM→host). A "virtual null-modem pair".
- **EGM serial callbacks** wired into the library via `sasSetSerialCallbacks` (`installEgmSerialCallbacks`), implementing `open/close/cleanup/setState/getState/setTimeouts/getTimeouts/getVersion/getTimestamp/readExtended/writeExtended/flushRx/flushTx/cancelRx`. `egmGetState` reports 19200/9/parity 0/1 stop; `egmGetTimeouts` reports 9/5000/1500. `egmReadExtended` returns `0x80000000` (READ_ERR) on an empty read so libsas keeps polling.
- **Host-side helpers**: `writeUnits`, `readRawUnits` (9-bit — needed because "A scenario that only looks at the payload cannot tell a loop-break chirp from a long-poll ACK, because both are a single byte equal to the gaming machine address"), `readUnits` (payload-only), `hostSendLongPoll` (appends CRC, sets wakeup), `hostGeneralPoll` (`0x80` then `0x80|addr`, 2 ms apart), `hostWakeup(pulses, gapMs)`, `hostEnableRte(enable)` (LP 0x0E).
- **`sasCrc`** (`0x1081`) and **`toBcd5`**.

The header is entirely `static`/`inline` so it can be included from a single TU.

`blackbox_test.cpp` (200,365 bytes) uses **no gtest macros** — it is a scenario dispatcher (`blackbox_test.cpp:4263-4297`) taking the scenario name as `argv[1]`. **18 scenarios implemented**: `cirs45`, `aftgmcashout`, `aftpartial`, `handpay`, `handpayqueue`, **`legacyhandpay`**, `gameorder`, `systemtime`, `syncpolllink`, `syncgate`, `syncnack`, `syncchirp`, `linkdown30s`, `tournament`, `ticketttl`, `progtimeout`, `handpayresend`, `rtenoactivity`.

**Finding: `legacyhandpay` is implemented but never registered with ctest** — `grep -n 'legacyhandpay' test/blackbox/CMakeLists.txt` returns nothing. It is dead as far as CI is concerned.

**34 ctest cases** are registered: 14 named `add_test` calls plus 20 generated by four CMake functions (8 × `add_gameorder_case`, 8 × `add_gameorder_burst_case`, 2 × `add_tournament_case`, 2 × `add_rtenoactivity_case`).

Parametrisation is via **environment variables on the test property**, e.g. `ENVIRONMENT "SAS_POLL_MS=${poll_ms};SAS_ROUNDS=${rounds};SAS_RTE=${rte};SAS_BURST=1"`. Labels are used for selection: `blackbox`, `cirs45`, `aft`, `handpay`, `gameorder`, `burst`, `sync`, `linkdown`, `tournament`, `ticket`, `progressive`, `rte`, `noactivity`, `systemtime`. Timeouts are **per case, computed against the test's own internal deadline** — the comment at `blackbox/CMakeLists.txt:140-147` explains that a flat 90 s would have ctest kill slow-poll burst runs before their own pass/fail check ran, producing false failures.

The CMakeLists is itself unusually well documented — each `add_test` carries the SAS 6.02 section and often the ticket ID it guards (IGS-171 for `tournament`, IGS-198 for `rtenoactivity`, §15.12 for `ticketttl`, §10.2 for `progtimeout`, §7.8.1 for `handpayresend`).

Root README confirms the story: "All tests are self-contained and run without any serial hardware. The CIRS45 AFT-timer regression uses an in-memory mock serial bus, so it runs in CI just like the unit tests (no real COM ports required)."

### 5.4 How an external host tool fits this test story

Today it does not: `sashost` has **no ctest registration** and is not built by default. The natural fit is a three-tier ladder that already exists in pieces:

| Tier | Transport | Runs where | Today |
|---|---|---|---|
| Unit | none | anywhere | `whitebox_tests` (371 gtest cases) |
| Integration | in-memory mock bus (`sas_blackbox.h`) | any CI runner | 34 blackbox ctest cases |
| Real hardware | two wired QXCOM ports, both sides ours | bench | `hwconformance` (manual, not ctest) |
| Real EGM | one QXCOM port, EGM is the DUT | bench/lab | **`sashost` — orphaned, not in ctest** |

A replacement host tool should slot into tier 4 *and* be able to run against tier 2 by swapping its transport for `MockPort`. That single change — making the transport an interface with a QXCOM implementation and a mock implementation — would let every host scenario run in CI without hardware, which is the thing `sashost` cannot do today.

---

## 6. Build and consumption

### 6.1 How libsas is built

Root `CMakeLists.txt`. CMake 3.28…3.31, languages C and CXX, project name `sas`.

Bootstraps Quixant's shared CMake tooling: `FetchContent` of `github.com/quixantplc/qxcmake` at `v2.16.0.0`, then `include(qx.cmake)`, `qx_init`, `qx_version` (which generates `src/sas_version.c` and `src/version.rc`), and finally `qx_pack()` for the distribution package.

**Both library kinds are built** (`CMakeLists.txt:87, 97`):

```cmake
add_library(${PROJECT_NAME}_shared SHARED ${SRC_FILES})   # → libsas.dll / libsas.so
add_library(${PROJECT_NAME}_static STATIC ${SRC_FILES})   # → libsas.lib / libsas.a
```

Output name is `libsas` on Windows, `sas` elsewhere (`CMakeLists.txt:79`).

Dependencies fetched from source, each guarded against a consumer having already declared the same target: **libnvram v5.3.2.1**, **libqxcom v3.3.4.0**, googletest release-1.12.1. Important architectural note at `CMakeLists.txt:118-125`: "`sas_shared`/`sas_static`/`sas_static_testable` **never link against them**; only the consumers that wire up a real transport (examples, hwconformance, poller) do." The library reaches its transport purely through `sasSetSerialCallbacks` — that decoupling is what makes the mock bus possible.

**HAL mode**: with `-DHAL=ON`, `SRC_FILES` becomes only `src/hal/libsas.cpp` (a 13,693-byte shim), while `sas_static_testable` still uses the full sources.

**Presets** (`CMakePresets.json`, version 3, Ninja generator): `Debug`, `Release`, `HALDebug`, `HALRelease`, `PreviewRelease`, `PreviewHALRelease`. All inherit a hidden `Default` with `HAL=OFF`, `PREVIEW=OFF`, `CMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded` (Debug overrides to `MultiThreadedDebug`). `PREVIEW=ON` ships binaries without the public header (`LIB_PUBLIC_HEADER` is a generator expression that empties under PREVIEW, `CMakeLists.txt:76`) and skips installing `examples/`.

Install layout (Release only, `CMakeLists.txt:229-261`): `${QXDIST_DIR}/lib/${QXARCH}`, `${QXDIST_DIR}/lib/static/${QXARCH}`, `${QXDIST_DIR}/bin/${QXARCH}`, `${QXDIST_DIR}/include`. With qxcmake ≥ 2.7.0.0 the package also ships `sasConfig.cmake` so consumers can `find_package(sas CONFIG)`.

Utilities: `poller` commented out (`:267`); `sashost` behind `option(SASHOST … OFF)` (`:274`); `hwconformance` added under `if(BUILD_TESTING)` (`:217`); `examples` always.

### 6.2 Artifacts actually on disk

`ls -la` of `lib/`, `bin/`, `out/` — **all Debug/x64, no Release anywhere.** Two parallel trees exist (`bin/Debug/x64/` and `bin/x64/Debug/`), the first newer:

| Path | Size | Modified |
|---|---|---|
| `bin/Debug/x64/libsas.dll` | 2,179,072 | 2026-09-01 14:15 |
| `bin/x64/Debug/libsas.dll` | 2,167,296 | 2026-08-10 23:40 |
| `lib/x64/Debug/libsas.lib` | 27,694 | 2026-08-10 23:40 |
| `lib/Debug/static/x64/libsas.lib` | — | static build |
| `lib/static/x64/libqxcom.lib`, `libnvram_dbg.lib` | — | dependency statics |

`file` reports both DLLs as `PE32+ executable for MS Windows 6.00 (DLL), x86-64, 9 sections`.

Also built: `bin/Debug/x64/whitebox_tests.exe`, `blackbox_test_dbg.exe`, `hwconformance_dbg.exe`, `sas-example_dbg.exe`, plus `libqxcom.dll` and `libnvram.dll` alongside.

`out/build/` holds two Ninja build trees, `Debug` and `igs203`.

### 6.3 Could a .NET tool P/Invoke this today?

**Mechanically yes; practically no, and for a reason that matters.**

Yes: `strings` on `bin/x64/Debug/libsas.dll` shows the export table carries the undecorated C names — `SAS_SetClientID`, `SAS_WriteException`, `SAS_RTE_GameStart`, `sasInit`, `sasAddSasChannel`, `sasAftGmToHost`, `sasCompleteCashout`, `sasCreateWagerCategory`, and so on. The DLL also embeds `SAS_LIB_PROD_ID=D_LIB_SAS_WIN_D64` and `SAS_LIB_VERSION=5.4.4.4`.

**But note the version skew**: the on-disk DLL reports `5.4.4.4` while the current header says `5.4.4.32` (`libsas.h:46-50`). The binary is **28 builds stale**. A .NET binding written against the current header would not match it.

**And the blocking problem**: `sasInit` calls `validateQuixantPlatform()` (`src/libsas_windows.cpp:66-121`), which — unless `VALIDATION_DEBUG` is defined, and line 75 shows it commented out in the shipped source — opens `\\.\QX10`, issues `DeviceIoControl` with `CTL_CODE(FILE_DEVICE_UNKNOWN, 0x828, …)` twice 10 ms apart, and requires the FPGA timer value to have changed. On any machine without the Quixant core driver this returns `QXT_INVALID_PLATF_ERR_OFFSET` (0x8000000E) and the library will not initialise. The root `CMakeLists.txt:186-190` says so plainly: the test-only target exists to "let the black-box tests call `sasInit` on a non-Quixant CI runner WITHOUT modifying any library source file."

**And the fundamental mismatch**: even where it initialises, this library is the **EGM**. P/Invoking it gives you a machine to be polled, not a host that polls. A host tool gets nothing from it except the EGM half of a loopback rig — which is exactly what `hwconformance` builds, and it links the static testable library rather than the DLL for precisely these reasons.

Practical conclusions for a replacement:
- For **host** behaviour: bind nothing. Implement the host directly over the transport, as `sashost`/`poller`/`hwconformance` all do.
- For an **EGM simulator** on the far end: link `sas_static_testable` (or a `VALIDATION_DEBUG` build) from C++, do not P/Invoke the shipped DLL.
- If a .NET binding is wanted anyway: it must target a freshly built DLL (5.4.4.32+), mirror `#pragma pack(8)`, and either run on Quixant hardware or on a build with platform validation compiled out.

---

## 7. CHANGELOG — timeline and SAS support

`CHANGELOG.md`, 99,861 bytes, **275 version entries**. Newest first, most recent `5.4.4.32` dated 2026/09/03; the tail reaches back to `1.3.0.0` (cashout validation rework, first NVRAM storage, first LP 3D/4D/57/58/7C implementations).

Entry format: `#### **_Version 5.4.4.NN_** - _Date: YYYY/MM/DD_`, then an italic category heading, then `+ [bugfix]` / `+ [new]` bullets. Categories seen in the recent window: _AFT_ / _Advanced funds transfer_, _Communication_, _Exceptions_, _Game information_, _Build_, _Handpay_, _Validation_, _Progressives_, _Tickets and validation_, _Ticket redemption_, _Real time event reporting_, _SAS timing and RTE behaviour_, _Tournament_-adjacent, _Tests_, _Cleanup_.

The prose style is notable and matches the repo's CLAUDE.md rules: it describes observable behaviour and cites the spec section, never internal class or method names. Example (5.4.4.29): "With real time event reporting enabled, an exception first reported in response to a long poll is no longer reported a second time on the general poll that follows it. SAS 6.02 Table 3.1 makes that general poll an implied acknowledgment of the long poll response."

**SAS version support: 6.02 only.** `grep` over the changelog finds 13 occurrences of "SAS 6.02" and one of "5.02"; `include/libsas_internal.h:218` hard-codes `SAS_VERSION "602"` as the three bytes returned by LP 54.

Recent feature timeline (last ~15 entries, all 2026/08-09) — a useful read on where the protocol risk currently lives:

- **5.4.4.32** — HAL build now exports every declared entry point (5 were missing).
- **5.4.4.31** — LP 1B handpay amount in accounting-denomination units (§Table 7.8); sub-cent Table C-4 codes 1B-1F rejected.
- **5.4.4.30** — Linux build fixed (stale per-channel TX thread references).
- **5.4.4.29** — RTE implied-ack: no double report on the general poll after a long-poll response (Table 3.1).
- **5.4.4.28** — **new** `sasAftCancelGmToHost()` withdraws an outstanding cashout-to-host request (§8.7).
- **5.4.4.27** — callback registration no longer races a host meter poll or an LP 74 lock request.
- **5.4.4.26** — LP 74 naming an unsupported transfer condition now withdraws a standing lock as well as answering FF (§8.2).
- **5.4.4.25** — LP 56 lists only games at the current player denomination (§16.2); multi-denom preamble denomination 00 answers identically. (This is the rule extracted into `lp56_enabled_games.h`.)
- **5.4.4.24** — LP 7E reports machine local time, not UTC (§7.18).
- **5.4.4.23** — LP A0 answered per-channel capabilities rather than machine-wide.
- **5.4.4.22** — unread secure-enhanced validation records re-reported across a restart (§15).
- **5.4.4.21** — GM→host transfer accepted while a cash-out to host is pending (§8.7).
- **5.4.4.20** — exception 51 no longer reported with no handpay outstanding (§7.8).
- **5.4.4.19** — LP 53 for game 0000 reports the largest max bet among *available* games (§7.6.5).
- **5.4.4.18 / .17** — queued exceptions ordered by priority across a power cycle; unread cash-out validation record reported at startup (§15.10).

---

## 8. The legacy tool being replaced: `utils/igt/`

Worth recording, since the brief is "specify a replacement for a legacy tool".

`utils/igt/sastest.exe` — `file` reports **`PE32 executable for MS Windows 4.00 (GUI), Intel i386`**. A 985 KB **32-bit Windows GUI** application with a `.HLP` file (`SASTEST.HLP`, WinHelp) and a `.GID` index — i.e. a tool from the WinHelp era, before Windows Vista dropped `winhlp32`. Configuration is `sastest.ini`, 1,735 lines, 56 KB, with 27 sections:

`[SAS Protocols]` `[Communications]` `[General Poll]` `[Long Poll Commands]` `[Electronic Funds Transfer]` `[Status Messages]` `[Messages]` `[Denominations]` `[Ticket Transfer Codes]` `[Ticket Machine Status Codes]` `[Validation Type Codes]` `[AFT Transfer Status Codes]` `[AFT Transaction Receipt Status]` `[Receive Validation Status Codes]` `[ASDC]` `[Bonus Game Data Structures]` `[Message Structures]` `[Data Structures]` `[Quick Commands]` `[Script]` `[AFT]` `[Meter Test]` `[Exception Handling]` `[LongPoll Handling]` `[RNGMode]` `[Active Help]` `[end]`

Its "scripting" is a list of **raw hex frames** with no structure, no assertions, no timing, and no result:

```ini
[Quick Commands]
017202ff00
017301ff
017400019000
0174ff000000
014C000001000000
81
016F0e0000000001000200030004000d00

[Script]
80 = Sending Sync
81 = General poll
;0193 = Unimplemented
;0111 = Coin In
;007f0308200114301424ca = set clock
```

Its output convention is preserved in `utils/igt/test/*.txt` — a `TX>=` / `RX<=` hex pair followed by a hand-decoded field list:

```
TX>= 01 6E 01 00 35 10
RX<= 01 6E 0A AE FF 01 00 01 04 00 00 00 00 D9 F8

$6E = AUTHENTICATION INFO: length = 0A
$6E = Component List CRC    = AEFF
$6E = Status                = 01
```

`examples/README.md` records the dependency: "it can simulate all the protocol messages with IGT simulator."

**What the legacy tool has that our in-house tools do not**: (a) breadth — an INI-driven table of *every* long poll, status code, and data structure in SAS, so any frame can be built by hand; (b) a decoded field view of responses; (c) an interactive operator UI with contextual help. **What it lacks**: any notion of an expectation, a pass/fail verdict, an exit code, repeatability, scripting beyond raw hex, cross-platform support, or 64-bit. It is 32-bit i386 with WinHelp — it will not survive much longer on modern Windows.

**The replacement's job, stated plainly**: `sastest.exe`'s breadth and field decoding, married to `sashost`'s repeatability, expectations and exit codes, with `hwconformance`'s reporting and `sas_blackbox.h`'s hardware-free transport.

---

## 9. Risks and unknowns

### Risks

1. **QXCOM 9-bit is the hard dependency.** SAS needs the wakeup bit to mark an address byte. Every host in this repo gets it from libqxcom (`sas_link.h:34-35`). A replacement targeting generic hardware must first establish that a 9th bit is reachable at all — mark/space parity tricks on a standard UART, or a specific USB-serial chipset. Not proven anywhere here.
2. **Two QXCOM channels in one process is unsafe.** `hwconformance.cpp:20-24` documents intermittent access violations and works around it with three processes. Any design that wants an EGM simulator and a host in one process inherits this.
3. **The prebuilt DLL is stale and platform-gated.** 5.4.4.4 on disk vs 5.4.4.32 in the header, and `sasInit` requires the `\\.\QX10` core driver.
4. **`sashost`'s "exception" detection is a heuristic.** Every non-zero RX byte outside a 150 ms window is recorded as an exception code. Any tool that widens the long-poll surface will produce phantom exceptions unless it parses frames properly first.
5. **`sashost` is orphaned.** `SASHOST=OFF`, no ctest entry, and the "verified by the unit test" claim in `sas_link.h:27` has no test behind it. Nothing detects a regression in it.
6. **`utils/poller` is dead code.** Commented out of the build; its README already disagrees with its source on `INTERVAL` (40 ms in code, 200 ms in README) and `CHIRP_WINDOW` (`INTERVAL*100` in code, `INTERVAL*5` in README).
7. **Documentation drift is real and observable.** The `expect-alive` verb missing from the sashost README table; the stale group-0 comment in `spec_10_2.sas`; the poller README; `legacyhandpay` implemented but unregistered. A replacement needs generated docs or a doc test, not hand-maintained tables.
8. **Timeout values are folklore.** `ReadTimeout` is 20 ms in sashost, 5000 ms in poller and hwconformance, the latter annotated "DIAGNOSTIC: matching utils/poller's known-good value". Nobody has written down why.
9. **`POLLING.txt` has no date**, only `HH:MM:SS.mmm` — a run crossing midnight is ambiguous and cross-log correlation is manual.
10. **Real-hardware scenarios take real time.** The blackbox burst cases carry ctest timeouts up to 230 s; the sashost cadence scenarios each run 60 s of assertion plus 8 s settling. A full matrix is tens of minutes, which constrains where it can run.

### Unknowns

- **UNKNOWN**: whether libqxcom can drive a generic USB CDC adapter, or whether it requires Quixant hardware. Nothing in this repo says.
- **UNKNOWN**: `qxComOpen`'s accepted port-name syntax. Callers pass `"com3"` / `"/dev/ttyS2"` bare; whether `\\.\COM12` (needed for COM10+) works is not shown here. The library's own node is `\\.\QXCOM`.
- **UNKNOWN**: whether `sashost` has ever been run in CI or only by hand. `.github/workflows/ci.yml` was not examined in detail.
- **UNKNOWN**: whether `sastest.exe`'s INI tables are redistributable, and whether they can be lifted as a data source for a long-poll catalogue.
- **UNKNOWN**: what `docs/sas-spec.txt` (573 KB) and `docs/SAS-6-02-Protocol.pdf` (2.1 MB) contain in structured form — a machine-readable long-poll table there would be the ideal catalogue source, but neither was parsed.
- **UNKNOWN**: whether the `loggerParser` description strings in `src/libsas_logger.cpp` cover enough of the command set to be reused as a decoder, or whether "Unparsed LP" dominates in practice. In this capture, 5 of 110 records were unparsed — but the capture only exercises a dozen commands.
- **UNKNOWN**: whether any existing consumer parses `POLLING.txt`-format logs. Nothing in this repo does.
- **UNKNOWN**: what `sas_static_testable`'s licensing/distribution status is — it is explicitly not installed, so shipping a tool that links it may need a decision.

---

## Appendix A — file inventory

| Path | Bytes | What |
|---|---|---|
| `include/libsas.h` | 87,518 | Public API: 122 exception codes, 124 long-poll codes, meter codes, all structs, 45 legacy `SAS_*` functions, `SAS_API` macro |
| `include/libsas_wrapper.h` | 16,588 | Modern `sas*` wrapper API (~60 functions) — installed |
| `include/libsas_db.h` | 44,059 | Internal C++ state/DB header (`sasContext`, channels, serial callbacks) — not installed |
| `include/libsas_internal.h` | 23,167 | Internal declarations; `SAS_VERSION "602"` at :218 |
| `include/lp.h` | 12,326 | 124 EGM-side long-poll handler declarations |
| `include/lp56_enabled_games.h` | 8,010 | 3 state-free `inline` predicates for LP 56 |
| `include/libsas_uart.h` | 3,155 | UART enums, `DEV_NODE`, 5 serial prototypes |
| `include/libsas_logger.h` | 2,900 | Logger declarations |
| `src/lp.cpp` | 391,618 | Long-poll implementations |
| `src/libsas_internal.cpp` | 250,876 | Core state machine |
| `src/libsas_wrapper.cpp` | 185,592 | Wrapper API |
| `src/libsas_threads.cpp` | 167,469 | Thread/timer machinery |
| `src/libsas.cpp` | 114,618 | Legacy API |
| `src/libsas_logger.cpp` | 44,908 | Trace logger + `loggerParser` |
| `src/libsas_uart.cpp` | 23,643 | Serial RX/TX + `HOST-n`/`EGM-n` tagging |
| `src/libsas_windows.cpp` | 34,049 | Win32 platform layer + `validateQuixantPlatform` |
| `src/libsas_linux.cpp` | 50,371 | Linux platform layer |
| `src/hal/libsas.cpp` | 13,693 | HAL shim |
| `utils/sashost/*` | ~64 KB + 10 scenarios | **The host prior art** |
| `utils/poller/poller.cpp` | 21,207 | Raw host probe, not built |
| `utils/hwconformance/hwconformance.cpp` | 95,721 | 3-process real-hardware conformance harness |
| `utils/hwconformance/README.md` | 22,869 | Excellent reporting/diagnostics documentation |
| `utils/POLLING.txt` | 24,440 | 330-line captured session (libsas logger format) |
| `utils/igt/sastest.exe` | 985,088 | Legacy 32-bit IGT GUI host tool |
| `utils/igt/sastest.ini` | 56,491 | Its 27-section protocol catalogue |
| `test/blackbox/sas_blackbox.h` | 11,963 | **In-memory mock serial bus** |
| `test/blackbox/blackbox_test.cpp` | 200,365 | 18 scenarios, 34 ctest cases |
| `test/blackbox/README.md` | 30,617 | Per-scenario rationale |
| `test/whitebox/whitebox_test.cpp` | 380,693 | 343 gtest cases |
| `test/whitebox/README.md` | 33,979 | |
| `CHANGELOG.md` | 99,861 | 275 versions |
| `docs/SAS-6-02-Protocol.pdf` | 2,108,443 | The spec |
| `docs/sas-spec.txt` | 573,726 | Text form of the spec |

## Appendix B — commands run

```
ls -la /c/Progetti/libsas/ ; ls -la /c/Progetti/libsas/.codegraph   → NO CODEGRAPH
ls -la include/ utils/ lib/ bin/ out/ src/ test/ docs/ examples/ inlines/
find lib -type f ; find bin -type f ; find out -maxdepth 3
cat utils/sashost/{README.md,CMakeLists.txt,sas_link.h,sas_link.cpp,host_engine.h,
    host_engine.cpp,script.h,script.cpp,sashost.cpp,gui.h,gui.cpp}
cat utils/sashost/scenarios/*.sas
grep -n 'extern *"C"\|__declspec\|dllexport\|SAS_API' include/*.h
sed -n '2585,2673p;1,120p' include/libsas.h
cat include/libsas_wrapper.h include/lp.h include/lp56_enabled_games.h include/libsas_uart.h
sed -n '232,240p;233,728p;1545,1600p;2495,2595p' include/libsas.h
cat CMakeLists.txt CMakePresets.json README.md
cat utils/poller/{README.md,CMakeLists.txt} ; grep -n ... utils/poller/poller.cpp
cat utils/hwconformance/CMakeLists.txt ; sed -n '1,466p' utils/hwconformance/README.md
sed -n '1,110p;143,180p;860,980p;1475,1600p;1911,2114p' utils/hwconformance/hwconformance.cpp
head -60 utils/POLLING.txt ; tail -50 utils/POLLING.txt ; wc -l utils/POLLING.txt
grep -oE '^[A-Za-z0-9_-]+ +- ' utils/POLLING.txt | sort | uniq -c
grep -E '^ {20,}' utils/POLLING.txt | sed 's/^ *//' | sort | uniq -c | sort -rn
cat test/CMakeLists.txt test/whitebox/CMakeLists.txt test/blackbox/sas_blackbox.h
grep -cE '^(TEST|TEST_F|TEST_P)\(' test/whitebox/*.cpp test/blackbox/blackbox_test.cpp
grep -n 'add_test' test/blackbox/CMakeLists.txt ; grep -n 'legacyhandpay' test/blackbox/CMakeLists.txt
sed -n '4263,4297p' test/blackbox/blackbox_test.cpp
head -100 CHANGELOG.md ; grep -cE '^#### \*\*\*?_Version' CHANGELOG.md
grep -onE 'SAS 6\.0[0-9]|5\.02' CHANGELOG.md | sort | uniq -c
sed -n '100,180p;60,100p' src/libsas_logger.cpp
grep -rn 'loggerAdd(' src/*.cpp ; grep -n 'strLogger' src/libsas_uart.cpp
sed -n '3143,3200p' src/lp.cpp ; grep -n 'SAS_VERSION' include/libsas_internal.h
grep -rn 'VALIDATION_DEBUG' src/*.cpp ; sed -n '55,130p' src/libsas_windows.cpp
file bin/*/x64/libsas.dll bin/x64/Debug/libsas.dll
"/c/Program Files/Strings/strings" -n 6 -nobanner bin/x64/Debug/libsas.dll | grep -E '^(sas|SAS_)'
file utils/igt/sastest.exe ; grep -aoE '^\[[^]]+\]' utils/igt/sastest.ini
awk '/^\[Script\]/,/^\[AFT\]/' utils/igt/sastest.ini
git log --oneline -8 ; git branch --show-current ; git status --short
cat docs/LOGGING.md ; head -60 examples/README.md
```
