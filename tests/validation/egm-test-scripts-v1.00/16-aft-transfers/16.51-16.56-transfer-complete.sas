# Chapter 16, sections 16.51 to 16.56 - the exception that says a transfer has finished.
#
# $69 is how a host learns that a transfer it started has reached a final state, so the cases ask
# three things of it: that it is raised exactly when it should be and not for a transfer that was
# refused outright, that it goes ahead of whatever else is queued, and that it keeps coming back
# until the host has read the result. The last three sections repeat the first three with
# real-time event reporting on, where the same exception arrives in the event format and answers
# long polls as well as general ones.
#
# 16.51 to 16.53 and 16.55 run against the software machine (D45), and so do the notes of 16.54.
# 16.56 waits: with reporting on, a long poll that meets a queued event fails its step (D43).

test "16.51 a finished transfer raises its exception" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft
  requires decoder.lp73, host.poll_sequence, stimulus.asset_number, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm settle
  host forget exceptions within 2s

  # A partial transfer in: pending, then $69, and reading the status acknowledges it.
  host send lp 72 data 36010000000000050000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status == $40
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00

  # A full transfer, peeked at once and read after its $69.
  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000132000000000000000000
  host send lp 72 data 02FE00
  host expect field transfer_status == $40
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00

  # Only long polls: completed and read before any general poll, so no $69 is left to report.
  host poll sequence
  host send sync
  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000133000000000000000000
  host wait 3s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host send sync
  host send gp draws nothing

  # Not registered: a debit transfer is refused $88 and raises nothing.
  host send lp 73 data 0180
  host send lp 72 data 36000040000000050000000000000000000000003930000000000000000000000000000000000000000000000134000000000000000000
  host expect field transfer_status == $88
  host send sync
  host send gp draws nothing

  # A second transfer while the first is pending is refused $C0; only the first raises $69.
  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000135000000000000000000
  host expect field transfer_status == $40
  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000136000000000000000000
  host expect field transfer_status == $C0
  host wait 3s
  host send gp draws aft_transfer_complete
  host send sync
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host send sync
  host send gp draws nothing

  note "A partial cashable transfer into the machine is answered as pending ($40) and $69 follows on a general poll; reading the status acknowledges it. The same for a full transfer followed at once by a peek at the current record."
  note "With only long polls on the line, a transfer that completes and is read back before any general poll leaves no $69 to report when general polls resume."
  note "With the registration cancelled, a debit transfer is refused as not registered ($88) and raises no $69. A transfer sent while another is still in progress is refused with $C0 or $C1 and raises none either."
}

test "16.52 the transfer exception goes ahead of the queue" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, machine.aft
  requires stimulus.door.main, host.poll_sequence, stimulus.asset_number, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm settle
  host forget exceptions within 2s

  # Only long polls on the line while the doors queue and the transfer completes.
  host poll sequence
  host send sync
  egm door main open
  egm door main close
  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status == $40
  host wait 3s

  # General polls again: $69 first, then the doors, each acknowledged by a poll elsewhere.
  host send gp draws aft_transfer_complete
  host send sync
  host send gp draws slot_door_opened
  host send sync
  host send gp draws slot_door_closed

  note "Queue some ordinary exceptions by opening and closing a door, with only long polls running. Complete a transfer, then start general polls: $69 comes first and the queued ones after it."
  note "Do it again, then poll the machine alone three times with nothing else on the line: the first two answers are $69, and the third may be or may not. When the round resumes, $69 once more and then the queue, until the status is read."
}

test "16.53 the transfer exception repeats until it is read" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft
  requires host.timing, stimulus.asset_number, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm settle
  host forget exceptions within 2s

  # A full transfer left unread: $69, then again about every fifteen seconds.
  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status == $40
  host expect exception aft_transfer_complete within 15s
  host expect exception aft_transfer_complete every 15s

  # A peek at the current record does not count as reading it, so the repeats carry on.
  host send lp 72 data 02FE00
  host expect exception aft_transfer_complete every 15s

  # Only the status interrogation stops them.
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect no exception aft_transfer_complete within 20s
}

test "16.54 the transfer exception as a real-time event" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, decoder.lp73
  requires host.poll_sequence, stimulus.asset_number, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm settle
  host forget exceptions within 2s

  # Reporting on with only the script's polls on the line: the completion arrives as an event.
  host poll sequence
  host send sync
  host send lp 0E data 01
  host send lp 72 data 36010000000000050000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status == $40
  host wait 3s
  host send gp draws aft_transfer_complete
  host send sync
  host send lp 72 data 02FF00
  host expect field transfer_status == $00

  # Not registered: the debit refusal raises no event.
  host send lp 73 data 0180
  host send lp 72 data 36000040000000050000000000000000000000003930000000000000000000000000000000000000000000000134000000000000000000
  host expect field transfer_status == $88
  host send sync
  host send gp draws nothing
  host send lp 0E data 00

  note "General polls carry the event here because a long poll answered with an event fails its step (D43)."
  note "With event reporting on, 16.51 again: the completion arrives in the event format, in answer to long polls as well as general polls."
  note "The refusals of 16.51 - not registered, or a second transfer during the first - raise no event. Turn event reporting off at the end."
}

test "16.55 the transfer event goes ahead of the queue" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, stimulus.door.main, host.poll_sequence
  requires decoder.lp1F, decoder.lp7E
  requires stimulus.asset_number, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm settle
  host forget exceptions within 2s

  # Reporting off while the doors queue and the transfer completes.
  host poll sequence
  host send sync
  egm door main open
  egm door main close
  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status == $40
  host wait 3s

  # Reporting on: the completion is the first event, and the doors follow. Each long poll differs from
  # the one before it, so each acknowledges the event it follows; the same poll twice would be a refusal.
  host send lp 0E data 01
  host send lp 1F draws event aft_transfer_complete
  host send lp 7E draws event slot_door_opened
  host send lp 1F draws event slot_door_closed
  note "Again with reporting off while the transfer completes; then turn it on and poll the machine alone three times. The first two carry the completion, the third may; when the long polls resume, the event comes once more ahead of the queue."
}

test "16.56 the transfer event repeats until it is read" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft
  requires stimulus.real_time_events, decoder.lp0E, host.timing
  requires stimulus.asset_number, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm settle
  host send lp 0E data 01
  host forget exceptions within 2s

  # A full transfer left unread: the event, then again about every fifteen seconds.
  host send lp 72 data 36000000000000050000000000000000000000003930000000000000000000000000000000000000000000000131000000000000000000
  host expect field transfer_status == $40
  host expect exception aft_transfer_complete within 15s
  host expect exception aft_transfer_complete every 15s

  # A peek at the current record does not count as reading it, so the repeats carry on.
  host send lp 72 data 02FE00
  host expect exception aft_transfer_complete every 15s

  # Only the status interrogation stops them.
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect no exception aft_transfer_complete within 20s

  note "The repeats are drawn here by the round's general polls; that a long poll draws a queued event in their place is the rule 11.9 asserts."
}
