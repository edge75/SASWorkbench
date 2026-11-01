# The other half of the money path, and the first test whose subject is a conversation rather
# than a single answer. A ticket goes into the acceptor; the machine holds it and says so; the
# host reads what is held, decides, and only then does the money move.
#
# It cannot be tested at one end. The machine never learns what a ticket is worth - it has read a
# barcode, not a value - and the host never learns a ticket went in until the machine says so, so
# a pass here is the two of them agreeing about something neither could have answered alone.
#
# Credits are asserted rather than the ticket-in meters, and not by preference: a meter is
# readable by name only where a long poll this build decodes carries it as a field, and the
# ticket-in meters are only reachable through the selected-meter polls, which name meters by code.

test "a ticket is redeemed and the credits arrive" {
  tag @needs-stimulus
  requires stimulus.ticket
  requires stimulus.settle
  requires decoder.lp4C
  requires decoder.lp70
  requires decoder.lp71

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced

  egm settle
  host forget exceptions

  # Written inline rather than read from a file: this number was never printed by anything, and
  # the test that needs a ticket a machine really produced is the print-then-redeem loop.
  egm ticket insert amount=2000 validation=1234567890123456
  host expect exception ticket_inserted within 5s

  # What the machine says it is holding. The amount is zero on purpose, and the specification says
  # so: the machine read a barcode.
  host send lp 70
  host expect field ticket_status of lp 70 == ticket_status_ticket_in_escrow
  host expect field validation_data of lp 70 == 1234567890123456
  host expect field transfer_amount of lp 70 == 0

  # The host's verdict, and the amount is the host's to declare.
  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=2000 parsing_code=0 validation_data=1234567890123456
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 5s

  # Reading the outcome is what closes the cycle. Until the host has read it the machine is still
  # holding the transaction open, which is why this is a step and not an assumption.
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed
  host expect field transfer_amount of lp 71 == 2000

  # Twenty dollars at a five-cent accounting denomination.
  host expect meter credits += 400 within 5s
}

# A ticket the host refuses. The machine must not take the money, and must say which of the two
# things happened - refused, rather than accepted and then reversed - because a meter that moved
# and moved back is indistinguishable on the wire from one that never moved.
test "a ticket the host will not validate leaves the machine untouched" {
  tag @needs-stimulus
  requires stimulus.ticket
  requires stimulus.settle
  requires decoder.lp4C
  requires decoder.lp70
  requires decoder.lp71

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced

  egm settle
  host forget exceptions

  egm ticket insert amount=500 validation=9999999999999999
  host expect exception ticket_inserted within 5s

  host send lp 70
  host expect field ticket_status of lp 70 == ticket_status_ticket_in_escrow

  host send lp 71 with transfer_code=ttc_ticket_already_redeemed transfer_amount=500 parsing_code=0 validation_data=9999999999999999
  host expect field machine_status of lp 71 == gmsc_ticket_rejected_by_host_or_unknown

  # No transfer happened, so there is nothing to announce and nothing to count.
  host expect no exception ticket_transfer_complete within 2s
  host expect meter credits unchanged
}

# The number the host redeems has to be the number the machine is holding. A host that quotes
# another ticket's number has a mismatched pair of records somewhere, and the machine's job is to
# refuse rather than to pay whichever amount arrived.
test "a redemption for another ticket's number is refused" {
  tag @needs-stimulus
  requires stimulus.ticket
  requires stimulus.settle
  requires decoder.lp4C
  requires decoder.lp70
  requires decoder.lp71

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced

  egm settle
  host forget exceptions

  egm ticket insert amount=1000 validation=1111111111111111
  host expect exception ticket_inserted within 5s

  host send lp 70
  host expect field validation_data of lp 70 == 1111111111111111

  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=1000 parsing_code=0 validation_data=2222222222222222
  host expect field machine_status of lp 71 == gmsc_validation_number_does_not_match

  host expect no exception ticket_transfer_complete within 2s
  host expect meter credits unchanged
}

# The number the host is told is the whole barcode, not the ticket's validation number on its own.
# A validator reads the strip and hands over what it read; the machine cannot know which two digits
# were the validation system, so all eighteen go on the wire. This is the case that would otherwise
# pass against the software machine and fail against a real one.
test "the number the host is told is the whole barcode" {
  tag @needs-stimulus
  requires stimulus.ticket
  requires stimulus.settle
  requires decoder.lp4C
  requires decoder.lp70
  requires decoder.lp71

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced

  egm settle
  host forget exceptions

  egm ticket insert amount=2000 validation=9876543210987654 system=64
  host expect exception ticket_inserted within 5s

  host send lp 70
  host expect field validation_data of lp 70 == 649876543210987654

  # Refused, so the escrow is empty for the case after this one.
  host send lp 71 with transfer_code=ttc_ticket_already_redeemed transfer_amount=2000 parsing_code=0 validation_data=649876543210987654
  host expect field machine_status of lp 71 == gmsc_ticket_rejected_by_host_or_unknown
}
