# Chapter 6, sections 6.7 to 6.12 - what is wagered, what is paid, and the coins on the way.
#
# Each case reads one meter through $6F and $AF, moves it with a stimulus, and reads it again. The
# movement is asserted through the single-meter long poll that reports the same meter, which this
# host can compare before and after; the selected-meter replies are checked for the code and a
# real width. Two halves stay with a person: the machine's own audit screen, and the requirement
# that $6F and $AF agree, which needs one reply compared with another (host.compare_replies).
#
# A wager is counted in game credits and a meter in accounting units, and the two differ by a ratio
# only the machine knows. Where the exact amount is the machine's to work out, the movement is
# asserted as "at least one" and the exact figure is left in a note; a note's value never moves
# such a meter by a guessed amount.
#
# This build has no coin stimulus. The cases whose subject is a coin skip naming stimulus.coin; the
# coin steps of the others are notes.

test "6.7 total coin in counts what is wagered and nothing else" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000000
  host expect field meters[0].code == 0
  host expect field meters[0].size >= 1
  host send lp AF data 0400000000
  host expect field meters[0].code == 0

  # One credit wagered moves it; money put on the machine and not wagered does not.
  egm bill insert 2000
  host expect meter total_coin_in unchanged within 3s
  egm play games=1 bet=1
  host expect meter total_coin_in +>= 1 within 5s
  egm bill insert 2000
  host expect meter total_coin_in unchanged within 3s

  # Credits won are wagered like any others.
  egm play games=1 bet=1 win=5
  egm play games=1 bet=1
  host expect meter total_coin_in +>= 1 within 5s

  host send lp 6F data 0400000000
  host expect field meters[0].code == 0
  host send lp AF data 0400000000
  host expect field meters[0].code == 0

  note "Before any play, meter 0000 in both replies matches the machine's audit screen in accounting units, and the two replies agree after every step."
  note "Each wager moves it by exactly the wager's value in accounting units: one credit, then the maximum bet, then a maximum bet made from credits won, then one made from credits that came from a note."
  note "At the machine: a rejected coin moves neither 0000 nor 0008; changing the bet before committing it moves nothing; and with inserted coins set to be wagered directly, the coins played move 0000 by their value. This build cannot insert a coin (stimulus.coin)."
}

test "6.8 total coin out counts wins paid by the machine and nothing paid by hand" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp1B, stimulus.bill, stimulus.play
  requires stimulus.handpay, stimulus.handpay.jackpot, stimulus.handpay.reset, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0400000100
  host expect field meters[0].code == 1
  host send lp AF data 0400000100
  host expect field meters[0].code == 1

  # A win paid to the credit meter is coin out, and is not a jackpot.
  egm bill insert 2000
  egm play games=1 bet=1 win=10
  host expect meter total_coin_out +>= 1 within 5s
  host expect meter total_jackpot unchanged within 3s

  # A jackpot paid by an attendant is not coin out.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount != 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect meter total_coin_out unchanged within 3s

  # Nor are credits cancelled by an attendant.
  host forget exceptions within 2s
  egm handpay 2000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount != 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect meter total_coin_out unchanged within 3s

  note "Meter 0001 in both replies matches the machine's audit screen in accounting units before the first step, and the two replies agree after every step."
  note "At the machine, with wins paid from the hopper: 0001 rises by the win and 0002 does not. A cash out paid by the hopper leaves 0001 alone. This build has no hopper stimulus."
  note "With double up configured: declining it leaves the base win counted once; taking it counts nothing until the double up resolves, and then only a double up that was won adds its amount."
}

test "6.9 credits from the coin acceptor count coins and nothing else" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, stimulus.coin

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000800
  host expect field meters[0].code == 8
  host send lp AF data 0400000800
  host expect field meters[0].code == 8

  egm bill insert 2000
  host expect meter true_coin_in unchanged within 3s

  note "Five coins move 0008 by their value; four more with coins diverted to the drop move it by theirs too; a rejected coin moves neither 0008 nor 0000. This language has no statement for a coin yet."
  note "Both replies agree, and match the machine's audit screen in accounting units, at the start and the end."
}

test "6.10 credits from an external coin acceptor" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.coin, machine.external_coin_acceptor

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002700
  host expect field meters[0].code == $27
  host expect field meters[0].size >= 1
  host send lp AF data 0400002700
  host expect field meters[0].code == $27

  note "Five coins at the external acceptor move 0027 by their value, and move total drop 0024 by the same, because those coins never reach a hopper."
  note "Both replies agree, and match the machine's audit screen in accounting units, at the start and the end."
}

test "6.11 credits paid from the hopper" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.coin, stimulus.hopper, stimulus.handpay, stimulus.button.collect

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000900
  host expect field meters[0].code == 9
  host send lp AF data 0400000900
  host expect field meters[0].code == 9

  note "Start with no credits and no hand pay pending. One coin and a cash out moves 0009 by the coins the hopper paid; so does a note and a cash out."
  note "With a partial pay of ten coins and a low jackpot limit, a jackpot moves 0009 by the coins the hopper paid towards it."
  note "A cash out large enough to become a cancelled-credit hand pay leaves 0009 alone. Both replies agree throughout and end matching the audit screen."
}

test "6.12 credits from coins sent to the drop" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.coin

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000A00
  host expect field meters[0].code == $0A
  host send lp AF data 0400000A00
  host expect field meters[0].code == $0A

  note "Five coins that go to the hopper leave 000A alone. Five that go to the drop move it by their value, and so do five more wagered directly as they go to the drop."
  note "Both replies agree throughout and end matching the audit screen in accounting units."
}
