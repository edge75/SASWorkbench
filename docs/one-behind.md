# One behind

Two defects, found while chasing a test that failed about one run in ten and only when all four
test projects ran at once. Both had the same shape: **one message's answer returned under another
message's name**, reported as success.

Neither was a flaky test. The flakiness was the symptom; CPU contention was only what made a reply
arrive late often enough to notice.

---

## The wire, and why a late reply is indistinguishable from the next one

SAS marks the start of a message with the wakeup bit and marks the end of one with nothing at all.
A reply has finished when the line goes quiet. That makes silence the only frame delimiter there
is — and it means a reply that arrives *after* the host stopped waiting cannot be told apart, by
looking at it, from the reply to the poll that follows.

The host never resynchronised the line after a transaction timed out. So a single slow answer put
the link one transaction behind, permanently, for the rest of the session:

```
long poll $2F came back Answered as 011F51580BC7
```

`$2F` was asked for. `$1F`'s frame came back. The outcome was **`Answered`**, so the frame went on
to be decoded as though it were the meters the caller asked for. Every later reply in that session
was one behind the same way, which is why the symptom was never one unreadable meter but a whole
contiguous tail of them.

**Fixed** by discarding whatever is on the line in the instant before a poll goes out. Placement is
the whole of it: a synchronisation poll and the quiet gap after it sit between the top of a cycle
and the message whose answer is about to be read, and a late reply landing in that gap is exactly
the one that would otherwise be read as the next reply. Draining at the top of the cycle left that
window open, and left a general poll's `00` being read as a long poll's answer.

What is discarded is **counted and recorded on the wire log**, never swallowed. A host that quietly
dropped a machine's answers would look perfectly healthy doing it.

## The handoff, one layer up

The same fault existed again between the caller and the poll thread, and the wire fix did not touch
it.

A caller that gives up waiting has not cancelled anything — the poll thread may still be in the
middle of the transaction. When it finished, it published its result to whoever was waiting next:

```
Expected: 47      ($2F, what was asked for)
Actual:   31      ($1F, what the previous caller had abandoned)
```

**Fixed** by giving every submission a ticket. A result whose ticket has been retired belongs to
nobody and is dropped rather than handed on. It stays on the wire log either way.

---

## What the tests are worth

Both fixes are covered by tests that were checked to fail without them and pass with them.

The first version of those tests staged the lateness with a sleep, and failed about one run in ten
under load — a timing test for a timing bug, proving nothing. They now stage it instead: a machine
told to stay silent times out on the clock the engine already owns, and the overdue answer is put
on the line by hand. Nothing about what they prove depends on any thread being scheduled promptly.

## The flakiness that was left, which was not a bug

With both defects fixed, what remained was tests carrying wall-clock assumptions that four test
assemblies running at once could break — a fixture given 200 ms to answer, on a machine where its
thread might not be scheduled for longer than that. Those are timing assumptions in tests that are
not about timing, and they were given headroom rather than left to fail: the one test that *is*
about a slow machine keeps a deliberate ratio between the delay and the timeout, and the one that
expects silence keeps a short timeout, because a loaded machine can only make silence more likely.

## A build break found on the way

`main` did not build in Debug. Visual Studio had written `<Build Solution="Debug|*"
Project="false" />` into the solution for two projects when it was opened, and that was committed.
CI builds Release, so CI stayed green while every local Debug build failed on projects the command
line and the window both reference. Restored.

---

# The third defect — 2026-09-07

The two fixes above were real and they were not all of it. `BLOCKERS.md §4` recorded three of
five full-solution runs still failing one timing-sensitive engine test, a different one each time,
and it reproduced with the extended-log wave reverted. This is what that was.

## Reproduced deliberately, before any code was read

The four test assemblies were run concurrently, repeatedly, at stated levels of contention — with
nothing else on the box, and with deliberate background load — because "run the suite a few times"
is not a controlled condition. That turned out to matter more than expected:

| contention | runs | runs with a failure |
|---|---|---|
| four assemblies, idle box | 8 | **0** |
| four assemblies, box already busy | 10 | **4** |
| four assemblies + eight spinners | 8 | **7** |

**The rate is a function of ambient load, not of the suite.** The two green runs reported during the
extended-log wave prove nothing, and neither would ten. Any claim about this suite's flakiness has
to name the load it was measured at.

## The defect: draining the line is not attribution

