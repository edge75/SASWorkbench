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
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation secure_enhanced
  requires stimulus.ticket, decoder.lp7B, decoder.lp4C, decoder.lp70, decoder.lp71

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1
  egm settle
  host forget exceptions within 2s

  # A mask of nothing changes nothing, whatever states come with it. $8021 is redemption ($0020)
  # on a machine that prints ($0001) and whose identifier a host has set ($8000).
  host send lp 7B data 080000000000000000
  host expect field current_validation_status of lp 7B == 32801
  host send lp 7B data 080000FFFF00000000
  host expect field current_validation_status of lp 7B == 32801

  # Redemption off: the ticket is refused before the host is ever asked.
  host send lp 7B data 082000000000000000
  host expect field current_validation_status of lp 7B == 32769
  egm ticket insert amount=1000 validation=0000000000000011
  host expect no exception ticket_inserted within 8s

  # Redemption on: a different ticket is taken and the host is asked about it, and about it alone.
  host send lp 7B data 082000200000000000
  host expect field current_validation_status of lp 7B == 32801
  egm ticket insert amount=1000 validation=0000000000000012
  host expect exception ticket_inserted within 8s
  host send lp 70
  host expect field validation_data of lp 70 == 0000000000000012

  # Refused, so the escrow is empty for the case after this one.
  host send lp 71 with transfer_code=ttc_ticket_already_redeemed transfer_amount=1000 parsing_code=0 validation_data=0000000000000012
  host expect field machine_status of lp 71 == gmsc_ticket_rejected_by_host_or_unknown

  note "The document also disables redemption for another reason, such as a failed bill validator, and expects the bit to read clear. That needs the validator fault the third batch would add."
}

test "10.7.2 a ticket offered to the machine raises its exception" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires stimulus.ticket, decoder.lp4C, decoder.lp70, decoder.lp71
  machine validation secure_enhanced

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1
  host forget exceptions within 2s

  egm ticket insert amount=1000 validation=0000000000000001
  host expect exception ticket_inserted within 8s

  # Refused, so the escrow is empty for the case after this one.
  host send lp 70
  host send lp 71 with transfer_code=ttc_ticket_already_redeemed transfer_amount=1000 parsing_code=0 validation_data=0000000000000001
  host expect field machine_status of lp 71 == gmsc_ticket_rejected_by_host_or_unknown
}

test "10.7.3 the host reads the ticket the machine is holding" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires stimulus.ticket, decoder.lp4C, decoder.lp70, decoder.lp71
  machine validation secure_enhanced

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1
  host forget exceptions within 2s

  egm ticket insert amount=1000 validation=0000000000000002
  host expect exception ticket_inserted within 8s

  # What the machine read off the paper, handed back so the host can decide about it.
  host send lp 70

  # Refused, so the escrow is empty for the case after this one.
  host send lp 71 with transfer_code=ttc_ticket_already_redeemed transfer_amount=1000 parsing_code=0 validation_data=0000000000000002
  host expect field machine_status of lp 71 == gmsc_ticket_rejected_by_host_or_unknown
}

test "10.7.4 the machine answers the redemption poll" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires stimulus.ticket, decoder.lp4C, decoder.lp70, decoder.lp71
  machine validation secure_enhanced

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1
  egm settle
  host forget exceptions within 2s

  egm ticket insert amount=1000 validation=0000000000000013
  host expect exception ticket_inserted within 8s
  host send lp 70

  # Good for ten dollars: the machine answers with what it is doing about it.
  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=1000 parsing_code=0 validation_data=0000000000000013
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect field transfer_amount of lp 71 == 1000
  host expect field parsing_code of lp 71 == 0
  host expect field validation_data of lp 71 == 0000000000000013
  host expect exception ticket_transfer_complete within 8s

  # Once stacked, the status the host asks for is the outcome.
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed
  host expect field transfer_amount of lp 71 == 1000

  note "The document repeats this for a restricted and a non-restricted promotional ticket, which are the third batch's subject."
}

