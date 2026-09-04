# 04 — libsas surface map (what the new host tool must be able to exercise)

Scope note: this report reads only `C:\Progetti\libsas\include`, `\src`, `\test`, `\examples`,
`CMakeLists.txt` and `CHANGELOG.md`. `utils/sashost`, `utils/poller` and `utils/hwconformance`
were NOT opened, per the hard scope rule. There is **no** `.codegraph/` directory in the repo
(checked: `ls -d C:/Progetti/libsas/.codegraph` → No such file or directory), so everything below
comes from direct reads/greps.

Library version at HEAD: **5.4.4.32** (`include/libsas.h:47-50` — `SAS_MAJOR 5 / SAS_MINOR 4 /
SAS_PATCH 4 / SAS_BUILD 32`). Prebuilt DLL on disk is 5.4.4.27
(`strings bin/Debug/x64/libsas.dll` → `SAS_LIB_VERSION=5.4.4.27`).

---

## 1. Public API surface — EGM side only

`README.md` line 6: *"Quixant Slot Accounting System 6.02 Protocol Library"*.

**libsas is exclusively the EGM / gaming-machine side of the SAS link.** There is no host-side
API anywhere in `include/`. Every long-poll handler in `src/lp.cpp` is a *responder*: it takes an
`sPacket* rxBuffer` that arrived from the host, and calls `sendHostPacket()` / `sendHostNack()` /
`sendHostBusyResponse()`. The library never originates a long poll. That is the whole reason a
separate host tool (sastest.exe today) has to exist.

### Headers and what each is

| Header | Public? | Contents |
|---|---|---|
| `include/libsas.h` (2673 lines) | **yes**, installed as `PUBLIC_HEADER` (`CMakeLists.txt`: `set(LIB_HEADER .../include/libsas.h)`) | All enums + all `SAS_*` legacy entry points |
| `include/libsas_wrapper.h` (671 lines) | **yes**, installed (`install(FILES ... include/libsas_wrapper.h DESTINATION ${QXDIST_DIR}/include)`) | The modern `sas*` API — init, channels, meters, tickets, AFT, handpay, progressives, wager categories, custom polls |
| `include/lp.h` | internal | Prototypes of the ~110 `lpXX_*` long-poll handlers |
| `include/libsas_internal.h` (544 lines) | internal | `internal*` helpers, `sChannelContext`, meter predicates |
| `include/libsas_db.h` (1243 lines) | internal | The NVRAM data model: `sSasContext`, `sExceptionList`, buffers, sizes |
| `include/libsas_uart.h` | internal | `openSasSerialDevice`, `readSasSerial`, `writeSasSerial`, `DEV_NODE = "\\\\.\\QXCOM"` / `/dev/ttyQ` |
| `include/libsas_logger.h`, `libsas_threads.h` | internal | logger + thread plumbing |
| `include/lp56_enabled_games.h` | internal, header-only, state-free | The LP 56 "which games are visible at this denom" predicate, deliberately factored out "so that it can be exercised without a bus" (file comment) |

### ABI: yes, stable flat `extern "C"`, and both STATIC and SHARED are built

`include/libsas.h:2595-2609`:

```c
#if defined(_WIN32)
#ifdef __cplusplus
#define SAS_API extern "C" __declspec(dllexport)
...
#else
#ifdef __cplusplus
#define SAS_API extern "C"
```

Every public function is declared `SAS_API`. `#pragma pack(push, 8)` guards the wrapper structs
("Quixant libraries are built with 8-bytes boundary alignment", `libsas_wrapper.h:7`).

`CMakeLists.txt` builds **both**:

- `add_library(sas_shared SHARED ${SRC_FILES})` → `libsas.dll` / `libsas.so`
- `add_library(sas_static STATIC ${SRC_FILES})` → `libsas.lib` / `libsas.a`
- plus a test-only `sas_static_testable` (full sources, `VALIDATION_DEBUG` + `_DEBUG` defined).

Verified export list from the shipped DLL
(`strings bin/Debug/x64/libsas.dll | grep -E '^sas[A-Z]|^SAS_'`):
**53 `SAS_*` symbols and 65 `sas*` symbols, all undecorated C names.** So **a .NET app can
P/Invoke libsas.dll directly** — no C++ mangling, no COM, only flat C with packed structs.
(Caveat: the struct-by-value parameters — `sSasEnabledFeatures`, `sChannelControl`,
`sCashoutHandpayTicket`, `sProgressiveHandpayData` — need careful `[StructLayout(Pack=8)]`
marshalling, and `sSasEnabledFeatures` is a 19-bit bitfield packed into a word.)

### API grouped by function

**Init / config / channels**
`sasInit(void** handle, const char* serialDeviceName, unsigned char address, sSasEnabledFeatures, callbackHandler)`,
`sasShutdown`, `sasChannelsShutdown`, `sasAddSasChannel`, `sasGetChannelControl`, `sasSetChannelControl`,
`sasGetChannelStatus`, `sasSetFeature`, `sasSetConfigurationParameter` / `sasGetConfigurationParameter`,
`sasSetBusyResponse`, `sasSetAccountingDenom`, `sasSetTokenDenomination`, `sasSetEnableDenomination`,
`sasSelectGameDenomination`, `sasSetSerialCallbacks`, `sasSetMetersProvider`, `sasSetAftLockStatusProvider`,
`sasNvramDataSizeof` / `sasNvramReadVersion` / `sasNvramWriteVersion`,
plus legacy `SAS_SetClientID(Channel)`, `SAS_ConfigureGame`, `SAS_EnableGame`, `SAS_SetSelectedGame`,
`SAS_GameInfo`, `SAS_SetCashoutLimits`, `SAS_SetAssetHouseID`, `SAS_SetFloorLocation`, `SAS_SetAssetControl`,
`SAS_RegisterSerialNumber`, `SAS_RegisterROMSignature` / `sasRegisterROMSignature`, `SAS_LibInventory`.

**Meters** — `sasReadMeter`, `sasWriteMeter`, `sasIncrementMeter`, `sasReadLegacyMeter`,
`sasWriteLegacyMeter`, `sasSetImmediateModeMeter` / `sasGetImmediateModeMeter`,
`SAS_ResetMeters`, `SAS_ChangeMeterStatus`, `SAS_ReadTournamentMeter` / `SAS_WriteTournamentMeter`,
`sasSetMetersProvider` (pull-model callback, up to 24 meters per call — `libsas_wrapper.h:210-218`).

**Exceptions / events** — `SAS_WriteException(handle, code)` (the EGM app raises an arbitrary
exception code), `sasGetStructInfo(..., EXCEPTION_STRUCT, index, sOneException*)` to inspect the
queue, and the RTE producers `SAS_RTE_GameStart`, `SAS_RTE_GameEnd`, `SAS_RTE_ReelStopped`,
`SAS_RTE_CardHeld`, `SAS_RTE_GameSelected`, `SAS_RTE_GameRecallEntry`, `SAS_RTE_SessionStart`,
`SAS_RTE_SessionEnd`. Application notifications arrive via the `callbackHandler(sSASData*)`
registered at `sasInit`.

