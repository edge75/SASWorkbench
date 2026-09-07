# Section 8 of the plan: endurance and recovery.
#
# The accounting denomination is stored state, and everything here asks the same question in a
# different way - does it survive? A machine that quietly reverted to cents after a power cut
# would under-report every meter from then on and look perfectly healthy doing it.

test "AD-42 the denomination survives a power failure mid-game" {
  tag @accounting-denomination @destructive
  requires decoder.lp1F
  requires machine.accounting_denomination
  requires machine.power_cycle
  requires stimulus.play

  machine accounting denomination 10c

  note "Configuration B. Cut power during a game, and again during a meter read. Restore power."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # The meters must be consistent, the denomination in force unchanged, and no memory error raised.
  host expect field denomination of lp 1F == 3
}

test "AD-43 the denomination survives losing the host and getting it back" {
  tag @accounting-denomination @slow
  requires decoder.lp6F
  requires machine.accounting_denomination
  requires machine.link_interruption

  machine accounting denomination 10c

  note "Configuration B. Disconnect the host for five minutes, then reconnect and read the meters again."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  note "The values continue in accounting units. Nothing reverts to cents."
}

test "AD-44 the denomination survives a restart of the protocol library" {
  tag @accounting-denomination
  requires decoder.lp1F
  requires machine.accounting_denomination
  requires machine.network_configuration

  machine accounting denomination 10c

  note "Configuration B. Change a network setting so the protocol library restarts."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # The value in force survives the restart and is published again.
  host expect field denomination of lp 1F == 3
}

test "AD-45 four hours of play show no drift" {
  tag @accounting-denomination @slow @needs-stimulus
  requires decoder.lp6F
  requires machine.accounting_denomination
  requires stimulus.play
  requires stimulus.real_time_events
  requires stimulus.legacy_bonus

  machine accounting denomination 10c

  note "Configuration B. Play continuously for four hours with real-time events and legacy bonusing enabled, polling throughout."
  note "There must be no drift between the audit menu's peso totals and the host's meters converted back, no warnings in the trace, and no lockups."
}
