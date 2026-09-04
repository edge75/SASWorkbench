# qpm_simulator control surface — definitive spec for a C# test orchestrator

Scope: everything a SasWorkbench orchestrator needs to drive EGM peripherals (peer A) without a human
touching the ImGui window. Every claim below is traced to a file+line I read or a command I ran.
Nothing is inferred from memory.

Repo: `C:\Progetti\qpm_simulator` @ `4db296b` ("Extended number of DIO to 32"), version 2.0.0.4
(`include/qpm_simulator.h` lines 8-11), origin `https://github.com/quixantplc/qpm_simulator`.
No `.codegraph/` directory exists in qpm_simulator, QPM2, QSP or QSP_DemoOpenGL — grep/read was used.

---

## 0. Executive answer

**Yes — a separate C# process can drive the whole EGM peripheral surface with zero modification to
qpm_simulator, and it should REPLACE the GUI rather than coexist with it.**

The segment is a plain 148-byte C struct in a 1 KB mapping with **no framing, no header, no lock, no
sequence counter and no dirty flag**. Field ownership is naturally disjoint: the EGM (qpm.dll) writes
only `hearbeat` and `lamps[32]`; the simulator GUI writes only `intrusion_mask`, `dio_mask`,
`bna_mask`, `printer_mask`. If the GUI is not running, a C# process becomes the sole writer of the
four stimulus fields and no synchronisation is needed at all. I verified writability empirically
(§3.1). The GUI is a GL/GLFW/ImGui app with no headless mode, so *not* running it is also what makes
unattended CI possible.

---

## 1. The shared-memory protocol

### 1.1 The OS object

`include/QpmSharedMemory.h` constructs `lsm::Memory fMemory("QpmSharedMemory", 1024, /*persist=*/true)`
(line 16). The EGM side is byte-for-byte the same wrapper at
`C:\Progetti\QPM2\src\hal\QpmSharedMemory.h`.

`persist == true` is decisive. In `libsharedmemory.hpp` v1.8.0
(`C:\Progetti\qpm_simulator\out\build\Debug\_deps\lsm-src\include\libsharedmemory\libsharedmemory.hpp`):

**Windows — it is a FILE-BACKED mapping with NO named kernel object.**

```
persistence_file_path(name)  ->  GetSystemStorageDirectory() + "\lsm_" + sanitize_name(name) + ".shm"
GetSystemStorageDirectory()  ->  %PROGRAMDATA%\shared_memory      (created if absent)
```

so the object is the file

```
C:\ProgramData\shared_memory\lsm_QpmSharedMemory.shm
```

`createOrOpen()` then calls `CreateFileA(...)` on that path and
`CreateFileMappingA(_fileHandle, NULL, PAGE_READWRITE, hi, lo, NULL)` — **the last argument is NULL,
i.e. the mapping object is unnamed**. There is therefore *no* `Global\` or `Local\` name to open, and
`OpenFileMapping` is not usable. Processes share state purely because they map the same file, and
Windows guarantees view coherence for views backed by the same file. The named-mapping branch
(`CreateFileMappingA(INVALID_HANDLE_VALUE, ..., _path.c_str())`) exists in the library but is only
taken when `persist == false`, which neither QPM nor the simulator does.

Verified on this machine:

```
$ ls -la /c/ProgramData/shared_memory/
-rw-r--r-- 1 alessio.brizi 1024 Feb  6  2026 lsm_QpmSharedMemory.shm
$ icacls C:\ProgramData\shared_memory\lsm_QpmSharedMemory.shm
  BUILTIN\Users:(R,W,D)   BUILTIN\Users:(I)(R,W,D)   NT AUTHORITY\SYSTEM:(I)(F)   ...
