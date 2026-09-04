# 01 — Binary Reverse-Engineering of `C:\IGT3\sastest.exe`

**Purpose:** complete, evidence-grounded inventory of the legacy IGT SAS host simulator's
user-facing features and UI structure, to serve as the parity checklist for the replacement app.

**Scope note:** `C:\Progetti\libsas\utils\sashost`, `utils/poller` and `utils/hwconformance` were
NOT read, cited, or used as design input. The only benchmark here is `sastest.exe`.
`C:\Progetti\libsas\src` / `include` were consulted only to identify what the library under test exposes.

---

## 0. Method & artefacts

| Artefact | Path | How produced |
|---|---|---|
| Raw resource dump | `.../research/exe-resources.txt` | `python peparse.py` (hand-written PE parser, no pefile) |
| Decoded Delphi forms | `.../research/exe-forms.txt` | `python dfm.py` (hand-written binary-DFM/TPF0 decoder) |
| Annotated UI tree | `.../research/exe-ui-tree.txt` | `python tree.py exe-forms.txt` |
| Filtered strings | `.../research/exe-strings.txt` | Sysinternals `strings -nobanner -a -n 4` + `-u -n 4` |
| Import list | `.../research/exe-imports.txt` | extracted from `exe-resources.txt` |

`pefile` is not installed (`ModuleNotFoundError: No module named 'pefile'`), so the PE, resource
directory, DFM and import parsers were written from scratch against the standard library.
All four DFM forms decoded with **zero parse errors** (`grep -c 'PARSE ERROR' exe-forms.txt` → `0`).

---

## 1. Binary identity

```
File: C:\IGT3\sastest.exe  size=985088
Machine=0x014c  NumberOfSections=8  TimeDateStamp=0x43a35394
  Link date (UTC): 2005-12-16T23:53:56
OptionalHeader magic=0x010b (PE32)   LinkerVersion=5.0
Subsystem=2 (GUI)  ImageBase=0x400000  OSVer=4.0
Sections: .text .data .tls .rdata .idata .edata .rsrc .reloc
```
*(`exe-resources.txt` lines 1–24)*

`RT_VERSION`:

| Field | Value |
|---|---|
| CompanyName | `IGT` |
| FileDescription | `SAS Simulator` |
| ProductName | `SAS Simulator` |
| FileVersion | `2.0.2.1` |
| ProductVersion (string) | `1.0.0.0` (fixed-block says 2.0.2.1 — inconsistent in the original) |
| LegalCopyright | `Copyright IGT 2002` |
| OriginalFilename | `SasTest.exe` |

**Toolchain: Borland C++Builder.** Evidence: string `Borland C++ - Copyright 2002 Borland Corporation`
(`raw-ascii.txt:6708`), the `DVCLAL` RCDATA licence blob, Borland button-glyph bitmaps
(`BBOK`, `BBCANCEL`, `BBABORT`, `BBRETRY`, `BBIGNORE`, `BBYES`, `BBNO`, `BBHELP`, `BBCLOSE`, `BBALL`),
and VCL RTTI class names (`TIGTComm`, `TForm1`).

**Consequence for RE:** the UI is *not* in `RT_MENU`/`RT_DIALOG`. Those are empty:

```
=== RT_MENU (decoded menu trees) ===      (nothing)
=== RT_ACCELERATOR ===                    (nothing)
=== RT_DIALOG ===  1 entry, "DLGTEMPLATE" — the generic VCL 1-control stub, no captions
```

The entire UI lives in four binary Delphi `TPF0` form streams in `RT_RCDATA`:

| RCDATA name | Bytes | Decoded to |
|---|---:|---|
| `TFORM1` | 36 720 | Main window `Form1: TForm1`, caption **“SAS Test Program”** |
| `TINPUTDIALOG` | 6 932 | Generic parameter-entry dialog |
| `TPOPUP` | 1 939 | Hex keypad popup |
| `TSASABOUTBOX` | 71 001 | About / EULA gate, caption **“IGT SAS Simulator”** |
| `DVCLAL` | 16 | Borland licence marker (not a form) |

`RT_STRING` contains only VCL/RTL boilerplate (weekday and month names, IDs 65296+) — no app strings.

---

## 2. Decoded menu tree

There is no `RT_MENU` resource; the menu is the `MainMenu1: TMainMenu` object inside `TFORM1`.
Decoded from `exe-ui-tree.txt` (lines 500–535):

```
File
├── Reload INI file            → ReloadINIfile1Click
├── Save Data                  → SaveData1Click        [Enabled = False at design time]
├── Save Default Data          → SaveDefaultData1Click
├── Restore Default Data       → RestoreDFClick
├── Save Report File           → SaveReportFile1Click
└── Exit                       → EXIT
Log
└── Capture Data               → CaptureData1Click     [Hint: "Saves Data in TXRXData.Dat"]
AFT
├── Save AFT Defaults          → SaveAFTDefaults1Click
└── Restore AFT defaults       → RestoreAFTdefaults1Click
Help
├── About                      → About1Click
├── Help                       → Help2Click            (opens SAStest.hlp — WinHelp)
└── Active Help                → ActiveHelp1Click
```

Plus one popup menu, on the `TINPUTDIALOG` edit box:

```
PopupMenu1 (TPopupMenu)
└── Keypad                     → Keypad1Click          (shows the TPOPUP hex keypad)
```

**No keyboard accelerators exist anywhere.** `RT_ACCELERATOR` is empty and
`grep -c 'ShortCut' exe-forms.txt` → `0`. The app is 100 % mouse-driven.

---

## 3. Dialogs / forms — complete control inventory

### 3.1 `Form1: TForm1` — main window

| Property | Value |
|---|---|
| Caption | `SAS Test Program` |
| Size | 808 × 646, min 640 × 480, `poScreenCenter` |
| Font | MS Sans Serif |
| Events | `OnCreate=FormCreate`, `OnClose=FormClose` |
| `ShowHint` | True (tooltips used throughout) |

**Top-level (always-visible) controls**