test "10.7.5 the machine status codes cover every refusal" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires stimulus.ticket, decoder.lp71, machine.ticket_states
  note "Work through the status codes the machine can answer the redemption poll with - rejected, no ticket in escrow, wrong amount, expired - by putting the machine into each state. Several need a ticket refused for a reason only a real acceptor produces."
}

test "10.7.6 a restricted ticket carries its own expiry in the redemption poll" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires machine.restricted_credits, decoder.lp71
  # The platform's restricted ticket option is off on the software machine, as it is by default (QSP SasNetwork.cpp:617).
  requires machine.restricted_tickets
  note "Redeem a restricted ticket whose expiry is a date rather than a number of days, and check the machine takes the date from the poll rather than from its own configuration."
}

test "10.7.7 restricted tickets credit the restricted meter" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires machine.restricted_credits
  # The platform's restricted ticket option is off on the software machine, as it is by default (QSP SasNetwork.cpp:617).
  requires machine.restricted_tickets
  note "Redeem a restricted ticket and confirm the credits land as restricted rather than cashable, which is what stops them being cashed out again as money."
}

test "10.7.8 the transfer complete exception follows the credit" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires stimulus.ticket, decoder.lp4C, decoder.lp70, decoder.lp71
  machine validation secure_enhanced

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1
  host forget exceptions within 2s

  egm ticket insert amount=1000 validation=0000000000000003
  host expect exception ticket_inserted within 8s

  # Refused, so the escrow is empty for the case after this one.
  host send lp 70
  host send lp 71 with transfer_code=ttc_ticket_already_redeemed transfer_amount=1000 parsing_code=0 validation_data=0000000000000003
  host expect field machine_status of lp 71 == gmsc_ticket_rejected_by_host_or_unknown

  note "The transfer-complete half needs the host to answer the redemption poll and then watch for the exception, which is written end to end in tests/regression/tickets/. What is asserted here is the half this case owns: the machine asks before it credits."
}

# The meters a redeemed ticket moves, each read by code with $6F and $AF and checked against the
# ticket's cents, its count, or the meter of its own kind beside it.

test "10.7.9 the total ticket in meter counts every kind of ticket" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus @needs-a-person
  requires stimulus.ticket, stimulus.settle
  requires decoder.lp4C, decoder.lp70, decoder.lp71, decoder.lp6F, decoder.lpAF
  requires host.compare_replies, host.meter_arithmetic
  requires machine.restricted_credits

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 080000150029002A00
  host expect field meters[0].code == $15
  host remember field meters[0].value as m15_before
  host expect field meters[1].code == $29
  host remember field meters[1].value as m29_before
  host expect field meters[2].code == $2A
  host remember field meters[2].value as m2A_before
  host send lp AF data 080000150029002A00
  host expect field meters[0].value == saved.m15_before
  host expect field meters[1].value == saved.m29_before
  host expect field meters[2].value == saved.m2A_before

  egm ticket insert amount=1000 validation=0000000000107090
  host expect exception ticket_inserted within 8s
  host send lp 70
  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=1000 parsing_code=0 validation_data=0000000000107090
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 8s
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed
  host forget exceptions within 2s
  egm ticket insert amount=1000 validation=0000000000107091 type=restricted_promotional_from_cashout
  host expect exception ticket_inserted within 8s
  host send lp 70
  host send lp 71 with transfer_code=ttc_valid_restricted_promotional_ticket transfer_amount=1000 parsing_code=0 validation_data=0000000000107091 restricted_expiration_month=12 restricted_expiration_day=31 restricted_expiration_year=2030 pool_id=0
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 8s
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_restricted_promotional_ticket_redeemed
  host forget exceptions within 2s

  host send lp 6F data 06000029002A00
  host expect field meters[0].code == $29
  host remember field meters[0].value as m29_after
  host expect field meters[1].code == $2A
  host remember field meters[1].value as m2A_after
  host expect field meters[0].value > saved.m29_before
  host expect field meters[1].value > saved.m2A_before
  host send lp 6F data 0400001500
  host expect field meters[0].value == saved.m15_before + saved.m29_after - saved.m29_before + saved.m2A_after - saved.m2A_before
  host send lp AF data 0400001500
  host expect field meters[0].value == saved.m15_before + saved.m29_after - saved.m29_before + saved.m2A_after - saved.m2A_before

  note "Meter 0015, in credits: a cashable and a restricted ticket each move it, by what each moved its own meter (0029 and 002A)."
}

