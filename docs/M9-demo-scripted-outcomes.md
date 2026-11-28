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

## Still pending: the real demo

The scripted build is configured and built (`C:\work\QSP_DemoOpenGL-2.0.4.37\bin\SasWorkbenchM0-Scripted\x64`). It has
not been run yet: the platform is in use by another session's verification of the plain build.
