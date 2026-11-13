# Chapter 9, sections 9.12 to 9.17 - the play meters, split by denomination.
#
# Every case reads a meter for the whole machine and the same meter at one cent and at five cents
# through the $B0 preamble, plays at one cent, reads them all again, and checks that what the two
# per-denomination meters gained adds up to exactly what the machine total gained, that the
# five-cent meter did not move, and that the total moved at all. A machine that credited every
# denomination, or none, fails the sum while passing any single reading.
#
# The set is fixed at one cent (code 01) and five cents (code 02), game 0000, because the language
# has no loops: a machine without one of them cannot answer the preamble for it, and the case
# skips at the reading it could not take. Playing at five cents, and any further denomination, is the same case
# with the codes swapped or added. The preamble has no layout here yet (`decoder.lpB0`), so its
# replies are read as the meter poll they wrap. Choosing a denomination on the machine is a
# person's job (`stimulus.select_denomination`).

test "9.12 coin in by denomination adds up to coin in" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB0, decoder.lpB2, host.meter_arithmetic
  requires stimulus.bill, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp 6F data 0400000000
  host remember field meters[0].value as total_before
  host send lp B0 data 07016F0400000000
  host remember field meters[0].value as cent1_before
  host send lp B0 data 07026F0400000000
  host remember field meters[0].value as cent5_before

  note "Choose one cent on the machine."
  egm bill insert 2000
  egm play games=3 bet=2 win=0
  host expect meter total_coin_in += 6 within 5s

  host send lp B0 data 07016F0400000000
  host remember field meters[0].value as cent1_after
  host send lp B0 data 07026F0400000000
  host expect field meters[0].value == saved.cent5_before
  host remember field meters[0].value as cent5_after
  host send lp 6F data 0400000000
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.cent1_after - saved.cent1_before + saved.cent5_after - saved.cent5_before

  note "Meter 0000. The document asks for every denomination the player can choose, up to twenty at a time; this case covers one cent and five cents."
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
  host send lp 6F data 0400000100
  host remember field meters[0].value as total_before
  host send lp B0 data 07016F0400000100
  host remember field meters[0].value as cent1_before
  host send lp B0 data 07026F0400000100
  host remember field meters[0].value as cent5_before

  note "Choose one cent on the machine."
  egm bill insert 2000
  egm play games=1 bet=2 win=10
  host expect meter total_coin_out += 10 within 5s

  host send lp B0 data 07016F0400000100
  host remember field meters[0].value as cent1_after
  host send lp B0 data 07026F0400000100
  host expect field meters[0].value == saved.cent5_before
  host remember field meters[0].value as cent5_after
  host send lp 6F data 0400000100
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.cent1_after - saved.cent1_before + saved.cent5_after - saved.cent5_before

  note "Meter 0001, moved by a winning game at one cent."
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
  host send lp 6F data 0400000200
  host remember field meters[0].value as total_before
  host send lp B0 data 07016F0400000200
  host remember field meters[0].value as cent1_before
  host send lp B0 data 07026F0400000200
  host remember field meters[0].value as cent5_before

  note "Choose one cent on the machine."
  egm settle
  host forget exceptions within 2s
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect meter total_jackpot +>= 1 within 5s

  host send lp B0 data 07016F0400000200
  host remember field meters[0].value as cent1_after
  host send lp B0 data 07026F0400000200
  host expect field meters[0].value == saved.cent5_before
  host remember field meters[0].value as cent5_after
  host send lp 6F data 0400000200
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.cent1_after - saved.cent1_before + saved.cent5_after - saved.cent5_before

  note "Meter 0002, moved by a jackpot hand pay won at one cent and keyed off. Record the amount the machine shows."
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
  host send lp 6F data 0400000500
  host remember field meters[0].value as total_before
  host send lp B0 data 07016F0400000500
  host remember field meters[0].value as cent1_before
  host send lp B0 data 07026F0400000500
  host remember field meters[0].value as cent5_before

  note "Choose one cent on the machine."
  egm bill insert 2000
  egm play games=4 bet=1 win=0
  host expect meter games_played += 4 within 5s

  host send lp B0 data 07016F0400000500
  host remember field meters[0].value as cent1_after
  host send lp B0 data 07026F0400000500
  host expect field meters[0].value == saved.cent5_before
  host remember field meters[0].value as cent5_after
  host send lp 6F data 0400000500
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.cent1_after - saved.cent1_before + saved.cent5_after - saved.cent5_before

  note "Meter 0005."
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
  host send lp 6F data 0400000600
  host remember field meters[0].value as total_before
  host send lp B0 data 07016F0400000600
  host remember field meters[0].value as cent1_before
  host send lp B0 data 07026F0400000600
  host remember field meters[0].value as cent5_before

  note "Choose one cent on the machine."
  egm bill insert 2000
  egm play games=1 bet=1 win=5
  host expect meter games_won += 1 within 5s

  host send lp B0 data 07016F0400000600
  host remember field meters[0].value as cent1_after
  host send lp B0 data 07026F0400000600
  host expect field meters[0].value == saved.cent5_before
  host remember field meters[0].value as cent5_after
  host send lp 6F data 0400000600
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.cent1_after - saved.cent1_before + saved.cent5_after - saved.cent5_before

  note "Meter 0006, moved by a winning game at one cent."
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
  host send lp 6F data 0400000700
  host remember field meters[0].value as total_before
  host send lp B0 data 07016F0400000700
  host remember field meters[0].value as cent1_before
  host send lp B0 data 07026F0400000700
  host remember field meters[0].value as cent5_before

  note "Choose one cent on the machine."
  egm bill insert 2000
  egm play games=1 bet=1 win=0
  host expect meter games_lost += 1 within 5s

  host send lp B0 data 07016F0400000700
  host remember field meters[0].value as cent1_after
  host send lp B0 data 07026F0400000700
  host expect field meters[0].value == saved.cent5_before
  host remember field meters[0].value as cent5_after
  host send lp 6F data 0400000700
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.cent1_after - saved.cent1_before + saved.cent5_after - saved.cent5_before

  note "Meter 0007, moved by a losing game at one cent."
}
