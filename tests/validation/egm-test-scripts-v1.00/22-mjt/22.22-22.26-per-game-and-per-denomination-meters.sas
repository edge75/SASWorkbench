# Chapter 22, sections 22.22 to 22.26 - the bonus meters a multiplied win moves, game by game and
# denomination by denomination.
#
# The extra part of a multiplied win lands on the meters of the game that won it and of the
# denomination it was played at, as well as the machine's. Each case sums a meter across every
# game, or reads it for one denomination, before and after, and the growth must equal the growth
# of the machine's own total. A per-game poll for a game number past the last implemented one is
# ignored.
#
# The software machine is neither multi-game nor multi-denomination, and this build cannot choose
# which game or denomination is played; all five skip.

test "22.22 legacy bonus meters by game add up" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  machine multiplied jackpot on
  requires machine.legacy_bonus, machine.multiple_games
  requires stimulus.select_game, host.game_meter_delta
  requires decoder.lp9A, decoder.lp51, decoder.lp56, decoder.event7C

  note "Turn progressives off and enable every game, twenty at a time if there are more. Read the number of games ($51) and the enabled game numbers ($56)."
  note "Sum 00D7 and 00D8 across every enabled game with $9A, and note the machine totals. For each enabled game, select it, enable three times deductible and play until a win: the credit meter grows by three times the win, $7C carries multiplier 3 and twice the win, and 00D7 grows by twice the win. Then enable four times non-deductible ($84) and win again: four times on the credit meter, $7C with $84 and three times the win, and 00D8 grows by three times the win."
  note "Afterwards, for each meter, the growth of the machine total less the growth of the per-game sum is zero. $9A for a game number one past the last implemented game is ignored."
}

test "22.23 machine-paid external bonus by game adds up" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  machine multiplied jackpot on
  requires machine.legacy_bonus, machine.multiple_games
  requires stimulus.select_game, host.game_meter_delta
  requires decoder.lp6F, decoder.lpAF, decoder.lp51, decoder.lp56, decoder.event7C

  note "Turn progressives off and enable up to twenty games at the same player denomination. Read the number of games ($51) and the enabled game numbers ($56)."
  note "Sum 001E across every enabled game with $6F and $AF, and note the machine total. For each enabled game, select it and win once at three times deductible and once at four times non-deductible, with the credit meter and $7C as in 22.22."
  note "Afterwards the growth of the machine total less the growth of the per-game sum is zero. $6F and $AF for a game number one past the last implemented game are ignored."
}

test "22.24 attendant-paid external bonus by game adds up" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  machine multiplied jackpot on
  requires machine.legacy_bonus, machine.multiple_games
  requires machine.jackpot_limit, stimulus.select_game, stimulus.handpay.reset
  requires host.game_meter_delta, decoder.lp6F, decoder.lpAF, decoder.lp51, decoder.lp56
  requires decoder.lp1B, decoder.event7C

  note "Turn progressives off, enable up to twenty games at the same player denomination, set the jackpot limit low with partial pay off, and read the games ($51, $56)."
  note "Sum 0021 across every enabled game with $6F and $AF, and note the machine total. For each enabled game, enable three times deductible, select it and play until a win goes to a hand pay: $51 and $7F with the game's win; the hand pay information ($1B) shows the jackpot with no partial pay; the attendant's reset brings $52 and $7C with multiplier 3 and twice the win. Disable at the end."
  note "Afterwards the growth of the machine total less the growth of the per-game sum is zero. $6F and $AF for a game number one past the last implemented game are ignored."
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

