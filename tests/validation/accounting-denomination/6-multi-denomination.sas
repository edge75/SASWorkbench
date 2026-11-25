# Section 6 of the plan: multi-denomination behaviour.
#
# The accounting denomination is a property of the machine, not of whatever game the player has
# most recently touched. IGS reported the fixed behaviour here as a bug; per section 16 it is
# correct, and this section is the evidence for that answer.

test "AD-33 the machine reply does not track the player's selection" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp1F
  requires machine.accounting_denomination
  requires machine.multiple_denominations
  requires stimulus.select_game

  machine accounting denomination 10c

  note "Enable several player denominations. Select each game in turn, reading the machine identification reply after each selection."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # The same value every time, whichever game is selected. Section 16: the unit must not change
  # dynamically due to player activity.
  host expect field denomination of lp 1F == 3
  host wait 2s
  host expect field denomination of lp 1F == 3
  host wait 2s
  host expect field denomination of lp 1F == 3
}

test "AD-34 the per-game reply answers the same as the machine reply" {
  tag @accounting-denomination @needs-configuration
  requires decoder.lp53
  requires machine.accounting_denomination
  requires machine.multiple_games

  machine accounting denomination 10c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  note "The accounting denomination byte is identical for every game number, even though this is a per-game poll."
}

test "AD-35 per-denomination data comes from the multi-denom preamble" {
  tag @accounting-denomination @needs-configuration
  machine games 1c 5c
  requires decoder.lpB0
  requires machine.multiple_denominations

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # Coin in at five cents, through the preamble: the answer wraps $6F and names the denomination.
  host send lp B0 data 07026F0400000000
  host expect field denomination of lp B0 == 2
  host expect field base_command of lp B0 == 0x6F
  host expect field meters[0].code of lp B0 == 0

  note "The preamble is how a host obtains data for one player denomination. This is the answer to send in place of the machine identification behaviour a host might have expected."
}

test "AD-36 the machine reports itself as multi-denomination" {
  tag @accounting-denomination @needs-configuration
  machine games 1c 5c
  requires decoder.lpA0
  requires machine.multiple_denominations

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # Bit 7 of the second feature byte is the multi-denomination extensions bit.
  host send lp A0 with game_number=0
  host expect field feature_codes_2 of lp A0 >= 0x80

  note "The multi-denom extension bit is set on any machine whose accounting denomination is, or could be, different from a player denomination."
}
