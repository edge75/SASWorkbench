# 05 — Serial Diagnosis: why `sastest.exe` dies on a USB-to-serial adapter, and the serial strategy for the replacement app

Date: 2026-09-03 · Target: `C:\IGT3\sastest.exe` (PE32, i386, GUI, 985,088 bytes, dated **2005-12-16**, Borland C++Builder / VCL)

Scope note: `C:\Progetti\libsas\utils\sashost`, `utils/poller`, `utils/hwconformance` were **not** read. Only `C:\Progetti\libsas\src`, `C:\Progetti\libsas\include`, and the vendored `qxcom` dependency under `out/build/Debug/_deps/qxcom-src` were used.

---

## 0. Bottom line

`sastest.exe` fails on a USB-to-serial adapter for **two independent, individually fatal reasons**, both proven from the binary:

1. **It physically cannot name a port above COM8.** The device-name table is a hard-coded 7-entry array of the legacy DOS names `"COM1:"…"COM8:"`. The string `\\.\` does not occur **anywhere** in the 985 KB file. `sastest.ini` has `Port = 11`, which indexes *past the end of that array* into unrelated stack memory. This alone produces the app's own `"Could not create Comm Port"` error before a single byte is exchanged.
2. **Its wakeup-bit mechanism is a timing race that only a real 16550 UART can win.** It flips the DCB between MARK and SPACE parity per frame and separates the address byte from the data bytes with nothing but `Sleep(WakeupDelay)` — 2 ms, per the INI. It never calls `FlushFileBuffers` (not even imported), so it cannot know the address byte left the UART. On USB the parity change travels the *control* pipe while the data travels the *bulk* pipe; 2 ms is not enough to order them.

Reason 1 is what the user is hitting today. Reason 2 is what they would hit next if reason 1 were patched.

---

## 1. How `sastest.exe` opens the port — evidence

### 1.1 Import table (no raw hardware IO)

`python pe_imports.py C:\IGT3\sastest.exe` → 8 sections, imagebase `0x400000`, PE32.

| DLL | Comm-relevant imports |
|---|---|
| KERNEL32 | `BuildCommDCBA`, `ClearCommError`, `CreateFileA`, `GetOverlappedResult`, `PurgeComm`, `ReadFile`, `SetCommMask`, `SetCommState`, `SetCommTimeouts`, `SetupComm`, `WaitCommEvent`, `WriteFile`, `Sleep`, `GetTickCount`, `SetPriorityClass`, `SetThreadPriority`, `GetPrivateProfileStringA`, `CreateThread` |
| WINMM | `timeBeginPeriod`, `timeEndPeriod`, `timeSetEvent`, `timeKillEvent` |
| ADVAPI32 | `RegOpenKeyExA`, `RegQueryValueExA`, `RegCloseKey` (3 only) |

**Conclusively absent:** `inpout32`, `dlportio`, `giveio`, `ntport`, `winio`, `porttalk` — no direct hardware port IO. Also absent: `FlushFileBuffers`, `EscapeCommFunction`, `GetCommState`, `GetCommModemStatus`, `TransmitCommChar`, and any SetupAPI/WMI enumeration.

> **The "direct port IO" hypothesis is dead.** It is a pure Win32 comm-API application. That also kills the idea that a shim driver could rescue it.

### 1.2 The port-name table — the primary failure

Raw bytes at file offset `0x000B761C` (`xxd -s 751100 -l 240 sastest.exe`):

```
000b75fc: 1c82 4b00 2282 4b00 2882 4b00 2e82 4b00  ..K.".K.(.K...K.
000b760c: 3a82 4b00 4082 4b00 4682 4b00 fd03 fd02  :.K.@.K.F.K.....
000b761c: 434f 4d31 3a00 434f 4d32 3a00 434f 4d33  COM1:.COM2:.COM3
000b762c: 3a00 434f 4d34 3a2c 434f 4d35 3a00 434f  :.COM4:,COM5:.CO
000b763c: 4d36 3a00 434f 4d37 3a00 434f 4d38 3a00  M6:.COM7:.COM8:.
```

Seven `char*` at VA `0x004B81FC`, pointing at seven NUL-terminated strings. Note byte `0x2C` (`,`) at offset `0x000B7633` where a NUL belongs: **`"COM4:,COM5:"` is a single string** — a comma-for-NUL typo in the original source that also shifts every later index.

The loader (capstone; `VA = fileoff + 0x400A00`):

```asm
0x004132b7  lea  edi, [ebp - 0x4c]
0x004132ba  mov  esi, 0x4b81fc          ; &portNames[0]
0x004132ce  mov  ecx, 7                 ; <<< SEVEN entries, hard-coded
0x004132e4  rep  movsd                  ; copy onto the stack at [ebp-0x4c]
```

The lookup and open:

```asm
0x004134a5  push 0                      ; hTemplateFile
0x004134a7  push 0x40000000             ; FILE_FLAG_OVERLAPPED
0x004134ac  push 3                      ; OPEN_EXISTING
0x004134ae  push 0                      ; lpSecurityAttributes
0x004134b0  push 0                      ; dwShareMode = 0
0x004134b2  push 0xc0000000             ; GENERIC_READ|GENERIC_WRITE
0x004134b7  mov  eax, dword ptr [ebp - 0x30]         ; <- the INI "Port" value
0x004134ba  mov  edx, dword ptr [ebp + eax*4 - 0x50] ; <- portNames[Port]  NO BOUNDS CHECK
0x004134be  push edx
0x004134bf  call 0x4b035c               ; CreateFileA
0x004134d0  cmp  dword ptr [eax + 0xfc], -1
0x004134e0  push 0x4b82b2               ; "Could not create Comm Port"
```

The array was copied to `[ebp-0x4c]` but is indexed from `[ebp-0x50]`, i.e. **1-based**. Real mapping:

| INI `Port` | device name actually opened |
|---|---|
| 1 | `COM1:` |
| 2 | `COM2:` |
| 3 | `COM3:` |
| 4 | `COM4:,COM5:` — **invalid device name, always fails** |
| 5 | `COM6:` — **off by one** |
| 6 | `COM7:` — off by one |
| 7 | `COM8:` — off by one |
| ≥8 | **out of bounds**: reads unrelated stack. `Port = 8` reads `[ebp-0x30]`, which is the port number itself. **`Port = 11` reads `[ebp-0x24]`, uninitialised stack.** |

Worse, before `CreateFileA` the code runs a `repne scasb` strlen on that pointer (`0x0041348D`) to copy the name into the object at `+0x19B` — so an out-of-range `Port` can access-violate before it ever reaches `CreateFileA`.

`SASTEST.HLP` confirms the intended semantics in the vendor's own words:

> "To select the comm port you are using, you must go to the INI file and under the SAS Protocols section specify the port. example: port = 2 — The above line sets the comm port to comm2."

**And the killer** — searching the whole 985 KB file for the DOS-device prefix:

```
literal '\\.\'  count: 0
'.\COM'         count: 0
'\\'            count: 1   (at 0x65258, unrelated)
'COM%'          count: 0   (no format string — the names are a fixed table)
```

There is **no code path in this binary that can ever produce a `\\.\COMxx` name**, and `CreateFileA("COM10:")` fails by design on Win32. `COM8` is a hard ceiling, and in practice the usable set is `{1,2,3}` plus three misnumbered slots.

### 1.3 What `Port` and `Index` mean

- `[SAS Protocols] Port` — a **1-based COM number** (per HLP), used unchecked as the table index. Legal range in this build: 1–7, of which 4 is broken and 5–7 are off by one. `Port = 11` is out of range.
- `[Communications] Index` — the key string exists at VA `0x00727021` in the `GetPrivateProfileStringA` section/key block, so it is read; its consumer was not traced. **UNKNOWN.**
- There is **no port combo box populated from these strings** — the only reference to the table is the single site at `0x004132BA`. Port selection is INI-only, which is exactly why the user has no way to see or pick their real port.

---

## 2. The wakeup bit — how it is really done

### 2.1 What `sastest.exe` (the HOST) does: per-byte MARK/SPACE parity flipping

Three `BuildCommDCBA` calls at open (`0x004133C7`, `0x00413437`, `0x00413448`) build two persistent DCBs from these literals at `0x000B764C`:

```
baud=19200 parity=S data=8 stop=2     -> DCB at object+0x4C  (SPACE = data bytes)
baud=19200 parity=M data=8 stop=2     -> DCB at object+0x30  (MARK  = address byte)
baud=19200 parity=S data=8 stop=2
```

So the line is **19200 baud, 8 data bits, MARK-or-SPACE (stick) parity, 2 stop bits** — a 12-bit frame ≈ **625 µs per byte**.

The transmit routine, reconstructed from `0x00413920`–`0x00413AAE`:

```c
ResetEvent(obj->hEvent);
if (obj->wakeupEnabled /* +0x199 */) {
    SetCommState(hComm, &obj->dcbMARK);           // 0x00413953
    WriteFile(hComm, buf, 1, &n, &ov);            // 0x00413975  ONE byte, OVERLAPPED, NOT waited on
    ResetEvent(obj->hEvent);
    if (len == 1) return 1;
    if (obj->dcbMARK.BaudRate <= 19200)           // 0x004139A3  cmp [edx+0x34], 0x4B00 / ja
        Sleep(obj->wakeupDelayMs);                // 0x004139C2 / 0x004139D3  <<< THE ONLY BARRIER
    SetCommState(hComm, &obj->dcbSPACE);          // 0x004139E9
    WriteFile(hComm, buf+1, len-1, &n, &ov);      // 0x00413A10
} else {
    SetCommState(hComm, &obj->dcbSPACE);          // 0x00413A28
    WriteFile(hComm, buf, len, &n, &ov);          // 0x00413A4D
}
WaitForSingleObject(obj->hEvent, 2000);           // 0x00413A72  push 0x7D0
GetOverlappedResult(hComm, &ov, &n, FALSE);       // 0x00413A91
```

`obj+0x79C` is the wakeup delay: defaulted to `1` at `0x004132E9` (`mov dword [eax+0x79c], 1`) and written by the trivial setter at `0x00413B30` (`mov [edx+0x79c], eax`) fed from `[SAS Protocols] Wakeup Delay`. **`sastest.ini` sets `Wakeup Delay = 2`, i.e. `Sleep(2)`.**

`timeBeginPeriod` is called at `0x00401F6A`, so `Sleep(2)` is a real ~2–3 ms. Against a 625 µs frame on a native 16550 that is a 3–4× margin — a race it usually wins, which is exactly why this program "works on the old PC".

DTR/RTS (`[Communications] DTR = 1`, the "Set DTR"/"Clear DTR" buttons) is done by rewriting the DCB flag bytes and re-calling `SetCommState` (`0x00413B4C`: `and cl,0xCF / or cl,0x10` on object `+0x38/+0x39` and `+0x54/+0x55`, then `SetCommState` at `0x00413BF9`/`0x00413C0F`) — consistent with `EscapeCommFunction` being absent from the imports.

The vendor documented the weakness themselves, in `SASTEST.HLP`:

> "Since the SAS protocol uses a 'wake up bit' (a 9th bit) for machine addressing we have to use the parity bit as a substitute. … This creates problems switching the parity bit on and off as necessary, because Windows requires you to totally re-initialize the COMM port and **it is virtually impossible to determine if the byte with the wakeup bit really went out of the UART**."

That is a false statement about Win32, and it is the root of the design: `FlushFileBuffers` has done exactly that since NT 3.1, and it is *not imported by this binary*. **The replacement app fixes this by calling it.**

### 2.2 What `libsas` (the EGM, on real hardware) does: a genuine 9-bit UART

`libsas` does **not** use parity tricks. `include/libsas_uart.h:49-53`:

```c
#if defined(_WIN32)
#define DEV_NODE	"\\\\.\\QXCOM"
#else
#define DEV_NODE	"/dev/ttyQ"
#endif
```

`src/libsas_uart.cpp:124`:

```c
result = sasContext.serialCallbacks.setState(*serialHandle, 19200, 9, UART_PAR_NONE, UART_STOP_ONE, FALSE);
```

**19200 baud, 9 data bits, no parity, 1 stop bit.** The 9th bit is a real data bit. Bytes are carried as `unsigned short` with bit 8 as the wakeup flag — `include/libsas_db.h:1081-1090`:

```c
unsigned short Payload : 8;
unsigned short Wakeup  : 1;
typedef union _SAS_SERIAL_DATA_UNIT { ... }
```

tested as `rxd & 0x100` throughout `readSasSerial` (`libsas_uart.cpp:231, 268, 453, 476`).

How the 9 bits are physically produced (vendored dependency `out/build/Debug/_deps/qxcom-src`):

- `v1/com/windows/com.c:172` — `case COM_DB9: dcb.ByteSize = DATABITS_8; serial->EnableNineBit = TRUE;`
  then `com.c:221` — `DeviceIoControl(serial->hPort, IOCTL_COM_EXTEND_DATABITS, &serial->EnableNineBit, ...)`.
  A **custom Quixant kernel driver** does the work; user mode just packs 2 bytes per 9-bit word (`com.c:388-390`).
- `v1/com/windows/com_windows.h:44-46` — `COM_PARITY_MASK 0x38`, `COM_FLAG_MARK_PARITY 0x28`, `COM_FLAG_SPACE_PARITY 0x38` — the 16550 **LCR** stick-parity bits.
- `v1/com/linux/com_linux.c:685-690` — the mechanism in the clear, poking the LCR directly via `ioperm`/`inb`/`outb`:

```c
reg_value = inb(handle->fintek_settings.io_base + LINE_CONTROL_REGISTER);
if(ninthbit) { reg_value &= ~0x38; reg_value |= 0x28; }   // MARK
else         { reg_value &= ~0x38; reg_value |= 0x38; }   // SPACE
outb(reg_value, (handle->fintek_settings.io_base + LINE_CONTROL_REGISTER));
```

So even the "real" implementation is stick-parity underneath — but performed **inside the kernel, one I/O instruction away from the UART transmit register**, on a known Fintek 16550-class part with a known FIFO. That is a fundamentally different guarantee from user-mode `SetCommState` on a bus with a 1 ms frame quantum.

### 2.3 The decisive question: can a generic USB adapter do per-byte MARK/SPACE?

**Answer: not reliably, and not by the mechanism `sastest.exe` uses.**

Why it breaks:

1. **Two pipes, no ordering guarantee.** Data goes out the **bulk OUT** endpoint; a parity change is a **control transfer** on endpoint 0 (FTDI `SIO_SET_DATA_REQUEST`, CP210x `SET_LINE_CTL`, CH340 vendor LCR write, `usbser.sys` CDC `SET_LINE_CODING`). The host controller schedules them independently, in 1 ms frames. Nothing makes the LCR change land on the byte boundary you intended.
2. **The chip glitches on reconfiguration mid-transmit.** The Linux `ftdi_sio` maintainers hit this directly: unnecessary control URBs that reset the data/parity or baud rate "cause the FTDI chip to glitch it's serial output and cause a corruption of a character it is currently outputting". The fix was to *skip* redundant reconfigurations — the opposite of what SAS needs, which is a deliberate reconfiguration every frame.
3. **Buffering hides the wire from you.** FTDI's own knowledge base explains the driver buffers writes precisely because per-byte writes destroy USB throughput. `WriteFile` returning tells you nothing about the wire.
4. **`Sleep(2)` is far too small.** A USB control round-trip plus bulk scheduling plus the adapter's internal FIFO is comfortably ≥ 2 ms of jitter on its own.
5. **Some adapters get stick parity wrong or not at all.** Reports on CP2102, PL2303, CH340 and FT232 describe parity bits generated incorrectly; CH340 mark/space needs Linux ≥ 4.12. Genuine FTDI is the only family with a solid reputation here.

**The asymmetry — and it is a large one.** A SAS *host* only needs to **transmit** wakeup discrimination. It does **not** need per-byte **receive** discrimination:

- In `libsas`, the EGM sets the wakeup bit on exactly one kind of transmission — `libsas_uart.cpp:634-641`: *"The loop-break chirp is the only transmission that carries the wakeup bit"*. Normal EGM replies are wakeup = 0.
- `sastest.exe` only ever reports an *aggregate* parity condition — the string `"GAME IS SENDING WAKE UP BITS!!!"`, driven by `ClearCommError` (five call sites, `0x00413779`–`0x004138D7`), never per byte.

So the hard half of the problem — per-byte RX tagging, which genuinely needs the QXCOM driver or a true 9-bit UART — **does not apply to the host we are building**. That materially improves the odds.

**Mitigations, in priority order:**

| # | Mitigation | Effect |
|---|---|---|
| 1 | Write the address byte **alone**, then `FlushFileBuffers` (drains TX, "will not return until all pending write operations have been transmitted"), **then** switch parity, then write the body, then flush again. | Removes the race entirely on a native UART. On USB it removes the *host-side* race and leaves only the adapter FIFO. **The single most important fix, and exactly what `sastest.exe` omits.** |
| 2 | Drop the FTDI/CP210x latency timer from 16 ms to 1 ms. | Fixes RX turnaround, not TX ordering. Necessary but not sufficient. |
| 3 | Prefer a real 9-bit/stick-parity path — genuine FTDI FT232R/FT232H via **D2XX** (`FT_SetDataCharacteristics`, `FT_SetLatencyTimer`), or a PCIe/ExpressCard 16550. | Best-effort correctness on USB. |
| 4 | **Preferred:** treat a native 16550 (or QXCOM) as the supported path, label USB adapters *best-effort*, and ship a runtime self-test. | Honest, and matches physics. |
| 5 | Escape hatch: run wakeup-less where the peer tolerates it (`sastest.exe` already has this — the "Wake Up Bits" checkbox, `WakeUpCheckBoxClick`, tooltip *"Unchecking this box disables wakeup bits"*, taking the `else` branch at `0x00413A17`). | Sidesteps the problem for single-EGM bench testing. |

---

## 3. Serial strategy for the new .NET/Avalonia app

Environment verified here: SDKs **8.0.424, 10.0.111, 10.0.300**; runtimes `Microsoft.NETCore.App 8.0.30 / 10.0.8 / 10.0.11`.

### 3.1 `System.IO.Ports.SerialPort` — measured, not assumed

I built and ran a probe (`scratchpad/research/sptest`, `net10.0`, `System.IO.Ports 10.0.0`) against this machine's real ports:

```
Runtime: 10.0.11  OS: Microsoft Windows NT 10.0.26200.0
GetPortNames: COM3, COM6, COM5, COM4
Parity enum values: None, Odd, Even, Mark, Space      (Mark=3, Space=4)

