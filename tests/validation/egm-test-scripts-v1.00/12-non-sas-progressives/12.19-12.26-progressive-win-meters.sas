# Chapter 12, sections 12.19 to 12.26 - the meters that count progressive wins.
#
# Two meters split progressive wins by who paid them, 001D for the machine and 0020 for an
# attendant, and long poll $83 reports the two added together, in accounting credits, for the
# machine or for one game. The multi-game and multi-denomination cases add that each game's or
# each denomination's movement sums to the machine's, and that a game number past the implemented
# count goes unanswered.
#
# 12.19 to 12.21 run on the software machine, built with the declared non-SAS level
# (machine.progressives) and winning it with 'egm progressive win' (stimulus.progressive), paid to
# its credit meter or, below a jackpot limit the case sets, by hand; the rest need several games or
# several denominations.
# The per-game cases declare two games and add each game's movement with remembered values
# (host.game_meter_delta). Every meter must also match the machine's own display, which is a
# person's to compare (D40).

test "12.19 cumulative progressive wins are the two progressive meters together" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, stimulus.handpay.reset, stimulus.bill
  requires decoder.lp6F, decoder.lpAF, decoder.lp83, decoder.lp1B, host.meter_arithmetic
  machine progressive non_sas levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 6F data 0600001D002000
  host remember field meters[0].value as machine_paid
  host remember field meters[1].value as attendant_paid
  host send lp AF data 0600001D002000
  host expect field meters[0].value == saved.machine_paid
  host expect field meters[1].value == saved.attendant_paid

  host send lp 83 with game_number=0
  host expect field game_number of lp 83 == 0
  host expect field cumulative_progressive_win of lp 83 == saved.machine_paid + saved.attendant_paid
  host remember field cumulative_progressive_win as cumulative

  egm progressive win level=1
  host send lp 83 with game_number=0
  host expect field cumulative_progressive_win of lp 83 > saved.cumulative

  note "Before the run, configure the machine for a non-SAS progressive at level 1 and select the progressive game."
  note "The total grows by exactly the win in accounting credits for the machine as a whole. A win paid by the machine raises $54, one paid by hand $51, which stops once the hand pay is read."
  note "The document finally sends the same poll with a broken check sequence: it is ignored or refused. This build cannot corrupt a check sequence on request (host.bad_crc)."
}

test "12.20 a progressive paid by the machine counts on meter 001D" {
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

  host send lp 6F data 0400001D00
  host remember field meters[0].value as machine_paid
  host send lp AF data 0400001D00
  host expect field meters[0].value == saved.machine_paid

  egm progressive win level=1
  host expect exception progressive_win within 30s
  host send lp 6F data 0400001D00
  host expect field meters[0].value > saved.machine_paid
  host remember field meters[0].value as machine_paid
  host send lp AF data 0400001D00
  host expect field meters[0].value == saved.machine_paid

  note "Before the run, configure the machine for a non-SAS progressive at level 1, select the progressive game, and leave nothing on the credit meter."
  note "The meter grows by exactly the win noted."
}

test "12.21 a progressive paid by hand counts on meter 0020" {
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

  host send lp 6F data 0400002000
  host remember field meters[0].value as attendant_paid
  host send lp AF data 0400002000
  host expect field meters[0].value == saved.attendant_paid

  egm progressive win level=1
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host send lp 6F data 0400002000
  host expect field meters[0].value > saved.attendant_paid
  host remember field meters[0].value as attendant_paid
  host send lp AF data 0400002000
  host expect field meters[0].value == saved.attendant_paid

  note "Before the run, configure the machine for a non-SAS progressive at level 1, a jackpot limit below the level's value and no partial pay, select the progressive game, and leave nothing on the credit meter."
  note "The meter grows by exactly the amount noted."
}

