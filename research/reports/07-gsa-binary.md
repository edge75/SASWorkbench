# 07 — GSA SAS Host Simulator v1.1: binary, data formats, DOS engine, serial rig

Scope: `C:\igs\GSA SAS Host Simulator v1.1`. Every claim below is grounded in a file I read or a
command I ran; anything I could not establish is written **UNKNOWN**.

Artefacts produced by this pass (all under
`C:\Users\ALESSI~1.QUI\AppData\Local\Temp\claude\C--IGT3\473c2bfe-6dd0-4a0e-979a-2d6bd2da83f6\scratchpad\research`):

| Path | Contents |
|---|---|
| `gsa-resources.txt` | full PE dump: header, sections, all 72 resources, menus, dialogs, strings, version, imports |
| `gsa-ui-tree.txt` | the complete UI tree (menus, accelerators, 25 dialogs with every control + caption) |
| `gsa-strings.txt`, `gsa-strings-uni.txt` | ANSI / UTF-16 strings of `SASHostSim.exe` |
| `gsat-strings.txt` | strings of the DOS engine `gsat.exe` |
| `gsa_decode/` | 8 Python decoders + `run_all.py` (see §3) |
| `decoded/` | output of `run_all.py`: 110 decoded files, **0 failures** |
| `cn_setup_log.txt`, `cn_deploy.txt`, `cn_start.bat`, `cn_stop.bat` | copies of the user's Chinese setup notes / launchers |
| `iso_text.txt`, `iso_btm.txt`, `onekey.txt`, `ts101.txt` | text extracted from the bootable ISO, `One Key Polls.doc`, the 10.1 test-script HTML |
| `gsa_dis.py`, `gsa_iat.py`, `gsa_xref.py`, `peparse2.py`, `comscan.py` | the disassembly / xref helpers used below |

---

## 0. The headline: this is a two-process, two-serial-port product

`SASHostSim.exe` is only the GUI. The SAS protocol is spoken by a **separate DOS program**,
`SAS Engine\gsat.exe`, over a **second serial link** using a private protocol the vendor calls
**GEP**. Evidence:

* `gsat.exe` strings: `@#ProjId=GEP Interface Library#@`, `@#ProjId=GSA SAS Tools Engine#@`,
  `Attempting GEP Comm Open, port %d:`, `Attempting SAS Comm Open, port %d :`,
  `GEPI HostSim Define Poll Cycle: the data:`, `GEP HOST RESET: Active Polls removed`.
* `SASHostSim.exe` strings: `GEPSendPoll`, `GEPSetPollBody`, `GEPSetPollCRC`, `GEPSetPollDelays`,
  `GEPSetPollWakeup`, `GEPRedeemTicket`, `GEPPerformDefinePollList`, `CGEPSetExceptionResponse`,
  `GEPBCDSet %02X:%04X:%02X`, `GEPSimpleSet`, `GEPGetCommand`, `GEPSimplePerform`, plus
  `Command queue, couldn't send command %02X` and `Unable to get sequence number for command!`.
* `ReleaseNotes.txt` (JIRA SASDS-27): *"This should include information related to the interface
  used between the GUI PC and the Engine PC."*; SASDS-15: *"Show connection information with engine
  and EGM"*. String 61204 in the exe is `NO CONNECTION`.

Shipped topology: **GUI PC —(GEP over RS-232)— Engine PC (DOS) —(SAS over RS-232)— EGM**.
The bootable CD (`SAS Engine\Bootable_ISO\gsat1_1.iso`, FreeDOS + isolinux + memdisk) exists so the
Engine PC needs no installed OS. This one fact explains most of what follows, including why the user
needed DOSBox-X.

---

## 1. The binary — `SASHostSim.exe`

### 1.1 Toolkit: MFC, not Delphi

```
PE32 GUI i386, 4 sections, 806912 bytes
TimeDateStamp 0x45005E95  -> 2006-09-07T18:01:57 UTC
LinkerVersion 6.0, ImageBase 0x400000, EntryPoint 0x8F326
.text  VA 0x001000 VSize 0x099ACB      .rdata VA 0x09B000 VSize 0x01441E
.data  VA 0x0B0000 VSize 0x006D58      .rsrc  VA 0x0B7000 VSize 0x0105E8
Imports: MFC42.DLL (415 ordinals), MSVCRT (57), KERNEL32 (43), USER32 (39),
         GDI32 (17), OLEAUT32 (10), MSVCP60 (8), ole32 (6), SETUPAPI (5), VERSION (3)
Exports: none (Export directory empty)
```

There are **no `TPF0` RT_RCDATA form streams**; the UI is classic `RT_MENU` + `RT_DIALOG`, so I
parsed those directly. Resource inventory: 3 `RT_ICON`, **4 `RT_MENU`**, **25 `RT_DIALOG`**,
33 `RT_STRING` blocks, 1 `RT_ACCELERATOR`, 2 `RT_GROUP_ICON`, 1 `RT_VERSION`, 3 of custom type 240.

`RT_VERSION`:

```
CompanyName      Gaming Standards Association
FileDescription  SAS Host Simulator Application
FileVersion      1.1          ProductVersion 1.1
InternalName     SASHostSim   OriginalFilename SASHostSim.EXE
ProductName      SAS 6.01 Host Simulator
LegalCopyright   Copyright (c) 2006
Binary FILEVERSION field is 0.5.9.0 (stale; the string resource says 1.1)
```

### 1.2 Complete UI tree

Full listing in `gsa-ui-tree.txt`. Summary:

**Menu 104 (main)**
`Configure` → Settings… (32803) | Define Poll… (32771) | Define Raw Poll… (32779) |
View/Modify Poll Definitions… (32772) | Define Polling Loop… (32777) | View/Modify Polling Loop…
(32798) | Set Exception Responses… (32799) | Ticket Database… (32821).
`Windows` → Meters (32796) | Game/Denom Meters (32812) | Exceptions (32797). `About HostSim…` (57664).

**Menu 118 (SAS Trace window)**
`Trace/Log` → Define Trace Filter… | View/Modify Trace Filter… | Pause Trace (Pause key) | Save
Trace to Text File… | Print Trace Information… | Log File… (greyed) | Stop Logging | View Log… |
`Timestamp Format` ▸ Absolute time / Relative to start of last message / Relative to start of last
poll / Relative to end of last message / Relative to end of last poll / Wall-clock time |
`Data Parsing` ▸ Raw data only / Text only / Both Raw and Text.

**Menu 110** → Timing details…  **Menu 117** → Save selected traces… / Print selected traces…

**Accelerators (table 104):** F1..F8 → commands 32780..32787 (send one-key poll 1..8);
Ctrl+F1..Ctrl+F8 → 32804..32811 (send the same poll with a **bad CRC**) — matching the
"Regular / Bad CRC" column headings on the main dialog.

