# Chapter 4, sections 4.5 to 4.7 - an exception stays on the wire until something acknowledges it.
#
# The same rule as sections 3.15 to 3.21, seen from the exception's side rather than the poll's.
# An exception is repeated until a poll arrives that is not a repeat of the one that carried it,
# and the three shapes that qualify get a case each: a long poll, a general poll to somebody else,
# and a global broadcast.
#
# All three need the host to drive an exact sequence with nothing else on the line, and this host
# sends a synchronisation poll - a global broadcast, which 4.7 says is itself an acknowledgement -
# ahead of every message it sends.

# A long poll acknowledges, and so does a long poll addressed to another machine.
test "4.5 a long poll acknowledges an exception" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires host.poll_sequence, stimulus.service_button
  requires decoder.lp1F

  note "Create four exceptions in order - slot door opened, slot door closed, change lamp on, change lamp off. With the host's resynchronisation off, establish communication with a poll to 00 and the identification poll."
  note "Send the general poll three times then the identification poll. The first two carry slot door opened; the third carries it again or is unanswered, and either is a pass. Repeat with the identification poll addressed to another machine on the end: the foreign poll is ignored and acknowledges all the same."
  note "Work through the four, ending with a general poll that reports no activity. The identification poll is answered with the machine's information every time it is addressed here, and never when it is addressed elsewhere."
}

# A general poll to a different address acknowledges without being answered.
test "4.6 a general poll to another address acknowledges" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires host.poll_sequence, stimulus.door.slot
  requires decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  host poll sequence

  # A sync poll of our own first: the round's last cycle has finished before the doors move, and a
  # machine stops answering a third general poll in a row with nothing between them (section 3.3).
  host send sync

  egm door slot open
  egm door slot close

  # The machine's own general poll draws the first of the two.
  host send gp draws slot_door_opened

  # The foreign poll is not answered at all - and it acknowledges, so the next poll here moves on
  # to the second rather than repeating the first.
  host send gp draws silence to 7F
  host send gp draws slot_door_closed

  # Polled again straight away the machine would repeat the second, as an implied NACK.
  host send gp draws silence to 7F
  host send gp draws nothing

  note "The document also sends the machine's own general poll three times over, where the first two carry the same exception because a repeated poll is an implied NACK. This build's software machine keeps no implied acknowledgement state - it hands out one exception per general poll whatever came before - so that half is not asserted here. The machine library does keep the state, so the half runs against a real machine and not against this peer."
}

# A global broadcast acknowledges. This is the one that makes the whole group unreachable from
# here, because the host's own synchronisation poll is a global broadcast.
test "4.7 a global broadcast acknowledges" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires host.poll_sequence
  requires decoder.lp1F, decoder.lp80

  note "Create slot door opened and slot door closed. With the host's resynchronisation off, establish communication, then send the general poll to this machine three times followed by a receive progressive amount addressed to 00."
  note "The first two carry slot door opened, the third carries it or is unanswered, and the broadcast is not answered. Alternate the general poll and the broadcast: the next general poll carries slot door closed, and the one after reports no activity or a third exception if one has arisen."
}
