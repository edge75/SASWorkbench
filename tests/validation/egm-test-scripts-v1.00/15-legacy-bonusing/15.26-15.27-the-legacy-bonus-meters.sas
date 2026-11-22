# Chapter 15, sections 15.26 and 15.27 - the three legacy bonus meters.
#
# The machine keeps one meter per tax status - deductible, non-deductible and wager match, codes
# 00D7, 00D8 and 00D9 - and long poll $9A reports all three for a game. The cases read them both
# ways: through $9A, and through the selected-meter poll $6F under the codes the document gives.
#
# A bonus is paid to the machine as a whole, so on a multi-game machine the game being played
# decides which game's meters it lands on; 15.27 checks that the per-game meters add up to the
# machine's total.

# @finding: the $6F readings do not move. The library answers codes D7, D8 and D9 from its own
# validation meters (libsas.h:1489 to :1497, read at lp.cpp:4281) and keeps the three legacy bonus
# meters apart, where only $9A reads them (lp.cpp:9354).
test "15.26 each bonus lands on the meter for its tax status" {
  tag @egm-test-scripts @chapter-15 @needs-a-person @finding
  requires machine.legacy_bonus, decoder.lp9A, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 9A data 0000
  host remember field deductible as lp9A_deductible
  host remember field non_deductible as lp9A_non_deductible
  host remember field wager_match as lp9A_wager_match
  host send lp 6F data 080000D700D800D900
  host expect field meters[0].code == $D7
  host remember field meters[0].value as deductible
  host expect field meters[1].code == $D8
  host remember field meters[1].value as non_deductible
  host expect field meters[2].code == $D9
  host remember field meters[2].value as wager_match

  host send lp 8A data 0000000500
  host send lp 8A data 0000000601
  host send lp 8A data 0000000702

  host send lp 9A data 0000
  host expect field deductible == saved.lp9A_deductible + 5
  host expect field non_deductible == saved.lp9A_non_deductible + 6
  host expect field wager_match == saved.lp9A_wager_match + 7
  host send lp 6F data 080000D700D800D900
  host expect field meters[0].value == saved.deductible + 5
  host expect field meters[1].value == saved.non_deductible + 6
  host expect field meters[2].value == saved.wager_match + 7

  note "Before the run, enable legacy bonusing. The $9A reply carries game number 00 00 and the three meters as four-byte BCD in that order, in accounting credits; they must equal the $6F readings, and the machine's meter display (D40)."
  note "Not run: $9A sent with a broken check sequence must be ignored or refused (host.bad_crc)."
}

test "15.27 per-game legacy bonus meters add up" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.legacy_bonus, machine.multiple_games, stimulus.select_game
  requires decoder.lp9A, decoder.lp51, decoder.lp56, host.game_meter_delta

  note "Turn progressives off and enable every game, twenty at a time if there are more. Read the number of games ($51) and the enabled game numbers ($56)."
  note "For each enabled game, sum 00D7, 00D8 and 00D9 across all games with $9A, and note the machine totals. Select the game and send the three bonuses of 15.26: each is acknowledged, $7C arrives, and the credit meter grows by 18."
  note "Afterwards, for each of the three meters, the growth of the machine total less the growth of the per-game sum is zero."
  note "$9A for a game number one past the last implemented game is ignored."
}
