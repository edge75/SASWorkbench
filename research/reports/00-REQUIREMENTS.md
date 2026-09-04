# 00 — Requirements Specification
## A new standalone cross-platform SAS host test application (Avalonia / C# / .NET) to replace and beat `C:\IGT3\sastest.exe`

**Status:** requirements draft for review · **Date:** 2026-09-03
**Inputs (read in full, not re-derived here):** `01-binary-re.md`, `02-ini-spec.md`, `03-docs-and-formats.md`,
`04-libsas-surface.md`, `05-serial-diagnosis.md`, `06-qsp-test-needs.md` — all in this directory.

**Scope rule honoured throughout:** nothing in this document derives from `C:\Progetti\libsas\utils\sashost`,
`utils/poller` or `utils/hwconformance`. Those trees were never opened by any input report and are not a
design input here. `C:\Progetti\libsas\src`, `include`, `test`, `examples`, `docs` and `C:\Progetti\QSP`
were read and are cited. The only benchmark is `C:\IGT3\sastest.exe`.

**Convention:** every factual claim carries its source report and section. Where the input reports disagree
or left something open it is marked **UNKNOWN** and the disagreement is stated, not smoothed over.

Working name for the product in this document: **SasBench** (host-side SAS 6.02 test bench).

---

## 1. What `sastest.exe` is

`sastest.exe` is IGT's **SAS host simulator**: a Windows desktop program that pretends to be the casino
management system at the other end of a slot machine's serial cable. SAS (Slot Accounting System) is a
master/slave protocol — the host polls, the gaming machine answers — so to test a gaming machine you need
something to do the polling. That is all this program is. It opens a COM port at 19200 baud, sends a
"general poll" every 200 ms or so, and lets an engineer click a drop-down to fire any of ~118 "long poll"
commands at the machine: read a meter, print a ticket, register an AFT transfer, broadcast a progressive
jackpot amount. It shows two scrolling windows — raw hex sent and received on the bottom, an English
translation of the same traffic on top (01 §3.2, 03 §1.2). Version 2.0.2.1, built with Borland C++Builder,
linked **2005-12-16**, and self-described in its own About box as *"a prototype test program… never meant
for release"* (01 §1, §3.5).

A Quixant engineer uses it like this. Quit the app; open `sastest.ini` in Notepad; set `Port = 3`; save;
relaunch; click through a modal "I Agree" licence dialog; watch `TX>= 81` scroll past and hope `RX<= 00`
appears next to it (03 §6 Workflow A). To read a meter, scroll a flat unsearchable list of ~200 entries,
pick one, and answer a stack of modal dialogs that ask for **hex bytes** — the operator does the BCD and
byte-order conversion in their head (03 §6 Workflow B). To run a sequence, hand-write hex frames into the
INI's `[Script]` section, tick a checkbox, and read the results out of the scrolling window; there is no
expected value, no verdict, and enabling scripting **switches off the normal poll loop entirely**
(02 §12). Everything the tool knows about SAS lives in one hand-maintained 1735-line INI file whose own
first line reads `;WARNING WARNING / ;Errors in this file cause unexpected results`, and whose vendor
documentation admits *"errors in the INI file may render the tester unusable and cause it to crash"* and
*"the program at this time does not have any error checking for the format of the ini file"* (02 §18, 03 §2).

It is stuck in 2005 in a very literal way. The binary can only name COM ports **1 through 8**, using the
DOS-era `"COM1:"` form; the string `\\.\` — the prefix Windows has required for COM10 and above since NT —
does not occur anywhere in the 985 KB file (05 §1.2). The 9th "wake-up" bit that SAS uses for addressing is
faked by flipping the UART's parity between MARK and SPACE and separating the address byte from the body
with `Sleep(2)`; the program never calls `FlushFileBuffers`, so it cannot know the address byte actually
left the chip, and the vendor's own help file says so (05 §2.1). There is **no command line, no exit code,
no headless mode, no IPC** — an exhaustive string search found no usage banner, no switch table, and no
`argv` reference beyond the Borland C runtime's own startup code (01 §6). It logs to a text file with
**one-second** timestamps, which cannot express a protocol whose response budget is 20 ms; the copy sitting
next to it on disk is 39,357,255 bytes containing exactly two distinct messages and not one single reply
(03 §4.3). Two of its feature tabs ship hidden, two buttons are captioned `test` and `Test Button`, and the
SAS-address radio group lists "58" twice so address 59 is unreachable (01 §3.2, §10).

---

## 2. Complete feature inventory of `sastest.exe` — the parity checklist

Derived from `01-binary-re.md` §9 (decoded Delphi form resources, import table, strings, shipped docs and
data files). Every row is one capability the replacement is measured against. IDs are stable; later
sections reference them.

### 2.1 Session / lifecycle (menus, About)
| ID | Capability |
|---|---|
| F1 | Modal EULA gate at every launch (`I Agree` / `I Don't Agree`), exits on refusal |
| F2 | About box: product name, version, copyright, "Supports SAS Versions" string |
| F3 | WinHelp `SAStest.hlp` + in-app **Active Help** (per-command protocol prose from INI `[Active Help]`) |
| F4 | File - Reload INI file — reload the command database without restarting |
| F5 | File - Save / Restore Default Data — full app state to a `*.set` file |
| F6 | AFT - Save / Restore AFT Defaults — AFT-only state to a `*.aft` file |

### 2.2 Link / transport
| ID | Capability |
|---|---|
| F7 | Serial port selection — COM1-COM8 only, chosen by an integer in INI `[SAS Protocols] Port`, restart required |
| F8 | Fixed line settings 19200 baud, 8 data bits, stick parity, **2 stop bits**; not configurable |
| F9 | Wake-up (9th) bit emulated by MARK/SPACE parity flip; can be disabled (`Wake Up Bits` checkbox) |
| F10 | Configurable wake-up delay 1-10 ms (INI `Wakeup Delay`) |
| F11 | Send multiple wake-up bits; `Test MWB0111` diagnostic |
| F12 | DTR/RTS set and clear ("DTR for Fiber Optic"), performed by rewriting the DCB |
| F13 | Comm loopback / echo suppression for fibre loops, incl. an echo-index offset |
| F14 | Live comm counters: `Time Outs =`, `Bytes Read =` |
| F15 | Process/thread priority boost (Priority Normal / Priority High) |

### 2.3 Polling engine
| ID | Capability |
|---|---|
| F16 | General-poll loop, continuous rate control 20-2100 ms (trackbar) |
| F17 | One-click rate presets: 40 ms / 80 ms / 150 ms / 200 ms / GPs OFF |
| F18 | Fine rate spinner 40-2000 ms |
| F19 | SAS address select 1-64 (radio group; item 59 holds the string "58" — a defect) |
| F20 | Address cycling up to a configurable maximum |
| F21 | Sync/phantom-poll control: `No Sync (80)`, `No Sync (with time slot)` |
| F22 | Manual single general poll; manual Re-Sync |
| F23 | Show / hide syncs and general polls in the log |
| F24 | Suppress idle chatter — hide `$00` (no activity) and `$1F` (waiting for input) |

### 2.4 Command dispatch
| ID | Capability |
|---|---|
| F25 | Long-poll picker driven by the INI: 122 entries over **118 distinct opcodes** |
| F26 | EFT command picker: 18 entries, with transaction-number and ACK two-phase handshake |
| F27 | ASDC `$A5` command set (deprecated by the vendor; section empty in every shipped INI) |
| F28 | Free-form custom hex command with optional CRC |
| F29 | Quick Commands — one-click verbatim byte strings from INI, CRC auto-appended when > 2 bytes |
| F30 | Resend last message / last command (four separate resend buttons) |
| F31 | Per-parameter modal input dialog, one dialog per field, with an on-screen hex keypad |
| F32 | Message Structures (frame envelope) + runtime Data Structure editor (payload templates) |
| F33 | Bonus Game Data Structures (deprecated; empty in every shipped INI) |

### 2.5 Decode / display
| ID | Capability |
|---|---|
| F34 | Dual-pane view: English decode above, raw TX/RX hex below, user-resizable splitter |
| F35 | Exception-code to English text (255 codes defined, 121 with real meanings, 134 "Unknown exception!") |
| F36 | Response field decoder: 395 descriptors over 117 codes; absolute offsets **and** a cursor/TLV mode |
| F37 | Per-message response-time display and a `Monitor Timing` toggle |
| F38 | CRC-error flagging in the log (`<-CRC Error !`), `Report CRC Errors` toggle |
| F39 | Freeze / resume the display |
| F40 | Clear English pane / hex pane / both |

### 2.6 Validation and TITO
| ID | Capability |
|---|---|
| F41 | Validation mode: No automatic processing / Standard / Secure Enhanced / System |
| F42 | System validation number (16 digits) + system ID (3), randomise, auto-increment |
| F43 | Secure Enhanced: machine ID + sequence number, manual and auto initialise (`$4C`) |
| F44 | Ticket redemption: `$70` interrogate, `$71` redeem, `$71` get status |
| F45 | Auto-redemption responder (on by default) |
| F46 | Ticket amount override, hex insert, transfer-code selection from an INI table |
| F47 | Restricted redemption: expiration + pool ID |

### 2.7 AFT
| ID | Capability |
|---|---|
| F48 | AFT registration `$73`: 5 registration codes, asset number, 20-byte BCD registration key, POS ID |
| F49 | Auto-register on demand (on by default) |
| F50 | AFT lock `$74`: 3 lock codes, 4 lock conditions, lock timeout |
| F51 | AFT transfer `$72`: 4 transfer codes x 8 transfer types |
| F52 | Cashable / restricted / non-restricted amounts (10-digit BCD each) |
| F53 | 7 transfer flags incl. host-cashout enable/mode and "accept only if locked" |
| F54 | Transaction ID, index, expiration, pool ID; auto-increment TID; append-unique TID |
| F55 | AFT interrogate + auto-interrogate on exception `$69` (on by default) |
| F56 | Cancel AFT transaction |
| F57 | Receipt data: 8 selectable field types |
| F58 | Transaction receipt text editor: 11 lines (location, address 1/2, in-house 1-4, debit 1-4) |
| F59 | Update Receipt Data (`$75`) |

### 2.8 Progressive
| ID | Capability |
|---|---|
| F60 | Single-level progressive broadcast `$80` (on by default) |
| F61 | Multiple-level progressive broadcast `$86` |
| F62 | Repeating broadcast at a 1-10 s interval |
| F63 | One-shot broadcast button |
| F64 | Auto-increment progressive amounts by a configurable increment |
| F65 | Progressive group select 0-255; per-level amounts |
| F66 | Show / hide broadcasts in the log |

### 2.9 Component authentication (`$6E`)
| ID | Capability |
|---|---|
| F67 | 4 actions: interrogate count / read status / authenticate / interrogate authentication status |
| F68 | 7 methods: None, CRC16, CRC32, MD5, Kobetron I, Kobetron II, SHA1 |
| F69 | Seed + offset entry |
| F70 | Addressing by component index or by component name |
| F71 | Optional inclusion of the authentication-data and address-specification blocks |
| F72 | Auto-interrogate (on by default) |

### 2.10 Multi-denom, meters, EFT tab
| ID | Capability |
|---|---|
| F73 | Multi-denom: denomination checklist + "Add preamble to long polls" (`$B0`) |
| F74 | Individual meter monitor: meter code + byte position + byte count, "Times Meter Changed" counter |
| F75 | Canned meter-test sequence (22 hard-coded steps in INI `[Meter Test]`) |
| F76 | EFT tab (ships hidden): transaction number inc/dec, ACK control, denomination list |

### 2.11 Fault injection (negative testing)
| ID | Capability |
|---|---|
| F77 | Force CRC error on transmit |
| F78 | Break the TX/RX line on the final ACK, then restore it |
| F79 | Garble the message |
| F80 | Bad validation number |
| F81 | Bad transfer code |
| F82 | Transaction-ID error |
| F83 | Zero registration key |
| F84 | Zero the transaction field |
| F85 | Force EFT ACK / suppress EFT ACK |
| F86 | Insert a general poll mid-sequence |

### 2.12 Scripting (such as it is)
| ID | Capability |
|---|---|
| F87 | Verbatim send-sequence from INI `[Script]`, CRC auto-appended over 2 bytes, English label per step |
| F88 | Scripting suppresses all automatic polling while it runs |
| F89 | Two list panes showing script steps and progress |

### 2.13 Logging and reporting
| ID | Capability |
|---|---|
| F90 | Log - Capture Data — append TX/RX to `TXRXData.dat` with 1-second `ctime()` timestamps |
| F91 | Report tab: free-text buffer, `Add Report Entry`, `Clear` (buffer wiped on any drop-down change) |
| F92 | File - Save Report File to `SasReport.txt` |
| F93 | Save arbitrary data through a `*.dat` save dialog |

### 2.14 Pulltab / RNG (ships hidden)
| ID | Capability |
|---|---|
| F94 | Pulltab paytable grid: 2 games x 9 line/coin combinations, wins, total tabs |
| F95 | RNG seed mode, force a pulltab, external `DD2K_n.BIN` outcome files (format UNKNOWN — no such file on disk) |

**Parity verdict up front.** The replacement must cover **F1-F93 in substance, not in shape** — F1 is
deliberately *not* reproduced (the EULA modal is a blocker for unattended runs), and F31's
one-modal-dialog-per-parameter interaction is replaced by an inline typed form. **F94/F95 are proposed for
drop**: both tabs ship hidden, `[RNGMode]` is empty in all four INI variants, and the `DD2K_%d.BIN` format
is UNKNOWN with no sample on disk (01 §12, 02 §20.5). That is a decision for the user, not a unilateral cut
(§7 Q2).

---

## 3. Why it fails today

Split into what the reports **proved** and what they **infer**. Confidence values are the input reports'.

### 3.1 Proven — read from the bytes, the disassembly, or the vendor's own documents

