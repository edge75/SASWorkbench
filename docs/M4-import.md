# M4 — Reading traffic nobody here authored

**Status: done, 2026-09-07.** `sasctl import` reads the conformance toolkit's traces and its
binary session log, and the legacy tester's serial log, into the same record a run of our own
writes. `sasctl explain` renders an import with no knowledge that captures exist, and the two
renderings of an import are asserted byte-identical for the same reason a run's are.

The point of it is a number. Our own suite exchanges only what it was taught to exchange, so a
coverage figure taken over it measures the script and not the protocol.

---

## The number, counted rather than claimed

Both figures are the second tally the extended log prints — over the **distinct long polls the
traffic contained**, not over all packets, because a poll loop is the overwhelming majority of any
session and every general poll is explained in full.

| | packets | distinct long polls | explained |
|---|---|---|---|
| **our own suite**, all three trees | 579 | 10 | **10 (100%)** |
| **the imported corpus**, all three sources | 1,128,939 | 21 | **9 (43%)** |

```
sasctl run tests --stimulus software --report jsonl --out ours
sasctl explain ours/run.jsonl

sasctl import "C:\igs\GSA SAS Host Simulator v1.1" \
              C:\IGT3\TXRXData.dat \
              C:\Progetti\libsas\utils\igt\TXRXData.dat --out corpus
```

**One hundred per cent to forty-three, on the same build, by changing nothing but whose traffic it
is.** That is the whole argument for the import: the first figure was never wrong, it was just a
measurement of the script. Neither figure moves a decoder; both are inputs to the ones that will.

### The backlog, produced rather than guessed

Eleven commands the corpus contains and this build cannot decode, and one it cannot even name:

```
  known but not decoded    $1F $20 $2F $35 $36 $37 $4C $4D $51 $54 $86
  not named at all         $30
```

Two of those are worth reading twice. **`$1F` and `$54` are decoded in our own suite and undecoded
over the corpus** — a command counts as explained only when every packet carrying it was, and the
corpus contains fragments of both that no layout fits. That is not a gap in the layout; it is a
property of the recording, and the next section is why.

`$30` is in no list this build holds — neither the machine library's nor the toolkit's — so it
cannot be named at all, which is a different and more interesting kind of gap.

## What each format does and does not hold

| | wakeup bit | clock | what it cost to read |
|---|---|---|---|
| toolkit trace, `.trc` | **yes**, in brackets | intervals, from the end of the previous poll | the bracket convention had to be established |
| toolkit session log, `.log` | no | the recorder's own millisecond counter | the direction field had to be corrected |
| legacy serial log, `.dat` | no | **whole seconds** | nothing |

Two of the three kept only the eight data bits. That is carried on the record as a property of the
link rather than of any message, and the explanation then says nothing about the wakeup bit for
such a capture. Without it, every host message in those files reads as one no machine would have
answered — a finding about the recorder dressed up as a finding about the machine.

### The bracket in a trace is the ninth bit, and that was established rather than assumed

It could as easily have been a marker for the start of a printed line, and reading it as the
wakeup bit would then have invented a protocol fault on a hundred machine messages. Counted over
the thirty-two traces the toolkit ships:

* a bracket never appears anywhere but a line's **first token**;
* it appears on **all 2,005** host lines;
* and on **13 of 945** machine lines.

A line-start marker would be on every line of both. The ninth bit on every host address byte and
on roughly one machine byte in seventy is exactly what the protocol says plus what a receiver
misreading mark parity would add.

**It also explains the fragments.** The tracer starts a new printed line at every byte carrying the
bit, so one fourteen-byte machine reply with two spurious bits in it comes out as three lines. They
are read as the tracer wrote them, and the spurious bit is reported beside them. Stitching them
back together would hide a fault on the line and put this build's guess about the message in its
place — and it is those fragments that make `$54` an undecoded command over the corpus.

### The session log's direction field was documented wrong

The earlier note on this format gave direction **0** for the machine and 1 for the host. Measured
over the log the toolkit ships:

| direction | records | shape |
|---|---|---|
| 1 | 723 | host messages, including every general poll |
| 2 | **645** | always exactly one byte, never a poll byte, always straight after a host general poll |
| 0 | **0** | — |

So **2 is the machine**, and 0 does not occur. A direction this build has not seen is now named as
unreadable rather than guessed at, so if a 0 ever turns up we find out instead of quietly reading
it as something.

One more thing that format does not give: **the message index is not the order.** The two
directions number their messages separately — a host message 95 sits between machine messages 352
and 354 — so the file's own order is the only ordering there is.

## The finding nobody wanted: the large legacy capture is worthless for breadth

`PLAN.md` expected `TXRXData.dat` to be a source of protocol breadth. The 39 MB file at `C:\IGT3`
contains, counted over all 2,248,986 lines:

```
  timestamp lines   1,124,493
  TX lines          1,124,493
  RX lines                  0
  distinct messages         2      $80 and $81, 562,246 and 562,247 times
```

**Thirty-nine megabytes of a tester polling something that never answered.** No long polls, no
replies, nothing to decode. It contributes exactly nothing, and the import says so rather than
reporting a million packets at a hundred per cent coverage and leaving the impression that
something was learned.

The 4 KB `TXRXData.dat` that sits beside `sastest.exe` carries nine distinct long polls — more
protocol in four kilobytes than in thirty-nine megabytes. Size was not the thing to look at.

## Why it is a skipped run, and not a pass

An import asserts nothing, so every case in it is a `SKIP` with the reason spelled out, and a run
in which nothing was asserted already carries a non-zero exit code. Reading somebody else's
traffic proves nothing about this build and must not be able to look like a pass.

That is also why the importer builds a `RunReport` and hands it to the writers that already
existed, rather than writing a record of its own. The record shape, the extended log, the coverage
tally and the byte-identity of the two renderings are all the code that was already there, so an
import cannot drift away from a run.

## What is read, and what is deliberately left behind

Only the bytes, the direction and the timing. **Every one of these formats also carries the other
tool's own prose describing each message, and none of it is ingested** — the words this product
ships are its own, and a description written by somebody else would be indistinguishable from one
of ours once it was in the record. A test asserts that no imported record carries a note.

Anything in a file this build cannot read is **named**, never skipped: a trace line whose bytes are
not hexadecimal, a record of a kind or a version or a direction this format does not define,
trailing bytes too few to be a record. A reader that dropped what it did not understand would make
a partial import look like a whole one.

## A file has to be a capture on purpose

Detection reads the file, not its name. The first attempt recognised the session log by a version
word of one followed by a length — and picked up nine of the toolkit's **configuration** files,
producing pages of warnings about records that were never records. It now walks the first records
and requires each to carry the one version this format uses, a time somebody could have recorded,
and a kind the format defines.

## Known limits, stated rather than discovered later

* **A trace's times accumulate that tool's rounding.** Each interval is measured from the end of
  the previous poll to one decimal place, and a message's place in the capture is their running
  total, so the error grows along the file. Two traces in the corpus also record an interruption,
  and the gap across one is not an interval anything measured.
* **The legacy log has no sub-second clock at all**, so every message inside one second carries the
  same time. The intervals a poll loop is judged by are not in that file, and nothing here fills
  them in.
* **The session log stores a time per byte and this build keeps one per message.** A record holds
  one time, and inventing a per-symbol shape nothing else in the product uses to carry the rest
  would be a change to the record made for one importer.
* **A capture is one case, not many.** A trace of conformance step 5 becomes one case named after
  its file; nothing here splits a capture into the transactions inside it, and the separators the
  tracer writes are not carried. That is the obvious next thing and it is not done.
* **Nothing is replayed or diffed yet.** `PLAN.md`'s M4 also asks for replay against a machine and
  a diff of the verdicts, and for the toolkit's XML and the legacy `.ini` as overlays diffed
  against the header-derived catalogue. This wave is the reader and the measurement only.
