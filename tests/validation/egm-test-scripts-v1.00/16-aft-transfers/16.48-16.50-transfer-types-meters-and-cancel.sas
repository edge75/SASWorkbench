# Chapter 16, sections 16.48 to 16.50 - every kind of transfer, the meters it moves, and a cancel.
#
# The broadest case in the chapter: each direction and each credit type is moved once, and the
# electronic transfer meters that belong to it must move by exactly that much - counts by one,
# credit meters in accounting units, cents meters in cents. The cancel case is the opposite end:
# a transfer withdrawn straight after it was asked for either stops or finishes, and the machine
# has to say which.
#
# All three run against the software machine; the debit and ticket kinds of 16.48 are a note, as the
# platform it mirrors performs neither.

# One cent is declared so a credit meter and a cents meter move by the same number.
test "16.48 each transfer type moves its own meters" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, machine.aft
  requires decoder.lp6F, host.compare_replies, host.meter_arithmetic, stimulus.play, machine.restricted_credits
  requires stimulus.asset_number

  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host forget exceptions within 2s

  # Into the machine, each kind alone: 0017 by the amount, and the kind's credit, cents and count meters.
  host send lp 6F data 0C000017002E00A000A1001B00
  host remember field meters[0].value as a0017
  host remember field meters[1].value as a002e
  host remember field meters[2].value as a00a0
  host remember field meters[3].value as a00a1
  host remember field meters[4].value as a001b
  host send lp 6F data 0E00002F00A200A3003000A400A500
  host remember field meters[0].value as b002f
  host remember field meters[1].value as b00a2
  host remember field meters[2].value as b00a3
  host remember field meters[3].value as b0030
  host remember field meters[4].value as b00a4
  host remember field meters[5].value as b00a5
  host send lp 72 data 36000000000000010000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host send lp 72 data 36000000000000000000000002000000000000003930000000000000000000000000000000000000000000000132000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host send lp 72 data 36000000000000000000000000000000000300003930000000000000000000000000000000000000000000000133000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host send lp 6F data 0C000017002E00A000A1001B00
  host expect field meters[0].value == saved.a0017 + 600
  host expect field meters[1].value == saved.a002e + 100
  host expect field meters[2].value == saved.a00a0 + 100
  host expect field meters[3].value == saved.a00a1 + 1
  host expect field meters[4].value == saved.a001b + 200
  host send lp 6F data 0E00002F00A200A3003000A400A500
  host expect field meters[0].value == saved.b002f + 200
  host expect field meters[1].value == saved.b00a2 + 200
  host expect field meters[2].value == saved.b00a3 + 1
  host expect field meters[3].value == saved.b0030 + 300
  host expect field meters[4].value == saved.b00a4 + 300
  host expect field meters[5].value == saved.b00a5 + 1

  # All three in one transfer: each count moves by one, each amount by its own.
  host send lp 6F data 0C000017002E00A000A1001B00
  host remember field meters[0].value as c0017
  host remember field meters[1].value as c002e
  host remember field meters[2].value as c00a0
  host remember field meters[3].value as c00a1
  host remember field meters[4].value as c001b
  host send lp 6F data 0E00002F00A200A3003000A400A500
  host remember field meters[0].value as d002f
  host remember field meters[1].value as d00a2
  host remember field meters[2].value as d00a3
  host remember field meters[3].value as d0030
  host remember field meters[4].value as d00a4
  host remember field meters[5].value as d00a5
  host send lp 72 data 36000000000000010000000002000000000300003930000000000000000000000000000000000000000000000134000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host send lp 6F data 0C000017002E00A000A1001B00
  host expect field meters[0].value == saved.c0017 + 600
  host expect field meters[1].value == saved.c002e + 100
  host expect field meters[2].value == saved.c00a0 + 100
  host expect field meters[3].value == saved.c00a1 + 1
  host expect field meters[4].value == saved.c001b + 200
  host send lp 6F data 0E00002F00A200A3003000A400A500
  host expect field meters[0].value == saved.d002f + 200
  host expect field meters[1].value == saved.d00a2 + 200
  host expect field meters[2].value == saved.d00a3 + 1
  host expect field meters[3].value == saved.d0030 + 300
  host expect field meters[4].value == saved.d00a4 + 300
  host expect field meters[5].value == saved.d00a5 + 1

  # Out of the machine to the host, all three: 0018 by the total and each kind's own meters.
  host send lp 6F data 0A000018003200B800B900
  host remember field meters[0].value as e0018
  host remember field meters[1].value as e0032
  host remember field meters[2].value as e00b8
  host remember field meters[3].value as e00b9
  host send lp 6F data 0E00003300BA00BB003400BC00BD00
  host remember field meters[0].value as f0033
  host remember field meters[1].value as f00ba
  host remember field meters[2].value as f00bb
  host remember field meters[3].value as f0034
  host remember field meters[4].value as f00bc
  host remember field meters[5].value as f00bd
  host send lp 72 data 36000080000000020000000004000000000600003930000000000000000000000000000000000000000000000135000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host send lp 6F data 0A000018003200B800B900
  host expect field meters[0].value == saved.e0018 + 1200
  host expect field meters[1].value == saved.e0032 + 200
  host expect field meters[2].value == saved.e00b8 + 200
  host expect field meters[3].value == saved.e00b9 + 1
  host send lp 6F data 0E00003300BA00BB003400BC00BD00
  host expect field meters[0].value == saved.f0033 + 400
  host expect field meters[1].value == saved.f00ba + 400
  host expect field meters[2].value == saved.f00bb + 1
  host expect field meters[3].value == saved.f0034 + 600
  host expect field meters[4].value == saved.f00bc + 600
  host expect field meters[5].value == saved.f00bd + 1

  # Played: restricted credit lowers 001B and raises 0019 by the wager, non-restricted raises 001A.
  host send lp 72 data 36000000000000000000000001000000000100003930000000000000000000000000000000000000000000000136000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host send lp 6F data 0A0000000019001A001B00
  host remember field meters[0].value as g0000
  host remember field meters[1].value as g0019
  host remember field meters[2].value as g001a
  host remember field meters[3].value as g001b
  egm play games=1 bet=1
  host send lp 6F data 0A0000000019001A001B00
  host expect field meters[0].value == saved.g0000 + 100
  host expect field meters[1].value == saved.g0019 + 100
  host expect field meters[2].value == saved.g001a
  host expect field meters[3].value == saved.g001b - 100
  egm play games=1 bet=1
  host send lp 6F data 0A0000000019001A001B00
  host expect field meters[0].value == saved.g0000 + 200
  host expect field meters[1].value == saved.g0019 + 100
  host expect field meters[2].value == saved.g001a + 100
  host expect field meters[3].value == saved.g001b - 100

  note "The pairs of kinds into and out of the machine move the same meters as the kinds alone and all three together, each by its own amount."
  note "To a ticket and from a debit account: restricted to ticket moves 0016, 0017, 00AA, 00AB, 002D and 002F; debit to the machine moves 0017, 0031, 00A6, 00A7; debit to ticket moves 0017, 0031, 00AC, 00AD. The platform the software machine mirrors performs neither, so the library refuses a transfer to a ticket and the platform fails a debit one."
  note "The same transfers to the host again as a win paid to the host after a win over a low credit limit, where the manufacturer has to say how restricted and non-restricted wins are produced; the platform the software machine mirrors hands such a win to an attendant instead (see 16.71)."
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