test "12.22 each game's cumulative progressive wins" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, machine.multiple_games, stimulus.play.game, stimulus.progressive
  requires stimulus.handpay.reset, decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF, decoder.lp83, decoder.lp1B
  requires host.meter_arithmetic, stimulus.select_game, stimulus.bill
  machine games 1c 1c
  machine progressive non_sas levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm bill insert 1000
  host forget exceptions within 2s

  host send lp 51
  host send lp 56
  host expect field count >= 1

  host send lp 6F data 0600011D002000
  host remember field meters[0].value as machine_paid
  host remember field meters[1].value as attendant_paid
  host send lp AF data 0600011D002000
  host expect field meters[0].value == saved.machine_paid
  host expect field meters[1].value == saved.attendant_paid

  host send lp 83 with game_number=1
  host expect field game_number of lp 83 == 1
  host expect field cumulative_progressive_win of lp 83 == saved.machine_paid + saved.attendant_paid
  host remember field cumulative_progressive_win as cumulative

  egm select game 1
  egm progressive win level=1
  host expect exception progressive_win within 30s
  host send lp 83 with game_number=1
  host expect field game_number of lp 83 == 1
  host expect field cumulative_progressive_win of lp 83 > saved.cumulative

  note "Before the run, configure the machine for a non-SAS progressive at level 1 with its jackpot limit low and partial pay off, enable up to twenty games at one player denomination, and leave nothing on the credit meter. The steps name game 1; use the first game in the enabled list."
  note "The game's total grows by exactly the win. Repeat the whole case for every other game in the enabled list until every implemented game has been enabled and tested."
}

test "12.23 each game's machine-paid progressive wins add up to the machine's" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  machine games 1c 1c
  machine progressive non_sas levels 1
  requires machine.progressives, machine.multiple_games, stimulus.play.game, stimulus.select_game, stimulus.progressive, stimulus.bill
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires host.game_meter_delta, host.length_prefixed_silence

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm bill insert 1000
  host forget exceptions within 2s

  # Two games enabled, so the sum below covers every one of them.
  host send lp 51
  host expect field games of lp 51 == 2
  host send lp 56
  host expect field count == 2

  host send lp 6F data 0400001D00
  host remember field meters[0].value as total_before
  host send lp AF data 0400001D00
  host expect field meters[0].value == saved.total_before
  host send lp 6F data 0400011D00
  host remember field meters[0].value as game1_before
  host send lp AF data 0400011D00
  host expect field meters[0].value == saved.game1_before
  host send lp 6F data 0400021D00
  host remember field meters[0].value as game2_before
  host send lp AF data 0400021D00
  host expect field meters[0].value == saved.game2_before

  egm select game 1
  egm progressive win level=1
  host expect exception progressive_win within 30s
  egm select game 2
  egm progressive win level=1
  host expect exception progressive_win within 30s

  host send lp 6F data 0400011D00
  host remember field meters[0].value as game1_after
  host send lp AF data 0400011D00
  host expect field meters[0].value == saved.game1_after
  host send lp 6F data 0400021D00
  host remember field meters[0].value as game2_after
  host send lp AF data 0400021D00
  host expect field meters[0].value == saved.game2_after
  host send lp 6F data 0400001D00
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before
  host send lp AF data 0400001D00
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before

  # The game number one past the implemented count.
  host expect no reply to lp 6F data 0400031D00
  host expect no reply to lp AF data 0400031D00

  note "Before the run, configure the machine for a non-SAS progressive at level 1 and enable exactly two progressive games at one player denomination; with more, repeat for each further pair until every implemented game has been tested."
  note "Meter 001D, moved by a progressive the machine pays itself in each game. This is the same experiment as 7.19."
}

test "12.24 each game's attendant-paid progressive wins add up to the machine's" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  machine games 1c 1c
  machine progressive non_sas levels 1
  requires machine.progressives, machine.multiple_games, machine.jackpot_limit, stimulus.play.game, stimulus.select_game, stimulus.progressive, stimulus.bill
  requires stimulus.handpay.reset, decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF, decoder.lp1B
  requires host.game_meter_delta, host.length_prefixed_silence

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm set jackpot limit 500
  egm bill insert 1000
  host forget exceptions within 2s

  # Two games enabled, so the sum below covers every one of them.
  host send lp 51
  host expect field games of lp 51 == 2
  host send lp 56
  host expect field count == 2

  host send lp 6F data 0400002000
  host remember field meters[0].value as total_before
  host send lp AF data 0400002000
  host expect field meters[0].value == saved.total_before
  host send lp 6F data 0400012000
  host remember field meters[0].value as game1_before
  host send lp AF data 0400012000
  host expect field meters[0].value == saved.game1_before
  host send lp 6F data 0400022000
  host remember field meters[0].value as game2_before
  host send lp AF data 0400022000
  host expect field meters[0].value == saved.game2_before

  egm select game 1
  egm progressive win level=1
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  egm select game 2
  egm progressive win level=1
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  host send lp 6F data 0400012000
  host remember field meters[0].value as game1_after
  host send lp AF data 0400012000
  host expect field meters[0].value == saved.game1_after
  host send lp 6F data 0400022000
  host remember field meters[0].value as game2_after
  host send lp AF data 0400022000
  host expect field meters[0].value == saved.game2_after
  host send lp 6F data 0400002000
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before
  host send lp AF data 0400002000
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before

  # The game number one past the implemented count.
  host expect no reply to lp 6F data 0400032000
  host expect no reply to lp AF data 0400032000

  note "Before the run, configure the machine for a non-SAS progressive at level 1, a jackpot limit below the level's value, and exactly two progressive games at one player denomination; with more, repeat for each further pair."
  note "Meter 0020, moved by the level won as a hand pay in each game, read with long poll 1B and reset. This is the same experiment as 7.22."
}

