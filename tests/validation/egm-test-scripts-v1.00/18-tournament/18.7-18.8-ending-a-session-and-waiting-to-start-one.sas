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
# platform has no tournaments, so both cases declare them (D60); the amounts assume one game credit
# per accounting unit, which each case declares.

test "18.7 the host ends a session, whatever limits it" {
  tag @egm-test-scripts @chapter-18 @needs-stimulus
  machine tournament on
  machine accounting denomination 100c
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
  host send lp 96 data 0000
  host expect field games_won == 1
  host send lp 97 data 0000
  host expect field credits_wagered == 60
  host send lp 98 data 0000
  host expect field credits_won == 10

  # Time only: five minutes and no credits. Two games well inside the time count, then the end.
  host send lp 8C data 000005000000000000
  # Read from where the new session starts: whether it zeroes its meters is 18.8's question, not this one.
  host send lp 95 data 0000
  host remember field games_played as played
  host send lp 96 data 0000
  host remember field games_won as won
  host send lp 97 data 0000
  host remember field credits_wagered as wagered
  host send lp 98 data 0000
  host remember field credits_won as credits_won
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
  host send lp 96 data 0000
  host expect field games_won == saved.won + 1
  host send lp 97 data 0000
  host expect field credits_wagered == saved.wagered + 4
  host send lp 98 data 0000
  host expect field credits_won == saved.credits_won + 4

  # Credits only: a hundred and no time limit, stopped well short of the hundred, then the end.
  host send lp 8C data 000000000000010000
  host send lp 95 data 0000
  host remember field games_played as played_2
  host send lp 96 data 0000
  host remember field games_won as won_2
  host send lp 97 data 0000
  host remember field credits_wagered as wagered_2
  host send lp 98 data 0000
  host remember field credits_won as credits_won_2
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
  machine tournament on
  machine accounting denomination 100c
  machine hopper on
  requires machine.tournament, stimulus.game_in_progress, stimulus.door.main, stimulus.hopper, stimulus.hopper_fault
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

  # During a game: acknowledged, and once the game is over the meters read zero - it was not counted.
  egm game start bet=1
  host send lp 8C data 000005000000000000
  host send lp 95 data 0000
  host expect field games_played == 0
  egm game end
  host send lp 95 data 0000
  host expect field games_played == 0
  host send lp 96 data 0000
  host expect field games_won == 0
  host send lp 97 data 0000
  host expect field credits_wagered == 0
  host send lp 98 data 0000
  host expect field credits_won == 0
  egm play games=1 bet=1 win=0
  host send lp 95 data 0000
  host expect field games_played == 1
  host send lp 8C data 000000000000000000

  # During a tilt, an empty hopper: acknowledged, and the session starts once it is cleared.
  egm hopper fault empty
  host send lp 8C data 000005000000000000
  host send lp 95 data 0000
  host expect field games_played == 0
  egm hopper fault cleared
  egm play games=1 bet=1 win=0
  host send lp 95 data 0000
  host expect field games_played == 1
  host send lp 8C data 000000000000000000

  # With the operator menu open: acknowledged, and the four meters read zero.
  egm menu operator enter
  host send lp 8C data 000005000000000000
  host send lp 95 data 0000
  host expect field games_played == 0
  host send lp 96 data 0000
  host expect field games_won == 0
  host send lp 97 data 0000
  host expect field credits_wagered == 0
  host send lp 98 data 0000
  host expect field credits_won == 0
  egm menu operator leave
  egm play games=1 bet=1 win=0
  host send lp 95 data 0000
  host expect field games_played == 1
  host send lp 8C data 000000000000000000

  note "Before the run, turn tournaments on, set a low jackpot limit at the machine, and leave it idle."
}