**Dialogs (25):**

| ID | Caption | Notes |
|---|---|---|
| 100 | About SAS Host Simulator | "SASHostSim Version 1.1", "Copyright (C) 2003" |
| **101** | *(main client, DLGTEMPLATEEX, 70 controls)* | "One-Key Polls" group: 8 rows of `Send` + poll combo + `to EGM address`, labelled F1..F8 / Ctrl-F1..Ctrl-F8; "Ctrl-Click to view/modify poll"; `Poll Set:` combo (1212); `Polling loop:` (1066) + `Start Polling Loop` (1067); `Test Group:` (1261), `Test Number:` (1199), `Save` (1214), `Add/Change…` (1215); **`Communication Port:` combo (1256, CBS_DROPDOWN — editable)**; `Enable SAS Pogressive Broadcast` (1264, sic) |
| 102 | ACK/NACK *(settings page)* | resend delay ms, number of NACKs, "Treat any response as ACK", "Ignore final NACK response", "Move to next poll after receiving real-time exception" |
| 103 | SAS Comms *(settings page)* | Poll rate (ms), Time from response to next poll (ms), "Poll time starts after reply received", "Verify data fields", "Send RESYNC on startup", "Send RESYNC on comm error", "Use Real-Time Events", "Display exception 1F", **"UART wakeup bit is delayed"** |
| 106 | Define Poll | name, Poll Template combo, Multi-Denom Poll + denom, No CRC / Valid CRC / Invalid CRC, `Interbyte Delays…`, `Wakeup Bits…` |
| 107 | Define Raw Poll | name, Poll Number, Poll Data (hex), CRC radio, Interbyte Delays…, Wakeup Bits… |
| 109 / 112 | Define / View-Modify Polling Loop | poll combo + `Send to EGM address` + `Add to Loop` + ordered list + Move Up/Down/Remove |
| 111 | View/Modify Definition of Poll | Save Changes / Copy Poll / Clear Changes / Delete |
| 114 | Exception Responses | exception code, "Ignore exception" vs "Respond with" + poll, Add/Remove Response |
| 115 | Tickets *(settings page)* | Validation Type combo, ticket printing/redemption switches, five delay(ms) edits, Validation System ID, Validation Number |
| 116 | Tickets In System | ticket list, System ID, number, amount, type, Redeemed, Expiration, Restricted pool ID |
| **117** | Progressives *(settings page)* | "Send multiple progressive updates in a single poll (86)", Progressive group, Delay between progressive updates (ms), **Levels 1..32** (checkbox + value in cents) |
| 118 | SAS Trace | the trace window |
| 119 | Define Test | Setup List combo, Name of New Setup, Add / Rename / Delete |
| 200 | Meter Values | meter-set combo, View/Modify, Delete, Game # (0 for EGM), Denom, 20 name/value rows |
| 201 | Select Meters to Display | meter-set name + 20 meter slots |
| 202 | Inter-byte Delays | per-byte delay editor |
| 203 | Message Filter | trace filter editor |
| 204 | Set Wakeup Bits | per-byte wakeup-bit editor |
| 205 | View/Modify Message Filters | |
| 206 | Detailed Timing | |
| 207 | Log File View | reads back `SASHostLogNNNNN.log` |
| 208 | Meter value for multiple games/denominations | |
| 209 | Exceptions | exception list window |

**Custom resource type 240** id 101 seeds the `Poll Set:` combo — `Set1 Set2 Set3 User`; ids 109/112
seed the poll combos of the polling-loop dialogs with 15 defaults (`1A - Send Current Credits` …
`2E - Delay Game 3 seconds`).

**RT_STRING** holds the whole meter-name catalogue (ids 62001–62178 short, 63001+ long; e.g.
`62001 'Coin In'`, `63009 'Total credits from coin acceptor'`) plus status-bar strings
`61204 'NO CONNECTION'`, `61205 'Host Simulator Configuration'`.

### 1.3 How the COM port is opened — and the COM10+ question

Comm code at `.text:0x00462947` (capstone dump via `gsa_dis.py`):

```
0x00462957  push 0 ; push 0x40000080 ; push 3 ; push 0 ; push 0 ; push 0xC0000000
0x0046296C  call 0x45CE40            ; CString -> LPCTSTR of the argument
0x00462972  call [0x49B05C]          ; CreateFileA
0x00462996  call [0x49B0A4]          ; GetCommState
0x0046299C  mov  dword [ebp-0x30], 0x1C200     ; DCB.BaudRate = 115200
0x004629A3  mov  byte  [ebp-0x21], 0           ; DCB.Parity   = NOPARITY
0x004629A7  mov  byte  [ebp-0x22], 8           ; DCB.ByteSize = 8
0x004629AB  mov  byte  [ebp-0x20], 0           ; DCB.StopBits = ONESTOPBIT
            ... fDtrControl=ENABLE, fRtsControl=ENABLE, fOutxCtsFlow=0,
                fOutxDsrFlow=0, fDsrSensitivity=0, fOutX=0, fInX=0
0x004629FD  call [0x49B0A0]          ; SetCommState
```

So the **GUI↔Engine (GEP) link is 115200 8N1, no flow control, DTR+RTS asserted**, opened
`GENERIC_READ|GENERIC_WRITE`, `OPEN_EXISTING`, `FILE_FLAG_OVERLAPPED`, then driven with
`SetCommMask` / `WaitCommEvent` / `GetOverlappedResult` / `CreateWaitableTimer`.

> The user's own note `DOSBox\部署說明.txt` says the GUI↔Engine link is **19200** 8N1. That is
> wrong: the binary and `ComPortSettings.dat` both say **115200**. It did not bite them because
> DOSBox-X `directserial` reprograms the host UART from the emulated one.

Port enumeration is modern and far better than `sastest.exe`'s hard-coded table:

```
0x0042B087 SetupDiGetClassDevsA
0x0042B141 SetupDiEnumDeviceInterfaces
0x0042B17D SetupDiGetDeviceInterfaceDetailA      -> DevicePath (\\?\ACPI#PNP0501#...)
0x0042B1C6 SetupDiGetDeviceRegistryPropertyA #0x0C (SPDRP_FRIENDLYNAME)
0x0042B1F5 SetupDiGetDeviceRegistryPropertyA #0x00 (SPDRP_DEVICEDESC)
0x0042B245 SetupDiGetDeviceRegistryPropertyA #0x0D (SPDRP_LOCATION_INFORMATION) + strncmp "USB"
0x0042AD63 CreateFileA(devicePath, ...)   -> probe; on failure the entry is REMOVED from the list
0x0042AE1E / 0x0042AE37  Find(" ") / Find(")")  -> extracts "COMn" out of the friendly name
```

