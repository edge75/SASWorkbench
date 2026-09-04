# 09 — The GSA SAS Conformance Test Corpus, and what SasWorkbench must be able to express

Source of record: `C:\igs\GSA SAS Host Simulator v1.1\` — the official Gaming Standards
Association host simulator and toolkit, **"GSA SAS Toolkit v1.2 for SAS v6.01/6.02"**,
document id **gsa-p0074.001.01**, released 2009-05-22.

Every claim below is tied to a file that was read or a command that was run. Where a fact
could not be established from the material present on disk it is written **UNKNOWN**.

---

## 0. Provenance and method

### 0.1 What is on disk

From `ls -la "/c/igs/GSA SAS Host Simulator v1.1/"` and a recursive listing:

| File | Size | mtime | What it is |
|---|---|---|---|
| `SASHostSim.exe` | 806,912 | 2026-07-16 19:25 | Windows MFC GUI (links `MFC42.DLL`, `msvcrt.dll` — see `SASHostSim.RPT`) |
| `SAS Engine\gsat.exe` | 407,985 | 2026-07-16 19:25 | **DOS** protected-mode engine (ships with `cwsdpmi.exe`, the CWSDPMI DPMI host) |
| `ReleaseNotes.txt` | 35,719 | 2026-07-16 19:25 | v1.2 change list by test chapter, then a 15-page JIRA export |
| `One Key Polls.doc` | 409,088 | 2026-07-16 19:25 | OLE2 Word doc — the named-poll / loop / filter / meter-set catalogue |
| `PollDef.txt` | 139,266 | 2026-07-25 17:00 | Human-readable export of the loaded poll definitions (661 entries) |
| `PollTemplates.xml` | 46,374 | 2026-07-16 | 138 `<SAS_template>` field layouts for host to EGM polls |
| `ResponseTemplates.xml` | 113,292 | 2026-07-16 | 117 `<SAS_template>` field layouts for EGM to host responses |
| `EventTemplates.xml` | 12,908 | 2026-07-16 | Field layouts for real-time-event (RTE) payloads |
| `SASTemplates.dtd` | 3,245 | 2026-07-16 | The schema for all three template files |
| `TestData\*.dat` | 24 files | 2026-07-16 | **Saved HostSim configurations, one record per conformance test** |
| `Defaults\` | — | 2026-07-16 | Pristine copies of every `.dat`/`.xml` for factory reset |
| `PollDefsSet1/2/3.dat`, `LoopDefs1/2/3.dat` | — | — | Three switchable poll-definition sets, each with a matching loop set |
| `UserPollDefinition.dat`, `UserLoopDefs.dat`, `User*Templates.xml` | — | — | The user-extensible fourth set |
| `MeterSetDefinitions.dat`, `Tracefilters.dat`, `TicketList.dat` | — | — | Meter sets, trace filters, ticket database |
| `10.1\`, `10.2\` | — | 2026-07-17 | The two test-script chapters the user has, plus captured `.trc` files |
| `DOSBox\` | — | 2026-07-18 | Local harness: DOSBox-X 2026.07.02 portable, to run `gsat.exe` |
| `SASHostSim.RPT` | 1,251 | 2026-07-18 | **A crash dump** — `C0000005 ACCESS_VIOLATION` at `00469605` |

Everything shipped by GSA carries mtime `2026-07-16 19:25` (the unpack date). Everything dated
2026-07-18 or later was produced locally by the lab.

### 0.2 Extraction commands actually run

```
# 10.2 test script (UTF-8 HTML, Traditional Chinese) -> UTF-8 text
python htmlx.py ".../10.2/EGM_TestScripts_v1.2_Chapter10_2_SecureEnhancedValidation.html" gsa-10-2.txt
  -> 447 lines
python htmlx.py ".../10.1/EGM_TestScripts_v1.2_Chapter10_1_StandardValidation.html"        gsa-10-1-mine.txt
  -> 163 lines   (agrees with the pre-existing gsa-10-1.txt)

# One Key Polls.doc — OLE2/CFBF. The Word 97 piece table (fcClx=5200, lcbClx=21, 1 piece,
# fcMin=1024 fcMac=9178) yields only an 8,154-char INI-format appendix. The actual catalogue
# is uncompressed in the WordDocument stream at file offset 1536..26866:
python okp.py    -> okp_body.txt, 26,922 chars, 256 lines

