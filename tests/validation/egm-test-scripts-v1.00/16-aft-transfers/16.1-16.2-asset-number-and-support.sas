# Chapter 16, sections 16.1 and 16.2 - what a machine must have before any funds transfer.
#
# A transfer names the machine it is for by its asset number, so the number is configured by a
# person, survives a power cycle, and is the same number every transfer reply carries back. Whether
# the machine does transfers at all it says in its enabled-features reply, in one bit of the second
# feature byte.
#
# The software machine performs no transfers and advertises none, so both cases need a machine
# configured for them. The second also needs a single bit of a reply byte compared on its own,
# which the language cannot yet do (host.field_bits); the byte is read and the bit is a note.

test "16.1 the asset number is set at the machine and kept" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @destructive
  requires machine.aft, machine.audit_menu, machine.power_cycle

  note "In the machine's configuration screens, try to type characters outside printable ASCII into the asset number: the machine refuses them."
  note "Set the asset number to 12345, save, and cycle the power. Back in the same screen the number is still 12345."
}

test "16.2 the enabled-features reply says whether transfers are on" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires machine.aft, machine.configuration_options, host.field_bits
  requires decoder.lpA0

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Game zero, the machine as a whole.
  host send lp A0 data 0000
  host expect field game_number == 0

  note "With transfers configured, bit 6 ($40) of the second feature byte is set. Turn transfers off at the machine and send the same poll: the bit is now clear, and nothing else in the reply has moved."
}
