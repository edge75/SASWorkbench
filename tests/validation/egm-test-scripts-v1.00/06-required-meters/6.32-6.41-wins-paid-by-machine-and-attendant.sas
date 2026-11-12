# Chapter 6, sections 6.32 to 6.41 - wins, split by who paid them and why.
#
# The machine's own payments break down into paytable, progressive and external bonus wins, which
# together make coin out; an attendant's payments break down the same way and together make the
# jackpot meter. Won credits is both totals, and hand-paid credits is the jackpot plus cancelled
# credits. Those sums are the point of 6.35 and 6.39 to 6.41, and a sum across several values is
# beyond this language today (host.compare_replies), so each is a note and the codes are asserted.
#
# None of 001C to 0023 has a single-meter long poll this host can read by name. Where a stimulus
# must move one, what is asserted is that the reply afterwards carries a value above zero - true of
# any machine that counted the win, fresh or not - and the exact movement is a note.

# Fails against the software machine, which adds a win to coin out and keeps no 001C breakdown.
test "6.32 machine-paid paytable wins" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, stimulus.bill, stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001C00
  host expect field meters[0].code == $1C
  host expect field meters[0].size >= 1
  host send lp AF data 0400001C00
  host expect field meters[0].code == $1C

  egm bill insert 2000
  egm play games=1 bet=1 win=10
  host expect meter total_coin_out +>= 1 within 5s
  host send lp 6F data 0400001C00
  host expect field meters[0].value >= 1
  host send lp AF data 0400001C00
  host expect field meters[0].value >= 1

  note "The win moves 001C by exactly its value in accounting units. With double up configured, a double up taken and won moves it by the amount finally won."
  note "Both replies agree after each step and start matching the audit screen."
}

test "6.33 machine-paid progressive wins" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001D00
  host expect field meters[0].code == $1D
  host expect field meters[0].size >= 1
  host send lp AF data 0400001D00
  host expect field meters[0].code == $1D
  host expect field meters[0].size >= 1

  note "Meter 001D carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.34 machine-paid external bonus wins" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001E00
  host expect field meters[0].code == $1E
  host expect field meters[0].size >= 1
  host send lp AF data 0400001E00
  host expect field meters[0].code == $1E
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where bonusing is switched off or not built in."
  note "Meter 001E carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.35 the machine's paid wins add up to coin out" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0A000001001C001D001E00
  host expect field meters[0].code >= 1
  host expect field meters[3].code >= 1
  host expect field meters[3].code <= $1E
  host send lp AF data 0A000001001C001D001E00
  host expect field meters[3].code <= $1E

  note "001C plus 001D plus 001E equals 0001, in each reply. Each code carries the same value in both replies and matches the audit screen."
}

# Fails against the software machine, which adds a jackpot to 0002 and keeps no 001F breakdown.
test "6.36.1 an attendant-paid jackpot over the jackpot limit" {
  tag @egm-test-scripts @chapter-6 @needs-stimulus @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp1B, stimulus.settle
  requires stimulus.handpay.jackpot, stimulus.handpay.reset

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp 6F data 0400001F00
  host expect field meters[0].code == $1F
  host send lp AF data 0400001F00
  host expect field meters[0].code == $1F

  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount != 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect meter total_jackpot +>= 1 within 5s

  host send lp 6F data 0400001F00
  host expect field meters[0].value >= 1
  host send lp AF data 0400001F00
  host expect field meters[0].value >= 1

  note "Configure a low jackpot limit and, if offered, a partial pay of ten credits. 001F moves by the jackpot less what the hopper paid, in accounting units."
  note "Both replies agree after each step and end matching the audit screen."
}

test "6.36.2 an attendant-paid jackpot over the credit limit" {
  tag @egm-test-scripts @chapter-6 @needs-a-person
  requires decoder.lp6F, decoder.lpAF, decoder.lp1B, stimulus.play, machine.credit_limit

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400001F00
  host expect field meters[0].code == $1F
  host send lp AF data 0400001F00
  host expect field meters[0].code == $1F

  note "Configure a low credit limit and, if offered, a partial pay of ten credits; play to a win that takes the credit meter over it, read $1B and pay it by hand."
  note "001F moves by the win less what the hopper paid, in accounting units. Both replies agree and end matching the audit screen."
}

test "6.37 attendant-paid progressive wins" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002000
  host expect field meters[0].code == $20
  host expect field meters[0].size >= 1
  host send lp AF data 0400002000
  host expect field meters[0].code == $20
  host expect field meters[0].size >= 1

  note "Meter 0020 carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.38 attendant-paid external bonus wins" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0400002100
  host expect field meters[0].code == $21
  host expect field meters[0].size >= 1
  host send lp AF data 0400002100
  host expect field meters[0].code == $21
  host expect field meters[0].size >= 1

  note "It is answered with a real width even where bonusing is switched off or not built in."
  note "Meter 0021 carries the same value in both replies, and it matches the audit screen in accounting units."
}

test "6.39 the attendant's paid wins add up to the jackpot meter" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 0A000002001F0020002100
  host expect field meters[0].code >= 2
  host expect field meters[3].code >= 2
  host expect field meters[3].code <= $21
  host send lp AF data 0A000002001F0020002100
  host expect field meters[3].code <= $21

  note "001F plus 0020 plus 0021 equals 0002, in each reply. Each code carries the same value in both replies and matches the audit screen."
}

test "6.40 won credits is coin out plus jackpots" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 080000220001000200
  host expect field meters[0].code >= 1
  host expect field meters[2].code >= 1
  host expect field meters[2].code <= $22
  host send lp AF data 080000220001000200
  host expect field meters[2].code <= $22

  note "0022 equals 0001 plus 0002, in each reply. Each code carries the same value in both replies and matches the audit screen."
}

test "6.41 hand-paid credits is cancelled credits plus jackpots" {
  tag @egm-test-scripts @chapter-6 @wire-only @needs-a-person
  requires decoder.lp6F, decoder.lpAF

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp 6F data 080000230003000200
  host expect field meters[0].code >= 2
  host expect field meters[2].code >= 2
  host expect field meters[2].code <= $23
  host send lp AF data 080000230003000200
  host expect field meters[2].code <= $23

  note "0023 equals 0003 plus 0002, in each reply. Each code carries the same value in both replies and matches the audit screen."
}
