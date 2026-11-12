# Chapter 23, sections 23.13 to 23.13.3 - the four ticket totals, each kept twice: once in cents
# and once as a count.
#
# Each case reads a pair of meters through $6F, $AF and $2F, moves money through a ticket, and
# reads them again. The cents meter must rise by the ticket's value and the count by one. Both are
# differences between two replies, which this language cannot take yet (host.compare_replies), so
# the ticket itself runs end to end and the movement is a note. None of these meters can be read by
# name in this build, because no fixed long poll carries them.
#
# The document puts the machine in system validation for all four. That governs how a printed
# ticket gets its number, not how a ticket is redeemed, so the two ticket-in cases run whatever the
# validation style. The two ticket-out cases need a host that answers a system validation request
# (host.validation_sequence), which this build does not have, so they run on a standard-validation
# machine instead and skip elsewhere.

test "23.13 cashable ticket in, in cents and in quantity" {
  tag @egm-test-scripts @chapter-23 @needs-stimulus @needs-a-person
  requires stimulus.ticket, stimulus.settle
  requires decoder.lp4C, decoder.lp70, decoder.lp71, decoder.lp6F, decoder.lpAF, decoder.lp2F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine in secure enhanced validation keeps its acceptor off until a host sets its identifier.
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0600000D001100
  host expect field meters[0].code >= $0D
  host expect field meters[1].code <= $11
  host send lp AF data 0600000D001100
  host expect field meters[1].code <= $11
  host send lp 2F data 0400000D11
  host expect field meters[1].code <= $11

  egm ticket insert amount=1000 validation=0000000000231300
  host expect exception ticket_inserted within 8s
  host send lp 70
  host expect field validation_data of lp 70 == 0000000000231300
  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=1000 parsing_code=0 validation_data=0000000000231300
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect field transfer_amount of lp 71 == 1000
  host expect exception ticket_transfer_complete within 8s
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed

  host send lp 6F data 0600000D001100
  host expect field meters[1].code <= $11
  host send lp AF data 0600000D001100
  host expect field meters[1].code <= $11
  host send lp 2F data 0400000D11
  host expect field meters[1].code <= $11

  note "Meter 000D rose by 1000 cents, the amount in the redemption reply, and 0011 by one; each carries the same value in all three replies before and after (host.compare_replies)."
  note "Where the machine has a meter display, both match it, the cents meter shown in accounting credits."
}

test "23.13.1 restricted ticket in, in cents and in quantity" {
  tag @egm-test-scripts @chapter-23 @needs-stimulus @needs-a-person
  requires stimulus.ticket, stimulus.settle
  requires decoder.lp4C, decoder.lp70, decoder.lp71, decoder.lp6F, decoder.lpAF, decoder.lp2F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0600000F001300
  host expect field meters[0].code >= $0F
  host expect field meters[1].code <= $13
  host send lp AF data 0600000F001300
  host expect field meters[1].code <= $13
  host send lp 2F data 0400000F13
  host expect field meters[1].code <= $13

  egm ticket insert amount=1000 validation=0000000000231310 type=restricted_promotional_from_cashout
  host expect exception ticket_inserted within 8s
  host send lp 70
  host expect field validation_data of lp 70 == 0000000000231310
  # A restricted ticket carries an expiry date and a pool with the host's verdict.
  host send lp 71 with transfer_code=ttc_valid_restricted_promotional_ticket transfer_amount=1000 parsing_code=0 validation_data=0000000000231310 restricted_expiration_month=12 restricted_expiration_day=31 restricted_expiration_year=2030 pool_id=0
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 8s
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_restricted_promotional_ticket_redeemed

  host send lp 6F data 0600000F001300
  host expect field meters[1].code <= $13
  host send lp AF data 0600000F001300
  host expect field meters[1].code <= $13
  host send lp 2F data 0400000F13
  host expect field meters[1].code <= $13

  note "Meter 000F rose by 1000 cents and 0013 by one, the same in all three replies (host.compare_replies)."
  note "Where the machine has a meter display, both match it."
}

test "23.13.2 cashable ticket out, in cents and in quantity" {
  tag @egm-test-scripts @chapter-23 @needs-stimulus @needs-a-person
  requires egm.validation == standard
  requires stimulus.bill, stimulus.button.collect, stimulus.settle, printer.wallet
  requires decoder.lp6F, decoder.lpAF, decoder.lp2F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0600000E001200
  host expect field meters[0].code >= $0E
  host expect field meters[1].code <= $12
  host send lp AF data 0600000E001200
  host expect field meters[1].code <= $12
  host send lp 2F data 0400000E12
  host expect field meters[1].code <= $12

  egm bill insert 100
  egm press collect
  egm expect ticket printed within 20s
  egm expect ticket amount_cents >= 100
  egm expect ticket validation_type == cashable_from_cashout

  host send lp 6F data 0600000E001200
  host expect field meters[1].code <= $12
  host send lp AF data 0600000E001200
  host expect field meters[1].code <= $12
  host send lp 2F data 0400000E12
  host expect field meters[1].code <= $12

  note "Meter 000E rose by the ticket's value in cents and 0012 by one, the same in all three replies. The ticket is the whole credit meter, so on a machine that already held credits it is worth more than the 100 cents put in (host.compare_replies)."
  note "Where the machine has a meter display, both match it."
}

test "23.13.3 restricted ticket out, in cents and in quantity" {
  tag @egm-test-scripts @chapter-23 @needs-stimulus @needs-a-person
  requires egm.validation == standard
  requires machine.restricted_credits, stimulus.button.collect, printer.wallet
  requires decoder.lp6F, decoder.lpAF, decoder.lp2F

  note "Read meters 0010 and 0014 through $6F, $AF and $2F (lists 06 00 00 10 00 14 00 and 04 00 00 10 14). Put 100 cents of restricted credit on the machine and cash out: it prints a restricted ticket."
  note "Read them again: 0010 rose by 100 cents and 0014 by one, the same in all three replies, and both match the machine's meter display where it has one. This build cannot put restricted credit on a machine."
}
