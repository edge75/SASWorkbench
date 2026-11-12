# Chapter 6, sections 6.22 to 6.26 - two counts that restart, the credit meter, and credits an
# attendant pays by hand.
#
# Games since power-up and games since the door closed are the only meters in the chapter that go
# back to zero in normal running, so each is played up, restarted, and played up again.
#
# The two hand pay cases cancel credits rather than pay a jackpot: a cash out the hopper cannot
# cover. Total hand-paid cancelled credits (0003) has no single-meter long poll, so this host cannot
# watch it move; it reads it through $6F, and how far it moved is left to a person.

test "6.22 games since power-up restarts at a power cycle" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person @destructive
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, stimulus.play, machine.power_cycle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002500
  host expect field meters[0].code == $25
  host send lp AF data 0400002500
  host expect field meters[0].code == $25

  egm bill insert 2000
  egm play games=3 bet=1
  host expect meter games_since_power_up += 3 within 5s

  egm power cycle
  host expect link up within 120s
  host expect meter games_since_power_up == 0 within 5s
  host send lp 6F data 0400002500
  host expect field meters[0].value == 0
  host send lp AF data 0400002500
  host expect field meters[0].value == 0

  egm play games=3 bet=1
  host expect meter games_since_power_up += 3 within 5s

  note "Wait for the machine to be playable again after the power cycle before the last three games. Both replies agree throughout and end matching the audit screen."
}

test "6.23 games since the door closed restarts when it closes" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, stimulus.play, stimulus.door.slot, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle

  host send lp 6F data 0400002600
  host expect field meters[0].code == $26
  host send lp AF data 0400002600
  host expect field meters[0].code == $26

  egm bill insert 2000
  egm play games=3 bet=1
  host expect meter games_since_door_closure += 3 within 5s

  egm door slot open
  egm door slot close
  host expect meter games_since_door_closure == 0 within 5s
  host send lp 6F data 0400002600
  host expect field meters[0].value == 0
  host send lp AF data 0400002600
  host expect field meters[0].value == 0

  egm play games=3 bet=1
  host expect meter games_since_door_closure += 3 within 5s

  note "Both replies agree throughout and end matching the audit screen."
}

test "6.24 current credits follows the credit meter" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, stimulus.play, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000C00
  host expect field meters[0].code == $0C
  host send lp AF data 0400000C00
  host expect field meters[0].code == $0C

  # Whatever was on it is cashed out first, so the meter starts from zero.
  egm bill insert 500
  egm press collect
  host expect meter credits == 0 within 20s
  host send lp 6F data 0400000C00
  host expect field meters[0].value == 0
  host send lp AF data 0400000C00
  host expect field meters[0].value == 0

  egm bill insert 2000
  host expect meter credits +>= 1 within 5s
  egm play games=1 bet=1 win=5
  egm play games=1 bet=1

  note "Reset the hand pay if the cash out became one. Five coins move 000C by their value; with inserted coins set to be wagered, five more leave it alone. This build cannot insert a coin."
  note "The note moves 000C by its value in accounting units. Across the games, read after each one: 000C falls by every wager and rises by every win paid to the credit meter, and always equals the credits the machine shows."
  note "Both replies agree throughout and end matching the audit screen."
}

test "6.25 hand-paid cancelled credits count a cash out paid by an attendant" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp1B, stimulus.settle
  requires stimulus.handpay, stimulus.handpay.reset

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 080000030002000100
  host expect field meters[0].code <= 3
  host expect field meters[2].code <= 3
  host send lp AF data 080000030002000100
  host expect field meters[2].code <= 3

  egm handpay 2000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount != 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect meter total_jackpot unchanged within 3s
  host expect meter total_coin_out unchanged within 3s

  host send lp 6F data 0400000300
  host expect field meters[0].code == 3
  host expect field meters[0].value >= 1
  host send lp AF data 0400000300
  host expect field meters[0].value >= 1

  note "Set the hopper limit so a cash out becomes a cancelled-credit hand pay, with a partial pay of ten credits. 0003 moves by the hand pay less what the hopper paid, in accounting units; this host cannot watch 0003 move, only read it."
  note "Ten coins inserted and played leave 0003 alone. Both replies agree throughout and end matching the audit screen."
}

# Measured against the software machine this fails: it pays the hand pay into 0003 and never
# moves 0004. That is a gap in the software machine; a real machine has not been measured yet.
test "6.26 total cancelled credits counts the same hand pay" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp1B, stimulus.settle
  requires stimulus.handpay, stimulus.handpay.reset

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0A00000400030002000100
  host expect field meters[0].code <= 4
  host expect field meters[3].code <= 4
  host send lp AF data 0A00000400030002000100
  host expect field meters[3].code <= 4

  egm handpay 2000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount != 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect meter total_cancelled_credits +>= 1 within 5s
  host expect meter total_jackpot unchanged within 3s
  host expect meter total_coin_out unchanged within 3s

  note "0004 moves by exactly as much as 0003 did, which is the hand pay less what the hopper paid. Ten coins inserted and played leave 0004 alone."
  note "Both replies agree throughout and end matching the audit screen."
}
