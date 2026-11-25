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
  requires machine.rom_signature, stimulus.settle
  requires decoder.lp21

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  # Seed zero: acknowledged, and the machine goes on answering while it calculates.
  host send lp 21 data 0000
  host send lp 1F

  # The round's general polls carry it once it is ready. There is no time limit, so the wait is long.
  host expect rom signature within 120s

  note "The signature matches what the manufacturer says it is for seed zero; the run's record shows it. Ask them for a seed and its signature if zero is not one they publish."
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
  requires host.poll_sequence, machine.power_cycle, stimulus.door.main, stimulus.button.service, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s

  # Ten exceptions taken with the line held, so they wait in the queue.
  host poll sequence
  host send sync
  egm door main open
  egm door main close
  egm press service
  egm press service
  egm door main open
  egm door main close
  egm press service
  egm press service
  egm door main open
  egm door main close

  egm power cycle
  # The machine chirps after booting, so the first sync may read a chirp as its reply; the second
  # is answered on a quiet line.
  host send sync
  host send sync

  # All ten come back in the order they happened. A poll to another machine between two of its own
  # stands in for the loop's acknowledgement.
  host send gp draws slot_door_opened
  host send gp draws silence to 7F
  host send gp draws slot_door_closed
  host send gp draws silence to 7F
  host send gp draws change_lamp_on
  host send gp draws silence to 7F
  host send gp draws change_lamp_off
  host send gp draws silence to 7F
  host send gp draws slot_door_opened
  host send gp draws silence to 7F
  host send gp draws slot_door_closed
  host send gp draws silence to 7F
  host send gp draws change_lamp_on
  host send gp draws silence to 7F
  host send gp draws change_lamp_off
  host send gp draws silence to 7F
  host send gp draws slot_door_opened
  host send gp draws silence to 7F
  host send gp draws slot_door_closed

  note "The document then polls until $00 and opens and closes the door once more. The power exceptions that may follow the ten are the machine's own to choose, so that tail is not run."
}
