# Chapter 11, sections 11.9 to 11.13 - the acknowledgement rules, in the event format.
#
# Exactly the rules of sections 3.15 to 3.23 and 4.5 to 4.7, tested again with reporting on,
# because the format an exception is sent in must not change which polls acknowledge it or how
# many times it is repeated. Five cases, all of them needing an exact host-driven sequence of
# named polls with nothing else on the line, and two of them a power cycle or five seconds of
# silence on top.
#
# This host runs a round on a timer and puts a synchronisation poll - itself a global broadcast,
# and so an acknowledgement - ahead of every message, so none of the sequences can be presented.

test "11.9 a long poll acknowledges an event" {
  tag @egm-test-scripts @chapter-11 @needs-a-person
  requires host.poll_sequence, stimulus.real_time_events, stimulus.service_button
  requires decoder.lp0E, decoder.lp1F, decoder.lp7E

  note "With the host's resynchronisation off, establish communication and turn reporting on. Create slot door opened, slot door closed, change lamp on and change lamp off."
  note "Send the identification poll three times: the first two carry slot door opened in the event format, and the third carries it again or is unanswered. Then twice more with a poll to another machine in front: still slot door opened, and the foreign poll unanswered."
  note "Send the send date and time poll twice: slot door closed, twice. The identification poll twice: change lamp on, twice. A foreign poll and the identification poll twice: change lamp off, twice. Then alternate until no activity comes back."
}

test "11.10 a general poll acknowledges an event" {
  tag @egm-test-scripts @chapter-11 @needs-a-person
  requires host.poll_sequence, stimulus.real_time_events, stimulus.service_button
  requires decoder.lp0E, decoder.lp1F

  note "With the host's resynchronisation off, establish communication and turn reporting on. Create the same four exceptions."
  note "Send a poll to 00 and the identification poll three times over: the first two identification polls carry slot door opened in the event format and the third carries it or is unanswered. The general poll to this machine three times: the same exception on the first two. The identification poll twice: the same again."
  note "Then the general poll twice for slot door closed, the identification poll twice for change lamp on, and a general poll to another address twice followed by one here for change lamp off. Every poll that is not a repeat acknowledges, whatever kind it is."
}

test "11.11 a global broadcast acknowledges an event" {
  tag @egm-test-scripts @chapter-11 @needs-a-person
  requires host.poll_sequence, stimulus.real_time_events, stimulus.service_button
  requires decoder.lp0E, decoder.lp1F

  note "With the host's resynchronisation off, establish communication and turn reporting on. Create the same four exceptions."
  note "Send a poll to 00 then the identification poll twice: both carry slot door opened in the event format. Send the broadcast twice then the identification poll twice: both carry slot door closed - the broadcasts acknowledged the first exception."
  note "Send a general poll to this machine twice: change lamp on, twice. Then two broadcasts and two general polls: change lamp off, twice."
}

test "11.12 a power cycle does not clear a final refusal" {
  tag @egm-test-scripts @chapter-11 @needs-a-person @destructive
  requires host.poll_sequence, machine.power_cycle, stimulus.real_time_events
  requires decoder.lp0E, decoder.lp1F

  note "With the host's resynchronisation off, establish communication, turn reporting on, and create slot door opened and slot door closed."
  note "Send a poll to 00 then the general poll three times: the first two carry slot door opened in the event format and the third carries it or is unanswered. Power cycle, then a poll to 00 and a general poll: slot door opened again, so the power cycle did not acknowledge it."
  note "Repeat with the identification poll three times for slot door closed, and power cycle again: a poll to 00, the identification poll and a general poll, and the identification poll carries slot door closed."
}

test "11.13 five seconds of silence does not clear a final refusal" {
  tag @egm-test-scripts @chapter-11 @needs-a-person
  requires host.poll_sequence, host.chirp, stimulus.real_time_events
  requires decoder.lp0E, decoder.lp1F

  note "With the host's resynchronisation off, establish communication, turn reporting on, and create slot door opened and slot door closed."
  note "Send a poll to 00 then the general poll twice: both carry slot door opened in the event format. Go quiet for more than five seconds - the machine chirps its own address with the wakeup bit set. Send a poll to 00, a general poll and another poll to 00: the general poll carries slot door opened again."
  note "Then a poll to 00 and the identification poll twice: both carry slot door closed. Go quiet again for more than five seconds, chirp again, and a poll to 00, the identification poll and a poll to 00: the identification poll carries slot door closed."
}