| Name | Class | Caption / Text | Notes |
|---|---|---|---|
| `ComboBox1` | TComboBox | `Long Poll Commands` | primary command picker, filled from INI |
| `ComboBox2` | TComboBox | `Electronic Funds Transfer` | EFT command picker, filled from INI |
| `Edit1` | TEdit | `Custom Command` | free-form hex entry; `OnKeyPress` |
| `CRCBox` | TCheckBox | `CRC` | append CRC to custom command |
| `SendCC` | TButton | `Send Custom Command` | `Enabled=False` until valid |
| `ResendStructButton` | TButton | `Resend Last Message` | |
| `ReSend` | TButton | `<- Resend` | `Enabled=False` |
| `Resend2` | TButton | `<-Resend` | `Enabled=False` |
| `Button2` | TButton | `Clear Msg. Disp` | clears English pane |
| `Button3` | TButton | `Clear Tx/Rx Disp` | clears hex pane |
| `ClrDisps` | TButton | `Clear Both Displays` | |
| `Button6` | TButton | `Stop Display` | toggles to `Start Display` |
| `Panel1` | TPanel | — | contains the two log panes |
| ├ `Memo2` | TMemo | — | English-decoded message pane |
| ├ `Splitter1` | TSplitter | — | user-resizable split |
| └ `Memo1` | TMemo | — | raw TX/RX hex pane |
| `Timer1` | TTimer | — | `Interval=50`, `Enabled=False` |
| `ProgRateTimer` | TTimer | — | progressive broadcast cadence |
| `SaveDialog1` | TSaveDialog | — | `*.dat` — `Dat File (*.dat)\|*.dat\|Any File (*.*)\|*.*` |
| `SaveDefaults` | TSaveDialog | `Save SAS Defaults` | `*.set` |
| `RestoreDefaults` | TOpenDialog | `Restore Defaults` | `*.set` |

**Secondary notebook `PageControl2`** (command-definition editor):

| Tab | Contents |
|---|---|
| `Command Sets` | `ComboBox5` “Command Sets” |
| `Command Set Structure Editor` | `ComboBox6` “Structures” — runtime editing of INI `[Data Structures]` |

### 3.2 Main notebook `PageControl1` — 14 tabs

| # | Tab caption | Object |
|---:|---|---|
| 1 | General Poll/Address | `TabSheet2` |
| 2 | Quick Cmd | `TabSheet3` |
| 3 | Misc | `ScriptTab` |
| 4 | Multi-Denom | `TabSheet7` |
| 5 | Validation | `TabSheet8` |
| 6 | Redemption | `TabSheet9` |
| 7 | Scripting | `TabSheet4` (nested: Script, Meter Test) |
| 8 | Progressive | `Progressive` |
| 9 | EFT Test | `TabSheet1` — **`TabVisible = False`** (hidden) |
| 10 | AFT | `TabSheet10` (nested: Registration/Lock, Transfer Funds, Receipt) |
| 11 | Pulltab | `TabSheet13` — **`TabVisible = False`** (hidden) |
| 12 | Report | `TabSheet14` |
| 13 | Authentication | `TabSheet18` |

*(That is 13 sheets; “EFT Test” and “Pulltab” ship hidden, so the shipped UI shows 11.)*

#### Tab 1 — General Poll/Address (`TabSheet2`)

| Control | Class | Caption / config |
|---|---|---|
| `TrackBar1` | TTrackBar | Min 20, Max 2100, Pos 2100. Hint *“Move to Change General poll rate”* |
| `Label3/4/5` | TLabel | scale legend `40ms` / `Off` / `2 seconds` |
| `GPlab` | TLabel | live status `Auto GP Off` |
| `ms40` | TButton | `40 ms GPs` |
| `Button10` | TButton | `80 ms GPs` |
| `GP100` | TButton | `150ms GP's` |
| `Button9` | TButton | `200ms GPs` |
| `Button11` | TButton | `GPs OFF` |
| `UpDown1` | TUpDown | Min 40, Max 2000 — fine poll-rate spinner |
| `SASAddress` | TRadioGroup | `SAS Address`, items `1`…`64` (**note: item 59 is the string `58` — duplicate, a real defect**) |
| `CycleAddress` | TCheckBox | `Cycle Address` |
| `UpDown3` | TUpDown | Min 1 Max 64. Hint *“Sets the Max Address for Cycling”* |
| `Label17` | TLabel | `Poll up to ` |
| `ShowPhantoms` | TCheckBox | `Show Syncs / GPs` |
| `NoPhantom` | TCheckBox | `No Sync (80)` — **Checked** |
| `NoSync` | TCheckBox | `No Sync (with time slot)` |
| `WakeUpCheckBox` | TCheckBox | `Wake Up Bits` — **Checked**. Hint *“Unchecking this box disables wakeup bits”* |
| `GroupBox13` | TGroupBox | `Comm Info` → `Time Outs=` *n*, `Bytes Read =` *n* |

#### Tab 2 — Quick Cmd (`TabSheet3`)

Single `ComboBox3: TListBox`, `Style = lbOwnerDrawVariable`. One click sends a
verbatim byte string from INI `[Quick Commands]`.

#### Tab 3 — Misc (`ScriptTab`)

| Control | Class | Caption / Hint |
|---|---|---|
| `GroupBox1` | TGroupBox | `Last Command` → `EditResend` (TEdit), `RepeatButton` = `Send Command`, `AddCRC` = `Add CRC` |
| `GPButton` | TButton | `Send GP` |
| `SendPhantom` | TButton | `Re-Sync` |
| `Button12` | TButton | `Send Multiple Wakeup Bits` |
| `MWB0111` | TButton | `Test MWB0111` |
| `Label25` | TLabel | `WU Delay Milliseconds ` |
| `ComboBox7` | TComboBox | items `1`…`10` (wake-up delay) |
| `ComboBox4` | TComboBox | `$A5 ASDC Commands` |
| `Disp1F` | TCheckBox | `Display Idle Activity` — Hint *“Display the $00(no activiy) or the $!F(waiting for user input)”* |
| `CRCErrors` | TCheckBox | `Report CRC Errors` |
| `CRCError` | TCheckBox | `Force CRC Error` |
| `Respond51` | TCheckBox | `Auto respond to req. for handpay info` — **Checked**. Hint *“Respond with $1B … to a $51 or $66”* |
| `Trans0` | TCheckBox | `"0" the Transaction field` |
| `EFTRespond` | TCheckBox | `Respond Selected EFT` |
| `ForceEFTACK` | TCheckBox | `Force EFT ACK` |
| `NoEFTACK` | TCheckBox | `No EFT ACK` |
| `TimingCB` | TCheckBox | `Monitor Timing` |
| `GroupBox10` | TGroupBox | `Priority` → `Priority Normal`, `Priority High` (process/thread priority) |
| `RTERGB` | TGroupBox | `Real Time Event Rep` → `Set `, `Clear` |
| `GroupBox11` | TGroupBox | `DTR for Fiber Optic` → `Set` (Hint *“Sets the DTR and RTS lines high”*), `Clear` |
| `GroupBox12` | TGroupBox | `Comm Loopback` → `Echoing Enabled`, `Use Echo Index` (Hint *“Check this box if first byte of recieved messages is cut off”*) |
| `Button1` / `test` | TButton | `Test Button` / `test` — **developer leftovers shipped in the UI** |

#### Tab 4 — Multi-Denom (`TabSheet7`)

