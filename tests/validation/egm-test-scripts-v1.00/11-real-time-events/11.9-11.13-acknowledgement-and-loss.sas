# Chapter 11, sections 11.9 to 11.13 - the acknowledgement rules, in the event format.
#
# Exactly the rules of sections 3.15 to 3.23 and 4.5 to 4.7, tested again with reporting on,
# because the format an exception is sent in must not change which polls acknowledge it or how
# many times it is repeated. With reporting on, a long poll to the machine is answered with the
# oldest queued event instead of its reply, so a long poll draws an exception here just as a
# general poll does, and 'draws event' asserts which one and that it came in the event format.
#
# Each case owns the line with 'host poll sequence': the timer and its synchronisation polls stop,
# and the case sends each poll itself. A third identical poll is a final refusal, which the machine
# may answer once more or leave unanswered; 'or silence' admits either, as the document does.

test "11.9 a long poll acknowledges an event" {
  tag @egm-test-scripts @chapter-11 @needs-stimulus
  requires host.poll_sequence, stimulus.real_time_events, stimulus.button.service
  requires stimulus.door.main, stimulus.settle
  requires decoder.lp0E, decoder.lp1F, decoder.lp7E

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s
  host send lp 0E data 01
  host forget exceptions within 2s

  # Slot door opened, slot door closed, change lamp on and change lamp off wait in the queue.
  host poll sequence
  host send sync
  egm door main open
  egm door main close
  egm press service
  egm press service
  # Making them can take longer than a loop break, so the machine is synchronised again first.
  host send sync

  # The identification poll three times: the first two carry slot door opened, the third is final.
  host send lp 1F draws event slot_door_opened
  host send lp 1F draws event slot_door_opened
  host send lp 1F draws event slot_door_opened or silence

  # A poll to another machine, unanswered, then the identification poll twice: still slot door opened.
  host send lp 1F to 7F
  host send lp 1F draws event slot_door_opened
  host send lp 1F draws event slot_door_opened

  # The date and time poll is a different long poll, so it acknowledges; twice it carries slot door closed.
  host send lp 7E draws event slot_door_closed
  host send lp 7E draws event slot_door_closed

  # The identification poll twice: change lamp on, twice.
  host send lp 1F draws event change_lamp_on
  host send lp 1F draws event change_lamp_on

  # A foreign poll acknowledges, and the identification poll twice carries change lamp off.
  host send lp 1F to 7F
  host send lp 1F draws event change_lamp_off
  host send lp 1F draws event change_lamp_off

  # Alternating, the queue drains, and the long poll is answered as itself.
  host send lp 7E
  host send gp draws event nothing
  host send lp 1F

  host poll every 200ms
}

test "11.10 a general poll acknowledges an event" {
  tag @egm-test-scripts @chapter-11 @needs-stimulus
  requires host.poll_sequence, stimulus.real_time_events, stimulus.button.service
  requires stimulus.door.main, stimulus.settle
  requires decoder.lp0E, decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s
  host send lp 0E data 01
  host forget exceptions within 2s

  host poll sequence
  host send sync
  egm door main open
  egm door main close
  egm press service
  egm press service
  # A poll to 00 establishes communication.
  host send sync

  # The identification poll three times: slot door opened twice, then a final refusal.
  host send lp 1F draws event slot_door_opened
  host send lp 1F draws event slot_door_opened
  host send lp 1F draws event slot_door_opened or silence

  # The general poll three times: the same exception on the first two, then a final refusal.
  host send gp draws event slot_door_opened
  host send gp draws event slot_door_opened
  host send gp draws event slot_door_opened or silence

  # The identification poll twice: the same again, since no refusal acknowledged it.
  host send lp 1F draws event slot_door_opened
  host send lp 1F draws event slot_door_opened

  # A general poll after the long poll acknowledges: slot door closed, twice.
  host send gp draws event slot_door_closed
  host send gp draws event slot_door_closed

  # The identification poll after the general poll acknowledges: change lamp on, twice.
  host send lp 1F draws event change_lamp_on
  host send lp 1F draws event change_lamp_on

  # Two general polls to another address, unanswered, then one here: change lamp off.
  host send gp draws silence to 7F
  host send gp draws silence to 7F
  host send gp draws event change_lamp_off

  host send lp 1F
  host send gp draws event nothing
  host poll every 200ms
}

