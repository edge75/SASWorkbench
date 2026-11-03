# Chapter 4, sections 4.1 to 4.4 - what a general poll draws out of a machine.
#
# One byte per poll, one exception at a time, oldest first, and $00 when there is nothing left.
# The queue is in non-volatile memory, so it survives a power cycle, and the machine answers only
# general polls addressed to itself.
#
# Chapter 4 as a whole is run with bonusing and real-time event reporting off, and with the host
# told to ignore exception $51 rather than act on it - a host that reset every hand pay it heard
# about would clear the very state these cases are about.

# The address half and the ordering half, both an exact host-driven sequence of general polls to
# named addresses. The change lamp exceptions come from the service button.
test "4.1 a general poll to this machine's address, and only this machine's" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires host.poll_sequence, stimulus.door.main, stimulus.button.service, stimulus.settle

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

  # Steps 2 and 3: polls to another machine are not answered, and take nothing out of the queue.
  host send gp draws silence to 02
  host send gp draws silence to 02
  host send gp draws silence to 02

  # Step 4: polled itself, it hands the four out one per poll in the order they happened. The
  # document runs a loop at this address; a poll to another machine between two of its own is
  # what stands in here for the loop's own acknowledgement.
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws change_lamp_on
  host send gp draws silence to 02
  host send gp draws change_lamp_off
  host send gp draws silence to 02

  # Steps 6 and 7: two more, and polls to 00 and to another machine draw neither.
  egm door main open
  egm door main close
  host send sync
  host send gp draws silence to 02
  host send sync
  host send gp draws silence to 02

  # Step 8: only the polls to this machine draw them.
  host send sync
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed

  # Step 9.
  host send gp draws silence to 02
  host send gp draws nothing
}

# The signature reply arrives on a general poll rather than as the answer to the poll that asked
# for it, which is what makes it a chapter 4 case at all. Checking it needs the number the
# manufacturer holds.
test "4.2 the ROM signature comes back on a general poll" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires machine.rom_signature
  requires decoder.lp21

  note "Ask for the ROM signature with a seed of zero. The machine acknowledges the poll and goes on answering everything else while it calculates - there is no time limit on how long that takes."
  note "The first general poll after the calculation finishes is answered with the machine's address, the command byte $21, the two-byte signature and the check sequence. The signature matches what the manufacturer says it is for that seed. Ask them for a seed and its signature if zero is not one they publish."
}

# No activity is a statement, not a silence: with nothing queued the machine still answers, with
# $00. This runs, with the door pair standing in for the exceptions the document creates.
test "4.3 no activity" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.settle, stimulus.door.main

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # A cold machine believes several doors are already open, so settling moves them and the
  # exceptions that follows belong to the setting-up rather than to the case.
  egm settle
  host forget exceptions within 2s

  egm door main open
  host expect exception slot_door_opened within 8s

  egm door main close
  host expect exception slot_door_closed within 8s

  # And then nothing more. The document says the machine answers every subsequent general poll
  # with $00; what is assertable from here is that it does not send either of those two again,
  # because a $00 is not reported as an exception at all - it is the absence of one.
  host expect no exception slot_door_opened within 3s
  host expect no exception slot_door_closed within 3s
}

# The queue is in non-volatile memory. Ten exceptions taken with the line down all survive the
# machine losing power, and the machine picks up where it left off.
test "4.4 the queue survives a power cycle" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.power_cycle

  note "With nothing polling, create ten exceptions - doors opened and closed, the meter display entered and left, the service button pressed and cleared. Power cycle the machine, then start polling."
  note "Every one of the ten comes back, one per general poll, as a non-zero byte. Keep polling until $00 comes back, then open and close the door: the next two general polls carry slot door opened and slot door closed."
}
