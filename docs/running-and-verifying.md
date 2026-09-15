# Running and verifying SasWorkbench

Everything needed to go from a fresh PowerShell prompt to a full green verification, in order.
Written 2026-09-15, at the end of M5. Every command below was run on this machine and the outputs
quoted are the ones it produced.

Nothing here needs the gaming platform, the machine library, a Quixant board, the peripheral
simulator or a network. The whole verification runs against the software gaming machine in this
repository. What each step actually proves is stated, because a green run against our own peer is
not the same claim as a green run against a real machine.

---

## 0. What has to be installed

One thing.

| | |
|---|---|
| **.NET SDK 10.0** | the only prerequisite. This machine has **10.0.401**; CI pins `10.0.x` |

Check it:

```powershell
dotnet --version
```

Expect `10.0.x`. If it prints something older, install the .NET 10 SDK from
<https://dotnet.microsoft.com/download/dotnet/10.0> and open a new terminal.

There is no `global.json`, so the newest 10.0 SDK on the machine is used. The target framework is
pinned to `net10.0` in `Directory.Build.props`.

**Not needed for any step in this document:** Visual Studio, `QSP`, `libsas`, `QSP_DemoOpenGL`,
`qpm_simulator`, CMake, a C++ toolchain, Python, a serial port, a Quixant board.

---

## 1. Set up the shell

```powershell
cd C:\Progetti\SasWorkbench
```

That is all the setup there is — **no environment variables are required**. The application reads
no configuration from the environment; everything a run needs is on its command line.

Two PowerShell notes that matter later:

* check an exit code with `$LASTEXITCODE`, immediately after the command;
* `&&` works in PowerShell 7+, which is what this repo assumes.

Optional, and only to save typing:

```powershell
$sasctl = "src\SasWorkbench.Cli\bin\Release\net10.0\sasctl.exe"
```

After step 2 that executable exists and can be run directly, which is faster than `dotnet run`
because it does not re-check the build each time.

---

## 2. Restore and compile

```powershell
dotnet restore SasWorkbench.slnx
dotnet build SasWorkbench.slnx --configuration Release --no-restore
```

Expect:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Zero warnings is not decoration.** `TreatWarningsAsErrors` is on, so a warning fails the build;
if the build succeeds there are none.

First build takes about a minute; later ones a few seconds.

---

## 3. The four checks — the same four CI runs

These are the ones to run. Together they are exactly what `.github/workflows/ci.yml` does on every
push, so if all four pass here they pass there.

Run them in this order. Each is independent, so a failure in one does not invalidate the others.

### 3.1 Unit tests

```powershell
dotnet test SasWorkbench.slnx --configuration Release --no-build
```

Expect **975 passed, 0 failed, 0 skipped**, as four suites:

```
Passed!  - Failed: 0, Passed:  44, Skipped: 0, Total:  44 - SasWorkbench.Qpm.Tests.dll
Passed!  - Failed: 0, Passed: 703, Skipped: 0, Total: 703 - SasWorkbench.Core.Tests.dll
Passed!  - Failed: 0, Passed:  19, Skipped: 0, Total:  19 - SasWorkbench.Gui.Tests.dll
Passed!  - Failed: 0, Passed: 209, Skipped: 0, Total: 209 - SasWorkbench.Egm.Tests.dll
```

Takes about 3–5 minutes; the suites run in parallel and the Egm one is the long pole at ~3 min.

*What it proves:* the protocol model, the container format, the script parser, the poll engine and
the software machine agree with themselves. **The refusals a floor has to make live here**, in
`BothAxesTests`, rather than in the script suites — expressed as cases they would be deliberate
skips, and a suite carrying deliberate skips cannot be read at a glance for the accidental one.

### 3.2 The regression suite

```powershell
dotnet run --project src\SasWorkbench.Cli --configuration Release --no-build -- `
  run tests\regression --loopback --stimulus software --report html,junit,md,jsonl --out artifacts\run
$LASTEXITCODE
```

Expect **`PASS  47 passed`** and `$LASTEXITCODE` of **0**, in roughly **135 seconds**.

There must be **no skips at all**. That is a standing rule of this tree: every deliberate skip is
kept in the unit suite instead, so any skip appearing here is an accident worth chasing.

Reports land in `artifacts\run\` (`run.html`, `run.junit.xml`, `run.md`, `run.jsonl`). `artifacts\`
is gitignored.

*What it proves:* the host and the software machine agree over a link that carries all nine bits —
both ends being our own code, which is what the provenance line under the results says in as many
words.

**About 38 of those 135 seconds are the hundred-machine case** (`@casino`). To skip it while
iterating:

```powershell
dotnet run --project src\SasWorkbench.Cli --configuration Release --no-build -- `
  run tests\regression --loopback --stimulus software --skip-tag casino
```