test "10.7.10 the cashable ticket in meter includes non-restricted promotional tickets" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus @needs-a-person
  requires stimulus.ticket, stimulus.settle
  requires decoder.lp4C, decoder.lp70, decoder.lp71, decoder.lp6F, decoder.lpAF
  requires host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 08000028002B002900
  host expect field meters[0].code == $28
  host remember field meters[0].value as m28_before
  host expect field meters[1].code == $2B
  host remember field meters[1].value as m2B_before
  host expect field meters[2].code == $29
  host remember field meters[2].value as m29_before
  host send lp AF data 08000028002B002900
  host expect field meters[0].value == saved.m28_before
  host expect field meters[1].value == saved.m2B_before
  host expect field meters[2].value == saved.m29_before

  egm ticket insert amount=1000 validation=0000000000107100
  host expect exception ticket_inserted within 8s
  host send lp 70
  host send lp 71 with transfer_code=ttc_valid_nonrestricted_promotional_ticket transfer_amount=1000 parsing_code=0 validation_data=0000000000107100
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 8s
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_nonrestricted_promotional_ticket_redeemed
  host forget exceptions within 2s

  host send lp 6F data 0400002B00
  host expect field meters[0].code == $2B
  host remember field meters[0].value as m2B_after
  host expect field meters[0].value > saved.m2B_before
  host send lp 6F data 06000028002900
  host expect field meters[0].value == saved.m28_before + saved.m2B_after - saved.m2B_before
  host expect field meters[1].value == saved.m29_before
  host send lp AF data 06000028002900
  host expect field meters[0].value == saved.m28_before + saved.m2B_after - saved.m2B_before
  host expect field meters[1].value == saved.m29_before

  note "Meter 0028, in credits: a non-restricted promotional ticket moves it by what it moves 002B, and leaves the regular cashable meter 0029 alone."
}

test "10.7.11 the cashable-only meter excludes promotional tickets" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus @needs-a-person
  requires stimulus.ticket, stimulus.settle
  requires decoder.lp4C, decoder.lp70, decoder.lp71, decoder.lp6F, decoder.lpAF
  requires host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 06000029002800
  host expect field meters[0].code == $29
  host remember field meters[0].value as m29_before
  host expect field meters[1].code == $28
  host remember field meters[1].value as m28_before
  host send lp AF data 06000029002800
  host expect field meters[0].value == saved.m29_before
  host expect field meters[1].value == saved.m28_before

  egm ticket insert amount=1000 validation=0000000000107110
  host expect exception ticket_inserted within 8s
  host send lp 70
  host send lp 71 with transfer_code=ttc_valid_nonrestricted_promotional_ticket transfer_amount=1000 parsing_code=0 validation_data=0000000000107110
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 8s
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_nonrestricted_promotional_ticket_redeemed
  host forget exceptions within 2s

  host send lp 6F data 0400002900
  host expect field meters[0].value == saved.m29_before
  host send lp AF data 0400002900
  host expect field meters[0].value == saved.m29_before
  host send lp 6F data 0400002800
  host expect field meters[0].code == $28
  host remember field meters[0].value as m28_promo
  host expect field meters[0].value > saved.m28_before

  egm ticket insert amount=1000 validation=0000000000107111
  host expect exception ticket_inserted within 8s
  host send lp 70
  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=1000 parsing_code=0 validation_data=0000000000107111
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 8s
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed
  host forget exceptions within 2s

  host send lp 6F data 0400002900
  host expect field meters[0].code == $29
  host remember field meters[0].value as m29_after
  host expect field meters[0].value > saved.m29_before
  host send lp 6F data 0400002800
  host expect field meters[0].value == saved.m28_promo + saved.m29_after - saved.m29_before
  host send lp AF data 0400002800
  host expect field meters[0].value == saved.m28_promo + saved.m29_after - saved.m29_before

  note "Meter 0029, in credits: the promotional ticket that moves 0028 leaves it alone, and a cashable ticket moves both by the same amount."
}

