# Chapter 13, sections 13.31 to 13.35 - the progressive meters of one game and of one denomination.
#
# On a multi-game machine every game keeps its own 001D and 0020, and $83 answers their sum for the
# game asked about. A win on one game moves that game's meters and the machine's by the same
# amount, so the gains of all the games together equal the gain of the whole. The same holds per
# denomination on a multi-denomination machine, read through the $B0 preamble. A game number past
# the last one implemented gets no answer.
#
# These need a multi-game or multi-denomination machine enrolled in a group, a progressive won on
# it, and for the denomination cases the $B0 preamble. Written for game 1 at the one-cent
# accounting unit; the document repeats each
# for every enabled game or denomination, in sets of up to twenty.

test "13.31 cumulative progressive wins of one game" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  machine games 1c 2c 5c 10c
  machine progressive sas group 1 levels 1
  requires machine.sas_progressives, machine.multiple_games, stimulus.progressive
  requires host.progressive_broadcast, host.meter_arithmetic, stimulus.select_game, stimulus.bill
  requires decoder.lp51, decoder.lp56, decoder.lp80, decoder.lp6F, decoder.lpAF, decoder.lp83, decoder.lp85

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  host forget exceptions within 2s

  host send lp 51
  host expect field games of lp 51 >= 2
  host send lp 56
  host expect field count of lp 56 >= 1

  # Game 1: its own 001D and 0020, and $83 their sum.
  host send lp 6F data 0400011D00
  host expect field meters[0].code == $1D
  host remember field meters[0].value as machine_paid
  host send lp AF data 0400012000
  host expect field meters[0].code == $20
  host remember field meters[0].value as attendant_paid
  host send lp 83 with game_number=1
  host expect field game_number of lp 83 == 1
  host expect field cumulative_progressive_win of lp 83 == saved.machine_paid + saved.attendant_paid

  # Level 1 is won on game 1, paid to the credit meter.
  egm bill insert 1000
  egm select game 1
  egm progressive win level=1
  host expect exception sas_progressive_level_hit within 10s
  host send lp 83 with game_number=1
  host expect field cumulative_progressive_win of lp 83 == saved.machine_paid + saved.attendant_paid + 500
  host send lp 85
  host expect field level of lp 85 == 1
  host forget exceptions within 2s
  host expect no exception sas_progressive_level_hit within 20s

  note "Before the run, enrol the machine in group 1 with level 1 and enable up to twenty games, all at the same player denomination. Game 1 stands in for the first game $56 lists; repeat for every enabled game until every implemented game has been covered."
  note "The win also raises $54, or $51 when it is a hand pay; reading $1B then stops $51. The meter readings match the audit screen for that game."
}

test "13.32 machine-paid progressive wins add up across games" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  machine games 1c 2c 5c 10c
  machine progressive sas group 1 levels 1
  requires machine.sas_progressives, machine.multiple_games, stimulus.progressive
  requires host.progressive_broadcast, host.meter_arithmetic, stimulus.select_game, stimulus.bill
  requires decoder.lp51, decoder.lp56, decoder.lp80, decoder.lp6F, decoder.lpAF, decoder.lp85

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  host forget exceptions within 2s

  host send lp 51
  host expect field games of lp 51 >= 2
  host send lp 56
  host expect field count of lp 56 >= 1

  host send lp 6F data 0400001D00
  host remember field meters[0].value as machine_before
  host send lp 6F data 0400011D00
  host remember field meters[0].value as game_before

  # Level 1 is won on game 1, paid to the credit meter.
  egm bill insert 1000
  egm select game 1
  egm progressive win level=1
  host expect exception sas_progressive_level_hit within 10s
  host send lp 85
  host forget exceptions within 2s
  host expect no exception sas_progressive_level_hit within 20s

  # The game's gain is the machine's gain.
  host send lp 6F data 0400001D00
  host remember field meters[0].value as machine_after
  host expect field meters[0].value == saved.machine_before + 500
  host send lp 6F data 0400011D00
  host expect field meters[0].value == saved.game_before + saved.machine_after - saved.machine_before
  host send lp AF data 0400011D00
  host expect field meters[0].value == saved.game_before + saved.machine_after - saved.machine_before

  note "Before the run, enrol the machine in group 1 with level 1 and enable up to twenty games, all at the same player denomination. Win once on every enabled progressive game, then the sum of every game's gain equals the machine's gain; written here for one game. Repeat with further sets of games until all are covered."
  note "Then ask $6F and $AF for 001D of the game one past the number $51 reports: the machine does not answer."
}

