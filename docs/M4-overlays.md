# M4 — Two other descriptions of SAS, read as overlays and diffed

**Status: done, 2026-09-08.** `sasctl catalogue diff` reads the legacy tester's configuration file
and the conformance toolkit's message templates as overlays over the header-derived catalogue, and
reports where the three disagree — command by command, exception by exception, and construct by
construct.

The diff is the deliverable. Nothing is imported: no name, no description and no field of theirs
reaches the catalogue, a layout, or the record a run writes, and there is a test that says so. What
the overlays are for is disagreement, and each disagreement is a question to settle against the
gaming machine library or against bytes.

```
sasctl catalogue diff --legacy   C:\IGT3\sastest.ini \
                      --toolkit "C:\igs\GSA SAS Host Simulator v1.1"
```

---

## The numbers, before and after

| | before | after |
|---|---|---|
| the catalogue, replies with a layout | 36 of 124 | **36 of 124** |
| our own suite, distinct long polls explained | 10 of 10 (100%) | **10 of 10 (100%)** |
| the imported corpus, distinct long polls explained | 18 of 18 (100%) | **18 of 18 (100%)** |

637 tests pass, twenty-two of them new.

**None of the three coverage figures moved, and that is the honest result of this wave.** A diff
establishes no layout, so it decodes nothing new. What it produces is the list of what to decode
next, and the previous two waves had run out of ways to produce one: the corpus is spent, and a
coverage figure taken over traffic can only ever name commands somebody happened to send.

---

## What was read, and what was deliberately left out

| | long polls | exceptions named | exception codes listed with no meaning |
|---|---|---|---|
| this build's catalogue and layouts | 124 | 122 | — |
| the legacy tester's configuration file | 139 | 120 | 134 |
| the conformance toolkit's templates | 137 | 116 | — |

Both overlays are reduced to one vocabulary — what a field's bytes are, how many there are, and
where the source puts them — because comparing element names would only establish that three tools
chose three spellings. Everything dropped in that reduction is printed, per source, rather than
quietly discarded: the display format of a field, the radix a number is shown in, the prompt text of
an input box, the multi-game flag that duplicates a parameter already present.

Anything a reader met and could not turn into a fact is **named, never dropped** — the rule the
capture importer already follows, for the same reason. A hole in the reading would otherwise come
out as a disagreement about the protocol. Over the real files there are none: every line of the five
sections this reads out of the legacy file fits the grammar documented inside that file, and every
element in the toolkit's templates is one its own document type definition declares.

Two reductions are worth stating because they were wrong first and the diff said so.

* **The transaction and acknowledgement flags of a command are bytes on the wire, not settings.**
  Read as settings, every funds-transfer command came out two bytes shorter than the toolkit has
  it, and each was reported as disagreeing about a length when the two sources agree exactly.
* **A message with a field that is present only sometimes has no fixed length.** Adding all of long
  poll `$71`'s fields up gave this build's longest form, 25 bytes, against the legacy tester's
  19 — which is not a disagreement about a number. It is one model saying "sometimes" and another
  having no way to.

## The checks that say the diff works

Four figures were established in the 2026-09-03/04 reverse-engineering pass, by reading those files
by hand. The diff reproduces all four from code, having been written without consulting them.

| | expected | the diff says |
|---|---|---|
| commands the legacy tester can send and cannot read an answer to | 22 | **22** |
| commands it can read an answer to and cannot send | 4 — `$00 $6E $72 $A5` | **4**, the same four |
| `$72`, `$6E`, `$B0` absent from its command lists | yes | **yes**, and `$B0` from the toolkit as well |
| toolkit polls with no reply template | 21 | **21** |

`$72` is the most interesting of those three. It is absent from both of the legacy tester's
command lists and it is not unreachable there: **six of that tool's stored message structures build
it**, so it can be sent without being listed. Reporting it as simply absent would have been a third
of the truth.

---

## Where the three disagree

144 long poll codes exist across the three sources. 40 have nothing to report; 104 have something.

| | count |
|---|---|
| presence — one source has the code, another does not | 61 |
| name — the two names are not the same thing | 72 |
| reply length — two sources fix different lengths | 9 |
| check sequence — one says the host's message ends in one, another says it does not | 2 |
| expressiveness — one model states something another cannot | 34 |
| wording — the same thing worded differently | 79 |

