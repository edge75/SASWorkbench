# 11 - Driving the EGM application: QSP_DemoOpenGL

Repo: `C:\Progetti\QSP_DemoOpenGL` (v2.0.4.6, CHANGELOG dated 2026-09-03). No `.codegraph/` -> read with grep/Read.
Every claim below cites a file I read or a command I ran. `UNKNOWN` marks anything I could not confirm.

---

## 0. Executive summary (the two things that decide the design)

1. **The DIO/button/intrusion/bill surface is already remotely drivable, today, with no code change** -
   in a `HAL` build the platform's QPM layer is `qpm-src/src/hal/libqpm.cpp`, which polls a 1 KB
   named shared-memory segment `QpmSharedMemory` every 125 ms and turns bit changes into button,
   door, note-acceptor and printer events. Writing `dio_mask` bit 4 is literally "press Play".
   (`C:\Progetti\QSP_DemoOpenGL\out\build\HALDebug\_deps\qpm-src\src\hal\libqpm.cpp:705-912`)

2. **...but the `HAL` build has NO SAS.** `C:\Progetti\libsas\CMakeLists.txt:68-71` -
   `if(NOT HAL) set(SRC_FILES ${FULL_SRC_FILES}) else() set(SRC_FILES src/hal/libsas.cpp)`.
   `C:\Progetti\libsas\src\hal\libsas.cpp` is a 130-line stub: `sasInit()` is
   `{*sasHandlePtr = (void*)0x01; return QXT_LIB_SUCCESS;}`, `sasSetSerialCallbacks()` is `{ }`,
   every write/exception entry point returns success and does nothing. QSP reinforces this:
   `C:\Progetti\QSP\src\LibMarketSas\LibMarketSas.cmake:22-34` links `qxcom_static` only when
   `NOT HAL`, and defines `HAL_MODE` otherwise; `SasNetwork.cpp:259-267` then compiles out
   `sasSetSerialCallbacks()` entirely (`#pragma message("SAS Serial callbacks disabled")`).

   Verified on the binaries already built on this machine:
   - `bin\Debug\x64\libmarketsas.dll` (HAL=OFF) contains `\\.\QX10` and `SAS_LIB_VERSION=5.4.4.30`
   - `bin\HALDebug\x64\libmarketsas.dll` (HAL=ON) contains `SasLib` (the stub's
     `SAS_LibInventory` string) and **no** `QX10`

3. **And the non-HAL build cannot start SAS on a laptop.** `sasInit()` calls
   `validateQuixantPlatform()` first (`C:\Progetti\libsas\src\libsas_wrapper.cpp:132-147`), which on
   Windows opens `\\.\QX10` and polls an FPGA timer via `DeviceIoControl`, returning
   `QXT_INVALID_PLATF_ERR_OFFSET` if absent, unless `VALIDATION_DEBUG` is defined
   (`C:\Progetti\libsas\src\libsas_windows.cpp:67-124`). On Linux the same gate opens `/dev/qxtio`
   unless `_DEBUG` is defined (`src\libsas_linux.cpp:122-150`). `VALIDATION_DEBUG` is applied
   **only** to the test-only `sas_static_testable` target (`libsas\CMakeLists.txt:197-207`), never to
   the `sas_static` that `marketsas` links.

**Net:** there is no existing build configuration of QSP_DemoOpenGL that is both (a) runnable on a
laptop with no Quixant hardware and (b) speaking real SAS. Making one is the single biggest
prerequisite for the whole conformance plan. See section 7 for the smallest change that gets there.

---

## 1. What the program is

An OpenGL/GLFW demo gaming application built on QSP (Quixant Software Platform), linking
`render qsp marketsas` (`CMakeLists.txt:145`).

- **Four games** (`QspCreateCfg.number_games = 4`, `src/GameApplication/Main.cpp:120`) and
  **two progressive levels** (`number_progressive_levels = 2`, ibid:121):
  `GameManager::CreateGame()` (`GameManager.cpp:458-500`) makes game index 0 and 1 =
  `Game01::SlotGame` (5-reel, 243-ways), index 2 and 3 = `Game02::WheelGame`.
- **Game select screen is index 0**; games are `[1,65535]` (`GameManager.cpp:5-8`).
- **Money model:** credits live in the platform, not the game. The game reads
  `qspGetAvailableCredits()` and bets via `qspGameStart()`; the platform owns the credit meter.
  Bet = `denomination * bet_factor_a * 50` where `bet_factor_b = 50` lines
  (`SlotGame_01.cpp:412-447`). Default denomination set is `{1,2,50,100}` cents
  (`GameManager.cpp`, `kDenominationValues[0]`), so game 1 at bet factor 1 costs **50 cents**.
- **Slot game state machine** (`SlotGame_01.h:67`):
  `enum class GameState {eIdle, eSpin, eWin, eWaitFeature, eFeatureSummary}`.

  Round flow, all in `SlotGame_01.cpp`:
  `ProcessButton(ePlay)` while `eIdle` -> `StartGame()` (`qspGameStart` takes the bet, opens a game
  record) -> `StartSpin()` (RNG stop positions, `qspGameReelStopPositions()` reports them to SAS) ->
  `CallbackSpinComplete()` -> `CalculateWin()` -> optional `ProgressiveWin()` /
  `CallbackStartWinAnimations()` -> `CallbackGameComplete()` (handles free games / feature) ->
  `CallbackGameEnd()` -> `qspGameEnd()` -> `GameManager::RequestAutoRebet()`.
