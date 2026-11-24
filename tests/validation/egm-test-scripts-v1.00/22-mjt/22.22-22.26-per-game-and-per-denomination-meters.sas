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
  requires machine.legacy_bonus, machine.multiple_denominations
  requires stimulus.select_denomination, host.meter_arithmetic
  requires decoder.lpB2, decoder.lpB0, decoder.lp6F, decoder.event7C

  note "Enable up to twenty player denominations, one per game on a multi-game machine, and read them back ($B2)."
  note "For each denomination, lowest first: note 001E for the machine and for that denomination (through the $B0 preamble on $6F). Select a game at that denomination and win once at three times deductible and once at four times non-deductible, as in 22.22. Both readings grow by the same amount."
  note "Repeat with further sets of twenty until every denomination has been through. Run only on a multi-denomination machine (9.1). The document reads the denominations back with a key named for another poll; $B2 is meant."
}

test "22.26 attendant-paid external bonus by denomination adds up" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  machine multiplied jackpot on
  requires machine.legacy_bonus, machine.multiple_denominations
  requires machine.jackpot_limit, stimulus.select_denomination, stimulus.handpay.reset
  requires host.meter_arithmetic, decoder.lpB2, decoder.lpB0, decoder.lp6F, decoder.lp1B
  requires decoder.event7C

  note "Enable up to twenty player denominations, set the jackpot limit low with partial pay off, and read the denominations back ($B2)."
  note "For each denomination, lowest first: note 0021 for the machine and for that denomination (through $B0 on $6F). Enable three times deductible, select a game at that denomination and play until a win goes to a hand pay, as in 22.24. Both readings grow by the same amount."
  note "Repeat until every denomination has been through, and disable at the end. Run only on a multi-denomination machine (9.1). As in 22.24, the document labels the multiplier non-deductible against a deductible enable."
}
