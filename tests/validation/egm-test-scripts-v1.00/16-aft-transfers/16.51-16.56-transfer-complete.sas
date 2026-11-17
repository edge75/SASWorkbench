# Chapter 16, sections 16.51 to 16.56 - the exception that says a transfer has finished.
#
# $69 is how a host learns that a transfer it started has reached a final state, so the cases ask
# three things of it: that it is raised exactly when it should be and not for a transfer that was
# refused outright, that it goes ahead of whatever else is queued, and that it keeps coming back
# until the host has read the result. The last three sections repeat the first three with
# real-time event reporting on, where the same exception arrives in the event format and answers
# long polls as well as general ones.
#
# All six need the funds-transfer command decoded and a machine that performs transfers, and
# neither is available here.

test "16.51 a finished transfer raises its exception" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft
  requires decoder.lp73, host.poll_sequence

  note "A partial cashable transfer into the machine is answered as pending ($40) and $69 follows on a general poll; reading the status acknowledges it. The same for a full transfer followed at once by a peek at the current record."
  note "With only long polls on the line, a transfer that completes and is read back before any general poll leaves no $69 to report when general polls resume."
  note "With the registration cancelled, a debit transfer is refused as not registered ($88) and raises no $69. A transfer sent while another is still in progress is refused with $C0 or $C1 and raises none either."
}

test "16.52 the transfer exception goes ahead of the queue" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, machine.aft
  requires stimulus.door.main, host.poll_sequence

  note "Queue some ordinary exceptions by opening and closing a door, with only long polls running. Complete a transfer, then start general polls: $69 comes first and the queued ones after it."
  note "Do it again, then poll the machine alone three times with nothing else on the line: the first two answers are $69, and the third may be or may not. When the round resumes, $69 once more and then the queue, until the status is read."
}

test "16.53 the transfer exception repeats until it is read" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft
  requires host.timing

  note "Complete a full transfer and leave it unread for a couple of minutes: $69 comes back on a general poll about every fifteen seconds."
  note "A peek at the current record does not count as reading it, so the repeats carry on. Only the status interrogation stops them."
}

test "16.54 the transfer exception as a real-time event" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, decoder.lp73

  note "With event reporting on, 16.51 again: the completion arrives in the event format, in answer to long polls as well as general polls."
  note "The refusals of 16.51 - not registered, or a second transfer during the first - raise no event. Turn event reporting off at the end."
}

test "16.55 the transfer event goes ahead of the queue" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, stimulus.door.main, host.poll_sequence

  note "Queue ordinary exceptions with event reporting off, complete a transfer, then turn reporting on: the completion event is the first thing reported and the queue follows."
  note "Again with reporting off while the transfer completes; then turn it on and poll the machine alone three times. The first two carry the completion, the third may; when the long polls resume, the event comes once more ahead of the queue."
}

test "16.56 the transfer event repeats until it is read" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, host.timing

  note "16.53 with event reporting on: the event comes back about every fifteen seconds on general and long polls, a peek does not stop it, and reading the status does."
}
