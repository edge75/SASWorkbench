# Chapter 22, sections 22.4 to 22.6 - every multiplier, the win range, and the max-bet restriction.
#
# The extra amount $7C reports is the win times one less than the multiplier, so a win of ten at
# three times reports twenty. The range limits are both inclusive, and a win outside them is paid
# as it is with no $7C at all. With the wager type set to max bet only, a game staked at less than
# the machine's maximum is not multiplied.
#
# None of this runs on the software machine, which has no multiplied jackpots; each case skips
# naming machine.multiplied_jackpot. The steps use the software stimulus's fixed wins so the
# figures can be stated; on a real machine a person plays until the machine wins.

test "22.4 each multiplier from two to ten" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  requires machine.legacy_bonus, machine.multiplied_jackpot
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 10000
  host forget exceptions within 2s

  host send lp 8B data 0000000099999999020000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 2 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 10 within 8s
  host send lp 8B data 0000000000000000010100
  host forget exceptions within 2s

  host send lp 8B data 0000000099999999030000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 20 within 8s
  host send lp 8B data 0000000000000000010100
  host forget exceptions within 2s

  host send lp 8B data 0000000099999999040000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 4 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 30 within 8s
  host send lp 8B data 0000000000000000010100
  host forget exceptions within 2s

  host send lp 8B data 0000000099999999050000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 5 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 40 within 8s
  host send lp 8B data 0000000000000000010100
  host forget exceptions within 2s

  host send lp 8B data 0000000099999999060000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 6 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 50 within 8s
  host send lp 8B data 0000000000000000010100
  host forget exceptions within 2s

  host send lp 8B data 0000000099999999070000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 7 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 60 within 8s
  host send lp 8B data 0000000000000000010100
  host forget exceptions within 2s

  host send lp 8B data 0000000099999999080000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 8 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 70 within 8s
  host send lp 8B data 0000000000000000010100
  host forget exceptions within 2s

  host send lp 8B data 0000000099999999090000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 9 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 80 within 8s
  host send lp 8B data 0000000000000000010100
  host forget exceptions within 2s

  host send lp 8B data 00000000999999990A0000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 10 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 90 within 8s
  host send lp 8B data 0000000000000000010100

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. Each time, $7F carries the game's own win and the credit meter grows by the multiplier times the win, as the machine shows."
}

test "22.5 only wins inside the range are multiplied" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  requires machine.legacy_bonus, machine.multiplied_jackpot
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 10000
  host forget exceptions within 2s

  # Nine and above.
  host send lp 8B data 0000000999999999020000
  egm play games=1 bet=1 win=8
  host expect exception game_ended within 8s
  host expect no exception legacy_bonus_pay_or_jackpot_awarded within 5s
  egm play games=1 bet=1 win=9
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 9 within 8s
  host send lp 8B data 0000000000000000010100
  host forget exceptions within 2s

  # Nine and below.
  host send lp 8B data 0000000000000009020000
  egm play games=1 bet=1 win=10
  host expect exception game_ended within 8s
  host expect no exception legacy_bonus_pay_or_jackpot_awarded within 5s
  egm play games=1 bet=1 win=9
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 9 within 8s
  host send lp 8B data 0000000000000000010100
  host forget exceptions within 2s

  # Five to nine.
  host send lp 8B data 0000000500000009020000
  egm play games=1 bet=1 win=4
  host expect exception game_ended within 8s
  host expect no exception legacy_bonus_pay_or_jackpot_awarded within 5s
  egm play games=1 bet=1 win=5
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 5 within 8s
  host forget exceptions within 2s
  egm play games=1 bet=1 win=10
  host expect exception game_ended within 8s
  host expect no exception legacy_bonus_pay_or_jackpot_awarded within 5s
  host send lp 8B data 0000000000000000010100

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. Wins are in credits; a win inside each range doubles on the credit meter and one outside it does not, as the machine shows."
  note "The document's acknowledgement for the second range names the first range's key; the second range's command is clearly meant."
}

test "22.6 max-bet only multiplies only max-bet games" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  requires machine.legacy_bonus, machine.multiplied_jackpot, machine.max_bet
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 10000
  host forget exceptions within 2s

  host send lp 8B data 0000000099999999020001
  egm play games=1 bet=1 win=10
  host expect exception game_ended within 8s
  host expect no exception legacy_bonus_pay_or_jackpot_awarded within 5s

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. The steps cover a game below max bet; this host does not know the machine's maximum bet (machine.max_bet), so the max-bet half is a person's."
  note "By hand: play at max bet until several wins. Each one doubles on the credit meter and brings $7C with multiplier 2 and an extra amount equal to the win; wins below max bet are paid as they are with no $7C. Finish with the disable command, which is acknowledged."
}