Open("COM3")        -> OK
Open("\\.\COM3")    -> ArgumentException: The given port name (\\.\COM3) does not resolve to a valid serial port.
Open("\\?\COM3")    -> ArgumentException: ... does not resolve to a valid serial port.
Open("COM3:")       -> ArgumentException: ... does not resolve to a valid serial port.
Open("COM12")       -> FileNotFoundException: Could not find file 'COM12'.   (port absent, NOT a naming failure)

OPENED COM3 with Parity.Mark OK. Switching to Space on an OPEN port -> allowed.
BaseStream concrete type: System.IO.Ports.SerialStream

SetCommState (parity swap)           x2000 = 75.90 ms => 38.0 us each
BaseStream.Flush (FlushFileBuffers)  x2000 =  5.88 ms =>  2.9 us each
```

(COM3/COM6 are Bluetooth virtual ports with no paired peer, so these are API-overhead numbers, not UART behaviour.)

**Findings:**

- **COM10+ works — but you must pass the plain name.** `SerialPort` builds the device path itself; `SerialStream.Windows.cs` calls `CreateFile(@"\\?\COM" + portNumber, ...)`. Passing `\\.\COM12` yourself is **rejected**, as is the legacy `COM12:` form. This is exactly the bug class that kills `sastest.exe`, and .NET is immune to it *provided you never hand-build the path*.
- **`Parity.Mark` / `Parity.Space` exist and work on Windows** — they map to `DCB.Parity = 3/4` and the setter re-issues `SetCommState` on a live handle at ~38 µs.
- **Cross-platform: Windows yes, Linux DANGEROUS.** The Linux backend does **not** implement `CMSPAR`. `src/native/libs/System.IO.Ports.Native/pal_termios.c`:

  ```c
  switch (parity) {
  case ParityOdd:  term.c_cflag |= PARENB | PARODD; break;
  case ParityEven: term.c_cflag |= PARENB; term.c_cflag &= ~((tcflag_t)(PARODD)); break;
  default:         term.c_cflag &= ~((tcflag_t)(PARENB | PARODD));
  }
  ```

  `Parity.Mark` and `Parity.Space` fall into `default:` and **silently disable parity — no exception, no warning.** On Linux the wakeup bit would never be transmitted and the app would look like it was working. Must-fix trap. (`ParityReplace` is likewise unimplemented on Linux — dotnet/runtime#110349.)
- **TX drain is available and is the crux fix**: `SerialPort.BaseStream.Flush()` → `FlushFileBuffers`, which for a comm handle "only flushes the transmit buffer … is subject to flow control but not to write time-outs, and will not return until all pending write operations have been transmitted."
- **Controllable:** `DtrEnable`, `RtsEnable`, `ReadTimeout`, `WriteTimeout`, `DiscardInBuffer` (`PurgeComm` RXCLEAR|RXABORT), `DiscardOutBuffer` (TXCLEAR|TXABORT), `ReadBufferSize`/`WriteBufferSize` (`SetupComm`), `ErrorReceived` (aggregate parity/framing/overrun — enough for the host's RX needs).
- **Documented pitfalls to avoid:** `DataReceived` is "completely unreliable" with out-of-order notifications; `BytesToRead` "clears error status without reporting it"; `Read`/`ReadLine` "handle errors completely wrong, and are synchronous". Use `BaseStream.ReadAsync` — "the only serial port read approaches that work correctly".

### 3.2 Direct P/Invoke to kernel32 — is it warranted?

**Yes, as a second backend — not as the only one.** What it buys over `SerialPort`:

1. **`COMMTIMEOUTS` you actually control.** SAS wants `ReadIntervalTimeout` ≈ 5 ms with a small `ReadTotalTimeoutConstant`. `SerialPort` maps its simplified timeouts onto this and you lose the interval semantics `libsas` depends on (`SAS_INTER_BYTE_DEADLINE`, `libsas_uart.cpp:208`).
2. **A single `SetCommState` with a pre-built DCB**, avoiding `SerialPort`'s per-property read-modify-write round trips.
3. **`EscapeCommFunction(SETDTR/CLRDTR/SETRTS/CLRRTS)`** — instant line control without rewriting the DCB (which `sastest.exe` had to do, having never imported it).
4. **Overlapped IO with your own `OVERLAPPED`/event**, plus `ClearCommError` giving `COMSTAT.cbInQue` and the exact error bits.
5. **`SetCommMask`/`WaitCommEvent` with `EV_TXEMPTY`** — a second, independent confirmation the shift register drained, complementing `FlushFileBuffers`.
6. On Linux the matching primitives are `tcsetattr` with `CMSPAR` (which .NET does not do), `tcdrain` (the `FlushFileBuffers` equivalent), and `TIOCSSERIAL`/`ASYNC_LOW_LATENCY`.

**Recommendation:** an `ISasSerialPort` abstraction with `SerialPort` as the safe default on Windows and a P/Invoke backend for the wakeup-bit path and for Linux. Do not build only the P/Invoke one — you lose .NET's correct `\\?\COM` naming for free.

### 3.3 Port enumeration with friendly names — the direct fix for the user's complaint

Highest-value, lowest-risk feature in the whole report. `sastest.exe` has **no enumeration at all** — the port is an opaque INI integer indexing a broken 7-entry table.

**Windows — WMI (verified working here):**

```powershell
Get-CimInstance Win32_PnPEntity | Where-Object { $_.Name -match 'COM\d+' } |
  Select-Object Name, DeviceID, Service, Manufacturer
