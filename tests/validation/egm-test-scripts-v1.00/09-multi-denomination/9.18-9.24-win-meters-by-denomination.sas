# Chapter 9, sections 9.18 to 9.24 - the win meters, split by denomination.
#
# The same experiment as 9.12 to 9.17, applied to the meters that say who paid a win and why: the
# machine or an attendant, for a paytable win, a progressive or an external bonus, and the total
# of all of them. Each per-denomination gain must sum exactly to the machine total's gain.
#
# None of these meters can be read by name in this build, so besides what 9.12 already needs, each
# case requires its meter, and the attendant-paid and progressive ones need a hand pay or a
# progressive this build cannot cause. The preamble data uses denomination code 01 and game 0000.
#
# 9.20 and 9.23 are numbered in the document but carry no steps of their own: each refers to a
# case in the bonusing chapter. They are kept here so the numbering has no holes and skip naming
# the bonus they need.

test "9.18 machine-paid paytable wins by denomination add up to the total" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB0, decoder.lpB2, host.meter_arithmetic
  requires meter.machine_paid_paytable_win
  requires stimulus.bill, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp B0 data 07016F0400001C00

  egm bill insert 2000
  egm play games=1 bet=1 win=5

  host send lp B0 data 07016F0400001C00

  note "Meter 001C. The gain summed across the denominations equals the gain in the machine's own paytable win meter, exactly. Repeat at every denomination."
  note "The document's step plays until a game loses, which would leave a paytable win meter where it was; this case reads it as playing until a game wins."
}

test "9.19 machine-paid progressive wins by denomination add up to the total" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB0, decoder.lpB2, host.meter_arithmetic
  requires meter.machine_paid_progressive_win
  requires stimulus.progressive

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp B0 data 07016F0400001D00

  note "Meter 001D, moved by playing until a progressive the machine pays itself is won at the chosen denomination. The gain summed across the denominations equals the gain in the machine total, exactly. Repeat at every denomination. The bonusing chapter covers the progressive itself (15.34)."
}

test "9.20 machine-paid external bonus wins by denomination" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.legacy_bonus
  requires meter.machine_paid_external_bonus_win

  note "Meter 001E. The document gives no steps here and points to the bonusing chapter (15.34): the same per-denomination sum as 9.18, with the win paid by the machine as an external bonus."
}

test "9.21 attendant-paid paytable wins by denomination add up to the total" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.select_denomination
  requires decoder.lpB0, decoder.lpB2, decoder.lp1B, host.meter_arithmetic
  requires meter.attendant_paid_paytable_win
  requires stimulus.settle, stimulus.handpay.jackpot, stimulus.handpay.reset

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp B2
  host send lp B0 data 07016F0400001F00

  egm settle
  host forget exceptions within 2s
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  host send lp B0 data 07016F0400001F00

  note "Meter 001F, moved by a jackpot hand pay won at the chosen denomination and keyed off. Record the amount the machine shows. The gain summed across the denominations equals the gain in the machine total, exactly. Repeat at every denomination."
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
  host send lp B0 data 07016F0400002000

  note "Meter 0020, moved by a progressive win at the chosen denomination that the machine sends to a hand pay, read and keyed off, once for every progressive game enabled. The gain summed across the denominations equals the gain in the machine total, exactly. Repeat at every denomination. The bonusing chapter covers the progressive itself (15.37)."
}

test "9.23 attendant-paid external bonus wins by denomination" {
  tag @egm-test-scripts @chapter-9 @needs-a-person
  requires machine.multiple_denominations, stimulus.legacy_bonus, stimulus.handpay.reset
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
  host send lp B0 data 07016F0400002200

  egm bill insert 2000
  egm play games=1 bet=1 win=5

  host send lp B0 data 07016F0400002200

  note "Meter 0022. The gain summed across the denominations equals the gain in the machine total, exactly. Repeat at every denomination."
  note "The document's step plays for a progressive win on every progressive game; any win moves this meter, so a paytable win is used here and a progressive win is the stronger version where the machine has one."
}
