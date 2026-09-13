# Handoff — does a real machine need the synchronisation poll?

**What you are being asked to settle, in one sentence:** whether a real gaming machine answers a
general poll addressed to itself when the poll before it was *another machine's* address rather
than a general poll of address zero.

**Why it is worth twenty minutes of your time.** It is the largest single lever on the round.

| | round of 100 machines |
|---|---|
| a synchronisation poll ahead of every general poll — what this build does today | **6.27 s** |
| no synchronisation polls at all | **3.15 s** |

Both figures are measured, not estimated, against a hundred software machines on one line. The
second one is only usable if a **real** machine behaves the way the specification's rule implies it
should, and nothing available here can settle that:

* our own software machine models no such rule at all — it answers its own address whenever it sees
  it;
* the gaming machine library resets its loop-break timer on **any** received address byte, so it
  does not require a different one either.

So both peers this project can reach say "yes" for reasons that prove nothing. A real machine is the
only thing that can answer.

---

## Before you start

* **Roughly 20 minutes**, most of it waiting for four short runs.
* You need the demo machine you normally use — the one `sasctl sweep` and `sasctl events` talk to
  over TCP. Nothing else is touched.
* **Nothing is written to the machine.** Every run below sends general polls and nothing else: no
  configuration command, no meter reset, no setter of any kind. Your NVRAM is not modified.
* You will produce **four numbers and four files**. Send me the files.

Make a folder for the results:

```
mkdir C:\Progetti\SasWorkbench\runs\sync-poll
```

---

## Step 0 — bring the machine up, the way you always do

1. Start the demo (`QSP_DemoOpenGL`, the Release build through your gitignored preset).
2. Set `SASWB_QPM_TRACE=1` in the shell you are about to use, so anything that goes wrong explains
   itself.
3. Confirm the link is alive before measuring anything:

```
sasctl probe --tcp 127.0.0.1:9760
```

If that does not show a machine, stop here and fix the link first — every run below would otherwise
record silence and I would not be able to tell a machine that refused a poll from a machine that was
never there.

**Note the port.** If yours is not `9760`, substitute it in every command below.

---

## Step 1 — the control: one machine, synchronisation polls on

This is what the build does today. It must answer. If it does not, something is wrong with the
setup rather than with the question.

```
sasctl floor --tcp 127.0.0.1:9760 --addresses 01 --rounds 20 --out runs\sync-poll\1-control
```

**Read:** `answered at least one poll   1 of 1`.

> If it says `0 of 1`, stop. The rest of the runs cannot be interpreted.

---

## Step 2 — one machine, no synchronisation polls

Same thing, with the address-zero polls removed. Nothing else changes.

```
sasctl floor --tcp 127.0.0.1:9760 --addresses 01 --rounds 20 --no-sync-polls --out runs\sync-poll\2-one-machine-no-sync
```

**What this tells us:**

* `1 of 1` — the library does not enforce the rule at all: it answers its own address with no other
  address ever having been sent. The halving is safe on this machine, though still not
  specification-conformant in general.
* `0 of 1` — the library *does* enforce it. Step 3 then becomes the interesting one.

---

## Step 3 — the decisive run: two addresses, no synchronisation polls

The round now has your real machine at `01` and a second address `02` with nothing at it. The host
polls `$81`, then `$82`, then `$81` again — so every poll your machine receives for itself is
immediately preceded by **another machine's address** and never by address zero.

**This is the exact question.**

```
sasctl floor --tcp 127.0.0.1:9760 --addresses 01-02 --rounds 20 --no-sync-polls --out runs\sync-poll\3-two-addresses-no-sync
```

**What to read:**

* `answered at least one poll   1 of 2`, with `never answered   $02` — **this is the answer we want.**
  Your machine answered with only another machine's address in front of it, so the synchronisation
  poll is not needed in a round and the hundred-machine round costs 3.15 s rather than 6.27 s.
* `answered at least one poll   0 of 2` — your machine stayed silent. The synchronisation poll is
  required, the round stays at 6.27 s, and `--no-sync-polls` will be removed from the default path
  rather than left as a trap.

`$02` answering nothing is expected and is not a failure — there is no machine there. The command
says so.

---

## Step 4 — after a restart, which is the other half of the rule

The rule has two halves: after a loop break, and **after power-up**. Step 3 covers the first. This
covers the second.

1. Leave nothing running.
2. **Close the demo and start it again** — a fresh start, so the machine has seen no address byte at
   all since it came up.
3. As soon as it is up, run:

```
sasctl floor --tcp 127.0.0.1:9760 --addresses 01-02 --rounds 20 --no-sync-polls --out runs\sync-poll\4-after-restart-no-sync
```

**What to read:** the same two outcomes as step 3. A machine that answers in step 3 but *not* in
step 4 is the most interesting result of the four — it would mean the rule applies only to a machine
that has just come up, which a floor host can satisfy once at start-up rather than on every poll.
That would be the best outcome available and I would want the file.

---

## Step 5 — hand the machine back

```
sasctl nvram restore
```

Then close the demo.

Nothing above writes to NVRAM, so this is belt and braces rather than a repair — run it anyway, so
the habit stays intact.

---

## What to send me

The four directories under `runs\sync-poll\`. Each holds a `round.jsonl` with every symbol that
crossed the wire, so I can check the answer against the bytes rather than against the summary line.

Or, if that is easier, just the four lines:

```
1-control                    answered at least one poll   ? of 1
2-one-machine-no-sync        answered at least one poll   ? of 1
3-two-addresses-no-sync      answered at least one poll   ? of 2
4-after-restart-no-sync      answered at least one poll   ? of 2
```

---

## What I will do with each outcome

| steps 3 and 4 | what changes |
|---|---|
| both answered | A round of two or more machines stops sending synchronisation polls. The hundred-machine round becomes **3.15 s**, measured. The change is guarded by a regression case and the result is labelled as verified against a real machine, naming which one and when. |
| 3 answered, 4 did not | A round sends one synchronisation poll at start-up and none afterwards. Round still ≈3.15 s, and the start-up rule gets its own case. The best outcome available. |
| neither answered | The synchronisation poll stays on every cycle. The round is **6.27 s** and that is the honest figure. `--no-sync-polls` is kept as a measurement tool and documented as something no real machine accepts, rather than left looking like an option. |
| 3 did not, 4 did | A result nothing predicts. I would want the wire log before changing anything. |

Whatever comes back, the 20 ms quiet gap is untouched. It is what decides a message has ended, and
it is not part of this question.