```

The file is exactly 1024 bytes and `BUILTIN\Users` has read+write+delete — `lsm` grants that
explicitly via `AssignPermissionsToFilesystemPath(..., UTIL_PERM_READ|UTIL_PERM_WRITE)`. **No elevation
is required.**

**Linux — POSIX shm.** `Memory::Memory` on the POSIX branch sets `_path = "/" + path`, then
`shm_open("/QpmSharedMemory", O_CREAT|O_RDWR, 0755)` + `ftruncate(fd, 1024)` +
`mmap(nullptr, 1024, PROT_READ|PROT_WRITE, MAP_SHARED, fd, 0)`. Visible as `/dev/shm/QpmSharedMemory`.
`persist` on Linux affects only whether `~Memory` calls `shm_unlink` — with `persist=true` it does not,
so the segment survives process exit. Note the asymmetry: **Windows = file-backed section under
ProgramData, Linux = POSIX shm**. Same 1024-byte raw payload; different path. C# must branch.

**No framing is added.** `lsm::Memory::data()` returns the raw base of the mapping. The library's
`SharedMemoryReadStream`/`WriteStream` classes *do* prepend a 1-byte flag + 4-byte size, but
`QpmSharedMemory` uses `lsm::Memory` directly, so **offset 0 is the first struct field**, not a flag.

### 1.2 The 1024-byte layout

Struct is declared identically twice — `src/Main.cpp` lines 25-33 (simulator) and
`C:\Progetti\QPM2\src\hal\libqpm.cpp` lines 152-161 (EGM), which additionally asserts
`static_assert(sizeof(QpmSharedState) < 1 * 1024)`. Standard-layout, all `uint32_t`, MSVC/GCC pack to
natural alignment ⇒ no padding.

| Offset | Size | Type | Field | Written by | Read by |
|---|---|---|---|---|---|
| `0x000` | 4 | `uint32_t` | `hearbeat` *(sic — the typo is in the source)* | **EGM** (`++state->hearbeat`, libqpm.cpp:722, every 125 ms) | Simulator GUI (Main.cpp:180) |
| `0x004` | 4 | `uint32_t` | `intrusion_mask` — bits 0..15 = intrusion/door switch, 1 = open | **Simulator GUI** (Main.cpp:141-146) | EGM (libqpm.cpp:724, :696) |
| `0x008` | 4 | `uint32_t` | `dio_mask` — bits 0..31 = digital input, 1 = asserted | **Simulator GUI** (Main.cpp:147-152 and :191) | EGM (libqpm.cpp:739, :689) |
| `0x00C` | 128 | `uint32_t[32]` | `lamps[0..31]` — DOUT/lamp state per line | **EGM** (`SetLampState`, libqpm.cpp:682) | Simulator GUI (Main.cpp:154, read-only) |
| `0x08C` | 4 | `uint32_t` | `bna_mask` — bill validator | **Simulator GUI** (Main.cpp:156-166) | EGM (libqpm.cpp:758) |
| `0x090` | 4 | `uint32_t` | `printer_mask` — printer | **Simulator GUI** (Main.cpp:167-172) | EGM (libqpm.cpp:841) |
| `0x094`..`0x3FF` | 876 | — | **unused, always zero** | — | — |

`sizeof(QpmSharedState) == 148 == 0x94`.

Empirical confirmation — the persisted file still holds the last GUI session's state, and it decodes
exactly as the table predicts:

```
$ xxd lsm_QpmSharedMemory.shm | head -2
00000000: cc00 0000 0500 0000 2000 0000 0000 0000
00000010: 0000 0000 0000 0000 0000 0000 0000 0000
  heartbeat      = 0x000000CC = 204
  intrusion_mask = 0x00000005 = bits 0,2   -> "Tamper #1" + "Main" checked
  dio_mask       = 0x00000020 = bit 5      -> "Audit" checkbox held
  lamps[0..]     = 0
