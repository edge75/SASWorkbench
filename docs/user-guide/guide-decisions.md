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

# Update for SasWorkbench 0.2.2: decisions (30-31/10/2026)

The two guides are brought to SasWorkbench 0.2.2 as version 1.1. The work order asked for the
`when` condition of `host send lp` and `sasctl nvram denomination`. Both exist only after the
v0.2.1 tag, so the product goes to 0.2.2, and the guides describe 0.2.2 as a whole. The decisions
were taken on 30/10/2026 in a grill-with-ui session.

## Terms

| Term | Meaning | Avoid |
| --- | --- | --- |
| held send | A `host send lp ... when <capability> == <value>` whose capability has another value: nothing is sent, the step passes with `not sent: ...`, and it is not an assertion. | skip; `NotSent` (that is a long-poll outcome) |
| fixture | A saved copy of the demo's memory (`fixtures/nvram/*.nvram.gz`), read, rewritten and restored by `sasctl nvram`. | snapshot; golden (for any fixture) |

## Why

The user merged #10 and #11 and asked for the EN/IT user guide to document `nvram denomination`
and the `when` clause, following the governance skill, in one pass, on a new branch, with a pull
request to `main`.

## Locked decisions

### Q1. The product goes to 0.2.2, and the guides say "from 0.2.2"

`Version` in `Directory.Build.props` goes from 0.2.1 to 0.2.2 in the same pull request. Merging it
publishes v0.2.2, because `release.yml` releases every push to `main` whose version has no tag.

- **Rejected, A: label the features "after 0.2.1, in a build of main".** It changed no version,
  but every label would have gone stale at the next release.
- **Rejected, C: no version label.** A reader with the 0.2.1 release would get a load error on
  `when` and "is not a memory action" on `denomination`.

### Q4. The guides describe 0.2.2 as a whole

A list "What changes in 0.2.2" carries every observable change since v0.2.1: `when` and
`nvram denomination` in full, and one line each for the layout-15 memories, the accounting unit
in `nvram show`, the poll-thread failure that stops polling with its reason, and the ticket-in
cases. The 0.2.1 history (serial fix, board measurement of 29/10) stays 0.2.1. The statements
about the current version are re-run with 0.2.2 and relabelled.

- **Rejected, B: stay on 0.2.1 and add only the two features.** The guide would have listed two
  of the release's changes and still described 0.2.1 as current.

### Q5. The guides become version 1.1

The files are renamed `_V1.1` with `git mv`. A new history row is added; the 1.0 row is unchanged.
This replaces the 29/10 decision "the guides stay 1.0, only the date changes", whose premise
(the guides never left this machine) ended when PR #6 put them in the repository.

- **Rejected, B: stay 1.0 with a new date and comment.** It rewrites the first-issue row and
  loses the trace of the 0.2.1 issue.

## Routine choices

- **Q2.** The two sentences that said 0.2.0 and 0.2.1 are unpublished are corrected in both
  guides: both releases are on GitHub since 30/10/2026, each with the Windows and Linux packages
  and `SHA256SUMS`. The board chapter's "both versions are local builds" stays, since it describes
  the bench of 29/10.
- **Q3.** No Graphify graph for this delta. Every new or relabelled claim is traced to source,
  unit test or run in a matrix kept outside the repository, in
  `C:/work/SasWorkbench/guide-build/reports/`.
- **Q6.** Word and PDF are rebuilt at the end, after the separate verification, into
  `C:/work/SasWorkbench/Guides`, and the denylist scan is re-run on the extracted text. They are
  not committed.

## Verified facts

- `when` closes `host send lp` in any form, after any `to <address>`, and only as
  `<capability> == <value>`; any other shape is a load error naming the expected form
  (VERIFIED_SOURCE, VERIFIED_RUNTIME with `--list`).
- The capabilities a `when` names that are asked of the machine are asked when the case starts,
  as for `requires`. Another value: nothing is sent, the step is PASS with
  `not sent: '<capability>' is '<x>', not '<y>'` and is not an assertion. No known value: the step
  fails (VERIFIED_SOURCE, unit tests, VERIFIED_RUNTIME on the software machine).
- `sasctl nvram denomination <amount>` takes `0.05`, `0,05` or `5c`, writes over the fixture or to
  `--out`, and prints the unit with its long poll `$1F` code. An amount `$1F` cannot report, such
  as 7c, is refused with exit code 2. It never touches the live memory (VERIFIED_RUNTIME).