- **Rendering loop** is `DemoApplication::Render(delta_time)` which first calls
  `ProcessQspMessages()` - the QSP callback queue is drained once per frame (`Main.cpp:206-227`).
  A window is mandatory (GLFW + OpenGL); there is no headless mode.
- **Audit menu is the platform's**, not the game's: `AuditManager` just calls
  `qspGetAuditFramebuffer(touch_down, x, y, &framebuffer)` and blits it
  (`Audit/AuditManager.cpp:302`). All configuration (SAS address, denominations, BV/printer
  protocol, NVRAM reset) happens by touching pixels on that framebuffer.
- **Diagnostics go to stdout**: `#define Diag printf` (`SlotGame_01.cpp:16`), plus `printf` in
  `Main.cpp` and `GameManager.cpp`. Lines a harness can parse:
  `[SlotGame #N] StartGame()`, `[SlotGame #N] CallbackSpinComplete()`, `SelectGame(N)`,
  `[GameManager] Auto rebet enabled/disabled`, `Diagnostic DIN: line %u down/up`,
  `AFT registration: status 0x%02x`.

### Keyboard shortcuts in the shipping `src/GameApplication/Main.cpp` (`KeyDown`, lines 240-274)

| Key | Effect |
|---|---|
| `0` / `-` / `+` / Backspace | time factor 0 / 0.1 / 1.0 / 4.0 (`kTimeFactor`) - **speeds the game up 4x** |
| Space | `AsyncProcessButtonDown(4, true)` = press DIN 4 = **Play** |
| F9 | toggle diagnostic DIN session (`qspStartDiagnosticDin`/`qspStopDiagnosticDin`) |
| F10 | `qspAftRegistrationRequest()` (request or acknowledge, decided by current status) |
| F11 | `qspAftRegistrationCancel()` (unregister) |

There is **no keyboard shortcut for Collect, Bet, bill-in or door** in the shipping main.

---

## 2. The KEYBOARD_INTRUSION flag - what it actually is

`src_keyboard_intrusion/` (5 files, 21 KB total). Enabled by `-DKEYBOARD_INTRUSION=ON`
(README) or the `KeyboardIntrusion` CMake preset (`CMakePresets.json`, inherits `Debug`).
`CMakeLists.txt:60-63` includes `src_keyboard_intrusion/keyboard_intrusion.cmake` when the variable
is *defined*, which does two things:

1. `target_sources(qsp PRIVATE src_keyboard_intrusion/LibPlatform/libqsp.cpp)` - compiles an extra
   translation unit **inside the qsp shared library**, so it gets QSP's private headers
   (`Platform.h`, `ConditionHandler.h`, `AuditHandler.h`, `NvramHandler.h`, `MoneyInHandler.h`).
2. `set(SRC_MAIN_CPP .../src_keyboard_intrusion/GameApplication/Main.cpp)` - swaps the demo's `main`
   for a variant with more keys.

### The injected API (`src_keyboard_intrusion/include/keyboard_intrusion_api.h`)

```c
QSP_API	QRESULT		qspTestSoftwareKeySwitch(uint32_t switch_idx, bool enter);
QSP_API	QRESULT		qspTestBillValidator(uint64_t amount);
QSP_API	QRESULT		qspTestIntrusion(uint32_t intrusion, bool open);
```

Implementation (`src_keyboard_intrusion/LibPlatform/libqsp.cpp`) reaches straight into the platform
singletons - it does **not** go through QPM:

- `qspTestSoftwareKeySwitch(0, enter)` = **Audit keyswitch**: checks
  `condition_handler->IsAuditAllowed()`, then `AddCondition(Qsp::Condition::eSwitchAudit)` and posts
  `AuditHandler::AsyncSetAuditMode(true)`.
- `qspTestSoftwareKeySwitch(1, enter)` = **Reset keyswitch**:
  `AddCondition(Qsp::Condition::eSwitchReset); condition_handler->ResetKeyswitch();`
- `qspTestBillValidator(amount)` = **note in**, bypassing the acceptor entirely:
  ```cpp
  Qsp::NvramBillTicketMeters *bill_meters = Qsp::NvramHandler::GetInstance()->GetBillTicketMeters();
  bill_meters->mPendingBanknoteIn.Write(amount);
  reinterpret_cast<Qsp::MoneyInHandler *>(...eMoneyInHandler)->BanknoteIn(amount);
  ```
- `qspTestIntrusion(idx, open)` = **doors**, over a fixed 5-entry table:
  `{eIntrusionTamper1, eIntrusionTamper2, eIntrusionMainMech, eIntrusionLogic, eIntrusionStacker}`
  -> `condition_handler->AddCondition(...)` / `RemoveCondition(...)`.

### The extra keys (`src_keyboard_intrusion/GameApplication/Main.cpp:250-315`)

