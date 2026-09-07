# M4 — The backlog the import produced, decoded

**Status: done, 2026-09-07.** The eleven commands `sasctl import` left unexplained have been
worked through. Eight of them needed a layout and got one. Three of them were never in the corpus
at all — they were bytes read out of pieces of other messages, and the honest fix was to stop
reading those pieces as messages.

Over the corpus the coverage tally goes from **9 of 21 distinct long polls (43%)** to
**18 of 18 (100%)**. Over our own suite it stays at **10 of 10**, which is the same point the
import made in the first place: our suite exchanges what it was taught to exchange, so its
figure measures the script. What did move on our side is the catalogue — **28 replies decode
field by field, now 36 of 124.**

---

## The numbers, both bodies of traffic, counted with `sasctl explain`

```
sasctl run tests --stimulus software --report jsonl --out ours
sasctl explain ours/run.jsonl

sasctl import "C:\igs\GSA SAS Host Simulator v1.1" \
              C:\IGT3\TXRXData.dat \
              C:\Progetti\libsas\utils\igt\TXRXData.dat --out corpus
```

| | packets | distinct long polls | explained, before | explained, after |
|---|---|---|---|---|
| our own suite, all three trees | 582 | 10 | 10 (100%) | 10 (100%) |
| the imported corpus, all three sources | 1,128,939 | 21 → **18** | 9 (43%) | **18 (100%)** |
| the catalogue, replies with a layout | — | 124 | 28 | **36** |

**The distinct-command count fell, and that is the second finding.** Three of the twenty-one
commands the corpus was said to contain — `$01`, `$20` and `$30` — were never sent by anybody.
Each was the second byte of a piece of some other message. Counting them as traffic inflated both
the numerator and the denominator of a coverage figure, and one of them, `$01`, was counted as
*explained*: a fragment of a `$1F` reply was read as a shutdown command and reported as
understood.

### What moved, command by command

| | before | after | proved by |
|---|---|---|---|
| `$1F` machine identification | undecoded over the corpus | explained | the layout was already right; a fragment was denying it |
| `$54` version and serial number | undecoded over the corpus | explained | as above |
| `$20` total value of bills | "undecoded" | **not in the corpus** | its only packet was a two-byte piece of a `$7E` reply |
| `$2F` selected meters for game n | undecoded | explained | the legacy tester's own capture, ten requests and eight replies |
| `$35 $36 $37` bill counts | undecoded | explained | the legacy tester's own capture, one exchange each |
| `$4C` secure enhanced validation id | undecoded | explained | the toolkit's 10.2.1 traces, sixteen packets |
| `$4D` enhanced validation information | undecoded | explained | the toolkit's 10.1.3 step 8 trace, one filled entry and five empty |
| `$51` number of games implemented | undecoded | explained | 161 copies of one reply across the toolkit's traces |
| `$86` multiple level progressive | undecoded | explained | the toolkit's traces and session log, fifty-two broadcasts |
| `$30` | not named at all | **not a command** | four pieces of split `$54` replies |
| `$01` disable | explained | **not in the corpus** | one piece of a split `$1F` reply |

---

## `$20` already had a layout, and that is worth saying plainly

The backlog listed `$20` among the commands with no layout. It has had one since M2 — the
bill-value meter is what `note-moves-bill-meters` asserts on, and it decodes in our own suite. It
appeared on the backlog because the tally marks a command undecoded when **any** packet carrying
it went unexplained, and the corpus holds exactly one packet whose second byte is `$20`:

```
10.2.1/step13.trc   line 43   EGM  0.6   [07] 20
```

Two bytes. It is the tail of a reply to `$7E`, which the tracer printed as three lines — `01`,
then `[7E]`, then `[07] 20` — of which only four bytes of an eleven-byte reply survive. The
toolkit's own prose under that line reads *"Send dollar value of bills meter"*, so the tool that
wrote the capture made the same mistake reading it back.

---

## What a fragmented capture is worth