`Preamble` (`Add preamble to long polls`), `SASDenomComboBox`, `CheckListBox1` (per-denomination selection).

#### Tab 5 — Validation (`TabSheet8`)

| Control | Caption / value |
|---|---|
| `RadioGroup1` | `Validation Type` — items: `No Automatic processing`, `Standard`, `Secure Enhanced`, `System` |
| `GroupBox2` | `System Validation` |
| ├ `SysValidationEdit` | `0123456789098765`, MaxLength 16 — *System Validation Number* |
| ├ `Edit2` | `01`, MaxLength 3 — *Validation System ID* |
| ├ `UseValidation` | `Change to this Number` |
| ├ `AutoIncVal` | `Auto Increment Validation Num.` |
| └ `RandSysValButton` | `Randomize SVN` |
| `GroupBox4` | `Secure Enhanced Validation` |
| ├ `EnhMachineIDEdit` | `000001`, MaxLength 6 — *Machine ID* |
| ├ `EnhSequenceEdit` | `000000`, MaxLength 6 — *Sequence #* |
| ├ `InitEnhVal` | `Initialize Enhanced Validation` |
| └ `AutoInitCB` | `Auto Initialize Validation` |
| `BadValNum` | `Bad Validation Num` — **negative test** |
| `BadTransCode` | `Bad Transfer Code` — **negative test** |
| `InsertGP` | `InsertGP` |

#### Tab 6 — Redemption (`TabSheet9`) — TITO

| Control | Caption / value |
|---|---|
| `GroupBox3` | `Redemption` |
| ├ `TransferCodesCB` | `Transfer Codes` (from INI `[Ticket Transfer Codes]`) |
| ├ `TxfrAmt` | `2500`, MaxLength 10 — *Ticket Amount* |
| ├ `UseTrans` | `Use this amount` |
| └ `InsertHex` | `Insert hex value` |
| `EnhValidationEdit` | `000000000000000000`, MaxLength 18 — *Validation Data* |
| `EnhValNumCheckBox` | `Use this #` |
| `AutoValReplyBox` | `Auto Redemption` — **Checked** |
| `Send70` | `Send 70 - Interrogate` |
| `SetValidationFlags` | `Send 71 - Redeem Tkt` |
| `GetStatus` | `Send 71 - Get Status` |
| `GroupBox5` | `Restricted Redemption` → `RestrictedExpirationEdit` `00000000`, `PoolIDEdit` `0000`, `UseAFT` = `Use Restricted Redemption` |

#### Tab 7 — Scripting (`TabSheet4`) → nested `PageControl4`

**Sub-tab `Script`:** `Scripting` checkbox, `ListBox1`, `ListBox2` (script steps + log).
Per `SASINI.doc`: *“Scripting short circuits all automatic polling so what is in the script is the only thing sent.”*

**Sub-tab `Meter Test`:** `GroupBox6` = `Individual Meter Test`

| Control | Caption / items |
|---|---|
| `MonitoredMeterCmd` | `Meter` — hex items `0`…`FF` |
| `MonitoredMeterPos` | `Position` — `0`…`40` |
| `MonitoredMeterBytes` | `How Many Bytes` — `0`…`20` |
| `MonitorMeter` | `Monitor Meter` |
| `Label29/30` | `Times Meter Changed = ` / live counter |
| `Label31` | *“This feature only works with scripting”* |
| `MeterTest` | button `Meter Test` |

#### Tab 8 — Progressive (`Progressive`)

| Control | Caption |
|---|---|
| `Do80` | `Use Single Level Progressive Broadcast` — **Checked** (long poll `$80`) |
| `Do86` | `Use Multiple Level Progressive Broadcast` (long poll `$86`) |
| `ProgRepeat` | `Enable Progressive Broadcasts` |
| `IncProgCB` | `Enable Progressive Incrementing` |
| `DispBroad` | `Display Broadcasts` |
| `Button4` | `Broadcast` (one-shot) |
| `UpDown2` | group, Min 0 Max 255 → `Label36` `Group= 1` |
| `UpDown6` | broadcast interval, Min 1 Max 10 → `Label1` `3 second(s)` |
| `Label14` / `In` | `Levels` / `Increment amt.` |

#### Tab 9 — EFT Test (`TabSheet1`, hidden)

`EFTButton`, `EFTEdit`, `ComboBox8`, `ComboBox9` (`Denoms`: `.01 .05 .10 .25 .50 1.00 2.00 5.00 10.00 20.00 50.00 100.00 500.00 1000.00 10000.00`),
`ACKCB`, `SetACK`, `Button7` `Inc Trans.`, `DecTrans` `Dec Trans.`, `EFTStat` label,
`BreakEFT` = `Break TXRX line on final ACK from game` (Hint *“Breaks line after first ack”*),
`RestoreRXLineButton` = `Restore TXRX Line`, `Garble` = `Garble`. **Line-fault injection.**

#### Tab 10 — AFT (`TabSheet10`) → nested `PageControl3`

**Sub-tab `Registration / Lock`**

| Control | Caption / items |
|---|---|
| `GroupBox8` | `AFT Lock` |
| ├ `LockCodeCB` | `00= Request Lock`, `80= Cancel lock or pending lock request`, `FF= Interrogate current status only` |
| ├ `LockCondition1..4` | `TXFR to Machine OK`, `TXFR from Machine OK`, `TXFR to Printer OK`, `Bonus to Machine OK` |
| ├ `LockTimeoutEdit` | `500` |
| └ `AFTLockButton` | `AFT Lock` |
| `GroupBox7` | `AFT Registration` |
| ├ `AFTRegCodeComboBox` | `00 = Initialize Registration`, `01 = Register Gaming Machine`, `40 = Request Operator Acknowledgement`, `80 = Unregister Gaming Machine`, `FF = Read Current Registration` |
| ├ `AFTAssetEdit` | `3041AB00` |
| ├ `AFTRegKeyEdit` | `9876543210123456`, MaxLength 40, Hint *“Enter Value in BCD”* |
| ├ `AFTPOSIDEdit` | `11223344` |
| ├ `RegGamingMach` | `Register Gaming Machine` |
| └ `AutoRegisterCB` | `Auto Register` — **Checked** |

**Sub-tab `Transfer Funds`**

