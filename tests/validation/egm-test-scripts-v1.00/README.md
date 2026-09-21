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

## Naming

A test is named with its section number first, so a verdict names the section: `3.4 loop break`.
Tags are `@egm-test-scripts` on everything, `@chapter-N`, and then the usual cross-cutting ones —
`@wire-only`, `@needs-stimulus`, `@destructive`, `@needs-a-person`.

## The documents themselves

`SAS test script/`, gitignored, and they stay on the user's machine. D4 applies: they decide what
is tested and not one word of what ships. Nothing in this tree reproduces their text.
