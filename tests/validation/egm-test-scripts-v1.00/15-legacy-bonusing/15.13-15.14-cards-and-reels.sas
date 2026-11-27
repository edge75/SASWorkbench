# Chapter 15, sections 15.13 and 15.14 - what a card game is showing, and where the reels stopped.
#
# Two read-only polls the legacy bonusing document groups here because a bonus host uses them to
# decide what to award. $8E returns a hand type - $00 for the dealt hand, $01 for the final one -
# and five card codes; $8F returns nine reel positions, leftmost first, padded with $FF.
#
# The platform has neither a card game nor physical reels, so each case declares the one it reads
# (D58, D61) and skips on a machine that has not got it.

test "15.13 the hand on a card game" {
  tag @egm-test-scripts @chapter-15 @needs-stimulus
  machine poker on
  requires machine.poker, stimulus.game_in_progress, stimulus.bill, decoder.lp8E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  egm bill insert 2000
  host forget exceptions within 2s

  # A hand dealt and left undrawn.
  egm game start bet=1
  host send lp 8E
  host expect field hand_type == 0
  host expect field card_1 <= $5E
  host expect field card_5 <= $5E

  # The game finished: the final hand.
  egm game end win=0
  host send lp 8E
  host expect field hand_type == 1
  host expect field card_1 <= $5E
  host expect field card_5 <= $5E

  note "Each card code must match the card on the screen, for the dealt hand and again for the final one, which a person checks."
}

test "15.14 the physical reel stops" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.physical_reels, stimulus.bill, stimulus.play, decoder.lp8F
  machine reels on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  egm bill insert 2000
  host forget exceptions within 2s

  egm play games=1 bet=1
  host send lp 8F
  host expect field reel_1 of lp 8F != $FF
  host expect field reel_5 of lp 8F != $FF
  host expect field reel_6 of lp 8F == $FF
  host expect field reel_9 of lp 8F == $FF

  note "Each position must be where that reel physically stopped, leftmost reel first, read on the centre line of a multi-line game. A machine with fewer than nine reels pads the rest with $FF; one with more reports the first nine. Which bytes are reels and which are padding depends on the machine, so the frames are a person's to read."
}