| Control | Caption / items |
|---|---|
| `TransferCodeCB` | `00 = Transfer request, full transfer only`, `01 = … partial transfer allowed`, `80 = Cancel transfer request`, `FF = Interrogate` |
| `TransferTypeCB` | `00 = Txfr in-house amount from host to GM`, `10 = Txfr bonus coin out win amount from host to GM`, `11 = Txfrr bonus jackpot win amount from host to GM`, `20 = Txfr in-house amount from host to ticket `, `40 = Txfrr debit amount from host to GM`, `60 = Txfr debit amount from host to ticket`, `80 = Txfr in-house amount from GM to host`, `90 = Txfr win amount from GM to host` |
| `CashableEdit` / `RestrictedEdit` / `NonRestrictedEdit` | `0000000000` each |
| `TransferFlag0` | `Host Cashout Enable Control` |
| `TransferFlag1` | `Host Cashout Enable` |
| `TransferFlag2` | `Host Cashout Mode  0=soft 1=hard` |
| `TransferFlag3` | `Cashout from Gaming Machine Request.` |
| `TransferFlag5` | `Use custon ticket data` *(sic)* |
| `TransferFlag6` | `Accept only if locked` |
| `TransferFlag7` | `Transaction Receipt request` |
| `TransactionIDEdit`, `ExpirationEdit`, `AFTPoolIDEdit`, `TransIndexEdit` (`00`) | ID / expiry / pool / index |
| `RecieptDataCB` | `00 = Transfer source/destination`, `01 = Date and time`, `10 = Patron name`, `11 = Patron acct#`, `13 = Account Balance`, `41 = ATM card#`, `42 = Transaction fee`, `43 = Total debit amount` |
| `TransSDCB` … `TotalDebitCB` | 8 checkboxes mirroring the receipt fields above |
| `AFTTransactionButton` | `Do AFT Transaction` |
| `CancelAFTButton` | `Cancel AFT Transaction` |
| `InterrogateButton` | `Interrogate AFT` |
| `AutoInterrogateCB` | `Auto Interrogate` — **Checked**. Hint *“…auto interrogate when 69 is recieved”* |
| `AppendTRID` / `TransIDAppend` | `Append Transaction ID` / read-only counter |
| `AutoIncTID` | `Auto Inc TID` |
| `TransIDError` | `Trans ID Error` — **negative test** |
| `ZeroRegKey` | `Zero Reg Key` — **negative test** |
| `UpDown4` (0–99), `UpDown5` | spinners |

**Sub-tab `Receipt`**

`TransReceiptCB` items: `00 = Location`, `01 =Address 1`, `02 = Address 2`, `10 = In House Line 1`,
`11 = In House Line 2`, `12 = In House Line 3`, `13 = In House Line 4`, `20 = Debit Line 1`,
`21 = Debit Line 2`, `22 = Debit Line 3`, `23 = Debit Line 4`. Eleven matching checkboxes,
`UpdateReceiptButton` = `Update Receipt Data (0x75)`, plus `Save AFT Defaults` / `Restore AFT Defaults`.

#### Tab 11 — Pulltab (`TabSheet13`, hidden)

Paytable grid labelled `1 Line 1 coin` … `9 Lines 5 coins`, `Game 1` / `Game 2` columns, `Wins`, `Total Tabs`,
`Pay for this sequence =`. `GroupBox9` = `Pulltab` → `RNGSeedCB` = `RNG Seed Mode Enabled`,
`ForcePulltab` = `Force a Pulltab`. Backed by INI `[RNGMode]` and external `DD2K_%d.BINx` files
(`raw-ascii.txt:6859`).

#### Tab 12 — Report (`TabSheet14`)

`Memo3` (free-text report buffer), `AddReportEntryButton` = `Add Report Entry`,
`ClrRptButton` = `Clear`. Saved via **File ▸ Save Report File** to `SasReport.txt`
(filter `TXT File (*.txt)|*.txt|Any File (*.*)|*.*`).

#### Tab 13 — Authentication (`TabSheet18`) — long poll `$6E`

| Control | Caption / items |
|---|---|
| `ActionCB` | `00 = Interrogate number of installed components`, `01 = Read status of component`, `02 = Authenticate component`, `03 = Interrogate authentication status` |
| `GroupBox14` | `Authentication Data` |
| ├ `AuthenticationMethodCB` | `00000000  = None`, `00000001 = CRC 16`, `00000002 = CRC 32`, `00000004 = MD5`, `00000008 = Kobetron I`, `00000010 = Kobetron II`, `00000020 = SHA1` |
| ├ `SeedEdit` | `1122334455` |
| ├ `OffsetEdit` | `66778899` |
| └ `IncludeAuthCB` | `Include Authentication Data` |
| `GroupBox15` | `Address Specification` |
| ├ `AddressingModeRB` | `00 = addressing  by component index`, `01 = addressing by component name` |
| ├ `ComponentNameEdit` | `0123456789` |
| ├ `ComponentIndexEdit` | `0123456789` |
| └ `IncludeAddressCB` | `Include Address Specification` |
| `Button5` | `Send Authentication Info` |
| `Button8` | `Interrogate` |
| `AuthAutoInterCB` | `Auto Interrogate` — **Checked** |

### 3.3 `InputDialog: TInputDialog`

Caption `Dialog`, `poScreenCenter`. Controls: `Bevel1`, `Message` (TLabel `Message`), `Image1`,
`Edit1` (Hint *“Enter a value”*), `ComboBox1`, `OKBtn` `OK`, `CancelBtn` `Cancel` (`Visible=False`),
`PopupMenu1` → `Keypad`. This is the generic per-parameter prompt driven by the INI `?XX = text@N`
input-box definitions — **the app asks for long-poll parameters one modal dialog at a time.**

### 3.4 `POPUP: TPOPUP`

Caption `Key Pad`. `Keys: TGroupBox` with buttons `0`–`9` and `A`–`F` (all wired to one
`Button1Click`), plus a hidden `S A` button. On-screen hex keypad for touch use.

### 3.5 `SASAboutBox: TSASAboutBox`

Caption `IGT SAS Simulator`. `ProductNameLab` = `SAS Simulator`, `VersionLAB` = `Version 1.1`,
`Copyright` = `Copyright (c) 1997-2005 International Game Technology`,
`SASVersionLabel` = `Supports SAS Versions`, a `TTimer` (`Interval=50`), and **two buttons:
`I Agree` / `I Don't Agree`.**

Memo text (verbatim):
> *This program uses multiple threads and will support 40 ms polling cycles and progressive
> broadcasts. As a result a 200 mhz Pentium is required to run properly.*
> *Conditions for use of this program: This is a prototype test program, and should be treated
> as such. This program is supplied AS IS, no guarantees of operation, warranties, or support is
> given. The user assumes all responsibility for any damage this program may cause. This program
> is used with the express permission of IGT. Any unauthorized distribution or duplication is
> strictly prohibited.*

**This is a modal EULA gate on every launch** — `I Don't Agree` exits. It is a hard blocker for
unattended/CI use.

---

## 4. Imports (`exe-imports.txt`)

9 DLLs, 406 imports. **All ANSI (`…A`) — no Unicode.** No `inpout32`, `dlportio` or `giveio`:
there is **no direct hardware-port access**; everything goes through the Win32 comm API.