```

### 1.3 Bit semantics

**`intrusion_mask` (16 bits used, `NUM_INTRUSIONS 16`)** — bit *i* set = intrusion switch *i*
triggered (door open). Level-held, not a pulse. The EGM XORs against its own previous copy
(libqpm.cpp:724) and fires `D2C_DOOR_OPEN` (3) on 0→1, `D2C_DOOR_CLOSED` (4) on 1→0, with
`Argument = i`. `qpmSecGetDoorByIndex()` also reads the live bit so a held-open door keeps reporting
`IsTriggered == 1` (libqpm.cpp:1094).

**`dio_mask` (32 bits, `NUM_DIO 32`)** — bit *i* set = DIN *i* asserted.
`D2C_GPIO_RISING_EDGE` (28) / `D2C_GPIO_FALLING_EDGE` (29) with `Argument = i` (libqpm.cpp:751).
`qpmGpioDin()` returns the raw mask (libqpm.cpp:1186-1194).

**`lamps[i]` — EGM→GUI, the only observable output channel.**
`qpmGpioDout(handle, mask, valueFlags)` computes `dout = log2(mask)`, strips
`F_QPM_GPIO_FIRE_AND_FORGET (0x80000000)` and stores the remaining flags verbatim
(libqpm.cpp:1163-1176). From `C:\Progetti\QPM2\include\qpm.h` lines 303-307:

| value | meaning | GUI rendering (Main.cpp:263-267) |
|---|---|---|
| `0x00` | `F_QPM_GPIO_OFF` | normal button |
| `0x01` | `F_QPM_GPIO_ON` | button tinted orange permanently |
| `0x02` | `F_QPM_GPIO_BLINK` | tinted for 0.667 s of every 1.333 s |
| `0x04` | `F_QPM_GPIO_BLINK_ALTERNATE` | *not rendered* — GUI treats it as off |

This array is the orchestrator's window into EGM intent (e.g. "is the Collect lamp lit?").

**`bna_mask`** — two zones, both written by the GUI, both diffed by the EGM (libqpm.cpp:758-839):

| bit | GUI control | EGM behaviour on **rising** edge |
|---|---|---|
| 0..7 | `sBnaNotes[i]` note checkbox | `D2C_ACCEPTOR_PENDING_NOTE` (14), `Argument` = denomination in cents |
| 8 | "Disconnected" | `D2C_SERIAL_DEVICE_LINK_DOWN` (26); 1→0 gives `LINK_UP` (25) |
| 9 | "Jam" | `D2C_ACCEPTOR_STATE_CHANGED` (10), `Argument = QPM_SERIAL_DEV_S_JAMMED (0x20)` |
| 10 | "Stacker Full" | `D2C_ACCEPTOR_STATE_CHANGED`, `QPM_SERIAL_DEV_S_FULL (0x80)` |
| 11 | "Stacker Rmvd" | `D2C_ACCEPTOR_STATE_CHANGED`, `QPM_SERIAL_DEV_S_NOSTACKER (0x40)` |
| — | all of 9..11 clear (`& 0xe00` goes non-zero → zero) | `D2C_ACCEPTOR_STATE_CHANGED`, `QPM_SERIAL_DEV_S_IDLE (0x01)` — "BNA Reset" |

> **DENOMINATION TRAP.** The GUI *labels* bits 0..7 from `qpm_simulator_config.json` `"Bills"`, but
> the EGM's denomination table is **hardcoded in libqpm.cpp:766**:
> `{100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000, 100000}` (cents), indexed by bit number.
> The shipped `Bills` array is `[100,200,500,1000,2000,5000,10000]`, so bits 0..6 agree and **bit 7 is
> $200.00 to the EGM but unlabeled ("##7") in the GUI**. The orchestrator must use the hardcoded
> table, not the JSON.

Note the two-step accept flow: the rising edge only yields `PENDING_NOTE`. `D2C_ACCEPTOR_NOTE_ACCEPTED`
(8) is emitted only when QSP calls back through `qpmBvFinalize`-style API →
`QpmWrapper::BillValidatorStack(true)` (libqpm.cpp:919-929, called from libqpm.cpp:1590 when
`finalizationAction == QPM_BV_ACTION_ACCEPT`). The bit must then be **cleared** to re-arm; leaving it
set produces nothing further.

**`printer_mask`** (libqpm.cpp:841-907):

| bit | GUI control | EGM behaviour |
|---|---|---|
| 0 | "Disconnected" | `D2C_SERIAL_DEVICE_LINK_DOWN`/`_UP` |
| 1 | "Jam" | `D2C_PRINTER_STATE_CHANGED` (11), `QPM_SERIAL_DEV_S_JAMMED (0x20)` |
| 2 | "Paper Out" | `D2C_PRINTER_STATE_CHANGED`, `QPM_SERIAL_DEV_S_PAPEROUT (0x100)` |
| 3 | "Paper Low" | `D2C_PRINTER_STATE_CHANGED`, `QPM_SERIAL_DEV_S_PAPERLOW (0x200)` |
| — | 1..3 all clear (`& 0x1e` non-zero → zero) | `QPM_SERIAL_DEV_S_IDLE` — "Printer Reset" |

### 1.4 Handshake, synchronisation, torn writes

**There is none.** No mutex, no named event, no semaphore, no atomic, no sequence counter, no dirty
flag. `hearbeat` is a plain non-atomic `uint32_t`.

What actually holds the design together is **disjoint field ownership**:

```
EGM (qpm.dll)  writes:  hearbeat, lamps[32]              reads: intrusion_mask, dio_mask, bna_mask, printer_mask
GUI (simulator) writes: intrusion_mask, dio_mask,        reads: hearbeat, lamps[32]
                        bna_mask, printer_mask