Wording is counted and not printed by default, and that is a judgement worth stating: two thirds of
those are this build's own names running their words together, because they are generated from the
library's identifiers. `Enablebillacceptor` against `Enable bill acceptor` is an artefact of a
generator, not a difference about the protocol, and printing all of them would bury the
seventy-two that do name something else. `--all` prints them.

### The nine reply-length disagreements

`$24 $25 $26 $64 $65 $66 $67 $6B` are the funds-transfer family, where the two overlays differ by
one byte on the amount field or by five on the whole reply. None of them is in this build's
catalogue, so nothing here can settle them.

**`$8F` is the ninth, and it is settled.** The legacy tester has the physical reel stop reply at
seven bytes; the toolkit has it at eleven. The library's own handler ends
`length = 11` — eleven bytes before the check sequence, nine reel positions and not five. The
toolkit is right and the legacy tester is describing a five-reel machine. That is one disagreement
found by the diff, taken to the library, and closed, and it is the shape every one of the other
eight would take if the library had a handler for them.

Two more disagreements were settled the same way while this was being built, and both were against
this reader rather than against a source — the transaction and acknowledgement flags, and the
message with fields that are only sometimes there, in the section above.

### The two check-sequence disagreements, which are a defect in this build

Both overlays say the host's own `$01` and `$02` messages end in a check sequence. This build says
they do not, and this build is wrong.

The gaming machine library's handler for both — `lpXX_EnableDisable` in `lp.cpp` — calls
`crcCheck(rxPacket, false)` and sends a negative acknowledgement when it fails. With a two-byte
frame that check reads the address and the command byte *as* the check sequence and computes zero
against them, so it can never pass. On that reading a real machine refuses a two-byte `$01`; it
has not yet been measured on the wire, and it should be.

What this build does instead is decide from the shape of the message rather than from the command.
`HostMessage.WriteLongPoll` appends a check sequence when the message carries data and not
otherwise, and the software machine verifies one only on a frame longer than two bytes. **The two
peers share the assumption, so they agree with each other and both disagree with the machine** —
which is the one kind of agreement D7 says proves nothing, and the reason running the same script
against both would never have caught it.

Seven more commands share that handler — `$03`–`$07`, `$0A` and `$0B` — so they carry the same
requirement. They have no layout here, so the diff had nothing to compare, and the defect would
arrive with the layout.

**Not fixed in this wave, deliberately.** The fix is a check-sequence property per command on the
layout, honoured by the host framing and by the machine, and that changes how every host message
is framed. It is the first thing in the next wave, and it should be measured on the wire against a
real machine rather than left as a reading of the handler, which is all it is today.

### The exceptions

256 codes across the sources; 200 with nothing to report, 56 with something.

* **Three exceptions the legacy tester names and this build does not have at all** — `$26`, `$65`
  and `$73`. Each is a code the library's enumeration does not carry.
* **`$01` is in that file's exception table and is not an exception.** It is that tool's own marker
  for an acknowledgement.
* **The 134 codes it lists and says it has no meaning for are neither named nor absent**, and are
  counted apart from both. Folding them either way would misstate what that file says by a hundred
  and thirty-four codes.
* **Eight exceptions carry data of their own**, and only the toolkit describes what follows the
  code: `$4F $7C $7E $7F $88 $8A $8B $8C`. This build decodes none of it — that is the real-time
  event format, still undecoded, now with a list attached to it.

---

## What each model can say

Read out of each model itself rather than out of the data, because a construct nobody used is still
one the model has. This build's from the field record; the toolkit's from its own document type
definition; the legacy tester's from the grammar documented in the comment blocks of the file.

| | here | legacy | toolkit |
|---|---|---|---|
| which end of a multi-byte number comes first | yes | no | no |
| what a number counts — credits, cents, whole currency units | yes | no | no |
| what a value is allowed to be | yes | yes | yes |
| named values of an enumeration | yes | yes | yes |
| named bits inside a byte | yes | no | yes |
| a field whose length an earlier field states | yes | yes | yes |
| a field whose width comes from a table keyed on an earlier value | yes | no | no |
| a group that repeats | yes | no | no |
| a field present only when an earlier one says so | yes | no | no |
| which fields are meters a test may read by name | yes | no | no |
| whether a message carries a check sequence | yes | yes | yes |
| a description of the message in prose | **no** | yes | yes |

The last row is not a gap. Both other models hold their authors' words, and decision D4 keeps those
out of anything this product ships.

