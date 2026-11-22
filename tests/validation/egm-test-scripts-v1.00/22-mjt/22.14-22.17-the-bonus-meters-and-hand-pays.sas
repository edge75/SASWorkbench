# Chapter 22, sections 22.14 to 22.17 - which meters the extra part of a multiplied win lands on.
#
# The extra part is a legacy bonus, so it lands on the legacy bonus meter for its tax status -
# deductible unless bit 7 of the multiplier is set - which long poll $9A reports. It also lands on
# one of the two external bonus meters: 001E when the machine pays it to the credit meter, 0021
# when the win is over the jackpot limit and an attendant pays it by hand. In the hand pay case the
# $7C arrives only after the hand pay is reset.
#
# The software machine offers no multiplied jackpots, so all four skip on machine.multiplied_jackpot;
# the hand pay pair also needs a jackpot limit set below the win (machine.jackpot_limit).

test "22.14 a deductible multiplied win paid to the credit meter" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  requires machine.legacy_bonus, machine.multiplied_jackpot, host.meter_arithmetic
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.lp6F, decoder.lp9A, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  host send lp 6F data 0600001E002100
  host remember field meters[0].value as machine_paid
  host remember field meters[1].value as attendant_paid
  host send lp 9A data 0000
  host remember field deductible as deductible

  host send lp 8B data 0000000099999999030000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 20 within 8s
  host expect field tax_status of event legacy_bonus_pay_or_jackpot_awarded == 0 within 8s
  host send lp 8B data 0000000000000000010100

  host send lp 9A data 0000
  host expect field deductible == saved.deductible + 20
  host send lp 6F data 0600001E002100
  host expect field meters[0].value == saved.machine_paid + 20
  host expect field meters[1].value == saved.attendant_paid

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. The $9A reply carries game number 0000 and the three legacy bonus meters in accounting credits; they and meters 001E and 0021 must match the machine's own display, where it has one (D40)."
}

test "22.15 a non-deductible multiplied win paid to the credit meter" {
  tag @egm-test-scripts @chapter-22 @needs-stimulus
  requires machine.legacy_bonus, machine.multiplied_jackpot, host.meter_arithmetic
  requires stimulus.real_time_events, stimulus.bill, stimulus.play
  requires decoder.lp0E, decoder.lp6F, decoder.lp9A, decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  host send lp 6F data 0600001E002100
  host remember field meters[0].value as machine_paid
  host remember field meters[1].value as attendant_paid
  host send lp 9A data 0000
  host remember field deductible as deductible
  host remember field non_deductible as non_deductible

  # Three times, with bit 7 marking it non-deductible.
  host send lp 8B data 0000000099999999830000
  egm play games=1 bet=1 win=10
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == $83 within 8s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 20 within 8s
  host send lp 8B data 0000000000000000010100

  host send lp 9A data 0000
  host expect field non_deductible == saved.non_deductible + 20
  host expect field deductible == saved.deductible
  host send lp 6F data 0600001E002100
  host expect field meters[0].value == saved.machine_paid + 20
  host expect field meters[1].value == saved.attendant_paid

  note "Before the run, enable legacy bonusing and multiplied jackpots at the machine. The meters must match the machine's own display, where it has one (D40)."
  note "The document lists the event's multiplier as plain 3 here; the specification puts the non-deductible flag in bit 7 of that byte, as the document itself shows for four times in 22.20, so this case expects $83."
}

test "22.16 a deductible multiplied win paid by hand" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  requires machine.legacy_bonus, machine.multiplied_jackpot, machine.jackpot_limit
  requires host.meter_arithmetic, stimulus.real_time_events, stimulus.bill, stimulus.play
  requires stimulus.handpay.reset, decoder.lp0E, decoder.lp1B, decoder.lp6F, decoder.lp9A
  requires decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  host send lp 6F data 0600001E002100
  host remember field meters[0].value as machine_paid
  host remember field meters[1].value as attendant_paid
  host send lp 9A data 0000
  host remember field deductible as deductible

  host send lp 8B data 0000000099999999030000
  egm play games=1 bet=1 win=2000
  host expect exception handpay_is_pending within 10s
  host expect field win_amount of event game_ended != 0 within 8s
  host send lp 1B
  host expect field amount != 0
  host expect field partial_pay == 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == 3 within 15s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 4000 within 15s
  host send lp 8B data 0000000000000000010100

  host send lp 9A data 0000
  host expect field deductible == saved.deductible + 4000
  host send lp 6F data 0600001E002100
  host expect field meters[0].value == saved.machine_paid
  host expect field meters[1].value == saved.attendant_paid + 4000

  note "Before the run, enable legacy bonusing and multiplied jackpots, set the jackpot limit below 2000 credits and switch partial pay off; the host must not ignore exception 51."
  note "The hand pay amount must be the jackpot the machine shows; the document does not say whether that is the game's win or the multiplied total, so the amount is a person's to compare. The meters must match the machine's own display (D40)."
}

test "22.17 a non-deductible multiplied win paid by hand" {
  tag @egm-test-scripts @chapter-22 @needs-a-person
  requires machine.legacy_bonus, machine.multiplied_jackpot, machine.jackpot_limit
  requires host.meter_arithmetic, stimulus.real_time_events, stimulus.bill, stimulus.play
  requires stimulus.handpay.reset, decoder.lp0E, decoder.lp1B, decoder.lp6F, decoder.lp9A
  requires decoder.event7F, decoder.event7C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host send lp 0E data 01
  egm bill insert 2000
  host forget exceptions within 2s

  host send lp 6F data 0600001E002100
  host remember field meters[0].value as machine_paid
  host remember field meters[1].value as attendant_paid
  host send lp 9A data 0000
  host remember field non_deductible as non_deductible

  host send lp 8B data 0000000099999999830000
  egm play games=1 bet=1 win=2000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount != 0
  host expect field partial_pay == 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect field multiplier of event legacy_bonus_pay_or_jackpot_awarded == $83 within 15s
  host expect field multiplied_win of event legacy_bonus_pay_or_jackpot_awarded == 4000 within 15s
  host send lp 8B data 0000000000000000010100

  host send lp 9A data 0000
  host expect field non_deductible == saved.non_deductible + 4000
  host send lp 6F data 0600001E002100
  host expect field meters[0].value == saved.machine_paid
  host expect field meters[1].value == saved.attendant_paid + 4000

  note "Before the run, enable legacy bonusing and multiplied jackpots, set the jackpot limit below 2000 credits and switch partial pay off; the host must not ignore exception 51."
  note "As in 22.15, the document writes the multiplier as plain 3 and this case expects bit 7 set. The hand pay amount and the meters are compared with the machine's display by a person (D40)."
}
