# SasWorkbench user guide: decisions (29/10/2026)

Two guides describe SasWorkbench 0.2.1. The first is a detailed Italian guide, 303-0XX-IT. The
second is its full English translation, 303-0XX. Both use the house template of the 303-030 and
303-033 Launchpad guides, are kept local for now, and carry version 1.0 DRAFT. DRAFT is dropped once
the board re-run of the serial fix is folded in. While the guides were being prepared, two defects
were found in the serial transport, and both are fixed on branch `serial-parity-fix` before the
guides describe serial links. The decisions were taken on 29/10/2026 in a grill-with-ui session.

## Terms

| Term | Meaning | Avoid |
| --- | --- | --- |
| 303-0XX | The English user guide in the 303-030/303-033 house template, with a temporary number, kept local for now. | customer guide, manual |
| Italian guide | The detailed guide in Italian (303-0XX-IT). The English guide is a full translation of it. | translation (for the Italian one) |
| software machine | The gaming machine built into SasWorkbench, used by the loopback runs. | fake EGM, simulator |
| board | Real Quixant hardware running QSP, reached over a serial port, such as the IQ-2 tested on 29/10. | real machine (in the repo docs that means the demo over TCP) |

## Why

The test team asked for a guide to using SasWorkbench in Markdown, Word and PDF. The guide had to
cover install, `sasctl probe`, `run`, `sweep` and `floor`, `--stimulus none` and `manual`, the
reports, the exit codes, the window, what not to use on a board, the per-channel category gates,
and how to read a wire trace. Its sources were chapter 5 of the weekly test guide and the first run
on a real board on 29/10.

The user then asked for:

- the guide in English, built with the same templates as 303-030 and 303-033;
- a temporary number, 303-0XX, because the guide stays local for now;
- a detailed guide in Italian as well.

## Locked decisions

### Q1. The English 303-0XX is a full translation of the Italian guide

Both guides have the same content. That includes reading a recorded run byte by byte, and the first
board measurement.

- **Rejected, A: 303-0XX covers use and outcomes only, with byte-level reading in the Italian guide
  alone.** This would have kept 303-0XX inside the SAS-detail rule of the 303 governance. The user
  preferred one content in two languages.
- **Rejected, B: a byte-level chapter in 303-0XX as a recorded exception to the rule.** This was
  superseded by C, which covers the same ground.
- **Consequence.** The governance rule against byte sequences and frames is set aside for these two
  guides, by the user's decision. All the other governance rules still apply to both guides: no
  customer, partner or site names, no ticket or PR identifiers, evidence for every claim, and a
  short revision history. The Italian guide is therefore written without customer or ticket names,
  so that its translation is clean.

### Q4. Fix the two serial-transport defects before the guides describe serial links

The fix goes on a branch and is unit-tested. TEST-GUIDE then re-runs the wire check and the sweep on
the IQ-2, and the guides describe the fixed build, 0.2.1.

- **Rejected, B: fix only the parity replacement.** That would leave long polls silent on the board
  the team is testing this week.
- **Rejected, C: change no code and document a known problem.** A guide would then document a serial
  link that does not work.

The complete list of changes was shown and approved before any code was written:

- `src/SasWorkbench.Core/Transport/SerialTransport.cs`, lines 98-105: parity replacement turned off.
- `src/SasWorkbench.Core/Transport/SerialTransport.cs`, lines 131-145: the line is drained onto the
  wire before every parity change.
- `src/SasWorkbench.Core/Transport/SerialTransport.cs`, lines 20-35: the comment corrected.

The callers are unchanged: HostSession.cs:164, ScriptEnvironment.cs:105 and :128,
SweepCommand.cs:294, FloorCommand.cs:575, ReplayCommand.cs:580, EventsCommand.cs:552 and
ProbeCommand.cs:76.

## Routine choices

- **Q2.** The Italian guide uses the same house template, with an Italian title and body and the
  number 303-0XX-IT. The template's labels, disclaimer and copyright stay in English.
- **Q3.** Figures are rendered with `tools/ui-specimen`, after its three hand-typed readouts are
  corrected to match the live window: the link claim, the POLL reply line, and the field units.
  The same figures are used in both guides.
- **Q5.** There is no fixed length limit. The aim is 35-45 pages per guide including figures, with
  the command and script-language references as appendices.
- **Q6.** The version is 1.0 DRAFT, dated 29/10/2026, author Alessio Brizi. The revision note says
  the guide is the first issue and describes SasWorkbench 0.2.1. DRAFT is dropped, with no version
  step, once the board re-run is folded in. Applied on 29/10/2026: the IQ-2 re-run confirmed the
  fix, so both guides are issued as 1.0.
- **Q7.** The Markdown and figures go in `C:/Progetti/SasWorkbench/docs/user-guide/`. The Word and
  PDF files go in their own folder, `C:/work/SasWorkbench/Guides`. Nothing is committed or pushed
  without the user's word.
- **Defaults taken (shown on the page).**
  - The cover title is "SasWorkbench User Guide"; the Italian title is "SasWorkbench Guida utente".
  - The page-2 product line reads "SasWorkbench" instead of "Launchpad".
  - Neither guide contains customer or ticket names.

## Verified facts

**Serial transport, 0.2.0 (VERIFIED_SOURCE)**

- The port opens at 19200 baud, 8 data bits, 1 stop bit and Space parity. The address byte is sent
  with Mark parity.
- Before this fix, the transport flushed and changed parity straight after queuing a byte, with no
  wait for the transmitter to empty.
- `ParityReplace` was never set. The .NET default replaces a byte that fails the parity check with
  `?` (0x3F). Setting it to 0 clears the Win32 error-character flag, and the setting survives later
  parity changes (`research/tools/SerialStream.Windows.cs:261-271, 290-305`).

