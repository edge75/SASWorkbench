# SasWorkbench

A scriptable SAS 6.02 **host** for testing gaming machine software.

Avalonia GUI + a `sasctl` CLI over one engine. It polls an EGM, decodes what comes back,
drives AFT/TITO/progressives, and **asserts** — so a conformance test that used to take a
person four hours can run unattended and be re-run whenever you need it.

```
$ sasctl run tests/conformance/10.1 --tcp 127.0.0.1:7200 --report html,junit
  PASS  10.1.1 Standard Validation Configuration      (0.8s, 3 asserts)
  PASS  10.1.2 No Ticket Redemption                   (1.4s, 5 asserts)
  SKIP  10.1.3 Cashout Ticket 1
        env 'ci' provides no stimulus.bill
```

A test drives **both ends** — the SAS host *and* the EGM — on one clock:

```
# tests/conformance/10.1/10.1.3-cashout-ticket-1.sas
requires stimulus.bill, printer, egm.validation == secure_enhanced

host connect tcp 127.0.0.1:7200 address 01
host poll every 200ms

egm  bill insert 2000                     # $20, via the QPM simulator
host expect exception bill_accepted_20 within 5s
host expect meter total_bill_in += 2000

egm  play games=2 bet=100
host expect meter games_played += 2

egm  press collect
host expect exception ticket_printed within 10s
host send lp 3D
host expect field validation_number unique
```

## Status

**Design complete, no code yet.** The next step is M0 — see `.planning/PLAN.md`.

## Start here

| File | |
|---|---|
| `CLAUDE.md` | Hard constraints and conventions |
| `.planning/DECISIONS.md` | Twenty settled decisions and why |
| `.planning/BLOCKERS.md` | Verified blockers, traps, and the unblocking strategy |
| `.planning/PLAN.md` | Milestones, riskiest-first |
| `.planning/HANDOFF.md` | How to resume in a fresh session |
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
