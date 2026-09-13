# The first case in this suite that needs two hosts, and the reason channels have been carried
# through the model, the script syntax and the reports since the first commit.
#
# The test document's own words: if the machine supports more than one SAS channel and standard
# validation can be put on a different one, enable at least two channels with validation on one of
# them; if it is single-channel only, skip this test. So a machine with one channel skips - the
# arrangement is declared, the runner asks the machine how many channels it opens, and a machine
# that opens one is an environmental miss rather than a failure.
#
# What the document asks for, step by step:
#
#   3. print a ticket while the host is on the validating channel
#   4. move the host to the channel that does not own validation - the machine sends exception 3D
#      there once as well
#   5. ask that host for the ticket - the machine should ignore the poll, or answer it with a zero
#      validation number and a zero amount
#   6. move back to the validating channel and ask again - the real number and the real amount
#
# Steps 4 and 5 come out of two different mechanisms in the machine library and neither is a
# judgement of ours. Which host hears an exception is decided by five predicates naming the codes
# in each category; 3D is in none of them, so it reaches every open channel - which is exactly what
# step 4 requires. Which host may ask a long poll is decided by a gate at the top of each handler;
# thirty-five commands carry one and the cashout ticket information poll does not - which is why
# step 5 is written as a failing expectation below rather than as a passing one.
#
# It needs money, so it runs against the software machine and not against QSP_DemoOpenGL: that
# machine has no licence and returns every note before its money handler sees one (BLOCKERS.md
# Trap D). Nothing here can clear that and no long poll can.

test "10.1.4 Ticket Printed - Non-Validating Host" {
  tag @needs-stimulus
  tag @channel2

  # Labelled as a finding because it is one: the machine library answers the cashout ticket
  # information poll on any channel, and step 5 below therefore does not hold against it. The
  # label is what lets a suite gate stay green while this stays red, and the gate also checks that
  # it is *still* red - so the day the library gates that poll, the check fails and somebody comes
  # here and takes the label off.
  tag @finding

  requires egm.validation == standard
  requires decoder.lp3D
  requires stimulus.bill
  requires stimulus.button.collect
  requires printer.wallet

  # Two channels, with validation on the second. Every other category stays on channel 1, which is
  # what leaving it unnamed means and what the machine's own menu does with it.
  machine channel 1 address 01
  machine channel 2 address 02 owns validation

  # The validating host first, because the ticket is printed while it is the one listening.
  host connect loopback address 02 channel 2
  host poll every 200ms
  host expect link up within 5s

  # And the host that owns everything else, on the machine's first channel.
  host connect loopback address 01 channel 1
  host expect link up within 5s

  host channel 2
  egm settle
  host forget exceptions

  host channel 1
  host forget exceptions

  # Step 3. Money in, cashout pressed, ticket printed, and the validating host is told.
  host channel 2
  egm bill insert 2000
  host expect meter total_bill_in += 20 within 5s

  egm press collect
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s

  host send lp 3D
  host expect field validation_number of lp 3D != 0
  host expect field validation_number of lp 3D == ticket.validation_number
  host expect field ticket_amount of lp 3D == ticket.amount_cents

  # Step 4. The other host is told about the ticket too, once. It is told because the code is in
  # no category, so the machine's own filter lets it through to every open channel.
  host channel 1
  host expect exception cashout_ticket_printed within 5s
  host expect no exception cashout_ticket_printed within 2s

  # Step 5. And it must not be able to read what was on the paper. The machine library answers this
  # poll on any channel, so this expectation does not hold against it - which is the finding this
  # case exists to produce, and it is left as a failure rather than softened into a note.
  host send lp 3D
  host expect field validation_number of lp 3D == 0
  host expect field ticket_amount of lp 3D == 0

  # Step 6. Back on the validating host, the ticket is still readable and still the right one.
  host channel 2
  host send lp 3D
  host expect field validation_number of lp 3D == ticket.validation_number
  host expect field ticket_amount of lp 3D == ticket.amount_cents
}
