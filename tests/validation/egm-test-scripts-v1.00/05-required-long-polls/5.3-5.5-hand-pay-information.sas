# Chapter 5, sections 5.3 to 5.5 - what the hand pay information poll reports.
#
# Six cases on one reply. The level byte says what kind of hand pay it is - zero for a game win,
# $40 for a top award, $80 for cancelled credits - and the amount is in accounting units. The
# partial-pay cases add the coins the hopper paid before the hand pay took over, and the legacy
# cases add that the reply comes back zeroed once the attendant has reset it.
#
# The amounts below are asserted exactly. Each running case declares the five-cent accounting
# unit, so a fifteen-hundred-dollar jackpot is thirty thousand units and a twenty-dollar cash out is
# four hundred; a machine in any other unit skips rather than being compared with the wrong number.

test "5.3.1 a jackpot hand pay with no partial pay" {
  tag @egm-test-scripts @chapter-5 @needs-stimulus
  requires stimulus.handpay.jackpot, stimulus.handpay.reset, stimulus.settle
  requires decoder.lp1B
  machine accounting denomination 5c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s

  # A game win that is not a top award: no group, level zero, the whole amount, nothing paid.
  host send lp 1B
  host expect field progressive_group of lp 1B == 0
  host expect field level of lp 1B == 0
  host expect field amount of lp 1B == 30000
  host expect field partial_pay of lp 1B == 0

  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "Before the run, configure the machine for a hand pay queue, a jackpot limit below fifteen hundred dollars, and no partial pay."
  note "A win the machine counts as its top award carries level $40 instead of zero. The reset byte is 00 for a machine that only offers a standard hand pay and 01 for one that can also reset to the credit meter; read it in the frames against the machine's configuration."
}

test "5.3.2 a jackpot hand pay with a partial pay" {
  tag @egm-test-scripts @chapter-5 @needs-a-person
  requires machine.partial_pay, stimulus.hopper, stimulus.handpay.jackpot
  requires decoder.lp1B

  note "Configure the machine for a hand pay queue, a low jackpot limit and a partial pay of ten coins. Play until a jackpot hand pay: $51 on a general poll, and ten coins leave the hopper."
  note "Read the hand pay information: as in 5.3.1, except that the amount is the jackpot less the partial pay, and the partial pay field is what the hopper paid, both in accounting units. Reset the hand pay: $52."
  note "A machine that does not offer partial pays passes by saying so; this build has no way to configure one or to watch a hopper pay."
}

test "5.4.1 a cancelled-credit hand pay with no partial pay" {
  tag @egm-test-scripts @chapter-5 @needs-stimulus
  requires stimulus.handpay, stimulus.handpay.reset, stimulus.settle
  requires decoder.lp1B
  machine accounting denomination 5c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm handpay 2000
  host expect exception handpay_is_pending within 10s

  # Cancelled credits are level $80, and the amount is what the player tried to cash out.
  host send lp 1B
  host expect field progressive_group of lp 1B == 0
  host expect field level of lp 1B == $80
  host expect field amount of lp 1B == 400
  host expect field partial_pay of lp 1B == 0

  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "The document configures the machine for legacy hand pay. Nothing this case asserts differs between the legacy and the queued form, so it runs on whichever the machine has; the difference between the two is what 5.5 and 5.6 test."
}

test "5.4.2 a cancelled-credit hand pay with a partial pay" {
  tag @egm-test-scripts @chapter-5 @needs-a-person
  requires machine.partial_pay, stimulus.hopper, stimulus.handpay
  requires decoder.lp1B

  note "Configure the machine for legacy hand pay, a low hopper limit and a partial pay of ten coins. Put credits above the limit on it and cash out: $51."
  note "Read the hand pay information: as in 5.4.1, except that the amount is the cash out less what the hopper paid, and the partial pay field is that payment, both in accounting units. Reset the hand pay: $52."
  note "Some machines do not offer partial pays, and some count the coins as an ordinary machine pay before the hand pay; either is a pass when the reply says so consistently."
}

# The legacy form keeps one hand pay and forgets it on the reset, whether or not it was ever read.
test "5.5.1 a legacy jackpot hand pay reads zero after the reset" {
  tag @egm-test-scripts @chapter-5 @needs-a-person
  requires machine.legacy_handpay, stimulus.handpay.jackpot, stimulus.handpay.reset
  requires decoder.lp1B

  note "Configure the machine for legacy hand pay, a low jackpot limit and a partial pay of ten coins. Play to a jackpot hand pay and watch for thirty seconds: exactly one $51, not one every fifteen seconds."
  note "Read the hand pay information: the jackpot, shaped as in 5.3.2. Reset the hand pay: $52. Read it again: every byte between the command and the check sequence is zero."
}

test "5.5.2 a legacy cancelled-credit hand pay reads zero after the reset" {
  tag @egm-test-scripts @chapter-5 @needs-a-person
  requires machine.legacy_handpay, stimulus.handpay, stimulus.handpay.reset
  requires decoder.lp1B

  note "Configure the machine for legacy hand pay, a low hopper limit and a partial pay of ten coins. Put more credits than the limit on it, cash out, and watch for thirty seconds: exactly one $51."
  note "Read the hand pay information: the cash out, shaped as in 5.4.2. Reset the hand pay: $52. Read it again: every byte between the command and the check sequence is zero."
}
