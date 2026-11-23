# M7 — Power cycles, memory faults and memory clears on the software machine

**Status: built 2026-11-23, under D44 and D48, as group 3 of M8.** The software machine can be
power cycled (`egm power cycle`), given the memory faults its platform reports (`egm memory fault
cmos-lost`, `eprom-changed`) and have its memory cleared (`egm nvram clear`, or `egm memory fault
clear`). Across a power cycle it keeps what the library's non-volatile image and the platform keep,
and nothing else. Seventeen more cases now run: one in chapter 3, five in chapter 4, one each in
chapters 6, 7 and 21, and eight in chapter 16; 16.11 fails as a labelled finding. The tree went
from 258 pass, 28 fail, 346 skip to 274, 29, 329.

---

## The rule

D44 again: **the protocol half is the library's.** The library keeps one image across a restart and
raises a handful of exceptions while it recovers it; it raises no power or memory exception of its
own. Every $17, $18, $32, $36, $3A and $7A is the platform's, and so is every decision about the
lock, the game delay and what a memory clear leaves. Each of those carries a one-line "QSP" comment
in the code.

## What survives a power cycle, and where each behaviour comes from

| Behaviour | Source |
|---|---|
| One image is kept: exception queues, validation buffer and identifier, transfer history, lock and registration, hand pay, legacy bonus award | `libsas_db.h:855` to `:957` |
| Every queued exception is marked unsent, so one sent and not acknowledged goes again; an overflow in flight is owed again | `libsas_internal.cpp:622` to `:659` |
| Pending replies are not kept, so a reading not yet acknowledged is forgotten | `libsas_db.h:1051`, `libsas_internal.cpp:904` |
| Every channel starts chirping and waits for a synchronisation poll | `libsas_internal.cpp:313`, `libsas_wrapper.cpp:318` |
| Real-time reporting is off after a power-up | `libsas_internal.cpp:305`, `test/blackbox/blackbox_test.cpp:5155` |
| $69 again for a transfer still pending or unread, and its fifteen-second reissue | `libsas_internal.cpp:847`, `:3044` |
| $3D again for an unread record under the enhanced styles, its ten unread seconds spent | `libsas_internal.cpp:681`, `:855` |
| $51 again for a hand pay still awaiting its reading | `libsas_internal.cpp:887` |
| $3F again on a secure enhanced machine with no identifier | `libsas_wrapper.cpp:337` |
| $18 on every start; the conditions restored from memory raise their codes; $17 once started | QSP `SasNetwork.cpp:159`, `ConditionHandler.cpp:741`, `SasNetwork_Conditions.hpp:477` |
| A disconnected printer is found again after the start, so its $75 follows $17 | QSP `ConditionHandler.cpp:662` |
| The transfer lock is removed | QSP `SasNetwork_Conditions.hpp:547` |
| A cash-out to the host in progress is cancelled | QSP `SasNetwork_Conditions.hpp:519` |
| The configured game delay is kept, a running one is not | QSP `SasNetwork.h:376`, `SasNetwork_Conditions.hpp:553` |
| Games since power-up start again; every other meter, the credits and the legacy escrow are kept | QSP `ConditionHandler.cpp:81`, `SasNetwork.h:498` |
| The host's shutdown is a condition, restored like any other | QSP `SasNetwork_Callbacks.hpp:33`, `SasNetwork.cpp:254` |

## Memory faults and the memory clear

