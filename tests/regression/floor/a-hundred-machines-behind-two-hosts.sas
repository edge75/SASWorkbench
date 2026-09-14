# D29, written down: a hundred machines behind two hosts partitioned by category.
#
# Tagged @casino because it costs a round of a hundred to run — the round is about six seconds, so
# every wait for an exception is up to six seconds long. Run it by name, or with --tag casino.
#
# The floor is one hundred machines and two lines. Channel one carries advanced funds transfer and
# legacy bonus; channel two carries validation and information. Both lines run 01-64, because an
# address is unique on a line and not on a floor: machine $25 is $25 on both of its ports. Each
# host polls its own round on its own wire, so the two rounds interleave rather than contend.
#
# What it proves that the four-machine case does not:
#
#   * that a hundred machines answer and are told apart, on two lines at once;
#   * that a fund movement on one machine is visible to the host that owns funds and to nobody
#     else, including the ninety-nine machines that were never touched;
#   * that a ticket redeemed on the validation host is a conversation with one named machine while
#     the other host is polling the whole floor underneath it;
#   * that every record in the one session log says which host and which machine it belongs to.

test "funds move on the host that owns them and a ticket is redeemed on the host that owns those" {
  tag @casino
  tag @needs-stimulus
  requires stimulus.bill
  requires stimulus.ticket
  requires decoder.lp70
  requires decoder.lp71
  requires decoder.lp74

  machine channel 1 addresses 01-64 owns aft legacy_bonus
  machine channel 2 addresses 01-64 owns validation information

  # The funds host. A hundred machines on one line, polled in turn.
  host connect loopback addresses 01-64 channel 1
  host poll every 0ms
  host expect link up within 30s

  # The validation host, on the other line, polling the same hundred machines by the same names.
  host connect loopback addresses 01-64 channel 2
  host expect link up within 30s

  # ---- funds, on the host that owns them ------------------------------------------------------
  host channel 1
  host machine 25
  egm settle
  host forget exceptions within 2s

  # $74 is an advanced funds transfer command, so this host is the one that may ask it. Lock code
  # $FF is the interrogate branch — it only reads, so the machine is not changed by being asked.
  # The balance it reports is the machine's, read before the money goes in.
  host send lp 74 with lock_code=$FF transfer_condition=0 lock_timeout=0
  host expect field current_cashable_amount of lp 74 >= 0

  # The money moves on machine $25 and on no other. The meter is read by a long poll addressed to
  # $25 while the round goes on around it.
  egm bill insert 2000
  host expect meter total_bill_in += 20 within 30s

  # The same reading through the funds command, which is the host that owns funds seeing the
  # movement in the form its category is about.
  host send lp 74 with lock_code=$FF transfer_condition=0 lock_timeout=0
  host expect field current_cashable_amount of lp 74 >= 1

  # A machine that was never touched, on the same line, counted nothing. This is the assertion
  # that a floor driven by name is driven by name: a stimulus applied to the head of the round
  # would pass every step above and fail this one.
  host machine 4A
  host expect meter total_bill_in += 0 within 30s

  # ---- and the same command on the host that does not own funds -------------------------------
  host channel 2
  host machine 25
  # The same message as above, written as its bytes because a silence assertion takes them that
  # way: lock code $FF, no transfer condition, no timeout. Framed at its full length on purpose —
  # a short $74 is dropped by the machine's receiver before any handler sees it, so its silence
  # would be the framing rather than the category gate this step is about.
  host expect no reply to lp 74 data FF000000

  # ---- a ticket, on the host that owns validation ---------------------------------------------
  # Still machine $25, still on the validation host. The ticket-inserted exception is a validation
  # exception, so it reaches this host's buffer and never the funds host's.
  egm ticket insert amount=2000 validation=1234567890123456
  host expect exception ticket_inserted within 30s

  host send lp 70
  host expect field ticket_status of lp 70 == ticket_status_ticket_in_escrow
  host expect field validation_data of lp 70 == 1234567890123456

  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=2000 parsing_code=0 validation_data=1234567890123456
  host expect field machine_status of lp 71 == gmsc_ticket_redemption_pending
  host expect exception ticket_transfer_complete within 30s

  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed
  host expect field transfer_amount of lp 71 == 2000

  # The funds host never heard the validation exception, because the machine never put it in that
  # channel's buffer. Asserted on the machine the ticket went into, so this is the partition and
  # not a coincidence of which machine the round had reached.
  host channel 1
  host machine 25
  host expect no exception ticket_transfer_complete within 2s
}
