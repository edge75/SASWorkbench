# EGM test scripts, revision 1.00

The machine-validation suite written from the eleven GSA EGM test-script documents. One directory
per chapter, numbered as the documents number them — the chapters run 3 to 23 continuously across
the whole suite, so a chapter number names its document without ambiguity and "we pass chapter 11"
is a sentence somebody can say.

**The revision is in the path on purpose.** `tests/conformance/10.1/` holds cases from the GSA
toolkit corpus, which is this same suite at **revision 1.2**, and the section numbers moved between
the two: revision 1.2's 10.1 *Standard Cashout Validation* is 10.2 here, and its 10.2 *Secure
Enhanced Validation* is 10.5 here. Without the revision in the path, "section 10.1" names two
different things. See D39.

## Where it stands

612 cases across 21 chapters. Of those, 69 can run today against the software machine, about 364
need something that does not exist yet — overwhelmingly a stimulus rather than a decoder — and 179
need a person at a machine whatever gets built. Against a real machine the split is 69 / 64 / 479,
because configuring a machine, clearing its memory and cycling its power are buildable against the
software peer and are a person's job on a real one.

Those numbers are the starting estimate from reading the documents, not a claim about this tree.
**The tree's own coverage line is the measurement** — how many cases exist, how many ran, how many
skipped and why — and it moves on its own as the build grows. See `docs/M7-reading-the-documents.md`
for how the documents were read and what the estimate is worth.

### Written so far

Eight chapters, 327 cases, measured against the software machine (`run` counts passes and
failures together):

| ch | chapter | cases | run | skip | what blocks the most of them |
|---:|---|---:|---:|---:|---|
| 3 | Communications | 24 | 7 | 17 | the service button (7), host timing (2), a power cycle (2) |
| 4 | Required exceptions | 60 | 10 | 50 | a hopper (8), note faults (6), a power cycle (5), memory faults (5) |
| 5 | Required long polls | 27 | 13 | 14 | more than one hand pay held at once (4), legacy hand pay (3), a hopper (2), partial pay (2) |
| 6 | Required meters | 72 | 59 | 13 | comparing two replies (4), a coin (4), a credit limit (2); 7 of the 59 fail, 4 of them findings |
| 7 | Multi-game | 23 | 6 | 17 | a multi-game machine (13), choosing which game is played (11) |
| 9 | Multi-denomination | 24 | 2 | 22 | a multi-denomination machine (22), the $B0 preamble (15), adding replies together (11) |
| 10 | Tickets | 72 | 7 | 65 | secure-enhanced validation (17), a host validation sequence (8), restricted credits (5) |
| 11 | Real-time events | 25 | 7 | 18 | a power cycle (4), comparing two replies (3), a multi-denomination machine (3) |
| | **total** | **327** | **111** | **216** | |
| 3 | Communications | 24 | 3 | 21 | an exact host-driven poll sequence (11) |
| 4 | Required exceptions | 60 | 4 | 56 | a hand pay (10), note faults (6), memory faults (5) |
| 8 | Weighted average theoretical payback | 1 | 1 | 0 | the comparison with the operator screen is a note (D40) |
| 11 | Real-time events | 25 | 7 | 18 | an exact poll sequence (5), a power cycle (3) |
| 20 | Hand pay reset methods | 3 | 0 | 3 | the $94 decoder (2), the credit-meter reset key (1) |
| 21 | Miscellaneous control and legacy | 17 | 3 | 14 | a decoder for the command itself (12) |
| 23 | Legacy meters | 17 | 16 | 1 | restricted credit (1); comparing two replies is a note in 15 |
| | **total** | **109** | **14** | **95** | |

When the first three chapters were written, two host capabilities blocked twenty of their cases.
`host.poll_sequence`, an exact sequence of polls with nothing else on the line, has since been
built. `host.chirp`, watching for the machine's own address after five seconds of silence, has not.

    sasctl run tests/validation/egm-test-scripts-v1.00 --loopback --stimulus software

## What a run of this suite means

Every case declares what it needs. A case whose requirement this environment cannot meet is a skip
with the reason, never a pass and never a failure. A suite that skips half its cases must not look
like one that passed them, which is why the coverage line is reported as a measurement rather than
a claim.

This tree is deliberately **not** `tests/regression/`, which runs green with no skips at all. A
document-driven suite is mostly cases no build can run yet, so putting one there would either
destroy that invariant or force the suite to be trimmed to the convenient cases.

## The two halves of a meter case

142 cases assert a value read over the wire **and** that it matches the same meter on the machine's
own display. Nothing here can read a machine's screen, so each is written as the wire assertion
that runs plus a note carrying the comparison for a person (D40). The case is therefore the whole
of what the document asks, with the automatable half actually running.

**The same shape is used wherever a criterion is a person's to check and the rest of the case is
not** — a machine's playability after a shutdown poll, a denomination this acceptor does not take,
which poll carried an event. The criterion becomes a `note` and the case runs; the alternative is
a skip that stops covering something the document requires, which is the reason D40 gives for
rejecting it. One case is one `test`, always: a case whose *automatable* criteria cannot run here
at all skips by name rather than being split or dropped.

## One case, one test

The tree holds every case in the chapters it covers, including the ones nothing can run. A case
that needs something this environment does not have declares it and skips naming it, which is what
makes the coverage line above a measurement rather than an opinion — build the capability and the
cases that needed it start running without anything here being rewritten.

## Naming

A test is named with its section number first, so a verdict names the section: `3.4 loop break`.
Tags are `@egm-test-scripts` on everything, `@chapter-N`, and then the usual cross-cutting ones —
`@wire-only`, `@needs-stimulus`, `@destructive`, `@needs-a-person`.

## The documents themselves

`SAS test script/`, gitignored, and they stay on the user's machine. D4 applies: they decide what
is tested and not one word of what ships. Nothing in this tree reproduces their text.