test "12.25 each denomination's machine-paid progressive wins add up to the machine's" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  machine games 1c 2c 5c 10c
  machine progressive non_sas levels 1
  requires machine.progressives, machine.multiple_denominations, stimulus.select_denomination
  requires stimulus.progressive, decoder.lpB0, decoder.lpB2, host.meter_arithmetic
  requires stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp 6F data 0400001D00
  host remember field meters[0].value as total_before
  host send lp B0 data 07016F0400001D00
  host remember field meters[0].value as cent1_before
  host send lp B0 data 07026F0400001D00
  host remember field meters[0].value as cent5_before

  egm select denomination 1c
  egm bill insert 2000
  egm progressive win level=1

  host send lp B0 data 07016F0400001D00
  host remember field meters[0].value as cent1_after
  host send lp B0 data 07026F0400001D00
  host expect field meters[0].value == saved.cent5_before
  host remember field meters[0].value as cent5_after
  host send lp 6F data 0400001D00
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.cent1_after - saved.cent1_before + saved.cent5_after - saved.cent5_before

  note "Before the run, configure the machine for a non-SAS progressive at level 1, enable up to twenty player denominations, give each game its own denomination on a multi-game machine, and set the jackpot limit high enough that the level never locks up."
  note "The steps fix the set at one cent and five cents, as 9.19 does. Repeat with every enabled denomination, and with further sets of up to twenty, until every player denomination has been tested. Run only on a machine that reports multi-denomination support (9.1)."
}

test "12.26 each denomination's attendant-paid progressive wins add up to the machine's" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  machine games 1c 2c 5c 10c
  machine progressive non_sas levels 1
  requires machine.progressives, machine.multiple_denominations, stimulus.select_denomination
  requires stimulus.progressive, machine.jackpot_limit, stimulus.handpay.reset
  requires decoder.lpB0, decoder.lpB2, decoder.lp1B, host.meter_arithmetic
  requires stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm set jackpot limit 500
  host forget exceptions within 2s

  host send lp B2
  host send lp 6F data 0400002000
  host remember field meters[0].value as total_before
  host send lp B0 data 07016F0400002000
  host remember field meters[0].value as cent1_before
  host send lp B0 data 07026F0400002000
  host remember field meters[0].value as cent5_before

  # Level 1 is won at one cent, above the jackpot limit, so it is paid by hand.
  egm select denomination 1c
  egm bill insert 2000
  egm progressive win level=1
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  host send lp B0 data 07016F0400002000
  host remember field meters[0].value as cent1_after
  host send lp B0 data 07026F0400002000
  host expect field meters[0].value == saved.cent5_before
  host remember field meters[0].value as cent5_after
  host send lp 6F data 0400002000
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.cent1_after - saved.cent1_before + saved.cent5_after - saved.cent5_before

  note "Before the run, configure the machine for a non-SAS progressive at level 1, enable up to twenty player denominations, and give each game its own denomination on a multi-game machine."
  note "The document's set-up asks for a jackpot limit high enough to prevent a hand pay, while its steps need one; the steps here set it below the level's value, which is the only way this meter moves."
  note "The steps fix the set at one cent and five cents, as 9.22 does. Repeat with every enabled denomination, and with further sets of up to twenty, until every player denomination has been tested. Run only on a machine that reports multi-denomination support (9.1)."
}