```

No word is written by both. Every field is a naturally aligned 32-bit value, so on x86-64 each
individual read/write is atomic in practice; **per-field tearing is impossible, cross-field atomicity
is not guaranteed**. Consequences:

1. The EGM's `memcpy(&fPreviousState, state, 148)` (libqpm.cpp:909) is not atomic against the GUI's
   writes. Worst case an event is detected on the next 125 ms tick instead of this one. Harmless.
2. **A real latent race in the existing GUI**: `ProcessSharedMemory()` writes
   `state->intrusion_mask = 0;` *into shared memory* and only then ORs the set bits back in
   (Main.cpp:141-146). Same pattern for `dio_mask`, `bna_mask`, `printer_mask`. If the EGM's 125 ms
   poll lands inside that window it sees every door momentarily closed and then reopened → a spurious
   `$12`/`$11` pair on the SAS wire. The window is nanoseconds against a 125 ms poll so it is rare,
   but it is a genuine flake source for a regression suite. **A C# orchestrator that computes the new
   mask in a local variable and writes it once does not have this bug** — another reason to replace
   the GUI rather than drive it.
3. **A third writer is safe if and only if it is the only writer of the stimulus fields.** If the C#
   orchestrator and the GUI both run, both do `intrusion_mask = <their own view>` 60 times a second
   and the GUI wins within ~16 ms. See §3.
4. **Startup wipe.** `QpmWrapper::QpmWrapper` does an unconditional
   `memset(fSharedMemory->Data(), 0, 1024)` (libqpm.cpp:557) — the EGM zeroes the whole segment when
   it starts. The simulator's wrapper memsets only when `open()` failed and it had to `create()`.
   ⇒ **Start the EGM first, wait for `hearbeat` to start incrementing, only then inject stimulus.**

### 1.5 The button-pulse machinery (why buttons are heartbeat-driven)

`Main.cpp:174-186`:

```cpp
if (sLastHeartbeat != state->hearbeat) {
    sLastHeartbeatButtons = 0;
    while (!sPendingDioButtons.empty()) { ... sLastHeartbeatButtons |= (1 << button); }
}
state->dio_mask |= sLastHeartbeatButtons;
sLastHeartbeat = state->hearbeat;
```

An ImGui `Button` click pushes the index onto `sPendingDioButtons`. The queue is drained only when the
heartbeat changes, and the resulting mask is OR-ed into `dio_mask` on every frame until the *next*
heartbeat change. Net effect: a click is asserted for exactly one 125 ms EGM poll period, producing
one rising and one falling edge. **If the EGM is not running the heartbeat never advances and the GUI
buttons are completely inert.** The `NN` checkbox beside each button is the level-hold variant, used
for keyswitches (Audit, Jkey, door keys).

---

## 2. The stimulus vocabulary (every GUI control)

Window: `yplatform::Application("Quixant Peripheral Management", 1920, 1080, /*fullscreen=*/false,
1280, 380)` — a 1280x380 window with four panels.

### 2.1 Intrusion panel — 16 checkboxes (level)

Labels come from `qpm_simulator_config.json` `"Security"[].label`, keyed by `"interrupt"`.
**The SAS meaning does not come from that file** — it comes from the EGM's own
`qsp-config.json` `Hardware.Doors[].type`, and for QSP_DemoOpenGL
(`C:\Progetti\QSP_DemoOpenGL\bin\Debug\x64\qsp-config.json`) the two disagree:

| bit | GUI label (qpm_simulator_config.json) | QSP_DemoOpenGL door `type` | SAS exception open / close |
|---|---|---|---|
| 0 | Tamper #1 | `tamper#1` | Card cage `$15` / `$16` |
| 1 | Tamper #2 | `tamper#2` | Card cage `$15` / `$16` |
| 2 | Main | `main` | **Slot door `$11` / `$12`** |
| 3 | **Logic** | **`topbox`** | Slot door `$11` / `$12` |
| 4 | **Stacker** | **`logic`** | Card cage `$15` / `$16` |
| 5 | **Dropbox** | **`stacker`** | **Cashbox door `$19` / `$1A`** |
| 6 | Topbox | *(unmapped)* | none — `QpmSecurityCallback` logs "unmapped intrusion" |
| 7 | Spare | *(unmapped)* | none |
| 8 | Main Opto | *(unmapped)* | none |
| 9 | CFast | *(unmapped)* | none |
| 10..15 | Tamper #3..#8 | *(unmapped)* | none |

> **This label/meaning mismatch is a live trap.** Only bits 0-5 do anything on the Demo EGM, and
> bits 3, 4, 5 mean something different from what the GUI says. The orchestrator must address
> intrusions **by bit index derived from the EGM's `qsp-config.json`**, never by the simulator's label.

The generic type→SAS mapping (independent of which bit) is `SasNetwork_Conditions.hpp` lines 21-36 and
is documented verbatim in `qsp-config.json`'s own `"Doors description"`:

| door `type` | SAS open / close |
|---|---|
| `main`, `opto`, `topbox` | `$11` SlotDoorOpened / `$12` SlotDoorClosed |
| `dropbox` | `$13` DropDoorOpened / `$14` DropDoorClosed |
| `stacker` | `$19` CashboxDoorOpened / `$1A` CashboxDoorClosed |
| `logic`, `tamper#1`..`tamper#8` | `$15` CardCageOpened / `$16` CardCageClosed |
| `spare` | `$1D` BellyDoorOpened / `$1E` BellyDoorClosed |
| `storage` | `$8E` ComponentListChanged (both directions) |

Doors already open at EGM startup take the `eStartupIntrusion*` path
(`SasNetwork_Conditions.hpp` lines 38-53) which maps to `NoActivity` — **no SAS exception is sent for
a door that was already open when the EGM booted**. Another reason to inject only after boot.

### 2.2 Buttons / Keyswitches panel — 32 momentary buttons + 32 level checkboxes

