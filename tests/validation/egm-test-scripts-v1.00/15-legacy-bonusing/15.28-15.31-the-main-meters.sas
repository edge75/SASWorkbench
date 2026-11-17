# Chapter 15, sections 15.28 to 15.31 - what a legacy bonus does to the four main meters.
#
# A bonus is a win that did not come from a wager. It does not touch coin in; it reaches coin out
# when the machine counts its wins as paid; it is a jackpot only when it is paid by an attendant;
# and the credit meter shows it at once. The three bonuses used throughout are five deductible,
# six non-deductible and seven wager-match credits, eighteen in all.
#
# The software machine offers no legacy bonusing (machine.legacy_bonus). Every meter here is read
# through $6F in accounting credits; each must also match the machine's own display (D40).

test "15.28 a bonus is not coin in" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, stimulus.play, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 060000000C00
  host remember field meters[0].value as coin_in
  host remember field meters[1].value as credits

  host send lp 8A data 0000000500
  host send lp 8A data 0000000601
  host send lp 8A data 0000000702
  host send lp 6F data 060000000C00
  host expect field meters[0].value == saved.coin_in
  host expect field meters[1].value == saved.credits + 18

  # One credit played from the bonus is coin in.
  egm play games=1 bet=1 win=0
  host send lp 6F data 0400000000
  host expect field meters[0].value == saved.coin_in + 1

  note "Before the run, enable legacy bonusing and leave the machine with nothing on the credit meter. The document also plays a maximum wager; coin in grows by exactly that many credits."
}

test "15.29 a bonus reaches coin out by the machine's meter model" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, machine.jackpot_limit, stimulus.play, stimulus.hopper
  requires stimulus.handpay.reset, decoder.lp1B, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # A bonus above the jackpot limit is paid by an attendant and never counts as coin out.
  host send lp 6F data 0400000100
  host remember field meters[0].value as coin_out
  host send lp 8A data 0000050002
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount == 500
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host send lp 6F data 0400000100
  host expect field meters[0].value == saved.coin_out

  note "Before the run, enable legacy bonusing, set the jackpot limit below 500 credits and disable partial pay, with nothing on the credit meter."
  note "Then, by hand, with the jackpot limit high: send the three bonuses. A machine that meters a win when it is won counts the eighteen credits as coin out now; one that meters it when it is paid counts them only once they leave the machine. The enabled features say which model this is (meter_model)."
  note "And with the machine set to pay to the hopper: send the three bonuses; eighteen credits leave the hopper and coin out grows by eighteen."
}

test "15.30 a bonus paid by an attendant is a jackpot" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, machine.jackpot_limit
  requires stimulus.handpay.reset, decoder.lp1B, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0400000200
  host remember field meters[0].value as jackpot
  host send lp 8A data 0000050002
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field level == 0
  host expect field amount == 500
  host expect field partial_pay == 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect exception legacy_bonus_pay_or_jackpot_awarded within 15s
  host send lp 6F data 0400000200
  host expect field meters[0].value == saved.jackpot + 500

  note "Before the run, enable legacy bonusing, set the jackpot limit below 500 credits and disable partial pay, with nothing on the credit meter."
  note "Then, by hand, with a partial pay of one coin: the same bonus pays one coin from the hopper before the hand pay, and the jackpot meter grows by the bonus less that one credit (machine.partial_pay)."
}

test "15.31 a bonus reaches the credit meter" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0400000C00
  host remember field meters[0].value as credits
  host send lp 8A data 0000000500
  host send lp 8A data 0000000601
  host send lp 8A data 0000000702
  host expect exception legacy_bonus_pay_or_jackpot_awarded within 8s
  host send lp 6F data 0400000C00
  host expect field meters[0].value == saved.credits + 18

  note "Before the run, enable legacy bonusing and leave the machine with nothing on the credit meter."
}