| Key | Call |
|---|---|
| F1 | `qspTestSoftwareKeySwitch(0, !fAuditVisible)` - enter/leave audit |
| F2 | `qspTestSoftwareKeySwitch(1, toggle)` - reset keyswitch |
| F5 | `qspTestIntrusion(2, toggle)` - **main door** open/close |
| F6 | `qspTestIntrusion(3, toggle)` - **logic door** open/close |
| B  | `qspTestBillValidator(5000)` - insert a **$50** note (hard-coded) |
| Q  | `qspCollectButtonPressed()` - **cash out** |
| Space | Play (same as shipping main) |

### Verdict: it is exactly the hook we want, but it is stale, and it is a *keyboard* hook

- **It is a real, deliberate test affordance**, and the pattern (an extra TU compiled into `qsp` +
  `QSP_API` functions) is the cheapest way to add any further stimulus we need.
- **All the QSP symbols it depends on still exist** on the QSP branch this repo pins
  (`bv-pending-banknote-replay`, `CMakeLists.txt:44`): `NvramHandler.h:454 mPendingBanknoteIn`,
  `ConditionHandler.h:97/104/109/110`, `MoneyInHandler.h:30 BanknoteIn`,
  `AuditHandler.h:101 AsyncSetAuditMode`, `Conditions.h:17-51`. So `libqsp.cpp` should still compile.
- **`src_keyboard_intrusion/GameApplication/Main.cpp` is stale and I expect it not to link.**
  It includes `../../src/GameApplication/DemoApplication.h`, which declares
  `void FrameResized(const float, const float) override;`, `ToggleDiagnosticDinSession()` and
  `AftRegistrationAction(bool)` (`DemoApplication.h:32,37,38`) - and the keyboard-intrusion `main`
  defines **none of them**. `FrameResized` is virtual with an inline body in the base
  (`QSP\src\LibRender\Yarra\Application.h:33`), so the base is fine, but `DemoApplication`'s own
  override is declared-not-defined and is referenced by `DemoApplication`'s vtable.
  *(Not empirically confirmed - I did not build. High confidence; verify by configuring the
  `KeyboardIntrusion` preset.)*
- It also still has `default: assert(0);` in `ProcessQspMessages()` (`:432`) and handles none of
  `QSP_CALLBACK_AUDIT_DIAGNOSTIC_DIN`, `QSP_CALLBACK_AFT_REGISTRATION`,
  `QSP_CALLBACK_GAME_AUTO_REBET` - exactly the three callbacks CHANGELOG 2.0.4.5 says a build
  against the newer QSP "would otherwise have asserted on". So even if it links, it will assert at
  runtime the moment a host enables auto rebet or an AFT registration arrives.
- No `out/build/KeyboardIntrusion` directory exists on this machine (only `Debug`, `HALDebug`,
  `HALRelease`, `Release`), consistent with it not having been built recently.
