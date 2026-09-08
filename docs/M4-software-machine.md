# M4 — Giving the software machine values

**Status: done, 2026-09-08.** The sweep of the last wave established forty-seven reply layouts
against a real machine, and no regression case could be written against any of them: the software
machine refused every command it had no values for, so a script asserting on one of those replies
had nothing to run against. It now answers seventy-one of the catalogue's eighty-three replies, and
the twelve it does not are named one by one with the reason.

```
sasctl egm --tcp 127.0.0.1:9760 --dial          the software machine as a peer
sasctl sweep --tcp 127.0.0.1:9760                ask it every command it may be asked
sasctl run tests/regression --loopback --stimulus software
```

---

## The numbers, before and after

| | before | after |
|---|---|---|
| the catalogue, replies with a layout | 83 of 124 | 83 of 124 |
| **the software machine, replies it answers** | **27 of 83** | **71 of 83** |
| a sweep of the software machine: answered | 22 | **66** |
| a sweep of the software machine: refused | 61 | **15** |
| a sweep of the software machine: answered nothing | 0 | **2** |
| **our own suite, distinct long polls explained** | 11 of 11 (100%) | **37 of 37 (100%)** |
| our own suite, distinct events explained | 4 of 4 (100%) | 4 of 4 (100%) |
| the imported corpus, distinct long polls explained | 18 of 18 (100%) | 18 of 18 (100%) |
| meters a script can read by name | 16 | 16 |
| tests under `tests/regression` | 14, no skips | **28, no skips** |

780 tests pass, thirty-seven of them new.

**The catalogue figure could not move and did not.** This wave establishes no layout: every reply
it fills in was already decodable, and what was missing was a machine willing to send one. The
corpus figure could not move either, for the reason the last two waves gave — that traffic was
recorded before any of this and carries none of these commands.

**The row that matters is the second.** A sweep sees a smaller number than the catalogue does
because it withholds forty-one commands by name, five of which the machine does answer; those five
are counted in the 71 and not in the 66.

---

## Command by command: what it can answer honestly, and what it cannot

The rule the machine is built on, in one sentence: **it answers where the answer is something it
actually holds — a meter it keeps, or a meter that is genuinely zero because it has no such
machinery — and refuses where it would have to invent one.**

The second half of that is not a formality. A zero from a machine that has no reels and a zero from
a machine whose reels have not moved are different statements, and only the second is worth
asserting on. A test that passed against the first would pass for a reason that has nothing to do
with the protocol.

### It can answer these, and did not before — 44 commands

| | what makes the answer honest |
|---|---|
| `$19` `$1C` five and eight accounting meters | every meter in both is one it already answers singly |
| `$18` games since power-up and since door closure | neither is a meter, both are moved by its own stimuli, so it now keeps them |
| `$1B` handpay information | it raises no handpay, and the library's own handler answers a zeroed record when none is held |
| `$2A` `$2B` `$47` coin acceptor and hopper credits | it has no coin acceptor and no hopper, so these have never moved |
| `$2D` handpay cancelled credits | a different meter from the one `$10` answers, and zero for the same reason |
| `$2F` selected meters, fixed width | the same meters as `$6F`, with the width taken from the library's own table |
| `$31`–`$45` twenty dedicated bill counts | seven are denominations it takes and counts; thirteen are notes it cannot be offered |
| `$46` credits from notes | a meter it keeps |
| `$48` last accepted note | it records the country and denomination of the last note, as the library does |
| `$49` `$4A` notes in the stacker and their value | a note taken goes to the stacker, and nothing here empties one |
| `$4F` hopper status | it says it cannot measure how full a hopper is, which leaves the level out of the reply |
| `$50` validation meters | the ticket quantity and value meters, which a cashout and a redemption now both move |
| `$51` `$55` `$56` games implemented, selected, enabled | one game, enabled, and none selected |
| `$52` per-game meters | one game, so its game meters are the machine's |
| `$53` game configuration | the same configuration the identification reply publishes |
| `$57` pending cashout | it prints the moment it is asked, so nothing is ever pending |
| `$B3` token denomination | no coin acceptor and no token, so nothing ever configured one |
| `$B4` wager category | one paytable, no categories — which the library's handler has an answer for |
| `$B5` extended game information | its own name, paytable, largest bet, and no progressive levels |
| `$4C` secure enhanced validation identifier | only on a machine configured for that validation, where it stores and echoes what it is given |
| `$B2` enabled player denominations | only on a machine built with a list of them |