That is **46 passed in ~100 s**. Run the full tree before pushing, because CI runs the full tree.

### 3.3 The conformance tree passes with the findings left out

```powershell
dotnet run --project src\SasWorkbench.Cli --configuration Release --no-build -- `
  run tests\conformance --loopback --stimulus software --skip-tag finding
$LASTEXITCODE
```

Expect **`PASS  2 passed`** and `$LASTEXITCODE` of **0**, in about 5 seconds.

### 3.4 The findings are still findings

```powershell
dotnet run --project src\SasWorkbench.Cli --configuration Release --no-build -- `
  run tests\conformance --loopback --stimulus software --tag finding
$LASTEXITCODE
```

Expect **`FAIL  0 passed, 1 failed`** and `$LASTEXITCODE` of **1**.

**A failure here is the pass condition.** A case labelled a finding is one where the machine
library does not do what a conformance document asks; checking that it still fails is what stops
the label outliving the finding. If this ever returns 0, the library has changed — take the label
off rather than papering over it.

---

## 4. Verifying M5 specifically

Steps 3.1–3.4 already cover M5; these are the things to look at if you want to see the milestone
rather than just its verdict.

### 4.1 The case D29 asks for

```powershell
dotnet run --project src\SasWorkbench.Cli --configuration Release --no-build -- `
  run tests\regression\floor\a-hundred-machines-behind-two-hosts.sas `
  --loopback --stimulus software --report jsonl,extended --out artifacts\d29
```

Expect one case, **20 assertions**, `PASS`. Wall time is 35–45 s depending on what else the laptop
is doing; about 18 s of that is the test and the rest is bringing up 100 machines on two lines and
taking them down again. The provenance line reads:

```
over multi-drop loopback, 100 machine(s), proven, stimulus software,
channels 1@01 2@01 · … validation=2 … information=2 (read from the machine),
polling 1=01-64 2=01-64
```

`polling 1=01-64 2=01-64` is the part that is new: before M5 a saved floor run's provenance
described one machine, and read back as a run of that one.

Then read the log a human reads:

```powershell
Get-Content artifacts\d29\run.log | Select-Object -Skip 130 -First 14
```

The two hosts interleave in one stream. `c2` marks the validation host and a blank marks the funds
host; `$01`, `$02` is the machine — and the machine column is **blank on the synchronisation
polls**, which are addressed to address zero and belong to nobody:

```
  #0     TX            381.3ms  1.80
        synchronisation poll — a general poll of address zero, which resynchronises every
        machine on the link and which none of them answers
  #1     TX    $01     407.0ms  1.81
        general poll of the machine at $01 — asks it for the next exception it has queued
  #2     RX    $01     436.3ms  0.00
        no exception — the machine has nothing queued to report
  #3     TX            437.0ms  1.80
  #4     TX    $02     467.2ms  1.82
  #5     TX c2         471.0ms  1.80
```

That blank column is the whole attribution rule in one place: of the 1,684 records in this run,
1,121 name a machine and the 563 that do not are **every one of them** a synchronisation poll.
There is no third category, and null never means machine one.

### 4.2 The round, with both hosts running

```powershell
dotnet run --project src\SasWorkbench.Cli --configuration Release --no-build -- `
  floor --machines 100 --channels 2 --rounds 5
```

Takes about a minute. Expect both lines within a few milliseconds of each other and of the
single-line figure, all 200 machine-ends answering, and nothing lost:

```
  channel   rounds    fastest     median    slowest   per machine
        1        5    6215.0     6239.8     6281.4      62.40 ms
        2        6    6215.0     6234.2     6265.0      62.34 ms

  channel 1   100 of 100
  channel 2   100 of 100
  symbols lost across every line   0
```

For the comparison that makes it mean something, run one line on its own:

```powershell
dotnet run --project src\SasWorkbench.Cli --configuration Release --no-build -- `
  floor --machines 100 --channels 1 --rounds 5
```

Expect a median of about **6238.8 ms** — the same. That equality is the design claim: a channel is
a port and a port is a line, so two hosts' rounds **interleave on independent wires** instead of
contending for one. Had it gone the other way the pair would cost about 12.5 s.

