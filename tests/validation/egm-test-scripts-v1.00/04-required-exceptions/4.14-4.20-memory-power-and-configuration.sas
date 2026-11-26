# Chapter 4, sections 4.14 to 4.20 - power, memory faults and the operator.
#
# Seven cases about events a machine raises about itself rather than about a player. On a real
# machine they need the power cut, a memory device made to fail, a different program chip fitted,
# or somebody in the configuration screens, and every one says "consult the manufacturer" in the
# document. A fault some machines never report has a capability of its own, so a case needing one
# skips on such a machine rather than failing after a step that cannot happen; the software machine,
# like its platform, reports only $32, $36 and $3A among them.

# Power applied always; power lost only if something in the machine stayed awake to notice.
test "4.14 AC power on and off" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.power_cycle, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm power cycle
  # The person confirms once the machine has booted, so only the link is awaited here.
  host expect link up within 120s

  # Power applied, once.
  host expect exception ac_power_applied within 30s
  host expect no exception ac_power_applied within 10s

  note "It reports power lost $18 as well if its communications board keeps power when the machine does not, or if it can tell afterwards that power was lost. A machine that can do neither is not failing this, so $18 is not asserted."
}

# Three CMOS faults with three codes, and the codes still come out even if the fault cost the
# machine its address - it reports them once an address has been set again.
test "4.15 CMOS RAM errors" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.memory_fault, stimulus.settle, stimulus.bill, decoder.lp1C, host.compare_replies
  requires machine.memory_fault.cmos_recovered, machine.memory_fault.cmos_bad_device

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # A note first, so the meters a recovered fault must keep are not all zero.
  egm bill insert 2000
  host expect meter total_drop +>= 1 within 5s
  host send lp 1C
  host remember field total_coin_in as coin_in
  host remember field total_coin_out as coin_out
  host remember field total_drop as drop
  host remember field games_played as games_played

  # Recovered, lost, bad device: $31, $32 and $33, one fault at a time.
  egm memory fault cmos-recovered
  # The person confirms once the machine has booted, so only the link is awaited here.
  host expect link up within 120s
  host expect exception cmosram_error_data_recovered within 30s

  # Recovered means the meters survived.
  host send lp 1C
  host expect field total_coin_in == saved.coin_in
  host expect field total_coin_out == saved.coin_out
  host expect field total_drop == saved.drop
  host expect field games_played == saved.games_played

  egm memory fault cmos-lost
  # The person confirms once the machine has booted, so only the link is awaited here.
  host expect link up within 120s
  host expect exception cmosram_error_no_data_recovered within 30s

  egm memory fault cmos-bad-device
  # The person confirms once the machine has booted, so only the link is awaited here.
  host expect link up within 120s
  host expect exception cmosram_error_bad_device within 30s

  note "A fault may cost the machine its address. If it does, set the address back to 1 before confirming the prompt: the code is still owed once an address is set again - see 3.9."
  note "Non-recoverable means the meters did not survive. What a machine shows in their place is its own, so after the lost and bad-device faults the meters are not asserted."
}

test "4.16 EEPROM errors" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.memory_fault.eeprom, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm memory fault eeprom-data
  # The person confirms once the machine has booted, so only the link is awaited here.
  host expect link up within 120s
  host expect exception eeprom_error_data within 30s

  egm memory fault eeprom-bad-device
  # The person confirms once the machine has booted, so only the link is awaited here.
  host expect link up within 120s
  host expect exception eeprom_error_bad_device within 30s

  note "A fault may cost the machine its address. If it does, set the address back to 1 before confirming the prompt: the code is still owed once an address is set again - see 3.9."
}

test "4.17 EPROM errors" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.memory_fault, machine.memory_fault.eprom_checksum, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # A different version fitted: a changed checksum, once.
  egm memory fault eprom-changed
  # The person confirms once the machine has booted, so only the link is awaited here.
  host expect link up within 120s
  host expect exception eeprom_error_version_changed within 30s
  host expect no exception eeprom_error_version_changed within 10s

  # Then a bad checksum, once.
  egm memory fault eprom-bad-checksum
  # The person confirms once the machine has booted, so only the link is awaited here.
  host expect link up within 120s
  host expect exception eeprom_error_bad_checksum within 30s
  host expect no exception eeprom_error_bad_checksum within 10s
}

test "4.18 partitioned EPROM errors" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.memory_fault.partitioned_eprom, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  egm memory fault partitioned-eprom-changed
  # The person confirms once the machine has booted, so only the link is awaited here.
  host expect link up within 120s
  host expect exception partitioned_eeprom_error_version_changed within 30s
  host expect no exception partitioned_eeprom_error_version_changed within 10s

  egm memory fault partitioned-eprom-bad-checksum
  # The person confirms once the machine has booted, so only the link is awaited here.
  host expect link up within 120s
  host expect exception partitioned_eeprom_error_bad_checksum within 30s
  host expect no exception partitioned_eeprom_error_bad_checksum within 10s

  note "Only a machine with a partitioned EPROM owes these. On one without, decline both prompts and the case skips."
}

# The reset is the operator's acknowledgement of the fault, and it has its own code.
test "4.19 memory error reset" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.memory_fault, machine.memory_fault.eeprom, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # A CMOS error, then the operator's reset of it.
  egm memory fault cmos-lost
  # The person confirms once the machine has booted, so only the link is awaited here.
  host expect link up within 120s
  host expect exception cmosram_error_no_data_recovered within 30s
  egm memory fault clear
  host expect exception memory_error_reset within 30s

  # The same with an EEPROM error.
  egm memory fault eeprom-data
  # The person confirms once the machine has booted, so only the link is awaited here.
  host expect link up within 120s
  host expect exception eeprom_error_data within 30s
  egm memory fault clear
  host expect exception memory_error_reset within 30s

  note "If the fault cost the machine its address, set the address again before confirming the prompt - both the fault and $3A then arrive on general polls."
}

# Any configuration change, including changing the machine's own poll address - which the host then
# has to follow to hear about it.
test "4.20 operator changed options" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires machine.configuration_options, host.poll_sequence
  requires stimulus.jackpot_limit, stimulus.credit_limit, machine.address_configuration
  requires decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s
  host poll sequence
  host send sync
  host send lp 1F

  # The jackpot limit changed and saved: one $3C on a general poll, and then nothing.
  egm set jackpot limit 100000
  host send gp draws operator_changed_options
  host send lp 1F
  host send gp draws nothing
  host send lp 1F

  # Something else changed and saved, the credit limit: another $3C.
  egm set credit limit 1000000
  host send gp draws operator_changed_options
  host send lp 1F
  host send gp draws nothing
  host send lp 1F

  # The address changed to 38 decimal: silence at 1, and at 38 the $3C and the identification poll.
  egm set address 26
  host expect no reply to lp 1F
  host poll address 26
  host send sync
  host send gp draws operator_changed_options
  host send lp 1F

  # Back to 1.
  egm set address 01
  host poll address 01
  host send sync
  host send gp draws operator_changed_options
  host poll every 200ms

  note "The steps change the jackpot limit and the credit limit; any other option the machine lists is checked the same way at the machine."
  note "What counts as an option is the machine's own list; $3C covers denomination, address, and anything that changes the answer to long polls $1F, $53, $54, $56, $A0, $B2, $B3, $B4 or $B5."
}
