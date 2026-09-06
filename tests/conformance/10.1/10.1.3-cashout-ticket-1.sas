# The first conformance case that runs. A machine in the standard validation style prints a
# ticket, announces it, and then answers for it: long poll $3D reports the number and the value
# of the ticket most recently printed, and both must be the ones on the paper.
#
# What makes this worth having is the cross-check. The machine numbers its own tickets, so the
# number is unknowable to whoever wrote this file - an assertion against a literal could say the
# reply is plausible, never that the host was told about this ticket. Comparing the reply against
# the ticket the printer filed is the only form of the question that means anything, and it needs
# both ends: the wallet the machine printed into and the wire the host read.
#
# Which machine it runs against is decided by the machine rather than by this file. The validation
# style is a property of a machine's memory, and the machine is asked for it over the link before
# any step runs, so a machine in another style skips this case saying which style it is in.
#
# Not covered here, and each for a reason rather than an oversight:
#
#   * the priority order in which the exceptions come back. The specification makes a printed
#     ticket jump ahead of the door exceptions raised before it; the software machine reports in
#     the order things happened and has no ordering assertion to be measured by, so claiming it
#     would be claiming something nothing checked.
#   * the change-light exceptions, which need a service button no machine here has.
#   * the reissue of the announcement while a record is still unread, which belongs to the
#     enhanced styles - standard validation keeps no buffer for a record to be unread in.
#   * cashout and jackpot receipts, which need a machine configured to print them.

test "10.1.3 Cashout Ticket 1" {
  tag @needs-stimulus

  requires egm.validation == standard
  requires decoder.lp3D
  requires stimulus.bill
  requires stimulus.button.collect
  requires stimulus.door.slot
  requires printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  egm settle
  host forget exceptions

  # A door either side of the cashout, so what follows is read out of a queue with other things
  # already in it rather than out of an empty one.
  egm door slot open
  host expect exception slot_door_opened within 5s
  egm door slot close
  host expect exception slot_door_closed within 5s

  egm bill insert 2000
  host expect meter total_bill_in += 20 within 5s

  egm press collect
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s

  host send lp 3D
  host expect field validation_number of lp 3D != 0
  host expect field validation_number of lp 3D == ticket.validation_number
  host expect field ticket_amount of lp 3D == ticket.amount_cents

  # A second ticket, for a different amount, because the reply carries the most recent one and a
  # machine that answered with the first would pass everything above.
  egm bill insert 1000
  host expect meter total_bill_in += 10 within 5s

  egm press collect
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s

  host send lp 3D
  host expect field validation_number of lp 3D == ticket.validation_number
  host expect field ticket_amount of lp 3D == ticket.amount_cents
}