Your absolute numbers will differ from these — the round is one timer wait per machine and the wait
is served by the platform, so a faster or busier laptop moves it. What should hold is that the two
lines agree with each other and with the single-line run on the same machine.

### 4.3 The log, packed and read back

Run a real floor session and pack it. **This takes about ten minutes** — leave it alone while it
runs, because a busy machine shows up in the slowest-round column:

```powershell
dotnet run --project src\SasWorkbench.Cli --configuration Release --no-build -- `
  floor --machines 100 --channels 2 --rounds 95 --out artifacts\floor10

$sasctl = "src\SasWorkbench.Cli\bin\Release\net10.0\sasctl.exe"
& $sasctl log pack artifacts\floor10\floor.jsonl --out artifacts\floor10\floor.saslog
& $sasctl log header artifacts\floor10\floor.saslog
& $sasctl log unpack artifacts\floor10\floor.saslog --out artifacts\floor10\roundtrip.jsonl
```

Expect roughly 57,590 wire records, **11,030,311 bytes** plain against **706,288 bytes** packed
(**15.62×**), and then the property the whole format rests on:

```powershell
$a = (Get-FileHash artifacts\floor10\floor.jsonl -ErrorAction Stop).Hash
$b = (Get-FileHash artifacts\floor10\roundtrip.jsonl -ErrorAction Stop).Hash
"$a"
"$b"
$a -eq $b
```

Expect the two hashes printed identical, then **`True`**.

**`-ErrorAction Stop` is not optional here, and the hashes are printed for the same reason.**
Written the obvious way — `(Get-FileHash A).Hash -eq (Get-FileHash B).Hash` — this check prints
`True` when *both files are missing*, because each `.Hash` is `$null` and `$null -eq $null` holds. I
wrote it that way first and it cheerfully verified nothing at all. With `-ErrorAction Stop` a
missing file stops the check instead of passing it.

The check is worth running rather than assuming: it returned `False` on this machine until M5
pinned the line ending — `unpack` was writing the platform's newline, so every `\n` came out
`\r\n` and the file differed by 57,594 bytes while every record was correct. The check that would
have caught it compared the records, which cannot see it.

A shorter version of the same check, without the ten minutes, works on any run you already have:

```powershell
$sasctl = "src\SasWorkbench.Cli\bin\Release\net10.0\sasctl.exe"
& $sasctl log pack artifacts\d29\run.jsonl --out artifacts\d29\run.saslog
& $sasctl log unpack artifacts\d29\run.saslog --out artifacts\d29\again.jsonl
$a = (Get-FileHash artifacts\d29\run.jsonl -ErrorAction Stop).Hash
$b = (Get-FileHash artifacts\d29\again.jsonl -ErrorAction Stop).Hash
$a -eq $b
```

On this machine that run packed **1,717 records, 415,911 bytes into 24,079 — 17.27× smaller** — and
came back `True`.

### 4.4 Seeking into the middle of a session

```powershell
& $sasctl log slice artifacts\floor10\floor.saslog --from 300 --to 330
```

It prints how many of the file's blocks it touched. A slice that read every block would mean the
file is merely compressed rather than seekable, which is why the count is printed at all.

Run it against the **ten-minute** file, not against a short one. The D29 run packs into a single
block, so a slice of it truthfully reports `1 of 1` and demonstrates nothing; the ten-minute file
has 11 blocks and a thirty-second window touches one of them.

---

## 5. Looking at it

### The window

```powershell
dotnet run --project src\SasWorkbench.Gui --configuration Release --no-build
```

### The window without launching it

After any change under `src\SasWorkbench.Gui`, this is the check that matters — a UI change here
cannot be verified by a passing build:

```powershell
dotnet run --project tools\ui-specimen -- artifacts\ui
```

It renders the window off-screen to PNGs in `artifacts\ui\` — **22 of them**, both themes, both
window sizes, real frames from a real run of `tests\regression`. It also turns on Avalonia's binding
diagnostics, so **read its console output**: a mistyped binding otherwise renders an empty cell and
reports nothing.

**It now takes several minutes rather than about two**, because it runs the whole regression tree to
fill the window with real verdicts and that tree now contains the hundred-machine case. See the
second decision below — running a floor of a hundred in order to draw a picture is waste, and it is
a small fix.

### What this environment can and cannot do

```powershell
dotnet run --project src\SasWorkbench.Cli --configuration Release --no-build -- probe
```

### Everything the command line offers

```powershell
& $sasctl --help
```

---

## 6. Two decisions waiting for you

### The one that matters: CI picked the case up

**CI now runs the hundred-machine case on every push,** because `ci.yml` runs the whole
`tests/regression` tree with no tag filter and the new case lives there. That adds roughly 38
seconds per platform, so about 76 seconds per CI run.

I did not change `ci.yml` — that is your call. The two options:

* **Leave it.** D29's case is then covered by CI on both Windows and Linux, which is the strongest
  version of the guarantee, at 76 s a push.
* **Skip it in CI.** Add `--skip-tag casino` to the *Run the regression scripts through the command
  line* step in `.github/workflows/ci.yml`. The case is then run by hand, and the tag is already in
  place for it.

My recommendation is to **leave it**: the case is the milestone, it is the only thing exercising
both axes at scale, and 76 seconds is cheap against a floor defect that would otherwise be found by
a person.

### And a smaller one: the UI specimen picked the case up too

`tools\ui-specimen` runs `tests\regression` to fill the window with real verdicts, with no tag
filter, so every specimen render now brings up a hundred machines. It still works — 22 images, as
before — but it takes several minutes instead of about two, for a picture that does not need a
floor.

I did not change it, for the same reason: it is your tool and the fix is a judgement about what the
picture should show. The options are to leave it, to give `ScriptRunner` the tag filter the command
line already applies, or to point the specimen at a subdirectory. Not urgent, and worth doing before
the next UI change rather than after.

---

## 7. One anomaly, reported rather than buried

On one of six full-tree runs, `run tests\regression` exited **1** while printing no failure. It did
not reproduce: the same command ran green three more times, and the floor cases alone ran green
three times. I could not pin it down, so it is written here rather than left out.

If you see it, the run's own record says which case it was — re-run with reports on and read the
verdict rather than the exit code:

```powershell
dotnet run --project src\SasWorkbench.Cli --configuration Release --no-build -- `
  run tests\regression --loopback --stimulus software --report jsonl --out artifacts\chase
Select-String -Path artifacts\chase\run.jsonl -Pattern '"verdict":"Fail"'
```