| DLL | Count | Notable |
|---|---:|---|
| KERNEL32 | 121 | see below |
| USER32 | 164 | standard VCL windowing |
| GDI32 | 71 | standard VCL drawing |
| COMCTL32 | 23 | `InitCommonControlsEx` (tabs, trackbar, updown, listview) |
| OLEAUT32 | 15 | by ordinal — VCL `BSTR`/Variant support |
| ADVAPI32 | 3 | `RegOpenKeyExA`, `RegQueryValueExA`, `RegCloseKey` — **read-only**, VCL locale/keyboard use |
| VERSION | 3 | `GetFileVersionInfoA`, `GetFileVersionInfoSizeA`, `VerQueryValueA` (About box) |
| COMDLG32 | 2 | `GetOpenFileNameA`, `GetSaveFileNameA` |
| WINMM | 4 | `timeBeginPeriod`, `timeEndPeriod`, `timeSetEvent`, `timeKillEvent` |

**Serial-comm APIs actually imported:**
`CreateFileA`, `BuildCommDCBA`, `SetCommState`, `SetCommTimeouts`, `SetupComm`, `SetCommMask`,
`WaitCommEvent`, `ClearCommError`, `PurgeComm`, `ReadFile`, `WriteFile`, `GetOverlappedResult`,
`CreateEventA`, `ResetEvent`, `SetEvent`, `WaitForSingleObject`.

**Notably ABSENT:** `GetCommState`, `EscapeCommFunction`, `GetCommProperties`,
`GetCommModemStatus`, `QueryPerformanceCounter`, `QueryPerformanceFrequency`,
`GetPrivateProfileIntA`, `GetPrivateProfileSectionA`, `CreateFileW`.

Interpretation, each grounded:
* **Overlapped, event-driven RX** — `SetCommMask` + `WaitCommEvent` + `GetOverlappedResult` + a
  dedicated `CreateThread` reader (the help file confirms: *“The receive buffer in this program
  uses a separate thread for reading the bytes coming in.”*).
* **DCB built from a string, never read back** — `BuildCommDCBA` present, `GetCommState` absent.
  The three DCB strings are hard-coded (§5).
* **DTR/RTS toggled through the DCB** (full port re-init), not via `EscapeCommFunction` —
  which is exactly the mechanism the help file blames for the wake-up-bit unreliability.
* **Timing = multimedia timer only.** `timeSetEvent`/`timeBeginPeriod` give ~1 ms callbacks;
  there is **no `QueryPerformanceCounter`**, so all latency measurement is at best millisecond
  granularity from `GetTickCount`/`timeGetTime`-class sources.
* **Thread/process priority is user-controllable** — `SetPriorityClass` + `SetThreadPriority`,
  matching the *Priority Normal / Priority High* buttons and the runtime errors
  `ERROR: SetPriorityClass` / `ERROR: SetThreadPriority`.
* **INI is mostly hand-parsed.** Only `GetPrivateProfileStringA`/`WritePrivateProfileStringA` are
  imported (no `…Int`/`…Section`), while the strings show the app opening the file itself
  (`INI FILE NOT FOUND`, `Cannot open input file!!`, and a literal list of `[Section]` header strings).

---

## 5. Serial configuration — hard-coded

From `raw-ascii.txt:7389–7399`, backed by the 8-entry pointer table at VA `0x4B81D8`
(verified by resolving each pointer to its string):

```
0x4b821c -> 'COM1:'
0x4b8222 -> 'COM2:'
0x4b8228 -> 'COM3:'
0x4b822e -> 'COM4:,COM5:'      <-- single string; missing NUL separator
0x4b823a -> 'COM6:'
0x4b8240 -> 'COM7:'
0x4b8246 -> 'COM8:'
```

```
baud=19200 parity=S data=8 stop=2        <- data byte  (Space parity = wakeup bit 0)
baud=19200 parity=M data=8 stop=2        <- address byte (Mark parity  = wakeup bit 1)
baud=19200 parity=S data=8 stop=2
Could not create Comm Port
BuildCommPort failed!
```

Findings:
1. **Only COM1–COM8 are reachable**, using the legacy `COMn:` name. There is no `\\.\COMn`
   form anywhere in the binary (`grep -naF '\\.\'` → no match; `grep -na 'COM%'` → no match).
   Modern USB/virtual serial adapters routinely enumerate at COM9+ and **cannot be used at all**.
2. **The port array is corrupt**: slot 4 is the literal string `COM4:,COM5:` — a missing
   NUL in the source table. Whatever the app does with slot 4, it is not a valid port name,
   and COM5 has no slot of its own.
3. `sastest.ini` ships with `Port = 11` (`sastest.ini:8`) — **out of range** for a 7-slot table.
4. **19200 8-N-2 is fixed**; baud, data bits and stop bits are not configurable from the UI or INI.
5. The wake-up (9th) bit is emulated by **flipping parity Mark↔Space**, which requires a full
   `SetCommState` re-init per byte. `SASINI.doc` states plainly: *“the SAS protocol uses something
   called Wakeup bits, this is not supported in the Windows API.”* `SASTEST.HLP` elaborates:
   *“Windows requires you to totally re-initialize the COMM port and it is virtually impossible to
   determine if the byte with the wakeup bit really went out of the UART. Also Windows COMM drivers
   do not have any facility to mark any one byte received with an error…”*

---

## 6. Command line & automation — **definitive: none**

Exhaustive search of both ASCII and Unicode strings:

| Probe | Result |
|---|---|
| `usage`, `Usage:`, `--help`, `switches`, `Invalid option`, `unknown option` | **no matches** |
| standalone `-x` / `/x` switch tokens | only `-INF`, `-NAN` (CRT float formatting) |
| `argv`, `argc`, `__argv` | **no matches** |
| `setargv`, `argument vector` | `Out of memory in _setargv0`, `No space for command line argument vector` |
| `GetCommandLineA` | imported from KERNEL32 |
| `WinMain` / `_main` symbol strings | **no matches** |

The only argv-related artefacts are the **Borland C++ CRT's own startup code**, which builds
`_argv` for every Borland program whether or not the program reads it. There is no usage banner,
no option table, and no switch strings. `Subsystem=2` (GUI) means there is no console output path either.

Corroborated by the shipped documentation. `SASTEST.HLP` describes the *only* supported way to
vary a run:

> *“To have it start from a different COMM port: 1. Create A Shortcut… 4. To select the comm port
> you are using, you must go to the INI file and under the SAS Protocols section specify the port.
> example: port = 2”*