The two rows that cost the most are the ones the toolkit's own authors worked around rather than
closed. Its definition declares **four element types that exist for one message each** — a repeating
meter list on `$6F` and `$AF`, a meter response on the same two, an element named after long poll
`$2F`, and an AFT receipt used once, inside the funds-transfer poll. And it marks part of
**seventeen** messages `optional` while its definition gives that element a name and no condition,
so it can say a group is sometimes there and never when. Both are the same missing generality, and
both are why a display model cannot be copied into an assertion model even where its offsets are
right.

---

## What the diff is worth

**86 commands.** 88 of the 124 in this build's catalogue have no layout; 86 of those are described
by at least one overlay — 84 by the legacy tester, 83 by the toolkit, 81 by both.

**All 86 are new.** None appears in the imported corpus's eighteen distinct commands, and none in
our own suite's ten. The corpus was never going to produce them, which is the whole reason this
wave exists.

**68 of the 86 have a reply described field by field by at least one overlay** — a second opinion to
check a layout against once it is written, of exactly the kind that settled `$8F`.

**83 of the 86 can have a layout read out of the library's own handler**, which is where every
layout in this build has come from. That is the backlog, and it is a large one.

**Three cannot: `$00`, `$0C` and `$0D`.** This build's catalogue has them because the library's
enumeration does, but the library's dispatcher does not send them anywhere, so there is no handler
to read a layout out of. `$0C` and `$0D` — set sound volume and play sound — appear in the
enumeration, in the library's logger and in its multi-denomination base-command list, and in no
dispatch. A machine would not answer them.

**Twenty more commands are in an overlay and in no part of this build.** Eighteen are the legacy
funds-transfer family that the library never implemented, and they are the same eighteen the
2026-09-03/04 pass found. The other two are a different kind of finding:

* **`$A5`** exists only in the legacy tester, and there only as a reply it can read. It has no entry
  in either command list, no template in the toolkit, and no code in the library. Nothing here can
  establish a layout for it and nothing here should try.
* **`$FD`** is in that tool's command list with a five-byte reply decoder attached, and the name it
  is listed under says plainly that it is a placeholder. The diff reports the code and does not
  classify it; reading the entry is what settles it. It is not a command.

Each of the twenty is **a finding to record honestly rather than a layout to write**. A description
in somebody else's display model is not a layout, and writing one from it would produce exactly the
kind of plausible guess the last wave's rule exists to refuse.

---

## Decision D4, and how it is enforced rather than promised

The overlays are read from wherever the operator's copies sit, compared, and dropped. Two tests hold
the line:

* one reads an overlay carrying a name nothing here would ever choose and then looks for that name
  in every description, symbol, layout name, handler name and field name this build holds;
* one asserts the direction of the dependency by reflection — the protocol model, the layouts, the
  session record and the capture readers cannot see the overlay types at all. That is what keeps the
  first test true for the messages its fixture never mentions.

Every fixture in the test file is written by us, in the grammar each source documents, with invented
command names. The tests therefore run on a machine with neither tool installed, and no word of
anybody else's file is committed here.

---

## Known limits, stated rather than discovered later

* **A name comparison is a judgement, and this one is deliberately coarse.** Words that carry no
  weight in this vocabulary are dropped before comparing, and two names whose letters match after
  spacing is removed are treated as the same. That is why `Setsoundvolume` and `Set sound volume`
  are not reported. It will also treat a genuine difference that survives only in a dropped word as
  agreement, and such a row is not printed at all — `--all` adds the rows judged to be the same
  thing worded differently, not the rows judged to be the same. Settling one of those means reading
  the two files.
* **A length is compared only where two sources fix one.** Silence is not agreement: a length only
  one source states is reported as stated by one source, and never as a match.
* **Five commands the legacy tester lists more than once keep only their first form.** The
  alternates are counted and named. Merging them would make one longer command out of two.
* **The permitted values under a command are counted as expressiveness and not carried as fields.**
  Comparing enumerations across three sources is a larger job than comparing shapes, and it is not
  done.
* **A toolkit group marked optional is carried as one field of no fixed length, and its members are
  not read.** That is why the funds-transfer poll is not reported as using a bespoke element type
  even though it does, one level down. The group's length is unfixed either way, so no length
  comparison is affected.
* **Nothing here is replayed.** `PLAN.md`'s M4 also asks for a capture replayed against a machine
  with the verdicts diffed, and for a capture split into the transactions inside it. Neither is
  done.
