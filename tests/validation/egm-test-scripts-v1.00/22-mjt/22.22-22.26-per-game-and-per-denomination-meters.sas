# Chapter 22, sections 22.22 to 22.26 - the bonus meters a multiplied win moves, game by game and
# denomination by denomination.
#
# The extra part of a multiplied win lands on the meters of the game that won it and of the
# denomination it was played at, as well as the machine's. Each case sums a meter across every
# game, or reads it for one denomination, before and after, and the growth must equal the growth
# of the machine's own total. A per-game poll for a game number past the last implemented one is
# ignored.
#
# The per-game cases declare two games and add each game's movement with remembered values
# (host.game_meter_delta); the per-denomination cases declare four games at four denominations.

test "22.22 legacy bonus meters by game add up" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  machine multiplied jackpot on
  # A game credit of one dollar is one accounting credit only at this unit, which every figure here assumes.
  machine accounting denomination 100c
  machine games 100c 100c
  requires machine.legacy_bonus, machine.multiple_games
  requires stimulus.select_game, host.game_meter_delta, stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.lp9A, decoder.lp51, decoder.lp56, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  # Two games enabled, so the sum below covers every one of them.
  host send lp 51
  host expect field games of lp 51 == 2
  host send lp 56
  host expect field count == 2

  host send lp 9A data 0000
  host remember field deductible as deductible_total_before
  host remember field non_deductible as non_deductible_total_before
  host send lp 9A data 0001
  host remember field deductible as deductible_game1_before
  host remember field non_deductible as non_deductible_game1_before
  host send lp 9A data 0002
  host remember field deductible as deductible_game2_before
  host remember field non_deductible as non_deductible_game2_before

  host forget exceptions within 2s
  egm select game 1
  host forget exceptions within 2s
  host send lp 8B data 0000000099999999030000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 20 within 8s
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host forget exceptions within 2s
  host send lp 8B data 0000000099999999840000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == $84 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 30 within 8s
  host send lp 8B data 0000000000000000010100

  host forget exceptions within 2s
  egm select game 2
  host forget exceptions within 2s
  host send lp 8B data 0000000099999999030000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 20 within 8s
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host forget exceptions within 2s
  host send lp 8B data 0000000099999999840000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == $84 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 30 within 8s
  host send lp 8B data 0000000000000000010100

  host send lp 9A data 0001
  host remember field deductible as deductible_game1_after
  host remember field non_deductible as non_deductible_game1_after
  host send lp 9A data 0002
  host remember field deductible as deductible_game2_after
  host remember field non_deductible as non_deductible_game2_after
  host send lp 9A data 0000
  host expect field deductible >= saved.deductible_total_before + 40
  host expect field deductible == saved.deductible_total_before + saved.deductible_game1_after - saved.deductible_game1_before + saved.deductible_game2_after - saved.deductible_game2_before
  host expect field non_deductible >= saved.non_deductible_total_before + 40
  host expect field non_deductible == saved.non_deductible_total_before + saved.non_deductible_game1_after - saved.non_deductible_game1_before + saved.non_deductible_game2_after - saved.non_deductible_game2_before

  # The game number one past the implemented count.
  host expect no reply to lp 9A data 0003

  note "Before the run, turn progressives off, enable legacy bonusing and multiplied jackpots, and enable exactly two games at the same player denomination; with more, repeat the case for each further pair."
  note "In each game a win at three times deductible puts twice the win on 00D7, and a win at four times non-deductible ($84) three times the win on 00D8; for each meter the growth of the machine's total less the growth of the per-game sum is zero."
}

