# Chapter 13, sections 13.22 to 13.30 - which meters a SAS progressive win moves, and which it must
# leave alone.
#
# A progressive the machine pays itself goes to coin out (0001) and to machine-paid progressive
# wins (001D); one the attendant pays goes to the jackpot meter (0002) and to attendant-paid
# progressive wins (0020). Neither is a paytable win (001C, 001F) or an external bonus (001E, 0021),
# so those four do not move. The cumulative progressive wins poll ($83) answers 001D and 0020 added
# together, for the game asked about or, for game 0, for the machine.
#
# Each case reads the meter with both $6F and $AF, wins level 1 at $5.00, and compares the gain.
# All of them declare the one-cent accounting unit, so the $5.00 win is five hundred units, and all
# of them compare the reply with the audit screen, which is a person's to do (D40). The cases a
# machine pays itself run on the software machine: they declare it enrolled in group 1, keep level 1
# broadcast in the background and win it with 'egm progressive win'. Those paid by hand set the jackpot
# limit to $4.00 first, below the level's $5.00.

test "13.22 coin out counts what the machine pays itself" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires machine.jackpot_limit, stimulus.handpay.reset, host.meter_arithmetic
  requires decoder.lp80, decoder.lp6F, decoder.lpAF, decoder.lp1B
  requires stimulus.bill
  machine accounting denomination 1c
  machine progressive sas group 1 levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 6F data 0400000100
  host expect field meters[0].code == 1
  host remember field meters[0].value as before
  host send lp AF data 0400000100
  host expect field meters[0].value == saved.before

  egm progressive win level=1
  host expect exception progressive_win within 10s
  host send lp 6F data 0400000100
  host expect field meters[0].value == saved.before + 500
  host send lp AF data 0400000100
  host expect field meters[0].value == saved.before + 500
  host remember field meters[0].value of lp AF as credited

  # Configured as a hand pay, the next win does not reach coin out.
  egm set jackpot limit 400
  egm progressive win level=1
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host send lp 6F data 0400000100
  host expect field meters[0].value == saved.credited
  host send lp AF data 0400000100
  host expect field meters[0].value == saved.credited

  note "Before the run, clear the credit meter, enrol the machine in group 1 with level 1, select the progressive game, and pay wins to the credit meter. Change the jackpot limit so level 1 is a hand pay before the second win."
  note "Not run: the same win paid from the hopper also adds five hundred units to 0001 (stimulus.hopper)."
  note "Every reading matches 0001 on the audit screen in accounting units."
}

test "13.23 the jackpot meter counts what the attendant pays" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires machine.jackpot_limit, stimulus.handpay.reset, host.meter_arithmetic
  requires decoder.lp80, decoder.lp6F, decoder.lpAF, decoder.lp1B
  requires stimulus.bill
  machine accounting denomination 1c
  machine progressive sas group 1 levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  egm set jackpot limit 400
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 6F data 0400000200
  host expect field meters[0].code == 2
  host remember field meters[0].value as before
  host send lp AF data 0400000200
  host expect field meters[0].value == saved.before

  egm progressive win level=1
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host send lp 6F data 0400000200
  host expect field meters[0].value == saved.before + 500
  host send lp AF data 0400000200
  host expect field meters[0].value == saved.before + 500

  note "Before the run, clear the credit meter, set the jackpot limit low, turn partial pay off, enrol the machine in group 1 with level 1, and select the progressive game. With a partial pay the gain is the jackpot less what the hopper paid."
  note "Not run: a progressive won while a legacy or AFT bonus is paid, the two together over the jackpot limit (machine.legacy_bonus or machine.aft_bonus). The jackpot meter gains the hand pay less any hopper payment; a bonus that did not end up in the hand pay goes to coin out instead."
  note "Every reading matches 0002 on the audit screen in accounting units."
}

test "13.24 a progressive is not a paytable win" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires host.compare_replies, decoder.lp80, decoder.lp6F, decoder.lpAF
  requires stimulus.bill
  machine accounting denomination 1c
  machine progressive sas group 1 levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 6F data 0400001C00
  host expect field meters[0].code == $1C
  host remember field meters[0].value as before
  host send lp AF data 0400001C00
  host expect field meters[0].value == saved.before

  egm progressive win level=1
  host expect exception progressive_win within 10s
  host send lp 6F data 0400001C00
  host expect field meters[0].value == saved.before
  host send lp AF data 0400001C00
  host expect field meters[0].value == saved.before

  note "Before the run, clear the credit meter, enrol the machine in group 1 with level 1, select the progressive game, and pay wins to the credit meter. Run 6.32 first."
  note "Every reading matches 001C on the audit screen in accounting units."
}

test "13.25 a progressive is not an external bonus" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires host.compare_replies, decoder.lp80, decoder.lp6F, decoder.lpAF
  requires stimulus.bill
  machine accounting denomination 1c
  machine progressive sas group 1 levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 6F data 0400001E00
  host expect field meters[0].code == $1E
  host remember field meters[0].value as before
  host send lp AF data 0400001E00
  host expect field meters[0].value == saved.before

  egm progressive win level=1
  host expect exception progressive_win within 10s
  host send lp 6F data 0400001E00
  host expect field meters[0].value == saved.before
  host send lp AF data 0400001E00
  host expect field meters[0].value == saved.before

  note "Before the run, clear the credit meter, enrol the machine in group 1 with level 1, select the progressive game, and pay wins to the credit meter. Run 6.34 first."
  note "Every reading matches 001E on the audit screen in accounting units."
}