- QSP v2.3.6.1 applies the unit only when its configuration enables
  `Sas.AccountingDenomination.Enable` and the unit divides every enabled game's denomination;
  otherwise it reports in cents (VERIFIED_SOURCE, as `sasctl` prints it; VERIFIED_RUNTIME on the
  demo on 30/10).
- v0.2.0 and v0.2.1 were published on 30/10/2026 at 05:46 UTC, each with both packages and
  `SHA256SUMS`; GitHub marks 0.2.0 as Latest (VERIFIED_LINK).

## Risks

- **Merging publishes.** The pull request's merge creates v0.2.2 on GitHub without a further step.
- **Two languages.** Every change is made in both guides; the separate verification compares them.

## Deferred

None.

## Open threads

- GitHub marks v0.2.0 as Latest. v0.2.2 will take that mark when it is published; changing it
  before then is an action on GitHub that needs the user's word.

## Applied (31/10/2026)

- **Rewrite and verification.** The Technical Writer applied the decisions to both guides; a
  separate agent then checked every changed line against the source and the 0.2.2 build. Its four
  defects were fixed in both languages: a present-tense sentence on the 29/10 bench, "from 0.2.1
  on" in troubleshooting, "unexpected" polling failures, and a missing line on the other changed
  test cases.
- **Re-runs with 0.2.2.** Tree and tag counts, `catalogue channels` and the sweep plan are the same
  as in 0.2.1 (VERIFIED_RUNTIME).
- **Word and PDF.** Rebuilt as V1.1, 74 pages each, with both history rows; the denylist scan of
  the extracted text finds nothing.
- **Still to confirm.** "Take release 0.2.2" is true only once the merge publishes v0.2.2.

## Confirmed (01/11/2026)

- v0.2.2 was published on 01/11/2026 at 06:09 UTC with both packages and `SHA256SUMS`, and GitHub
  now marks it Latest (VERIFIED_LINK). The Windows package matches its checksum and its `sasctl
  --version` prints `0.2.2` (VERIFIED_RUNTIME). "Take release 0.2.2" holds, and the open thread
  on Latest is closed.

# Guide 1.2 (09/11/2026)

## Decisions

- **The guides describe 0.2.3.** The steps and stimuli added after v0.2.2 made the "0.2.2"
  statements false for the code they now document. `Version` becomes 0.2.3, so merging publishes
  v0.2.3; a list "Compared with 0.2.2" leads the version section and the 0.2.2 list stays as history.
- **Version 1.2.** Files renamed `_V1.2` with `git mv`; one new history row, rows 1.0 and 1.1 unchanged.
- **Word and PDF** rebuilt as V1.2 into `C:/work/SasWorkbench/Guides`, not committed.

## Applied

- The Technical Writer rewrote both guides; a separate pass checked every changed line against the
  source and the 0.2.3 build. Four of its five defects were fixed in both languages; the fifth was
  contradicted by the test trees (8 tests still require `host.validation_sequence`).
- Word and PDF V1.2: EN 75 pages, IT 76; the denylist scan of the PDF text finds nothing; the twin
  check shows only the translated placeholder of `sasctl run`, as in 1.0 and 1.1.

## Open threads

- "Take release 0.2.3" holds only once the merge publishes v0.2.3.
- The accounting-unit rule in "qpm and nvram" is limited to the arrangement-15 fixtures (user, 09/11):
  with the arrangement-19 fixtures (1c and 2c games on) the v2.3.6.34 demo was recorded reporting
  5 cents, which this rule does not explain and the guide does not claim.

# Guide 1.3 (11/11/2026)

## Decisions

- **Version 1.3** (user, 11/11): files renamed `_V1.3` with `git mv`; one new history row, earlier rows unchanged.
- **nvman may be named** (user, 11/11): the `$32` paragraph names it in both languages.
- **Word and PDF** rebuilt as V1.3 into `C:/work/SasWorkbench/Guides`, not committed: EN 75 pages, IT 76; no denylist hit; twin check as in 1.2.

## Open threads

- **Version 0.2.4** (user, 11/11): the body describes 0.2.4, with a list "Compared with 0.2.3"; the 0.2.3 list stays as history.
- "Take release 0.2.4" holds only once the merge publishes v0.2.4.
