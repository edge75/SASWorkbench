# Chapter 7, sections 7.18 to 7.24 - the per-game meters that a win moves, split by who paid it
# and whether it was a progressive.
#
# The same experiment as 7.12 to 7.17 (see that file's header). The meters here also need the win to
# arrive by a particular route - paid by the machine, paid by an attendant, or a progressive - so
# the progressive cases declare a non-SAS level and the attendant-paid one a jackpot limit below it.
#
# The document numbers no 7.20 and no 7.23; the numbering below follows it.

test "7.18 each game's machine-paid paytable wins add up to the machine's" {
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

  host send lp 6F data 0400001C00
  host remember field meters[0].value as total_before
  host send lp AF data 0400001C00
  host expect field meters[0].value == saved.total_before
  host send lp 6F data 0400011C00
  host remember field meters[0].value as game1_before
  host send lp AF data 0400011C00
  host expect field meters[0].value == saved.game1_before
  host send lp 6F data 0400021C00
  host remember field meters[0].value as game2_before
  host send lp AF data 0400021C00
  host expect field meters[0].value == saved.game2_before

  egm select game 1
  egm play games=1 bet=1 win=10
  egm select game 2
  egm play games=1 bet=2 win=10

  host send lp 6F data 0400011C00
  host remember field meters[0].value as game1_after
  host send lp AF data 0400011C00
  host expect field meters[0].value == saved.game1_after
  host send lp 6F data 0400021C00
  host remember field meters[0].value as game2_after
  host send lp AF data 0400021C00
  host expect field meters[0].value == saved.game2_after
  host send lp 6F data 0400001C00
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before
  host send lp AF data 0400001C00
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before

  # The game number one past the implemented count.
  host expect no reply to lp 6F data 0400031C00
  host expect no reply to lp AF data 0400031C00

  note "Meter 001C, moved by a paytable win the machine pays itself in each enabled game. Before the run, turn progressives off and enable exactly two games, at the same player denomination; with more, repeat the case for each further pair."
}

test "7.19 each game's machine-paid progressive wins add up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  machine games 1c 1c
  machine progressive non_sas levels 1
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires machine.multiple_games, machine.progressives, stimulus.bill, stimulus.play.game, stimulus.select_game, stimulus.progressive
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

  note "Meter 001D, moved by a progressive the machine pays itself in each progressive game. Before the run, configure a non-SAS progressive at level 1 for every enabled game. Before the run, turn progressives off and enable exactly two games, at the same player denomination; with more, repeat the case for each further pair."
}

test "7.21 each game's attendant-paid paytable wins add up to the machine's" {
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

  host send lp 6F data 0400001F00
  host remember field meters[0].value as total_before
  host send lp AF data 0400001F00
  host expect field meters[0].value == saved.total_before
  host send lp 6F data 0400011F00
  host remember field meters[0].value as game1_before
  host send lp AF data 0400011F00
  host expect field meters[0].value == saved.game1_before
  host send lp 6F data 0400021F00
  host remember field meters[0].value as game2_before
  host send lp AF data 0400021F00
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

  host send lp 6F data 0400011F00
  host remember field meters[0].value as game1_after
  host send lp AF data 0400011F00
  host expect field meters[0].value == saved.game1_after
  host send lp 6F data 0400021F00
  host remember field meters[0].value as game2_after
  host send lp AF data 0400021F00
  host expect field meters[0].value == saved.game2_after
  host send lp 6F data 0400001F00
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before
  host send lp AF data 0400001F00
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before

  # The game number one past the implemented count.
  host expect no reply to lp 6F data 0400031F00
  host expect no reply to lp AF data 0400031F00

  note "Meter 001F, moved by a jackpot an attendant pays in each enabled game, read with long poll 1B and reset. Before the run, turn progressives off and enable exactly two games, at the same player denomination; with more, repeat the case for each further pair."
}

test "7.22 each game's attendant-paid progressive wins add up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  machine games 1c 1c
  machine progressive non_sas levels 1
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires machine.multiple_games, machine.progressives, machine.jackpot_limit, stimulus.bill, stimulus.play.game, stimulus.select_game
  requires stimulus.progressive, stimulus.handpay.reset, decoder.lp1B
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

  note "Meter 0020, moved by a progressive large enough to need an attendant in each progressive game, read with long poll 1B and reset. Before the run, configure a non-SAS progressive at level 1 for every enabled game and a jackpot limit below its value. Before the run, turn progressives off and enable exactly two games, at the same player denomination; with more, repeat the case for each further pair."
}

test "7.24 each game's total won adds up to the machine's" {
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

  host send lp 6F data 0400002200
  host remember field meters[0].value as total_before
  host send lp AF data 0400002200
  host expect field meters[0].value == saved.total_before
  host send lp 6F data 0400012200
  host remember field meters[0].value as game1_before
  host send lp AF data 0400012200
  host expect field meters[0].value == saved.game1_before
  host send lp 6F data 0400022200
  host remember field meters[0].value as game2_before
  host send lp AF data 0400022200
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

  host send lp 6F data 0400012200
  host remember field meters[0].value as game1_after
  host send lp AF data 0400012200
  host expect field meters[0].value == saved.game1_after
  host send lp 6F data 0400022200
  host remember field meters[0].value as game2_after
  host send lp AF data 0400022200
  host expect field meters[0].value == saved.game2_after
  host send lp 6F data 0400002200
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before
  host send lp AF data 0400002200
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.game1_after - saved.game1_before + saved.game2_after - saved.game2_before

  # The game number one past the implemented count.
  host expect no reply to lp 6F data 0400032200
  host expect no reply to lp AF data 0400032200

  note "Meter 0022, moved by a jackpot in each enabled game, read with long poll 1B and reset. Before the run, turn progressives off and enable exactly two games, at the same player denomination; with more, repeat the case for each further pair."
}
