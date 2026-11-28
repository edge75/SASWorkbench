# Chapter 12, sections 12.13 to 12.18 - what a progressive win does to the meters that are not
# about progressives.
#
# A progressive the machine pays is coin out, wherever it goes; one an attendant pays is a jackpot
# and never coin out. It is never a paytable win and never an external bonus, paid by either party.
# Each case reads its meter with $6F and $AF before and after the win, and both polls must agree.
#
# The cases run on the software machine, which is built with the declared non-SAS level
# (machine.progressives) and wins it with 'egm progressive win' (stimulus.progressive); those paid by
# hand set the jackpot limit below the level's value. 12.13 also needs a hopper. Every meter must also match the machine's own display,
# which is a person's to compare (D40). Each case starts with nothing on the credit meter, and the
# software machine is given one note to stake its game with.

test "12.13 a progressive paid by the machine is coin out, one paid by hand is not" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, machine.jackpot_limit, stimulus.hopper
  requires stimulus.handpay.reset, decoder.lp6F, decoder.lpAF, decoder.lp1B, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0400000100
  host remember field meters[0].value as coin_out
  host send lp AF data 0400000100
  host expect field meters[0].value == saved.coin_out

  # Paid to the credit meter: coin out grows by the win.
  note "Set wins to go to the credit meter, play until level 1 is won, and note the win in accounting credits."
  host send lp 6F data 0400000100
  host expect field meters[0].value > saved.coin_out
  host remember field meters[0].value as coin_out
  host send lp AF data 0400000100
  host expect field meters[0].value == saved.coin_out

  # Paid from the hopper: coin out grows again.
  note "Set wins to be paid from the hopper, play until level 1 is won, and note the win in accounting credits."
  host send lp 6F data 0400000100
  host expect field meters[0].value > saved.coin_out
  host remember field meters[0].value as coin_out
  host send lp AF data 0400000100
  host expect field meters[0].value == saved.coin_out

  # Paid by an attendant: coin out does not move.
  note "Set the jackpot limit below the level's value and play until level 1 is won as a hand pay."
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host send lp 6F data 0400000100
  host expect field meters[0].value == saved.coin_out
  host send lp AF data 0400000100
  host expect field meters[0].value == saved.coin_out

  note "Before the run, configure the machine for a non-SAS progressive at level 1, select the progressive game, and leave nothing on the credit meter."
  note "Each of the two increases is exactly the win noted; the steps assert only that the meter moved, because the win is known to the person, not to the script."
}

test "12.14 a progressive paid by hand is a jackpot" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, machine.jackpot_limit, stimulus.bill
  requires stimulus.handpay.reset, decoder.lp6F, decoder.lpAF, decoder.lp1B, host.meter_arithmetic
  machine progressive non_sas levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm set jackpot limit 500
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 6F data 0400000200
  host remember field meters[0].value as jackpot
  host send lp AF data 0400000200
  host expect field meters[0].value == saved.jackpot

  egm progressive win level=1
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host send lp 6F data 0400000200
  host expect field meters[0].value > saved.jackpot
  host remember field meters[0].value as jackpot
  host send lp AF data 0400000200
  host expect field meters[0].value == saved.jackpot

  note "Before the run, configure the machine for a non-SAS progressive at level 1, a jackpot limit below the level's value and no partial pay, select the progressive game, and leave nothing on the credit meter."
  note "The jackpot meter grows by the hand pay in accounting credits, less anything the hopper paid towards it."
  note "Then, by hand, win level 1 again while a legacy or an AFT bonus arrives, so that the two together lock the machine up (machine.legacy_bonus or machine.aft): the jackpot meter grows the same way, and coin out counts the bonus only where it did not end in the hand pay."
}

test "12.15 a progressive is not a machine-paid paytable win" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, stimulus.bill
  requires decoder.lp6F, decoder.lpAF, host.meter_arithmetic
  machine progressive non_sas levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 6F data 0400001C00
  host remember field meters[0].value as paytable
  host send lp AF data 0400001C00
  host expect field meters[0].value == saved.paytable

  # The meter stays put only if the winning game pays no line, so the step says so.
  egm progressive win level=1 win=0
  host expect exception progressive_win within 30s
  host send lp 6F data 0400001C00
  host expect field meters[0].value == saved.paytable
  host send lp AF data 0400001C00
  host expect field meters[0].value == saved.paytable

  note "Before the run, configure the machine for a non-SAS progressive at level 1, select the progressive game, and leave nothing on the credit meter. The machine should already pass 6.32 for this meter."
  note "The winning game must pay nothing from its paytable, or the meter moves by that part; win a progressive on a game that pays no line."
}

test "12.16 a progressive is not a machine-paid external bonus" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, stimulus.bill
  requires decoder.lp6F, decoder.lpAF, host.meter_arithmetic
  machine progressive non_sas levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 6F data 0400001E00
  host remember field meters[0].value as bonus
  host send lp AF data 0400001E00
  host expect field meters[0].value == saved.bonus

  egm progressive win level=1
  host expect exception progressive_win within 30s
  host send lp 6F data 0400001E00
  host expect field meters[0].value == saved.bonus
  host send lp AF data 0400001E00
  host expect field meters[0].value == saved.bonus

  note "Before the run, configure the machine for a non-SAS progressive at level 1, select the progressive game, and leave nothing on the credit meter. The machine should already pass 6.34 for this meter."
}

test "12.17 a progressive paid by hand is not an attendant-paid paytable win" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, machine.jackpot_limit, stimulus.bill
  requires stimulus.handpay.reset, decoder.lp6F, decoder.lpAF, decoder.lp1B, host.meter_arithmetic
  machine progressive non_sas levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm set jackpot limit 500
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 6F data 0400001F00
  host remember field meters[0].value as paytable
  host send lp AF data 0400001F00
  host expect field meters[0].value == saved.paytable

  # The meter stays put only if the winning game pays no line, so the step says so.
  egm progressive win level=1 win=0
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host send lp 6F data 0400001F00
  host expect field meters[0].value == saved.paytable
  host send lp AF data 0400001F00
  host expect field meters[0].value == saved.paytable

  note "Before the run, configure the machine for a non-SAS progressive at level 1, a jackpot limit below the level's value and no partial pay, select the progressive game, and leave nothing on the credit meter. The machine should already pass 6.36 for this meter."
}

test "12.18 a progressive paid by hand is not an attendant-paid external bonus" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, machine.jackpot_limit, stimulus.bill
  requires stimulus.handpay.reset, decoder.lp6F, decoder.lpAF, decoder.lp1B, host.meter_arithmetic
  machine progressive non_sas levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm set jackpot limit 500
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 6F data 0400002100
  host remember field meters[0].value as bonus
  host send lp AF data 0400002100
  host expect field meters[0].value == saved.bonus

  egm progressive win level=1
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host send lp 6F data 0400002100
  host expect field meters[0].value == saved.bonus
  host send lp AF data 0400002100
  host expect field meters[0].value == saved.bonus

  note "Before the run, configure the machine for a non-SAS progressive at level 1, a jackpot limit below the level's value and no partial pay, select the progressive game, and leave nothing on the credit meter. The machine should already pass 6.38 for this meter."
}
