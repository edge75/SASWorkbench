# The HAL build of the demo, reached through qxcom-hal-bridge, with no person: the qpm stimulus
# inserts the notes and drops the ticket into the acceptor directory. The qpm_simulator window must
# be closed, because it rewrites the shared region on every frame. The machine is on secure
# enhanced validation with ticket in enabled.

test "two notes in a row are both accepted" {
  tag @needs-stimulus
  requires stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions

  egm bill insert 2000
  host expect exception bill_accepted_20 within 10s
  egm bill insert 2000
  host expect exception bill_accepted_20 within 10s
}

test "a note returned while the acceptor is disabled does not block the next one" {
  tag @needs-stimulus
  requires stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions

  host send lp 07
  egm bill insert 2000
  host expect exception bill_rejected within 10s

  host send lp 06
  egm bill insert 2000
  host expect exception bill_accepted_20 within 10s
}

test "a host redeems a ticket dropped into the acceptor" {
  tag @needs-stimulus
  requires decoder.lp70
  requires decoder.lp71

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1
  egm settle
  host forget exceptions

  egm ticket insert amount=2000 validation=1234567890123456
  host expect exception ticket_inserted within 15s

  host send lp 70
  host expect field validation_data of lp 70 == 1234567890123456

  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=2000 parsing_code=0 validation_data=1234567890123456
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 15s

  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed
}