**W1 — It cannot name a COM port above COM8, and the user's adapter is above COM8.** The device-name table
is a hard-coded 7-entry array of `"COM1:"..."COM8:"` at VA `0x004B81FC`, loaded by `mov ecx, 7` at
`0x004132CE`; the string `\\.\` occurs **zero** times in the whole 985 KB binary, and there is no `COM%`
format string (05 §1.2). `CreateFileA("COM10:")` cannot work on Win32 by design. This is the direct cause
of complaint (b) — "it does not work on a laptop where the serial port is a USB adapter".
**Confidence: VERY HIGH (99 %).**

**W2 — The shipped configuration is out of bounds and the table itself is corrupt.** Live `sastest.ini`
has `Port = 11`; the lookup `mov edx, [ebp + eax*4 - 0x50]` at `0x004134BA` has **no bounds check**, so
`Port = 11` reads uninitialised stack, and a `repne scasb` strlen runs on that pointer *before*
`CreateFileA` — so it can access-violate before it ever tries to open anything. Slot 4 of the table is the
single string `"COM4:,COM5:"` (byte `0x2C` where a NUL belongs, at file offset `0x000B7633`), which is not
a valid device name, and that typo shifts every later slot by one: `Port=5` opens COM6, `Port=6` opens
COM7, `Port=7` opens COM8. **Confidence: VERY HIGH (95-99 %).** (05 §1.2, 01 §5.)

**W3 — There is no automation surface of any kind.** Exhaustive ASCII and Unicode string search: no
`usage`, no `--help`, no switch table, no `argv` reference except the Borland CRT's own startup code;
`Subsystem=2` (GUI) means there is no console output path either. The vendor's help file states the only
supported way to vary a run is *"create a shortcut… go to the INI file and specify the port"*. On top of
that, every launch is gated by a modal `I Agree` / `I Don't Agree` dialog. This is complaint (c), confirmed
at the binary level. **Confidence: PROVEN (01 §6, §3.5).**

**W4 — `[Script]` is a replay list, not a test language.** The entire grammar is
`<hex frame>[ = <English text>]`. Report 02 §12(c) proves item by item the absence of loops, waits,
assertions, variables, conditionals, response-field capture, named/multiple scripts, and any pass/fail
verdict. The only `Repeat*` symbols in the binary are a manual "Send Command" button and the progressive
broadcast repeat; the only `Assertion failed` strings belong to the Borland C runtime. And per the vendor,
*"Scripting short circuits all automatic polling so what is in the script is the only thing sent"* — so a
script cannot even observe the machine while it runs. **Confidence: PROVEN.**

**W5 — Response timing is unmeasurable.** `QueryPerformanceCounter` is **not** imported (01 §4). Timing
comes from `GetTickCount`/`timeGetTime`-class sources at 1-15.6 ms granularity, against a SAS 6.02 §2.3.1
budget of **20 ms** for the whole response (06 §6 T03, citing `libsas_internal.h:222`
`SAS_EGM_RESPONSE_TIMEOUT 20`). The on-screen `( ms response time)` never reaches the log file at all.
**Confidence: PROVEN.**

**W6 — The capture file is unusable as evidence.** `TXRXData.dat` is a text log of one `ctime()` line plus
one `TX>=`/`RX<=` line per frame: **1-second resolution**, ~40 bytes of timestamp per 1-byte poll, CR-only
line endings, no latency, no CRC verdict, no port identity, no rotation, no size cap (about 15 MB/day,
unbounded). The copy on disk is 39,357,255 bytes containing **exactly two distinct messages (`80` and
`81`), zero responses and zero exceptions** — 62.5 hours of polling a machine that never answered, and
nothing in the tool said so. A timeout is represented only by the *absence* of an `RX<=` line.
**Confidence: PROVEN (03 §4).**

**W7 — The wake-up-bit transmit path is a race with no barrier.** Reconstructed from the disassembly at
`0x00413920`-`0x00413AAE`: set MARK parity, `WriteFile` one byte (overlapped, **not** waited on),
`Sleep(WakeupDelay)`, set SPACE parity, `WriteFile` the rest. `FlushFileBuffers` is **not imported**, so
the only ordering barrier between the address byte and the parity change is a 2 ms sleep against a 625 us
byte time. The vendor documents the consequence themselves: *"it is virtually impossible to determine if
the byte with the wakeup bit really went out of the UART"* — a claim that is **false about Win32**
(`FlushFileBuffers` has done exactly that since NT 3.1) and is the root of the whole design.
**Confidence: PROVEN as a design flaw (05 §2.1).**

**W8 — The configuration format has no schema and no validation.** 1735 lines, 25-27 sections; positional
CSV in which a comma inside a description silently corrupts the entry; two key sigils; **three mutually
incompatible enum syntaxes**; dead keys nobody reads (`Setup File`, `DTR`, `[AFT] Transfer code/type`);
live keys that appear in no variant (`Length`, `Auto Initialize`); hand-maintained length bytes;
hard-coded machine addresses in three separate sections; ~100 lines of `$6F`/`$AF` copy-paste for want of a
repeat construct; runtime template edits discarded on exit. The vendor's own docs say malformed entries
crash the tool and that there is no format checking. **Confidence: PROVEN (02 §18).**

**W9 — Automation exists only as hard-coded checkboxes.** `Auto Redemption`, `Auto Register`,
`Auto Interrogate`, `Auto Increment Validation Num.`, `Auto Initialize Validation`, `Auto Inc TID`,
`Auto respond to req. for handpay info`. None is configurable from the INI, none is composable, none is
scriptable, none is observable in the log. The one reactive hook in the file, `[Exception Handling]`,
contains a single self-referential line `$7E = $7E`. **Confidence: PROVEN for the checkbox list; the
semantics of `[Exception Handling]` are INFERRED — see §3.3 (02 §12(c), §15).**

**W10 — Shipped-quality defects visible in the UI.** The address radio group lists `58` at position 59, so
address 59 is unreachable and 58 appears twice; two whole tabs (`EFT Test`, `Pulltab`) ship with
`TabVisible = False`; two buttons are captioned `test` and `Test Button`; five typos in shipped text
(`Reciept Data`, `Compnent Name`, `custon ticket data`, `Txfrr`, `activiy`); zero keyboard accelerators
anywhere (`RT_ACCELERATOR` empty, zero `ShortCut` properties). **Confidence: PROVEN (01 §3.2, §10).**

**W11 — One instance, one port, one EGM, ANSI only.** All 406 imports are ANSI variants; there is a single
port object; the report buffer is *"cleared each time you select the long poll drop down, EFT drop down, or
send a custom command"* and *"doesn't work with scripting"* — so the report tool cannot capture the two
things an engineer most needs to report: a scripted run, and a multi-command sequence.
**Confidence: PROVEN (01 §4, §10; 03 §1.3).**

### 3.2 Suspected / inferred — stated as such

**W12 — Per-byte MARK/SPACE parity switching over a USB-serial bridge is unreliable even after W1-W3 are
fixed.** The argument is structural: data goes out the **bulk** endpoint while a parity change is a
**control** transfer on endpoint 0, scheduled independently in 1 ms USB frames; FTDI's Linux maintainers
documented that redundant reconfiguration URBs *"cause the FTDI chip to glitch its serial output and
corrupt a character it is currently outputting"*; FTDI's own KB explains that writes are buffered
precisely so per-byte writes do not destroy throughput; and several bridges (CP2102, PL2303, CH340) have
reported stick-parity defects. **This was not measured — no USB-serial adapter is attached to the
development machine (05 §4).** Report 05 rates it **HIGH (~90 %)** that the naive mechanism breaks on USB,
and separately **MEDIUM-HIGH (~75 %)** that a *properly written* host (write, drain, switch, write,
drain, latency timer 1 ms, genuine FTDI) can drive SAS over USB. **This is the riskiest assumption in the
project; Milestone 1 exists to settle it.**

**W13 — A default 16 ms FTDI/CP210x latency timer blows the 5 ms inter-byte budget by 3x.** The 5 ms
figure is first-party — SAS 6.02 §2.3.2, quoted verbatim inside `libsas_uart.cpp:201-207`. The 16 ms
default and its 1-255 ms range come from FTDI's own knowledge base; the CP210x registry path is a
secondary source (**MEDIUM**); CH340 has no documented equivalent (**UNKNOWN**); Microsoft's `usbser.sys`
CDC driver has **no latency knob at all** — and the only USB serial device ever attached to this machine is
a FutureLogic CDC device on `usbser.sys`, the worst case. **Confidence: HIGH in general, untested here
(05 §3.4, §4).**

**W14 — The 20 ms response budget is already being missed in practice.** `progressive_sas.log` (a
host-side capture found at the QSP repo root; **producer UNKNOWN**) shows LP `$1A` answered in **29 ms**
and LP `$53` in **46 ms**, and `.docs\qxcom-serialread-latency.patch` documents a ~10 ms driver regression
that ate half the budget. The same file contains 36 sends of LP `$2E` (delay game) and **36 timeouts, zero
responses**, repeating every ~20 s for the whole 250 s capture, while other polls on the same link answer
normally. Whether that is an EGM defect or a host defect is **UNKNOWN** — the capture shows only that no
response arrived. **Confidence: the measurements are PROVEN; the cause is UNKNOWN (06 §4.3, §9).**

**W15 — A tester driven from `conf.ini` will believe false things about the EGM.** `C:\IGT3\conf.ini` is
byte-identical to `C:\Progetti\libsas\include\conf.ini` and is **not read by `sastest.exe` at all** — no
reference to it or to any of its key names exists in the binary. It is the *libsas example EGM's* feature
declaration. Compared against what QSP actually passes to `sasInit`, **five bits disagree**, including
`componentAuthentication` (ini 1, QSP 0 with a `// TODO` comment) and `jackpotMultiplier` (ini 1, QSP 0).
A host that trusts the ini will send `$6E` to a machine that does not implement it. **Confidence: the
byte-identity and the five-bit mismatch are PROVEN; that anyone is currently misled is SUSPECTED
(02 §2, 04 §3, 06 §3.5).**

**W16 — The two ends have never agreed on the line framing.** `sastest.exe` transmits `data=8 stop=2`
(a 12-bit frame, 625 us) while libsas configures the EGM UART as `19200, 9 data bits, no parity, 1 stop
bit` (11 bits, 573 us). Receivers normally tolerate extra stop bits, so this is unlikely to be a fault —
but it means framing must be explicit and configurable in the new tool, not baked in. **Confidence: the two
configurations are PROVEN; that the mismatch matters is SUSPECTED (05 §2.1, §2.2).**

### 3.3 Open UNKNOWNs the reports refused to guess at

* Semantics of `[Exception Handling]` (`$7E = $7E`), `[LongPoll Handling]` (empty) and `[RNGMode]` (empty).
  The "on event X send Y" reading of the first is INFERRED, not confirmed (02 §15, §20.4).
* The undocumented 8th field `b` on `$08`; whether field 5 of the command flag list is `CRYPT` (the INI's
  own header) or `STATUS` (both vendor documents) — every line in the file uses `n`, so the data cannot
  settle it (02 §20.1-2).
* Whether `@0` variable-length inputs take raw hex or literal ASCII (02 §20.3).
* Whether `TXRXData.dat` is appended or truncated per run, and whether logging can be turned off (03 §7).
* The `.set` / `.aft` file formats — no such file exists on disk to inspect (03 §3, §7).
* The `DD2K_%d.BIN` RNG outcome-file format (01 §12).
* Whether a real FTDI FT232H with `LatencyTimer = 1` plus drain-then-switch actually holds the MARK/SPACE
  boundary at 19200 — **untested, no hardware present. The one experiment that most needs running**
  (05 §7).
* Whether the target EGM tolerates wake-up-less operation at all (05 §7).
* libsas's default values for the fifteen `SAS_CONF_*` parameters QSP never sets (06 §3.4, §9).
* What produced `progressive_sas.log`, and what its `Timeout, EGM poll response` line actually measures
  (06 §9).

---

## 4. Requirements for the new app

Numbered user stories, each with acceptance criteria. **P0 = v1.0, must ship. P1 = v1.1. P2 = later or
if cheap.** Parity references (`F...`) tie back to §2; failure references (`W...`) tie back to §3.

### 4.1 Port discovery and connection — P0

**US-1 (P0) — I pick my port from a list that shows what the device actually is.**
*Replaces F7. Fixes W1, W2.*
- The connection panel lists every serial port on the machine with a **friendly name**, e.g.
  `COM12 — FTDI USB Serial Port (FT232R, SN A50285BI)`, sourced on Windows from `Win32_PnPEntity`
  (`Name LIKE '%(COM%'`) with `HKLM\HARDWARE\DEVICEMAP\SERIALCOMM` as a fallback, and on Linux from
  `/dev/serial/by-id/*` resolved to `/dev/ttyUSB*` / `/dev/ttyACM*` (05 §3.3).
- **COM10 and above must work.** The port name is passed to the transport as `"COM12"`; the app never
  builds a `\\.\COMxx` or `COM12:` path by hand — `System.IO.Ports` rejects both and constructs
  `\\?\COM12` itself (measured, 05 §3.1).
- Ports the app cannot open are listed and greyed with the reason ("in use by another process",
  "access denied"), never silently omitted.
- The list refreshes on hot-plug (`WM_DEVICECHANGE` on Windows; polling `/dev` on Linux).
- The chosen port is persisted **by stable `DeviceID`**, not by COM number, because USB adapters move
  (05 §6). If the saved device is absent at next launch the app says which device it was looking for.
- `HKLM\SYSTEM\CurrentControlSet\Control\COM Name Arbiter\ComDB` is read so the UI can say
  "COM11 has never been assigned on this machine" instead of failing opaquely (05 §3.3).

**US-2 (P0) — The app tells me when my adapter is the problem, before I waste an afternoon.**
*New. Fixes W13.*
- On open, the port is classified: native 16550 / FTDI / CP210x / CH340 / `usbser.sys` CDC / Bluetooth /
  QXCOM, and a **capability banner** is shown: `SupportsStickParity`, `IsUsbBridge`, `LatencyTimerMs`,
  `SupportsTrueNineBit`.