**Conclusion: `sastest.exe` has zero automation entry points.** No CLI arguments, no exit codes
worth reading, no scriptable IPC, no headless mode. The only "configuration surface" is editing
`sastest.ini` on disk before launch, and every run additionally requires a human to dismiss the
`I Agree` EULA dialog. The user's #1 complaint is confirmed at the binary level.

---

## 7. The INI: the app's real extensibility model

`C:\IGT3\sastest.ini` (56 492 bytes, 27 sections) is the command/response database. It is what
makes the tool valuable, and it is **all it has** in place of scripting. Section headers are
literal strings in the binary (`raw-ascii.txt:6757–6779`).

| Section | Entries | Role |
|---|---:|---|
| `[SAS Protocols]` | 9 | startup defaults: `Wakeup Delay`, `Port`, `Poll Rate`, `Address`, `Validation`, `Timing`, `CRC Check`, `Setup File`, `Version` |
| `[Communications]` | 3 | `Echo`, `DTR`, `Index` |
| `[General Poll]` | 254 | exception-code → English text |
| `[Long Poll Commands]` | 210 | command definitions + input-box specs (**118 distinct opcodes**) |
| `[Electronic Funds Transfer]` | 24 | EFT command definitions |
| `[Status Messages]` | 21 | EFT status-code text |
| `[Messages]` | 347 | **response decoders** — field offset, format, byte count |
| `[Denominations]` | 30 | multi-denom combo contents |
| `[Ticket Transfer Codes]` | 13 | TITO |
| `[Ticket Machine Status Codes]` | 19 | TITO |
| `[Validation Type Codes]` | 11 | |
| `[AFT Transfer Status Codes]` | 32 | |
| `[AFT Transaction Receipt Status]` | 7 | |
| `[Receive Validation Status Codes]` | 6 | |
| `[ASDC]`, `[Bonus Game Data Structures]` | — | ASDC / bonus message builders |
| `[Message Structures]` | 4 | arbitrary message builder |
| `[Data Structures]` | 16 `BEGIN`/`END` blocks | named, runtime-editable payload templates |
| `[Quick Commands]` | 7 raw lines | one-click verbatim byte strings |
| `[Script]` | — | verbatim send sequence with English annotations |
| `[AFT]` | ~35 | AFT defaults incl. receipt text |
| `[Meter Test]` | 22 | canned meter-poll sequence |
| `[Exception Handling]`, `[LongPoll Handling]`, `[RNGMode]` | — | |
| `[Active Help]` | 7 topics | per-command context help text |

Long-poll definition grammar (documented in-file at `sastest.ini:280–300` and in `SASINI.doc`):

```
$XX = Description,LENGTH,TRANS,ACK,TYPE,CRYPT,CRC,<#input boxes>
?XX = Input box prompt@<bytes>
```

Response-decoder grammar (`sastest.ini:727–751`):

```
$XX = Text = ,<offset>,%<d|a><nbytes>
   ; "&" prefix = message carries an overall length byte
   ; "*" prefix = this is a length byte for the following data
```

Distinct long-poll opcodes defined (118):
`01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F 10 11 12 13 14 15 16 17 18 19 1A 1B 1C 1E 1F 20 21
27 2A 2B 2C 2D 2E 2F 31 32 33 34 35 36 37 38 39 3A 3B 3C 3D 3E 3F 40 41 42 43 44 45 46 47 48 49
4A 4C 4D 4F 50 51 52 53 54 55 56 57 58 6F 70 71 73 74 76 7B 7C 7D 7E 7F 83 84 85 87 8A 8B 8C 8E
8F 90 94 95 96 97 98 99 9A A0 A4 A8 AA AF B1 B2 B3 B4 B5 B6 B7 FD`

`SASINI.doc` warns: *“errors in the INI file may render the tester unusable and cause it to crash”*
and *“The program at this time does not have any error checking for the format of the ini file.”*
**There is no validation of this 56 KB hand-edited file.**

---

## 8. Logging / capture

* **Log ▸ Capture Data** appends to `TXRXData.dat` (hint: *“Saves Data in TXRXData.Dat”*).
* Observed format (`C:\IGT3\TXRXData.dat`, 39 357 255 bytes):

```
Tue Feb 25 09:44:31 2025\r
TX>= 81\r
Tue Feb 25 09:44:32 2025\r
TX>= 80\r
```

Two lines per frame: a full `ctime()` string, then the direction-tagged hex. Consequences:
**1-second timestamp resolution** (useless for SAS timing analysis), ~40 bytes of timestamp per
1-byte poll, CR-only line endings, and no framing/parse structure. The live UI can show
`( ms response time)` and `<-CRC Error !` (`raw-ascii.txt`), but **none of that reaches the file** —
the capture is strictly `TX>=` / `RX<=` plus a coarse timestamp. 39 MB here is almost entirely
general polls.
* **File ▸ Save Report File** writes `SasReport.txt` (manually typed notes from the Report tab).
* Setup persistence: `*.set` (`Save/Restore Default Data`), `*.aft` (`Save/Restore AFT Defaults`).

---

## 9. FEATURE INVENTORY (parity checklist)

Organised by UI area. Every row is traceable to a decoded control, string, or import above.

### 9.1 Session / lifecycle
| # | Capability | Evidence |
|---|---|---|
| F1 | Modal EULA gate at launch (`I Agree` / `I Don't Agree`) | `TSASABOUTBOX` |
| F2 | About box: product, version, copyright, supported SAS versions | `TSASABOUTBOX` labels |
| F3 | WinHelp (`SAStest.hlp`) + in-app **Active Help** per command | menu `Help`/`Active Help`, INI `[Active Help]` |
| F4 | Reload INI without restart | menu `File ▸ Reload INI file` |
| F5 | Save/restore full app state to `*.set` | `SaveDefaults`/`RestoreDefaults` TOpen/SaveDialog |
| F6 | Save/restore AFT-only state to `*.aft` | `*.AFT` filter strings, `AFT` menu |

### 9.2 Link / transport
| # | Capability | Evidence |
|---|---|---|
| F7 | Serial port select (COM1–COM8 only, via INI `Port`) | port pointer table; `SASTEST.HLP` |
| F8 | Fixed 19200 8-N-2 | `baud=19200 parity=S data=8 stop=2` |
| F9 | Wake-up (9th) bit via Mark/Space parity flip; can be disabled | `parity=M`/`parity=S`; `WakeUpCheckBox` |
| F10 | Configurable wake-up delay (1–10 ms) | `ComboBox7`, INI `Wakeup Delay` |
| F11 | Send multiple wake-up bits / `MWB0111` test | `Button12`, `MWB0111` |
| F12 | DTR/RTS set & clear ("for Fiber Optic") | `GroupBox11` Set/Clear |
| F13 | Comm loopback / echo handling incl. echo-index offset | `GroupBox12` `Echoing Enabled`, `Use Echo Index` |
| F14 | Live comm counters: timeouts, bytes read | `GroupBox13 "Comm Info"` |
| F15 | Process/thread priority boost | `Priority Normal` / `Priority High`; `SetPriorityClass` |

