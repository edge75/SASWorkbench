# Chapter 14, sections 14.6 to 14.9 - what kind of money a bonus is, and what it ignores.
#
# A bonus may carry cashable and non-restricted credits but never restricted ones. Non-restricted
# credits are played like any other and count as non-restricted play; a bonus too large for the
# credit meter or the jackpot limit is paid the way the machine pays any jackpot; and the parts of
# a transfer that only mean something for restricted credits - the expiry and the pool - are
# disregarded. A bonus never prints a receipt.
#
# The same two blockers as the rest of the chapter: no layout for long poll $72 (decoder.lp72) and
# no machine that offers AFT bonusing (machine.aft_bonus). The amounts here are asserted in credits
# against a one-cent accounting unit, so a machine in any other unit skips.

test "14.6 non-restricted bonus credits count as non-restricted play" {
  tag @egm-test-scripts @chapter-14 @needs-a-person
  requires machine.aft_bonus, decoder.lp72, host.meter_arithmetic
  requires stimulus.button.collect, stimulus.play, stimulus.settle
  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm press collect
  egm settle
  host forget exceptions within 2s

  host send lp 72 data 36000010000000000000000000000000000500013930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0

  # One credit played from the bonus moves coin in and non-restricted play by the same credit.
  host send lp 6F data 0600000000001A00
  host remember field meters[0].value as coin_in
  host remember field meters[1].value as nonrestricted_played
  egm play games=1 bet=1
  host send lp 6F data 0600000000001A00
  host expect field meters[0].value == saved.coin_in + 1
  host expect field meters[1].value == saved.nonrestricted_played + 1

  note "Before the run, enable AFT bonusing and leave the machine idle and playable. The document plays several wagers; each must move both meters by its own size in accounting credits."
}

test "14.7 a bonus too large for the machine is paid as a jackpot" {
  tag @egm-test-scripts @chapter-14 @needs-a-person
  requires machine.aft_bonus, decoder.lp72, decoder.lp1B
  requires machine.credit_limit, machine.jackpot_limit
  requires stimulus.handpay.reset, stimulus.settle
  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # Five hundred dollars, above both limits.
  host send lp 72 data 36000010000000000000000000000000050000013930000000000000000000000000000000000000000000000133000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field progressive_group of lp 1B == 0
  host expect field level of lp 1B == 0
  host expect field amount of lp 1B == 50000
  host expect field partial_pay of lp 1B == 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0

  # Eighty dollars: inside the credit limit, above the jackpot limit.
  host send lp 72 data 36000010000000000000000000000000008000013930000000000000000000000000000000000000000000000134000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field level of lp 1B == 0
  host expect field amount of lp 1B == 8000
  host expect field partial_pay of lp 1B == 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0

  note "Before the run, enable AFT bonusing, set the credit limit to one hundred dollars and the jackpot limit to seventy-five, with no partial pay, and leave the machine idle and playable."
  note "Then raise the jackpot limit to two hundred dollars and send a coin-out bonus of 150.00 (non-restricted, a new transaction identifier). It is now above the credit limit and inside the jackpot limit: it must still be paid as a hand pay, with the hand pay information shaped as above and its amount 15000."
}

# The expiry and pool of a bonus are ignored, and every credit it brings is cashed out with the rest.
test "14.8 a bonus ignores the expiry and the pool" {
  tag @egm-test-scripts @chapter-14 @needs-a-person
  requires machine.aft_bonus, decoder.lp72
  requires stimulus.button.collect, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  egm press collect
  egm settle
  host forget exceptions within 2s

  host send lp 72 data 36000010000000050000000000000000000500013930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0
  egm press collect
  egm settle
  host forget exceptions within 2s

  # An expiry already in the past and pool 2: neither stops the bonus, and neither is kept.
  host send lp 72 data 36000010000000050000000000000000000500013930000000000000000000000000000000000000000000000132010120200200000000
  host expect field transfer_status of lp 72 == $40
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0
  host expect field expiration of lp 72 == 0
  host expect field pool_id of lp 72 == 0
  egm press collect
  egm settle
  host forget exceptions within 2s

  # The same with no cashable part.
  host send lp 72 data 36000010000000000000000000000000000500013930000000000000000000000000000000000000000000000133010120200200000000
  host expect field transfer_status of lp 72 == $40
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0
  egm press collect

  note "Before the run, enable AFT bonusing and leave the machine idle and playable with nothing on the credit meter."
  note "After each bonus the cash out takes every cashable and non-restricted credit off the machine; a person confirms nothing is left on the meter, since a machine may pay by ticket, hopper or hand pay."
}

test "14.9 a bonus never prints a receipt" {
  tag @egm-test-scripts @chapter-14 @needs-a-person
  requires machine.aft_bonus, decoder.lp72, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  # Receipt requested, with a patron's name, account and balance as receipt data.
  host send lp 72 data 530000100000000500000000000000000000008139300000000000000000000000000000000000000000000001380000000000001D10094A6F6520536D6974681109313233343536373839130500002395000000
  host expect field transfer_status of lp 72 == $40
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == $92

  note "Before the run, enable AFT bonusing and leave the machine idle and playable. The document waits for $69 before the interrogation; a machine that refuses the receipt outright may never raise it, so the step does not."
}
