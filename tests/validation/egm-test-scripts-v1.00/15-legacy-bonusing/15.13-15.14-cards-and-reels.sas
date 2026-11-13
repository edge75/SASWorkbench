# Chapter 15, sections 15.13 and 15.14 - what a card game is showing, and where the reels stopped.
#
# Two read-only polls the legacy bonusing document groups here because a bonus host uses them to
# decide what to award. $8E returns a hand type - $00 for the dealt hand, $01 for the final one -
# and five card codes; $8F returns nine reel positions, leftmost first, padded with $FF.
#
# The software machine has neither a card game nor physical reels and does not answer either poll,
# as the machine library does not when the game has none; both cases skip on that.

test "15.13 the hand on a card game" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.poker, stimulus.game_in_progress, decoder.lp8E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 8E
  host expect field hand_type == 0
  host expect field card_1 <= $5E
  host expect field card_5 <= $5E

  note "Before the run, deal a hand on a draw poker game and leave it undrawn. The steps read the dealt hand; each card code must match the card on the screen, which a person checks."
  note "Then finish the game and send the poll again: the hand type is 01 and the cards are the final hand, again as shown."
}

test "15.14 the physical reel stops" {
  tag @egm-test-scripts @chapter-15 @needs-a-person
  requires machine.physical_reels, stimulus.bill, stimulus.play, decoder.lp8F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  egm bill insert 2000
  host forget exceptions within 2s

  egm play games=1 bet=1
  host send lp 8F

  note "Each position must be where that reel physically stopped, leftmost reel first, read on the centre line of a multi-line game. A machine with fewer than nine reels pads the rest with $FF; one with more reports the first nine. Which bytes are reels and which are padding depends on the machine, so the frames are a person's to read."
}
