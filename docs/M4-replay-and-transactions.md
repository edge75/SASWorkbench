# M4 — Replaying somebody else's traffic, and splitting it into transactions

**Status: done, 2026-09-09.** Two things `PLAN.md` had asked for and nothing here had done: send a
recorded session's host messages to a machine and diff the verdicts, and split a capture into the
transactions inside it. Both are about traffic somebody else recorded, which is the only body of
evidence this project has that it did not author.

```
sasctl transactions --rules                          how each level is split, and where it cannot be
sasctl transactions <run.jsonl or capture>           the exchanges and the episodes inside one
sasctl replay --causes                               what a replay can conclude from a difference
sasctl replay <recording> --loopback                 against the software machine
sasctl replay <recording> --tcp 127.0.0.1:9760       against QSP_DemoOpenGL
sasctl replay <run.jsonl> --test <word>              one case of a suite at a time
```

---

## The numbers, before and after

| | before | after |
|---|---|---|
| the catalogue, replies with a layout | 83 of 124 | 83 of 124 |
| the catalogue, meters readable by name | 40 | 40 |
| the catalogue, event payloads established | 10, and 2 never produced | 10, and 2 never produced |
| our own suite, distinct long polls explained | 57 of 57 (100%) | 57 of 57 (100%) |
| our own suite, distinct events explained | 4 of 4 (100%) | 4 of 4 (100%) |
| our own suite, packets | 1,491 | 1,479 |
| the imported corpus, distinct long polls | 18 of 18 (100%) | 18 of 18 (100%) |
| the imported corpus, packets | 1,128,939 | 1,128,939 |
| a fresh sweep of the software machine | 76 of 83 (91%) | 76 of 83 (91%) |
| tests | 818 | **869** |

**Not one coverage figure moved, and none could.** This wave establishes no layout: it reads
traffic that was already read and asks a machine questions the sweep already asked. Saying so
plainly is the point — the numbers this wave produces are new ones, and they are below.

The suite's packet count went from 1,491 to 1,479 across two runs of the same suite with no
change to any script. That is the poll loop: a case that waits for a machine sends however many
general polls that wait takes.

### The numbers this wave does produce

| replay | distinct requests | agrees | configuration | revision | state | **unexplained** | not replayed | no comparison |
|---|---|---|---|---|---|---|---|---|
| the toolkit's 29 traces → software machine | 12 | 0 | 5 | 0 | 1 | **0** | 5 | 1 |
| the toolkit's 29 traces → `QSP_DemoOpenGL` | 12 | 0 | 5 | 0 | 1 | **0** | 5 | 1 |
| our own suite → software machine | 70 | 44 | 0 | 0 | 17 | **0** | 9 | 0 |
| our own suite → `QSP_DemoOpenGL` | 70 | **36** | 6 | 0 | 19 | **0** | 9 | 0 |

| split | packets | exchanges | episodes | ambiguous places |
|---|---|---|---|---|
| our own suite | 1,491 | 994 | 33 | 2 |
| one toolkit trace (`10.1/10.1.3/step10.trc`) | 65 | 47 | 5 | 13 |

The third row of the first table is the control and is the reason to believe the fourth. Replaying
our own suite's traffic against a fresh instance of the machine that produced it comes back
**44 identical replies, 17 differing only in fields that move on their own, and nothing
unexplained** — so when the same recording against a real machine says 36 identical and six
configurations, the six are the machines differing rather than the classifier flailing.

---

## Replay: a diff is worthless until it says which of four things it is

This is the only expectation in the project somebody else wrote. Every other case says a machine
ought to do something because we decided it ought to; a replay says a machine did something, once,
and asks whether the machine in front of it does the same.

A bare diff of that is worse than useless. **Two machines are never in the same configuration and
have never done the same things**, so almost nothing matches and the output is a wall of red with
one real finding somewhere in it. The whole command is therefore the classification, and every row
says which of these it is:

| | what it means | what makes a row this |
|---|---|---|
| **agrees** | nothing to explain | the same bytes after the echoed address |
| **configuration** | a machine in another configuration | one side refused; or every differing field is what the machine is and how it was set up; or one side is silent on a command the library gates on a setting the two are measured to differ on |
| **revision** | a machine of another revision | the library has no handler for the command at all; or the reply lengths differ and exactly one is the length the library's own handler writes |
| **state** | the same kind of machine, a different life | the differing fields are meters, the machine's current condition, or fields **measured** to move between two reads of one machine |
| **unexplained** | a defect in one of the two machines, **or a gap in this classifier** | anything else |
| not replayed | nothing was sent | a command that changes the machine, or a recorded message whose own check sequence does not check out |
| no comparison | it was sent and there is nothing to compare | the recording holds no answer |