```
→ returned `Standard Serial over Bluetooth link (COM5)` etc. with full `DeviceID` and `Service`.

C#: NuGet `System.Management` (Windows-only) —
`new ManagementObjectSearcher("SELECT Name, DeviceID, Service FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'")`, then regex `\(COM(\d+)\)` off `Name`. Prefer `Win32_PnPEntity` over `Win32_SerialPort`: the latter misses many USB CDC devices.

**Windows — registry (fastest, no WMI dependency; verified):**

`HKLM\HARDWARE\DEVICEMAP\SERIALCOMM` returned exactly the four live ports (value name = device path, data = `COMn`). Combine with `HKLM\SYSTEM\CurrentControlSet\Enum\...\Device Parameters\PortName` plus the sibling `FriendlyName`/`DeviceDesc` for the human label. Use `Microsoft.Win32.Registry` (in-box for `net8.0-windows`/`net10.0-windows`).

Also read `HKLM\SYSTEM\CurrentControlSet\Control\COM Name Arbiter\ComDB` — a bitmap of every claimed COM number (bit *n* = COM *n+1*) — so the UI can say "COM11 has never been assigned on this machine" instead of failing silently.

**SetupAPI** (`SetupDiGetClassDevs` with `GUID_DEVINTERFACE_COMPORT`) is the most complete but needs substantial P/Invoke; worth it only if you also want hot-plug `WM_DEVICECHANGE` notification, which you probably do.

**Linux:** enumerate `/dev/serial/by-id/*` (symlinks carrying vendor/product/serial) and resolve to `/dev/ttyUSB*` / `/dev/ttyACM*`; fall back to `/sys/class/tty/*/device/driver`. Plain `Directory.GetFiles("/dev","ttyUSB*")` gives no friendly name.

**NuGet worth taking:** `System.IO.Ports` (required), `System.Management` (Windows WMI). **Not worth taking:** the `SerialPortStream`/RJCP forks — they add a native shim you would then own; the enumeration you need is ~80 lines of registry/WMI code.

### 3.4 FTDI latency timer — verified path

From FTDI's own knowledge base article *"Setting a Custom Default Latency Timer Value"*:

```
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\FTDIBUS\<VID+PID+serial>\0000\Device Parameters\LatencyTimer   (REG_DWORD)
```

Default **16 ms** on both Windows and Linux; range **1–255 ms**. The driver persists the per-device value, so the documented procedure is: write the value, then **reopen the port**. (`projectgus/hairless-midiserial`'s `PortLatency_win32.cpp` is a working reference implementation.)

**On this machine `HKLM\SYSTEM\CurrentControlSet\Enum\FTDIBUS` does not exist** — no FTDI device has ever been attached (§4). So the path is confirmed from vendor documentation, not local observation. Treat the exact subkey shape as **verified-by-docs, to be re-confirmed against real hardware**.

- **CP210x:** same idea, different tree — `HKLM\SYSTEM\CurrentControlSet\Enum\USB\VID_10C4&PID_EA60\<instance>\Device Parameters\LatencyTimer`, DWORD, default 16 ms, range 1–255. **Confidence MEDIUM** — secondary source, not Silicon Labs' own docs.
- **CH340:** no documented equivalent found. **UNKNOWN.** Assume untunable; recommend against CH340 for SAS.
- **usbser.sys (CDC-ACM):** no latency knob at all. **None.**

**What the app should do:** on open, if the device is FTDI or CP210x, read `LatencyTimer`; if > 2 ms show a non-modal banner — *"COM12 (FTDI) has a 16 ms latency timer; SAS turnaround needs ≤ 2 ms. [Fix it] [Ignore]"* — and offer to write `1` (needs elevation; degrade to instructions if denied). Never write it silently.

### 3.5 Timing

| Parameter | Value | Source |
|---|---|---|
| Baud / framing | 19200; EGM = 8 data + wakeup + 1 stop (true 9-bit); `sastest.exe` = 8 data + stick parity + **2** stop | `libsas_uart.cpp:124`; DCB literals at `0x000B764C` |
| Inter-byte delay | **≤ 5 ms** | `libsas_uart.cpp:201-207`, quoting *"SAS 6.02 section 2.3.2"* verbatim; libsas itself budgets 9 ms "to be charitable" |
| EGM response time | **~20 ms** | secondary literature — **MEDIUM confidence**, not read from the 6.02 spec |
| Poll rate | 200 ms typical, down to 40 ms with RTE | matches `sastest.ini` `Poll Rate = 200`; `sastest.exe` drives it with `timeSetEvent` (`0x004023C7`, `0x00402432`) |

One byte at 19200 = 521–625 µs. The 5 ms inter-byte budget is **~8–10 byte times** — comfortable for a native UART, but a 16 ms FTDI latency timer blows it by 3×. **That is the whole ballgame for USB.**

Can .NET on Windows meet this? **Yes — 5 ms and 20 ms are not hard real-time**, provided you engineer the IO path:

1. **`timeBeginPeriod(1)`** for the session (`sastest.exe` already does this at `0x00401F6A`). On Windows 10 2004+ it is per-process, so it is safe, and it makes timer granularity 1 ms.
2. **One dedicated foreground thread per port**, `ThreadPriority.AboveNormal` — **not** `Highest`, **not** `RealTime`. (`sastest.exe` calls `SetPriorityClass`+`SetThreadPriority` and its own help file still blames Windows scheduling; over-elevating starves the UI and makes it worse.) Never run the IO loop on the thread pool: injection delay under load is ~500 ms, 100× the budget.
3. **`ServerGarbageCollector=false`, `ConcurrentGarbageCollection=true`, `TieredPGO=true`** — and the important one, **allocate nothing on the IO path.** Pre-allocated frame buffers, `ArrayPool<byte>`/`Span<byte>`, no `string` formatting in the loop (do it on the logging side). A gen-0 GC is ~100 µs–1 ms; a gen-2 pause will exceed 5 ms. Use `GC.TryStartNoGCRegion` around a poll burst only if measurement demands it.
4. **`System.Threading.Channels.Channel<T>`** (unbounded, `SingleReader=true`, `SingleWriter=true`) to hand frames to the UI — never a lock shared with the IO thread, never `Dispatcher.Invoke` from it. In Avalonia, marshal with `Dispatcher.UIThread.Post` at a **coalesced** rate (~30 Hz), not per byte.
5. **Timestamp with `Stopwatch.GetTimestamp()`** (QPC). Never `DateTime.Now`, never `GetTickCount` — 15.6 ms granularity, which cannot resolve a 20 ms requirement, yet is what `sastest.exe` uses for its response-time display.
6. **Drive the poll cadence from a waitable timer / `PeriodicTimer` on the IO thread**, and display measured jitter so the operator can see when the machine is the problem.
7. **`COMMTIMEOUTS.ReadIntervalTimeout = 5`** so the driver enforces the inter-byte deadline instead of you polling for it.

---

## 4. What is actually on THIS machine

```
Get-CimInstance Win32_PnPEntity | ? { $_.Name -match 'COM\d+' }
  Standard Serial over Bluetooth link (COM5)  BTHENUM\...LOCALMFG&0002...   Service BTHMODEM
  Standard Serial over Bluetooth link (COM4)  BTHENUM\...VID&0001005D_PID&223B...
  Standard Serial over Bluetooth link (COM3)  BTHENUM\...LOCALMFG&0000..._00000000
  Standard Serial over Bluetooth link (COM6)  BTHENUM\...LOCALMFG&0000..._00000001

