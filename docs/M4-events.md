# M4 — The real-time event format

**Status: partly done, 2026-09-08.** The format is decoded and measured on the wire. The ten
payloads are established from the machine library's own builders and **none of them has been
produced**, because the stimulus that would produce four of them stopped working on this machine
and nothing here drives the other six.

```
sasctl catalogue events                            what an exception carries after its code
sasctl events --list                               what a measurement would cause, and what it cannot
sasctl events --tcp 127.0.0.1:9760 --egm-resources <machine>/Resources
sasctl run tests --stimulus software --real-time-events
```

---

## The numbers, before and after

| | before | after |
|---|---|---|
| the catalogue, replies with a layout | 83 of 124 | 83 of 124 |
| **exception payloads with a layout** | — | **10**, and 2 more the library names and never produces |
| **payloads checked against bytes a machine sent** | — | **0 of 10** |
| our own suite, distinct long polls explained | 10 of 10 (100%) | **11 of 11 (100%)** |
| our own suite, distinct events explained | — | **4 of 4 (100%)** |
| the imported corpus, distinct long polls explained | 18 of 18 (100%) | 18 of 18 (100%) |
| the imported corpus, distinct events | — | **0** — nobody in it ever turned reporting on |
| meters a script can read by name | 16 | 16 |

743 tests pass, forty-seven of them new.

**The reply figure could not move and did not**: this wave establishes no reply layout. The row
that matters is the third, and it reads zero. What that zero means is the whole of the second half
of this document.

---

## What a machine sends when it is reporting in real time

A machine told to report in real time stops answering a general poll with one byte and answers with
a whole message instead:

```
01 FF 11 14 A4
^^ ^^ ^^ ^^^^^
|  |  |  check sequence
|  |  the exception code
|  the marker that says this is an event and not a reply
the machine's address
```

Whatever that exception carries goes between the code and the check sequence. Most carry nothing,
and for those the three bytes are the whole event.

**Measured, not read out of source.** `QSP_DemoOpenGL` sent eleven distinct exceptions in that
format on 2026-09-08 — `$11 $12 $15 $16 $17 $18 $19 $1A $3C $82 $83` — and every one is in
`RealTimeEventTests`, copied verbatim with its check sequence. So the frame is established against
a real machine even though no payload is.

Three first-party statements of the frame agree, and the third is the one worth having: the machine
library's transmit path builds it, its own logger reads it back the same way, and the machine sent
it. Nothing from another description of SAS was used.

### The marker is what makes it unambiguous

A reply and an event both start with the machine's address, and the byte after it decides which.
No long poll is answered with `$FF`, so a multi-byte answer carrying it is an event and a
multi-byte answer carrying anything else is a reply. That is why the poll engine can read one
without knowing which poll it answers — and reading the first byte of an event as an exception
code, which is what this build did before, invents a machine event out of a message about another
one.

---

## The ten payloads

Each is read from the library function that fills `exception.data` byte by byte and sets
`exception.length`; the transmit path copies exactly that many bytes after the code. So a layout
here is the assignment list, and nothing is inferred from a field's name.

| | carries | read from |
|---|---|---|
| `$4F` note accepted | country code, denomination code, that denomination's count | `SAS_BillAccepted` |
| `$7C` legacy bonus | multiplier, multiplied win, tax status, bonus | `SAS_LegacyBonusAward` |
| `$7E` game started | credits wagered, coin in, wager type, progressive group | `SAS_RTE_GameStart` |
| `$7F` game ended | the win | `SAS_RTE_GameEnd` |
| `$88` reel stopped | which reel, which position | `SAS_RTE_ReelStopped` |
| `$8A` game recalled | which game, how far back | `SAS_RTE_GameRecallEntry` |
| `$8B` cards held | one bit per card | `SAS_RTE_CardHeld` |
| `$8C` game selected | which game | `SAS_RTE_GameSelected` |
| `$9E` session started | length, game, denomination, wager, games in session | `SAS_RTE_SessionStart` |
| `$9F` session ended | length, games, sessions, wins, extra coin in | `SAS_RTE_SessionEnd` |

Three things in that table are findings rather than transcription.

