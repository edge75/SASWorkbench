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
