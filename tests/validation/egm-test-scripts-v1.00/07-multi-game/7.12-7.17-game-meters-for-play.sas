# Chapter 7, sections 7.12 to 7.17 - the per-game meters that playing moves.
#
# Every case in this file and the next is the same experiment on a different meter: read the meter
# for each enabled game and for the machine as a whole, play every enabled game, read them all
# again, and check that the per-game movements add up to the machine's movement exactly. A machine
# that charged every game to game one, or counted a game twice, passes any one-game reading and
# fails this sum. Each case then asks for that meter for a game number one past the last
# implemented game, which the machine must not answer.
#
# The language has no loops, so each case names its games: two enabled games, declared for the
# software machine, each read with 6F and AF, the movements added with remembered values
# (host.game_meter_delta) and the player choosing the game played (stimulus.play.game). The 6F and
# AF polls carry their own length, which the host checks before it asserts their silence
# (host.length_prefixed_silence).
#
# The document enables at most twenty games at a time and repeats for any more; every machine this
# project has seen fits in one round.

test "7.12 each game's coin in adds up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  machine games 1c 1c
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires machine.multiple_games, stimulus.bill, stimulus.play.game, stimulus.select_game
  requires host.game_meter_delta, host.length_prefixed_silence

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm bill insert 2000
  host forget exceptions within 2s

  # Two games enabled, so the sum below covers every one of them.
  host send lp 51
  host expect field games of lp 51 == 2
  host send lp 56
  host expect field count == 2

  host send lp 6F data 0400000000
  host remember field meters[0].value as total_before
  host send lp AF data 0400000000
  host expect field meters[0].value == saved.total_before
  host send lp 6F data 0400010000
  host remember field meters[0].value as game1_before
  host send lp AF data 0400010000
  host expect field meters[0].value == saved.game1_before
  host send lp 6F data 0400020000
  host remember field meters[0].value as game2_before
  host send lp AF data 0400020000
  host expect field meters[0].value == saved.game2_before

  egm select game 1
  egm play games=1 bet=2 win=0
  egm select game 2
  egm play games=1 bet=3 win=0

  host send lp 6F data 0400010000
  host remember field meters[0].value as game1_after
  host send lp AF data 0400010000
  host expect field meters[0].value == saved.game1_after
  host send lp 6F data 0400020000
  host remember field meters[0].value as game2_after
  host send lp AF data 0400020000
  host expect field meters[0].value == saved.game2_after
  host send lp 6F data 0400000000
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before
  host send lp AF data 0400000000
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before

  # The game number one past the implemented count.
  host expect no reply to lp 6F data 0400030000
  host expect no reply to lp AF data 0400030000

  note "Meter 0000, moved by one losing game in each enabled game. Before the run, turn progressives off and enable exactly two games, at the same player denomination; with more, repeat the case for each further pair."
}

test "7.13 each game's coin out adds up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  machine games 1c 1c
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires machine.multiple_games, stimulus.bill, stimulus.play.game, stimulus.select_game
  requires host.game_meter_delta, host.length_prefixed_silence

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm bill insert 2000
  host forget exceptions within 2s

  # Two games enabled, so the sum below covers every one of them.
  host send lp 51
  host expect field games of lp 51 == 2
  host send lp 56
  host expect field count == 2

  host send lp 6F data 0400000100
  host remember field meters[0].value as total_before
  host send lp AF data 0400000100
  host expect field meters[0].value == saved.total_before
  host send lp 6F data 0400010100
  host remember field meters[0].value as game1_before
  host send lp AF data 0400010100
  host expect field meters[0].value == saved.game1_before
  host send lp 6F data 0400020100
  host remember field meters[0].value as game2_before
  host send lp AF data 0400020100
  host expect field meters[0].value == saved.game2_before

  egm select game 1
  egm play games=1 bet=1 win=10
  egm select game 2
  egm play games=1 bet=2 win=10

  host send lp 6F data 0400010100
  host remember field meters[0].value as game1_after
  host send lp AF data 0400010100
  host expect field meters[0].value == saved.game1_after
  host send lp 6F data 0400020100
  host remember field meters[0].value as game2_after
  host send lp AF data 0400020100
  host expect field meters[0].value == saved.game2_after
  host send lp 6F data 0400000100
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before
  host send lp AF data 0400000100
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before

  # The game number one past the implemented count.
  host expect no reply to lp 6F data 0400030100
  host expect no reply to lp AF data 0400030100

  note "Meter 0001, moved by one winning game in each enabled game. Before the run, turn progressives off and enable exactly two games, at the same player denomination; with more, repeat the case for each further pair."
}

