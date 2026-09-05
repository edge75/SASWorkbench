# M2 — EGM stimulus, the two-ended test

**Status: done.** A single script inserts a twenty-dollar note into a real gaming machine and
asserts both the exception and the meters over the SAS wire, unattended, with no person and
no peripheral simulator window. Doors, buttons and the machine's configuration menu are all
driven from this repository.

---

## What runs today, unattended, with no person and no simulator window

```
sasctl run tests/regression --tcp 127.0.0.1:9760 --stimulus qpm ^
    --egm-resources C:\Progetti\QSP_DemoOpenGL\bin\SasWorkbenchM0\x64\Resources

  PASS  a twenty raises its exception and moves its meters (8.2s, 7 assert(s))
  PASS  the main door raises the slot door exceptions (5.2s, 3 assert(s))
  PASS  the stacker door raises the cashbox exceptions, not the slot door ones (7.2s, 4 assert(s))
  PASS  the link comes up and the machine identifies itself (1.4s, 5 assert(s))
  over tcp 127.0.0.1:9760 (listen), proven, stimulus qpm
  PASS  4 passed in 22.2s
```

The first is the milestone: a note goes into the acceptor through shared memory, and `$4A`
and the bill meters are read back off the wire.

Both ends in one script: the stimulus happens at the machine, the verdict is read off the
wire. Every one of these files also runs against the software machine in this repository,
where they are part of the committed suite and run on every push — which is what `D7` asked
for, one script and two peers.

The stacker test is the one that earns its place. The peripheral simulator labels that switch
differently from the way this machine wires it, so a driver that trusted those labels would
open a different door; asserting that the cashbox exceptions arrive **and the slot door ones
do not** is what catches it.

## What was built

### The peripheral driver

`SasWorkbench.Qpm` memory-maps the shared region the machine reads its peripherals from and
writes the four stimulus masks. Three rules make it safe to run beside a live machine.

**It refuses when nothing is reading.** Writing to the region always succeeds — the bytes
land whether or not a machine exists — so every assertion after a stimulus would be made
against a machine that never saw it. The heartbeat is the only evidence anybody is reading,
and a heartbeat that has not moved is a refusal, not a warning.

**It refuses when something else is writing.** The peripheral simulator's own window rewrites
all four masks on every frame it draws, so a third party's writes survive about sixteen
milliseconds. The driver writes an inert bit, reads it back, and refuses if it has gone. The
decision recorded for this milestone is to replace that window for unattended runs rather
than to change it; its external-control mode belongs with the ticket work, where the
simulator is being changed anyway.

**It times by the machine's clock, not the wall's.** The machine samples its peripherals once
per heartbeat, so anything held for a fixed number of milliseconds is sometimes not seen at
all. Every action is held for two heartbeats.

### Everything is read from the machine, never from the simulator's labels

The machine declares its own doors, keyswitches and buttons in its resource directory, and
the driver reads them from there. A machine whose configuration cannot be read offers **no
door and no button at all**, and the tests that needed one skip by name — an index guessed
from the wrong source would open a different door and then assert on the exception a
different door produces, which reads as a protocol defect and is not one.

```
sasctl probe --stimulus qpm --egm-resources <machine>/Resources

Peripherals of the machine now running
  doors              tamper#1, tamper#2, main, topbox, logic, stacker
  buttons            Collect, Service, Reset/JKey, Play, Audit, Game Menu, Bet x1 … Bet x5
```

### Two new script steps

`egm settle` brings the machine to a known state: every door shut, the note acceptor
reconnected. It is a step a test asks for rather than something that happens to it, because
a machine detects a door by the *change* in its mask — so a door it believes is open at
power-up, and a cold start believes several are, can only be shut by opening and shutting it
again. That puts real door exceptions on the wire, and a test that had not asked for them
would be right to be surprised.

`host forget exceptions` waits for the machine to stop reporting, then leaves everything so
far out of the assertions that follow. The waiting is the point: a machine reports one
exception per poll, so when settling ends most of what it caused is still queued inside the
machine. The wire log keeps all of them — what changes is only what an assertion may match.

