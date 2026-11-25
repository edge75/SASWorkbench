# Chapter 7, sections 7.9 to 7.11 - which game is selected, which are enabled, and the long
# description of each.

# The selected game is zero while nothing is selected and the game's own number once a player picks
# one. The case's point is the second half - each game, picked in turn, reported by number - and
# nothing in this build selects a game on the machine, so it skips rather than passing on the idle
# answer alone.
test "7.9 the selected game is the one the player chose" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  machine games 1c 2c 5c 10c
  requires decoder.lp51, decoder.lp55, stimulus.select_game, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # The game menu stands for attract mode: nothing is selected.
  egm select game 0
  host send lp 55
  host expect field selected_game of lp 55 == 0

  host send lp 51
  host remember field games of lp 51 as implemented

  egm select game 1
  host send lp 55
  host expect field selected_game of lp 55 == 1

  egm select game 4
  host send lp 55
  host expect field selected_game of lp 55 == 4
  host expect field selected_game of lp 55 <= saved.implemented

  note "With nothing selected (the machine in attract mode), long poll 55 answers game zero."
  note "Select each enabled game in turn on the machine and read 55 after each: it answers that game's number, which lies between one and the count long poll 51 gives."
}

# The enabled list: its count, and one number per enabled game. Whether the list matches what the
# operator enabled is a comparison with the machine's configuration; that the list is internally
# consistent and non-empty is on the wire.
test "7.10 the enabled list names every enabled game" {
  tag @egm-test-scripts @chapter-7 @wire-only
  requires decoder.lp51, decoder.lp56

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Only a machine that reports at least one game takes this poll.
  host send lp 51
  host expect field games >= 1

  host send lp 56
  host expect field count >= 1
  host expect field length >= 3
  host expect field games[0].game_number >= 1

  note "The count and the list must match the games the machine has enabled, and each number must be one the machine has configured (D40). The length is one byte for the count plus two per game, which the language cannot compute; read it in the log."
}

# The extended description of game zero and of each game. Game zero has no progressive group and
# no levels in this case because progressives are off; its maximum bet is the largest any game on
# the machine takes, which only a person can find out.
test "7.11 the extended description of the machine and of each game" {
  tag @egm-test-scripts @chapter-7 @wire-only
  requires decoder.lp51, decoder.lpB5

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 51
  host expect field games >= 1

  host send lp B5 with game_number=0
  host expect field game_number == 0
  host expect field length >= 9
  host expect field length <= 30
  host expect field progressive_group == 0
  host expect field progressive_levels == 0
  host expect field name_length <= 20

  host send lp B5 with game_number=1
  host expect field game_number == 1
  host expect field name_length <= 20

  note "The case assumes progressives are switched off on the machine."
  note "Game zero's maximum bet is the largest maximum bet of any game the machine holds, enabled or not, in game credits. Each game's maximum bet matches its paytable in the audit display, capped at 9999, and each game's name matches the name the audit display gives it (D40)."
  note "A name is printable characters only, as many as the name length says; the language cannot check the characters one by one, so read them in the log. Repeat the game one request for every enabled game; on the demo machine that is games one to four."
}