test "7.14 each game's jackpot adds up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  machine games 1c 1c
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires machine.multiple_games, stimulus.bill, stimulus.select_game
  requires stimulus.handpay.jackpot, stimulus.handpay.reset, decoder.lp1B
  requires stimulus.play.game
  requires host.game_meter_delta, host.length_prefixed_silence

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

  host send lp 6F data 0400000200
  host remember field meters[0].value as total_before
  host send lp AF data 0400000200
  host expect field meters[0].value == saved.total_before
  host send lp 6F data 0400010200
  host remember field meters[0].value as game1_before
  host send lp AF data 0400010200
  host expect field meters[0].value == saved.game1_before
  host send lp 6F data 0400020200
  host remember field meters[0].value as game2_before
  host send lp AF data 0400020200
  host expect field meters[0].value == saved.game2_before

  egm select game 1
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount != 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  egm select game 2
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount != 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  host send lp 6F data 0400010200
  host remember field meters[0].value as game1_after
  host send lp AF data 0400010200
  host expect field meters[0].value == saved.game1_after
  host send lp 6F data 0400020200
  host remember field meters[0].value as game2_after
  host send lp AF data 0400020200
  host expect field meters[0].value == saved.game2_after
  host send lp 6F data 0400000200
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before
  host send lp AF data 0400000200
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before

  # The game number one past the implemented count.
  host expect no reply to lp 6F data 0400030200
  host expect no reply to lp AF data 0400030200

  note "Meter 0002, moved by a jackpot that locks the machine up in each enabled game, read with long poll 1B and reset. Before the run, turn progressives off and enable exactly two games, at the same player denomination; with more, repeat the case for each further pair."
}

test "7.15 each game's games played adds up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  machine games 1c 1c
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires machine.multiple_games, stimulus.bill, stimulus.play.game, stimulus.select_game
  requires host.game_meter_delta, host.length_prefixed_silence

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm bill insert 2000
  host forget exceptions within 2s

  # Two games enabled, so the sum below covers every one of them.
  host send lp 51
  host expect field games of lp 51 == 2
  host send lp 56
  host expect field count == 2

  host send lp 6F data 0400000500
  host remember field meters[0].value as total_before
  host send lp AF data 0400000500
  host expect field meters[0].value == saved.total_before
  host send lp 6F data 0400010500
  host remember field meters[0].value as game1_before
  host send lp AF data 0400010500
  host expect field meters[0].value == saved.game1_before
  host send lp 6F data 0400020500
  host remember field meters[0].value as game2_before
  host send lp AF data 0400020500
  host expect field meters[0].value == saved.game2_before

  egm select game 1
  egm play games=1 bet=1 win=0
  egm select game 2
  egm play games=2 bet=1 win=0

  host send lp 6F data 0400010500
  host remember field meters[0].value as game1_after
  host send lp AF data 0400010500
  host expect field meters[0].value == saved.game1_after
  host send lp 6F data 0400020500
  host remember field meters[0].value as game2_after
  host send lp AF data 0400020500
  host expect field meters[0].value == saved.game2_after
  host send lp 6F data 0400000500
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before
  host send lp AF data 0400000500
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before

  # The game number one past the implemented count.
  host expect no reply to lp 6F data 0400030500
  host expect no reply to lp AF data 0400030500

  note "Meter 0005: one game in the first enabled game and two in the second, so the two movements differ. Before the run, turn progressives off and enable exactly two games, at the same player denomination; with more, repeat the case for each further pair."
}

