# Chapter 21, sections 21.15.1 to 21.16 - the host switches individual games off and on.
#
# Long poll $09 names a game, two BCD digits, and carries one byte: zero to disable it, anything
# else to enable it. Game zero is the machine as a whole, which this command cannot switch off -
# shutdown ($01) does that - so a $09 for game zero, or for a game the machine does not have, must
# go unanswered. Like 21.9 to 21.12 the command is optional: a machine may ignore it for a real
# game too, and the language cannot accept silence as a pass, so only the half that must be silent
# is written as a step.
#
# 21.16 sends the same command wrapped in the multi-denomination preamble ($B0), which names a
# denomination as well and is answered with a status byte rather than a bare acknowledgement.
#
# @finding: 21.15.1 and 21.15.2 - the library refuses $09 for game zero and for a game past the last,
# with the address and bit 7, rather than leaving it unanswered (lp.cpp:524).

test "21.15.1 one game is disabled and enabled by long poll 09" {
  tag @egm-test-scripts @chapter-21 @needs-stimulus @finding
  machine games 1c 2c 5c 10c
  requires decoder.lp55, decoder.lp09, stimulus.select_game, stimulus.play, stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # The game the player is in, by number, after a game of it.
  egm select game 2
  egm bill insert 1000
  egm play games=1 bet=1 win=0
  host send lp 55
  host expect field selected_game >= 1

  # Game zero cannot be switched off this way, so the poll is ignored.
  host expect no reply to lp 09 data 000000

  note "Select and play a game first, so long poll 55 names it. Send $09 disabling that game: if acknowledged, it can no longer be played; if ignored, it still can. Then $09 enabling it: if acknowledged, it is playable again (D40)."
  note "The game number in the poll is the one 55 returned; the language has no variables, so on a given machine write it in, as `host send lp 09 data <game>00` and `host send lp 09 data <game>01`."
  note "Not run: the disable sent with a broken check sequence must be ignored or refused (host.bad_crc)."
}

# Every game in turn, then every game back again, with the two ends of the range unanswered.
test "21.15.2 every game is disabled and enabled by long poll 09" {
  tag @egm-test-scripts @chapter-21 @needs-stimulus @finding
  machine games 1c 2c 5c 10c
  requires decoder.lp56, decoder.lp09, machine.multiple_games

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 56
  host expect field count >= 1

  host expect no reply to lp 09 data 000000

  note "Send $09 disabling each game from 1 to the highest number long poll 56 returned, then one past it. Game 0 and the one past the last go unanswered. Each game that acknowledged can no longer be played; if the machine ignored them all, every game that was enabled still is."
  note "Send the same range again enabling each game. Those acknowledged become playable, though the machine's own rules may keep some off; game 0 and the one past the last are again unanswered."
  note "The language has no loops or variables, so the range is a person's to write in for a given machine, ending with `host expect no reply to lp 09 data <n+1>00`."
}

# A game offered at more than one denomination can be switched off at one of them alone.
test "21.16 one game at one denomination is disabled through the multi-denomination preamble" {
  tag @egm-test-scripts @chapter-21 @needs-stimulus
  requires decoder.lpA0, decoder.lp56, decoder.lpB0, decoder.lp09
  requires machine.multiple_games, machine.multiple_denominations
  requires stimulus.select_denomination, stimulus.select_game

  note "Only for a machine with several player denominations, several games at each, and at least one game offered at more than one of them. Ask for the features of game 0 ($A0): if the multi-denomination extensions bit of the second feature byte is clear, the case does not apply."
  note "For each player denomination, select it and read the enabled games ($56 inside $B0). Choose a game listed under two denominations, or any game and denomination if none is."
  note "Select that denomination and game and check it plays. Send $09 inside $B0 disabling it at that denomination: the reply is the address, $B0, a length, the base command $09 and one status byte. A status of 00 or 81 means the game is still playable; 01 means it is now disabled and cannot be played."
  note "The same game at its other denomination (or another game at this one) is still playable. Back at the first denomination it is unplayable only if the status was 01. Send $09 inside $B0 enabling it: status 01 means it is playable again, 00 or 81 that it never stopped being."
}