The choice was between reading the pieces as written, stitching them with the fault reported, or
naming them as unreadable. **They are named as unreadable**, and no byte is repaired.

### Reading them as written was inventing traffic

A reply's second byte is the machine echoing the command it was asked. In a piece of a reply that
byte is whatever payload happened to land there — an ASCII `0` from a serial number, the `20` of
a date. Read as written it produced three commands the corpus never contained, denied two the
corpus contained many times over, and reported one of the invented ones as *explained in full*.
That is the failure mode the extended log exists to prevent, running inside the extended log.

### Stitching cannot be done, and would buy nothing where it can

The corpus holds thirteen machine pieces, and they came from eight split messages:

* **Two are head pieces with no tail at all** — `01 54 09 36 30 32 31 30` in `10.2.1/step3.trc`
  and `10.2.1/step5.trc`. The reply's own length byte says nine data bytes follow and six are
  present, and the next line in each file is a different message entirely. There is nothing to
  stitch them to.
* **One is the tail of a triple that does not add up to a message.** The `$7E` reply above yields
  four bytes of eleven. Stitching produces a truncated frame whose check sequence still fails — a
  guess presented as a recovery.
* **Ten are five head-and-tail pairs that do reassemble** into a correct fourteen-byte `$54` or
  twenty-four-byte `$1F` reply. In **all five** of those the tracer printed the intact reply a line
  or two later anyway, so stitching recovers nothing that is not already in the capture and already
  decoded.

So stitching is impossible for three of the eight split messages and pointless for the other five,
and in every case it would replace a fault in the recording with this build's guess about the
message. That is the rule of `D34` — nothing goes into an explanation that was not already in the
record.

### The rule that was implemented

> A run of bytes long enough to carry a check sequence whose check sequence does not check out is
> not read as a message. It is recorded verbatim, the failed check is reported beside it, and no
> command is read out of it.

The check sequence is the protocol's own test of whether a run of bytes is one message — SAS
delimits messages with silence and with nothing else, so there is no other test available. When it
fails, no byte of the frame is established, the command byte included.

A machine message shorter than four bytes falls under the same rule, because a reply is at least
an address, a command and a check sequence. The two shorter forms a machine may send are handled
before it: a one-byte answer, and the **busy response** — the address and a zero, two bytes and no
check sequence — which this build had not recognised at all and used to read as a reply to a long
poll `$00`.

The rule runs in both directions. Two host messages in the corpus fail it, and they are a genuine
finding rather than a fragment:

```
10.2.1/step10.trc   HOST   [01] 4C 01 00 10 01 00 00 76 6A     computes 75 6A
10.2.1/step12.trc   HOST   [01] 4C 00 00 00 00 00 00 24 F7     computes 23 F7
```

Both differ from a correct frame by exactly one in the low byte of the check sequence. A machine
receiving either would refuse it. Naming them as `$4C` and reporting the bad check would have been
*probably* right, and probably right is what this rule exists to refuse; the bytes and the
arithmetic are both on the record, so a reader can see what they almost certainly were.

**What the rule costs, stated rather than discovered later.** A genuinely corrupted reply from a
real machine no longer says which long poll it was answering. That is the same trade as everywhere
else here: the command byte of a broken frame is not established, and a reader who wants the likely
reading has the bytes and the poll that preceded them. Nothing in either body of traffic loses a
real command to it — the corpus's fifteen are the thirteen pieces and the two bad host frames, and
our own suite produces none at all.

### A correction to what `M4-import.md` said

That document explained the fragments by the tracer starting a new printed line at every byte
carrying the ninth bit. That is true and it is not the whole cause. Two of the splits carry **no
ninth bit anywhere**:

```
10.2.2/step2.trc:84   EGM  18.8   01 54 09 36 30 32 31 30
10.2.2/step2.trc:86   EGM  28.8   30 30 30 31 5B C9
```

