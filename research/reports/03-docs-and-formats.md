# 03 — Vendor Documentation Recovery & Data-File Formats

Research target: **C:\IGT3\sastest.exe** (the legacy IGT SAS tester we intend to replace).
Every claim below is traceable to a file read or a command run; anything not established is
marked **UNKNOWN**.

Scope note: `C:\Progetti\libsas\utils\sashost`, `utils/poller` and `utils/hwconformance` were
**not** read, cited, or used as design input. `C:\Progetti\libsas\src` and
`C:\Progetti\libsas\include` were read only to obtain the authoritative SAS CRC algorithm.

Artefacts produced in this directory:

| File | What it is |
|---|---|
| `hlp-text.txt` | Full raw text of SASTEST.HLP (both extraction methods) |
| `sasini-doc-text.txt` | Full text of SASINI.doc + the OLE2 directory listing |
| `txrx_decode.py` | Working decoder for the TXRXData.dat capture format |
| `txrx-decoded-small.txt` | Decoder output, first 50 records of the small capture |
| `hlp_topic.py`, `hlp_text.py`, `hlp2.py`, `hlp_dump.py`, `doc.py` | Extraction scripts |

---

## 1. SASTEST.HLP — recovered in full

`file` reports: *MS Windows 95 help, Thu Nov 7 22:46:29 2002, 30394 bytes*.

Structure confirmed by `hlp_dump.py` / `hlp2.py`:

```
magic = 0x00035F3F   dirstart = 78   filesize = 30394 (matches actual)
internal files:  |SYSTEM(53B)  |TOPIC(24449B)  |FONT(577B)
                 |CTXOMAP(2B)  |TTLBTREE(2086B) |CONTEXT(2086B)
|SYSTEM header: magic=0x036C  ver 1.33  flags = 0
```

**`flags = 0` means the topic text is NOT LZ77-compressed and NOT phrase-compressed.**
There is no `|Phrases` file. This is why step (a) — Sysinternals `strings` — already produced
clean prose; step (b) (direct `|TOPIC` block decode, stripping the 12-byte block header) was
still run to recover the 1–3 character tokens (`$`, `?`, `@`, `=`) that `strings -n 5` dropped,
which matter because they are the INI file's syntax. Both dumps are in `hlp-text.txt`.

### 1.1 Help table of contents (the tool's feature list, verbatim)

From the "HELP TOPICS" topic:

1. Minimum system requirements
2. Installation
3. Overview
4. Invoking a SAS/LONG POLL Command
5. Invoking a EFT/ECT/SBM Command
6. Working with the SASTest.INI File
7. Known Problems
8. Disclaimer
9. New Items

### 1.2 What the help says the tool does

- *"The SASTest program allows you to test AFT - EFT - ECT - SBM - SAS commands using the
  standard Windows point and click method. It is INI file driven, meaning that all commands
  and responses can be setup and changed in the SASTest.INI file."*
- *"Auto Ticket Redemption/Validation is now supported, which allows complete testing of the
  ticket in ticket out process."*
- *"Please remember the actual bytes transmitted and received is what is important!!! The
  English message window is just a convenience and if your INI file is incomplete these
  messages may not get translated."*
- Screen model: a **Long Poll Commands dropdown** and an **ECT/EFT Commands dropdown**; a
  **lower window** showing raw hex as `TX=` / `RX=`; an **upper window** showing the decoded
  English. *"if the English statement is not in the window it is probably not listed in the
  ini file."*
- Install = copy `SASTest.exe` + `SASTest.ini` + the help file to any directory. COM port is
  **not** selectable in the UI — *"you must go to the INI file and under the SAS Protocols
  section specify the port. example: port = 2"*.

### 1.3 "New Items" — the last feature drop the vendor documented

- **GENERAL** — updated for Windows NT/2000/98/95; the externally released tester is now the
  same build as the internal one.
- **POLLING** — *"Ability to cycle through address's up to a chosen number"*; *"Multi denom
  preamble"*.
- **TICKET VALIDATION** — automatic validation for standard and enhanced; manual validation
  with a button per step of the sequence; adjustable validation numbers; adjustable ticket
  amounts; **error introduction into ticket data**; English messages for ticket status.
