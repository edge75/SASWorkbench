# Chapter 13, sections 13.31 to 13.35 - the progressive meters of one game and of one denomination.
#
# On a multi-game machine every game keeps its own 001D and 0020, and $83 answers their sum for the
# game asked about. A win on one game moves that game's meters and the machine's by the same
# amount, so the gains of all the games together equal the gain of the whole. The same holds per
# denomination on a multi-denomination machine, read through the $B0 preamble. A game number past
# the last one implemented gets no answer.
#
# These need a multi-game or multi-denomination machine enrolled in a group, a progressive won on
# it, and for the denomination cases the $B0 preamble, which this build cannot decode
# (decoder.lpB0). Written for game 1 at the one-cent accounting unit; the document repeats each
# for every enabled game or denomination, in sets of up to twenty.

test "13.31 cumulative progressive wins of one game" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  requires machine.sas_progressives, machine.multiple_games, stimulus.progressive
  requires host.progressive_broadcast, host.meter_arithmetic
  requires decoder.lp51, decoder.lp56, decoder.lp80, decoder.lp6F, decoder.lpAF, decoder.lp83, decoder.lp85
  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 80 with progressive_group=1 level=1 amount=500
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

  # A person wins level 1 on game 1 here; read $1B first if it is a hand pay.
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
  requires machine.sas_progressives, machine.multiple_games, stimulus.progressive
  requires host.progressive_broadcast, host.meter_arithmetic
  requires decoder.lp51, decoder.lp56, decoder.lp80, decoder.lp6F, decoder.lpAF, decoder.lp85
  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 80 with progressive_group=1 level=1 amount=500
  host forget exceptions within 2s

  host send lp 51
  host expect field games of lp 51 >= 2
  host send lp 56
  host expect field count of lp 56 >= 1

  host send lp 6F data 0400001D00
  host remember field meters[0].value as machine_before
  host send lp 6F data 0400011D00
  host remember field meters[0].value as game_before

  # A person wins level 1 on game 1 here, paid to the credit meter.
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
  requires machine.sas_progressives, machine.multiple_games, stimulus.progressive
  requires host.progressive_broadcast, host.meter_arithmetic, machine.jackpot_limit
  requires stimulus.handpay.reset
  requires decoder.lp51, decoder.lp56, decoder.lp80, decoder.lp6F, decoder.lpAF, decoder.lp1B, decoder.lp85
  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 80 with progressive_group=1 level=1 amount=500
  host forget exceptions within 2s

  host send lp 51
  host expect field games of lp 51 >= 2
  host send lp 56
  host expect field count of lp 56 >= 1

  host send lp 6F data 0400002000
  host remember field meters[0].value as machine_before
  host send lp 6F data 0400012000
  host remember field meters[0].value as game_before

  # A person wins level 1 on game 1 here, as a hand pay.
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
  requires machine.sas_progressives, machine.multiple_denominations, stimulus.progressive
  requires stimulus.select_denomination, host.progressive_broadcast, decoder.lpB0, decoder.lpB2

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host expect field count of lp B2 >= 2

  note "Only for a multi-denomination machine. Enrol it in group 1 with level 1, enable up to twenty player denominations - a different one for each game on a multi-game machine - and set the jackpot limit high enough that nothing becomes a hand pay."
  note "Read 001D for each denomination $B2 lists, game 0, with $6F and $AF through the $B0 preamble, and 001D for the machine. Win level 1 at every denomination, reading $85 each time until $56 stops. Read them all again: the sum of the denominations' gains equals the machine's gain. Repeat with further sets of denominations until all are covered."
}

test "13.35 attendant-paid progressive wins add up across denominations" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  requires machine.sas_progressives, machine.multiple_denominations, stimulus.progressive
  requires stimulus.select_denomination, host.progressive_broadcast, machine.jackpot_limit
  requires stimulus.handpay.reset, decoder.lpB0, decoder.lpB2

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host expect field count of lp B2 >= 2

  note "Only for a multi-denomination machine. Enrol it in group 1 with level 1 and enable up to twenty player denominations. The document sets the jackpot limit high here, as in 13.34, yet needs every win to be a hand pay; set it low."
  note "Read 0020 for each denomination $B2 lists, game 0, with $6F and $AF through the $B0 preamble, and 0020 for the machine. Win a progressive hand pay at every denomination: $51, then $52 once $1B is read and the hand pay keyed off, and $56 until $85 is read. Read them all again: the sum of the denominations' gains equals the machine's gain. Repeat with further sets of denominations until all are covered."
}
