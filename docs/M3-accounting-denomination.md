# The accounting denomination in a script

**Status: done, 2026-09-07.** A case can state the accounting denomination it assumes, and a step
can change one during a run. Six of the forty-four cases in
`tests/validation/accounting-denomination/` now run against the software machine; before this they
all skipped. This is D36, and the last item in M3.

```
  PASS  AD-16 the credits meters are reported in accounting units
  PASS  AD-25 ticket amounts stay in cents
  PASS  AD-29 counts stay counts
  PASS  AD-30 the payback percentage stays a percentage
  PASS  AD-38 a remainder on a meter fed in cents is normal
  PASS  AD-39 a meter near its limit is carried at full width

  PASS  6 passed, 38 skipped
```

Six, not the seven D36 estimated. The seventh — AD-37, truncation never rounds up — needs a wager
of twenty-five centavos, which needs a game credit worth twenty-five centavos, and nothing here
can say what a game credit is worth. It skips naming that rather than asserting a truncation it
never caused. The suite is not green and was never going to be: the other thirty-seven cases need
a memory clear, a power cycle, the configuration file, the audit pages, a handpay, transfers,
progressives, a bonus, a second game, or a decoder this build has not established yet.

---

## The two forms

### The declaration

```
test "AD-16 the credits meters are reported in accounting units" {
  machine accounting denomination 10c
  ...
}
```

`machine` describes; `egm` acts. That is the whole distinction, and it is deliberate — the two
words sit next to each other in a script and mean opposite things.

Against the software machine the declaration **builds a peer in that configuration**, before the
link opens. Against any other machine it **configures nothing**: the value is read back off the
wire and compared, so the case either matches and runs, or skips naming what the machine is
actually in force at.

```
SKIP  a case written for ten centavos
      egm.accounting_denomination: 'egm.accounting_denomination' is '5c', not '10c'
```

Never a silent reconfigure, and never a failure — a machine in another unit is an environment the
case was not written for, not a defect. That is D19 applied to a unit rather than to a capability,
and it is checked by the same path, with the same wording, as the validation style a machine
already answers for.

A denomination is written as a number of cents (`10c`) or as a Table C-4 code (`code 03`). A bare
number is refused: `3` is ten cents as a code and three cents as an amount, and the two readings
differ by more than a factor of three on every credit meter in the machine. A unit the protocol
cannot express as an accounting denomination — the five sub-cent codes, whose value in the
library's own table is zero — is refused while the script is read, rather than dividing by zero
later.

The player denominations can be declared alongside, and section 16's divisibility rule is then
enforced against them:

```
machine accounting denomination 10c
machine player denominations 10c 25c 50c
```

They are refused on their own, because without an accounting denomination they declare nothing a
machine can be built or checked against.

### The setter

```
egm set accounting denomination 10c
```

A stimulus like any other, which is what keeps it honest. Section 16 makes the unit a property a
memory clear establishes, so **no real machine can honour this** — and on one that cannot be
reconfigured the step refuses with a reason and the case skips, the same treatment a door nobody
has wired gets. The capability is `stimulus.accounting_denomination`, and only the software
machine offers it.

Two things it deliberately does not do:

* **It does not fall back.** A value section 16 disallows is refused outright rather than replaced
  with one cent. Falling back would leave the machine counting in a unit the script never asked
  for while every assertion after it looked healthy.
* **It does not rescale.** Values already counted stay in the unit they were counted in.
  Rescaling would either invent money or truncate it away, and a machine cannot be in two units at
  once. A case that wants a clean measurement declares its denomination instead of changing one.

---

## Which reply the declaration is checked against

**Long poll `$1F`, the machine identification reply, byte 7.**

Verified against the library's own handler and the bytes it writes, not against reasoning about
the specification. The handler that answers `$1F` fills its denomination byte straight from the
machine's stored accounting denomination. Exactly one other reply carries the same value — `$53`,
the per-game configuration reply, byte 9 — and it is the same stored byte, so the two cannot
disagree. Nothing else on the wire publishes it.

