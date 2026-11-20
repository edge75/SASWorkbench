# Chapter 16, sections 16.48 to 16.50 - every kind of transfer, the meters it moves, and a cancel.
#
# The broadest case in the chapter: each direction and each credit type is moved once, and the
# electronic transfer meters that belong to it must move by exactly that much - counts by one,
# credit meters in accounting units, cents meters in cents. The cancel case is the opposite end:
# a transfer withdrawn straight after it was asked for either stops or finishes, and the machine
# has to say which.
#
# 16.49 and 16.50 run against the software machine for the in-house cashable transfers it performs (D45);
# the restricted, non-restricted, debit and ticket kinds of 16.48 wait for a machine that holds them.

test "16.48 each transfer type moves its own meters" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires decoder.lp73, decoder.lp6F, host.compare_replies, host.meter_arithmetic, stimulus.play, machine.restricted_credits, machine.credit_limit

  note "Before each transfer, remember the count, credit and cents meters of the kind being moved; after it, each count is up by one, each credit meter by the amount in accounting units, and each cents meter by the amount in cents."
  note "Into the machine from the house account: cashable, restricted and non-restricted alone and in every combination, each moving 0017 plus the type's own meters (002E/00A0/00A1, 002F/00A2/00A3, 0030/00A4/00A5)."
  note "To a ticket and from a debit account: restricted to ticket moves 0016, 0017, 00AA, 00AB, 002D and 002F; debit to the machine moves 0017, 0031, 00A6, 00A7; debit to ticket moves 0017, 0031, 00AC, 00AD. The debit kinds need the machine registered first, and the ticket kinds need a printer and secure enhanced or system validation."
  note "Out of the machine to the host: cashable, restricted and non-restricted alone and combined, moving 0018 plus 0032/00B8/00B9, 0033/00BA/00BB and 0034/00BC/00BD. The same again as a win paid to the host after a win over a low credit limit, where the manufacturer has to say how restricted and non-restricted wins are produced."
  note "Restricted credits put on the machine raise 001B; playing them lowers 001B and raises 0019 by the wager. Non-restricted credits played raise 001A by the wager."
}

# One cent is declared so a credit meter and a cents meter move by the same number.
test "16.49 the transfer meters" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires decoder.lp6F, host.meter_arithmetic, stimulus.asset_number

  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host forget exceptions within 2s

  # Into the machine: 0017 and 002E in credits, 00A0 in cents, 00A1 a count.
  host send lp 6F data 0A000017002E00A000A100
  host remember field meters[0].value as total_in
  host remember field meters[1].value as cashable_in
  host remember field meters[2].value as cents_in
  host remember field meters[3].value as count_in
  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host send lp 6F data 0A000017002E00A000A100
  host expect field meters[0].value == saved.total_in + 500
  host expect field meters[1].value == saved.cashable_in + 500
  host expect field meters[2].value == saved.cents_in + 500
  host expect field meters[3].value == saved.count_in + 1

  # Out to the host: 0018 and 0032 in credits, 00B8 in cents, 00B9 a count.
  host send lp 6F data 0A000018003200B800B900
  host remember field meters[0].value as total_out
  host remember field meters[1].value as cashable_out
  host remember field meters[2].value as cents_out
  host remember field meters[3].value as count_out
  host send lp 72 data 36000080000000050000000000000000000000003930000000000000000000000000000000000000000000000132000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host send lp 6F data 0A000018003200B800B900
  host expect field meters[0].value == saved.total_out + 500
  host expect field meters[1].value == saved.cashable_out + 500
  host expect field meters[2].value == saved.cents_out + 500
  host expect field meters[3].value == saved.count_out + 1

  note "The document covers these meters through every transfer of 16.48; the restricted, non-restricted, debit and ticket meters are read there."
}

# Without a receipt: receipts are phase two (D45), and a receipt asked of this machine is refused
# before there is anything to cancel.
test "16.50 a transfer cancelled straight after it was asked for" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft
  requires host.poll_sequence, stimulus.asset_number

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host forget exceptions within 2s

  # The transfer and its cancel, with nothing between them on the line.
  host poll sequence
  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
  host send lp 72 data 0180
  host expect field transfer_status <= $80

  # Final: cancelled ($80) or complete ($00), whichever is true.
  host poll every 200ms
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status <= $80

  note "Ask for a full cashable transfer into the machine with a receipt wanted, and send the cancel form of the same command with nothing in between."
  note "If the machine stopped the transfer, its status says it was cancelled ($80); if it was too late, the status is the one the transfer had reached, complete ($00) or pending ($40). Either is a pass so long as it is the truth."
}
