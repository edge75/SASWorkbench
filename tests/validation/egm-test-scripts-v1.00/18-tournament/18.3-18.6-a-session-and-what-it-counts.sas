# Chapter 18, sections 18.3 to 18.6 - a tournament session limited by time, by credits or by
# both, and what its four meters count.
#
# Inside a session the machine keeps four counts for the game: games played ($95), games won
# ($96), credits wagered ($97) and credits won ($98). Each reply is the game number and a
# four-byte BCD count. Credits are counted in the accounting unit. Play outside a session - after
# its time is up, after its credits are gone, or on credits the player put in - moves none of
# them. The timer starts with the first game of the session, not with the poll.
#
#   game 0, 5 minutes, 500 credits     8C 0000 0500 00000500 00
#   game 0, 10 minutes, 100 credits    8C 0000 1000 00000100 00
#   game 0, 5 minutes, 100 credits     8C 0000 0500 00000100 00     pulses on: last byte 01
#   any tournament meter of game 0     95|96|97|98 0000
#
# The software machine plays no tournaments, and this build decodes none of the four replies, so
# every case skips; the reply field names are provisional. The amounts below assume one game
# credit is one accounting unit; on another machine scale them.

test "18.3 a timed session counts until its time runs out" {
  tag @egm-test-scripts @chapter-18 @needs-stimulus
  requires machine.tournament, stimulus.play, stimulus.bill
  requires decoder.lp95, decoder.lp96, decoder.lp97, decoder.lp98

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 8C data 000005000000050000
  host send lp 95 data 0000
  host expect field games_played == 0
  host send lp 96 data 0000
  host expect field games_won == 0
  host send lp 97 data 0000
  host expect field credits_wagered == 0
  host send lp 98 data 0000
  host expect field credits_won == 0

  # One losing game of two credits: one game and two credits more.
  egm play games=1 bet=2 win=0
  host send lp 95 data 0000
  host expect field game_number == 0
  host expect field games_played == 1
  host send lp 97 data 0000
  host expect field game_number == 0
  host expect field credits_wagered == 2

  # Then a winning one, inside the five minutes.
  egm play games=1 bet=2 win=10
  host send lp 96 data 0000
  host expect field game_number == 0
  host expect field games_won == 1
  host send lp 98 data 0000
  host expect field game_number == 0
  host expect field credits_won == 10

  # Past the five minutes, a won game on the player's own credits is counted nowhere.
  host wait 5m
  egm bill insert 2000
  egm play games=1 bet=2 win=10
  host send lp 95 data 0000
  host expect field games_played == 2
  host send lp 96 data 0000
  host expect field games_won == 1
  host send lp 97 data 0000
  host expect field credits_wagered == 4
  host send lp 98 data 0000
  host expect field credits_won == 10

  note "Before the run, turn tournaments on at the machine and leave it idle."
}

test "18.4 a session limited by credits ends when they are spent" {
  tag @egm-test-scripts @chapter-18 @needs-stimulus
  requires machine.tournament, stimulus.play, stimulus.bill
  requires decoder.lp95, decoder.lp96, decoder.lp97, decoder.lp98

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 8C data 000010000000010000
  host send lp 95 data 0000
  host expect field games_played == 0
  host send lp 96 data 0000
  host expect field games_won == 0
  host send lp 97 data 0000
  host expect field credits_wagered == 0
  host send lp 98 data 0000
  host expect field credits_won == 0

  # A game that wins back its bet, then ten losing ones that spend the hundred credits.
  egm play games=1 bet=10 win=10
  egm play games=10 bet=10 win=0
  host send lp 95 data 0000
  host expect field games_played == 11
  host send lp 96 data 0000
  host expect field games_won == 1
  host send lp 97 data 0000
  host expect field credits_wagered == 110
  host send lp 98 data 0000
  host expect field credits_won == 10

  # Inside the ten minutes still, but on the player's own money: nothing moves.
  egm bill insert 2000
  egm play games=2 bet=5 win=0
  host send lp 95 data 0000
  host expect field games_played == 11
  host send lp 96 data 0000
  host expect field games_won == 1
  host send lp 97 data 0000
  host expect field credits_wagered == 110
  host send lp 98 data 0000
  host expect field credits_won == 10

  note "Before the run, turn tournaments on at the machine and leave it idle."
}

test "18.5 a session limited by time and credits, then a second one" {
  tag @egm-test-scripts @chapter-18 @needs-stimulus
  requires machine.tournament, stimulus.play, stimulus.bill, host.meter_arithmetic
  requires decoder.lp95, decoder.lp96, decoder.lp97, decoder.lp98

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  # The first session: five minutes and a hundred credits, spent before the time is up.
  host send lp 8C data 000005000000010000
  host send lp 95 data 0000
  host expect field games_played == 0
  host send lp 96 data 0000
  host expect field games_won == 0
  host send lp 97 data 0000
  host expect field credits_wagered == 0
  host send lp 98 data 0000
  host expect field credits_won == 0

  egm play games=1 bet=10 win=10
  egm play games=10 bet=10 win=0
  host send lp 95 data 0000
  host expect field games_played == 11
  host send lp 96 data 0000
  host expect field games_won == 1
  host send lp 97 data 0000
  host expect field credits_wagered == 110
  host send lp 98 data 0000
  host expect field credits_won == 10

  egm bill insert 2000
  egm play games=2 bet=5 win=0
  host send lp 95 data 0000
  host expect field games_played == 11
  host send lp 97 data 0000
  host expect field credits_wagered == 110

  # The second session, from wherever the meters now stand.
  host send lp 8C data 000005000000010000
  host send lp 95 data 0000
  host remember field games_played as played
  host send lp 96 data 0000
  host remember field games_won as won
  host send lp 97 data 0000
  host remember field credits_wagered as wagered
  host send lp 98 data 0000
  host remember field credits_won as credits_won

  # Small bets, so the credits outlast the time.
  egm play games=1 bet=2 win=0
  egm play games=1 bet=2 win=6
  host send lp 95 data 0000
  host expect field games_played == saved.played + 2
  host send lp 96 data 0000
  host expect field games_won == saved.won + 1
  host send lp 97 data 0000
  host expect field credits_wagered == saved.wagered + 4
  host send lp 98 data 0000
  host expect field credits_won == saved.credits_won + 6

  # And once the five minutes are up, a won game moves nothing.
  host wait 5m
  egm play games=1 bet=2 win=6
  host send lp 95 data 0000
  host expect field games_played == saved.played + 2
  host send lp 96 data 0000
  host expect field games_won == saved.won + 1
  host send lp 97 data 0000
  host expect field credits_wagered == saved.wagered + 4
  host send lp 98 data 0000
  host expect field credits_won == saved.credits_won + 6

  note "Before the run, turn tournaments on at the machine and leave it idle."
  note "Whether a new session starts its meters from zero this case does not decide; 18.8 expects it to, so compare."
}

test "18.6 a session with tournament pulses on" {
  tag @egm-test-scripts @chapter-18 @wire-only
  requires machine.tournament

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Five minutes, a hundred credits, pulses on: the machine acknowledges it.
  host send lp 8C data 000005000000010001

  # Not asked for by the document: the session is ended so the next case starts outside one.
  host send lp 8C data 000000000000000000

  note "The document asks only for the acknowledgement and leaves what the machine does with pulses open. Note what it does."
}