test "13.26 a progressive hand pay is not an attendant-paid paytable win" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires machine.jackpot_limit, stimulus.handpay.reset, host.compare_replies
  requires decoder.lp80, decoder.lp6F, decoder.lpAF, decoder.lp1B
  requires stimulus.bill
  machine accounting denomination 1c
  machine progressive sas group 1 levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  egm set jackpot limit 400
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 6F data 0400001F00
  host expect field meters[0].code == $1F
  host remember field meters[0].value as before
  host send lp AF data 0400001F00
  host expect field meters[0].value == saved.before

  egm progressive win level=1
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host send lp 6F data 0400001F00
  host expect field meters[0].value == saved.before
  host send lp AF data 0400001F00
  host expect field meters[0].value == saved.before

  note "Before the run, clear the credit meter, set the jackpot limit low, turn partial pay off, enrol the machine in group 1 with level 1, and select the progressive game. Run 6.36 first."
  note "Every reading matches 001F on the audit screen in accounting units."
}

test "13.27 a progressive hand pay is not an attendant-paid external bonus" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires machine.jackpot_limit, stimulus.handpay.reset, host.compare_replies
  requires decoder.lp80, decoder.lp6F, decoder.lpAF, decoder.lp1B
  requires stimulus.bill
  machine accounting denomination 1c
  machine progressive sas group 1 levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  egm set jackpot limit 400
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 6F data 0400002100
  host expect field meters[0].code == $21
  host remember field meters[0].value as before
  host send lp AF data 0400002100
  host expect field meters[0].value == saved.before

  egm progressive win level=1
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host send lp 6F data 0400002100
  host expect field meters[0].value == saved.before
  host send lp AF data 0400002100
  host expect field meters[0].value == saved.before

  note "Before the run, clear the credit meter, set the jackpot limit low, turn partial pay off, enrol the machine in group 1 with level 1, and select the progressive game. Run 6.38 first."
  note "Every reading matches 0021 on the audit screen in accounting units."
}

test "13.28 cumulative progressive wins of the machine as a whole" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires host.meter_arithmetic, decoder.lp80, decoder.lp6F, decoder.lpAF, decoder.lp83
  requires stimulus.bill
  machine accounting denomination 1c
  machine progressive sas group 1 levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  egm bill insert 1000
  host forget exceptions within 2s

  # Game 0 answers 001D and 0020 added together, in accounting units.
  host send lp 6F data 0400001D00
  host remember field meters[0].value as machine_paid
  host send lp AF data 0400002000
  host remember field meters[0].value as attendant_paid
  host send lp 83 with game_number=0
  host expect field game_number of lp 83 == 0
  host expect field cumulative_progressive_win of lp 83 == saved.machine_paid + saved.attendant_paid

  egm progressive win level=1
  host expect exception sas_progressive_level_hit within 10s
  host send lp 83 with game_number=0
  host expect field cumulative_progressive_win of lp 83 == saved.machine_paid + saved.attendant_paid + 500

  note "Before the run, enrol the machine in group 1 with level 1 and select the progressive game. The win also raises $54, or $51 when it is a hand pay; reading $1B then stops $51. The two meter readings match the audit screen."
}

test "13.29 machine-paid progressive wins" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires host.meter_arithmetic, decoder.lp80, decoder.lp6F, decoder.lpAF
  requires stimulus.bill
  machine accounting denomination 1c
  machine progressive sas group 1 levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 6F data 0400001D00
  host expect field meters[0].code == $1D
  host remember field meters[0].value as before
  host send lp AF data 0400001D00
  host expect field meters[0].value == saved.before

  egm progressive win level=1
  host expect exception progressive_win within 10s
  host send lp 6F data 0400001D00
  host expect field meters[0].value == saved.before + 500
  host send lp AF data 0400001D00
  host expect field meters[0].value == saved.before + 500

  note "Before the run, clear the credit meter, enrol the machine in group 1 with level 1, select the progressive game, and pay wins to the credit meter."
  note "Every reading matches 001D on the audit screen in accounting units."
}

test "13.30 attendant-paid progressive wins" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires machine.jackpot_limit, stimulus.handpay.reset, host.meter_arithmetic
  requires decoder.lp80, decoder.lp6F, decoder.lpAF, decoder.lp1B
  requires stimulus.bill
  machine accounting denomination 1c
  machine progressive sas group 1 levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  egm set jackpot limit 400
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 6F data 0400002000
  host expect field meters[0].code == $20
  host remember field meters[0].value as before
  host send lp AF data 0400002000
  host expect field meters[0].value == saved.before

  egm progressive win level=1
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host send lp 6F data 0400002000
  host expect field meters[0].value == saved.before + 500
  host send lp AF data 0400002000
  host expect field meters[0].value == saved.before + 500

  note "Before the run, clear the credit meter, set the jackpot limit low, turn partial pay off, enrol the machine in group 1 with level 1, and select the progressive game."
  note "Every reading matches 0020 on the audit screen in accounting units."
}
