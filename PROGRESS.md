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