test "11.11 a global broadcast acknowledges an event" {
  tag @egm-test-scripts @chapter-11 @needs-stimulus @destructive
  requires host.poll_sequence, stimulus.real_time_events, stimulus.button.service
  requires stimulus.door.main, stimulus.settle
  requires decoder.lp0E, decoder.lp1F, decoder.lp7F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s
  host send lp 0E data 01
  host forget exceptions within 2s

  host poll sequence
  host send sync
  egm door main open
  egm door main close
  egm press service
  egm press service
  # A poll to 00 establishes communication.
  host send sync

  # The identification poll twice: slot door opened both times.
  host send lp 1F draws event slot_door_opened
  host send lp 1F draws event slot_door_opened

  # The broadcast twice, which no machine answers, then the identification poll twice: slot door
  # closed, so the broadcasts acknowledged the first exception.
  host send lp 7F data 06152031103000 to 00
  host send lp 7F data 06152031103000 to 00
  host send lp 1F draws event slot_door_closed
  host send lp 1F draws event slot_door_closed

  # The general poll twice: change lamp on, twice.
  host send gp draws event change_lamp_on
  host send gp draws event change_lamp_on

  # Two broadcasts and two general polls: change lamp off, twice.
  host send lp 7F data 06152031103000 to 00
  host send lp 7F data 06152031103000 to 00
  host send gp draws event change_lamp_off
  host send gp draws event change_lamp_off

  host send lp 1F
  host send gp draws event nothing
  host poll every 200ms

  note "The broadcast is the date and time long poll sent to 00, as in 3.19; it sets the machine's clock to June 15 2031, hence destructive: set it back when the case is done."
}

test "11.12 a power cycle does not clear a final refusal" {
  tag @egm-test-scripts @chapter-11 @needs-a-person @destructive
  requires host.poll_sequence, machine.power_cycle, stimulus.real_time_events
  requires stimulus.door.main, stimulus.settle
  requires decoder.lp0E, decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s
  host send lp 0E data 01
  host forget exceptions within 2s

  host poll sequence
  host send sync
  egm door main open
  egm door main close
  host send sync

  # The general poll three times: slot door opened twice, then a final refusal.
  host send gp draws event slot_door_opened
  host send gp draws event slot_door_opened
  host send gp draws event slot_door_opened or silence

  # After a power cycle a poll to 00 and a general poll draw slot door opened again: the power cycle
  # did not acknowledge it.
  egm power cycle
  # The machine chirps after booting, so the first sync may read a chirp as its reply; the second
  # is answered on a quiet line.
  host send sync
  host send sync
  host send gp draws slot_door_opened

  # Reporting is off after the power cycle (11.5), so it is turned on again; that long poll
  # acknowledges slot door opened. The identification poll three times: slot door closed twice,
  # then a final refusal.
  host send lp 0E data 01
  host send lp 1F draws event slot_door_closed
  host send lp 1F draws event slot_door_closed
  host send lp 1F draws event slot_door_closed or silence

  # Power cycle again: a poll to 00, reporting on, and the identification poll carries slot door closed.
  egm power cycle
  host send sync
  host send sync
  host send lp 0E data 01
  host send lp 1F draws event slot_door_closed
  host send gp

  host poll every 200ms

  note "The document's second and third rounds use the identification poll with reporting on, but a power cycle turns reporting off (11.5), so the case turns it on again after each power cycle before asking. The long poll that does so is not a repeat, so it acknowledges what came before it, as any poll would."
}

test "11.13 five seconds of silence does not clear a final refusal" {
  tag @egm-test-scripts @chapter-11 @needs-stimulus
  requires host.poll_sequence, host.chirp, stimulus.real_time_events
  requires stimulus.door.main, stimulus.settle
  requires decoder.lp0E, decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s
  host send lp 0E data 01
  host forget exceptions within 2s

  host poll sequence
  host send sync
  egm door main open
  egm door main close
  host send sync

  # A poll to 00 then the general poll twice: slot door opened both times.
  host send gp draws event slot_door_opened
  host send gp draws event slot_door_opened

  # More than five seconds of quiet: the machine chirps its address with the wakeup bit.
  host expect no chirp for 2s
  host expect chirp within 5s

  # A poll to 00, a general poll and another poll to 00: slot door opened again.
  host send sync
  host send gp draws event slot_door_opened
  host send sync

  # A poll to 00 and the identification poll twice: slot door closed both times.
  host send sync
  host send lp 1F draws event slot_door_closed
  host send lp 1F draws event slot_door_closed

  # Quiet again, the chirp again, then a poll to 00, the identification poll and a poll to 00.
  host expect no chirp for 2s
  host expect chirp within 5s
  host send sync
  host send lp 1F draws event slot_door_closed
  host send sync

  host poll every 200ms
}