test "22.23 machine-paid external bonus by game adds up" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  machine multiplied jackpot on
  # A game credit of one dollar is one accounting credit only at this unit, which every figure here assumes.
  machine accounting denomination 100c
  machine games 100c 100c
  requires machine.legacy_bonus, machine.multiple_games
  requires stimulus.select_game, host.game_meter_delta, stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.lp6F, decoder.lpAF, decoder.lp51, decoder.lp56, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  # Two games enabled, so the sum below covers every one of them.
  host send lp 51
  host expect field games of lp 51 == 2
  host send lp 56
  host expect field count == 2

  host send lp 6F data 0400001E00
  host remember field meters[0].value as meter_total_before
  host send lp AF data 0400001E00
  host expect field meters[0].value == saved.meter_total_before
  host send lp 6F data 0400011E00
  host remember field meters[0].value as meter_game1_before
  host send lp AF data 0400011E00
  host expect field meters[0].value == saved.meter_game1_before
  host send lp 6F data 0400021E00
  host remember field meters[0].value as meter_game2_before
  host send lp AF data 0400021E00
  host expect field meters[0].value == saved.meter_game2_before

  host forget exceptions within 2s
  egm select game 1
  host forget exceptions within 2s
  host send lp 8B data 0000000099999999030000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 20 within 8s
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host forget exceptions within 2s
  host send lp 8B data 0000000099999999840000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == $84 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 30 within 8s
  host send lp 8B data 0000000000000000010100

  host forget exceptions within 2s
  egm select game 2
  host forget exceptions within 2s
  host send lp 8B data 0000000099999999030000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 20 within 8s
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host forget exceptions within 2s
  host send lp 8B data 0000000099999999840000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == $84 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 30 within 8s
  host send lp 8B data 0000000000000000010100

  host send lp 6F data 0400011E00
  host remember field meters[0].value as meter_game1_after
  host send lp AF data 0400011E00
  host expect field meters[0].value == saved.meter_game1_after
  host send lp 6F data 0400021E00
  host remember field meters[0].value as meter_game2_after
  host send lp AF data 0400021E00
  host expect field meters[0].value == saved.meter_game2_after
  host send lp 6F data 0400001E00
  host expect field meters[0].value >= saved.meter_total_before + 100
  host expect field meters[0].value == saved.meter_total_before + saved.meter_game1_after - saved.meter_game1_before + saved.meter_game2_after - saved.meter_game2_before
  host send lp AF data 0400001E00
  host expect field meters[0].value >= saved.meter_total_before + 100
  host expect field meters[0].value == saved.meter_total_before + saved.meter_game1_after - saved.meter_game1_before + saved.meter_game2_after - saved.meter_game2_before

  # The game number one past the implemented count.
  host expect no reply to lp 6F data 0400031E00
  host expect no reply to lp AF data 0400031E00

  note "Before the run, turn progressives off, enable legacy bonusing and multiplied jackpots, and enable exactly two games at the same player denomination; with more, repeat the case for each further pair."
  note "In each game one win at three times deductible and one at four times non-deductible, as in 22.22; the growth of 001E for the machine less the growth of the per-game sum is zero."
}

test "22.24 attendant-paid external bonus by game adds up" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  machine multiplied jackpot on
  # A game credit of one dollar is one accounting credit only at this unit, which every figure here assumes.
  machine accounting denomination 100c
  machine games 100c 100c
  requires machine.legacy_bonus, machine.multiple_games
  requires machine.jackpot_limit, stimulus.select_game, stimulus.handpay.reset
  requires host.game_meter_delta, stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.lp6F, decoder.lpAF, decoder.lp51, decoder.lp56
  requires decoder.lp1B, decoder.event7C, decoder.event7F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  egm set jackpot limit 100000
  host forget exceptions within 2s

  # Two games enabled, so the sum below covers every one of them.
  host send lp 51
  host expect field games of lp 51 == 2
  host send lp 56
  host expect field count == 2

  host send lp 6F data 0400002100
  host remember field meters[0].value as meter_total_before
  host send lp AF data 0400002100
  host expect field meters[0].value == saved.meter_total_before
  host send lp 6F data 0400012100
  host remember field meters[0].value as meter_game1_before
  host send lp AF data 0400012100
  host expect field meters[0].value == saved.meter_game1_before
  host send lp 6F data 0400022100
  host remember field meters[0].value as meter_game2_before
  host send lp AF data 0400022100
  host expect field meters[0].value == saved.meter_game2_before

  host forget exceptions within 2s
  egm select game 1
  host forget exceptions within 2s
  host send lp 8B data 0000000099999999030000
  egm play games=1 bet=1 win=2000
  host expect exception handpay_is_pending within 10s
  host expect field win_amount of event game_ended != 0 within 8s
  host send lp 1B
  host expect field amount != 0
  host expect field partial_pay == 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 15s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 4000 within 15s
  host send lp 8B data 0000000000000000010100

  host forget exceptions within 2s
  egm select game 2
  host forget exceptions within 2s
  host send lp 8B data 0000000099999999030000
  egm play games=1 bet=1 win=2000
  host expect exception handpay_is_pending within 10s
  host expect field win_amount of event game_ended != 0 within 8s
  host send lp 1B
  host expect field amount != 0
  host expect field partial_pay == 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 15s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 4000 within 15s
  host send lp 8B data 0000000000000000010100

  host send lp 6F data 0400012100
  host remember field meters[0].value as meter_game1_after
  host send lp AF data 0400012100
  host expect field meters[0].value == saved.meter_game1_after
  host send lp 6F data 0400022100
  host remember field meters[0].value as meter_game2_after
  host send lp AF data 0400022100
  host expect field meters[0].value == saved.meter_game2_after
  host send lp 6F data 0400002100
  host expect field meters[0].value >= saved.meter_total_before + 8000
  host expect field meters[0].value == saved.meter_total_before + saved.meter_game1_after - saved.meter_game1_before + saved.meter_game2_after - saved.meter_game2_before
  host send lp AF data 0400002100
  host expect field meters[0].value >= saved.meter_total_before + 8000
  host expect field meters[0].value == saved.meter_total_before + saved.meter_game1_after - saved.meter_game1_before + saved.meter_game2_after - saved.meter_game2_before

  # The game number one past the implemented count.
  host expect no reply to lp 6F data 0400032100
  host expect no reply to lp AF data 0400032100

  note "Before the run, turn progressives off, enable legacy bonusing and multiplied jackpots, and enable exactly two games at the same player denomination; with more, repeat the case for each further pair. Switch partial pay off. The steps set the jackpot limit to $1,000, below the 2000-credit win."
  note "In each game a win at three times deductible goes to a hand pay; the reset brings $52 and $7C with multiplier 3 and twice the win. The growth of 0021 for the machine less the growth of the per-game sum is zero."
  note "The document labels the multiplier non-deductible here, though the enable it sends is deductible; this case expects deductible, plain 3."
}