HKLM\HARDWARE\DEVICEMAP\SERIALCOMM
  \Device\BthModem0=COM5  \Device\BthModem1=COM4  \Device\BthModem2=COM3  \Device\BthModem3=COM6

[System.IO.Ports.SerialPort]::GetPortNames()  ->  COM3, COM6, COM5, COM4

COM Name Arbiter ComDB claimed: COM3, COM4, COM5, COM6, COM7
```

Phantom (registered, not currently attached):

```
HKLM\SYSTEM\CurrentControlSet\Enum\USB\VID_16F5&PID_0807&MI_00\6&2b0c2591&0&0000\Device Parameters  PortName=COM7
  FriendlyName : Communications Port (COM7)
  Mfg          : @oem310.inf,%mfgname%;http://www.futurelogic-inc.com
  Service      : usbser
```

**Reading:**

- **There is no COM11 on this machine and there never has been** (`ComDB` tops out at COM7). `sastest.ini`'s `Port = 11` cannot open anything here even in principle — it walks off the end of a 7-entry table. `Port = 11` is almost certainly a leftover from the machine where the user *did* have a USB adapter at COM11, which is itself strong circumstantial confirmation of the COM10+ ceiling.
- The only USB serial device ever attached is a **FutureLogic** (gaming ticket-printer vendor) CDC device on `usbser.sys` — i.e. the user's USB-serial exposure is through Microsoft's generic CDC-ACM driver, which has **no latency knob** and routes `SET_LINE_CODING` over the control pipe. Worst case for per-byte parity switching.
- No FTDI (`Enum\FTDIBUS` missing), no CP210x, no CH340 on this box.
- **No native 16550 and no `\\.\QXCOM` device is present**, so neither `sastest.exe` nor a `libsas`-backed EGM can run against real hardware on this laptop today.

---

## 5. Ranked failure hypotheses

| # | Hypothesis | Verdict | Evidence | Confidence |
|---|---|---|---|---|
| 1 | **Cannot name COM10+**: uses bare `"COMn:"`, never `\\.\COMn` | **CONFIRMED — primary cause** | 0 occurrences of `\\.\` in the file; `COM%` count 0; 7-entry table at `0x004B81FC`; `mov ecx,7` at `0x004132CE`; `CreateFileA` at `0x004134BF` | **VERY HIGH (99%)** |
| 2 | **`Port = 11` is out of bounds** → garbage pointer → `"Could not create Comm Port"` (or AV in the `repne scasb` at `0x0041348D`) | **CONFIRMED** | Unchecked `[ebp + eax*4 - 0x50]` at `0x004134BA`; `ComDB` shows COM11 never existed here | **VERY HIGH (95%)** |
| 3 | **`"COM4:,COM5:"` typo** breaks `Port=4` and shifts `Port=5,6,7` | **CONFIRMED** | Byte `0x2C` at `0x000B7633` where a NUL belongs | **VERY HIGH (99%)** |
| 4 | **Wakeup-bit race**: MARK write → `Sleep(2)` → parity swap, no TX drain | **CONFIRMED design flaw; would break USB even after #1–#3 are fixed** | `0x00413953`→`0x00413975`→`0x004139C2`→`0x004139E9`; `FlushFileBuffers` absent from imports; vendor admits it in `SASTEST.HLP` | **HIGH (90%)** that it breaks on USB; **MEDIUM (60%)** that it is also marginal on some native UARTs |
| 5 | **16 ms FTDI/CP210x latency timer** violates the 5 ms inter-byte budget | **PLAUSIBLE, untestable here** (no FTDI on this box) | FTDI KB: default 16 ms, range 1–255; `libsas_uart.cpp:201-207` cites the 5 ms rule | **HIGH (85%)** in general; **N/A** for this machine |
| 6 | **Direct hardware port IO** (inpout32 etc.) | **REFUTED** | Import table: no such DLL; only Win32 comm APIs | **VERY HIGH (99%)** |
| 7 | RX per-byte wakeup tagging impossible on USB | **True but IRRELEVANT to a host** | `libsas_uart.cpp:634` — EGM sets wakeup only on the loop-break chirp; `sastest.exe` only reports aggregate via `ClearCommError` | **HIGH (85%)** |

---

## 6. Recommended serial layer for the new app

**Architecture — `ISasSerialPort` with two backends behind one interface:**

```
ISasSerialPort
 ├─ Open(PortDescriptor, SasLineConfig)   // never a bare int; never a hand-built \\.\ path
 ├─ WriteFrame(ReadOnlySpan<byte> frame, bool wakeupOnFirstByte)
 ├─ ReadAsync(Memory<byte>, CancellationToken)     // BaseStream.ReadAsync / overlapped
 ├─ event ErrorReceived   // aggregate parity/framing/overrun -> "GAME IS SENDING WAKE UP BITS"
 └─ Capabilities { SupportsStickParity, IsUsbBridge, LatencyTimerMs, SupportsTrueNineBit }

 ├── ManagedBackend : System.IO.Ports.SerialPort            [default, Windows]
 └── NativeBackend  : kernel32 P/Invoke (Win) / termios+CMSPAR (Linux)  [wakeup path, Linux]
