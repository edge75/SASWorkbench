# M4 — Why the machine stopped taking money

**Status: diagnosed, 2026-09-08. The cause is the machine's and cannot be fixed from here.**

`BLOCKERS.md` Trap D recorded that the note stimulus that M2 established no longer worked, that
everything on this side had been checked, and that it cost four real-time event payloads. It is now
chased to the bottom. The machine's developer licence is gone; a machine with no licence raises a
lockup; a locked-up machine disables its bill validator and returns every note before the money
handler ever sees it.

```
SASWB_QPM_TRACE=1                 make the peripheral boundary and the platform's own refusals
                                  reach the log
sasctl events --tcp 127.0.0.1:9760 --egm-resources <machine>/Resources
```

---

## What was measured

The whole chain, link by link, on `QSP_DemoOpenGL` with `fixtures/nvram/golden.nvram.gz` installed.

| | what was measured | where |
|---|---|---|
| 1 | the note reaches the machine: `note in escrow (id 14, argument 2000)` | the peripheral boundary, instrumented here |
| 2 | the machine answers *return*, and says why: **`Pending banknote while BV disabled`** | the platform's own trace |
| 3 | its bill validator is disabled on **every** evaluation, never once enabled | the platform's own trace, 44 calls in one run and 0 of them `(1)` |
| 4 | the condition doing it is **`Quixant License`**, raised at powerup and never removed | the platform's own trace |
| 5 | the licence key store was last written 2026-09-07 16:32 | the file's timestamp |

Step 3 is the one that settles it. A run with **no door touched, no audit cycle and an empty
condition set** still refuses money, and so does one where the host has just sent `$06` to enable
the acceptor and `$08` with every denomination bit set — both acknowledged by the machine. The gate
is not a door, not the host, and not the note's value: a dollar, a five and a twenty are all
returned.

## Why it cannot be fixed from here

* **The licence key is not in the memory image.** It lives in the peripheral manager's own secure
  storage file, so restoring the memory fixture — which this project does before every run, and
  which leaves the machine's memory byte-identical to the fixture — cannot bring it back.
* **A master reset clears it.** The platform says so in its own comment, and the store was rewritten
  at the hour the master reset noted in Trap D happened. That reset was recorded at the time as a
  thing ruled out; it was the cause.
* **No long poll clears the lockup.** Measured, not argued.
* **Only a person can.** The machine's audit menu, Licensing → Quixant License → *Enter New Key*,
  with a key from Quixant.

This is the case the instruction covered: it turned out to be theirs, so it is said rather than
worked around.

## The part that made it expensive, and what was built for it

The failure was a five-second timeout and nothing else. Three things were invisible:

* **The peripheral manager says nothing about a note.** It reports one to the machine and prints no
  line either way.
* **The platform's own account never reached this process's output.** Its logger drops everything
  below an error unless a game asks for more, and the demo never asks.
* **Nothing on the SAS wire says the machine is locked up.** The licence condition maps to the
  no-activity exception, so a host sees an ordinary quiet machine.

The first two are now fixable from here and are fixed. With `SASWB_QPM_TRACE=1` the substituted
peripheral manager prints every message crossing into the machine and every escrow decision coming
back, and asks the platform to raise its own trace level — by name at run time, so the peripheral
manager still does not depend on the platform and a renamed entry point degrades to one printed line
rather than a build failure. That is what turned this from a guess into five measured steps.

## What was tried and taken out again

**Reading the machine's fault light.** The platform commands a light tower to blink on a lockup, the
machine's own configuration says which output it is on, and the shared region carries thirty-two
lamp words — so an unattended run should have been able to see a locked-up machine and skip with
that reason instead of failing on a timeout. It was built, and then measured: **with the lockup
active and the platform tracing that it had commanded the candle to blink, every lamp word reads
zero**, from the raw bytes and over ten samples. A check that answers "not locked up" about a
locked-up machine is worse than no check, so it came out. `BLOCKERS.md §5` has been corrected: the
lamp array is a read-back channel into what the machine intends, but not for the light towers on
this build.

So an unattended run still cannot tell a locked-up machine from a broken stimulus. That is recorded
as a finding rather than papered over.

## What it still costs

`sasctl events` was run again after the diagnosis and produced **0 of 10** payloads — the same four
blocked by this, four nothing here drives, and two no machine running this library can raise.
`ConfirmedByCapture` is false on all ten and the test that asserts it still passes, which is what
that test is for.
