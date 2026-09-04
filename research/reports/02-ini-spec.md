# `sastest.ini` — Complete Functional Specification of IGT `sastest.exe`

**Scope**: full decode of `C:\IGT3\sastest.ini` (1735 lines) and its variants, plus `C:\IGT3\conf.ini`.
**Purpose**: define exactly what the legacy tool can do, so the replacement application can inherit
every capability and fix what the file cannot express.

**Primary sources actually read**

| File | Lines / size | Role |
|---|---|---|
| `C:\IGT3\sastest.ini` | 1735 | the live config — read in full |
| `C:\IGT3\conf.ini` | 24 | SAS feature-flag file (separate from sastest.ini) |
| `C:\IGT3\sastest.ini.ORG` | 1735 | pristine 2013 copy |
| `C:\IGT3\_________sastest.ini` | 1793 | 2017 working copy |
| `C:\IGT3\test\sastest.ini` | 1793 | 2013 copy, differs from `_________` only in `Port` |
| `C:\Progetti\libsas\utils\igt\sastest.ini` | 1735 | same content as live, `Port = 2` |
| `C:\IGT3\SASINI.doc` | 31 KB Word | **vendor documentation of the INI format** (strings-extracted) |
| `C:\IGT3\SASTEST.HLP` | 30 KB WinHelp | **vendor user manual + INI reference** (strings-extracted) |
| `C:\IGT3\sastest.exe` | 985088 B, Borland C++ Builder 5, 2005-12-16 | binary; ASCII strings + DFM form resources extracted |

`md5sum` confirms `C:\IGT3\sastest.exe` and `C:\Progetti\libsas\utils\igt\sastest.exe` are the **same
binary** (`7332b0dddd4cfc22cbea15cd249cd27e`), so there is exactly one tool to beat.

Working artefacts produced by this analysis (same directory as this report):
`lp_table.md`, `gp_table.md`, `eft_table.md`, `exe_strings.txt`, `sasini_doc.txt`.

---

## 0. Executive summary of the INI as a system

`sastest.ini` is not a settings file. It is **four separate data-driven sub-systems** glued into one
Windows INI:

1. **A command catalogue** — `[Long Poll Commands]`, `[Electronic Funds Transfer]`, `[ASDC]`,
   `[Bonus Game Data Structures]`, `[Message Structures]`. Each entry becomes one item in a
   drop-down; selecting it pops modal input boxes, assembles a frame, appends CRC, transmits.
   **122 long-poll entries** (118 distinct codes) + **18 EFT entries**.
2. **A response decoder DSL** — `[Messages]` (**395 field descriptors** over **117 command codes**),
   backed by lookup tables `[General Poll]` (255 codes), `[Status Messages]`, `[Denominations]`,
   `[Ticket Transfer Codes]`, `[Ticket Machine Status Codes]`, `[Validation Type Codes]`,
   `[AFT Transfer Status Codes]`, `[AFT Transaction Receipt Status]`,
   `[Receive Validation Status Codes]`. This is the single most valuable artefact to port.
3. **A payload template library** — `[Data Structures]` (9 `BEGIN…END` blocks), editable at runtime
   from the *Command Set Structure Editor* tab, bound to a command by `[Message Structures]`.
4. **Three "replay" lists** — `[Quick Commands]` (one-click raw frames), `[Script]` (sequential
   replay that suppresses normal polling), `[Meter Test]` (a meter-delta harness).
   All three are **flat lists of literal hex with an optional English label**. No control flow.

Plus `[Active Help]` — **7 `BEGIN = $XX` … `END` prose topics** of SAS protocol documentation shown
in-app.

The vendor's own words (`SASINI.doc`, strings-extracted):

> "The SAS tester INI file is the main definition file for SAS commands, messages, scripts, and
> special functions. Its purpose is to allow existing and new SAS long polls, exceptions, messages
> and functions to be defined without recompiling the tester."

and, on the disclaimer page of `SASTEST.HLP`:

> "This is a prototype test program and as such it was never meant for release. … It is basically a
> bare bones program to help develop SAS protocols. There are going to be errors and strange
> behavior will be exhibited."

### 0.1 The UI surface the INI feeds

Tab captions recovered from the DFM form resources inside `sastest.exe`
(`grep -A6 '^TTabSheet$' exe_strings.txt`), in file order:

`Command Sets` · `Command Set Structure Editor` · `General Poll/Address` · `Quick Cmd` · `Misc` ·
`Multi-Denom` · `Validation` · `Redemption` · `Scripting` · `Script` · `Meter Test` · `Progressive` ·
`EFT Test` · `Registration / Lock` · `Transfer Funds` · `Receipt` · `Pulltab` · `Report` ·
`Authentication`

Every one of those tabs is driven by, or decorated by, an INI section. `Command Set Structure Editor`
is the runtime editor for `[Data Structures]`; `Report` writes `SasReport.txt`.

---

## 1. What the variants tell us (what the user actually touches)

`diff` results, run verbatim:

```
$ diff /c/IGT3/sastest.ini.ORG /c/IGT3/sastest.ini
7c7
< Port = 1            (ORG)
> Port = 11           (live)
1637,1651c1637,1651     -> [Meter Test]: ORG "0211../020F/022F03..", live "0111../010F/012F03.."
```

```
$ diff /c/IGT3/_________sastest.ini /c/IGT3/test/sastest.ini
7c7
< Port = 3
> Port = 1
```

```
$ diff -u /c/IGT3/sastest.ini /c/IGT3/_________sastest.ini      (abridged)
  [SAS Protocols]   Port 11 vs 3
  [Quick Commands]  live dropped "80" and one duplicate "81"; live ADDED
                    016F0e0000000001000200030004000d00
  [Script]          live has only 2 lines; the 2017 copy had ~40 active lines
                    plus 2 commented-out alternative scripts
  [Meter Test]      address byte 01 (live) vs 02 (2017 copy)
```

**Findings that matter for the new tool**

| Observation | Evidence | Consequence |
|---|---|---|
| The only setting ever edited across four copies is `Port`. | all three diffs, line 7 | COM-port selection must be a first-class UI control, not an INI edit plus restart. |
| Changing the gaming-machine address means **hand-editing the address byte of every raw frame** in `[Meter Test]`, `[Quick Commands]` and `[Script]`. | 15 lines rewritten `02xx` to `01xx` in the ORG-to-live diff | The new tool must treat address as a *variable*, resolved at send time. |
| `[Script]` is edited by deleting and commenting-out whole blocks; the 2017 file literally carries two named scripts separated by comments `;script 1 - default scripts` / `; script 2 - used for Implied NAK`. | `_________sastest.ini` lines 1588-1653 | There is no concept of a named, selectable, storable test case. Scripts are mutually exclusive and destructive to edit. |
| `[ASDC]` and `[Bonus Game Data Structures]` are **empty in every variant** (lines 1396-1398). | `grep -n '^\['` plus section dump of both files | Legacy; the vendor deprecated them ("These commands will be going away in future releases" — `SASTEST.HLP`). Port their replacement (`[Message Structures]` + `[Data Structures]`) instead. |
| Live `[Quick Commands]` gained `016F0e…` (extended meters, 6 selectors) in 2025. | `diff -u` hunk at line 1580 | Extended-meter interrogation (`$6F`/`$AF`) is current work; it must be ergonomic in the new tool, not a hand-typed hex string. |

---

## 2. `conf.ini` — SAS feature flags (NOT read by `sastest.exe`)

`C:\IGT3\conf.ini`, 24 lines, three sections mirroring the SAS long-poll `$A0` "Send enabled
features" response bytes:

```ini
[features1]
jackpotMultiplier=1
aftBonusAwards=1
legacyBonusAwards=0
tournament=0
validationExtensions=1
validationStyleSystem=0
validatonStyleSecureEnhanced=1
ticketRedemption=1

[features2]
meterModelWonCreditsMeteredWhenWon=0
meterModelWonCreditsMeteredWhenPlayedOrPaid=0
ticketsToTotalDropAndTotalCancelledCredits=1
extendedMeters=1
componentAuthentication=1
advancedFundsTransfer=1
multiDenomExtensions=1

[features3]
maximumPollingRate=1
multipleSasProgressiveWinReporting=1
meterChangeNotification=0
sessionPlay=0
```

Note the typo `validatonStyleSecureEnhanced` (missing `i`) — preserved verbatim.

**Grammar**: `key=value` with no spaces, boolean 0/1, three fixed sections. Key names are camelCase
English, one per feature bit of the `$A0` response.

**Runtime role**: none inside `sastest.exe`. A `grep` of the extracted binary strings finds no
`conf.ini` reference and none of these key names; the only INI filenames in the binary are
`sastest.ini` / `SASTEST.INI` (`exe_strings.txt` lines 6772, 6788). So **`conf.ini` belongs to the
libsas side (the device under test), not to the tester.** It is the *expected* `$A0` feature bitmap.

**New app**: make this a first-class "expected feature profile" — load it, send `$A0`, and diff
expected vs actual bit by bit, per game number. The legacy tool cannot do this at all; it only
prints `Features1/2/3` as three decimal bytes (`[Messages]`, lines 1173-1177).

---

## 3. `[SAS Protocols]` (lines 5-14) and `[Communications]` (lines 16-19)

Live content:

```ini
[SAS Protocols]
Wakeup Delay = 2          ; ms delay around the emulated 9th (wake-up) bit
Port = 11                 ; COM port number
Poll Rate = 200           ; general-poll interval, ms
Address = 1               ; gaming machine SAS address (decimal)
Validation = Enhanced     ; validation scheme: Enhanced | System | Standard
Timing = 0
CRC Check = 1             ; 1 = verify received CRC
Setup File = SAStest.set
Version = "s 2.00 - 6.02"

[Communications]
Echo = 0                  ; 1 = fibre-optic loop, ignore our own echoed bytes
DTR = 1
;Index = -1               ; fibre-optic echo index fudge factor
```

**(a) Grammar**: plain `Key = Value`, one per line, `;` starts a comment. Values are decimal
integers, bare words, or a quoted string (`Version`).

**(b) Runtime**: read once at start-up into the connection/transport layer. `Version` is display-only
(the binary also carries hard-coded `Supports SAS Version` / `Supports SAS Version 1.0` strings at
`exe_strings.txt` 6838, 6841). `Validation = Enhanced` selects the validation state machine used by
the `Validation` / `Redemption` tabs.

**Which keys the binary actually reads** (exact-match `grep -x` against `exe_strings.txt`):

| Key | In exe? | Line in `exe_strings.txt` |
|---|---|---|
| `Wakeup Delay`, `Port`, `Poll Rate`, `Address`, `Validation` | YES | 6747, 6748, 6749, 6750, 6751 |
| `Timing`, `CRC Check`, `Version`, `Length`, `Auto Initialize` | YES | 6835, 6833, 6837, 6843, 6845 |
| `Echo`, `Index` | YES | 6826, 6829 |
| **`Setup File`** | **NO** | — |
| **`DTR`** | **NO** | — |

So `Setup File = SAStest.set` and `DTR = 1` are **dead keys** in the live INI. The `.set` file is
reached through a File-menu dialog instead (`Setup File (*.set)|*.set|Any File|*.*`,
`exe_strings.txt` 6919). Two further `[SAS Protocols]` keys — `Length` and `Auto Initialize` — are
read by the exe but are **absent from every INI variant**; their semantics are UNKNOWN.

`SASTEST.HLP` explains why `Wakeup Delay` exists at all:

> "Since the SAS protocol uses a 'wake up bit' (a 9th bit) for machine addressing we have to use the
> parity bit as a substitute. … Windows requires you to totally re-initialize the COMM port and it
> is virtually impossible to determine if the byte with the wakeup bit really went out of the UART."

**(c) New app**: none of this belongs in a hand-edited text file. Port / baud / address / poll-rate /
validation-mode become a named *connection profile* object (multiple profiles, switchable at
runtime, no restart). Replace the parity-bit wake-up hack with a genuinely 9-bit-capable transport.
Keep `Echo`/`Index` semantics only if fibre-optic loops are still in use, and expose them as a
"loopback echo suppression" toggle with auto-detection.

---

## 4. `[General Poll]` (lines 21-277) — exception decode table

**(a) Grammar**

```
$XX = <free text to end of line>
```

`$` is mandatory; `XX` is the exception byte in upper-case hex; `=` surrounded by spaces; everything
after `= ` is the English text. A leading `;` comments the whole entry out. No other tokens.

**(b) Runtime**: a pure lookup table. When a general poll (`address | 0x80`) returns a non-zero
exception byte, the tool prints this text into the English pane. Nothing else — no action, no
filtering, no counting, no acknowledgement logic driven from here.

**Size**: 255 entries, `$00` … `$FE`.
* `$00` is present but **commented out** (`;$00 = No Exception Activity.`, line 22), so nothing is
  printed for "no activity".
* **`$FF` is absent entirely** (`grep -n '\$FF' sastest.ini` returns no hit in lines 21-277).
* 121 entries carry real text; **134 are the placeholder `Unknown exception!`**.

**(c) New app**: keep the table, but promote it from "string to print" to a typed record —
`{code, name, category (door/tilt/bill/meter/AFT/ticket/progressive/…), severity, sas_version_introduced,
expected_followup_long_poll}`. That single change makes exceptions filterable, assertable in a test
(`expect exception $6F within 2 s`), and countable. Ship the 121 known meanings as a built-in
baseline so the file starts complete rather than 134-times "Unknown exception!".

### 4.1 Complete exception table `$00`-`$FE`

