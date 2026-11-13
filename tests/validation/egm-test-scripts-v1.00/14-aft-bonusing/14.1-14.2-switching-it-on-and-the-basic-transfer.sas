# Chapter 14, sections 14.1 and 14.2 - whether the machine takes a bonus over AFT at all, and the
# nine transfers that say what it does with one.
#
# An AFT bonus is a funds transfer of type $10 (paid to the credit meter as a win) or $11 (paid
# as a jackpot, which on a low jackpot limit means a hand pay). The host sends it with transfer
# code $00 and index $00, and then asks how it went with the interrogation form, transfer code $FF.
# Every request in this chapter carries asset number 12345, no registration key unless a case is
# about one, and a transaction identifier no earlier request in the case used.
#
# Two things stop the whole chapter here. This build has no layout for long poll $72, so neither
# the request it answers nor the status it reports can be read (decoder.lp72); and the software
# machine offers no bonusing of either kind, which a case states as machine.aft_bonus. The cases
# are written out in full regardless, so the day both exist they run without being rewritten.
#
# The document's host simulator leaves the hand pay exception unacknowledged throughout, so that a
# case can read the hand pay itself. Nothing here needs that: a case that expects $51 waits for it
# like any other exception.

# Bonusing switched off at the operator menu: the enabled-features bit says so, and the bonus
# requests of both kinds are either ignored or refused.
test "14.1 AFT bonusing can be switched off" {
  tag @egm-test-scripts @chapter-14 @needs-a-person
  requires machine.aft_bonus, decoder.lp72

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  host send lp A0 with game_number=0
  host expect field game_number == 0

  # A coin-out bonus and then a jackpot bonus, each followed by the interrogation.
  host send lp 72 data 36000010000000050000000000000000000000013930000000000000000000000000000000000000000000000131000000000000000000
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 >= $80
  host send lp 72 data 36000011000000050000000000000000000000013930000000000000000000000000000000000000000000000132000000000000000000
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 >= $80

  note "Before the run, find the operator setting that turns AFT bonusing on and off - a machine may keep one switch for both kinds of bonusing or one for each - and turn it on. Read the enabled features for game 0: bit 1 of the first feature byte is set."
  note "Then turn it off, read them again, and run the steps: bit 1 is now clear. This build names no single bit of that byte, so both readings are for a person to check in the frames."
  note "With it off, the document accepts silence as well as a refusal for each transfer and each interrogation. The steps assert the refusal; a machine that stays silent instead passes by the frames."
}

