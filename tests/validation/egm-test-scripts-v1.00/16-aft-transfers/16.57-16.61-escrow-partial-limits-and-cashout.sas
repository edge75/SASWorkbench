# Chapter 16, sections 16.57 to 16.61 - transfers that cannot simply complete on the spot.
#
# A transfer that arrives mid-game has to wait for the game; one that asks for more than the
# machine holds or can take has to be cut down to what is possible; a transfer to the host can be
# the machine's own cashout; and a second request on top of one still pending has to be turned
# away without disturbing the first. In every one of them the status the host reads afterwards
# must be exactly what happened to the money.
#
# None can run here: the funds-transfer command has no layout and the software machine does not
# transfer.

test "16.57 a transfer that arrives during a game waits for it" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, machine.aft
  requires stimulus.game_in_progress

  note "Start a game and, while it is still running, ask for a partial cashable transfer into the machine: it is answered as pending ($40)."
  note "Once the game ends the transfer completes, and $69 then repeats about every fifteen seconds until the status is read."
}

test "16.58 partial transfers to the host take what there is" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.restricted_credits, host.compare_replies

  note "From an empty machine, put ten dollars of restricted credit on; then ask for a partial transfer to the host far larger than that in every type. The machine sends exactly the ten restricted dollars, the status shows them, and the credit meter is zero."
  note "The same with five dollars of non-restricted credit and then with five dollars cashable: each time exactly what was there goes back, and the machine ends empty."
  note "Finally all three in turn, leaving the restricted, non-restricted and cashable amounts on the machine together."
}

test "16.59 a transfer near the credit limit" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.credit_limit, stimulus.bill, host.meter_arithmetic

  note "Bring the machine within four dollars of its credit limit. A full five-dollar transfer is refused, as over the limit or not possible now ($84 or $86)."
  note "A partial five-dollar transfer is accepted as pending and completes with the gap to the limit and no more; the credit meter ends exactly at the limit."
  note "Within ten dollars of the limit, a partial transfer of one cashable, five restricted and four non-restricted dollars fits whole. Within six dollars, the same request delivers the five restricted and one non-restricted and no cashable: the order the machine fills a partial transfer in is restricted, then non-restricted, then cashable."
}

test "16.60 the host takes a cashout as a transfer" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.restricted_credits

  note "Put five dollars of each credit type on an empty machine. A zero-amount transfer to the host carrying the cashout flag is accepted as pending, raises $69, and the machine cashes out the cashable and non-restricted credits by its usual means; the restricted ones stay or leave on a restricted ticket, and the transfer record shows nothing moved."
  note "Reload the machine and repeat with the cashout request also asking for six restricted dollars: cashable and non-restricted leave as before, the five restricted dollars leave the credit meter, and the record shows five restricted and zero for the other two."
}

test "16.61 a second transfer while one is in progress" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft
  requires machine.aft_bonus, host.poll_sequence

  note "Ask for a full cashable transfer with a receipt, then, while it is pending, a full bonus jackpot transfer: the second is refused as conflicting with the one in progress ($C0)."
  note "A partial bonus transfer straight after is refused the same way, with $C0 or $C1."
  note "With the round stopped, three status reads and then the refused transfer again: every status shows where the first transfer really ended up, and the refusal is still $C0 or $C1. A final status read shows the amount the first one moved."
}
