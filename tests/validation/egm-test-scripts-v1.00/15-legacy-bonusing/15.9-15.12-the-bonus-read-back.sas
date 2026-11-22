# Chapter 15, sections 15.9 to 15.12 - asking the machine what it paid.
#
# Long poll $90 answers with the last legacy bonus: multiplier, multiplied win, tax status and the
# bonus itself, and reads zero once it has been read. A bonus too large for the jackpot limit is
# not paid to the credit meter at all but locks the machine up in a hand pay, which the hand pay
# information poll $1B then describes like any other jackpot - group 0, level 0, the bonus as the
# amount - and $7C follows the reset rather than the award.
#
# The software machine takes legacy bonuses but sets no jackpot limit (machine.jackpot_limit), so
# only 15.9 runs here.

test "15.9 the last bonus can be read back" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, decoder.lp90

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  # Deductible.
  host send lp 8A data 0000000500
  host expect exception legacy_bonus_pay_or_jackpot_awarded within 8s
  host send lp 90
  host expect field multiplier == 0
  host expect field multiplied_win == 0
  host expect field tax_status == 0
  host expect field bonus == 5

  # Non-deductible.
  host send lp 8A data 0000000501
  host expect exception legacy_bonus_pay_or_jackpot_awarded within 8s
  host send lp 90
  host expect field tax_status == 1
  host expect field bonus == 5

  # Wager match.
  host send lp 8A data 0000000502
  host expect exception legacy_bonus_pay_or_jackpot_awarded within 8s
  host send lp 90
  host expect field tax_status == 2
  host expect field bonus == 5

  # Read again with nothing new: zero.
  host send lp 90
  host expect field multiplier == 0
  host expect field multiplied_win == 0
  host expect field bonus == 0

  note "Before the run, enable legacy bonusing and leave the machine idle. Each bonus also adds five credits to the credit meter at once; 15.3 asserts that."
  note "The document's last read shows no tax status value; a machine that keeps the previous one there rather than zeroing it is not failing."
}

test "15.10 a bonus above the jackpot limit is a hand pay" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, machine.jackpot_limit, stimulus.handpay.reset, stimulus.settle
  requires decoder.lp1B

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 8A data 0000050000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field progressive_group == 0
  host expect field level == 0
  host expect field amount == 500
  host expect field partial_pay == 0
  host expect field reset_id == 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect exception legacy_bonus_pay_or_jackpot_awarded within 15s

  note "Before the run, enable legacy bonusing, set the jackpot limit below 500 credits, and disable partial pay and progressives. The amount is in accounting credits."
  note "With reporting on, $51 and $52 arrive in the event format and $7C carries the bonus with tax status 00."
}

test "15.11 two bonuses above the limit are two hand pays" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, machine.jackpot_limit, stimulus.handpay.reset, stimulus.settle
  requires decoder.lp1B, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0400000C00
  host remember field meters[0].value as credits

  # Two of 500 and one of 5, all deductible.
  host send lp 8A data 0000050000
  host send lp 8A data 0000050000
  host send lp 8A data 0000000500

  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field level == 0
  host expect field amount == 500
  host expect field partial_pay == 0
  host expect field reset_id == 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect exception legacy_bonus_pay_or_jackpot_awarded within 15s

  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount == 500
  host expect field partial_pay == 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect exception legacy_bonus_pay_or_jackpot_awarded within 15s

  # The small one goes to the credit meter.
  host send lp 6F data 0400000C00
  host expect field meters[0].value == saved.credits + 5

  note "Before the run, enable legacy bonusing, set the jackpot limit below 500 credits, and disable partial pay and progressives."
  note "With reporting on, the first reset is followed by $7C carrying 500 and then the second $51; the second reset by $7C carrying 500 and $7C carrying 5. The order is in the trace."
}

test "15.12 a bonus during a game's own hand pay is added to the next" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, machine.jackpot_limit, machine.partial_pay
  requires stimulus.hopper, stimulus.play, stimulus.handpay.reset
  requires decoder.lp1B

  note "Enable legacy bonusing, set a low jackpot limit with a partial pay of ten credits, and disable progressives. Play to a jackpot: ten coins leave the hopper and $51 arrives."
  note "Send a deductible bonus above the jackpot limit and then one of five credits; both are acknowledged. The hand pay information shows the game's jackpot less the ten-credit partial pay, and a partial pay of 10."
  note "Reset it: $52, then $51 again. The hand pay information now shows the large bonus plus the five credits as one amount, with no partial pay. Reset it: $52, then $7C carrying that sum with tax status 00."
}
