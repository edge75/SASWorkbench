# Chapter 4, sections 4.14 to 4.20 - power, memory faults and the operator.
#
# Seven cases about events a machine raises about itself rather than about a player. None of them
# can be produced here: they need the power cut, a memory device made to fail, a different program
# chip fitted, or somebody in the machine's configuration screens. Every one of them says "consult
# the manufacturer" in the document, which is the document admitting the same thing.

# Power applied always; power lost only if something in the machine stayed awake to notice.
test "4.14 AC power on and off" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.power_cycle

  note "With the link up, cut the machine's power and restore it. It reports power applied $17 on a general poll, once."
  note "It reports power lost $18 as well if its communications board keeps power when the machine does not, or if it can tell afterwards that power was lost. A machine that can do neither is not failing this."
}

# Three CMOS faults with three codes, and the codes still come out even if the fault cost the
# machine its address - it reports them once an address has been set again.
test "4.15 CMOS RAM errors" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.memory_fault

  note "Induce a recoverable CMOS error, a non-recoverable one and a bad device, if the machine can be made to. Each is reported on a general poll as $31, $32 and $33 - recovered, lost, bad device."
  note "Recoverable means the meters survived; non-recoverable means they did not. The codes are still owed if the fault reset the machine's address to zero - see 3.9."
}

test "4.16 EEPROM errors" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.memory_fault

  note "Induce an EEPROM data error and a bad EEPROM device, if the machine can be made to. They are reported on a general poll as $34 and $35."
}

test "4.17 EPROM errors" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.memory_fault

  note "Fit a different EPROM version and power up. The machine reports a changed checksum $36 on a general poll, once."
  note "Then create a bad EPROM checksum, if the machine can be made to: $37, once."
}

test "4.18 partitioned EPROM errors" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.memory_fault

  note "On a machine with a partitioned EPROM, fit a different version and power up: a changed partitioned checksum $38 on a general poll, once. Then create a bad partitioned checksum: $39, once."
}

# The reset is the operator's acknowledgement of the fault, and it has its own code.
test "4.19 memory error reset" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.memory_fault

  note "Induce a CMOS error: $32 on a general poll if the machine kept its address. Clear it at the machine: $3A. If the fault cost the machine its address, set the address again - both $32 and $3A then arrive on general polls."
  note "Repeat with an EEPROM error: $34, then $3A on the reset, and the same rule about a lost address."
}

# Any configuration change, including changing the machine's own poll address - which the host then
# has to follow to hear about it.
test "4.20 operator changed options" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires machine.configuration_options
  requires decoder.lp1F

  note "With the link up and no credits on the machine, enter its configuration, change the jackpot limit and leave: one $3C on a general poll. Change something else and leave: another $3C."
  note "Now change the machine's address to 38 decimal and leave. It stops answering. Poll address 38 instead: a $3C arrives there, and the identification poll addressed to 38 is answered. Set the address back to 1 afterwards."
  note "What counts as an option is the machine's own list; $3C covers denomination, address, and anything that changes the answer to long polls $1F, $53, $54, $56, $A0, $B2, $B3, $B4 or $B5."
}
