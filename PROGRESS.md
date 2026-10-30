# Progress

Append one line per completed wave:
`PHASE:<id> WAVE:<id> STATUS:<complete|failed> TS:<ISO-8601-UTC>`

M0, M1, M1.5, M2 and M6 are complete — see `docs/`. M3 is in progress: the ticket path runs in
both directions and GSA 10.1.3 runs unattended against the software machine
(`docs/M3-tickets.md`), the memory images a real machine needs are committed, with GSA 10.1.1
passing against one (`docs/M3-golden-memory.md`), and a run now explains itself packet by packet
either as it runs or afterwards from its own record, with wall-clock time as the fourth timestamp
mode (`docs/M3-extended-log.md`), and a case can state the accounting denomination it assumes and
change one during a run, which starts six of the forty-four validation cases measuring
(`docs/M3-accounting-denomination.md`). M3 is complete.

M4 has started, and it opened with the flakiness of `BLOCKERS.md §4` rather than with a decoder: a
third defect of the one-behind family was found and fixed, and four concurrent assemblies now run
green ten times out of ten on an idle box (`docs/one-behind.md`). Then the importer: traffic nobody
here authored is read into the record a run writes, and the coverage tally falls from 10 of 10
distinct long polls over our own suite to 9 of 21 over the corpus (`docs/M4-import.md`). Then the
backlog that produced: eight layouts established from the library's own handlers and verified
against named captures, and three commands that turned out never to have been sent — bytes read
out of pieces of other messages, which are now named as unreadable rather than decoded. The corpus
tally goes to 18 of 18 and the catalogue to 36 of 124 replies (`docs/M4-layouts.md`). Then the
overlay diff: the legacy tester's configuration and the conformance toolkit's templates are read as
overlays over the header-derived catalogue and diffed, with nothing imported from either. No
coverage figure moves, because a diff establishes no layout; it produces 86 commands for the backlog
that no traffic here has carried, and closes two disagreements against the library — one of which
is a defect here, in how a host message that carries no data is framed (`docs/M4-overlays.md`).
Then that defect, fixed and measured on the wire: whether the host's message ends in a check
sequence is a property of the command, read out of two library sources that agree on all 124, and a
real machine answers a two-byte $01 with silence rather than the refusal the handler predicted.
Then the sweep: sasctl asks a real machine every command in the catalogue that can be asked, 41 are
withheld by name with what each would have changed, and 47 of the backlog's 86 become layouts
checked against the bytes that machine sent — the catalogue goes from 36 to 83 of 124 replies and
the backlog from 86 to 39 (`docs/M4-sweep.md`).
Then the real-time event format, decoded and measured: a machine reporting in real time answers a
general poll with a whole message, eleven distinct exceptions arrived in that format from a real
machine, and ten payloads are established from the library's own builders — none of which any run
here has produced, each recorded as unproduced with the reason. Our own suite goes to 11 of 11
distinct long polls and 4 of 4 distinct events; the corpus stays at 18 of 18 and carries no events.
Two blockers came out of it (`docs/M4-events.md`, `BLOCKERS.md` Traps D and E).
Then the meter index, widened: a script reads forty meters by name rather than sixteen, and no name
that already existed changed the command it comes from. The reason three waves deferred it was
discharged by measuring rather than by reasoning - the generator now reads out of the library which
commands report which meters and which of two reading paths each takes, and the software machine
models the rule, so one note reads as 1 through the bulk reply and 0 through the dedicated count on
a machine in the library's default meter mode. On the machine under test it cannot arise: the
platform sets full manual. Our own suite goes from 37 to 57 distinct long polls explained and
`tests/regression` from 28 cases to 33, still with no skips (`docs/M4-meter-index.md`).

M5 and the first eight waves of M7 are recorded below. In October a bridge put the host on the
virtual SAS port of an unmodified HAL build, and the note acceptor's tickets moved into QPM2
(`tools/qxcom-hal-bridge/README.md`). On 2026-10-17 the baseline moved to libsas v5.4.5.13, QSP
v2.3.5.32 and QPM2 pull request 218 (`fixtures/pinned-versions.json`): three more long polls are
channel-gated, a sweep of the demo decodes every reply it gets, and ticket-in works end to end. The
committed memory fixtures are arrangement 7 and the new QSP writes 13, so the demo now runs on a
memory configured by hand until a new golden one is made. On 2026-10-29 SasWorkbench first ran on a
Quixant board over a serial port. Two serial-transport defects were found, fixed in 0.2.1 and
confirmed on the board. A user guide was also written in Italian and English (`docs/user-guide/`).
The next steps are in `.planning/HANDOFF-2026-10-29.md`.

