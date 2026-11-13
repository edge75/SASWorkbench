# Chapter 6, sections 6.32 to 6.41 - wins, split by who paid them and why.
#
# The machine's own payments break down into paytable, progressive and external bonus wins, which
# together make coin out; an attendant's payments break down the same way and together make the
# jackpot meter. Won credits is both totals, and hand-paid credits is the jackpot plus cancelled
# credits. Those sums are the point of 6.35 and 6.39 to 6.41. A reply may list its meters in any
# order, so each sum is taken from one-meter requests, where the entry is known to be the code asked
# for; the list request is still sent for its shape.
#
# None of 001C to 0023 has a single-meter long poll this host can read by name. Where a stimulus
# moves one, its gain is compared with the gain of the total it belongs to, both in accounting
# units, because nothing else was paid in between.

test "6.32 machine-paid paytable wins" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, stimulus.play
  requires host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001C00
  host expect field meters[0].code == $1C
  host expect field meters[0].size >= 1
  host remember field meters[0].value as paytable_before
  host send lp AF data 0400001C00
  host expect field meters[0].code == $1C
  host expect field meters[0].value == saved.paytable_before
  host send lp 6F data 0400000100
  host expect field meters[0].code == 1
  host remember field meters[0].value as coin_out_before

  egm bill insert 2000
  egm play games=1 bet=1 win=10
  host expect meter total_coin_out +>= 1 within 5s

  # The win is the only thing paid, so 001C moves by exactly what coin out moved.
  host send lp 6F data 0400000100
  host expect field meters[0].code == 1
  host remember field meters[0].value as coin_out_after
  host send lp 6F data 0400001C00
  host expect field meters[0].code == $1C
  host expect field meters[0].value > saved.paytable_before
  host expect field meters[0].value == saved.paytable_before + saved.coin_out_after - saved.coin_out_before
  host send lp AF data 0400001C00
  host expect field meters[0].value == saved.paytable_before + saved.coin_out_after - saved.coin_out_before

  note "With double up configured, a double up taken and won moves 001C by the amount finally won."
  note "The replies start matching the audit screen."
}

test "6.33 machine-paid progressive wins" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001D00
  host expect field meters[0].code == $1D
  host expect field meters[0].size >= 1
  host remember field meters[0].value as progressive
  host send lp AF data 0400001D00
  host expect field meters[0].code == $1D
  host expect field meters[0].size >= 1
  host expect field meters[0].value == saved.progressive

  note "Meter 001D matches the audit screen in accounting units."
}

test "6.34 machine-paid external bonus wins" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001E00
  host expect field meters[0].code == $1E
  host expect field meters[0].size >= 1
  host remember field meters[0].value as bonus
  host send lp AF data 0400001E00
  host expect field meters[0].code == $1E
  host expect field meters[0].size >= 1
  host expect field meters[0].value == saved.bonus

  note "It is answered with a real width even where bonusing is switched off or not built in."
  note "Meter 001E matches the audit screen in accounting units."
}

test "6.35 the machine's paid wins add up to coin out" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0A000001001C001D001E00
  host expect field meters[0].code >= 1
  host expect field meters[3].code >= 1
  host expect field meters[3].code <= $1E
  host send lp AF data 0A000001001C001D001E00
  host expect field meters[3].code <= $1E

  # One meter per request, so each value is known to be the code asked for.
  host send lp 6F data 0400001C00
  host expect field meters[0].code == $1C
  host remember field meters[0].value as paytable
  host send lp 6F data 0400001D00
  host expect field meters[0].code == $1D
  host remember field meters[0].value as progressive
  host send lp 6F data 0400001E00
  host expect field meters[0].code == $1E
  host remember field meters[0].value as bonus
  host send lp 6F data 0400000100
  host expect field meters[0].code == $01
  host expect field meters[0].value == saved.paytable + saved.progressive + saved.bonus
  host remember field meters[0].value as coin_out

  # The same meters through $AF carry the same values.
  host send lp AF data 0400001C00
  host expect field meters[0].code == $1C
  host expect field meters[0].value == saved.paytable
  host send lp AF data 0400001D00
  host expect field meters[0].code == $1D
  host expect field meters[0].value == saved.progressive
  host send lp AF data 0400001E00
  host expect field meters[0].code == $1E
  host expect field meters[0].value == saved.bonus
  host send lp AF data 0400000100
  host expect field meters[0].code == $01
  host expect field meters[0].value == saved.coin_out

  note "Each code matches the audit screen."
}

test "6.36.1 an attendant-paid jackpot over the jackpot limit" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp1B, stimulus.settle
  requires stimulus.handpay.jackpot, stimulus.handpay.reset
  requires host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0400001F00
  host expect field meters[0].code == $1F
  host remember field meters[0].value as attendant_before
  host send lp AF data 0400001F00
  host expect field meters[0].code == $1F
  host expect field meters[0].value == saved.attendant_before
  host send lp 6F data 0400000200
  host expect field meters[0].code == 2
  host remember field meters[0].value as jackpot_before

  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount != 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect meter total_jackpot +>= 1 within 5s

  # The jackpot is the only thing an attendant paid, so 001F moves by exactly what 0002 moved.
  host send lp 6F data 0400000200
  host expect field meters[0].code == 2
  host remember field meters[0].value as jackpot_after
  host send lp 6F data 0400001F00
  host expect field meters[0].code == $1F
  host expect field meters[0].value > saved.attendant_before
  host expect field meters[0].value == saved.attendant_before + saved.jackpot_after - saved.jackpot_before
  host send lp AF data 0400001F00
  host expect field meters[0].value == saved.attendant_before + saved.jackpot_after - saved.jackpot_before

  note "Configure a low jackpot limit and, if offered, a partial pay of ten credits. 001F moves by the jackpot less what the hopper paid, in accounting units; that figure is the machine's to work out, so the gain is checked against 0002 instead."
  note "The replies end matching the audit screen."
}

