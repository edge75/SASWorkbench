# M0 — Prove the wire

**Status: done.** `QSP_DemoOpenGL` boots with simulated peripherals, libsas initialises with
no Quixant board, and a real SAS 6.02 conversation runs over a TCP socket carrying 9-bit
symbols. Captured 2026-09-04; raw logs in `research/artifacts/m0/`.

---

## The dump

`TX` is the host (`tools/qxcom-tcp/host_probe.py`), `RX` is libsas inside the demo.
Each symbol is printed as `<wakeup>.<payload>`, so `1.81` is payload `$81` with the wakeup
bit set and `0.1F` is payload `$1F` without it.

```
[  4672.00ms] --  EGM connected from 127.0.0.1:57671
[  4687.00ms] TX 1.80   sync poll - address zero
[  4890.00ms] TX 1.81   general poll
[  4922.00ms] RX 0.15
[  5109.00ms] TX 1.80   sync poll - address zero
[  5312.00ms] TX 1.81   general poll
[  5359.00ms] RX 0.11
[  5531.00ms] TX 1.80   sync poll - address zero
[  5750.00ms] TX 1.81   general poll
[  5781.00ms] RX 0.18
[  5953.00ms] TX 1.80   sync poll - address zero
[  6172.00ms] TX 1.81   general poll
[  6203.00ms] RX 0.98
[  6390.00ms] TX 1.80   sync poll - address zero
[  6594.00ms] TX 1.81   general poll
[  6625.00ms] RX 0.99
[  6797.00ms] TX 1.80   sync poll - address zero
[  7000.00ms] TX 1.81   general poll
[  7031.00ms] RX 0.17
[  7219.00ms] TX 1.80   sync poll - address zero
[  7437.00ms] TX 1.81   general poll
[  7469.00ms] RX 0.15
[  7640.00ms] TX 1.80   sync poll - address zero
[  7828.00ms] TX 1.81   general poll
[  7859.00ms] RX 0.11
[  8047.00ms] TX 1.80   sync poll - address zero
[  8250.00ms] TX 1.81   general poll
[  8281.00ms] RX 0.00
[  8453.00ms] TX 1.80   sync poll - address zero
[  8656.00ms] TX 1.81   general poll
[  8687.00ms] RX 0.00
[  8859.00ms] TX 1.01 0.1F   long poll 1F - send gaming machine ID
[  8890.00ms] RX 0.01 0.1F 0.51 0.58 0.30 0.30 0.31 0.02 0.FF 0.00 0.00 0.00 0.30 0.30 0.31 0.00 0.00 0.00 0.39 0.32 0.31 0.30 0.86 0.3F
[ 11890.00ms] --  34 symbol(s) in 11 message(s) received from the EGM
[ 11890.00ms] --  EGM messages carrying a wakeup bit: 0 (expected 0)
```

### What it says

* **The wakeup bit is on the address byte and nowhere else.** Every host message starts
  `1.` and continues `0.`; every one of the 34 symbols the machine sent back is `0.`.
  This is the property a virtual COM pair cannot carry, and the reason for D2.
* **The exception queue drains, one exception per general poll**, and then reports `$00`
  (nothing to report) once empty — `$15`, `$11`, `$18`, `$98`, `$99`, `$17`, then the
  unacknowledged ones again, then empty. The machine's own log confirms each one
  (`sendHostPacket with exceptioncode:15 on channel:0`, and so on).
* **Long poll `$1F` is answered in full**, 24 bytes:
  `01 1F | 51 58 | 30 30 31 | 02 | FF | 00 | 00 00 | 30 30 31 00 00 00 | 39 32 31 30 | 86 3F`
  — address, command, machine ID `QX`, additional ID `001`, denomination, max bet,
  progressive group, game options, paytable `001`, base percentage `9210`, CRC.

The machine's own diagnostics for the same run:

```
sasInit:303: Channel Link Down and syncSAS=3
threadTimers:2451: Channel Link Up  sc[i].linkState=3 ... sc[i].syncSas=0
>> Sent package {0x01, 0x1F, 0x51, 0x58, ... 0x86, 0x3F, }
```

No `\\.\QX10`, no Quixant board, no serial hardware.

---

## What was built

### 1. `tools/qxcom-tcp/` — the missing transport

`SIMULATE_QXCOM` has been an option in QSP's CMake for a long time, and
`simulator/include/qxcom/` has shipped the headers, but nobody ever wrote the
implementation. This is it, backed by a TCP socket instead of `\\.\QXCOM`.

The seam turned out to be exactly where `BLOCKERS.md §2` predicted. MarketSas does this:

```cpp
QUIXANT_SERIAL_CALLBACKS serialCallbacks = {0};
serialCallbacks.callbacksVersion = CALLBACKS_VERSION;
assignCallbacks((void*)&serialCallbacks);       // libqxcom fills the struct in
sasSetSerialCallbacks((void*)&serialCallbacks); // libsas is handed the same struct
```

So supplying `assignCallbacks` is the whole of it. Wire format: one symbol per
little-endian `uint16`, bit 8 = wakeup.

