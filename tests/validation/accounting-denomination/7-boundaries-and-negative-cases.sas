# Section 7 of the plan: boundaries and negative cases.
#
# Truncation, remainders, field widths, and the values the machine must refuse.

# Rounding up would create money that the machine does not hold, so a remainder is always dropped.
test "AD-37 truncation never rounds up" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp6F
  requires machine.accounting_denomination
  requires stimulus.play

  machine accounting denomination 10c

  # A wager of twenty-five centavos needs a game credit worth twenty-five centavos, and nothing
  # here can set what a game credit is worth - the software machine plays in whole pesos, so every
  # coin-in it can produce is already a whole number of accounting units and would demonstrate
  # nothing. The case skips naming that rather than asserting a truncation it never caused.
  requires machine.game_credit_value

  note "Configuration B. Contrive twenty-five centavos of coin in on a single game."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # Twenty-five centavos at ten centavos is two units and a remainder of five, and the answer is
  # two. Three would be the machine reporting money it never took.
  host send lp 6F data 0400000000
  host expect field meters[0].value == 2
}

# A ticket, a note or a transfer carries any number of cents, and section 16's divisibility rule
# constrains only the denominations a player may choose - so a remainder on a meter fed in cents
# is inherent rather than a defect, and no warning is traced for it.
test "AD-38 a remainder on a meter fed in cents is normal" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp6F
  requires machine.accounting_denomination
  requires stimulus.ticket

  machine accounting denomination 10c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # Twenty-five pesos fifty-three, which the accounting denomination does not divide.
  egm ticket insert amount=2553 validation=3333333333333333
  host expect exception ticket_inserted within 5s
  host send lp 70
  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=2553 parsing_code=0 validation_data=3333333333333333
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed

  # Two thousand five hundred and fifty-three centavos truncates to two hundred and fifty-five
  # units. Record the shortfall for the certification pack.
  host send lp 6F data 0400001500
  host expect field meters[0].code == 21
  host expect field meters[0].value == 255
}

# The selected-meter polls widen a meter as its value grows, which is what stops them rolling
# over silently the way the fixed-width polls do.
test "AD-39 a meter near its limit is carried at full width" {
  tag @accounting-denomination @slow @needs-stimulus
  requires decoder.lp6F
  requires machine.accounting_denomination
  requires stimulus.ticket
  requires stimulus.play

  machine accounting denomination 10c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # Ten million pesos, loaded in one document rather than driven up a note at a time, and wagered
  # in one game. A hundred million accounting units needs nine digits and so cannot be carried in
  # four bytes.
  egm ticket insert amount=1000000000 validation=4444444444444444
  host expect exception ticket_inserted within 5s
  host send lp 70
  host send lp 71 with transfer_code=ttc_valid_cashable_ticket transfer_amount=1000000000 parsing_code=0 validation_data=4444444444444444
  host send lp 71 with transfer_code=ttc_request_for_current_ticket_status
  host expect field machine_status of lp 71 == gmsc_cashable_ticket_redeemed

  egm play games=1 bet=10000000

  # The size byte is what proves it: a machine that never widened the field would answer four
  # bytes and lose the top digits.
  host send lp 6F data 0400000000
  host expect field meters[0].size >= 5
}

# Recorded, not blocked. This is existing behaviour, is neither caused nor cured by the change
# under test, and has been deferred - so the case exists to capture what is observed rather than
# to turn the suite red over something nobody is fixing in this release.
test "AD-40 a win too large for its field is reported modulo the field" {
  tag @accounting-denomination @known-limitation
  requires decoder.lp7F
  requires machine.accounting_denomination
  requires stimulus.play

  machine accounting denomination 10c

  note "Configuration B. Contrive, or simulate at the host, a single game win above ten million pesos so that it exceeds a four-byte field."
  note "The value is reported modulo the field, so a large enough win reports as zero. Record what is observed for the certification pack and do not raise it as a regression."
}

# The table holds these codes but their value is zero, so a build that accepted one would divide
# by zero rather than refuse.
test "AD-41 a sub-cent accounting denomination is refused" {
  tag @accounting-denomination @needs-configuration
  requires decoder.lp1F
  requires machine.accounting_denomination
  requires machine.nvram_clear

  machine accounting denomination 10c

  note "Attempt to set a sub-cent accounting denomination, Table C-4 codes 1B to 1F. It must be refused with the invalid-denomination error."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host expect field denomination of lp 1F == 1
}