Labels from `qpm_simulator_config.json` `"DIO"[].label`, keyed by `"dio"`; unlabeled lines render as
`##1xx`. All 32 are rendered (`kLayoutDio[]`, Main.cpp:222-228, 4 columns x 8) — the CHANGELOG line
"still 16 in the UI" is stale.

| DIO | Label | Role on the Demo EGM | SAS consequence |
|---|---|---|---|
| 0 | Collect | game button; game calls `qspCollectButtonPressed()` (libqsp.cpp:218) → `MoneyOutHandler::CollectButtonPressed` → `MarketHandler::CashoutButtonPressed` → `SasNetwork::SendException(CashoutButtonPressed)` | **`$66` Cashout button pressed** — SasNetwork.cpp:1429; comment at MoneyOutHandler.cpp:86 notes it is sent unconditionally |
| 2 | Service | game button | game-defined |
| 3 | Jkey | **`Keyswitches.reset` = 3** → `ButtonHandler::AsyncProcessKeyswitch` | `Condition::eSwitchReset` → `ConditionSwitchReset`; clears handpay → `$52` HandpayWasReset when a handpay is pending |
| 4 | Play | game button | drives game play → coin-in/out meters, `$7E` game started etc. |
| 5 | Audit | **`Keyswitches.audit` = 5** | `Condition::eSwitchAudit`; opens the attendant menu |
| 6 | Menu | game button | game-defined |
| 7,8,9 | Bet x1 / x2 / x3 | game buttons | game-defined |
| 14,15 | Cnd#1 / Cnd#2 | market DIN candidates (`fMarketDins`) | market-defined |
| 28,30 | Key#1 / Key#2 | game buttons | game-defined |
| 61 | *(unlabeled)* | **reserved: DC-fail / power-fail interrupt** — `ButtonHandler.cpp:156` special-cases `Argument == 61`. Out of range for `NUM_DIO 32`, so unreachable through this segment. | would record a power-fail stamp |
| all others | `##1xx` | delivered as raw DIN edges to the game | — |

Non-keyswitch, non-market DINs are forwarded to the game only when
`!condition_handler->AreInhibitingConditionsActive()` (ButtonHandler.cpp:~215) — i.e. **an open door
suppresses button delivery**. Sequencing matters: close the door before pressing Play.

### 2.3 Bill Validator panel — 8 note checkboxes + 4 state checkboxes

Note checkbox *i* is labelled `$<Bills[i]/100>.<%02u>` from the config. Denomination actually credited
comes from the hardcoded table (§1.3). Mapping to SAS bill-accepted exceptions
(`libsas.h` lines 450-486), which fire only after QSP finalises the note with `ACCEPT`:

