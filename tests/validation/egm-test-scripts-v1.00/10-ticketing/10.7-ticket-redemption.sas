# Chapter 10, section 10.7 - taking a ticket in.
#
# The other direction, and the one with money at stake in the opposite sense: the machine reads a
# ticket, asks the host whether it is good for anything, and credits the player only if the host
# says so. A machine that credited before the answer, or twice for one ticket, would be giving
# money away.
#
# The exchange is three polls. The machine raises the ticket-inserted exception; the host reads the
# ticket data and decides; the host sends the redemption poll with an amount and a status, and the
# machine answers it and then reports the transfer complete. The whole exchange is written end to
# end against this same machine in tests/regression/tickets/, so what these cases add is the
# document's own decomposition of it rather than a second copy.

test "10.7.1 redemption is enabled through the status poll" {
  tag @egm-test-scripts @chapter-10
  requires decoder.lp7B, machine.redemption_switch
  note "Set the redemption bit and read the status back, then offer a ticket with the bit clear and with it set: refused in one case and taken in the other. The software machine takes its redemption setting from the validation style rather than from a switch, so the two halves cannot be told apart here."
}

test "10.7.2 a ticket offered to the machine raises its exception" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires stimulus.ticket
  requires egm.validation == secure_enhanced

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  egm ticket insert amount=1000 validation=0000000000000001
  host expect exception ticket_inserted within 8s
}

test "10.7.3 the host reads the ticket the machine is holding" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires stimulus.ticket, decoder.lp70
  requires egm.validation == secure_enhanced

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  egm ticket insert amount=1000 validation=0000000000000002
  host expect exception ticket_inserted within 8s

  # What the machine read off the paper, handed back so the host can decide about it.
  host send lp 70
}

test "10.7.4 the machine answers the redemption poll" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires stimulus.ticket, decoder.lp71, host.validation_sequence
  note "Send the redemption poll carrying the amount and a status saying the ticket is good, and the machine answers with its own status and the amount it will credit."
}

test "10.7.5 the machine status codes cover every refusal" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires stimulus.ticket, decoder.lp71, machine.ticket_states
  note "Work through the status codes the machine can answer the redemption poll with - rejected, no ticket in escrow, wrong amount, expired - by putting the machine into each state. Several need a ticket refused for a reason only a real acceptor produces."
}

test "10.7.6 a restricted ticket carries its own expiry in the redemption poll" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires machine.restricted_credits, decoder.lp71
  note "Redeem a restricted ticket whose expiry is a date rather than a number of days, and check the machine takes the date from the poll rather than from its own configuration."
}

test "10.7.7 restricted tickets credit the restricted meter" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires machine.restricted_credits
  note "Redeem a restricted ticket and confirm the credits land as restricted rather than cashable, which is what stops them being cashed out again as money."
}

test "10.7.8 the transfer complete exception follows the credit" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires stimulus.ticket
  requires egm.validation == secure_enhanced

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  egm ticket insert amount=1000 validation=0000000000000003
  host expect exception ticket_inserted within 8s

  note "The transfer-complete half needs the host to answer the redemption poll and then watch for the exception, which is written end to end in tests/regression/tickets/. What is asserted here is the half this case owns: the machine asks before it credits."
}

# The meters a redeemed ticket moves. None of them can be read by name in this build, and a meter
# this host cannot name is a skip: reading the wrong meter and finding it moved would be worse
# than not reading it at all.

test "10.7.9 the total ticket in meter counts every kind of ticket" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires meter.total_ticket_in, machine.restricted_credits
  note "Redeem a cashable and a restricted ticket and check both move the total."
}

test "10.7.10 the cashable ticket in meter includes non-restricted promotional tickets" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires meter.total_cashable_ticket_in
  note "Meter 002B and the cashable total: a non-restricted promotional ticket counts in both."
}

test "10.7.11 the cashable-only meter excludes promotional tickets" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires meter.cashable_only_ticket_in
  note "The counterpart of 10.7.10: the same promotional ticket must not appear here. The pair is what proves the machine tells the two kinds apart."
}

test "10.7.12 restricted promotional tickets credit meter 002A" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires meter.total_restricted_ticket_in, machine.restricted_credits
  note "Redeem a restricted promotional ticket and read meter 002A before and after."
}

test "10.7.13 non-restricted promotional tickets credit meter 002B" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires meter.total_nonrestricted_promotional_ticket_in
  note "The same for meter 002B, with a non-restricted promotional ticket."
}

test "10.7.14 cashable tickets in are counted in cents and quantity" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires meter.cashable_ticket_in_cents, meter.cashable_ticket_in_quantity
  note "Meters 0080 and 0081, moved together by a redeemed cashable ticket - the cents by its value and the quantity by one."
}

test "10.7.15 restricted tickets in are counted in cents and quantity" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires meter.restricted_ticket_in_cents, meter.restricted_ticket_in_quantity
  requires machine.restricted_credits
  note "Meters 0082 and 0083, the same pair for restricted tickets."
  note "The extraction of this document repeats this section number three times where the source runs 10.7.15 to 10.7.17, with the same title and the same criteria each time. They are written here as one; if the source holds three distinct cases, this is the place that owes two more."
}

test "10.7.18 a ticket inserted is reported in the event format" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires stimulus.ticket, stimulus.real_time_events
  requires egm.validation == secure_enhanced

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 0E data 01
  host forget exceptions within 2s

  egm ticket insert amount=1000 validation=0000000000000004
  host expect exception ticket_inserted within 8s
}

test "10.7.19 a transfer complete is reported in the event format" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires stimulus.ticket, stimulus.real_time_events, host.validation_sequence
  note "The same as 10.7.18 for the transfer-complete exception, which needs the host to complete the redemption exchange first."
}