- **FIBER OPTIC SUPPORT** — the tester echoes and ignores its own transmitted bytes; there is
  an INI entry to index this *"just in case there is a problem"* (this is
  `[Communications] Echo` / the commented-out `Index` — see §5.1). Enables redeeming tickets
  from multiple machines on the loop, but *"the machines being polled must be on the loop you
  cannot poll a machine that doesn't answer at this time."*
- **NEW REPORT BUILDING TOOL** — records the entire transaction into a buffer that you insert
  into a memo box on the Report page. **Caveat, verbatim:** *"The buffer is cleared each time
  you select the long poll drop down, EFT drop down, or send a custom command. This feature
  doesn't work with scripting."*
- **NEW SAVE/RESTORE DEFAULTS** — settings can be saved to a `.SET` file, many of them, under
  the File menu.

### 1.4 Known Problems — the vendor's own admissions

These are the strongest justification for a new tool; quoting the help:

- SAS uses a **9th "wake up" bit** for addressing; the tester substitutes the **parity bit**.
  Under Windows *"it is virtually impossible to determine if the byte with the wakeup bit
  really went out of the UART."*
- Toggling parity *"requires you to totally re-initialize the COMM port."*
- Windows COMM drivers *"do not have any facility to mark any one byte received with an
  error"* — only whether *any* byte in the buffer errored — so a bad byte cannot be tagged.
- The RX path is *"a separate thread… Since it is unknown how many bytes are really coming in
  we have to wait for a time out. This causes polling cycle timing problems as well as
  response time problems when the game requires an ACK."*
- *"There are times when things get out of sync with the messaging. Always look in the bottom
  window if something looks weird."*
- **Disclaimer:** *"This is a prototype test program and as such it was never meant for
  release… It is basically a bare bones program to help develop SAS protocols. There are going
  to be errors and strange behavior will be exhibited."*

### 1.5 INI syntax the help specifies (complements SASINI.doc)

Command line: `$XX = Description,LENGTH,TRANS,ACK,TYPE,STATUS,CRC,#inputboxes` (each Y/N).
Worked example from the help: `$0C = Set Sound Volume ,n,n,n,n,n,Y,1`.
Input box line: `?XX = prompt text @ <n bytes>` — e.g. `?0C = Enter the Volume Level @1`.
Message decode line: `$XX = English text,<start pos>,%d<nn>` where `%d` = decimal, `%a` = ASCII
and `nn` = byte count.

The help lists the sections it says *"must be in the included verbatim"*:
`[General Poll] [Long Poll Commands] [Electronic Funds Transfer] [ASDC] [Message Structures]`
`[Status Messages] [Messages] [Script] [Bonus Game Data Structures] [Data Structures]`
`[Quick Commands] [end]`.

**The help is out of date**: the shipped `sastest.ini` has 25 sections (§5.1), 13 of which
appear in neither the help nor SASINI.doc.

---

## 2. SASINI.doc — the vendor's INI specification, recovered in full

`file` reports a Word 9.0 (Word 97/2000) document, author `tgail`, created 15 Nov 2001, last
saved 3 Dec 2001, revision 8, 1179 words. Extracted by walking the OLE2 container in stdlib
Python (`doc.py`): FAT/DIFAT → directory → `WordDocument` stream (12834 B) → FIB
(`wIdent=0xA5EC`, `nFib=193`, `fWhichTblStm=1`) → `fcClx/lcbClx` in `1Table` → piece table →
1 piece, compressed (cp1252). **8154 characters recovered — the complete body text.**

Its own summary of purpose (verbatim):

> *"Its purpose is to allow existing and new SAS long polls, exceptions, messages and functions
> to be defined without recompiling the tester."*

> *"Please note that errors in the INI file may render the tester unusable and cause it to
> crash. This tool is just that a tool, it is not a full blown system."*

### 2.1 The sections the doc explains (the previously cryptic ones resolved)