- If the device is FTDI or CP210x, the app reads `...\Device Parameters\LatencyTimer`. If it is > 2 ms it
  shows a non-modal warning naming the value and the requirement ("SAS inter-byte deadline is 5 ms —
  SAS 6.02 §2.3.2") with a **[Fix it]** button that writes `1` and prompts to re-open the port. It never
  writes the value silently, and it degrades to printed instructions if elevation is denied (05 §3.4).
- CH340 and `usbser.sys` are flagged **not recommended** with the reason ("no latency control available").
- Ports on which the wake-up bit cannot be guaranteed are marked **best-effort**, and the run record
  records that fact so a green test on dubious hardware is not mistaken for a green test.

**US-3 (P0) — All line parameters are configurable, and framing is explicit.**
*Replaces F8, F9, F10, F12, F13. Fixes W16.*
- Baud (default 19200), data bits, parity mode, **stop bits (default 1, selectable 2)**, wake-up mode
  (`stick-parity emulation` / `true 9-bit` / `off`), wake-up delay/drain policy, DTR, RTS, echo
  suppression with an auto-detected echo index — all in one profile, changeable **without restarting**.
- Changing any of them is logged into the trace as a `LinkConfigChanged` event, so a capture always
  states the framing it was taken under.

**US-4 (P0) — One button tells me whether the link is healthy.**
*New. The single biggest usability improvement over the legacy tool (05 §6.5).*
- **Line self-test** runs: port opens; loopback (if a loopback plug or the internal loopback transport is
  selected) or a live peer; a wake-up-tagged address byte is transmitted and its delivery confirmed;
  round-trip latency is measured with `Stopwatch.GetTimestamp()`; inter-byte gaps are histogrammed.
- Verdict in under 10 s: **PASS / DEGRADED / FAIL** with a one-line cause and a copyable diagnostic block
  (OS, .NET version, port classification, latency timer, measured p50/p99, wake-up confirmation).
- Available from the GUI and as `sasctl selftest --port COM12`, with its own exit code.

**US-5 (P1) — I can drive more than one EGM.**
*Extends F19/F20 beyond the legacy tool's single link.*
- Up to 4 concurrent links (libsas supports `SAS_CHANNEL_1..4`, 04 §1), each with its own port, address,
  poll rate and trace, shown as separate sessions in the same window.
- Address cycling 1..N on a link, and per-link channel-function assignment mirroring QSP's
  `channel_general/aft/progressive/legacy_bonus/validation/comp_auth/information` (06 §3.3).

### 4.2 The polling engine and its timing — P0

**US-6 (P0) — Polling is precise, and I can see how precise it is.**
*Replaces F16-F18, F21-F24. Fixes W5.*
- General-poll loop with rate settable 20-2100 ms continuously, presets 40/80/150/200 ms and OFF, plus a
  fine spinner — parity with F16-F18.
- Cadence is driven off a monotonic deadline computed from `Stopwatch.GetTimestamp()` (QPC), not from a
  Windows timer tick; `timeBeginPeriod(1)` is set for the process on Windows.
- The UI shows **live measured jitter** (p50/p95/p99/max of actual poll interval) next to the requested
  rate, so "the machine is slow" and "my PC is slow" become distinguishable.
- Sync/phantom-poll control (`80`, no-sync, time-slot variants) and manual single-poll / re-sync — parity
  with F21/F22.
- Show/hide syncs, general polls, idle `$00`/`$1F` and broadcasts as **display filters over a complete
  trace** — the trace always records everything; filters never change what is captured (contrast W6).

**US-7 (P0) — Every frame carries a real timestamp and a real latency.**
*Fixes W5, W6, W14.*
- Both directions timestamped from QPC with **microsecond resolution**, anchored once per session to UTC
  wall clock so traces can be correlated with other logs.
- Per-exchange latency = last TX byte to first RX byte, recorded on the frame, not just displayed.
- A poll with no response is recorded as an explicit `Timeout` event with its deadline — never as an
  absent line (W6).
- Aggregate counters per link: polls sent, responses, timeouts, CRC errors, framing/parity errors, bytes
  each way — parity with F14, and cross-checkable against the EGM's own
  `qspGetNetworkDiagnosticInfo` byte counters (06 §7.5).

**US-8 (P0) — The poll loop keeps running while I do everything else.**
*Fixes W4/F88 — the legacy tool's scripting kills polling.*
- Long polls, scripts, broadcasts and workbench actions are **interleaved into the running poll loop**, not
  substituted for it. A script may explicitly `poll stop` if a test needs silence (e.g. T05 link-down), and
  that is an operation, not a side effect.
- Background broadcast (`$80`/`$86`) runs concurrently with polling and with a script.

### 4.3 The long-poll command surface — P0

**US-9 (P0) — Every command is data-driven, searchable and typed.**
*Replaces F25, F26, F27, F31. Fixes W8.*
- All 118 long-poll opcodes and 18 EFT commands available from a **searchable, filterable** command
  palette (by code, by name, by category, by SAS version, by required feature bit) — not a flat 200-row
  drop-down in file order (03 §6 Workflow B).
- Command **variants** are named sub-commands (`$71 Redeem ticket` vs `$71 Redeem ticket — interrogate`),
  not duplicate keys in an ordered list (02 §5.1).
- Parameters are entered in **one inline form**, typed and validated, never a stack of modal hex dialogs:
  each parameter declares `name`, `width`, `encoding` (`bcd` / `uint_be` / `uint_le` / `ascii` / `bitmap` /
  `bool` / `enum` / `duration` / `money_cents` / `date`), `enum table`, `default`, `min/max`.
- Amounts are entered as money (`25.00`) and dates as dates; BCD and `MMDDYYYY` conversion is the app's job.
- Length bytes and CRC are **always computed**, never typed (02 §10.3).
- The **address is a variable** resolved at send time; changing the EGM address never means editing frames
  (02 §1 — the legacy workflow required rewriting the address byte on 15 lines).
- Commands the current profile says the EGM does not support are marked, with the feature bit named; the
  user may still send them (that is a valid negative test, e.g. T52).
- Explicit P0 coverage of the commands the legacy tool could not reach from any command list: **`$72`
  (AFT transfer funds), `$6E` (authentication), `$B0` (multi-denom preamble)** (02 §5.1(c)).

**US-10 (P0) — Raw frames, quick commands and templates still work, but better.**
*Replaces F28, F29, F30, F32, F33.*
- Free-form hex send with an explicit per-entry **"append CRC" flag** (not the legacy implicit "> 2 bytes"
  rule), with the frame **decoded and shown before it is sent**.
- Quick-command library: labelled, foldered, with `${address}` and other variables substituted at send
  time, persisted, and importable from INI `[Quick Commands]`.
- Payload templates (`[Data Structures]` equivalent): typed, nested TLV, **computed lengths**,
  parameterised (`${asset}`, `${next_transaction_id}`, `${now+30d}`), **persisted across sessions**
  (the legacy editor discarded edits on exit — 02 §10.3), validated against the target command's schema
  before send.
- Resend last command / last frame — parity with F30.

**US-11 (P1) — Legacy EFT is modelled as the two-phase transaction it is.**
*Replaces F26, F76, F85.*
- The transaction-number/ACK handshake is an explicit `phase: request | confirm` with
  `transaction_number: auto`, not two boolean flags the operator must understand (02 §6(d)).
- Returned status bytes are automatically checked against the `[Status Messages]` table **with severity**:
  `$00` pass, `$01`-`$0F` legitimate refusals, **`$10`-`$14` hard conformance violations that fail a test
  automatically** — the INI itself flags these with `;The following status codes indicate improper EFT
  implementation` (02 §7).

### 4.4 Data-driven encode/decode and the frame inspector — P0

**US-12 (P0) — Decoding is a typed schema, not positional guesswork.**
*Replaces F36. Fixes W8.*
- A versioned, schema-validated protocol database (JSON with a JSON Schema, one file per domain) carrying:
  commands, response layouts, enum tables, meter table, exception table, help topics.
- The response decoder supports everything the legacy `[Messages]` DSL did — absolute 1-based offsets **and**
  cursor/TLV mode with runtime length bytes (02 §8.2) — **plus** everything it could not express:
  `repeat ... until end_of_frame` (kills the 12x `$6F`/`$AF` unroll), bounds checking against the received
  length, explicit numeric types (`bcd`/`uint_be`/`uint_le`/`bitmap`), units and scaling,
  `enum: <table>` binding per field, named bit ranges, variant discrimination (`when: length == N` /
  `when: field X == V`), version and feature gating (`since: 6.02`,
  `requires_feature: advancedFundsTransfer`), and an `expect:` clause per field (02 §8.5).
- **A frame that does not match its schema is an error surfaced in the UI and a test failure** — never a
  blank English pane (03 §6 Workflow B: *"if the English statement is not in the window it is probably not
  listed in the ini file"*).

**US-13 (P0) — The frame inspector shows me bytes and meaning at the same time.**
*Replaces F34, F37, F38, F39, F40.*
- Three linked panes: trace list (time, direction, code, summary, latency, verdict), hex view with
  **field-coloured byte ranges**, and a field table (name, offset, width, encoding, raw bytes, decoded
  value, enum label, unit).
- Hover or select a field to highlight its bytes and vice versa.
- CRC is verified on every received frame and shown as a chip (`CRC OK` / `CRC BAD (calc 2B8B, wire 2B8C)`)
  — parity with F38, using the SAS CRC transcribed from `libsas_utils.cpp:97-113` (03 §4.2).
- Freeze/resume display, clear panes, copy-as-hex, copy-as-C-array — parity with F39/F40.
- Search and filter the trace by code, direction, field value, latency threshold, verdict.

### 4.5 Live meters — P0

**US-14 (P0) — Meters are a table with deltas, not a scrolling transcript.**
*Replaces F74, F75.*
- A meter grid: code, name, per-game / per-denomination scope, width, last value, **delta since snapshot**,
  timestamp, staleness.
- `Snapshot` / `Diff` — snapshot the whole meter set, perform an action, snapshot again, view the delta
  table. This generalises the legacy "Times Meter Changed" counter (a single integer with no expected
  value — 02 §14(c)).
- Meter sweeps are composed in the UI (pick meters, pick games/denoms) and executed as `$0F`/`$19`/`$1C`/
  `$2F`/`$52`/`$6F`/`$AF`/`$99`/`$9A`, respecting libsas's real constraints: **`$2F` takes at most 10 meter
  codes per poll**, the `isValidMeter` gap list, `getMeterMinimumSize` 4- or 5-byte BCD, `isMultigameMeter`
  (14 codes), `isMultidenomMeter` (behind `$B0`) (04 §4).
- **Built-in invariants**, checkable live and assertable in a script: `total coin out + total jackpot ==
  total won` (the invariant IGT's own `[Active Help]` `$14` topic states in prose — 02 §16),
  `games played - games won == games lost` as separately counted values, monotonicity, and rollover
  detection at the SAS field width (06 T17, T20).

**US-15 (P1) — Extended meters are ergonomic.**
- `$6F`/`$AF` meter selection by **name** from the 196-code `SasMeterCode` table (04 §4), not hand-typed
  2-byte selectors — the very thing the team was doing by hand as recently as 2025
  (`016F0e0000000001000200030004000d00` was added to `[Quick Commands]`; 02 §1).
- Variable BCD width (4 to 8 by magnitude) and the fixed-8 mode (`SAS_ENABLE_FIX_BCD_SIZE`) are both
  decoded and both assertable (04 §3).

### 4.6 The exception log — P0

**US-16 (P0) — Exceptions are a first-class, filterable, countable log.**
*Replaces F35. Fixes W9, W6.*
- Every general-poll exception is a record: `{timestamp, code, name, category, severity, payload (RTE),
  channel, sequence, acknowledged}` — promoted from "string to print" to a typed registry, seeded with the
  121 known meanings from the INI and extended to libsas's 122-code `SasExceptionCode` enum (02 §4(c),
  04 §5).
- Filter by code, category (door / tilt / bill / meter / AFT / ticket / progressive / power / auth),
  severity and time; **count** per code; timeline view; export.
- RTE mode is supported and displayed correctly: with RTE on, the frame is
  `[address][0xFF][code][data...][crc][crc]`, and with the queue empty and RTE on the EGM is **silent** on
  general polls — the app must not report that as a timeout (04 §5; 06 T10).
- The app tracks the implied ACK/NACK state so it can show whether an exception was acknowledged, re-offered
  or dropped (04 §5; 06 T09).
- **Event rules** (generalising the legacy `[Exception Handling]` line): `on exception $XX -> actions`, with
  conditions, multiple actions, per-rule enable/disable, and every firing logged. Available in the GUI and
  in scripts (02 §15).

### 4.7 AFT / TITO / EFT workbenches — P0

**US-17 (P0) — AFT is a typed transaction, not 40 hex digits.**
*Replaces F48-F59. Fixes W8.*
- Registration (`$73`), lock (`$74`), transfer (`$72`), receipt data (`$75`), custom ticket data (`$76`),
  extended validation (`$7B`) as typed forms with the full field surface listed in 02 §13(c).
- Computed lengths everywhere (`Transaction ID Length`, `Receipt Total Data Length` — every hand-typed
  length in the legacy INI is a latent bug, 02 §10.3), auto-incrementing transaction ID, expiry entered as
  a **date**, receipt built from typed TLV entries.
- Returned status is automatically checked against the 29-code `[AFT Transfer Status Codes]` table **with
  severity**: `00`/`01` pass, `40` pending/retry, `80`-`C1` fail (02 §9).
- A **transfer history** view (request, pending, complete/refused) with the lock state, the registration
  state and the current amounts — none of which the legacy tool showed (03 §6 Workflow E).
- Negative tests are first-class: bad transaction ID, zero registration key, bad transfer code, unregistered
  transfer, receipt-when-unsupported — parity with F80-F83.

**US-18 (P0) — TITO has a lifecycle view.**
*Replaces F41-F47.*
- Validation mode selector (none / Standard / Secure Enhanced / System) with the corresponding state
  machine; `$4C` initialise, `$4D` read enhanced validation info, `$58` receive validation number,
  `$70`/`$71` interrogate/redeem/status, `$7B`/`$7C`/`$7D` ticket data.
- A **ticket lifecycle** panel that ties issue, validation number, redeem and status together and explains
  an outcome by cross-referencing all four legacy code tables automatically (`[Ticket Transfer Codes]`,
  `[Ticket Machine Status Codes]`, `[Validation Type Codes]`, `[Receive Validation Status Codes]`) — the
  legacy tool made the operator do that lookup by hand (03 §6 Workflow F).
- Auto-redemption responder, amount override, restricted redemption (expiration + pool ID) — parity with
  F45-F47 — each as an **explicit, scriptable rule** rather than a hidden checkbox (W9).

**US-19 (P1) — Legacy EFT tab is restored, unhidden.** Parity with F76 and F85, including transaction
number inc/dec and forced/suppressed ACK. The legacy tab shipped hidden (`TabVisible = False`); this one
does not.

### 4.8 Progressives — P0

**US-20 (P0) — Broadcasts are configurable, repeating and observable.**
*Replaces F60-F66.*
- Single-level `$80` and multi-level `$86`, group 0-255, per-level amounts, one-shot and repeating at a
  configurable interval (the QSP capture shows 2 s in practice — 06 §4.3), auto-increment.
- Per-level enable/disable so a test can starve **one** level and leave the others fresh — required by
  SAS 6.02 §10.2, which is per level, not per machine (06 T42).
- The broadcast runs on the link thread interleaved with polling, and every broadcast is traced (show/hide
  is a display filter only).
- Level-win reads `$83`/`$84`/`$85`/`$87` decoded with named fields.

### 4.9 The scripting language, assertions and CI — P0

**US-21 (P0) — I can write a test without being a C# developer, and CI can run it headless.**
*This is the heart of the ask. Fixes W3, W4, W9. Fully specified in §5.*
- A test is a **named, versioned file** on disk, selectable and runnable individually or as a suite —
  not one `[Script]` section per config file that users switch by commenting blocks out (02 §12).
- The language provides: typed sends with named parameters; assertions on decoded fields, exceptions,
  status bytes, meters and timing; capture of a response field into a variable and reuse in a later send;
  loops, waits, timeouts, conditionals; setup/teardown; background event hooks; fault injection.
- **Verdict is machine-readable**: per-assertion pass/fail with expected vs actual and the frames that
  produced it; JUnit XML and JSON reports; a documented exit-code contract (§5.7).
- The GUI and the CLI run the **same engine and the same script files**, verified in CI by running one
  script both ways and diffing the run records.

**US-22 (P0) — The CLI is a first-class product.**

```
sasctl run <script...> [--port COM12] [--address 1] [--profile qsp-gli11]
                       [--param key=value...] [--filter tag:aft] [--seed 12345]
                       [--report junit:results.xml] [--report json:results.json]
                       [--trace out/traces/] [--timeout 30m] [--strict]
sasctl lint <script...>          # parse + schema-validate, no I/O, exit 0 or 3
sasctl list <script...>          # enumerate tests, tags, requirements
sasctl ports                     # enumerate ports with friendly names + capabilities
sasctl selftest --port COM12     # the US-4 line self-test, headless
sasctl decode <hex...>           # decode a frame from the command line
sasctl replay <trace> [--as host|egm]
sasctl diff <traceA> <traceB>
```

- No GUI toolkit is loaded by `sasctl` (it references Engine + Scripting only, never Avalonia), so it runs
  on a headless build agent.
- **No EULA, no modal, no interactive prompt, ever** (W3/F1).

### 4.10 Trace capture, replay and diff — P0/P1

**US-23 (P0) — Traces are complete, compact, and good enough to prove a bug.**
*Replaces F90, F93. Fixes W6.*
- Append-only trace file (length-prefixed binary with a JSONL sidecar index, or JSONL with a binary blob
  store — either way streaming, never slurped): per frame `{t_us_monotonic, t_utc_anchor, link, direction,
  bytes, wakeup_flag, crc_verdict, latency_us, decode_verdict, tags}` plus link events (open, config
  change, timeout, parity/framing error, script step boundary, assertion result).
- Idle-poll suppression is a **capture policy** (`record everything` / `collapse idle runs` /
  `record only around events`), with rotation and size caps. Default `collapse idle runs` — the legacy file
  was 99.99 % idle noise (03 §4.3).
- Export to CSV / text / the legacy `TX>=`/`RX<=` shape, so existing habits still work.

**US-24 (P1) — I can replay and diff.**
- Replay a trace as the **host** (re-send the TX stream) or as the **EGM** (answer with the recorded RX
  stream) — mechanically straightforward because every TX frame is complete and CRC-correct (03 §4.4).
  Timing fidelity is honest: replay of a *legacy* capture is content-faithful but not time-faithful
  (1-second timestamps), and the app says so; replay of a *new* capture is time-faithful to microseconds.
- **Diff two runs**: normalise to `(direction, code, decoded fields)` tuples, collapse idle runs, and show
  a structural diff. Assertable from a script (`expect trace matches baseline "t03"`).
- **Import** `TXRXData.dat` and `progressive_sas.log` — both formats are fully decoded and a working parser
  exists (03 §4.2, 06 §4.3), so years of archived Quixant captures become readable in the new app for
  almost no cost. This is a cheap adoption lever.

### 4.11 Inline protocol help — P0

**US-25 (P0) — The spec is next to the command.**
*Replaces F3.*
- A code-keyed help store rendered **beside** the decoded frame (not in a modal), seeded verbatim with the
  7 `[Active Help]` topics (IGT's own wording of the SAS contract) and extended toward all 118 long polls,
  121 known exceptions and every status table (02 §16).
- Each topic may carry **runnable checks**: the `$14` topic states
  `total coin out + total jackpot == total won` in prose; in the new tool that is a check the meter view
  and a script can both run (02 §16(d)).
- Help content lives in the schema database, is versioned with it, and is editable in-app.
- The team's own reference material is linkable: `docs/SAS-6-02-Protocol.pdf` and `docs/sas-spec.txt`
  (574 KB, grep-able) ship in the libsas repo (04 §9).

### 4.12 Configuration profiles that mirror the EGM — P0

**US-26 (P0) — A run states which EGM configuration it was valid for.**
*Fixes W15.*
- A **profile** is a named, versioned object holding: connection settings, SAS address(es), poll rate,
  validation style, **expected feature flags** (the 19 bits of `sSasEnabledFeatures`, named exactly as in
  `conf.ini` — including the vendor's typo `validatonStyleSecureEnhanced`, preserved for round-tripping),
  limits (Max Ticket In/Out, Max Cashless In/Out, Max Credit Meter...), accounting denomination, and
  channel-function assignment.
- Profiles import from `conf.ini` and from QSP's `Resources/configuration.json` (`Market.SAS.*`) so the
  tool speaks the same vocabulary as the EGM (06 §3.2-3.3).
- **`$A0` reconciliation is a P0 feature**: send `$A0`, decode the three feature bytes bit by bit, and
  show a diff against the profile — green/amber/red per bit. A script can `expect features == profile
  qsp_gli11` or fail loudly. The legacy tool printed the three bytes as decimal numbers and nothing else
  (02 §2). This directly catches the five-bit `conf.ini`-vs-QSP mismatch (W15).
- The run record captures: app version, profile hash, EGM's `$A0` bytes, `$54` SAS version + serial, `$1F`
  machine ID, port classification, and the test outcomes — so "which SAS scenarios have been run, against
  what" stops being unanswerable (06 §7.7).

### 4.13 Fault injection — P0

**US-27 (P0) — Negative testing is a first-class citizen.**
*Replaces F77-F86.*
- Injectable faults, from the GUI and from a script, scoped to the next frame / N frames / all frames:
  bad CRC, truncated frame, over-long frame, wrong address, garbled byte, dropped response, delayed
  response, wrong length byte, out-of-range parameter value, suppressed or forced ACK, mid-sequence general
  poll insert, and TX/RX line break + restore.
- Every injection is recorded in the trace as an explicit event so a failing test explains itself.

### 4.14 Import, migration and adoption — P0

**US-28 (P0) — My 1735-line INI comes across, and its bugs are reported not inherited.**
*Fixes W8. Adoption-critical: a tool that cannot read this file will not be adopted (03 §7).*
- Import `sastest.ini` into the schema database: `[Long Poll Commands]` (122 entries), `[Electronic Funds
  Transfer]` (18), `[Messages]` (395 descriptors), `[General Poll]` (255), the 8 enum tables,
  `[Data Structures]` (9 blocks), `[Message Structures]`, `[Quick Commands]`, `[Script]`, `[Meter Test]`,
  `[AFT]`, `[Active Help]`. Regexes that match every line of every section with zero failures are already
  published in 02 §21.
- The importer **validates** and produces a report: offsets that overrun the frame, hand-typed lengths that
  disagree with their data, duplicate keys, dead keys, commands with no decoder (22 of them, 02 §5.1(c)),
  decoders with no command. It imports faithfully and flags each defect rather than silently importing
  years-old mis-decodes (03 §7).
- `[Script]` and `[Meter Test]` import to runnable scripts in the new language, with the address byte
  replaced by `${address}` (02 §1).

**US-29 (P1) — Everything a legacy `.set`/`.aft` did, a profile does.** Format is UNKNOWN (no sample file
on disk, 03 §3) so import is best-effort; export/import of the new profile format is P0.

### 4.15 Quality-of-life the legacy tool never had — P1

**US-30 (P1)** Keyboard shortcuts throughout and a command palette (the legacy tool has **zero**
accelerators, W10). **US-31 (P1)** Light/dark themes and a high-contrast bench mode.
**US-32 (P1)** Undo on every destructive editor action. **US-33 (P1)** Crash-safe session recovery.
**US-34 (P2)** Localisation-ready strings (the legacy tool is ANSI-only, W11).

### 4.16 EGM-side stimulus — P2 (interface now, implementation later)

**US-35 (P2) — The host can sequence against EGM-side events.**
- Roughly half of the 57 QSP scenarios need a door opened, a note inserted, a game played or a keyswitch
  turned (marked with a gear in 06 §6). Report 06 §7.4 calls an EGM-side agent *"the single largest missing
  piece"* and explicitly puts it **outside** the SAS host's scope.
- v1 therefore ships the **interface**: a `stimulus "<name>" { ... }` script step and a pluggable agent
  transport (TCP/JSON), plus a `manual` agent that prompts an operator and waits — so a scenario is written
  once and becomes unattended the day an agent exists.

---

## 5. The scripting language

Designed from scratch for this application. Nothing is inherited from `sastest.exe`'s `[Script]` list or
from any other tool.

Name: **SASL** (SAS Scenario Language). File extension `.sasl`. UTF-8, `//` and `/* */` comments.

### 5.1 The format decision — bespoke DSL, YAML, or an embedded general-purpose language?

**Recommendation: HYBRID — a bespoke DSL as the authoring surface, with an embedded Lua escape hatch
(P2) and a lossless JSON serialisation of the compiled AST.**

Reasoning, against the specific evidence:

| Option | Why not (or why only partly) |
|---|---|
| **Pure YAML/JSON** | The 57 scenarios in 06 §6 need sequencing, timing, loops, captures and expressions. In YAML those become deeply nested maps whose leaves are **strings that are secretly expressions** (`"p99(m.latency) < 20ms"`). You end up writing a DSL anyway — but inside string literals, with no parser, no column-accurate error messages and no editor support. YAML also has real footguns for this domain: `01` parses as the integer 1, not an address byte; `$71` needs quoting; the Norway problem. Rejected as the primary surface. |
| **Embedded Python** | Best ecosystem, but adds either CPython (a native dependency that breaks single-file cross-platform publishing) or IronPython 3 (large, less maintained). Also: a general-purpose language makes every test an arbitrary program, which is what makes CI results hard to summarise — you cannot enumerate a run's steps without executing it. |
| **Embedded Lua** | Small, sandboxable, pure-managed via MoonSharp, deterministic. Good *escape hatch*. Poor *primary surface*: a test written in Lua is a program, not a document; assertions become function calls whose expected/actual values must be plumbed into a report by convention; and `--list` / `--lint` become impossible without running the script. |
| **C# scripting (Roslyn)** | Most natural for the app's own maintainers and needs no marshalling layer — but the user is a **C++ engineer, not a C# developer**, and the brief says tests must be writable without being one. Roslyn scripting also costs ~15 MB of dependencies and 1-2 s of first-compile latency per run, and gives a test file the full power of the BCL, which is a sandboxing and reproducibility problem in CI. |
| **Bespoke DSL** | Fits the domain exactly: frames, codes, durations, fields, exceptions and assertions are *first-class syntax*, so the common case is one readable line. It is statically checkable — `sasctl lint` catches an unknown opcode, a wrong parameter name, a type error or a bad field reference **before any I/O**, which is worth a great deal when the alternative is discovering it 40 minutes into a soak. Every step and assertion is a data structure, so `--list`, JUnit output, per-assertion expected/actual and a GUI step-debugger all fall out for free. Cost: we own a lexer, parser, binder and interpreter (~3-4 k lines) plus the editor tooling. |

The hybrid resolves the one real weakness of the bespoke DSL — arbitrary computation — without paying for
a general-purpose runtime in the common path:

1. **DSL (P0)** with a genuine expression sublanguage (arithmetic, comparisons, indexing, field access,
   built-in functions, list/record literals). Requirement: the DSL alone must express **all 57 scenarios**
   in 06 §6 with no escape hatch. That is the acceptance test for the language design.
2. **Lua escape hatch (P2)**, `lua { ... }` blocks and `lua.fn(...)` calls over a whitelisted API, for
   computed payloads and custom predicates. Sandboxed (no `io`, no `os`, no `require`), deterministic seed,
   step-budget capped. If it turns out to be needed in more than ~5 % of tests, the DSL is under-designed
   and that is the signal to extend it.
3. **JSON AST (P0)** — every compiled script serialises losslessly to JSON and back, so scripts can be
   generated by other tools, edited in the GUI, and stored in a run record verbatim.

### 5.2 Grammar (EBNF)

Terminals in `"quotes"`. `{ }` = zero or more, `[ ]` = optional, `|` = alternation.

```ebnf
(* ---------------- file ---------------- *)
script      = { item } ;
item        = use_decl | const_decl | var_decl | profile_decl | proc_decl
            | fixture | suite_decl | test_decl ;

use_decl    = "use" string ;                          (* include another .sasl file *)
const_decl  = "const" ident "=" expr ;                (* file scope, immutable *)
var_decl    = "var" ident "=" expr ;                  (* mutable accumulator *)
profile_decl= "profile" ident "{" { prop } "}" ;
prop        = ident ":" expr ;
proc_decl   = "proc" ident "(" [ params ] ")" block ;
params      = ident [ "=" expr ] { "," ident [ "=" expr ] } ;
fixture     = ( "setup" | "teardown" ) [ "once" ] block ;
suite_decl  = "suite" string [ attrs ] "{" { item } "}" ;
test_decl   = "test" string [ attrs ] block ;

attrs       = { attr } ;
attr        = "tags" "[" ident { "," ident } "]"
            | "timeout" duration
            | "priority" ( "p0" | "p1" | "p2" )
            | "requires" requirement
            | "iterations" int
            | "xfail" string ;                        (* known-broken: inverts the verdict *)
requirement = "feature" ident | "profile" ident | "hardware" ident | "agent" ident ;

(* ---------------- statements ---------------- *)
block       = "{" { stmt } "}" ;
stmt        = connect | disconnect | poll | send | broadcast | receive | wait_for | delay
            | expect | soft_expect | assert_st | capture | let_st | set_st
            | repeat | for_st | if_st | on_rule | snapshot | measure | report_st
            | inject | stimulus | log_st | fail_st | skip_st | call_st | block ;

connect     = "connect" [ ident ] record ;            (* connect link1 { port: "COM12", ... } *)
disconnect  = "disconnect" [ ident ] ;
poll        = "poll" ( "start" [ record ] | "stop" | "once" [ "sync" ] | "burst" expr ) ;
send        = "send" cmd_ref [ arg ] [ "on" ident ] [ "as" ident ] [ "expect" matcher ] ;
broadcast   = "broadcast" ( cmd_ref arg [ "every" duration ] [ "for" duration ] | "stop" ) ;
receive     = "receive" [ "exception" ] [ "as" ident ] [ "timeout" duration ] ;
wait_for    = "wait" "for" event_pat [ "timeout" duration ] [ "as" ident ] ;
delay       = "wait" expr ;                           (* expr must be a duration *)
expect      = "expect" target matcher ;
soft_expect = "soft" expect ;                         (* records the failure, keeps going *)
assert_st   = "assert" expr [ "," string ] ;
capture     = "capture" ident ":=" expr ;             (* recorded in the run record *)
let_st      = "let" ident "=" expr ;                  (* not recorded *)
set_st      = "set" ident "=" expr ;                  (* only for names declared with var *)
repeat      = "repeat" ( expr | "until" expr | "while" expr )
              [ "every" duration ] [ "timeout" duration ] block ;
for_st      = "for" ident "in" expr block ;
if_st       = "if" expr block [ "else" ( if_st | block ) ] ;
on_rule     = "on" event_pat block ;                  (* background, armed for the enclosing scope *)
snapshot    = "snapshot" ( "meters" | "features" | "state" ) [ arg ] "as" ident ;
measure     = "measure" ident block ;                 (* collects latency/timeout samples *)
report_st   = "report" ( "stat" string record | "note" expr | "attach" string ) ;
inject      = "inject" fault [ "for" ( "next" | expr | "all" ) ] ;
stimulus    = "stimulus" string [ arg ] [ "timeout" duration ] ;
log_st      = "log" expr ;
fail_st     = "fail" string ;
skip_st     = "skip" string ;
call_st     = "call" ident [ "(" [ args ] ")" ] ;

cmd_ref     = sascode | ident | "raw" expr ;          (* $74 | AftLockAndStatus | raw h"0174..." *)
arg         = record | expr ;                         (* record literal, or an expr yielding one *)
fault       = "bad_crc" | "truncate" "(" expr ")" | "extend" "(" expr ")" | "wrong_address"
            | "garble" [ "(" expr ")" ] | "drop_response" | "delay_response" "(" duration ")"
            | "bad_length" | "no_ack" | "force_ack" | "line_break" | "line_restore" ;

event_pat   = "exception" ( sascode | ident | "any" )
            | "response" [ cmd_ref ]
            | "timeout"
            | "link" ( "up" | "down" )
            | "stimulus" string ;

(* ---------------- assertions ---------------- *)
target      = "response" | "no" "response" | "ack" | "nack" | "busy"
            | "crc" | "frame" | "length" | "latency"
            | "exception" ( sascode | ident | "any" ) | "no" "exception"
            | "field" string | "enum" string | "status"
            | "meter" ( ident | sascode ) | "meters"
            | "features" | "trace" | "link" | "port" "." ident | "egm" "." ident
            | ident { "." ident } ;                   (* r.transfer_status, m.latency, ... *)

matcher     = rel_matcher
            | "within" duration | "during" duration | "after" duration
            | "exactly" expr [ "during" duration ] | "at" "least" expr | "at" "most" expr
            | "matches" expr | "in" expr | "between" expr "and" expr
            | "ok" | "bad" | "unchanged" | "monotonic" | "consistent" | "empty"
            | "delta" rel_matcher | "in" "order"
            | "not" matcher ;
rel_matcher = [ "==" | "!=" | "<" | "<=" | ">" | ">=" | "~=" ] expr ;  (* ~= approx, with tolerance *)

(* ---------------- expressions ---------------- *)
expr        = or_expr ;
or_expr     = and_expr { "or" and_expr } ;
and_expr    = not_expr { "and" not_expr } ;
not_expr    = [ "not" ] cmp_expr ;
cmp_expr    = add_expr [ ( "==" | "!=" | "<" | "<=" | ">" | ">=" | "in" ) add_expr ] ;
add_expr    = mul_expr { ( "+" | "-" ) mul_expr } ;
mul_expr    = unary { ( "*" | "/" | "%" ) unary } ;
unary       = [ "-" ] postfix ;
postfix     = primary { "." ident | "[" expr "]" | "(" [ args ] ")" } ;
primary     = number | duration | string | hexlit | sascode | bool | "null"
            | ident | list | record | "(" expr ")" ;
args        = expr { "," expr } ;
list        = "[" [ expr { "," expr } ] "]" ;
record      = "{" [ field { "," field } ] "}" ;
field       = ( ident | string ) ":" expr ;

(* ---------------- lexical ---------------- *)
ident       = letter { letter | digit | "_" } ;
number      = dec | hex ;
dec         = digit { digit | "_" } [ "." digit { digit } ] ;
hex         = "0x" hexdigit { hexdigit } ;
sascode     = "$" hexdigit hexdigit ;                 (* $74 - a SAS code, never an integer *)
hexlit      = "h" quote { hexdigit | whitespace } quote ;   (* h"01 74 00 01 90 00" *)
duration    = dec ( "us" | "ms" | "s" | "m" | "h" ) ;
string      = quote { char | escape | interp } quote ;
interp      = "${" expr "}" ;                         (* string interpolation *)
bool        = "true" | "false" | "on" | "off" ;
comment     = "//" ... eol | "/*" ... "*/" ;
```

Notes on deliberate choices:

* `$XX` is **only** a SAS code, never a number and never money — it matches how the team already reads the
  spec and the INI. Money is written `cents(2500)` or `credits(100)`; there is no `$25.00` literal, so the
  sigil never becomes ambiguous.
* Durations are typed. `20ms` is not `20`. A comparison between a duration and a bare number is a
  **compile error**, which removes the classic unit bug from timing tests.
* Bindings are **single-assignment** within a scope (`capture` / `let`); rebinding the same name in the
  same scope is a compile error. Mutation requires an explicit `var` + `set`. Loop variables are fresh per
  iteration. This keeps a trace deterministic and matches the project's immutability preference.

### 5.3 Complete command list

**Link and session**

| Statement | Meaning |
|---|---|
| `connect [name] { port, address, baud, data_bits, stop_bits, wakeup, wakeup_mode, dtr, rts, echo, profile }` | Open a link. `port` accepts a name (`"COM12"`), a stable device id, or `auto` (single match only). |
| `disconnect [name]` | Close a link, flush its trace. |
| `poll start { rate, sync, address, cycle_to, rte }` | Start the general-poll loop. |
| `poll stop` | Stop polling (the link stays open). |
| `poll once [sync]` | One general poll, optionally preceded by the `80` sync. |
| `poll burst N` | N back-to-back general polls at the configured rate. |

**Traffic**

| Statement | Meaning |
|---|---|
| `send <cmd> [args] [on link] [as r] [expect ...]` | Encode, append length/CRC, transmit, await response. Binds `r`. |
| `broadcast <cmd> args [every d] [for d]` / `broadcast stop` | Repeating address-0 broadcast in the background. |
| `receive [exception] [as e] [timeout d]` | Take the next response or exception without sending. |
| `wait for <event> [timeout d] [as e]` | Block until an event: `exception $6F`, `response $74`, `timeout`, `link up|down`, `stimulus "x"`. |
| `wait 500ms` | Sleep (the poll loop keeps running). |
| `inject <fault> [for next\|N\|all]` | Fault injection (see the grammar's `fault`). |
| `stimulus "door.main.open" [args] [timeout d]` | Ask the EGM-side agent to do something (P2; the `manual` agent prompts). |

**State, flow and reporting**

| Statement | Meaning |
|---|---|
| `capture x := expr` | Bind and **record** into the run record (shown in the report). |
| `let x = expr` / `var x = 0` / `set x = x + 1` | Bind (not recorded) / declare mutable / assign. |
| `repeat N [every d] [timeout d] { ... }` | Counted loop, optional pacing and wall-clock cap. |
| `repeat until <cond> [every d] [timeout d] { ... }` | Poll-until loop; exceeding `timeout` is a failure. |
| `repeat while <cond> ...` | Inverse. |
| `for x in <list> { ... }` | Iterate a list. |
| `if <cond> { ... } else { ... }` | Conditional. |
| `on <event> { ... }` | Background rule armed for the enclosing block (generalises `[Exception Handling]`). |
| `snapshot meters as before` / `snapshot features as f` / `snapshot state as s` | Capture a whole subsystem for later diffing. |
| `measure m { ... }` | Collect latency/timeout samples for everything inside; exposes `m.latency`, `m.timeouts`, `m.count`. |
| `report stat "name" { ... }` / `report note expr` / `report attach "path"` | Attach data, notes or files to the test result. |
| `log expr`, `fail "msg"`, `skip "reason"` | Diagnostics and explicit verdicts. |
| `proc name(a, b=default) { ... }` + `call name(1, 2)` | Reusable step sequences. |
| `use "common.sasl"` | Include. |
| `setup [once] { ... }` / `teardown [once] { ... }` | Fixtures (see §5.6). |

**Built-in functions**

`hex(v[,n])`, `bcd(v,n)`, `ascii(s)`, `bytes(...)`, `len(x)`, `int(x)`, `str(x)`, `abs(x)`,
`cents(x)`, `credits(x)`, `date("YYYY-MM-DD")`, `now()`, `elapsed(t)`, `uuid()`, `random([seed])`,
`sum(list)`, `min`, `max`, `mean`, `p50`, `p95`, `p99`, `count`, `jitter`, `crc(bytes)`,
`bit(v,n)`, `bits(v)`, `env("NAME")`, `param("name"[,default])`, `file("path")`, `delta(a,b)`,
`meter(name[,game[,denom]])`, `exceptions([code])`, `feature(name)`.

### 5.4 Complete assertion vocabulary

Every assertion produces a structured record: `{id, kind, target, expected, actual, verdict, frames[],
t_start, t_end, message}`. That record is what the JUnit/JSON reporters and the GUI results pane consume.

**Transport / link**

```
expect link up  |  expect link down  |  expect link up within 5s
expect port.stick_parity                 expect port.latency_timer <= 2ms
expect port.classification in ["native_16550", "ftdi", "qxcom"]
```

**Frame level**

```
expect response                          expect response within 20ms
expect no response                       expect no response during 500ms
expect ack | expect nack | expect busy
expect crc ok | expect crc bad
expect length == 12                      expect frame matches h"01 74 ..."
expect frame decodes                     expect no decode errors
```

**Field level** (`field` by schema name; `enum` compares the decoded label)

```
expect field "Transfer Status" == $00
expect field "Cashable Amount" == cents(2500)
expect field "Asset Number" != 0
expect field "Serial Number" matches "^QX[0-9]{6}$"
expect enum "Game Lock Status" == game_locked
expect field "Meter 1" between 100 and 200
expect r.transfer_status == success           // dotted access on a captured response
```

**Status severity** (from the enum registry, with the severity classes identified in 02 §7 and §9)

```
expect status success                    expect status in [success, pending]
expect status not conformance_violation  // $10-$14 in [Status Messages] fail a run automatically
```

**Exceptions**

```
expect exception $6F within 2s           expect exception $99 exactly 1 during 90s
expect no exception during 10s           expect no exception $70
expect exception any within 500ms        expect exceptions [$11, $12] in order
expect exception queue empty
```

**Meters**

```
expect meter TotalCoinIn delta == 100    expect meter TotalDrop unchanged
expect meters monotonic                  expect meters consistent
      // "consistent" runs the built-in invariants, incl. coin_out + jackpot == won  (02 §16)
expect meter $6F.width == 5
```

**Timing**

```
expect latency < 20ms                    expect p99(m.latency) < 20ms
expect max(m.latency) <= 20ms            expect count(m.timeouts) == 0
expect jitter(poll.interval) < 2ms       expect elapsed(t0) between 9.5s and 10.5s
```

**Configuration / features**

```
expect features == profile qsp_gli11     expect feature component_authentication == false
expect field "SAS Version" == "602"
```

**Trace**

```
expect trace matches baseline "t03"      expect no unanswered polls
expect trace.timeouts == 0
```

**Generic and negative**

```
assert <expr> [, "message"]              expect not <matcher>
soft expect ...                          // collect and continue rather than abort the test
fail "..."                               skip "..."
```

### 5.5 Variable and capture semantics

- **Response object.** `send $74 { ... } as r` binds `r` with: `r.ok`, `r.timeout`, `r.raw` (bytes),
  `r.code`, `r.length`, `r.latency` (duration), `r.crc_ok`, `r.decoded` (bool), `r.severity`,
  `r.fields["Game Lock Status"]` (raw value), `r.enums["Game Lock Status"]` (decoded label), and snake_case
  dotted sugar `r.game_lock_status`. Unknown field names are a **compile-time** error, checked against the
  command's response schema.
- **Capture and reuse across steps** — the thing the legacy tool could never do (02 §12(c)):

  ```
  send $4D { buffer_index: 0 } as v
  capture seq := v.fields["Sequence Number"]
  send $58 { system_id: 1, validation_number: seq }
  ```

- **`capture` is recorded, `let` is not.** Captures appear in the run record and the HTML/JSON report with
  their values, which is how a failing run explains itself.
- **Scoping** is lexical and block-structured. `const` is file scope. Names bound in `setup` are visible to
  every test in the enclosing suite (the setup block executes as the test's prologue, so each test gets its
  own fresh binding). Names bound inside a `repeat` or `for` body die at the end of each iteration.
- **Single assignment** in a scope; use `var`/`set` for accumulators. Shadowing an outer name is a warning
  (an error under `--strict`).
- **Snapshots are values**: `snapshot meters as before` ... `snapshot meters as after` ...
  `expect after.TotalCoinIn - before.TotalCoinIn == 100`, or the sugar
  `expect meter TotalCoinIn delta == 100`, which uses the most recent snapshot pair.
- **Measure blocks** collect sample sets: `m.latency` is a sample set usable with `p50/p95/p99/max/jitter`,
  `m.timeouts` is a list, `m.count` an integer.

### 5.6 Control flow, setup/teardown, background rules

- `repeat N`, `repeat until <cond>`, `repeat while <cond>`, each optionally `every <duration>` (paced) and
  `timeout <duration>` (wall-clock cap; exceeding it is a **failure**, not a silent exit).
- `wait <duration>` sleeps without stopping the poll loop; `wait for <event> timeout <d>` blocks on an
  event and **fails on timeout** unless the event pattern is used in an `expect no ...` form.
- Every `test` has an implicit timeout (default 60 s, overridable per test and per run). Hitting it is a
  failure with the partial trace attached.
- `setup` / `teardown` run per test; `setup once` / `teardown once` run per suite. **`teardown` always
  runs**, including after a failure or an abort, and its own failures are reported separately from the
  test's verdict, so a broken teardown cannot mask or manufacture a pass.
- `on <event> { ... }` installs a background rule for the enclosing block — the generalisation of the legacy
  `[Exception Handling]` hook (02 §15), and the mechanism that lets a test poll normally *and* react:

  ```
  on exception $69 { send $72 { transfer_code: interrogate } as t
                     expect t.transfer_status in [success, pending] }
  ```

  Rules fire on the engine thread, are serialised with script steps, and every firing is traced.

### 5.7 Pass/fail and the exit-code contract for CI

**Verdicts:** `pass` · `fail` (an assertion failed) · `error` (could not run: transport, agent, encode
error) · `skip` (`skip`, or an unmet `requires`) · `xfail` (declared `xfail`, failed as expected) ·
`xpass` (declared `xfail`, unexpectedly passed — **counts as a failure**, so known-broken tests get closed).

**A test fails on the first hard `expect`** unless it is `soft expect`, which records and continues.
`assert` is hard. `fail` and `skip` are explicit.

**Exit codes (`sasctl run`):**

| Code | Meaning |
|---|---|
| `0` | Every test passed (skips and xfails allowed). |
| `1` | At least one test **failed** an assertion (or xpassed). |
| `2` | **Execution error** — port could not be opened, link never came up, EGM unreachable at setup, stimulus agent unavailable, teardown error. No verdict could be established. |
| `3` | **Script error** — syntax, unknown opcode, unknown field, type error, schema violation. Detected by `sasctl lint` **before any I/O**, so CI can gate a PR without hardware. |
| `4` | Run aborted — global `--timeout` exceeded, or interrupted (SIGINT / Ctrl-C). Partial results are still written. |
| `5` | Internal error (a bug in the tool). Always accompanied by a crash-report path. |

`--strict` promotes warnings (shadowed names, unused captures, best-effort transport, undecoded frames) to
failures. `--report junit:...` writes standard JUnit XML (one `<testsuite>` per suite, one `<testcase>` per
test, `<failure>` carrying expected/actual and the frame excerpt, `<system-out>` carrying the captures);
`--report json:...` writes the full run record; `--trace <dir>` writes one trace file per test, named in
the report so a CI artifact links straight to the bytes.

### 5.8 Four worked examples, taken from the QSP scenarios

These are implementations of scenarios **T03, T29, T42 and T49** from `06-qsp-test-needs.md` §6.

#### Example 1 — T03: every long-poll response inside 20 ms (SAS 6.02 §2.3.1)

```
// t03_turnaround.sasl
suite "SAS 6.02 2.3.1 - long-poll turnaround" tags [timing, conformance] {

  const PROBES = [
    { cmd: $1A, args: {} },                                   // send current credits
    { cmd: $11, args: {} },                                   // total coin in
    { cmd: $12, args: {} },                                   // total coin out
    { cmd: $15, args: {} },                                   // games played
    { cmd: $1F, args: {} },                                   // machine id and information
    { cmd: $54, args: {} },                                   // SAS version + serial
    { cmd: $55, args: {} },                                   // selected game number
    { cmd: $2F, args: { game: 0, meters: [$00] } },            // selected meters for game n
    { cmd: $6F, args: { game: 0, meters: [0x0000, 0x000D] } }, // extended meters
    { cmd: $B5, args: { game: 1 } }                            // extended game n information
  ]

  setup {
    connect link1 {
      port:      param("port", "COM12"),
      address:   param("address", 1),
      baud:      19200,
      stop_bits: 1,
      wakeup:    on,
      profile:   param("profile", "qsp-gli11")
    }
    expect port.stick_parity
    expect port.latency_timer <= 2ms        // a 16 ms FTDI default fails here, loudly, not silently
    poll start { rate: 200ms }
    expect link up within 5s
    snapshot features as f
    expect features == profile "qsp-gli11"  // do not measure a machine you cannot identify
  }

  teardown { poll stop  disconnect link1 }

  test "T03 - p99 and max turnaround under 20 ms" priority p0 timeout 20m {
    for p in PROBES {
      measure m {
        repeat 500 every 200ms {
          send p.cmd p.args as r
          expect r response within 20ms     // hard fail on ANY sample over budget
          expect r crc ok
          expect r frame decodes
        }
      }
      report stat "lp_${hex(p.cmd)}" {
        n:   m.count,        p50: p50(m.latency), p95: p95(m.latency),
        p99: p99(m.latency), max: max(m.latency), timeouts: count(m.timeouts)
      }
      expect count(m.timeouts) == 0
      expect p99(m.latency)   <  20ms
      expect max(m.latency)   <= 20ms
    }
  }
}
```

*Why this shape:* 06 T03 calls this *"the single highest-value test in the list"*, and
`progressive_sas.log` already shows 29 ms and 46 ms turnarounds against a 20 ms budget (06 §4.3). The
legacy tool cannot express any of it — no loop, no measurement better than ~15 ms, no verdict (W4, W5).

#### Example 2 — T29: AFT lock request, status progression and timeout

```
// t29_aft_lock.sasl
suite "AFT lock (LP 74)" tags [aft] {

  setup {
    connect link1 { port: param("port"), address: param("address", 1), profile: "qsp-gli11" }
    poll start { rate: 200ms }
    expect link up within 5s
    expect feature advanced_funds_transfer          // from LP A0, not from conf.ini  (W15)

    send $73 { registration_code: read_status } as reg   // must be registered first
    if reg.enums["Registration Status"] != gm_registered {
      send $73 { registration_code: register_gaming_machine,
                 asset_number:      param("asset", 0x3041AB00),
                 registration_key:  param("regkey", h"98765432101234569876543210123456"),
                 pos_id:            0x11223344 } as rr
      expect rr response within 20ms
      wait for exception $6D timeout 30s            // AFT registration acknowledged
    }
  }

  teardown {
    send $74 { lock_code: cancel_lock, lock_condition: 0x00, lock_timeout: 0s }
    poll stop  disconnect link1
  }

  test "T29 - lock is granted, reported, and self-releases at the timeout" priority p0 timeout 90s {
    send $74 { lock_code: request_lock, lock_condition: 0x01, lock_timeout: 10s } as lk
    expect lk response within 20ms
    expect lk crc ok
    expect lk.enums["Game Lock Status"] in [game_lock_pending, game_locked]

    wait for exception $6F timeout 5s                // Game locked

    repeat until last.$74.enums["Game Lock Status"] == game_locked every 200ms timeout 5s {
      send $74 { lock_code: interrogate } as s
      expect s response within 20ms
    }
    capture t_locked := now()

    // IGT Advantage will not send LP 72 unless LP 74 says GLS_GAME_LOCKED  (06 T29)
    expect last.$74.enums["Available Transfers"] not empty

    repeat until last.$74.enums["Game Lock Status"] == game_not_locked every 200ms timeout 15s {
      send $74 { lock_code: interrogate } as s
      expect s response within 20ms
    }
    capture lock_held := elapsed(t_locked)
    expect lock_held between 9.5s and 10.5s          // lock_timeout is hundredths of a second

    // and a cancel releases immediately
    send $74 { lock_code: request_lock, lock_condition: 0x01, lock_timeout: 60s } as lk2
    wait for exception $6F timeout 5s
    capture t2 := now()
    send $74 { lock_code: cancel_lock, lock_condition: 0x00, lock_timeout: 0s } as cx
    expect cx response within 20ms
    repeat until last.$74.enums["Game Lock Status"] == game_not_locked every 200ms timeout 3s {
      send $74 { lock_code: interrogate }
    }
    expect elapsed(t2) < 2s
  }
}
```

*Why this shape:* 06 T29 requires the pending-to-locked progression, exception `$6F`, release at
`timeout/100` seconds and an immediate cancel — none of which the legacy tool can assert. The lock timeout
is entered as `10s` and the codec emits the 2-byte BCD hundredths (US-9).

#### Example 3 — T42: linked progressive, per-level 5 s staleness (SAS 6.02 §10.2)

```
// t42_progressive_levels.sasl
suite "Linked progressives" tags [progressive] {

  const L1 = { level: 1, amount: cents(1_500_000) }
  const L2 = { level: 2, amount: cents(  250_000) }

  setup {
    connect link1 { port: param("port"), address: param("address", 1), profile: "qsp-gli11" }
    poll start { rate: 200ms }
    expect link up within 5s
    expect feature multiple_sas_progressive_win_reporting
  }
  teardown { broadcast stop  poll stop  disconnect link1 }

  test "T42 - a stale level locks up, and only that level's broadcast clears it" priority p0 timeout 3m {

    // 1. both levels fresh for 30 s -> no timeout
    broadcast $86 { group: param("group", 1), levels: [L1, L2] } every 2s for 30s
    expect no exception $53 during 30s

    // 2. starve level 2 only -> exception 53 inside the 5 s window
    broadcast stop
    broadcast $86 { group: param("group", 1), levels: [L1] } every 2s
    wait for exception $53 timeout 7s as ex
    capture t_stale := now()

    // 3. keep broadcasting level 1 only: the lockup must NOT clear
    wait 15s
    expect egm.condition "progressive_timeout" == true      // via the EGM agent, when present
    expect no exception $53 during 15s                      // reported once, not repeatedly

    // 4. resume level 2 -> the lockup clears
    broadcast stop
    broadcast $86 { group: param("group", 1), levels: [L1, L2] } every 2s for 20s
    repeat until egm.condition "progressive_timeout" == false every 500ms timeout 10s { }
    capture recovery := elapsed(t_stale)
    report stat "progressive" { time_to_recovery: recovery }
  }

  test "T43 - group 0 disables the timeout entirely" priority p1 timeout 90s {
    requires agent egm
    stimulus "config.progressive_group_id" { value: 0 }
    broadcast stop
    expect no exception $53 during 60s
    expect egm.condition "progressive_timeout" == false
  }
}
```

*Why this shape:* 06 T42 is explicit that SAS 6.02 §10.2 is **per level**, and that clearing the lockup on
the first broadcast of *any* level would unlock a machine still showing a frozen jackpot. The two
`expect egm.condition` lines are the only ones needing the P2 agent; every other line is wire-only.

#### Example 4 — T49: LP `$2E` "delay game" must always be answered (a defect already in a capture)

```
// t49_game_delay.sasl
suite "LP 2E - delay game" tags [regression, conformance] {

  setup {
    connect link1 { port: param("port"), address: param("address", 1), profile: "qsp-gli11" }
    poll start { rate: 200ms }
    expect link up within 5s
  }
  teardown { send $2E { delay: 0ms }  poll stop  disconnect link1 }

  test "T49 - 100 consecutive LP 2E are all answered" priority p0 timeout 3m {
    // progressive_sas.log: 36 sends of 01 2E 00 00 9B 0F, 36 "Timeout, EGM poll response",
    // zero responses, while LP 1A/55/53 answered on the same link at the same time (06 4.3).
    measure m {
      repeat 100 every 200ms {
        send $2E { delay: 0ms } as r
        expect r response within 20ms
        expect r ack
      }
    }
    expect count(m.timeouts) == 0
    report stat "lp_2E" { p99: p99(m.latency), max: max(m.latency), timeouts: count(m.timeouts) }
  }

  test "T49b - a 5 s delay actually holds the next game for 5 s" priority p1 timeout 60s {
    requires agent egm
    stimulus "game.end"
    send $2E { delay: 5s } as d
    expect d ack
    capture t0 := now()
    stimulus "game.start" timeout 1s
    expect stimulus.rejected
    repeat until egm.state "can_start_game" == true every 100ms timeout 8s { }
    capture held := elapsed(t0)
    expect held between 4.8s and 5.5s          // monotonic clock: LP 7F must not move it (06 T49)
  }

  test "T49c - delay 0 cancels a running delay" priority p1 timeout 60s {
    requires agent egm
    send $2E { delay: 10s } as d1
    expect d1 ack
    wait 1s
    send $2E { delay: 0ms } as d2
    expect d2 ack
    repeat until egm.state "can_start_game" == true every 100ms timeout 2s { }
  }
}
```

*Why this shape:* 06 T49 says of this scenario, *"A reproducible, already-captured, unreported defect.
This scenario alone justifies the tool."* The first test is pure wire and runs today with no agent.

---

## 6. Architecture

### 6.1 Layering

```
+------------------------------------------------------------------------------+
|  Sas.App.Desktop       Avalonia views, XAML, styles. No protocol logic.       |
+------------------------------------------------------------------------------+
|  Sas.App               ViewModels (MVVM). Subscribes to engine event streams; |
|                        issues engine commands. Knows nothing about serial.    |
+-------------------------------+----------------------------------------------+
|  Sas.Scripting                |  sasctl (headless CLI)                        |
|   lexer/parser/binder/AST     |   argument parsing, reporters, exit codes      |
|   interpreter, assertions     |   references Engine + Scripting ONLY           |
|   reporters (JUnit/JSON/HTML) |   (never Avalonia - must run on a build agent) |
+-------------------------------+----------------------------------------------+
|  Sas.Engine            Link session + poll loop (one dedicated thread each),  |
|                        exception log, meter model, AFT/TITO/progressive state,|
|                        event-rule engine, trace writer/reader, fault injector.|
+------------------------------------------------------------------------------+
|  Sas.Protocol          Frame codec (encode/decode), SAS CRC, BCD, command and |
|                        message schemas, enum registry, meter table, exception |
|                        registry, help store, legacy INI importer.             |
+------------------------------------------------------------------------------+
|  Sas.Transport         ISasLink + backends: ManagedSerial (System.IO.Ports),  |
|                        NativeSerial (kernel32 / termios+CMSPAR), Loopback,    |
|                        TraceReplay. Port discovery + capability probing.      |
+------------------------------------------------------------------------------+
|  Sas.Abstractions      Records, interfaces, enums. No dependencies at all.    |
+------------------------------------------------------------------------------+
```

Dependency rule: arrows point **down only**. `Sas.Protocol` never references `Sas.Transport`
(a codec that cannot touch a port is a codec you can fuzz). `Sas.Engine` never references `Sas.App`.
`Sas.Scripting` never references Avalonia. Enforced by an architecture test in CI.

### 6.2 Solution / project layout

```
SasBench.sln
  src/
    Sas.Abstractions/          netstandard2.1 - records, interfaces, no deps
    Sas.Protocol/              net8.0         - codec, schemas, CRC/BCD, INI importer
    Sas.Transport/             net8.0         - ISasLink, backends, discovery
    Sas.Transport.Windows/     net8.0-windows - WMI/registry discovery, kernel32 backend
    Sas.Engine/                net8.0         - sessions, poll loop, state, traces
    Sas.Scripting/             net8.0         - SASL compiler + interpreter + reporters
    Sas.App/                   net8.0         - view-models, services
    Sas.App.Desktop/           net8.0         - Avalonia 11, Fluent theme
    sasctl/                    net8.0         - headless runner, AOT-friendly
  data/
    schemas/commands.json  messages.json  enums.json  meters.json  exceptions.json
    schemas/*.schema.json                     # JSON Schema for each of the above
    help/$XX.md                               # code-keyed help topics
    profiles/qsp-gli11.json  conf-ini.json
    scenarios/T01...T57.sasl                  # the QSP scenario library (P2 to complete)
  tests/
    Sas.Protocol.Tests/  Sas.Engine.Tests/  Sas.Scripting.Tests/
    Sas.Transport.Tests/ Sas.Architecture.Tests/  Sas.E2E.Tests/   # loopback + FakeEgm
  tools/
    schemagen/            # reads libsas headers READ-ONLY, emits schema stubs (6.6)
```

Target `net8.0` (LTS, installed here as 8.0.30) with a `net10.0` build validated in CI (10.0.11 is also
present, 05 §3). Avalonia 11.x. **No dependency on modifying libsas or QSP** — the app links nothing from
either; it drives them over the wire. Nothing under `C:\Progetti\libsas\utils` is referenced.

### 6.3 Threading model for the poll loop

The deadlines are soft-real-time but tight: SAS 6.02 §2.3.2 gives **5 ms** between bytes (first-party,
quoted in `libsas_uart.cpp:201-207`) and §2.3.1 gives **20 ms** for a whole response
(`libsas_internal.h:222`). One byte at 19200 8-N-1 is 573 us. The plan below is report 05 §3.5's, adopted
in full.

- **One dedicated `Thread` per link** ("link thread"), `IsBackground = false`,
  `ThreadPriority.AboveNormal` — deliberately **not** `Highest` or `RealTime`; the legacy tool raises
  process and thread priority and its own help file still blames Windows scheduling, and over-elevating
  starves the UI (05 §3.5). The link thread owns the port handle exclusively.
- **Never the thread pool.** Thread-pool injection delay under load is ~500 ms — 100x the inter-byte
  budget.
- **`timeBeginPeriod(1)`** for the process on Windows (per-process since Win10 2004, so it is safe).
  Deadlines computed from `Stopwatch.GetTimestamp()`; the last ~500 us before a deadline is spun with
  `SpinWait` rather than slept, because `Sleep(1)` cannot resolve it.
- **Zero allocation on the hot path.** Pre-allocated frame buffers, `ArrayPool<byte>`, `Span<byte>`, no
  string formatting in the loop (formatting happens on the trace/UI side). Workstation concurrent GC
  (`ServerGarbageCollector=false`, `ConcurrentGarbageCollection=true`, `TieredPGO=true`). A gen-0 GC is
  ~100 us to 1 ms and survivable; a gen-2 pause is not, so the loop must not create garbage.
- **Commands in** via a bounded `Channel<LinkCommand>` (capacity 256, `SingleReader = true`).
  **Events out** via an unbounded `Channel<TraceEvent>` (`SingleWriter = true`) with two consumers: the
  trace writer (its own thread, batched file I/O) and the UI aggregator.
- **UI marshalling** is coalesced: `Dispatcher.UIThread.Post` at ~30 Hz with batched updates, never per
  byte, never `Dispatcher.Invoke` from the link thread.
- **The script engine runs on its own thread.** Each step posts a `LinkCommand` and awaits a
  `TaskCompletionSource` completed by the link thread. The script thread never touches the port and never
  blocks it; a slow assertion cannot delay a poll.
- **Reads** use `BaseStream.ReadAsync` (managed backend) or overlapped I/O with a dedicated event (native
  backend). `SerialPort.DataReceived`, `BytesToRead`, `Read` and `ReadLine` are **banned** — they are
  documented as unreliable, error-swallowing and synchronous (05 §3.1).
- **`COMMTIMEOUTS.ReadIntervalTimeout = 5 ms`** on the native backend, so the driver enforces the SAS
  inter-byte deadline instead of the app polling for it.
- **Timestamps** are always QPC (`Stopwatch.GetTimestamp`), never `DateTime.Now` and never `GetTickCount`
  (15.6 ms granularity — which is what the legacy tool uses for its response-time display, W5).

### 6.4 How the headless runner shares the engine with the GUI

There is **one** engine and **one** script interpreter; only the presentation differs.

- `sasctl` references `Sas.Engine` + `Sas.Scripting` and nothing else. It constructs a `LinkSession`,
  a `ScriptRunner`, and one or more `IRunReporter`s (console, JUnit, JSON), and streams `RunRecord` events
  to them.
- The GUI's "Run script" button constructs the **same** `ScriptRunner` over the **same** already-open
  `LinkSession` and streams the **same** `RunRecord` events into a results pane — so the GUI is a reporter,
  not a second implementation.
- The GUI can additionally **step** a script (the interpreter exposes a step-boundary hook), highlight the
  current statement and show live captures. That is a debugging affordance on top of the shared engine, not
  a fork of it.
- **CI enforces the equivalence**: a smoke script is run headless and again through the GUI's automation
  entry point against the loopback transport, and the two `RunRecord` JSON documents must match modulo
  timestamps. If the two front-ends ever diverge, that test fails.

### 6.5 The wake-up bit / MARK-SPACE mechanism — can .NET do it?

**What the reports establish (05 §3.1, measured on this machine, .NET 10.0.11, Windows 11 26200):**

* `Parity.Mark` and `Parity.Space` **exist and work on Windows** — they map to `DCB.Parity = 3/4`, and
  switching parity on an already-open port is allowed and costs **~38 us** per `SetCommState`.
* `SerialPort.BaseStream.Flush()` maps to **`FlushFileBuffers`**, which for a comm handle *"will not return
  until all pending write operations have been transmitted"* — measured at **~2.9 us** of API overhead.
  **This is the exact barrier `sastest.exe` omits** (W7).
* **COM10+ works**, provided you pass the plain name: `SerialPort` builds `\\?\COM12` itself, and
  hand-built `\\.\COM12`, `\\?\COM12` and `COM12:` are all **rejected** with `ArgumentException`.

So the transmit primitive is:

```csharp
// address byte alone, MARK parity, then DRAIN before touching the line control
port.Parity = Parity.Mark;
stream.Write(frame[..1]);
stream.Flush();                  // FlushFileBuffers - returns only when the byte is on the wire
port.Parity = Parity.Space;
stream.Write(frame[1..]);
stream.Flush();
```

`sastest.exe` replaces `stream.Flush()` with `Sleep(2)`. Report 05's words: *"That one substitution is the
difference between correct and hopeful."* The native backend belt-and-braces the drain with
`SetCommMask(EV_TXEMPTY)` + `WaitCommEvent`.

**Confidence, quoted from report 05 §6:**

* *"`sastest.exe` cannot open COM10+, and `Port = 11` is out of bounds"* — **VERY HIGH (99 %)**.
* *"Naive per-byte MARK/SPACE switching over USB is unreliable"* — **HIGH (~90 %)**, grounded in the
  two-pipe argument, the FTDI control-URB glitch reports and FTDI's buffered-writes documentation, but
  **not measured on hardware — no USB-serial adapter is attached to this machine**.
* *"A properly written host (drain-then-switch, 1 ms latency timer, genuine FTDI) can drive SAS over USB"*
  — **MEDIUM-HIGH (~75 %)**, and *"must be validated against real hardware before it is promised to
  anyone."*

**The Linux trap — a must-fix, not a nicety.** `System.IO.Ports` on Linux does **not** implement `CMSPAR`:
`pal_termios.c` sends `Parity.Mark` / `Parity.Space` into the `default:` branch, which **silently disables
parity — no exception, no warning** (05 §3.1). The wake-up bit would simply never be transmitted and the
app would look like it was working. Therefore: on Linux the app **must** use the native `termios` +
`CMSPAR` backend, or **refuse** to enable wake-up bits with an explicit error. Silently no-oping is
forbidden, and there is a unit test asserting that the managed backend reports
`SupportsStickParity == false` on Linux.

**Contingency ladder if the boundary cannot be held on the user's hardware:**

1. **Native 16550 (PCIe/ExpressCard) or Quixant QXCOM** — the *supported* path. QXCOM does true 9-bit in
   the kernel (`IOCTL_COM_EXTEND_DATABITS`), one I/O instruction from the transmit register (05 §2.2). If
   the app is given a QXCOM device it uses true 9-bit and the parity trick disappears entirely.
2. **Genuine FTDI FT232R/FT232H with `LatencyTimer = 1` + drain-then-switch** — promoted to *supported*
   only if Milestone 1 measures it holding at 19200. Otherwise it stays *best-effort*.
3. **FTDI D2XX backend (P2)** — `FT_SetDataCharacteristics` + `FT_SetLatencyTimer`, bypassing the VCP
   driver, if the VCP path proves marginal.
4. **Wake-up-less mode** — the legacy tool already has this escape hatch (the `Wake Up Bits` checkbox, the
   `else` branch at `0x00413A17`). Whether the target EGM tolerates it is **UNKNOWN and machine-dependent**
   (05 §7), so it is offered with a warning and recorded in the run record.
5. **Honest labelling** — a documented supported-hardware list, a capability banner on every session, and
   `best_effort_transport: true` stamped into every run record produced on one. A green suite on dubious
   hardware must never look identical to a green suite on good hardware.

What the app will **never** do: silently drop the wake-up bit, silently retry until it works, or claim a
capability it has not probed.

### 6.6 How the app is tested — no hardware required

- **`LoopbackTransport`** — two in-memory ring buffers of **9-bit units** (payload in bits 0-7, wake-up in
  bit 8). That representation is deliberately identical to libsas's own
  `struct { unsigned short Payload : 8; unsigned short Wakeup : 1; }` (`libsas_db.h:1081-1090`), so the
  app's framing is validated against the same model the EGM uses. It supports injectable latency, jitter,
  byte loss and parity/framing errors, so timeout and CRC paths are testable deterministically.
- **`FakeEgm`** — a schema-driven responder in `Sas.Engine.Tests`: configurable feature bits, an exception
  queue with implied-ACK semantics, a meter model, AFT/TITO/progressive state and a configurable response
  latency distribution. It lets every script, every assertion and the whole run-record pipeline be tested
  with zero hardware, and it lets the T01-T57 scenario library be written and regression-tested *before*
  hardware exists. (Report 04 §6 shows libsas's own black-box suite does exactly this with an in-memory
  mock bus and the **public** `sasSetSerialCallbacks` entry point — that proves the shape is sound. We
  write our own in C#; we do not link or copy the C++ one, and we do not modify libsas.)
- **Golden vectors** — real frames with verified CRCs, taken from the decoded captures: the meter-sweep
  records in `TXRXData.dat` (every multi-byte frame validates CRC, 03 §4.2) and the LP `$53` response in
  `progressive_sas.log` containing ASCII `QXGam...Game159300` (06 T51). Decoder round-trip tests run
  against these.
- **Property tests** — encode/decode/encode is the identity for every command in the schema; every
  `[Messages]` descriptor imported from the legacy INI either decodes its sample frame or is reported as a
  defect (US-28).
- **Architecture tests** — the dependency rule in §6.1 is asserted in CI.
- **Two-front-end equivalence test** — §6.4.
- **Optional hardware ring** — a nightly job on a bench machine with a real EGM runs the P0 scenario
  subset; it is a *separate* CI lane so the hardware-free suite always gates PRs.

---

## 7. Open questions for the user

**Q1 — Which serial hardware do we commit to supporting, and can we get one on the bench this week?**
This gates the riskiest assumption in the project (W12) and therefore Milestone 1.
- (a) Native 16550 (PCIe/ExpressCard card, or a dock) — *supported*, lowest risk, needs hardware.
- (b) Genuine FTDI FT232R/FT232H USB adapter with `LatencyTimer = 1` — *best-effort until measured*.
- (c) Quixant QXCOM board — true 9-bit in the kernel, no parity trick at all, but only on Quixant hardware.
- (d) Ship wake-up-less operation only, and require the EGM to tolerate it.

**Recommendation: (a) + (b) together.** Buy one genuine FTDI FT232H (about 20 GBP) and one native-16550
card, run the M1 spike against a real EGM, and let the measurement decide which is labelled *supported* and
which *best-effort*. (c) becomes automatic if a QXCOM device is present. (d) is a fallback, not a plan —
whether the EGM tolerates it is UNKNOWN (05 §7).

**Q2 — What is the fate of the legacy INI: faithful import, or curated catalogue? (And do we drop Pulltab/RNG?)**
- (a) **Faithful import** of all 1735 lines including its defects, with a validation report (US-28).
- (b) **Curated built-in catalogue** generated from `libsas.h` / `libsas_wrapper.h` (118 LP codes, 196
  meter codes, 122 exception codes are all enums in the headers, 04 §2/§4/§5), so the schema can never
  drift from the library — with the INI importable on top for anything the headers do not carry.
- (c) Both: ship (b) as the baseline, import (a) as an overlay, diff them and show the discrepancies.

**Recommendation: (c).** The headers are the ground truth for what libsas actually implements; the INI is
the ground truth for what the team's tests currently assume. The diff between them is itself a valuable
finding — 22 long polls in the INI have no decoder, and `$72`/`$6E`/`$B0` are missing from the INI's
command lists entirely (02 §5.1). **Sub-question: drop F94/F95 (Pulltab/RNG)?** Recommendation: drop them —
both tabs ship hidden, the INI sections are empty and the `DD2K` format is UNKNOWN — but confirm before
cutting, since only you know whether anyone still uses them.

**Q3 — Is the EGM-side stimulus agent in scope, and if so who builds it?**
Roughly half the 57 scenarios need a door opened, a note inserted or a game played; report 06 §7.4 calls it
*"the single largest missing piece"* and puts it outside the SAS host's scope.
- (a) Out of scope for v1: ship the `stimulus` step + a `manual` agent that prompts an operator.
- (b) In scope: build a small QSP-side agent (TCP/JSON) exposing door / note / game / keyswitch / config
  actions.
- (c) Reuse the existing peripheral test rig (`gds_tester.exe` + `gds_tester_cmd.txt`,
  `doc\Peripheral_Simulator_Tests.md`) as the transport.

**Recommendation: (a) for v1, with the protocol defined now.** Roughly 25 of the 57 scenarios (T03, T08,
T09, T49a, T51, T52, the meter reads, the AFT wire behaviour) are **pure wire and run unattended today** —
already far beyond anything achievable now. Then decide (b) vs (c) with QSP's owners.

**Q4 — Windows-only for v1, or cross-platform from day one?**
- (a) Windows-only v1; Linux later. Fastest to a working tool; the wake-up path is proven on Windows.
- (b) Windows + Linux day one. Needed if EGMs are bench-tested from Linux hosts — QSP's own reference
  configuration names `/dev/ttyS5` (06 §3.2), so Linux is clearly in use somewhere.
- (c) Windows-only GUI, but `sasctl` cross-platform from day one, so CI can run headless on Linux agents.

**Recommendation: (c).** Avalonia gives cross-platform essentially free for the GUI, but the *transport*
does not come free: `System.IO.Ports` on Linux silently no-ops `Parity.Mark`/`Space` (05 §3.1), so Linux
needs the native `termios`+`CMSPAR` backend written and tested. Ship (c), keep the Linux backend behind a
capability probe from M2, and promote to (b) once it is measured. If your CI agents are Windows, say so and
this collapses to (a).

---

## 8. Risk register

| # | Risk | Severity | Evidence | Mitigation |
|---|---|---|---|---|
| R1 | **The wake-up bit cannot be held over the user's USB adapter**, so the new tool fails on exactly the machine the old one fails on. | **Critical** | 05 §2.3, §6: HIGH (~90 %) that the naive mechanism breaks on USB; MEDIUM-HIGH (~75 %) that drain-then-switch fixes it; **not measured, no adapter on this machine** (05 §4). | **Milestone 1 is nothing but this experiment** (§9). Drain-then-switch + `EV_TXEMPTY`; latency-timer fix; capability probing; the 5-step contingency ladder (§6.5); honest `best_effort_transport` labelling. Decide the supported-hardware list from measurement, not hope. |
| R2 | **`System.IO.Ports` silently disables parity on Linux**, so wake-up bits vanish with no error and the app looks fine. | **Critical** (if Linux is in scope) | `pal_termios.c` `default:` branch, read directly (05 §3.1); `ParityReplace` likewise unimplemented (dotnet/runtime#110349). | Native `termios`+`CMSPAR` backend on Linux; the managed backend reports `SupportsStickParity == false` there; a unit test asserts it; the app **refuses** rather than no-ops. |
| R3 | **The app cannot measure the 20 ms budget accurately enough to be believed** — its own stack adds latency it cannot separate from the EGM's. | High | 20 ms budget (`libsas_internal.h:222`); observed 29 ms / 46 ms turnarounds; a 10 ms driver regression documented in `.docs\qxcom-serialread-latency.patch` (06 §4.3). | Loopback calibration on every run: measure the app's own TX-to-RX floor and report it alongside every timing stat; use `EV_TXEMPTY` and `ReadIntervalTimeout=5ms` so the driver, not the app, marks byte boundaries; state clearly that sub-millisecond claims need a hardware line-tap. |
| R4 | **Poll-loop jitter from GC or scheduling** produces false failures in timing tests. | High | 05 §3.5: gen-2 pauses exceed 5 ms; thread-pool injection delay ~500 ms. | Dedicated `AboveNormal` thread, zero allocation on the hot path, workstation concurrent GC, `timeBeginPeriod(1)`, spin-wait for the last 500 us; the app **displays its own jitter** so a bad host is visibly distinguished from a bad EGM; timing tests record host jitter alongside the verdict. |
| R5 | **Schema authoring is bigger than it looks** — 118 commands with typed parameters, 395 response descriptors, 196 meter codes, 122 exceptions, 8 enum tables, plus help. | High | 02 §17 (section sizes); 04 §2/§4/§5 (libsas enums). | Generate the skeleton from `libsas.h` / `libsas_wrapper.h` (read-only, `tools/schemagen`) and overlay the imported INI; validate with JSON Schema in CI; treat the schema as a shipped, versioned artefact with its own tests and golden vectors; prioritise the ~30 codes the QSP scenarios actually need for P0 and backfill the rest. |
| R6 | **Importing the legacy INI imports years of silent mis-decodes** — positional offsets nobody has ever verified. | Medium-High | 03 §7 explicitly warns of this; 02 §8.4 shows `$6F`/`$AF` running off the end of short frames by design. | The importer validates rather than trusts: bounds-check every descriptor, cross-check against the header-derived catalogue (Q2c), and mark any imported decoder that disagrees as `unverified` until a golden frame confirms it. |
| R7 | **The tool is built and not adopted**, because the team's muscle memory and 1735-line config live in the old one. | Medium-High | 03 §7: *"any new tool that cannot import [sastest.ini] will not be adopted"*; the GID proves the tool is in live use in `C:\Quixant_Activity\SAS\SAS_Server_tool\` (03 §3). | Day-one INI import (US-28) and legacy capture import (US-24); keep the two-pane hex+English view as a first-class layout, not a nostalgia mode; ship the Quick Commands and Meter Test the team already uses as working examples; get the M1 spike in front of the user on their own bench. |
| R8 | **Half the scenario library cannot run unattended** without an EGM-side agent that does not exist. | Medium-High | 06 §7.4, §8. | Q3: ship the `stimulus` interface + `manual` agent in v1; prioritise the ~25 wire-only scenarios; make the agent's absence a `skip` with a clear reason, never a silent pass. |
| R9 | **libsas is pinned to a branch, not a tag** (`IGS-226-accounting-denomination`), so EGM behaviour can change under a passing suite. | Medium | 06 §3.1, §8 (`CMakeLists.txt:122`). | Record libsas/QSP/QPM versions and the EGM's `$A0`/`$54`/`$1F` answers in every run record; fail a run whose profile reconciliation changed since the baseline; make version drift visible in the report header. |
| R10 | **Scope creep from 57 scenarios and 95 legacy features** turns v1 into a two-year project. | Medium | §2 (95 features), 06 §6 (57 scenarios). | Strict P0/P1/P2 in §4; M1-M4 deliver a *usable* tool before any workbench polish; the parity checklist is a checklist, not a sprint plan; F94/F95 proposed for drop (Q2). |
| R11 | **Fifteen `SAS_CONF_*` libsas parameters run at defaults nobody has read**, so the tool may assert behaviour that is configurable and unconfigured. | Medium | 06 §3.4, §9 — explicitly UNKNOWN. | Every scenario states the EGM configuration it assumes; the run record captures it; where a behaviour is configurable, the test asserts *both* settings or declares which one it assumes. Read the defaults out of the libsas source as an early task. |
| R12 | **Real-money paths move real NVRAM meters**, so a rerun is not idempotent. | Medium | 06 §8. | Snapshot/restore where possible; require and **record** a memory clear between money runs; mark money-moving tests with a `destructive` tag that CI must opt into. |
| R13 | **The developer machine has no usable serial hardware at all** — no native 16550, no QXCOM, no FTDI; only Bluetooth virtual ports and a phantom FutureLogic CDC entry. | Medium | 05 §4, measured. | The loopback transport + `FakeEgm` make everything except R1 developable without hardware; R1 is exactly why M1 is first and needs a bench slot with a real EGM. |

---

## 9. Delivery plan

Each milestone states what it delivers and how it is demonstrated. **M1 is the smallest thing that proves
the riskiest assumption (R1) — nothing else is built until it has an answer.**

### M1 — "Wire Spike": prove the wake-up bit and the timing budget on real hardware

*Target: 1 week plus hardware lead time. ~600 lines, console only, thrown away or folded into
`Sas.Transport`.*

**Delivers**
- Port enumeration with friendly names (WMI + `SERIALCOMM` + `ComDB`), including COM10+ (US-1).
- Port classification and latency-timer read/fix for FTDI and CP210x (US-2).
- The transmit primitive: MARK, write address byte, **`FlushFileBuffers`**, SPACE, write body, flush —
  with an `EV_TXEMPTY` cross-check on the native backend (§6.5).
- A poll loop on a dedicated `AboveNormal` thread with `timeBeginPeriod(1)` and QPC timestamps, polling
  `0x80`/`0x81` at 200 ms and 40 ms.
- Measurement: TX-to-RX latency histogram (p50/p95/p99/max), inter-byte gaps, wake-up delivery
  confirmation, host-side jitter, and a loopback self-calibration floor.

**How we demo / verify it**
1. Against the **loopback transport** on any laptop: the frame encoder, CRC and measurement harness are
   exercised with no hardware.
2. Against a **real EGM** (QSP + libsas on a Quixant board) over: (i) a native 16550, (ii) a genuine FTDI
   with `LatencyTimer = 16` and then `= 1`, (iii) the user's actual USB adapter. Print the four histograms
   side by side.
3. **Pass criteria:** the EGM answers general polls and at least LP `$1A`, `$11`, `$54` correctly; wake-up
   discrimination is confirmed (the EGM answers only its own address); p99 turnaround < 20 ms on at least
   one transport; and we can state, from measurement, which transports are *supported* and which are
   *best-effort*.
4. **If it fails on every USB path**, we have the answer before writing an application, and Q1 is decided
   by data: the tool ships requiring a native 16550 or QXCOM, and says so on the tin.

### M2 — Protocol core and trace

*Target: 2-3 weeks. No UI.*

**Delivers:** `Sas.Abstractions`, `Sas.Protocol` (CRC, BCD, frame codec, schema model with the full decode
feature set of §4.4 incl. repeat/TLV/variants/bounds), the schema database seeded from `libsas.h` plus the
imported INI, the enum/meter/exception registries, the help store, the legacy INI importer with its
validation report, the trace writer/reader, and importers for `TXRXData.dat` and `progressive_sas.log`.
Plus `Sas.Transport` proper (managed + native + loopback backends) and `FakeEgm`.

**Demo/verify:** `sasctl decode "01 2F 07 00 00 00 00 00 46 00 8E C3"` prints named fields and `CRC OK`;
`sasctl import sastest.ini` prints the defect report; encode/decode round-trip property tests pass over the
whole catalogue; golden-vector tests pass on real captured frames; the whole layer runs in CI with no
hardware.

### M3 — GUI shell, live session, decode

*Target: 3-4 weeks.*

**Delivers:** Avalonia shell; connection panel with discovery, capability banner and line self-test
(US-1, US-2, US-4); poll engine with rate control, address, sync and jitter display (US-6, US-7);
trace + frame inspector (US-13); exception log (US-16); command palette with typed parameter forms (US-9);
raw/quick send with fault injection (US-10, US-27); inline help beside the frame (US-25); profiles with
`$A0` reconciliation (US-26).

**Demo/verify:** connect to a real EGM on a USB adapter at COM12 with no file editing and no restart; run a
meter sweep; open a door on the EGM and watch the exception appear, decoded, in a filterable log; show the
`$A0` bit-by-bit diff catching the five-bit `conf.ini`-vs-QSP mismatch (W15).

### M4 — SASL and the headless runner

*Target: 4-5 weeks. This is the milestone that answers complaint (c).*

**Delivers:** the full §5 language (lexer, parser, binder, interpreter), the assertion vocabulary,
captures, control flow, fixtures, background rules, `measure`/`snapshot`/`report`, JUnit/JSON/HTML
reporters, run records, and `sasctl` with the §5.7 exit-code contract; GUI script editor with syntax
highlighting, `lint`, run and step.

**Demo/verify:** the four §5.8 scripts run green against `FakeEgm` in CI with no hardware, and are then run
against the real EGM at the bench; **T49 reproduces the LP `$2E` timeout defect already sitting in
`progressive_sas.log` and fails the build with a JUnit report and an attached trace**; the same script run
from the GUI and from `sasctl` produces identical run records (§6.4).

### M5 — Domain workbenches

*Target: 3-4 weeks.*

**Delivers:** AFT workbench with computed lengths, transaction history and status severity (US-17);
TITO/validation lifecycle (US-18); progressive broadcast with per-level control (US-20); meter grid with
snapshot/delta and built-in invariants (US-14, US-15); the legacy EFT tab, unhidden (US-19); event-rule
editor (US-16); template library with persistence and parameters (US-10).

**Demo/verify:** run an end-to-end AFT transfer and a full TITO ticket-in/ticket-out on a real EGM without
typing a single hex digit; show the meter delta table after 10 scripted games; show
`total coin out + total jackpot == total won` checked automatically (02 §16).

### M6 — Replay, diff and the scenario library

*Target: 3 weeks.*

**Delivers:** trace replay as host and as EGM, structural trace diff and baselines (US-24); the QSP
scenario library — the wire-only subset of T01-T57 as shipped `.sasl` files; the `stimulus` interface and
`manual` agent (US-35); soak mode; the HTML report with timelines.

**Demo/verify:** `sasctl run scenarios/*.sasl --report junit:results.xml` on a bench EGM, unattended,
overnight, producing a JUnit file a build server renders; diff last night's trace against tonight's and
show what changed.

### M7 — Hardening, packaging, adoption

*Target: 2-3 weeks.*

**Delivers:** single-file self-contained publish for Windows (and `sasctl` for Linux), signing, an
installer, crash reporting, keyboard shortcuts and themes (US-30 to US-33), the migration guide from
`sastest.exe`, and a documented supported-hardware list derived from M1's measurements.

**Demo/verify:** a colleague who has never seen the tool connects to an EGM and reads a meter in under two
minutes with no documentation; the migration guide walks their existing `sastest.ini` through import; CI
runs the hardware-free suite on every PR and the hardware suite nightly.

**Total: roughly 18-23 weeks of engineering, with a usable, adoption-worthy tool at the end of M4 (about
half way) and the riskiest question answered in week one.**
