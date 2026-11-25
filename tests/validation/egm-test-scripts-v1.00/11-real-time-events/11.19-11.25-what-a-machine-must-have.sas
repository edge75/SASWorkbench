# Chapter 11, sections 11.19 to 11.25 - the events only some machines owe, and the signature.
#
# Six of these need something this machine is not - a poker game, physical reels, more than one
# game, a bet button that commits credits one at a time, a game recall screen - and the seventh
# needs a number only the manufacturer has. All seven share one rule worth reading first: these
# events exist only while reporting is on, and a machine that sends one with reporting off has
# failed, which is the second half of every case here.

test "11.19 a player asks for draw cards" {
  tag @egm-test-scripts @chapter-11 @needs-a-person
  requires machine.poker

  note "Only for poker machines. With reporting on, deal, hold a card and ask to draw: one $87 in the event format - the machine's address, the marker, the code and a check sequence, with nothing between."
  note "Turn reporting off and do it again: no $87 in answer to any poll, ever."
}

test "11.20 each reel comes to rest" {
  tag @egm-test-scripts @chapter-11 @needs-a-person
  requires stimulus.reel

  note "Only for machines with physical reels. With reporting on, play several games: one $88 per reel, each carrying the reel's number and where it stopped. The number runs from 01 up and never exceeds the reels the machine has; the stop is 00 to FF and matches where the reel physically came to rest."
  note "A machine with more than nine reels reports the first nine, left to right. A multi-line reel game reports the centre line where one credit enables more than one line, and otherwise the line one credit can win on."
  note "Turn reporting off and play again: no $88 in answer to any poll."
}

test "11.21 each credit committed as a wager" {
  tag @egm-test-scripts @chapter-11 @needs-a-person
  requires stimulus.bet_button

  note "Only for machines whose maximum bet is ten credits or fewer. With reporting on and credits on the machine, press bet-one up to the maximum: one $89 for each credit committed, in the event format with no payload."
  note "Play the game, turn reporting off, and press bet-one again: no $89 in answer to any poll."
}

test "11.22 a past game put back on screen" {
  tag @egm-test-scripts @chapter-11 @needs-a-person
  requires machine.game_recall
  requires decoder.lp55

  note "With reporting on, select a game and play it, then read the selected game number: the machine answers with that number as two BCD digits."
  note "Enter the game recall screen: one $8A carrying the same game number and a recall index of zero. Move to the game before it: the index reads one. On a machine with one game the number reads zero."
  note "Leave the screen, turn reporting off, and enter it again: $8A comes back as one bare byte on a general poll rather than as an event."
}

test "11.23 which cards were held" {
  tag @egm-test-scripts @chapter-11 @needs-a-person
  requires machine.poker

  note "Only for poker machines. With reporting on, bet and deal, then hold the rightmost card: one $8B carrying $84 - the high bit says held and the low bits number the card. Release it: $04. Hold the leftmost: $80. Release: $00. The middle: $82 then $02. Second from the left: $81 then $01. Second from the right: $83 then $03."
  note "A machine with more than five cards reports the first five, left to right."
  note "Turn reporting off, hold and release several cards and finish the game: no $8B in answer to any poll."
}

test "11.24 the player chooses a different game" {
  tag @egm-test-scripts @chapter-11 @needs-a-person
  machine games 1c 2c 5c 10c
  requires machine.multiple_games
  requires decoder.lp55, stimulus.select_game, stimulus.real_time_events, decoder.event8C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  host send lp 0E data 01
  egm select game 2
  host expect field game_number of event game_selected == 2 within 5s
  host forget exceptions within 2s
  host send lp 55
  host expect field selected_game of lp 55 == 2

  # The game menu: no game is selected while it is open.
  egm select game 0
  host expect field game_number of event game_selected == 0 within 5s
  host forget exceptions within 2s
  host send lp 0E data 00

  egm select game 3
  host expect exception game_selected within 5s
  egm select game 0
  host expect exception game_selected within 5s

  note "Only for multi-game machines. With reporting on, select a new game: one $8C carrying its number as two BCD digits. Read the selected game number: the same number comes back."
  note "Enter the game selection menu: another $8C, with the number zero - no game is selected while the menu is open."
  note "Turn reporting off, select a game and open the menu again: one bare $8C byte for each, on a general poll."
}

# The signature, which with reporting on comes back as the answer to a long poll and with it off
# comes back on a general poll - the same reply either way, on whichever poll the loop offers.
test "11.25 the ROM signature in both modes" {
  tag @egm-test-scripts @chapter-11 @needs-a-person
  requires machine.rom_signature
  requires decoder.lp21, decoder.lp0E

  note "With reporting on and a polling loop carrying no general polls, ask for the ROM signature. The machine acknowledges with its address and no wakeup bit, stays playable and stays idle, and answers a later long poll with the address, the command byte $21, the two-byte signature and a check sequence."
  note "Turn reporting off, switch to a loop with general polls, and ask again: the same reply arrives on a general poll instead."
  note "The signature matches what the manufacturer says it is, both times. There is no time limit on how long the calculation takes. See 4.2 and 5.10."
}