test "7.16 each game's games won adds up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  machine games 1c 1c
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires machine.multiple_games, stimulus.bill, stimulus.play.game, stimulus.select_game
  requires host.game_meter_delta, host.length_prefixed_silence

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm bill insert 2000
  host forget exceptions within 2s

  # Two games enabled, so the sum below covers every one of them.
  host send lp 51
  host expect field games of lp 51 == 2
  host send lp 56
  host expect field count == 2

  host send lp 6F data 0400000600
  host remember field meters[0].value as total_before
  host send lp AF data 0400000600
  host expect field meters[0].value == saved.total_before
  host send lp 6F data 0400010600
  host remember field meters[0].value as game1_before
  host send lp AF data 0400010600
  host expect field meters[0].value == saved.game1_before
  host send lp 6F data 0400020600
  host remember field meters[0].value as game2_before
  host send lp AF data 0400020600
  host expect field meters[0].value == saved.game2_before

  egm select game 1
  egm play games=1 bet=1 win=5
  egm select game 2
  egm play games=1 bet=2 win=5

  host send lp 6F data 0400010600
  host remember field meters[0].value as game1_after
  host send lp AF data 0400010600
  host expect field meters[0].value == saved.game1_after
  host send lp 6F data 0400020600
  host remember field meters[0].value as game2_after
  host send lp AF data 0400020600
  host expect field meters[0].value == saved.game2_after
  host send lp 6F data 0400000600
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before
  host send lp AF data 0400000600
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before

  # The game number one past the implemented count.
  host expect no reply to lp 6F data 0400030600
  host expect no reply to lp AF data 0400030600

  note "Meter 0006, moved by one winning game in each enabled game. Before the run, turn progressives off and enable exactly two games, at the same player denomination; with more, repeat the case for each further pair."
}

test "7.17 each game's games lost adds up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  machine games 1c 1c
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires machine.multiple_games, stimulus.bill, stimulus.play.game, stimulus.select_game
  requires host.game_meter_delta, host.length_prefixed_silence

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm bill insert 2000
  host forget exceptions within 2s

  # Two games enabled, so the sum below covers every one of them.
  host send lp 51
  host expect field games of lp 51 == 2
  host send lp 56
  host expect field count == 2

  host send lp 6F data 0400000700
  host remember field meters[0].value as total_before
  host send lp AF data 0400000700
  host expect field meters[0].value == saved.total_before
  host send lp 6F data 0400010700
  host remember field meters[0].value as game1_before
  host send lp AF data 0400010700
  host expect field meters[0].value == saved.game1_before
  host send lp 6F data 0400020700
  host remember field meters[0].value as game2_before
  host send lp AF data 0400020700
  host expect field meters[0].value == saved.game2_before

  egm select game 1
  egm play games=1 bet=1 win=0
  egm select game 2
  egm play games=1 bet=2 win=0

  host send lp 6F data 0400010700
  host remember field meters[0].value as game1_after
  host send lp AF data 0400010700
  host expect field meters[0].value == saved.game1_after
  host send lp 6F data 0400020700
  host remember field meters[0].value as game2_after
  host send lp AF data 0400020700
  host expect field meters[0].value == saved.game2_after
  host send lp 6F data 0400000700
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before
  host send lp AF data 0400000700
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before

  # The game number one past the implemented count.
  host expect no reply to lp 6F data 0400030700
  host expect no reply to lp AF data 0400030700

  note "Meter 0007, moved by one losing game in each enabled game. Before the run, turn progressives off and enable exactly two games, at the same player denomination; with more, repeat the case for each further pair."
}
