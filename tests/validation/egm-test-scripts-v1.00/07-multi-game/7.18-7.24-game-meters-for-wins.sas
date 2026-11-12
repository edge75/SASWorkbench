# Chapter 7, sections 7.18 to 7.24 - the per-game meters that a win moves, split by who paid it
# and whether it was a progressive.
#
# The same experiment as 7.12 to 7.17, and blocked for the same reasons (see that file's header):
# one game on the software machine, no way to choose the game played, no "moved by" for a per-game
# field, and no silence assertion on 6F and AF. The meters here also need the win to arrive by a
# particular route - paid by the machine, paid by an attendant, or a progressive - so several
# cases need a stimulus on top.
#
# The document numbers no 7.20 and no 7.23; the numbering below follows it.

test "7.18 each game's machine-paid paytable wins add up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires machine.multiple_games, stimulus.bill, stimulus.play.game
  requires host.game_meter_delta, host.length_prefixed_silence

  note "Read meter 001C for game zero and every enabled game, play each enabled game until the machine itself pays a paytable win, and read again: the per-game movements sum to game zero's movement exactly."
  note "6F and AF go unanswered for the game number one past the implemented count."
}

test "7.19 each game's machine-paid progressive wins add up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF
  requires machine.multiple_games, stimulus.play.game, stimulus.progressive
  requires host.game_meter_delta, host.length_prefixed_silence

  note "Read meter 001D for game zero and every progressive game, win a progressive the machine pays itself in each, and read again: the per-game movements sum to game zero's movement exactly. The progressive award is set up as in the bonusing chapter."
  note "6F and AF go unanswered for the game number one past the implemented count."
}

test "7.21 each game's attendant-paid paytable wins add up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF, decoder.lp1B
  requires machine.multiple_games, stimulus.bill, stimulus.play.game
  requires stimulus.handpay.jackpot, stimulus.handpay.reset
  requires host.game_meter_delta, host.length_prefixed_silence

  note "Read meter 001F for game zero and every enabled game. In each enabled game win a jackpot that an attendant pays, read it with long poll 1B and reset the hand pay, then read again: the per-game movements sum to game zero's movement exactly."
  note "6F and AF go unanswered for the game number one past the implemented count."
}

test "7.22 each game's attendant-paid progressive wins add up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF, decoder.lp1B
  requires machine.multiple_games, stimulus.play.game, stimulus.progressive
  requires stimulus.handpay.reset
  requires host.game_meter_delta, host.length_prefixed_silence

  note "Read meter 0020 for game zero and every progressive game. In each, win a progressive large enough to need an attendant, read it with long poll 1B and reset the hand pay, then read again: the per-game movements sum to game zero's movement exactly. The award is set up as in the bonusing chapter."
  note "6F and AF go unanswered for the game number one past the implemented count."
}

test "7.24 each game's total won adds up to the machine's" {
  tag @egm-test-scripts @chapter-7 @needs-stimulus
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF, decoder.lp1B
  requires machine.multiple_games, stimulus.bill, stimulus.play.game
  requires stimulus.handpay.jackpot, stimulus.handpay.reset
  requires host.game_meter_delta, host.length_prefixed_silence

  note "Read meter 0022 for game zero and every enabled game. In each enabled game win a jackpot, read it with long poll 1B and reset the hand pay, then read again: the per-game movements sum to game zero's movement exactly."
  note "6F and AF go unanswered for the game number one past the implemented count."
}
