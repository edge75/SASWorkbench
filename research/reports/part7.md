
### 12.4 Section index of `C:\IGT3\sastest.ini`

| Line | Section | Non-blank lines | Comments | Role |
|---|---|---|---|---|
| 5 | `[SAS Protocols]` | 9 | 0 | link/session defaults |
| 16 | `[Communications]` | 3 | 1 | echo / DTR / fibre index |
| 21 | `[General Poll]` | 255 | 1 | exception lookup |
| 278 | `[Long Poll Commands]` | 258 | 36 | command catalogue |
| 660 | `[Electronic Funds Transfer]` | 24 | 0 | EFT catalogue |
| 703 | `[Status Messages]` | 22 | 1 | EFT status lookup |
| 727 | `[Messages]` | 418 | 23 | response decoders |
| 1264 | `[Denominations]` | 30 | 0 | denom lookup |
| 1296 | `[Ticket Transfer Codes]` | 13 | 0 | host redemption codes |
| 1311 | `[Ticket Machine Status Codes]` | 19 | 0 | machine redemption codes |
| 1332 | `[Validation Type Codes]` | 11 | 0 | validation type lookup |
| 1345 | `[AFT Transfer Status Codes]` | 32 | 0 | generic decoder: `$72` byte 5 |
| 1379 | `[AFT Transaction Receipt Status]` | 7 | 0 | generic decoder: `$72` byte 6 |
| 1388 | `[Receive Validation Status Codes]` | 6 | 0 | generic decoder: `$58` byte 3 |
| 1396 | `[ASDC]` | 0 | 0 | **empty / obsolete** |
| 1397 | `[Bonus Game Data Structures]` | 0 | 0 | **empty / obsolete** |
| 1399 | `[Message Structures]` | 8 | 4 | `Command Sets` combo, 2 rows |
| 1409 | `[Data Structures]` | 158 | 0 | 9 payload templates |
| 1577 | `[Quick Commands]` | 7 | 0 | one-click raw frames |
| 1586 | `[Script]` | 9 | 7 | playlist |
| 1597 | `[AFT]` | 33 | 3 | AFT form defaults |
| 1636 | `[Meter Test]` | 23 | 1 | meter-sweep playlist |
| 1661 | `[Exception Handling]` | 1 | 0 | one auto-response rule |
| 1664 | `[LongPoll Handling]` | 0 | 0 | **empty** |
| 1665 | `[RNGMode]` | 0 | 0 | **empty** |
| 1667 | `[Active Help]` | 57 | 0 | 7 help topics |
| 1735 | `[end]` | 0 | 0 | terminator |

### 12.5 Verbatim quotations relied on

`SASINI.doc` (IGT, 2001):

> *"Its purpose is to allow existing and new SAS long polls, exceptions, messages and functions
> to be defined without recompiling the tester."*

> *"A data structure is probably one of the most versatile pieces here; you can define the
> defaults and put a name to it for easy recognition. … Along with a Message structure that
> takes care of the essentials of a SAS command while the Data Structure fills in the data as
> described by the user between A BEGIN and END statement."*

> *"[Script] This section allows for verbatim scripting, the commands are sent out as entered
> here and there is an English statement shows up in the Text window. Note that any command over
> 2 bytes in length will have a CRC automatically attached to it. Scripting short circuits all
> automatic polling so what is in the script is the only thing sent."*

> *"[Quick Commands] This section allows the user to define any command verbatim and that
> command will show up on the Quick Command page. All that is required to send that command is
> to click on it."*

> *"This tool is just that a tool, it is not a full blown system. It is the responsibility of the
> person testing to insure a game responds correctly to its polls, and this tool will not
> guarantee that."*

`SASTEST.HLP` (IGT, 2002):

> *"The following categories must be in the included verbatim: [General Poll] [Long Poll
> Commands] [Electronic Funds Transfer] [ASDC] [Message Structures] [Status Messages] [Messages]
> [Script] [Bonus Game Data Structures] [Data Structures] [Quick Commands]"*

> *"Since the SAS protocol uses a 'wake up bit' (a 9th bit) for machine addressing we have to use
> the parity bit as a substitute. … it is virtually impossible to determine if the byte with the
> wakeup bit really went out of the UART. … This causes polling cycle timing problems as well as
> response time problems when the game requires an ACK."*

> *"This is a prototype test program and as such it was never meant for release."*

---

## 13. Open questions (UNKNOWN)

1. **CRC policy.** The documented rule ("over 2 bytes") contradicts the 2-byte frames in
   `[Meter Test]`/`[Script]` and the pre-CRC'd `;007f…24ca` example. Resolve empirically
   against a live machine or by dynamic tracing before porting.
2. **Byte order of the TRANS / ACK / TYPE / STATUS bytes** inside an assembled frame. All four
   flags are `n` on all 140 entries in the live file, so no example exists.
3. **Flag 5 is `CRYPT` (INI comment) or `STATUS` (both IGT docs)?** Unobservable.
4. **The `,b` eighth field on `$08`.** One occurrence, no documentation.
5. **`[SAS Protocols] Timing`** semantics; `Length` and `Auto Initialize` defaults (keys read by
   the binary, absent from every INI).
6. **`[Communications] Index` default** (commented out in the live file).
7. **`[LongPoll Handling]` grammar** — section is read, empty everywhere; the value format for
   `[Exception Handling]` (`$7E = $7E`) is inferred from one sample.
8. **`[RNGMode]` file format** — `DD2K_%d.BIN` binary outcome files are not present in `C:\IGT3`.
9. **`[Script Test]`** — read by the binary, defined nowhere.
10. **Whether `Version = "s 2.00 - 6.02"` keeps or strips its quotes** when displayed.
11. **Whether `%a` and `%A` differ.** Both are used inside the same `$1F` record; no behavioural
    difference is documented.
12. **Sentinel offset `99`** on `&` lines — assumed to mean "no offset / title row" from the six
    occurrences; not documented.
13. Whether `conf.ini` is consumed by anything in this repository (it is not read by
    `sastest.exe`; presumed to belong to the SAS *slave* under test).