### It cannot answer these, and they stay refused — 9 commands

| | what it would have to invent |
|---|---|
| `$83` `$84` `$85` `$86` `$87` | a progressive. This machine belongs to no group and no level, and nothing pays one |
| `$90` `$9A` | a legacy bonus. Nothing here awards one |
| `$8F` | reels. It spins none |
| `$8E` | cards. It deals none |

`$86` is the odd one in that list because it is the host setting progressive amounts rather than
asking for any, and it is refused for the same reason: a machine with no progressive has nowhere to
put them.

### It answers three with silence, which is not the same as refusing

`$4D` always, and `$4C` and `$B2` on a machine not configured for them.

All three are the machine library's own behaviour: its handlers return without sending anything
when a validation command reaches a machine not performing that validation, and when the enabled
player denominations are asked of a machine that advertises only one. A refusal would tell a host
the command was understood and declined, which is a different thing to debug — and the distinction
is one this project already paid for once, when a real machine answered a badly framed `$01` with
silence rather than the refusal a handler predicted.

The same rule now covers a question about a game the machine does not have: `$2D`, `$52`, `$53`,
`$B4` and `$B5` are silent rather than refusing, because the library's handlers return before
writing anything.

### Seven have no layout in this build at all

`$6E`, `$95`–`$99` and `$A4`. Nothing changed for them here: five of them are the tournament meters
the last wave's sweep could not establish because the machine under test does not advertise
tournament mode.

---

## The eleven replies where both peers send the same bytes

This is the part worth more than the count. Eleven of the new replies are **byte-identical** to what
`QSP_DemoOpenGL` answered when the catalogue was swept on 2026-09-08 — address, payload and check
sequence. Both machines were fresh, so both answered zeroes, and a field width, an offset or a
length byte one out on either side breaks the match.

```
$18 $19 $2D $48 $50 $52 $53 $55 $57 $B3 $B4
```

`SoftwareMachineRepliesTests` holds the comparison, with every expected string copied from the
capture rather than from this machine's own output. That is D7's diff of the two peers, made
concrete: run the same question against both and compare, and a disagreement is a finding in one of
them.

**It found two.** The wager category reply and the extended game information reply were each
carrying a length byte one too small — the fixed part of both is one byte longer than it was
counted as. Nothing in this build would have noticed, because the decoder reads those replies by
their own inner length fields and both round-tripped cleanly; only the real machine's bytes
disagreed. `$B4` is now identical to the machine's, character for character.

---

## What changed in this build

* **The software machine's replies moved into a file of their own**, with the rule above written at
  the top of it. The wire, the stimuli and the refusals stay where they were.
* **Six things its own stimuli move are now kept.** The slot door's opening count and the two
  games-played counts, the number and value of notes in the stacker, the country and denomination
  of the last note taken, and the validation meter family a ticket moves alongside the machine-wide
  totals. Every one of these was something the machine's own stimulus changed and its own reply
  would have reported as zero.
* **A command answered with silence is modelled apart from one that is refused**, so the machine
  can be silent exactly where the library is.
* **The message encoder can write a field whose width an earlier field states.** It could already
  do so inside a repeating group and not at the top level, where three fields need it — the wager
  category's coin-in meter and both of the extended game reply's names. Written at the top level,
  such a field silently occupied no bytes at all. The rule now lives in one place and both callers
  read it.
* **Two options were added to the machine**: what it calls its game, and what its token is worth.
  Both are configuration of a piece with the serial number and paytable identifier it already
  publishes, rather than machinery.

---

## What was deliberately not done

* **No new meter a script can read by name.** The index stayed at sixteen, for the reason the last
  two waves gave: several of these commands report a meter that is already the answer to a meter
  name, through a handler that scales it differently. Widening the index is a wave of its own.
* **No layout was added or changed.** Where this machine's reply and a layout disagreed, the layout
  was right and this machine was corrected.
* **The per-game meter classification is not modelled.** The library omits a meter that is not a
  multi-game meter from `$2F` when the game number is not zero. This machine has one game, so its
  game meters and its machine meters are the same values; a case that needs the distinction needs a
  machine with more than one game, and the code says so where it would matter.
* **`machine.wager_categories` is still unmet**, so validation case AD-20 still skips naming it.
  This machine answers `$B4` now, but it answers it as a machine with no wager categories — which is
  exactly what that case cannot be written against.
* **The tournament meters and the three other undecoded commands were not touched.** They need a
  machine that advertises tournament mode, which is not this one and not the one downstairs.