| bit | cents (EGM table) | GUI label | SAS exception |
|---|---|---|---|
| 0 | 100 | $1.00 | `$47` USD1BillAccepted |
| 1 | 200 | $2.00 | `$4D` USD2BillAccepted |
| 2 | 500 | $5.00 | `$48` USD5BillAccepted |
| 3 | 1000 | $10.00 | `$49` USD10BillAccepted |
| 4 | 2000 | $20.00 | **`$4A` USD20BillAccepted** |
| 5 | 5000 | $50.00 | `$4B` USD50BillAccepted |
| 6 | 10000 | $100.00 | `$4C` USD100BillAccepted |
| 7 | 20000 | *(##7)* | `$50` USD200BillAccepted |

(`$4F` BillAccepted is the generic/RTE code; which of the two the EGM emits depends on the SAS RTE
setting — **UNKNOWN**, not verified in this pass.)

BNA state checkboxes → `SasNetwork_Conditions.hpp` lines 70-76:

| GUI checkbox | bit | QSP `Condition` | SAS exception |
|---|---|---|---|
| Disconnected | 8 | `eBnaDisconnected` | `$29` BillAcceptorHwFailure |
| Jam | 9 | `eBnaJam` | `$28` BillJam |
| Stacker Full | 10 | `eBnaStackerFull` | `$27` CashboxFull |
| Stacker Rmvd | 11 | `eBnaStackerRemoved` | `$1B` CashboxRemoved / `$1C` CashboxInstalled on clear |

### 2.4 Printer panel — 4 state checkboxes

`SasNetwork_Conditions.hpp` lines 78-82:

| GUI checkbox | bit | QSP `Condition` | SAS exception |
|---|---|---|---|
| Disconnected | 0 | `ePrinterMalfunction` | `$75` PrinterPowerOff / `$76` PrinterPowerOn on clear |
| Jam | 1 | `ePrinterJam` | `$78` PrinterCarriageJammed |
| Paper Out | 2 | `ePrinterPaperOut` | `$61` PrinterPaperOutError |
| Paper Low | 3 | `ePrinterPaperLow` | `$74` PrinterPaperLow |

### 2.5 What the GUI CANNOT do

No control exists for: game play/outcome, handpay, ticket insertion (`D2C_ACCEPTOR_PENDING_TCKT` (15)
is never generated from this segment — `fPendingTicket` is only ever set `false` at libqpm.cpp:772),
MCR card reads, barcode scans, AC power loss, or the SAS serial link itself. Those must come from the
game app (QSP_DemoOpenGL) or from the SAS host side.

---

## 3. Can a third process drive it?

### 3.1 Yes — verified empirically

I mapped the live segment from a third process (Python 3.10, a stand-in for .NET's identical
`MemoryMappedFile` path), read every field, wrote two bits, read them back, and restored the original
1024 bytes byte-for-byte:

```
size: 1024
BEFORE heartbeat=204 intrusion=0x00000005 dio=0x00000020 bna=0x00000000 printer=0x00000000
AFTER  intrusion=0x00000005 printer=0x00000004  -> WRITE OK: True
RESTORED identical: True
file identical on disk: True
```

(script kept at `...\scratchpad\shmtest.py`, backup at `...\scratchpad\shm_backup.bin`)

### 3.2 The exact C# call

`MemoryMappedFile.CreateFromFile` — **not** `OpenExisting`, because the mapping object is unnamed
(§1.1). No P/Invoke needed.

```csharp
// Windows
const string Path = @"C:\ProgramData\shared_memory\lsm_QpmSharedMemory.shm";
// Linux: "/dev/shm/QpmSharedMemory"

var fs = new FileStream(Path, FileMode.Open, FileAccess.ReadWrite,
                        FileShare.ReadWrite | FileShare.Delete);
var mmf = MemoryMappedFile.CreateFromFile(fs, null, 1024,
                        MemoryMappedFileAccess.ReadWrite, HandleInheritability.None,
                        leaveOpen: false);
var view = mmf.CreateViewAccessor(0, 1024, MemoryMappedFileAccess.ReadWrite);

const int OFF_HEARTBEAT = 0, OFF_INTRUSION = 4, OFF_DIO = 8,
          OFF_LAMPS = 12, OFF_BNA = 140, OFF_PRINTER = 144;

uint Heartbeat()          => view.ReadUInt32(OFF_HEARTBEAT);
uint Lamp(int i)          => view.ReadUInt32(OFF_LAMPS + 4 * i);   // i in 0..31
void SetIntrusion(uint m) => view.Write(OFF_INTRUSION, m);
void SetDio(uint m)       => view.Write(OFF_DIO, m);
void SetBna(uint m)       => view.Write(OFF_BNA, m);
void SetPrinter(uint m)   => view.Write(OFF_PRINTER, m);
```

`FileShare.ReadWrite` is mandatory — the EGM holds the file open with
`FILE_SHARE_READ | FILE_SHARE_WRITE`. Keep one shadow copy of each mask in the orchestrator and write
whole words; never read-modify-write across a poll boundary.

### 3.3 Pulse and level protocol (mirror the GUI, don't guess at timings)

The EGM polls every 125 ms and does `++hearbeat` *before* diffing (libqpm.cpp:715-722). Use the
heartbeat as the clock, not `Thread.Sleep`:

```
PulseDio(bit):
  h0 = Heartbeat()
  SetDio(mask | 1<<bit)
  WaitUntil(Heartbeat() >= h0 + 2)     // rising edge definitely observed
  SetDio(mask & ~(1<<bit))
  WaitUntil(Heartbeat() >= h0 + 4)     // falling edge definitely observed
```

~250-500 ms per press. Doors, keyswitches and BNA/printer state flags are **level** signals: set and
leave, then wait 2 heartbeats before asserting on the SAS side. BNA note bits are **edge**: pulse them
exactly like a DIO bit, otherwise the note cannot be re-inserted.

`WaitUntil` should also fail fast if the heartbeat does not advance within, say, 2 s — that is the
orchestrator's liveness check that the EGM is actually up.

### 3.4 What can go wrong

| Risk | Detail | Mitigation |
|---|---|---|
| **GUI running concurrently** | `ProcessSharedMemory()` rewrites all four stimulus masks from its own booleans on every rendered frame (~60 Hz). Any orchestrator write is erased in ≲16 ms. | **Do not run qpm_simulator.exe during automated tests.** Mutually exclusive by design. |
| **EGM startup wipe** | `memset(..., 0, 1024)` at libqpm.cpp:557. | Start EGM, poll until `hearbeat` advances, then inject. |
| **Segment absent** | On a clean machine the file does not exist until the EGM or the simulator creates it. | Start the EGM first; or have C# create a 1024-byte zero file and ACL it for `BUILTIN\Users` (that is exactly what `lsm` does). |
| **Stale state across runs** | `persist=true`, so the file survives reboots with the last masks in it (heartbeat 204 / intrusion 0x5 / dio 0x20 was still sitting there from a Feb-2026 session). | Zero the 148 bytes at test-suite start, after the EGM has booted. |
| **Doors open at boot report nothing** | `eStartupIntrusion*` → `NoActivity`. | Never leave doors set between tests; zero first. |
| **Buttons swallowed while a door is open** | `AreInhibitingConditionsActive()` gate in `ButtonHandler`. | Order stimuli: close doors → press buttons. |
| **Unmapped intrusion bits** | Bits 6-15 log "unmapped intrusion" on the Demo config and do nothing. | Derive the usable set from the EGM's `qsp-config.json`, not the simulator's. |
| **Torn cross-field read** | 148 bytes are not snapshot-atomic. | Only ever affects timing by one 125 ms tick; ignore. |

**Does the simulator poll continuously?** Yes — `ProcessSharedMemory()` is the first statement of
`Render()`, and `Application::Run()` calls `Render()` every frame with `glfwSwapInterval(1)` (vsync,
~60 Hz). So it is not event-driven. But that continuous polling is exactly what makes it a *clobberer*
rather than a *cooperator*: it reads `lamps` and unconditionally re-writes the other four masks. There
is no code path by which the GUI would notice and adopt a third party's writes. **A third process
cannot drive the GUI; it must take the GUI's place.**

### 3.5 The alternative: a TCP/JSON control server inside qpm_simulator

Sketch: in `DemoApplication`, spawn a `std::jthread` with a tiny blocking accept loop on
`127.0.0.1:port`, newline-delimited JSON, mutating the same `sIntrusion[]` / `sDioCheckBox[]` /
`sPendingDioButtons` / `sBnaNotes[]` / `sBnaState[]` / `sPrinterState[]` arrays the ImGui widgets
already mutate, guarded by one `std::mutex` taken in `ProcessSharedMemory()`:

```json
{"cmd":"intrusion","index":2,"state":true}
{"cmd":"press","dio":0}
{"cmd":"hold","dio":5,"state":true}
{"cmd":"note","index":4}
{"cmd":"bna_state","index":1,"state":true}
{"cmd":"printer_state","index":2,"state":true}
{"cmd":"get_lamps"}   ->  {"lamps":[0,2,0,...]}
```

Realistic size: ~250-350 lines (a portable BSD-socket/Winsock accept loop is ~120 lines of that, JSON
parsing reuses the rapidjson already vendored at `src/rapidjson/`), plus a CMake option and a config
key for the port. Perhaps a day of work including tests.

**Honest comparison:**

| | Direct shared memory from C# | TCP/JSON server in qpm_simulator |
|---|---|---|
| Changes to qpm_simulator | **none** | fork/PR to a company repo pinned by `GIT_TAG v2.0.0.4` in `QSP/CMakeLists.txt:93` — needs a release and a QSP bump |
| Code to write | ~150 lines of C#, no native deps | ~300 lines C++ **plus** the C# client anyway |
| Headless CI | **works — nothing GL is involved** | still needs a GL context and a window (the server lives inside the GUI app) |
| Linux | one path change (`/dev/shm/QpmSharedMemory`) | same code, but still needs X/Xvfb |
| Extra moving part | none | one more process to start, health-check and kill |
| Semantics | must re-implement the 125 ms pulse rule (~20 lines) | reuses the GUI's own pulse machinery |
| Observability of lamps | direct array read | needs a poll/subscribe command |
| Human can watch the test | no (GUI must be off) | yes |

**Recommendation: direct shared memory.** The TCP server's only real advantage — reusing the pulse
machinery and letting a human watch — is outweighed by the fact that it keeps a mandatory GL window in
the CI loop, which is the single biggest obstacle to unattended runs (§4). A reasonable middle ground
is to build the C# access as a small `QpmSimBus` class now and, separately and later, propose the TCP
server upstream as a *developer convenience*, not as the test transport.

---

## 4. Lifecycle

**Where the binary is.**

- `C:\Progetti\qpm_simulator\bin\Debug\x64\qpm_simulator.exe` (5.3 MB, 2026-06-19) — standalone build.
  `out/` contains only CMake/FetchContent scaffolding and no simulator exe.
- `C:\Progetti\QSP\bin\HALDebug\x64\qpm_simulator.exe` and `...\bin\HALRelease\x64\qpm_simulator.exe`
  (2026-09-02) — built as part of QSP's `HAL` configuration.
  `QSP/CMakeLists.txt:87-96` does `if(HAL) FetchContent_Declare(qpm_simulator ... GIT_TAG v2.0.0.4)`.
- QSP_DemoOpenGL does **not** build it; it consumes the same segment through `qpm.dll` from QPM2
  (`QSP/CMakeLists.txt:75-80`, `GIT_TAG v8.16.1.0`).

**What must sit next to the executable.** Both are resolved via `get_executable_dir()`
(`GetModuleFileNameA` / `readlink("/proc/self/exe")`), **not** the working directory — so the exe can
be launched from anywhere:

1. `qpm_simulator_config.json` — `ParseJson.hpp:68-70`. Missing → `yplatform::Exit()`, which on Windows
   pops a **modal `MessageBoxW`** (`Application.cpp:57`) and blocks forever. Fatal for CI.
2. `gentium.ttf` — `Main.cpp:132-133`, `io.Fonts->AddFontFromFileTTF(..., 18)`. Missing → ImGui assert.

Both are copied automatically by a `POST_BUILD` step (`CMakeLists.txt` "Post build to move
configuration file to the output directory") and both are present in every bin dir I checked.

**Startup.** `main()` (Main.cpp:79-85) takes `argc/argv` and **ignores them entirely — there are no
command-line options, no port, no headless flag**. It constructs `DemoApplication` (which creates the
shared memory, parses the JSON, initialises ImGui) and calls `Run()`.

**Headless: no.** `yplatform::Application::Application` unconditionally calls `glfwInit()`,
requests an **OpenGL 3.3 Core** context (`Application.cpp:167-176`), calls `glfwCreateWindow` with
`assert(fWindow)`, `glfwMakeContextCurrent`, `glewInit()` (hard `exit(1)` on failure), and aborts with
`printf("No GLSL support. Aborting."); exit(1)` if `GL_SHADING_LANGUAGE_VERSION` is null. There is
**no `GLFW_VISIBLE`/`GLFW_OFFSCREEN` hint, no `EGL`/`OSMesa` path, and no headless option anywhere in
`CMakeLists.txt`** — I read the whole file; the only conditionals are `UNIX OR APPLE` vs Windows for
finding GLFW, and a `Rel` build install rule. GLFW + ImGui are hard requirements: they are listed
unconditionally in `add_executable`, and `target_link_libraries` always links
`${GLFW3_LIB} opengl32 winmm` (Windows) or `glfw OpenGL::GL` (Unix). Windows links the **static**
`glfw3_mt.lib`; the `find_file(GLFW3_DLL glfw3.dll ...)` result is never consumed, and indeed no
`glfw3.dll` sits next to any built exe.

**Verdict for unattended CI:** the simulator GUI cannot be run headless without patching it. On
Windows it would need a logged-in interactive desktop session; on Linux, `Xvfb` + a Mesa softpipe/llvmpipe
GL 3.3 context. **The direct-shared-memory approach sidesteps all of this: the orchestrator does not
start qpm_simulator at all, and the only process that needs a window is the EGM game itself
(QSP_DemoOpenGL) — whose own headless story is a separate question I did not investigate.**

---

## 5. Files read (all absolute)

- `C:\Progetti\qpm_simulator\src\Main.cpp`, `src\Application.cpp`, `include\Application.h`,
  `include\QpmSharedMemory.h`, `include\qpm_simulator.h`, `include\ParseJson.hpp`,
  `src\qpm_simulator_config.json`, `CMakeLists.txt`, `CHANGELOG.md`, `README.md`
- `C:\Progetti\qpm_simulator\out\build\Debug\_deps\lsm-src\include\libsharedmemory\libsharedmemory.hpp`
  (libsharedmemory v1.8.0)
- `C:\Progetti\QPM2\src\hal\libqpm.cpp`, `C:\Progetti\QPM2\src\hal\QpmSharedMemory.h`,
  `C:\Progetti\QPM2\include\qpm.h`
- `C:\Progetti\QSP\CMakeLists.txt`, `src\LibPlatform\SecurityHandler.cpp`,
  `src\LibPlatform\ButtonHandler.cpp`, `src\LibPlatform\MoneyOutHandler.cpp`,
  `src\LibMarketSas\SasNetwork_Conditions.hpp`, `src\LibMarketSas\SasNetwork.cpp`
- `C:\Progetti\libsas\include\libsas.h`
- `C:\Progetti\QSP_DemoOpenGL\bin\Debug\x64\qsp-config.json`
- `C:\ProgramData\shared_memory\lsm_QpmSharedMemory.shm` (hexdump + live mmap read/write test)

## 6. UNKNOWNs

- Whether the Demo EGM runs SAS in RTE mode, i.e. whether an accepted note yields the
  denomination-specific `$47`..`$50` or the generic `$4F`.
- Which DIO indices QSP_DemoOpenGL's game code binds to `qspCollectButtonPressed()` / play / bet.
  The `Collect`/`Play`/`Bet` labels are the *simulator's* config; the game's own binding was not read.
- `QpmWrapper::SetIntrusionLabel` is declared (libqpm.cpp:543) but I did not locate its definition;
  since `QpmSharedState` has no label field it cannot be writing labels into this segment.
- Whether QSP_DemoOpenGL itself can run without a window (separate investigation).
- Linux behaviour was read from source only; not executed.