| Code | Meaning |
|---|---|
| $00 | No Exception Activity. *(commented out in file)* |
| $01 | ACK |
| $02 | Unknown exception! |
| $03 | Unknown exception! |
| $04 | Unknown exception! |
| $05 | Unknown exception! |
| $06 | Unknown exception! |
| $07 | Unknown exception! |
| $08 | Unknown exception! |
| $09 | Unknown exception! |
| $0A | Unknown exception! |
| $0B | Unknown exception! |
| $0C | Unknown exception! |
| $0D | Unknown exception! |
| $0E | Unknown exception! |
| $0F | Unknown exception! |
| $10 | Unknown exception! |
| $11 | Slot door was opened. |
| $12 | Slot door was closed. |
| $13 | Drop door was opened. |
| $14 | Drop door was closed. |
| $15 | Card cage was opened. |
| $16 | Card cage was closed. |
| $17 | AC power applied to gaming machine. |
| $18 | AC power lost from gaming machine. |
| $19 | Cashbox door was opened. |
| $1A | Cashbox door was closed. |
| $1B | Cashbox was removed. |
| $1C | Cashbox was installed. |
| $1D | Belly door was opened. |
| $1E | Belly door was closed. |
| $1F | No activity and waiting for user input. |
| $20 | General tilt. (unspecified) |
| $21 | Coin in tilt. |
| $22 | Coin out tilt. |
| $23 | Hopper empty detected. |
| $24 | Extra coin paid. |
| $25 | Diverter malfunction. |
| $26 | A player-initiated cash out has occurred. |
| $27 | Cashbox full detected. |
| $28 | Bill jam detected. |
| $29 | Bill acceptor hardware failure. |
| $2A | Reverse bill detected. |
| $2B | Bill Rejected. |
| $2C | Counterfeit bill detected. |
| $2D | Reverse coin in detected. |
| $2E | Cashbox near full detected. |
| $2F | Unknown exception! |
| $30 | Unknown exception! |
| $31 | CMOS RAM error (data recovered from EEPROM). |
| $32 | CMOS RAM error (no data recovered from EEPROM). |
| $33 | CMOS RAM error (bad device). |
| $34 | EEPROM error (data error). |
| $35 | EEPROM error (bad device). |
| $36 | EPROM error (different checksum - version changed). |
| $37 | EPROM error (bad checksum compare). |
| $38 | Partitioned EPROM error (checksum - version changed). |
| $39 | Partitioned EPROM error (bad checksum compare). |
| $3A | Memory error reset (operator used self test switch). |
| $3B | Low backup battery detected. |
| $3C | Operator changed options. |
| $3D | A cash out ticket has been printed. |
| $3E | Hand pay has been validated. |
| $3F | Validation ID not configured. |
| $40 | Reel Tilt (reel unspecified). |
| $41 | Reel 1 tilt. |
| $42 | Reel 2 tilt. |
| $43 | Reel 3 tilt. |
| $44 | Reel 4 tilt. |
| $45 | Reel 5 tilt. |
| $46 | Reel mechanism disconnected. |
| $47 | $1.00 bill accepted. |
| $48 | $5.00 bill accepted. |
| $49 | $10.00 bill accepted. |
| $4A | $20.00 bill accepted. |
| $4B | $50.00 bill accepted. |
| $4C | $100.00 bill accepted. |
| $4D | $2.00 bill accepted. |
| $4E | $500.00 bill accepted. |
| $4F | Bill accepted. |
| $50 | $200.00 bill accepted. |
| $51 | Hand pay is pending. |
| $52 | Hand pay was reset. |
| $53 | No progressive information received for 5 seconds. |
| $54 | Progressive win (cashout device/credit paid). |
| $55 | Player has cancelled the hand pay request. |
| $56 | SAS progressive level hit. |
| $57 | System validation request. |
| $58 | Unknown exception! |
| $59 | Unknown exception! |
| $5A | Unknown exception! |
| $5B | Unknown exception! |
| $5C | Unknown exception! |
| $5D | Unknown exception! |
| $5E | Unknown exception! |
| $5F | Unknown exception! |
| $60 | Printer communication error. |
| $61 | Printer paper out error. |
| $62 | Unknown exception! |
| $63 | Unknown exception! |
| $64 | Unknown exception! |
| $65 | Cash out win to system request. |
| $66 | Cash out button pressed. |
| $67 | Ticket has been inserted. |
| $68 | Ticket transfer complete. |
| $69 | AFT transfer complete. |
| $6A | AFT request for host cashout. |
| $6B | AFT request for host win cashout. |
| $6C | AFT request to register. |
| $6D | AFT registration acknowledged. |
| $6E | AFT registration cancelled. |
| $6F | Game locked. |
| $70 | Exception buffer overflow. |
| $71 | Change lamp on. |
| $72 | Change lamp off. |
| $73 | Game reset during payout. |
| $74 | Printer paper low. |
| $75 | Printer power off. |
| $76 | Printer power on. |
| $77 | Replace printer ribbon. |
| $78 | Printer carriage jammed. |
| $79 | Coin in lockout malfunction. |
| $7A | Game soft meters reset to zero. |
| $7B | Bill validator totals reset by attendant. |
| $7C | A legacy bonus pay has occurred. |
| $7D | Unknown exception! |
| $7E | Game has started. |
| $7F | Game has ended. |
| $80 | Hopper full detected. |
| $81 | Hopper level low detected. |
| $82 | Display meters has been entered. |
| $83 | Display meters has been exited. |
| $84 | Self test has been entered. |
| $85 | Self test has been exited. |
| $86 | Game is out of service (by attendant). |
| $87 | Player has requested draw cards. |
| $88 | Reel n has stopped. |
| $89 | Coin/credit wagered. |
| $8A | Game recall entry has been displayed. |
| $8B | Card held/not held. |
| $8C | Game selected. |
| $8D | Unknown exception! |
| $8E | Component list changed. |
| $8F | Authentication complete. |
| $90 | Unknown exception! |
| $91 | Unknown exception! |
| $92 | Unknown exception! |
| $93 | Unknown exception! |
| $94 | Unknown exception! |
| $95 | Unknown exception! |
| $96 | Unknown exception! |
| $97 | Unknown exception! |
| $98 | Power off card cage access. |
| $99 | Power off slot door access. |
| $9A | Power off cashbox door access. |
| $9B | Power off drop door access. |
| $9C | Unknown exception! |
| $9D | Unknown exception! |
| $9E | Unknown exception! |
| $9F | Unknown exception! |
| $A0 | Meter change pending. |
| $A1 | Meter change cancelled. |
| $A2 | Enabled games/denoms changed. |
| $A3 | Unknown exception! |
| $A4 | Unknown exception! |
| $A5 | Unknown exception! |
| $A6 | Unknown exception! |
| $A7 | Unknown exception! |
| $A8 | Unknown exception! |
| $A9 | Unknown exception! |
| $AA | Unknown exception! |
| $AB | Unknown exception! |
| $AC | Unknown exception! |
| $AD | Unknown exception! |
| $AE | Unknown exception! |
| $AF | Unknown exception! |
| $B0 | Unknown exception! |
| $B1 | Unknown exception! |
| $B2 | Unknown exception! |
| $B3 | Unknown exception! |
| $B4 | Unknown exception! |
| $B5 | Unknown exception! |
| $B6 | Unknown exception! |
| $B7 | Unknown exception! |
| $B8 | Unknown exception! |
| $B9 | Unknown exception! |
| $BA | Unknown exception! |
| $BB | Unknown exception! |
| $BC | Unknown exception! |
| $BD | Unknown exception! |
| $BE | Unknown exception! |
| $BF | Unknown exception! |
| $C0 | Unknown exception! |
| $C1 | Unknown exception! |
| $C2 | Unknown exception! |
| $C3 | Unknown exception! |
| $C4 | Unknown exception! |
| $C5 | Unknown exception! |
| $C6 | Unknown exception! |
| $C7 | Unknown exception! |
| $C8 | Unknown exception! |
| $C9 | Unknown exception! |
| $CA | Unknown exception! |
| $CB | Unknown exception! |
| $CC | Unknown exception! |
| $CD | Unknown exception! |
| $CE | Unknown exception! |
| $CF | Unknown exception! |
| $D0 | Unknown exception! |
| $D1 | Unknown exception! |
| $D2 | Unknown exception! |
| $D3 | Unknown exception! |
| $D4 | Unknown exception! |
| $D5 | Unknown exception! |
| $D6 | Unknown exception! |
| $D7 | Unknown exception! |
| $D8 | Unknown exception! |
| $D9 | Unknown exception! |
| $DA | Unknown exception! |
| $DB | Unknown exception! |
| $DC | Unknown exception! |
| $DD | Unknown exception! |
| $DE | Unknown exception! |
| $DF | Unknown exception! |
| $E0 | Unknown exception! |
| $E1 | Unknown exception! |
| $E2 | Unknown exception! |
| $E3 | Unknown exception! |
| $E4 | Unknown exception! |
| $E5 | Unknown exception! |
| $E6 | Unknown exception! |
| $E7 | Unknown exception! |
| $E8 | Unknown exception! |
| $E9 | Unknown exception! |
| $EA | Unknown exception! |
| $EB | Unknown exception! |
| $EC | Unknown exception! |
| $ED | Unknown exception! |
| $EE | Unknown exception! |
| $EF | Unknown exception! |
| $F0 | Unknown exception! |
| $F1 | Unknown exception! |
| $F2 | Unknown exception! |
| $F3 | Unknown exception! |
| $F4 | Unknown exception! |
| $F5 | Unknown exception! |
| $F6 | Unknown exception! |
| $F7 | Unknown exception! |
| $F8 | Unknown exception! |
| $F9 | Unknown exception! |
| $FA | Unknown exception! |
| $FB | Unknown exception! |
| $FC | Unknown exception! |
| $FD | Unknown exception! |
| $FE | Unknown exception! |

---

## 5. `[Long Poll Commands]` (lines 278-658) — the command surface

This is the section that defines *what the tool can send*. It is also the section a new tool must
reproduce feature-for-feature on day one.

### 5.1 Exact grammar

Three line kinds, plus comments (`;`) and blank lines.

**(1) Command line**

```
$XX = <description>,<LENGTH>,<TRANS>,<ACK>,<TYPE>,<STATUS>,<CRC>,<nBoxes>[,<extra>]
```