Consequences that matter to SasWorkbench:

1. Ports come from SetupAPI, **not** a fixed table. The four `COM1..COM4` literals next to
   `CCommConfigPage` in `.rdata` are only fallback combo items.
2. The probe opens the **device interface path**, so COM10+ enumerate correctly — but a port already
   held by another process **silently disappears from the list** (a genuinely hostile behaviour).
3. The name kept for the session is the `COMn` text parsed out of the friendly name
   (`ComPortSettings.dat` holds `COM3`).
4. **There is no `\\.\` prefix anywhere in the image** (raw byte scan for that literal: zero hits)
   and `CreateFileA` receives the `CString` unmodified, so the *working* open of `COM10`+ would
   still fail. Same end-user symptom as `sastest.exe`, different root cause.

### 1.4 Crash-report writer

`SASHostSim.RPT` is **not a test report** — it is a crash dump written by an
`SetUnhandledExceptionFilter` handler. The exe carries the exact format strings
(`//=====…`, `Exception code: %08X %s`, `Fault address:  %08X %02X:%08X %s`, `Registers:`,
`EAX:%08X`…, `Call stack:`, `Address   Frame     Logical addr  Module`, `%08X  %08X  %04X:%08X %s`)
and the full exception-name table (`ACCESS_VIOLATION`, `STACK_OVERFLOW`, …). The shipped file
records a real crash on 2026-07-18 18:30:

```
Exception code: C0000005 ACCESS_VIOLATION
Fault address:  00469605 01:00068605 G:\GSA SAS Host Simulator v1.1\SASHostSim.exe
Call stack: 00469605 <- 0043D878 <- 0043CE8C <- 004107DA <- 004105B1 <- MFC42 <- msvcrt <- KERNEL32 <- ntdll
```