Each piece carries its own interval, so the tracer timed them as separate events. Something else in
that recorder splits a reply as well — a read boundary, most likely — and the ninth bit is one
cause of fragmentation rather than the only one. The rule above does not depend on knowing which,
which is the reason it is written in terms of the protocol rather than in terms of that tool.

---

## `$30` is not a command

It is in no list this build holds because it is not one. It is not a long poll in the gaming
machine library's headers, it has no template in the conformance toolkit's `PollTemplates.xml`,
and the legacy tester's `sastest.ini` names `$30` only in its *exception* list, as
`Unknown exception!`.

Every packet the tally attributed to it is a tail piece of a `$54` reply:

```
10.2.2/step1.trc   #835   30 30 30 30 31 5B C9
10.2.2/step1.trc   #1071  31 30 30 30 30 31 5B C9
10.2.2/step2.trc   #42    30 30 30 31 5B C9
10.2.2/step2.trc   #703   30 30 30 31 5B C9
```

`30` is the ASCII digit `0`, out of the serial number `"100001"`, and `5B C9` is the check
sequence of the whole fourteen-byte reply — which is why none of these four check out on their
own. Read as a message, the first byte becomes an address and the second a command, and a
command `$30` appears that nobody ever sent.

---

## The eight layouts, and what proved each

Every one was read out of the gaming machine library's own handler in
`C:\Progetti\libsas\src\lp.cpp` and each names that handler, the way the existing layouts do. Then
every one was checked against bytes from the corpus — the tests are in `CorpusLayoutTests`, which
names the capture for each.

### `$2F` — selected meters for game n · `lp2F_SendSelectedMetersForGameN`

The same idea as `$6F` and a different shape, in two ways that both matter.

* **A meter code is one byte**, not two. The request `01 2F 03 00 00 05` asks for meter `$05`;
  read as `$6F` does it, the two bytes `00 05` would name meter `$0500`.
* **A meter's value carries no width.** `$6F` puts a size byte before each meter; `$2F` does not,
  and the handler writes `getMeterMinimumSize(code)` bytes. So the only thing in the message that
  says where one meter ends and the next begins is *which meter it is*.

That needed a general addition rather than a decoder for one command, the same way `$6F` did: a
field whose length comes from a table keyed on an earlier field's value. `SasMeterWidths` is that
table, copied from `getMeterMinimumSize` as a list of codes rather than derived from each meter's
unit — the two are not the same set, and a cents meter the library did not name there is still
four bytes wide on the wire.

Proved against the legacy tester's own 4 KB capture, which carries ten of these requests and eight of the replies to them:

```
01 2F 03 00 00 05 26 7C              ask for meter $05 of game 0
01 2F 07 00 00 05 00 00 00 26 58 B3  seven bytes: game 0000, meter $05, four bytes, 26
```

### `$35 $36 $37` — the twenty-, fifty- and hundred-unit bill counts · `lp35`/`lp36`/`lp37`

Four bytes of binary coded decimal, through the library's shared `lpXX_SendMeter`. Proved against
the legacy tester's capture, which asks all three in one run.

**Their fields are deliberately not offered under a meter name, and that is a finding rather than
a formality.** `lpXX_SendMeter` divides the value it reports by the accounting denomination when
the machine's meter mode is automatic or manual and the meter is not a multi-game meter;
`lp1E_SendBillMeters`, which answers the same six meters, does not. So `$1E` and `$35` do not
always report the same number for the twenty-unit bill count. The meter index prefers a dedicated
single-meter command over a bulk one, and left alone it would have moved `bills_20`, `bills_50`
and `bills_100` off `$1E` — silently changing what an existing test measures. A script that has
always read the meter from `$1E` still reads it from `$1E`; a test that wants the dedicated
command's answer asserts on the field of that reply.

**`$31` to `$34` were not added.** They share the handler and the six bytes, so writing them from
the shape of their neighbours would have taken a minute — and no capture here carries one. They
stay undecoded, which is the difference between a layout and a guess that happens to be right.