| Section | Vendor's explanation (condensed from `sasini-doc-text.txt`) |
|---|---|
| `[SAS Protocol Defaults]` | *"a new section that allows the user to setup certain defaults when the tester fires up."* Note the shipped file actually calls it **`[SAS Protocols]`** — the doc's name is wrong. |
| `[General Poll]` | `$` + 2 hex chars + ` = ` + English text for each general-poll exception response. |
| `[Long Poll Commands]` | `$XX = name,LENGTH,TRANS,ACK,TYPE,STATUS,CRC,#boxes` then one `?XX = prompt@nbytes` per box. Worked example given: `$09 = Enable/Disable Game N,n,n,n,n,n,Y,2` with `?09 = Enter the Game number@2` and `?09 = Enter Enable 01 or Disable 00@1`. |
| `[Electronic Funds Transfer]` | *"exactly the same thing as above"*, entries appear in the EFT dropdown. |
| `[Status Messages]` | Machine-status text for EFT commands; works like General Poll. |
| `[Messages]` | *"how the tester dissects the returned message"* — `$XX = text,<start pos>,%d\|%a<nn>`. |
| `[Denominations]` | *"used to fill the SAS Denomination Combo box used for multi denom polls."* |
| `[ASDC]` | *"Not used anymore in most games, same as long poll but has a special header."* |
| **`[Bonus Game Data Structures]`** | Builds bonus structures that work like ASDC commands. Unique `$` id, drop-down text, box count, then `?` boxes. |
| **`[Message Structures]`** | *"used to build almost any message you wish and allows you to edit the individual bytes during run time."* Same grammar as above, **and: "if the number is 0 you may select from a data structure."** |
| **`[Data Structures]`** | *"probably one of the most versatile pieces here"*. Named blocks between `BEGIN` and `END`; a Message Structure supplies the SAS command envelope while the Data Structure supplies the payload. **"At run time this data can be changed by selecting the Command Set Structure Editor tab, then selecting from a drop down that will allow the user to select the structure and change the data which will last until the program is exited."** |
| `[Quick Commands]` | Verbatim commands, one per line, that appear on the Quick Command page and are sent on click. *"any command over 2 bytes in length will have a CRC automatically attached."* |
| **`[Script]`** | Verbatim scripting; English text shown in the text window; CRC auto-appended over 2 bytes. **"Scripting short circuits all automatic polling so what is in the script is the only thing sent."** |
| `[Ticket Transfer Codes]` | Codes used by the *system* during redemption. |
| `[Ticket Machine Status Codes]` | Codes sent back by the *machine* during redemption. |
| `[Validation Type Codes]` | Codes used by the system during redemption. |

**Key resolutions for our earlier cryptic sections:**
- `[Data Structures]` are *named, run-time-editable payload templates*, edited from the
  **Command Set Structure Editor** tab, and **edits are lost on exit** (never written back).
- `[Message Structures]` are the *envelope*; an input box declared with `@0` bytes is the hook
  that lets the operator pick a Data Structure instead of typing bytes. Confirmed by the
  comment in the shipped ini at line 1400: `; @0 lets you pick a Data Structure`.
- `[Script]` **disables all automatic polling** while running — a behaviour with real
  consequences that no UI label states.
- **`[Active Help]` is documented nowhere.** `grep -i "active help"` against both
  `sasini-doc-text.txt` and `hlp-strings.txt` returns nothing. It is a later addition; see §5.2
  for what it actually contains.

---

## 3. SAStest.GID — identified, contains no topic titles

Header bytes: `3f5f 0300` = magic **0x00035F3F**, the same WinHelp signature. `file` reports
*MS Windows help Global Index, 8628 bytes*. Internal files (via `strings`):
`|FILES  |Flags  |KWBTREE  |KWMAP  |Pete`.

It holds **no topic titles** — the `|TTLBTREE` lives in the .HLP, not the .GID; the GID holds
WinHelp's keyword index and per-user window state. The only substantive content is two
absolute paths:

```
C:\Quixant_Activity\SAS\SAS_Server_tool\SAStest.hlp
C:\Quixant_Activity\SAS\SAS_Server_tool\SAStest.CNT
```

