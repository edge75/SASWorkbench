# M1 — Thin vertical slice

**Status: done.** A script written in a readable line-per-step language runs unattended against
a real SAS 6.02 gaming machine over a TCP link carrying nine-bit symbols, asserts on the decoded
reply, and produces a self-contained report. Every layer is exercised before any of them is
widened. Captured 2026-09-04.

---

## The proof

`sasctl run tests/regression/wire --tcp 127.0.0.1:9760 --address 01`, with the real machine —
`QSP_DemoOpenGL` linked against the platform library, no Quixant board — dialling the host:

```
  PASS  the link comes up and the machine identifies itself (6.9s, 5 assert(s))

  over tcp 127.0.0.1:9760 (listen), proven, stimulus none
  PASS  1 passed in 7.0s
```

Exit code 0. The five assertions, with what the machine actually returned:

| Assertion | Expected | Actual |
|---|---|---|
| `expect link up within 5s` | Up | Up |
| `send lp 1F` | a reply | answered in 30.7 ms |
| `expect field game_id == QX` | QX | QX |
| `expect field base_percentage of lp 1F >= 7500` | 7500 | 9210 |
| `expect field sas_version of lp 54 == 602` | 602 | 602 |

The wire, with the wakeup flag before each byte:

```
[  5927ms] TX 1.80   sync poll - address zero
[  5954ms] TX 1.81   general poll
[  5985ms] RX 0.18
[  6126ms] TX 1.80   sync poll - address zero
[  6157ms] TX 1.01 0.1F   long poll $1F
[  6189ms] RX 0.01 0.1F 0.51 0.58 0.30 0.30 0.31 0.02 0.FF 0.00 0.00 0.00
              0.30 0.30 0.31 0.00 0.00 0.00 0.39 0.32 0.31 0.30 0.86 0.3F
[  6730ms] TX 1.80   sync poll - address zero
[  6761ms] TX 1.01 0.54   long poll $54
[  6791ms] RX 0.01 0.54 0.03 0.36 0.30 0.32 0.D0 0.8D
```

The identification reply is byte for byte the one the wire proof captured. The wakeup bit is on
the address byte and on nothing else, in both directions.

### The same script against both peers

Run against the software machine in this repository as well, which is what D7 asks for:

| Peer | Verdict | Assertions |
|---|---|---|
| the platform library, over TCP | PASS | 5 |
| the software machine, over a loopback link | PASS | 5 |

No disagreement on this script. A disagreement would have been a finding in one of the two, and
the point of running both is to have somewhere for it to show up.

---

## What was built

### The wire, and what it refuses

`SasWorkbench.Core` carries one symbol as eight data bits plus the wakeup bit, over a loopback
link, a TCP socket, or a serial port. The frame check sequence is ported step for step from the
platform library's own routine and is **verified in a test against the 24 captured bytes** of a
real reply — if it disagreed with the machine, every frame we sent would be rejected for a reason
no other test would explain.

Three refusals are deliberate:

* A transport that cannot carry the wakeup bit is refused rather than used with eight bits. A link
  that silently dropped it would leave every test passing over a link no machine can hear.
* The serial backend refuses to open on anything but Windows, because the framework's serial layer
  discards mark and space parity there without an error. It is reported as **unverified** even on
  Windows: no adapter has been measured, and a pass on it is not evidence about hardware.
* A received symbol carrying the wakeup bit fails the poll. Only a host may set it.

The poll loop runs on its own thread and allocates nothing in the steady state, because a
collection pause inside a timing assertion is a test that fails for no reproducible reason.

**The synchronisation poll is not optional.** After power-up and after every loop break a machine
ignores polls addressed to itself until it has seen a general poll of address zero, so the engine
emits one ahead of every poll. The wire above shows it doing so.

### The protocol model

The catalogue is **generated** from the platform library's own enumerations —
124 long polls, 122 exceptions, 196 meters — by `tools/protogen/protogen.py`, and committed. It
cannot drift from the library: a renamed command shows up as a diff.

On top of it are **21 message layouts**, each read from the library's handler for that command and
naming it, so a reviewer can check the claim in one step. The layout model carries the four things
a display-only model cannot express: arrays, conditional presence, byte order and units, plus
constraints. Byte order is not decoration — the game options field in the identification reply is
little-endian while almost everything else is not, and reading it the other way produces a
plausible wrong answer.

The gap between 124 and 21 is the honest part. A command in the catalogue with no layout reports
as **known but undecoded**, so a test that needs it is skipped with that reason rather than run
against a guess.

Two rules run through the decoder. Bytes that cannot be read the way the layout says stop the
decode and name the byte. A value that reads fine but breaks its constraint does **not** stop it:
the value is reported with the reason it is wrong, so a test asserting on another field still gets
an answer.

### The test language

One statement per line, no build step, and every name resolved at parse time against the
generated catalogue:

```
test "a twenty raises its exception and moves its meters" {
  requires stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm bill insert 2000
  host expect exception bill_accepted_20 within 5s
  host expect meter total_bill_in += 2000 within 2s
  host expect meter bills_20 += 1 within 2s
}
```

Anything the parser does not recognise is an error naming the line and what was expected there. A
step silently ignored would be a test that passes without testing anything, which is the worst
failure a runner has. So a misspelled exception, a meter this build cannot read, a field a reply
does not hold, and a connect step with no address are all refused before a link is opened.

**YAML is the same language.** A YAML document carries the name, requirements and labels as data,
and each step is a line of the identical grammar parsed by the identical parser. Two notations
only earn their keep if neither can drift from the other; a test proves both forms of the same
test reach the same verdict.