A negated expectation that catches an earlier step's exception now says so, and says what to
write instead, rather than reporting only that the exception arrived.

### Operator commands

```
sasctl qpm status | settle | audit open|close | door <name> open|close
sasctl qpm press <button> | hold <button> on|off | bill <cents>
sasctl nvram status | save | restore
```

`qpm audit open` does three things at once because the menu needs all three: the machine
refuses to configure anything unless its main **and** logic doors are open, and it says so on
screen rather than anywhere a person would look first.

`nvram save` and `restore` both refuse while a machine is running: a copy taken under one is
a configuration nobody ever had, and a copy written under one is overwritten within the
second. Eight megabytes of machine memory compresses to about ten kilobytes, so a saved
configuration is small enough to keep beside the tests that depend on it.

---

## Two preconditions a note has, and how they were found

Neither is in this repository, and a machine missing either drops the note internally with
nothing on the wire to say so — which is the least debuggable failure there is.

**A note acceptor configured in the machine's own memory.** Which acceptor a machine has, and
which notes it takes, live in NVRAM and are set through the machine's menu by hand. That was
done once and saved to `fixtures/nvram/golden.nvram.gz`; `sasctl nvram restore` puts it back.

**On a developer-licensed machine, the audit menu opened and closed once per boot.** This is
not a property of the protocol or of a production machine: a developer licence is what a
Debug build is obliged to install, and it raises a condition that inhibits the note acceptor
until the menu has been entered and left once. A machine running a production licence needs
none of this. It is recorded because every machine this project drives on a laptop is a Debug
build, so in practice it is always needed here.

Finding it also corrected a wrong theory. The reasoning said a host must enable denominations
with long poll `$08`, because that is what writes the machine's network side of the
enabled-notes mask. Measured against the machine, that was wrong:

| | audit menu cycled | `$08` sent | note accepted |
|---|---|---|---|
| | yes | no | **yes** |
| | no | yes | no |

So clearing the licence condition is what matters, and `$08` is neither necessary nor
sufficient here. The long poll is still in the catalogue, because it is a real command worth
being able to send and decode — it is simply not the reason a note is refused.

## One more thing the machine corrected

A twenty-dollar note moves `total_bill_in` by twenty, not by two thousand. The software
machine had been counting one unit per cent, so it read a hundred times high and happily
passed a test that the real machine failed. This is the disagreement-between-peers that `D7`
exists to surface, and it only surfaced because the same script was run against both.

**Corrected on 2026-09-05, after the machine was measured again.** The reason given here was
wrong even though the number was right: that meter is the bill-value meter, which the
specification fixes in whole currency units, so twenty is twenty dollars and has nothing to do
with what a credit is worth. The credit meters beside it *are* in the machine's accounting
denomination — measured at four hundred for the same note, on a machine accounting in five
cents — and the fixture was reporting twenty for them too.

---

## What is deliberately not done

**Playing games is refused, with a reason.** A game needs credit, a bet and no inhibiting
condition, and the machine decides its own outcomes from a free-running generator — so a step
asking for two games and a win of fifty cannot be honoured by pressing anything. The host's
own auto-rebet long poll is the lever that makes a machine play by itself, and it belongs on
the host side.

**One intermittent failure, not introduced here and not chased here.** Running every test
project at once, `Every_meter_the_index_offers_can_actually_be_read` has failed twice in
perhaps a dozen runs with a framing desync against the software machine (*"The reply is
addressed to $00, not to 01"*). It passes on its own and on repeated runs, and only ever
fails when four test projects are running at once. It is in the loopback fixture or the poll
loop's timing, not in anything this milestone touched, and it is worth finding before it is
blamed on something else.

---

## Constraints held

* `C:\Progetti\QSP` — not modified.
* `C:\Progetti\QSP_DemoOpenGL` — not modified in this milestone; it still carries only the
  gitignored `CMakeUserPresets.json` from M0.
* `C:\Progetti\qpm_simulator` — **not modified**. Unattended runs replace its window rather
  than changing it; the driver refuses to start if it is open.
* `libsas/utils/sashost`, `utils/poller`, `utils/hwconformance` — not read.
