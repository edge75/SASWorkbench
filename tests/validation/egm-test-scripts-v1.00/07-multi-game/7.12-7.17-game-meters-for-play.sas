# Chapter 7, sections 7.12 to 7.17 - the per-game meters that playing moves.
#
# Every case in this file and the next is the same experiment on a different meter: read the meter
# for each enabled game and for the machine as a whole, play every enabled game, read them all
# again, and check that the per-game movements add up to the machine's movement exactly. A machine
# that charged every game to game one, or counted a game twice, passes any one-game reading and
# fails this sum. Each case then asks for that meter for a game number one past the last
# implemented game, which the machine must not answer.
#
# None of it runs in this build, for three reasons that each stop it on their own:
#   * the software machine holds one game, and a sum over one game proves nothing about sharing
#     (machine.multiple_games);
#   * the play stimulus cannot choose which game it plays (stimulus.play.game);
#   * a per-game meter is a field of the 6F/AF reply, and fields are asserted by value only - the
#     language has no "moved by" for a field and no way to add several replies together
#     (host.game_meter_delta).
# The silence for the out-of-range game number is a fourth: this build refuses to assert silence
# on a poll whose length is carried in the message, as 6F and AF are, because a short one is
# dropped by the receiver before anything reads it (host.length_prefixed_silence).
#
# The document enables at most twenty games at a time and repeats for any more; every machine this
# project has seen fits in one round.

test "7.12 each game's coin in adds up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires machine.multiple_games, stimulus.bill, stimulus.play.game
  requires host.game_meter_delta, host.length_prefixed_silence

  note "With progressives off and every enabled game at the same player denomination, read meter 0000 for game zero and for each game in the enabled list, through both 6F and AF."
  note "Insert money and play each enabled game. Read them all again: the sum of the per-game movements equals the movement of game zero's meter 0000, to the unit."
  note "Ask 6F and AF for the meter of game number one more than long poll 51's count: the machine does not answer."
}

test "7.13 each game's coin out adds up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires machine.multiple_games, stimulus.bill, stimulus.play.game
  requires host.game_meter_delta, host.length_prefixed_silence

  note "Read meter 0001 for game zero and every enabled game, then play each enabled game until it wins. The per-game movements sum to game zero's movement exactly."
  note "Ask 6F and AF for meter 0001 of the game number one past the implemented count: no answer."
}

test "7.14 each game's jackpot adds up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF, decoder.lp1B
  requires machine.multiple_games, stimulus.bill, stimulus.play.game
  requires stimulus.handpay.jackpot, stimulus.handpay.reset
  requires host.game_meter_delta, host.length_prefixed_silence

  note "Read meter 0002 for game zero and every enabled game. In each enabled game win a jackpot that locks the machine for a hand pay, read the hand pay with long poll 1B and reset it."
  note "The per-game movements sum to game zero's movement exactly, and 6F and AF go unanswered for the game number one past the implemented count."
}

test "7.15 each game's games played adds up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires machine.multiple_games, stimulus.bill, stimulus.play.game
  requires host.game_meter_delta, host.length_prefixed_silence

  note "Read meter 0005 for game zero and every enabled game, play each enabled game, and read again: the per-game movements sum to game zero's movement exactly."
  note "6F and AF go unanswered for the game number one past the implemented count."
}

test "7.16 each game's games won adds up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires machine.multiple_games, stimulus.bill, stimulus.play.game
  requires host.game_meter_delta, host.length_prefixed_silence

  note "Read meter 0006 for game zero and every enabled game, play each enabled game until it wins, and read again: the per-game movements sum to game zero's movement exactly."
  note "6F and AF go unanswered for the game number one past the implemented count."
}

test "7.17 each game's games lost adds up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires machine.multiple_games, stimulus.bill, stimulus.play.game
  requires host.game_meter_delta, host.length_prefixed_silence

  note "Read meter 0007 for game zero and every enabled game, play each enabled game until it loses, and read again: the per-game movements sum to game zero's movement exactly."
  note "6F and AF go unanswered for the game number one past the implemented count."
}
