# Chapter 9, sections 9.18 to 9.24 - the win meters, split by denomination.
#
# The same experiment as 9.12 to 9.17, applied to the meters that say who paid a win and why: the
# machine or an attendant, for a paytable win, a progressive or an external bonus, and the total
# of all of them. Each reads the machine total and the meter at one cent and five cents through the
# $B0 preamble, wins at one cent, and checks the two per-denomination gains sum exactly to the
# total's gain, with the five-cent meter unchanged and the total moved. See 9.12 for why the set
# is fixed at those two.
#
# Each meter is read by code through $6F. 9.22 still needs an attendant-paid progressive, and 9.24
# total won credits, which the software machine does not keep, so both skip on their meter.
#
# 9.20 and 9.23 are numbered in the document but carry no steps of their own: each refers to a
# case in the bonusing chapter. They are kept here so the numbering has no holes and skip naming
# the bonus they need.

test "9.18 machine-paid paytable wins by denomination add up to the total" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  machine games 1c 2c 5c 10c
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB0, decoder.lpB2, host.meter_arithmetic
  requires stimulus.bill, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp 6F data 0400001C00
  host remember field meters[0].value as total_before
  host send lp B0 data 07016F0400001C00
  host remember field meters[0].value as cent1_before
  host send lp B0 data 07026F0400001C00
  host remember field meters[0].value as cent5_before

  egm select denomination 1c
  egm bill insert 2000
  egm play games=1 bet=1 win=5

  host send lp B0 data 07016F0400001C00
  host remember field meters[0].value as cent1_after
  host send lp B0 data 07026F0400001C00
  host expect field meters[0].value == saved.cent5_before
  host remember field meters[0].value as cent5_after
  host send lp 6F data 0400001C00
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.cent1_after - saved.cent1_before + saved.cent5_after - saved.cent5_before

  note "Meter 001C."
  note "The document's step plays until a game loses, which would leave a paytable win meter where it was; this case reads it as playing until a game wins."
}

test "9.19 machine-paid progressive wins by denomination add up to the total" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  machine games 1c 2c 5c 10c
  machine progressive non_sas levels 1
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB0, decoder.lpB2, host.meter_arithmetic
  requires machine.progressives, stimulus.progressive, stimulus.bill

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp 6F data 0400001D00
  host remember field meters[0].value as total_before
  host send lp B0 data 07016F0400001D00
  host remember field meters[0].value as cent1_before
  host send lp B0 data 07026F0400001D00
  host remember field meters[0].value as cent5_before

  egm select denomination 1c
  egm bill insert 2000
  egm progressive win level=1

  host send lp B0 data 07016F0400001D00
  host remember field meters[0].value as cent1_after
  host send lp B0 data 07026F0400001D00
  host expect field meters[0].value == saved.cent5_before
  host remember field meters[0].value as cent5_after
  host send lp 6F data 0400001D00
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.cent1_after - saved.cent1_before + saved.cent5_after - saved.cent5_before

  note "Meter 001D. The bonusing chapter covers the progressive itself (15.34)."
}

test "9.20 machine-paid external bonus wins by denomination" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, machine.legacy_bonus
  requires meter.machine_paid_external_bonus_win

  note "Meter 001E. The document gives no steps here and points to the bonusing chapter (15.34): the same per-denomination sum as 9.18, with the win paid by the machine as an external bonus."
}

test "9.21 attendant-paid paytable wins by denomination add up to the total" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  machine games 1c 2c 5c 10c
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB0, decoder.lpB2, decoder.lp1B, host.meter_arithmetic
  requires stimulus.settle, stimulus.handpay.jackpot, stimulus.handpay.reset

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp 6F data 0400001F00
  host remember field meters[0].value as total_before
  host send lp B0 data 07016F0400001F00
  host remember field meters[0].value as cent1_before
  host send lp B0 data 07026F0400001F00
  host remember field meters[0].value as cent5_before

  egm select denomination 1c
  egm settle
  host forget exceptions within 2s
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  host send lp B0 data 07016F0400001F00
  host remember field meters[0].value as cent1_after
  host send lp B0 data 07026F0400001F00
  host expect field meters[0].value == saved.cent5_before
  host remember field meters[0].value as cent5_after
  host send lp 6F data 0400001F00
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.cent1_after - saved.cent1_before + saved.cent5_after - saved.cent5_before

  note "Meter 001F, moved by a jackpot hand pay won at one cent and keyed off. Record the amount the machine shows."
}

test "9.22 attendant-paid progressive wins by denomination add up to the total" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB0, decoder.lpB2, decoder.lp1B, host.meter_arithmetic
  requires meter.attendant_paid_progressive_win
  requires stimulus.progressive, stimulus.handpay.reset

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp 6F data 0400002000
  host remember field meters[0].value as total_before
  host send lp B0 data 07016F0400002000
  host remember field meters[0].value as cent1_before
  host send lp B0 data 07026F0400002000
  host remember field meters[0].value as cent5_before

  note "Choose one cent on the machine."
  note "Play at one cent until a progressive win goes to a hand pay; read it with $1B and key it off, once for every progressive game enabled."

  host send lp B0 data 07016F0400002000
  host remember field meters[0].value as cent1_after
  host send lp B0 data 07026F0400002000
  host expect field meters[0].value == saved.cent5_before
  host remember field meters[0].value as cent5_after
  host send lp 6F data 0400002000
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.cent1_after - saved.cent1_before + saved.cent5_after - saved.cent5_before

  note "Meter 0020. The bonusing chapter covers the progressive itself (15.37)."
}

test "9.23 attendant-paid external bonus wins by denomination" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, machine.legacy_bonus, stimulus.handpay.reset
  requires meter.attendant_paid_external_bonus_win

  note "Meter 0021. The document gives no steps here and points to the bonusing chapter (15.37): the same per-denomination sum as 9.21, with the win an external bonus paid by an attendant."
}

test "9.24 total won credits by denomination add up to the total" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB0, decoder.lpB2, host.meter_arithmetic
  requires meter.total_won_credits
  requires stimulus.bill, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp 6F data 0400002200
  host remember field meters[0].value as total_before
  host send lp B0 data 07016F0400002200
  host remember field meters[0].value as cent1_before
  host send lp B0 data 07026F0400002200
  host remember field meters[0].value as cent5_before

  note "Choose one cent on the machine."
  egm bill insert 2000
  egm play games=1 bet=1 win=5

  host send lp B0 data 07016F0400002200
  host remember field meters[0].value as cent1_after
  host send lp B0 data 07026F0400002200
  host expect field meters[0].value == saved.cent5_before
  host remember field meters[0].value as cent5_after
  host send lp 6F data 0400002200
  host expect field meters[0].value >= saved.total_before + 1
  host expect field meters[0].value == saved.total_before + saved.cent1_after - saved.cent1_before + saved.cent5_after - saved.cent5_before

  note "Meter 0022."
  note "The document's step plays for a progressive win on every progressive game; any win moves this meter, so a paytable win is used here and a progressive win is the stronger version where the machine has one."
}
