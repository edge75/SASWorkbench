# SasWorkbench

A scriptable SAS 6.02 **host** for testing gaming machine software.

A desktop window and a `sasctl` command line over one engine. It polls a gaming machine,
decodes what comes back, and **asserts** — so a conformance test that used to take a person four
hours can run unattended and be re-run whenever you need it.

```
$ sasctl run tests/conformance/10.1 --tcp 127.0.0.1:7200 --report html,junit
  SKIP  10.1.1 standard validation configuration
        egm.validation: this environment provides no 'egm.validation'

  over tcp 127.0.0.1:7200 (listen), proven, stimulus none
  SKIP  0 passed, 1 skipped in 0.9s
```

That is what it prints today, and the exit code is 3 rather than 0. A run in which nothing could
be tested is not a success, and this tool will not dress one up as one. AFT, tickets and
progressives are catalogued but not yet decoded, so a test that needs them is skipped with that
reason — see `docs/M1-thin-vertical-slice.md`.

A test drives **both ends** — the SAS host *and* the machine — on one clock:

```
# tests/conformance/10.1/10.1.3-cashout-ticket-1.sas
requires stimulus.bill, printer, egm.validation == secure_enhanced

host connect tcp 127.0.0.1:7200 address 01
host poll every 200ms

egm  bill insert 2000                     # $20
host expect exception bill_accepted_20 within 5s
host expect meter total_bill_in += 2000

egm  play games=2 bet=100
host expect meter games_played += 2

egm  press collect                        # skipped until the simulator drives buttons
host expect exception ticket_printed within 10s
```

## Status

**M1 done.** A script runs unattended against a real SAS 6.02 gaming machine over a link that
carries the wakeup bit, asserts on the decoded reply, and writes a self-contained report. The
conformance corpus needs the ticket work before it can run; until then those tests report as
skipped, with the reason. Next is M2 — see `.planning/PLAN.md`.

```
$ sasctl run tests/regression --loopback --stimulus software
  PASS  a twenty raises its exception and moves its meters (2.6s, 7 assert(s))
  PASS  the link comes up and the machine identifies itself (1.0s, 5 assert(s))

  over loopback (host end), proven, stimulus software
  PASS  2 passed in 3.6s
```

| | |
|---|---|
| Long polls catalogued, from the machine library's own headers | 124 |
| Replies that decode field by field | 21 |
| Meters readable by name | 17 |
| Exceptions catalogued | 122 |
| Unit tests | 242 |

## Try it

```
sasctl run tests/regression --loopback --stimulus software   # against the software machine
sasctl egm --tcp 127.0.0.1:9760 --dial                       # run that machine as a peer
sasctl run tests/regression --tcp 127.0.0.1:9760             # and point the host at it
sasctl probe                                                 # what can this machine do?
sasctl catalogue layouts                                     # every reply we can decode
```

`SasWorkbench` is the same engine with a window: the live wire with the wakeup flag on each
symbol, a reply decoded field by field, the meters, the exceptions, and a script run.

## Start here

| File | |
|---|---|
| `CLAUDE.md` | Hard constraints and conventions |
| `.planning/DECISIONS.md` | Twenty settled decisions and why |
| `.planning/BLOCKERS.md` | Verified blockers, traps, and the unblocking strategy |
| `.planning/PLAN.md` | Milestones, riskiest-first |
| `docs/M0-prove-the-wire.md` | The wire proof, with the hex dump |
| `docs/M1-thin-vertical-slice.md` | What M1 built, and what it found |
| `research/reports/` | 14 reverse-engineering reports |

## Why it exists

The two tools this replaces both fail, for reasons now established at instruction level:

**IGT `sastest.exe`** (2005) opens serial ports from a hard-coded table of `"COM1:".."COM8:"`,
indexed unchecked. `\\.\` appears zero times in the binary, so COM10+ — where USB adapters
land — is unreachable. It has no command-line interface, no switches, and its `[Script]`
feature is a list of hex frames with no loops, waits, assertions or verdicts. Its own help
file calls it *"a prototype test program… never meant for release."*

**GSA SAS Host Simulator v1.1** (2006) doesn't speak SAS at all — an MFC GUI drives a DOS
program (`gsat.exe`) over a second serial link using a private protocol, which is why it
needs DOSBox and a pair of COM ports. Also no automation entry point.

Neither can be scripted. This one is built engine-first, so anything you can do by hand is
expressible as a test.