**`$7E` carries two numbers in two different units, in one event.** Credits wagered is a number of
game credits and does not move when the accounting denomination does; the coin in meter beside it
is in accounting units. That is the pair a validation case wanted this layout for, and reading
either as the other is a mistake nothing else on the wire would catch.

**`$9E` and `$9F` are written differently from the other eight.** Their builders write plain binary
where every other builder converts to binary coded decimal first, and they begin with a byte
holding the length of what follows, which no other event does. Recorded as the library writes it
rather than smoothed into the shape of its neighbours.

**Two exceptions the library will only ever send in this format are ones nothing ever queues.**
`$87` and `$89` are on the transmit path's real-time-only list, so a host that never enables
reporting cannot see them — and no function in the library or the platform above it queues either,
so no machine running this library can produce one. Their payloads cannot be established here at
all, and writing them from somebody else's model is the guess this project refuses.

### What the overlay diff said, and what the library says

The overlay diff named eight exceptions that carry data: `$4F $7C $7E $7F $88 $8A $8B $8C`. The
library has **ten**, adding the two session events. The extra two are ours and not theirs, which is
what a model derived from the library rather than from a display model is for.

---

## The measurement, and why it produced nothing

`sasctl events` is the counterpart of the catalogue sweep, for the half of the protocol a sweep
cannot reach: a long poll can be asked for and an event has to be caused. It prints what it is
about to send before opening the link, because it sends three commands the sweep withholds on
purpose — `$0E` to turn reporting on, `$AA` to make the machine play by itself, and `$06` to tell
it to take notes — and it turns reporting and auto-rebet off again and says whether that was
acknowledged.

Against `QSP_DemoOpenGL` with the standard-validation image, on 2026-09-08:

```
  $0E on   Acknowledged
  driving the machine
    every door opened and shut, the note acceptor cycled, the reset keyswitch turned
    the machine went quiet after 30 exception(s)
    Game Menu pressed              no $8C in 5s
    $06      Acknowledged
    a note of 2000 cents offered   no $4F in 10s
  $AA on   Acknowledged
    Play pressed                   no $7E in 10s
  $AA off  Acknowledged
  $0E off  Acknowledged

  11 exception(s) arrived in the event format carrying nothing
  0 of 10 payloads checked against bytes a machine sent
```

**Every one of the ten is recorded as unproduced with the reason, and the reasons are three
different kinds.**

* **Nothing here drives four of them and it is a property of the machine, not of the measurement.**
  `$8A` needs an audit-replay screen, `$8B` needs a card game and this machine has reels, `$8C`
  needs somebody working the game menu, and `$7C` needs the host to pay a bonus, which is money.
* **Two cannot be produced by any machine running this library.** Nothing in the platform above it
  ever calls the session builders, so `$9E` and `$9F` are unreachable rather than merely undriven.
* **Four should have been produced and were not**, and that is the finding below.

### The note-and-play stimulus stopped working, and it is not this wave's doing

> **Diagnosed 2026-09-08, and it is the machine's.** The machine's developer licence is gone, and a
> machine with no licence raises a lockup that disables its bill validator, so every note offered is
> returned before the money handler sees it. Nothing here can clear it and no long poll can either —
> only a person entering a key in the machine's own audit menu. The full chain, measured link by
> link, is `BLOCKERS.md` Trap D. A second `sasctl events` run after the diagnosis produced **0 of 10**
> payloads, so nothing below changes.

`$4F`, `$7E`, `$7F` and `$88` all hang off one thing: the machine taking a note, which is the credit
a game needs. It did not take one.

**Measured with reporting off, so it is not caused by turning reporting on.** The regression case
`tests/regression/meters/note-moves-bill-meters.sas` — the M2 case that established this path —
fails against the same machine today with no reporting anywhere near it: *"nothing in 5000ms"*, and
both bill meters flat.

**The fault is on the machine's side of the shared region, and its own log says so.** The library
never wrote a `$4F` at all, so `SAS_RegisterBillAccepted` was never called: the note never reached
the platform's money handler. Everything on this side of it works and was checked — the door mask
raises real door exceptions, the audit keyswitch opens and closes the machine's own menu, the note
mask reaches the peripheral layer (its own `BV Link Down`/`Link Up` lines appear when the acceptor
is cycled), and the auto-rebet command reaches the game (`Auto rebet enabled`). What does not
happen is the note being accepted and the game being started by a button.

