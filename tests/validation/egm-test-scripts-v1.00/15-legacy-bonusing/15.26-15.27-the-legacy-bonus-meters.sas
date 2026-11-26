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

# @finding: the per-game $9A meters never move. The platform writes only the machine's legacy bonus
# meters (QSP SasNetwork_AftTransfer.hpp:1745), and in the full manual meter mode it sets the library
# moves no game's meter on an award (libsas.cpp:2617), so $9A for a game answers zero.
test "15.27 per-game legacy bonus meters add up" {
  tag @egm-test-scripts @chapter-15 @needs-a-person @finding
  machine games 1c 1c
  requires machine.legacy_bonus, machine.multiple_games, stimulus.select_game
  requires decoder.lp9A, decoder.lp51, decoder.lp56, host.game_meter_delta

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # Two games enabled, so the sum below covers every one of them.
  host send lp 51
  host expect field games of lp 51 == 2
  host send lp 56
  host expect field count == 2

  host send lp 9A data 0000
  host remember field deductible as deductible_total_before
  host remember field non_deductible as non_deductible_total_before
  host remember field wager_match as wager_match_total_before
  host send lp 9A data 0001
  host remember field deductible as deductible_game1_before
  host remember field non_deductible as non_deductible_game1_before
  host remember field wager_match as wager_match_game1_before
  host send lp 9A data 0002
  host remember field deductible as deductible_game2_before
  host remember field non_deductible as non_deductible_game2_before
  host remember field wager_match as wager_match_game2_before

  host forget exceptions within 2s
  egm select game 1
  host send lp 8A data 0000000500
  host send lp 8A data 0000000601
  host send lp 8A data 0000000702
  host expect exception legacy_bonus_pay_or_jackpot_awarded within 8s

  host forget exceptions within 2s
  egm select game 2
  host send lp 8A data 0000000500
  host send lp 8A data 0000000601
  host send lp 8A data 0000000702
  host expect exception legacy_bonus_pay_or_jackpot_awarded within 8s

  host send lp 9A data 0001
  host remember field deductible as deductible_game1_after
  host remember field non_deductible as non_deductible_game1_after
  host remember field wager_match as wager_match_game1_after
  host send lp 9A data 0002
  host remember field deductible as deductible_game2_after
  host remember field non_deductible as non_deductible_game2_after
  host remember field wager_match as wager_match_game2_after
  host send lp 9A data 0000
  host expect field deductible >= saved.deductible_total_before + 10
  host expect field deductible == saved.deductible_total_before + saved.deductible_game1_after - saved.deductible_game1_before + saved.deductible_game2_after - saved.deductible_game2_before
  host expect field non_deductible >= saved.non_deductible_total_before + 10
  host expect field non_deductible == saved.non_deductible_total_before + saved.non_deductible_game1_after - saved.non_deductible_game1_before + saved.non_deductible_game2_after - saved.non_deductible_game2_before
  host expect field wager_match >= saved.wager_match_total_before + 10
  host expect field wager_match == saved.wager_match_total_before + saved.wager_match_game1_after - saved.wager_match_game1_before + saved.wager_match_game2_after - saved.wager_match_game2_before

  # The game number one past the implemented count.
  host expect no reply to lp 9A data 0003

  note "Before the run, turn progressives off and enable exactly two games at the same player denomination; with more, repeat the case for each further pair."
  note "In each game the three bonuses of 15.26 are acknowledged and $7C arrives; for each of the three meters, the growth of the machine's total less the growth of the per-game sum is zero."
}
