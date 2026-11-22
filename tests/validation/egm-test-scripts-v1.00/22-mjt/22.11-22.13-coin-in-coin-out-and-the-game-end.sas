# Chapter 22, sections 22.11 to 22.13 - the main meters and the game end event under a multiplier.
#
# Credits won through a multiplier are ordinary credits once paid: wagering them moves coin in like
# any other credit, and the whole multiplied win - the game's part and the extra part - lands on
# coin out. The game end event, though, reports only what the game itself paid; the extra part is
# the $7C's to report.
#
# The software machine offers no multiplied jackpots, so all three skip on machine.multiplied_jackpot.
# The meters are read with the selected-meter poll $6F, which this build decodes.

test "22.11 credits won through a multiplier count as coin in when wagered" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  requires machine.legacy_bonus, machine.multiplied_jackpot, host.meter_arithmetic
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.lp6F, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  host send lp 6F data 0400000000
  host expect field meters[0].code == 0
  host remember field meters[0].value as coin_in

  host send lp 8B data 0000000099999999030000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 20 within 8s

  # Thirty credits came from that win; stake all thirty on the next game.
  egm play games=1 bet=30 win=0
  host expect exception game_ended within 8s
  host send lp 8B data 0000000000000000010100

  host send lp 6F data 0400000000
  host expect field meters[0].value == saved.coin_in + 31

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. Meter 0000 must match the machine's own coin in meter before and after (D40)."
}

test "22.12 a multiplied win lands on coin out whole" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  requires machine.legacy_bonus, machine.multiplied_jackpot, host.meter_arithmetic
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.lp6F, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  host send lp 6F data 0400000100
  host expect field meters[0].code == 1
  host remember field meters[0].value as coin_out

  host send lp 8B data 0000000099999999030000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 20 within 8s
  host forget exceptions within 2s
  egm play games=1 bet=1 win=5
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 10 within 8s
  host send lp 8B data 0000000000000000010100

  host send lp 6F data 0400000100
  host expect field meters[0].value == saved.coin_out + 45

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. Meter 0001 must match the machine's own coin out meter before and after (D40)."
  note "The document allows a machine that moves coin out only when the credits leave the credit meter, by a cash-out or a wager; on such a machine cash out before the last reading."
}

test "22.13 the game end event leaves the multiplied part out" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  requires machine.legacy_bonus, machine.multiplied_jackpot
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  host send lp 8B data 0000000099999999030000

  # A losing game still ends with $7F, carrying nothing, and no $7C.
  egm play games=1 bet=1 win=0
  host expect field win_amount of event game_ended == 0 within 8s
  host expect no exception legacy_bonus_pay_or_jackpot_awarded within 5s

  egm play games=1 bet=1 win=10
  host expect field win_amount of event game_ended != 0 within 8s
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 20 within 8s
  host send lp 8B data 0000000000000000010100

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. The winning game's $7F must carry the ten-credit win alone, not the thirty paid; this build reports that field in cents, so compare it with the machine's denomination in the frames."
}
