# Chapter 18, sections 18.7 and 18.8 - ending a session from the host, and a machine that is busy
# when it is told to start one.
#
# The host ends a session with $8C carrying zero time and zero credits. A session with zero
# credits and a time is limited by time alone; one with zero time and some credits by credits
# alone. After the end, play moves none of the four tournament meters.
#
#   end the session                      8C 0000 0000 00000000 00
#   5 minutes, 50 credits                8C 0000 0500 00000050 00
#   5 minutes, time only                 8C 0000 0500 00000000 00
#   100 credits, no time limit           8C 0000 0000 00000100 00
#
# A machine told to start a session while a game, a tilt, a hand pay or an operator menu is in
# progress acknowledges at once but waits until that is over before it enters the session. The
# software machine plays no tournaments and this build decodes none of the meter replies, so both
# cases skip; the field names are provisional and the amounts assume one game credit per
# accounting unit.

test "18.7 the host ends a session, whatever limits it" {
  tag @egm-test-scripts @chapter-18 @needs-stimulus
  requires machine.tournament, stimulus.play, stimulus.bill, host.meter_arithmetic
  requires decoder.lp95, decoder.lp96, decoder.lp97, decoder.lp98

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  # Five minutes and fifty credits, all of them spent.
  host send lp 8C data 000005000000005000
  host send lp 95 data 0000
  host expect field games_played == 0
  host send lp 96 data 0000
  host expect field games_won == 0
  host send lp 97 data 0000
  host expect field credits_wagered == 0
  host send lp 98 data 0000
  host expect field credits_won == 0

  egm play games=1 bet=10 win=10
  egm play games=5 bet=10 win=0
  host send lp 95 data 0000
  host expect field games_played == 6
  host send lp 96 data 0000
  host expect field games_won == 1
  host send lp 97 data 0000
  host expect field credits_wagered == 60
  host send lp 98 data 0000
  host expect field credits_won == 10

  # Ended, then played on the player's money: nothing moves.
  host send lp 8C data 000000000000000000
  egm bill insert 2000
  egm play games=1 bet=2 win=4
  host send lp 95 data 0000
  host expect field games_played == 6
  host remember field games_played as played
  host send lp 96 data 0000
  host expect field games_won == 1
  host remember field games_won as won
  host send lp 97 data 0000
  host expect field credits_wagered == 60
  host remember field credits_wagered as wagered
  host send lp 98 data 0000
  host expect field credits_won == 10
  host remember field credits_won as credits_won

  # Time only: five minutes and no credits. Two games well inside the time count, then the end.
  host send lp 8C data 000005000000000000
  egm play games=1 bet=2 win=0
  egm play games=1 bet=2 win=4
  host send lp 95 data 0000
  host expect field games_played == saved.played + 2
  host send lp 96 data 0000
  host expect field games_won == saved.won + 1
  host send lp 97 data 0000
  host expect field credits_wagered == saved.wagered + 4
  host send lp 98 data 0000
  host expect field credits_won == saved.credits_won + 4

  host send lp 8C data 000000000000000000
  egm play games=1 bet=2 win=4
  host send lp 95 data 0000
  host expect field games_played == saved.played + 2
  host remember field games_played as played_2
  host send lp 96 data 0000
  host expect field games_won == saved.won + 1
  host remember field games_won as won_2
  host send lp 97 data 0000
  host expect field credits_wagered == saved.wagered + 4
  host remember field credits_wagered as wagered_2
  host send lp 98 data 0000
  host expect field credits_won == saved.credits_won + 4
  host remember field credits_won as credits_won_2

  # Credits only: a hundred and no time limit, stopped well short of the hundred, then the end.
  host send lp 8C data 000000000000010000
  egm play games=1 bet=5 win=0
  egm play games=1 bet=5 win=10
  host send lp 95 data 0000
  host expect field games_played == saved.played_2 + 2
  host send lp 96 data 0000
  host expect field games_won == saved.won_2 + 1
  host send lp 97 data 0000
  host expect field credits_wagered == saved.wagered_2 + 10
  host send lp 98 data 0000
  host expect field credits_won == saved.credits_won_2 + 10

  host send lp 8C data 000000000000000000
  egm play games=1 bet=5 win=10
  host send lp 95 data 0000
  host expect field games_played == saved.played_2 + 2
  host send lp 96 data 0000
  host expect field games_won == saved.won_2 + 1
  host send lp 97 data 0000
  host expect field credits_wagered == saved.wagered_2 + 10
  host send lp 98 data 0000
  host expect field credits_won == saved.credits_won_2 + 10

  note "Before the run, turn tournaments on at the machine and leave it idle."
}

test "18.8 a session asked for while the machine is busy starts once it is free" {
  tag @egm-test-scripts @chapter-18 @needs-stimulus @needs-a-person
  requires machine.tournament, stimulus.game_in_progress, stimulus.door.main, stimulus.hopper
  requires machine.audit_menu, machine.jackpot_limit, stimulus.handpay.jackpot
  requires stimulus.handpay.reset, stimulus.play, stimulus.bill
  requires decoder.lp95, decoder.lp96, decoder.lp97, decoder.lp98

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  egm bill insert 2000
  host forget exceptions within 2s

  # With the main door open: acknowledged, and the meters stand at zero.
  egm door main open
  host send lp 8C data 000005000000000000
  host send lp 95 data 0000
  host expect field games_played == 0
  host send lp 96 data 0000
  host expect field games_won == 0
  host send lp 97 data 0000
  host expect field credits_wagered == 0
  host send lp 98 data 0000
  host expect field credits_won == 0
  egm door main close
  egm play games=1 bet=1 win=0
  host send lp 95 data 0000
  host expect field games_played == 1
  host send lp 8C data 000000000000000000

  # With a jackpot waiting to be paid by hand: the same, once it is reset.
  egm handpay jackpot 200000
  host send lp 8C data 000005000000000000
  host send lp 95 data 0000
  host expect field games_played == 0
  host send lp 96 data 0000
  host expect field games_won == 0
  host send lp 97 data 0000
  host expect field credits_wagered == 0
  host send lp 98 data 0000
  host expect field credits_won == 0
  egm handpay reset
  egm play games=1 bet=1 win=0
  host send lp 95 data 0000
  host expect field games_played == 1
  host send lp 8C data 000000000000000000

  note "Before the run, turn tournaments on, set a low jackpot limit at the machine, and leave it idle."
  note "By hand, during a game: start a game, and before it finishes send the poll starting a five-minute time-only session, and $95 for game 0. The poll is acknowledged. Once the game is over the four meters read zero - the game in progress was not counted. Play one game: games played reads 1. End the session."
  note "By hand, during a tilt such as an empty hopper: send the poll starting the session, and it is acknowledged. Clear the tilt and play one game: games played reads 1. End the session."
  note "By hand, with the operator menu open: send the poll and the four meter polls. It is acknowledged and the meters read zero. Leave the menu and play one game: games played reads 1. End the session."
}
