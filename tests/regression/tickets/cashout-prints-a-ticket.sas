# The first test whose subject is a thing the machine produced rather than a thing it said.
#
# A note goes in, the collect button comes down, and the machine's printer files a ticket in the
# wallet - the directory the machine writes, the peripheral simulator displays and this host reads.
# The assertions are on the ticket's fields, never on how it looks, because a rendered ticket is
# for a person and a ticket's fields are for a test.
#
# What the ticket carries is bounded by what the printer was told. A pool, a validation system, an
# asset number and a location are never handed to a printer, so they are not asserted here: the
# host knows those from what it sent and can check them against its own record instead.

test "a cashout prints a ticket into the wallet" {
  tag @needs-stimulus
  requires stimulus.bill
  requires stimulus.button.collect
  requires printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm settle
  host forget exceptions

  egm bill insert 2000
  host expect meter total_bill_in += 20 within 5s

  egm press collect
  egm expect ticket printed within 20s

  # At least the note just inserted, rather than exactly it. A cashout collects the whole credit
  # meter, so a machine that already had money on it prints more - measured at 6500 the first time
  # this ran after the other suites had each left a note behind. Equality would only hold on an
  # empty machine, which would make this test depend on what ran before it.
  egm expect ticket amount_cents >= 2000
  egm expect ticket validation_number != 0
  egm expect ticket validation_type == cashable_from_cashout

  # The machine emptied itself into the ticket, which is the half of a cashout the wire can see.
  host expect meter credits == 0 within 5s
}