My best guess, offered as a guess: the hundred-machine case waits up to 30 s for exceptions that
arrive once per ~6.25 s round, so a laptop that stalls for several seconds mid-run could miss one.
If it recurs and the failing case is that one, the waits are the thing to widen — **not** the 20 ms
quiet gap, which is what decides a message has ended and does not move without asking.

---

## 8. If something goes wrong

| symptom | what it is |
|---|---|
| `Build FAILED` with `MSB3027 … file is locked by sasctl` | a `sasctl` or GUI process is still running. Close it, or `Stop-Process -Name sasctl`. |
| Build fails on a warning | `TreatWarningsAsErrors` is on by design. Fix the warning; do not turn it off. |
| A run reports `SKIP` in `tests\regression` | not expected. That tree runs with no skips; a skip is a real finding. |
| Exit code 3 | nothing was actually tested — every case was skipped. Deliberately not a success. |
| Exit code 2 | the tool could not do what was asked; the message says what. |
| `'--out' is not an option of 'sasctl run'` | a *warning* that appears when an earlier argument error stopped the command before it read `--out`. Fix the first error. |
| Tickets appear somewhere unexpected | the wallet defaults to `C:\ProgramData\qpm_simulator\wallet`. Nothing in this document depends on it. |

---

## 9. The short version

If you want one block to paste and walk away from:

```powershell
cd C:\Progetti\SasWorkbench
dotnet restore SasWorkbench.slnx
dotnet build SasWorkbench.slnx --configuration Release --no-restore
dotnet test SasWorkbench.slnx --configuration Release --no-build
dotnet run --project src\SasWorkbench.Cli -c Release --no-build -- run tests\regression --loopback --stimulus software
dotnet run --project src\SasWorkbench.Cli -c Release --no-build -- run tests\conformance --loopback --stimulus software --skip-tag finding
dotnet run --project src\SasWorkbench.Cli -c Release --no-build -- run tests\conformance --loopback --stimulus software --tag finding
```

Expected, in order: build with 0 warnings · **975** unit tests pass · **47** regression cases pass,
no skips · **2** conformance cases pass · **1** finding still fails, exit code **1**.

That last line failing is correct. Everything else failing is not.
