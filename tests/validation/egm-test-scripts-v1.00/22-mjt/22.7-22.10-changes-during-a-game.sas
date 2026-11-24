# Chapter 22, sections 22.7 to 22.10 - a change to the multiplied jackpot sent while a game is in
# progress.
#
# The machine acknowledges a $8B received mid-game but applies it only once that game has ended,
# so the game in progress keeps whatever setting it started with - a new multiplier, a new range,
# a new wager restriction or a disable all wait for the next game.
#
# The software machine holds a game open with 'egm game start' and ends it with 'egm game end', so each
# case sends the change between the two. A game start is queued as an event, so it is drained first:
# a long poll sent while an event waits is answered with the event (D44).

test "22.7 a multiplier sent mid-game waits for the next game" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  machine multiplied jackpot on
  # A game credit of one dollar is one accounting credit only at this unit, which every figure here assumes.
  machine accounting denomination 100c
  requires machine.legacy_bonus, stimulus.game_in_progress, stimulus.bill
  requires stimulus.real_time_events, decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s
  host send lp 8B data 0000000000000000010100
  host forget exceptions within 2s

  # With the mode off, three times enabled during a game leaves that game's win as it is.
  egm game start bet=1
  host forget exceptions within 2s
  host send lp 8B data 0000000099999999030000
  egm game end win=10
  host expect exception game_ended within 8s
  host expect no exception legacy_bonus_pay_or_jackpot_awarded within 5s

  # With three times enabled while idle, four times enabled during a game leaves that game at three.
  host send lp 8B data 0000000000000000010100
  host send lp 8B data 0000000099999999030000
  egm game start bet=1
  host forget exceptions within 2s
  host send lp 8B data 0000000099999999040000
  egm game end win=10
  host expect field win_amount of event game_ended != 0 within 8s
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 20 within 8s
  host forget exceptions within 2s
  host send lp 8B data 0000000000000000010100

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. On a real machine send each change before the draw or while the reels spin, and repeat until such a game wins; each change is acknowledged."
  note "The multiplied win is tripled on the credit meter and $7F carries the game's own win, as the machine shows."
}

test "22.8 a range sent mid-game waits for the next game" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  machine multiplied jackpot on
  # A game credit of one dollar is one accounting credit only at this unit, which every figure here assumes.
  machine accounting denomination 100c
  requires machine.legacy_bonus, stimulus.game_in_progress, stimulus.bill
  requires stimulus.real_time_events, decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s
  host send lp 8B data 0000000000000000010100
  host send lp 8B data 0000999800009999030000
  host forget exceptions within 2s

  # Three times for every win, sent during a game that started with the narrow range.
  egm game start bet=1
  host forget exceptions within 2s
  host send lp 8B data 0000000099999999030000
  egm game end win=10
  host expect exception game_ended within 8s
  host expect no exception legacy_bonus_pay_or_jackpot_awarded within 5s
  host send lp 8B data 0000000000000000010100

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. On a real machine repeat (disable, the narrow range, a game with the change sent during it) until such a game wins."
  note "The ordinary win is outside the narrow range the game started with, so it is paid as it is on the credit meter."
}

test "22.9 a wager restriction lifted mid-game waits for the next game" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  machine multiplied jackpot on
  # A game credit of one dollar is one accounting credit only at this unit, which every figure here assumes.
  machine accounting denomination 100c
  requires machine.legacy_bonus, stimulus.game_in_progress, stimulus.bill
  requires stimulus.real_time_events, decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s
  host send lp 8B data 0000000000000000010100
  host send lp 8B data 0000000099999999030001
  host forget exceptions within 2s

  # Three times for any wager, sent during a one-credit game that started under the max-bet restriction.
  egm game start bet=1
  host forget exceptions within 2s
  host send lp 8B data 0000000099999999030000
  egm game end win=10
  host expect exception game_ended within 8s
  host expect no exception legacy_bonus_pay_or_jackpot_awarded within 5s
  host send lp 8B data 0000000000000000010100

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine, with a maximum bet above one credit. On a real machine repeat (disable, max bet only, a game with the change sent during it) until such a game wins."
  note "The game started under the max-bet restriction, so the win is paid as it is on the credit meter."
}

test "22.10 a disable sent mid-game waits for the next game" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  machine multiplied jackpot on
  # A game credit of one dollar is one accounting credit only at this unit, which every figure here assumes.
  machine accounting denomination 100c
  requires machine.legacy_bonus, stimulus.game_in_progress, stimulus.bill
  requires stimulus.real_time_events, decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s
  host send lp 8B data 0000000099999999030000
  host forget exceptions within 2s

  # The disable, sent during a game that started with three times on.
  egm game start bet=1
  host forget exceptions within 2s
  host send lp 8B data 0000000000000000010100
  egm game end win=10
  host expect field win_amount of event game_ended != 0 within 8s
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 20 within 8s

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. On a real machine repeat (three times, a game with the disable sent during it) until such a game wins."
  note "The game started with the multiplier on, so the win is tripled on the credit meter and $7F carries the game's own win, as the machine shows."
}
