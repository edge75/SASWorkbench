# Chapter 6, sections 6.7 to 6.12 - what is wagered, what is paid, and the coins on the way.
#
# Each case reads one meter through $6F and $AF, moves it with a stimulus, and reads it again. The
# movement is asserted through the single-meter long poll that reports the same meter, which this
# host can compare before and after; the selected-meter replies are checked for the code and a
# real width. That $6F and $AF agree is asserted by remembering the first reply's value and
# comparing the second with it, at the start of each case and again at its end. The machine's own
# audit screen stays with a person.
#
# A wager is counted in game credits and a meter in accounting units, and the two differ by a ratio
# only the machine knows. Where the exact amount is the machine's to work out, the movement is
# asserted as "at least one" and the exact figure is left in a note; a note's value never moves
# such a meter by a guessed amount.
#
# The platform has no coin acceptor or hopper, so the cases whose subject is a coin declare them and
# run on a software machine built with them (D58); a coin is one accounting unit there.

test "6.7 total coin in counts what is wagered and nothing else" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, stimulus.play, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000000
  host expect field meters[0].code == 0
  host expect field meters[0].size >= 1
  host remember field meters[0].value as coin_in_before
  host send lp AF data 0400000000
  host expect field meters[0].code == 0
  host expect field meters[0].value == saved.coin_in_before

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
  host remember field meters[0].value as coin_in_after
  host send lp AF data 0400000000
  host expect field meters[0].code == 0
  host expect field meters[0].value == saved.coin_in_after

  note "Before any play, meter 0000 matches the machine's audit screen in accounting units. The replies are compared at the start and the end, not between the steps."
  note "Each wager moves it by exactly the wager's value in accounting units: one credit, then the maximum bet, then a maximum bet made from credits won, then one made from credits that came from a note."
  note "At the machine: a rejected coin moves neither 0000 nor 0008; changing the bet before committing it moves nothing; and with inserted coins set to be wagered directly, the coins played move 0000 by their value. This build cannot insert a coin (stimulus.coin)."
}

test "6.8 total coin out counts wins paid by the machine and nothing paid by hand" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp1B, stimulus.bill, stimulus.play
  requires stimulus.handpay, stimulus.handpay.jackpot, stimulus.handpay.reset, stimulus.settle
  requires host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0400000100
  host expect field meters[0].code == 1
  host remember field meters[0].value as coin_out_before
  host send lp AF data 0400000100
  host expect field meters[0].code == 1
  host expect field meters[0].value == saved.coin_out_before

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

  # Both polls agree at the end as well.
  host send lp 6F data 0400000100
  host expect field meters[0].code == 1
  host remember field meters[0].value as coin_out_after
  host send lp AF data 0400000100
  host expect field meters[0].code == 1
  host expect field meters[0].value == saved.coin_out_after

  note "Meter 0001 matches the machine's audit screen in accounting units before the first step. The replies are compared at the start and the end, not between the steps."
  note "At the machine, with wins paid from the hopper: 0001 rises by the win and 0002 does not. A cash out paid by the hopper leaves 0001 alone. This build has no hopper stimulus."
  note "With double up configured: declining it leaves the base win counted once; taking it counts nothing until the double up resolves, and then only a double up that was won adds its amount."
}

test "6.9 credits from the coin acceptor count coins and nothing else" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, stimulus.coin, host.compare_replies
  machine coin acceptor on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000800
  host expect field meters[0].code == $08
  host remember field meters[0].value as coins
  host send lp AF data 0400000800
  host expect field meters[0].code == $08
  host expect field meters[0].value == saved.coins

  # A note is not a coin.
  egm bill insert 2000
  host expect meter true_coin_in unchanged within 3s

  # Five coins, with no hopper to take them, so all to the drop: counted all the same.
  egm coin insert 5
  host expect meter true_coin_in += 5 within 3s
  host send lp 6F data 0400000800
  host expect field meters[0].value == saved.coins + 5
  host remember field meters[0].value as coins
  host send lp AF data 0400000800
  host expect field meters[0].value == saved.coins

  note "A rejected coin moves neither 0008 nor 0000; this machine's acceptor rejects none."
  note "The replies match the machine's audit screen in accounting units at the start and the end."
}

