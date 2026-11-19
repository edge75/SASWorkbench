# Chapter 22, sections 22.7 to 22.10 - a change to the multiplied jackpot sent while a game is in
# progress.
#
# The machine acknowledges a $8B received mid-game but applies it only once that game has ended,
# so the game in progress keeps whatever setting it started with - a new multiplier, a new range,
# a new wager restriction or a disable all wait for the next game.
#
# Sending a poll while a game is held open needs a stimulus this build does not have
# (stimulus.game_in_progress): the software machine plays a game in one step. Each case is written
# as the setup that runs plus notes, and skips on that stimulus and on machine.multiplied_jackpot.

test "22.7 a multiplier sent mid-game waits for the next game" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  requires machine.legacy_bonus, machine.multiplied_jackpot, stimulus.game_in_progress
  requires stimulus.real_time_events, decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  host send lp 8B data 0000000000000000010100
  host forget exceptions within 2s

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine."
  note "With the mode off, start a game and send the three-times enable before it ends - before the draw, or while the reels spin. It is acknowledged; repeat, disabling first each time, until such a game wins. That win is paid as it is: $7F carries it and no $7C follows."
  note "With three times enabled while idle, start a game and send the four-times enable before it ends, repeating until such a game wins. The win is tripled on the credit meter, $7F carries the game's own win, and $7C carries multiplier 3 and twice the win."
}

test "22.8 a range sent mid-game waits for the next game" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  requires machine.legacy_bonus, machine.multiplied_jackpot, stimulus.game_in_progress
  requires stimulus.real_time_events, decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  host send lp 8B data 0000000000000000010100
  host send lp 8B data 0000999800009999030000
  host forget exceptions within 2s

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. The steps leave three times enabled for wins of 9998 and 9999 only."
  note "Start a game and send the three-times enable for every win before it ends, repeating (disable, the narrow range, a game) until such a game wins. The ordinary win is outside the narrow range the game started with, so it is paid as it is: $7F carries it and no $7C follows."
}

test "22.9 a wager restriction lifted mid-game waits for the next game" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  requires machine.legacy_bonus, machine.multiplied_jackpot, stimulus.game_in_progress
  requires stimulus.real_time_events, decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  host send lp 8B data 0000000000000000010100
  host send lp 8B data 0000000099999999030001
  host forget exceptions within 2s

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. The steps leave three times enabled for max-bet games only."
  note "Start a game staking one credit and send the three-times enable for any wager before it ends, repeating (disable, max bet only, a game) until such a game wins. The game started under the max-bet restriction, so the win is paid as it is: $7F carries it and no $7C follows."
}

test "22.10 a disable sent mid-game waits for the next game" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  requires machine.legacy_bonus, machine.multiplied_jackpot, stimulus.game_in_progress
  requires stimulus.real_time_events, decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  host send lp 8B data 0000000099999999030000
  host forget exceptions within 2s

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. The steps leave three times enabled for every win."
  note "Start a game and send the disable before it ends, repeating (three times, a game) until such a game wins. The game started with the multiplier on, so the win is tripled on the credit meter, $7F carries the game's own win, and $7C carries multiplier 3 and twice the win."
}