test "10.7.12 restricted promotional tickets credit meter 002A" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus @needs-a-person
  requires stimulus.ticket, stimulus.settle
  requires decoder.lp4C, decoder.lp70, decoder.lp71, decoder.lp6F, decoder.lpAF
  requires host.compare_replies, host.meter_arithmetic
  requires machine.restricted_credits

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0800002A0082008300
  host expect field meters[0].code == $2A
  host remember field meters[0].value as m2A_before
  host expect field meters[1].code == $82
  host remember field meters[1].value as m82_before
  host expect field meters[2].code == $83
  host remember field meters[2].value as m83_before
  host send lp AF data 0800002A0082008300
  host expect field meters[0].value == saved.m2A_before
  host expect field meters[1].value == saved.m82_before
  host expect field meters[2].value == saved.m83_before

  egm ticket insert amount=1000 validation=0000000000107120 type=restricted_promotional_from_cashout
  host expect exception ticket_inserted within 8s
  host send lp 70
  host send lp 71 with transfer_code=ttc_valid_restricted_promotional_ticket transfer_amount=1000 parsing_code=0 validation_data=0000000000107120 restricted_expiration_month=12 restricted_expiration_day=31 restricted_expiration_year=2030 pool_id=0
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 8s
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_restricted_promotional_ticket_redeemed
  host forget exceptions within 2s

  host send lp 6F data 0800002A0082008300
  host expect field meters[0].value > saved.m2A_before
  host expect field meters[1].value == saved.m82_before + 1000
  host expect field meters[2].value == saved.m83_before + 1
  host send lp AF data 0800002A0082008300
  host expect field meters[0].value > saved.m2A_before
  host expect field meters[1].value == saved.m82_before + 1000
  host expect field meters[2].value == saved.m83_before + 1

  note "Meter 002A grows in credits, beside the restricted ticket's cents and count (0082 and 0083)."
}

test "10.7.13 non-restricted promotional tickets credit meter 002B" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus @needs-a-person
  requires stimulus.ticket, stimulus.settle
  requires decoder.lp4C, decoder.lp70, decoder.lp71, decoder.lp6F, decoder.lpAF
  requires host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0800002B0084008500
  host expect field meters[0].code == $2B
  host remember field meters[0].value as m2B_before
  host expect field meters[1].code == $84
  host remember field meters[1].value as m84_before
  host expect field meters[2].code == $85
  host remember field meters[2].value as m85_before
  host send lp AF data 0800002B0084008500
  host expect field meters[0].value == saved.m2B_before
  host expect field meters[1].value == saved.m84_before
  host expect field meters[2].value == saved.m85_before

  egm ticket insert amount=1000 validation=0000000000107130
  host expect exception ticket_inserted within 8s
  host send lp 70
  host send lp 71 with transfer_code=ttc_valid_nonrestricted_promotional_ticket transfer_amount=1000 parsing_code=0 validation_data=0000000000107130
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 8s
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_nonrestricted_promotional_ticket_redeemed
  host forget exceptions within 2s

  host send lp 6F data 0800002B0084008500
  host expect field meters[0].value > saved.m2B_before
  host expect field meters[1].value == saved.m84_before + 1000
  host expect field meters[2].value == saved.m85_before + 1
  host send lp AF data 0800002B0084008500
  host expect field meters[0].value > saved.m2B_before
  host expect field meters[1].value == saved.m84_before + 1000
  host expect field meters[2].value == saved.m85_before + 1

  note "Meter 002B grows in credits, beside the non-restricted ticket's cents and count (0084 and 0085)."
}