What was ruled out on the way: a second demo left running from an earlier attempt, a corrupted
memory image (one boot did master-reset, and every run after it restored `golden.nvram.gz` first),
a note offered into the burst of exceptions a cold start makes, the machine sitting on its game
select screen, an attendant condition the reset keyswitch would clear, and the host having left the
note acceptor disabled.

This is a blocker of its own and it belongs in `BLOCKERS.md`, not here. It costs four payloads.

### A second finding, which the measurement did produce

**The machine under test answers nothing at all to a general poll while reporting in real time and
its queue is empty.** 686 host messages, 36 replies: it answered only the polls it had something
for. The library the catalogue is generated from does not behave that way — it sends the event
format with a code of zero — and the two are different revisions:

```
C:\Progetti\libsas   4dc57d0   is 24 commits ahead of the revision the machine was built from
  ...
  d6fe417  IGS-271 answer a general poll with no activity in real time event mode
  ...
1f41480   the revision QSP fetches, on the pinned branch
```

So the measurement found, on the wire, the defect that commit fixes. It also names a gap worth
carrying: **the generator reads the checkout and the machine runs the fetched revision**, and the
two can disagree about anything. Every layout this project has established so far comes from the
checkout; every byte it has checked one against comes from a machine 24 commits behind it.

---

## What changed in this build

* **`SasEventLayouts`** holds the ten payloads, each naming the library function it came from.
  `ConfirmedByCapture` is false on all ten and a test asserts that, so the day a measurement
  produces one the flag has to be set deliberately.
* **`SasMessageDecoder.DecodeEvent`** reads a payload against its layout, reporting offsets from the
  start of the frame the way a reply does.
* **The poll engine reads the event format** instead of naming it. It checks the marker, the check
  sequence and the address before reading anything, and a frame that fails any of the three is
  recorded and dropped rather than read. The exception reaches a script as the exception it is,
  with its bytes attached, whether or not anything can read them.
* **The extended log counts events in a tally of their own**, printed only where there are any: a
  run in which reporting was never on has no events, and reporting zero would read as a gap.
* **A script can assert on a payload**: `host expect field credits_wagered of event game_started
  == 2 within 5s`. It does not consume the event, unlike an exception expectation, because three
  assertions about three fields of one event have to see the same event.
* **The software machine answers in the event format**, and queues what the library queues: the
  note exception carrying the country, the denomination and the count, and a game start and a game
  end for every round. With reporting off it reduces the note exception to the per-denomination
  code the way the library does, so nothing a host already saw changes.
* **Turning reporting on is opt-in and named.** `sasctl run --real-time-events` is what lets a case
  ask for it against a real machine; without it every such case skips. The software machine offers
  the capability of its own accord, because that machine is one this build made and knows.

### The software machine's silence, modelled rather than smoothed over

With reporting off, the library dequeues one of the four event-only exceptions and then sends
nothing at all — not the code, and not the no-activity byte either — so that general poll goes
unanswered. The software machine does the same, and a test pins it. A host that reads silence as
"nothing queued" is being told something false, and that is a property of the machine a test should
be able to see.

---

## What was deliberately not done

* **No payload was guessed at from another description of SAS.** The overlay diff's eight are named
  by the conformance toolkit and this build reads none of its words; the ten layouts are the
  library's assignment lists.
* **`$87` and `$89` have no layout and cannot get one here.** Nothing queues them.
* **The note-and-play blocker was not chased to the bottom.** It is recorded with everything ruled
  out, and it is a wave of its own.
* **No meter a script can read by name was added.** The index stayed at sixteen, for the same
  reason the last wave gave.
* **The three validation cases that name `decoder.lp7F` still name it.** They mean the game-end
  event and `$7F` is also the long poll that sets the machine's clock, so the requirement is true
  and is not what the case means. Correcting it would turn two note-only cases into errors, and
  writing those cases needs a machine that plays games. `decoder.event7F` is the name they should
  carry.
