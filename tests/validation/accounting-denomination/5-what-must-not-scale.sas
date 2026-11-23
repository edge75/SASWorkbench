# Section 5 of the plan: what must NOT scale with the accounting denomination.
#
# A value wrongly divided here under-reports money to the host, so these cases carry the same
# weight as the ones that must be divided. Every amount below is in centavos at configuration B
# and must read exactly as it would at one cent.

test "AD-25 ticket amounts stay in cents" {
  tag @accounting-denomination @needs-stimulus @blocking
  # The enhanced styles keep the printed record waiting for $4D, and this case never reads it.
  requires egm.validation == standard
  requires decoder.lp6F
  requires machine.accounting_denomination
  requires stimulus.ticket
  requires stimulus.button.collect
  requires printer.wallet

  machine accounting denomination 10c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # Thirteen pesos thirty in, cashed straight back out, then twelve pesos twenty in. Both amounts
  # carry odd centavos on purpose: the meters below are defined in cents and must show every one
  # of them, while the credit meters these same tickets feed are in accounting units.
  egm ticket insert amount=1330 validation=1111111111111111
  host expect exception ticket_inserted within 5s
  host send lp 70
  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=1330 parsing_code=0 validation_data=1111111111111111
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed

  egm press collect
  egm expect ticket printed within 20s

  egm ticket insert amount=1220 validation=2222222222222222
  host expect exception ticket_inserted within 5s
  host send lp 70
  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=1220 parsing_code=0 validation_data=2222222222222222
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed

  # The cents ticket meters: cashable ticket in and out, restricted ticket in and out.
  host send lp 6F data 0A00000D000E000F001000

  # Two thousand five hundred and fifty centavos, not two hundred and fifty-five.
  host expect field meters[0].code == 13
  host expect field meters[0].value == 2550
  host expect field meters[1].code == 14
  host expect field meters[1].value == 1330
}

# Two hundred pesos in and fifty out, as in-house cashable transfers addressed to asset 12345. The
# meters read are 00A0 (cashable in, cents) and 00B8 (cashable out to the host, cents); the case
# used to read 00A2, the restricted meter, which no cashable transfer moves.
test "AD-26 transfer amounts stay in cents" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp6F, decoder.lp72
  requires machine.accounting_denomination
  requires machine.aft, stimulus.asset_number, host.meter_arithmetic

  machine accounting denomination 10c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s
  egm set asset number 12345
  host forget exceptions within 2s

  host send lp 6F data 060000A000B800
  host remember field meters[0].value as in_before
  host remember field meters[1].value as out_before

  # 20000 cents in, read back once complete, which is what ends the transfer.
  host send lp 72 data 36000000000002000000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status == $40
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field cashable_amount == 20000

  # 5000 cents out to the host.
  host send lp 72 data 36000080000000500000000000000000000000003930000000000000000000000000000000000000000000000132000000000000000000
  host expect field transfer_status == $40
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field cashable_amount == 5000

  # Twenty thousand centavos and five thousand, not two thousand and five hundred.
  host send lp 6F data 060000A000B800
  host expect field meters[0].code == 160
  host expect field meters[0].value == saved.in_before + 20000
  host expect field meters[1].code == 184
  host expect field meters[1].value == saved.out_before + 5000
}

test "AD-27 the current transfer amounts stay in cents" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp74, decoder.lp72
  requires machine.accounting_denomination
  requires machine.aft, stimulus.asset_number, stimulus.button.collect

  machine accounting denomination 10c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s
  egm set asset number 12345

  # An empty machine, then a known balance of two hundred pesos put on by transfer.
  egm press collect
  host forget exceptions within 2s
  host send lp 72 data 36000000000002000000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
  host expect exception aft_transfer_complete within 10s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00

  # Two thousand credits at ten centavos, reported as twenty thousand centavos.
  host send lp 74 data FF000000
  host expect field current_cashable_amount == 20000
  host expect field current_restricted_amount == 0
  host expect field current_nonrestricted_amount == 0

  note "A wrong unit here refuses a legitimate cash-out. Restricted and non-restricted balances wait for a machine that holds them."
}

test "AD-28 progressive amounts stay in cents" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp84
  requires machine.accounting_denomination
  requires machine.progressives
  requires stimulus.progressive
  requires stimulus.bill

  machine accounting denomination 10c
  machine progressive non_sas levels 1

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # The software machine's own level stands at $10.00, so the reply carries 1000 cents and not 100 units.
  egm bill insert 1000
  egm progressive win level=1
  host send lp 84
  host expect field amount == 1000

  note "Configuration B, with a progressive level at a known amount. Section 10.4 puts these amounts in units of cents."
}

test "AD-29 counts stay counts" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp6F
  requires machine.accounting_denomination
  requires stimulus.bill
  requires stimulus.play

  machine accounting denomination 10c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # A known number of games, because a count can only be shown not to scale against a number
  # somebody knows. The plan reads this one off the audit page; here it is contrived.
  egm bill insert 1000
  egm play games=3 bet=2

  # Games played and won, ticket quantities, and the transfer counts. B9 is a quantity rather than
  # an amount, which is easy to get wrong because it sits among the transfer meters.
  host send lp 6F data 12000005000600110012001300B900BB00BD00

  host expect field meters[0].code == 5
  host expect field meters[0].value == 3
}

test "AD-30 the payback percentage stays a percentage" {
  tag @accounting-denomination
  requires decoder.lp6F
  requires machine.accounting_denomination

  machine accounting denomination 10c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # Hundredths of a percent, so a machine at 92.10 per cent reads 9210 whatever the denomination.
  host send lp 6F data 0400007F00

  host expect field meters[0].code == 127
  host expect field meters[0].value >= 0
}

test "AD-31 attendant keyed funds stay in cents" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp6F
  requires machine.accounting_denomination
  requires stimulus.attendant_keyed

  machine accounting denomination 10c

  note "Configuration B. Key on one hundred pesos and key off forty."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # These sit outside Table C-7 and the machine holds them in cents.
  host send lp 6F data 060000FA00FD00

  host expect field meters[0].code == 250
  host expect field meters[0].value == 10000
  host expect field meters[1].code == 253
  host expect field meters[1].value == 4000
}

# The door and power counts are refused by the selected-meter polls on purpose, so they are read
# with the legacy meters command instead. A suite that read them the other way would report them
# as unsupported and never notice.
test "AD-32 door and power counts are read from the legacy meters and are unscaled" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp1C
  requires machine.accounting_denomination
  requires stimulus.door.main
  requires stimulus.power_cycle

  machine accounting denomination 10c

  note "Configuration B. Open and close the main door twice, then power-cycle once."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 1C
  host expect field slot_door_opened == 2
  host expect field power_reset == 1
}