### 9.3 Polling engine
| # | Capability | Evidence |
|---|---|---|
| F16 | General-poll loop with continuous rate control 20–2100 ms | `TrackBar1` |
| F17 | One-click rate presets 40/80/150/200 ms + OFF | `ms40`,`Button10`,`GP100`,`Button9`,`Button11` |
| F18 | Fine rate spinner 40–2000 ms | `UpDown1` |
| F19 | SAS address select 1–64 | `SASAddress` TRadioGroup |
| F20 | Address cycling up to a configurable max | `CycleAddress`, `UpDown3` |
| F21 | Sync/phantom-poll control: `No Sync (80)`, `No Sync (with time slot)` | `NoPhantom`, `NoSync` |
| F22 | Manual single GP and manual Re-Sync | `Send GP`, `Re-Sync` |
| F23 | Show/hide syncs & GPs in the log | `ShowPhantoms` |
| F24 | Suppress idle chatter (`$00` / `$1F`) | `Disp1F` |

### 9.4 Command dispatch
| # | Capability | Evidence |
|---|---|---|
| F25 | Long-poll picker driven by INI (118 opcodes, 210 defs) | `ComboBox1` + `[Long Poll Commands]` |
| F26 | EFT command picker (24 defs) | `ComboBox2` + `[Electronic Funds Transfer]` |
| F27 | ASDC `$A5` command set | `ComboBox4` `$A5 ASDC Commands`, INI `[ASDC]` |
| F28 | Free-form custom hex command with optional CRC | `Edit1`, `CRCBox`, `SendCC` |
| F29 | Quick Commands: one-click verbatim byte strings | `TabSheet3`, INI `[Quick Commands]` |
| F30 | Resend last message / last command | `ResendStructButton`, `ReSend`, `Resend2`, `RepeatButton` |
| F31 | Per-parameter modal input dialog with on-screen hex keypad | `TINPUTDIALOG` + `TPOPUP` |
| F32 | Message Structures builder + runtime **Data Structure editor** | `PageControl2` tabs; INI `[Message Structures]`/`[Data Structures]` |
| F33 | Bonus game data structures | INI `[Bonus Game Data Structures]` |

### 9.5 Decode / display
| # | Capability | Evidence |
|---|---|---|
| F34 | Dual-pane view: English decode + raw TX/RX hex, user-resizable | `Memo2`/`Memo1`/`Splitter1` |
| F35 | Exception-code → English (254 mappings) | INI `[General Poll]` |
| F36 | Response field decoder: offset + `%d`/`%a` + byte count (347 rules) | INI `[Messages]` |
| F37 | Per-message response-time display | `( ms response time)` string; `Monitor Timing` |
| F38 | CRC error flagging in the log | `<-CRC Error !`, `Report CRC Errors` |
| F39 | Freeze/resume the display | `Stop Display` / `Start Display` |
| F40 | Clear English pane / hex pane / both | `Button2`, `Button3`, `ClrDisps` |

### 9.6 Validation & TITO
| # | Capability | Evidence |
|---|---|---|
| F41 | Validation mode: none / Standard / Secure Enhanced / System | `RadioGroup1` |
| F42 | System validation number (16) + system ID (3), randomise, auto-increment | `GroupBox2` |
| F43 | Secure Enhanced: machine ID + sequence #, manual & auto initialise | `GroupBox4` |
| F44 | Ticket redemption: `$70` interrogate, `$71` redeem, `$71` get status | `Send70`,`SetValidationFlags`,`GetStatus` |
| F45 | Auto-redemption responder | `AutoValReplyBox` (default on) |
| F46 | Ticket amount override, hex insert, transfer-code select | `TxfrAmt`,`InsertHex`,`TransferCodesCB` |
| F47 | Restricted redemption: expiration + pool ID | `GroupBox5` |

### 9.7 AFT
| # | Capability | Evidence |
|---|---|---|
| F48 | AFT registration: 5 reg codes, asset #, reg key (BCD), POS ID | `GroupBox7` |
| F49 | Auto-register on demand | `AutoRegisterCB` (default on) |
| F50 | AFT lock: 3 lock codes, 4 lock conditions, timeout | `GroupBox8` |
| F51 | AFT transfer: 4 transfer codes × 8 transfer types | `TransferCodeCB`,`TransferTypeCB` |
| F52 | Cashable / restricted / non-restricted amounts | 3 TEdits |
| F53 | 7 transfer flags incl. host-cashout mode and "accept only if locked" | `TransferFlag0..7` |
| F54 | Transaction ID, index, expiration, pool ID; auto-increment; append-unique | `TransactionIDEdit` … `AutoIncTID`,`AppendTRID` |
| F55 | AFT interrogate + auto-interrogate on `$69` | `InterrogateButton`,`AutoInterrogateCB` |
| F56 | Cancel AFT transaction | `CancelAFTButton` |
| F57 | Receipt data: 8 field types selectable | `RecieptDataCB` + 8 checkboxes |
| F58 | Transaction receipt text editor: 11 lines (location, address, in-house, debit) | `TabSheet15` |
| F59 | Update Receipt Data (`0x75`) | `UpdateReceiptButton` |

### 9.8 Progressive
| # | Capability | Evidence |
|---|---|---|
| F60 | Single-level broadcast (`$80`) | `Do80` (default on) |
| F61 | Multi-level broadcast (`$86`) | `Do86` |
| F62 | Repeating broadcast at 1–10 s interval | `ProgRepeat`,`UpDown6`,`ProgRateTimer` |
| F63 | One-shot broadcast | `Button4` |
| F64 | Auto-increment progressive amounts | `IncProgCB`, `In` = `Increment amt.` |
| F65 | Group select 0–255, level amounts | `UpDown2`, `Level %d Amount` format string |
| F66 | Show/hide broadcasts in the log | `DispBroad` |

### 9.9 Component authentication (`$6E`)
| # | Capability | Evidence |
|---|---|---|
| F67 | 4 actions: count / read status / authenticate / interrogate status | `ActionCB` |
| F68 | 7 methods: None, CRC16, CRC32, MD5, Kobetron I, Kobetron II, SHA1 | `AuthenticationMethodCB` |
| F69 | Seed + offset entry | `SeedEdit`,`OffsetEdit` |
| F70 | Addressing by component index or name | `AddressingModeRB` |
| F71 | Optional inclusion of auth-data / address blocks | `IncludeAuthCB`,`IncludeAddressCB` |
| F72 | Auto-interrogate | `AuthAutoInterCB` (default on) |

