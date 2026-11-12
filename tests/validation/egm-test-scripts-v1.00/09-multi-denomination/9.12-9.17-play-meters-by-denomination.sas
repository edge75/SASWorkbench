# Chapter 9, sections 9.12 to 9.17 - the play meters, split by denomination.
#
# Every case here is one experiment run once per player denomination: read a meter for the whole
# machine and the same meter for each denomination through the $B0 preamble, play at one
# denomination, read them all again, and check that what the per-denomination meters gained adds
# up to exactly what the machine total gained. A machine that credited every denomination, or
# none, would fail the sum while passing any single reading.
#
# Two things keep these from running. The preamble has no layout here (`decoder.lpB0`), and the
# language has no variables, so it cannot add several replies together and compare the sum with
# the difference of two more (`host.meter_arithmetic`). The machine-total half is written out as
# steps, so it runs the moment the rest can. Choosing a denomination on the machine is a person's
# job (`stimulus.select_denomination`).
#
# The preamble data uses denomination code 01, one cent, and game 0000; see 9.7 for why a machine
# without one cent needs another code. The document asks for up to twenty denominations at a time,
# with a different denomination on each game of a multi-game machine.

test "9.12 coin in by denomination adds up to coin in" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB0, decoder.lpB2, host.meter_arithmetic
  requires stimulus.bill, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp B0 data 07016F0400000000

  egm bill insert 2000
  egm play games=3 bet=2 win=0
  host expect meter total_coin_in += 6 within 5s

  host send lp B0 data 07016F0400000000

  note "Meter 0000. Read it for game zero at every denomination listed in $B2, before and after playing at the chosen one. The gain summed across the denominations equals the gain in the machine's own coin in meter, exactly. Repeat at every denomination."
}

test "9.13 coin out by denomination adds up to coin out" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB0, decoder.lpB2, host.meter_arithmetic
  requires stimulus.bill, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp B0 data 07016F0400000100

  egm bill insert 2000
  egm play games=1 bet=2 win=10
  host expect meter total_coin_out += 10 within 5s

  host send lp B0 data 07016F0400000100

  note "Meter 0001, moved by a winning game at the chosen denomination. The gain summed across the denominations equals the gain in the machine's own coin out meter, exactly. Repeat at every denomination."
}

test "9.14 jackpot by denomination adds up to jackpot" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB0, decoder.lpB2, decoder.lp1B, host.meter_arithmetic
  requires stimulus.settle, stimulus.handpay.jackpot, stimulus.handpay.reset

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp B0 data 07016F0400000200

  egm settle
  host forget exceptions within 2s
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect meter total_jackpot +>= 1 within 5s

  host send lp B0 data 07016F0400000200

  note "Meter 0002, moved by a jackpot hand pay won at the chosen denomination and keyed off. Record the amount the machine shows. The gain summed across the denominations equals the gain in the machine's own jackpot meter, exactly. Repeat at every denomination."
}

test "9.15 games played by denomination adds up to games played" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB0, decoder.lpB2, host.meter_arithmetic
  requires stimulus.bill, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp B0 data 07016F0400000500

  egm bill insert 2000
  egm play games=4 bet=1 win=0
  host expect meter games_played += 4 within 5s

  host send lp B0 data 07016F0400000500

  note "Meter 0005. The games counted summed across the denominations equal the gain in the machine's own games played meter, exactly. Repeat at every denomination."
}

test "9.16 games won by denomination adds up to games won" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB0, decoder.lpB2, host.meter_arithmetic
  requires stimulus.bill, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp B0 data 07016F0400000600

  egm bill insert 2000
  egm play games=1 bet=1 win=5
  host expect meter games_won += 1 within 5s

  host send lp B0 data 07016F0400000600

  note "Meter 0006, moved by playing until a game wins at the chosen denomination. The gain summed across the denominations equals the gain in the machine's own games won meter, exactly. Repeat at every denomination."
}

test "9.17 games lost by denomination adds up to games lost" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB0, decoder.lpB2, host.meter_arithmetic
  requires stimulus.bill, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp B0 data 07016F0400000700

  egm bill insert 2000
  egm play games=1 bet=1 win=0
  host expect meter games_lost += 1 within 5s

  host send lp B0 data 07016F0400000700

  note "Meter 0007, moved by playing until a game loses at the chosen denomination. The gain summed across the denominations equals the gain in the machine's own games lost meter, exactly. Repeat at every denomination."
}
