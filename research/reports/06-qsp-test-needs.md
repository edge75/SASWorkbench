# QSP's SAS surface, and the automated test scenarios a replacement host tool must run

Research target: `C:\Progetti\QSP` (the platform under test) + `C:\Progetti\libsas` (the library it
consumes). Benchmark for the new tool: `C:\IGT3\sastest.exe`.

**Scope note.** `C:\Progetti\libsas\utils\sashost`, `utils\poller` and `utils\hwconformance` were
NOT opened, read, searched or used at any point. Nothing below derives from them.

Every claim carries a file path + line or the command that produced it. Anything I could not
establish is marked **UNKNOWN**.

---

## 0. Bottom line

QSP has a large, live, deeply-specified SAS 6.02/6.03 implementation (`marketsas`, ~14.3k lines on
top of libsas) and **zero automated tests that put a single byte on a SAS wire**. The 8-target,
12,149-line GoogleTest suite tests *pure rule headers* only — 7 of the 8 targets link `gtest` and
nothing else (`C:\Progetti\QSP\test\QSPTest.cmake:20-23, 55-61`). The simulator/HAL build, which is
the only way to run QSP without a Quixant board, replaces libsas with a 130-line stub in which every
SAS call returns success and nothing happens (`C:\Progetti\libsas\src\hal\libsas.cpp`). CI never runs
ctest at all. So today the entire SAS surface is verified by a human holding `sastest.exe` against a
physical board over a serial cable, and nothing records what was covered.

---

## 1. `marketsas` — what it is

### 1.1 Location and shape

`C:\Progetti\QSP\src\LibMarketSas\` — 40 files, 14,292 lines (`wc -l *.h *.hpp *.cpp *.inl *.cmake`).
Built as a **shared** library named `libmarketsas` (`LibMarketSas.cmake:3, 46`), linking `sas_static`
plus `qxcom_static` (`LibMarketSas.cmake:23, 33`).

Largest units:

| File | Lines | Subject |
|---|---|---|
| `SasNetwork.cpp` | 1588 | start/stop, configuration, NVRAM bridge, 1 s timer |
| `SasNetwork_AftTransfer.hpp` | 1431 | LP 72/73/74/75/76 AFT |
| `SasNetwork_Meters.hpp` | 1392 | the 256-entry Table C-7 meter table |
| `SasNetwork_Ticket.hpp` | 1018 | TITO in/out, validation, LP 4C/58/71/7B/7C/7D |
| `SasNetwork_Conditions.hpp` | 933 | condition → exception table, handpay |
| `SasNetwork_Callbacks.hpp` | 871 | the libsas callback dispatch |
| `SasMarket.cpp` | 805 | `MarketBase` implementation |
| `Audit_SasConfiguration.hpp` | 539 | the operator SAS configuration page |

### 1.2 What it adds on top of libsas

libsas is the wire engine: framing, CRC, implied ACK/NACK, the exception queue, long-poll parsing and
response construction, the AFT/registration/handpay/progressive state machines. `marketsas` is
everything the *machine* has to decide, which libsas cannot know:

- **Condition → exception mapping.** A fixed table `kSasExceptions[]`
  (`SasNetwork_Conditions.hpp:19-80+`) maps each platform `Condition` to an activated and a cleared
  exception code, plus a handler. e.g. `eIntrusionMainMech → SlotDoorOpened/SlotDoorClosed`,
  `eIntrusionLogic → CardCageOpened/CardCageClosed`, `eIntrusionStacker →
  CashboxDoorOpened/CashboxDoorClosed`, `eIntrusionDropbox → DropDoorOpened/DropDoorClosed`,
  `eIntrusionSecondary → BellyDoorOpened/BellyDoorClosed`, `eIntrusionStorage →
  ComponentListChanged (0x8E)`.
- **Meter derivation.** A 256-entry table of lambdas (`SasNetwork_Meters.hpp:267+`), one per SAS
  Table C-7 meter code; **135 of 256 are implemented**, 14 of those are per-game
  (measured by script over `SasNetwork_Meters.hpp`). Values are computed from NVRAM and converted to
  the accounting denomination in `SasCallbackMetersProvider` (`SasNetwork_Meters.hpp:1252-1321`).
- **Exception rate/duplication rules** free of libsas: `ExceptionReporting.h` — report each
  power-off door access (0x98/0x99/0x9A/0x9B) once per power-up; report game selected (0x8C) only on
  change and at most once a second.
- **A non-volatile held-exception ledger** for reports raised before the link exists
  (`HeldExceptionLedger.h`, `SasNetwork.h` `PendingExceptions::eMaxPendingExceptions = 64`).
- **Startup gating**: `SasStartupState.h` / `ResolveSasStartupState()` — address 0, or a market that
  withholds the link until configured, both keep libsas un-started.
- **Money/ticket/handpay policy**: `SasMoneyRules.h`, `CashoutPrinterRules.h`,
  `AftCashoutFallback.h`, `HandpayResetRules.h`, `TicketDataProvenance.h`.
- **The operator audit pages** (`Audit_Sas*.hpp`) and the GLI-11 v3.0 language table
  (`Language_Gli11v30.inl`).
- **NVRAM ownership**: `marketsas` supplies libsas' `MEM_APIS` (init/read/write/close) out of QSP's
  NVRAM handler (`SasNetwork.cpp:250-256`).

### 1.3 Markets / jurisdictions

Two jurisdictions and three protocol values exist:

- `enum class Jurisdiction : uint32_t {eGli11, eNsw};`
- `enum class NetworkProtocol : uint32_t {eNone, eSas, eNsw};`
  (`C:\Progetti\QSP\src\LibPlatform\JsonHandler.h:158-159`)

`SasMarket.h:3` describes itself as "**GLI-11 v3.0, SAS 6.03 Market Handler**". A sibling
`C:\Progetti\QSP\src\LibMarketNsw\` implements the NSW/OLGR protocol (`NswNetwork.cpp`,
`Language_Nsw.inl`) — a different wire protocol, not SAS. PAGCOR requirements are carried as
configuration options inside the GLI-11 build, not as a third jurisdiction
(`C:\Progetti\QSP\doc\PAGCOR\` + `"stacker removed self clearing"` in
`bin/HALDebug/x64/Resources/configuration.json:375`).

### 1.4 Interface to the platform and to the game

- **To the platform**: `class SasMarket : public MarketBase` (`SasMarket.h:35`) — ~50 virtual
  overrides (conditions, meters reset, progressive notification/award, door/banknote/ticket events,
  timers, NVRAM reset hooks, network configuration get/set, diagnostics, AFT registration). The
  platform instantiates it through `QSP_API MarketBase *CreateMarket();` (`SasMarket.h:104`).
- **To the game**: only through `libqsp.h` / `libqsp_audit.h` callbacks. The SAS-specific public
  surface is thin: `qspGetNetworkConfiguration` / `qspSetNetworkConfiguration` /
  `qspNetworkDiagnostics` / `qspGetNetworkDiagnosticInfo` (`include/libqsp_audit.h:541-544`),
  `qspAftRegistrationRequest` / `qspAftRegistrationCancel` / `qspGetAftRegistrationStatus`
  (`:589-591`), `qspGetMarketAuditData` (`:520`), `qspGameSasCardInformation` /
  `qspGameSasCardHeld` / `qspGameReelStopPositions` (`include/libqsp.h:322-323`), plus the
  callbacks `QSP_CALLBACK_LINK_PROGRESSIVE_METERS`, `QSP_CALLBACK_NETWORK_SOUND`,
  `QSP_CALLBACK_GAME_AUTO_REBET`, `QSP_CALLBACK_ROM_SIGNATURE_16`,
  `QSP_CALLBACK_AFT_REGISTRATION`.
- **Public market header**: `C:\Progetti\QSP\include\libmarketsas.h` — `QspSasConfiguration`
  (manufacturer id, 4 poll addresses, progressive group id, AFT asset number, 32-bit flags) and the
  `SasMarketAuditData` / `SasCashlessCountIndex` audit enums.

---

## 2. How QSP consumes libsas

### 2.1 The libsas API surface QSP actually calls

`grep -rhoE '\bsas[A-Z][A-Za-z0-9_]*\s*\(' src/ test/ examples/ | sort | uniq -c` gives **43 distinct
libsas entry points** (libsas exports ~74, declared in `C:\Progetti\libsas\include\libsas_wrapper.h`):

```
sasInit sasShutdown sasAddSasChannel sasRemoveChannel sasSetChannelControl sasGetChannelStatus
sasSetSerialCallbacks sasLoggerConfig sasGetStructInfo sasSetConfigurationParameter
sasNvramReadVersion sasNvramDataSizeof
sasSetImmediateModeMeter sasSetMetersProvider sasWriteMeter sasReadMeter sasWriteLegacyMeter
sasSetAccountingDenom sasSetEnableDenomination sasCreateWagerCategory sasWriteWageredCredit
sasSetCurrentAmounts sasSetAftTransferLimit sasUpdateAftTransfer sasAftGmToHost
sasAftCancelGmToHost sasSetAftLockStatusProvider sasSetAftRegistrationStatus
sasGetAftRegistrationStatus
sasStartCashout sasCompleteCashout sasCancelCashout
sasStartTicketRedemption sasCompleteTicketRedemption sasRefuseTicketRedemption
sasGetTicketExpiration sasSetExtendedValidationStatus
sasHandpayStart sasHandpayReset sasHandpayConfigureLegacyReporting
sasSetProgressiveLevelWin sasSetBusyResponse
```

Not called by QSP: `sasRegisterComponent`, `sasReplyAuthentication`, `sasGetComponentAuthentication`
(component authentication), `sasRegisterCustomPoll` / `sasCustomPollResponse` (custom polls),
`sasSetFeature`, `sasSelectGameDenomination`, `sasIncrementMeter`, `sasSetTicketExpiration`,
`sasHandpayRefuse`, `sasHandpayConfigureRemoteReset`, `sasRegisterROMSignature`
(`grep` over `C:\Progetti\QSP\src`).

QSP handles **all 15** `SasLibInternalNotifications` values
(`grep -rhoE 'INTERNAL_NOTIFICATION_[A-Z0-9_]+' src/ | sort -u`, cross-checked against
`C:\Progetti\libsas\include\libsas.h:213-228`).

### 2.2 Which SAS features are LIVE in QSP

The authoritative statement is the `sSasEnabledFeatures` passed to `sasInit`
(`C:\Progetti\QSP\src\LibMarketSas\SasNetwork.cpp:274-294, 336`). Struct definition:
`C:\Progetti\libsas\include\libsas_wrapper.h:105-126`.

| Feature (LP A0 bit) | QSP value at `sasInit` | Evidence |
|---|---|---|
| jackpotMultiplier | **0** (never set in the `sasInit` block) | `SasNetwork.cpp:274-294` |
| aftBonusAwards | 1 | `SasNetwork.cpp:275` |
| legacyBonusAwards | operator flag `legacy_bonus_enabled` | `SasNetwork.cpp:276` |
| **tournament** | **0** (never set) | `SasNetwork.cpp:274-294` |
| validationExtensions | derived from validation style | `:277-278` |
| validationStyleSystem / validatonStyleSecureEnhanced | from operator validation style | `:279-280` |
| ticketRedemption | `ticket_in_enabled && style supports redemption` | `:285-286` |
| meterModelWonCreditsMeteredWhenWon | 1 | `:287` |
| ticketsToTotalDropAndTotalCancelledCredits | 1 | `:288` |
| extendedMeters | 1 | `:289` |
| **componentAuthentication** | **0** — `// TODO modify when LibQpm supports component authentication` | `:290` |
| advancedFundsTransfer | 1 | `:291` |
| multiDenomExtensions | 1 | `:292` |
| maximumPollingRate | 1 | `:293` |
| multipleSasProgressiveWinReporting | 1 | `:294` |
| **meterChangeNotification** | **0** (never set) | `:274-294` |
| **sessionPlay** | **0** (never set) | `:274-294` |