`$1F` is the one used because it needs no game number, is answered unconditionally, and is the
reply D36 calls the identification reply. `$53` has no decoder in this build yet, which is why
AD-09b — the case that asserts the two replies agree — still skips.

### And no, the player denomination cannot stand in for it

`$B1` answers the **current player denomination**: a different value, held per game, changing when
the player changes games, and **not sent at all** unless the machine's multi-denomination feature
bit is set — the handler returns without a reply. Reading it as the accounting denomination would
be wrong on a multi-denomination machine and would read silence on every machine that is not one.

One consequence worth recording: the software machine currently answers `$B1` with its accounting
denomination. On a single-denomination machine the two coincide, so nothing here is wrong today,
but the two are separate values and the fixture will need a player denomination of its own before
the multi-denomination cases (AD-33 to AD-36) can run.

---

## Why the declaration is not a `requires`

It reuses `requires`' machinery and is not one of them, for four reasons:

1. **`requires` cannot configure anything, by construction.** It is checked after the environment
   is built, and the factory that builds it is handed only the connect step and the run's options.
   D36 needs the software peer built *in* the declared configuration, which means the value has to
   reach the factory — so the factory signature grew a parameter, and `requires` would have had to
   become a thing that can mutate the machine to do the same job. Every requirement in every script
   would then be a potential reconfiguration.
2. **They are two different behaviours.** A requirement is a gate: unmet means skip. A declaration
   is a gate on one machine and a build instruction on another. `requires x == y` can only say the
   first.
3. **A requirement compares a string against a capability set.** A declaration carries a Table C-4
   code, a value in cents, and a list of player denominations, and has to refuse a unit no machine
   can be in force at while the script is being read.
4. **Keeping them apart keeps `requires` meaning one thing.**

What it does reuse is everything after the link is up. The declaration becomes a requirement the
machine itself answers — `egm.accounting_denomination`, alongside `egm.validation` — so an unmet
one is a skip with a reason and never a failure, and both peers are asked the same question the
same way rather than one being asked and the other being told what our own fixture is in.

The probe now asks only for what a case actually wants. A run whose cases never mention the
validation style no longer puts an enabled-features poll on the link, which keeps the extended
log's coverage tally about the traffic the suite authored.

---

## What the six cases found

They were written from a test plan meant for a person with a machine in front of them, so each
carried a `note` describing an action rather than the steps to perform it. Turning six of those
notes into steps found two things.

* **The software machine kept no machine-wide ticket totals.** A machine keeps ticket in and out
  in cents *and* in credits, and it is the pair that makes the accounting denomination visible at
  all — the cents meter reads the same at every unit while the credits meter moves with it. The
  fixture kept only the cents half, so AD-38 had nothing to measure. Both are kept now.
* **Two assertions could not measure what their case was for.** AD-29 asked only that the games
  played meter was not negative, and AD-39 that a meter was at least four bytes wide, which is its
  floor. A count can only be shown not to scale against a number somebody knows, and a field can
  only be shown to widen by exceeding its floor, so both now play a known number of games and
  assert the number. AD-25's note and its assertions disagreed about which of its two amounts was
  the ticket in and which the ticket out; the assertions were followed and the note corrected.

Two amounts in the plan turned out not to be reachable and were replaced with ones that are, and
the comment in each case says so. AD-16 funds five hundred pesos rather than a thousand, because a
machine that plays exactly what it is told needs no headroom.

---

## Reading it back

The declaration is recorded in a run's own record and rendered in the extended log, so a person
reading a run afterwards is told what the case assumed:

```
  assumes  accounting denomination 10c (code $03)   (declared at line 14)
```

The two renderings — live and from the record — stay byte-identical, which is what makes the
extended log a rendering of the record rather than a second capture.