### 9.10 Multi-denom, meters, EFT
| # | Capability | Evidence |
|---|---|---|
| F73 | Multi-denom: denomination checklist + add preamble to long polls | `TabSheet7` |
| F74 | Individual meter monitoring: meter/position/byte-count, change counter | `GroupBox6` |
| F75 | Canned meter test sequence | `MeterTest` + INI `[Meter Test]` |
| F76 | EFT transaction number inc/dec, ACK control, denomination list | `TabSheet1` (hidden) |

### 9.11 Fault injection (negative testing)
| # | Capability | Evidence |
|---|---|---|
| F77 | Force CRC error on transmit | `CRCError` |
| F78 | Break the TX/RX line on final ACK, then restore | `BreakEFT`,`RestoreRXLineButton`, `Simulated broken RX TX Line..Message not processed` |
| F79 | Garble the message | `Garble` |
| F80 | Bad validation number | `BadValNum` |
| F81 | Bad transfer code | `BadTransCode` |
| F82 | Transaction-ID error | `TransIDError` |
| F83 | Zero registration key | `ZeroRegKey` |
| F84 | Zero the transaction field | `Trans0` |
| F85 | Force / suppress EFT ACK | `ForceEFTACK`,`NoEFTACK` |
| F86 | Insert a GP mid-sequence | `InsertGP` |

### 9.12 Scripting (such as it is)
| # | Capability | Evidence |
|---|---|---|
| F87 | Verbatim send-sequence from INI `[Script]`, auto-CRC over 2 bytes | `TabSheet16`, `SASINI.doc` |
| F88 | Scripting suppresses all automatic polling | `SASINI.doc` |
| F89 | Two list panes for script steps and progress | `ListBox1`,`ListBox2` |

### 9.13 Logging & reporting
| # | Capability | Evidence |
|---|---|---|
| F90 | Capture TX/RX to `TXRXData.dat` (1-second timestamps) | `Log ▸ Capture Data`; file inspection |
| F91 | Manual report buffer + `Add Report Entry` + `Clear` | `TabSheet14` |
| F92 | Save report to `SasReport.txt` | `File ▸ Save Report File` |
| F93 | Save arbitrary data via `*.dat` save dialog | `SaveDialog1` |

### 9.14 Pulltab / RNG (hidden)
| # | Capability | Evidence |
|---|---|---|
| F94 | Pulltab paytable display, 2 games, 9 line/coin combinations | `TabSheet13` |
| F95 | RNG seed mode, force a pulltab, external `DD2K_n.BIN` data | `RNGSeedCB`,`ForcePulltab`, INI `[RNGMode]` |

---

## 10. Gaps and defects in the legacy tool (the case for the rewrite)

Every item verified above.

1. **No automation whatsoever.** No CLI args, no headless mode, no exit codes, no IPC (§6).
2. **A modal EULA on every launch** blocks unattended runs (§3.5).
3. **COM1–COM8 only**, legacy `COMn:` naming, no `\\.\COMn` (§5). Modern adapters at COM9+ are unusable.
4. **Corrupt port table** — slot 4 is the literal string `COM4:,COM5:` (§5).
5. **Shipped INI is self-inconsistent** — `Port = 11` exceeds the 7-slot table (§5).
6. **Fixed 19200 8-N-2**; no baud/framing configurability (§5).
7. **Address list bug** — `SASAddress` items are `1..64` but position 59 holds `58`, so 59 is unreachable and 58 appears twice (§3.2 Tab 1).
8. **No keyboard shortcuts at all** — zero `RT_ACCELERATOR`, zero `ShortCut` properties (§2).
9. **Capture file is 1-second resolution**, two lines and ~40 bytes of `ctime()` per frame; response times and CRC flags are shown on screen but never written to file (§8).
10. **Response-time measurement has no high-resolution clock** — `QueryPerformanceCounter` is not imported (§4).
11. **56 KB hand-edited INI with no validation**; the vendor's own doc says malformed entries crash the app (§7).
12. **Developer debris shipped in the UI** — buttons literally captioned `test` and `Test Button` (§3.2 Tab 3).
13. **Two whole feature tabs are hidden** (`EFT Test`, `Pulltab`) and only reachable by editing the binary (§3.2).
14. **ANSI-only, single-instance, single-port** — one app instance can drive exactly one EGM.
15. **Wake-up-bit emulation is admitted unreliable** by the vendor's own documentation (§5).
16. **Self-described as a prototype**: *“This is a prototype test program… never meant for release”* (`SASTEST.HLP`).
17. Typos in shipped UI text: `Reciept Data`, `Compnent Name`, `custon ticket data`, `Txfrr`, `activiy`.

---

## 11. Context: what the new app will drive

`C:\Progetti\libsas` is the **gaming-machine (EGM/client) side** SAS 6.02 library
(`README.md`: *“Quixant Slot Accounting System 6.02 Protocol Library”*). `sastest.exe` is the
**host/system side** that polls it. The replacement is therefore a host-side SAS simulator and
conformance driver.

Note: `C:\IGT3\conf.ini` is byte-identical to `C:\Progetti\libsas\include\conf.ini`
(`diff -q` → identical) and is **libsas's feature-flag file, not a sastest.exe config** —
`grep -ic 'conf\.ini'` against the binary's strings returns `0`. It nonetheless documents the
feature matrix the new tool must exercise: jackpot multiplier, AFT/legacy bonus awards, tournament,
validation extensions, ticket redemption, extended meters, component authentication, AFT,
multi-denom extensions, maximum polling rate, multiple SAS progressive win reporting,
meter change notification, session play.

---

## 12. UNKNOWNs

* Exact runtime semantics of `[Exception Handling]`, `[LongPoll Handling]`, `[RNGMode]` —
  sections are present but empty/near-empty in the shipped INI; no code was disassembled.
* Whether `Timing = 1` in `[SAS Protocols]` changes the log format — no timing-annotated
  records appear in the 39 MB `TXRXData.dat` sample.
* The `DD2K_%d.BINx` / `File%d` / `Frequency%d` / `Mult%d` RNG data-file format — no such
  files are present in `C:\IGT3`.
* Whether `Index` under `[Communications]` (commented out in the shipped INI) selects the
  echo-index offset or a port index.
* The precise meaning of the `CRYPT`/`TYPE` columns in the long-poll grammar beyond
  "extended EFT only" as stated in the INI comments.
* Content of the `Supports SAS Versions` label at runtime; the INI carries
  `Version = "s 2.00 - 6.02"` but the About box builds the string at run time.
* No disassembly was performed (`objdump`/`radare2` unavailable), so all behavioural claims
  are grounded in resources, strings, imports, shipped docs and data files — not in code flow.
