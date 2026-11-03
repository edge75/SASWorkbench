# Chapter 4, sections 4.5 to 4.7 - an exception stays on the wire until something acknowledges it.
#
# The same rule as sections 3.15 to 3.21, seen from the exception's side rather than the poll's.
# An exception is repeated until a poll arrives that is not a repeat of the one that carried it,
# and the three shapes that qualify get a case each: a long poll, a general poll to somebody else,
# and a global broadcast.
#
# All three need the host to drive an exact sequence with nothing else on the line, which
# `host poll sequence` gives them. 4.7 also needs a broadcast long poll this build cannot send.

# A long poll acknowledges, and so does a long poll addressed to another machine.
test "4.5 a long poll acknowledges an exception" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires host.poll_sequence, stimulus.door.main, stimulus.button.service, stimulus.settle
  requires decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s

  # The line is the case's from here: the four exceptions wait in the queue until it polls.
  host poll sequence
  host send sync
  egm door main open
  egm door main close
  egm press service
  egm press service

  # Making them can take longer than a loop break, so the machine is synchronised again first.
  host send sync
  # Step 3: a poll to 00 and the identification poll establish communication.
  host send lp 1F

  # Step 4: three in a row is a final refusal. The first two carry the exception, the third may
  # carry it or go unanswered, and the identification poll after them is answered.
  host send gp draws slot_door_opened
  host send gp draws slot_door_opened
  host send gp
  host send lp 1F

  # Steps 5 and 6: a final refusal acknowledges nothing, so the same exception comes back, with a
  # poll to another machine after it the first time and the identification poll the second.
  host send gp draws slot_door_opened
  host send gp draws slot_door_opened
  host send gp
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws slot_door_opened
  host send gp
  host send lp 1F

  # Step 7: twice is only a refusal, and the identification poll after it acknowledges.
  host send gp draws slot_door_opened
  host send gp draws slot_door_opened
  host send lp 1F

  # Step 8: so the next one moves on, and a poll to another machine acknowledges as well.
  host send gp draws slot_door_closed
  host send gp draws silence to 02

  # Steps 9 and 10.
  host send gp draws change_lamp_on
  host send lp 1F
  host send gp draws change_lamp_off
  host send lp 1F
  host send gp draws nothing
  host send lp 1F

  note "The document's poll to another machine is the identification long poll at address 2; this build asserts a long poll only by its answer, so a general poll to address 2 stands in for it, which section 4.6 makes the same acknowledgement."
}

# A general poll to a different address acknowledges without being answered.
test "4.6 a general poll to another address acknowledges" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires host.poll_sequence, stimulus.door.slot
  requires decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
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