| Behaviour | Source |
|---|---|
| A memory error found while running raises $32 once and takes the machine out of play until a memory clear | QSP `SasNetwork_Conditions.hpp:59`, `ConditionDefaults.h:56`, `NvramHandler.cpp:475` |
| A changed program raises $36, and again at every start until a memory clear | QSP `AttachableDeviceHandler.cpp:436`, `SasNetwork_Conditions.hpp:62` |
| Out of play: no transfer lock, no transfers, legacy bonus busy | QSP `SasNetwork_AftTransfer.hpp:1110`, `SasGameRules.h:278` |
| $31 is never raised: a bank recovered from its mirror reports nothing | QSP `NvramHandler_BankCache.hpp:231` |
| $33, $34, $35, $37, $38 and $39 are never raised | QSP `SasNetwork_Conditions.hpp:19` to `:121` |
| The memory clear zeroes the library's image to its defaults: no records, no history, not registered, no hand pay, no award | `libsas_internal.cpp:756` to `:821` |
| It zeroes the platform's meters, drops what was owed and reports $3A then $7A; the asset number becomes 1 | QSP `SasNetwork.cpp:1197` to `:1221` |
| $3C follows as the configuration completes | QSP `SasNetwork_Conditions.hpp:63`, `:925` |
| Legacy bonusing on, transfers and the other switches back to the machine's own | QSP `SasRules.h:437`, `SasNetwork.cpp:1206` |

## What the platform decides, and what this machine says

* **A memory fault is the one found while running** (D48). Memory found invalid at power-up starts
  no link (QSP `SasNetwork.cpp:309`), so the host would hear nothing and the $32 the platform holds is
  dropped by the memory clear that alone recovers the machine.
* **The memory clear includes the attendant's configuration**: the poll address is set again, so
  the machine answers where it did, and the asset number stays at 1 until a case sets it.
* **The machine is dead to the line for half a second** while off; a host polling on a timer sees
  the link come back once it synchronises the machine.

## How the cases learn what the machine does

* `machine.power_cycle`, `machine.memory_fault` and `machine.nvram_clear` are offered by the software
  backend and the manual backend.
* `machine.memory_fault.cmos_recovered`, `.cmos_bad_device`, `.eeprom`, `.eprom_checksum` and
  `.partitioned_eprom` are offered by the manual backend only, because the platform never reports
  them. Each memory fault step needs the capability of the code it raises.

## What it deliberately does not model

* Memory found invalid at power-up, and the silent machine it leaves (3.9 stays notes only).
* Doors opened while the power is off and their codes $98 to $9B, and the door states reported after
  a start; a door open across a power cycle stays open and says nothing.
* A cash-out, a ticket redemption or a transfer caught half way by the power cut: the platform turns
  a ticket still printing into a hand pay and refuses a redemption, and this machine's transfers and
  cash-outs are over before a cut can catch them.
* A second channel with transfers cancelling the registration at every start
  (`libsas_wrapper.cpp:885`): the software machine is one channel when it is power cycled in a case.
* Sound, auto play and maintenance mode, which 21.9 to 21.12 read at the machine.
* The accounting denomination cases that configure the machine through its file: AD-09, AD-09b,
  AD-12, AD-13 and AD-41 now also require `machine.config_file`, which the memory clear alone does
  not give them.

## Script work done while bringing the cases up

None of these loosens an assertion.

* 4.15 to 4.19 stay one test each and now also require the capability of every fault they ask for,
  so they skip on the software machine by name; 4.14 runs.
* 4.23.2 now also requires `machine.handpay_queue`: its second jackpot arrives while the first is
  still held, which only a hand pay queue can take.
* 4.26, 16.84 and 16.86 had notes only and now have bodies, using `egm nvram clear`; 4.27 stays notes,
  as the period meters it clears are not modelled.
* 16.3, 16.4 and 16.11 now set asset number 12345, as their file says the software machine is given it.
* 16.34 puts a poll to another machine between its two general polls: the same general poll twice is
  a refusal (3.20), so the second asked for the first exception again.

## Findings against the documents

| Case | What the library and the platform do |
|---|---|
| 16.11 | A memory error neither cancels the registration nor raises $6E: the library keeps it in its image and only a memory clear empties it, silently (`libsas_internal.cpp:756`), with what was owed dropped (QSP `SasNetwork.cpp:1218`); memory found invalid at power-up starts no link (QSP `SasNetwork.cpp:309`). |