**First board run, 29/10 (VERIFIED_RUNTIME, the board's own records)**

- Every general-poll reply reads `3F`: 5 of 5 in the wire check, 372 of 401 in the overnight
  regression and 76 of 78 in the overnight M7. The remaining replies are all long-poll replies.
- The sweep got answers to 7 of 85 commands, apparently at random, including multi-byte requests.
- The results of SW-1, SW-2 and SW-4 say nothing about QSP. TEST-GUIDE has marked them for a re-run.

**The serial fix (VERIFIED_BUILD, unit tests)**

- Four new tests: the line is drained before every parity change, a message that needs no parity
  change is not drained, the port is created with parity replacement off, and one character takes
  eleven bit times.
- The serial tests pass, 7 of 7.
- Board confirmation is pending.

**Template (VERIFIED_SOURCE)**

- 303-030 V1.14 and 303-033 V1.5 were built by syncing their Markdown into the previous Word file
  (`C:\work\qsp-docs-1.7.3\doc\tools\Publish-Guide.ps1` and `guide_sync.py`).
- A first version has no previous Word file. It is rendered with `mkdocx.py` from the
  qsp-demo-capture skill into a copy of 303-030 V1.14
  (`C:\Progetti\QSP\.claude\reports\QSP-documentation-review\word\303\`).
- The copy keeps elements 0-103: the cover, the revision history, the copyright and the table of
  contents.
- The PDF is exported with
  `C:\Progetti\launchpad-baseline-work\fase2\tools\export-docx-pdf.ps1`, which refreshes the fields
  and table of contents twice before exporting.

**Release state (VERIFIED_SOURCE / LINK)**

- 0.2.0 is not published. PR #4 is open; its Windows CI is green since the case-4.6 correction.
- The latest GitHub release is v0.1.0.

**Window (VERIFIED_SOURCE)**

- The visible navigation is the rail: POLL, METERS, EXCEPTIONS, SUITE, STIMULUS and RECORD. The
  instrument tab headers are hidden.
- There are no keyboard shortcuts.
- `Write report` always writes HTML.
- In the 0.2.1 build the guides were first written for, the `Follow` check box had no effect and
  the seal showed PROVEN with no link. Both are fixed in 0.2.1 since, and the guides say so.

**Script language (VERIFIED_SOURCE)**

- `host send lp ... data` takes the raw bytes after the command byte, with any length byte written
  by the author, and the CRC is added automatically.
- `sasctl run` always listens on TCP.
- There is no manual-step statement.
- YAML scripts are accepted, with limitations.

## Risks

- **The transmit fix is a timing estimate until the board re-run confirms it.** It waits for the
  queue to empty plus two character times. If the re-run still shows silent long polls, the next
  step is to measure the address byte's parity bit with a logic analyser.
- **The byte-level content is a governance exception.** The two guides carry byte-level SAS detail
  that other 303 guides do not. If either guide is ever shared beyond the team, that decision should
  be reviewed.
- **Two languages double the maintenance.** Every change has to be made in both guides.
- **The figures show a loopback session, not a board.**

## Deferred

None.

## Decisions after delivery (29/10/2026)

The user took these after the 1.0 build. The work order is in `.planning/HANDOFF-2026-10-29.md`.

- **Length.** The guides stay at 71-72 pages. The appendices are not cut.
- **Cover.** The LAUNCHPAD logo from the template stays.
- **Commit.** `docs/user-guide/` is committed on `serial-parity-fix`. Word and PDF stay outside the
  repository.
- **Product defects.** The defects found while writing the guide are fixed. The guide text that
  describes them as known issues is then updated in both languages.
- **TEST-GUIDE board facts.** Two facts from chapter 6 on the IQ-2 go into both guides:
  - **LP $73 code 00 at full length.** It goes in the script-language appendix as an AFT example.
  - **Silence during "audit exit".** It goes in the troubleshooting table.
- **Version.** The guides stay 1.0, because they have never left this machine. Only the date
  changes if they are rebuilt after 29/10.

## Open threads

- **Board runs.** The IQ-2 re-run with 0.2.1 is done: link check, sweep and the overnight suites.
  Still open:
  - floor run 4, after a power cycle;
  - the suites with `--stimulus manual`.
- **Test corrections.** F1-F4 and F5a/F5d are made on PR #4's branch, and its CI is green.
- **Push and PR.** Pushing `serial-parity-fix` and opening its PR are approved. Merging it, merging
  PR #4 and publishing the 0.2.1 release still need the user's word.

## Applied (29/10/2026, night)

- **Product defects.** Fixed in 0.2.1: option order and `--list` in `sasctl run`, `--out` without
  `--report`, two channels on one port, `Follow`, the narrowed STREAM, the seal with no link, the
  serial caveat, and the sweep's `$7B`. The guide text that called them known issues is replaced by
  the new behaviour, in both languages (VERIFIED_RUNTIME for the command line, VERIFIED_UI from the
  window specimen).
- **AFT example.** Added to Appendix B from the IQ-2 traces SA-T3-a and SA-T3-a2: the short
  initialisation left status `80`, the full one gave `00` then `01` (VERIFIED_RUNTIME), and libsas
  reads the asset number for codes `00`, `01` and `40` (VERIFIED_SOURCE).
- **Audit exit.** Added to the serial troubleshooting table from trace SA-T3-c2: general polls went
  unanswered from 81.6 s to 118.7 s, then `$6E`, `$12`, `$16` and `$83` arrived (VERIFIED_RUNTIME).
  The link goes `down` after 5 s of silence (VERIFIED_SOURCE).
- **Length.** The rebuilt guides are 73 pages (English) and 72 pages (Italian).