PHASE:M0 WAVE:W1-transport STATUS:complete TS:2026-09-04T00:04:14Z
PHASE:M0 WAVE:W2-build-wiring STATUS:complete TS:2026-09-04T00:18:42Z
PHASE:M0 WAVE:W3-wire-proof STATUS:complete TS:2026-09-04T00:26:30Z
PHASE:M1 WAVE:W1-core-wire STATUS:complete TS:2026-09-04T06:08:49Z
PHASE:M1 WAVE:W2-protocol-model STATUS:complete TS:2026-09-04T06:20:34Z
PHASE:M1 WAVE:W3-fake-egm STATUS:complete TS:2026-09-04T06:25:32Z
PHASE:M1 WAVE:W4-script-engine STATUS:complete TS:2026-09-04T06:46:06Z
PHASE:M1 WAVE:W5-cli-and-reports STATUS:complete TS:2026-09-04T06:58:26Z
PHASE:M1 WAVE:W7-ci STATUS:complete TS:2026-09-04T07:00:50Z
PHASE:M1 WAVE:W6-gui STATUS:complete TS:2026-09-04T07:31:04Z
PHASE:M6 WAVE:W1-release-pipeline STATUS:complete TS:2026-09-04T14:12:42Z
PHASE:M1.5 WAVE:W1-tokens-and-chips STATUS:complete TS:2026-09-04T14:45:05Z
PHASE:M1.5 WAVE:W2-shell STATUS:complete TS:2026-09-04T14:54:13Z
PHASE:M1.5 WAVE:W3-session-model STATUS:complete TS:2026-09-04T15:16:16Z
PHASE:M1.5 WAVE:W4-session-stream STATUS:complete TS:2026-09-04T15:29:58Z
PHASE:M1.5 WAVE:W5-suite-instrument STATUS:complete TS:2026-09-04T15:45:12Z
PHASE:M1.5 WAVE:W6-report-and-controls STATUS:complete TS:2026-09-04T16:13:09Z
PHASE:M2 WAVE:W1-qpm-driver STATUS:complete TS:2026-09-05T05:24:02Z
PHASE:M2 WAVE:W2-egm-config-and-doors STATUS:complete TS:2026-09-05T05:37:52Z
PHASE:M2 WAVE:W3-operator-commands STATUS:complete TS:2026-09-05T05:48:42Z
PHASE:M2 WAVE:W4-window-and-forget STATUS:complete TS:2026-09-05T06:05:51Z
PHASE:M2 WAVE:W5-docs STATUS:complete TS:2026-09-05T06:08:34Z
PHASE:M2 WAVE:W6-note-and-fixture STATUS:complete TS:2026-09-05T06:54:38Z
PHASE:M3 WAVE:W0-one-behind STATUS:complete TS:2026-09-05T12:40:00Z
PHASE:M3 WAVE:W0-selected-meters STATUS:complete TS:2026-09-05T13:30:00Z
PHASE:M3 WAVE:W0-accounting-denomination-suite STATUS:complete TS:2026-09-05T14:05:00Z
PHASE:M3 WAVE:W0-trace-search STATUS:complete TS:2026-09-05T15:10:00Z
PHASE:M3 WAVE:W0-unit-defects STATUS:complete TS:2026-09-05T14:35:00Z
PHASE:M3 WAVE:W1-mask-race STATUS:complete TS:2026-09-05T18:07:14Z
PHASE:M3 WAVE:W2-ticket-model STATUS:complete TS:2026-09-06T06:07:48Z
PHASE:M3 WAVE:W3-printer-panel STATUS:complete TS:2026-09-06T06:07:48Z
PHASE:M3 WAVE:W4-ticket-out STATUS:complete TS:2026-09-06T07:40:43Z
PHASE:M3 WAVE:W5-ticket-in STATUS:complete TS:2026-09-06T10:20:00Z
PHASE:M3 WAVE:W6-cashout-ticket-info STATUS:complete TS:2026-09-06T19:48:51Z
PHASE:M3 WAVE:W7-golden-nvram STATUS:complete TS:2026-09-06T21:51:44Z
PHASE:M3 WAVE:W8-extended-log STATUS:complete TS:2026-09-07T08:35:00Z
PHASE:M3 WAVE:W9-accounting-denomination STATUS:complete TS:2026-09-07T11:45:55Z
PHASE:M4 WAVE:W0-attribution STATUS:complete TS:2026-09-07T18:05:00Z
PHASE:M4 WAVE:W1-capture-import STATUS:complete TS:2026-09-07T19:20:00Z
PHASE:M4 WAVE:W2-backlog-layouts STATUS:complete TS:2026-09-07T22:10:00Z
PHASE:M4 WAVE:W3-overlay-diff STATUS:complete TS:2026-09-07T22:44:48Z
PHASE:M4 WAVE:W4-check-sequence STATUS:complete TS:2026-09-08T07:55:00Z
PHASE:M4 WAVE:W5-catalogue-sweep STATUS:complete TS:2026-09-08T06:50:04Z
PHASE:M4 WAVE:W6-real-time-events STATUS:complete TS:2026-09-08T12:19:10Z
PHASE:M4 WAVE:W7-software-machine-values STATUS:complete TS:2026-09-08T18:04:18Z
PHASE:M4 WAVE:W8-note-stimulus-diagnosis STATUS:complete TS:2026-09-08T19:00:32Z
PHASE:M4 WAVE:W9-meter-index STATUS:complete TS:2026-09-09T00:39:17Z
PHASE:M4 WAVE:W10-replay-and-transactions STATUS:complete TS:2026-09-09T07:05:00Z
PHASE:M4 WAVE:W11-channel-map STATUS:complete TS:2026-09-09T09:35:00Z
PHASE:M4 WAVE:W11-recording-and-channels STATUS:complete TS:2026-09-09T11:35:00Z
PHASE:M4 WAVE:W12-catalogue-breadth STATUS:complete TS:2026-09-12T23:32:47Z
PHASE:M5 WAVE:W0-readiness-and-poll-cycle STATUS:complete TS:2026-09-13T19:06:23Z
PHASE:M5 WAVE:W1-map-into-the-record STATUS:complete TS:2026-09-13T20:42:49Z
PHASE:M5 WAVE:W2-round-of-addresses STATUS:complete TS:2026-09-13T21:05:37Z
PHASE:M5 WAVE:W3-machine-on-the-record STATUS:complete TS:2026-09-13T21:56:28Z
PHASE:M5 WAVE:W4-container-log STATUS:complete TS:2026-09-13T22:20:13Z
PHASE:M5 WAVE:W5-both-axes-and-floor-stimulus STATUS:complete TS:2026-09-14T05:51:07Z
PHASE:M7 WAVE:W0-read-the-documents STATUS:complete TS:2026-09-21T21:37:15Z
PHASE:M7 WAVE:W1-unsupported-poll-is-silence STATUS:complete TS:2026-09-21T22:29:50Z
PHASE:M7 WAVE:W2-chapter-3-communications STATUS:complete TS:2026-09-21T23:24:41Z
PHASE:M7 WAVE:W3-chapter-4-required-exceptions STATUS:complete TS:2026-09-21T23:24:41Z
PHASE:M7 WAVE:W4-chapter-11-real-time-events STATUS:complete TS:2026-09-21T23:24:41Z
PHASE:M7 WAVE:W5-long-poll-answered-with-an-event STATUS:complete TS:2026-09-22T18:48:23Z
PHASE:M7 WAVE:W6-poll-sequence STATUS:complete TS:2026-09-28T21:58:50Z
PHASE:M7 WAVE:W7-chapter-10-ticketing STATUS:complete TS:2026-09-28T22:20:38Z
PHASE:M7 WAVE:W8-baseline-libsas-5.4.5.13 STATUS:complete TS:2026-10-17T06:57:38Z
PHASE:M7 WAVE:W9-serial-on-a-board STATUS:complete TS:2026-10-29T13:46:18Z
PHASE:DOC WAVE:W1-user-guide-it-en-1.0 STATUS:complete TS:2026-10-29T18:31:04Z
PHASE:R1 WAVE:A-test-fixes-F1-F5 STATUS:complete TS:2026-10-29T19:42:05Z
PHASE:R1 WAVE:B-rebase-serial-parity-fix STATUS:complete TS:2026-10-29T19:42:05Z
PHASE:R1 WAVE:C-code-tests-specimen STATUS:complete TS:2026-10-29T20:13:45Z
PHASE:R2 WAVE:C-commit-and-board-build STATUS:complete TS:2026-10-29T21:09:38Z
PHASE:R2 WAVE:D-guides-it-en STATUS:complete TS:2026-10-29T21:30:25Z
PHASE:R2 WAVE:E-commit-user-guide STATUS:complete TS:2026-10-29T21:50:15Z
PHASE:R2 WAVE:F-push-and-pr-6 STATUS:complete TS:2026-10-29T21:50:15Z
PHASE:R3 WAVE:R1-R3-link-faults-and-sequence STATUS:complete TS:2026-10-29T22:49:16Z
PHASE:R3 WAVE:repin-libsas-5.4.6.4-pin-and-catalogue STATUS:complete TS:2026-10-29T22:54:03Z
PHASE:R4 WAVE:R4-poll-thread-catch-all STATUS:complete TS:2026-10-30T06:15:50Z
PHASE:R6 WAVE:demo-build-qsp-2.3.6.1-libsas-5.4.6.4 STATUS:complete TS:2026-10-30T12:09:04Z
PHASE:R6 WAVE:demo-measure-vs-2026-10-17 STATUS:failed TS:2026-10-30T13:32:41Z
