# The HAL build of the demo, reached through qxcom-hal-bridge, with the qpm_simulator window open.
# The machine is on secure enhanced validation with ticket in enabled. A person does the
# peripheral half by hand: the window rewrites the shared region, so the qpm stimulus cannot.

test "a host redeems a ticket a person puts into the acceptor" {
  requires decoder.lp70
  requires decoder.lp71

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1
  host forget exceptions

  # A person inserts a ticket with validation number 1234567890123456 for $20.00.
  host expect exception ticket_inserted within 120s

  host send lp 70
  host expect field validation_data of lp 70 == 1234567890123456

  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=2000 parsing_code=0 validation_data=1234567890123456
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 15s

  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed
}

test "a HAL machine prints a cash-out ticket while a host polls" {
  requires decoder.lp4D

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions

  # A machine can still be re-reporting an older ticket's exception, so the cash-out itself is
  # proven by the credit meter emptying, not by the exception alone.
  host expect meter credits >= 1 within 2s

  # A person presses Collect on the game.
  host expect meter credits == 0 within 120s
  host expect exception cashout_ticket_printed within 15s
  host send lp 4D with function=0
  # The record counts as read only once the host polls again after long poll 4D; an unread one
  # takes the printer out of use as the cash-out device ten seconds later.
  host send lp 1A
}
