# Section 3 of the plan: configuring the accounting denomination.
#
# The whole section reads one byte - the accounting denomination in the machine identification
# reply - and asserts what the machine did with what it was configured with. Table C-4 code 01 is
# one cent and code 03 is ten centavos, so a case that expects a fallback expects 01.

test "AD-08 an absent key defaults to one cent" {
  tag @accounting-denomination @needs-configuration
  requires decoder.lp1F
  requires machine.config_file
  requires machine.nvram_clear

  note "Remove the accounting denomination key entirely, clear NVRAM, and boot."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host expect field denomination of lp 1F == 1
}

test "AD-09 a legal value is accepted" {
  tag @accounting-denomination @needs-configuration
  requires decoder.lp1F
  requires machine.accounting_denomination
  requires machine.config_file
  requires machine.nvram_clear

  note "Set the accounting denomination to 10 with the nine player denominations enabled, clear NVRAM, and boot."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host expect field denomination of lp 1F == 3
}

test "AD-09b the per-game reply agrees with the machine reply" {
  tag @accounting-denomination @needs-configuration
  requires decoder.lp53
  requires machine.accounting_denomination
  requires machine.config_file
  requires machine.nvram_clear

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 53 data 0000
  host expect field denomination == 3
}

# The regulatory property of the whole change: a configured value that has not been through a
# memory clear must not be in force, however plainly it is written in the file.
test "AD-10 a change does not take effect without a memory clear" {
  tag @accounting-denomination @needs-configuration @blocking
  requires decoder.lp1F
  requires machine.config_file

  note "On a machine in force at one cent, edit the file to 10 and restart WITHOUT clearing NVRAM."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # Still the old value. The audit page is where the pending value is shown, and that half of the
  # case cannot be read over the wire.
  host expect field denomination of lp 1F == 1
}

test "AD-11 and takes effect after one" {
  tag @accounting-denomination @needs-configuration
  requires decoder.lp1F
  requires machine.config_file
  requires machine.nvram_clear

  note "Continue from AD-10: clear NVRAM and reboot."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host expect field denomination of lp 1F == 3
}

# Twenty-five does not divide twenty, and section 16 requires every player denomination to divide
# evenly by the accounting one. The machine reports in cents instead, and keeps playing.
test "AD-12 an indivisible value is refused and the machine reports in cents" {
  tag @accounting-denomination @needs-configuration
  requires decoder.lp1F
  requires machine.accounting_denomination
  requires machine.config_file
  requires machine.nvram_clear

  note "Set the accounting denomination to 25 with the nine player denominations enabled, clear NVRAM, and boot."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host expect field denomination of lp 1F == 1
}

test "AD-13 a value the protocol cannot express is refused" {
  tag @accounting-denomination @needs-configuration
  requires decoder.lp1F
  requires machine.accounting_denomination
  requires machine.config_file
  requires machine.nvram_clear

  note "Set the accounting denomination to 30, which is not in Table C-4. Clear NVRAM and boot."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host expect field denomination of lp 1F == 1
}

# AD-14 and AD-15 are start-up and audit-menu behaviour with nothing on the wire to assert on.
# They are written so the suite accounts for every case in the plan rather than only the ones a
# host can see, and they skip by name until the machine can be driven that far.
test "AD-14 a malformed value is refused at start-up" {
  tag @accounting-denomination @needs-configuration
  requires machine.config_file
  requires machine.startup_log

  note "Set the key to \"ten\", then 0, then -5. Each must be a start-up configuration error naming the key, and the machine must not silently default."
}

test "AD-15 the value is not operator-editable" {
  tag @accounting-denomination @needs-configuration
  requires machine.audit_menu

  note "On a machine in force at ten centavos, the audit page's accounting denomination row is displayed only, with no edit control. Section 16 requires the unit not to change dynamically."
}
