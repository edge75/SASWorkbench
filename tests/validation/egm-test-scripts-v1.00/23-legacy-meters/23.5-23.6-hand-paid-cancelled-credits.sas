# Chapter 23, sections 23.5 and 23.6 - credits cancelled by a hand pay, through $2D.
#
# $2D carries one meter, the hand-paid cancelled credits (code 0003), for a game named in the
# request. Both cases remember the value of that code from $6F and require $AF, $2F and $2D to
# carry the same number. All four are in credits.

test "23.5 hand-paid cancelled credits for the machine as a whole" {
  tag @egm-test-scripts @chapter-23 @wire-only
  requires decoder.lp6F, decoder.lpAF, decoder.lp2F, decoder.lp2D
  requires host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000300
  host expect field meters[0].code == 3
  host remember field meters[0].value as cancelled
  host send lp AF data 0400000300
  host expect field meters[0].code == 3
  host expect field meters[0].value == saved.cancelled
  host send lp 2F data 03000003
  host expect field meters[0].code == 3
  host expect field meters[0].value == saved.cancelled

  host send lp 2D with game_number=0
  host expect field game_number == 0
  host expect field handpay_cancelled_credits == saved.cancelled
}

# The same for each enabled game. A machine that keeps this meter only for the whole machine is
# allowed to ignore $2D for any game other than zero, which this language cannot accept as an
# alternative to an answer, so a 'Silent' failure on the $2D step for game one is that rule and not
# a fault.
test "23.6 hand-paid cancelled credits for each enabled game" {
  tag @egm-test-scripts @chapter-23 @wire-only
  requires decoder.lp51, decoder.lp56, decoder.lp6F, decoder.lpAF, decoder.lp2F, decoder.lp2D
  requires host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 51
  host expect field games >= 1
  host send lp 56
  host expect field games[0].game_number >= 1

  host send lp 6F data 0400010300
  host expect field game_number == 1
  host expect field meters[0].code == 3
  host remember field meters[0].value as cancelled
  host send lp AF data 0400010300
  host expect field game_number == 1
  host expect field meters[0].code == 3
  host expect field meters[0].value == saved.cancelled
  host send lp 2F data 03000103
  host expect field game_number == 1
  host expect field meters[0].code == 3
  host expect field meters[0].value == saved.cancelled

  host send lp 2D with game_number=1
  host expect field game_number == 1
  host expect field handpay_cancelled_credits == saved.cancelled

  note "Game one is written in because the language has no variables. Repeat the four polls for every game long poll 56 names, enabling further sets of games until long poll 51's count is covered."
  note "Not run: $2D sent again with a corrupted check sequence is ignored or refused (host.bad_check_sequence)."
}
