# Chapter 16, sections 16.57 to 16.61 - transfers that cannot simply complete on the spot.
#
# A transfer that arrives mid-game has to wait for the game; one that asks for more than the
# machine holds or can take has to be cut down to what is possible; a transfer to the host can be
# the machine's own cashout; and a second request on top of one still pending has to be turned
# away without disturbing the first. In every one of them the status the host reads afterwards
# must be exactly what happened to the money.
#
# The software machine holds all three kinds of credit and a credit limit a case can set, so all but
# 16.57, which needs a game left running, run against it.

test "16.57 a transfer that arrives during a game waits for it" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, machine.aft
  requires stimulus.game_in_progress

  note "Start a game and, while it is still running, ask for a partial cashable transfer into the machine: it is answered as pending ($40)."
  note "Once the game ends the transfer completes, and $69 then repeats about every fifteen seconds until the status is read."
}

test "16.58 partial transfers to the host take what there is" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, decoder.lp74, machine.aft
  requires machine.restricted_credits, host.compare_replies, stimulus.asset_number

  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host forget exceptions within 2s

  # Ten restricted dollars on, then a partial transfer to the host far larger in every type: exactly the ten go back.
  host send lp 72 data 36000000000000000000000010000000000000003930000000000000000000000000000000000000000000000131000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host send lp 72 data 36010080000010000000001000000000100000003930000000000000000000000000000000000000000000000132000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field restricted_amount == 1000
  host expect field cashable_amount == 0
  host expect field nonrestricted_amount == 0
  host expect meter credits == 0 within 2s

  # Five non-restricted dollars, the same way.
  host send lp 72 data 36000000000000000000000000000000000500003930000000000000000000000000000000000000000000000133000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host send lp 72 data 36010080000010000000001000000000100000003930000000000000000000000000000000000000000000000134000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field nonrestricted_amount == 500
  host expect field restricted_amount == 0
  host expect field cashable_amount == 0
  host expect meter credits == 0 within 2s

  # Five cashable dollars, the same way.
  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000135000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host send lp 72 data 36010080000010000000001000000000100000003930000000000000000000000000000000000000000000000136000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field cashable_amount == 500
  host expect field restricted_amount == 0
  host expect field nonrestricted_amount == 0
  host expect meter credits == 0 within 2s

  # All three together: each goes back exactly, and the machine ends empty.
  host send lp 72 data 36000000000000050000000010000000000500003930000000000000000000000000000000000000000000000137000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host send lp 72 data 36010080000010000000001000000000100000003930000000000000000000000000000000000000000000000138000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field cashable_amount == 500
  host expect field restricted_amount == 1000
  host expect field nonrestricted_amount == 500
  host send lp 74 data FF000000
  host expect field current_cashable_amount == 0
  host expect field current_restricted_amount == 0
  host expect field current_nonrestricted_amount == 0
}

# One cent is declared so the room under the credit limit is counted in cents.
test "16.59 a transfer near the credit limit" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, machine.aft
  requires machine.credit_limit, machine.restricted_credits, stimulus.credit_limit, stimulus.asset_number

  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host forget exceptions within 2s

  # Within four dollars of the limit: a full five-dollar transfer is refused, a partial one fills the gap and no more.
  egm set credit limit 400
  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status == $84
  host send lp 72 data 36010000000000050000000000000000000000003930000000000000000000000000000000000000000000000132000000000000000000
  host expect field transfer_status == $40
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $01
  host expect field cashable_amount == 400
  host expect meter credits == 400 within 2s

  # Within ten dollars: one cashable, five restricted and four non-restricted dollars fit whole.
  egm set credit limit 1400
  host send lp 72 data 36010000000000010000000005000000000400003930000000000000000000000000000000000000000000000133000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field cashable_amount == 100
  host expect field restricted_amount == 500
  host expect field nonrestricted_amount == 400

  # Within six dollars: the same request delivers the five restricted and one non-restricted, and no cashable.
  egm set credit limit 2000
  host send lp 72 data 36010000000000010000000005000000000400003930000000000000000000000000000000000000000000000134000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $01
  host expect field restricted_amount == 500
  host expect field nonrestricted_amount == 100
  host expect field cashable_amount == 0
  host expect meter credits == 2000 within 2s

  note "The document also allows the full transfer to be refused as not possible now ($86)."
}

test "16.60 the host takes a cashout as a transfer" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, decoder.lp74, machine.aft
  requires machine.restricted_credits, stimulus.asset_number, printer.wallet

  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host forget exceptions within 2s

  # Five dollars of each kind, then a zero transfer to the host asking the machine to cash out.
  host send lp 72 data 36000000000000050000000005000000000500003930000000000000000000000000000000000000000000000131000000000100000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host send lp 72 data 36000080000000000000000000000000000000083930000000000000000000000000000000000000000000000132000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field cashable_amount == 0
  host expect field restricted_amount == 0
  host expect field nonrestricted_amount == 0

  # The machine pays cashable and non-restricted its usual way, and the restricted credit stays.
  egm expect ticket printed within 10s
  host send lp 74 data FF000000
  host expect field current_cashable_amount == 0
  host expect field current_nonrestricted_amount == 0
  host expect field current_restricted_amount == 500

  # Reloaded, and the request also asks for six restricted dollars: the five there leave, the rest is cashed out.
  host send lp 72 data 36000000000000050000000000000000000500003930000000000000000000000000000000000000000000000133000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host send lp 72 data 36010080000000000000000006000000000000083930000000000000000000000000000000000000000000000134000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field restricted_amount == 500
  host expect field cashable_amount == 0
  host expect field nonrestricted_amount == 0
  egm expect ticket printed within 10s
  host expect meter credits == 0 within 5s

  note "The zero transfer is answered pending and raises $69 on a real machine; the software machine completes it before its reply goes, which is the one answer the host reads back either way."
  note "Restricted credit stays on the machine because the platform's restricted tickets are off; a machine that prints them may cash it out on a restricted ticket instead."
}

test "16.61 a second transfer while one is in progress" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft
  requires machine.aft_bonus, host.poll_sequence, machine.aft_receipts, stimulus.asset_number, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm settle
  host forget exceptions within 2s

  # A full transfer with a receipt, which carries a patron account because an in-house receipt needs
  # one; while it is pending a jackpot bonus and a partial bonus are both refused.
  host send lp 72 data 3C000000000000050000000000000000000000803930000000000000000000000000000000000000000000000131000000000000061104343234320000
  host expect field transfer_status == $40
  host send lp 72 data 36000011000000050000000000000000000000003930000000000000000000000000000000000000000000000132000000000000000000
  host expect field transfer_status == $C0
  host send lp 72 data 36010010000000050000000000000000000000003930000000000000000000000000000000000000000000000133000000000000000000
  host expect field transfer_status >= $C0
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field cashable_amount == 500

  note "Ask for a full cashable transfer with a receipt, then, while it is pending, a full bonus jackpot transfer: the second is refused as conflicting with the one in progress ($C0)."
  note "A partial bonus transfer straight after is refused the same way, with $C0 or $C1."
  note "With the round stopped, three status reads and then the refused transfer again: every status shows where the first transfer really ended up, and the refusal is still $C0 or $C1. A final status read shows the amount the first one moved."
}
