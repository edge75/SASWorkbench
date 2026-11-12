# Chapter 6, sections 6.18 to 6.21 - jackpots paid by hand, and the count of games.
#
# A jackpot is what an attendant pays for a win; the machine's own payments are coin out. The two
# must never both count the same credits. Where a partial pay is configured the hopper pays part
# of a jackpot first, and that part is coin out - so how far each meter moves depends on the
# machine's set-up, and is a note, while which of them moves at all is asserted.
#
# Games are counts and are asserted exactly.

test "6.18.1 a jackpot over the jackpot limit is counted as a jackpot" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp1B, stimulus.bill, stimulus.play, stimulus.settle
  requires stimulus.handpay.jackpot, stimulus.handpay.reset

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 080000020003000100
  host expect field meters[0].code <= 3
  host expect field meters[2].code <= 3
  host send lp AF data 080000020003000100
  host expect field meters[2].code <= 3

  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount != 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect meter total_jackpot +>= 1 within 5s

  # A win the machine pays to its own credit meter is coin out, not a jackpot.
  egm bill insert 2000
  egm play games=1 bet=1 win=10
  host expect meter total_jackpot unchanged within 3s
  host expect meter total_coin_out +>= 1 within 5s

  note "Configure a low jackpot limit and, if offered, a partial pay of ten credits. The jackpot moves 0002 by its value less what the hopper paid, in accounting units; 0003 does not move; 0001 moves by exactly what the hopper paid."
  note "Both replies agree after each step and end matching the audit screen."
}

# Which kind of hand pay a win over the credit limit becomes is the machine's choice, and the case
# has a branch for each; only a person can create the win and see which one it chose.
test "6.18.2 a win over the credit limit" {
  tag @egm-test-scripts @chapter-6 @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp1B, stimulus.play, machine.credit_limit

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 080000020003000100
  host expect field meters[2].code <= 3
  host send lp AF data 080000020003000100
  host expect field meters[2].code <= 3

  note "Configure a low credit limit and, if offered, a partial pay of ten credits, and play to a win that would take the credit meter over it."
  note "If it becomes a jackpot hand pay: read $1B, key it off; 0002 moves by the win less any hopper part, 0003 does not, 0001 by the hopper part."
  note "If it becomes a cancelled-credit hand pay: read $1B, key it off; 0003 moves by the amount less any hopper part, 0002 does not, 0001 by the hopper part."
  note "If the machine instead pays it to the credit meter: 0001 moves by the win and neither 0002 nor 0003 does. Both replies agree throughout and end matching the audit screen."
}

test "6.19 games played counts every game" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000500
  host expect field meters[0].code == 5
  host send lp AF data 0400000500
  host expect field meters[0].code == 5

  egm bill insert 2000
  egm play games=3 bet=1
  host expect meter games_played += 3 within 5s
  egm play games=3 bet=1 win=5
  host expect meter games_played += 3 within 5s

  note "At the machine, start games every way it offers - buttons, handle, touch screen - and with wins paid from the hopper: each game moves 0005 by one, whatever started it and however it paid."
  note "Both replies agree after each step and end matching the audit screen."
}

test "6.20 games won counts each game that wins" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000600
  host expect field meters[0].code == 6
  host send lp AF data 0400000600
  host expect field meters[0].code == 6

  egm bill insert 2000
  # A meter's starting point is retaken before every stimulus, so each win is asserted on its own.
  egm play games=1 bet=1 win=5
  host expect meter games_won += 1 within 5s
  egm play games=1 bet=1 win=5
  host expect meter games_won += 1 within 5s
  egm play games=1 bet=1 win=5
  host expect meter games_won += 1 within 5s
  egm play games=2 bet=1
  host expect meter games_won unchanged within 3s

  note "With wins paid from the hopper, a win still moves 0006 by one. With double up: each double up won moves 0006 by one, and a double up lost moves 0007 instead."
  note "Both replies agree after each step and end matching the audit screen."
}

test "6.21 games lost counts each game that does not win" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000700
  host expect field meters[0].code == 7
  host send lp AF data 0400000700
  host expect field meters[0].code == 7

  egm bill insert 2000
  egm play games=3 bet=1
  host expect meter games_lost += 3 within 5s
  egm play games=1 bet=1 win=5
  host expect meter games_lost unchanged within 3s

  note "Read both polls during a game as well as after it, through several wins and losses: 0007 never goes down when a game ends, whatever it showed in the middle of one."
  note "With wins paid from the hopper a loss still moves it by one. With double up: each double up lost moves 0007 by one, and one won does not. Both replies agree throughout and end matching the audit screen."
}