**AFT** — `sasSetAftRegistrationStatus` / `sasGetAftRegistrationStatus`, `SAS_AFTRegisterRequest`,
`sasSetAftTransferLimit`, `sasUpdateAftTransfer`, `sasAftGmToHost`, `sasAftCancelGmToHost` (new in
5.4.4.28), `SAS_AFTUpdateLock`, `sasSetCurrentAmounts`, `sasGetCustomAftTicketData`,
`sasSetAftLockStatusProvider`. Deprecated: `SAS_AFTUpdateTransfer`, `SAS_AFTUpdatePartialTransfer`.

**Tickets & validation** — `sasStartCashout`, `sasCompleteCashout`, `sasCancelCashout`,
`sasStartTicketRedemption`, `sasCompleteTicketRedemption`, `sasRefuseTicketRedemption`,
`sasSetExtendedValidationStatus`, `sasGetTicketExpiration` / `sasSetTicketExpiration`.

**Handpay** — `sasHandpayStart`, `sasHandpayReset`, `sasHandpayRefuse`,
`sasHandpayConfigureRemoteReset`, `sasHandpayConfigureLegacyReporting`,
`SAS_RegisterHandPay`, `SAS_HandPayReset`, `SAS_ReplyJackpotHandpayReset`.

**Progressives / bonusing** — `sasSetProgressiveLevelWin(handle, level, winAmount,
eProgressiveException mode, sProgressiveHandpayData)` and `SAS_LegacyBonusAward(handle,
multiplier, multipliedWin, taxStatus, bonus, game)`. `SAS_PROGRESSIVE_LEVELS = 33`
(`libsas_db.h:80`), i.e. levels 1..32.
`eProgressiveException { LEVEL_HIT_AND_CASHOUT_PAID=0x00, HANDPAY_PENDING=0x01,
INHIBITS_JACKPOT_CASHOUT_PAID=0xFF }` (`libsas_wrapper.h:180-188`) selects which exceptions fire.

**Wager categories** — `sasCreateWagerCategory`, `sasWriteWageredCredit`,
`sasGetNumberOfWagerCategories`, `sasGetWagerCategoryInfo`, `sasGetCreditWagered`.

**Component authentication** — `sasRegisterComponent`, `sasReplyAuthentication`,
`sasGetComponentAuthentication` (+ deprecated `SAS_RegisterComponent`, `SAS_ReplyAuthentication`).

**Custom long polls** — `sasRegisterCustomPoll(cmd, length, crcRequired, ackRequired,
resetCustomPoll)`, `sasGetCustomPoll`, `sasCustomPollResponse`. `MAX_CUSTOM_POLL = 20`
(`libsas_db.h:135`). At dispatch time the custom table is scanned **before** the built-in switch
(`libsas_threads.cpp:3530-3560`), so a registered custom code shadows a standard LP.

**Logging** — `sasLoggerConfig(handle, isEnabled, filename)`, `sasEnableDisableLogger`,
`sasSetLoggerCallbacks`.

**Multi-channel** — `enum SAS_CHANNELS { SAS_CHANNEL_1..4, SAS_CHANNELS_SUPPORTED }`
(`libsas.h:2263-2271`). Four independent SAS links, each with its own `sChannelControl` bitfield of
category ownership: `generalControl, aft, progressive, legacyBonus, validation,
componentAuthentication, information` (`libsas_wrapper.h:127-137`).

---

## 2. The long-poll dispatch table

The single dispatcher is the switch in **`src/libsas_threads.cpp:3552-3980`** (the packet-decoding
thread). Codes are `enum SasLongPollCode` in **`include/libsas.h:1561-1687`**. Descriptions below
are the library's own, taken from `src/libsas_logger.cpp:200-...` where one exists.