```

**The transmit primitive — this is the fix:**

```csharp
// address byte alone, MARK parity, then DRAIN before touching the line control
port.Parity = Parity.Mark;
stream.Write(frame[..1]);
stream.Flush();                 // FlushFileBuffers: returns only when the byte is on the wire
port.Parity = Parity.Space;
stream.Write(frame[1..]);
stream.Flush();
```

`sastest.exe` replaces `stream.Flush()` with `Sleep(2)`. That one substitution is the difference between correct and hopeful. Belt-and-braces the drain with `SetCommMask(EV_TXEMPTY)` + `WaitCommEvent` on the native backend.

**Non-negotiables:**

1. **Never construct a device path by hand.** Pass `"COM12"` to `SerialPort`; it emits `\\?\COM12` itself. Verified above.
2. **Port picking is a UI list with friendly names**, from `Win32_PnPEntity` (+`SERIALCOMM` fallback), never an INI index. Persist by **stable `DeviceID`**, not by COM number — USB adapters move. Show `COM12 — FTDI USB Serial Port (COM12)`.
3. **Guard the Linux parity trap.** `Parity.Mark`/`Space` silently become "no parity" on Linux in `System.IO.Ports`. Use the native termios+`CMSPAR` backend there, or refuse to enable wakeup bits with an explicit error. **Never let it silently no-op.**
4. **Detect and report the transport.** On open, classify the port (native 16550 / FTDI / CP210x / CH340 / `usbser` CDC / QXCOM / Bluetooth), read the latency timer where one exists, and surface a capability banner. Offer the one-click FTDI/CP210x latency fix.
5. **Ship a "Line self-test"** — loopback or against a known EGM: send an address byte with wakeup, verify the peer's framing, measure real turnaround with QPC. It turns "it doesn't work" into a diagnosis in ten seconds, and is the single feature that most improves on the legacy tool.
6. **IO on a dedicated thread; `Channel<T>` to the UI; zero allocation on the hot path; `timeBeginPeriod(1)`; QPC timestamps.**

**Confidence on the wakeup-bit / USB-adapter conclusion**

- *`sastest.exe` cannot open COM10+, and `Port = 11` is out of bounds* — **VERY HIGH (99%)**. Read directly from the bytes and disassembly; corroborated by the vendor's own help file and this machine's `ComDB`.
- *Naive per-byte MARK/SPACE switching over USB is unreliable* — **HIGH (~90%)**. Grounded in the two-pipe argument, the FTDI control-URB glitch reports, FTDI's buffered-writes documentation, and the absence of any TX drain in `sastest.exe`. **Not measured on hardware here — no USB-serial adapter is currently attached to this machine** — so this is reasoning plus literature, not observation.
- *A properly written host (drain-then-switch, 1 ms latency timer, genuine FTDI) can drive SAS over USB* — **MEDIUM-HIGH (~75%)**. The host-only-transmits asymmetry makes this far more likely than the EGM case, but it **must be validated against real hardware before it is promised to anyone.** Plan for a native 16550 or QXCOM as the supported path and USB as best-effort.

---

## 7. Unknowns

- The exact SAS 6.02 **EGM response-time** figure (~20 ms) comes from secondary literature, not the spec. The 5 ms inter-byte figure is first-party (quoted verbatim in `libsas_uart.cpp`).
- `[Communications] Index` — the key is read by the binary; its consumer was not traced. **UNKNOWN.**
- Whether a genuine FTDI FT232H with `LatencyTimer = 1` and drain-then-switch actually holds the MARK/SPACE boundary at 19200 — **untested, no hardware present.** This is the one experiment that most needs running.
- CP210x `LatencyTimer` path is from a secondary source; CH340 and `usbser.sys` have no known equivalent.
- Whether the target EGM tolerates wakeup-less operation (the checkbox path) — machine-dependent, **UNKNOWN**.

---

## Appendix — artefacts produced

| File | Purpose |
|---|---|
| `scratchpad/research/strings_all.txt`, `strings_off.txt` | Sysinternals `strings64 -a -n 3` dumps (with `-o` offsets) |
| `scratchpad/research/pe_imports.py` | PE section + import-table dumper |
| `scratchpad/research/find_refs.py` | Cross-reference finder for the COM/DCB string VAs; `\\.\` search |
| `scratchpad/research/iat.py`, `f79c.py` | IAT→name mapping, thunk resolution, field-offset xrefs |
| `scratchpad/research/disasm.py` | capstone x86-32 disassembler (`VA = fileoff + 0x400A00`) |
| `scratchpad/research/sptest/` | .NET 10 probe: port naming, Mark/Space parity, Flush + SetCommState timing |
| `scratchpad/research/sasini_doc.txt`, `sastest_hlp.txt` | Extracted text of `SASINI.doc` and `SASTEST.HLP` |

Sources consulted on the web:
[Sparx Engineering — If you must use .NET SerialPort](https://sparxeng.com/blog/software/must-use-net-system-io-ports-serialport) ·
[FTDI — Setting a Custom Default Latency Timer Value](https://www.ftdichip.com/Support/Knowledgebase/settingacustomdefaultlaten.htm) ·
[FTDI — Buffered Writes](https://www.ftdichip.com/Support/Knowledgebase/bufferedwrites.htm) ·
[LKML — Fix corruption issue in USB ftdi driver](https://lkml.iu.edu/hypermail/linux/kernel/1111.0/01190.html) ·
[Microsoft — FlushFileBuffers](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-flushfilebuffers) ·
[Microsoft — HOWTO: Specify Serial Ports Larger than COM9](https://support.microsoft.com/en-us/topic/howto-specify-serial-ports-larger-than-com9-db9078a5-b7b6-bf00-240f-f749ebfd913e) ·
[dotnet/runtime — SerialStream.Windows.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.IO.Ports/src/System/IO/Ports/SerialStream.Windows.cs) ·
[dotnet/runtime — pal_termios.c](https://github.com/dotnet/runtime/blob/main/src/native/libs/System.IO.Ports.Native/pal_termios.c) ·
[dotnet/runtime#110349 — ParityReplace not working on Linux](https://github.com/dotnet/runtime/issues/110349) ·
[EEVblog — Chinese USB-to-serial adapters, parity support](https://www.eevblog.com/forum/projects/chinese-usb-to-serial-adapters-no-proper-parity-support-on-linux/) ·
[hairless-midiserial PortLatency_win32.cpp](https://github.com/projectgus/hairless-midiserial/blob/master/src/PortLatency_win32.cpp) ·
[Numato — Why can't I access COM ports above COM9](https://numato.com/blog/why-cant-i-access-com-ports-above-com9-on-windows/)