So the live feature set is: **meters (135/256 codes), AFT (in/out/bonus/win/ticket, registration,
lock), TITO (secure-enhanced + system validation, ticket in and out), progressives (SAS
broadcast + level win reporting, up to 32 levels), legacy bonus (operator-gated), handpay (standard,
legacy reporting, remote reset), multi-denom, machine/game enable-disable, game delay, maintenance
mode, ROM signature (LP 21, CRC-16 chain), real-time events (delegated wholly to libsas), date/time,
bill validator control.**

**Not live: tournament, session play, meter change notification, component authentication, jackpot
multiplier, custom long polls.**

A trap for the new tool: a **second** `sSasEnabledFeatures` at `SasNetwork.cpp:464-486` (jackpotMultiplier `:465`, componentAuthentication `:482`) sets
`jackpotMultiplier = 1` and `componentAuthentication = 1`, and the code comments say plainly that
this struct **never reaches libsas** ("nothing here calls sasSetFeature … What the host actually sees
in long poll A0 is set once by the `sasInit()` call in `AsyncStart()`" — `SasNetwork.cpp:468-472`).
Reading LP A0 is the only reliable way to know what the EGM claims.

### 2.3 Metering mode

`sasSetImmediateModeMeter(fSasHandle, eMeterMode::FULL_MANUAL)` +
`sasSetMetersProvider(SasCallbackMetersProvider)` (`SasNetwork.cpp:365-370`). libsas therefore
**pulls** every meter from QSP at the moment the host asks, taking the platform lock
(`SasNetwork_Meters.hpp:1252-1321`). There is no meter cache to go stale — but there is a lock, and a
long meter poll (LP 6F/AF/2F extended meters) walks it once per requested code.

---

## 3. SAS configuration in QSP — exact paths and keys

Configuration is split across **three** places. The new host tool must mirror all three or it will
disagree with the EGM.

### 3.1 Build-time (compile flags)

`C:\Progetti\QSP\CMakePresets.json` — `HAL: ON|OFF`, `QSP_CONFIG: ./Resources/configuration.json`.
`C:\Progetti\QSP\src\LibMarketSas\LibMarketSas.cmake:26-31` — `HAL_MODE` define disables
`sasSetSerialCallbacks`, i.e. no serial transport. `SAS_LOGGING` (`:48-51`) enables
`sasLoggerConfig(fSasHandle, 1, "logsas.txt")` (`SasNetwork.cpp:387`).
`C:\Progetti\QSP\CMakeLists.txt:120-126` pins libsas to branch `IGS-226-accounting-denomination`
(commented `#v5.4.4.31`).

### 3.2 Market configuration file — `Resources/configuration.json` (a.k.a. `qsp-config.json`)

Path is set by `-DQSP_CONFIG` (default `./Resources/configuration.json`). The SAS-only options live
under `"Market" → "SAS"`. Shipped example:
`C:\Progetti\QSP\bin\HALDebug\x64\Resources\configuration.json:378-472`.

| Key | Values | Meaning |
|---|---|---|
| `Market.jurisdiction` | `"GLI11"` \| `"NSW"` | jurisdiction rules |
| `Market.protocol` | `"SAS"` \| `"NSW"` \| `"none"` | which market layer runs |
| `Market.SAS.manufacturer id` | 2-char, e.g. `"QX"` | Table C-1 game id (also accepted at `Market.` level with a warning — `JsonHandler.cpp:820-830`) |
| `Market.SAS.transaction receipts` | bool, default **false** | LP 72 receipt requests refused with Table 8.3e status **90** when false |
| `Market.SAS.progressive level hit at notification` | bool, default false | announce level hit at win confirmation instead of at award |
| `Market.SAS.start network` | `"immediately"` \| `"audit exit"` \| `"restart"` | when the link comes up |
| `Market.SAS.attendant menu reporting` | `"on page displayed"` \| `"on audit entry"` | when exceptions 82/83 are reported |
| `Market.SAS.accounting denomination` | whole cents, default 1 | the unit of every credits value reported; answered in LP 1F and 53; adopted only at a memory clear |
| `Market.auto rebet enabled` | bool, default true | false ⇒ LP AA ignored, not ACKed |
| `Market.bill period reset second door` | a `Hardware.Doors.type` | which door + stacker permits a BV period reset (exception 7B) |
| `Market.stacker removed self clearing` | bool, default false | PAGCOR 3.9.1.5 self-clearing stacker |
| `Market.rom_signature_targets` | list of Hash Target names | LP 21 CRC-16 chain; empty ⇒ delegated to the game via `QSP_CALLBACK_ROM_SIGNATURE_16` |
| `Market.audit key in game` | `"refuse"` \| `"ignore"` | refuse ⇒ exception 20 general tilt |
| `Market.Limits.*` | cents | Max Ticket In/Out, Max Cashless In/Out, Max Bills In, Max Money In, Max Credit Meter, Large Game Win, Large Progressive Win |
| `Hardware.Serial Ports.Network 1` / `Network 2` | e.g. `/dev/ttyS5`, `COM3` | the SAS channels |
| `Hardware.Doors[].type` | `main`/`logic`/`stacker`/`dropbox`/`topbox`/`spare`/`opto`/`storage`/`tamper#1..8` | fixes the SAS door exception pair, documented inline at `qsp-config.json:17-22` |

### 3.3 Operator / NVRAM configuration — `QspSasConfiguration`

Written through `qspSetNetworkConfiguration` from the audit page `AuditSasConfiguration`
(`src/LibMarketSas/Audit_SasConfiguration.hpp`), stored in NVRAM
(`SasNetwork.cpp:755-767`). Struct: `include/libmarketsas.h:20-27`; flags:
`src/LibMarketSas/SasConfigurationFlags.h:17-45`.

| Field / flag | Bits | Operator label |
|---|---|---|
| `poll_address[4]` | 0-127 each | "Poll Address #1..#4"; **0 ⇒ SAS never starts** (SAS 6.02 §2.1) |
| `aft_asset_number` | u32 | "AFT Asset Number"; non-zero is a prerequisite for AFT registration (§8.1) |
| `progressive_group_id` | u16 | "Progressive Group ID"; **0 ⇒ non-SAS progressives, no 5 s timeout** |
| `sas_manufacturer_id[2]` | | from the JSON, not editable |
| `disable_poll_timeout` | 1 | "Disable Machine when SAS Poll Timeout" |
| `ticket_in_enabled` | 1 | "Ticket In Enabled" |
| `validation_type` | 2 | "Ticket Out Validation": Standard / Secure Enhanced / System |
| `secure_enhanced_configured` | 1 | set by LP 4C |
| `collect_restricted_tickets` | 1 | (currently `#if 0` in the UI) |
| `sas_603` | 2 | 6.02 / 6.03 (currently `#if 0` in the UI) |
| `non_sas_hit_reporting` | 1 | (currently `#if 0` in the UI) |
| `force_cashout_to_host` | 2 | "Cashout To Host": Disabled / Host Configured / Always |
| `cashout_to_host_enabled` | 1 | set by the host via LP 72 in "Host Configured" mode |
| `cashout_to_host_hard` | 1 | no fallback permitted |
| `legacy_handpay_reporting` | 1 | "Legacy handpay reporting" → `SAS_ENABLE_LEGACY_HANDPAY_REPORTING` |
| `legacy_bonus_enabled` | 1 | "Legacy Bonus supported" |
| `channel_general/aft/progressive/legacy_bonus/validation/comp_auth/information` | 2 each | which of the 4 SAS channels carries each function |

### 3.4 libsas runtime parameters QSP sets

`sasSetConfigurationParameter` is called for exactly two of the 17 `eSasEnumConf` values
(`C:\Progetti\libsas\include\libsas_wrapper.h:62-92`):
- `SAS_ENABLE_LEGACY_HANDPAY_REPORTING` (`SasNetwork.cpp:574`)
- `SAS_DISABLE_AUTO_REBET` (`SasNetwork.cpp:580`)

The other fifteen (`SAS_CONF_7B_AUTO_REPLY`, `SAS_CONF_LP_IMP_ACK_NACK`, `SAS_NACK_RTE`,
`SAS_DISABLE_IMPLNACK_LP71`, `SAS_CONF_PARTIAL_PAY`, `SAS_PROGRESSIVE_T_AT_STARTUP`,
`SAS_ENABLE_FIX_BCD_SIZE`, `SAS_ENABLE_B5_PLAYER_DENOMS`, …) run at their libsas defaults. **UNKNOWN**
what those defaults are — not read.

### 3.5 The `conf.ini` question — and a real mismatch

`C:\IGT3\conf.ini` is byte-for-byte the same file as `C:\Progetti\libsas\include\conf.ini`
(both 545-byte `[features1]/[features2]/[features3]` files; `diff` of the two `cat` outputs is
empty). It is a **feature-code declaration**, i.e. the same bit set as `sSasEnabledFeatures`, in ini
form. QSP has no equivalent file — its feature set is hard-coded at `SasNetwork.cpp:274-294`.

Comparing the two, **five bits disagree**:

| Feature | `C:\IGT3\conf.ini` | QSP at `sasInit` |
|---|---|---|
| `jackpotMultiplier` | 1 | 0 |
| `meterModelWonCreditsMeteredWhenWon` | 0 | 1 |
| `ticketsToTotalDropAndTotalCancelledCredits` | 1 | 1 |
| `componentAuthentication` | 1 | 0 (`SasNetwork.cpp:290`) |
| `validationStyleSystem` / `validatonStyleSecureEnhanced` | 0 / 1 | operator-selected |
| `legacyBonusAwards` | 0 | operator-selected |

A host configured from `conf.ini` therefore believes the EGM supports component authentication and
jackpot multiplier when this EGM says it does not. **The new tool must read LP A0 from the EGM and
either adopt it or fail the run loudly, rather than assume an ini.**

---

## 4. Progressives

### 4.1 Example 10

`C:\Progetti\QSP\examples\Example10_ProgressiveJackpots\src\` (1,735 lines).
- `Main.cpp:264` — `.number_progressive_levels = 2`, `.number_games = 2`.
- `GameManager.cpp:102-129` — two levels ("Grand", "Major"), two configuration sets ("Small",
  "Large"), each level a `QspProgressiveCfg` with `level` (1-32, "limited by SAS"), `type`,
  `startup` (reset value in cents), `ceiling`, `increment` (×10000), `hidden_increment`, `rtp`,
  `probability_hit`. Pushed with `qspSetProgressiveConfiguration(lvl+1, &cfg)` (`GameManager.cpp:293`).
- **Both shipped sets use `QSP_PROGRESSIVE_STAND_ALONE`** (`GameManager.cpp:117-123`), so as shipped
  Example 10 exercises SAPJ, **not** the linked/host-broadcast path. To exercise LP 86 you must edit
  the config to `QSP_PROGRESSIVE_LINKED` *and* set a non-zero Progressive Group ID in the audit menu.
- `Main.cpp:143-145` — `QSP_CALLBACK_LINK_PROGRESSIVE_METERS` is what tells the game a broadcast
  arrived.

### 4.2 The QSP progressive rules

`src/LibMarketSas/SasNetwork_Progressives.hpp`:
- Linked levels are tracked as a 32-bit mask; **every configured linked level starts stale** and the
  `eProgressiveTimeout` lockup stands until *each one* has produced a fresh broadcast (`:16-34`,
  `:141-180`) — SAS 6.02 §10.2 is per level, not per machine.
- The timeout is latched **solely** from libsas' notifications, never a QSP-local timer
  (`SasNetwork.h` comment; `SasNetwork.cpp:797-799`). libsas reuses
  `INTERNAL_NOTIFICATION_PROGRESSIVES_TIMEOUT` for 33 timers: `status == 0` is the master (which also
  queues real SAS exception 53), `status == 1..32` are per-level and queue nothing
  (`SasNetwork.h` comment above `AsyncInternalNotification`).
- **Group ID 0 disables the timeout entirely** — group 00 is reserved for non-SAS progressives
  (`SasNetwork_Progressives.hpp:44-56`).
- Level-hit reporting is configurable: default at award, optionally at win notification
  (`"progressive level hit at notification"`), and the two paths are mutually exclusive per level via
  `fAnnouncedProgressiveLevelHit` (`:63-140`).

### 4.3 `C:\Progetti\QSP\progressive_sas.log`

**Size** 51,576 bytes, 2,143 lines, last modified 13 Jul 2025. It is **untracked** — `.gitignore:96`
excludes `*.log` and `git log -- progressive_sas.log` returns nothing. A working artefact someone
left at the repo root.

**Format.** `<millis> Tx EGM <hex bytes>` / `<millis> Rx EGM <hex bytes>`, with continuation lines
`  0010 <bytes>` carrying the offset when a frame exceeds 16 bytes, plus bare
`<millis>   Timeout, EGM poll response` lines. Direction is named relative to the EGM, so `Tx EGM` is
host→machine. **This is a host-side capture, not a libsas log**: libsas' own logger writes
`EGM-1      - HH:MM:SS.mmm - …` (`C:\Progetti\libsas\src\libsas_logger.cpp:136-137` and
`libsas_uart.cpp:684`). It is also not `sastest.exe`'s `TXRXData.dat`, whose lines are
`Tue Feb 25 09:44:31 2025\r\nTX>= 81\r\n` — one-second resolution, no RX interleave
(`head -c 600 /c/IGT3/TXRXData.dat | xxd`). **Producer: UNKNOWN.**

**What a parse of it shows** (python script over the file):

```
total msgs 2020, span 17200590 → 17451190 ms  (~250 s)
TX:  609 × general poll 81      369 × general poll 80 (sync/broadcast address)
     118 × broadcast LP 86      79 × LP 1A     39 × LP 55     36 × LP 2E     4 × LP 53
RX:  571 × 00 (no exception)    79 × LP 1A response (8 bytes)  39 × LP 55 response (6 bytes)
      4 × LP 53 response (26 bytes)   36 × 8C (game selected)   1 × 53 (progressive timeout)
OTHER: 36 × "Timeout, EGM poll response"
```

Four things it reveals about SAS progressive traffic in practice:

1. **The host alternates 0x80 (address 0, no answer expected) with 0x81 (the EGM at address 1)** and
   polls at 200 ms. The same 80/81 alternation appears in `sastest.exe`'s `TXRXData.dat`, so it is
   the house pattern, not a quirk of this capture.
2. **LP 86 "receive multiple progressive levels" is broadcast every 2 s**, e.g.
   `17201790 Tx EGM 00 86 0D 01 01 01 00 92 33 80 02 00 10 92 33 80 / C9 9E` — address 00, LP 86,
   length 0x0D, group 01, then two 6-byte records (level 01 amount `01 00 92 33 80`, level 02 amount
   `00 10 92 33 80`) and a CRC. 2 s is comfortably inside §10.2's 5 s per-level window.
3. **Exactly one exception 53 (progressive win / progressive-related) was reported in 250 s**, and
   **36 exception 8C (game selected)** — the 8C flood is exactly the behaviour QSP 2.3.3.33 later
   added the one-per-second cap for (`CHANGELOG.md:258-264`).
4. **A reproducible failure is sitting in the file.** Every ~20 s the host sends
   `01 2E 00 00 9B 0F` — LP 2E "Delay game", delay 0000 — **three times, and every one times out**
   (lines 88-93, 258-263, 428-433, 605-610, 767-770 …). 36 sends, 36 timeouts, zero responses, while
   LP 1A/55/53 on the same link at the same time all answer. Whatever the cause, no automated test
   would have let it live in a capture for a year.
   Also note the observed turnarounds: LP 1A `17208190 → 17208219` = **29 ms**, LP 53
   `17208990 → 17209036` = **46 ms**, against SAS 6.02 §2.3.1's **20 ms** budget
   (`C:\Progetti\libsas\include\libsas_internal.h:222` `SAS_EGM_RESPONSE_TIMEOUT 20`, and the
   independent note in `C:\Progetti\QSP\.docs\qxcom-serialread-latency.patch` — "On a SAS channel
   that is ~10 ms added to every host poll, against a protocol budget of 20 ms for the whole
   response (SAS 6.02 section 2.3.1)").

---

## 5. Testing today

### 5.1 What exists

`C:\Progetti\QSP\test\` — 8 sources, 12,149 lines, **GoogleTest** (fetched by `qxcmake`,
`QSPTest.cmake:3-4`). Test counts by `grep -c 'TEST('`:

| Target | File | TESTs | Links |
|---|---|---|---|
| `qsp_sas_test` | `sas_test.cpp` | 114 | gtest only |
| `qsp_money_test` | `money_test.cpp` | 133 | gtest only |
| `qsp_conditions_test` | `conditions_test.cpp` | 93 | gtest only |
| `qsp_meters_test` | `meters_test.cpp` | 54 | gtest only |
| `qsp_aft_test` | `aft_test.cpp` | 107 | gtest only |
| `qsp_games_test` | `games_test.cpp` | 29 | gtest only |
| `qsp_peripherals_test` | `peripherals_test.cpp` | 93 | gtest only |
| `qsp_platform_test` | `platform_test.cpp` | 27 | QSP + nvram_static + marketsas (non-HAL) |

The cmake says it outright: *"The first seven are built against the pure rule headers alone: those
headers are deliberately free of the platform singletons, so the targets link gtest and nothing else
— no QSP, no libsas, no NVRAM, no hardware."* (`QSPTest.cmake:20-23`).

`sas_test.cpp`'s includes confirm it: `SasRules.h`, `SasStartupState.h`, `ExceptionReporting.h`,
`HeldExceptionLedger.h`, `NetworkLinkStatus.h`, `SasConfigurationFlags.h`, `PeripheralRules.h`,
`Language_Gli11v30.inl` — **no `libsas.h`, no `SasNetwork.h`** (`sas_test.cpp:9-31`).

**So: 620 tests, and not one of them sends or receives a SAS byte.** Nothing tests the wire, the
frame, the CRC, the response latency, the exception ordering as the host sees it, or any long poll.

There is also **no `add_test()` and no `enable_testing()`** anywhere
(`grep -rn 'add_test\|enable_testing' CMakeLists.txt test/ cmake/` → empty), and `.github/workflows/ci.yml`
has no ctest step (`grep -n 'ctest\|qsp_.*_test' .github/workflows/ci.yml` → empty). The test binaries
are built by CI and never run.

### 5.2 `C:\Progetti\QSP\simulator\`

Contents (`ls -R simulator`): `include/nvram/libnvram.h`, `include/qxcom/libqxcom.h`,
`include/qxcom/libqxcom_callbacks.h`, `include/utils.h`, `nvram/libnvram.cpp`.

`simulator/nvram/libnvram.cpp` is a **file-backed NVRAM**: `nvram.bin`, 0x800000 bytes, two banks,
mirror mode ("Wrapper for libnvram when working on non-Quixant hardware (standard desktop PC)",
`:1-4`, `:17-19`). That is the whole of the simulator: NVRAM only. `simulator/include/qxcom/` carries
headers, no implementation.

### 5.3 Can QSP run without Quixant hardware? — yes, but with SAS dead

`HAL=ON` (presets `HALDebug`, `HALRelease`, `PreviewHALRelease`) forces `SIMULATE_NVRAM` and
`SIMULATE_QXCOM` and defines `SKIP_INTEGRITY_CHECKS` (`CMakeLists.txt:40-44`), pulls
`qpm_simulator v2.0.0.4` (`:87-100`), and — critically — libsas builds
`src/hal/libsas.cpp` instead of its real sources (`C:\Progetti\libsas\CMakeLists.txt:65-72`).

`C:\Progetti\libsas\src\hal\libsas.cpp` is **130 lines of no-ops**:
`sasInit` sets the handle to `(void*)0x01` and returns success; `sasSetSerialCallbacks` is empty;
`sasWriteMeter`/`sasReadMeter` hit a `kSasMeters[32][256]` array; everything else returns
`QXT_LIB_SUCCESS`. Only the AFT registration status is genuinely modelled, and the file says why:
*"minus the exceptions there is no link to send"* (`:14-18`).

`marketsas` itself compiles with `HAL_MODE`, which skips `sasSetSerialCallbacks` entirely
(`SasNetwork.cpp:258-266`), and `QSPTest.cmake:34-37` drops `marketsas` from the platform test link
in HAL builds.

**Conclusion: on a desktop PC you get a QSP that boots, plays and keeps meters — with no SAS
protocol at all.** There is a `CustomSimulation` preset advertising `SIMULATE_SAS: ON`
(`CMakePresets.json:78-88`) but **nothing in the build consumes that variable**
(`grep -n 'SIMULATE_SAS' CMakeLists.txt cmake/*.cmake src/*/*.cmake` → no match). It is dead
configuration.

### 5.4 What testing SAS actually costs today

Reconstructed from the evidence above plus `doc\Peripheral_Simulator_Tests.md`, which is the only
written record of a manual test procedure in the repo:

1. Build **non-HAL** (real `libnvram`, real `libqxcom`, real `libsas`) — so a **physical Quixant
   board** is required. `platform_test.cpp` is the only target that touches the platform and even it
   is documented as failing wholesale with `QPM_DISABLED (0xA3001FFF)` on a desktop
   (`doc\Peripheral_Simulator_Tests.md:19-20`).
2. Wire a **serial cable** from `Hardware.Serial Ports.Network 1` (`/dev/ttyS5` in the reference
   config) to the host PC.
3. Configure the EGM **by hand at the machine**: turn the audit keyswitch, open the SAS
   Configuration page, type a poll address, an AFT asset number, a progressive group id, tick the
   validation style and the cash-out mode, save, and — on `"start network": "restart"` — power-cycle
   (`Audit_SasConfiguration.hpp`, `SasNetwork.cpp:311-330`).
4. Run **`sastest.exe`** on the host PC and drive it by hand: press the button for each long poll,
   read the answer off the screen, compare it against the SAS 6.02 text in
   `C:\Progetti\QSP\doc\sas_full.txt` (9,437 lines) or Appendix C from memory.
5. Provoke EGM-side events by hand: open a door, insert a note, print a ticket, force a handpay,
   turn the reset key.
6. Record the outcome, if at all, in a markdown table by hand — the pattern
   `doc\Peripheral_Simulator_Tests.md:120-127` uses for the *peripheral* simulator ("Last run
   2026-08-26 by Salvatore Rivieccio … S6 and S7 need someone at the machine and were not re-run").

**There is no SAS equivalent of that document.** Nothing in the repo records which SAS scenarios have
ever been run, against which libsas/QSP versions, or with what result.

---

## 6. THE DELIVERABLE — 32 automatable regression scenarios

Format: **name | preconditions | host actions | assertions | why it matters**.
These are written to be run unattended by a scripted host against QSP+libsas on a board, with the
host owning the serial line and (where marked ⚙) a side channel for EGM-side stimulus.

Timing constants the assertions lean on, all evidenced:
`SAS_EGM_RESPONSE_TIMEOUT = 20` ms (`libsas_internal.h:222`);
200 ms timer thread, `timerCountLinkDown > 25` ⇒ **5 s** silence ⇒ link down
(`libsas_threads.cpp:2122, 2373, 2403`);
`NO_IMPLIED_ACK_MAX_COUNT = 150` ⇒ **30 s** no-implied-ACK latch (`libsas_internal.h:233-234`);
`EXCEPTION_BUFFER_LENGTH = 64` (`libsas_db.h:146`) and QSP's held ledger also 64 (`SasNetwork.h`).

### Link, sync and timing

**T01 — Link bring-up from cold**
| | |
|---|---|
| Pre | EGM powered off; poll address 1 configured; `start network: immediately` |
| Host | Power the EGM ⚙; poll `0x81` continuously from before power-on; log every byte with ms timestamps |
| Assert | First response byte appears within N s of power-up (record the baseline; regress on change); the first non-zero exception seen is from the power-up set (0x8F AC power applied / 0x99 power-off slot door access if a door was worked while off), never a code from a previous run; `qspGetNetworkDiagnosticInfo().link_status[0] == QSP_LINK_STATUS_UP` (`libqsp_audit.h:398`) |
| Why | 2.3.3.53 fixed "AC power was lost reported twice on every power cycle" (`CHANGELOG.md:84-88`); nothing guards it |

**T02 — Poll address 0 means silence**
| | |
|---|---|
| Pre | Poll Address #1 set to 0 via `qspSetNetworkConfiguration` ⚙ |
| Host | Poll 0x80 and 0x81 for 60 s |
| Assert | **Zero** bytes received on the line; no chirp; the EGM keeps playing (credit in/out still work ⚙); diagnostics report `QSP_LINK_STATUS_NOT_POLLED` (4) not `_DOWN` (3) |
| Why | SAS 6.02 §2.1; `SasNetwork.cpp:311-330`; `NetworkLinkStatus.h:20-27`. A regression here takes a machine off the floor or, worse, makes address 0 answer |

**T03 — Long-poll response inside 20 ms**
| | |
|---|---|
| Pre | Link up, quiescent EGM |
| Host | Send LP 1A, 11, 12, 15, 1F, 54, 55, 2F, 6F, B5 — 500 iterations each, measuring last-TX-byte to first-RX-byte with ≥100 µs resolution |
| Assert | p99 < 20 ms for every poll; **hard fail** on any single sample > 20 ms; report p50/p95/p99/max per poll code |
| Why | SAS 6.02 §2.3.1. `progressive_sas.log` shows 29 ms (LP 1A) and 46 ms (LP 53) turnarounds, and `.docs\qxcom-serialread-latency.patch` documents a 10 ms driver regression that ate half the budget. This is the single highest-value test in the list |

**T04 — Sustained poll rate**
| | |
|---|---|
| Pre | Link up; `maximumPollingRate` is claimed (`SasNetwork.cpp:291`) |
| Host | Poll at 200 ms for 60 s, then at 40 ms for 60 s, then at the fastest the feature permits for 60 s |
| Assert | No missed responses; no link-down notification; response latency distribution does not degrade between rates; byte counters in `QspNetworkDiagnosticInfo.total_bytes_received[0]` advance monotonically and match the host's own count |
| Why | The claimed feature bit is untested; a slow meters-provider path under the platform lock (`SasNetwork_Meters.hpp:1257`) would show up only here |

**T05 — Link down after 5 s of silence, and recovery**
| | |
|---|---|
| Pre | Link up; `disable_poll_timeout` **set** (machine disables on poll timeout) |
| Host | Stop polling for 8 s, then resume |
| Assert | The EGM enters the link-down lockup within 5.0-6.0 s of the last poll (per `timerCountLinkDown > 25` at 200 ms); on resumption the link returns to UP; the display overlay text names "Network Link Down : channel 1" ⚙; no exception is lost across the outage — anything raised while down is delivered afterwards, in order |
| Why | `libsas_threads.cpp:2403`; `CHANGELOG.md:270-273` shows the multi-channel text was wrong until 2.3.3.32 |

**T06 — Link down with `disable_poll_timeout` clear**
| | |
|---|---|
| Pre | As T05 but `disable_poll_timeout` **clear** |
| Host | Same 8 s silence |
| Assert | The machine goes on playing and taking money ⚙; the condition raised is the non-inhibiting one; on resumption no spurious exception is emitted for the transition itself |
| Why | `AsyncInternalNotification` picks `eLinkDownInhibiting` vs `eLinkDownNonInhibiting` off that one flag (`SasNetwork_Callbacks.hpp:412`) |

**T07 — Two channels, one down**
| | |
|---|---|
| Pre | Poll Address #1 and #2 configured on `Network 1`/`Network 2`; channel functions split (e.g. AFT on channel 2) |
| Host | Keep channel 1 polling, stop channel 2 for 8 s, resume, then reverse |
| Assert | The lock-up stands while *either* channel is down and clears only when both are up; the on-screen text lists both channel numbers while both are down; an AFT long poll on channel 1 is still refused while the AFT-designated channel is down |
| Why | `Qsp::AnyChannelLinkDown` / `DownChannelNamesChanged` (`SasNetwork_Callbacks.hpp:405-445`); `CHANGELOG.md:270-273`; `CHANGELOG.md:388-393` records a bug where saving Network settings silently killed address #2 |

**T08 — Bad CRC and NACK handling**
| | |
|---|---|
| Pre | Link up |
| Host | Send a well-formed long poll with a deliberately corrupt CRC (e.g. LP 1A with the last byte flipped); then a poll with a truncated frame; then a poll with a good CRC but an unimplemented command byte |
| Assert | Corrupt CRC ⇒ **no response at all** (SAS 6.02: the machine ignores it); the very next correctly-framed poll is answered normally; an unimplemented long poll is ignored rather than ACKed; the exception queue is unchanged by any of the three |
| Why | `SAS_CONF_LP_IMP_ACK_NACK`, `SAS_DISABLE_IMPLNACK_LP71` and the 4-state/20-event implied-ACK/NACK machine (`libsas_db.h:94-97`) run at defaults nobody has verified |

**T09 — Implied ACK / implied NACK sequencing**
| | |
|---|---|
| Pre | Link up; at least three exceptions queued (open and close two doors ⚙) |
| Host | General-poll once, take the exception, then **withhold** the acknowledging poll for 25 s, then resume; separately, repeat the *same* poll rather than advancing, three times |
| Assert | The un-acknowledged exception is re-offered, not dropped; a repeated poll re-offers the same exception and does not advance the queue; the queue survives without duplication; after 30 s with no implied ACK the channel goes DOWN (`NO_IMPLIED_ACK_MAX_COUNT`) even though polls are still arriving |
| Why | `libsas_internal.h:230-247`; `libsas_utils.cpp:806-890`; the 30 s latch is a distinct link-down path from the 5 s one and is never exercised |

**T10 — Real-time event reporting on/off**
| | |
|---|---|
| Pre | Link up |
| Host | LP 0E enable RTE; provoke a door open ⚙; observe the response to the long poll that caused it, then the following general poll; LP 0E disable; repeat |
| Assert | With RTE on, the exception is delivered in the long-poll response **and is not repeated on the following general poll**; with RTE off, it appears on the general poll only; in both cases each exception appears exactly once |
| Why | `CHANGELOG.md:72-75` — fixed in 2.3.3.55, guarded by nothing. QSP itself does nothing for LP 0E (`SasNetwork_Callbacks.hpp:67-69`), so the whole behaviour is libsas' |

### Exceptions

**T11 — Door exception pairs, all six door types**
| | |
|---|---|
| Pre | Link up; a configuration wiring `main`, `logic`, `stacker`, `dropbox`, `topbox`, `spare`, and `opto` (`qsp-config.json` `Hardware.Doors`) |
| Host | For each door: assert the intrusion input open, poll to drain, assert it closed, poll to drain ⚙ |
| Assert | Exactly the documented pair, in order: main/opto/topbox ⇒ **0x11/0x12**, dropbox ⇒ **0x13/0x14**, stacker ⇒ **0x19/0x1A**, logic + tamper#1..8 ⇒ **0x15/0x16**, spare ⇒ **0x1D/0x1E**, storage ⇒ **0x8E** for both edges; no other exception in between |
| Why | The mapping is stated in two places that must agree — `qsp-config.json:17-22` and `kSasExceptions[]` (`SasNetwork_Conditions.hpp:21-37`). A silent divergence misreports a cash-box open as a card cage |

**T12 — Power-off door access reported once per power-up**
| | |
|---|---|
| Pre | EGM powered off; open and close the main door six times while off ⚙ |
| Host | Power on, poll continuously for 90 s |
| Assert | Exception **0x99** appears exactly **once**; likewise 0x98/0x9A/0x9B for card cage/cash box/drop door; no paired "closed" code is emitted for any of them |
| Why | `ExceptionReporting.h:39-66` says the old behaviour was "the same code eighteen times in nineteen seconds", which on a one-exception-per-poll link buries everything behind it |

**T13 — Game-selected (0x8C) throttling**
| | |
|---|---|
| Pre | A multi-game configuration (≥ 20 games); link up, polling at 200 ms |
| Host | Drive a full sweep of the game menu, one game per 50 ms, for 10 s ⚙; poll throughout |
| Assert | At most **one** 0x8C per second; the game the host reads back with LP 55 always equals the game the machine is actually on at that moment; re-selecting the game already selected produces **no** 0x8C |
| Why | `ExceptionReporting.h:81-160`; `CHANGELOG.md:258-264` — a 90-game sweep used to overflow the 64-entry buffer and put game start/end minutes behind. This is exactly the failure `progressive_sas.log` still shows 36 instances of |

**T14 — Exception ordering under load**
| | |
|---|---|
| Pre | Link up, polling at 200 ms |
| Host | Provoke a known sequence of 20 distinct exceptions in a scripted order in under 2 s ⚙ (door open, door close, bill accepted, game start, game end, ticket printed, …); poll to drain |
| Assert | All 20 arrive; the order matches the order they were provoked in, **except** where libsas' documented priority rules reorder them; not one is duplicated; the drain completes in ≤ 20 general polls |
| Why | libsas has an explicit `internalGetExceptionPriority` reordering path (`libsas_internal.cpp:3865-3917`) that no test describes |

**T15 — Exception buffer overflow (0x70) and cease**
| | |
|---|---|
| Pre | Link up but **not polling** (or polling very slowly) |
| Host | Stop general-polling; provoke > 64 exceptions ⚙; resume polling and drain the whole queue |
| Assert | Exception **0x70 ExceptionBufferOverflow** is reported; it is placed ahead of anything not yet transmitted but never ahead of an exception already sent (`libsas_internal.cpp:3873-3917`); the oldest entries are dropped, not the newest (SAS 6.02 §2.2.1); once drained, `INTERNAL_NOTIFICATION_EXCEPTION_BUFFER_OVERFLOW_CEASED` fires and the EGM's `eExceptionBufferFull` condition clears ⚙; 0x70 is not reported a second time for the same episode |
| Why | 64 is a hard limit (`libsas_db.h:146`) that T13's failure mode reaches in normal play, and nothing tests it |

**T16 — Held exceptions survive a restart**
| | |
|---|---|
| Pre | `"start network": "restart"`; a machine mid-configuration, so the link is withheld |
| Host | Not polling (link withheld); provoke a memory reset + configuration save ⚙ so 3A, 7A and 3C are owed; power-cycle; then poll |
| Assert | Exactly one 0x3A, one 0x7A and one 0x3C arrive after the restart, in that order, each exactly once; a second power-cycle produces none of them again |
| Why | `HeldExceptionLedger.h:15-35` documents that reading the store after writing to it is what made AC-power-lost double; `CHANGELOG.md:84-88`, `:137-141` |

### Meters

**T17 — Meter correctness after scripted play**
| | |
|---|---|
| Pre | Memory cleared; single game, known denomination; link up |
| Host | Read the full implemented meter set (135 codes) via LP 0F/19/1C/2F/6F/AF as a baseline; drive N scripted games with known bet/win ⚙; re-read the whole set |
| Assert | For every one of the 135 implemented codes, `after - before` equals the value computed from the scripted play; `0x00 total coin in` == sum of bets; `0x01 total coin out` == wins + progressive machine-paid + external-bonus machine-paid (`SasNetwork_Meters.hpp:280-297`); `total coin out + total jackpot == total won`; `games played - games won == games lost` **as separately counted, not derived** |
| Why | The 135-lambda table is where every accounting bug lives, and `CHANGELOG.md:44-50` says games-lost was derived until 2.3.3.58 and wrong after a rollover |

**T18 — Accounting-denomination reporting**
| | |
|---|---|
| Pre | `Market.SAS.accounting denomination` set to 5 (or 25), memory cleared to adopt it |
| Host | LP 1F and LP 53; then read every credits-denominated meter and every cents-denominated one |
| Assert | LP 1F and LP 53 report the **accounting denomination**, not the first game's denomination; every meter SAS defines in credits is reported in that unit; ticket, transfer and progressive amounts stay in **cents**; a legacy bonus meter (LP 9A) is in accounting units; sub-unit remainders are truncated once at the top, not per game |
| Why | New in 2.3.3.57 (`CHANGELOG.md:53-63`), touches every meter, is adopted only across a memory clear, and the conversion point is a single line (`SasNetwork_Meters.hpp:1316-1317`) |

**T19 — Illegal accounting denomination is refused**
| | |
|---|---|
| Pre | `accounting denomination` set to a value that is not a Table C-4 code, or that does not divide an offered player denomination |
| Host | Poll; read LP 1F / 53 |
| Assert | The machine reports **1 cent** and the audit page says "not valid for the denominations offered" ⚙ rather than sitting in "requires memory clear"; the link still starts |
| Why | `SasNetwork.cpp:960-1006`, `Audit_SasConfiguration.hpp:190-203` |

**T20 — Meter rollover**
| | |
|---|---|
| Pre | Meters seeded near their SAS field width (8-digit BCD for LP 1C-family, 10-digit for extended) ⚙ |
| Host | Drive play across the boundary; read the meter before, at, and after |
| Assert | The reported value wraps modulo the field width, does not saturate, does not go negative, and does not corrupt an adjacent meter; **games lost stays correct across the wrap**; the extended-meter form (LP 6F/AF) reports the wider value consistently with the narrow form |
| Why | `CHANGELOG.md:44-50` names rollover as the reason games-lost was re-counted rather than derived |

**T21 — Per-game vs master meters, multi-denom**
| | |
|---|---|
| Pre | ≥ 3 games at ≥ 2 distinct denominations; link up |
| Host | Play a known number of games on each; then LP 2F / 52 / 6F / AF for each game index and each denomination, and LP B0 multi-denom preamble wrapping the same polls; also game 0 |
| Assert | Per-game values sum to the master value for each of the 14 per-game codes; a poll for game N with the *wrong* denomination is refused (`QXT_LIB_SAS_INVALID_DENOM_ERR` path, `SasNetwork_Meters.hpp:1292-1294`); game 0 with denom 0 returns the machine total; a game index above the configured count is refused, not answered with zeros |
| Why | The provider's game/denom fan-out (`SasNetwork_Meters.hpp:1286-1310`) has four branches and no test |

**T22 — Unimplemented meter codes**
| | |
|---|---|
| Pre | Link up |
| Host | Poll every meter code 0x00-0xFF individually |
| Assert | The 135 implemented codes answer plausible values; the 121 unimplemented ones answer a defined value (0) rather than stale data, garbage, or a refusal that stalls the link; no code causes a response over 20 ms or a missed poll |
| Why | The table has 256 entries and only 135 lambdas (measured); the empty ones are `continue`-d silently (`SasNetwork_Meters.hpp:1282-1283`) |

### AFT

**T23 — AFT registration handshake**
| | |
|---|---|
| Pre | Non-zero AFT asset number configured; machine unregistered |
| Host | LP 73 registration request with asset number + registration key + POS id; then poll for exception; then trigger the operator acknowledgement ⚙ (`qspAftRegistrationRequest`); then LP 73 read-status |
| Assert | Exception **6C** (registration ready) is reported; after operator acknowledgement, **6D** (registered); LP 73 status reads `RS_GM_REGISTERED`; a registration attempt with **asset number 0** is refused (SAS 6.02 §8.1); `qspGetAftRegistrationStatus` and the LP 73 answer never disagree |
| Why | `SasNetwork.h` (AFT registration block), `Audit_SasConfiguration.hpp:113-118`, `.docs\findings-igs202-aft-registration.md` |

**T24 — Registration cancelled by a configuration change**
| | |
|---|---|
| Pre | Machine registered |
| Host | Change any long-poll-74 status-mask input — e.g. flip `transaction receipts` in the configuration file and restart, or change the asset number ⚙ — then LP 73 |
| Assert | Exception **6E** is reported and the machine reads back unregistered |
| Why | SAS 6.02 §8.1, quoted verbatim in `configuration.json:395-400`; `CancelAftRegistrationOnConfigurationChange` exists but is unexercised |

**T25 — LP 74 answers the machine's *current* state**
| | |
|---|---|
| Pre | Registered machine, link up |
| Host | LP 74 (status only) repeatedly while, between polls, the EGM is put through: idle → game in progress → door open → handpay pending → host-disabled → link recovered ⚙ |
| Assert | `availableTransfers`, `hostCashoutStatus` and `aftStatus` change with the machine's state on the very next poll; a host-disabled or link-down machine offers **no** transfer types even while a cash-out to the host is pending; a pending cash-out keeps *transfer from GM* on offer but refuses to-GM and to-ticket with status **87** |
| Why | SAS 6.02 §8.2 quoted in `SasNetwork.h`; `CHANGELOG.md:451`, `:477` record both directions of this being wrong |

**T26 — AFT transfer in, full and partial**
| | |
|---|---|
| Pre | Registered; known credit meter; `Max Cashless In` configured |
| Host | LP 72 `TT_INHOUSE_AMOUNT_FROM_HOST_TO_GM` for (a) an amount that fits, (b) an amount over `Max Cashless In` with partial **allowed**, (c) the same with partial **not allowed**, (d) an amount that would breach `Max Credit Meter`; poll the transfer status to completion each time |
| Assert | (a) status **00 full transfer successful**, credit meter up by exactly the amount, cashless-in count for the right credit type incremented (`qspGetMarketAuditData(eCashlessTransactionCounts, eCashlessInCount)`); (b) partial status with the transferred amount reported, not the requested one; (c) status **39 amount exceeds GM limit**; (d) refused before any funds move; in every case the amounts reported back equal the amounts actually moved |
| Why | `SasNetwork_AftTransfer.hpp:305-366`; the transferred-vs-requested distinction is held in `fPendingAftRequested` / `fPendingAftTransferred` and re-reported later by a receipt (`SasNetwork.h`) |

**T27 — AFT transfer out, and the 8 s cash-out request timer**
| | |
|---|---|
| Pre | Registered; credits on the meter; `Cashout To Host` = "Always"; `cashout_to_host_hard` clear |
| Host | Press cash-out ⚙; observe exception 6A/6B; then (a) answer with LP 72 `TT_INHOUSE_AMOUNT_FROM_GM_TO_HOST` within 8 s, and (b) in a second run, answer never |
| Assert | (a) full transfer, credit meter to zero, cashless-out count incremented; (b) after **8 s** (`fTimerAftCashoutRequest = 8`, `SasNetwork_Conditions.hpp:885`) the machine falls back — ticket-out if available, else handpay — and **stops** putting the request to the host at that moment, not 8 s later; the fallback is single-shot even if the 6A/6B timeout notification also fires |
| Why | `SasNetwork_AftTransfer.hpp:24-80`; `CHANGELOG.md:76-81` (2.3.3.54) and `:121-127` are both about this timer being wrong |

**T28 — AFT hard mode: no fallback**
| | |
|---|---|
| Pre | `Cashout To Host` = "Always" **and** `cashout_to_host_hard` set |
| Host | Trigger a cash-out ⚙; never answer |
| Assert | The machine raises the cash-out-to-host-failure lockup and **keeps re-issuing the request every second** while it stands (`kHostCashoutReissueSeconds = 1`, `AftCashoutFallback.h:134`); no ticket is printed and no handpay is registered until an attendant intervenes; the moment the attendant chooses another way to pay, the request is withdrawn on the next poll |
| Why | SAS 6.02 §8.7; `CHANGELOG.md:76-81` records the request standing for 8 s after settlement |

**T29 — AFT lock request and its timeout**
| | |
|---|---|
| Pre | Registered; machine idle |
| Host | LP 74 `LC_REQUEST_LOCK` with timeout 0x0064 (10.0 s); poll LP 74 every 200 ms; do not transfer |
| Assert | LP 74 answers `GLS_GAME_LOCK_PENDING` immediately and `GLS_GAME_LOCKED` once the EGM condition is actually up; exception **6F** is reported; the lock releases automatically at **timeout/100 seconds** (`SasNetwork_AftTransfer.hpp:800`, `SasNetwork.cpp:784-787`); a `LC_CANCEL_LOCK` before then releases immediately; a lock requested while a game is in progress does not lock mid-game |
| Why | `SasNetwork_AftTransfer.hpp:796-838` carries a comment that IGT Advantage will not send LP 72 unless 74 says `GLS_GAME_LOCKED` — a field-found interop bug with no regression test |

**T30 — AFT bonus awards, coin-out and jackpot**
| | |
|---|---|
| Pre | Registered; `aftBonusAwards` claimed |
| Host | LP 72 `TT_BONUS_COIN_OUT_AMOUNT_FROM_HOST_TO_GM`; then `TT_BONUS_JACKPOT_WIN_AMOUNT_FROM_HOST_TO_GM` |
| Assert | Coin-out bonus goes to the credit meter and increments the bonus-in count; jackpot bonus registers a **handpay** (`handpay = (transferType == TT_BONUS_JACKPOT_WIN…)`, `SasNetwork_AftTransfer.hpp:370`) and is reported as handpay-pending, not as a credit; the external-bonus meters move and the game meters do not |
| Why | `SasNetwork_AftTransfer.hpp:367-450`; two transfer types share one branch and only differ by one bool |

**T31 — AFT transaction receipt refused when unsupported**
| | |
|---|---|
| Pre | `Market.SAS.transaction receipts` = **false** (the default) |
| Host | LP 72 with the receipt-requested flag set |
| Assert | Status **90** is returned **before any funds move** (credit meter unchanged, no meter moves); with the setting true, the transfer proceeds and the receipt print is attempted |
| Why | Stated as required behaviour in `configuration.json:401-403`; the default ships false so the refusing path is what every machine runs |

### TITO / validation

**T32 — Secure-enhanced validation configuration (LP 4C)**
| | |
|---|---|
| Pre | Validation type = Secure Enhanced; `secure_enhanced_configured` clear (fresh memory) |
| Host | Poll without sending LP 4C for 60 s; then send LP 4C with a machine validation id + sequence number |
| Assert | Before LP 4C: exception **3F ValidationIdNotConfigured** is reported at power-up and every 15 s, **once** per interval, not twice (`SasNetwork.cpp:800-810`); the machine will not print a cash-out ticket; after LP 4C the exception stops, `eValidationRequired` clears, and the machine prints |
| Why | The 15 s re-report was duplicated across two independently-phased timers until the comment at `SasNetwork.cpp:800-806` was written; nothing guards it |

**T33 — Ticket out, print, and validation record readback**
| | |
|---|---|
| Pre | Secure enhanced configured; credits on the meter; printer present |
| Host | Trigger cash-out ⚙; poll for exceptions; LP 3D (send cash-out ticket info) and LP 4D (send enhanced validation info) |
| Assert | The ticket-printed exception is reported; LP 3D/4D return the validation number, amount and sequence number that were actually printed; the validation sequence number increments by exactly 1 per ticket and never repeats; total-cancelled-credits and the ticket-out meters move by the ticket value; the credit meter is zero |
| Why | `SasNetwork_Ticket.hpp:562-700`; the validation number is the anti-fraud primitive |

**T34 — Ticket-out print timeout escalates to handpay**
| | |
|---|---|
| Pre | As T33 but the printer is made to never report print-complete ⚙ |
| Host | Trigger cash-out; poll for **35 s** |
| Assert | After **30 s** (`fTimerTicketOutPrint = 30`, `SasNetwork_Ticket.hpp:773`) the ticket-out is cancelled and escalated to a handpay; the player is never left unpaid; no ticket-printed exception is reported; a handpay-pending exception is |
| Why | `SasNetwork.cpp:788-791`, `EscalateCancelledTicketOutToHandpay` — the money-loss path |

**T35 — Ticket redemption (ticket in), accepted and refused**
| | |
|---|---|
| Pre | Ticket In enabled; validation style secure enhanced; `Max Ticket In` and `Max Credit Meter` configured |
| Host | Insert a ticket ⚙; answer the resulting LP 70/71 with (a) an authorisation for the full amount, (b) an authorisation for a **different** amount, (c) a refusal with each of the SAS reject reasons, (d) no answer at all |
| Assert | (a) credits added exactly once, ticket-in meters move, stacked; (b) handled per spec, not silently accepted; (c) the ticket is returned and the on-screen reason matches the code (`DisplayTicketRejectReason`, `SasNetwork_Ticket.hpp:366-394`); (d) after **30 s** (`fTimerTicketInStack = 30`, `:248`) the redemption is cancelled and the ticket returned; a ticket above `Max Ticket In`, or one that would breach `Max Credit Meter`, is refused before the host is even asked |
| Why | `SasNetwork_Ticket.hpp:47-330`; four independent refusal paths, one timer, and real money |

**T36 — Standard validation prints its own ticket**
| | |
|---|---|
| Pre | Validation type = **Standard**; ticket-in therefore forced off |
| Host | Trigger cash-out ⚙; then send LP 7B withdrawing the printer as the cash-out device; trigger cash-out again |
| Assert | First cash-out prints a ticket with a machine-calculated validation number (SAS §15.14) — **not** a handpay; LP A0 reports `ticketRedemption` **clear**; after LP 7B the machine stops printing; an inserted ticket is always refused |
| Why | `CHANGELOG.md:474`, `:483`; the standard-validation branch regressed to handpay once already, and `enabled_features.ticketRedemption` deliberately reads clear here (`SasNetwork.cpp:285-286`) |

**T37 — Validation buffer full**
| | |
|---|---|
| Pre | Secure enhanced or system validation |
| Host | Cause enough tickets/handpays to fill the validation buffer without ever reading it with LP 3D/4D ⚙ |
| Assert | `INTERNAL_NOTIFICATION_VALIDATION_BUFFER_FULL` raises the inhibiting lockup; a machine on **standard** validation in the same situation does **not** lock up (`SasNetwork_Callbacks.hpp:497-505`); reading the records clears it |
| Why | The comment there says raising it under standard validation would let an uninterested host take the machine out of service by doing nothing |

**T38 — Cash-out pending in buffer (10 s)**
| | |
|---|---|
| Pre | A cash-out validation record sitting unread |
| Host | Withhold LP 3D/4D for 15 s |
| Assert | `INTERNAL_NOTIFICATION_CASHOUT_PENDING_IN_BUFFER` fires at ~10 s and the matching END notification fires once the record is read; the machine's behaviour matches whichever of the two states it is in |
| Why | `SasNetwork_Callbacks.hpp:507-512`; libsas measures it on its own 200 ms timer |

### Handpay

**T39 — Handpay set, report and remote reset**
| | |
|---|---|
| Pre | Link up; a win above the handpay threshold ⚙ |
| Host | Poll for the handpay exception; LP 1B (send handpay info); LP A8 to enable a reset method; LP 94 remote handpay reset |
| Assert | The handpay-pending exception is reported; LP 1B reports the correct level (0x80 for cancel-credits, the progressive level otherwise — `SasNetwork_Conditions.hpp:693-695`), amount, partial-pay and reset id; **LP 1B's reported reset id and LP A8's acceptance agree** — a win above the upper jackpot limit must not be reported as credit-meter-resettable and then accepted; LP 94 resets the handpay and exception **52** is reported; a machine that answered A8 with **01 (unable)** must **refuse** the following 94 |
| Why | `HandpayMayResetToCreditMeter` (`SasNetwork_Conditions.hpp:642-664`) exists precisely because the two answers disagreed; `AsyncLpEnableHandpayResetMethod` takes the acknowledgement code for the same reason (`SasNetwork_Callbacks.hpp:391-396`) |

**T40 — Legacy handpay reporting on/off**
| | |
|---|---|
| Pre | Two runs: `legacy_handpay_reporting` set, then clear |
| Host | Provoke a handpay; poll for 60 s without sending LP 1B |
| Assert | With the flag **clear**, exception **51** is re-issued every 15 s; with it **set**, 51 is sent **once** |
| Why | `SAS_ENABLE_LEGACY_HANDPAY_REPORTING` (`libsas_wrapper.h:77-80`), driven from the audit checkbox (`SasNetwork.cpp:574`); the only libsas configuration parameter QSP sets from operator input |

**T41 — Handpay collected by the host with LP 72**
| | |
|---|---|
| Pre | A jackpot handpay pending |
| Host | LP 72 `TT_WIN_AMOUNT_FROM_GM_TO_HOST` |
| Assert | The handpay is settled by the transfer; **neither** exception 51 nor 52 is emitted for it, and the RESET key does nothing afterwards; the jackpot meter moves and the credit meter does not |
| Why | `CHANGELOG.md:327-330` records this exact case being wrong |

### Progressives

**T42 — Linked progressive broadcast and the per-level 5 s rule**
| | |
|---|---|
| Pre | Two levels configured as `QSP_PROGRESSIVE_LINKED`; **non-zero** Progressive Group ID |
| Host | Broadcast LP 86 for both levels every 2 s for 30 s; then broadcast **only level 1** for 20 s; then resume both |
| Assert | While both are broadcast, no timeout and the game's displayed amounts follow the broadcast values (via `QSP_CALLBACK_LINK_PROGRESSIVE_METERS` ⚙); once level 2 goes stale, exception **53** is reported and the timeout lockup goes up; **the lockup does not clear when level 1 is broadcast again — only when level 2 is** (`SasNetwork_Progressives.hpp:141-180`); it clears when both are fresh |
| Why | SAS 6.02 §10.2 is per level. Clearing on the first broadcast of any level unlocks a machine still showing a frozen jackpot |

**T43 — Progressive group 0 disables the timeout**
| | |
|---|---|
| Pre | Linked levels configured but Progressive Group ID = **0** |
| Host | Send no LP 86 at all for 60 s |
| Assert | No exception 53; no timeout lockup; the machine plays |
| Why | `SasNetwork_Progressives.hpp:44-56` — group 00 is reserved for non-SAS progressives, and a machine left there must not sit in a timeout no broadcast can lift |

**T44 — Level hit and win reporting, both modes**
| | |
|---|---|
| Pre | Two runs: `"progressive level hit at notification"` false, then true |
| Host | Trigger a linked progressive win ⚙; poll; then LP 84/85/87 (send progressive win / send SAS progressive win / send multiple SAS progressive win) |
| Assert | **Default (false)**: nothing at win confirmation; at award, the level hit and the win are reported once, with the awarded (possibly host-capped) amount. **True**: the level hit is announced at confirmation with mode `INHIBITS_JACKPOT_CASHOUT_PAID`; at award only "paid" is reported and the win is **not** queued twice; LP 87 reports the win once in either mode; a handpay-paid progressive registers the handpay rather than crediting |
| Why | `SasNetwork_Progressives.hpp:63-140`; the double-count guard is a single bitmask (`fAnnouncedProgressiveLevelHit`) |

**T45 — Progressive win after link recovery**
| | |
|---|---|
| Pre | Linked progressives, link up |
| Host | Drop the link; trigger a progressive win ⚙; restore the link after 20 s; poll |
| Assert | The win is reported exactly once after recovery; every linked level is marked stale on the drop and the timeout stands until each has been re-broadcast (`SasNetwork_Callbacks.hpp:424-430`); the amount reported is the amount awarded, not a stale broadcast value |
| Why | Combines T05 and T42, which is where a real floor outage lands |

### Machine state, identity, integrity

**T46 — Machine disable / enable (LP 01 / 02)**
| | |
|---|---|
| Pre | Link up, machine playable |
| Host | LP 01 (shutdown); attempt play ⚙; LP 1A; LP 74; LP 02 (startup) |
| Assert | The machine refuses to start a game while disabled but still answers every poll; LP 74 offers **no** transfer types while disabled; the credit meter and every meter are unchanged by the disable itself; LP 02 restores play; the state survives a poll gap but **not** a power cycle unless the spec says so |
| Why | `AsyncLpEgmEnable` (`SasNetwork_Callbacks.hpp:344-350`) is two lines with a large blast radius; `CHANGELOG.md:451` ties it to LP 74 |

**T47 — Game enable / disable (LP 09) including "all games"**
| | |
|---|---|
| Pre | Multi-game configuration |
| Host | LP 09 disable game N; LP 56 (send enabled game numbers); LP 09 with game 0 disable-all; LP 09 with game 0 **enable**-all; LP 09 for a game index beyond the configured count |
| Assert | LP 56 reflects the change; game 0 disable disables every game; **game 0 enable is rejected** (`SasNetwork_Callbacks.hpp:452-456`); an out-of-range index is ignored, not answered with a wrong game; a disabled game cannot be selected ⚙ |
| Why | Three explicit guard clauses, none tested |

**T48 — Maintenance mode (LP 0A / 0B)**
| | |
|---|---|
| Pre | Link up |
| Host | LP 0A; open a door ⚙; LP 0B; open a door again; power-cycle while in maintenance mode |
| Assert | LP 0A: the machine says so on screen, **stays playable**, still takes and pays money, reports **no** exception for entering, and the door alarm is silenced (but the door exception is still reported); LP 0B ends it; a power cycle ends it; taking the machine out of service from the attendant menu is unaffected and still reports exception **86** |
| Why | `SasNetwork_Callbacks.hpp:352-379`; `CHANGELOG.md:275-282`, `:343-347` — it was accepted silently and, before that, wrongly disabled the bill validator |

**T49 — Game delay (LP 2E) — and the timeout in the captured log**
| | |
|---|---|
| Pre | Link up |
| Host | LP 2E with delay 0x0032 (5.0 s) immediately after a game end; attempt to start a game ⚙; then LP 2E with delay **0x0000**; then send LP 2E 100 times back to back |
| Assert | **Every LP 2E is answered** — this is the failure sitting in `progressive_sas.log` (36 sends, 36 `Timeout, EGM poll response`, at lines 88-93 and every ~20 s thereafter); a 5 s delay actually holds the next game for 5 s measured on a **monotonic** clock (so LP 7F cannot shorten or lengthen it — `SasNetwork.cpp:826-833`); delay 0 cancels; a legacy bonus arriving during the delay is escrowed and paid after it (§13.7, `SasNetwork.cpp:851-863`) |
| Why | A reproducible, already-captured, unreported defect. This scenario alone justifies the tool |

**T50 — ROM / program signature (LP 21)**
| | |
|---|---|
| Pre | `Market.rom_signature_targets` = `["Main Storage"]` |
| Host | LP 21 with a known seed; poll for the answer; repeat with a different seed; send a **second** LP 21 while the first is still calculating; run LP 21 while the game also asks for the hash engine ⚙ |
| Assert | The CRC-16 returned is the deterministic chain over the configured targets for that seed and is reproducible run to run; a different seed gives a different answer; a second LP 21 **supersedes** the first (SAS 6.02 §6.1) rather than both cancelling; an interrupted calculation is retried (up to 3 for an interruption, 1 for an unreadable target — `SasRules.h:493`) and a partial hash is **never** registered as a signature; with `rom_signature_targets` empty, the answer comes from the game via `QSP_CALLBACK_ROM_SIGNATURE_16` |
| Why | `CHANGELOG.md:457-458`; the abandoned-without-an-answer bug is exactly a host-visible hang |

**T51 — Machine identity polls**
| | |
|---|---|
| Pre | Known `manufacturer id`, asset number, serial number, game configuration |
| Host | LP 1F (send machine id / info), LP 54 (SAS version + machine serial), LP 53 (game N configuration), LP 51 (total games implemented), LP B5 (extended game N info), LP B7 (set machine numbers), LP A0 (send enabled features) |
| Assert | LP 1F reports the configured 2-char manufacturer id and the **accounting denomination**; LP 54 reports SAS version `"602"` (`libsas_internal.h:218`); LP 53's per-game record matches the configured paytable, denomination, max bet (in credits, `SasNetwork.cpp:1091`) and RTP formatted to 4 places; LP A0's feature bits match §2.2 exactly — including **componentAuthentication = 0** and **tournament = 0** — and do **not** match `C:\IGT3\conf.ini` |
| Why | These are the polls a host uses to decide what else it may send; a wrong A0 makes every later test meaningless. The captured LP 53 response `01 53 00 06 51 58 47 61 6D 01 FF 01 00 00 47 61 6D 65 31 35 39 33 30 30 29 1F` (`progressive_sas.log:74`) contains ASCII `QXGam…Game159300` — a ready-made golden vector |

**T52 — Component authentication is honestly declined**
| | |
|---|---|
| Pre | Link up |
| Host | LP A0 to read the feature bits; then LP 6E (send authentication) regardless |
| Assert | LP A0 reports `componentAuthentication` **clear**; LP 6E is not answered as though it were supported; the link is not disturbed by the attempt |
| Why | `SasNetwork.cpp:290` — `componentAuthentication = 0 // TODO modify when LibQpm supports component authentication`, while `conf.ini` says 1. A host that believes the ini will send 6E |

**T53 — Bill validator control (LP 06 / 07 / 08)**
| | |
|---|---|
| Pre | Bill validator configured with a known denomination set |
| Host | LP 08 with a bill mask enabling only $1 and $20; insert a $5 and a $20 ⚙; LP 07 disable acceptor; insert a note; LP 06 enable |
| Assert | The enabled-bills mask is mapped through the SAS denomination table to the machine's own list (`SasNetwork_Callbacks.hpp:405-436`); $5 is refused and $20 accepted; while disabled no note is accepted at all; the per-denomination bill meters (0x2A-0x45 family) move only for accepted notes; exception `BillRejected`/`CounterfeitBill` is reported for a rejected/cheat note |
| Why | A 32-entry SAS table cross-mapped to a variable machine list, with no test |

**T54 — Date and time (LP 7E / 7F)**
| | |
|---|---|
| Pre | Link up |
| Host | LP 7E (read); LP 7F setting a valid date/time; LP 7E again; LP 7F with a date the machine's timezone cannot represent |
| Assert | LP 7E answers the **machine's own clock**; a valid 7F is adopted as local time and the following 7E reflects it; an unrepresentable 7F is **refused**, not partially applied; a running game delay (T49) is unaffected by either |
| Why | `CHANGELOG.md:334-339`; `Qsp::HostDateTimeToPosix` (`SasRules.h:117`) |

**T55 — Operator changed options (exception 3C)**
| | |
|---|---|
| Pre | A fully configured, running machine |
| Host | (a) Re-save the SAS configuration page with **nothing changed** ⚙; (b) change the poll address; (c) change the validation style; (d) configure a brand-new/memory-cleared machine for the first time |
| Assert | (a) **no** exception; (b) and (c) exception **3C**, and the link restarts cleanly with the second channel still open; (d) **no** exception; in (b)/(c) the exception survives the restart it triggers |
| Why | `CHANGELOG.md:471`, `:388-393`; `Qsp::NetworkConfigurationChanged` / `OperatorChangedOptionsIsOwed` (`SasRules.h:334, 361`) |

**T56 — `start network` modes**
| | |
|---|---|
| Pre | Three runs, one per value of `Market.SAS.start network` |
| Host | Poll from before power-on; drive the configuration and audit-exit sequence ⚙ |
| Assert | `"immediately"`: answers as soon as an address is configured. `"audit exit"`: silent until **every** page is saved **and** the audit menu is left — but a machine whose configuration was **already complete** at power-on answers even if it comes up with the audit key turned (`CHANGELOG.md:96-101`). `"restart"`: silent for the whole configuring session, answers on the next start. In all three, held exceptions (T16) are delivered when the link finally comes up, oldest first |
| Why | Three modes × the already-configured exception; two of the four combinations have been shipped broken |

**T57 — Soak: 24 h unattended**
| | |
|---|---|
| Pre | Full configuration, link up |
| Host | Poll at 200 ms for 24 h, interleaving a randomised but seeded mix of every long poll above, with scripted EGM stimulus ⚙, and a scripted link drop every hour |
| Assert | Zero unanswered polls (other than the deliberate drops); zero responses over 20 ms; meter monotonicity holds throughout; `total_bytes_sent`/`total_bytes_received` in `QspNetworkDiagnosticInfo` match the host's own counters; no memory growth in the EGM process; the run is reproducible from its seed |
| Why | Nothing in the repo has ever run SAS unattended for an hour, let alone a day; the only long capture that exists is `progressive_sas.log`'s 250 seconds |

---

## 7. What this implies for the new tool

1. **Scriptable, unattended, exit-code-bearing.** `sastest.exe` is a Borland C++Builder GUI
   (`strings.exe` shows `TForm1 *`, `TAFT *`, `TAuthentication *`, `TProgressiveInfo *`) driven by a
   human. Every scenario above needs a script, an assertion language, and a pass/fail.
2. **Millisecond-or-better timestamping on both directions.** `sastest.exe`'s `TXRXData.dat` records
   only whole seconds (`Tue Feb 25 09:44:31 2025 / TX>= 81`), which cannot express the 20 ms budget
   at all. `progressive_sas.log` proves the format the tool needs.
3. **Frame decode with named fields.** T51's LP 53 golden vector should be assertable as
   `manufacturer_id == "QX" && game_id == "Game1" && rtp == 93.00` and not as a hex string.
4. **EGM-side stimulus (marked ⚙).** Roughly half the scenarios need a door opened, a note inserted,
   a game played or a keyswitch turned. Without an EGM-side agent — the SAS equivalent of
   `gds_tester.exe` and its `gds_tester_cmd.txt` (`doc\Peripheral_Simulator_Tests.md:66-84`) — these
   stay manual. That agent is the single largest missing piece and is not in scope of the SAS host
   itself, but the host must be able to sequence against it.
5. **Read the EGM's own view.** `qspGetNetworkDiagnosticInfo` gives per-channel link status and byte
   counters (`libqsp_audit.h:392-411`); `qspGetMarketAuditData` gives the cashless transaction
   counts. Cross-checking wire against EGM state catches a whole class of bug the wire alone cannot.
6. **Configuration parity.** The tool must read LP A0 and reconcile it against its own expectations,
   and must be able to express QSP's three configuration layers (§3) so a run states which EGM
   configuration it was valid for — exactly what `doc\Peripheral_Simulator_Tests.md` does by hand for
   peripherals.
7. **A machine-readable run record.** Version of QSP, libsas, QPM, the EGM configuration hash, the
   scenario list, and per-scenario pass/fail — so "which SAS scenarios have been run, against what"
   stops being unanswerable.

---

## 8. Risks

- Half these scenarios cannot run without an EGM-side stimulus agent that does not exist.
- The HAL/simulator path is a dead end for SAS: `src/hal/libsas.cpp` would have to be replaced by a
  real software SAS endpoint before any of this runs off-hardware.
- `progressive_sas.log`'s producer is UNKNOWN; its `Timeout, EGM poll response` semantics
  (what timeout, measured from what) are therefore inferred, not established.
- libsas is pinned to a **branch** (`IGS-226-accounting-denomination`), not a tag
  (`CMakeLists.txt:122`), so an EGM-side behaviour can change under a passing test suite.
- Fifteen of seventeen `eSasEnumConf` parameters run at libsas defaults nobody has read; the tool may
  assert behaviour that is configurable and not configured.
- Real money paths (T26-T31, T33-T36) will move real meters in NVRAM; the harness needs a memory
  clear between runs and must record that it did one.

## 9. Unknowns

- What produced `progressive_sas.log`, and whether that tool still exists.
- libsas' default values for the fifteen unset `SAS_CONF_*` parameters.
- Whether `sastest.exe` can be scripted at all (no command-line strings found; not investigated
  further — outside this brief).
- Whether the 36 LP 2E timeouts are an EGM defect or a host defect; the capture shows only that no
  response arrived.
- Actual SAS field widths per meter code (needed to make T20 precise) — in `doc\sas_full.txt`
  Appendix C, not extracted here.
- Whether `qsp_platform_test` currently passes on a real board.