| Token | Rule |
|---|---|
| `$` | mandatory sigil, marks a command |
| `XX` | 2 hex chars, the SAS long-poll function code |
| ` = ` | `=` with spaces around it |
| `<description>` | free text shown in the drop-down. **Commas are forbidden** (they are the field separator). Trailing whitespace is padding only. |
| `<LENGTH>` | `y`/`n` — the frame carries a length byte after the command byte |
| `<TRANS>` | `y`/`n` — the frame carries a transaction number (EFT / extended EFT only) |
| `<ACK>` | `y`/`n` — the frame carries an ACK byte, and an ACK is transmitted (EFT) |
| `<TYPE>` | `y`/`n` — the frame carries a type byte (extended EFT only) |
| `<STATUS>` | `y`/`n` — the frame carries a status byte. **The header comment in the INI (line 288) mislabels this field `CRYPT`; both `SASINI.doc` and `SASTEST.HLP` call it STATUS.** It is `n` on every line in the file, so the discrepancy is untestable from data alone. |
| `<CRC>` | `y`/`n` — append the 16-bit SAS CRC. Case-insensitive: the file uses both `y` and `Y`. |
| `<nBoxes>` | decimal count of `?XX` input boxes that follow |
| `<extra>` | **undocumented 8th field.** Appears exactly once in the whole file: `$08 … ,n,n,n,n,n,Y,2,b` (line 330). `b` is UNKNOWN — most plausibly "binary/bitmap input" (`$08`'s first box is a 4-byte denomination bitmap), but nothing in the binary strings, `SASINI.doc` or `SASTEST.HLP` mentions an 8th field. |

**(2) Input-box line**

```
?XX = <prompt text>@<nBytes>
```

`?` sigil; `XX` binds the box to the command with the same code; `@` separates prompt from the byte
count. Boxes are consumed **in file order** and their entered bytes are concatenated into the payload
in that order. `nBytes` is decimal.

`@0` has two documented meanings depending on section:
* in `[Message Structures]` — "if the number is 0 you may select from a data structure"
  (`SASINI.doc`), i.e. the box becomes a picker over `[Data Structures]`;
* in `[Long Poll Commands]` — used only for genuinely variable-length ASCII/blob payloads:
  `?76 = Enter data @0`, `?7C = Enter data @0`, `?7D = Enter location data @0` (and address1/2),
  `?B7 = Enter floor location (ASCII)@0`. Treated as a free-length field. The exact input encoding
  (raw hex vs literal ASCII) is UNKNOWN from the file; the `?B7` prompt says "(ASCII)".

**(3) Value-enumeration line** (undocumented in `SASINI.doc`/`SASTEST.HLP`, present in the file)

```
*XX = <value> = <meaning>
```

Used exactly once, for `$50`, lines 469-480 — eleven annotations of the "validation type" byte:

```
*50 = 00 = Cashable ticket, no lockup
*50 = 01 = Promotional ticket
*50 = 02 = Cashable ticket from AFT
...
*50 = 82 = Nonrestricted promotional ticket redeemed
```

**(b) Runtime behaviour**

Each `$XX` line becomes one row of the *Long Poll Commands* drop-down on the `Command Sets` tab
(`SASTEST.HLP`: "press the small down arrow on the right side of the Long Poll Commands dropdown").
Selecting it pops `nBoxes` modal input dialogs in file order, assembles
`address || cmd || [length] || [trans] || [ack] || [type] || [status] || <box bytes> || [CRC]`,
transmits it, and logs `TX>=` / `RX<=` hex plus the decoded English from `[Messages]`.

**The key repeats.** `$6F`, `$71`, `$73` and `$AF` each appear twice with different box sets —
that is how the tool offers command *variants* ("Redeem ticket" vs "Redeem ticket interrogate";
"Extended Meters (get 2 meters)" vs "(get 1 meter)"). The drop-down is an ordered list, not a map.

**(c) Statistics** (parsed programmatically; every line accounted for, zero parse failures)

* **122 command entries**, **118 distinct codes**, 4 codes duplicated.
* Flag-tuple frequency: `n,n,n,n,n,n,0` × 65 (bare read-only meter polls); `n,n,n,n,n,y,1` × 15;
  `n,n,n,n,n,Y,0` × 9; `n,n,n,n,n,Y,1` × 7; `y,n,n,n,n,y,2` × 5; the rest are one-offs.
* `TRANS`, `ACK`, `TYPE`, `STATUS` are **`n` on every single line** of this section — they exist only
  for `[Electronic Funds Transfer]`.
* Only `LENGTH=y` entries build variable-length frames: 15 entries over 11 distinct codes —
  `$2F $6F(x2) $71(x2) $73(x2) $76 $7B $7C $7D $AF(x2) $B6 $B7`.
* Codes present in `[Messages]` but **not** invocable from `[Long Poll Commands]`:
  `$00 $1D $22 $23 $24 $25 $26 $28 $29 $62 $63 $64 $65 $66 $67 $69 $6A $6B` — all of which *are* in
  `[Electronic Funds Transfer]` — plus **`$00` (busy), `$6E` (authentication), `$72` (AFT transfer
  funds) and `$A5` (ASDC)** which are in **neither** command section. `$6E` is driven by the
  hard-coded `Authentication` tab, `$72` by the hard-coded `Transfer Funds` tab, `$A5` by the
  (empty) `[ASDC]` section.
* 22 long-poll codes have **no `[Messages]` decoder** — every one is a write/ACK-only command:
  `$01`-`$0E`, `$2E`, `$7C`, `$7D`, `$7F`, `$8A`, `$8B`, `$8C`, `$AA`.
  **So `$72` — the single most important AFT poll — cannot be issued from either command section;
  it is reachable only through `[Quick Commands]`, `[Message Structures]` + `[Data Structures]`,
  or the hard-wired AFT tabs.**

**(d) How the new app should reimplement it**

Do not port the positional CSV. The information content is:
`{code, variant name, frame shape flags, ordered parameter list of {name, byte width, encoding,
enumerated values, default}}`. That is a **command schema**. Express it in a typed, versioned,
validating format (JSON/YAML/TOML with a JSON-Schema, or generated straight from `libsas` headers so
it can never drift). Requirements the legacy format cannot meet and the new one must:

* parameter **encoding** must be explicit (BCD vs binary vs ASCII vs bitmap) — today it lives in
  free-text prompts like "Enter BINARY delay time" / "Enter asset number (binary)" / "(hex)";
* enumerated parameter values (the `*50` mechanism) must be first-class for **every** parameter,
  not one command;
* per-command **SAS version gating** and feature-flag gating (link to `conf.ini`);
* variants must be named sub-commands, not duplicate keys in an ordered list;
* every command must be invocable **programmatically** (script/API), not only from a drop-down;
* `$72`, `$6E` and the AFT/authentication families must be data-driven like the rest.

### 5.2 Complete long-poll command table

| Line | Code | Description | Len | Trans | ACK | Type | Status | CRC | #Boxes | Input boxes (prompt@bytes) |
|---|---|---|---|---|---|---|---|---|---|---|
| 316 | $01 | Shutdown (lock out play) | n | n | n | n | n | Y | 0 | - |
| 318 | $02 | Startup (enable play) | n | n | n | n | n | Y | 0 | - |
| 320 | $03 | Sound off (all sounds disabled) | n | n | n | n | n | Y | 0 | - |
| 322 | $04 | Sound on (all sounds enabled) | n | n | n | n | n | Y | 0 | - |
| 324 | $05 | Reel spin sounds disabled | n | n | n | n | n | Y | 0 | - |
| 326 | $06 | Enable bill acceptor | n | n | n | n | n | Y | 0 | - |
| 328 | $07 | Disable bill acceptor | n | n | n | n | n | Y | 0 | - |
| 330 | $08 | Configure bill denominations | n | n | n | n | n | Y | 2 ,b | Enter bill denomination bitmap (FFFFFF00):@4; Enter bill acceptor action flag 00 or 01:@1 |
| 334 | $09 | Enable/disable game n | n | n | n | n | n | Y | 2 | Enter the game number@2; Enter 01-Enable or 00-Disable@1 |
| 338 | $0A | Enter maintenance mode | n | n | n | n | n | Y | 0 | - |
| 340 | $0B | Exit maintenance mode | n | n | n | n | n | Y | 0 | - |
| 342 | $0C | Set sound volume | n | n | n | n | n | Y | 1 | Enter the volume level@4 |
| 345 | $0D | Play sound | n | n | n | n | n | Y | 1 | Enter the sound number@4 |
| 348 | $0E | Enable/disable real time event reporting | n | n | n | n | n | Y | 1 | Enter 01-Enable or 00-Disable@1 |
| 351 | $0F | Send meters $10 through $15 | n | n | n | n | n | n | 0 | - |
| 353 | $10 | Send total cancelled credits meter | n | n | n | n | n | n | 0 | - |
| 355 | $11 | Send total coin in meter | n | n | n | n | n | n | 0 | - |
| 357 | $12 | Send total coin out meter | n | n | n | n | n | n | 0 | - |
| 359 | $13 | Send total drop meter | n | n | n | n | n | n | 0 | - |
| 361 | $14 | Send total jackpot meter | n | n | n | n | n | n | 0 | - |
| 363 | $15 | Send games played meter | n | n | n | n | n | n | 0 | - |
| 365 | $16 | Send games won meter | n | n | n | n | n | n | 0 | - |
| 367 | $17 | Send games lost meter | n | n | n | n | n | n | 0 | - |
| 369 | $18 | Send games since last power up and door closure | n | n | n | n | n | n | 0 | - |
| 371 | $19 | Send Meters $11 through $15 | n | n | n | n | n | n | 0 | - |
| 373 | $1A | Send current credits | n | n | n | n | n | n | 0 | - |
| 375 | $1B | Send hand pay information | n | n | n | n | n | n | 0 | - |
| 377 | $1C | Send meters | n | n | n | n | n | n | 0 | - |
| 379 | $1E | Send bill meters (# of bills) | n | n | n | n | n | n | 0 | - |
| 381 | $1F | Send gaming machine ID & information | n | n | n | n | n | n | 0 | - |
| 383 | $20 | Send dollar value of bills meter | n | n | n | n | n | n | 0 | - |
| 385 | $21 | ROM signature verification | n | n | n | n | n | y | 1 | Enter 2 byte seed@2 |
| 388 | $27 | Send current restricted promotional credits | n | n | n | n | n | n | 0 | - |
| 390 | $2A | Send true coin in meter (coins) | n | n | n | n | n | n | 0 | - |
| 392 | $2B | Send true coin out meter (coins) | n | n | n | n | n | n | 0 | - |
| 394 | $2C | Send current hopper level (coins) | n | n | n | n | n | n | 0 | - |
| 396 | $2D | Send total hand paid cancelled credits | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 399 | $2E | Delay the gaming machine | n | n | n | n | n | y | 1 | Enter BINARY delay time in units of 100ms@2 |
| 402 | $2F | Send selected meters for game n | y | n | n | n | n | y | 2 | Enter the game number (0000 = machine)@2; Enter the meter number (hex)@1 |
| 406 | $31 | Send $1.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 408 | $32 | Send $2.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 410 | $33 | Send $5.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 412 | $34 | Send $10.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 414 | $35 | Send $20.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 416 | $36 | Send $50.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 418 | $37 | Send $100.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 420 | $38 | Send $500.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 422 | $39 | Send $1000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 424 | $3A | Send $200.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 426 | $3B | Send $25.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 428 | $3C | Send $2000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 430 | $3D | Send cash out ticket information | n | n | n | n | n | n | 0 | - |
| 432 | $3E | Send $2500.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 434 | $3F | Send $5000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 436 | $40 | Send $10000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 438 | $41 | Send $20000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 440 | $42 | Send $25000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 442 | $43 | Send $50000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 444 | $44 | Send $100000.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 446 | $45 | Send $250.00 bills in meter | n | n | n | n | n | n | 0 | - |
| 448 | $46 | Send credit amount of all bills accepted | n | n | n | n | n | n | 0 | - |
| 450 | $47 | Send coin amount from an external coin acceptor | n | n | n | n | n | n | 0 | - |
| 452 | $48 | Send last accepted bill information | n | n | n | n | n | n | 0 | - |
| 454 | $49 | Send number of bills currently in stacker | n | n | n | n | n | n | 0 | - |
| 456 | $4A | Send credit amt of bills currently in stacker | n | n | n | n | n | n | 0 | - |
| 458 | $4C | Set enhanced validation ID | n | n | n | n | n | y | 2 | Enter the machine ID (binary)@3; Enter starting sequence number (binary)@3 |
| 462 | $4D | Send enhanced validation info | n | n | n | n | n | y | 1 | Enter buffer index (00=current info)@1 |
| 465 | $4F | Send current hopper status | n | n | n | n | n | n | 0 | - |
| 467 | $50 | Send validation meters | n | n | n | n | n | y | 1 | Enter validation type@1 |
| 482 | $51 | Send total number of games implemented | n | n | n | n | n | n | 0 | - |
| 484 | $52 | Send game n meters | n | n | n | n | n | Y | 1 | Enter the game number (0000 = machine)@2 |
| 487 | $53 | Send game n configuration | n | n | n | n | n | Y | 1 | Enter the game number (0000 = machine)@2 |
| 490 | $54 | Send SAS version and machine serial number | n | n | n | n | n | n | 0 | - |
| 492 | $55 | Send selected game number | n | n | n | n | n | n | 0 | - |
| 494 | $56 | Send enabled game numbers | n | n | n | n | n | n | 0 | - |
| 496 | $57 | Send pending cashout info | n | n | n | n | n | n | 0 | - |
| 498 | $58 | Receive validation number | n | n | n | n | n | y | 2 | Enter validation system ID@1; Enter validation number@8 |
| 502 | $6F | Extended Meters (get 2 meters) | y | n | n | n | n | y | 3 | Game number (0000 = machine)@2; Meter code 1 (Table C-7)@2; Meter code 2 (Table C-7)@2 |
| 507 | $6F | Extended Meters (get 1 meter) | y | n | n | n | n | y | 2 | Game number (0000 = machine)@2; Meter code (Table C-7)@2 |
| 511 | $70 | Send ticket validation data | n | n | n | n | n | n | 0 | - |
| 513 | $71 | Redeem ticket | y | n | n | n | n | y | 4 | Enter transfer code@1; Enter transfer amount@5; Enter parsing code@1; Enter validation number@9 |
| 519 | $71 | Redeem ticket interrogate | y | n | n | n | n | y | 1 | Enter transfer code@1 |
| 522 | $73 | AFT register gaming machine | y | n | n | n | n | y | 4 | Enter registration code@1; Enter asset number (binary)@4; Enter registration key@20; Enter POS ID@4 |
| 528 | $73 | AFT register interrogate | y | n | n | n | n | y | 1 | Enter registration code@1 |
| 531 | $74 | AFT game lock and status request | n | n | n | n | n | y | 3 | Enter lock code@1; Enter lock condition@1; Enter lock timeout@2 |
| 536 | $76 | Set custom AFT ticket data | y | n | n | n | n | y | 3 | Enter function@1; Enter data code@1; Enter data@0 |
| 541 | $7B | Extended validation status | y | n | n | n | n | y | 4 | Enter control mask (binary)@2; Enter control states (binary)@2; Enter cashable expiration@2; Enter restricted expiration@2 |
| 547 | $7C | Set extended ticket data | y | n | n | n | n | y | 2 | Enter data code@1; Enter data@0 |
| 551 | $7D | Set ticket data | y | n | n | n | n | y | 5 | Enter host ID@2; Enter expiration in number of days@1; Enter location data@0; Enter address1 data@0; Enter address2 data@0 |
| 558 | $7E | Send current date and time | n | n | n | n | n | n | 0 | - |
| 560 | $7F | Set date and time | n | n | n | n | n | Y | 2 | Enter the date (MMDDYYYY)@4; Enter the time (HHMMSS)@3 |
| 564 | $83 | Send cumulative progressive wins | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 567 | $84 | Send progressive win amount | n | n | n | n | n | n | 0 | - |
| 569 | $85 | Send SAS progressive win amount | n | n | n | n | n | n | 0 | - |
| 571 | $87 | Send multiple SAS progressive win amounts | n | n | n | n | n | n | 0 | - |
| 573 | $8A | Initiate a legacy bonus pay | n | n | n | n | n | y | 2 | Enter the amount of the bonus@4; 00 deduct/01 non-deduct/02 wagermatch@1 |
| 577 | $8B | Initiate multiplied jackpot mode | n | n | n | n | n | y | 5 | Enter the minimum win@4; Enter the maximum win@4; Enter the multiplier (hex 01-0A)@1; Enter 00-enable or 01-disable@1; Enter 00-all wagers or 01-max bet only@1 |
| 584 | $8C | Enter/exit tournament mode | n | n | n | n | n | y | 5 | Enter the game number (0000 = machine)@2; Enter time minutes@1; Enter time seconds@1; Enter credits@4; 00-disable or 01-enable tournament pulses@1 |
| 591 | $8E | Send card information | n | n | n | n | n | n | 0 | - |
| 593 | $8F | Send reel stop information | n | n | n | n | n | n | 0 | - |
| 595 | $90 | Send legacy bonus win amount | n | n | n | n | n | n | 0 | - |
| 597 | $94 | Reset hand pay | n | n | n | n | n | y | 0 | - |
| 599 | $95 | Send tournament games played | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 602 | $96 | Send tournament games won | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 605 | $97 | Send tournament credits wagered | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 608 | $98 | Send tournament credits won | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 611 | $99 | Send tournament meters 95 through 98 | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 614 | $9A | Send legacy bonus meters | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 617 | $A0 | Send enabled features | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 620 | $A4 | Send the cash out limit | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 623 | $A8 | Enable jackpot reset method | n | n | n | n | n | Y | 1 | 00-Standard or 01-Reset to credit meter@1 |
| 626 | $AA | Enable/disable auto rebet | n | n | n | n | n | Y | 1 | 00-Disable or 01-Enable@1 |
| 629 | $AF | Alt Extended Meters (get 2 meters) | y | n | n | n | n | y | 3 | Game number (0000 = machine)@2; Meter code 1 (Table C-7)@2; Meter code 2 (Table C-7)@2 |
| 634 | $AF | Alt Extended Meters (get 1 meter) | y | n | n | n | n | y | 2 | Game number (0000 = machine)@2; Meter code (Table C-7)@2 |
| 638 | $B1 | Send current player denomination | n | n | n | n | n | n | 0 | - |
| 640 | $B2 | Send enabled player denominations | n | n | n | n | n | n | 0 | - |
| 642 | $B3 | Send token denomination | n | n | n | n | n | n | 0 | - |
| 644 | $B4 | Send wager category information | n | n | n | n | n | y | 2 | Enter the game number (0000 = machine)@2; Enter the wager category (0000 = total coin in)@2 |
| 648 | $B5 | Send extended game n information | n | n | n | n | n | y | 1 | Enter the game number (0000 = machine)@2 |
| 651 | $B6 | Meter collect status | y | n | n | n | n | y | 1 | 00=Ack 01=Ready 80=Unable@1 |
| 654 | $B7 | Set machine numbers | y | n | n | n | n | y | 2 | Enter asset number (binary)@4; Enter floor location (ASCII)@0 |
| 658 | $FD | dummy | n | n | n | n | n | n | 0 | - |

---

## 6. `[Electronic Funds Transfer]` (lines 660-701) — the legacy EFT surface

**(a) Grammar**: byte-for-byte identical to `[Long Poll Commands]` — `$XX = desc,LENGTH,TRANS,ACK,TYPE,STATUS,CRC,nBoxes`
plus `?XX = prompt@n` boxes.

> "Electronic Funds Transfer is identical to the Long Poll Commands with the exception that the
> Command Description appears in the Electronic Funds Transfer Drop Down." — `SASTEST.HLP`

**(b) Runtime**: populates the second drop-down (`EFT Test` tab). This is the **only** section where
the `TRANS` and `ACK` flags are ever `y`. Flag-tuple census over the 18 entries:

| Flags (`LEN,TRANS,ACK,TYPE,STATUS,CRC,nBoxes`) | Count | Meaning |
|---|---|---|
| `n,y,y,n,n,y,1` | 6 | money-moving command with a transaction number, an ACK bit and one amount box |
| `n,y,y,n,n,y,0` | 6 | money-moving command with transaction number + ACK, no operand |
| `n,n,n,n,n,n,0` | 4 | pure query, no CRC (`$1D $27 $28 $6A`) |
| `n,n,y,n,n,y,0` | 2 | query with ACK bit (`$26 $66`) |

The EFT frame therefore carries a *transaction number* the tool increments, and an *ACK* bit that
must be toggled between the "request" and the "confirm" pass — a two-phase handshake. Responses are
decoded by `[Messages]` **and** by `[Status Messages]` (the tool inspects the status byte when the
message declares one).

**(c) Codes** — 18 entries, all distinct, split into a "dollars" family (`$22`-`$29`) and a
"credits" family (`$62`-`$6B`):

Note `$27` ("current promotional / restricted promotional credits") is the one code declared in
**both** `[Long Poll Commands]` (line 388) and `[Electronic Funds Transfer]` (line 683).

**(d) New app**: EFT is superseded by AFT (`$72`/`$73`/`$74`) in SAS 6.x, but a conformance tool must
still exercise it. Model the transaction-number/ACK handshake as an explicit **stateful two-phase
transaction** in the command schema (`phase: request | confirm`, `transaction_number: auto`), not as
two boolean flags the operator has to understand. Assert the returned status byte automatically
against `[Status Messages]` rather than merely printing it.

### 6.1 Complete EFT command table

| Line | Code | Description | Len | Trans | ACK | Type | Status | CRC | #Boxes | Input box |
|---|---|---|---|---|---|---|---|---|---|---|
| 661 | $1D | Cumulative meters (credits) | n | n | n | n | n | n | 0 | - |
| 663 | $22 | Load non-cashable money (dollars) | n | y | y | n | n | y | 1 | Enter how many dollars@4 |
| 666 | $23 | Load promotional money (dollars) | n | y | y | n | n | y | 1 | Enter how many dollars@4 |
| 669 | $24 | Force cash out to host (dollars) | n | y | y | n | n | y | 0 | - |
| 671 | $25 | Force cash out from gaming machine (dollars) | n | y | y | n | n | y | 0 | - |
| 673 | $26 | Request for cash out dollar amount | n | n | y | n | n | y | 0 | - |
| 675 | $27 | Request for current promotional credits | n | n | n | n | n | n | 0 | - |
| 677 | $28 | Request EFT transaction log | n | n | n | n | n | n | 0 | - |
| 679 | $29 | Load cashable money (dollars) | n | y | y | n | n | y | 1 | Enter how many dollars@4 |
| 682 | $62 | Load non-cashable credits | n | y | y | n | n | y | 1 | Enter how many credits@4 |
| 685 | $63 | Load promotional credits | n | y | y | n | n | y | 1 | Enter how many credits@4 |
| 688 | $64 | Force cash out to host (credits) | n | y | y | n | n | y | 0 | - |
| 690 | $65 | Force cash out from gaming machine (credits) | n | y | y | n | n | y | 0 | - |
| 692 | $66 | Request for cash out credit amount | n | n | y | n | n | y | 0 | - |
| 694 | $67 | Cash out win to host | n | y | y | n | n | y | 0 | - |
| 696 | $69 | Load cashable credits | n | y | y | n | n | y | 1 | Enter how many credits@4 |
| 699 | $6A | Request available EFT transfers | n | n | n | n | n | n | 0 | - |
| 701 | $6B | Force promotional cash out to host | n | y | y | n | n | y | 0 | - |

---

## 7. `[Status Messages]` (lines 703-725) — EFT status-byte decode

**(a) Grammar**: `$XX = <text>`, identical to `[General Poll]`.

**(b) Runtime**: `SASTEST.HLP` — *"The `[Status Messages]` section works in the same manner as the
General poll section with the exception that the program looks at the Status Byte if it is denoted in
the message sent."* i.e. it is consulted only when the outgoing command declared `STATUS=y` (or, in
practice, for EFT responses).

**(c) Size**: 21 entries, `$00`-`$14`, contiguous, no gaps.

| Code | Meaning | Code | Meaning |
|---|---|---|---|
| `$00` | Operation successful | `$0B` | Request already completed |
| `$01` | No denomination set | `$0C` | Gaming machine disabled |
| `$02` | Door open | `$0D` | Gaming machine out of service |
| `$03` | Partial credit amount due to denomination | `$0E` | Gaming machine busy |
| `$04` | Partial credit amount due to credit limit | `$0F` | No win cashout to host in progress |
| `$05` | Invalid transaction number | `$10` | ERROR! (non-BCD data) |
| `$06` | No credit switch | `$11` | ERROR! (already completed) |
| `$07` | In tilt condition | `$12` | ERROR! (disabled) |
| `$08` | Invalid ACK | `$13` | ERROR! (out of service) |
| `$09` | In game play mode | `$14` | ERROR! (busy) |
| `$0A` | Data field contains non-BCD data | | |

Line 719 carries the significant comment: `;The following status codes indicate improper EFT
implementation` — codes `$10`-`$14` are **conformance failures**, deliberately distinguished from the
legal `$0A`-`$0E` equivalents. The legacy tool prints them in the same colour as everything else.

**New app**: this is a ready-made **assertion vocabulary**. `$00` = pass; `$01`-`$0F` = legitimate
refusals that a test may or may not expect; `$10`-`$14` = **hard conformance failures that must fail
a test run automatically**. Encode that severity in the table.

---

## 8. `[Messages]` (lines 727-1262) — THE RESPONSE DECODER DSL

This is the crown jewel: a declarative, offset-and-length-based binary field decoder for SAS
responses, with a cursor mode for variable-length data. **395 field descriptors covering 117 command
codes.** Every line in the section parses under the grammar below (verified programmatically: zero
unparsed lines).

### 8.1 Exact grammar

```
<sigil><XX> <label-text> , <offset> , %<fmt><len>
```

Formally (regex that matched all 395 lines):

```
^([$&*])([0-9A-Fa-f]{2})(.*?),(\d{2}),%([adA])(\d{2})\s*$
```

| Element | Rule |
|---|---|
| `<sigil>` | `$` = ordinary field · `&` = message header/title line · `*` = length-byte marker. |
| `<XX>` | 2 hex chars — the **long-poll code of the response** being described. Groups all lines for one command. |
| `<label-text>` | everything up to the second-to-last comma. Conventionally `" = <English name> = "` but the tool takes it verbatim; it may contain `=`, spaces, brackets, and even ASCII art. **Commas are forbidden** (field separator). Trailing `=` and padding are cosmetic. |
| `<offset>` | 2-digit **decimal** byte offset into the received frame, **1-based, counting the address byte as `01` and the command byte as `02`** (stated explicitly in the INI header, line 748). `00` = *cursor mode* (see below). `99` = header sentinel, used only on `&` lines. |
| `%` | mandatory marker introducing the display format. |
| `<fmt>` | one letter: `d` = decimal (BCD/numeric) · `a` or `A` = ASCII. Case is not significant (`%a02` and `%A06` both appear, both ASCII). Census: `d` × 383, `a` × 8, `A` × 4. |
| `<len>` | 2-digit decimal byte count to consume/display. `00` = *runtime length* (see below). |

The INI's own header (lines 728-746) documents the three sigils:

```
; An "&" in front of the messages denotes that this has the overall length byte in it
; A "*" in front denotes that this is a length byte for the following data -
; it must have a 00 in the position and a 00 in the length -
; all following should 00 the position field and specify its length if necessary
```

### 8.2 The two addressing modes — this is the whole trick

**Mode A — absolute offset (fixed-layout responses).**
`offset != 00` : read `len` bytes starting at byte `offset` of the received frame. Fields are
independent; order in the file is display order only. Example, `$1C` "Send meters" (lines 793-800):

```
$1C = Total Coin In Meter   = ,03,%d04
$1C = Total Coin Out Meter  = ,07,%d04
$1C = Total Drop Meter      = ,11,%d04
$1C = Total Jackpot Meter   = ,15,%d04
$1C = Games Played Meter    = ,19,%d04
$1C = Games Won Meter       = ,23,%d04
$1C = Door Open Meter       = ,27,%d04
$1C = Power Reset Meter     = ,31,%d04
```

Offsets step 03, 07, 11, 15 … — **decimal, +4 each**, proving the offsets are decimal not hex.

Mixed ASCII/decimal example, `$1F` "gaming machine ID & information" (lines 815-822):

```
$1F = Gaming Machine ID = ,03,%a02      <- 2 ASCII chars at byte 3
$1F = Additional ID     = ,05,%a03      <- 3 ASCII chars
$1F = Denomination      = ,08,%d01      <- 1 numeric byte (decode via [Denominations])
$1F = Max Bet           = ,09,%d01
$1F = Progressive Group = ,10,%d01
$1F = Game Options      = ,11,%d02
$1F = Pay Table ID      = ,13,%A06      <- 6 ASCII chars
$1F = Base Percentage   = ,19,%A04
```

**Mode B — cursor / TLV mode (variable-length responses).**
`offset == 00` : read `len` bytes **from the running cursor**, then advance the cursor. `len == 00`
means "the length was supplied at runtime by the preceding `*` line". A `*` line itself reads **one**
length byte (it is written `,00,%d00`) and arms the next `$…,00,%d00` field to consume exactly that
many bytes. An `&` line (`,99,%d00`) declares that the frame carries an overall length byte and
prints the group title.

Canonical example, `$72` AFT transfer-funds response (lines 1058-1080), quoted verbatim:

```
&72 = AFT Transfer Funds:,99,%d00           <- header: frame has an overall length byte
$72 = AFT Buffer Position       = ,04,%d01  <- absolute offsets while layout is fixed
$72 = Transfer Status           = ,05,%d01
$72 = Receipt status            = ,06,%d01
$72 = Transfer Type             = ,07,%d01
$72 = Cashable Amount           = ,08,%d05
$72 = Restricted Amount         = ,13,%d05
$72 = Nonrestricted Amount      = ,18,%d05
$72 = Transfer Flags            = ,23,%d01
$72 = Asset Number              = ,24,%d04
*72 = Transaction ID Length     = ,00,%d00  <- read 1 length byte L
$72 = Transaction ID            = ,00,%d00  <- consume L bytes from cursor
$72 = Transaction Date          = ,00,%d04  <- cursor, fixed 4 bytes
$72 = Transaction Time          = ,00,%d03
$72 = Expiration                = ,00,%d04
$72 = Pool ID                   = ,00,%d02
*72 = Total Cashable Size       = ,00,%d00  <- read 1 length byte
$72 = Total Cashable Meter      = ,00,%d00  <- consume that many bytes
*72 = Total Restricted Size     = ,00,%d00
$72 = Total Restricted Meter    = ,00,%d00
*72 = Total Nonrestricted Size  = ,00,%d00
$72 = Total Nonrestricted Meter = ,00,%d00
```

The cursor is seeded by the last absolute field (`Asset Number` at 24, 4 bytes long, so the cursor
starts at byte 28). **The transition from absolute to cursor mode happens implicitly at the first
`offset == 00` line.**

Repeating groups are expressed by **manual unrolling**, not by a repeat construct. `$6F` "Extended
Meters" (lines 1007-1056) spells out twelve identical `Code / *Length / Value` triplets:

```
&6F = Extended Meters:,99,%d00
$6F = Game Number      = ,04,%d02
$6F = Meter 1 Code     = ,06,%d02
*6F = Meter 1 Length   = ,00,%d00
$6F = Meter 1          = ,00,%d00
$6F = Meter 2 Code     = ,00,%d02
*6F = Meter 2 Length   = ,00,%d00
$6F = Meter 2          = ,00,%d00
...  (through "Meter 12")
```

`$AF` (lines 1189-1226) is a byte-identical copy of the same 12-way unroll. **That single missing
"repeat until end of frame" construct costs ~100 lines of duplicated INI.**

### 8.3 Every token, enumerated

| Token | Where | Meaning | Real example (file line) |
|---|---|---|---|
| `$` | col 1 | ordinary decoded field | `$1A = Current Credits = ,03,%d04` (785) |
| `&` | col 1 | group header; frame has an overall length byte; always paired with offset `99`, len `00` | `&B5 = Extended Game n Info:,99,%d00` (1244) |
| `*` | col 1 | read one byte as a length; arms the next `,00,%d00` field | `*B5 = Game Name Length = ,00,%d00` (1249) |
| `XX` | cols 2-3 | long-poll code this descriptor belongs to | `$4D` … (912-921) |
| `,NN,` | offset | absolute 1-based byte offset (address = 01, command = 02) | `,03,` |
| `,00,` | offset | cursor mode — continue from the running position | `$72 = Expiration = ,00,%d04` (1072) |
| `,99,` | offset | header sentinel; only ever on `&` lines (all 6 of them) | `&72 = …,99,%d00` (1058) |
| `%d` | fmt | decimal / BCD numeric display | 383 of 395 lines |
| `%a` `%A` | fmt | ASCII display (case-insensitive) | `$54 = Serial Number = ,07,%a10` (962) |
| `NN` len | length | byte count. Census: 04 × 118, 00 × 89, 01 × 77, 02 × 64, 05 × 23, 03 × 10, 09 × 7, 10 × 3, 06 × 2, 08 × 1, 20 × 1 | |
| `00` len | length | runtime length: from the preceding `*` line, or "rest of field" for a `%d00` after a `*` | |

**Purely cosmetic rows.** Some rows exist only to print static text and decode nothing — offset `00`
*and* length `00` with no preceding `*`. `$28` uses them to draw a column-header diagram for the EFT
transaction log (lines 846-849):

```
$28 =                            C T A S [   D    ],00,%d00
$28 =                            M R C T [   A    ],00,%d00
$28 =                            D N K A [   T    ],00,%d00
$28 =                              #   T [   A    ],00,%d00
```

`$94` and `$A8` use the same device to list the meaning of a returned code inline (lines 1146-1148 and
1186-1188):

```
$94 = Reset Code = ,03,%d01
$94     00 - Hand pay was reset,00,%d00
$94     01 - Unable to reset the hand pay,00,%d00
$94     02 - Not in a hand pay condition,00,%d00
```

Note this is a *third* way of expressing an enumeration, after `*50 = 00 = …` in `[Long Poll
Commands]` and the standalone decode-table sections. **The format has three mutually incompatible
mechanisms for the same concept.**

### 8.4 How a message is bound to its structure

Binding is **by long-poll code alone**: sigil-code `XX` of the `[Messages]` group must equal the
command byte of the response. There is no length discrimination, no variant selection, no
version gating. Consequences visible in the file:

* `$18` (line 780-781) declares *two* fields at offsets 03 and 05, correct for the single response
  layout — fine.
* `$0F` (lines 752-757) and `$19` (773-777) and `$1C` (793-800) are separate codes, so no clash.
* But `$6F` / `$AF` must describe **12 meters** even when the game returns 1 — the tool simply runs
  off the end of the buffer for the missing ones. There is no "stop at frame end" rule in the DSL.
* When one code has two request variants with **different** response layouts (e.g. `$71` redeem vs
  `$71` interrogate), only one `[Messages]` group can exist. The file works around this by making the
  two layouts identical (lines 1103-1112).

### 8.5 Reimplementation spec — write a parser from this alone

A faithful evaluator of the legacy DSL:

```
parse:
  for each non-blank, non-';' line in [Messages]:
     m := ^([$&*])([0-9A-Fa-f]{2})(.*?),(\d{2}),%([adA])(\d{2})\s*$
     emit Descriptor{sigil, code=upper(2), label=trim(3), offset=int(4), fmt=lower(5), len=int(6)}
  group descriptors by code, preserving file order

decode(frame, code):                 # frame[0] = address, frame[1] = command
  cursor := null
  pending_len := null
  for d in group(code):
     if d.sigil == '&':              # offset 99, len 00
        print title d.label; note "frame carries overall length byte"; continue
     if d.sigil == '*':              # read one length byte
        pending_len := byte_at(cursor); cursor += 1
        print d.label, pending_len; continue
     if d.offset != 0:
        start := d.offset - 1        # 1-based, address = 01
        n     := d.len
        cursor := start + n          # seed/advance the cursor
     else:
        if cursor == null: cursor := 2          # after address+command
        n := (d.len != 0) ? d.len : take(pending_len ?? 0)
        if n == 0 and pending_len == null:
            print d.label (static text only); continue    # cosmetic row
        start := cursor; cursor += n
     value := frame[start : start+n]
     print d.label, (d.fmt == 'a' ? ascii(value) : bcd_or_decimal(value))
```

**What the new app must add** (each item is something the legacy DSL provably cannot express):

| Gap in the legacy DSL | Evidence | Required in the new format |
|---|---|---|
| No repeat/until-end-of-frame | `$6F`/`$AF` unrolled 12× identically, ~100 lines | `repeat { … } until end_of_frame` or `count: <field-ref>` |
| No length validation | nothing compares declared offsets to the received frame size | bounds-check every field; report "frame shorter than schema" as a test failure |
| No numeric type distinction | `%d` covers BCD, binary and bitmaps alike; the file has to say "(binary)" in prose (`$7B` lines 1198-1201, `$4C` lines 458-460) | explicit `bcd` / `uint_be` / `uint_le` / `bitmap` / `ascii` / `bool` types |
| No units / scaling | "Win Amount ($ and cents)" is a comment (`$84`, line 1130) | `unit: cents`, `scale`, formatted rendering |
| No enumeration binding | `[Denominations]`, `[Ticket Machine Status Codes]`, … are wired by hard-coded `Command`/`Position` keys, not by the field | `enum: <table-name>` on the field |
| No variant discrimination | `$71` request variants share one decoder | `when: length == N` / `when: field X == V` |
| No SAS-version / feature gating | none | `since: 6.02`, `requires_feature: advancedFundsTransfer` |
| No bitfields | `Game Options` (`$1F`, line 820) is printed as a 2-byte decimal | named bit ranges |
| **No assertions** | nothing in the DSL can say "this field must equal X" | `expect:` clause per field — the single biggest addition |
| Three incompatible enum syntaxes | `*50 = vv = txt`; `$94    vv - txt,00,%d00`; standalone `[…Codes]` sections | one enum mechanism |

---

## 9. Decode-table sections — confirmed, sized, and summarised

All of these are flat `KEY = text` lookups. Three of them additionally carry a **binding header** of
`Command` / `Position` / `Message` keys, which is how the tool knows *which byte of which response*
the table decodes. `Command` and `Position` are confirmed as real keys read by the binary
(`exe_strings.txt` lines 6906, 6907).

| Section | Lines | Key syntax | Entries | Binding header | What it decodes |
|---|---|---|---|---|---|
| `[Denominations]` | 1264-1294 | `HH = text` (**no `$`**) | 30 | none | denomination byte; fills the "SAS Denomination" combo used by multi-denom polls (`SASINI.doc`). Codes `00`-`1B` plus `40` and `FF` = "Invalid". |
| `[Ticket Transfer Codes]` | 1296-1309 | `HH = text` | 13 | none | host-to-machine ticket transfer code (`$71` request): `00`-`02` valid, `80`-`88` rejection reasons, `FF` = "Request for current ticket status". |
| `[Ticket Machine Status Codes]` | 1311-1330 | `HH = text` | 19 | none | machine-to-host ticket status (`$70`/`$71` response): `00`-`02` redeemed, `20` waiting for LP 71, `40` pending, `80`-`8B` rejections, `C0` incompatible, `FF` no info. |
| `[Validation Type Codes]` | 1332-1343 | `HH = text` | 11 | none | validation type byte (`$4D`, `$50`, `$7B`). Same 11 values duplicated as `*50 =` lines at 469-480 — **the file stores this table twice, in two different syntaxes.** |
| `[AFT Transfer Status Codes]` | 1345-1377 | `HH = text` | 29 | `Command = 0x72`, `Position = 5`, `Message = AFT FUNDS TRANSFER STATUS` | byte 5 of the `$72` response. `00`/`01` success, `40` pending, `80`-`95` + `9F` + `C0`/`C1` failure reasons, `FF` no info. **The richest error vocabulary in the file.** |
| `[AFT Transaction Receipt Status]` | 1379-1386 | `HH = text` | 4 | `Command = 0x72`, `Position = 6`, `Message = AFT RECEIPT STATUS:` | byte 6 of the `$72` response: `00` printed, `20` in progress, `40` pending, `FF` not requested. |
| `[Receive Validation Status Codes]` | 1388-1394 | `HH = text` | 3 | `Command = 0x58`, `Position = 3`, `Message = Game Returned $58 response with Status Code` | byte 3 of the `$58` response: `00` ack, `80` not in cashout, `81` improper validation rejected. |
| `[ASDC]` | 1396 | (would be long-poll syntax) | **0 — empty** | — | deprecated. `SASTEST.HLP`: identical to Long Poll except the 2nd Y/N parameter selects a `[Bonus Game Data Structures]` entry; *"These commands will be going away in future releases."* |
| `[Bonus Game Data Structures]` | 1397-1398 | `$ID = desc,nBoxes` + `?ID = prompt@n` | **0 — empty** | — | deprecated. `SASTEST.HLP` preserves 5 historical examples: Times Pay Bonus, Generic Pay Coin In/Out Bonus, two Coin In Pick-A-Prize Bonus variants. |

Note the syntax inconsistency: `[General Poll]`, `[Status Messages]`, `[Long Poll Commands]`,
`[EFT]`, `[Messages]` all require a **`$` sigil**; the eight decode tables above use a **bare hex
key**. Two conventions in one file.

**New app**: collapse all of these into one enum registry (`{table_id, code, label, severity,
category}`) and reference tables *from field definitions* rather than from `Command`/`Position`
coordinates. Severity matters: `[AFT Transfer Status Codes]` `00`/`01` = pass, `40` = retry,
`80`-`C1` = fail; `[Status Messages]` `$10`-`$14` = conformance violation. Encoding that turns every
decode into an automatic verdict.

---

## 10. `[Message Structures]` (lines 1399-1407) + `[Data Structures]` (lines 1409-1575)

Together these are the tool's **template mechanism**: `[Message Structures]` defines the *frame
envelope*, `[Data Structures]` defines the *payload*, and the two are bound at runtime by a picker.

### 10.1 `[Message Structures]` — grammar

Identical to `[Long Poll Commands]`, with one extra rule stated in the section's own header
(lines 1400-1403):

```
; @0 lets you pick a Data Structure
; @x is an input box where x is the number of bytes for input
; The $00 in the first Command below for example is only for message grouping
; You must supply the command with the Data Structure below
```

The complete live content is just two entries:

```
$00 = 1 data Structure w/Length,y,n,n,n,n,y,1
?00 = Pick Data Structure@0
$00 = 1 data Structure w/o Length,n,n,n,n,n,y,1
?00 = Pick Data Structure@0
```

* The code `$00` is **not** a SAS command — it is a grouping key only ("only for message grouping").
  The real command byte comes from the chosen data structure.
* Flags: the first variant sets `LENGTH=y` (insert a length byte after the command byte), the second
  `LENGTH=n`. Both set `CRC=y`. That is the entire difference between them.
* `?00 = Pick Data Structure@0` — `@0` turns the input box into a **combo box over the names of all
  `[Data Structures]` blocks** (`SASINI.doc`: *"the number of bytes that are required to enter, if
  the number is 0 you may select from a data structure"*).

**Runtime**: `SASINI.doc` — *"This section is used to build almost any message you wish and allows
you to edit the individual bytes during run time."* It is the sanctioned replacement for the
deprecated `[ASDC]` / `[Bonus Game Data Structures]` mechanism.

### 10.2 `[Data Structures]` — grammar

```
BEGIN
<Structure Display Name> = <HH>          <- FIRST line: name + SAS command byte
<Field Label>            = <hex bytes>   <- 0..n payload fields, concatenated in order
<Field Label>            = "<ASCII>"     <- quoted form: literal ASCII, no hex conversion
END
```

Rules proven by the file:

| Rule | Evidence |
|---|---|
| The block starts with bare `BEGIN` and ends with bare `END`, no `=`. | 9 `BEGIN` / 9 `END` in lines 1409-1575, counted with `awk` |
| The **first** line's *value* is the SAS command byte; its *key* is the human name shown in the picker. | `AFT Restricted to Game = 72` (1411); `Location Name Test = 7C` (1555); `Enter Real Time Event Mode = 0E` (1568) |
| Every subsequent line contributes its value's bytes to the payload, **in file order**. Labels are free text and are never transmitted. | whole section |
| Labels need not be unique and may contain punctuation and parentheses. | `Patron Name (Larry) = 4C61727279` (1428); `Length` appears 3× inside one block (1557, 1560, 1563) |
| Values are **hex digit pairs**, arbitrary length, or a **double-quoted ASCII literal**. | `Registration Key = 0000…0000` (40 hex chars = 20 bytes, 1420); `Data = "LUCKY LARRY'S CASINO"` (1558) |
| Length fields are **not computed** — the author writes them by hand and they must agree. | `Length = 14` (line 1557, hex 0x14 = 20) precedes `"LUCKY LARRY'S CASINO"` (1558) = exactly 20 chars; `Length = 0F` (1563) precedes `"RENO, NV  89521"` (1564) = exactly 15 chars |
| Nested TLV structure is expressed by convention only — `…Code / …Length / …value` triples. | `Patron Name Code = 10` / `Patron Name Length = 05` / `Patron Name (Larry) = 4C61727279` (1426-1428) |
| An outer total-length field is likewise hand-maintained. | `Receipt Total Data Length = 1A` (1425) = 26 = sum of the three following TLVs (2+5 + 2+10 + 2+5 = 26) |

**The 9 live structures**

| # | Line | Name | Cmd | Purpose |
|---|---|---|---|---|
| 1 | 1411 | `AFT Restricted to Game` | `72` | AFT transfer of 20.00 restricted credits to the game, with a 3-TLV receipt block (patron name / account no. / balance) |
| 2 | 1438 | `AFT Debit Ticket` | `72` | debit transfer, transfer type `60`, flags `C0`, receipt TLVs source / card no. / fee |
| 3 | 1465 | `AFT Bonus to Game` | `72` | bonus transfer, transfer type `10`, cashable 2.00, no receipt |
| 4 | 1483 | `AFT All to Host` | `72` | cash-out everything: all three amounts `0099999999`, transfer type `80`, flags `80` |
| 5 | 1510 | `AFT Win to Host` | `72` | win cash-out, transfer type `90` |
| 6 | 1537 | `AFT Reject Host` | `72` | transfer type `80` with transfer code `00` — used to provoke a rejection |
| 7 | 1555 | `Location Name Test` | `7C` | set extended ticket data: location + address1 + address2 as quoted ASCII |
| 8 | 1568 | `Enter Real Time Event Mode` | `0E` | `Enable = 01` |
| 9 | 1573 | `Exit Real Time Event Mode` | `0E` | `Disable = 00` |

Six of nine are `$72` AFT variants — confirming §5.1's finding that `$72` is unreachable from the
command drop-downs and *must* be driven this way.

The older `SASTEST.HLP` copy of the section shows the mechanism was also used for reel/bonus/ASDC
payloads (`ID 0004 THEME 0001 Diamond Mine = FF`, `Link Bonus Game Update = B0`,
`ASDC Command Open Close Live Video= A5`), so the command byte really is arbitrary.

### 10.3 Runtime and reimplementation

`SASINI.doc`:

> "A data structure is probably one of the most versatile pieces here … At run time this data can be
> changed by selecting the **Command Set Structure Editor** tab, then selecting from a drop down that
> will allow the user to select the structure and change the data **which will last until the program
> is exited**."

So: templates are editable in-session but **edits are never persisted**. That is a major defect — a
carefully tuned AFT payload is lost on exit.

**New app** — port this concept, fix these five things:

1. **Compute lengths.** `Length`, `Transaction ID Length`, `Receipt Total Data Length` must be derived
   from the data, not hand-typed. Every hand-typed length in the file is a latent bug.
2. **Real nesting.** Replace the `Code / Length / value` naming convention with actual nested TLV
   objects, so a receipt block is a list of typed entries.
3. **Persist edits**, version them, and let a structure be saved under a new name.
4. **Bind by schema, not by picker.** A structure declaring command `72` should be validated against
   the `$72` request schema before it is sent, so a malformed payload is caught at edit time.
5. **Parameterise.** Asset number, transaction ID, amounts and expiry should be variables/expressions
   (`${asset}`, `${next_transaction_id}`, `${now+30d}`), not frozen literals — today `Asset Number =
   09030000` is baked into six structures and `[AFT] Asset Number = 777` disagrees with all of them.

---

## 11. `[Quick Commands]` (lines 1577-1584) — one-click raw frames

**(a) Grammar**: one **bare hex string per line**, no `=`, no label, no comments used in the live
file. Whitespace-trimmed. Case-insensitive hex.

**(b) Runtime**: `SASINI.doc` — *"allows the user to define any command verbatim and that command
will show up on the Quick Command page. All that is required to send that command is to click on it."*
Sent at the next poll cycle. **CRC rule: "any command over 2 bytes in length will have a CRC
automatically attached to it."** So a 1- or 2-byte line is sent as-is; anything longer gets the
16-bit SAS CRC appended. The `Quick Cmd` tab renders one clickable row per line.

**(c) Decode of the 7 live entries** (address byte `01` throughout; `81` = `0x01 | 0x80`):

| Line | Raw | Bytes | What it does |
|---|---|---|---|
| 1578 | `017202ff00` | `01 72 02 FF 00` (+CRC) | **LP $72 AFT transfer funds**, length `02`, transfer code `FF` = *interrogate*, transaction index `00`. "What is the status of the current AFT transfer?" |
| 1579 | `017301ff` | `01 73 01 FF` (+CRC) | **LP $73 AFT register gaming machine**, length `01`, registration code `FF` = *read current registration status*. |
| 1580 | `017400019000` | `01 74 00 01 90 00` (+CRC) | **LP $74 AFT game lock/status**: lock code `00` = request lock, lock condition `01`, lock timeout `9000` BCD = 90.00 s. |
| 1581 | `0174ff000000` | `01 74 FF 00 00 00` (+CRC) | **LP $74 interrogate**: lock code `FF` = query status only, condition `00`, timeout `0000`. |
| 1582 | `014C000001000000` | `01 4C 000001 000000` (+CRC) | **LP $4C Set enhanced validation ID**: machine ID = `000001`, starting sequence number = `000000`. |
| 1583 | `81` | `81` | **General poll** to address 1. 1 byte, no CRC. |
| 1584 | `016F0e0000000001000200030004000d00` | `01 6F 0E` + 14 payload bytes (+CRC) | **LP $6F Extended meters**, length `0E` = 14: game number `0000`, then six 2-byte meter selectors as literally encoded: `0000 0100 0200 0300 0400 0D00`. (If the author intended big-endian codes 0000/0001/0002/0003/0004/000D the bytes are shifted by one — UNKNOWN which was meant; the file as written produces the former.) |

The 2017 copy additionally carried `80` (sync/wake-up) and a duplicate `81`.

**(d) New app**: keep one-click raw send — it is genuinely useful — but (i) label each entry,
(ii) substitute `${address}` instead of a hard-coded first byte, (iii) show the *decoded* frame
alongside the hex before sending, (iv) group entries into folders, (v) make "append CRC" an explicit
per-entry flag instead of an implicit "> 2 bytes" rule.

---

## 12. `[Script]` (lines 1586-1595) — the scripting facility, and why it cannot drive tests

**(a) Exact grammar** — this is the whole language:

```
<hex frame>[ = <English text>]
```

* one step per line, in execution order;
* `<hex frame>` is a bare hex string, exactly as in `[Quick Commands]`;
* the optional `= <text>` after it is **display only** — it is printed in the log window when the step
  runs. It has no effect on the frame;
* `;` at the start of a line comments the step out;
* the older `_________sastest.ini` shows lines with **no** `= text` at all (`0110`, `0111`, `0115`, …)
  and lines with text; the INI carries the warning `;make sure there are "=" or it wont work.` just
  above `[Meter Test]` (line 1635), so at least in `[Meter Test]` the `=` is mandatory. Whether a
  bare line is honoured in `[Script]` is UNKNOWN.

Live content, in full:

```
[Script]
80 = Sending Sync
81 = General poll
;0193 = Unimplemented
;8100 = General Poll PLUS!
;0111 = Coin In
;0112 = Coin Out
;007f0308200114301424ca = set clock
;017e = get time 1
;01fe = unsupported
```

**(b) Runtime**

* `SASINI.doc`: *"This section allows for verbatim scripting, the commands are sent out as entered
  here and there is an English statement shows up in the Text window. Note that any command over 2
  bytes in length will have a CRC automatically attached to it. **Scripting short circuits all
  automatic polling so what is in the script is the only thing sent.**"*
* UI: `Scripting` tab → `Script` page, holding a `Scripting` **checkbox** (`ScriptingClick`) and two
  list boxes (`ListBox1`, `ListBox2`). Ticking the box replaces the normal poll loop with the list.
* `SASTEST.HLP`, on the report tool: *"This feature doesn't work with scripting."* Confirmed by the
  form resource `&This feature only works with scripting` on the Meter Test tab — i.e. Meter Test and
  Report are mutually exclusive.

**(c) What the script language CANNOT express — proven, item by item**

| Capability | Present? | Proof |
|---|---|---|
| **Loops / repeat N times** | **NO** | The only production is `hex[= text]`. The only `Repeat*` symbols in the binary are `RepeatButton` / `RepeatButtonClick`, whose DFM `Caption` is **`Send Command`** (`exe_strings.txt` 9163-9172) — a manual resend button — and `ProgRepeat`, which belongs to the Progressive broadcast tab. |
| **Waits / delays / sleeps** | **NO** | No delay token in the grammar; `grep -i '^(delay|wait|pause|sleep)' exe_strings.txt` returns only Win32 API names (`WaitCommEvent`, `WaitForSingleObject`, `WaitMessage`). Step pacing is the fixed `Poll Rate = 200`. |
| **Assertions / expected responses** | **NO** | Nothing in the grammar refers to a response at all. The only `Assertion failed` strings (7655, 8524) are the Borland C runtime's own. |
| **Variables / parameters** | **NO** | Frames are frozen literals — that is precisely why the address byte had to be rewritten on 15 lines when the address changed (§1). |
| **Conditionals / branching** | **NO** | No `if`, no jump, no label. Steps run strictly top to bottom. |
| **Capturing a response field** | **NO** | Responses are decoded to *screen text* by `[Messages]`; no construct binds a decoded field to a name for reuse. Chaining (e.g. take the sequence number from `$4D`, feed it to `$58`) is impossible. |
| **Named / multiple scripts** | **NO** | One `[Script]` section per file. Users switch scripts by commenting blocks out — visible in `_________sastest.ini` as `;script 1 - default scripts` and `; script 2 - used for Implied NAK`. |
| **Pass/fail verdict, exit code, machine-readable result** | **NO** | Output is a scrolling text window plus an optional manual `SasReport.txt` copy-paste — and the report tool is disabled while scripting. |
| **Error injection / negative testing** | **PARTLY, by accident** | Only by hand-typing a malformed frame (`;0193 = Unimplemented`, `;01fe = unsupported`, `;8100 = General Poll PLUS!`). No systematic bad-CRC / truncation / timing fuzzing. |
| **Interleaving with normal polling** | **NO** | Scripting *replaces* the poll loop ("short circuits all automatic polling"). A script cannot say "poll normally, and when exception $6F arrives do X". |

**This is the complete proof of the user's core complaint.** `[Script]` is a *macro recorder replay
list*, not a test language: it can send bytes in a fixed order and print a caption. It cannot observe,
decide, wait, repeat, parameterise, or report a verdict. Every meaningful automation in the tool is
instead **hard-coded into checkboxes** — `Auto Redemption`, `Auto Register`, `Auto Interrogate`
("Check this box to auto interrogate when 69 is recieved", `exe_strings.txt` 10727), `Auto Increment
Validation Num.`, `Auto Initialize Validation`, `Auto Inc TID`, `Auto respond to req. for handpay
info`, `No Automatic processing`. None of them is configurable from the INI, none is composable, none
is scriptable.

**(d) New app — the replacement must provide, minimally:**

1. **Named, versioned test cases** stored as files, selectable and runnable individually or as suites.
2. **Send steps with symbolic parameters**: `send $6F { game: 0, meters: [0x0000, 0x000D] }` —
   address, CRC and length computed, never typed.
3. **Expect steps**: `expect response $6F within 200ms` · `expect field "Meter 1" == 12345` ·
   `expect exception $3D` · `expect status != $10..$14` · `expect NACK` · `expect timeout`.
4. **Capture and reuse**: `capture seq := $4D."Sequence Number"` then `send $58 { validation: seq }`.
5. **Control flow**: loops with counts and until-conditions, delays, conditionals, setup/teardown.
6. **Concurrent normal polling** with exception-driven hooks (`on exception $6F: …`).
7. **A verdict**: per-step pass/fail, machine-readable results (JUnit XML / JSON), non-zero exit code,
   and a full frame trace attached to each failure — so it can run in CI unattended.
8. **Negative testing as a first-class citizen**: deliberate bad CRC, wrong length, truncated frame,
   out-of-range values, wrong address, timing violations.

---

## 13. `[AFT]` (lines 1597-1634) — AFT default field values

**(a) Grammar**: `Key = Value`, free-text or hex values, `;` comment lines used as visual group
headers (`;transfer funds`, `;receipt data`). Values are **not** normalised — some are ASCII prose
("Ima Winner"), some are BCD hex ("0000010000"), one is decimal ("777").

**(b) Runtime**: these are the **initial contents of the AFT tab edit boxes** (`Registration / Lock`,
`Transfer Funds`, `Receipt`). They are loaded once at start-up. Every key below is confirmed present
in the binary's string table (`exe_strings.txt` lines 6753-6771 for the receipt block, 6804-6812 for
the transfer block), **except `Transfer code` and `Transfer type`**, which `grep -i -x` does **not**
find — they are dead keys, exactly like `Setup File` and `DTR`.

**(c) The complete AFT field surface — every field the tool can populate**

| Group | INI key | Live value | Type / width | Used by |
|---|---|---|---|---|
| Registration | `Asset Number` | `777` | 4 bytes binary (`?73 = Enter asset number (binary)@4`) | `$73`, `$72`, `$7B`, `$74`, `$B7` |
| Registration | `Registration Key` | `0102030405060708091011121314151617181920` | 20 bytes hex (`?73 …@20`) | `$73` |
| Registration | `POSID` | `44332211` | 4 bytes (`?73 = Enter POS ID@4`) | `$73` |
| Transfer | `Transfer code` | `00` | 1 byte | **DEAD KEY — not read by the exe** |
| Transfer | `Transfer type` | `00` | 1 byte | **DEAD KEY — not read by the exe** |
| Transfer | `Cashable Amount` | `0000000000` | 5 bytes BCD (10 digits, cents) | `$72` |
| Transfer | `Restricted Amount` | `0000000000` | 5 bytes BCD | `$72` |
| Transfer | `Nonrestricted Amount` | `0000000000` | 5 bytes BCD | `$72` |
| Transfer | `Transaction ID` | `Test Transaction` | variable-length ASCII, preceded by a length byte | `$72` |
| Transfer | `Expiration` | `05302003` | 4 bytes BCD `MMDDYYYY` | `$72` |
| Transfer | `Pool ID` | `0c00` | 2 bytes | `$72` |
| Receipt | `Transfer source/destination` | `From Primary Account` | ASCII TLV | `$72` receipt data |
| Receipt | `Date and time` | `07172003010203` | 7 bytes BCD `MMDDYYYYHHMMSS` | `$72` receipt |
| Receipt | `Patron name` | `Ima Winner` | ASCII TLV (code `10`) | `$72` receipt |
| Receipt | `Patron acct#` | `123ABC456DEF` | ASCII TLV (code `11`) | `$72` receipt |
| Receipt | `Account Balance` | `0000010000` | 5 bytes BCD (code `13`) | `$72` receipt |
| Receipt | `ATM card#` | `1234` | TLV (code `41`) | `$72` debit receipt |
| Receipt | `Transaction fee` | `0000000032` | 5 bytes BCD (code `42`) | `$72` debit receipt |
| Receipt | `Total debit amount` | `0000000132` | 5 bytes BCD | `$72` debit receipt |
| Ticket text | `Location` | `Default Location` | ASCII | `$7D` / `$7C` |
| Ticket text | `Address 1` | `Default Address 1` | ASCII | `$7D` |
| Ticket text | `Address 2` | `Default Address 2` | ASCII | `$7D` |
| Ticket text | `In House Line 1..4` | `In House Line n` | ASCII ×4 | `$7C`/`$76` custom ticket data |
| Ticket text | `Debit Line 1..4` | `Debit Line n` | ASCII ×4 | `$7C`/`$76` custom ticket data |

30 keys, 28 live.

**AFT long polls the tool supports**

| Poll | Where defined | Fields |
|---|---|---|
| `$72` AFT transfer funds | **NOT in any command section** — only `[Quick Commands]` line 1578, the six `[Data Structures]` `72` templates, and the hard-coded `Transfer Funds` tab | transfer code, transaction index, transfer type, cashable/restricted/nonrestricted amount, transfer flags, asset number, registration key, transaction ID (+length), expiration, pool ID, receipt TLV block |
| `$73` AFT register gaming machine | `[Long Poll Commands]` 522 (4 boxes) and 528 (interrogate, 1 box) | registration code@1, asset number@4 binary, registration key@20, POS ID@4 |
| `$74` AFT game lock and status | `[Long Poll Commands]` 531 | lock code@1, lock condition@1, lock timeout@2 |
| `$76` set custom AFT ticket data | `[Long Poll Commands]` 536 | function@1, data code@1, data@0 |
| `$7B` extended validation status | `[Long Poll Commands]` 541 | control mask@2 binary, control states@2 binary, cashable expiration@2, restricted expiration@2 |

Responses are decoded by `&72`/`$72` (22 descriptors), `$73` (5), `$74` (12), `*76`/`$76` (2),
`$7B` (4) in `[Messages]`, and the status bytes by `[AFT Transfer Status Codes]` (29 codes) and
`[AFT Transaction Receipt Status]` (4 codes).

**(d) New app**: make AFT a **typed transaction object** with computed lengths, an auto-incrementing
transaction ID, expiry expressed as a date (not `MMDDYYYY` hex the operator types), and a receipt
block built from typed TLV entries. Validate against the `[AFT Transfer Status Codes]` table
automatically and fail the test on `80`-`C1`. Critically: **`$72` must be a first-class, schema-driven
command**, not a hex string in `[Quick Commands]`.

---

## 14. `[Meter Test]` (lines 1636-1659) — the meter-test sequence

**(a) Grammar**: identical to `[Script]` — `<hex frame> = <English text>`, one step per line. The
`=` is **mandatory** here; line 1635 carries the warning `;make sure there are "=" or it wont work.`
`;` comments a step out (line 1659: `;008001010001234020 = Sending Progressive`).

**(b) The 22 live steps** (live file uses address `01`; the 2013/2017 copies used `02`):

```
0111  = test                             <- $11 total coin in
0112  = test                             <- $12 total coin out
0113  = test                             <- $13 total drop
0114  = test                             <- $14 total jackpot
010F  = get meters                       <- $0F meters $10-$15
012F03000000 = get selected meters       <- $2F len 03, game 0000, meter 00
012F03000001 = get selected meters       <- ... meter 01
012F03000002 ... 012F03000009            <- meters 02..09, ten $2F polls in all
0135 = send $20.00 bill meter            <- $35
81   = Sending General Poll
0136 = send $50.00 bill meter            <- $36
81   = Sending General Poll
0137 = send $100.00 bill meter           <- $37
81   = Sending General Poll
80   = Sending Re-Sync
;008001010001234020 = Sending Progressive
```

Note the pattern `<meter poll> / 81 general poll` — the general poll is interleaved manually so that
exceptions raised by the meter poll are drained. There is no construct for this; it is typed out.

**(c) Runtime — this is the closest thing the tool has to an automated test.** The `Meter Test` tab
(DFM resources, `exe_strings.txt` 9878-9975) contains:

* group box **`Individual Meter Test`** with three combos — `MonitoredMeterCmd` ("Meter"),
  `MonitoredMeterPos` ("Position"), `MonitoredMeterBytes` ("How Many Bytes");
* checkbox **`Monitor Meter`** (`MonitorMeterClick`);
* label **`Times Meter Changed = `** with a live counter;
* button **`Meter Test`** (`MeterTestClick`) that runs the `[Meter Test]` list;
* label **`&This feature only works with scripting`**.

So the tool can: replay a fixed poll sequence, watch one nominated meter (by command + byte offset +
width, i.e. hand-entered `[Messages]`-style coordinates) and **count how many times its value
changed**. That is the entire automated-verification capability of the product: *one integer,
manually configured, no expected value, no pass/fail.*

**(d) New app**: generalise to **meter snapshots and deltas**. Snapshot the whole meter set before and
after an action, diff it, and assert expectations (`coin_in delta == 100`, `games_played delta == 1`,
`total_drop unchanged`). Monitor arbitrarily many meters by *name*, not by byte offset. Alert on
non-monotonic meters, rollovers, and cross-meter invariants (SAS requires
`total_coin_out + total_jackpot == total_won`, stated in this very file's `[Active Help]` `$14` topic).

---

## 15. `[Exception Handling]` (1661-1662), `[LongPoll Handling]` (1664), `[RNGMode]` (1665)

| Section | Live content | Read by exe? | Assessment |
|---|---|---|---|
| `[Exception Handling]` | one line: `$7E = $7E` | YES — `exe_strings.txt` 6901 | Grammar is `$<exception code> = $<long poll code>`. The single live entry maps exception `$7E` ("Game has started") to long poll `$7E` ("Send current date and time"). The most plausible semantics — **"when this exception arrives, automatically issue this long poll"** — fits both the key/value shape and the section name, but is **not documented in `SASINI.doc` or `SASTEST.HLP` and is not otherwise confirmed: INFERRED, treat as UNKNOWN.** |
| `[LongPoll Handling]` | **empty** | YES — `exe_strings.txt` 6902 | Presumably the same mechanism keyed on a long-poll response instead of an exception. No live data anywhere. UNKNOWN. |
| `[RNGMode]` | **empty** | YES — `exe_strings.txt` 6813-6897 | The binary reads keys `Mode`, `Cycle`, `Rate`, `Games`, `Frequency`, plus indexed `File%d`, `Frequency%d`, `Mult%d`, and loads files named `DD2K_%d.BIN`. Error strings `File not Found` and `ERROR Invalid Line # Shutting down game!!!` sit immediately after. So: an **RNG-sequence replay mode** driving the game from binary outcome files. Never configured in any of the four INI variants. UNKNOWN in detail. |

**New app**: `[Exception Handling]` / `[LongPoll Handling]`, however thin, are the tool's *only*
reactive-automation hook, and they are exactly the right idea. Generalise them into proper event
rules: `on exception $6F -> send $74 {lock: interrogate}; assert …`, with multiple actions,
conditions and per-rule enable/disable — and make them observable in the log.

---

## 16. `[Active Help]` (lines 1667-1733) — inline protocol documentation

**(a) Grammar**

```
Active = 1                      <- section flag; key confirmed in the exe (exe_strings.txt 6854)
BEGIN = $XX
<free-form prose, any number of lines, blank lines allowed>
END
```

* `BEGIN = $XX` opens a topic keyed by the **long-poll / message code** in hex.
* Everything until the next bare `END` is the topic body, verbatim.
* Blank lines inside a topic are preserved; blank lines between topics are ignored.
* Extraction rule for a converter: `for each block matched by /^BEGIN = \$([0-9A-F]{2})\s*$/ …
  /^END\s*$/ : topic[code] = body`. The first body line is conventionally a title
  (`0x11=Total Coin In Meter`, `0x15 - Games Played Meter`) — note the two different separators
  (`=` and ` - `), so a converter should split on the first `=` or `-` and fall back to the whole line.

**(b) Runtime**: with `Active = 1` the tool shows the matching topic when the operator selects that
command — context-sensitive protocol help. `[ACTIVE HELP]` also appears as an upper-case literal in
the binary (`exe_strings.txt` 6855), suggesting the section header match is case-insensitive.

**(c) Size — 7 topics**, at lines 1669, 1678, 1695, 1701, 1706, 1715, 1729:

| Topic | Title line | Length | Substance |
|---|---|---|---|
| `$00` | ACK / NACK | 5 lines | Defines ACK as a single-byte response containing the machine address, and NACK as `address OR 0x80` sent on a malformed message / bad CRC. |
| `$01` | `0x01=Shutdown Command` | 14 lines | Full behavioural contract: disable all inputs except cash-out and change/attendant; complete the current game cycle including double-up; award pending bonuses; resulting win becomes a hand-pay; then disable. |
| `$11` | `0x11=Total Coin In Meter` | 2 lines | "Total credits wagered on the gaming machine from all sources." |
| `$12` | `0x12=Total Coin Out Meter` | 2 lines | "Total value of all credits directly paid by the gaming machine as a result of winning wagers." |
| `$13` | `0x13=Total Drop Meter` | 6 lines | Composition of total drop (bill acceptor + coin + redeemed tickets); notes that ticket inclusion must be reported in the `$A0` response. |
| `$14` | `0x14=Total Jackpot Meter` | 11 lines | The richest topic: attendant-paid wins and external bonus awards; **states the invariant `total coin out + total jackpot == total won credits`**; hand-paid cancelled credits never counted here. |
| `$15` | `0x15 - Games Played Meter` | 3 lines | Total games played; used for average wager per game. |

**(d) Is the text extractable into a reusable inline-help knowledge base? YES — trivially.**
The block format is unambiguous, code-keyed and machine-parseable with the two-regex rule above. All
seven topics are clean prose with no markup, and each is already keyed to the exact code the new app
will use as its own primary key.

**The new app should absolutely inherit this, and go much further.** The mechanism is excellent and
the content is 7/118 complete — the legacy tool ships help for 6 % of its own command surface. The
replacement should:

* ship the same code-keyed topic store, seeded with these 7 topics verbatim (they are IGT's own
  wording of the SAS contract);
* extend coverage to all 118 long-poll codes, 121 known exceptions and every status table;
* make each topic carry the *testable* assertions it states in prose — the `$14` invariant
  `coin_out + jackpot == won` should be a **runnable check**, not a paragraph;
* render help beside the decoded frame, not in a modal.

Extraction command that works today:
`awk '/^BEGIN = \$/{c=$3; body=""; next} /^END$/{print c": "body; c=""} c!=""{body=body"\n"$0}' sastest.ini`

---

## 17. Section-by-section summary table

| Section | Lines | Kind | Size | Ported as |
|---|---|---|---|---|
| `[SAS Protocols]` | 5-14 | settings | 9 keys (2 dead) | connection profile object |
| `[Communications]` | 16-19 | settings | 3 keys (1 dead, 1 commented) | transport options |
| `[General Poll]` | 21-277 | decode table | 255 codes (121 named) | typed exception registry |
| `[Long Poll Commands]` | 278-658 | command catalogue | 122 entries / 118 codes | command schema |
| `[Electronic Funds Transfer]` | 660-701 | command catalogue | 18 entries | command schema, phase-aware |
| `[Status Messages]` | 703-725 | decode table | 21 codes | enum + severity |
| `[Messages]` | 727-1262 | **decoder DSL** | 395 descriptors / 117 codes | **typed binary schema — the core port** |
| `[Denominations]` | 1264-1294 | decode table | 30 codes | enum |
| `[Ticket Transfer Codes]` | 1296-1309 | decode table | 13 codes | enum |
| `[Ticket Machine Status Codes]` | 1311-1330 | decode table | 19 codes | enum + severity |
| `[Validation Type Codes]` | 1332-1343 | decode table | 11 codes (duplicated at `*50`) | enum |
| `[AFT Transfer Status Codes]` | 1345-1377 | decode table + binding | 29 codes, `Command`/`Position`/`Message` | enum + severity |
| `[AFT Transaction Receipt Status]` | 1379-1386 | decode table + binding | 4 codes | enum |
| `[Receive Validation Status Codes]` | 1388-1394 | decode table + binding | 3 codes | enum |
| `[ASDC]` | 1396 | command catalogue | **empty**, deprecated | drop |
| `[Bonus Game Data Structures]` | 1397-1398 | command catalogue | **empty**, deprecated | drop |
| `[Message Structures]` | 1399-1407 | envelope templates | 2 entries | frame builder |
| `[Data Structures]` | 1409-1575 | payload templates | 9 `BEGIN…END` blocks | typed, persisted, parameterised templates |
| `[Quick Commands]` | 1577-1584 | raw replay | 7 frames | labelled quick-send with variables |
| `[Script]` | 1586-1595 | sequential replay | 2 active + 7 commented | **real test language** |
| `[AFT]` | 1597-1634 | defaults | 30 keys (2 dead) | typed AFT transaction object |
| `[Meter Test]` | 1636-1659 | replay + 1 counter | 22 steps | meter snapshot/delta assertions |
| `[Exception Handling]` | 1661-1662 | event rule | 1 rule | event-rule engine |
| `[LongPoll Handling]` | 1664 | event rule | **empty** | event-rule engine |
| `[RNGMode]` | 1665 | RNG replay | **empty** | UNKNOWN — investigate before dropping |
| `[Active Help]` | 1667-1733 | help corpus | 1 key + 7 topics | code-keyed knowledge base |
| `[end]` | 1735 | terminator | — | n/a |

---

## 18. Systemic defects of the INI as a format (the case for replacing it)

1. **No schema, no validation.** The file's own first line is `;WARNING WARNING / ;Errors in this file
   cause unexpected results`. `SASINI.doc`: *"errors in the INI file may render the tester unusable
   and cause it to crash."* `SASTEST.HLP`: *"The program at this time does not have any error checking
   for the format of the ini file."*
2. **Positional CSV with a forbidden character.** Commands are `desc,y,n,n,n,n,y,2`; a comma anywhere
   in a description silently corrupts the entry. The header comment says so explicitly (line 296).
3. **Two comment conventions, two key sigils, three enum syntaxes.** `$XX` vs bare `HH`;
   `*50 = vv = txt` vs `$94    vv - txt,00,%d00` vs standalone `[…Codes]` sections.
4. **Documentation drifts from the file.** The `[Long Poll Commands]` header calls field 5 `CRYPT`;
   both vendor documents call it `STATUS`.
5. **Dead keys ship in the live file.** `Setup File`, `DTR`, `[AFT] Transfer code`, `[AFT] Transfer
   type` are read by nobody. Two keys the exe *does* read (`Length`, `Auto Initialize`) exist in no
   variant.
6. **Undocumented tokens ship in the live file.** The 8th field `b` on `$08`; the `*XX = v = t`
   enum lines; the `,99,` sentinel.
7. **Manual unrolling instead of repetition.** ~100 lines duplicated between `$6F` and `$AF`.
8. **Hand-maintained lengths.** Every `Length =` in `[Data Structures]` is a bug waiting to happen.
9. **Hard-coded addresses.** Changing the machine address means editing every frame in three sections.
10. **No persistence of runtime edits.** `[Data Structures]` edits die with the process.
11. **One of everything.** One script, one meter test, one quick-command list, one config — no named,
    versioned, selectable artefacts.
12. **No verdict.** Nothing in the entire 1735-line file can express "this is what I expect to
    happen", so nothing can fail. The tool can only *show*; it can never *check*.

---

## 19. Capability checklist the replacement must satisfy

Everything below is derived from something in this file, not invented.

**Must inherit (parity — the tool genuinely does these)**

| # | Capability | Source in the INI |
|---|---|---|
| 1 | Data-driven command catalogue, 118 long-poll codes + 18 EFT, with named variants and per-parameter byte widths | `[Long Poll Commands]`, `[Electronic Funds Transfer]` |
| 2 | Data-driven response decoder: absolute-offset fields *and* a cursor/TLV mode with runtime length bytes | `[Messages]` 395 descriptors, `$`/`&`/`*` sigils |
| 3 | Exception naming for all 256 general-poll codes (121 already written) | `[General Poll]` |
| 4 | 8 enum decode tables (denominations, ticket transfer, ticket machine status, validation type, AFT transfer status, AFT receipt status, receive-validation status, EFT status) | §9 |
| 5 | Reusable, runtime-editable payload templates bound to a command byte | `[Data Structures]` + `[Message Structures]` |
| 6 | One-click raw frame send with automatic CRC | `[Quick Commands]` |
| 7 | Sequential frame replay that suspends normal polling | `[Script]` |
| 8 | Meter-poll sequence + single-meter change counter | `[Meter Test]` |
| 9 | Reactive rule: exception → automatic long poll | `[Exception Handling]` `$7E = $7E` |
| 10 | Code-keyed inline protocol help | `[Active Help]`, 7 topics |
| 11 | AFT transaction and receipt field defaults (28 live fields) | `[AFT]` |
| 12 | TX/RX hex trace with response-time measurement and CRC-error flagging | exe strings `TX>=`, `RX<=`, ` ms response time)`, ` <-CRC Error !` |
| 13 | Report export | exe strings `SasReport.txt`, `TXT File (*.txt)` |
| 14 | Saveable settings profiles | `.SET` / `.AFT` file dialogs in the exe |

**Must add (the tool provably cannot do these — see §12)**

| # | Capability | Why |
|---|---|---|
| 15 | A real test language: loops, waits, conditionals, variables, setup/teardown | `[Script]` has none of them |
| 16 | Assertions on decoded fields, exceptions, status bytes and timing | nothing in 1735 lines can express an expectation |
| 17 | Capture-and-reuse of decoded response fields | no binding construct exists |
| 18 | Pass/fail verdict, machine-readable results, non-zero exit code, CI-runnable headless mode | output is a text window only |
| 19 | Named, versioned, multiple test cases / scripts / quick-command sets | one of each per file |
| 20 | Symbolic address and computed lengths/CRC everywhere | address hard-coded in 3 sections; lengths hand-typed |
| 21 | Schema validation of the config itself, with clear errors | *"errors in this file cause unexpected results"* |
| 22 | Explicit field types (BCD / binary / ASCII / bitmap / bitfield) and units | `%d` conflates all numeric encodings |
| 23 | Repeat/until-end-of-frame in the decoder | `$6F`/`$AF` unrolled 12× |
| 24 | Expected-vs-actual feature-flag comparison against `conf.ini` | `$A0` printed as 3 raw bytes |
| 25 | Meter snapshot/delta assertions and cross-meter invariants | only "Times Meter Changed" |
| 26 | Event-rule engine (multiple actions, conditions) generalising `[Exception Handling]` | 1 rule, undocumented |
| 27 | Systematic negative testing (bad CRC, truncation, wrong length/address, timing) | only by hand-typing bad hex |
| 28 | Concurrent polling with exception hooks | scripting *replaces* polling |
| 29 | Persisted runtime edits to templates | *"will last until the program is exited"* |
| 30 | Help coverage for all 118 codes, with the stated invariants made runnable | 7 of 118 topics today |

---

## 20. Open questions / UNKNOWNs

1. The 8th field `b` on `$08` (line 330) — undocumented in `SASINI.doc`, `SASTEST.HLP` and the INI's
   own header. Best guess: "bitmap/binary input". **UNKNOWN.**
2. Field 5 of the command flag list: the INI header (line 288) says `CRYPT`, both vendor documents say
   `STATUS`. Every line in the file uses `n`, so the file cannot settle it. **UNKNOWN.**
3. `@0` in `[Long Poll Commands]` (`$76`, `$7C`, `$7D`, `$B7`): variable-length input, but whether the
   operator types raw hex or literal ASCII is **UNKNOWN** (the `?B7` prompt says "(ASCII)").
4. Semantics of `[Exception Handling]` / `[LongPoll Handling]`. Key/value shape and section names
   strongly suggest "on event X, send Y", but this is **INFERRED, not confirmed**.
5. `[RNGMode]`: the exe reads `Mode`, `Cycle`, `Rate`, `Games`, `Frequency`, `File%d`, `Frequency%d`,
   `Mult%d` and loads `DD2K_%d.BIN`. No variant configures it. Purpose and file format **UNKNOWN**.
6. `[SAS Protocols]` keys `Length` and `Auto Initialize` are read by the exe but appear in no INI
   variant. **UNKNOWN.**
7. Whether a `[Script]` line without ` = text` is executed or skipped. The `;make sure there are "="`
   warning (line 1635) sits above `[Meter Test]`, yet the 2017 `[Script]` is full of bare lines.
   **UNKNOWN.**
8. `[Quick Commands]` line 1584 `016F0e0000000001000200030004000d00` — the six meter selectors as
   literally encoded are `0000 0100 0200 0300 0400 0D00`; whether the author intended
   `0000 0001 0002 0003 0004 000D` (one byte shifted) is **UNKNOWN**.
9. `TXRXData.dat` (39 357 255 bytes, 2025-02-28) sits beside the exe and is referenced by the string
   `TXRXData.dat` plus `Cannot open input file!!`. It is almost certainly the captured TX/RX traffic
   store. Its record format was **not** decoded here (out of scope for the INI task) but it is worth a
   separate look — it may contain a large corpus of real frames for regression-testing the new
   decoder.
10. `Validation = Enhanced` — the full set of accepted values is **UNKNOWN** from the file (only
    `Enhanced` appears); `SASTEST.HLP` mentions "standard and enhanced".

---

## 21. Appendix — how the numbers in this report were produced

```
grep -n '^\[' /c/IGT3/sastest.ini                      # section map
diff /c/IGT3/sastest.ini.ORG /c/IGT3/sastest.ini       # what the user changed
diff -u /c/IGT3/sastest.ini /c/IGT3/_________sastest.ini
md5sum /c/IGT3/sastest.exe /c/Progetti/libsas/utils/igt/sastest.exe
"/c/Program Files/Strings/strings" -nobanner -n 4 sastest.exe  > exe_strings.txt
"/c/Program Files/Strings/strings" -nobanner -n 5 SASINI.doc   > sasini_doc.txt
"/c/Program Files/Strings/strings" -nobanner -n 6 SASTEST.HLP
grep -A6 '^TTabSheet$' exe_strings.txt                 # UI tab captions from the DFM resources
python  # section extraction + regex parse of every entry; counts, flag census, cross-checks
```

Regexes that matched **every** line of their section with zero failures:

* `[Long Poll Commands]` / `[Electronic Funds Transfer]` command: `^\$([0-9A-Fa-f]{2})\s*=\s*(.*)$`
  then split the remainder on `,`.
* input box: `^\?([0-9A-Fa-f]{2})\s*=\s*(.*?)@(\d+)\s*$`
* enum annotation: `^\*([0-9A-Fa-f]{2})\s*=\s*(.*)$`
* `[General Poll]` / `[Status Messages]`: `^;?\$([0-9A-Fa-f]{2})\s*=\s*(.*)$`
* `[Messages]`: `^([$&*])([0-9A-Fa-f]{2})(.*?),(\d{2}),%([adA])(\d{2})\s*$`
* `[Active Help]`: `^BEGIN = \$([0-9A-F]{2})\s*$` … `^END\s*$`
* `[Data Structures]`: `^BEGIN$` … `^END$`, first inner line `^(.+?)\s*=\s*([0-9A-Fa-f]{2})$`

Counts: 122 long-poll entries / 118 codes · 18 EFT entries · 255 general-poll codes (121 named) ·
21 status messages · 395 message descriptors / 117 codes · 30 denominations · 13 ticket transfer
codes · 19 ticket machine status codes · 11 validation type codes · 29 AFT transfer status codes ·
4 AFT receipt status codes · 3 receive-validation status codes · 2 message structures ·
9 data structures · 7 quick commands · 2 active script steps (+7 commented) · 30 AFT keys ·
22 meter-test steps · 1 exception-handling rule · 7 active-help topics.