test "22.25 machine-paid external bonus by denomination adds up" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  machine multiplied jackpot on
  machine games 1c 2c 5c 10c
  requires machine.legacy_bonus, machine.multiple_denominations
  requires stimulus.select_denomination, host.meter_arithmetic
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lpB2, decoder.lpB0, decoder.lp6F, decoder.lp0E, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  host send lp B2
  host expect field count of lp B2 >= 2

  # One cent: the machine's 001E and the denomination's, then a win at three times deductible and one
  # at four times non-deductible, as in 22.22.
  egm select denomination 1c
  host send lp 6F data 0400001E00
  host remember field meters[0].value as machine_before
  host send lp B0 data 07016F0400001E00
  host remember field meters[0].value as cent1_before

  host send lp 8B data 0000000000000000010100
  host send lp 8B data 0000000099999999030000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 20 within 8s
  host forget exceptions within 2s
  host send lp 8B data 0000000000000000010100
  host send lp 8B data 0000000099999999840000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == $84 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 30 within 8s
  host send lp 8B data 0000000000000000010100

  # Both readings grow by the same amount.
  host send lp 6F data 0400001E00
  host expect field meters[0].value == saved.machine_before + 50
  host send lp B0 data 07016F0400001E00
  host expect field meters[0].value == saved.cent1_before + 50

  note "Enable up to twenty player denominations, one per game on a multi-game machine; the steps take one cent. Repeat with every denomination, lowest first, and with further sets of twenty until every denomination has been through. Run only on a multi-denomination machine (9.1). The document reads the denominations back with a key named for another poll; $B2 is meant."
}
test "22.26 attendant-paid external bonus by denomination adds up" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  machine multiplied jackpot on
  machine games 1c 2c 5c 10c
  requires machine.legacy_bonus, machine.multiple_denominations
  requires machine.jackpot_limit, stimulus.select_denomination, stimulus.handpay.reset
  requires host.meter_arithmetic, decoder.lpB2, decoder.lpB0, decoder.lp6F, decoder.lp1B
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  egm set jackpot limit 1000
  host forget exceptions within 2s

  host send lp B2
  host expect field count of lp B2 >= 2

  # One cent: the machine's 0021 and the denomination's, then a three times deductible win paid by hand, as in 22.24.
  egm select denomination 1c
  host send lp 6F data 0400002100
  host remember field meters[0].value as machine_before
  host send lp B0 data 07016F0400002100
  host remember field meters[0].value as cent1_before

  host send lp 8B data 0000000000000000010100
  host send lp 8B data 0000000099999999030000
  egm play games=1 bet=1 win=2000
  host expect exception handpay_is_pending within 10s
  host expect field win_amount of event game_ended != 0 within 8s
  host send lp 1B
  host expect field amount != 0
  host expect field partial_pay == 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 15s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 4000 within 15s
  host send lp 8B data 0000000000000000010100

  # Both readings grow by the same amount.
  host send lp 6F data 0400002100
  host expect field meters[0].value == saved.machine_before + 4000
  host send lp B0 data 07016F0400002100
  host expect field meters[0].value == saved.cent1_before + 4000

  note "Enable up to twenty player denominations with the jackpot limit low and partial pay off; the steps take one cent. Repeat until every denomination has been through, lowest first. Run only on a multi-denomination machine (9.1). As in 22.24, the document labels the multiplier non-deductible against a deductible enable; this case expects deductible, plain 3."
}

