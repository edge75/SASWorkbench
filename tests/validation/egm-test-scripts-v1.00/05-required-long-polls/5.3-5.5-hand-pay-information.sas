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
  tag @egm-test-scripts @chapter-5 @needs-stimulus
  requires machine.partial_pay, stimulus.hopper, stimulus.handpay.jackpot, stimulus.handpay.reset
  requires stimulus.settle, decoder.lp1B
  machine accounting denomination 5c
  machine partial pay on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # Ten coins leave the hopper towards the jackpot; the attendant owes the rest.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field progressive_group of lp 1B == 0
  host expect field level of lp 1B == 0
  host expect field amount of lp 1B == 29990
  host expect field partial_pay of lp 1B == 10

  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "Configure the machine for a hand pay queue, a low jackpot limit and a partial pay of ten coins, a coin being one accounting unit. A machine that does not offer partial pays passes by saying so."
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
  tag @egm-test-scripts @chapter-5 @needs-stimulus
  requires machine.partial_pay, stimulus.hopper, stimulus.handpay, stimulus.bill, stimulus.button.collect
  requires stimulus.handpay.reset, stimulus.settle, decoder.lp1B
  machine accounting denomination 5c
  machine handpay legacy
  machine partial pay on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # Over the hopper limit: ten coins from the hopper, the rest by hand.
  egm set hopper limit 1000
  egm bill insert 2000
  egm press collect
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field level of lp 1B == $80
  host expect field amount of lp 1B == 390
  host expect field partial_pay of lp 1B == 10

  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "Configure the machine for legacy hand pay, a low hopper limit and a partial pay of ten coins. Some machines count the coins as an ordinary machine pay before the hand pay; either is a pass when the reply says so consistently."
}

# The legacy form keeps one hand pay and forgets it on the reset, whether or not it was ever read.
test "5.5.1 a legacy jackpot hand pay reads zero after the reset" {
  tag @egm-test-scripts @chapter-5 @needs-stimulus
  requires machine.legacy_handpay, stimulus.handpay.jackpot, stimulus.handpay.reset, stimulus.settle
  requires decoder.lp1B
  machine accounting denomination 5c
  machine handpay legacy

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # Exactly one $51, not one every fifteen seconds.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host forget exceptions within 2s
  host expect no exception handpay_is_pending within 30s

  host send lp 1B
  host expect field progressive_group of lp 1B == 0
  host expect field level of lp 1B == 0
  host expect field amount of lp 1B == 30000
  host expect field partial_pay of lp 1B == 0

  # The reset brings $52, and the reading after it is zeros throughout.
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host wait 1s
  host send lp 1B
  host expect field progressive_group of lp 1B == 0
  host expect field level of lp 1B == 0
  host expect field amount of lp 1B == 0
  host expect field partial_pay of lp 1B == 0
  host expect field reset_id of lp 1B == 0

  note "Configure legacy hand pay and a jackpot limit below fifteen hundred dollars before the run. The document adds a partial pay of ten coins; a machine without a hopper pays none, so the partial pay reads zero."
}

test "5.5.2 a legacy cancelled-credit hand pay reads zero after the reset" {
  tag @egm-test-scripts @chapter-5 @needs-stimulus
  requires machine.legacy_handpay, stimulus.handpay, stimulus.handpay.reset, stimulus.settle
  requires decoder.lp1B
  machine accounting denomination 5c
  machine handpay legacy

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # Exactly one $51, not one every fifteen seconds.
  egm handpay 2000
  host expect exception handpay_is_pending within 10s
  host forget exceptions within 2s
  host expect no exception handpay_is_pending within 30s

  host send lp 1B
  host expect field progressive_group of lp 1B == 0
  host expect field level of lp 1B == $80
  host expect field amount of lp 1B == 400
  host expect field partial_pay of lp 1B == 0

  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host wait 1s
  host send lp 1B
  host expect field progressive_group of lp 1B == 0
  host expect field level of lp 1B == 0
  host expect field amount of lp 1B == 0
  host expect field partial_pay of lp 1B == 0
  host expect field reset_id of lp 1B == 0

  note "Configure legacy hand pay before the run. The document cashes out over a low hopper limit with a partial pay of ten coins; a machine without a hopper pays it all by hand, so the partial pay reads zero."
}