**Only the fifth makes a replay red**, and it is deliberately not called a defect on its own
authority: nothing here can tell a defect from a hole in its own tables, and every one of them
needs a person. The three legitimate causes come back as passes with the reason on the record,
which is what stops a machine answering differently for a good reason from reading as a failure.

### What it will not do

* **It does not send a command that changes the machine.** A conformance capture configures a
  machine before it measures it, so a recording is full of setters. Every one is withheld by name
  with what it would have changed, out of **the same table the catalogue sweep uses** — so a
  setter added to the library cannot start being sent to somebody's machine by way of a recording
  that happens to contain it. Replaying our own suite withheld nine: `$0E` and eight forms of
  `$71`, which redeems a ticket and moves money.
* **It does not replay general polls.** What one returns is whichever exception the machine had
  queued, which is a fact about what happened to that machine at that moment. 693 of our suite's
  994 exchanges are polls, and diffing them would be noise.
* **It does not repair a recording.** Two `$4C` messages in the toolkit's traces fail their own
  check sequence — they are pieces of other messages the tracer printed as lines of their own —
  and they are not sent, because sending this build's guess at what they were meant to be would be
  measuring the guess.

### What is measured rather than asserted

Three things, and they are worth different amounts. The classifier applies them strongest first.

1. **Measured.** Every request is sent **twice**, so a field that moved between two reads of the
   same machine seconds apart moves on its own, whatever any table says. That is one extra poll per
   distinct request and it is the only thing here that turns "this field is state" into a fact.
2. **Derived.** A field the layout marks as a meter accumulates by construction, and that mark is
   already checked against the library's own handlers by a test.
3. **Stated.** Ninety-two field names classified by hand — 32 as configuration, 60 as state —
   because which fields are a machine's identity and which are its condition is a judgement about
   the protocol and not something the library says. **A name in neither list is reported as
   unexplained rather than assumed harmless**, which is what keeps the gap countable, and a test
   fails if a name in the table exists in no layout. It caught one on its first run: `coin_out`,
   which no reply has.

Two names genuinely mean two things and are resolved per command: a `count` is a number of notes
in the last-accepted-bill reply and a number of enabled games in the enabled-games reply, which is
a meter and a configuration.

### The five things a real replay taught the classifier

Every one of these came out of running it rather than out of thinking about it, and each was a
wrong verdict before it was a rule.

**1. A length byte is not a difference of its own.** The serial-number reply differed in
`serial_number` — a machine's identity, nothing more — and in the `length` byte in front of it,
which differed for no other reason. Reported separately it double-counted one disagreement and, worse,
left it unexplained. A framing length now takes the cause of what it measures, recognised from the
layout: a reply whose total the layout cannot fix carries its own length byte.

**2. A two-bit field is not its bytes.** The validation style is bits six and five of a status
byte, so its bytes *are* that whole byte. Comparing bytes reported the style as differing whenever
anything else in that byte did, and put a field reading **0 on both machines** into a list of
disagreements. Fields are compared as values now.

**3. A recording holding two answers to one request is not a measurement.** Where several captures
are read together, two answers to one request may be two machines — and this turned a serial
number into a field that "moves on its own". Only the live machine's two reads count as the
measurement, because they are provably the same machine. The recorded variance is still reported,
and where it exists the row says so: *the recording holds 3 different answers to this request and
this compares against the first.*

**4. Silence has to be read against the machine's own configuration.** A recorded machine answered
`$4D` with a zeroed validation record and the machine in front of the replay answered nothing, and
the first classifier called that a possible defect. It is not one: the library's handler **returns
without sending anything** when a validation command reaches a machine not performing that
validation, and it says so beside the code, citing the specification. So the replay now reads
what both machines say about themselves and runs in **two passes** — everything is asked, then what
the two are measured to disagree on is worked out, then anything is classified — because a row
about `$4D` is only explicable in the light of what `$A0` said, and `$A0` may be sent after it.