Two findings from that:
1. The GID was regenerated **on a Quixant engineer's machine** (dated 13 Feb 2013), in
   `C:\Quixant_Activity\SAS\SAS_Server_tool\` — i.e. this tool is in live use in that team.
2. It references **`SAStest.CNT`**, the WinHelp *Contents* file. `ls /c/IGT3/*.CNT` →
   *No such file or directory*. **The contents/index tab of the help is broken as shipped.**
   Likewise `[SAS Protocols] Setup File = SAStest.set` names a settings file that
   `ls /c/IGT3/*.set` shows does not exist — the save/restore-defaults feature ships with no
   default profile.

The help table of contents in §1.1 therefore had to come from the `.HLP`'s own "HELP TOPICS"
topic, which is what §1.1 quotes.

---

## 4. TXRXData.dat — capture format fully decoded

### 4.1 Format

`file` reports **ASCII text, with CRLF line terminators** for *both* copies. It is a **text**
log, not binary; there is no header, no footer, no magic, and endianness is not applicable
(the SAS payload's own CRC is little-endian / LSB-first on the wire).

Exactly three line kinds, established by `sed 's/[0-9A-Fa-f]/H/g' | sort -u` over the small
file, which yields only these shapes:

```
Thu Mar 02 04:11:52 2017        <- C ctime()/asctime() form. 1-second resolution.
                                   No timezone, no milliseconds, no monotonic clock.
TX>= 01 2F 03 00 00 00 8B 2B    <- bytes the tester sent      (uppercase hex, space separated)
RX<= 01 2F 07 00 00 00 00 00 46 00 8E C3   <- bytes received
```

**Record grammar:** one timestamp line, then exactly one `TX>=` line, then **zero or one**
`RX<=` line. Proven on the 39 MB file: 2,248,986 total lines, 1,124,493 `TX>=` lines, therefore
1,124,493 timestamp lines — an exact 1:1 timestamp:TX ratio. **A record with no `RX<=` line is a
no-response / timeout**; that is the *only* way a timeout is represented.

Format consistency was verified exhaustively, not sampled — this counts every line in all
39,357,255 bytes that is neither a well-formed TX/RX nor a well-formed ctime stamp:

```
grep -v -c -E '^(TX>=|RX<=) ([0-9A-Fa-f]{2} )*[0-9A-Fa-f]{2}$|^[A-Z][a-z]{2} [A-Z][a-z]{2} [ 0-9][0-9] [0-9]{2}:[0-9]{2}:[0-9]{2} [0-9]{4}$' /c/IGT3/TXRXData.dat
  -> 0
```

**Zero violations.** The format holds across both files.

Fields that are **absent**: direction flag byte (inferred from the `TX>=`/`RX<=` prefix), length
field, sequence number, per-record duration/latency, port identity, address/session marker,
any indication of a parity/wake-up bit, and any framing-error or CRC-failure annotation.

### 4.2 Decoder and validation

`txrx_decode.py` parses the format, groups records, and glosses each frame. It validates the
SAS CRC using the algorithm transcribed from **`C:\Progetti\libsas\src\libsas_utils.cpp`
lines 97–113** (`crcCalc`, nibble-wise, `q = (crc ^ c) & 017; crc = (crc >> 4) ^ (q * 010201)`,
emitted LSB-first). Poll names are quoted from the `[Script]` section reproduced in
SASTEST.HLP (`81 = Sending General Poll`, `80 = Sending Re-Sync`).

**Every multi-byte frame in the small capture validates CRC=OK** — which independently confirms
both the log format and that the bytes are genuine SAS traffic. Excerpt of the decoder output
(full 50 records in `txrx-decoded-small.txt`):

```
44   Thu Mar 02 04:11:52 2017 TX>=  81            General Poll (odd)
                              RX<=  00            single byte (addr/poll 0x00)
45   Thu Mar 02 04:11:52 2017 TX>=  01 11         addr=1 cmd=$11 Send coin in meter
                              RX<=  01 11 00 00 46 00 99 A9   CRC=OK (calc A999, wire A999)
49   Thu Mar 02 04:11:53 2017 TX>=  01 0F         addr=1 cmd=$0F Send selected meters (multi)
                              RX<=  01 0F 00 00 61 50 00 00 46 00 00 00 30 85 00 26 12 70
                                    00 00 00 00 00 00 00 26 13 59   CRC=OK (calc 5913, wire 5913)
50   Thu Mar 02 04:11:53 2017 TX>=  01 2F 03 00 00 00 8B 2B   CRC=OK (calc 2B8B, wire 2B8B)
                              RX<=  01 2F 07 00 00 00 00 00 46 00 8E C3  CRC=OK (calc C38E, wire C38E)
54   Thu Mar 02 04:11:54 2017 TX>=  01 2F 03 00 00 04 AF 6D   CRC=OK (calc 6DAF, wire 6DAF)
                              RX<=  --            *** NO RESPONSE (timeout) ***
58   Thu Mar 02 04:11:55 2017 TX>=  01 2F 03 00 00 08 C3 A7   CRC=OK (calc A7C3, wire A7C3)
                              RX<=  --            *** NO RESPONSE (timeout) ***
```

Records 54 and 58 are real, silent data loss: a per-game selected-meters read (`$2F` for game
index 04 and 08) that the machine never answered, in the middle of a meter sweep whose other
eight reads succeeded. **Nothing in the log flags this as an error** — the operator must notice
a missing line. This is exactly the class of bug the new tool should surface loudly.

The small capture is a complete, useful worked example of a meter-sweep session: general
polling, then `$11 $12 $13 $14` (coin in / coin out / drop / jackpot), then `$0F` (multi-meter),
then `$2F` for game indices 00–09, then `$35 $36 $37` ($20/$50/$100 bill meters), interleaved
with general polls, then back to idle polling.

### 4.3 The 39 MB file is 39 MB of nothing

```
wc -l                                 -> 2,248,986 lines
grep -c '^TX>= '                      -> 1,124,493
grep -c 'RX'                          -> 0            (ZERO lines contain "RX" at all)
grep -oE '^TX>= .*$' | sort | uniq -c -> 562,247  TX>= 81
                                         562,246  TX>= 80
first timestamp -> Tue Feb 25 09:44:31 2025
last  timestamp -> Fri Feb 28 00:12:52 2025
```

**The entire 39,357,255-byte file contains exactly two distinct messages (`80` and `81`),
not one single response byte, and not one single exception.** It is 62.5 hours of a tester
polling a machine that was never connected or never answered.

Implications, all directly measurable:
- Growth rate ≈ 39.36 MB / 224,901 s ≈ **175 bytes/s ≈ 15 MB per day, unbounded**. There is no
  rotation, no size cap and no compression.
- The log is written even when **nothing is happening** — there is no "log only on change" or
  "suppress idle polls" option.
- Because idle polls dominate ~99.99% of the volume, the signal-to-noise ratio of an
  as-captured log is effectively zero, and finding the interesting seconds means scrolling
  through millions of lines.
- Timestamps are 1-second granularity, so at ~5 polls/sec **ordering within a second is the only
  timing information available** — you cannot measure a response latency at all.

### 4.4 Replay / diff feasibility

- **Replay: yes, mechanically straightforward.** Every TX frame is complete and byte-exact
  (CRC included), so a replayer can re-send the TX stream verbatim, or act as a machine-side
  responder replaying the RX stream. The blocker is timing fidelity: 1-second timestamps cannot
  reproduce SAS inter-byte or poll-cycle timing, so replay is faithful in *content* but not in
  *time*. A new tool that records microsecond monotonic timestamps fixes this permanently.
- **Diff: yes, and it is the highest-value cheap feature.** Records normalise to
  `(direction, hex bytes)` tuples; dropping timestamps and collapsing runs of idle
  `80`/`81`/`00` gives a canonical form that diffs cleanly between two runs of the same script.
  The small capture would reduce from 209 lines to roughly 25 meaningful records.
- **Reading the team's existing captures is free value**: `txrx_decode.py` already parses both
  files with zero format violations, so an importer for this exact text format costs almost
  nothing and immediately makes years of archived Quixant captures readable in the new app.
- **UNKNOWN**: whether `sastest.exe` truncates or appends `TXRXData.dat` on start, and whether
  writing it can be turned off. The small copy begins with an orphan `RX<= 00` that has no
  preceding timestamp or TX line, so that file starts part-way through a transaction; whether
  that is a truncated copy, a wrap, or an interleaving race between the writer and the RX
  thread is not determinable from the bytes alone.

---

## 5. The shipped INI vs. the documentation

### 5.1 Section inventory (`grep -n '^\[' /c/IGT3/sastest.ini`)

25 sections. Marked ✓ if the vendor documented it in SASINI.doc or SASTEST.HLP:

| Line | Section | Documented |
|---|---|---|
| 5 | `[SAS Protocols]` | partly — doc calls it `[SAS Protocol Defaults]` |
| 16 | `[Communications]` | ✗ |
| 21 | `[General Poll]` | ✓ |
| 278 | `[Long Poll Commands]` | ✓ |
| 660 | `[Electronic Funds Transfer]` | ✓ |
| 703 | `[Status Messages]` | ✓ |
| 727 | `[Messages]` | ✓ |
| 1264 | `[Denominations]` | ✓ (doc only) |
| 1296 | `[Ticket Transfer Codes]` | ✓ (doc only, one line) |
| 1311 | `[Ticket Machine Status Codes]` | ✓ (doc only, one line) |
| 1332 | `[Validation Type Codes]` | ✓ (doc only, one line) |
| 1345 | `[AFT Transfer Status Codes]` | ✗ |
| 1379 | `[AFT Transaction Receipt Status]` | ✗ |
| 1388 | `[Receive Validation Status Codes]` | ✗ |
| 1396 | `[ASDC]` | ✓ (deprecated by vendor) |
| 1397 | `[Bonus Game Data Structures]` | ✓ |
| 1399 | `[Message Structures]` | ✓ |
| 1409 | `[Data Structures]` | ✓ |
| 1577 | `[Quick Commands]` | ✓ |
| 1586 | `[Script]` | ✓ |
| 1597 | `[AFT]` | ✗ |
| 1636 | `[Meter Test]` | ✗ |
| 1661 | `[Exception Handling]` | ✗ |
| 1664 | `[LongPoll Handling]` | ✗ |
| 1665 | `[RNGMode]` | ✗ |
| 1667 | `[Active Help]` | ✗ |
| 1735 | `[end]` | ✓ |

**13 of 25 sections are undocumented.** `[LongPoll Handling]` and `[RNGMode]` are present but
**completely empty**; `[Exception Handling]` contains one self-referential line `$7E = $7E`.
Their semantics are **UNKNOWN**.

Connection settings actually live in `[SAS Protocols]` / `[Communications]`:

```
Wakeup Delay = 2      Port = 11       Poll Rate = 200      Address = 1
Validation = Enhanced Timing = 0      CRC Check = 1
Setup File = SAStest.set              Version = "s 2.00 - 6.02"
Echo = 0              DTR = 1         ;Index = -1
```

`Poll Rate = 200` (ms) is consistent with the ~5 polls/sec measured in §4.3.
`Echo` and the commented-out `Index` are the fiber-optic knobs the help's "New Items" describes.

### 5.2 `[Active Help]` — what it actually is

Undocumented anywhere, but self-evident from its content: an operator-facing **inline protocol
reference**, keyed by long-poll code, in `BEGIN = $XX … END` blocks, gated by `Active = 1`.
Example entries present: `$00` (ACK/NACK semantics), `$01` (Shutdown Command, ~10 lines of SAS
spec prose), `$11` Total Coin In, `$12` Total Coin Out, `$13` Total Drop, `$14` Total Jackpot,
`$15` Games Played. The text is lifted from the SAS protocol specification, e.g.:

> *"A NACK is a single byte response to a long poll that is sent when the gaming machine
> receives an improperly formatted message, ie: incorrect crc. The response is a single byte
> consisting of the gaming machines address ORed with 0x80(hex)."*

So someone started building context-sensitive spec help into the INI and stopped after 7 of the
~254 defined general-poll exceptions and ~200 long polls. **That is a strong signal of unmet
user need**: engineers want the spec text next to the command, and the legacy tool made them
hand-author it in an INI file.

---

## 6. The user workflow — how an engineer actually uses sastest.exe

The tab structure is taken from the main form definition extracted from the executable
(`form_TFORM1.dfm`, `TTabSheet` captions). Top-level tabs, in order:

`Command Sets` · `Command Set Structure Editor` · `General Poll/Address` · `Quick Cmd` ·
`Misc` · `Multi-Denom` · `Validation` · `Redemption` · `Scripting` (sub-tabs *Script*,
*Meter Test*) · `Progressive` · `EFT Test` · `AFT` (sub-tabs *Registration / Lock*,
*Transfer Funds*, *Receipt*) · `Pulltab` · `Report` · `Authentication`

Below the tabs sit the two windows the help describes: an **upper window** (decoded English)
and a **lower window** (raw `TX=` / `RX=` hex).

### Workflow A — Connect to a machine and start polling

1. **Quit the app.** Open `sastest.ini` in a text editor.
2. Edit `[SAS Protocols] Port =` to the COM number, `Address =` to the machine address,
   `Poll Rate =`, `Wakeup Delay =`, `CRC Check =`. For fiber, edit `[Communications] Echo`.
3. Save, relaunch, and watch the lower window for `TX= 81` / `RX= 00`.

**Friction.** Connection setup is an out-of-process file edit with a mandatory restart — the
help itself tells you to make a Windows shortcut per COM port. There is no port enumeration, no
"is anything there?" probe and no connection state indicator: a machine that is powered off
looks exactly like a machine that is fine, which is precisely how 39 MB of dead polling
(§4.3) gets logged unnoticed. Per SASINI.doc, *"errors in the INI file may render the tester
unusable and cause it to crash"* — with, per the help, *"no error checking for the format of
the ini file."*

### Workflow B — Send a long poll and read a meter

1. Go to the main Long Poll Commands dropdown, scroll the list, pick e.g. *Send coin in meter*.
2. If the command declares input boxes, a modal prompts for each field's bytes in hex.
3. The bytes go out; the lower window shows `TX=` / `RX=`; the upper window shows the decode.

**Friction.** The dropdown is a flat, unsearchable list of ~200 entries in INI order. Input
boxes take **hex bytes**, not values — the operator does the BCD/endianness conversion by hand.
A meter reads correctly only if a matching `[Messages]` line exists; otherwise, per the help,
*"if the English statement is not in the window it is probably not listed in the ini file"* —
silent non-decoding that looks like a machine fault. And the decode is positional
(`,03,%d04`), so an off-by-one in the INI mis-parses forever with no warning.

### Workflow C — Sweep all the meters (the small capture, §4.2)

1. Scripting → **Meter Test** sub-tab, which runs the `[Meter Test]` INI list verbatim:
   `0111 0112 0113 0114 010F`, `012F03000000…09` per game, `0135 0136 0137`, `81`, `80`.
2. Read the results out of the scrolling lower window.

**Friction.** The sweep list is INI-authored, not UI-composed. Results arrive as a scrolling hex
transcript, not a meter table — nothing accumulates the ten `$2F` responses into per-game rows.
Two of the twelve reads in the captured session got no reply and **nothing said so**; you find
that by noticing an absent line. There is no pass/fail, no expected-value comparison, no export.

### Workflow D — Send an arbitrary / custom command

1. **Quick Cmd** tab: click a pre-baked hex string from `[Quick Commands]`; it is sent on the
   next poll cycle with a CRC auto-appended if >2 bytes.
2. Or **Command Sets** + **Command Set Structure Editor**: pick a `[Message Structure]`
   envelope, and where a box is declared `@0`, pick a named `[Data Structure]` payload and edit
   its bytes.

**Friction.** New quick commands require editing the INI and restarting. Structure-editor edits
are, per SASINI.doc, *"[changes] which will last until the program is exited"* — **never saved
back**, so a carefully built payload is lost every session. There is no request builder that
knows a command's real field layout; everything is hand-assembled hex.

### Workflow E — Run an AFT transfer

1. **AFT** tab → *Registration / Lock*: register using `Asset Number`, `Registration Key`,
   `POSID` — all sourced from `[AFT]` in the INI.
2. → *Transfer Funds*: set transfer code/type, cashable / restricted / non-restricted amounts,
   transaction ID, expiration, pool ID.
3. → *Receipt*: patron name, account #, balance, ATM card #, fee, total debit, and the
   location / address / in-house / debit text lines.
4. Send; read the `$72` response and look the status byte up against
   `[AFT Transfer Status Codes]` (≈30 codes) and `[AFT Transaction Receipt Status]`.

**Friction.** Defaults live in the INI (`Asset Number =777`, an expiration of `05302003`,
`Patron name =Ima Winner`), so changing a test fixture means editing a file. The 40-hex-digit
registration key is a single unvalidated INI string. Amounts are fixed-width BCD strings
(`0000000000`) typed by hand. There is no transfer history, no lock-state display, and no
way to script a multi-step AFT scenario — and if you *do* use `[Script]`, per SASINI.doc it
*"short circuits all automatic polling"*, so the machine stops being polled underneath you.

### Workflow F — Ticket in / ticket out (validation & redemption)

1. **Validation** tab: choose standard vs enhanced (`[SAS Protocols] Validation = Enhanced`),
   set validation numbers and ticket amounts, optionally **introduce errors into the ticket
   data** (a genuinely good feature, per the help's "New Items").
2. **Redemption** tab: run automatic validation, or step manually with a button per stage of
   the sequence.
3. Interpret results against `[Ticket Transfer Codes]`, `[Ticket Machine Status Codes]`,
   `[Validation Type Codes]`, `[Receive Validation Status Codes]`.

**Friction.** Four separate code tables must be cross-referenced by hand to explain one
outcome. The multi-machine fiber case carries the vendor's own caveat that *"the machines being
polled must be on the loop you cannot poll a machine that doesn't answer."* No ticket lifecycle
view ties issue → redeem → status together.

### Workflow G — Watch exceptions during a soak

1. **General Poll/Address** tab: enable address cycling up to a chosen number.
2. Watch the upper window for exception text sourced from `[General Poll]` (254 `$XX` entries
   defined in the shipped INI).

**Friction.** Exceptions scroll past in the same window as everything else. There is no
exception log, no filter, no counter, no alerting and no persistence. `[Exception Handling]`
contains only `$7E = $7E`, so whatever it was meant to do is unimplemented or **UNKNOWN**.

### Workflow H — Produce evidence for a report

1. **Report** tab: paste the recorded transaction buffer into a memo box, add notes.

**Friction, in the vendor's own words:** *"The buffer is cleared each time you select the long
poll drop down, EFT drop down, or send a custom command. This feature doesn't work with
scripting."* So the report tool cannot capture the two things engineers most need to report:
a scripted test run, and a sequence of several different commands. Meanwhile the real
transcript is being written to `TXRXData.dat` in a form that is 99.99% idle noise (§4.3).

### Cross-cutting friction

- **Restart-to-configure.** Port, address, poll rate, AFT fixtures, quick commands, scripts,
  meter-test lists and command definitions are all INI-only.
- **Unvalidated INI.** Per the help: no format checking; per the doc: bad entries *"may render
  the tester unusable and cause it to crash."* 1,735 lines of it, hand-maintained.
- **Nothing persists.** Structure-editor edits die on exit; `Setup File = SAStest.set` names a
  file that does not exist; there is no run history.
- **Silence is the failure mode.** Timeouts, undecoded messages and dead links all present as
  *absence* — a missing line, a blank English window, a poll with no reply.
- **Timing is unmeasurable.** 1-second log timestamps, and a documented architecture in which
  Windows *"has a tendency to chop up your message"* and wake-up-bit delivery cannot be
  confirmed.
- **Two-window hex UI.** All 15 tabs funnel into one scrolling hex transcript plus one English
  transcript; nothing is tabular, filterable, searchable or exportable.

---

## 7. Risks and open questions

**Risks**
- The 39 MB capture proves the archive may be mostly idle noise; an importer must stream, not
  slurp, and must collapse idle runs or it will be unusable.
- 1-second timestamps mean archived captures can never yield latency data — replay timing
  fidelity is permanently lost for historical files.
- Because `[Messages]` decoding is positional and unvalidated, existing INIs may contain
  wrong offsets that have silently mis-decoded meters for years; importing them faithfully
  imports the bugs.
- The parity-bit wake-up substitution is a documented, unresolved correctness hazard on
  Windows; a new app must state clearly which hardware path it uses.
- `sastest.ini` is 1,735 lines of hand-maintained config that the team depends on; any new tool
  that cannot import it will not be adopted.

**Unknowns**
- Semantics of `[LongPoll Handling]`, `[RNGMode]` (both empty) and `[Exception Handling]`
  (`$7E = $7E` only).
- Whether `TXRXData.dat` is appended or truncated per run, and whether logging can be disabled.
- Why the small capture begins with an orphan `RX<= 00`.
- The `.SET` file format — no `.set` file exists on disk to inspect.
- Whether `Timing = 0` and `Wakeup Delay = 2` have units or enumerated meanings.