test "10.7.14 cashable tickets in are counted in cents and quantity" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus @needs-a-person
  requires stimulus.ticket, stimulus.settle
  requires decoder.lp4C, decoder.lp70, decoder.lp71, decoder.lp6F, decoder.lpAF
  requires host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 06000080008100
  host expect field meters[0].code == $80
  host remember field meters[0].value as m80_before
  host expect field meters[1].code == $81
  host remember field meters[1].value as m81_before
  host send lp AF data 06000080008100
  host expect field meters[0].value == saved.m80_before
  host expect field meters[1].value == saved.m81_before

  egm ticket insert amount=1000 validation=0000000000107140
  host expect exception ticket_inserted within 8s
  host send lp 70
  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=1000 parsing_code=0 validation_data=0000000000107140
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 8s
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed
  host forget exceptions within 2s
  egm ticket insert amount=500 validation=0000000000107141
  host expect exception ticket_inserted within 8s
  host send lp 70
  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=500 parsing_code=0 validation_data=0000000000107141
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 8s
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed
  host forget exceptions within 2s

  host send lp 6F data 06000080008100
  host expect field meters[0].value == saved.m80_before + 1500
  host expect field meters[1].value == saved.m81_before + 2
  host send lp AF data 06000080008100
  host expect field meters[0].value == saved.m80_before + 1500
  host expect field meters[1].value == saved.m81_before + 2

  note "Meters 0080 and 0081, moved together by each redeemed cashable ticket: the cents by its value and the quantity by one. Two tickets of different values prove the pair are not the same number twice."
}

test "10.7.15 restricted tickets in are counted in cents and quantity" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus @needs-a-person
  requires stimulus.ticket, stimulus.settle
  requires decoder.lp4C, decoder.lp70, decoder.lp71, decoder.lp6F, decoder.lpAF
  requires host.compare_replies, host.meter_arithmetic
  requires machine.restricted_credits

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 06000082008300
  host expect field meters[0].code == $82
  host remember field meters[0].value as m82_before
  host expect field meters[1].code == $83
  host remember field meters[1].value as m83_before
  host send lp AF data 06000082008300
  host expect field meters[0].value == saved.m82_before
  host expect field meters[1].value == saved.m83_before

  egm ticket insert amount=1000 validation=0000000000107150 type=restricted_promotional_from_cashout
  host expect exception ticket_inserted within 8s
  host send lp 70
  host send lp 71 with transfer_code=ttc_valid_restricted_promotional_ticket transfer_amount=1000 parsing_code=0 validation_data=0000000000107150 restricted_expiration_month=12 restricted_expiration_day=31 restricted_expiration_year=2030 pool_id=0
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 8s
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_restricted_promotional_ticket_redeemed
  host forget exceptions within 2s

  host send lp 6F data 06000082008300
  host expect field meters[0].value == saved.m82_before + 1000
  host expect field meters[1].value == saved.m83_before + 1
  host send lp AF data 06000082008300
  host expect field meters[0].value == saved.m82_before + 1000
  host expect field meters[1].value == saved.m83_before + 1

  note "Meters 0082 and 0083, the same pair for restricted tickets."
  note "The extraction of this document repeats this section number three times where the source runs 10.7.15 to 10.7.17, with the same title and the same criteria each time. They are written here as one; if the source holds three distinct cases, this is the place that owes two more."
}

test "10.7.18 a ticket inserted is reported in the event format" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires stimulus.ticket, stimulus.real_time_events, decoder.lp4C, decoder.lp70, decoder.lp71
  machine validation secure_enhanced

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1

  host send lp 0E data 01
  host forget exceptions within 2s

  egm ticket insert amount=1000 validation=0000000000000004
  host expect exception ticket_inserted within 8s

  # Refused, so the escrow is empty for the case after this one.
  host send lp 70
  host send lp 71 with transfer_code=ttc_ticket_already_redeemed transfer_amount=1000 parsing_code=0 validation_data=0000000000000004
  host expect field machine_status of lp 71 == gmsc_ticket_rejected_by_host_or_unknown
}

test "10.7.19 a transfer complete is reported in the event format" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires stimulus.ticket, stimulus.real_time_events, host.validation_sequence
  requires decoder.lp4C, decoder.lp70, decoder.lp71
  machine validation secure_enhanced

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1

  host send lp 0E data 01
  host forget exceptions within 2s

  egm ticket insert amount=1000 validation=0000000000000019
  host expect exception ticket_inserted within 8s
  host send lp 70
  host expect field validation_data of lp 70 == 0000000000000019

  # Redeemed, so the machine reports the transfer complete, in the event format.
  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=1000 parsing_code=0 validation_data=0000000000000019
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 8s
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed

  host send lp 0E data 00
}
