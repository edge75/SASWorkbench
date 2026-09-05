# Section 5 of the plan: what must NOT scale with the accounting denomination.
#
# A value wrongly divided here under-reports money to the host, so these cases carry the same
# weight as the ones that must be divided. Every amount below is in centavos at configuration B
# and must read exactly as it would at one cent.

test "AD-25 ticket amounts stay in cents" {
  tag @accounting-denomination @needs-stimulus @blocking
  requires decoder.lp6F
  requires machine.accounting_denomination
  requires stimulus.ticket

  note "Configuration B. Print a ticket for twenty-five pesos fifty, and redeem one for thirteen pesos thirty."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # The cents ticket meters: cashable ticket in and out, restricted ticket in and out.
  host send lp 6F data 0A00000D000E000F001000

  # Two thousand five hundred and fifty centavos, not two hundred and fifty-five.
  host expect field meters[0].code == 13
  host expect field meters[0].value == 2550
  host expect field meters[1].code == 14
  host expect field meters[1].value == 1330
}

test "AD-26 transfer amounts stay in cents" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp6F
  requires machine.accounting_denomination
  requires stimulus.aft

  note "Configuration B. Transfer two hundred pesos in and fifty pesos out."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 6F data 060000A000A200

  host expect field meters[0].code == 160
  host expect field meters[0].value == 20000
}

test "AD-27 the current transfer amounts stay in cents" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp74
  requires machine.accounting_denomination
  requires stimulus.aft

  note "Configuration B, with a known balance. The current cashable, restricted and nonrestricted amounts are all in centavos. A wrong unit here refuses a legitimate cash-out."
}

test "AD-28 progressive amounts stay in cents" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp84
  requires machine.accounting_denomination
  requires stimulus.progressive

  note "Configuration B, with a progressive level at a known amount. Section 10.4 puts these amounts in units of cents."
}

test "AD-29 counts stay counts" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp6F
  requires machine.accounting_denomination
  requires stimulus.play

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # Games played and won, ticket quantities, and the transfer counts. B9 is a quantity rather than
  # an amount, which is easy to get wrong because it sits among the transfer meters.
  host send lp 6F data 12000005000600110012001300B900BB00BD00

  host expect field meters[0].code == 5
  host expect field meters[0].value >= 0
}

test "AD-30 the payback percentage stays a percentage" {
  tag @accounting-denomination
  requires decoder.lp6F
  requires machine.accounting_denomination

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

  note "Configuration B. Open and close the main door twice, then power-cycle once."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 1C
  host expect field slot_door_opened == 2
  host expect field power_reset == 1
}