(`G:\` shows the user ran it from another drive before it was moved to `C:\igs`.)

---

## 2. Command line / automation — there is none

Definitive for `SASHostSim.exe`:

* No `/`- or `-`-prefixed option string exists in the ANSI or UTF-16 string pools
  (regex `^[-/][A-Za-z?]` over both: zero hits). No usage text, no `Usage:`, no `argv` text.
* **No exports at all**, no `DllRegisterServer` — it cannot be an OLE automation server.
* `ole32` is imported (`CoInitialize`, `CoCreateInstance`, `CLSIDFromProgID`, `CLSIDFromString`,
  `OleRun`) but only from the MFC `COleDispatchDriver::CreateDispatch` helper at
  `0x470B8B`/`0x471310`, and **no ProgID or CLSID string exists in the image** — dead linkage.
* No DDE: no `ddeexec`, no `\shell\open`, no `EnableShellOpen`.
* The MFC document-template string (`RT_STRING` 104) is
  `SASHostSim\n\nSASHos\n\n\nSASHostSim.Document\nSASHos Document` — the extension and filter fields
  are **empty**, so even the default MFC "open the file named on the command line" path has no
  registered type to act on.

`gsat.exe`, by contrast, **does** take a command line (§4.2).

**Implication.** The user could not have automated conformance testing with this tool either.
Selecting a Test Group/Test Number, pressing F1..F8, starting the polling loop, saving `stepN.trc`
and judging pass/fail are all manual GUI actions. The `10.1/` and `10.2/` folders are exactly that
manual labour: Chinese-annotated HTML test scripts with numbered steps and a `P/F` column, plus one
`.trc` per step as evidence.

---

## 3. The data files — decoded

Decoders live in `gsa_decode/`; `run_all.py` regenerates `decoded/`. **110 files decoded, 0
failures, every container consumed to exactly EOF** — that byte-exact termination is the validation.

### 3.0 The three universal primitives

Every binary `.dat` is little-endian and uses only:

```
WORD    uint16 LE
DWORD   uint32 LE
CSTRING BYTE length, then <length> bytes of latin-1 text   (nothing longer than 255 in shipped data)
```

Files start with `WORD fileVersion` and — except the two single-record ones — `DWORD recordCount`.
There is no `CArchive` object framing, no magic number, no checksum: a corrupted file is
undetectable. That matters (§3.4).

### 3.1 `ComPortSettings.dat` (14 bytes)

```
WORD    version   = 1
CSTRING portName            "COM3" (live)   "COM1" (Defaults)
DWORD   baudRate  = 0x0001C200 = 115200
BYTE    byteSize  = 8
BYTE    parity    = 0 (none)
BYTE    stopBits  = 0 (one)
```
Exactly the DCB programmed at `0x0046299C`.

### 3.2 `TestConfigFile.dat` (19 bytes)

```
WORD    version = 0x0014 (20)
CSTRING fileName        live: "Legacy_Bonus.dat"   Defaults: "Communications.dat "
```
i.e. which `TestData\*.dat` group is currently loaded.

### 3.3 `Tracefilters.dat` (207 B)

```
WORD  version = 1 ; DWORD filterCount
filter := WORD 0x0002 ; CSTRING name ; BYTE kind(=1) ; DWORD codeCount ; BYTE pollCode[codeCount]
```

| name | codes |
|---|---|
| ROM signature | 21 |
| Ticket Tests | 3D 4C 4D 57 58 70 71 7B 7C 7D |
| Tickets/AFT | 3D 4C 4D 57 58 70 71 72 73 74 75 76 7B 7C 7D A0 |
| 11 filter | 11 |
| Multi-Game A4 | 09 A4 |
| Progressive | 80 83 84 85 86 87 |
| 2F Filter | 2F |
| *(empty name — user-created)* | 3F |
| General Poll *(user-created)* | 81 |

### 3.4 `TicketList.dat` (710 B) — the ticket database

```
WORD  version = 1 ; DWORD ticketCount
ticket := WORD 0x0002
          CSTRING ticketNumber      (validation number, normally 16 ASCII digits)
          CSTRING systemId          ("00")
          BYTE    ticketType        (1 in every record; the combo enum in the exe is
                                     0 Cashable / 1 Restricted Promotional /
                                     2 Non-restricted Promotional /
                                     3 Non-redeemable Cashout / 4 Handpay-Jackpot Receipt)
          BYTE    redeemed          (0/1)
          double  amount            (IEEE-754 LE)
          BYTE    flag              (1 on the shipped sample, 0 on user rows — UNKNOWN)
          CSTRING expiration        (8 chars: MMDDYYYY, or a day count)
          WORD    restrictedPoolId
```

16 tickets live vs 1 in Defaults. **Ten of the user's rows contain binary garbage in
`ticketNumber`** (`'000000004D DATA\x00'`, `'000000004(\x00!\x00%\x00)'`,
`'000000004\x01\x00\x00Game'` …) while their amount/expiry/pool fields are consistent
(50 / `00005100` / pool 4). That is heap garbage written through a fixed-width edit field — almost
certainly the same defect that produced `SASHostSim.RPT`. **A format with no length or type
validation, plus a GUI that writes uninitialised memory, is precisely the failure mode SasWorkbench
must not reproduce.**

### 3.5 Poll library: `PollDefsSet1/2/3.dat`, `UserPollDefinition.dat`

```
file   := WORD fileVersion(1) ; DWORD groupCount ; group[groupCount]     (ascending pollCode)
group  := BYTE pollCode ; BYTE instanceCount ; BYTE[3] pad(00 00 00) ; record[instanceCount]
record := WORD 0x0005 ; WORD 0x0001              (constant record tag / sub-version)
          CSTRING name                           ("%02X - <text>"; may be zero-length)
          BYTE instanceIndex                     (1-based inside the group)
          BYTE pollCode                          (== the group's pollCode in ALL four files)
          BYTE dataLen ; BYTE data[dataLen]      (SAS message WITHOUT address and WITHOUT CRC;
                                                  data[0] is the command byte)
          BYTE crcMode                           (0 = No CRC, 1 = Valid CRC,
                                                  2 = Invalid CRC (unused), 0xFF = unset)
          BYTE[26..35] tail                      (always starts 00 00 00 <DWORD> <DWORD>, contains
                                                  the constant marker 02 00 01 00; holds the
                                                  multi-denom flag/denom, the interbyte-delay list
                                                  and the wakeup-bit list — NOT fully decoded)
```

Counts (live): `PollDefsSet1` 144 groups / **613** polls; `Set2` 111 / 412; `Set3` 111 / 414;
`UserPollDefinition` 123 / 284. Defaults: 126/521, 111/412, 111/414, 123/284.

Decoded `data` is directly usable:

```
[08.1] "08 - Configure bill denom=all actions = yes"    data=08 ff ff ff 00 01     valid CRC
[08.3] "08 - Configure bill denom=$10$20 actions = yes" data=08 18 00 00 00 01     valid CRC
[09.4] "09 - (B0 .05) Send Enable Game 0"               data=09 00 00 01           valid CRC
[2F.19] "2F - Meter Codes 62-6B" data=2f 0c 00 00 62 63 64 65 66 67 68 69 6a 6b    valid CRC
[71.1] "71 - Send Redeem Ticket Command" data=71 16 00 00 00 00 05 00 …            valid CRC
```

Codes present in Set1: `00-0B 0E-2F 31-4A 4C 4D 4F-58 62-67 69 6A 6B 6E-76 7B-80 83-87 8A-8C 8E 8F
90 94-9A A0 A4 A8 AA AF B1-B7 BF CF D3 D4 F1 FE`. `BF` and `CF` are declared with
**instanceCount 0** (empty groups); the parser handles that.

### 3.6 Polling loops: `LoopDefs1/2/3.dat`, `PollLoopDefinitions.dat`, `UserLoopDefs.dat`

```
file  := WORD version(1) ; DWORD loopCount ; loop[loopCount]
loop  := WORD 0x0001 ; CSTRING name ; WORD loopId ; DWORD entryCount ; entry[entryCount]
entry := WORD 0x0001 ; BYTE egmAddress ; BYTE instanceIndex ; BYTE pollCode
```

5 bytes per entry; `(pollCode, instanceIndex)` is exactly the key from §3.5. One loop file per poll
set (`PollDefsSet1↔LoopDefs1`, `2↔2`, `3↔3`, `UserPollDefinition↔UserLoopDefs`);
`PollLoopDefinitions.dat` is the master copy the GUI runs (53 loops).

Example — `Basic_1`, 17 entries: `addr0/poll00`, then addr1 with
`1F 51 00 54 00 53(#3) A0 B3 B5 AF 00 2F(#3) 6F(#39) 0F 19 1C`.

**A loop can express an ordered list of (address, poll) and nothing else.** No repeat count, no
delay, no branch, no wait, no condition, no assertion.

### 3.7 Meter sets: `MeterSetDefinitions.dat`

```
file := WORD version(1) ; DWORD setCount
set  := WORD 0x0001 ; CSTRING name ; DWORD gameNumber ; DWORD denomIndex(-1 = any)
        DWORD meterCount ; WORD meterCode[meterCount]
```
41 sets live vs 31 in Defaults.

### 3.8 Test configuration templates: `HostConfigs.dat` and `TestData\*.dat`

Same container in both; all 49 files (live + Defaults) parse to exactly EOF.

```
file   := WORD fileVersion(0x000B) ; DWORD configCount ; config[configCount]
config := WORD 0x000B ; CSTRING name ; settings

settings :=
  WORD pollRateMs                     WORD responseToNextPollMs
  BYTE[9] flagsA                      (SAS Comms + ACK/NACK check boxes)
  WORD nackCount                      WORD resendDelayMs
  BYTE[9] flagsB                      (Tickets page check boxes)
  DWORD delay1 ; DWORD delay2
  CSTRING validationSystemId          CSTRING validationNumber
  BYTE[12] zero
  DWORD delay3 ; WORD reserved ; DWORD delay4 ; BYTE[3] zero
  32 x { CSTRING levelAmount ; CSTRING incrementRate ; BYTE enabled }   <- Progressives page
  CSTRING comPort
  8  x { DWORD pollRef ; DWORD egmAddress ; DWORD reserved }            <- one-key F1..F8
         pollRef = (pollCode << 8) | instanceIndex ; 0 = empty slot
  DWORD sel1 ; DWORD sel2 ; DWORD sel3 ; DWORD sel4   (combo selections; -3 / -1 = none)
  BYTE[22] zero ; CSTRING placeholder ("dummy") ; BYTE[5] zero
```

The `pollRef` packing is the most useful find here — it makes a whole test setup machine-readable,
e.g. `TestData\Enhance_Val.dat` "Current":

```
pollRate=40ms respToNextPoll=0ms nack=3 resendDelay=40ms port=COM1
one-key: F1=7B.18@1 F2=4C.1@1 F3=71.1@1 F4=4D.1@1 F5=0E.1@1 F6=0E.2@1 F7=7B.25@1 F8=-
```

**Not present anywhere in the file set: the exception→response table** (dialog 114). No dedicated
file exists and the config blob has no variable-length list, so the auto-responses described all
over `ReleaseNotes.txt` appear to be **session-only** state pushed to the engine via
`CGEPSetExceptionResponse`. That is an inference from absence — marked **UNKNOWN**.

### 3.9 `SASHostLog00000.log` (30 KB) — the binary event log

```
record := WORD version(1) ; DWORD unixTime(time_t) ; WORD payloadLen ; BYTE payload[payloadLen]

payload type 0x00  text : ASCII, no NUL   e.g. "*** Log started at 07/18/26 18:27:07 ***"
payload type 0x44  data :
        BYTE  sequence        (wraps at 256)
        BYTE  side            (0 = host, 1 = EGM)
        DWORD startTicks      (ms, GetTickCount-style)
        WORD  byteCount
        BYTE  messageClass    (1 = poll/command, 2 = response)
        byteCount x { BYTE value ; WORD ticksLow16 }     <- per-byte timestamp
```

Rolling files (`%s%05d.log`); the viewer (dialog 207) rejects other versions
(`Unexpected version of logfile, operation cancelled`).

The per-byte `ticksLow16` is the valuable part: it preserves **inter-byte gaps in milliseconds**,
e.g. `#9 HOST->EGM 01 1F  ms: +0 +6`, and an 18-byte LP 86 broadcast spread `+0 +6 +12 … +98`.
The **wakeup (9th) bit is NOT stored** — only the 8 data bits.

Decoded shipped log: 1374 records across three sessions, 723 host messages and 645 EGM messages,
three `*** Log started/stopped ***` pairs (the 2026-07-19 session with the EGM answering `00`
"No Activity" to general polls).

### 3.10 `*.trc` — the trace export **(import target for SasWorkbench)**

Plain ANSI text, CRLF. Produced by *Trace/Log → Save Trace to Text File…* (file filter
`Trace Files (*.trc)|*.trc|All Files (*.*)|*.*||`). **Write-only — the application never reads a
`.trc` back.**

```
Trace started at %x %X               <- locale short date, locale time
<blank>
Timestamp is <mode>
<blank>
( message | "----------" | "***PAUSE***" )*

message  = dataline CRLF textline CRLF
dataline = side  ("HOST   " | "EGM    ", 7 chars)
           stamp ("%8lu.%d" in absolute mode, "%5lu.%d" otherwise, or "-------")
           raw   ("[%02X] " for a byte whose WAKEUP bit was set, "%02X " otherwise)
textline = indent + the simulator's own decoding
           indent = 20 spaces in "absolute time" mode, 17 spaces in the five relative modes
```

The six mode strings, verbatim from `.rdata`: `absolute time`,
`relative to start of previous poll`, `relative to start of previous message`,
`relative to end of previous poll`, `relative to end of previous message`, `wall-clock time`.

Worked example (`step2.trc`):

```
Trace started at 07/19/26 12:47:05

Timestamp is relative to end of previous poll

HOST   -------   [01] A0 00 00 6C 13
                 Send enabled features: Game number = 0000;
EGM       34.5   01 A0 00 00 D2 CD 03 00 00 00 92 40
                 Send enabled features: Game number = 0000; Feature 1: (Bit 0) Jackpot multiplier
                 enabled = NO; … (Bits 5 to 6) Validation style - (02) Secure Enhanced; …
```

Importer notes:

* The brackets mark the **wakeup bit**, not the direction. `10.1/10.1.3/step8.trc` contains
  `EGM    -------   [01]` — an EGM byte with the wakeup bit set and *no* decoded text line. An
  importer that assumes "brackets ⇒ host" will mis-parse real captures.
* A message with no decoded text simply has no second line.
* `----------` separates capture segments and terminates the file; `***PAUSE***` records
  *Trace/Log → Pause Trace*.
* My parser handles all **32** `.trc` files in the package: **2950 messages, 0 header failures**.

### 3.11 `PollDef.txt` — an append-only export, not an input

The brief guessed this was the user-extensible poll-definition mechanism. It is not. At
`.text:0x004381CD`:

```
push 0x4B13F0   ; "a+"
push 0x4B13F4   ; "PollDef.txt"
call [0x49B858] ; fopen
…
push 0x4B1400   ; "%s = %s\n"
call [0x49B854] ; fprintf
call [0x49B850] ; fclose
```

`fopen("PollDef.txt","a+")` — **append**. The file is a human-readable dump of the selected poll(s)
produced by the *"view the translated text in a window in table format"* feature added in v1.1
(ReleaseNotes, GLI 3/28/06 item 9). It is never read back, has no grammar, and accumulates forever
(the shipped one is 139 KB / 7082 lines / 661 blocks):

```
7B - Validate handpay & handpay receipt (bit2=1)
(Bit 0) Use printer as cashout device = 1
(Bit 1) Use printer as handpay receipt device = 1
Cashable ticket and receipt expiration = 0
Restricted ticket default expiration = 0
CRC = Yes
Multi-Denom Preamble = No
<blank><blank>
```

Parameter names come from the XML templates (§3.12). The small `10.2/10.2.1/PollDef.txt` and
`10.2/10.2.3/PollDef.txt` are the user copying this export next to the step evidence.

### 3.12 The real extensibility mechanism: `SASTemplates.dtd` + `*Templates.xml`

This is the part of the toolkit worth stealing. `SASTemplates.dtd` (3.2 KB) defines a small
declarative DSL for SAS message layout; five documents instantiate it, and the exe loads all five
(`\PollTemplates.xml`, `\UserPollTemplates.xml`, `\ResponseTemplates.xml`,
`\UserResponseTemplates.xml`, `\EventTemplates.xml`):

| file | size | role |
|---|---|---|
| `PollTemplates.xml` | 46 KB | host→EGM long-poll layouts |
| `ResponseTemplates.xml` | 113 KB | EGM→host response layouts |
| `EventTemplates.xml` | 13 KB | real-time-event payload layouts |
| `UserPollTemplates.xml` / `UserResponseTemplates.xml` | 210 B each | **empty `<SAS_templates/>` — the user extension point, never used** |

Element vocabulary (from the DTD):

```
SAS_template  @name @cmd @crc(yes|no) @length(yes|no) @multigame(yes|no)
  bcd_parm @name @size              hex_parm @name @size
  binary_parm @name @size @length @radix(binary|decimal|hex)
  bool_parm @name                   ascii_parm @name @size
  variable_ascii_parm | variable_bcd_parm | variable_hex_parm   @name @size @length
  variable_meter_parm @name         variable_meter_response @name
  choice_parm @name @size > choice_entry @name @value
  bitmap_parm @name @size > bitmap_field @size @offset > (bool_parm | choice_parm)
  option_choice_parm @name @size > option_choice_entry @name @value > <one sub-parm>
  aft_receipt_parm @name            poll_2F_meter_parm @name
  padding @size                     optional @name > (any of the above)+
```

Examples:

```xml
<SAS_template name="ROM signature verification" cmd="21" crc="yes">
  <binary_parm name="Signature Seed" size="2"/>
</SAS_template>
<SAS_template name="Send extended meters for game N" cmd="6F" crc="yes" multigame="yes" length="yes">
  <bcd_parm name="Game number" size="2"/>
  <variable_meter_parm name="Meters"/>
</SAS_template>
<SAS_template name="Set machine numbers" cmd="B7" crc="yes" length="yes">
  <binary_parm name="Asset number" size="4"/>
  <variable_ascii_parm name="Floor location" size="40" length="yes"/>
</SAS_template>
```

The same document drives *encoding* (Define Poll dialog), *decoding* (the trace text lines) and
*printing* (`PollDef.txt`). The exe's parser reports errors such as
`Optional parm: invalid sub-parameter type %s`, `%s: invalid size %d (must be between 1 and 4)`,
`Padding parameter requires a size`, `%s: Variable-length ASCII parameter requires a maximum size`.

**What this DSL can and cannot express, plainly.**

*Can:* the byte layout of one SAS message — BCD/hex/binary/ASCII fields of fixed or variable size,
booleans, enumerated choices with named values, bitmaps with per-bit names, length-prefixed and
optional sections, the multi-game preamble and CRC presence, the meter-list and AFT-receipt shapes.

*Cannot:* anything procedural. There is **no loop, no wait, no delay, no variable, no conditional,
no expectation, no assertion, no verdict, no cross-message reference and no meter arithmetic** — the
DTD has no element for any of it. A "test" in this toolkit is therefore a saved
`TestData\<group>.dat` template (poll rate + check boxes + eight one-key slots + progressive levels)
plus a named polling loop plus a trace filter — and then a human pressing keys and filling in a
`P/F` column in an HTML script.

### 3.13 `configFiles.txt` / `configDesc.txt`

Two parallel CRLF text lists (file name ↔ display name) enumerating the test groups. Added in v1.1
beta 4 precisely *"so new test categories can be added without new code and recompiling"*
(ReleaseNotes). Live has 24 entries, Defaults 23 — the v1.2 update appended `Adendda.dat` /
`Addenda` (sic). Note the list says `User_Defined.dat` while the file on disk is `User_defined.dat`.

### 3.14 What the user actually customised

By mtime (install 2026-07-16 19:25; `Defaults\` is the v1.1 baseline while the root also carries the
vendor's v1.2 content, so a plain `Defaults` diff over-reports):

| file | when | change |
|---|---|---|
| `ComPortSettings.dat` | 07-18 17:23 | `COM1` → **`COM3`** |
| `SASHostSim.RPT` | 07-18 18:30 | crash |
| `LoopDefs1.dat` | 07-19 11:12 | 31 → 34 loops; new **`EFT Meters`**, **`Key Meters`**, **`my_loop`**; `All 2F Meters` 21→38 entries, `Ticket Meters` 12→15 |
| `UserLoopDefs.dat` | 07-19 11:14 | 23 loops |
| `MeterSetDefinitions.dat` | 07-19 11:23 | 31 → 41 sets; added **`All Meters 1..9`** and **`Key Meters`**; edited `EFT`, `AFT bonus`, `Required Group 1` |
| `Tracefilters.dat` | 07-19 13:02 | 7 → 9 filters; added an unnamed one (`3F`) and **`General Poll`** (`81`) |
| `SASHostLog00000.log` | 07-19 13:04 | 3 logging sessions |
| `step2/3/5.trc` | 07-19 12:48–13:22 | root-level captures |
| `TestData\Enhance_Val.dat` | 07-19 14:20 | edited |
| `TestConfigFile.dat` | 07-25 13:37 | selected group → `Legacy_Bonus.dat` |
| `PollDefsSet1.dat` + `PollDef.txt` | 07-25 17:00 | 521 → **613** polls (≈110 added: the whole `22/23/24/25/29` EFT family, `2F` meter-code blocks 18–25, `1D`, `27`, `28`, `14.2`) |
| `TicketList.dat` | 07-26 09:42 | 1 → 16 tickets (10 with corrupt numbers) |
| `10.1/`, `10.2/` | 08-07 | 32 `.trc` step captures + 2 Chinese-annotated HTML test scripts + 2 `PollDef.txt` extracts |

`HostConfigs.dat`, `PollDefsSet2/3.dat`, `PollLoopDefinitions.dat` and `UserPollDefinition.dat`
were **never touched** — the user worked entirely in poll **Set1**.

The profile says the work was **meters, EFT/AFT, ticket validation (10.1 Standard, 10.2 Secure
Enhanced) and progressives**, done by hand-building polls, loops and meter sets in dialogs, one at
a time.

---

## 4. The DOS engine `gsat.exe` and the DOSBox rig

### 4.1 What `gsat.exe` is

```
file: MS-DOS executable, COFF for MS-DOS, DJGPP go32 v2.02 DOS extender (stub),
      autoload "CWSDPMI.EXE"     (stub built 2001-12-24; DJGPP libc built 2001-12-24, gcc 2.8.1)
407985 bytes; ships with cwsdpmi.exe (20125 B) + CWSDPMI.DOC
```

A 32-bit protected-mode DOS program that drives the UARTs **directly at I/O-port level with its own
IRQ handlers** (`irq.c`, `com.c`, `_InitIRQ`, `_IRQWrap0..`, `_EnableIRQ_End`). That is why it was
never ported to Windows and why the vendor shipped a bootable DOS CD.

Source-file names from the COFF debug records:

```
gsat.c  init.c  initutil.c  util.c  limits.c  getaddr.c   com.c  irq.c  gccint8.c
hostsim.c hostloop.c hsasutil.c hacknack.c        <- HOST side
gamesim.c gameloop.c gamesas.c  gacknack.c        <- GAME (EGM) side
gepix.c geputil.c GEPif.c                          <- GUI link
meters.c player.c progress.c ted.c tedplay.c tickutil.c scriptix.c torment.c gsasutil.c
```

Symbols include `GEPInitialize`, `GEPSend`, `GEPReceive`, `GEP_Perform`, `GEP_Set`,
`GEP_Interpreter`, `GEP_get_status`, `GEPStatusCallback`, `GEP_Trace_Sequence_Number`,
`GEP_Reset_After_Disconnect`, `Build_GEP_Response`, `prepare_GEP_trace_packet_host_poll`,
`prepare_GEP_trace_packet_host_reply`, `prepare_GEP_trace_packet_game`,
`prepare_RESYNC_GEP_trace_packet`, `prepare_Chirp_GEP_trace_packet`, `Display_GEP_Trace`.
Note the `prepare_GEP_trace_packet_*` family: **the trace the GUI shows is produced by the engine
and shipped over GEP** — the GUI never sees the SAS wire itself.

### 4.2 `gsat.exe` command line — it does have one

Strings, verbatim:

```
GSA SAS Tools Engine
GSA Tools Engine running as %s
host
game
gui-port-addr=     gui-port-irq=     sas-port-addr=     sas-port-irq=
parameter %s,PARAMETER NOT A VALID OPTION
SAS Port Address [0x%03X] is invalid. Valid range is 200 to 3FF
SAS Port Address must be entered as a hex number.
GUI Port Address [0x%03X] is invalid. Valid range is 200 to 3FF
SAS Port IRQ [%d] is invalid. Valid IRQs are [2,3,4,5,7,9,10,11,12,13,14,15]
Port IRQ's cannot be assigned to same value.
Port addresses cannot be assigned to same value.
*****EXITING - Command line arguments incorrect.******
AFTER INTERNAL ENGINE INIT / GUI Port IRQ = %d ADDRESS = 0x%03X / SAS Port IRQ = %d ADDRESS = 0x%03X
Attempting GEP Comm Open, port %d:  /  Attempting SAS Comm Open, port %d :
```

So `gsat.exe [host|game] [gui-port-addr=3F8] [gui-port-irq=4] [sas-port-addr=2F8] [sas-port-irq=3]`.
The defaults are **UNKNOWN** from the strings alone; the DOSBox rig and the user's notes imply
GUI = DOS COM1 (0x3F8/IRQ4) and SAS = DOS COM2 (0x2F8/IRQ3), matching `serial1`/`serial2` in
`dosbox-x.conf`.

Inside the bootable ISO I recovered the batch files: one runs `gsat.exe` (host — the CD's own file
list says *"host.btm  Use this batch file to load and run gsat.exe"*), another runs
**`gsat.exe game`**. **The same binary is both the SAS host-simulator engine and an EGM/game
simulator** — the toolkit ships an EGM simulator the user never used.

### 4.3 What `gsat.exe` does

Its interactive display menu (from strings) is a full EGM/host state inspector:

```
------ Display -----------
 3       -  SAS Progressive Hit Queue      7 -  0x7B Values
 8       -  SAS Level Amounts              9 -  Non-SAS Level Amounts
 b B     -  Ticket Buffer                  d D -  Handpay FIFO
 e E     -  Exception Queue                p P -  Active Polling Cycle
 t T     -  TED queue - t=terse T=verbose  v V -  Engine Version Number
 a       -  AFT Info    f - AFT Meters     i -  AFT Buffer
 x X     -  Display Debug Info             h H ? - Print this menu
 <space> -  Pause SAS display (SAS comm continues)
 g G     -  Pause GEP display (GEP comm continues)
 c - cls   <CR> - scroll one line   <esc> - Terminate engine execution
```

It carries the whole SAS model: the full `M_*` meter enumeration (`M_TOTAL_COIN_IN_CREDITS` …
`M_TOTAL_E_TRANS_TO_HOST_...`), the AFT registration state machine
(`AFT_REGISTRATION_READY/REGISTERED/PENDING/NOT_REGISTERED`,
`AFT_REGISTRATION_0x73_NEVER_RECEIVED`), AFT transfer arithmetic (`UpdateAFT72`, cash/restricted/
non-restricted pools, partial pay, escrow), validation types
(`NONE / STANDARD / SYSTEM / SECURE_ENHANCED`), the `0x7B` configuration bits
(`X7B_USE_PRINTER_AS_CASHOUT_DEVICE`, `X7B_SECURE_ENHANCED_VALIDATION_CONFIGURATION`, …), a handpay
FIFO, a ticket buffer, progressive levels, an exception queue and a TED (timed-event) queue.
It also runs a link supervisor (`GEP HOST RESET / GEP GAME RESET / GEP Host Failed / GEP Game
Failed`) and knows `Can't do that! AC Power is OFF`.

`Bootable_ISO\gsat1_1.iso` (2.2 MB) is a FreeDOS/4DOS boot CD: `ISOLINUX/` with `ISOLINUX.BIN`,
`MEMDISK.` and a 1.44 MB `BTDSK.IMG` floppy image containing `autoexec.bat`, `config.sys`,
`menu.btm`, `4dos\`, `fdos\`, `sas\gsat.exe`, `cwsdpmi.exe`, `host.btm`. Its README says the switch
from MS-DOS to FreeDOS/4DOS was a **licensing** decision at GLI (*"GLI doesn't have enough licenses
for all the SAS Engine PCs using the bootdisk"*).

### 4.4 The DOSBox rig the user built — and what it proves

`DOSBox\dosbox-x.conf` (the operative file):

```ini
# GUI (SASHostSim.exe) uses COM3
# Engine (gsat.exe in DOSBox) uses COM4
[dosbox] title=GSA SAS Engine   memsize=16
[cpu]    core=auto  cputype=pentium  cycles=32000
[serial]
serial1=directserial realport:COM4     ; GEP  (GUI communication)
serial2=directserial realport:COM6     ; SAS  (EGM communication, EGM on COM5)
serial3=dummy
serial4=dummy
```

`DOSBox\autoexec.bat` mounts `SAS Engine` and runs `gsat.exe` **with no arguments**.
`啟動SAS測試環境.bat` is a one-click launcher: `taskkill dosbox-x.exe / SASHostSim.exe /
dotnet.exe`, regenerate `autoexec.bat` with the absolute path, start DOSBox-X, wait 6 s, start
`SASHostSim.exe`, then instruct the operator to *"select COM3, wait for green light, then start your
EGM game (COM5)"*. `關閉SAS測試環境.bat` just taskkills both. (`taskkill dotnet.exe` is a leftover
from the abandoned .NET "SasBridge".)

The 13 PowerShell scripts are a serial-diagnosis campaign; each carries a Chinese header stating
purpose / method / **result** / conclusion:

| script | what it did | recorded result |
|---|---|---|
| `test_comport.ps1` | send `HELLO_SAS` COM6→COM5 @19200 8N1 | garbage on COM5 → *"COM5 cannot be used as standard RS-232"* |
| `test_baudrate.ps1` | COM6→COM5 at 19200/9600/115200/38400/57600/4800/2400/1200 | garbage at **every** rate → not a baud problem, it is the COM5 SAS hardware |
| `test_parity.ps1` | 8N1/8M1/8S1/8E1/8O1 + RTS, COM5↔COM6 | garbage (16 bytes) in every configuration; header notes *"SAS uses Mark/Space parity to emulate the 9-bit wake-up bit"* |
| `test_clean.ps1` | single byte + CTS/DSR/CD check | COM6→COM5: 1 byte in → **4 bytes out (`C1-6E-68-38`), bit 7 set**; COM5→COM6: nothing; CTS/DSR/CD all True |
| `test_direction.ps1` | self-echo + both directions | COM5→COM6 clean (`0xAA`→`0xAA`); COM6→COM5 mangled (`0x77` → `F7-3A-4F-03`); COM5 self-loop returns 4 garbage bytes → *"COM5 is a SAS-slave-only UART, COM6 is standard RS-232"* |
| `test_loopback.ps1` | ABC/XYZ both ways COM5↔COM6 | one direction garbled, the other silent |
| `test_all_pairs.ps1` | `0x55` across every pair of COM1/2/3/4/6 | **no internal cross-connections at all** except the external COM3↔COM4 cable |
| `test_com3_com4.ps1` | ABC/XYZ both ways | **PASS both ways** → chosen for the GEP link |
| `test_com3_com5.ps1` | COM3↔COM5 | COM3→COM5 garbage (12 bytes), COM5→COM3 silent |
| `test_com12_com6.ps1` | USB-RS232 (ATEN PL2303GC) ↔ COM6 | **PASS both ways** |
| `test_com12_com5.ps1` | USB-RS232 ↔ COM5 | same failure as every other port into COM5 |
| `sniff_com3.ps1` | listen on COM3 at 5 baud rates for engine traffic | *"if no data on any baud rate, Engine may not be sending yet — try pressing 'p' in DOSBox"* |
| `sniff_com4.ps1` | try to open COM4; **"Access denied" is the pass condition** | used to confirm DOSBox actually grabbed COM4 |
| `listen_com6.ps1` | passive listen on COM6 @19200 for 3 s | *"No data — EGM is silent (not sending without being polled)"* |

`DOSBox\環境架設紀錄.txt` (2026-07-18, "environment setup record") is the conclusion of all of it:

* **Quixant port map:** COM1–COM4 standard RS-232 (Quixant Communications Port); **COM5 = dedicated
  SAS UART, 9-bit hardware, not standard RS-232**; COM6 standard RS-232; COM12 = external ATEN
  PL2303GC USB-RS232.
* Key findings, verbatim: *"COM5's SAS UART processes received data in hardware (1 byte becomes
  4 bytes, bit 7 modified)"*; *"data sent by COM5 is not received by COM6 (one-way dead)"*;
  *"but SAS communication through COM5 via the Quixant SDK works normally"*; *"the IGT SAS Simulator
  (sastest.exe) on COM6 communicates with the COM5 EGM normally"*.
* **Final working topology:** `SASHostSim.exe (COM3) ⇄ COM4 (DOSBox serial1 = gsat GEP)` and
  `gsat.exe SAS (COM6) ⇄ COM5 (EGM game program, Quixant SAS UART)`.
* DOSBox tuning: `cputype=pentium` (`gsat.exe` is a DJGPP 386 protected-mode program;
  `pentium_mmx` or higher **with `cycles=max` hangs**); `cycles=32000` stable, `cycles=max` faster
  but flaky.
* Troubleshooting log: *"NO CONNECTION"* = engine not started or wrong port; *"EGM reports Link
  Down"* = `gsat` polling cycle too slow / serial timing inaccurate → raise `cycles`;
  *"EGM reports Progressive Timeout"* = LP 86 not sent regularly, or level count / group ID mismatch.
* **Rejected approaches, with reasons:** (A) com0com virtual ports — unnecessary, enough physical
  ports; (B) a TCP "SAS Bridge" relay doing mark/space parity — *"works but unstable, the added
  latency causes link down"*; (C) COM12 (USB) as the SAS port — *"DOSBox hangs waiting for CTS
  because the USB adapter provides no CTS when the far end is unconnected"*; (D) DOSBox
  `serial2=COM5` directly — *"COM5 is SAS-dedicated hardware, `directserial` is incompatible"*.

**What the existence of these scripts supports.** The user spent at least a full day (2026-07-18 →
07-19) not testing SAS but fighting the transport, and ended with a four-port, two-process,
DOS-emulator contraption that still needs an operator to press keys. The three things that cost them
that day are all things SasWorkbench controls directly:

1. **The wakeup bit.** Every failing experiment is an attempt to get a 9th bit through a Windows
   `SerialPort`. Per-byte mark/space parity is the only portable way; the mangled `C1-6E-68-38`
   result shows the Quixant SAS UART reinterprets an ordinary 8N1 stream.
2. **Timing.** `gsat.exe` needs a ~40 ms poll cadence; DOSBox cycle tuning is a proxy for real-time
   scheduling, and "Link Down" / "Progressive Timeout" are timing failures, not protocol failures.
3. **Port topology.** COM5 is EGM-side only; the host must sit on an ordinary port
   (COM6 works, and `sastest.exe` on COM6 already talks to the COM5 EGM).

---

## 5. Comparison with the IGT `sastest.exe` (prior findings)

| | IGT `sastest.exe` | GSA `SASHostSim.exe` |
|---|---|---|
| Toolkit | Borland C++Builder, Delphi `TPF0` forms in RT_RCDATA | **MFC 4.2 / MSVC 6, RT_MENU + RT_DIALOG** |
| Protocol engine | in-process | **separate DOS program over a private "GEP" serial link** |
| Port discovery | hard-coded `"COM1:".."COM8:"` table | **SetupAPI enumeration + device-path probe**; COM10+ listed but the working open still lacks `\\.\` |
| Long polls | 118 dispatched | 613 named poll definitions over 144 command codes in Set1 alone (+412 / +414 / +284 in the other sets) |
| Message model | compiled in | **declarative XML DSL** (`SASTemplates.dtd`) driving encode + decode + print |
| Automation | none | **none** (no switches, no OLE server, no DDE, no exports) |
| Trace export | — | `.trc` plain text with per-message decode and wakeup-bit brackets |
| Wire log | — | binary `.log` with **per-byte millisecond timestamps** |

---

## 6. Unknowns / not established

* The **GEP wire protocol** itself (framing, command ids, sequence numbering). I recovered the API
  names on both sides but not a single byte layout. Reconstructing it would need a disassembly of
  `gsat.exe` (DJGPP COFF) or of `SASHostSim.exe`'s command queue.
* The **tail bytes of a poll-definition record** (26–35 bytes) — they hold the multi-denom flag,
  interbyte delays and wakeup-bit list, but I did not separate the fields. The user-visible content
  is recoverable from `PollDef.txt` instead.
* Where **exception→response** mappings are persisted, if anywhere (§3.8).
* The exact meaning of the two 9-byte `flagsA`/`flagsB` blocks and the four `sel` DWORDs in a test
  configuration (I can diff them but cannot name every bit).
* `gsat.exe`'s **default** port addresses/IRQs when invoked with no arguments.
* `TicketList.dat` `ticketType` is 1 in every record; I have not proven it maps onto the
  `Cashable / Restricted Promotional / …` combo enum.
