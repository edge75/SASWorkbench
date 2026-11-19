# Chapter 18, sections 18.9 and 18.10 - the four tournament meters in one reply, and a session on
# each game of a multi-game machine.
#
# Long poll $99 carries all four tournament meters of one game in a single reply, in the order of
# $95 to $98, each four bytes of BCD after the game number. They must agree with the separate
# polls. On a multi-game machine a session names its game, and the meters are kept per game.
#
#   game 0, 10 minutes, 100 credits    8C 0000 1000 00000100 00
#   game 1, the same                   8C 0001 1000 00000100 00
#   all four meters of game 0          99 0000
#
# The software machine plays no tournaments and this build decodes none of the meter replies, so
# both cases skip; the field names are provisional and the amounts assume one game credit per
# accounting unit.

test "18.9 the meter group agrees with the four separate meters" {
  tag @egm-test-scripts @chapter-18 @needs-stimulus
  requires machine.tournament, stimulus.play
  requires decoder.lp95, decoder.lp96, decoder.lp97, decoder.lp98, decoder.lp99

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 8C data 000010000000010000
  host send lp 99 data 0000
  host expect field game_number == 0
  host expect field games_played == 0
  host expect field games_won == 0
  host expect field credits_wagered == 0
  host expect field credits_won == 0

  # The hundred credits spent, one game winning back its bet.
  egm play games=1 bet=10 win=10
  egm play games=10 bet=10 win=0
  host send lp 95 data 0000
  host expect field games_played == 11
  host remember field games_played as played
  host send lp 96 data 0000
  host expect field games_won == 1
  host remember field games_won as won
  host send lp 97 data 0000
  host expect field credits_wagered == 110
  host remember field credits_wagered as wagered
  host send lp 98 data 0000
  host expect field credits_won == 10
  host remember field credits_won as credits_won

  host send lp 99 data 0000
  host expect field game_number == 0
  host expect field games_played == saved.played
  host expect field games_won == saved.won
  host expect field credits_wagered == saved.wagered
  host expect field credits_won == saved.credits_won

  host send lp 8C data 000000000000000000

  note "Before the run, turn tournaments on at the machine and leave it idle."
  note "Not run: the meter group with a broken check sequence must be ignored, or refused with the machine's address with its top bit set (host.bad_crc)."
}

test "18.10 a session on each game of a multi-game machine" {
  tag @egm-test-scripts @chapter-18 @needs-stimulus @needs-a-person
  requires machine.tournament, machine.multiple_games, stimulus.select_game
  requires stimulus.play, stimulus.bill
  requires decoder.lp51, decoder.lp56
  requires decoder.lp95, decoder.lp96, decoder.lp97, decoder.lp98, decoder.lp99

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 51
  host expect field games >= 1
  host remember field games as implemented
  host send lp 56
  host expect field count >= 1
  host expect field count <= saved.implemented
  # The session below names game one, so game one has to be enabled and listed first.
  host expect field games[0].game_number == 1

  # The machine as a whole: a hundred credits spent, then the player's own money.
  host send lp 8C data 000010000000010000
  host send lp 99 data 0000
  host expect field games_played == 0
  host expect field games_won == 0
  host expect field credits_wagered == 0
  host expect field credits_won == 0
  egm play games=1 bet=10 win=10
  egm play games=10 bet=10 win=0
  host send lp 99 data 0000
  host expect field games_played == 11
  host expect field games_won == 1
  host expect field credits_wagered == 110
  host expect field credits_won == 10
  egm bill insert 2000
  egm play games=2 bet=5 win=0
  host send lp 99 data 0000
  host expect field games_played == 11
  host expect field credits_wagered == 110

  # Game one on its own.
  host send lp 8C data 000110000000010000
  host send lp 95 data 0001
  host expect field game_number == 1
  host expect field games_played == 0
  host send lp 96 data 0001
  host expect field games_won == 0
  host send lp 97 data 0001
  host expect field credits_wagered == 0
  host send lp 98 data 0001
  host expect field credits_won == 0
  host send lp 99 data 0001
  host expect field game_number == 1
  host expect field games_played == 0
  egm play games=1 bet=10 win=10
  egm play games=10 bet=10 win=0
  host send lp 99 data 0001
  host expect field games_played == 11
  host expect field games_won == 1
  host expect field credits_wagered == 110
  host expect field credits_won == 10
  egm play games=2 bet=5 win=0
  host send lp 99 data 0001
  host expect field games_played == 11
  host expect field credits_wagered == 110

  note "Before the run, turn progressives off, give every game the same player denomination, turn tournaments on, and select game one at the machine before its session starts."
  note "By hand, for every other game in the enabled list: change the game number in the session poll and in the five meter polls, select that game, and repeat the game-one block. Stop once as many games as the machine implements have been through it."
  note "The separate meter polls ($95 to $98) answer with the same counts as $99 at each read; compare them in the trace."
}
