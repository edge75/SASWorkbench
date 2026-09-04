# Decisions

Twenty decisions taken with the user on 2026-09-03/04, before any code was written.
Each was put to them explicitly. **Do not silently revisit these.** If new evidence
contradicts one, say so and ask.

---

## The goal, in one sentence

**SasWorkbench turns the GSA SAS Toolkit conformance corpus into unattended regression
tests that run all-local on a laptop against QSP + libsas.** Replacing IGT's
`sastest.exe` is a side effect, not the objective.

The user's own words: *"Launch an unattended test that gives stimuli to both EGM and SAS
Host and see if the other part behaves well according to the SAS Specifications."*

A test drives **both ends**. The SAS host asserts; the EGM gets stimulated. A step like
"play 2 games" is explicitly *not* the SAS host's job.

---

## D1 — Test targets
TCP software EGM (primary) · real board over USB-serial · trace replay.
Explicitly **not** a real board wired to a CI runner.

## D2 — Simulator transport
**TCP carrying 9-bit SAS symbols**: little-endian `uint16` per symbol, bit 8 = wakeup,
bits 0–7 = data.

Rejected: a virtual COM pair (com0com). Reason: it transports 8-bit bytes, so the wakeup
bit is lost — and address-vs-data detection (`libsas_uart.cpp:231`) is the most
protocol-critical behaviour in SAS. Tests would pass while the real link was broken.

## D3 — Audience
All four: the user, other QSP/libsas devs, QA engineers, and customers/integrators.
Consequences: syntax must be readable without a manual; no build step to run a test;
errors must name the line and say what was expected.

## D4 — Spec IP
**Our own words; protocol model derived from libsas's own source.** The SAS 6.02 PDF is
stamped *"IGT … Confidential and Proprietary / Distributed to Quixant LTD"*, and the app
may ship to customers, so no third-party spec text is redistributed. The spec and the GSA
XML are internal cross-references only.

## D5 — Script form
**Purpose-built DSL + YAML + a .NET library**, all over one engine.
YAML for simple linear flows, DSL when timing and assertions get real, C# when a dev needs
actual logic. Plus **GUI session recording** that emits a script.

## D6 — Hardware spike
**Deferred.** No serial hardware is available. Build against the TCP simulator. The serial
backend still ships, behind an honest capability probe, labelled unverified until measured.

## D7 — EGM peers
**Both.** A C# fake EGM for fast unit tests, and a libsas-backed bridge for real
integration. **Run the same scripts against both and diff the verdicts** — disagreement is
a genuine finding in one of them.

## D8 — V1 shape
**Thin vertical slice, CLI and GUI together.** Narrow but complete, so every layer is
exercised before anything is widened.

## D9 — Home
`C:\Progetti\SasWorkbench`. CLI binary `sasctl`. GUI `SasWorkbench`. Org `quixantplc`.

## D10 — Stimulus
**Pluggable `stimulus` step with swappable backends**: `manual` (prompts a human), `qpm`
(drives the simulator, unattended), `none` (**skips**, reported as skipped — *never*
silently passes).

## D11 — Orchestration
**One script with `egm` and `host` steps, one process, one clock.**
Rationale: SAS assertions are about timing relative to EGM events. Two processes means two
clocks and "within 5 seconds" becomes guesswork.

## D12 — Ticket printer (in `qpm_simulator`)
Printer panel rendering the ticket with a real barcode · a **wallet** so printed tickets can
be re-inserted to close the print→redeem loop · **JSON per ticket so tests assert on fields,
never on pixels** · and **import of externally-authored JSON tickets** for negative cases
(expired, wrong checksum, foreign machine, already redeemed).

## D13 — Run report
**Self-contained HTML by default**, with the raw event log embedded inside it so it stays
one file but remains machine-readable. `junit`, `md` and `jsonl` all available and mixable:
`sasctl run tests/ --report html,junit`.

## D14 — State reset
**All four, composed.** Relative assertions are the *default discipline* (`+= 2`, never
`== 2`, because SAS meters only accumulate). Plus NVRAM snapshot/restore, opt-in RAM clear
for tests that declare it, and baseline-diff to catch side effects nobody asserted.

## D15 — Change scope
Approved: `QSP_DemoOpenGL` build wiring · `qpm_simulator` (printer, wallet, external-control
mode, race fix) · a golden `nvram.bin` fixture · a deterministic-RNG option.

**Constraint from the user:** changes to `QSP_DemoOpenGL` live on a **branch** and in a
**dev-only preset** — no customer ever builds them. And: *"I don't want to change the QSP
repo at all."*

**How that is satisfied** — see `BLOCKERS.md`. Short version: the missing `libqxcom`
implementation lives in *this* repo; `CMakeUserPresets.json` is already gitignored
(`QSP_DemoOpenGL/.gitignore:131`) so the wiring is not a tracked file; and libsas is
*rebuilt* with an existing target, not edited.

## D16 — Channels
**First-class in the model, script syntax and reports from day one; ship 1, prove 2.**
libsas supports 4 (`SAS_CHANNEL_1..4`) with per-channel exception buffers and
`getAftControllingChannel()`. GSA 10.1.4 needs two. Retrofitting a channel dimension into a
poll engine built for one is painful, so carry it from the start even while unused.

## D17 — EGM role
**Mine `gsat.exe`'s 599 domain symbols into a behaviour/assertion checklist first**, then
decide. The codec is bidirectional anyway; the C# fake EGM stays an internal fixture —
libsas *is* the real EGM under test.

## D18 — Suite layout
`tests/conformance/<GSA id>/` for the official corpus (so "we pass 10.1.1–10.1.7" is a
sentence you can say) and `tests/regression/<feature>/` for our own. Tags cut across:
`@wire-only @needs-stimulus @destructive @slow @channel2 @requires-ram-clear`.

## D19 — Capability requirements
**Each test declares what it needs; the runner probes the environment and skips-with-reason
when unmet.** Never fail a test for an environmental reason — that trains people to ignore
red. Requirements are checked, so unlike tags they cannot go stale.

```
test "10.1.3 Cashout Ticket 1" {
  requires stimulus.bill
  requires stimulus.button.collect
  requires printer
  requires egm.validation == secure_enhanced
}
```

## D20 — Linux
**SasWorkbench builds and its tests run on Linux CI from M1** (matrix: windows-latest +
ubuntu-latest) so portability never rots. Deferred: the Linux serial backend (must use
native `termios`+`CMSPAR`, or **refuse** — `System.IO.Ports` silently no-ops MARK/SPACE
parity on Linux with no error), and the full rig on Linux (needs `qpm_simulator` headless).

---

## Cross-cutting principles the user endorsed

1. **Never silently pass.** An unavailable capability is a `SKIP` with a reason.
2. **Never silently degrade.** If the wakeup bit cannot be transmitted, refuse — do not
   drop it and carry on looking healthy.
3. **Assert on data, never on pixels.** Tickets are JSON for tests, images for humans.
4. **Honest labelling.** A green suite on unverified hardware must not look identical to a
   green suite on good hardware.
