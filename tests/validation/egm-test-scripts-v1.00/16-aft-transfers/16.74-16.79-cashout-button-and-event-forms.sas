# Chapter 16, sections 16.74 to 16.79 - the cashout button exception, and every host-cashout case
# again with real-time events on.
#
# $66 says only that the button was pressed, once per press, whatever the host has asked for; it
# sits alongside $6A rather than replacing it. The last five sections take 16.66, 16.68, 16.69,
# 16.72 and 16.74 and ask for the same behaviour with event reporting on, where the exceptions come
# in the event format and answer long polls as well as general ones.
#
# All six need the funds-transfer command decoded and a machine that performs transfers.

test "16.74 the cashout button exception beside the host request" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires decoder.lp74, stimulus.button.collect, machine.transfer_limit

  note "Interrogate the lock and status: where the machine can currently send transfers to the host, available transfers bit 1 is set. Where it cannot, go straight to the last part."
  note "Load five dollars of each type and enable cashout to the host. Press cashout: one $66 comes on a general poll, and $6A repeats about every 800 ms for eight seconds among the other exceptions before the machine pays the usual way."
  note "Reload and re-enable, set the transfer limit or credit limit below five dollars, and press again: one $66 and the usual payment. A partial transfer of everything to the host then empties the machine with status $01."
  note "Last, disable cashout to the host and press cashout: the machine waits about 800 ms and then starts its usual cashout."
}

test "16.75 the host cashout request as a real-time event" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, stimulus.button.collect, machine.transfer_limit

  note "16.66 with event reporting on: everything passes as it does there, and $6A arrives in the event format on long polls as well as general polls."
}

test "16.76 the host cashout event goes ahead of the queue" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, stimulus.button.collect, stimulus.door.main, host.poll_sequence

  note "16.68 with event reporting on: everything passes as it does there, and $6A arrives in the event format on long polls as well as general polls."
}

test "16.77 the host win request as a real-time event" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, machine.credit_limit, machine.transfer_limit, stimulus.play

  note "16.70 with event reporting on: everything passes as it does there, and $6B arrives in the event format on long polls as well as general polls."
  note "The document points this section at 16.69, but its title and its exception are those of 16.70; the pointer is taken as a misprint."
}

test "16.78 the host win event goes ahead of the queue" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, machine.credit_limit, stimulus.play, stimulus.door.main, host.poll_sequence

  note "16.72 with event reporting on: everything passes as it does there, and $6B arrives in the event format on long polls as well as general polls."
}

test "16.79 the cashout button exception as a real-time event" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, decoder.lp74, stimulus.button.collect, machine.transfer_limit

  note "16.74 with event reporting on: everything passes as it does there, and $66 arrives in the event format on long polls as well as general polls."
}