# Six transfers that succeed and three that must not, from the document's two tables. A credit
# meter moves by the amount in credits, a cents meter by the amount in cents, and a count by one.
test "14.2 the basic bonus transfers" {
  tag @egm-test-scripts @chapter-14 @needs-a-person
  requires machine.aft_bonus, decoder.lp72
  requires host.meter_arithmetic, stimulus.handpay.reset
  machine accounting denomination 1c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # Line 1: a coin-out bonus, cashable.
  host send lp 6F data 0E000001001E00AE00AF00B000B100
  host remember field meters[0].value as t1_m0
  host remember field meters[1].value as t1_m1
  host remember field meters[2].value as t1_m2
  host remember field meters[3].value as t1_m3
  host remember field meters[4].value as t1_m4
  host remember field meters[5].value as t1_m5
  host send lp 72 data 36000010000000050000000000000000000000013930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0
  host send lp 6F data 0E000001001E00AE00AF00B000B100
  host expect field meters[0].value == saved.t1_m0 + 500
  host expect field meters[1].value == saved.t1_m1 + 500
  host expect field meters[2].value == saved.t1_m2 + 500
  host expect field meters[3].value == saved.t1_m3 + 1
  host expect field meters[4].value == saved.t1_m4
  host expect field meters[5].value == saved.t1_m5

  # Line 2: a coin-out bonus, non-restricted.
  host send lp 6F data 0E000001001E00AE00AF00B000B100
  host remember field meters[0].value as t2_m0
  host remember field meters[1].value as t2_m1
  host remember field meters[2].value as t2_m2
  host remember field meters[3].value as t2_m3
  host remember field meters[4].value as t2_m4
  host remember field meters[5].value as t2_m5
  host send lp 72 data 36000010000000000000000000000000000500013930000000000000000000000000000000000000000000000132000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0
  host send lp 6F data 0E000001001E00AE00AF00B000B100
  host expect field meters[0].value == saved.t2_m0 + 500
  host expect field meters[1].value == saved.t2_m1 + 500
  host expect field meters[2].value == saved.t2_m2
  host expect field meters[3].value == saved.t2_m3
  host expect field meters[4].value == saved.t2_m4 + 500
  host expect field meters[5].value == saved.t2_m5 + 1

  # Line 3: a coin-out bonus carrying both kinds.
  host send lp 6F data 0E000001001E00AE00AF00B000B100
  host remember field meters[0].value as t3_m0
  host remember field meters[1].value as t3_m1
  host remember field meters[2].value as t3_m2
  host remember field meters[3].value as t3_m3
  host remember field meters[4].value as t3_m4
  host remember field meters[5].value as t3_m5
  host send lp 72 data 36000010000000050000000000000000000500013930000000000000000000000000000000000000000000000133000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0
  host send lp 6F data 0E000001001E00AE00AF00B000B100
  host expect field meters[0].value == saved.t3_m0 + 1000
  host expect field meters[1].value == saved.t3_m1 + 1000
  host expect field meters[2].value == saved.t3_m2 + 500
  host expect field meters[3].value == saved.t3_m3 + 1
  host expect field meters[4].value == saved.t3_m4 + 500
  host expect field meters[5].value == saved.t3_m5 + 1

  # Line 4: a jackpot bonus, cashable. The machine locks up in a hand pay, which is reset here.
  host send lp 6F data 0E000002002100AE00AF00B000B100
  host remember field meters[0].value as t4_m0
  host remember field meters[1].value as t4_m1
  host remember field meters[2].value as t4_m2
  host remember field meters[3].value as t4_m3
  host remember field meters[4].value as t4_m4
  host remember field meters[5].value as t4_m5
  host send lp 72 data 36000011000000050000000000000000000000013930000000000000000000000000000000000000000000000134000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0
  host send lp 6F data 0E000002002100AE00AF00B000B100
  host expect field meters[0].value == saved.t4_m0 + 500
  host expect field meters[1].value == saved.t4_m1 + 500
  host expect field meters[2].value == saved.t4_m2 + 500
  host expect field meters[3].value == saved.t4_m3 + 1
  host expect field meters[4].value == saved.t4_m4
  host expect field meters[5].value == saved.t4_m5

  # Line 5: a jackpot bonus, non-restricted.
  host send lp 6F data 0E000002002100AE00AF00B000B100
  host remember field meters[0].value as t5_m0
  host remember field meters[1].value as t5_m1
  host remember field meters[2].value as t5_m2
  host remember field meters[3].value as t5_m3
  host remember field meters[4].value as t5_m4
  host remember field meters[5].value as t5_m5
  host send lp 72 data 36000011000000000000000000000000000500013930000000000000000000000000000000000000000000000135000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0
  host send lp 6F data 0E000002002100AE00AF00B000B100
  host expect field meters[0].value == saved.t5_m0 + 500
  host expect field meters[1].value == saved.t5_m1 + 500
  host expect field meters[2].value == saved.t5_m2
  host expect field meters[3].value == saved.t5_m3
  host expect field meters[4].value == saved.t5_m4 + 500
  host expect field meters[5].value == saved.t5_m5 + 1

  # Line 6: a jackpot bonus carrying both kinds.
  host send lp 6F data 0E000002002100AE00AF00B000B100
  host remember field meters[0].value as t6_m0
  host remember field meters[1].value as t6_m1
  host remember field meters[2].value as t6_m2
  host remember field meters[3].value as t6_m3
  host remember field meters[4].value as t6_m4
  host remember field meters[5].value as t6_m5
  host send lp 72 data 36000011000000050000000000000000000500013930000000000000000000000000000000000000000000000136000000000000000000
  host expect field transfer_status of lp 72 == $40
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == 0
  host send lp 6F data 0E000002002100AE00AF00B000B100
  host expect field meters[0].value == saved.t6_m0 + 1000
  host expect field meters[1].value == saved.t6_m1 + 1000
  host expect field meters[2].value == saved.t6_m2 + 500
  host expect field meters[3].value == saved.t6_m3 + 1
  host expect field meters[4].value == saved.t6_m4 + 500
  host expect field meters[5].value == saved.t6_m5 + 1

  # Refusal 1: a coin-out bonus that asks for restricted credits, which a bonus cannot carry.
  host send lp 72 data 36000010000000050000000005000000000000013930000000000000000000000000000000000000000000000137000000000000000000
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 >= $80

  # Refusal 2: the same for a jackpot bonus.
  host send lp 72 data 36000011000000050000000005000000000000013930000000000000000000000000000000000000000000000138000000000000000000
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 >= $80

  # Refusal 3: a jackpot bonus carrying a registration key, which this machine never issued.
  host send lp 72 data 36000011000000050000000000000000000000013930000001000000000000000000000000000000000000000139000000000000000000
  host send lp 72 data 02FF00
  host expect field transfer_status of lp 72 == $88

  note "Before the run, enable AFT bonusing, set the jackpot limit high enough that a five-dollar coin-out bonus is not a hand pay and low enough that a five-dollar jackpot bonus is, and leave the machine idle and playable."
  note "Each immediate reply to a transfer carries the request back - the same transaction identifier, type and three amounts - with a length byte equal to the reply less five bytes, asset number 39 30 00 00, receipt status $FF and pool 00 00. Not asserted field by field until $72 has a layout."
  note "For each refusal the immediate reply may carry $40 or a status at or above $80; what must hold is the status the interrogation reports, and that no meter moved."
}