The 8-bit `read`/`write` callbacks **return an error rather than transferring data** — an
8-bit path cannot represent the wakeup bit, and a link that silently drops it would look
healthy while being unusable. libsas uses only the 9-bit `readExtended`/`writeExtended`
pair, which is fully implemented.

`tools/qxcom-tcp/test/link_roundtrip.cpp` drives the transport through the same call
sequence libsas uses and checks the wakeup bit survives a round trip; it also asserts that
`setState` with 8 data bits is refused.

### 2. `CMakeUserPresets.json` — the build wiring

Gitignored in `QSP_DemoOpenGL` (`.gitignore:131`), so it is not a tracked file, and it is
the only file added to that repository. It defines one preset, `SasWorkbenchM0`, inheriting
`CustomSimulation`, and names three CMake files that live in *this* repo
(`tools/qxcom-tcp/cmake/`). Those three are injected through CMake's own
`CMAKE_PROJECT_<name>_INCLUDE[_BEFORE]` hooks, so no `CMakeLists.txt` anywhere is edited:

| Injected into | Does | Why |
|---|---|---|
| the demo | supplies the `qxcom_static` target, adds `SKIP_INTEGRITY_CHECKS` | QSP and libsas both fetch the hardware libqxcom only `if(NOT TARGET qxcom_static)`, so defining it first wins. The integrity check has nothing to verify against a simulated NVRAM file. The target is added as a subdirectory, because a Release build refuses to configure if the demo's own directory already holds one. |
| QPM2 | `set(HAL ON)` for that project only | Peripherals must be simulated; nothing else in the build may be. |
| libsas | `add_compile_definitions(VALIDATION_DEBUG)` | Compiles out the `\\.\QX10` platform check, using the library's own guard. |

The preset also sets **`CMAKE_BUILD_TYPE=Release`**, and that is not a performance choice. A debug
build of the platform requires the `DEMO` licence feature on top of `SAS`, deliberately, so that a
debug build cannot run on production hardware by accident — and the key in this machine's secure
storage carries `SAS` only. Built `Debug`, a valid key is refused, the machine raises a licence
lockup and takes no money; built `Release`, the same key validates. `BLOCKERS.md` Trap D is the
whole story. It also sets `FETCHCONTENT_UPDATES_DISCONNECTED=ON`, because the branches the demo
pins its dependencies to have been renamed upstream and every measurement in this project was taken
against the revisions already on disk.

Build it with:

```
cmake --preset SasWorkbenchM0 && cmake --build out/build/SasWorkbenchM0
```

and run it with `tools/qxcom-tcp/run-m0.ps1`.

### 3. `tools/qxcom-tcp/host_probe.py` — the throwaway host

Listens, sends the polls, dumps both directions. It implements no state machine and no
assertions; M1 replaces it.

---

## Two things worth carrying into M1

**A host that only ever polls one machine gets silence.** After power-up (and after every
loop break) the machine ignores polls addressed to itself until it has seen either a poll
for a *different* machine or a general poll of address zero — SAS 6.02 section 3.3, and
libsas implements it exactly (`syncSas`, states `SAS_CHIRP` → `SAS_SYNC`). The first run of
this probe sent ten general polls and one long poll to address 1 and got nothing back, and
it was right to get nothing back. **The poll engine must emit the sync poll**, and a test
that asserts "the machine answered" needs to distinguish "wrong" from "correctly ignoring
me while it resynchronises".

**Startup exceptions are not noise, and they are not stable.** This run began with six
queued exceptions (`$15` card cage, `$11` slot door, `$18`, `$98`/`$99` power-off access,
`$17`) because the simulated peripherals reported doors already open at boot. Any
conformance test that starts by asserting an empty exception queue will fail for reasons
that have nothing to do with the behaviour under test. This is the same territory as the
`qpm_simulator` mask-zeroing race in `BLOCKERS.md §3` Trap B.

---

## Deviation from the plan, and why

`PLAN.md` said to link `sas_static_testable`. The build links `sas_static` instead, rebuilt
with `VALIDATION_DEBUG` — which produces the same thing (`sas_static_testable` *is* the full
sources plus `VALIDATION_DEBUG`) with less surgery:

* `sas_static_testable` only exists when `BUILD_TESTING` is on, which also builds libsas's
  own test suite and `utils/hwconformance` — a directory this project is not to touch.
* MarketSas hard-codes `target_link_libraries(marketsas PRIVATE sas_static)`. Redirecting
  that to another target means rewriting a target property of a repository we do not own,
  from outside. Giving `sas_static` the property that makes the testable target testable is
  the same outcome by a shorter route.

Nothing about D15 or the strategy in `BLOCKERS.md §2` changes: libsas is *rebuilt*, not
edited, and the switch is its own.

## Constraints held

* `C:\Progetti\QSP` — **not modified**; `git status` clean.
* `C:\Progetti\QSP_DemoOpenGL` — on branch `qsp-new-callbacks`, `git status` clean. The only
  file added is the already-gitignored `CMakeUserPresets.json`.
* `C:\Progetti\libsas` — **not modified**; `git status` clean.
* `libsas/utils/sashost`, `utils/poller`, `utils/hwconformance` — not read, and with
  `BUILD_TESTING=OFF` not built either.
* The user's `C:\ProgramData\quixant\launchpad\nvram.bin` was backed up before the first run.