# Complete chapter/test map, straight out of the saved HostSim configs:
for f in TestData/*.dat; do strings -n 4 "$f" | grep -Eo 'TEST[_ ]?[0-9][0-9.]*' | sort -u; done

# Trace inventory and format analysis:
python trcsum.py -> trc-summary.txt   (32 files, 285,621 bytes total)

# Per-step automation classification:
python classify3.py -> step-classification.txt  (297 steps)
```

All artefacts are in
`C:\Users\ALESSI~1.QUI\AppData\Local\Temp\claude\C--IGT3\473c2bfe-6dd0-4a0e-979a-2d6bd2da83f6\scratchpad\research\`.

### 0.3 The harness the lab has already built around this toolkit

`DOSBox\環境架設紀錄.txt` (2026-07-18, "Environment setup record", platform: *Quixant gaming
platform (Windows)*) documents the working topology:

```
SASHostSim.exe (COM3) <-- null modem --> COM4 (DOSBox-X serial1 = gsat.exe GEP)
gsat.exe SAS   (COM6) <-- null modem --> COM5 (EGM game program, Quixant SAS UART)
```

- `SASHostSim.exe` is only a GUI. `gsat.exe` — a **DJGPP 386 protected-mode DOS program** —
  does the actual SAS wire work. The two talk over a serial link the notes call
  **GEP (GUI–Engine Protocol)**.
- Inside DOSBox: `DOS COM1 (0x3F8, IRQ4) = GEP`, `DOS COM2 (0x2F8, IRQ3) = SAS`.
- Required DOSBox tuning, quoted: `cputype=pentium` + `cycles=max`; anything above
  `pentium_mmx` with `cycles=max` hangs (*"pentium_mmx 以上搭配 cycles=max 會導致卡住"*).
- Troubleshooting log records `Link Down` when *"gsat.exe 的 polling cycle 太慢"* (the polling
  cycle is too slow) and `Progressive Timeout` when LP 86 is not broadcast on time.
- Abandoned approaches: com0com; a TCP "SAS Bridge" with mark/space parity (*"mark/space
  parity wake-up bit 時機不夠精確"* — wake-up-bit timing not precise enough); COM12
  (USB-serial, blocks waiting on CTS).
- COM5 on the Quixant platform is the **dedicated 9-bit SAS UART**; the notes state it is not
  compatible with standard RS-232 `directserial`, and that a 1-byte write comes back as
  4 bytes with bit 7 modified.

This is the status quo SasWorkbench replaces: a Windows MFC front end driving a DOS
protected-mode engine inside a CPU emulator across two physical null-modem cables — and it
crashed at least once (`SASHostSim.RPT`, access violation).

---

## 1. Full transcription — Chapter 10.1, Standard Cashout Validation

Header of `10.1/EGM_TestScripts_v1.2_Chapter10_1_StandardValidation.html`:
*"SECTION 3: TICKETING | GSA SAS Toolkit v1.2 for SAS v6.01/6.02 | gsa-p0074.001.01"*,
Chapter 10 – Tickets, Section 10.1, **pages 159–168**. `[1]` = Requirements Document,
`[2]` = SAS Protocol Manual v6.02.

### 1.0 Chapter preconditions and reference tables

**Estimated total: 2.5 – 4 hours for 7 tests.**

| Test | Estimate | Subject |
|---|---|---|
| 10.1.1 | 5–10 min | Verify LP A0 reports Standard Validation configuration |
| 10.1.2 | 5–10 min | Confirm tickets are not redeemable in Standard mode |
| 10.1.3 | 30–45 min | Full ticket-print flow, exception 3D, LP 3D response |
| 10.1.4 | 15–20 min | Behaviour on a non-validating channel in a multi-channel EGM |
| 10.1.5 | 45–60 min | Ticket meter increments (incl. handpay / win-to-printer) |
| 10.1.6 | 15–20 min | Exception 3D format while RTE is enabled |
| 10.1.7 | 20–30 min | Ticket data survives power cycle; unique validation IDs |

Preconditions applying to every 10.1 test:
- EGM SAS address = 1
- EGM configured for **Standard Ticket Validation**
- EGM **not** in RTE mode (power cycle, or host sends RTE-disable)
- If the EGM supports handpay or jackpot receipts, configure it to print them

Equipment: an EGM supporting Standard Ticket Validation; the HostSim; several valid cashout
tickets (for 10.1.2); a printer, internal or external.

Risks called out by the document:

| Risk | Why | Mitigation given |
|---|---|---|
| Must trigger win-to-handpay | 10.1.5 needs credit/jackpot limits adjusted | Confirm in advance how the EGM's limits are set |
| Must trigger win-to-printer | 10.1.5 needs a win large enough to print directly | Set a very low limit so it is easy to hit |
| Multiple SAS channels | 10.1.4 needs a dual-channel setup | If the EGM is single-channel, skip the test |

**Reference table — LP 3D "Send Cashout Ticket Information" response format**

| Field | Bytes | Value | Notes |
|---|---|---|---|
| Address | 1 binary | `01` | EGM address, wakeup bit not set |
| Command | 1 binary | `3D` | Send cashout ticket information |
| Validation number | 4 BCD | `XXXX` | Standard validation number, must be unique |
| Ticket amount | 5 BCD | `XXXXX` | Cents |
| CRC | 2 binary | `0000`–`FFFF` | 16-bit CRC, LSB first |

**Reference table — Exception 3D in RTE mode**

| Field | Bytes | Value |
|---|---|---|
| Address | 1 binary | `01` |
| RTE identifier | 1 binary | `FF` |
| Exception code | 1 binary | `3D` (Ticket Printed) |
| CRC | 2 binary | `0000`–`FFFF`, LSB first |

**Meter codes exercised by 10.1.5**

| Code | Meaning | Unit |
|---|---|---|
| `0004` | Total cancelled credits | SAS accounting credits |
| `000E` | Total tickets out credits | cents |
| `0012` | Total tickets out count | count |
| `0016` | Total ticket out credits | SAS accounting credits |
| `002C` | Total cashable ticket out credits | SAS accounting credits |
| `0038` | Total cashable ticket out quantity | count |
| `0086` | Regular cashable ticket out cents | cents |
| `0087` | Regular cashable ticket out quantity | count |

---

### 10.1.1 — Standard Validation Configuration
*Requirement [1] 10.1.1 · est. 5–10 min · 2 steps*

**HostSim setup** — Test Group: `Standard Validation`, Test Number: `TEST_10.01.01`
**One Key Polls** — `F1 = A0-Send Enabled Features Game 0, address 1`
**Polling Loops** — `1 = Basic_1`
**Filters** — `Display One-Key Poll`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | RAM clear the EGM if necessary. Configure the EGM for standard validation. | — | needs EGM stimulus |
| 2 | Start the polling loop. Send F1. | EGM answers LP A0 in the format of [2] Table 7.14b, **and Feature Codes byte 1 bits 6–5 = `00`** (per [2] Table 7.14c). | wire-only |

*Note in the script:* an EGM can only be configured for one ticket validation mode at a time.

---

### 10.1.2 — No Ticket Redemption
*Requirement [1] 10.1.2 · est. 5–10 min · 2 steps*

**HostSim setup** — Test Group: `Standard Validation`, Test Number: `TEST_10.01.02`
**One Key Polls** — none · **Polling Loops** — `1 = Basic_1` · **Filters** — `1 = Display One-Key Poll`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1. | — | wire-only |
| 2 | **Insert several cashout tickets** and watch the trace window. | EGM does **not** report exception `67`. EGM neither stacks the ticket nor credits it. | needs EGM stimulus |

*Note:* in Standard Validation mode tickets must not be redeemable.

---

### 10.1.3 — Cashout Ticket 1
*Requirement [1] 10.1.3 · est. 30–45 min · 16 steps*
Covers: LP 3D (send cashout ticket information); exception 3D (a cashout ticket has been
printed); handpay and jackpot receipts.

**HostSim setup** — Test Group: `Standard Validation`, Test Number: `TEST_10.01.03`
**One Key Polls**
- `F1 = 00-General poll, address 0`
- `F2 = 00-General poll, address 1`
- `F3 = 3D-Send cashout ticket information, address 1`

**Polling Loops** — `1 = Basic_No General`, `2 = Basic_1` · **Filters** — `1 = Display One-Key Poll`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1, send F1 to confirm comms are synchronised. | — | wire-only |
| 2 | **Open then close the EGM door.** | — | needs EGM stimulus |
| 3 | With credits on the EGM, **press cashout**. | EGM prints a ticket. | needs human judgement (paper) |
| 4 | **Press the Service button** to light it, then clear it. | — | needs EGM stimulus |
| 5 | Send F2 repeatedly until exception `00` or `1F` appears. Inspect the trace. | EGM returns a 1-byte exception `3D` (Ticket Printed) in the **first** general-poll response, then in subsequent general-poll responses returns, in order: `11` Slot Door Open, `12` Slot Door Closed, `71` Change light on, `72` Change light off. | wire-only |
| 6 | Send F3. | EGM answers F3 as: `(01)` 1-byte EGM address; `(3D)` 1-byte command; `(XXXX)` 4-BCD standard validation number; `(XXXXX)` 5-BCD ticket amount in cents; `(0000-FFFF)` 2-byte CRC LSB first. | wire-only |
| 7 | Insert credits and cash out. | EGM prints a ticket. | needs human judgement (paper) |
| 8 | Stop Loop 1. Send, in order: F1, F2, F2, F2, F1, F2, F1. | EGM returns exception `3D` in the first two F2 responses; the third F2 may be ignored or may return `3D`. EGM returns exception `3D` in the last F2 response. | wire-only |
| 9 | Start Loop 2. | — | wire-only |
| 10 | Insert credits and press cashout. | EGM prints a ticket for the correct amount. EGM issues exception `3D`. | needs human judgement (paper) |
| 11 | Insert credits and press cashout again. | EGM prints a ticket for the correct amount. EGM issues exception `3D`. | needs human judgement (paper) |
| 12 | Send F3. | EGM answers F3 with a **non-zero** validation ID and an amount matching the ticket printed in step 11. | wire-only |
| 13 | If more `3D` exceptions were reported, send F3 again. | EGM answers F3. If the validation ID and amount are non-zero they must correspond to the ticket printed in step 11. | wire-only |
| 14 | Send F3. | EGM answers F3 in the same format as step 6. | wire-only |
| 15 | If supported, configure the EGM to print a **cashout receipt**. Insert credits, cash out, send F3. | EGM prints a cashout receipt and issues exception `3D`. EGM answers F3 appropriately. | needs human judgement (paper) |
| 16 | If supported, configure the EGM to print a **jackpot receipt**. Insert credits and play until a jackpot is won. Send F3. | EGM prints a jackpot receipt and issues exception `3D`. EGM answers F3 appropriately. | needs human judgement (paper) |

---

### 10.1.4 — Ticket Printed, Non-Validating Host {3D}
*Requirement [1] 10.1.4 · est. 15–20 min · 6 steps*

**HostSim setup** — Test Group: `Standard Validation`, Test Number: `TEST_10.01.04`
**One Key Polls** — `F1 = 3D-Send cashout ticket information, address 1`
**Polling Loops** — `1 = Basic_1` · **Filters** — `1 = Display One-Key Poll`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | If the EGM supports multiple SAS channels and standard validation can be assigned to a specific channel, configure and enable at least two SAS channels with standard validation on one of them. **If the EGM is single-channel, skip this test.** | — | needs EGM stimulus |
| 2 | Confirm SASHostSim is connected to the validating channel. Start Loop 1. | — | wire-only |
| 3 | Insert credits and press cashout. | EGM prints a ticket. EGM issues exception `3D`. | needs EGM stimulus |
| 4 | **Move SASHostSim to the non-validating channel.** | EGM issues exception `3D` once. | needs EGM stimulus (recabling) |
| 5 | Send F1. | EGM either ignores F1, **or** answers F1 with validation ID and amount both zero. | wire-only |
| 6 | Move SASHostSim back to the validating channel. Send F1 again. | EGM answers F1 appropriately, with a non-zero validation number and the printed ticket's amount. | wire-only |

---

### 10.1.5 — Ticket Meters
*Requirement [1] 10.1.5 · est. 45–60 min · 13 steps*
Meter codes: `0004, 000E, 0012, 0016, 002C, 0038, 0086, 0087`

**HostSim setup** — Test Group: `Standard Validation`, Test Number: `TEST_10.01.05`
**One Key Polls** — `F1 = A0-Send Enabled Features Game 0, address 1`
**Polling Loops** — `1 = Ticket meters` · **Filters** — `1 = Display One-Key Poll`
**Meter Set** — `1 = Legacy ticket meters`

> **Discrepancy worth recording.** `One Key Polls.doc` defines
> `Legacy ticket meters = (000D), (000E), (000F), (0010), (0011), (0012), (0013), (0014)`,
> which does **not** contain `0004`, `0016`, `002C`, `0038`, `0086` or `0087`. Either the
> meter set was redefined for v1.2 or the header is stale. The pass criteria in steps 3 and 5
> are authoritative. SasWorkbench should treat the meter list as data owned by the script,
> not by a named preset it cannot see.

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1 and select Meter Set 1. | — | wire-only |
| 2 | Send F1, verify feature code 2 bit 2. | See the 10.1.1 pass criterion. | wire-only |
| 3 | Observe Meter Set 1 and record the current values. | Every meter code's value matches the corresponding meter on the EGM itself: `0004` total cancelled credits; `000E` total tickets out credits; `0012` total tickets out count; `0016` total ticket out credits; `002C` total cashable ticket out credits; `0038` total cashable ticket out quantity; `0086` regular cashable ticket out cents; `0087` regular cashable ticket out quantity. | **needs human judgement** — requires reading the EGM's own meter display |
| 4 | Insert credits and cash out. | EGM issues exception `3D` and answers LP 3D with the cashout information. | needs EGM stimulus |
| 5 | Observe the Meter Set 1 window. | • `0004` increments by the cashout amount (SAS accounting credits) **iff** LP A0 feature 2 bit 2 = 1; otherwise it does not increment. • `0016` and `002C` increment by the cashout amount (SAS accounting credits). • `000E` and `0086` increment by the cashout amount (cents). • `0012`, `0038`, `0087` each increment by 1. • **No other meter changes.** | wire-only |
| 6 | Insert $10 of credits. Play away $5. Cash out the remainder. | EGM prints a ticket for the correct cashout amount. EGM issues exception `3D`. EGM answers LP 3D with a non-zero validation ID and the correct amount. | needs human judgement (play + paper) |
| 7 | Select and observe Meter Set 1. | Same increment rules as step 5, based on the step-6 cashout amount. | wire-only |
| 8 | Adjust the cashout limit so a cashout-to-handpay can be produced. Insert enough credits. | — | needs EGM stimulus |
| 9 | Cash out credits to trigger a handpay. Reset the handpay. Observe Meter Set 1. | `0004` increments by the handpay amount (accounting credits). **No other meter increments.** | needs EGM stimulus |
| 10 | Adjust credit/jackpot limits so win-to-handpay is easy to trigger. | — | needs EGM stimulus |
| 11 | Insert credits and play until win-to-handpay occurs; observe Meter Set 1. | **No meter increments at all.** | needs human judgement (play) |
| 12 | Adjust credit/cashout limits so win-to-printer is easy to trigger. | — | needs EGM stimulus |
| 13 | Insert credits and play until win-to-printer occurs; observe Meter Set 1. | Same increment rules as step 5, based on the win-to-printer amount. | needs human judgement (play) |

---

### 10.1.6 — Ticket Printed, RTE {3D}
*Requirement [1] 10.1.6 · est. 15–20 min · 6 steps*

**HostSim setup** — Test Group: `Standard Validation`, Test Number: `TEST_10.01.06`
**One Key Polls**
- `F1 = 00-General Poll, address 0`
- `F2 = 00-General Poll, address 1`
- `F3 = 0E-RTE-Enable, address 1`
- `F4 = 0E-RTE-Disable, address 1`

**Polling Loops** — `1 = Basic_No General` · **Filters** — `1 = Display One-Key Poll`

> `ReleaseNotes.txt`, TESTS 10x: *"10.01.06: A LP 3D (Send cashout ticket information) has
> been added as an auto-response to exception 3D."* — the simulator answers this test's
> exception automatically. That is an event-driven rule, not a script step.

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1, send F1 to confirm sync. | — | wire-only |
| 2 | Send F3 to enable RTE. | EGM ACKs F3. | wire-only |
| 3 | Insert credits and press cashout. | EGM prints a ticket. When responding to a host long poll addressed to the EGM, the EGM emits an **RTE-format exception 3D**: `(01)` 1-byte EGM address; `(FF)` 1-byte RTE identifier; `(3D)` 1-byte exception code; `(0000-FFFF)` 2-byte CRC LSB first. EGM answers LP 3D with the correct validation ID and amount. | needs human judgement (paper) |
| 4 | Insert credits. Stop Loop 1, then cash out. | EGM prints a ticket. | needs human judgement (paper) |
| 5 | Send F1, F2, F1, F2, F1, F2. | EGM emits the RTE-format exception `3D` (format as step 3) when responding to general poll `81`. | wire-only |
| 6 | Start Loop 1. Send F4 to disable RTE. | — | wire-only |

---

### 10.1.7 — Cashout Ticket 2 (power cycle and unique ID)
*Requirement [1] 10.1.7 · est. 20–30 min · 10 steps*
Covers: maintaining ticket information during power-off; unique validation numbers between
power-off and RAM clear.

**HostSim setup** — Test Group: `Standard Validation`, Test Number: `TEST_10.01.07`
**One Key Polls** — `F1 = 3D-Send cashout ticket information, address 1`
**Polling Loops** — `1 = Basic_1` · **Filters** — `1 = Display One-Key Poll`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1. | — | wire-only |
| 2 | Insert credits and cash out. | EGM prints a ticket with correct data. EGM issues exception `3D`. | needs human judgement (paper) |
| 3 | **Power cycle the EGM**, wait for comms to re-establish. | — | needs EGM stimulus |
| 4 | Send F1. | EGM answers LP 3D with the validation ID and amount **identical to the ticket printed in step 2**. | wire-only |
| 5 | Insert credits and cash out. | EGM prints a ticket with correct data. EGM issues exception `3D`. EGM answers LP 3D with matching ID and amount. **This ticket's validation ID is non-zero and different from step 2's.** | needs human judgement (paper) |
| 6 | Send F1. | EGM answers LP 3D with the validation ID and amount matching step 5's ticket. | wire-only |
| 7 | **RAM clear the EGM** and reconfigure it for standard validation. Confirm comms with HostSim. | — | needs EGM stimulus |
| 8 | Insert credits and cash out. | EGM prints a ticket with correct data. EGM issues exception `3D`. EGM answers LP 3D with matching ID and amount. **This ticket's validation ID is non-zero and different from step 2's or step 4's.** | needs human judgement (paper) |
| 9 | Send F1. | EGM answers F1 with a non-zero validation ID and an amount matching step 8's ticket. | wire-only |
| 10 | Repeat steps 5 and 6 once more. | **This ticket's validation ID differs from every previously printed ticket's ID.** | needs human judgement (paper) |
---

## 2. Full transcription — Chapter 10.2, Secure Enhanced Validation

From `10.2/EGM_TestScripts_v1.2_Chapter10_2_SecureEnhancedValidation.html`, pages 170–205,
same document id, **released 2009/05/22**.

> **Scope warning.** The local HTML covers **10.2.1 – 10.2.15 only**. Its own closing line
> says: *"後半段 10.2.16~10.2.30 請見另一份文件"* — "for 10.2.16–10.2.30 see a separate
> document". `TestData\Enhance_Val.dat` contains saved HostSim configurations for
> `TEST_10.2.01` … `TEST_10.2.30`, confirming the chapter really has **30** tests. The
> other 15 are **not on disk**.

### 2.0 Chapter preconditions and reference tables

**Estimated total: 6 – 10 hours for the 15 tests present.**

| Test | Subject | Estimate |
|---|---|---|
| 10.2.1 | Secure Enhanced Validation Configuration | 20–30 min |
| 10.2.2 | 40 ms Polling Cycle | 5–10 min |
| 10.2.3 | Printing Cashout Tickets | 40–60 min |
| 10.2.4 | More Enhanced Algorithm | 45–60 min |
| 10.2.5 | Validation Type (4D) — 1 | 30–45 min |
| 10.2.6 | Validation Type (4D) — 2 | 40–60 min |
| 10.2.7 | Validation Information (4D) — 3 | 30–45 min |
| 10.2.8 | Ticket Buffer Full | 40–60 min |
| 10.2.9 | Ticket Data (7D) | 40–60 min |
| 10.2.10 | Set Extended Ticket Data (7C) | 40–60 min |
| 10.2.11 | Restricted Ticket Title (7C) | 30–40 min |
| 10.2.12 | Extended Validation Status (7B) — 1 | 40–60 min |
| 10.2.13 | Extended Validation Status (7B) — 2 | 40–60 min |
| 10.2.14 | Extended Validation Status (7B) — 3 | 45–60 min |
| 10.2.15 | Extended Validation Status (7B) — 4 | 30–45 min |

Equipment and EGM configuration required for the whole chapter:
- HostSim; a ticket printer; a bill/coin acceptor to load credits; an **attendant key**
  (to reset handpays and open the main door); several already-printed cashout tickets
- EGM: **Secure Enhanced Validation** enabled; **handpay validation** enabled;
  **restricted ticket printing** enabled; **ticket redemption** enabled
- EGM SAS address = 1; EGM **not** in RTE mode unless a test says otherwise

Risks called out by the document:

| Risk | Mitigation given |
|---|---|
| 10.2.4 needs many cashouts to verify different validation IDs | Prepare enough cash/tokens; record the expected value for each |
| 10.2.6 needs a jackpot handpay | Lower the jackpot limit so it triggers easily |
| 10.2.7 needs repeated cashouts until the index rolls over | Long; **may need 30+ cycles** |
| 10.2.8 needs the ticket buffer filled | Requires many consecutive cashouts with no LP 4D read |
| 10.2.12–10.2.15 need repeated EGM reconfiguration | Record the 7B status-bit changes at every step |

**Long polls used by Secure Enhanced Validation**

| LP | Meaning | Direction |
|---|---|---|
| `4C` | Set Secure Enhanced Validation ID | Host to EGM |
| `4D` | Send Enhanced Validation Information | Host to EGM (EGM returns ticket data) |
| `58` | Receive Validation Number | EGM to Host |
| `70` | Send Ticket Validation Data | Host to EGM (redemption) |
| `71` | Redeem Ticket | Host to EGM |
| `7B` | Extended Validation Status (controls printer/handpay/restricted/redemption) | Host to EGM |
| `7C` | Set Extended Ticket Data (Location / Title / Address) | Host to EGM |
| `7D` | Set Ticket Data (Host ID / Expiration / Name / Address) | Host to EGM |
| `A0` | Send Enabled Game Features | Host to EGM |

Validation ID is **8 bytes** here, versus 4 BCD in Standard validation.

**Exception codes**

| Code | Meaning |
|---|---|
| `3D` | A cashout ticket has been printed |
| `3E` | A handpay receipt has been printed |
| `3F` | Validation ID not configured — **re-sent every 15 seconds** |
| `67` | Ticket inserted, awaiting host validation |

**Validation type codes**

| Code | Meaning |
|---|---|
| `00` | Cashable ticket |
| `01` | Restricted promotional ticket |
| `02` | Non-restricted promotional ticket |
| `10` | Cancelled credit handpay **with** receipt |
| `20` | Jackpot win handpay **with** receipt |
| `40` | Cancelled credit handpay **without** receipt |
| `60` | Jackpot win handpay **without** receipt |

**LP 7B status bits**

| Bit | Meaning (1 = yes) |
|---|---|
| 0 | Printer usable as cashout device |
| 1 | Printer usable as handpay-receipt device |
| 2 | Handpay validation and receipt configured |
| 3 | Printing restricted tickets allowed |
| 4 | Printing foreign restricted tickets allowed |
| 5 | Ticket redemption allowed |
| 6–14 | Reserved (= 0) |
| 15 | Secure enhanced validation currently configured |

---

### 10.2.1 — Secure Enhanced Validation Configuration
*Requirement [1] 10.2.1 · est. 20–30 min · 13 steps*
Verifies: LP A0; exception 3F; LP 4C.

**HostSim setup** — Test Group: `Enhanced Validation`, Test Number: `TEST_10.02.01`
**One Key Polls**
- `F1 = A0-Send Enabled Features Game 0, address 1`
- `F2 = 4C-Set Validation ID seq 1, address 1`
- `F3 = 4C-Set Validation ID seq 99, address 1`
- `F4 = 4C-View Validation ID, address 1`

**Polling Loops** — `1 = Basic_1` · **Filters** — `Display One-Key Poll`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | RAM clear the EGM if necessary. Configure the EGM for Secure Enhanced Validation. Enable handpay validation, receipt printing, restricted-ticket printing, and validation extensions if supported. | — | needs EGM stimulus |
| 2 | Start the polling loop. Send F1. | EGM answers F1 with: `(01)` EGM address (no wakeup bit); `(A0)` 1-byte command; `(0000)` 2-BCD game number = 0; 1-byte Features1; 1-byte Features2; 1-byte Features3; 3-byte reserved LSB first; 2-byte CRC LSB first. **Features1 bit 5 = 0 and bit 6 = 1** (Enhanced validation). Features1 bit 4 = 1 if validation extensions supported, else 0. Features1 bit 7 = 1 if ticket redemption enabled, else 0. Features2 bit 0 = 1 if ticket amount is included in total cancelled credits, else 0. | wire-only |
| 3 | Observe the trace window for 30 seconds. | EGM sends exception `3F` **every 15 seconds**. EGM is unplayable and in validation tilt. | needs human judgement (tilt state on cabinet) |
| 4 | Stop Loop 1. Open the main door. Power cycle the EGM and wait for a full restart. | — | needs EGM stimulus |
| 5 | Clear the trace window. Start Loop 1 and observe. | The **first** exception the EGM reports after restart is `3F`; all other exceptions come after it. EGM keeps reporting `3F` every 15 s. EGM is still unplayable and in validation tilt. | needs human judgement (tilt state) |
| 6 | Send F2. | EGM answers F2 with: EGM address (no wakeup bit); `(4C)` command; `(010010)` 3-byte machine validation ID LSB first; `(010000)` 3-byte sequence number LSB first; `(0000-FFFF)` 2-byte CRC LSB first. EGM **stops** sending exception `3F`. EGM becomes playable / validation tilt clears. | needs human judgement (tilt clears) |
| 7 | Send F4. | EGM answers F4 **exactly as in step 6**. EGM stays playable, no validation tilt. | wire-only |
| 8 | Send F3. | EGM answers F3 in step-6 format but with **sequence number = `0x99`**. | wire-only |
| 9 | Send F4. | EGM answers F4 **exactly as in step 8**. | wire-only |
| 10 | Press **Ctrl+F2** (send F2 with a deliberately corrupted CRC). | EGM **NACKs or ignores** F2. | wire-only |
| 11 | Send F4. | EGM answers F4 **exactly as in step 9** (i.e. the bad poll changed nothing). | wire-only |
| 12 | Press **Ctrl+F4**. | EGM NACKs or ignores F4. | wire-only |
| 13 | Power cycle the EGM and wait for comms to re-establish. | EGM does **not** send exception `3F` after power-up. | needs EGM stimulus |

*Note:* an EGM can only be configured for one ticket validation type.

> **`Ctrl+Fn` is a first-class primitive.** In HostSim, `Ctrl` + a function key sends the
> stored One Key Poll **with a bad CRC**. Steps 10 and 12 are pure negative tests. This is
> confirmed by `10.2.4` step 16 (*"Ctrl + F1 → EGM NACK Ctrl + F1"*) and `10.2.3` step 16
> (*"Ctrl + F1 → EGM ignores or NACKs F1"*).

---

### 10.2.2 — 40 ms Polling Cycle
*Requirement [1] 10.2.2 · est. 5–10 min · 2 steps*

**HostSim setup** — Test Group: `Enhanced Validation`, Test Number: `TEST_10.02.02`
**One Key Polls** — none · **Polling Loops** — `1 = Basic_1`, `2 = Basic Meters_1`
**Filters** — `1 = Display All Traces`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Run Loop 1 for 15 seconds, then pause and inspect the trace window. | EGM answers **every** poll sent to its address. | wire-only |
| 2 | Clear and resume the trace. Start Loop 2. Wait 15 s, then pause and inspect. | EGM answers **every** poll sent to its address. | wire-only |

*Note:* the EGM supports a 40 ms polling rate.

This is the **only fully wire-only test in the chapter** and the single cleanest candidate for
unattended automation. The captured evidence bears it out: `10.2/10.2.2/step1.trc` is
120,819 bytes / 1,289 events and `step2.trc` is 82,065 bytes / 873 events, with host
inter-poll gaps clustered at **39.4–40.2 ms**.

---

### 10.2.3 — Printing Cashout Tickets
*Requirement [1] 10.2.3 · est. 40–60 min · 21 steps*
Verifies: LP 4D; exception 3D; priority exceptions; LP 4C; the enhanced validation algorithm;
implied NACKs.

**HostSim setup** — Test Group: `Enhanced Validation`, Test Number: `TEST_10.02.03`
**One Key Polls**
- `F1 = 4D-Send Enhanced Validation Information FC-00, address 1`
- `F2 = 4D-Send Enhanced Validation Information FC-FF, address 1`
- `F3 = 4C-Mac. ID = 0x1 Seq. 0x1, address 1`
- `F4 = 4C-View Validation ID, address 1`
- `F5 = 00-General Poll, address 0`
- `F6 = 00-General Poll, address 1`

**Polling Loops** — `1 = Basic_1`, `2 = Basic_No General` · **Filters** — `1 = Display One-Key Poll`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1. | — | wire-only |
| 2 | Send F1 three times. | All three times the EGM answers F1 with: `(01)` 1-byte EGM SAS address; `(4D)` command; `(00)` 1-byte validation type = 00 (cashable); `(00)` 1-byte index number; `(00000000)` 4-BCD date; `(000000)` 3-BCD time; 8-BCD validation number = all zeros; 5-BCD amount = zeros; 2-byte ticket number = zeros LSB first; `(00)` 1-BCD validation system ID; 4-BCD expiration = zeros; `(0000)` 2-byte pool ID LSB first; 2-byte CRC LSB first. | wire-only |
| 3 | Send F3. | EGM answers F3 as in 10.2.1 step 6 but with machine validation ID = `0x01`, sequence number = `0x01`. | wire-only |
| 4 | Insert credits and cash out to ticket. Observe the trace for more than 30 s. | EGM prints a cashout ticket. EGM sends exception `3D` or `3E` **every 15 s**. | needs human judgement (paper) |
| 5 | Select Loop 2. Open the main door. Power cycle the EGM. Wait for comms to recover. | — | needs EGM stimulus |
| 6 | Send F6. | EGM answers F6 with exception `3D` or `3E`. | wire-only |
| 7 | Stop Loop 2 and wait >15 s (or until the EGM starts to chirp). Send F5, F6, F6, F6, F5, F6. | The 1st, 2nd and last F6 return exception `3D` or `3E`. The 3rd F6 may or may not be answered. | needs human judgement (audible chirp) |
| 8 | Send F5, F1, F1, F1, F5, F1. | The 1st, 2nd and last F1 are answered in step-2 format but with: validation type = `00`; **index > 0 (01–1F)**; date = the actual EGM date; time = the actual print time; **validation ID = `6338-7426-2374-8784`**; amount = the cashout amount in cents; **ticket sequence number > 0000 (0000–270F)**; validation system ID = `00`; expiration = `MMDDYYYY` or `00000001`–`00009998` days; pool ID = zeros. The 3rd F1 may or may not be answered. | wire-only |
| 9 | Start Loop 1 and observe the trace for more than 30 s. | EGM answers general polls with exception `3D` or `3E` every 15 s. **After the first 3D/3E, the EGM reports other exceptions.** | wire-only |
| 10 | Send F4. | EGM answers F4 as in step 3 but with **sequence number = `0x02`**. | wire-only |
| 11 | Send F2 and wait >15 s. | EGM answers F2 **exactly as step 8**. EGM keeps reporting `3D`/`3E` every 15 s. | wire-only |
| 12 | Send F4. | EGM answers F4 **exactly as step 10**. | wire-only |
| 13 | Send F1 and wait >15 s. | EGM answers F1 **exactly as step 8**. EGM **stops** reporting `3D`/`3E`. | wire-only |
| 14 | Send F1 again. | EGM answers F1 **exactly as step 2** (all zeros). | wire-only |
| 15 | Insert credits and cash out to ticket again. | EGM prints a cashout ticket. EGM reports `3D`/`3E` every 15 s. | needs human judgement (paper) |
| 16 | Press **Ctrl+F1**. | EGM ignores or NACKs F1. EGM keeps reporting `3D`/`3E`. | wire-only |
| 17 | Send F1. | EGM answers F1 in step-8 format but: index = step-8 index + 1; date/time = actual; **validation ID = `0277-9195-7475-6456`**; amount = the cashout amount in cents; ticket sequence number = step 8 + 1; expiration = `MMDDYYYY` or a day count. EGM stops reporting `3D`/`3E`. | wire-only |
| 18 | Send F4. | EGM answers F4 as step 3 but with **sequence number = `0x03`**. | wire-only |
| 19 | Send F3 then F4. | EGM answers both **exactly as step 18**. | wire-only |
| 20 | Insert credits and cash out to ticket. | EGM prints a cashout ticket. EGM reports `3D`/`3E` every 15 s. | needs human judgement (paper) |
| 21 | Send F1. | EGM answers F1 in step-8 format but: index = step-17 index + 1; date/time = actual; **validation ID = `0677-4902-7529-6352`**; amount = the cashout amount in cents; ticket sequence number = step 17 + 1; expiration = `MMDDYYYY` or a day count. EGM stops reporting `3D`/`3E`. | wire-only |

> The hard-coded expected validation IDs (`6338-7426-2374-8784`, `0277-9195-7475-6456`,
> `0677-4902-7529-6352`) are the **output of the SAS secure-enhanced validation algorithm**
> for machine ID `0x01` with sequence numbers 1, 2, 3. SasWorkbench must implement that
> algorithm to compute expectations, or the test degrades to "some non-zero value".
> Corroboration: `TicketList.dat` in the toolkit root contains the literal string
> `6338742623748784` alongside other 16-digit validation numbers — the simulator keeps a
> ticket database keyed by these values.

---

### 10.2.4 — More Enhanced Algorithm
*Requirement [1] 10.2.4 · est. 45–60 min · 17 steps*
Verifies: LP 4C and the enhanced validation algorithm at boundary values.

**HostSim setup** — Test Group: `Enhanced Validation`, Test Number: `TEST_10.02.04`
**One Key Polls**
- `F1 = 4C-Mac. ID = 0xFFFFFF Seq = 0xFFFFFE, address 1`
- `F2 = 4C-Mac. ID = 0x1 Seq = 0xFFFFFE, address 1`
- `F3 = 4C-Mac. ID = 0xFFFF99 Seq = 0x999999, address 1`
- `F4 = 4C-Mac. ID = 0x654321 Seq = 0x123456, address 1`
- `F5 = 4C-View Validation ID, address 1`

**Polling Loops** — `1 = Basic_1` · **Filters** — `1 = Display One-Key Poll`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1. | — | wire-only |
| 2 | Send F1. | Answer in 10.2.1 step-6 format with machine validation ID = `0xFFFFFF`, sequence number = `0xFFFFFE`. | wire-only |
| 3 | Insert cashable credits and cash out to ticket. Observe the trace. | EGM prints a cashout ticket, sends `3D`/`3E`, and answers LP 4D with: validation type `00`; index > 0 (01–1F); actual date/time; **validation ID = `0000-0000-8006-1624`**; amount in cents; ticket sequence number > 0000; validation system ID `00`; expiration `MMDDYYYY` or day count. | needs human judgement (paper) |
| 4 | Send F5. | Same as step 2 but **sequence number = `0xFFFFFF`**. | wire-only |
| 5 | Insert cashable credits and cash out to ticket. | Ticket printed, `3D`/`3E`, LP 4D with index = step-3 index + 1, **validation ID = `3577-5759-0786-4320`**, ticket sequence = step 3 + 1. | needs human judgement (paper) |
| 6 | Send F5. | Same as step 2 but **sequence number = `0x0`** (rolled over from `FFFFFF`). | wire-only |
| 7 | Insert cashable credits and cash out to ticket. | Ticket printed, `3D`/`3E`, LP 4D with index = step-5 index + 1, **validation ID = `9529-4486-5049-2376`**, ticket sequence = step 5 + 1. | needs human judgement (paper) |
| 8 | Send F5. | Same as step 2 but **sequence number = `0x1`**. | wire-only |
| 9 | Send F2 then F5. | Both answer with machine validation ID = `0x1`, sequence number = `0xFFFFFE`. | wire-only |
| 10 | Send F3 then F5. | Both answer with machine validation ID = `0xFFFF99`, sequence number = `0x999999`. | wire-only |
| 11 | Insert cashable credits and cash out to ticket. | Ticket printed, `3D`/`3E`, LP 4D with index = step-7 index + 1, **validation ID = `2195-7162-0687-6517`**, ticket sequence = step 7 + 1. | needs human judgement (paper) |
| 12 | Send F5. | As step 10 but **sequence number = `0x99999A`**. | wire-only |
| 13 | Send F4 then F5. | Both answer with machine validation ID = `0x654321`, sequence number = `0x123456`. | wire-only |
| 14 | Insert cashable credits and cash out to ticket. | Ticket printed, `3D`/`3E`, LP 4D with index = step-11 index + 1, **validation ID = `7166-4767-3591-9448`**, ticket sequence = step 11 + 1. | needs human judgement (paper) |
| 15 | Send F5. | As step 13 but **sequence number = `0x123457`**. | wire-only |
| 16 | Press **Ctrl+F1**. | EGM **NACKs** Ctrl+F1. | wire-only |
| 17 | Repeat step 14 (insert credits, cash out to ticket). | EGM prints a cashout ticket with **validation number = `4329-0620-6382-8368`**. | needs human judgement (paper) |

> Seven independently computable expected validation IDs. This test is, in effect, a
> **golden-vector suite for the SAS secure-enhanced validation algorithm** — exactly the kind
> of thing SasWorkbench can turn into a pure unit test of `libsas`'s algorithm implementation,
> with no EGM at all.

---

### 10.2.5 — Validation Type (4D) — 1
*Requirement [1] 10.2.5 · est. 30–45 min · 13 steps*
Verifies validation types `00` cashable, `01` restricted promotional, `02` non-restricted
promotional.

**HostSim setup** — Test Group: `Enhanced Validation`, Test Number: `TEST_10.02.05`
**One Key Polls** — `F1 = 70-Send ticket validation data, address 1`, `F2 = 71-Send Redeem Ticket Command, address 1`
**Polling Loops** — `1 = Basic_1` · **Filters** — `1 = Ticket Tests`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1. | — | wire-only |
| 2 | Insert cash credits and cash out to ticket. | Ticket printed; `3D`/`3E`; LP 4D as 10.2.3 step 2 with **validation type = `00`**. | needs human judgement (paper) |
| 3 | For the ticket just printed, edit F2: validation number = the ticket's validation number; transfer code = `01` (restricted promotional); transfer amount = `1000`; parsing code = 0; restricted expiration = pool ID = 0. *(The validation number is the last 16 digits under the barcode; the first 2 digits are the Validation System ID.)* | — | **needs human judgement** — read the barcode off paper |
| 4 | Insert the ticket. On receiving exception `67`, send F1 then F2. | EGM credits **$10**. | needs EGM stimulus |
| 5 | Cash out credits to ticket (assumes the EGM is configured to print restricted promotional tickets). | EGM prints a **$10.00** ticket; `3D`/`3E`; LP 4D with **validation type = `01`**. | needs human judgement (paper) |
| 6 | For the ticket just printed, edit F2's validation data: validation number = that ticket's; transfer code = `02` (non-restricted promotional); transfer amount = `1500`; parsing code = 0; restricted expiration = pool ID = 0. | — | needs human judgement (barcode) |
| 7 | Insert the ticket. On exception `67`, send F1, F2. | EGM credits **$15**. | needs EGM stimulus |
| 8 | Cash out credits to ticket. | EGM prints a **$15.00** ticket; `3D`/`3E`; LP 4D with **validation type = `00`**. | needs human judgement (paper) |
| 9 | Insert bill credits and play until a win posts to the credit meter. Cash out all remaining credits to ticket. | Ticket printed; `3D`/`3E`; LP 4D with **validation type = `00`**. | needs human judgement (play + paper) |
| 10 | Take a cashout ticket. Edit F2: validation number = that ticket's; transfer code = `01`; transfer amount = `500`; parsing code = 0; restricted expiration = pool ID = 0. | — | needs human judgement (barcode) |
| 11 | Insert the ticket. On exception `67`, send F1, F2. | EGM credits **$5**. | needs EGM stimulus |
| 12 | Wager up to $5 and play. | — | needs EGM stimulus |
| 13 | Cash out the credits produced by the win. If step 12 produced no win, repeat steps 10–12 until there are credits to cash out. | EGM prints a ticket for the remaining credits; `3D`/`3E`; LP 4D with **validation type = `00`**. | needs human judgement (play + paper) |

*Note:* the validation type reflects the type of ticket/receipt that was created.

---

### 10.2.6 — Validation Type (4D) — 2
*Requirement [1] 10.2.6 · est. 40–60 min · 13 steps*
Verifies validation types `10`, `20`, `40`, `60` (handpay with/without receipt).

**HostSim setup** — Test Group: `Enhanced Validation`, Test Number: `TEST_10.02.06`
**One Key Polls** — none · **Polling Loops** — `1 = Basic_1` · **Filters** — `1 = Ticket Tests`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1. | — | wire-only |
| 2 | Adjust cashout/credit limits so a cashout handpay can be triggered. Enable handpay receipts if supported; **if not supported, jump to step 8**. | — | needs EGM stimulus |
| 3 | Insert enough credits to trigger a handpay. Cash out credits. | EGM locks into handpay. | needs human judgement (cabinet state) |
| 4 | Reset the handpay. | EGM prints a **cashout receipt**; `3D`/`3E`; LP 4D with **validation type = `10`**. | needs human judgement (paper) |
| 5 | Adjust jackpot/credit limits so win-to-handpay triggers easily. | — | needs EGM stimulus |
| 6 | Insert credits and play until win-to-handpay. | EGM locks into handpay for the win amount. | needs human judgement (play) |
| 7 | Reset the handpay. | EGM prints a **jackpot receipt**; `3D`/`3E`; LP 4D with **validation type = `20`**. | needs human judgement (paper) |
| 8 | Disable handpay receipts if currently enabled. | — | needs EGM stimulus |
| 9 | Adjust cashout/credit limits so a cashout handpay can be triggered. | — | needs EGM stimulus |
| 10 | Insert enough credits to trigger a handpay. Cash out credits. | EGM locks into handpay; `3D`/`3E`; LP 4D with **validation type = `40`**. | needs human judgement (cabinet state) |
| 11 | Adjust jackpot/credit limits so win-to-handpay triggers easily. | — | needs EGM stimulus |
| 12 | Insert credits and play until win-to-handpay. | EGM locks into handpay for the win amount. | needs human judgement (play) |
| 13 | Reset the handpay. | EGM prints a jackpot receipt; `3D`/`3E`; LP 4D with **validation type = `60`**. | needs human judgement (paper) |

---

### 10.2.7 — Validation Information (4D) — 3
*Requirement [1] 10.2.7 · est. 30–45 min · 8 steps*
Verifies: index number; ticket number; circular buffer behaviour.

**HostSim setup** — Test Group: `Enhanced Validation`, Test Number: `TEST_10.02.07`
**One Key Polls** — `F1 = 4D-Send Enhanced Validation Information FC-xx, address 1`
**Polling Loops** — `1 = Basic_1` · **Filters** — `1 = Ticket Tests`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1. | — | wire-only |
| 2 | Re-adjust credit/jackpot limits so a normal cashout ticket can be produced. | — | needs EGM stimulus |
| 3 | Insert credits. Cash out to ticket and watch the LP 4D response in the trace. Record the index number for later comparison. | Ticket printed for the correct amount; `3D`/`3E`; LP 4D in 10.2.3 step-2 format with **index number > 0 (01–1F)** and **ticket number > 0 (0000–270F), LSB first**. The LP 4D ticket number **matches the number printed on the ticket** (printed in decimal, so convert before comparing). | needs human judgement (paper) |
| 4 | Repeat step 3 until the index number returns to `01`. | The index number increments from some value up to **N**, then **rolls back to 1, skipping 0**, where N is the ticket buffer size and **N must be between 5 and 31**. The ticket number keeps incrementing and does **not** roll back to 0 during this test. Every ticket number matches the printed ticket. | needs human judgement (paper, many iterations) |
| 5 | Power cycle the EGM and wait for comms to re-establish. | — | needs EGM stimulus |
| 6 | Insert credits and cash out to ticket. | Ticket printed for the correct amount; `3D`/`3E`; LP 4D with **index number = `02`**, ticket number = last ticket of step 4 + 1, and matching the printed ticket. | needs human judgement (paper) |
| 7 | Change F1's function code to `1`. | LP 4D returns the correct ticket data with **index number = `01`**. | wire-only |
| 8 | Repeat step 7 with function codes 2 … N (N = ticket buffer size). | LP 4D returns the correct ticket data with **index number equal to the function code entered in F1**. | wire-only |

---

### 10.2.8 — Ticket Buffer Full
*Requirement [1] 10.2.8 · est. 40–60 min · 21 steps*
Verifies: no ticket while an unread ticket is in the buffer; no ticket when the loop breaks;
validation tilt when the buffer is full of unread tickets.

**HostSim setup** — Test Group: `Enhanced Validation`, Test Number: `TEST_10.02.08`
**One Key Polls** — `F1 = 4D-Send Enhanced Validation Information FC-00, address 1`
**Polling Loops** — `1 = Basic_1` · **Filters** — `1 = Ticket Tests`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1 and send F1 to confirm EGM and HostSim are in sync. | — | wire-only |
| 2 | Re-adjust credit/jackpot limits so a normal cashout ticket can be produced. | — | needs EGM stimulus |
| 3 | Insert credits. Cash out to ticket. | Ticket printed for the correct amount; `3D`/`3E`. | needs human judgement (paper) |
| 4 | Insert credits. Cash out to ticket again. | **EGM locks into handpay** for the credit amount. | needs human judgement (cabinet state) |
| 5 | Reset the handpay. | EGM returns to playable. EGM keeps reporting `3D`/`3E` every 15 s. | needs human judgement |
| 6 | Insert credits. Cash out to ticket again. | EGM locks into handpay again. | needs human judgement |
| 7 | Reset the handpay. | EGM returns to playable. `3D`/`3E` continues every 15 s. | needs human judgement |
| 8 | Power cycle the EGM and wait for comms to re-establish. | EGM is playable after boot. `3D`/`3E` continues every 15 s. | needs human judgement |
| 9 | Insert credits. Cash out to ticket again. Reset the handpay. | EGM locks into handpay again; after reset it is playable; `3D`/`3E` continues every 15 s. | needs human judgement |
| 10 | Send F1 repeatedly until the LP 4D response data is **all zeros**. | — | wire-only |
| 11 | Insert credits. Cash out to ticket. | Ticket printed for the correct amount; `3D`/`3E`. | needs human judgement (paper) |
| 12 | Send F1. | LP 4D returns the correct ticket data. EGM **stops** reporting `3D`/`3E`. | wire-only |
| 13 | Insert credits. | — | needs EGM stimulus |
| 14 | Stop Loop 1 and wait > 10 s. | — | wire-only |
| 15 | Cash out credits to ticket. | **EGM locks into handpay** for the credit amount. | needs human judgement |
| 16 | Reset the handpay. | — | needs EGM stimulus |
| 17 | Start Loop 1. Send F1. | LP 4D returns the correct ticket data. EGM stops reporting `3D`/`3E`. | wire-only |
| 18 | Insert credits and print tickets. Print **N−1** tickets (N = ticket buffer size). | EGM keeps reporting `3D`/`3E` every 15 s. EGM stays playable. | needs human judgement (paper, many iterations) |
| 19 | Insert credits. | After the credits are inserted the **EGM becomes disabled** — because only one buffer slot remains for unread validation information. `3D`/`3E` continues every 15 s. | needs human judgement (cabinet state) |
| 20 | Send F1 once. | EGM answers F1 with the **oldest unread** ticket information in the buffer. `3D`/`3E` continues. **EGM returns to playable.** | needs human judgement (cabinet state) |
| 21 | Send F1 repeatedly until the LP 4D response data is all zeros. | EGM returns the correct ticket data one record at a time until every ticket record has been read. | wire-only |

---

### 10.2.9 — Ticket Data (7D)
*Requirement [1] 10.2.9 · est. 40–60 min · 21 steps*
Verifies: host ID; expiration; name and addresses; type-S and global LP 7D.

**HostSim setup** — Test Group: `Enhanced Validation`, Test Number: `TEST_10.02.09`
**One Key Polls**
- `F1 = 7D-Set Ticket Data 1, address 1`
- `F2 = 7D-Set Ticket Data 2, address 0`
- `F3 = 7D-Set Ticket Data No Location, address 1`
- `F4 = 7D-Set Ticket Data No Address 1, address 1`
- `F5 = 7D-Set Ticket Data No Address 2, address 0`
- `F6 = 7D-Set Ticket Data Blanks, address 1`
- `F7 = 7D-Set Ticket Data Null, address 1`
- `F8 = 7D-Interrogate, address 1`

**Polling Loops** — `1 = Basic_1` · **Filters** — `1 = Ticket Tests`

> `ReleaseNotes.txt`, TESTS 10x: *"10.02.09: The HostSim saved 7D (Interrogate) long poll was
> incorrectly formatted. It has been changed so that 'Set Data' is unchecked. The LPs stored
> as F3, F4, F5, and F6 all now use **ASCII blanks** to blank a field per the protocol. The LP
> stored as F7 has had the raw poll information properly formatted to now allow Steps 20 and
> 21 to be properly performed. An Exception 3D automatic response has been added."*

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1. This test assumes LP 7B and 7C have not previously been sent; if they have, a RAM clear may be needed. | — | needs EGM stimulus |
| 2 | Send F1. | EGM answers F1 with: `(01)` 1-byte address; `(7D)` command; `(01)` 1-byte **ACK flag**; `(0000-FFFF)` 2-byte CRC LSB first. | wire-only |
| 3 | Send F8. | EGM answers F8 as step 2 but with **ACK flag = `0`**. | wire-only |
| 4 | Insert credits. Cash out to ticket. | Ticket printed for the correct amount; `3D`/`3E`; LP 4D expiration = **30 days** or `MMDDYYYY` (EGM current date + 30 days). The **printed ticket** contains: Expiration = 30 days; Location = `Test Casino`; Address1 = `111 Penny Lane`; Address2 = `Anywhere World 01210`. | needs human judgement (paper) |
| 5 | Send F2. | EGM does **not** answer F2 (global poll, address 0). | wire-only |
| 6 | Send F8. | EGM answers F8 as step 2, **ACK flag = `01`**. | wire-only |
| 7 | Insert credits. Cash out to ticket. | LP 4D expiration = **255 days** or `MMDDYYYY` (+255 days). Printed ticket: Expiration = 255 days; Location = `Test Casino 2`; Address1 = `111 Penny Lane 2`; Address2 = `Anywhere World 01212`. | needs human judgement (paper) |
| 8 | Send F3. | EGM answers F3 as step 2, ACK flag = `01`. | wire-only |
| 9 | Insert credits. Cash out to ticket. | Ticket printed; LP 4D: expiration = 30 days; **Location = (blank)**; Address1 = `111 Penny Lane`; Address2 = `Anywhere World 01210`. | needs human judgement (paper) |
| 10 | Send F4. | EGM answers F4 as step 2, ACK flag = `01`. | wire-only |
| 11 | Insert credits. Cash out to ticket. | Expiration = 30 days; Location = `Test Casino`; **Address1 = (blank)**; Address2 = `Anywhere World 01210`. | needs human judgement (paper) |
| 12 | Send F5. | EGM does **not** answer F5 (global poll, address 0). | wire-only |
| 13 | Send F8. | EGM answers F8 as step 2, ACK flag = `01`. | wire-only |
| 14 | Send F8 again. | EGM answers F8 as step 2 but **ACK flag = `0`**. | wire-only |
| 15 | Insert credits. Cash out to ticket. | Expiration = 30 days; Location = `Test Casino`; Address1 = `111 Penny Lane`; **Address2 = (blank)**. | needs human judgement (paper) |
| 16 | Send F6. | EGM answers F6 as step 2, ACK flag = `01`. | wire-only |
| 17 | Insert credits. Cash out to ticket. | Expiration = **99 days**; Location, Address1 and Address2 all **blank**. | needs human judgement (paper) |
| 18 | Send F1 **twice per second for 5 seconds**. | EGM answers **every** F1 as step 2 with ACK flag = `01`. | wire-only |
| 19 | Insert credits. Cash out to ticket. | Ticket printed for the correct amount; `3D`/`3E`; LP 4D expiration = 30 days or `MMDDYYYY`. Printed ticket: Expiration = 30 days; Location = `Test Casino`; Address1 = `111 Penny Lane`; Address2 = `Anywhere World 01210`. | needs human judgement (paper) |
| 20 | Send F7 and record the response. | — | wire-only |
| 21 | Insert credits. Cash out to ticket. | **If** step 20's F7 ACK flag was `01`: LP 4D expiration = **254 days**; ticket Expiration = 254 days; Location = `AB`; Address1 = `a`; Address2 = `b`. **Otherwise** (ACK ≠ 01) the result must equal step 19. | needs human judgement (paper) |

---

### 10.2.10 — Set Extended Ticket Data (7C)
*Requirement [1] 10.2.10 · est. 40–60 min · 16 steps*
Verifies: location information; type-S and global LP 7C; LP 7C interrogation; bad (null) data;
omitting certain data codes/data; retention in non-volatile memory.

**HostSim setup** — Test Group: `Enhanced Validation`, Test Number: `TEST_10.02.10`
**One Key Polls**
- `F1 = 7C-Set Extended Ticket Data 1, address 1`
- `F2 = 7C-Set Extended Ticket Data 2, address 0`
- `F3 = 7C-Set Extended Ticket Data all codes 0 length, address 1`
- `F4 = 7C-Set Extended Ticket Data Null, address 1`
- `F5 = 7C-Set Extended Ticket Data No Res Title, address 1`
- `F6 = 7C-Set Extended Ticket Data Interrogate, address 1`
- `F7 = 7D-Set Ticket Data 2, address 1`

**Polling Loops** — `1 = Basic_1` · **Filters** — `1 = Ticket Tests`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1. Assumes the EGM supports Validation Extensions (see 10.2.1's LP A0 response, feature code 1 bit 4). | — | wire-only |
| 2 | Send F7. | EGM answers F7 with ACK flag = `01`. | wire-only |
| 3 | Send F1. | EGM answers F1: `(01)` address; `(7C)` command; `(01)` ACK flag; `(0000-FFFF)` 2-byte CRC LSB first. | wire-only |
| 4 | Send F6. | EGM answers F6 as step 3 but **ACK flag = `0`**. | wire-only |
| 5 | Insert credits. Cash out to ticket. Assumes LP 7B has not previously been sent. | Ticket printed; `3D`/`3E`; LP 4D expiration = 255 days or `MMDDYYYY`. Printed ticket: Expiration = 255 days; Ticket title = default (e.g. `Cashout Ticket`); Location = `TEST CASINO`; Address1 = `111 PENNY LANE`; Address2 = `ANYWHERE, WORLD 01210`. | needs human judgement (paper) |
| 6 | Send F2. | EGM does **not** answer F2 (global poll, address 0). | wire-only |
| 7 | Send F6 twice. | 1st F6: answer as step 3 with ACK flag = `01`. 2nd F6: **ACK flag = `00`**. | wire-only |
| 8 | Insert credits. Cash out to ticket. | Expiration = 255 days; title = default; Location = `TEST CASINO 2`; Address1 = `111 PENNY LANE 2`; Address2 = `ANYWHERE, WORLD 01210 2`. | needs human judgement (paper) |
| 9 | Send F3. | EGM answers F3 as step 3, ACK flag = `01`. | wire-only |
| 10 | Insert credits. Cash out to ticket. | Expiration = 255 days; title = default; Location / Address1 / Address2 = **default, or the previous 7D data**. | needs human judgement (paper) |
| 11 | Send F1 again. | EGM answers F1 as step 3, ACK flag = `01`. | wire-only |
| 12 | Insert credits. Cash out to ticket. | Expiration = 255 days; title = default; Location = `TEST CASINO`; Address1 = `111 PENNY LANE`; Address2 = `ANYWHERE, WORLD 01210`. | needs human judgement (paper) |
| 13 | Send F7. | EGM answers F7 with ACK flag = `01`. | wire-only |
| 14 | Insert credits. Cash out to ticket. | Same as step 12 — i.e. **the 7C data wins over the 7D data**. | needs human judgement (paper) |
| 15 | **Power cycle** the EGM and wait for comms to re-establish. | — | needs EGM stimulus |
| 16 | Insert credits and cash out to ticket. | Same as step 12 — the 7C data **survived the power cycle** (non-volatile). | needs human judgement (paper) |

---

### 10.2.11 — Restricted Ticket Title (7C)
*Requirement [1] 10.2.11 · est. 30–40 min · 13 steps*

**HostSim setup** — Test Group: `Enhanced Validation`, Test Number: `TEST_10.02.11`
**One Key Polls**
- `F1 = 7C-Set Extended Ticket Data 2, address 0`
- `F2 = 7C-Set Extended Ticket Data Res. Title 0 length, address 1`
- `F3 = Set Extended Ticket Data Res Title Only`
- `F4 = 70-Send ticket validation data, address 1`
- `F5 = 71-Send Redeem Ticket Command, address 1`

**Polling Loops** — `1 = Basic_1` · **Filters** — `1 = Ticket Tests`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1. Assumes the EGM supports printing restricted tickets and Validation Extensions. | — | wire-only |
| 2 | Send F1. | EGM does **not** answer F1 (global poll, address 0). | wire-only |
| 3 | Take a ticket and edit F5's validation data: validation number = that ticket's; amount = `1700`; transfer code = `01`; all other fields = 0. | — | needs human judgement (barcode) |
| 4 | Insert the ticket; on exception `67`, send F4 then F5. | EGM credits **$17**. | needs EGM stimulus |
| 5 | Cash out credits to ticket. | EGM prints a **$17.00** ticket; `3D`/`3E`; LP 4D expiration = 255 days. Printed ticket: Expiration = 255 days; **Ticket title = `PLAYABLE ONLY 2`**; Location = `TEST CASINO 2`; Address1 = `111 PENNY LANE 2`; Address2 = `ANYWHERE, WORLD 01210 2`. | needs human judgement (paper) |
| 6 | Send F2. | EGM answers F2 with ACK flag = `01`. | wire-only |
| 7 | Take a ticket and edit F5: validation number = that ticket's; amount = `1400`; transfer code = `01`; others = 0. | — | needs human judgement (barcode) |
| 8 | Insert the ticket; on `67`, send F4, F5. | EGM credits **$14**. | needs EGM stimulus |
| 9 | Cash out credits to ticket. | **$14.00** ticket; `3D`/`3E`; LP 4D expiration = 255 days. Printed ticket: **Ticket title = default title**; Location/Address as step 5. | needs human judgement (paper) |
| 10 | Send F3. | EGM answers F3 with ACK flag = `01`. | wire-only |
| 11 | Take a ticket and edit F5: validation number = that ticket's; amount = `1900`; transfer code = `01`; others = 0. | — | needs human judgement (barcode) |
| 12 | Insert the ticket; on `67`, send F4, F5. | EGM credits **$19**. | needs EGM stimulus |
| 13 | Cash out credits to ticket. | **$19.00** ticket; `3D`/`3E`; LP 4D expiration = 255 days. Printed ticket: **Ticket title = `RESTRICTED PROMO`**; Location/Address as step 5. | needs human judgement (paper) |

---

### 10.2.12 — Extended Validation Status (7B) — 1
*Requirement [1] 10.2.12 · est. 40–60 min · 18 steps*
Verifies bit 0 (printer as cashout device) and bit 1 (printer as handpay-receipt device).

**HostSim setup** — Test Group: `Enhanced Validation`, Test Number: `TEST_10.02.12`
**One Key Polls**
- `F1 = 7B-Disable printer as cashout device (bit0 = 0), address 1`
- `F2 = 7B-Enable printer as cashout device (bit0 = 1), address 0`
- `F3 = 7B-Disable printer as HP receipt device (bit1 = 0), address 0`
- `F4 = 7B-Enable printer as HP receipt device (bit1 = 1), address 1`
- `F5 = 7B-Interrogate, address 1`
- `F6 = 70-Send ticket validation data, address 1`
- `F7 = 71-Send Redeem Ticket Command, address 1`

**Polling Loops** — `1 = Basic_1` · **Filters** — `1 = Ticket Tests`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1. Assumes Validation Extensions supported, handpay validation and receipt printing enabled. | — | wire-only |
| 2 | Send F5. | EGM answers F5: `(01)` address; `(7B)` command; `(00-nn)` 1-byte length of following bytes excluding CRC; `(00000000-FFFFFFFF)` 4-byte asset number LSB first; `(0000-3F80)` 2-byte **status bits** LSB first; `(0000-9999)` 2-BCD cashable/receipt expiration; `(0000-9999)` 2-BCD restricted default expiration; 2-byte CRC LSB first. Asset number matches the EGM's configured asset number. Expirations match the currently configured day counts. Status bit 0 = 1 if the printer can be used for cashout else 0; bit 1 = 1 if usable for handpay receipts else 0; bit 2 = 1 if handpay validation and receipt configured else 0; bit 3 = 1 if restricted printing allowed else 0; bit 4 = 1 if foreign restricted printing allowed else 0; bit 5 = 1 if ticket redemption allowed else 0; **bits 6–14 = 0**; bit 15 = 1 if secure enhanced is configured else 0. | wire-only |
| 3 | If step 2's status bit 0 or 1 is 0, check the EGM's corresponding configuration (RAM clear and reconfigure if necessary). | — | needs EGM stimulus |
| 4 | Send F1. | EGM answers as step 2 but **status bit 0 = 0**. | wire-only |
| 5 | Insert credits and cash out to ticket. | **EGM locks into handpay.** | needs human judgement (cabinet state) |
| 6 | Reset the handpay. | EGM prints a **handpay receipt**; `3D`/`3E`; LP 4D with **validation type = `10`** and amount = the handpay amount in cents. | needs human judgement (paper) |
| 7 | Send F2 then F5. | F5 answers as step 2 but **status bit 0 = 1**. | wire-only |
| 8 | Insert credits. | — | needs EGM stimulus |
| 9 | Cash out credits to ticket. | EGM prints a cashout ticket for the correct amount; `3D`/`3E`; LP 4D with **validation type = `00`** and amount = the cashout amount in cents. | needs human judgement (paper) |
| 10 | Send F3 then F1. | F1 answers with **status bit 0 = 0 and bit 1 = 0**. | wire-only |
| 11 | Insert credits and cash out to ticket. | EGM locks into handpay. | needs human judgement |
| 12 | Reset the handpay. | EGM does **not** print a handpay receipt; `3D`/`3E`; LP 4D with **validation type = `40`**, amount = handpay amount in cents. | needs human judgement (absence of paper) |
| 13 | Adjust jackpot/credit limits so win-to-handpay triggers easily. | — | needs EGM stimulus |
| 14 | Insert credits and play until win-to-handpay. | — | needs EGM stimulus |
| 15 | Reset the handpay. | EGM does **not** print a jackpot receipt; `3D`/`3E`; LP 4D with **validation type = `60`**. | needs human judgement (absence of paper) |
| 16 | Send F4. | EGM answers as step 10's F1 but **status bit 1 = 1**. | wire-only |
| 17 | Insert credits and cash out. | EGM locks into handpay. | needs human judgement |
| 18 | Reset the handpay. | EGM prints a handpay receipt; `3D`/`3E`; LP 4D with **validation type = `10`**. | needs human judgement (paper) |

---

### 10.2.13 — Extended Validation Status (7B) — 2
*Requirement [1] 10.2.13 · est. 40–60 min · 21 steps*
Verifies bit 1 (use printer as handpay-receipt device) and bit 2 (validate handpay and receipt).

**HostSim setup** — Test Group: `Enhanced Validation`, Test Number: `TEST_10.02.13`
**One Key Polls**
- `F1 = 7B-Enable all, address 1`
- `F2 = 7B-Disable printer as cashout device (bit0 = 0), address 1`
- `F3 = 7B-Disable printer as HP receipt device (bit1 = 0), address 1`
- `F4 = 7B-Disable handpay validation & receipt (bit2 = 0), address 1`
- `F5 = 7B-Validate handpay and receipt (bit2 = 1), address 1`
- `F6 = 70-Send ticket validation data, address 1`
- `F7 = 71-Send Redeem Ticket Command, address 1`

**Polling Loops** — `1 = Basic_1` · **Filters** — `1 = Ticket Tests`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1. Assumes Validation Extensions supported; handpay validation and receipt printing enabled. | — | wire-only |
| 2 | Send F1. | EGM answers as 10.2.12 step 2 with **status bits 0–2 all = 1**. | wire-only |
| 3 | If any of bits 0, 1, 2 is 0, check the EGM configuration (RAM clear and reconfigure if necessary); otherwise go to step 4. | — | needs EGM stimulus |
| 4 | Send F4. | Answer as 10.2.12 step 2 with **status bit 2 = 0**. | wire-only |
| 5 | Adjust cashout/credit limits so a cashout handpay can be triggered. | — | needs EGM stimulus |
| 6 | Insert credits and cash out. | EGM locks into handpay. | needs human judgement |
| 7 | Reset the handpay. | EGM does **not** print a handpay receipt. EGM does **not** send `3D`/`3E`. | needs human judgement (absence of paper) |
| 8 | Insert credits and play until win-to-handpay. | — | needs EGM stimulus |
| 9 | Reset the handpay. | EGM does **not** print a jackpot receipt. EGM does **not** send `3D`/`3E`. | needs human judgement |
| 10 | Send F5. | Answer as 10.2.12 step 2 with **status bit 2 = 1**. | wire-only |
| 11 | Adjust cashout/credit limits so a cashout handpay can be triggered. | — | needs EGM stimulus |
| 12 | Insert enough credits to trigger a handpay. Cash out credits. | EGM locks into handpay. | needs human judgement |
| 13 | Reset the handpay. | EGM prints a handpay receipt; `3D`/`3E`; LP 4D with **validation type = `10`**. | needs human judgement (paper) |
| 14 | Adjust the jackpot limit so win-to-handpay triggers easily. Insert credits and play until win-to-handpay. | — | needs EGM stimulus |
| 15 | Reset the handpay. | EGM prints a jackpot receipt; `3D`/`3E`; LP 4D with **validation type = `20`**. | needs human judgement (paper) |
| 16 | Send F3. | Answer as 10.2.12 step 2 with **status bit 1 = 0**. | wire-only |
| 17 | Adjust cashout/credit limits so a cashout handpay can be triggered. | — | needs EGM stimulus |
| 18 | Insert enough credits to trigger a handpay. Cash out credits. | EGM locks into handpay. | needs human judgement |
| 19 | Reset the handpay. | EGM does **not** print a handpay receipt; `3D`/`3E`; LP 4D with **validation type = `40`**. | needs human judgement |
| 20 | Adjust the jackpot limit so win-to-handpay triggers easily. Insert credits and play until win-to-handpay. | — | needs EGM stimulus |
| 21 | Reset the handpay. | EGM does **not** print a jackpot receipt; `3D`/`3E`; LP 4D with **validation type = `60`**. | needs human judgement |

---

### 10.2.14 — Extended Validation Status (7B) — 3
*Requirement [1] 10.2.14 · est. 45–60 min · 27 steps*
Verifies bit 0 (printer as cashout device), bit 3 (print restricted tickets), bit 4 (print
foreign restricted tickets), and cash-out when both cashable and restricted credits exist.

**HostSim setup** — Test Group: `Enhanced Validation`, Test Number: `TEST_10.02.14`
**One Key Polls**
- `F1 = 7B-Enable all, address 1`
- `F2 = 7B-Disable printer as cashout device (bit0=0), address 1`
- `F3 = 7B-No restricted tickets (bit3=0), address 1`
- `F4 = 7B-Print restricted tickets (bit3=1), address 1`
- `F5 = 7B-No foreign restricted tickets (bit4=0), address 1`
- `F6 = 7B-Print foreign restricted tickets (bit4=1), address 1`
- `F7 = 70-Send ticket validation data, address 1`
- `F8 = 71-Send Redeem Ticket Command, address 1`

**Polling Loops** — `1 = Basic_1` · **Filters** — `1 = Ticket Tests`

> `ReleaseNotes.txt`, TESTS 10x: *"10.02.14: The 7B long poll stored as F4 on the simulator
> has been modified to have the correct control mask bit checked."* — LP 7B carries a
> **control mask** distinct from the value bits; setting one bit means "change only this bit".

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1. Assumes Validation Extensions supported, restricted-ticket printing and ticket redemption enabled. | — | wire-only |
| 2 | Configure the EGM to allow printing restricted tickets and ticket redemption (RAM clear if not already set). | — | needs EGM stimulus |
| 3 | Send F1. | Answer as 10.2.12 step 2 with **status bit 3 = 1** and **bit 4 = 1**. | wire-only |
| 4 | Take a cashout ticket. Edit F8: validation number = that ticket's; amount = `1200`; transfer code = `01`; others = 0. | — | needs human judgement (barcode) |
| 5 | Insert the ticket; on exception `67`, send F7 then F8. | EGM credits **$12.00**. | needs EGM stimulus |
| 6 | Cash out credits to ticket. | EGM prints a **restricted promotional** ticket; `3D`/`3E`; LP 4D with correct validation data, **validation type = `01`**. | needs human judgement (paper) |
| 7 | Send F3. | Answer as step 3 but **status bit 3 = 0**. | wire-only |
| 8 | Take the previous cashout ticket. Edit F8: validation number = that ticket's; amount = `900`; transfer code = `01`; others = 0. | — | needs human judgement (barcode) |
| 9 | Insert the ticket; on `67`, send F7, F8. | EGM credits **$9.00**. | needs EGM stimulus |
| 10 | Cash out credits to ticket. | EGM does **not** print a ticket. It may briefly display a message that promotional credits cannot be cashed out. | needs human judgement (on-screen message) |
| 11 | Insert cash credits and cash out to ticket. | EGM prints a cashout ticket for **only the cashable amount**; `3D`/`3E`; LP 4D with **validation type = `00`**. | needs human judgement (paper) |
| 12 | Send F4. | Answer as step 3 with **status bit 3 = 1**. | wire-only |
| 13 | Cash out credits to ticket. | EGM prints a **$9.00 restricted** ticket; LP 4D as step 6. | needs human judgement (paper) |
| 14 | Send F2. | Answer as step 3 with **status bit 0 = 0**. | wire-only |
| 15 | Take a cashout ticket. Edit F8: validation number = that ticket's; amount = `700`; transfer code = `01`; others = 0. | — | needs human judgement (barcode) |
| 16 | Insert the ticket; on `67`, send F7, F8. | EGM credits **$7.00**. | needs EGM stimulus |
| 17 | Cash out credits to ticket. | EGM does **not** cash out. It may briefly display a printer-unavailable or similar message. | needs human judgement (on-screen message) |
| 18 | Send F1. | EGM answers F1 with **bits 0–5 = 1** and **bit 15 = 1**. | wire-only |
| 19 | Cash out credits to ticket again. | EGM prints a **$7.00 restricted** ticket; `3D`/`3E`; LP 4D with **validation type = `01`**. | needs human judgement (paper) |
| 20 | Send F5. | Answer as step 18 but **bit 4 = 0**. | wire-only |
| 21 | Take a cashout ticket. Edit F8: validation number = that ticket's; amount = `1100`; transfer code = `01`; others = 0. | — | needs human judgement (barcode) |
| 22 | Insert the ticket; on `67`, send F7, F8. | EGM credits **$11.00**. | needs EGM stimulus |
| 23 | Cash out credits to ticket. | EGM prints an **$11.00 restricted** ticket; `3D`/`3E`; LP 4D with **validation type = `01`**. | needs human judgement (paper) |
| 24 | Insert cash credits. | — | needs EGM stimulus |
| 25 | Take a cashout ticket. Edit F8: validation number = that ticket's; amount = `500`; transfer code = `01`; others = 0. | — | needs human judgement (barcode) |
| 26 | Insert the ticket; on `67`, send F7, F8. | EGM credits **$5.00**. | needs EGM stimulus |
| 27 | Cash out credits to ticket. | EGM prints a cashout ticket for the step-24 amount; `3D`/`3E`; LP 4D with correct data, **validation type = `00`**. The EGM **may then immediately print a $5.00 restricted ticket**. The EGM keeps sending `3D`/`3E` until the host sends LP 4D to read the restricted ticket's information; LP 4D then returns correct validation info with **validation type = `01`**. | needs human judgement (paper) |

---

### 10.2.15 — Extended Validation Status (7B) — 4
*Requirement [1] 10.2.15 · est. 30–45 min · 18 steps*
Verifies bit 5 (ticket redemption) and cash-out when cashable, restricted and non-restricted
credits all exist.

**HostSim setup** — Test Group: `Enhanced Validation`, Test Number: `TEST_10.02.15`
**One Key Polls**
- `F1 = 7B-Enable all, address 1`
- `F2 = 7B-Disable ticket redemption (bit5=0), address 1`
- `F3 = 7B-Enable ticket redemption (bit5=1), address 1`
- `F4 = 70-Send ticket validation data, address 1`
- `F5 = 71-Send Redeem Ticket Command, address 1`

**Polling Loops** — `1 = Basic_1` · **Filters** — `1 = Ticket Tests`

| # | Step | Pass criterion | Class |
|---|---|---|---|
| 1 | Start Loop 1. Assumes Validation Extensions supported, secure enhanced and ticket redemption enabled. | — | wire-only |
| 2 | Configure the EGM to allow printing restricted tickets and ticket redemption (RAM clear if not already set). | — | needs EGM stimulus |
| 3 | Send F1. | Answer as 10.2.12 step 2 with **status bit 5 = 1**. | wire-only |
| 4 | Take a cashout ticket. Edit F5: validation number = that ticket's; amount = `400`; transfer code = `01`; others = 0. | — | needs human judgement (barcode) |
| 5 | Insert the ticket; on `67`, send F4, F5. | EGM credits **$4.00** as restricted promotional credits. | needs EGM stimulus |
| 6 | Take a cashout ticket. Edit F5: validation number = that ticket's; amount = `600`; transfer code = `00`; others = 0. | — | needs human judgement (barcode) |
| 7 | Insert the ticket; on `67`, send F4, F5. | EGM credits **$6.00** cashable. | needs EGM stimulus |
| 8 | Send F2. | Answer as step 3 but **status bit 5 = 0**. | wire-only |
| 9 | Insert **any** ticket and observe the trace. | EGM does **not** report exception `67`. **EGM immediately ejects the ticket.** | needs EGM stimulus |
| 10 | Cash out credits. | EGM prints a **$6.00** cashout ticket; `3D`/`3E`; LP 4D with **validation type = `00`**. EGM **may then immediately print a $4.00 restricted ticket** and keep sending `3D`/`3E` until the host reads it with LP 4D, which then returns **validation type = `01`**. | needs human judgement (paper) |
| 11 | Send F3. | Answer as step 3 with **status bit 5 = 1**. | wire-only |
| 12 | Take a cashout ticket. Edit F5: validation number = that ticket's; amount = `800`; transfer code = `01`; others = 0. | — | needs human judgement (barcode) |
| 13 | Insert the ticket; on `67`, send F4, F5. | EGM credits **$8.00** restricted promotional. | needs EGM stimulus |
| 14 | Take a cashout ticket. Edit F5: validation number = that ticket's; amount = `1300`; transfer code = `02`; others = 0. | — | needs human judgement (barcode) |
| 15 | Insert the ticket; on `67`, send F4, F5. | EGM credits **$13.00** non-restricted promotional. | needs EGM stimulus |
| 16 | Take a cashout ticket. Edit F5: validation number = that ticket's; amount = `200`; transfer code = `00`; others = 0. | — | needs human judgement (barcode) |
| 17 | Insert the ticket; on `67`, send F4, F5. | EGM credits **$2.00** cashable. | needs EGM stimulus |
| 18 | Cash out credits to ticket. | EGM prints a **$15.00** cashout ticket ($2 cashable + $13 non-restricted); `3D`/`3E`; LP 4D with **validation type = `00`**. EGM **may then immediately print an $8.00 restricted ticket** and keep sending `3D`/`3E` until the host reads it with LP 4D, which then returns **validation type = `01`**. | needs human judgement (paper) |
---

## 3. The manual-effort baseline

### 3.1 Stated times

| Chapter | Tests | Document's headline estimate | Sum of its own per-test estimates |
|---|---|---|---|
| 10.1 Standard Cashout Validation | 7 | **2.5 – 4 hours** | 135 – 195 min (2.25 – 3.25 h) |
| 10.2 Secure Enhanced Validation (10.2.1–10.2.15 only) | 15 | **6 – 10 hours** | 515 – 755 min (8.6 – 12.6 h) |
| **Total for the material on disk** | **22** | **8.5 – 14 hours** | **10.9 – 15.9 hours** |

The per-test estimates sum to more than the chapter headline for 10.2 (8.6–12.6 h vs 6–10 h).
Take **8.5–14 hours as the conservative figure and 16 hours as the realistic worst case**, for
22 tests — barely 5% of the toolkit's 427 saved test configurations (§4.1). A first-pass EGM
that fails and has to be retested doubles this.

Not counted: 10.2.16–10.2.30 (15 more tests, script not on disk, likely another 6–10 h) and
10.3 System Validation (26 tests, estimate UNKNOWN).

### 3.2 Step-by-step classification

Every step in both chapters was classified by `classify3.py`, whose rule set is:

- **needs human judgement** if the step or its pass criterion mentions printed output, an
  on-cabinet state (playable / disabled / validation tilt / locked into handpay), reading a
  validation number off a barcode, comparing to the EGM's own meter display, an on-screen
  message, or an audible chirp.
- otherwise **needs EGM stimulus** if the step requires a physical/operator action: insert
  cash, insert a ticket, cash out, play, open a door, power cycle, RAM clear, reset a handpay,
  change a limit, reconfigure the EGM, or recable.
- otherwise **wire-only**.

Full per-step detail is in `step-classification.txt`.

**Totals across 297 steps (10.1 = 55, 10.2.1–15 = 242):**

| Class | Steps | Share |
|---|---|---|
| needs human judgement | 130 | 44% |
| wire-only | 109 | 37% |
| needs EGM stimulus | 58 | 20% |

**Per test:**

| Test | wire-only | needs EGM stimulus | needs human judgement | total |
|---|---|---|---|---|
| 10.1.1 | 1 | 1 | 0 | 2 |
| 10.1.2 | 1 | 1 | 0 | 2 |
| 10.1.3 | 8 | 2 | 6 | 16 |
| 10.1.4 | 2 | 3 | 1 | 6 |
| 10.1.5 | 2 | 4 | 7 | 13 |
| 10.1.6 | 4 | 0 | 2 | 6 |
| 10.1.7 | 4 | 2 | 4 | 10 |
| 10.2.1 | 6 | 2 | 5 | 13 |
| **10.2.2** | **2** | **0** | **0** | **2** |
| 10.2.3 | 16 | 1 | 4 | 21 |
| 10.2.4 | 11 | 0 | 6 | 17 |
| 10.2.5 | 1 | 4 | 8 | 13 |
| 10.2.6 | 1 | 3 | 9 | 13 |
| 10.2.7 | 3 | 2 | 3 | 8 |
| 10.2.8 | 6 | 4 | 11 | 21 |
| 10.2.9 | 12 | 1 | 8 | 21 |
| 10.2.10 | 9 | 1 | 6 | 16 |
| 10.2.11 | 3 | 3 | 7 | 13 |
| 10.2.12 | 4 | 4 | 10 | 18 |
| 10.2.13 | 4 | 7 | 10 | 21 |
| 10.2.14 | 5 | 7 | 15 | 27 |
| 10.2.15 | 4 | 6 | 8 | 18 |

### 3.3 What this actually means for automation

**Only one test in the corpus — 10.2.2, 40 ms Polling Cycle — is 100% wire-only** and can run
completely unattended today. Everything else is interleaved.

But the classification understates the win, because the *distribution* matters more than the
totals:

1. **Long wire-only runs exist inside otherwise-manual tests.** 10.2.3 is 16/21 wire-only,
   10.2.4 is 11/17, 10.2.9 is 12/21, 10.2.10 is 9/16. In 10.2.4 the only manual acts are
   "insert credits and cash out" (7 times); every other step is a poll-and-check. A workbench
   that automates the wire and pauses for the human at exactly seven points converts a
   45–60 min test into a few minutes of human attention.
2. **A lot of "human judgement" is only human because the operator is reading paper.** The
   same value is *also* on the wire in the LP 4D response. Steps such as 10.2.9 step 4
   ("printed ticket contains Location = Test Casino") are physically two assertions: a wire
   assertion (LP 4D expiration = 30 days) that a workbench can make, and a paper assertion
   that it cannot. Splitting them lets the machine carry the wire half and leave a one-line
   visual checklist for the human.
3. **Three categories genuinely cannot be automated without an EGM-side agent:**
   - money in (bill/coin/ticket insertion), cashout button, play, door, service button,
     attendant key / handpay reset;
   - EGM configuration changes (RAM clear, validation mode, credit/jackpot/cashout limits,
     receipt printing on/off, channel assignment);
   - power cycle.
4. **Two categories cannot be automated at all from the host side:** reading printed paper,
   and reading the EGM's own screen (meter display, "promotional credits cannot be cashed
   out" message, validation-tilt indication). Even an EGM-side stimulus agent only helps if it
   can also *report* those states back.

**Realistic split of the 8.5–14 h baseline.** If SasWorkbench automates the wire and delegates
stimulus to an agent on the QSP side (which is feasible — the EGM here is Quixant's own
platform), the residual human time is dominated by the ~130 judgement steps, most of which are
"look at the ticket that just printed". Estimate: **the wire work collapses to minutes; the
human residue is on the order of 2–4 hours** for 10.1 + 10.2.1–15, and much of that is
re-loading the note acceptor. That is the honest claim. Anyone promising fully unattended
conformance for chapter 10 has not read the chapter.

---

## 4. The wider toolkit — the complete chapter/test map

### 4.1 The authoritative map is in `TestData\*.dat`, not the release notes

`ReleaseNotes.txt` only lists tests that *changed* in v1.2. The complete map is in the 24
saved-configuration files, which store one record per conformance test. Record layout,
established by `xxd` on `TestData\Standard_Val.dat` and cross-checked with `strings`:

```
0b 00                  record marker
<len> "TEST_10.1.01"   Pascal-style length-prefixed test number  (the record key)
... ~0x180 bytes of fixed payload: enabled-poll flags, one-key-poll slots, timing values,
    poll/loop indices, progressive levels ...
04 "COM1"              Pascal-style port name
... more payload ...
13 "Required Accounting"   Pascal-style meter-set name
```

Record stride is ~402 bytes plus the length of the variable strings. The **Test Group** is not
in the record — it is the *file*, mapped by index through `configFiles.txt` to `configDesc.txt`
(both files are 24 lines, positionally aligned; the release notes confirm they were introduced
precisely so "new test categories can be added without new code and recompiling the program").

### 4.2 The complete map

| Chapter | Test Group (`configDesc.txt`) | Config file | Test numbers present | Count |
|---|---|---|---|---|
| 1, 2 | — | — | none — UNKNOWN, presumably scope/setup sections of the document | 0 |
| **3** | Communications | `Communications.dat` | `TEST_03.01` … `03.12` | 12 |
| **4** | Required Exceptions | `Required_Exc.dat` | `TEST_04.01` … `04.40` | 40 |
| **5** | Required Long Polls | `Required_LP.dat` | `TEST_05.01` … `05.16` | 16 |
| **6** | Required Meters | `Required_Meters.dat` | `06.01`, `06.02.1–.6`, `06.03`–`06.07`, `06.08.1–.3`, `06.09`–`06.14` | 21 |
| **7** | Multi-Game | `Multi_Game.dat` | `TEST_07.01` … `07.06` | 6 |
| **8** | Weighted Theo | `Weighted_Theo.dat` | `TEST_08.01` … `08.04` | 4 |
| **9** | Multi-Denom | `Multi_Denom.dat` | `TEST_09.01` … `09.07` | 7 |
| **10.1** | Standard Validation | `Standard_Val.dat` | `TEST_10.1.01` … `10.1.07` | 7 ← *script on disk* |
| **10.2** | Enhance Validation | `Enhance_Val.dat` | `TEST_10.2.01` … `10.2.30` | 30 ← *script on disk for 01–15 only* |
| **10.3** | System Validation | `System_Val.dat` | `TEST_10.3.01` … `10.3.26` | 26 |
| **11** | RTE | `RTE.dat` | `11.01`–`11.06`, `11.07.1–.8`, `11.08`–`11.25` | 32 |
| **12** | Non-SAS Progressive | `NonSAS_Prog.dat` | `12.01`–`12.10`, `12.10.01`, `12.11`–`12.17` | 18 |
| **13** | SAS Progressive | `SAS_Prog.dat` | `13.01`–`13.06`, `13.06.1`, `13.07`–`13.19` | 20 |
| **14** | AFT Bonus | `AFT_Bonus.dat` | `TEST_14.01` … `14.13` | 13 |
| **15** | Legacy Bonus | `Legacy_Bonus.dat` | `TEST_15.01` … `15.18` | 18 |
| **16.1** | AFT Common | `AFT_Common.dat` | `TEST_16.1.01` … `16.1.20` | 20 |
| **16.2** | AFT InHouse | `AFT_Inhouse.dat` | `16.2.01`–`16.2.15`, `16.2.15.1`, `16.2.16`–`16.2.30` | 31 |
| **16.3** | AFT Debit | `AFT_Debit.dat` | `TEST_16.3.01` … `16.3.11` | 11 |
| **17** | Bill Hopper | `Bill_Hopper.dat` | **none** — file contains no `TEST_` records | 0 |
| **18** | Tournament | `Tournament.dat` | `TEST_18.01` … `18.09` | 9 |
| **19** | Authentication | `Authentication.dat` | `19.01`–`19.04`, `19.05.01`, `19.05.02`, `19.06`–`19.13` | 14 |
| **20–23** | Miscellaneous/Legacy | `Misc_Legacy.dat` | `20.01`–`20.03` (3); `21.01`–`21.14`, `21.15.01`, `21.15.02` (16); `22.01`–`22.17` (17); `23.01`–`23.07`, `23.08.01`, `23.08.02`, `23.09`, `23.10.01`–`23.10.04`, `23.11`–`23.13` (17) | 53 |
| **24–29** | Addenda | `Adendda.dat` *(sic)* | `24.01`–`24.03` (3); `25.01`–`25.04`, `25.05.01`–`25.05.03` (7); `26.01` (1); `27.01`–`27.04`, `27.05.01`, `27.05.02` (6); `28.01` (1); `29.01` (1) | 19 |
| — | User Defined | `User_defined.dat` | user copies of `25.*` | (11, not counted) |
| | | | **TOTAL** | **427** |

**Chapter 17 = Bill Hopper, defined but never populated.** Evidence: `Bill_Hopper.dat` exists
and is listed in `configFiles.txt` in the position between Authentication (19) and
Miscellaneous/Legacy (20), it contains zero `TEST_` records, and the v1.1 GUI notes in
`ReleaseNotes.txt` state *"Added predefined test configurations for each of the
category...**Bill hoppers not yet defined**."* Bill-hopper *meter codes* `0058`–`0078` were
added (SASDS-7), but no test procedures. UNKNOWN whether the printed test-script document has
a chapter 17.

**Chapters 1 and 2 have no test records.** UNKNOWN what they are; most likely introduction and
test-setup sections of the printed script.

### 4.3 What `ReleaseNotes.txt` adds — v1.2 change list by chapter

Read in full (1,003 lines). The v1.2 section attributes changes to these tests:

| Chapter | Tests changed in v1.2 | Nature of change (verbatim substance) |
|---|---|---|
| 3x | `03.04` | *Settings:* "Send RESYNC on startup" and "Send RESYNC on comm error" **unchecked** |
| 5x | `05.16` | Default trace filter changed to `Display One-Key Poll` |
| 6x | `06.05` | **Meter code `0008`** added to meter set `Required Group 1` |
| 7x | `07.02` | `F4` modified to have Game Number attribute `0` |
| 9x | `09.07` | The stored `LP FE (B0)` "Long Poll Not Supported" (slot F2) now **appends a valid CRC** |
| 10x | `10.01.06` | LP `3D` added as an **auto-response to exception 3D** |
| 10x | `10.02.09` | 7D Interrogate was misformatted — "Set Data" now unchecked; F3/F4/F5/F6 now use **ASCII blanks** to blank a field per the protocol; F7 raw poll reformatted so steps 20/21 work; exception `3D` auto-response added |
| 10x | `10.02.14` | 7B poll stored as F4 fixed to check the **correct control-mask bit** |
| 10x | `10.02.30` | No longer auto-responds with **exception 68** |
| 10x | `10.03.05`, `10.03.07` | Auto-respond with **LP 4D** to an EGM-reported **exception 3D** |
| 10x | `10.03.08` | 7D requests F3/F4/F5 fixed to send genuinely blank fields |
| 10x | `10.03.10` | `F6` added; auto-respond with **LP 71** to an EGM-reported **exception 68** |
| 10x | `10.03.12` | Auto-respond LP 71 to exception 68; LP 7B "Print restricted tickets (bit3=1)" in F4 fixed to check **only** control-mask bit 3 |
| 10x | `10.03.13`, `10.03.14` | Auto-respond with LP 71 to exception 68 |
| 11x | `11.08` | Default filter corrected |
| 11x | `11.12` | Long polls F3 and F4 changed to match the test script |
| 15x | `15.02` | LP built as `F5` given a **60-second delay** |
| 15x | `15.14`–`15.18` | Stored LPs changed to match the test script |
| 16x | `16.01.02` | F1 changed to match test script |
| 16x | `16.01.03` | F1–F8 changed to match the One Key Polls in the test header; LP 73 in F2 sends a valid request with an **invalid Asset Number** |
| 16x | `16.01.20` | Default polling loop changed to `Basic_1` |
| 16x | `16.02.15`, `16.02.16` | Auto-respond LP 4D to EGM exception `3D` or `3E` |
| 16x | `16.02.17` | Auto-respond LP 4D to `3D`/`3E`; **new LP 72 created and stored as F2**; account number field added and populated |
| 16x | `16.02.19` | F1/F2/F3 made **non-date-specific** |
| 16x | `16.02.21` | LP 72 in F3 transfers the amount stated in the poll description |
| 16x | `16.02.25` | LP 72 interrogate auto-response to exception `69` **disabled** |
| 16x | `16.02.28` | Auto-respond LP 72 to exception `69` |
| 16x | `16.02.29` | Auto-respond LP 4D to `3D`/`3E` |
| 16x | `16.03.03` | Auto-respond LP 72 to exception `69` |
| 16x | `16.03.05` | LP 72 in F5: Total Debit Amount changed to **$21.50** (was $41.50) |
| 16x | `16.03.06` | LP 72 in F7: Field 1 = Code `41`, data `7788` |
| 16x | `16.03.07` | LP 72 in F5: Field 1 = Code `41`, data `7777` |
| 16x | `16.03.08` | F4 made non-date-specific |
| 16x | `16.03.11` | LP 72 interrogate auto-response to exception `69` disabled |
| 22x | `22.07` | F3 one-key poll changed to match the test-script header |
| 23x | `23.02` | Meter sets created as detailed in the test-script header |
| 23x | `23.12` | Auto-respond with **LP 71-FF** to an EGM-reported exception `3D` |
| 24x–29x | (whole range) | *"Added support for new test scripts (Section 11) created as a result of recent errata and addenda to the SAS protocol, for miscellaneous new functionality, meter change notification, player denomination reporting, session support, tip money meter, and keyed credits."* |

Chapters 24–29 therefore cover, in some order, six subjects: miscellaneous new functionality,
meter change notification, player denomination reporting, session support, tip money meter,
keyed credits. **Which number maps to which subject is UNKNOWN** — the notes give the list but
not the mapping. Corroborating artefacts exist: One Key Polls `B1 Send Current Player
Denomination`, `B2 Send Enabled Player Denominations`, `B3 Send token denomination`; the JIRA
export records meter `0079` (session played) and exception `2F` (bill acceptor version
changed) being added; `Tracefilters.dat` contains a filter literally named `2F Filter`.

### 4.4 The One Key Poll / Loop / Filter / Meter Set catalogue

Extracted from `One Key Polls.doc` (raw text region, `okp_body.txt`). Opening line:
*"One Key Polls, Polling Loops, and Filters used in this section are defined below."*

**One Key Polls** — 200+ named entries, keyed by `<command hex> <name>`, each with a plain-text
description of its field settings. Examples verbatim:

```
00 General Poll                 = This is a single byte message that consists of the EGM address ORed with 80 hex.
1F_noWB                         = This is a 1F poll configured without a wakeup bit.
21 ROM Signature 0 seed         = Not multi denom, valid CRC, Signature Seed = 0.
4C Set Validation ID Seq 1      = Not multi denom, valid CRC, Machine ID = 100001, Sequence Number = 1.
4D Send Enhanced Validation Information FC 00 = Not multi denom, valid CRC, Function Code = 00.
58 Receive Validation Number    = Validation System ID = 11, Validation Number = 1234.
71 Send Redeem Ticket Command   = Transfer code = Valid cashable ticket, Transfer amount = 500, Parsing code = 00,
                                  Validation system ID = 00, validation data = xx…, Restricted expiration = 0, Pool ID = 00.
71 Interrogate                  = 71-01-FF-CRC
7C Set Extended Ticket Data 1   = Location = Test Casino, Address1 = 111 Penny Lane, Address 2 = Anywhere, World 01210,
                                  Restricted Ticket Title = Playable Only, Debit Ticket Title = Debit Ticket.
7D Interrogate                  = 7D-00-CRC
FE Undefined                    = This is a manually created undefined poll FE.
```

The recurring value `User altered to fit test` marks slots the operator is expected to edit at
run time — the toolkit's admission that ~40% of its own poll library is a template, not a
definition.

Note `1F_noWB` — **a poll deliberately sent without the wakeup bit**. Any host that models
bytes as 8-bit cannot express this test.

**Polling Loops** — 22 named loops, each an ordered list of `(command, description, address)`.
Two representative definitions, verbatim from the document's table:

```
Basic_1:            80@0 RESYNC, 1F@1, 1F@38, 51@1, 81@1 (General Poll), 54@1, 54@127, 7E@1, 81@1
Basic_No General:   1F@1, 1F@38, 51@1, 54@1, 54@127, 7E@1
Basic_38:           80@0, 1F@38, 1F@1, 51@38, 81@38, 54@38, 54@1, 7E@38, 81@38
Basic_127:          80@0, 1F@127, 1F@1, 51@127, 81@127, 54@127, 54@1, 7E@127, 81@127
Minimal_1:          1F@1, 81@1
Minimal_2:          1F@1
Handpay:            1F@1, 1B@1, 81@1
Basic Meters_1:     80@0, 1F@1, 1F@38, 51@1, 6F "Basic Meters 1"@1, 81@1, 54@1, 54@127, 7E@1,
                    AF "Basic Meters 1"@1, 81@1
Ticket Meters:      6F "Ticket"@1, 1F@1, 6F "Ticket 2"@1, 1F@38, 6F "Ticket 3"@1, 51@1, 81@1, 54@1,
                    AF "Ticket"@1, 54@127, AF "Ticket 2"@1, 7E@1, AF "Ticket 3"@1, 81@1
Basic_MLT:          80@0 then 1F/51 for addresses 0–4, then 81 for 0–4, then 54/7E for 0–4, then 81 for 0–4
Unimplemented polls: BF@1, CF@1, 1F@1
AFT / Progressive-Meters / Multi-Denom 6F/AF game 0 / Meter-Group-1 / Meter-Group-2 /
User Defined A4 / User Defined 09 / Bonus-Meters / Tournament_1 / RTE - No General /
Basic_38_Only / Basic No Resync_1
```

`LoopDefs1.dat` confirms the loaded set and adds a couple more names
(`General Only`, `Multi-Game`, `Multi-Denom`, `Required Meters`, `Legacy Meters Test`,
`All 2F Meters`, `Multi-Game 2F`, `Multi-Denomination 2F`, `AFT Bonus`, `EFT Meters`,
`Key Meters!`, `my_loop"` — the last a locally created loop).

**The loops embed negative tests.** `1F@38` and `54@127` poll addresses `0x26` and `0x7F`,
where **no EGM exists**. Every loop iteration therefore asserts "the EGM under test does not
answer polls addressed to someone else". This is visible in the captured traces as
`HOST [26] 1F` and `HOST [7F] 54` with no `EGM` line following.

**Filters** — from `One Key Polls.doc` and `Tracefilters.dat`:

```
Display All Traces    = Built in selection for no filter selected.
Display One-Key Poll  = Built in selection for only displaying the One Key Polls sent.
Rom Signature         = Filtering for just the 21 poll.
Ticket Tests          = 3D, 4C, 4D, 57, 58, 70, 71, 7B, 7C, 7D
Tickets/AFT           = 3D, 4C, 4D, 57, 58, 70, 71, 72, 73, 74, 75, 76, 7B, 7C, 7D, A0
Multi-Game A4         = A4, 09
```
`Tracefilters.dat` also holds `11 filter`, `Progressive`, `2F Filter`, `General Poll`.
A filter is therefore **a set of command codes to include** — nothing more.

**Meter Sets** — 22 named sets, each a list of meter codes; e.g.
`Ticket = (0015),(0016),(0019),(001A),(001B),(0028),(0029),(002A),(002B),(002C),(002D)`,
`Legacy ticket meters = (000D),(000E),(000F),(0010),(0011),(0012),(0013),(0014)`,
`Tournament = (00F5),(00F6),(00F7),(00F8)`.
`MeterSetDefinitions.dat` confirms the loaded set and adds
`Core meter codes set 1` / `Core meter codes set 2`.

### 4.5 The machine-readable field schema — the most directly reusable artefact

`SASTemplates.dtd` (3,245 bytes, *"edited with XMLSPY v5 rel. 4 U by Bill Turner (Acres
Gaming)"*) defines a complete field-typing language for SAS messages:

| Element | Attributes | Meaning |
|---|---|---|
| `SAS_template` | `name`, `cmd`, `crc`, `length`, `multigame` | One message |
| `bcd_parm` | `name`, `size` | BCD field of *size* bytes |
| `hex_parm`, `binary_parm` | `name`, `size`, `length`, `radix` (binary/decimal/hex) | Integer field with display radix |
| `ascii_parm` | `name`, `size` | Fixed ASCII field |
| `variable_ascii_parm`, `variable_bcd_parm`, `variable_hex_parm` | `name`, `size`, `length` | Length-prefixed variable field |
| `variable_meter_parm`, `variable_meter_response`, `poll_2F_meter_parm` | `name` | Meter lists |
| `choice_parm` + `choice_entry` | `name`, `size`; entries `name`, `value` | Enumerated byte |
| `bitmap_parm` + `bitmap_field` | `name`, `size`; fields `size`, `offset` | Bit-addressed flags, each a `bool_parm` or a nested `choice_parm` |
| `bool_parm` | `name` | Single bit |
| `padding` | `size` | Reserved bytes |
| `optional` | `name` | Optional trailing group |
| `option_choice_parm` + `option_choice_entry` | `name`, `size` | Tagged union |
| `aft_receipt_parm` | `name` | AFT receipt sub-structure |

Counts: `PollTemplates.xml` **138** templates, `ResponseTemplates.xml` **117** templates,
`EventTemplates.xml` for RTE payloads.

Verbatim example — `ResponseTemplates.xml`, LP 4D, the response 10.2.3 through 10.2.15 assert against:

```xml
<SAS_template name="Send enhanced validation information" cmd="4D" crc="yes">
  <choice_parm name="Validation type" size="1">
    <choice_entry name="Cashable ticket from cashout or win" value="00"/>
    <choice_entry name="Restricted promotional ticket from cashout" value="01"/>
    <choice_entry name="Cashable ticket from AFT transfer" value="02"/>
    <choice_entry name="Restricted ticket from AFT transfer" value="03"/>
    <choice_entry name="Debit ticket from AFT transfer" value="04"/>
    <choice_entry name="Handpay from cashout, with receipt" value="10"/>
    <choice_entry name="Handpay from single win, with receipt" value="20"/>
    <choice_entry name="Handpay from cashout, without receipt" value="40"/>
    <choice_entry name="Handpay from single win, without receipt" value="60"/>
    <choice_entry name="Cashable ticket redeemed" value="80"/>
    <choice_entry name="Restricted promotional ticket redeemed" value="81"/>
    <choice_entry name="Nonrestricted promotional ticket redeemed" value="82"/>
    <choice_entry name="Unsupported value" value="F1"/>
  </choice_parm>
  <binary_parm name="Index number" size="1"/>
  <bcd_parm name="Date" size="4"/>
  <bcd_parm name="Time" size="3"/>
  <bcd_parm name="Validation number" size="8"/>
  <bcd_parm name="Amount" size="5"/>
  <binary_parm name="Ticket number" size="2" radix="decimal"/>
  <bcd_parm name="Validation system ID" size="1"/>
  <bcd_parm name="Expiration" size="4"/>
  <binary_parm name="Pool ID" size="2"/>
</SAS_template>
```

And LP 7B, which turns the 10.2.12–10.2.15 bit assertions into named booleans:

```xml
<SAS_template name="Extended validation status" cmd="7B" length="yes" crc="yes">
  <binary_parm name="Asset number" size="4" radix="decimal"/>
  <bitmap_parm name="Status bits" size="2">
    <bitmap_field offset="0"><bool_parm name="Use printer as cashout device"/></bitmap_field>
    <bitmap_field offset="1"><bool_parm name="Use printer as handpay receipt device"/></bitmap_field>
    <bitmap_field offset="2"><bool_parm name="Validate handpays and handpay receipts"/></bitmap_field>
    <bitmap_field offset="3"><bool_parm name="Print restricted tickets"/></bitmap_field>
    <bitmap_field offset="4"><bool_parm name="Ticket for foreign restricted amounts"/></bitmap_field>
    <bitmap_field offset="5"><bool_parm name="Ticket redemption"/></bitmap_field>
    <bitmap_field offset="15"><bool_parm name="Secure enhanced validation is configured"/></bitmap_field>
  </bitmap_parm>
  <bcd_parm name="Cashable ticket and receipt expiration" size="2"/>
  <bcd_parm name="Restricted ticket default expiration" size="2"/>
</SAS_template>
```

`A0` similarly encodes Feature 1 bits 5–6 as a nested `choice_parm` with
`Standard or none = 00`, `System = 01`, `Secure Enhanced = 02`, `Reserved = 03` — which is
exactly the assertion in 10.1.1 step 2 and 10.2.1 step 2.

### 4.6 The poll-definition export format

`PollDef.txt` (139,266 bytes) is HostSim's human-readable dump of the currently loaded poll
set: **661 named poll definitions** across 68 distinct commands. Distribution (top of the
list): `72` ×180, `74` ×60, `67` ×26, `69` ×23, `7D` ×19, `65` ×19, `73` ×18, `62` ×18,
`64` ×16, `09` ×16, `76` ×14, `6F` ×14. Record format:

```
7B - Validate handpay & handpay receipt (bit2=1)
(Bit 0) Use printer as cashout device = 1
(Bit 1) Use printer as handpay receipt device = 1
Cashable ticket and receipt expiration = 0
Restricted ticket default expiration = 0
CRC = Yes
Multi-Denom Preamble = No
```

i.e. **command + name**, then **named field = value** lines drawn straight from the XML
template, then two flags: whether a CRC is appended, and whether a multi-denom preamble is
prefixed. `10.2\10.2.1\PollDef.txt` and `10.2\10.2.3\PollDef.txt` are per-test exports of the
same format, saved next to the traces as part of the evidence pack.

---

## 5. The `.trc` step traces

### 5.1 Inventory

32 files, 285,621 bytes total. Produced by `trcsum.py`; `evts` = event lines,
`sep` = `----------` separators, `pause` = `***PAUSE***` markers.

| Path | bytes | evts | HOST | EGM | sep | pause | Trace started at |
|---|---|---|---|---|---|---|---|
| `10.1/10.1.1/step2.trc` | 1,402 | 6 | 4 | 2 | 0 | 0 | 07/20/26 09:16:34 |
| `10.1/10.1.2/step2.trc` | 1,098 | 10 | 4 | 6 | 2 | 0 | 07/20/26 09:47:18 |
| `10.1/10.1.3/step5.trc` | 8,898 | 94 | 65 | 29 | 16 | 0 | 07/20/26 10:01:32 |
| `10.1/10.1.3/step6.trc` | 616 | 5 | 4 | 1 | 1 | 0 | 07/20/26 10:08:10 |
| `10.1/10.1.3/step8.trc` | 838 | 15 | 7 | 8 | 1 | 0 | 07/20/26 10:10:12 |
| `10.1/10.1.3/step10.trc` | 6,373 | 65 | 34 | 31 | 13 | 0 | 07/20/26 10:13:07 |
| `10.1/10.1.3/step11.trc` | 6,424 | 65 | 34 | 31 | 13 | 0 | 07/20/26 10:14:29 |
| `10.1/10.1.3/step12.trc` | 2,034 | 20 | 13 | 7 | 4 | 0 | 07/20/26 10:15:15 |
| `10.1/10.1.3/step12_13.trc` | 5,332 | 55 | 30 | 25 | 11 | 0 | 07/20/26 10:15:15 |
| `10.1/10.1.3/step14.trc` | 7,740 | 80 | 45 | 35 | 16 | 0 | 07/20/26 10:15:15 |
| `10.2/10.2.1/step2.trc` | 1,441 | 6 | 4 | 2 | 1 | 1 | 07/20/26 15:07:13 |
| `10.2/10.2.1/step3.trc` | 2,625 | 25 | 12 | 13 | 5 | 1 | 07/20/26 15:07:53 |
| `10.2/10.2.1/step5.trc` | 5,223 | 60 | 33 | 27 | 12 | 1 | 07/20/26 15:10:05 |
| `10.2/10.2.1/step6.trc` | 724 | 6 | 3 | 3 | 1 | 1 | 07/20/26 15:12:17 |
| `10.2/10.2.1/step7.trc` | 1,071 | 6 | 4 | 2 | 1 | 1 | 07/20/26 15:12:41 |
| `10.2/10.2.1/step8.trc` | 792 | 6 | 4 | 2 | 1 | 1 | 07/20/26 15:13:07 |
| `10.2/10.2.1/step9.trc` | 918 | 6 | 3 | 3 | 1 | 1 | 07/20/26 15:13:30 |
| `10.2/10.2.1/step10.trc` | 682 | 6 | 4 | 2 | 1 | 1 | 07/20/26 15:13:53 |
| `10.2/10.2.1/step11.trc` | 805 | 6 | 4 | 2 | 1 | 1 | 07/20/26 15:14:11 |
| `10.2/10.2.1/step12.trc` | 646 | 6 | 3 | 3 | 1 | 1 | 07/20/26 15:14:32 |
| `10.2/10.2.1/step13.trc` | 3,414 | 40 | 22 | 18 | 8 | 1 | 07/20/26 15:42:08 |
| `10.2/10.2.2/step1.trc` | **120,819** | 1,289 | 930 | 359 | 0 | 1 | 07/20/26 15:58:45 |
| `10.2/10.2.2/step2.trc` | **82,065** | 873 | 624 | 249 | 0 | 1 | 07/20/26 16:00:01 |
| `10.2/10.2.3/step2.trc` | 3,127 | 22 | 12 | 10 | 4 | 1 | 07/20/26 17:55:49 |
| `10.2/10.2.3/step3.trc` | 702 | 6 | 3 | 3 | 1 | 0 | 07/20/26 17:56:26 |
| `10.2/10.2.3/step4.trc` | 3,173 | 31 | 16 | 15 | 6 | 0 | 07/20/26 17:56:26 |
| `10.2/10.2.3/step6.trc` | 665 | 6 | 4 | 2 | 1 | 0 | 07/20/26 17:59:41 |
| `10.2/10.2.3/step7_.trc` | 3,329 | 34 | 23 | 11 | 6 | 0 | 07/20/26 18:00:29 |
| `10.2/10.2.3/step8.trc` | 4,898 | 34 | 24 | 10 | 6 | 0 | 07/20/26 18:00:53 |
| `step2.trc` (root) | 1,019 | 2 | 1 | 1 | 0 | 0 | 07/19/26 12:47:05 |
| `step3.trc` (root) | 3,431 | 35 | 18 | 17 | 7 | 0 | 07/19/26 13:17:25 |
| `step5.trc` (root) | 3,297 | 30 | 14 | 16 | 6 | 1 | 07/19/26 13:21:22 |

Two `PollDef.txt` files are saved alongside (`10.2/10.2.1/`, `10.2/10.2.3/`) — the poll
definitions in force when those traces were taken.

### 5.2 Decoded format

Plain text, **CRLF** line endings, cp1252/ASCII. Fixed-column layout:

```
Trace started at MM/DD/YY HH:MM:SS<CRLF>
<CRLF>
Timestamp is relative to end of previous poll<CRLF>
<CRLF>
<event line><CRLF>
<annotation line><CRLF>
...
```

**Event line** (verified byte-exact by `xxd` on `10.1/10.1.1/step2.trc`):

| Columns | Content |
|---|---|
| 1–7 | Source, left-aligned in 7: `HOST   ` or `EGM    ` |
| 8–14 | Timestamp, **right-aligned in 7**: milliseconds with one decimal (`  199.5`), or `-------` for the first event of a burst |
| 15–17 | three spaces |
| 18… | Raw bytes as space-separated uppercase hex, then one trailing space |

**Annotation line**: exactly **17 spaces**, then the decoded text, then a trailing space. Field
decodes are `Name = Value; ` pairs; bitmaps decode as `(Bit n) Name = YES/NO; `; enumerations
as `Name - (hh) Label; `.

**Wakeup-bit convention**: a byte written `[XX]` was transmitted **with the 9th (wakeup) bit
set**; a bare `XX` was not. Only host-transmitted address/command bytes appear bracketed in
normal traffic. This is the trace's representation of the same 9-bit word model `libsas` uses
(bit 8 = wakeup).

**Markers**: `----------` on its own line separates bursts; `***PAUSE***` marks where the
operator paused the trace. `sep` counts in the table above are a good proxy for how many times
the operator poked the UI during a step.

**Worked example**, `10.1/10.1.1/step2.trc` in full — this is the entire evidence for test
10.1.1 step 2:

```
Trace started at 07/20/26 09:16:34

Timestamp is relative to end of previous poll

HOST   -------   [7F] 54
                 Send SAS version ID and EGM serial number:
HOST      39.5   [7F] 54
                 Send SAS version ID and EGM serial number:
HOST     199.5   [01] A0 00 00 6C 13
                 Send enabled features: Game number = 0000;
EGM       25.1   01 A0 00 00 82 CD 03 00 00 00 F3 00
                 Send enabled features: Game number = 0000; Feature 1: (Bit 0) Jackpot multiplier
                 enabled = NO; (Bit 1) AFT bonus awards = YES; ... (Bits 5 to 6) Validation style -
                 (00) Standard or none; (Bit 7) Ticket redemption = YES; ...
HOST      45.2   [01] 7E
                 Send current date and time:
EGM       25.4   01 7E 07 20 20 26 09 16 34 51 39
                 Send current date and time: Date = 07202026; Time = 091634;
```

The pass criterion — *Feature Codes byte 1 bits 6–5 = `00`* — is satisfied: byte `82` is
`1000 0010`, bits 6–5 = `00`, and the decoder prints `Validation style - (00) Standard or none`.

### 5.3 What they are: **captured evidence, not golden files**

This is the decisive question and the answer is unambiguous.

**Evidence they are local captures:**
1. **Dates.** Every `.trc` carries `Trace started at 07/19/26` or `07/20/26`, and its filesystem
   mtime matches to the minute (`10.1/10.1.1/step2.trc` internal `09:16:34`, mtime
   `2026-07-20T09:17:06`). Every file GSA shipped is stamped `2026-07-16T19:25:46`. The traces
   were made **3–4 days after the toolkit was unpacked**, by the lab.
2. **The EGM identifies itself as the device under test.** Every trace contains
   `01 1F 51 58 47 61 6D … Game ID = "QX"; Additional ID = "Gam"; … Paytable = "Game15";
   Base percentage = "9660"` and `01 54 09 36 30 32 31 30 30 30 30 31 … SAS Version = "602";
   Serial number = "100001"`. **`QX` is Quixant.** A GSA golden file would not contain a
   specific vendor's game ID, serial number, paytable name and RTP.
3. **They contain failures and noise.** `10.1/10.1.3/step5.trc` is the capture for the step
   whose pass criterion is exceptions `3D`, then `11`, `12`, `71`, `72` in order. What it
   actually contains is `6A` (AFT request for host cashout), three `3D`s, `53` (no progressive
   info for 5 seconds), `11`, `12`, `74` (printer paper low) — **no `71`/`72` change-light
   exceptions at all**, plus unrelated exceptions the criterion does not mention. A golden file
   would encode the criterion; this file records what one particular EGM did.
   `10.1/10.1.3/step8.trc` is worse: `[80] RESYNC` … `6A` … `6A` … `6A` … 5.2-second gaps —
   a broken run.
4. **They are not one-per-step.** 10.1.3 has 16 steps and 8 traces; 10.1.1 has 2 steps and one
   trace (for step 2, the only step with a criterion). Filenames like `step12_13.trc` and
   `step7_.trc` are operator naming, not a generated scheme. Nothing under `Defaults\` or
   `TestData\` contains a `.trc`.
5. **Ancillary evidence saved next to them.** `10.2/10.2.1/PollDef.txt` contains four poll
   dumps (`4C - Set Validation ID seq 1`, `seq 99`, two × `View Validation ID`) — the operator
   exporting the polls in force. Evidence pack, not fixture.

**Conclusion: they are per-step captured evidence, produced by the local lab in July 2026 as
the record of an actual conformance run against a Quixant EGM.** They are **not** GSA
expected-output golden files.

**They are still valuable, for four different reasons than replay-and-diff:**
- **Format ground truth.** They pin the exact host↔EGM byte sequences, the `[..]` wakeup
  convention, and the decoder's field-name vocabulary — a free conformance test for
  SasWorkbench's own encoder/decoder and trace writer.
- **Timing ground truth.** Host inter-poll gaps cluster at **39.4–40.2 ms** and EGM response
  latency at **22–38 ms**. That is the real timing envelope SasWorkbench must hit, measured on
  the real hardware.
- **Loop ground truth.** They confirm the documented `Basic_1` composition byte for byte,
  including the `[26] 1F` and `[7F] 54` decoy polls to non-existent addresses, and the retry
  behaviour when those go unanswered (the same poll is repeated 2–3 times).
- **Regression corpus.** They are exactly the input a `sasctl trace decode` command should be
  able to parse, so SasWorkbench can read the lab's existing evidence archive.

**They should become golden files.** Nothing stops SasWorkbench from re-running these steps
against a known-good EGM and *promoting* the resulting traces to expected output, with a
normaliser that masks timestamps, dates, serial numbers and validation IDs. That is the high-
value follow-on — but it is a thing to build, not a thing GSA shipped.
---

## 6. THE DELIVERABLE — what SasWorkbench's script format must be able to express

Everything in this section is derived from a specific step in the corpus. The citation is
given for each requirement so nothing here is invented.

### 6.1 Test identity and reporting

Every test header in both chapters has the same shape, so the script format must carry it:

| Field | Example | Source |
|---|---|---|
| Test Group | `Standard Validation` | HostSim setup line; the `TestData` file name |
| Test Number | `TEST_10.01.01` | HostSim setup line; the record key in `TestData\*.dat` |
| Requirement id | `[1] 10.1.1` | "測試需求" row |
| Title | `Standard Validation Configuration` | section heading |
| Estimate | `5–10 min` | summary table |
| Verifies | list of LPs / exceptions / behaviours | "驗證項目" row |
| References | `[1]` Requirements Document, `[2]` SAS Protocol Manual v6.02 | footer |

Output requirements, from the P/F column that every step table carries:
- **per-step verdict** — `PASS` / `FAIL` / `SKIP` / `BLOCKED` (manual step not answered) /
  `NOT-RUN`;
- **overall verdict** for the test, and for the group;
- **evidence per step**: the trace slice, in the `.trc` format of §5.2 so it drops straight
  into the lab's existing archive, plus the poll definitions in force (the lab already does
  this by hand — `10.2\10.2.1\PollDef.txt`);
- a machine-readable result document (JSON) for CI, and the human P/F table for the
  submission pack.

### 6.2 Setup and preconditions

| Requirement | Cited from |
|---|---|
| EGM SAS address | every test header, `address 1` |
| Validation mode: standard / secure enhanced / system / none | 10.1.1 s1, 10.2.1 s1 |
| RTE enabled/disabled as a precondition, and toggled mid-test | 10.1 chapter precondition; 10.1.6 s2/s6 |
| RAM clear | 10.1.1 s1, 10.1.7 s7, 10.2.1 s1, 10.2.14 s2 |
| Feature toggles: handpay validation, receipt printing, restricted-ticket printing, foreign restricted, ticket redemption, validation extensions | 10.2 chapter preconditions; 10.2.12 s3 |
| Numeric EGM limits: credit, cashout, jackpot | 10.1.5 s8/s10/s12; 10.2.6 s2/s5/s9/s11 |
| Asset number | 10.2.12 s2 pass criterion |
| Multi-channel: which SAS channel is the validating one | 10.1.4 s1 |
| **Assumption declarations** that gate a test — "assumes LP 7B and 7C have not previously been sent" | 10.2.9 s1, 10.2.10 s5 |
| **Capability guards / skip** — "if the EGM is single-channel, skip this test" | 10.1.4 s1 |
| **Conditional jump** — "if handpay receipts are not supported, jump to step 8" | 10.2.6 s2 |
| Requirement that a precondition be *verified on the wire before proceeding* — "if status bit 0 or 1 = 0, check the EGM configuration" | 10.2.12 s3, 10.2.13 s3 |

### 6.3 Poll definitions and named bindings (the "One Key Poll" concept)

A named poll is: **command byte + address + ordered named field values + `crc: yes/no` +
`multi_denom_preamble: yes/no`** (§4.6, verbatim `PollDef.txt` record shape). The field names
and their types come from `PollTemplates.xml` (§4.5), so a script writes
`sequence_number: 0x99`, not a byte offset.

Beyond the plain case the corpus demands:

| Variant | Cited from |
|---|---|
| Address 0 = **global poll**, expected to produce no response | 10.2.9 s5/s12, 10.2.10 s6, 10.2.11 s2 |
| Poll to an address **no EGM owns** (`38`, `127`) as a negative check inside every loop | `Basic_1` definition; visible as `[26] 1F` / `[7F] 54` in every trace |
| **Deliberately bad CRC** — HostSim's `Ctrl+Fn` | 10.2.1 s10/s12, 10.2.3 s16, 10.2.4 s16 |
| **Deliberately omitted wakeup bit** — the `1F_noWB` named poll | `One Key Polls.doc` |
| **Raw literal poll** — `71 Interrogate = 71-01-FF-CRC`, `7D Interrogate = 7D-00-CRC` | `One Key Polls.doc` |
| **Undefined / unimplemented command** — `FE Undefined`, loop `Unimplemented polls` (`BF`, `CF`) | `One Key Polls.doc` |
| A poll with a **control mask** separate from its value bits (LP 7B) | ReleaseNotes `10.02.14`, `10.03.12` |
| **Blanking a field with ASCII spaces** vs omitting it vs zero-length | ReleaseNotes `10.02.09`; 10.2.9 s8/s10/s16; 10.2.10 s9 (`all codes 0 length`) |
| **Null / malformed data** as a negative test | 10.2.9 s20 (`7D-Set Ticket Data Null`), 10.2.10 s4 |
| **Runtime-bound field values** — "edit F2: validation number = the ticket's validation number" | 10.2.5 s3/s6/s10; 10.2.11 s3/s7/s11; 10.2.14 s4/s8/s15/s21/s25; 10.2.15 s4/s6/s12/s14/s16 |
| **A poll that carries a send delay** | ReleaseNotes `15.02`: "LP constructed as F5 to have a 60 second delay" |
| **Date-independence** as a property of a stored poll | ReleaseNotes `16.02.19`, `16.03.08`: "modified to be non-date-specific" |
| **Binding to a short name** the steps refer to (`F1`…`F8`) | every test header |

The binding table is per test and must round-trip into the report, because the P/F sheet is
meaningless without it.

### 6.4 Named polling loops running concurrently with the foreground steps

**This is a genuine concurrency requirement, not a convenience.** A loop is an ordered list of
`(poll, address)` that runs continuously while the script's foreground steps execute. Every
test in the corpus begins "Start Loop 1", and the steps then interleave with it.

Semantics the corpus pins down:

1. **Loops are named, defined per test, and switchable mid-test.** 10.1.3 defines
   `1 = Basic_No General` and `2 = Basic_1` and moves from one to the other at step 9.
   10.2.3 defines `1 = Basic_1`, `2 = Basic_No General` and selects Loop 2 at step 5.
2. **Start / stop / pause with the foreground continuing.** 10.1.3 s8 "Stop Loop 1. Send F1,
   F2, F2, F2, F1, F2, F1"; 10.2.8 s14 "Stop Loop 1 and wait > 10 s"; 10.2.3 s7 "Stop Loop 2
   and wait > 15 s".
   **Stopping the loop is itself the stimulus** — it makes the EGM's comms watchdog fire
   (10.2.3 s7 "or until the EGM starts to chirp"; 10.2.8 s15 the EGM locks into handpay
   because the link broke).
3. **A foreground one-key poll is injected into the loop at the next poll slot — not
   immediately.** `ReleaseNotes.txt` SASDS-6 is explicit: *"Verify that one key polls are not
   sent immediately upon the keypress. The one key poll should be sent at the next polling
   time as specified by the configured settings within the simulator."* Resolution: *Fixed*.
   So the scheduler is a single serialised poll queue with a foreground priority lane, not two
   independent writers.
4. **A second, independent background emitter runs on its own timer.** The SAS progressive
   broadcast (LP 86 / LP 80) is enabled by a checkbox and paced by "Delay between progressive
   updates (ms) = 1000" (lab config, `環境架設紀錄.txt` §6). It is visible in
   `10.1/10.1.3/step10.trc` as `HOST [00] 86 0D 01 01 …` interleaved into `Basic_1`. The lab's
   troubleshooting log records `Progressive Timeout` when it stops. So: **two concurrent
   producers plus a foreground lane**, all funnelling into one serial port.
5. **Event-driven auto-responses fire from the loop, asynchronously.** From
   `ReleaseNotes.txt`: exception `3D` → send LP `3D` (`10.01.06`); exception `3D`/`3E` →
   send LP `4D` (`10.02.09`, `10.03.05`, `10.03.07`, `16.02.15/16/17/29`); exception `68` →
   send LP `71` (`10.03.10/12/13/14`); exception `69` → send LP `72` (`16.02.28`,
   `16.03.03`); exception `3D` → send LP `71-FF` (`23.12`). And they must be individually
   **disable-able** (`16.02.25`, `16.03.11`, `10.02.30` "no longer auto respond with exception
   68"). A script therefore needs `on exception X send Y` rules with per-test enable/disable.
6. **Rate-limited foreground bursts.** 10.2.9 s18: "send F1 **twice per second for 5
   seconds**" — 10 sends at 500 ms spacing, while `Basic_1` keeps running.
7. **Timing budget.** The loop must sustain a **40 ms** poll period (10.2.2; measured
   39.4–40.2 ms in `10.2/10.2.2/step1.trc`). The whole reason `gsat.exe` exists as a DOS
   program is that the 1990s Windows API could not hold this. Modern .NET on a dedicated
   thread can — but only if the design treats the loop as a real-time producer, not a timer
   callback.

### 6.5 Trace filters and recording

| Requirement | Cited from |
|---|---|
| A filter is a **set of command codes to include** | `Ticket Tests = 3D, 4C, 4D, 57, 58, 70, 71, 7B, 7C, 7D` |
| Built-in `Display All Traces` and `Display One-Key Poll` (only the foreground polls) | `One Key Polls.doc`; every test header |
| Filters are named and selected per test, sometimes more than one | headers list `Filters 1 = …` |
| **Filtering must not hide exceptions, one-key polls or auto-responses** | JIRA SASDS-1 resolution: those are shown "by design of the application", overriding the filter |
| Clear the trace / pause the trace / resume | 10.2.1 s5 "clear the trace window"; 10.2.2 s1–2 "pause and inspect", "clear and resume"; `***PAUSE***` marker in the `.trc` |
| Save the trace per step, named by step | the whole `10.1/10.1.3/step*.trc` convention |
| A separate **meter window** showing a named Meter Set and its live values | 10.1.5 s3/s5/s7/s9/s11/s13; JIRA SASDS-3 |
| A rolling **log** distinct from the trace, with a configurable max size | `SASHostLog00000.log` (binary); JIRA SASDS-22 |

Filtering is a *view* concern. SasWorkbench should record everything and filter on render —
the toolkit's own bug history (SASDS-1) is a warning about filtering at capture time.

### 6.6 The assertion vocabulary

Every distinct kind of pass criterion in the corpus, with a citation. This is the list a
SasWorkbench assertion library must cover.

| # | Assertion | Cited from |
|---|---|---|
| 1 | **Response arrives** to a given poll | 10.1.1 s2 |
| 2 | **No response** — the EGM must stay silent | 10.2.9 s5 (global poll), 10.2.15 s9 (no exception 67) |
| 3 | **NACK or ignore** — either is acceptable | 10.2.1 s10/s12, 10.2.3 s16 |
| 4 | **Format conforms to a named table** — "[2] Table 7.14b", or the toolkit's `SAS_template` | 10.1.1 s2, 10.2.1 s2, 10.2.12 s2 |
| 5 | **Field equals a literal** | 10.2.5 s2 `validation type = 00` |
| 6 | **Field bit or bit-range equals** | 10.1.1 s2 `Feature1 bits 6–5 = 00`; 10.2.12 s2 `bit 3 = 1`; **`bits 6–14 = 0`** (a reserved-range check) |
| 7 | **Field is non-zero** | 10.1.3 s12 "validation ID non-zero" |
| 8 | **Field within an inclusive range** | 10.2.3 s8 `index 01–1F`, `ticket seq 0000–270F`; 10.2.7 s4 `5 ≤ N ≤ 31` |
| 9 | **Field matches one of a set of shapes** | 10.2.3 s8 expiration is *either* `MMDDYYYY` *or* `00000001–00009998` days |
| 10 | **Field equals a value captured in an earlier step** | 10.1.3 s12 "amount matches step 11"; 10.1.7 s4 "identical to step 2" |
| 11 | **Field equals earlier value + 1** | 10.2.3 s17 `index = step8.index + 1`; 10.2.4 s5/s7/s11/s14 |
| 12 | **Whole response byte-identical to an earlier response** | 10.2.1 s7 "exactly as step 6"; s9, s11; 10.2.3 s11/s12/s19 |
| 13 | **Field differs from every previously observed value** (uniqueness across the run) | 10.1.7 s10 "differs from every previously printed ticket's ID" |
| 14 | **Field equals a computed expectation** — the secure-enhanced validation algorithm applied to (machine ID, sequence number) | 10.2.3 s8/s17/s21; 10.2.4 s3/s5/s7/s11/s14/s17 — **eight hard-coded golden vectors** |
| 15 | **Exception arrives**, by code | 10.1.3 s10 |
| 16 | **Exception arrives in a specific response slot** | 10.1.3 s5 "in the **first** general-poll response"; 10.2.3 s8 "the 1st, 2nd and last F1" |
| 17 | **Exception ordering** — a required sequence | 10.1.3 s5: `3D`, then `11`, `12`, `71`, `72` |
| 18 | **Exception priority** — X must be reported before any other exception after an event | 10.2.1 s5 "the first exception after restart is `3F`; all others come after it" |
| 19 | **Exception recurs with a period** | 10.2.1 s3 "every 15 seconds"; 10.2.3 s4/s9/s11 |
| 20 | **Exception stops** — none observed for at least T | 10.2.1 s6, 10.2.3 s13/s17/s21, 10.2.8 s12/s17 |
| 21 | **Exception format** (RTE-framed: address, `FF`, code, CRC) | 10.1.6 s3/s5 |
| 22 | **Either of two exception codes is acceptable** | all of 10.2 — "`3D` or `3E`" |
| 23 | **Optional response** — "the 3rd F1 may or may not be answered" | 10.2.3 s8; 10.1.3 s8 |
| 24 | **Meter increments by exactly N** | 10.1.5 s5 `0012`, `0038`, `0087` each +1 |
| 25 | **Meter increments by a captured amount, in a stated unit** | 10.1.5 s5: `0016`/`002C` by the cashout in *accounting credits*, `000E`/`0086` by the same cashout in *cents* |
| 26 | **Meter increments conditionally on another field** | 10.1.5 s5: `0004` increments **iff** LP A0 Feature 2 bit 2 = 1 |
| 27 | **No other meter changes** — a closed-world assertion over the whole meter set | 10.1.5 s5 "其他 meter 值不變"; s9 "no other meter increments"; s11 "no meter increments at all" |
| 28 | **Meter value agrees with the EGM's own displayed meter** | 10.1.5 s3 — **not automatable from the host side** |
| 29 | **Value persists across a power cycle** | 10.1.7 s4; 10.2.10 s16 |
| 30 | **Value survives a RAM clear** *(or must not)* | 10.1.7 s8 |
| 31 | **Response within time T / every poll answered over a window** | 10.2.2 s1/s2 at a 40 ms period |
| 32 | **Counter rollover behaviour** — increments to N then wraps to 1, skipping 0 | 10.2.7 s4; and the 4C sequence number `FFFFFF → 0` in 10.2.4 s6 |
| 33 | **Conditional criterion** — assert A if an earlier response had property P, else assert B | 10.2.9 s21: "if step 20's ACK flag was 01 then expiration = 254 days … otherwise identical to step 19" |
| 34 | **EGM state assertion** — playable / disabled / validation tilt / locked into handpay | 10.2.1 s3/s5/s6, 10.2.8 s4/s19/s20 — **needs an EGM-side agent to report** |
| 35 | **Printed-artefact assertion** — a ticket exists, with title/location/address/expiration/amount | 10.2.9 s4, 10.2.10 s5, 10.2.11 s5/s9/s13 — **not automatable from the host side** |
| 36 | **Absence of a printed artefact** | 10.2.12 s12/s15, 10.2.13 s7/s9 — **not automatable from the host side** |
| 37 | **Wire value agrees with printed value** | 10.2.7 s3 "the LP 4D ticket number matches the number on the ticket (printed in decimal, convert before comparing)" |

### 6.7 Manual and stimulus steps

The corpus needs three distinct kinds of non-wire step, and they must not be conflated:

**(a) EGM stimulus — delegable to an agent.** Insert cash/coins; insert a ticket; press
cashout; play N games / wager X; open and close a door; press the service button; reset a
handpay (attendant key); power cycle; RAM clear; change validation mode; change a credit /
cashout / jackpot limit; enable or disable receipt printing; assign a SAS channel. Because
the EGM here is Quixant's own platform, most of these are reachable from an EGM-side agent
over a side channel. A script must be able to say *what* it needs, not *how*.

**(b) Human action that no agent can do.** Recable the host to a different SAS channel
(10.1.4 s4/s6); physically retrieve a printed ticket and read the 16-digit validation number
from under the barcode (10.2.5 s3 and 11 more places) — and that value must then flow into a
later poll's field.

**(c) Human observation.** Read the printed ticket's title/location/address/expiration; read
the EGM screen (meter values, "promotional credits cannot be cashed out", validation tilt);
hear the chirp (10.2.3 s7).

Script requirements that follow:
- a `stimulus` step that names an action, its parameters and a target agent, with a fallback
  to a human prompt when no agent is attached;
- a `manual` step that blocks, shows a prompt and a checklist, and takes `PASS` / `FAIL` /
  `SKIP` / free-text plus **captured values** bound to script variables;
- both must record who/what satisfied them in the report, so a submission pack shows which
  steps a machine asserted and which a human attested;
- a **headless mode** where every manual step resolves to `BLOCKED` rather than hanging — so
  the wire-only subset (109 of 297 steps) can run in CI;
- **operator-paced waits** — "observe for 30 seconds", "wait > 15 s", "wait until the EGM
  chirps".

### 6.8 Cross-step state

Not optional: a third of the assertions are relative.
- Named capture of any decoded field into a script variable (`step6.validation_id`).
- Capture of a whole response for later byte-identical comparison (assertion 12).
- A **set** accumulated across steps for uniqueness (assertion 13).
- Meter snapshots — a full named meter set captured before and after, so "no other meter
  changed" (assertion 27) is checkable.
- Operator-supplied values (a barcode) as first-class variables.
- **State that survives a power cycle within the script** — 10.1.7 asserts across a reboot.

### 6.9 Control flow

| Construct | Cited from |
|---|---|
| Ordered numbered steps with a P/F each | every test |
| Explicit poll sequences | 10.1.3 s8 `F1,F2,F2,F2,F1,F2,F1`; 10.2.3 s7/s8 |
| **Repeat until a condition** | 10.1.3 s5 "until exception 00 or 1F"; 10.2.7 s4 "until index = 01"; 10.2.8 s10/s21 "until the response is all zeros" |
| **Bounded loop over a computed range** | 10.2.7 s8 "function code 2 … N where N is the buffer size discovered in step 4" |
| **Conditional skip of the whole test** | 10.1.4 s1 |
| **Conditional jump forward** | 10.2.6 s2 "jump to step 8" |
| Repeat an earlier step group | 10.1.7 s10 "repeat steps 5 and 6 once more" |

---

## 7. Three tests written as concrete scripts

Proposed syntax: YAML with a small embedded expression language. Rationale — the corpus is 90%
declarative data (poll fields, expected fields), the 10% that is imperative is simple control
flow, and a YAML document diffs and reviews well in the regulated-submission context this
lives in. `sasctl run 10.1.1.sast.yaml` executes it; the Avalonia GUI edits the same file.

### 7.1 `10.1.1.sast.yaml` — Standard Validation Configuration

```yaml
sasTest: 1
test:
  group:       Standard Validation
  number:      TEST_10.01.01
  requirement: "[1] 10.1.1"
  title:       Standard Validation Configuration
  estimate:    5-10min
  references:  { "1": "GSA Requirements Document", "2": "SAS Protocol Manual v6.02" }
  verifies:    [ "LP A0 reports Standard Validation" ]

egm:
  address: 1
  require:
    validationMode: standard          # asserted via A0, and requested from the stimulus agent
  note: "An EGM can only be configured for one ticket validation mode."

polls:
  F1:
    name: "A0-Send Enabled Features Game 0"
    cmd:  A0
    to:   1
    fields: { gameNumber: 0 }
    crc:  yes
    multiDenomPreamble: no

loops:
  1: Basic_1                          # from the shared loop library, see 7.4

trace:
  filter: DisplayOneKeyPoll
  saveAs: "10.1.1/step{step}.trc"

steps:
  - step: 1
    stimulus:
      agent: egm
      actions:
        - ramClear: { ifNeeded: true }
        - setValidationMode: standard
    # no pass criterion in the script; success is "the next step works"

  - step: 2
    do:
      - startLoop: 1
      - send: F1
    expect:
      - respondsTo: F1
      - conformsTo: { template: "A0/Send enabled features", ref: "[2] Table 7.14b" }
      - field: "Feature 1[6:5]"
        equals: 0b00
        ref: "[2] Table 7.14c"
        because: "Standard or none"
```

### 7.2 `10.1.3.sast.yaml` — Cashout Ticket 1

```yaml
sasTest: 1
test:
  group:       Standard Validation
  number:      TEST_10.01.03
  requirement: "[1] 10.1.3"
  title:       Cashout Ticket 1
  estimate:    30-45min
  verifies:
    - "LP 3D: send cashout ticket information"
    - "Exception 3D: a cashout ticket has been printed"
    - "Handpay and jackpot receipts"

egm:
  address: 1
  require: { validationMode: standard, rte: off }
  optional:                            # drives the SKIP of steps 15/16
    cashoutReceipt: probe
    jackpotReceipt: probe

polls:
  F1: { name: "00-General poll",  cmd: 00, to: 0 }
  F2: { name: "00-General poll",  cmd: 00, to: 1 }
  F3: { name: "3D-Send cashout ticket information", cmd: 3D, to: 1, crc: yes }

loops:
  1: Basic_No_General
  2: Basic_1

trace:
  filter: DisplayOneKeyPoll
  saveAs: "10.1.3/step{step}.trc"

steps:
  - step: 1
    do: [ { startLoop: 1 }, { send: F1 } ]
    expect: [ { commsSynchronised: true } ]

  - step: 2
    stimulus: { agent: egm, actions: [ { door: open }, { door: close } ] }

  - step: 3
    stimulus: { agent: egm, actions: [ { insertCredits: 1000 }, { pressCashout: true } ] }
    manual:
      prompt: "Confirm the EGM printed a ticket."
      checklist: [ "A ticket was printed" ]

  - step: 4
    stimulus: { agent: egm, actions: [ { pressService: true }, { clearService: true } ] }

  - step: 5
    do:
      - repeat:
          send: F2
          until: "exception in [0x00, 0x1F]"
          maxIterations: 200
    expect:
      - exceptionSequence:
          window: sinceStep(4)
          first:  0x3D                       # must be in the FIRST general-poll response
          thenInOrder: [ 0x11, 0x12, 0x71, 0x72 ]
          allowInterleaved: false

  - step: 6
    do: [ { send: F3 } ]
    expect:
      - conformsTo: { template: "3D/Send cash out ticket information" }
      - field: address            equals: 0x01
      - field: command            equals: 0x3D
      - field: "Validation number" is: bcd(4)
      - field: "Ticket amount"     is: bcd(5), unit: cents
      - field: crc                 valid: true, byteOrder: lsbFirst
    capture:
      step6.response: $response          # for assertion 12 in step 14

  - step: 7
    stimulus: { agent: egm, actions: [ { insertCredits: 1000 }, { pressCashout: true } ] }
    manual: { prompt: "Confirm a ticket printed.", checklist: [ "Ticket printed" ] }

  - step: 8
    do:
      - stopLoop: 1
      - sendSequence: [ F1, F2, F2, F2, F1, F2, F1 ]
    expect:
      - onSend: { index: 2, poll: F2 }  exception: 0x3D          # 1st F2
      - onSend: { index: 3, poll: F2 }  exception: 0x3D          # 2nd F2
      - onSend: { index: 4, poll: F2 }  exception: { anyOf: [0x3D, none] }   # 3rd F2, optional
      - onSend: { index: 6, poll: F2 }  exception: 0x3D          # last F2

  - step: 9
    do: [ { startLoop: 2 } ]

  - step: 10
    stimulus: { agent: egm, actions: [ { insertCredits: 1000 }, { pressCashout: true } ] }
    expect:  [ { exception: 0x3D, within: 20polls } ]
    manual:  { prompt: "Confirm the printed ticket amount is correct.",
               capture: { step10.printedAmount: money } }

  - step: 11
    stimulus: { agent: egm, actions: [ { insertCredits: 500 }, { pressCashout: true } ] }
    expect:  [ { exception: 0x3D, within: 20polls } ]
    manual:  { prompt: "Confirm the printed ticket amount is correct.",
               capture: { step11.printedAmount: money } }

  - step: 12
    do: [ { send: F3 } ]
    expect:
      - field: "Validation number" nonZero: true
      - field: "Ticket amount"     equals: $step11.printedAmount
    capture: { step12.validationNumber: field("Validation number") }

  - step: 13
    when: "exceptionsPending(0x3D) > 0"
    do: [ { send: F3 } ]
    expect:
      - anyOf:
          - allOf: [ { field: "Validation number" equals: 0 },
                     { field: "Ticket amount"     equals: 0 } ]
          - allOf: [ { field: "Validation number" equals: $step12.validationNumber },
                     { field: "Ticket amount"     equals: $step11.printedAmount } ]

  - step: 14
    do: [ { send: F3 } ]
    expect: [ { conformsTo: { template: "3D/Send cash out ticket information" } } ]

  - step: 15
    skipUnless: "egm.optional.cashoutReceipt"
    stimulus:
      agent: egm
      actions: [ { setCashoutReceipt: on }, { insertCredits: 1000 }, { pressCashout: true } ]
    do: [ { send: F3 } ]
    expect: [ { exception: 0x3D, within: 20polls }, { respondsTo: F3 } ]
    manual:  { prompt: "Confirm a cashout RECEIPT was printed.",
               checklist: [ "Cashout receipt printed" ] }

  - step: 16
    skipUnless: "egm.optional.jackpotReceipt"
    stimulus:
      agent: egm
      actions: [ { setJackpotReceipt: on }, { insertCredits: 2000 },
                 { playUntil: jackpotWin, timeout: 15min } ]
    do: [ { send: F3 } ]
    expect: [ { exception: 0x3D, within: 20polls }, { respondsTo: F3 } ]
    manual:  { prompt: "Confirm a jackpot RECEIPT was printed.",
               checklist: [ "Jackpot receipt printed" ] }
```

### 7.3 `10.1.7.sast.yaml` — Cashout Ticket 2 (power cycle, unique IDs)

```yaml
sasTest: 1
test:
  group:       Standard Validation
  number:      TEST_10.01.07
  requirement: "[1] 10.1.7"
  title:       Cashout Ticket 2
  estimate:    20-30min
  verifies:
    - "Ticket information is maintained during power off"
    - "Validation numbers are unique across power off and RAM clear"

egm:
  address: 1
  require: { validationMode: standard, rte: off }

polls:
  F1: { name: "3D-Send cashout ticket information", cmd: 3D, to: 1, crc: yes }

loops:
  1: Basic_1

trace:
  filter: DisplayOneKeyPoll
  saveAs: "10.1.7/step{step}.trc"

# A set that accumulates across the whole run, including across the power cycle
# and the RAM clear. This is the machinery for assertion 13.
sets:
  seenValidationIds: []

steps:
  - step: 1
    do: [ { startLoop: 1 } ]

  - step: 2
    stimulus: { agent: egm, actions: [ { insertCredits: 1000 }, { pressCashout: true } ] }
    expect:  [ { exception: 0x3D, within: 20polls } ]
    manual:  { prompt: "Confirm the ticket printed with correct data.",
               capture: { step2.printedAmount: money } }

  - step: 3
    stimulus: { agent: egm, actions: [ { powerCycle: true } ] }
    do:       [ { waitForComms: { timeout: 120s } } ]

  - step: 4
    do: [ { send: F1 } ]
    expect:
      - field: "Validation number" equals: $step2.validationNumber   # persistence
      - field: "Ticket amount"     equals: $step2.printedAmount
    capture:
      step2.validationNumber: field("Validation number")   # bound here on first read
      $sets.seenValidationIds: += field("Validation number")

  - step: 5
    stimulus: { agent: egm, actions: [ { insertCredits: 1500 }, { pressCashout: true } ] }
    expect:
      - exception: 0x3D, within: 20polls
      - onPoll: F1
        field: "Validation number"
        nonZero:  true
        notEquals: $step2.validationNumber
    capture:
      step5.validationNumber: field("Validation number")
      $sets.seenValidationIds: += field("Validation number")
    manual: { prompt: "Confirm the ticket printed with correct data.",
              capture: { step5.printedAmount: money } }

  - step: 6
    do: [ { send: F1 } ]
    expect:
      - field: "Validation number" equals: $step5.validationNumber
      - field: "Ticket amount"     equals: $step5.printedAmount

  - step: 7
    stimulus:
      agent: egm
      actions: [ { ramClear: true }, { setValidationMode: standard } ]
    do: [ { waitForComms: { timeout: 120s } } ]

  - step: 8
    stimulus: { agent: egm, actions: [ { insertCredits: 1000 }, { pressCashout: true } ] }
    expect:
      - exception: 0x3D, within: 20polls
      - onPoll: F1
        field: "Validation number"
        nonZero: true
        notIn:   $sets.seenValidationIds        # survives the RAM clear
    capture:
      step8.validationNumber: field("Validation number")
      $sets.seenValidationIds: += field("Validation number")
    manual: { prompt: "Confirm the ticket printed with correct data.",
              capture: { step8.printedAmount: money } }

  - step: 9
    do: [ { send: F1 } ]
    expect:
      - field: "Validation number" nonZero: true
      - field: "Ticket amount"     equals: $step8.printedAmount

  - step: 10
    repeatSteps: [5, 6]
    times: 1
    additionalExpect:
      - field: "Validation number" notIn: $sets.seenValidationIds
```

### 7.4 The shared loop library these scripts reference

```yaml
# loops.sast.yaml — transcribed verbatim from One Key Polls.doc and confirmed
# byte-for-byte against 10.2/10.2.2/step1.trc
Basic_1:
  - { cmd: 80, to: 0,   desc: "RESYNC (General Poll to address 0)" }
  - { cmd: 1F, to: 1,   desc: "Send gaming machine ID and information" }
  - { cmd: 1F, to: 38,  desc: "…to an address no EGM owns — must NOT answer" }
  - { cmd: 51, to: 1,   desc: "Send total number of games implemented" }
  - { cmd: 81, to: 1,   desc: "General Poll" }
  - { cmd: 54, to: 1,   desc: "Send SAS version ID and EGM serial number" }
  - { cmd: 54, to: 127, desc: "…to an address no EGM owns — must NOT answer" }
  - { cmd: 7E, to: 1,   desc: "Send current date and time" }
  - { cmd: 81, to: 1,   desc: "General Poll" }

Basic_No_General:
  - { cmd: 1F, to: 1 }
  - { cmd: 1F, to: 38 }
  - { cmd: 51, to: 1 }
  - { cmd: 54, to: 1 }
  - { cmd: 54, to: 127 }
  - { cmd: 7E, to: 1 }

timing:
  pollPeriodMs: 40                 # 10.2.2; measured 39.4-40.2 ms in the captured traces
  responseTimeoutMs: 20
  foregroundInjection: nextPollSlot    # SASDS-6: NOT immediate

background:
  progressiveBroadcast:
    enabled: false                 # per-test; the lab enables it with 6 levels at 1000 ms
    cmd: 86
    to:  0
    periodMs: 1000

autoResponses: []                  # e.g. { onException: 0x3D, send: LP3D }  — 10.1.6 only
```

### 7.5 What these scripts still cannot express, and why

**In 10.1.1**

| Cannot express | Why |
|---|---|
| That the EGM was *actually* RAM-cleared and set to standard validation in step 1 | The only evidence is the A0 response in step 2. If the stimulus agent silently fails, step 2 fails and the report blames the wrong thing. A `stimulus` step has no independent verification channel — it needs a post-condition probe. |
| `[2] Table 7.14b` as a normative reference | The script points at the toolkit's `A0/Send enabled features` template, which is *someone's transcription* of the table. If GSA and Acres disagree, the script silently follows Acres. There is no machine-readable form of the SAS manual. |

**In 10.1.3**

| Cannot express | Why |
|---|---|
| Step 3/7/10/11/15/16 "the EGM printed a ticket **for the correct amount**" | The amount is on paper. The wire only proves the EGM *claims* an amount in LP 3D. Verifying the printer agrees requires a camera, a printer tap, or an EGM-side agent that can report what it sent to the printer. The script degrades this to a human checklist item, which is what the paper form already does — no automation gained. |
| Step 5's ordering assertion, robustly | The criterion says `3D` in the **first** general-poll response, then `11, 12, 71, 72`. `Basic_1` emits general polls continuously, and `10.1/10.1.3/step5.trc` shows the real EGM interleaving `6A`, `53` and `74` among them. "In order" therefore has to mean "in relative order, ignoring unlisted codes" — but the script cannot know whether an unlisted code is benign noise or a defect. `allowInterleaved: false` above is a lie; the honest encoding needs a policy for unexpected exceptions that the source document never states. |
| Step 13's "if more 3D exceptions were reported" | `exceptionsPending(0x3D)` is a host-side guess. The host cannot see the EGM's queue depth; it can only observe what has arrived. If the EGM has queued a `3D` it has not yet delivered, the guard is wrong. |
| Step 15/16 `skipUnless: egm.optional.cashoutReceipt` | Nothing in SAS reports whether the EGM supports cashout or jackpot receipts. It has to be declared by hand in a machine profile, which makes the "skip" a configuration claim, not a measurement. |
| Step 16 `playUntil: jackpotWin` | A jackpot is stochastic. The step depends on limits set in a *different* test (10.1.5 steps 10/12) and on chance. A 15-minute timeout is a guess; the script cannot state the real precondition, which is "the operator has rigged the machine so this is likely". |

**In 10.1.7**

| Cannot express | Why |
|---|---|
| Step 4's capture-then-compare ordering | The criterion is "the ID matches the ticket printed in step 2", but step 2's ID was never read over the wire — no LP 3D was sent before the power cycle. So `step2.validationNumber` is *defined* by step 4's own response, and the assertion is vacuous unless the operator read the number off the paper ticket in step 2. The script above papers over this; the honest version needs a `manual.capture` of the barcode in step 2, which turns a wire-only step into a human step. **The source document has the same hole.** |
| "unique across power off and RAM clear" as a *property* rather than a sample | Ten cashouts prove ten IDs differ. They do not prove the generator is collision-free. The real requirement is a property of the algorithm; the test is a spot check. SasWorkbench can do better than the paper test here — exhaustively exercising the ID generator in `libsas` as a unit test — but the *conformance script* cannot say that. |
| Step 10 `repeatSteps: [5,6] times: 1` with `additionalExpect` | Splicing an extra assertion into a re-run of earlier steps is a hack. The clean form is a parameterised sub-procedure, which means the script format needs procedures — and then the step numbering that the P/F report and the regulator's submission depend on stops being a flat 1..N list. Flat numbering and reuse are in direct tension, and the paper corpus assumes flat numbering everywhere. |
| Any assertion spanning the power cycle about *host* state | `waitForComms` hides a lot: the host's own poll loop, the RESYNC behaviour, the retry policy. `ReleaseNotes.txt` `03.04` shows "Send RESYNC on startup" and "Send RESYNC on comm error" are per-test *settings*, so they belong in the script — but the corpus never says which value each test needs, only that 03.04 changed. UNKNOWN for 10.1.7. |

**Cross-cutting, applying to all three**

| Cannot express | Why |
|---|---|
| The 40 ms real-time guarantee as an assertion the script owns | 10.2.2 asserts "the EGM answers every poll" at 40 ms. But if *SasWorkbench* is late, the EGM appears to fail. The script needs to assert on the host's own jitter first and abort the test if the host missed its budget — a self-check the source corpus never contemplates because `gsat.exe` ran on bare DOS. |
| Which exceptions are "expected noise" for a given EGM | Every real trace is full of `6A`, `53`, `74`, `2B` that no criterion mentions. Without a per-machine allow-list, strict assertions produce false failures and lenient ones produce false passes. The corpus assumes a human filters this by eye. |
| The relationship between a step's P/F and the *requirement* it proves | The header lists "verifies: LP 3D, exception 3D, receipts" for the whole test but never maps individual steps to individual requirements. A regulator asking "which step proves exception 3D?" gets no answer from the source document, so the script cannot carry a faithful mapping — only one SasWorkbench invents. |

---

## 8. Recommendations for SasWorkbench, ranked

1. **Import `PollTemplates.xml`, `ResponseTemplates.xml`, `EventTemplates.xml` and
   `SASTemplates.dtd` as the message-schema layer.** 255 templates covering polls and
   responses, with named fields, BCD/hex/ASCII/bitmap/enum typing, optional groups and tagged
   unions. It is the single highest-value artefact in the toolkit and it is already
   machine-readable. Ship a converter to whatever SasWorkbench's native schema is, keep the
   `name`/`cmd` identity, and use the field names verbatim so scripts and traces read the same
   as the corpus.
2. **Adopt the `.trc` text format for evidence output, byte-exactly** (§5.2). It costs almost
   nothing, and it makes SasWorkbench output interchangeable with the lab's existing archive
   and with any regulator already reading GSA traces.
3. **Adopt the four-concept model — named poll, named loop, named filter, named meter set — but
   put the definitions in version-controlled text, not in `.dat` blobs.** `PollDef.txt` and
   `One Key Polls.doc` already show what the text form looks like; the binary files are only a
   serialisation the GUI happens to use, and the toolkit's own history (SASDS-24, SASDS-28)
   is a catalogue of pain caused by treating them as the source of truth.
4. **Build the loop scheduler as a single serialised poll queue with three producers** —
   the running loop, the foreground one-key lane (injected at the next slot, per SASDS-6), and
   timed background emitters (progressive broadcast) — plus event-driven auto-response rules.
   This is the concurrency model the corpus assumes on every page.
5. **Implement the secure-enhanced validation algorithm and check it against the eight golden
   vectors in 10.2.3 and 10.2.4** before writing any host code. Those vectors are a free,
   authoritative unit-test suite, and they can be run with no EGM present.
6. **Split every "printed ticket" criterion into a wire half and a paper half.** Automate the
   wire half; emit the paper half as a one-line operator checklist attached to the step. This
   is where most of the 130 judgement steps become cheap.
7. **Define the EGM stimulus agent interface early.** The action vocabulary is small and
   closed: insert credits, insert ticket, cashout, play, door, service, handpay reset, power
   cycle, RAM clear, set validation mode, set limit, set receipt flag. Twelve verbs turn 58
   stimulus steps and a large share of the judgement steps into automation. Since the EGM is
   Quixant's own, this is a build decision, not a research problem.
8. **Do not promise unattended conformance.** One test out of 22 is fully wire-only. Promise
   *attended but fast*: the machine drives the wire, asserts everything observable, and asks
   the human only for money, buttons and paper.

---

## 9. Open questions / UNKNOWN

- The test-script documents for chapters 1–9, 10.2.16–10.2.30, 10.3, 11–29 are **not on disk**.
  Only the saved HostSim configurations for them exist. 405 of 427 tests are unspecified here.
- Which of chapters 24–29 covers which of the six addendum subjects (miscellaneous new
  functionality, meter change notification, player denomination reporting, session support,
  tip money meter, keyed credits) — the release notes list the subjects but not the mapping.
- Whether chapters 1 and 2 exist as tests at all. No `TEST_01.*` or `TEST_02.*` record exists
  in any `TestData` file.
- Whether the printed document has a chapter 17. `Bill_Hopper.dat` exists and is empty; the
  v1.1 notes say bill hoppers were "not yet defined".
- The exact binary layout of a `TestData\*.dat` record beyond the test number, port name and
  meter-set name. ~0x180 bytes of the ~402-byte payload were not decoded; the one-key-poll
  slot bindings and loop indices are in there and would let SasWorkbench import all 427 test
  configurations automatically. This is a worthwhile follow-on.
- The GEP (GUI–Engine Protocol) between `SASHostSim.exe` and `gsat.exe`. Not examined;
  SASDS-27 records that GSA never documented it either.
- What "[2] Table 7.14b / 7.14c" say normatively. Inferred from `ResponseTemplates.xml` and
  from a real A0 response in `10.1/10.1.1/step2.trc`; the SAS manual itself was not consulted.
- Whether the LP 3D "Send RESYNC on startup / on comm error" settings differ per test beyond
  `03.04`. The release notes change only that one.
- The `.trc` timestamp semantics at burst boundaries: some first-events after a
  `----------` carry very large values (e.g. one event in `10.2/10.2.1/step13.trc`), which the
  header line ("relative to end of previous poll") does not explain. Possibly a counter
  artefact across a pause. Not resolved.
