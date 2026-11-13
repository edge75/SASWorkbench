# Chapter 23, section 23.12 - the four accounting meters for one game, through $52.
#
# Read a game's meters, play it to a jackpot that locks the machine for a hand pay, reset the hand
# pay, and read them again: coin in, coin out, jackpot and games played each move by what the play
# put through them. The first reading is remembered and the second is compared with it.
#
# The wager is taken from the credit meter ($1A) before and after the play, because a bet of one
# credit is in the player's denomination and coin in is in accounting units. The hand pay is
# reported in cents ($1B) and the jackpot meter in accounting units, which the language cannot
# convert, so the jackpot is only required to rise.

test "23.12 game meters move with a game played to a jackpot" {
  tag @egm-test-scripts @chapter-23 @needs-stimulus
  requires decoder.lp51, decoder.lp56, decoder.lp52, decoder.lp1B
  requires stimulus.bill, stimulus.play, stimulus.settle
  requires stimulus.handpay.jackpot, stimulus.handpay.reset
  requires host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 51
  host expect field games >= 1
  host send lp 56
  host expect field games[0].game_number >= 1

  host send lp 52 with game_number=1
  host expect field game_number == 1
  host remember field total_coin_in as coin_in
  host remember field total_coin_out as coin_out
  host remember field total_jackpot as jackpot
  host remember field games_played as games

  # Money in and a losing wager of one credit; every game ends with its own exception.
  egm bill insert 2000
  host expect exception bill_accepted_20 within 10s
  host send lp 1A
  host remember field current_credits as credits_before
  egm play games=1 bet=1 win=0
  host expect exception game_ended within 10s
  host send lp 1A
  host remember field current_credits as credits_after

  # A jackpot over the machine's limit. The hand pay information is read before the reset, which a
  # machine that queues its hand pays needs before it will report the reset.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount of lp 1B != 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  host send lp 52 with game_number=1
  host expect field game_number == 1
  host expect field total_coin_in == saved.coin_in + saved.credits_before - saved.credits_after
  host expect field total_coin_in > saved.coin_in
  host expect field total_coin_out == saved.coin_out
  host expect field games_played == saved.games + 1
  host expect field total_jackpot > saved.jackpot

  note "Not asserted exactly: the jackpot meter rose by the $1B amount converted from cents to accounting units. Check it by hand against the machine's accounting denomination."
  note "Before the run: progressives off, every game enabled at one player denomination, legacy bonusing on, partial pay off, and a jackpot limit below the amount the prompt asks for. The document wagers one to five credits and plays until a jackpot comes; a machine that decides its own outcomes cannot be asked for one, so it runs only where the outcome is taken from outside."
  note "Game one is written in because the language has no variables. Repeat for every game long poll 56 names, enabling further sets until long poll 51's count is covered."
  note "Not run: $52 sent again with a corrupted check sequence is ignored or refused (host.bad_check_sequence)."
}
