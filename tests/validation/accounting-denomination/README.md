# Accounting denomination

The machine-validation cases for the accounting denomination, written from the test plan and its
wire specification. One file per section of the plan, named as the plan names it, so "we pass
section 4" is a sentence somebody can say.

**The plan's own section 0 and section 1 are deliberately absent.** Those are the baseline capture
and the regression comparison at one cent, and both are diffs against a *previous* release rather
than assertions about the build under test. They are run by hand with two builds, not here.

## Where it stands

Thirteen of the forty-four cases run against the software machine; the rest skip naming what they need.
Nothing here has ever run against a real machine, because the unit a machine counts in is
established by a memory clear and nothing this build can do performs one.

## What a run of this suite means

Every case declares what it needs. A case whose requirement this environment cannot meet is
reported as a skip with the reason, never as a pass and never as a failure — an environment problem
reported as a defect trains people to ignore red.

A large part of the plan needs things this build cannot yet do unattended: clearing NVRAM, editing
the market configuration file, reading the audit pages, forcing a handpay, moving funds, awarding a
legacy bonus, or cycling power. Those cases are written out in full so the suite is the whole plan
rather than the convenient part of it, and each skips by name until the capability exists. The
report's coverage line is the honest measure: how many cases exist, how many ran, how many were
skipped and why.

## The two configurations

The plan runs at two settings, and every case says which it assumes — in the script rather than in
a comment, so that the software machine is built in it and a real machine is checked against it:

```
machine accounting denomination 10c
```

* **Configuration A** — the default, one cent. Every conversion is the identity.
* **Configuration B** — ten centavos, with the nine player denominations enabled.

The nine player denominations are not enumerated in the plan we were given, so no case declares
them and none of these cases exercises section 16's divisibility rule. `machine player
denominations` exists for when they are known. See `docs/M3-accounting-denomination.md`.

## Units, in one line

A value is in accounting units, in cents, in game credits, or a plain count, and exactly one of
those. Dividing a value that should not be divided under-reports money to the host; failing to
divide one that should be over-reports it. Both are defects and the plan weighs them equally.
