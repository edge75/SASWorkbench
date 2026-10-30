# M4 — The rest of the catalogue, and why most of it is not work

**Status: done, 2026-09-13.** The last thing `PLAN.md` asked of M4: the long polls the catalogue
still could not decode field by field, and the event payloads a locked-up machine had blocked.

```
sasctl catalogue undecoded                       every command with no layout, and what stands
                                                 in the way of each
sasctl sweep --tcp 127.0.0.1:9760                two more commands are now asked, and answer
sasctl run tests/regression/machine/read-only-forms-of-two-setters.sas --loopback --stimulus software
sasctl events --tcp 127.0.0.1:9760 --egm-resources <machine>/Resources
```

---

## The numbers, before and after

| | before | after |
|---|---|---|
| the catalogue, replies with a layout | 83 of 124 | **85 of 124** |
| of those, confirmed against bytes a machine sent | 57 | **59** |
| the catalogue, event payloads **confirmed against a capture** | 0 of 10 | **5 of 10** |
| the catalogue, meters readable by name | 40 | 40 |
| our own suite, distinct long polls explained | 57 of 57 (100%) | **59 of 59 (100%)** |
| our own suite, packets | 1,673 | **1,689** |
| our own suite, cases | 50 pass · 39 skip | **52 pass · 39 skip** |
| the imported corpus, distinct long polls | 18 of 18 (100%) | 18 of 18 (100%) |
| the imported corpus, packets | 1,128,939 | 1,128,939 |
| a sweep of the software machine | 66 answered · 2 silent · 41 withheld | **68 answered · 2 silent · 39 withheld** |
| a sweep of `QSP_DemoOpenGL` | 74 answered · 9 silent · 41 withheld | **76 answered · 9 silent · 39 withheld** |
| tests | 915 | **918** |

The corpus does not move and could not: neither new command appears anywhere in it. That is the
point of keeping the figure — it measures whose traffic it is, not what this build can read.

---

## Part one: what is left, said plainly

Thirty-nine of the catalogue's 124 commands have no layout, and **most of them are not work
waiting to be done.** `sasctl catalogue undecoded` now prints, for each one, three things — all
three read rather than written down:

* whether the library's dispatcher sends the command anywhere at all;
* the conditions its handler returns on without answering, **verbatim from the library**;
* how many bytes the receiver frames the host's message at;
* and what this build does about it, out of the sweep's own plan.

The verbatim condition is what makes the listing worth having. A reader can go to the same line in
the library and disagree.

### What that produces

The tool counts them itself, in an order that stops anything being counted twice — a command can
be both withheld and gated, and the buckets would otherwise sum to more than the list:

```
COUNTED, in this order so that nothing is counted twice
  dispatched nowhere by the library               4
  not asked here, because it changes the machine  28
  asked here, and the machine gates its answer     7
  asked here, and nothing explains the silence     0
```

**The last line is the whole answer to "how much of this is work": none of it.** The four are
`$0C`, `$0D` and two markers the enumeration carries that are not commands. The seven gated ones
are the five tournament meters (through the shared tournament-meter helper), `$6E` on component
authentication, and `$A4`, which answers nothing until a cashout limit is configured. The
twenty-eight are setters, each named in the sweep's table with what it would change.

**The five tournament meters and the four funds-transfer setters are the two families worth naming.**
The tournament meters are silent on this machine because its game does not advertise tournament
mode, and a host cannot change that. `$72`, `$73`, `$75` and `$76` all move money or set what is
printed on a receipt, so they are withheld on their own merits and not because of registration —
which is the correction worth recording, because this family is usually described as "blocked on a
registered host". It is not. `$74` answered here with `aft_status` = `$B2`, and the library's
`AS_AFT_REGISTERED` is bit `$08` — clear. So this machine has funds transfer enabled, three kinds of
transfer available, and **no host registered**, and it answered anyway.

### The trap this part fell into first

**A silence is only evidence when a properly framed message reached a handler.** The receiver's own
table says how long each host message is and drops anything shorter as incomplete, before any
handler sees it — so a bare poll of a six-byte command is met with silence that is about the
framing and says nothing about the command. Two of the probes written on the way here made exactly
that mistake and had to be redone: `$95` and `$99` asked with three data bytes were **refused**,
because the receiver framed six and checked the sequence over the wrong span. Asked with two, they
are silent — and *that* silence is the tournament gate.

The same trap was already in the sweep, for `$6E`. Its ladder worked up from the shortest form,
which gave the command a length byte of zero; the handler then read the check sequence as the
action code, rejected it and said nothing. The sweep now asks `$6E` with action `$00`, which
interrogates how many components are installed, so its silence is the machine's and not the
request's. It is still silent, and now for a reason the listing names.

---

## Part two: two commands that read when asked the right way

The sweep's rule is that a command whose handler is named for sending something is asked and every
other command is withheld. Two commands were withheld and should not have been, because each
carries **one argument for which the handler takes no branch that writes**:

| | the argument | the branch |
|---|---|---|
| `$74` lock and status | lock code `$FF` | the library's `LC_INTERROGATE`, whose whole branch is one read of the machine's own lock status |
| `$7B` extended validation status | an all-zero control mask and zero expiry dates, at full length | the handler changes only the status bits the mask selects, and this mask selects none; zero expiry dates leave the stored ones alone |

Both are now asked, both answer, and both layouts were read out of the handler and then checked
against the bytes:

```
$74  01 74 23 01 00 00 00 FF 13 01 B2 7F 00 00 00 55 50 …  40 bytes, length byte 35
$7B  01 7B 0A 01 00 00 00 39 80 41 60 00 30 3C 8C          15 bytes, length byte 10
```

In both, the length byte is the total less the header and the check sequence, which is the layout's
own arithmetic agreeing with the machine's. And **both replies carry the same asset number, 1**,
built by two different handlers out of the same stored value — the cheapest cross-check either
layout has, because reading one of them wrongly would have to be wrong in exactly the same way in
the other to go unnoticed.

`$74` answered `$FF` for its lock status, which is the library's `GLS_GAME_NOT_LOCKED`. That is the
measurement that makes the argument safe rather than merely believed to be: a machine that had
taken the lock would have answered `$00`.

### What had to be closed at the same time

A replay sends the request a recording holds, not one it chose. The moment these two commands
stopped being withheld, replaying a conformance capture could have **locked somebody's machine** —
a capture is full of `$74` with a real lock code. So a replay now compares the recorded bytes
against the reading argument and refuses anything else, with the reason. The test names both
commands one at a time; a third has to be added deliberately.

### The second trap: what may be asserted as a literal

A reply's field is assertable as a literal only when it is **what the machine is** rather than what
it is doing, and the replay classifier's table is what decides. The new fields were classified
deliberately, in both directions: the asset number, the transaction-history size, the transfer
limit and the validation settings are configuration; the lock status, the available transfers, the
three credit amounts and the restricted-pool fields are state. The committed case asserts the first
kind and reads the second without claiming a value.

---

## Part three: the machine takes money again, and five payloads followed

`BLOCKERS.md` Trap D recorded a machine that refused every note because its licence was gone. It
was neither gone nor unfixable — it was the **build**. `LicenseHandler` requires the `DEMO` licence
feature in addition to `SAS` on a debug build, as an internal measure against debug builds running
on production hardware, and the key in this machine's secure storage carries `SAS` only. The demo
was configured `CMAKE_BUILD_TYPE=Debug`, so a valid key was rejected, the licence lockup was raised
at powerup and never removed, and the platform disabled the note acceptor on every evaluation.

Built `Release` — a change to `CMakeUserPresets.json`, which is gitignored and the one file in that
repository this project may write — the same key validates:

```
[CONDITION] RemoveCondition(Quixant License)
[INFO]      AsyncEnableBillValidator(1)          ← the first time this machine has ever done this
PASS  a twenty raises its exception and moves its meters (8.3s, 7 assert(s))
```

With money and games available, `sasctl events` produced **five payloads out of ten**, against zero
before:

| | | measured |
|---|---|---|
| `$4F` | bill accepted | denomination code 04 is the twenty, and the meter read 2 after exactly two twenties had gone in |
| `$7E` | game started | over nine games the first field held 50 every time while the second climbed 50, 100, 150 — which is what tells a wager from a meter |
| `$7F` | game ended | eight games, four of which won nothing and reported zero |
| `$88` | reel stopped | forty-five times, the first byte running 1 to 5 in order |
| `$8C` | game selected | when the game menu was pressed |

The other five are named as unproduced with the reason, as before: two the library never queues,
one is money a sweep will not send, and two need a person or a card game. The test that used to
assert *no* payload was confirmed now names the five that are and the five that are not, so a sixth
is a deliberate edit.

---

## What was found on the way and is a finding, not a fix

**The generated catalogue was stale, and regenerating it moved `$2F` from a fixed eight bytes to a
length byte.** That is Trap E, measured on a wire for the first time. The checkout the generator
reads frames `$2F` by a length byte, with a comment saying the fixed size read a wider request
short; the copy `QSP_DemoOpenGL` builds from still frames it at eight. They describe different
machines and neither reading is wrong.

The consequence was concrete: the sweep began asking `$2F` for no meters at all, which the machine
under test drops as incomplete, and a command that had answered for two milestones went silent. The
fix is not to pin either number. A command the receiver frames by a length byte is now asked twice —
for nothing, and for one entry — and the sweep records which form the machine answered. One extra
message, and the difference between a reply and a silence that means nothing.

`sasctl catalogue undecoded` is written the same way for the same reason: it reports what the
library says and what the machine did, and where those two disagree it says so rather than choosing.