test "6.36.2 an attendant-paid jackpot over the credit limit" {
  tag @egm-test-scripts @chapter-6 @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp1B, stimulus.play, machine.credit_limit
  requires host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001F00
  host expect field meters[0].code == $1F
  host remember field meters[0].value as attendant_before
  host send lp AF data 0400001F00
  host expect field meters[0].code == $1F
  host expect field meters[0].value == saved.attendant_before

  note "Configure a low credit limit and, if offered, a partial pay of ten credits; play to a win that takes the credit meter over it, read $1B and pay it by hand."
  note "001F moves by the win less what the hopper paid, in accounting units. Afterwards both replies agree and match the audit screen; the case has no step after the hand pay, so this is the person's to read."
}

test "6.37 attendant-paid progressive wins" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002000
  host expect field meters[0].code == $20
  host expect field meters[0].size >= 1
  host remember field meters[0].value as progressive
  host send lp AF data 0400002000
  host expect field meters[0].code == $20
  host expect field meters[0].size >= 1
  host expect field meters[0].value == saved.progressive

  note "Meter 0020 matches the audit screen in accounting units."
}

test "6.38 attendant-paid external bonus wins" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, host.compare_replies

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002100
  host expect field meters[0].code == $21
  host expect field meters[0].size >= 1
  host remember field meters[0].value as bonus
  host send lp AF data 0400002100
  host expect field meters[0].code == $21
  host expect field meters[0].size >= 1
  host expect field meters[0].value == saved.bonus

  note "It is answered with a real width even where bonusing is switched off or not built in."
  note "Meter 0021 matches the audit screen in accounting units."
}

test "6.39 the attendant's paid wins add up to the jackpot meter" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0A000002001F0020002100
  host expect field meters[0].code >= 2
  host expect field meters[3].code >= 2
  host expect field meters[3].code <= $21
  host send lp AF data 0A000002001F0020002100
  host expect field meters[3].code <= $21

  # One meter per request, so each value is known to be the code asked for.
  host send lp 6F data 0400001F00
  host expect field meters[0].code == $1F
  host remember field meters[0].value as paytable
  host send lp 6F data 0400002000
  host expect field meters[0].code == $20
  host remember field meters[0].value as progressive
  host send lp 6F data 0400002100
  host expect field meters[0].code == $21
  host remember field meters[0].value as bonus
  host send lp 6F data 0400000200
  host expect field meters[0].code == $02
  host expect field meters[0].value == saved.paytable + saved.progressive + saved.bonus
  host remember field meters[0].value as jackpot

  # The same meters through $AF carry the same values.
  host send lp AF data 0400001F00
  host expect field meters[0].code == $1F
  host expect field meters[0].value == saved.paytable
  host send lp AF data 0400002000
  host expect field meters[0].code == $20
  host expect field meters[0].value == saved.progressive
  host send lp AF data 0400002100
  host expect field meters[0].code == $21
  host expect field meters[0].value == saved.bonus
  host send lp AF data 0400000200
  host expect field meters[0].code == $02
  host expect field meters[0].value == saved.jackpot

  note "Each code matches the audit screen."
}

test "6.40 won credits is coin out plus jackpots" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 080000220001000200
  host expect field meters[0].code >= 1
  host expect field meters[2].code >= 1
  host expect field meters[2].code <= $22
  host send lp AF data 080000220001000200
  host expect field meters[2].code <= $22

  # One meter per request, so each value is known to be the code asked for.
  host send lp 6F data 0400000100
  host expect field meters[0].code == $01
  host remember field meters[0].value as coin_out
  host send lp 6F data 0400000200
  host expect field meters[0].code == $02
  host remember field meters[0].value as jackpot
  host send lp 6F data 0400002200
  host expect field meters[0].code == $22
  host expect field meters[0].value == saved.coin_out + saved.jackpot
  host remember field meters[0].value as won

  # The same meters through $AF carry the same values.
  host send lp AF data 0400000100
  host expect field meters[0].code == $01
  host expect field meters[0].value == saved.coin_out
  host send lp AF data 0400000200
  host expect field meters[0].code == $02
  host expect field meters[0].value == saved.jackpot
  host send lp AF data 0400002200
  host expect field meters[0].code == $22
  host expect field meters[0].value == saved.won

  note "Each code matches the audit screen."
}

test "6.41 hand-paid credits is cancelled credits plus jackpots" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF, host.compare_replies, host.meter_arithmetic

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 080000230003000200
  host expect field meters[0].code >= 2
  host expect field meters[2].code >= 2
  host expect field meters[2].code <= $23
  host send lp AF data 080000230003000200
  host expect field meters[2].code <= $23

  # One meter per request, so each value is known to be the code asked for.
  host send lp 6F data 0400000300
  host expect field meters[0].code == $03
  host remember field meters[0].value as cancelled
  host send lp 6F data 0400000200
  host expect field meters[0].code == $02
  host remember field meters[0].value as jackpot
  host send lp 6F data 0400002300
  host expect field meters[0].code == $23
  host expect field meters[0].value == saved.cancelled + saved.jackpot
  host remember field meters[0].value as hand_paid

  # The same meters through $AF carry the same values.
  host send lp AF data 0400000300
  host expect field meters[0].code == $03
  host expect field meters[0].value == saved.cancelled
  host send lp AF data 0400000200
  host expect field meters[0].code == $02
  host expect field meters[0].value == saved.jackpot
  host send lp AF data 0400002300
  host expect field meters[0].code == $23
  host expect field meters[0].value == saved.hand_paid

  note "Each code matches the audit screen."
}
