# Chapter 3, sections 3.7 to 3.10 - which polls belong to this machine.
#
# A machine's poll address is set at the machine, in its own configuration screens, and there is
# no long poll that sets one. That is what puts most of this group out of reach: the software
# machine is built at whatever address a script connects to, which proves it answers there and
# proves nothing about an attendant being able to change it.
#
# The other half - that a machine ignores polls addressed to somebody else - needs one machine on
# a line that carries polls to several addresses. A round of addresses here builds a machine at
# each of them, so there is no address on the line with nobody behind it.

# The whole range is configurable, not just the low addresses, and the machine works at each.
test "3.7 the address is configurable across the whole range" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires machine.address_configuration

  note "Set the machine's address to 1 and poll it: it answers polls to its own address after the first synchronisation poll, and ignores every other address. Set it to 127 decimal and repeat. Set it to 38 decimal and repeat. The configuration screen must accept any value from 0 to 127."
}

# Address zero is the synchronisation address and belongs to no machine, so a machine set to zero
# is deaf - and the clock it was given before it went deaf is still there when it comes back.
test "3.8 a machine at address zero answers nothing" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires machine.address_configuration
  requires decoder.lp7F, decoder.lp7E

  note "Set the clock with the receive date and time poll, then read it back: the machine reports what was sent. Set the machine's address to 0 and poll a round that includes address 0: it answers nothing at all, including a receive date and time addressed to 0 carrying a different time."
  note "Set the address back to 1 and read the clock again. It still reads what the first poll set, which is what proves the machine ignored the second rather than quietly taking it."
  note "The machine must pass 5.15 and 5.16 first, or a wrong clock here says nothing about addressing."
}

# A machine whose memory has been lost must not come back talking. It comes back at address zero
# and stays silent until somebody sets an address, because a machine that resumed answering after
# losing its memory would be reporting meters nobody can trust.
test "3.9 a memory fault resets the address to zero" {
  tag @egm-test-scripts @chapter-3 @needs-a-person @destructive
  requires machine.memory_fault

  note "With the link up, create a critical memory error at the machine and let it boot back to a playable state. It has reset its address to 0 and ignores every general and long poll until an address above 0 is configured."
}

# The other side of 3.7: a poll to somebody else must not be acted on, not merely not answered.
test "3.10 polls to another address change nothing" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.poll_sequence

  note "Poll a round carrying general and long polls to several addresses including 0, with only this machine on the line. It ignores every general poll to address 0, answers general polls to its own address with an exception byte and long polls to its own address with their replies, and ignores both to any other address."
  note "Then check the machine's state: nothing a poll to another address asked for has happened. A machine that answers only its own polls but acts on everybody's fails this, and the trace alone will not show it - the state has to be read at the machine."
}