`+= 2000` is measured against a baseline taken immediately before each stimulus step, which is
what makes it mean "two thousand more than before the note went in".

### Capability requirements

Each test declares what it needs; the runner probes and **skips with a reason** when it is unmet.
Never a failure: failing a test for an environmental reason teaches people that red means nothing.

```
  SKIP  10.1.1 standard validation configuration
        egm.validation: this environment provides no 'egm.validation'
```

That test is committed under `tests/conformance/10.1/` on purpose. It asks for the validation
configuration, which nothing here can provide yet, and it reports as skipped — not passed, not
deleted, and not quietly missing from the suite.

### Reports

Self-contained HTML by default, with the same run embedded inside the page as line-delimited JSON
so the file a person reads and the file a tool reads cannot disagree. Also JUnit, Markdown and
JSON Lines, mixable: `--report html,junit`.

Each says what the run was carried over, **before** the results: the link, whether it is proven or
unverified, what was driving the machine, and any caveat. A page of green produced over a link
nobody has measured is a much weaker claim than the same page produced against hardware, and a
reader must not have to hunt for which one they are holding.

Exit codes: 0 passed, 1 failed, 2 the tool could not do what was asked, and **3 nothing was
actually tested**. A suite in which every test was skipped is not a success, and the workflow
checks that it exits 3.

### The software machine

`SasWorkbench.Egm` answers SAS properly over any transport. Its identification reply is
**byte-identical to the real machine's capture**, which is the test that keeps it comparable.
Anything it cannot answer it refuses: a fixture that invented a reply would be worse than useless,
because a test would come to depend on the invention.

It is a fixture, not the thing under test. The platform library is the machine, and where the two
differ the library is right by definition.

### The window

Avalonia, over the same session, layouts and decoder a script run uses — so whatever can be done
by hand is expressible as a test. It shows the live wire with the wakeup flag on each symbol, the
decoded fields of a reply, the meters, the exceptions with their meanings, and a script run with
its verdicts. The line under the connection bar says what the link is worth.

### Continuous integration

Windows and Linux from this milestone, so portability never rots. Nothing in the workflow needs
the platform or the machine library. It also runs the regression scripts through the command line
and checks that an untestable suite exits 3.

---

## Findings worth carrying forward

**The real machine reports an empty serial number.** Long poll `$54` came back as
`01 54 03 36 30 32 D0 8D`: a length of three, the revision `602`, and nothing after it. The
software machine reports a serial number by default. Any conformance test that asserts on a
machine serial number will need it configured first, and the length byte is the field that says
whether one is there.

**Startup exceptions are still not stable, and now they are visible.** This run began with
exception `$18` queued before the host had asked for anything. A test that starts by asserting an
empty exception queue will fail for reasons unrelated to what it is testing.

**A meter is what the library says is a meter.** The meters view was the thing that caught this:
it listed the amount owed on a pending handpay and the hopper level as meters, because both are
counted in credits or coins. Neither is one — the handpay amount is a status that exists only while
a handpay is outstanding, and the library keeps no meter code for the hopper level at all. Layouts
now mark meters explicitly, and a test asserts that every meter offered can actually be read.
Seventeen can.

**`--stimulus software` with `--tcp` is refused rather than ignored.** The software machine is
reached over a loopback link, so it cannot drive a machine on a socket. Handing back a backend
that did nothing would have let every assertion after a stimulus step check the machine's earlier
state and pass for the wrong reason.

---

## Deviations from the plan, and why

**`stimulus software` was added alongside `none`, `manual` and `qpm`.** D10 named three backends; this
is a fourth, and it is the direct consequence of D7 asking for the same scripts to run against
both peers. Nothing about D10 changes: `qpm` is still the unattended backend and is still not
implemented, so it provides nothing and says so.

**YAML steps are lines of the purpose-built language rather than a second notation.** D5 asked for
YAML alongside the DSL over one engine. One engine turned out to mean one grammar: structured YAML
step objects would have been a second syntax to keep in step with the first. The document supplies
the structure and metadata; the steps are the same lines.

**M1's stated finish line was `10.1.1` printing PASS.** It reports SKIP, with the reason, and that
is the correct outcome: the validation configuration commands are not modelled yet. What passes
instead is the regression suite, against the real machine, which exercises every layer the
milestone was for. The conformance corpus needs the ticket work.

---

## Constraints held

* `C:\Progetti\QSP` — not modified.
* `C:\Progetti\libsas` — not modified.
* `C:\Progetti\QSP_DemoOpenGL` — not modified in this milestone; the wire proof's branch and its
  already-gitignored `CMakeUserPresets.json` were reused as they stood.
* `libsas/utils/sashost`, `utils/poller`, `utils/hwconformance` — not read.
* No third-party specification text is reproduced. Every description in the product is ours, and
  the protocol model comes from the platform library's own headers and handlers.
* The machine's `nvram.bin` was backed up before the run to
  `C:\ProgramData\quixant\launchpad\nvram.bin.saswb-m1-backup`. Booting the demo changed it, as it
  always does; the backup is kept rather than restored, because the change is the machine's own
  bookkeeping and discarding it is not ours to decide.

---

## Where the numbers come from

| | |
|---|---|
| Unit tests | 242, all passing on Windows |
| Long polls catalogued | 124, generated from the library's enumerations |
| Replies that decode field by field | 21 |
| Meters readable by name | 17 |
| Exceptions catalogued | 122 |
| Committed scripts | 3, one of which is expected to skip |
