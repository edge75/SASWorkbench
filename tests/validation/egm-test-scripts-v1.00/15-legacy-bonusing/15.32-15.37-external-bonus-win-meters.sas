# Chapter 15, sections 15.32 to 15.37 - the two external bonus meters, for the machine, per game
# and per denomination.
#
# A legacy bonus paid by the machine lands on 001E; one too large for the jackpot limit, paid by an
# attendant, lands on 0021. Each is kept for the machine as a whole and for every game and every
# denomination, and the per-game and per-denomination meters must grow by exactly what the
# machine total grew by.
#
# The software machine sets no jackpot limit and is neither a multi-game nor a multi-denomination
# machine, so only 15.32 runs here.

test "15.32 a bonus paid by the machine is an external bonus win" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0600000C001E00
  host remember field meters[0].value as credits
  host remember field meters[1].value as external_bonus
  host send lp 8A data 0000000500
  host send lp 8A data 0000000601
  host send lp 8A data 0000000702
  host expect exception legacy_bonus_pay_or_jackpot_awarded within 8s
  host send lp 6F data 0600000C001E00
  host expect field meters[0].value == saved.credits + 18
  host expect field meters[1].value == saved.external_bonus + 18
  host send lp AF data 0600000C001E00
  host expect field meters[1].value == saved.external_bonus + 18

  note "Before the run, enable legacy bonusing, set the jackpot limit high, and leave the machine with nothing on the credit meter."
}

test "15.33 machine-paid bonuses by game add up" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, machine.multiple_games, stimulus.select_game
  requires decoder.lp51, decoder.lp56, host.game_meter_delta

  note "Turn progressives off, enable up to twenty games at the same player denomination, and read the number of games ($51) and the enabled game numbers ($56)."
  note "Sum 001E across all enabled games with $6F and $AF, and note the machine total. For each enabled game, select it and send the three bonuses: each is acknowledged, $7C arrives, and the credit meter and 001E grow by 18."
  note "Afterwards the growth of the machine total less the growth of the per-game sum is zero. Repeat with the next twenty games if there are more. $6F and $AF for a game number one past the last are ignored."
}

test "15.34 machine-paid bonuses by denomination add up" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB2, decoder.lpB0, host.meter_arithmetic

  note "Enable up to twenty player denominations, one per game on a multi-game machine, and read them back ($B2)."
  note "For each denomination, note 001E for the machine and for that denomination (through the $B0 preamble on $6F), select a game at that denomination, and send the three bonuses: acknowledged, $7C, eighteen credits. Both readings grow by the same amount."
  note "Repeat until every denomination has been through. Run only on a multi-denomination machine (9.1)."
}

test "15.35 a bonus paid by an attendant is an attendant external bonus" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, machine.jackpot_limit
  requires stimulus.handpay.reset, decoder.lp1B, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0400002100
  host remember field meters[0].value as attendant_bonus
  host send lp 8A data 0000050002
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount == 500
  host expect field partial_pay == 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect exception legacy_bonus_pay_or_jackpot_awarded within 15s
  host send lp 6F data 0400002100
  host expect field meters[0].value == saved.attendant_bonus + 500

  note "Before the run, enable legacy bonusing, set the jackpot limit below 500 credits and disable partial pay, with nothing on the credit meter."
  note "Then, by hand, with a partial pay of one coin: the meter grows by the bonus less that one credit (machine.partial_pay)."
}

test "15.36 attendant-paid bonuses by game add up" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, machine.multiple_games, machine.jackpot_limit, stimulus.select_game
  requires stimulus.handpay.reset, decoder.lp51, decoder.lp56, host.game_meter_delta

  note "As 15.33, with meter 0021 and one wager-match bonus above the jackpot limit per game: each locks the machine up, the hand pay information matches 15.10, and the reset brings $52 and $7C."
  note "Afterwards the growth of the machine total less the growth of the per-game sum is zero, and $6F and $AF for a game number one past the last are ignored."
}

test "15.37 attendant-paid bonuses by denomination add up" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, machine.multiple_denominations, machine.jackpot_limit
  requires stimulus.select_denomination, stimulus.handpay.reset
  requires decoder.lpB2, decoder.lpB0, host.meter_arithmetic

  note "As 15.34, with meter 0021 and one wager-match bonus above the jackpot limit per denomination, each paid by an attendant as in 15.35. Run only on a multi-denomination machine (9.1)."
}