Reading *every* answer the recording holds mattered here rather than the first: the toolkit's
traces carry the same machine's features reply twice, with `feature_codes_1` of `$82` and `$D2` —
**standard validation in one and secure enhanced in the other**, because the corpus reconfigures
the machine between steps. Reading only the first ruled out the very configuration that explained
the silence.

**5. The gate is what the library says, not what the feature bit is called.** `$70` and `$71` look
gated on the ticket-redemption bit. The library's own comment says otherwise: a redemption cycle
needs the bit set **and** a validation style that is not standard, and *"a machine with no style
configured is not exempt"*. Our suite's recording and the demo agreed on the redemption bit, so the
bit alone explained nothing and the row stayed red; the style explains it.

### What settled the last red row, and it is the shape of the whole command

Replaying one ticket case of our suite against `QSP_DemoOpenGL` left one row unexplained: the
recording answered `$70` with a ticket-validation record and the machine answered nothing. The
recording of a single case holds no features reply at all, so there was nothing to compare the gate
against, and the row said exactly that — *nothing in this replay read `validation_style` from both
machines, so whether that is the reason cannot be said; reading it would settle this row.*

So the replay now reads it. **One poll of the machine's own enabled features**, asked by the replay
rather than taken out of the recording, and the row becomes:

> the machine answered nothing, and it says of itself that `validation_style` is 0 — the value the
> library answers `$70` with silence at. So this machine did what the library says; the recorded
> machine was in another configuration.

That is a conclusion from a measurement of one machine plus a rule read out of the library, and it
is the strongest thing available: the recording genuinely does not say what the recorded machine
was configured as, and no amount of staring at it will.

### The limitation that stays, and the flag that works around it

**A run of a whole suite is not one machine.** The suite reconfigures the machine between cases —
some cases declare standard validation, some secure enhanced, some a different accounting
denomination — so read as one recording its features reply may describe the machine in one case
and not the one that answered in another. `--test <word>` replays one case at a time, and that is a
flag rather than a change to the classifier because the limitation is in the recording.

---

## What the two machines turned out to be

Measured from the replies themselves, replaying our own suite against `QSP_DemoOpenGL`:

| | our software machine | `QSP_DemoOpenGL` |
|---|---|---|
| `feature_codes_1` · `_2` · `_3` | 0 · 0 · 0 | 6 · 205 · 3 |
| `meter_model` | 0 | 1 |
| `games` | 1 | 4 |
| `max_bet` | 255 | 500 |
| `game_name` | `QXFAKE` | `Multigame Pack` |
| `paytable_name` | `001` | `PT_001` |
| `serial_number` | `QXFAKE0001` | *(empty)* |

Two of those are worth a second look. **The demo answers an empty serial number**, which is not
something a host would enjoy. And the toolkit's recorded machine answers `serial_number` as
`100001` in one reply and as nothing in another, so an empty serial is not unique to this demo.

---

## Transactions: two levels, and one of them is a judgement

A capture is a flat list of packets and no question worth asking is about one packet. Two levels
come out, and they are kept apart because they answer different questions.

### Level one — the exchange

One host message and the machine messages that answer it. **Mechanical**: the protocol says what
answers what, so nothing here is a judgement. What it costs is one discipline:

> A reply is attributed to the poll whose address and command **it echoes**, never to whatever
> happened to precede it.

That is the same rule the poll engine itself had to be corrected to use, and for the same reason:
attributing by position reports one command's answer under another command's name. A reply that
echoes another command is reported as answering nothing, and the poll before it as unanswered.

A repeated long poll that has not been answered is the protocol's own retry — the repeat means
*send it again* — so it stays inside the exchange it repeats and only the count moves. **A repeated
general poll is not a retry of anything**: it is the poll loop, and each turn of it is its own
exchange. Collapsing those would turn a session's whole idle stretch into one exchange with a
large number on it.

Over our own suite: 1,491 packets become **994 exchanges** — 497 synchronisation polls, 196 general
polls of which 137 were the host waiting, and 301 long polls, with nothing unanswered and nothing
unattributed.

### Level two — the episode

Several exchanges that are one thing happening. This level **is** a judgement about what a person
means by a transaction, so it is written down where it can be argued with, and
`sasctl transactions --rules` prints every rule rather than leaving it implied.