Default case is `default: break;` — an unsupported long poll is **silently ignored**: no answer,
no NACK. That is deliberate (`test/blackbox/CMakeLists.txt:236-238`, IGS-171: *"SAS 6.02 section
4.4 requires an unsupported long poll to be IGNORED — not answered and not NACKed either, since a
NACK still tells the host the poll was understood"*).

| LP | Symbolic name | Handled? | Handler / notes |
|---|---|---|---|
| 0x00 | LP_LIB_INTERNAL_NOTIFICATION | n/a | Not a wire code — the `sSASData.command` value libsas uses for internal notifications to the app |
| 0x01 | LP_SHUTDOWN | yes | `lpXX_EnableDisable` — Shutdown |
| 0x02 | LP_STARTUP | yes | `lpXX_EnableDisable` — Startup |
| 0x03 | LP_SOUNDOFF | yes | `lpXX_EnableDisable` — Sound off |
| 0x04 | LP_SOUNDON | yes | `lpXX_EnableDisable` — Sound on |
| 0x05 | LP_REELSPIN_SOUNDSDISABLE | yes | `lpXX_EnableDisable` — Reel spin / game play sounds disabled |
| 0x06 | LP_ENABLEBILLACCEPTOR | yes | `lpXX_EnableDisable` |
| 0x07 | LP_DISABLEBILLACCEPTOR | yes | `lpXX_EnableDisable` |
| 0x08 | LP_CONFIGUREBILLDENOMINATIONS | yes | `lp08_ConfigureBillDenominations` |
| 0x09 | LP_ENABLE_DISABLE_GAME_N | yes | `lp09_EnableDisableGameN` — **multi-denom aware** |
| 0x0A | LP_ENTERMAINTENANCEMODE | yes | `lpXX_EnableDisable` |
| 0x0B | LP_EXITMAINTENANCEMODE | yes | `lpXX_EnableDisable` |
| 0x0C | LP_SETSOUNDVOLUME | **NO** | Enum exists and the logger decodes it, but there is no `case` in the dispatcher → ignored on the wire |
| 0x0D | LP_PLAYSOUNDNUMBER | **NO** | Same: enum + logger only, no handler |
| 0x0E | LP_ENABLE_DISABLE_RTEVENT | yes | `lp0E_EnableDisableRealTimeEventReporting` — switches exception framing |
| 0x0F | LP_SENDMETERS_10_15 | yes | `lp0F_SendMeters` — meters 10 through 15 |
| 0x10 | LP_SENDTOTALCANCELLEDCREDITS | yes | `lp10_...` → meter `TotalCancelledCredits` (0x04) |
| 0x11 | LP_SENDTOTALCOININ | yes | `lp11_...` → meter 0x00 — **multi-denom aware** |
| 0x12 | LP_SENDTOTALCOINOUT | yes | `lp12_...` → meter 0x01 — **multi-denom aware** |
| 0x13 | LP_SENDTOTALDROP | yes | `lp13_...` → meter `TotalDropIn_Crd` (0x24) |
| 0x14 | LP_SENDTOTALJACKPOT | yes | `lp14_...` → meter 0x02 — **multi-denom aware** |
| 0x15 | LP_SENDGAMESPLAYED | yes | `lp15_...` → meter 0x05 — **multi-denom aware** |
| 0x16 | LP_SENDGAMESWON | yes | `lp16_...` → meter 0x06 — **multi-denom aware** |
| 0x17 | LP_SENDGAMESLOST | yes | `lp17_...` → meter 0x07 — **multi-denom aware** |
| 0x18 | LP_SENDGAMES_FROM_UP_DOOR | yes | `lp18_...` — games since power-up + since slot-door closure |
| 0x19 | LP_SENDMETERS_11_15 | yes | `lp19_SendMeters` |
| 0x1A | LP_SENDCURRENTCREDITS | yes | `lp1A_SendCurrentCredits` |
| 0x1B | LP_SENDHANDPAYINFO | yes | `lp1B_SendHandpayInformation` — also stops the 15 s exception-0x51 resend |
| 0x1C | LP_SENDMETERS | yes | `lp1C_SendMeters` — coin in/out, drop, jackpot, played, won, door, power reset |
| 0x1E | LP_SENDTOTALMETERS | yes | `lp1E_SendBillMeters` — total bill meters (# of bills) |
| 0x1F | LP_SENDMACHINEID_INFO | yes | `lp1F_SendGamingMachineIdAndInformation` |
| 0x20 | LP_SENDTOTALDOLLARVALUE | yes | `lp20_SendTotalDollarValueOfBills` |
| 0x21 | LP_ROMVERIFICATION | yes | `lp21_RomSignature` — also pre-empts the general-poll exception path while a signature is pending (`libsas_threads.cpp:3480-3520`) |
| 0x2A | LP_SENDTRUECOININ | yes | `lp2A_SendTrueCoinIn` |
| 0x2B | LP_SENDTRUECOINOUT | yes | `lp2B_SendTrueCoinOut` |
| 0x2C | LP_SENDCURRENTHOPPERLEVEL | yes | `lp2C_SendCurrentHopperLevel` |
| 0x2D | LP_SENDTOTALHANDPAYCANCELLED | yes | `lp2D_...`; config flag `SAS_CONF_2D_NOT_MULTIGAME` changes its game scoping |
| 0x2E | LP_DELAYGAME | yes | `lp2E_GameDelay` |
| 0x2F | LP_SENDMETERSFORGAME_N | yes | `lp2F_SendSelectedMetersForGameN` — **max 10 meter codes per poll** (`lp.cpp:1830-1836`), validated via `isValidMeter` / `isMultigameMeter` / `isMultidenomMeter`; **multi-denom aware** |
| 0x31 | LP_SEND1BILLSINMETER | yes | meter 0x40 |
| 0x32 | LP_SEND2BILLSINMETER | yes | meter 0x41 |
| 0x33 | LP_SEND5BILLSINMETER | yes | meter 0x42 |
| 0x34 | LP_SEND10BILLSINMETER | yes | meter 0x43 |
| 0x35 | LP_SEND20BILLSINMETER | yes | meter 0x44 |
| 0x36 | LP_SEND50BILLSINMETER | yes | meter 0x46 |
| 0x37 | LP_SEND100BILLSINMETER | yes | meter 0x47 |
| 0x38 | LP_SEND500BILLSINMETER | yes | meter 0x4A |
| 0x39 | LP_SEND1000BILSSINMETER | yes | meter 0x4B |
| 0x3A | LP_SEND200BILLSINMETER | yes | meter 0x48 |
| 0x3B | LP_SEND25BILLSINMETER | yes | meter 0x45 |
| 0x3C | LP_SEND2000BILLSINMETER | yes | meter 0x4C |
| 0x3D | LP_SENDCASHOUTTICKETINFO | yes | `lp3D_SendCashoutTicketInformation` |
| 0x3E | LP_SEND2500BILLSINMETER | yes | meter 0x4D |
| 0x3F | LP_SEND5000BILLSINMETER | yes | meter 0x4E |
| 0x40 | LP_SEND10000BILLSINMETER | yes | meter 0x4F |
| 0x41 | LP_SEND20000BILLSINMETER | yes | meter 0x50 |
| 0x42 | LP_SEND25000BILLSINMETER | yes | meter 0x51 |
| 0x43 | LP_SEND50000BILLSINMETER | yes | meter 0x52 |
| 0x44 | LP_SEND100000BILLSINMETER | yes | meter 0x53 |
| 0x45 | LP_SEND250BILLSINMETER | yes | meter 0x49 |
| 0x46 | LP_SENDCREDITACCEPTED | yes | `lp46_...` — credit amount of all bills accepted |
| 0x47 | LP_SENDCOINEXTERNALACCEPTOR | yes | `lp47_...` |
| 0x48 | LP_SENDLASTACCEPTEDBILLINFO | yes | `lp48_...` |
| 0x49 | LP_SENDNUMBEROFBILLINSTACKER | yes | `lp49_...` |
| 0x4A | LP_SENDCREDITINTHESTACKER | yes | `lp4A_...` |
| 0x4C | LP_SETSECUREENHANCEDVALID | yes | `lp4C_SetSecureEnhancedValidationId` — sets validation ID + sequence number |
| 0x4D | LP_SENDENHANCEDVALIDATIONINFO | yes | `lp4D_SendEnhancedValidationInfo` |
| 0x4F | LP_SENDCURRENTHOPPERSTATUS | yes | `lp4F_...` |
| 0x50 | LP_SENDVALIDATIONMETERES | yes | `lp50_SendValidationMeters` — the 0x00C2..0x00D9 "Lp50*" meter block |
| 0x51 | LP_SENDTOTALGAMESIMPLEMENTED | yes | `lp51_...` |
| 0x52 | LP_SENDGAME_N_METERS | yes | `lp52_SendGameNMeters` |
| 0x53 | LP_SENDGAME_N_CONFIGURATION | yes | `lp53_...`; `SAS_CONF_53_MAXBET` alters the max-bet field |
| 0x54 | LP_SENDSASVERID_MACSERIAL | yes | `lp54_...` — SAS version ID + machine serial |
| 0x55 | LP_SENDSELECTEDGAMENUMBER | yes | `lp55_...` |
| 0x56 | LP_SENDENABLEGAMENUMBERS | yes | `lp56_SendEnabledGameNumbers` — **multi-denom aware**; narrowed to the current player denomination since 5.4.4.25 |
| 0x57 | LP_SENDPENDINGCASHOUTINFO | yes | `lp57_...` |
| 0x58 | LP_RECEIVEVALIDATION | yes | `lp58_ReceiveValidationNumber` |
| 0x6E | LP_SENDAUTHENTICATION | yes | `lp6E_SendAuthenticationInfo` — gated by `COMPONENT_AUTHENTICATION_BIT` (`lp.cpp:3702`) |
| 0x6F | LP_SENDEXTENDEDMETERS_N | yes | `lp6F_SendExtendedMetersForGameN` — **multi-denom aware** |
| 0x70 | LP_SENDTICKETVALIDATION | yes | `lp70_SendTicketValidationData` — 10 s window from ticket insertion |
| 0x71 | LP_REDEEMTICKET | yes | `lp71_RedeemTicket` — 30 s window; `SAS_DISABLE_IMPLNACK_LP71` config; `LP71_COMMAND_MAX_LENGTH = 3 + 0x2D` |
| 0x72 | LP_AFT_TRANSFERFUNDS | yes | `lp72_TransferFunds` — gated by `AFT_BIT` (`lp.cpp:5238`) and `channelControl.aft` |
| 0x73 | LP_AFT_REGISTERGAMING | yes | `lp73_RegisterGamingMachine` — gated by `AFT_BIT` (`lp.cpp:6147`) |
| 0x74 | LP_AFT_LOCKANDSTATUS | yes | `lp74_LockAndStatusRequest` — gated by `AFT_BIT` (`lp.cpp:6404`) |
| 0x75 | LP_AFT_SETRECEIPT_DATA | yes | `lp75_SetAftReceiptData` — gated by `AFT_BIT` (`lp.cpp:6690`) |
| 0x76 | LP_AFT_SETCUSTOMTICKET | yes | `lp76_SetCustomAftTicketData` — gated by `AFT_BIT` (`lp.cpp:6825`) |
| 0x7B | LP_EXTENDEDVALIDATION | yes | `lp7B_ExtendedValidationStatus`; `SAS_CONF_7B_AUTO_REPLY`, `SAS_CONF_7B_PREVIOUS_STATE`, `MASK_TRIGGER_STATUSCONTROLBIT` |
| 0x7C | LP_SETEXTENDEDTICKET | yes | `lp7C_SetExtendedTicketData` |
| 0x7D | LP_SETTICKETDATA | yes | `lp7D_SetTicketData` |
| 0x7E | LP_SENDDATEANDTIME | yes | `lp7E_SendCurrentDateAndTime` — machine local time, not UTC (5.4.4.24) |
| 0x7F | LP_RECEIVEDATEANDTIME | yes | `lp7F_ReceiveDateAndTime` |
| 0x80 | LP_RECEIVEPROGRESSIVEAMOUNT | yes | `lp80_SingleLevelProgressive` — broadcast; arms the 5 s exception-0x53 timer |
| 0x83 | LP_SENDCUMULATIVEWINS | yes | `lp83_SendCumulativeProgressiveWins` |
| 0x84 | LP_SENDPROGRESSIVEWIN | yes | `lp84_SendProgressiveWinAmount` |
| 0x85 | LP_SENDSASPROGRESSIVEWIN | yes | `lp85_SendSasProgressiveWinAmount` |
| 0x86 | LP_RECEIVEPROGRESSIVELEVELS | yes | `lp86_MultipleLevelProgressive` — multi-level broadcast |
| 0x87 | LP_SENDMULTIPLESASPROGWIN | yes | `lp87_SendMultipleSasProgressiveWinAmount` — advertised by `PROGRESSIVE_WIN_BIT` |
| 0x8A | LP_INITALEGACYBONUSPAY | yes | `lp8A_InitiateLegacyBonusPay` — gated by `LEGACY_BONUS_AWARDS_BIT` (`lp.cpp:8546`); busy-response option `BR_LP8A` |
| 0x8B | LP_INITMULTIPLEJACKPOT | yes | `lp8B_InitiateMultipliedJackpot` — gated by `LEGACY_BONUS_AWARDS_BIT` **and** `JACKPOT_MULTIPLIER_BIT` (`lp.cpp:8607-8609`) |
| 0x8C | LP_ENTEREXITTROURNAMENT | yes | `lp8C_EnterExitTournamentMode` — gated by `TOURNAMENT_BIT` (`lp.cpp:8727`) |
| 0x8E | LP_SENDCARDINFO | yes | `lp8E_SendCardInformation` |
| 0x8F | LP_SENDREELSTOPINFO | yes | `lp8F_SendPhysicalReelStopInformation` |
| 0x90 | LP_SENDLEGACYBONUSWIN | yes | `lp90_SendLegacyBonusWinAmount` — gated by `LEGACY_BONUS_AWARDS_BIT` (`lp.cpp:8889`) |
| 0x94 | LP_REMOTEHANDPAYRESET | yes | `lp94_RemoteHandpayReset`; busy-response option `BR_LP94` |
| 0x95 | LP_SENDTOURNAMENTGAMEPLAYED | yes | `lp95_...` — tournament meter 0x0000 |
| 0x96 | LP_SENDTOURNAMENTGAMEWON | yes | `lp96_...` — tournament meter 0x0001 |
| 0x97 | LP_SENDTOURNAMENTCREWAGERED | yes | `lp97_...` — tournament meter 0x0002 |
| 0x98 | LP_SENDTOURNAMENTCREWON | yes | `lp98_...` — tournament meter 0x0003 |
| 0x99 | LP_SENDMETERS95_98 | yes | `lp99_SendMeters95Through98` |
| 0x9A | LP_SENDLEGACYBONUSMETERS | yes | `lp9A_SendLegacyBonusMeters` — gated by `LEGACY_BONUS_AWARDS_BIT` (`lp.cpp:9216`) |
| 0xA0 | LP_SENDENABLEFEATURES | yes | `lpA0_SendEnabledFeatures` — returns `features[0..2]`, **masked per channel** by `maskEnabledFeaturesForChannel` |
| 0xA4 | LP_SENDCASHOUTLIMIT | yes | `lpA4_SendCashoutLimit` — requires `channelControl.information` |
| 0xA8 | LP_ENABLEJACKPOTHANDPAYRESET | yes | `lpA8_EnableJackpotHandpayResetMethod` |
| 0xAA | LP_ENABLEDISABLEGAMEAUTOREBET | yes | `lpAA_EnableDisableGameAutoRebet`; ignored entirely when `SAS_DISABLE_AUTO_REBET` is set |
| 0xAF | LP_SENDEXTENDEDMETERSFORGAMEN | yes | `lpAF_SendExtendedMetersForGameN` — **multi-denom aware**; variable BCD width via `getExtendedMeterSize`, or fixed 8 bytes with `SAS_ENABLE_FIX_BCD_SIZE` |
| 0xB0 | LP_MULTIDENOMPREAMBLE | yes | `lpB0_MultiDenomPreamble` — gated by `MULTI_DENOM_BIT` (`lp.cpp:9731`); wraps a base command, see below |
| 0xB1 | LP_SENDNOWPLAYERDENOMINATION | yes | `lpB1_SendCurrentPlayerDenomination` |
| 0xB2 | LP_SENDENABLEPLAYERSDENOMINA | yes | `lpB2_SendEnabledPlayerDenominations` |
| 0xB3 | LP_SENDTOKENDENOMINATION | yes | `lpB3_SendTokenDenomination` |
| 0xB4 | LP_SENDWAGERCATEGORYINFO | yes | `lpB4_SendWagerCategoryInformation` |
| 0xB5 | LP_SENDEXTENDEDGAMENINFO | yes | `lpB5_SendExtendedGameNInformation` — **multi-denom aware**; `SAS_ENABLE_B5_PLAYER_DENOMS` appends a non-standard player-denom block |
| 0xB6 | LP_METERCOLLECTSTATUSCMD | yes | `lpB6_MeterCollectStatus` — gated by `METER_CHANGE_NOTIFICATION_BIT` (`lp.cpp:10729`) |
| 0xB7 | LP_SETMACHINENUMBERS | yes | `lpB7_SetMachineNumbers` — asset + floor location, gated by `SAS_SetAssetControl` |
| 0xFF | LP_EVENTRESPONSETOLONGPOLL | n/a | Not dispatched; it is the RTE message identifier byte in the `[addr][FF][code][data][crc][crc]` frame |

**Multi-denom preamble sub-table (LP 0xB0)** — `src/lp.cpp:9781-10010`. Only these base commands
are multi-denom aware: **0x09, 0x11, 0x12, 0x14, 0x15, 0x16, 0x17, 0x2F, 0x56, 0x6F, 0xAF, 0xB5**.
Everything else in the enumerated list answers error `MDPEC_NOT_MULTI_DENOM_AWARE_LP (3)`; anything
not enumerated at all answers `MDPEC_LP_NOT_SUPPORTED_OR_IGNORED (1)`. Error codes
(`libsas.h:2139-2145`): 1 = not supported/ignored, 2 = improperly formatted, 3 = not multi-denom
aware, 4 = not supported in that format for that denom, 5 = not a valid player denom.
Priority order is documented inline at `lp.cpp:9773-9780`.

**Custom polls** shadow the table: `libsas_threads.cpp:3532-3560` scans
`sasContext.nvramData.customPoll[MAX_CUSTOM_POLL]` first and, on a hit, calls `clpXX_sendToUser()`
instead of the switch.

---

## 3. Feature flags

`C:\IGT3\conf.ini` is **byte-identical** to `C:\Progetti\libsas\include\conf.ini` and
`C:\Progetti\libsas\src\conf.ini` (verified with `diff`, all three match). The file is **not read
by the library** — only by the example EGM app (`examples/example_sas.cpp:1354-1389`, via
`INIReader`, with a menu entry "Reload features from \"conf.ini\" file" at
`examples/example_menu.cpp:193`). Its presence in `C:\IGT3` means the libsas *example EGM* has been
run in that folder against sastest.exe — i.e. `C:\IGT3` is the existing host-vs-EGM test rig.

The flags map 1:1 onto `sSasEnabledFeatures` (`include/libsas_wrapper.h:105-126`), a 19-bit
bitfield passed by value into `sasInit`. `sasInit` packs them into the three feature bytes at
`src/libsas_wrapper.cpp:248-269` (identical code in `sasSetFeature`, `libsas_wrapper.cpp:1620-1640`).

| conf.ini key | Byte.bit | Mask (`libsas.h:182-207`) | What it gates on the wire |
|---|---|---|---|
| `jackpotMultiplier` | F1 b0 | `JACKPOT_MULTIPLIER_BIT 0x01` | LP 8B refused when clear (`lp.cpp:8609`) |
| `aftBonusAwards` | F1 b1 | `AFT_BONUS_AWARDS_BIT 0x02` | Advertised in LP A0; cleared per channel when `channelControl.aft` is off |
| `legacyBonusAwards` | F1 b2 | `LEGACY_BONUS_AWARDS_BIT 0x04` | LP 8A, 8B, 90, 9A all return early when clear (`lp.cpp:1682, 8546, 8607, 8889, 9216`; `libsas.cpp:2548`) |
| `tournament` | F1 b3 | `TOURNAMENT_BIT 0x08` | LP 8C returns early when clear (`lp.cpp:8727`); `isTournamentEnabled` at `lp.cpp:191` gates the 95–99 tournament meters. Covered by `bb_tournament_unsupported` / `bb_tournament_supported` |
| `validationExtensions` | F1 b4 | `VALIDATION_EXTENSIONS_BIT 0x10` | Restricted-promotional ticket acceptance in LP 71 (`lp.cpp:4588`) |
| `validationStyleSystem` | F1 b5 | `VALIDATION_STYLE_SYSTEM` (`CASHOUT_VALIDATION_SYSTEM<<5`) | Cashout validation style. `sasInit` rejects system+secure both set (`libsas_wrapper.cpp:151-153` → `QXT_LIB_SAS_INVALID_PARAMETER`) |
| `validatonStyleSecureEnhanced` (sic) | F1 b6 | `VALIDATION_STYLE_SECURE_ENHANCED` | Enables the 4C/4D secure-enhanced flow |
| `ticketRedemption` | F1 b7 | `TICKET_REDEMPTION_BIT 0x80` | Ticket redemption path refused when clear (`libsas_internal.cpp:5095`) |
| `meterModelWonCreditsMeteredWhenWon` | F2 b0 | `METER_MODEL_WON_CREDITS_METERED_WHEN_WON 0x01` | Meter accrual model; mutually exclusive with the next (rejected at init) |
| `meterModelWonCreditsMeteredWhenPlayedOrPaid` | F2 b1 | `..._WHEN_PLAYED_OR_PAID 0x02` | idem |
| `ticketsToTotalDropAndTotalCancelledCredits` | F2 b2 | `TICKET_TOTAL_DROP_BIT 0x04` | Whether tickets roll into meters 0x24 / 0x04 |
| `extendedMeters` | F2 b3 | `EXTENDED_METERS_BIT 0x08` | **Always forced on** — `sasInit` ORs the bit in unconditionally (`libsas_wrapper.cpp:261`), so the conf.ini value is ignored |
| `componentAuthentication` | F2 b4 | `COMPONENT_AUTHENTICATION_BIT 0x10` | LP 6E returns early when clear (`lp.cpp:3702`); `libsas_internal.cpp:5624` |
| `advancedFundsTransfer` | F2 b6 | `AFT_BIT 0x40` | LP 72/73/74/75/76 all return early when clear (`lp.cpp:5238, 6147, 6404, 6690, 6825`) |
| `multiDenomExtensions` | F2 b7 | `MULTI_DENOM_BIT 0x80` | LP B0 returns immediately when clear (`lp.cpp:9731`), plus `lp.cpp:10085, 10163` |
| `maximumPollingRate` | F3 b0 | `MAX_POLLING_RATE_BIT 0x01` | **Always forced on** (`libsas_wrapper.cpp:267`) — 40 ms polling always advertised |
| `multipleSasProgressiveWinReporting` | F3 b1 | `PROGRESSIVE_WIN_BIT 0x02` | **Always forced on** (`libsas_wrapper.cpp:268`); cleared per channel when `channelControl.progressive` is off |
| `meterChangeNotification` | F3 b2 | `METER_CHANGE_NOTIFICATION_BIT 0x04` | LP B6 returns early when clear (`lp.cpp:10729`); gates exceptions 0xA0/0xA1 |
| `sessionPlay` | F3 b4 | `SESSION_PLAY_BIT 0x10` | Session play; gates exceptions 0x9E/0x9F |

**Per-channel masking of LP A0** — `maskEnabledFeaturesForChannel` (`src/libsas_utils.cpp:1216-1241`):
a channel that does not own a category answers as though the feature did not exist.
`legacyBonus` off → clears 0x01|0x04 in F1; `aft` off → clears 0x02 in F1 and 0x40 in F2;
`validation` off → clears 0x10|0x60|0x80 in F1; `componentAuthentication` off → clears 0x10 in F2;
`progressive` off → clears 0x02 in F3. The header comment there is the canonical list of which LPs
each category owns (aft → 72/74; legacyBonus → 8A/8B; validation → 70/71/4C/4D/57/7B/7C/7D;
progressive → 80/86; componentAuthentication → 6E).

**Behaviour knobs beyond the feature bits** — `eSasEnumConf` (`include/libsas_wrapper.h:61-91`),
set with `sasSetConfigurationParameter`: `SAS_CONF_2D_NOT_MULTIGAME`, `SAS_CONF_7B_AUTO_REPLY`,
`SAS_CONF_7B_PREVIOUS_STATE`, `SAS_CONF_CELEBRATION_WIN`, `SAS_CONF_53_MAXBET`,
`SAS_CONF_SERIAL_TX_BYTE`, `SAS_CONF_72_USER_NOTIFY`, `SAS_CONF_PARTIAL_PAY`,
`SAS_CONF_LP_IMP_ACK_NACK`, `SAS_PROGRESSIVE_T_AT_STARTUP`, `SAS_NACK_RTE`,
`SAS_DISABLE_IMPLNACK_LP71`, `SAS_ENABLE_FIX_BCD_SIZE`, `SAS_ENABLE_LEGACY_HANDPAY_REPORTING`,
`SAS_ENABLE_B5_PLAYER_DENOMS`, `SAS_DISABLE_AUTO_REBET`. Several visibly change the wire (fixed
8-byte BCD, one-shot vs 15 s exception-0x51 resend, extra LP B5 block, LP AA silence), so the host
tool must be able to tolerate and verify both settings of each.

Busy responses: `eBusyResponse { NO_BR, BR_LP8A=1, BR_LP94=2, BR_LP09=4, BR_LP72=8, BR_ALL=0xFF }`
(`libsas_wrapper.h:196-203`) — those four long polls can answer "busy" instead of a result.

---

## 4. Meters

**Model.** Meters live in the NVRAM context as a dense 2-D array:
`unsigned long long cached_meters[SAS_METERS_QUANTITY][SAS_DENOMINATIONS_QUANTITY]`
(`include/libsas_db.h:638`), with `SAS_METERS_QUANTITY = 258` (`:76`, comment: *"added two meters
0x00C0 and 0x00C1"*) and `SAS_DENOMINATIONS_QUANTITY = 32` (`:78`). Index 0 of the denomination
axis is the accounting/max-bet slot, never a playable denomination
(`lp56_enabled_games.h:139-140`). Meters may instead be **pulled from the application** through
`sasSetMetersProvider` — a callback reading up to 24 meters per call (`libsas_wrapper.h:210-218`),
routed via `internalReadMeters` (`libsas_internal.h:433`).

**Codes.** `enum SasMeterCode` (`include/libsas.h:730-1520`) defines **196 named meter codes**
across 0x0000–0x00FF. Blocks:

- 0x00–0x39 core accounting (coin in/out, jackpot, handpay, games, tickets, electronic transfers)
- 0x3E–0x3F stacker
- 0x40–0x57 bills accepted by denomination; 0x58–0x62 bills to drop; 0x63–0x6D diverted to hopper; 0x6E–0x78 dispensed from hopper
- 0x79 SessionsPlayed, 0x7A TipMoney, 0x7F WeightedAverageTheoreticalPaybackPercentage
- 0x80–0x93 ticket in/out and validated-handpay cents/qty pairs
- 0xA0–0xB1 AFT in-house/debit/bonus transfers to machine and to ticket
- 0xB8–0xBD AFT transfers to host
- 0xC0 NumberSlotDoorOpened, 0xC1 NumberACPowerLost (Quixant additions; explicitly *rejected* in LP 2F, `lp.cpp:1869-1871`)
- 0xC2–0xD9 the "Lp50*" validation-meter block returned by LP 50
- 0xFA–0xFF keyed-on / keyed-off funds

Separate tiny enums: `SasTournamentMeterCode {0..3}` (`libsas.h:1522-1527`) and
`SasLegacyBonusMeterCode { LegacyDeductible, LegacyNondeductible, LegacyWagerMatch }` (`:1530-1534`).

**Decode rules the host must replicate** (all in `src/libsas_utils.cpp`):

- `isValidMeter(meter)` (`:1020-1032`) — a *gap list*: codes in 0x3A–0x3D, 0x7B–0x7E, 0x94–0x9F,
  0xB2–0xB7, 0xBE–0xBF, 0xDA–0xF9 and anything ≥ 258 are invalid.
- `getMeterMinimumSize(meter)` (`:163-181`) — **5 BCD bytes** for the "cents" meters
  (0x0D–0x10, 0x80, 0x82, 0x84, 0x86, 0x88, 0x8A, 0x8C, 0x8E, 0x90, 0x92, 0xA0, 0xA2, 0xA4, 0xA6,
  0xA8, 0xAA, 0xAC, 0xAE, 0xB0, 0xB8, 0xBA, 0xBC); **4 BCD bytes** for everything else.
  This is SAS 6.02 Table C-7 "Min Size".
- `getExtendedMeterSize(meter, value)` (`:190-...`) — for LP 6F/AF the width grows 4→5→6→7→8 by
  magnitude, unless `SAS_ENABLE_FIX_BCD_SIZE` forces a flat 8.
- `isMultigameMeter(meter)` (`:972-990`) — only 0x00, 0x01, 0x02, 0x05, 0x06, 0x07, 0x1C, 0x1D,
  0x1E, 0x1F, 0x20, 0x21, 0x22, 0x7F may be requested for a game number > 0.
- `isMultidenomMeter(meter)` (`:1002-1010`) — 0x00–0x02, 0x05–0x07, 0x1C–0x22 only, behind an LP B0 preamble.

**Meter long polls.** Single-meter: 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x1A, 0x20,
0x2A, 0x2B, 0x2C, 0x2D, 0x31–0x3C / 0x3E–0x45 (bills), 0x46, 0x47, 0x49, 0x4A.
Multiple/grouped: 0x0F (10–15), 0x18, 0x19 (11–15), 0x1C, 0x1E, 0x50 (validation meters),
0x52 (game N), 0x99 (95–98), 0x9A (legacy bonus).
Selected: **0x2F** (up to 10 meter codes, 4/5-byte BCD each).
Extended: **0x6F** and **0xAF** (variable-width BCD). All of 0x11/0x12/0x14/0x15/0x16/0x17/0x2F/0x6F/0xAF
also reachable behind the 0xB0 preamble.
Meter modes: `eMeterMode { DEFAULT_AUTO, IMMEDIATE_AUTO, DEFAULT_MANUAL, IMMEDIATE_MANUAL,
FULL_MANUAL }` (`libsas_wrapper.h:189-195`) — in DEFAULT_* modes a multi-denom 2F response is
scaled by denomination (`lp.cpp:1899-1903`).

---

## 5. Exceptions

**Queue.** `sExceptionList { sException items[EXCEPTION_BUFFER_LENGTH]; unsigned char countE; }`
(`include/libsas_db.h:776-779`), one per channel: `sExceptionList exceptionBuffer[SAS_CHANNELS_SUPPORTED]`
(`:864`). `EXCEPTION_BUFFER_LENGTH = 64`, `EXCEPTION_DATA_SIZE = 20` (`:145-146`).
Each entry is `{ code, length, data[20], isSent }` (`:769-774`).
Overflow is a tri-state per channel: `STATE_EXCEPTION_BUFFER { NORMAL, OVERFLOW, OVERFLOW_NOTIFIED }`
(`:349-353`), reported as exception `0x70 ExceptionBufferOverflow`.

**Reporting.** `src/libsas_threads.cpp:3325-3470`, on a general poll (`address|0x80`) or an RTE poll:

- **RTE off**: one raw code byte per poll. Exceptions **0x87, 0x88, 0x89, 0x8B are suppressed**
  entirely in non-RTE mode (`libsas_threads.cpp:3396-3402`). Empty queue → `0x00 NoActivity`.
- **RTE on** (enabled by LP 0x0E): frame is `[address][0xFF][code][data...][crc][crc]`
  (`RealTimeEventMessageIdentifier = 0xFF`). With the queue empty and RTE on the EGM stays
  **silent** on 80/81 general polls — no `0x00` (IGS-198, pinned by `bb_rtenoactivity`,
  `test/blackbox/CMakeLists.txt:281-296`).
- `BillAccepted (0x4F)` is rewritten to the denomination-specific code 0x47–0x50 on dequeue
  (`libsas_threads.cpp:3358-3392`), driven by `BILL_DENOM_*`.
- Implied ACK/NACK: `internalShouldArmExceptionDisposal(...)` decides when the exception is
  disposed; the state machine constants are `IMPLAN_*` (`libsas_db.h:106-130`), 20 events × 4 states.
- ROM signature pre-empts the exception path: while `isPendingRomSignature`, the general poll is
  answered with an LP 0x21 ROM-verification frame instead (`libsas_threads.cpp:3474-3520`).

**Codes.** `enum SasExceptionCode` (`include/libsas.h:233-728`) defines **122 codes**, 0x00 and
0x11–0xA2 plus 0xFF. The ones libsas raises **by itself** (grep `internalWriteException` across
`src/`): `ValidationIdNotConfigured (0x3F)`, `CashoutTicketPrinted (0x3D)`,
`HandpayIsPending (0x51)`, `HandpayWasReset (0x52)`, `NoProgressiveInfoReceived (0x53)`,
`PlayerCancelledHandpayRequest (0x55)`, `SASProgressiveLevelHit (0x56)`,
`SystemValidationRequest (0x57)`, `TicketTransferComplete (0x68)`, `AFTTransferComplete (0x69)`,
`AFTRegistrationCancelled (0x6E)`, `GameLocked (0x6F)`, `ExceptionBufferOverflow (0x70)`,
`ComponentListChanged (0x8E)`, `AuthenticationComplete (0x8F)`, plus the AFT cashout-request
exceptions 0x6A/0x6B via the `aftException` variable (`libsas_threads.cpp:2675`).
Everything else is raised by the EGM application through `SAS_WriteException(handle, code)`
(`libsas.cpp:697-702`) — so the host tool must be prepared for **any** of the 122.
RTE-only producers with payload: `GameStarted (0x7E)`, `GameEnded (0x7F)`, `ReelNStopped (0x88)`,
`CardHeldOrNotHeld (0x8B)`, `GameSelected (0x8C)`, `GameRecallEntryDisplayed (0x8A)`,
`PlayerRequestedDrawCards (0x87)`, `CoinOrCreditWagered (0x89)`, `SessionStarted (0x9E)`,
`SessionEnd (0x9F)` — via the `SAS_RTE_*` API.

Timing behaviours the host must tolerate: 0x51 re-issued every 15 s until LP 1B
(`bb_handpayresend`); 0x53 five seconds after the last progressive broadcast, once per loss of
comms, re-armed by a fresh broadcast (`bb_progtimeout`).

---

## 6. Tests — and the hardware-free EGM question

**Framework**: GoogleTest, pinned `release-1.12.1`, fetched by `FetchContent` in
`test/CMakeLists.txt` (and re-declared early in the root `CMakeLists.txt` so libnvram's own
unpinned `main` declaration cannot win the race). Driven by CTest.

**Counts** (`grep -cE '^\s*TEST(_F)?\(' test/whitebox/*.cpp`):

- `whitebox_test.cpp` — **343** cases
- `lp56_enabled_games_test.cpp` — 10
- `accounting_denom_test.cpp` — 10
- `rte_implied_ack_test.cpp` — 6
- `provider_lock_test.cpp` — 2
- **371 white-box gtest cases total**, linked against `sas_static_testable`.
- Black-box: one `blackbox_test` executable with **18 scenarios**
  (`cirs45, aftgmcashout, aftpartial, handpay, handpayqueue, legacyhandpay, gameorder, systemtime,
  syncpolllink, syncgate, syncnack, syncchirp, linkdown30s, tournament, ticketttl, progtimeout,
  handpayresend, rtenoactivity` — `blackbox_test.cpp:4263-4297`), registered as **≈35 CTest cases**
  because `gameorder` is instantiated 8× (poll rate × RTE) plus 8 burst variants, `tournament` 2×,
  `rtenoactivity` 2×.

### The mock serial bus — YES, an EGM can be run entirely in software

`test/blackbox/sas_blackbox.h` (11,963 bytes) is exactly what the README advertises:
*"All tests are self-contained and run without any serial hardware. The CIRS45 AFT-timer
regression uses an in-memory mock serial bus, so it runs in CI just like the unit tests
(no real COM ports required)."* (`README.md`).

How it works:

1. `class MockPort` — a mutex+condvar-guarded `std::deque<uint16_t>` of **9-bit SAS units**
   (payload in bits 0–7, wakeup bit in bit 8). Two instances form a virtual null-modem pair:
   `gToEgm` (host→EGM) and `gToHost` (EGM→host).
2. `installEgmSerialCallbacks()` fills a `QUIXANT_SERIAL_CALLBACKS` struct (libqxcom's
   `libqxcom_callbacks.h`: `open/close/cleanup/setState/getState/setTimeouts/getTimeouts/
   getVersion/getTimestamp/read/readExtended/write/writeExtended/flushRx/flushTx/cancelRx`,
   `callbacksVersion = CALLBACKS_VERSION`) whose `readExtended`/`writeExtended` drain and push
   `MockPort`, and hands it to libsas via **`sasSetSerialCallbacks(&cb)` — a public, exported
   entry point** (confirmed present in `libsas.dll`'s export strings).
   `egmReadExtended` returns `0x80000000` on an empty read so libsas keeps polling.
3. Host side: `hostWakeup()`, `hostGeneralPoll()` (0x80 sync, then `0x80|addr`),
   `hostSendLongPoll(body, len)` (appends the SAS CRC, sets the wakeup bit on the address byte),
   `hostEnableRte(enable)` (LP 0x0E), `readRawUnits()` (keeps the wakeup bit — the only way to tell
   a loop-break chirp from a long-poll ACK, both being a lone address byte) and `readUnits()`
   (payload only).
4. `sasCrc()` reproduces libsas's `crcCalc` (nibble-wise, `q * 0x1081`), `toBcd5()` encodes a
   5-byte BCD amount.
5. Boot sequence used by every scenario (`blackbox_test.cpp:303-340`): `installEgmSerialCallbacks()`
   → `sasInit(&handle, "com3", SAS_ADDRESS, features, callback)` → `SAS_ConfigureGame(...)` →
   optional `SAS_AFTRegisterRequest` / `sasSetCurrentAmounts` → run host thread + EGM thread.

**Answer to the key question:** an EGM instance **can** be run purely in software today, and the
plumbing to do it is *public API*, not test-private. Two caveats:

- **No cross-process path exists.** `grep -rn "socket\|TCP\|WSAStartup\|pipe" src/*.cpp` returns
  nothing. The mock bus is in-process only; the "host" in the black-box tests is another thread in
  the same executable poking `MockPort` directly. To let an *external* host app talk to it you must
  write a bridge process that links libsas, installs serial callbacks whose
  `readExtended`/`writeExtended` are backed by a TCP socket / named pipe / com0com virtual COM pair
  instead of `MockPort`. That is roughly the 200 lines of `sas_blackbox.h` with the deque swapped
  for a socket — nothing in libsas resists it.
- **Platform validation.** `sasInit` calls `validateQuixantPlatform()` first
  (`src/libsas_wrapper.cpp:141-146`), which on Windows opens `\\.\QX10` and polls an FPGA timer
  twice via `DeviceIoControl(... CTL_CODE(..., 0x828, ...) ...)`, returning
  `QXT_INVALID_PLATF_ERR_OFFSET` if the driver is absent or the timer does not tick
  (`src/libsas_windows.cpp:67-121`). The whole body is compiled out under
  `#if !defined(VALIDATION_DEBUG)`. The tests get around it because `sas_static_testable` is built
  with `target_compile_definitions(... VALIDATION_DEBUG ...)` (root `CMakeLists.txt`).
  **The shipped `libsas.dll` does NOT define VALIDATION_DEBUG**, so a software EGM on a non-Quixant
  PC needs either a Quixant board with the QX10 driver, or a purpose-built `VALIDATION_DEBUG` library.

---

## 7. Build artefacts

`lib/` and `bin/` are not empty — a full Debug x64 build is already present.

```
bin/Debug/x64/libsas.dll          2,179,072   2026-09-01   (SAS_LIB_VERSION=5.4.4.27)
bin/Debug/x64/libqxcom.dll          164,864
bin/Debug/x64/libnvram.dll          430,080
bin/Debug/x64/blackbox_test_dbg.exe 3,836,928
bin/Debug/x64/whitebox_tests.exe    6,992,384
bin/Debug/x64/sas-example_dbg.exe   3,504,128
bin/Debug/x64/hwconformance_dbg.exe 3,997,184
bin/x64/Debug/libsas.dll          2,167,296   2026-08-10

lib/Debug/x64/libsas.lib                  (import lib for the DLL)
lib/Debug/x64/sas_static_testable.lib     (the VALIDATION_DEBUG full-source static lib)
lib/Debug/static/x64/libsas.lib           (static)
lib/static/x64/Debug/{libsas,libqxcom,libnvram_dbg}.lib
out/build/{Debug,igs203}/...              (CMake trees, gtest/gmock libs)
```

A release build produces, per the `install()` rules in `CMakeLists.txt`:
`sas_shared` → `bin/${QXARCH}/libsas.dll`; `sas_static` → `lib/static/${QXARCH}/libsas.lib`;
public headers `libsas.h` + `libsas_wrapper.h` → `include/`; plus `libsas-example`, the README,
the `examples/` tree, and a `sasConfig.cmake` so consumers can `find_package(sas CONFIG)`
(via `qx_pack()`). Dependencies `libnvram v5.3.2.1` and `libqxcom v3.3.4.0` are fetched from
GitHub by CMake, not vendored — and **`sas_shared`/`sas_static`/`sas_static_testable` never link
them** (CMake comment); only the consumers that wire a real transport do.

Also worth knowing: `-DHAL=ON` replaces every source with the stub `src/hal/libsas.cpp`, where e.g.
`sasInit` just sets `*sasHandlePtr = (void*)0x01` and returns `QXT_LIB_SUCCESS`. `-DSASHOST=ON`
builds `utils/sashost` (out of scope, and off by default).

Reference material shipped in the repo: **`docs/SAS-6-02-Protocol.pdf` (2.1 MB)** and
**`docs/sas-spec.txt` (574 KB)**, plus `docs/AFT_CASHOUT_LP72.md`, `AFT_LOCK_STATUS_PROVIDER.md`,
`JACKPOTS_AND_PROGRESSIVES.md`, `METER_PROVIDER.md`, `PROGRESSIVE_LEVEL_WIN.md`,
`ROM_SIGNATURE.md`, `LOGGING.md`, `sas_sync_sm.md`.

---

## 8. CHANGELOG — SAS version and timeline

`CHANGELOG.md` is 99,861 bytes / 1,635 lines with **276 version entries**, newest first, from
**1.3.0.0** at the bottom to **5.4.4.32 (2026/09/03)** at the top.

Protocol target is stated unambiguously: `README.md` line 6 — *"Quixant Slot Accounting System
**6.02** Protocol Library"* — and the changelog cites **"SAS 6.02"** and specific sections 13 times
(sections 2.2.1, 3.2, 3.3, 4.4, 7.8, 7.8.1, 7.8.2, 7.18, 8.2, 8.7, 10.2, 12.5, 12.6, 15.12, 16.2
and Tables 3.1, 7.23b, 12.5, 16.1d, C-4, C-7 all appear).

Recent feature timeline (top entries):

- 5.4.4.32 (2026/09/03) — HAL build now exports every declared entry point
- 5.4.4.31 — LP 1B handpay amount reported in accounting-denom units unless a progressive portion is present; sub-cent denom codes 1B–1F now rejected
- 5.4.4.30 — Linux build fixed (stale per-channel TX thread references)
- 5.4.4.29 — with RTE on, an exception first reported to a long poll is not repeated on the following general poll (Table 3.1 implied ACK)
- 5.4.4.28 — **new** `sasAftCancelGmToHost()` withdraws an outstanding cashout-to-host request
- 5.4.4.27 — meter poll / LP 74 racing provider (re)registration no longer risks a stall
- 5.4.4.26 — LP 74 naming an unsupported transfer condition now withdraws the standing lock as well as answering FF
- 5.4.4.25 — LP 56 lists only games at the current player denomination (§16.2)
- 5.4.4.24 — LP 7E reports machine local time, not UTC

---

## 9. Reusable assets for the new host tool

- **`src/libsas_logger.cpp:190-...`** already contains a complete LP-code → human-readable-string
  decode table (98 distinct `LP_*` descriptions extracted), plus exception and ACK/NACK/chirp
  classification. It is a ready-made spec for the new tool's trace pane.
- **`test/blackbox/sas_blackbox.h`** is a working, minimal SAS *host* implementation: CRC, BCD,
  9-bit framing, general poll, long poll with CRC, RTE enable, wakeup-bit-aware read. ~250 lines.
- **`docs/sas-spec.txt`** (574 KB plain text) is grep-able for every table and field layout.
- The 18 black-box scenarios are effectively a written conformance test plan the new host tool
  should be able to drive from the host side.
