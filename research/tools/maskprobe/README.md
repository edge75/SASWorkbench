# maskprobe — measuring the peripheral-state race

The only instrument that can check Trap B, the race in `qpm_simulator` that published a
peripheral mask one bit at a time and so let another process observe states no door was ever
in. The defect and the numbers this probe produced are recorded in
`.planning/BLOCKERS.md §3`; this directory is the thing that produced them, kept so the check
can be repeated rather than re-derived.

It is a measuring instrument, not a test. It lives here with the other instruments and is
deliberately **not** in `SasWorkbench.slnx`: it is Windows-only, and CI builds the solution by
name, so nothing picks it up.

## What it measures

It samples one 32-bit field of the shared peripheral region as fast as a core will go, and
reports every distinct value it saw, how many times that value was entered, and **how long each
one was actually held**.

Residency is what separates a fingertip from a glitch, and it needs no judgement call. A person
ticking a checkbox holds the new value for hundreds of milliseconds. A mask being cleared and
rebuilt in place holds an intermediate value for well under a microsecond. Six orders of
magnitude apart, so any value with a mean hold under a millisecond is reported as a glitch.

A mask published in one aligned store shows exactly one value. A mask rebuilt in place shows
extra values with tiny residency.

## Why it proves a writer is alive before it believes anything

The region is a **file** that persists after its writer exits (`persist=true`, see
`BLOCKERS.md §3`), so a closed simulator and a well-behaved one look identical: both give a
constant value, which is exactly what a clean run looks like. One measurement run was voided by
precisely that before this check existed.

So liveness is proved rather than assumed. The probe writes a sentinel into the field and waits
for the simulator's own 60 Hz rewrite to erase it — Trap A, the unconditional clobber, used here
as the signal that a writer is present. It checks **before and after** sampling, so a simulator
that dies midway cannot be reported as a clean run.

## Running it

The simulator must be running, and **a person must tick at least one door**: nothing else drives
the intrusion mask, so with every box unticked the mask is a constant zero and there is nothing
to measure. The probe waits up to 180 s for a non-zero value and then gives up rather than
reporting a clean run it never made.

```
dotnet run -c Release --project research/tools/maskprobe -- <seconds> <label>
```

Both arguments are optional — 10 seconds and `run` by default. Tick the doors, leave the window
alone, and let it sample.

| Exit code | Meaning |
|---|---|
| `0` | clean — every value observed was held long enough to be a deliberate change |
| `1` | race — at least one value was held for under a millisecond, which no hand can produce |
| `2` | no signal — the mask stayed zero, so nobody ticked anything |
| `3` | no writer — the sentinel survived, so the region is a stale file and nothing was measured |

It reads the **intrusion mask** at offset `0x004`. The other three stimulus masks are the same
shape at their own offsets (`dio` `0x008`, `bna` `0x08C`, `printer` `0x090`); point
`IntrusionOffset` at one of those to measure it instead.

## What it found, and what it does not establish

Measured 2026-09-06 with four doors ticked, on both binaries built from the same preset: the
pre-fix build showed 10 transitions and 3 distinct values over 10 s, including `0x0007` — a
*partial* mask, three doors shut and one still open, which is a state that never existed. The
fixed build showed 0 transitions and 1 value over 20 s and 50 billion samples.

**It establishes no rate.** The probe under-observes badly, because the writer holds the cache
line while it makes its stores and most of the reader's loads are serviced only after the burst.
It caught a bad value in roughly 5 of ~600 frames. The measurement shows the intermediate states
are real and observable from another process, which is what the fix removes; it does not support
a claim of one spurious door event every N hours.

## Why there is no unattended regression test for this

Because the masks are all zero until a person ticks a checkbox, and `qpm_simulator` has no
external-control mode driving them. Until something else can drive the mask, this check needs a
human, which is why it is an instrument here and not a case under `tests/`.