| episode | keyed on | opens | ends |
|---|---|---|---|
| cashout ticket | `validation_number` | ticket printed · cashout button · validation request | the host reads what was on it |
| ticket redemption | `validation_data` | ticket inserted | transfer complete |
| note accepted | **nothing in the bytes** | the machine took or refused a note | the host moves on |
| handpay | **nothing in the bytes** | handpay pending | reset or cancelled |
| door | the door named by the exception code | any door opened | the matching door closed |

Two of the tables behind these are **derived** from the gaming machine library's own symbol names —
which exceptions say a note was taken, which say a door opened — so a code added to the library
joins the right episode with no change here. Which long polls belong to a ticket's life is a
judgement no amount of reading the library produces, so it is stated. Every code named is checked
against the generated catalogue by a test.

Over our own suite: **33 episodes** — 4 cashout tickets, 8 ticket redemptions, 15 notes and 6
doors.

### Where the split is not in the bytes, it says so

This is the half worth more than the counts.

* **A note episode has no key at all.** Nothing in a bill meter's reply names which note it is
  about, so two notes taken close together genuinely cannot be told apart. Where two are open at
  once, both episodes carry *a second note accepted episode opened while one was still open, and
  nothing in these replies says which of the two any of them is about* — six of our suite's 33
  episodes are marked that way. The alternative is presenting an adjacency guess as a reading,
  which would be inventing the very transactions this was asked to find.
* **A door episode never has that problem**, because which door an exception is about is in the
  code itself. Two doors open at once are two episodes and neither is a guess. That contrast is why
  the derived and stated halves are kept visible.
* **A cashout ticket is keyed and can still be unkeyed.** Where the host never reads the ticket, no
  validation number is available and the episode says it cannot be told from another of its kind.
* **An episode may close with none open**, which means the recording begins inside one. Reported as
  an episode whose beginning is not in the record rather than dropped.
* Four ways an episode can end are kept apart: the machine reported it closed, the host read what
  was on it, the host moved on, or **the recording stops inside it**. Reporting the last as closed
  would be a small lie that makes a truncated recording look whole.

### Two findings about the corpus, from splitting it

**The toolkit's traces are filtered.** Splitting `10.1/10.1.3/step10.trc` reports thirteen machine
messages with no poll of theirs in the file, and the blocks are separated by a rule with the first
message of each carrying no timestamp. The file holds the machine's answers to `$51` polls that
were never written down — the toolkit's trace filters record some polls and not others. So
**silence in a trace is not a machine having answered nothing**, and the replay's *no comparison*
outcome exists because of this.

**The toolkit's session log holds no data replies at all.** Replaying it produced thirteen rows and
not one comparison: every machine message in that format is exactly one byte, which the import
already knew, and it means the 1,368-message session log is **useless for replay** in the same way
the 39 MB legacy capture was useless for breadth. The `.trc` traces and our own runs are the only
recordings here a replay can use.

**A recording can hold traffic to more than one machine.** The toolkit's traces poll `$7F`, `$01`
and `$26`. A reader assuming one machine per recording would read the unanswered `$7F` polls as a
machine ignoring its host; the split records every address it saw, and the replay says plainly that
everything goes to the one machine in front of it.

---

## What was not done, and why

* **No case under `tests/regression`.** That tree carries an invariant the others cannot: every
  case in it runs green against the software machine with **no skips at all**. A replay is mostly
  skips by construction — nine withheld setters out of seventy requests on our own suite — so a
  replay case there would either destroy the invariant or be trimmed to the convenient half. The
  fifty-one new tests are unit tests, and the classifier's own behaviour is where the assertions
  belong.
* **Money was not needed and was not moved.** Every recording replayed here is reads and
  configuration; the eight `$71` redemptions and the `$4C` validation-identifier setters in it were
  withheld by name. So `BLOCKERS.md` Trap D — the machine has no licence and takes no money — did
  not block any part of this, and nothing here needed a note to be accepted.
* **Nothing was replayed over serial.** No adapter is attached; the transport is unchanged.

## One thing measured on the way that is not this wave's

A sweep of the software machine answered 65 of its 83 commands on one run and 66 on two others,
differing on `$0F` — six meters in one reply — which answered nothing once in three runs. Nothing
in this wave touches the sweep or the software machine, and the run in which it was silent was
taken **before** any of these changes. It is the same shape as the flakiness `BLOCKERS.md` records
under load, at a load nobody was applying, and it is recorded here because it was seen rather than
because it was understood.
