# M3 — Tickets, the money path in both directions

**Status: the ticket path is built at both ends.** A machine prints a ticket and it is filed
where a test and a person can both read it; a ticket goes back into a machine and a host
redeems it. The milestone is not finished — GSA **10.1.3 Cashout Ticket 1** needs the cashout
ticket information long poll, and a real machine cannot complete a redemption until its
validation style is configured — but nothing in the loop is missing a mechanism any more.

---

## What runs today

```
sasctl run tests/regression/tickets --stimulus software

  PASS  a cashout prints a ticket into the wallet
  PASS  a ticket is redeemed and the credits arrive
  PASS  a ticket the host will not validate leaves the machine untouched
  PASS  a redemption for another ticket's number is refused
  PASS  the number the host is told is the whole barcode
```

The second is the one that could not be written before. A redemption cannot be tested at one
end: the machine never learns what a ticket is worth — it has read a barcode — and the host
never learns a ticket went in until the machine says so. A pass is the two of them agreeing
about something neither could have answered alone, which is exactly what `D7` asked for.

## The two directions

### Out — the wallet

A directory holding one ticket per file. The machine's printer writes into it, the peripheral
simulator shows what is in it, and the test host reads it to assert on what was printed.

### In — the acceptor

A second directory, and a queue rather than a store: what is taken is gone. A ticket dropped
in is a ticket going into the machine's note acceptor. Three things write to it and they all
write the same file — a script step, a button in the simulator that inserts a ticket from the
wallet, and a button that inserts any ticket file by path. `D35` asked for that: the by-hand
path and the scripted path are one path, or the thing a person demonstrates stops being the
thing the suite tests.

A ticket is written under a temporary name and renamed into place, because the machine is
watching the directory and would otherwise be able to read one whose bytes are still arriving.

## What a script says

```
egm ticket insert amount=2000 validation=1234567890123456
host expect exception ticket_inserted within 5s

host send lp 70
host expect field ticket_status of lp 70 == ticket_status_ticket_in_escrow
host expect field validation_data of lp 70 == 1234567890123456

host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=2000 parsing_code=0 validation_data=1234567890123456
host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
host expect exception ticket_transfer_complete within 5s

host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed
```

Two additions to the language make that readable.

**A command's fields can be named.** `with <field>=<value>` builds the command from the layout
instead of from a hexadecimal string, and does it while the script is read — so a field this
build cannot write is a mistake in the script, caught before a machine is touched. A command
whose layout has not been established refuses to have its fields named and says to write the
bytes instead, which keeps the honest distinction between a command we can describe and one we
can only send.

**A code can be written by name.** `ttc_valid_cashable_ticket`, `gmsc_ticket_redemption_pending`
and the rest are the gaming machine library's own names, generated from its header rather than
invented here, so a script can be checked against the header in one step. The same names work
on both sides of the exchange, in a `with` clause and in an assertion.

A number in either place is decimal and a hexadecimal code is written `$40`. That is deliberate:
`machine_status == 40` reads as decimal forty and is not the `$40` anybody meant, and the
ambiguity is refused rather than guessed at.

## Three things worth recording

### The number the host is told is the whole barcode

A validator reads the strip and hands over what it read. The machine has no way of knowing
which of those digits were the validation system identifier, so all eighteen go to the host —
the two-digit identifier and then the validation number in sixteen. The two numbers are the
same only while the identifier is zero, which is what every machine in scope prints today.

This was found by reading the platform: it composes the printed barcode as
`%02u%016llu` and turns an inserted one back into a number with `atoll` of all eighteen
characters. Modelling it as the validation number alone would have passed against the software
machine and failed against a real one, on the one case — a ticket carrying a validation system
— that a negative test is most likely to use. Both implementations now compose it the same way
and each pins the other's answer in a test.

### The reply to the command that accepts a ticket is "pending", not "redeemed"

The machine has agreed to take the ticket and has not taken it yet. A host told otherwise would
have no reason ever to ask again, and would never learn whether the money actually moved. The
cycle closes only when the host reads the outcome: until then the acceptor stays busy, because
the machine cannot know its answer arrived and a second ticket must not go in on the assumption
that it did.

### The simulated note acceptor could never report a ticket at all

The peripheral manager's simulated bill validator has a flag for a ticket in escrow that is
only ever cleared, and answers every request for the waiting document's barcode with the same
constant. Nothing could start a redemption on a real machine. That entry point is now
substituted the same way the printer is — the peripheral manager's own source is included
rather than copied, so it remains the only place that file is maintained.

## Proven against a real machine

The demo boots, the substituted acceptor picks a ticket out of the directory, reads its barcode
and reports a document in escrow, and the machine decides what to do with it:

```
[SasWorkbench] watching C:/ProgramData/qpm_simulator/acceptor for a ticket to insert
[SasWorkbench] ticket 20260906-120500-000-0001.json in escrow: 001234567890123456, 2000 cents
[SasWorkbench] the ticket in escrow was returned
```

Returned, not redeemed, and that is the expected answer today: a machine on fresh memory has no
enhanced validation style configured, and redemption exists only for the enhanced styles. What
this run proves is the mechanism — the event reaches the machine, the barcode it reads is the
one on the ticket, and the machine's decision comes back. The machine's own reason for
returning it is not visible in this capture, so it is not claimed here.

## What is left in the milestone

* **A golden memory image with an enhanced validation style**, without which a real machine
  returns every ticket. `BLOCKERS.md §4` has always named this as a prerequisite for every
  ticket test.
* **The cashout ticket information long poll**, which GSA 10.1.3 is mostly about.
* **The extended log** (`D34`) and **the accounting denomination in a script** (`D36`).
* **The fourth timestamp mode, wall-clock time** (`D28`), outstanding from M1.5.

## Known limits, stated rather than discovered later

* A restricted redemption that leaves out the expiration and pool identifier is legal and the
  gaming machine library accepts it, but this build's layout expects them whenever the transfer
  code says restricted. Sending one without them means writing the bytes by hand.
* The validation data is nine bytes because the parsing code is zero. A non-zero parsing code
  makes it as long as the length byte allows, and that is not modelled.
* The ticket-in meters cannot be asserted on by name. A meter is readable by name only where a
  long poll this build decodes carries it as a field, and those meters are reachable only
  through the selected-meter polls, which name meters by code.
* The software machine holds one ticket at a time. Two tickets in flight, the resend rule that
  makes an identical command a status request, and the buffer of past redemptions are all
  things the real library does and this fixture does not.
