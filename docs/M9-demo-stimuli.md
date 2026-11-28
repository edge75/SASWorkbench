# M9 — Driving the real demo's menus, faults, plays, restarts and registration

**Status: built 2026-11-27, under D63, for the gaps the user approved on 2026-11-26
(`.planning/demo-actions-audit.md`, ranked gaps 1, 2, 3, 4, 6, 7, 8 and 9).** The peripheral stimulus
(`--stimulus qpm`) now works the audit key, jams the note acceptor, plays games whose outcome the demo
decides, holds a game, chooses the first enabled game, restarts the demo from a declared executable and
posts its AFT registration key. It was first run against the rebuilt demo on 2026-11-28; what that
measured is at the end.

---

## The rule

The demo decides its own outcomes, so nothing here asks it for one. A step is carried only through a
lever the demo already reads (the shared region's masks, its own window, its own executable), and the
result is confirmed where it can be: on the wire through the host, or in the demo's own log. What can
never be done is refused **before the case starts**, so a case is skipped without a note inserted or a
door opened; what fails while a case runs ends the case, so the assertions after it are skipped and
never failed against a machine nobody drove.

## What each step does on the demo

| Step | On the demo | Confirmed by | Source |
|---|---|---|---|
| `egm menu attendant enter\|leave` | turns or returns the `audit` keyswitch (DIN 5), **no door opened** | the case's own `$82`/`$83` | QSP `SasNetwork_Meters.hpp:171` (entry reported when the page is drawn), `SasNetwork_Conditions.hpp:943`, `:959`; `game_configuration.json` DIN 5 |
| `egm menu operator ...` | refused | — | `$84`/`$85` come only from `qspActivateOperatorMode` or the diagnostic pages (QSP `libqsp.cpp:2173`, `SasNetwork.cpp:1714`), both touch |
| `egm press bet` | refused | — | the bet buttons set a multiplier and commit nothing until play (`SlotGame_01.cpp:344-351`); `$89` is written at game start (QSP `SasNetwork.cpp:1359`) |
| `egm note fault jam\|cleared` | sets or clears bit 9 of the acceptor mask | the case's own `$29` | QSP `SasNetwork_Conditions.hpp` `eBnaJam` → `BillJam`; `QpmLayout.BnaJamBit` |
| `egm note fault <other>`, `egm printer fault ...` | refused | — | disconnection (bit 8) is not verified on the demo; printer bits are only the simulator window's labels (`qpm_simulator/src/Main.cpp:57`) and the measured build prints through this project's shim |
| `egm play games=N [bet=B]` | presses the bet button whose multiplier wagers B, then play, and waits for `$7F` per game | `$7F` heard after the press (not consumed) | `SlotGame_01.cpp:340-351`, `:433`, `:845`; `WheelGame.cpp:276-282` |
| `egm play ... win=W` (zero too) | refused | — | the outcome is the demo's RNG (`Math_01.cpp:151`, `WheelGame.cpp:363`) |
| `egm game start [bet=B]` | as play, but returns once `$7E` is heard | `$7E` | as above |
| `egm game end` | waits for `$7F`; refuses when the game had already ended before the step | `$7F` | the spin ends by itself |
| `egm game end win=W` | refused | — | as play |
| `egm select game N` | only N = the first enabled game (`$56`); presses Game Menu until `$55` reads N | `$55` | `GameSelect.cpp:114-127` goes to the first enabled game only |
| `egm select game 0` | refused | — | the selection screen cannot be confirmed on the wire |
| `egm power cycle` | kills the process whose image is exactly `--egm-exe`, starts it again from that file, waits for the heartbeat | heartbeat; the runner resynchronises a sequenced case | `Main.cpp:97-158` (`qspStart`/`qspEnd`) |
| `egm aft register` | posts F10 (press and release, real scan code) to the demo's GLFW window | the demo's log line `AFT registration: request accepted, status now 0xNN`, then long poll `$73 01 FF` must read the same status | `Main.cpp:280-288`, `:509-538`; `DemoLog.cpp` (log beside the executable, flushed per line) |

## What the host now lends a backend

A backend may implement two new optional interfaces. **`IStimulusPreflight`** says which steps it would
refuse whatever the machine's state; the runner checks every stimulus step of a case before the case
starts and skips it naming the first. **`IHostAwareStimulusBackend`** receives a view of the host:
exceptions heard after a mark, never consumed, and single long polls, each followed by one round of the
host's general polls so the case's next long poll of the same command is not read as a retransmission
(libsas#248). In a sequenced case the view is unavailable, because any read would change what the case
measures; a play, a game or a selection then ends the case, and registration is confirmed by the log
alone.

A refusal raised by the peripheral driver now **ends the case**: the remaining steps are skipped with the
reason, as a channel that cannot be opened already does.

## Run options

* `--egm-exe <file>` — the demo's executable. Offers `machine.power_cycle`,
  `stimulus.aft_registration` and `machine.aft_operator_registration` (the demo's F10 action). Only a process whose image path equals it is stopped or sent a key; none
  or several running is a refusal.
* `--egm-bet-credits <n>` — the credits one bet multiplier wagers on the first enabled game (50 × game
  denomination ÷ accounting denomination: 50 on the layout-19 fixtures at 5c with a 5c game). Offers
  `stimulus.play`, `stimulus.game_in_progress` and `stimulus.select_game`. Without it, plays are refused
  naming the option.

`machine.audit_menu` is offered when the profile has an audit keyswitch, `stimulus.note_fault` always.
`stimulus.bet_button` is never offered.

## What it unlocks

Counted from the validation tree's `requires` lines and the preflight rules (not from a run):

* **No option:** 4.28 (display meters) and 4.41 (bill jam). 4.29, 4.40, 4.42, 4.44.1 and 4.44.2 now
  reach the driver and are skipped by preflight with the exact reason instead of a missing capability.
* **`--egm-exe`:** 3.6, 3.22, 4.4, 4.8, 4.14, 7.1, 11.12, 16.33-16.35, 19.13, 21.9-21.12, AD-07
  (power cycle), and 16.15 when `--real-time-events` is given (registration).
* **`--egm-bet-credits`:** 15.27, 15.33 (selection). Of the 50 cases it brings in, 47 stay skipped by
  preflight: almost every play in the GSA scripts is `bet=1`, or states a win.

## Findings

* **The demo cannot wager the bets the GSA scripts use.** Its smallest bet is 50 lines × the game
  denomination (`SlotGame_01.cpp:348`; the wheel's `kGameCost` is 50 too), so `bet=1` to `bet=5`
  cannot be placed at any accounting denomination. The play steps without a win are carried, but they
  unlock almost nothing in the GSA tree.
* **The demo has no bet-one button**, so the 11.19-11.25 and 15.15-15.25 cases that press bet once per
  credit cannot run on it whatever the driver does.
* **The operator menu is touch-only** on this platform.

## Still pending: the real demo

Nothing above was run against the demo: no `OpenGL_Game.exe` of the measured build exists any more,
and the platform was free but there was nothing of ours to drive. To verify, with the platform lock
free, no `qsp_platform_test` or `Quixant.QSP.RESTAPI` running and `qpm_storage.bin` hashed:

```
cd <demo>\bin\SasWorkbenchM0\x64
OpenGL_Game.exe
sasctl run tests/validation/egm-test-scripts-v1.00 --tcp 127.0.0.1:9760 --address 01 --stimulus qpm ^
  --egm-resources <demo>\bin\SasWorkbenchM0\x64\Resources --egm-exe <demo>\bin\SasWorkbenchM0\x64\OpenGL_Game.exe ^
  --egm-bet-credits 50 --tag chapter-4
```

Unknown until then: whether the audit key alone draws the page that sends `$82`, whether posted F10
reaches GLFW on the measured build, the spin length against a held game, and whether `$55` follows the
Game Menu press.

## Measured on the demo, 2026-11-28

Rebuilt at the pins (demo v2.0.4.37, QSP v2.3.6.34, QPM2 v9.0.0.14, libsas v5.4.6.16) with all four
configure trust lines, golden layout-19 memory, `qpm_storage.bin` `6183DE07`.

* **The audit key alone sends `$82` and `$83`.** 4.28 passes with the key turned and returned, no door.
* **Posted F10 reaches the game.** Each post adds `AFT registration: request accepted, status now 0x80`
  to the demo's log, from `WM_KEYDOWN` and `WM_SYSKEYDOWN` alike. The registration step still reports
  that nothing arrived, because it counts the log's lines including the empty one after the last newline
  and so skips the first new line; `$73` was therefore not read back.
* **A spin lasts 10.0 s** from `$7E` to `$7F` (three games at 50 credits: 10.05, 9.97, 10.01 s), and
  `$7E` comes 0.65 s after the Play press.
* **`$55` follows the Game Menu press**: it read 0 on the selection screen and 1 about 0.8 s after one
  press, with `SelectGame(1) denomination 5` in the demo's log.
* **A bill jam is a lockup that only the Reset/JKey keyswitch clears.** After `egm note fault cleared`
  the machine keeps returning every note (`$2B`) and ignores Service, across a restart, until the
  keyswitch is turned (QSP `BillValidatorHandler.cpp:1180`, `ConditionHandler.cpp:839`). In a run, 4.41
  therefore fails 4.5, 4.52, 4.54, 4.8 and 4.11 after it; turning the keyswitch made 4.52 and 4.54 pass.
* Chapter 4 on this build: 12 pass, 5 fail (all from the jam above), 43 skip; 4.4 and 4.14 restart the
  demo and pass.
