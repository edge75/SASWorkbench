# M9 — Scripted outcomes on the real demo

**Status: built 2026-11-28, under D64, for gap 5 of `.planning/demo-actions-audit.md` (forced outcomes) as the
user approved it on 2026-11-26.** A dev build of the demo, made by a second preset, takes the values of its game draws
from a queue the test host writes. The peripheral stimulus (`--stimulus qpm`) uses it for progressive wins and losing
games. Real-demo verification: see the end.

---

## The rule

The measured build is not changed: its preset, `SasWorkbenchM0`, stays exactly as it was. A second preset swaps the
demo's generator, and every result from that build says so. A step that needs a scripted outcome is skipped, naming why,
on any machine that is not a live scripted build. Nothing in QSP_DemoOpenGL, QSP or libsas is edited.

## The build: what is replaced

The demo draws its outcomes in two places, both through `Rng::GenerateUint32` (QSP_DemoOpenGL v2.0.4.37):

| Draw | Range | Source |
|---|---|---|
| slot, one stop per reel, five per spin | `0..15`, `0..16`, `0..15`, `0..14`, `0..13` | `Game_01/Math_01.cpp:151`, strips `:17-21` |
| wheel, one segment per spin | `0..19` | `Game_02/WheelGame.cpp:425`, `WheelMath.h:20` |

`tools/qxcom-tcp/cmake/saswb-scripted-outcomes.cmake` drops `src/GameApplication/Rng.cpp` from the `OpenGL_Game` target
and adds `tools/qxcom-tcp/src/saswb_scripted_rng.cpp`, which includes the stock file whole with its class renamed. The
stock generator still seeds, cycles in its background thread and writes its state to vendor NVRAM after every draw,
and is still asked for every game draw. The queued value is returned instead of its value only when one is queued. The
hook is included by `saswb-top-level.cmake` only when the preset sets `SASWB_SCRIPTED_OUTCOMES=ON`. It stops the
configure if the demo no longer builds that file, and prints `SasWorkbench: SCRIPTED OUTCOMES ... never ship this build`.

The preset, added beside the plain one in the demo's gitignored `CMakeUserPresets.json`:

```json
{
  "name": "SasWorkbenchM0-Scripted",
  "inherits": "SasWorkbenchM0",
  "binaryDir": "${sourceDir}/out/build/${presetName}",
  "cacheVariables": {
    "SASWB_ROOT": "<this checkout>",
    "CMAKE_PROJECT_OpenGL_Game_INCLUDE": "<this checkout>/tools/qxcom-tcp/cmake/saswb-top-level.cmake",
    "CMAKE_PROJECT_qpm_INCLUDE_BEFORE": "<this checkout>/tools/qxcom-tcp/cmake/saswb-qpm-hal.cmake",
    "CMAKE_PROJECT_sas_INCLUDE_BEFORE": "<this checkout>/tools/qxcom-tcp/cmake/saswb-sas-validation.cmake",
    "SASWB_SCRIPTED_OUTCOMES": "ON",
    "SASWB_OUTCOMES_DIR": "C:/ProgramData/saswb/outcomes"
  }
}
```

It builds to `bin\SasWorkbenchM0-Scripted\x64`, beside the plain build. The two share the platform, NVRAM and the
peripheral region, so only one runs at a time.

## The queue, the log and the marker

All three are in `C:\ProgramData\saswb\outcomes` (`--egm-outcomes` on the host, `SASWB_OUTCOMES_DIR` in the preset).

* **`queue.txt`**: one outcome per line, `<id> <alternative>=<max>:<value>,... [<alternative>=...]`. The host writes it
  atomically, one outcome per game. The build takes a line by renaming the file. The first draw of a game picks the
  alternative whose first range matches, and every later draw must match its own range too. A value outside its range,
  a range no alternative starts with, or a draw the alternative did not expect is refused and logged; that draw is
  then the stock generator's. Nothing is clamped. The rules are in `tools/qxcom-tcp/src/saswb_outcome_queue.h`, and
  `test/outcome_queue_check.cpp` checks them without the demo.
* **`draws.log`**: every game draw, forced or not (`forced <id> slot 2/5 0..16 -> 13`, `done <id> slot`,
  `refused <id>: ...`, `drawn 0..19 random -> 7`). A step is confirmed only by its outcome's `done` line.
* **`scripted-outcomes.marker`**: written at start-up with the process id, its creation time and the executable path,
  and removed at a clean exit. The host accepts it only while that process, with that creation time, is still
  running. With `--egm-exe`, the marker must also be from that executable.

## What each step does on the scripted build

| Step | Queued | Confirmed by |
|---|---|---|
| `egm progressive win level=1` | slot `12,13,13,13,12`: five jackpots on the top row, a way win → Grand (`Math_01.cpp:397-400`) | `done` in the log, within 10 s of play |
| `egm progressive win level=1,2` | slot `11,12,12,12,11`: five jackpots on the middle row → Grand, Major (`:392-393`) | as above |
| `egm progressive win level=2` | slot `10,11,11,11,10` (bottom row → Major, `:394-395`) or wheel segment 3 (`WheelMath.cpp:75-77`) | as above |
| `egm play games=N bet=B win=0` | per game: slot `0,2,0,0,0` or wheel segment 0, both paying nothing | `done`, then `$7F` as for any play (D63) |
| `egm progressive win ... win=<n>` (zero too) | refused: the slot's five jackpot symbols also pay 1000 credits per multiplier (`Math_01.cpp:41`) | — |
| `egm play ... win=<n>`, n > 0 | refused: no stop solver is kept here | — |
| `egm handpay jackpot ...` | refused, as before: the jackpot limit is set only by touch | — |
| any of the above, no live marker | skipped before the case starts, naming the marker | — |

