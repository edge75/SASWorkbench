# Chapter 3, sections 3.7 to 3.10 - which polls belong to this machine.
#
# A machine's poll address is set at the machine, in its own configuration screens, and there is
# no long poll that sets one. 'egm set address' is the operator doing that, and 'host poll address'
# is the host being pointed at the machine's new address afterwards, as a site would repoint it.
#
# The other half - that a machine ignores polls addressed to somebody else - needs one machine on
# a line that carries polls to several addresses. In a sequence the host sends polls to addresses
# nobody holds, so there is no address on the line with nobody behind it.

# The whole range is configurable, not just the low addresses, and the machine works at each.
test "3.7 the address is configurable across the whole range" {
  tag @egm-test-scripts @chapter-3 @needs-stimulus
  requires machine.address_configuration, host.poll_sequence
  requires decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s
  host poll sequence

  # Address 1: its own polls are answered and every other address is ignored.
  host send gp draws nothing
  host send lp 1F
  host send gp draws silence to 7F
  host send gp draws silence to 26

  # Address 127: nothing to its own address until the first synchronisation poll, then it answers
  # there, reports the configuration change, and ignores the others.
  egm set address 7F
  host poll address 7F
  host expect no reply to lp 1F
  host send sync
  host send gp draws operator_changed_options
  host send lp 1F
  host send gp draws silence to 01
  host send gp draws silence to 26

  # Address 38 decimal, the same again.
  egm set address 26
  host poll address 26
  host expect no reply to lp 1F
  host send sync
  host send gp draws operator_changed_options
  host send lp 1F
  host send gp draws silence to 01
  host send gp draws silence to 7F

  # Back to 1 for whatever runs next.
  egm set address 01
  host poll address 01
  host send sync
  host send gp draws operator_changed_options
  host poll every 200ms

  note "The configuration screen accepting every value from 0 to 127, and refusing anything above, is checked at the machine."
}

# Address zero is the synchronisation address and belongs to no machine, so a machine set to zero
# is deaf - and the clock it was given before it went deaf is still there when it comes back.
# Setting the clock leaves it changed, hence destructive.
test "3.8 a machine at address zero answers nothing" {
  tag @egm-test-scripts @chapter-3 @needs-stimulus @destructive
  requires machine.address_configuration, host.poll_sequence
  requires decoder.lp7F, decoder.lp7E, decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s

  # The clock set to June 15 2031, 10:30:00, and read back.
  host send lp 7F data 06152031103000
  host send lp 7E
  host expect field month of lp 7E == 6
  host expect field day of lp 7E == 15
  host expect field year of lp 7E == 2031
  host expect field hour of lp 7E == 10

  # At address 0 nothing is answered: its old address, a stranger, nor a clock of January 1 2032
  # sent to 00.
  egm set address 00
  host poll sequence
  host expect no reply to lp 1F
  host send gp draws silence
  host send sync
  host send gp draws silence to 02
  host send lp 7F data 01012032080000 to 00
  host expect no reply to lp 7F data 01012032080000

  # Back at 1 the clock still reads what the first poll set.
  egm set address 01
  host send sync
  host send gp draws operator_changed_options
  host send lp 7E
  host expect field year of lp 7E == 2031
  host expect field month of lp 7E == 6
  host expect field day of lp 7E == 15
  host poll every 200ms

  note "The machine must pass 5.15 and 5.16 first, or a wrong clock here says nothing about addressing. Set the clock back to the right time when the case is done."
}

# A machine whose memory has been lost must not come back talking. It comes back at address zero
# and stays silent until somebody sets an address, because a machine that resumed answering after
# losing its memory would be reporting meters nobody can trust.
test "3.9 a memory fault resets the address to zero" {
  tag @egm-test-scripts @chapter-3 @needs-a-person @destructive
  requires machine.memory_fault

  # The software machine raises its memory error while running, with the link up; memory found
  # invalid while it starts, which leaves the platform at address 0 (QSP SasNetwork.cpp:309), it does not model.
  requires machine.memory_fault.at_start_up

  note "With the link up, create a critical memory error at the machine and let it boot back to a playable state. It has reset its address to 0 and ignores every general and long poll until an address above 0 is configured."
}

# The other side of 3.7: a poll to somebody else must not be acted on, not merely not answered.
test "3.10 polls to another address change nothing" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.poll_sequence
  requires decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s

  # The case owns the line from here, because a poll to another address is only meaningful when
  # nothing else is on the wire to answer for it.
  host poll sequence

  # Its own address is answered, on both kinds of poll.
  host send gp draws nothing
  host send lp 1F

  # Address 00 and a stranger are not, and neither is answered at all.
  host send sync
  host send gp draws silence to 7F

  # And its own address still answers afterwards, so the strangers changed nothing it does.
  host send gp draws nothing
  host send lp 1F

  note "The wire half runs above. The half a person has to do is at the machine: read its state and check that nothing a poll to another address asked for has happened. A machine that answers only its own polls but acts on everybody's passes everything above and still fails the case, and no trace can show it."
}