test "13.33 attendant-paid progressive wins add up across games" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  machine games 1c 2c 5c 10c
  machine progressive sas group 1 levels 1
  requires machine.sas_progressives, machine.multiple_games, stimulus.progressive
  requires host.progressive_broadcast, host.meter_arithmetic, machine.jackpot_limit
  requires stimulus.handpay.reset, stimulus.select_game, stimulus.bill
  requires decoder.lp51, decoder.lp56, decoder.lp80, decoder.lp6F, decoder.lpAF, decoder.lp1B, decoder.lp85
  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  egm set jackpot limit 400
  host forget exceptions within 2s

  host send lp 51
  host expect field games of lp 51 >= 2
  host send lp 56
  host expect field count of lp 56 >= 1

  host send lp 6F data 0400002000
  host remember field meters[0].value as machine_before
  host send lp 6F data 0400012000
  host remember field meters[0].value as game_before

  # Level 1 is won on game 1, above the jackpot limit, so it is paid by hand.
  egm bill insert 1000
  egm select game 1
  egm progressive win level=1
  host expect exception sas_progressive_level_hit within 10s
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host send lp 85
  host forget exceptions within 2s
  host expect no exception sas_progressive_level_hit within 20s

  # The game's gain is the machine's gain.
  host send lp 6F data 0400002000
  host remember field meters[0].value as machine_after
  host expect field meters[0].value == saved.machine_before + 500
  host send lp 6F data 0400012000
  host expect field meters[0].value == saved.game_before + saved.machine_after - saved.machine_before
  host send lp AF data 0400012000
  host expect field meters[0].value == saved.game_before + saved.machine_after - saved.machine_before

  note "Before the run, set the jackpot limit low, enrol the machine in group 1 with level 1, enable up to twenty games at the same player denomination, and stop the host answering $51 with $1B on its own. Win a progressive hand pay on every enabled game; the sum of every game's gain equals the machine's gain. Repeat with further sets of games until all are covered."
  note "Then ask $6F and $AF for 0020 of the game one past the number $51 reports: the machine does not answer."
}

test "13.34 machine-paid progressive wins add up across denominations" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  machine games 1c 2c 5c 10c
  machine progressive sas group 1 levels 1
  requires machine.sas_progressives, machine.multiple_denominations, stimulus.progressive
  requires stimulus.select_denomination, host.progressive_broadcast, decoder.lpB0, decoder.lpB2
  requires host.meter_arithmetic, stimulus.bill, decoder.lp6F, decoder.lp80, decoder.lp85

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  host forget exceptions within 2s

  host send lp B2
  host expect field count of lp B2 >= 2

  # 1D for the machine and for one cent and five cents, game 0, through the $B0 preamble.
  host send lp 6F data 0400001D00
  host remember field meters[0].value as total_before
  host send lp B0 data 07016F0400001D00
  host remember field meters[0].value as cent1_before
  host send lp B0 data 07026F0400001D00
  host remember field meters[0].value as cent5_before

  # Level 1 is won at one cent; $85 is read until $56 stops.
  egm select denomination 1c
  egm bill insert 1000
  egm progressive win level=1
  host expect exception sas_progressive_level_hit within 10s
  host send lp 85
  host forget exceptions within 2s
  host expect no exception sas_progressive_level_hit within 20s

  # The denominations' gains add up to the machine's gain.
  host send lp B0 data 07016F0400001D00
  host remember field meters[0].value as cent1_after
  host send lp B0 data 07026F0400001D00
  host expect field meters[0].value == saved.cent5_before
  host remember field meters[0].value as cent5_after
  host send lp 6F data 0400001D00
  host expect field meters[0].value == saved.total_before + 500
  host expect field meters[0].value == saved.total_before + saved.cent1_after - saved.cent1_before + saved.cent5_after - saved.cent5_before

  note "Only for a multi-denomination machine. Enrol it in group 1 with level 1, enable up to twenty player denominations - a different one for each game on a multi-game machine - and set the jackpot limit high enough that nothing becomes a hand pay. The steps fix the set at one cent and five cents; win at every denomination and repeat with further sets until all are covered."
}

test "13.35 attendant-paid progressive wins add up across denominations" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  machine games 1c 2c 5c 10c
  machine progressive sas group 1 levels 1
  requires machine.sas_progressives, machine.multiple_denominations, stimulus.progressive
  requires stimulus.select_denomination, host.progressive_broadcast, decoder.lpB0, decoder.lpB2
  requires host.meter_arithmetic, stimulus.bill, decoder.lp6F, decoder.lp80, decoder.lp85, machine.jackpot_limit
  requires stimulus.handpay.reset, decoder.lp1B

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host broadcast lp 80 with progressive_group=1 level=1 amount=500 every 1s
  egm set jackpot limit 400
  host forget exceptions within 2s

  host send lp B2
  host expect field count of lp B2 >= 2

  # 20 for the machine and for one cent and five cents, game 0, through the $B0 preamble.
  host send lp 6F data 0400002000
  host remember field meters[0].value as total_before
  host send lp B0 data 07016F0400002000
  host remember field meters[0].value as cent1_before
  host send lp B0 data 07026F0400002000
  host remember field meters[0].value as cent5_before

  # Level 1 is won at one cent, above the jackpot limit, so it is paid by hand; $85 is read until $56 stops.
  egm select denomination 1c
  egm bill insert 1000
  egm progressive win level=1
  host expect exception sas_progressive_level_hit within 10s
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host send lp 85
  host forget exceptions within 2s
  host expect no exception sas_progressive_level_hit within 20s

  # The denominations' gains add up to the machine's gain.
  host send lp B0 data 07016F0400002000
  host remember field meters[0].value as cent1_after
  host send lp B0 data 07026F0400002000
  host expect field meters[0].value == saved.cent5_before
  host remember field meters[0].value as cent5_after
  host send lp 6F data 0400002000
  host expect field meters[0].value == saved.total_before + 500
  host expect field meters[0].value == saved.total_before + saved.cent1_after - saved.cent1_before + saved.cent5_after - saved.cent5_before

  note "Only for a multi-denomination machine. Enrol it in group 1 with level 1 and enable up to twenty player denominations. The document sets the jackpot limit high here, as in 13.34, yet needs every win to be a hand pay; set it low. The steps fix the set at one cent and five cents; win at every denomination and repeat with further sets until all are covered."
}