`Resynchronise` drops whatever is on the line in the instant before a poll goes out. That is the
first fix above, and it is sound as far as it goes. What it cannot do is catch an answer that
arrives *after* it — and there is nothing between the drain and the transmission but the
transmission itself. On a slow machine the overdue answer to the previous transaction lands inside
the next one's window, and the engine attributed a message to a transaction **by when it arrived**.

Proved by staging rather than by argument. A line that hands back prepared messages, one per read
with a silence after each, delivered a well-formed answer to `$1F` as `$2F`'s window opened:

```
long poll $2F came back Answered as 011F51580BC7
```

The same sentence as the first defect in this document, from a different cause, with the first fix
in place. The engine checked the address and the check sequence and never checked the one byte that
says which poll a reply answers.

## The fix: a message is attributed to the transaction it can belong to, or it is not attributed

Silence is SAS's only frame delimiter, so a late message cannot be told apart by *when* it arrived.
It can be told apart by *what it says*. So the engine now reads until a message arrives that could
be this transaction's answer, and discards — counted, recorded, never swallowed — anything that
provably could not be. The machine's time to answer is not extended by foreign traffic: the
deadline runs from the moment the poll was sent, so a noisy line cannot make the host more patient
than a quiet one.

What counts as possible was read off the gaming machine library's own senders, not reasoned about:

* an **acknowledgement** is the address alone, and a **rejection** is the address with bit 7 set;
* a **busy response** is the address and a zero — two bytes, no check sequence, and **the one reply
  to a long poll that echoes no command**. A rule written from the specification's response layout
  alone would have thrown it away and reported a silence;
* every other reply echoes the command it answers, in every handler that sends one;
* a **general poll** is answered by one byte, or by the real-time event format, which carries the
  address and a fixed identifier where a long poll's reply carries its command. The library's own
  log reader discriminates on exactly that byte.

One ambiguity is left rather than guessed at: an exception code equal to the machine's own address
cannot be distinguished from an acknowledgement, and is read as one.

### The same fault ran the other way, and that half was worse

A stale long poll reply landing in a *general* poll's window had its first byte read as an
exception code. That does not produce an unreadable meter; it **invents a machine event that never
happened**, and somebody would go looking for the door that opened. A general poll's answer is now
attributed the same way, and a reply in the real-time event format — which this build does not
decode — is named as undecoded rather than having a code read out of it.

## What was left was not a defect, and is now off the clock

With attribution fixed, every remaining failure under load was an honest `Silent`: *the machine did
not answer*. It was true. Both software machines waited for a **timed** silence before answering —
a ten-millisecond read that had to expire — so their turnaround was ten milliseconds plus whatever
a loaded scheduler added, against a host that waits two hundred. A timed wait is precisely what
contention stretches.

They no longer wait for it. A read takes everything the line was holding under one lock, so a read
that did not fill the buffer ends the message, and the machine answers on that instead. Nothing
about a machine's turnaround now depends on a timer firing promptly.

The fixtures that are **not** about the host's patience were then given a patient host, and the two
that are about a clock keep their own short ones. A session can now be told how patient to be at
all, which a script run could always say and a session could not.

## Rates after, at the same stated loads

| contention | runs | runs with a failure |
|---|---|---|
| four assemblies, idle box | 10 | **0** |
| four assemblies + eight spinners | 8 | **1** |

The one failure at eight spinners is the honest form of the thing this document is about: *the poll
engine did not reach this command in time*, after waiting three seconds for a thread on a box with
twelve runnable processes. Nothing reports one command's answer under another command's name any
more, at any load.

Two intermediate measurements, because they say which change did what: with attribution fixed and
nothing else, eight spinners still failed 6 runs in 10 — but every failure had become an honest
silence rather than a wrong answer. Taking the timed wait out of the machines' turnaround and
giving the fixtures a patient host is what moved the rate.

## What is still true, stated rather than discovered later

* **A busy response is reported as `Answered`.** It is a machine saying *ask again later*, not an
  answer, and calling it one is a pre-existing overstatement this work made visible without fixing.
  Nothing in the suite sends one today, which is why it was left rather than changed in a wave
  about something else.
* **The real-time event format is not decoded.** A general poll answered in it is named and
  recorded; no exception is reported from it. Enabling real-time events would therefore lose events
  rather than misreport them — a gap, honestly labelled, and a candidate for the breadth work.
* **Attribution shrinks the window; it does not abolish the ambiguity.** A one-byte reply whose
  value happens to equal the machine's address is read as an acknowledgement, and a stale message
  arriving inside the quiet gap of a real reply is read as part of it. Both are properties of a
  protocol that delimits messages with silence, not of this implementation.
