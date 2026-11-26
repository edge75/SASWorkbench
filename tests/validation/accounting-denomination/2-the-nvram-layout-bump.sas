# Section 2 of the plan: the NVRAM layout bump.
#
# This release changes the stored layout, and the cost accepted for that is a memory error on any
# machine upgraded without a clear. Almost none of it is visible on the wire - what is being
# asserted is that the machine refuses to run, and a machine that refuses to run answers nothing.
# The cases are written out so the suite accounts for the whole plan, and each says what a person
# has to see.

test "AD-05 an upgrade without a memory clear raises a memory error" {
  tag @accounting-denomination @destructive @blocking
  requires machine.upgrade_in_place
  requires machine.audit_menu

  note "Take a machine on the previous release with meters on it. Upgrade to the build under test WITHOUT clearing NVRAM, and boot."
  note "The machine must raise a memory error and refuse to play, the trace must record the layout mismatch with the stored and current version numbers, and no meter value may be displayed or reported between the mismatch and the clear. Before this release a mismatch raised nothing and let the machine run on meters read at the wrong offsets."
}

test "AD-05b the memory error persists until the memory is cleared" {
  tag @accounting-denomination @destructive
  requires machine.upgrade_in_place
  requires machine.power_cycle

  note "Continue from AD-05. Power-cycle twice without clearing. The memory error is raised each time: only a master reset stamps the new layout revision."
}

test "AD-05c an illegal configured value reads as invalid rather than pending" {
  tag @accounting-denomination @needs-configuration
  requires machine.accounting_denomination
  requires machine.nvram_clear
  requires machine.audit_menu
  # Reading what a page of the machine's own menu shows takes a person; the software machine's menus only report entering and leaving.
  requires machine.audit_screens

  note "Configure an illegal value - 25 with the nine player denominations - clear NVRAM, boot, and open the SAS configuration audit page."
  note "The second row must read that the value is not valid for the denominations offered, not that it requires a memory clear. A clear would rewrite the same illegal value and change nothing, so telling the operator to clear would be telling them to do something useless."
}

test "AD-06 a memory clear recovers the machine" {
  tag @accounting-denomination @destructive
  requires decoder.lp1F
  requires machine.nvram_clear

  note "Continue from AD-05. Perform a memory clear from the audit menu and reboot."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  # The machine is answering again, which is the half of this case a host can see. All meters at
  # zero and the configuration re-applied from the file are the other half.
  host expect field denomination of lp 1F >= 1
}

# The bump must not turn every boot into a clear. A machine that lost its meters on an ordinary
# restart would be a far worse defect than the one this release accepted.
test "AD-07 meters survive an ordinary restart" {
  tag @accounting-denomination @needs-stimulus
  requires decoder.lp6F
  requires machine.power_cycle
  requires stimulus.play

  note "After AD-06, play ten games and note the meters."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 5s

  host send lp 6F data 0400000000
  host expect field meters[0].value >= 0

  note "Power-cycle the machine, then read the same meter again. It must be unchanged."
}
