# SasWorkbench

A scriptable SAS 6.02 **host** application — Avalonia GUI + `sasctl` CLI, C#/.NET — that
turns the GSA SAS Toolkit conformance corpus into unattended regression tests against
Quixant's QSP platform and libsas library, running all-local on a laptop.

**Read `.planning/DECISIONS.md` before proposing anything.** Twenty-nine decisions were taken
with the user — twenty before any code was written, nine more at the end of the design phase. Do not silently revisit them; if new evidence
contradicts one, say so and ask.

Then `.planning/BLOCKERS.md` (verified facts, traps, and the unblocking strategy) and
`.planning/PLAN.md` (milestones, riskiest-first).

---

## Hard constraints

1. **Never read, cite, or take design inspiration from `C:\Progetti\libsas\utils\sashost`**,
   nor `utils/poller` or `utils/hwconformance`. The user ruled these out explicitly, twice.
   Reading `libsas/src`, `libsas/include` and `libsas/test` is expected and fine.
2. **Do not edit tracked source in `C:\Progetti\QSP`.** The user: *"I don't want to change
   the QSP repo at all."* The strategy that satisfies this is in `BLOCKERS.md §2`.
3. **Changes to `C:\Progetti\QSP_DemoOpenGL` go on a branch and in a dev-only preset.**
   No customer build may include them. `CMakeUserPresets.json` is already gitignored
   (`.gitignore:131`) — prefer it to any tracked file.
4. **Do not redistribute third-party spec content.** The SAS 6.02 PDF is IGT
   confidential/"Distributed to Quixant LTD"; GSA's XML and `gsat.exe` symbols are GSA's.
   All descriptive text in this product is authored by us, with the protocol model derived
   from libsas's own headers. Use the others as internal cross-references only.
5. **`C:\Progetti\libsas\docs\sas-spec.txt` is unreliable — do not use it.** It is
   `pdftotext -layout` output and every message-layout table is **sheared**: description
   cells are taller than field rows, so a line-based extractor puts descriptions on the
   wrong rows. Verified against Table 8.2a. Extract from the PDF with **word coordinates**
   (`pdfplumber` `extract_words()`, cluster by x/y) — a working proof is in
   `research/tools/tbl_rebuild.py`.

## Principles the user endorsed

* **Never silently pass.** An unavailable capability is a `SKIP` with a reason.
* **Never silently degrade.** If the wakeup bit cannot be sent, refuse — don't drop it and
  look healthy.
* **Assert on data, never on pixels.** Tickets are JSON for tests, images for humans.
* **Honest labelling.** A green suite on unverified hardware must not look like a green
  suite on good hardware.
* **Verify, don't assume.** The user caught a corrupted spec extraction by intuition. Check
  claims against the bytes; state confidence honestly; mark UNKNOWN rather than guessing.

## Layout

```
.planning/         DECISIONS.md · BLOCKERS.md · PLAN.md          ← read these first
research/reports/  14 reverse-engineering reports (~700 KB)
research/tools/    Python decoders: PE/DFM/WinHelp/OLE2, .trc, .log, GSA .dat, PDF tables
research/artifacts/ extracted dumps, symbol tables, decoded traces
src/               SasWorkbench.Core · .Cli (sasctl) · .Gui (Avalonia) · .Egm (C# fake EGM)
tools/qxcom-tcp/   the missing libqxcom implementation, over TCP (M0)
tests/conformance/<GSA id>/   tests/regression/<feature>/
```

## Related repositories (read-only unless stated)

| Path | What it is |
|---|---|
| `C:\Progetti\libsas` | SAS 6.02 protocol library, **EGM side only**. The thing under test. |
| `C:\Progetti\QSP` | Gaming platform consuming libsas. **Do not edit.** |
| `C:\Progetti\QSP_DemoOpenGL` | The demo EGM app. Branch + dev preset only. |
| `C:\Progetti\qpm_simulator` | Peripheral/IO simulator, shared memory. Changes approved. |
| `C:\IGT3` | IGT `sastest.exe` — the legacy tool being replaced. Reference. |
| `C:\igs\GSA SAS Host Simulator v1.1` | GSA toolkit + conformance corpus. Reference. |

## Conventions

* Commit format `<type>: <description>` (feat, fix, refactor, docs, test, chore, perf, ci).
  **1-2 short sentences** (max 20 words each); a PR description may run to 4. No attribution
  lines and no chat-session links, ever.
* Changelogs, commit messages and PR descriptions describe **observable behaviour and public
  interfaces** — never internal class, method or file names.
* Comments and CHANGELOG entries: one sentence. Test comments may be longer.
* Before fixing a bug, find **all** call sites and show the list before editing.
* `PROGRESS.md` protocol: before each milestone/wave read it and skip completed entries;
  after each, append `PHASE:<id> WAVE:<id> STATUS:<complete|failed> TS:<ISO-8601-UTC>`.