A progressive win does not wait for `$7F`: a jackpot over the limit locks the machine up before its game ends, and the
case waits for its own exception. A bet is pressed only when the step states one (`bet=`), with the same
`--egm-bet-credits` rule as plays. Otherwise the game is played at the multiplier the demo is on. Each slot outcome was
checked against the demo's way, scatter and jackpot rules. `DemoOutcomesTests` keeps a test-only reading of
`Math_01.cpp` and checks every constant again. The host never computes a win.

## Labelling

A live marker adds `machine.build = scripted outcomes (dev build)` to the driver's offer. The runner puts it in every
result's provenance, so the console line, the JSON record, and the Markdown, HTML and JUnit reports all name it.
`stimulus.progressive` is offered by the driver on any build with a play button. On a plain build the case reaches
preflight and is skipped with the reason, not with a missing capability.

## Case corrections

12.15, 12.17, 13.24 and 13.26 assert that a paytable-win meter does not move across a progressive win. Their notes say
the winning game must pay no line. Their steps now say `win=0`. The software machine is unchanged, since zero was
already its default. The demo refuses the step, because its Grand always pays the line.

## What it unlocks

Counted from `requires` lines, not from a run. `machine.progressives` (non-SAS levels) is never offered on QPM, so
chapter 12 still skips. The chapter-13 SAS-progressive cases that need no jackpot limit, power cycle or second game
(13.16-13.19, 13.21, 13.25, 13.28, 13.29, and 13.38 and 13.40 with `--real-time-events`) reach the step. They run when
the demo is set to a linked progressive and `$1F` reports the group they declare. Losing plays unlock no GSA case: their
bets are 1 to 10 credits, which the demo cannot wager (D63).

## On the real demo (2026-11-28)

Run on QSP_DemoOpenGL v2.0.4.37 (`C:\work\QSP_DemoOpenGL-2.0.4.37`), both builds in turn, with the platform free.
The case file is `tests/validation/demo-scripted-outcomes/scripted-outcomes.sas`; it needs `stimulus.scripted_outcomes`, so it skips
on the software machine.

* **The plain build refuses.** With a copy of the cases that does not require the capability, both steps were skipped
  before the case started, naming the missing marker; the provenance carries no machine label.
* **The scripted build names itself.** Its marker was found live and every result read
  `stimulus qpm, machine: scripted outcomes (dev build)`. Its log showed every game draw. That included a game the demo
  replayed by itself at start-up, which drew from the stock generator because nothing was queued.
* **No outcome was drawn yet.** On the memory the previous run left behind, the demo dropped every play and bet press
  and rejected every note (`$2B`), on the plain build as well, while still taking the keyswitch. A platform inhibiting
  condition was active, which a pulse of the reset key did not clear. The host queued each outcome, waited the 10 s,
  withdrew it and ended the case, as designed. The progressive and losing outcomes are still to be seen on a demo that
  takes play. The memory found at the start (`0E5C2A19`) boots into a memory error on both builds and was restored
  afterwards; `qpm_storage.bin` stayed `63CC489F`.

## Retest on golden memory, 2026-11-28

Both builds rebuilt at the pins with all trust lines; the scripted preset now builds from the main checkout.
Golden layout-19 memory, `qpm_storage.bin` `6183DE07`.

* **The demo starts on its game selection screen and ignores Play there.** On a freshly started demo both
  cases skipped again (no draw within 10 s), and a stock play on the plain build ended no game within 90 s.
  After `egm select game 1` the same demo session played: a stock game moved games played by one, and
  both cases passed, the losing pair (`0,2,0,0,0` twice) and the level-one win (`12,13,13,13,12`) with
  `$54`. Before a case runs on a fresh demo a game has to be chosen, so both cases now warm up and choose game 1 first.
* **The plain build refuses**, naming the marker (here a stale one left by a terminated scripted demo).
  12.15 and 12.17 skip for `machine.progressives`, 13.26 for `machine.jackpot_limit`, 13.24 is refused for its
  stated `win=0`, and 13.25, 13.28 and 13.29 are refused for the missing marker.
* Chapters 12 and 13 on the scripted build: 3 pass, 1 fail, 68 skip. The failure is 12.4: long poll B0
  answers with an error and no game number on libsas v5.4.6.16.

## Fresh start, 2026-11-28 evening

Both cases now warm up for 20 s and choose game 1 before the note. Golden memory, `qpm_storage.bin` `6183DE07`,
demo started 5 s before the host: on the scripted build both pass (the losing pair and the level-one win with
`$54`), each confirmed by its `done` line; on the plain build both skip for the missing
`stimulus.scripted_outcomes`.