- **Structurally it is the wrong shape for an orchestrator**: the stimulus is a keystroke into a
  focused GLFW window, and the note value is hard-coded to 5000. Driving it means synthetic key
  events (`xdotool` is in `tools/scripts/prepare_system.sh`'s package list; `SendInput` on Windows)
  with no acknowledgement. The shared-memory route (section 3) is strictly better.

---

## 3. How a game is played programmatically - the full path

### 3a. From outside the process, via qpm_simulator shared memory (HAL builds only)

`qpm-src/src/hal/libqpm.cpp` (the HAL implementation of LibQPM, built when `HAL=ON`):

```cpp
// libqpm.cpp:152-161
struct QpmSharedState
{
    uint32_t  hearbeat;        // +0
    uint32_t  intrusion_mask;  // +4
    uint32_t  dio_mask;        // +8
    uint32_t  lamps[32];       // +12 .. +139
    uint32_t  bna_mask;        // +140
    uint32_t  printer_mask;    // +144
};
static_assert(sizeof(QpmSharedState) < 1 * 1024);
```

mapped over the 1 KB segment named `QpmSharedMemory`
(`qpm_simulator-src/include/QpmSharedMemory.h`, `lsm::Memory fMemory("QpmSharedMemory", 1024, true)`,
i.e. the `libsharedmemory` library; the identical class also lives at
`qpm-src/src/hal/QpmSharedMemory.h`).

`QpmWrapper::QpmThread` (`libqpm.cpp:705-912`) runs a **125 ms** loop that XORs the current state
against the previous one and fires QPM device-to-client callbacks per changed bit:

| Field | Bit meaning | Emitted event |
|---|---|---|
| `dio_mask` | bits 0-31 = DIN lines | `D2C_GPIO_RISING_EDGE` / `D2C_GPIO_FALLING_EDGE`, `Argument = line` |
| `intrusion_mask` | bits 0-15 = intrusion index | `D2C_DOOR_OPEN` / `D2C_DOOR_CLOSED` |
| `bna_mask` | bits 0-7 = a note from `{$1,$2,$5,$10,$20,$50,$100,$200}` | `D2C_ACCEPTOR_PENDING_NOTE` with the value in cents |
| `bna_mask` | bit 8 = BV link down, 9 = jam, 10 = stacker full, 11 = stacker removed | `D2C_SERIAL_DEVICE_LINK_*`, `D2C_ACCEPTOR_STATE_CHANGED` |
| `printer_mask` | bit 0 = link down, 1 = jam, 2 = paper out, 3 = paper low | `D2C_PRINTER_STATE_CHANGED` |
| `lamps[32]` | written **by** QPM, read by the simulator | lamp state per DOUT |
| `hearbeat` | incremented by QPM every 125 ms | liveness probe for the orchestrator |

Then QSP: `ButtonHandler::QpmDinCallback` (`QSP\src\LibPlatform\ButtonHandler.cpp:150-225`) filters
the audit/reset keyswitches and market DINs, checks
`condition_handler->AreInhibitingConditionsActive()`, and posts
`{QSP_CALLBACK_PHYSICAL_BUTTON, data1=din, data2=down}` to the game.
`SecurityHandler.cpp:356` consumes `D2C_DOOR_OPEN`.
`BillValidatorHandler.cpp:730-760` consumes `D2C_ACCEPTOR_PENDING_NOTE` and drives the escrow
state machine, ending in `qpmBVActOnWaitingNoteOrTicket(..., QPM_BV_ACTION_ACCEPT)` (`:1196`),
which the HAL answers with `D2C_ACCEPTOR_NOTE_ACCEPTED` (`libqpm.cpp:914-930`).

Then the game: `Main.cpp:355` `case QSP_CALLBACK_PHYSICAL_BUTTON: AsyncProcessButtonDown(...)` ->
`GameManager::AsyncPhysicalButtonDown(din, down)` (`GameManager.cpp:753`) -> table lookup ->
`ProcessButton(button, data)` -> `SlotGame::ProcessButton` / `GameSelect::ProcessButton`.

**The DIN table is data, from `src/game_configuration.json` -> `Resources/game_configuration.json`**
(`InitializePhysicalButtonMapping()`, `GameManager.cpp` lines ~67-115). Shipped mapping:

| DIN | `button_type` | data | label |
|---|---|---|---|
| 0 | collect | 0 | Collect |
| 2 | service | 0 | Service |
| 3 | keyswitch | 0 | Reset/JKey |
| **4** | **play** | 0 | Play |
| 5 | keyswitch | 0 | Audit |
| **6** | **game_menu** | 0 | Game Menu |
| 7..11 | bet | 1..5 | Bet x1..x5 |
| 14,15 | none | | Candle #1/#2 |

`DemoApplication::ReadyToRun()` registers `qspSetDigitalInMask(0x0000ffff)` - **DIN 0-15 only**
(`Main.cpp:190-192`).

**So "play 2 games" from outside the process, in a HAL build, is:**

```
(once)  pulse DIN 6  -> GameSelect::ProcessButton(eGameMenu) -> SelectGame(1)   [GameSelect.cpp:117]
(once)  pulse DIN 7  -> bet factor 1                                            [SlotGame_01.cpp:331]
(x2)    pulse DIN 4  -> StartGame() -> spin -> qspGameEnd()                     [SlotGame_01.cpp:326]
        wait for "[SlotGame #1] CallbackSpinComplete()" / credit-meter change between presses
(cash)  pulse DIN 0  -> qspCollectButtonPressed()                               [SlotGame_01.cpp:339]
```
"Pulse" = set the bit, wait >= 125 ms, clear it. Only the rising edge acts
(`AsyncProcessButtonDown` returns early when `!down`, `Main.cpp:506-509`).

### 3b. From inside the process, via the on-screen button deck (any build)

`ButtonDeck::MouseDown(x, y, down)` (`ButtonDeck.cpp:123-135`) hit-tests `kButtonDefs` and calls
`GameManager::ProcessButton()` directly. The deck has Collect, Game Menu, Bet x1/x2/x3 and Play
(`ButtonDeck.cpp:26-36`; Bet x4/x5 are parked at `(9999,9999)`). Reachable with synthetic mouse
clicks, but coordinates depend on `canvas_width/height`, `window_scale`, `button_deck_offset_x/y`
from `game_configuration.json`, so it is layout-fragile. Fallback only.

### 3c. From the SAS host itself - the auto-rebet lever (this is a big one)

CHANGELOG 2.0.4.5 and the code agree: **SAS long poll AA (enable/disable auto rebet) makes the
demo play rounds by itself.**

- `Main.cpp:424-428`: `case QSP_CALLBACK_GAME_AUTO_REBET: fGameManager->AsyncGameAutoRebet(msg.data1);`
- `GameManager::AsyncGameAutoRebet` (`GameManager.cpp:842`) sets `fAutoRebetEnabled`.
- `SlotGame::CallbackGameEnd()` calls `fGameManager->RequestAutoRebet()` (`SlotGame_01.cpp:804`).
- `RequestAutoRebet` (`GameManager.cpp:858`) arms `fAutoRebetCountdown = 2.0f` seconds unless
  lockup / game-select.
- `GameManager::Render` (`GameManager.cpp:727-737`) decrements it and calls
  `ProcessButton(Button::ePlay, 0)` when it reaches 0. `Render` receives `delta_time == 0` while a
  fault is up (`Main.cpp:223`), so a lockup **holds** the countdown rather than betting through it.
- Enabled by default in the config: `"auto rebet enabled": true` under `Market`
  (`bin\Debug\x64\Resources\configuration.json`).

**Implication:** once the SAS link works, SasWorkbench can drive "play N games" purely over the
wire - send LP AA enable, count game start/end exceptions, send LP AA disable - with the DIO route
needed only to seed credits and arm the first bet. This is the cleanest primitive for the corpus,
and it exercises a real SAS conformance requirement at the same time.

### 3d. RNG / determinism

**There is no seed override and no way to force outcomes from outside the process.**

- `Rng` (`src/GameApplication/Rng.cpp`) is `std::mt19937_64` seeded from `std::random_device` and
  from an 8-byte state persisted in **vendor NVRAM at offset `4096 - 8`**
  (`qspNvramReadVendorData(kVendorRngOffset, 8, &rng_state)`, `Rng.cpp:56,71`). It re-writes that
  state on **every** `GenerateUint32()` (`:146-147`), and a background thread cycles the engine
  every 100-200 ms (`RngThread`, `:119-135`) for GLI-11 3.2.6 compliance. So even a fixed seed does
  not give reproducible outcomes: the cycling thread is wall-clock dependent.
- The only outcome-forcing hook is `Math::TestSetRngOutcomes(const std::vector<uint32_t>&)`
  (`Game_01/Math_01.h:67`, `Math_01.cpp:363`), consumed once in `Math::StartSpin`
  (`Math_01.cpp:155-165`) and then cleared. Its **only caller** is a touch on a star icon while the
  help screen is open, hard-coded to a jackpot:
  ```cpp
  // SlotGame_01.cpp:251-257
  if (fHelpScreenVisible && fShowProgramIcon->Contains(x, y))
  {
      std::vector<uint32_t> progressive_win = {12, 12, 12, 12, 12};
      fMath->TestSetRngOutcomes(progressive_win);
  }
  ```
  `WheelGame` has no equivalent (`WheelGame.cpp:349` calls the RNG directly).

For SAS conformance this is mostly acceptable - the corpus cares that a game was played and metered,
not which symbols landed - but "force a progressive win / force a handpay-sized win" needs either
this hook widened into an injectable API, or a large credit balance and patience.

---

## 4. Existing automation hooks - the honest inventory

| Looked for | Found |
|---|---|
| Command-line arguments | **None.** `int main(int argc, char **argv)` in both `src/GameApplication/Main.cpp:91` and `src_keyboard_intrusion/GameApplication/Main.cpp:118`; those two lines are the *only* matches for `argc`/`argv` in the whole `src/`, `src_keyboard_intrusion/`, `include/` tree, and neither is read. |
| Environment variables | **None** (`grep -rn "getenv\|GetEnvironmentVariable" src/ src_keyboard_intrusion/ include/` -> no hits). |
| Autoplay / demo / attract mode | **None in the app.** The only autoplay is SAS-driven (section 3c). `grep -rni "autoplay\|auto_play\|scripted\|replay_file"` -> no hits. |
| Scripted-input file | **None.** |
| RNG seed override | **None** (section 3d). |
| Test harness in the repo | `tests/` holds three **hand-run markdown documents only**: `test_cases.md` (24 categories, "500+" cases), `UAT_test_cases.md` (30 sections of PASS/FAIL checkboxes), `zero_transaction.md` (a hand-typed AFT long poll 72 byte string). No executable tests, no CTest, no scripts. |
| CI | `.github/workflows/GLI.yml` and `releases.yml` - **build-and-upload only**, no test step. |
| Shell tooling | `tools/scripts/run_demo.sh` (Linux: kills stale `Application`, arranges `xrandr`/`xinput`, launches, `wmctrl` to size the window), `prepare_system.sh`, `set_capabilities.sh`, `update_files.sh`. `prepare_system.sh` installs `xdotool`, `wmctrl`, `evemu-tools`, `evtest`, `libinput-tools`, `socat` - a hint that input injection has been done by hand on Linux. |
| In-process API for test stimuli | `src_keyboard_intrusion/` (section 2) - the pattern exists and works. |

`tests/test_cases.md` section 20 "Special Test Modes" documents exactly two: `TestSetRngOutcomes()`
and the Keyboard Intrusion build. That is the whole surface the team itself claims.

---

## 5. What it needs to run on a laptop

### Build

- **Dependencies:** GLFW3, OpenAL(-soft), libvpx (`vcpkg.json`), plus OpenGL. Linux additionally
  `libsensors-dev`, `libssl-dev`, `libglew-dev`, `libdav1d-dev` per `prepare_system.sh` and
  `.github/workflows/releases.yml`. `qxcmake v2.16.0.0` and QSP (branch
  `bv-pending-banknote-replay`) are pulled by FetchContent; `-DFETCHCONTENT_FULLY_DISCONNECTED=ON`
  works offline once cached.
- **Presets** (`CMakePresets.json`): `Debug`, `Release`, `HALDebug`, `HALRelease`,
  `PreviewRelease`, `PreviewHALRelease`, `KeyboardIntrusion`, `CustomSimulation`. All inherit
  `QSP_CONFIG=./Resources/configuration.json`, `GRAPHICS_CONFIG=./Resources/game_configuration.json`,
  `USE_LICENSING=ON` (the two `Preview*` presets set it OFF).
- **`HAL=ON` is the "no hardware" switch.** `QSP\CMakeLists.txt:40-44`:
  ```cmake
  if(HAL)
      set(SIMULATE_NVRAM ON CACHE BOOL "NVRAM forced to simulation" FORCE)
      set(SIMULATE_QXCOM ON CACHE BOOL "QXCOM forced to simulation" FORCE)
      add_compile_definitions(SKIP_INTEGRITY_CHECKS)
  endif()
  ```
  and `:87-95` fetches `qpm_simulator v2.0.0.4`. Confirmed by the caches on this machine:
  `out/build/HALDebug/CMakeCache.txt` -> `HAL=ON, SIMULATE_NVRAM=ON, SIMULATE_QXCOM=ON`;
  `out/build/Debug/CMakeCache.txt` -> all OFF. `HALDebug/_deps` contains `qpm_simulator-src` and
  `lsm-src` (libsharedmemory); `Debug/_deps` instead contains the real `nvram`, `libbv`,
  `libprinter`, `gds_dep`, `qxcom`.
- Both `Debug` and `HALDebug` **have already been built on this laptop** (binaries dated
  2026-09-03: `bin\Debug\x64\OpenGL_Game.exe`, `bin\HALDebug\x64\OpenGL_Game.exe`), so the build
  itself works here.

### Runtime

- A **display** is required (GLFW window; `graphics.fullscreen=false`, `window_scale=0.5`,
  `canvas 3840x2160` -> a 1920x1080 window by default).
- **Simulated NVRAM is a file**: `%PROGRAMDATA%\quixant\launchpad\nvram.bin` on Windows,
  `/etc/quixant/launchpad/` (falling back to `~/.quixant/launchpad/`) on Linux
  (`QSP\simulator\include\utils.h:191-240`, `QSP\simulator\nvram\libnvram.cpp:17,55-95`).
  Present here: `C:\ProgramData\quixant\launchpad\nvram.bin` (8 MB, mtime 2026-09-03 11:43) plus
  `qpm_storage.bin` (512 B) and `nvram-Demo-Godot.bin`.
- **Licensing (`USE_LICENSING=ON`) is not a hard gate.** `LicenseHandler::LicenseHandler()`
  (`QSP\src\LibPlatform\LicenseHandler.cpp:653-670`) only sets `QSP_ERROR_LICENSE` when the status
  is neither VALID nor INVALID ("can't check"), and `src/GameApplication/Main.cpp:129-135` treats
  `QSP_ERROR_LICENSE` as **non-fatal** (prints a hint about clearing NVRAM and continues); any other
  `qspStart` failure is `assert(0); exit(1)`. There is a nag screen (`StartNagScreen()`).
  `PreviewRelease` / `PreviewHALRelease` set `USE_LICENSING=OFF` outright.
- **SAS will not come up** - see section 0. In `Debug`/`Release`, `sasInit()` fails the QX10 FPGA
  check; in `HALDebug`/`HALRelease`, `sasInit()` is a stub that "succeeds" and does nothing.
- Bill validator and printer are **inert until configured in the audit menu**:
  `BillValidatorHandler` returns immediately when
  `nvram_cfg->mBillValidatorProtocol.Read() == QPM_SERIAL_DEV_NONE`
  (`QSP\src\LibPlatform\BillValidatorHandler.cpp:101-124`); `SasNetwork.cpp:248` derives
  `printer_available` the same way from `mPrinterProtocol`.
- Linux extras: `setcap cap_sys_time+ep` (audit clock), `systemd-timesyncd` disabled, timezone
  `Etc/GMT` (README + `run_demo.sh`).

---

## 6. Where SAS is configured

Three layers, and only the first is a file a test can edit before launch.

**(a) `Resources/configuration.json`, `Market` section** - jurisdiction-level, read at
`qspStart()`. Values in the built `bin\Debug\x64\Resources\configuration.json`:

```
Market.jurisdiction                     = "GLI11"
Market.protocol                         = "SAS"          # or "none"
Market.SAS."manufacturer id"            = "QX"           # SAS table C-1 game id
Market.SAS."accounting denomination"    = 1              # cents; LP 1F / 53 unit
Market.SAS."start network"              = "immediately"  # | "audit exit" | "restart"
Market.SAS."attendant menu reporting"   = "on page displayed"
Market.SAS."transaction receipts"       = false
Market.SAS."progressive level hit at notification" = false
Market."auto rebet enabled"             = true
Market."keyed in credits"               = true
Market."number replay games"            = 10
Hardware."Serial Ports"."Network 1"     = "/dev/ttyS5"   # <-- the SAS port
Hardware."Serial Ports"."Network 2"     = "/dev/ttyS2"
Hardware.Keyswitches.audit              = 5              # DIN
Hardware.Keyswitches.reset              = 3              # DIN
Hardware.Doors  = tamper#1=0, tamper#2=1, main=2, topbox=3, logic=4, stacker=5
```
`SasNetwork::AsyncStart` passes `json_handler->GetSerialPort(JsonHandler::SerialPort::eNetwork1)`
straight into `sasInit()` (`QSP\src\LibMarketSas\SasNetwork.cpp:336`). Note the *Windows* build here
still carries `/dev/ttyS5` - the port string is a plain config value and only matters once `sasInit`
actually opens something.

The "SAS description" block also documents that **accounting denomination changes are adopted only
by a memory clear**, and that a **polling address of 0 disables the link entirely**
(SAS 6.02 section 2.1) whatever `start network` says.

**(b) NVRAM operator settings - the poll address lives here, not in the JSON.**
`SasNetwork.cpp:296-302`: `fDramPollAddress[0] = fNvramPollAddress[0].Read();` and the link is not
started when it resolves to 0 (`ResolveSasStartupState`, `:312-330`). The audit menu Network page
(`UAT_test_cases.md` 17d: "Port Address ID, Poll Address, AFT Port Numbers, Progressive Group ID",
plus checkboxes for poll timeout, legacy handpay, legacy bonus, control-to-host, and a Save button)
is the only shipped way to set it - which means synthetic clicks on the audit framebuffer.

**(c) The programmatic route exists, but only in-process.** `QSP\include\libqsp_audit.h`:
```c
QSP_API	QRESULT	qspGetNetworkConfiguration(void *cfg);
QSP_API	QRESULT	qspSetNetworkConfiguration(void *cfg);
QSP_API	QRESULT	qspMemoryReset();                                 // RAM clear
QSP_API	QRESULT	qspNetworkDiagnostics(uint32_t enable, char *logfile);
QSP_API	QRESULT	qspSetSystemTime(uint64_t posix_time);
QSP_API	QRESULT	qspSetHardwareConfiguration(QspHardwareCfg *cfg);  // BV / printer protocol
```
with (`QSP\include\libmarketsas.h:20-26`):
```c
typedef struct _QspSasConfiguration
{
    uint8_t   sas_manufacturer_id[2];
    uint16_t  poll_address[4];        // up to 4 SAS channels
    uint16_t  progressive_group_id;
    uint32_t  aft_asset_number;
    uint32_t  flags;                  // validation type, ticket-in, 6.02/6.03, channel routing...
} QspSasConfiguration;
```
The demo **never calls these** (`grep -rn "qspSetNetworkConfiguration\|qspMemoryReset" src/
src_keyboard_intrusion/` -> no hits); its `AuditManager` only blits the platform framebuffer.
They are exported from `qsp.dll` / `libqsp.so`, so a `src_keyboard_intrusion`-style TU (or a tiny
loader) can call them.

---

## 7. The smallest change that makes this a usable EGM peer

Three things, in dependency order.

1. **A build that has both qpm_simulator and real SAS.** Today `HAL` is one switch that forces
   NVRAM, QXCOM *and* libsas into simulation. Needed: `HAL=ON` for QPM/NVRAM but libsas built from
   `FULL_SRC_FILES` with `VALIDATION_DEBUG` (Windows) / `_DEBUG` (Linux), and `HAL_MODE` **not**
   defined for `marketsas` so `sasSetSerialCallbacks()` is compiled in
   (`SasNetwork.cpp:259-267`). That is a handful of lines across
   `libsas\CMakeLists.txt:68-71`, `QSP\src\LibMarketSas\LibMarketSas.cmake:22-34` and
   `QSP\CMakeLists.txt:40-44` - e.g. a `SAS_TESTABLE` option that decouples libsas from `HAL`.
   *Risk: none of the three repos is set up for this combination today; it has to be tried.*
2. **A serial transport SasWorkbench can reach.** `sasSetSerialCallbacks()` takes a
   `QUIXANT_SERIAL_CALLBACKS` struct of `open/close/setState/read/readExtended/write/writeExtended/
   flush/cancel` function pointers (`QSP\simulator\include\qxcom\libqxcom_callbacks.h:43-58`; note
   `readExtended`/`writeExtended` are `unsigned short*` = the 9-bit words). QSP fills it from
   libqxcom's `assignCallbacks()`. Substituting a TCP-backed implementation there is the natural
   join to the TCP 9-bit transport SasWorkbench will speak, and it needs no serial port, no
   `com0com`, no null-modem.
3. **A stimulus channel with acknowledgement.** The shared memory gives edges but no reply. Extend
   the `src_keyboard_intrusion` pattern into a small control surface (named pipe / loopback socket
   inside the app, or just more `QSP_API` functions plus a thread) exposing:
   `SelectGame`, `PressButton(din)`, `SetIntrusion`, `InsertNote(cents)`, `InsertTicket(barcode)`,
   `SetNetworkConfiguration`, `MemoryReset`, `GetCredits/GetMeters`, `ForceRngOutcome`, and a
   `LastPrintedTicket` readback. Every one of those is a call the platform already exports.

---

## 8. Orchestrator stimulus matrix (GSA conformance corpus vs. THIS app)

Assumes a `HAL`-family build unless noted. "SHM" = write `QpmSharedMemory` from the orchestrator.

| Stimulus | How, today | Feasibility |
|---|---|---|
| **Select a game** | SHM pulse DIN 6 (`game_menu`) -> `GameSelect::ProcessButton` -> `SelectGame(1)`; or click a denom icon on the select screen (`GameSelect.cpp:138-146`) | works today |
| **Play a game (1..N)** | SHM pulse DIN 4 per round; wait for `[SlotGame #N] CallbackSpinComplete()` on stdout / credit-meter change. Or, once SAS works, **SAS LP AA** auto rebet plays a round every 2 s by itself (3c). The round must be seeded: game selected, bet factor set, credits >= 50c | works today (SHM); SAS route needs 7.1 |
| **Change bet** | SHM pulse DIN 7..11 = bet x1..x5 (`game_configuration.json`) | works today |
| **Insert a note** | SHM set `bna_mask` bit i (i=0..7 -> $1,$2,$5,$10,$20,$50,$100,$200). Escrow -> accept is automatic in `BillValidatorHandler`. **Prerequisite:** a BV protocol configured in NVRAM, else the handler bails at `BillValidatorHandler.cpp:108` | works today, after one-off audit-menu configuration |
| **Press Collect / cash out** | SHM pulse DIN 0 -> `qspCollectButtonPressed()` | works today |
| **Open / close a door** | SHM toggle `intrusion_mask` bit: 0=tamper#1, 1=tamper#2, 2=main, 3=topbox, 4=logic, 5=stacker (`Hardware.Doors`) | works today |
| **Audit / Reset keyswitch** | SHM toggle DIN 5 (audit) / DIN 3 (reset) - `ButtonHandler.cpp:184` routes them to `AsyncProcessKeyswitch`. Keyboard-intrusion `qspTestSoftwareKeySwitch()` does the same in-process | works today |
| **Print a ticket** | Consequence of Collect, not a direct stimulus. In HAL the printer is `printf`-only: `qpmPrinterPrint` dumps template, **validation number (barcode)**, voucher number, date/time, amount, expiry to stdout (`libqpm.cpp:1633-1646`), so the orchestrator captures the barcode by parsing stdout. **Prerequisite:** printer protocol configured in NVRAM | works today (read-back via stdout) |
| **Redeem / insert a ticket** | **Not reachable.** The HAL QPM never sets `fPendingTicket = true` and never emits a ticket-pending event; `bna_mask` only carries notes (`libqpm.cpp:651,770,924`). Needs a new SHM field or an injected `qspTestTicketIn(barcode)` | significant work |
| **RAM clear / memory reset** | Offline: stop the app, delete `%PROGRAMDATA%\quixant\launchpad\nvram.bin` (the simulator recreates it filled with `0xdeadbeef` -> pristine, `libnvram.cpp:76-95`). In-process: `qspMemoryReset()` (`libqsp_audit.h:487`), which the demo does not call. Audit-menu route needs doors open (`UAT-CFG-061`) | works today (file delete); API route is a small change |
| **Power cycle** | Kill and relaunch the process. Recovery is real: `StartPowerupRecovery()` -> `qspGameRecoveryStart` -> replays the interrupted round (`GameManager.cpp:559-598`). NVRAM survives because it is a file | works today |
| **Set SAS poll address / features / denom before launch** | No file carries the poll address - it is NVRAM (`SasNetwork.cpp:296`). Either drive the audit menu with synthetic clicks, pre-seed `nvram.bin`, or call `qspSetNetworkConfiguration(QspSasConfiguration*)` from an injected TU | small change (injected API) / significant (NVRAM pre-seeding) |
| **Force a specific outcome (jackpot, handpay-sized win)** | Only `Math::TestSetRngOutcomes()`, reachable solely by a touch on the star icon with the help screen open, hard-coded to `{12,12,12,12,12}` (`SlotGame_01.cpp:251-257`). No wheel-game equivalent | small change (widen the hook) |
| **Speed the test up** | `kTimeFactor = 4.0f` on Backspace (`Main.cpp:247`) - a keystroke, not remotely settable. Auto rebet's 2 s delay is the hard constant `kAutoRebetDelaySeconds` (`GameManager.cpp:40`) | small change |
| **Observe EGM state** | stdout (`Diag`=printf, QPM printfs, AFT/DIN traces), QSP file logger (`debug_verbosity=1` in `QspCreateCfg`), `nvram.bin`, lamp states in SHM `lamps[32]`, and - once SAS works - the wire itself plus `qspNetworkDiagnostics(enable, logfile)` | works today (indirect) |

---

## 9. Open questions / UNKNOWN

- Does the `KeyboardIntrusion` preset still configure and link? I predict a link error on
  `DemoApplication::FrameResized` and a runtime `assert(0)` on the three newer callbacks, but I did
  not build it.
- Exact `libsharedmemory` (`lsm`) segment naming on Windows vs Linux (`CreateFileMapping` name vs
  `/dev/shm/...`) - I read the wrapper (`QpmSharedMemory.h`) but not `lsm::Memory` itself, so the
  orchestrator's own mapping code needs that detail checked in
  `out\build\HALDebug\_deps\lsm-src`.
- Whether `SIMULATE_QXCOM=ON` with `HAL=OFF` (the `CustomSimulation` preset) produces a working
  binary at all: `QSP\CMakeLists.txt:47-51` only adds `simulator/include/qxcom` to the include path
  and there is no simulator qxcom implementation in `QSP\simulator\` (only `nvram/libnvram.cpp`),
  while `:136-152` still fetches the real libqxcom when `NOT HAL`. Looks vestigial; untested.
- Whether the platform's audit menu can be driven reliably by synthetic clicks (fixed framebuffer
  coordinates at 1920x1080 scaled by `audit_scale`/`window_scale`) - plausible but brittle;
  not attempted.
- Whether a HAL build's `sasInit` stub returning success makes QSP believe the link is up and
  therefore mask conditions a real run would raise - i.e. how misleading a HAL run is as a
  "SAS EGM". Not traced.
- `bin\Debug\x64\qsp-config.json` (24 KB) vs `Resources\configuration.json` (38 KB): the former is
  not the file `QSP_CONFIG` points at. Probably a leftover; not confirmed.