test "6.10 credits from an external coin acceptor" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus
  requires decoder.lp6F, decoder.lpAF, stimulus.coin, machine.external_coin_acceptor
  requires host.compare_replies
  machine coin acceptor external

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002700
  host expect field meters[0].code == $27
  host remember field meters[0].value as external_coins
  host send lp AF data 0400002700
  host expect field meters[0].code == $27
  host expect field meters[0].value == saved.external_coins
  host send lp 6F data 0400002400
  host remember field meters[0].value as drop

  # Five coins at the external acceptor move its meter and the drop, as they never reach a hopper.
  egm coin insert 5
  host send lp 6F data 0400002700
  host expect field meters[0].value == saved.external_coins + 5
  host remember field meters[0].value as external_coins
  host send lp AF data 0400002700
  host expect field meters[0].value == saved.external_coins
  host send lp 6F data 0400002400
  host expect field meters[0].value == saved.drop + 5

  note "At the end both replies match the machine's audit screen in accounting units."
}

test "6.11 credits paid from the hopper" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus
  requires decoder.lp6F, decoder.lpAF, stimulus.coin, stimulus.hopper, stimulus.handpay, stimulus.button.collect
  requires stimulus.bill, stimulus.handpay.jackpot, stimulus.handpay.reset, stimulus.settle
  requires host.compare_replies
  machine accounting denomination 5c
  machine coin acceptor on
  machine partial pay on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0400000900
  host expect field meters[0].code == $09
  host remember field meters[0].value as hopper_paid
  host send lp AF data 0400000900
  host expect field meters[0].code == $09
  host expect field meters[0].value == saved.hopper_paid

  # One coin and a cash out: the hopper pays it back.
  egm coin insert 1
  egm press collect
  host send lp 6F data 0400000900
  host expect field meters[0].value == saved.hopper_paid + 1
  host remember field meters[0].value as hopper_paid
  host send lp AF data 0400000900
  host expect field meters[0].value == saved.hopper_paid

  # A note and a cash out: twenty coins.
  egm bill insert 100
  egm press collect
  host send lp 6F data 0400000900
  host expect field meters[0].value == saved.hopper_paid + 20
  host remember field meters[0].value as hopper_paid
  host send lp AF data 0400000900
  host expect field meters[0].value == saved.hopper_paid

  # A jackpot over the limit: the hopper pays the ten-coin partial pay towards it.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  egm handpay reset
  host send lp 6F data 0400000900
  host expect field meters[0].value == saved.hopper_paid + 10
  host remember field meters[0].value as hopper_paid
  host send lp AF data 0400000900
  host expect field meters[0].value == saved.hopper_paid

  # A cash out over the hopper limit: the hopper pays its partial pay and nothing more.
  egm set hopper limit 1000
  egm bill insert 2000
  egm press collect
  host expect exception handpay_is_pending within 10s
  egm handpay reset
  host send lp 6F data 0400000900
  host expect field meters[0].value == saved.hopper_paid + 10
  host remember field meters[0].value as hopper_paid
  host send lp AF data 0400000900
  host expect field meters[0].value == saved.hopper_paid

  note "The document has the cancelled-credit hand pay leave 0009 alone; on a machine whose partial pay applies to cash outs as well (5.4.2) the hopper pays the partial pay and no more, which is what is asserted."
  note "After each step both replies end matching the audit screen."
}

test "6.12 credits from coins sent to the drop" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus
  requires decoder.lp6F, decoder.lpAF, stimulus.coin, stimulus.hopper, host.compare_replies
  machine coin acceptor on
  machine hopper on

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400000A00
  host expect field meters[0].code == $0A
  host remember field meters[0].value as coins_to_drop
  host send lp AF data 0400000A00
  host expect field meters[0].code == $0A
  host expect field meters[0].value == saved.coins_to_drop

  # Coins the hopper has room for leave 000A alone.
  egm hopper level 500
  egm coin insert 5
  host send lp 6F data 0400000A00
  host expect field meters[0].value == saved.coins_to_drop + 0
  host remember field meters[0].value as coins_to_drop
  host send lp AF data 0400000A00
  host expect field meters[0].value == saved.coins_to_drop

  # A full hopper sends them to the drop.
  egm hopper level 1000
  egm coin insert 5
  host send lp 6F data 0400000A00
  host expect field meters[0].value == saved.coins_to_drop + 5
  host remember field meters[0].value as coins_to_drop
  host send lp AF data 0400000A00
  host expect field meters[0].value == saved.coins_to_drop

  note "Five more wagered directly as they go to the drop move it by their value as well; this machine takes coins onto the credit meter only."
  note "After each step both replies end matching the audit screen in accounting units."
}