### `$4C` — set the secure enhanced validation identifier · `lp4C_SetSecureEnhancedValidationId`

Six bytes each way, and the two halves are the same six: the machine answers with what it now
holds, which is what the host sent unless the host sent a zero identifier — the library's way of
asking without setting.

Both numbers are **plain binary, least significant byte first**, which the handler states in a
comment and then does by hand rather than with a memcpy. Read big-endian the identifier in the
corpus comes out as `$011000` instead of `$100001`, which looks just as much like a number
somebody chose.

Proved against the toolkit's 10.2.1 traces: `01 4C 01 00 10 01 00 00` sets identifier `$100001`
and sequence 1, and `01 4C 01 00 10 99 00 00` comes back after the machine has advanced the
sequence to `$99`.

### `$4D` — one entry of the validation buffer · `lp4D_SendEnhancedValidationInfo`

Thirty-three bytes plus the check sequence, and the longest layout established here. The function
code chooses the entry: zero reads the current one and advances the machine when the host
acknowledges, `$FF` looks at it without advancing, anything else is an index.

The offsets were fixed by the **clock**, which is the only reason to trust them. The reply in the
toolkit's `10.1.3/step8.trc` reads 07/20/2026 17:57:38, and that trace's own header says the
recording started on `07/20/26`. A layout one byte out would put the year where the day is and
still decode into numbers that look like a date.

```
01 4D 00 01 07 20 20 26 17 57 38 01 69 37 04 85 31 50 32 00 00 10 00 00 01 00 ... 7A E0
      ^^ ^^ ^^^^^ ^^^^^ ^^^^^^^^ ^^^^^^^^^^^^^^^^^^^^^^^ ^^^^^^^^^^^^^^ ^^^^^
      |  |  |     |     |        validation number       amount         ticket number
      |  |  |     |     time 17:57:38
      |  |  |     year 2026
      |  |  month 07, day 20
      |  index 1
      validation type 00
```

Two of its fields are binary least significant byte first rather than BCD — the ticket number and
the pool identifier, both written with a `memcpy` from a machine word. The ticket number reads 1,
which is what a first ticket should read; big-endian it would read 256.

Five of the corpus's six `$4D` replies are the all-zero form the library sends when the buffer
holds nothing, and they are why every date field permits zero as well as a real month.

### `$51` — number of games implemented · `lp51_SendNumberOfGamesImplemented`

Two bytes of binary coded decimal. The most-repeated reply in the whole corpus: 161 copies of
`01 51 00 09 45 58` across the toolkit's traces, nine games every time.

### `$86` — multiple level progressive · `lp86_MultipleLevelProgressive`

A broadcast, and the only command modelled here that normally goes to address zero, where nothing
answers at all. Sent to a machine's own address the handler acknowledges it, so the layout's
response shape is an acknowledgement and its broadcast flag is set.

The length byte counts the group byte **plus six bytes per level**, so a message carrying two
levels reads `$0D`. A reader that took it for a count of levels would walk off the end.

Proved against the toolkit's session log:

```
00 86 0D 01 01 00 00 00 04 14 02 00 00 00 04 14 A2 90
         ^^ ^^^^^^^^^^^^^^^^^^ ^^^^^^^^^^^^^^^^^^
         |  level 1 at $4.14   level 2 at $4.14
         group 1
```

---

## What this leaves

* **Our own suite's figure did not move, and cannot.** None of these eight commands is sent by any
  script in the three trees, and the fake machine answers none of them — it refuses a command it
  has no values for, which is what it did before these layouts existed and what it does now. Giving
  it answers for them is the obvious next step and is not done.
* **`$31` to `$34`** are undecoded on purpose, as above.
* **`$21`, `$6E`, `$72`, `$B0`** and the rest of the catalogue's 88 remaining commands are
  untouched. The corpus does not contain them either, so the next figure to move needs traffic
  nobody here has yet — which is what replay and a second capture source are for.
* **The real-time event format is still not decoded**, unchanged from `one-behind.md`.
