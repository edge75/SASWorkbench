# Chapter 16, sections 16.33 to 16.36 - the game-locked exception, $6F.
#
# While a lock is pending or held the machine keeps reminding the host, raising $6F about every
# five seconds, and stops the moment the lock goes - by cancel, power cycle or a lost link. $6F is
# a priority exception: it goes to the front of whatever is queued, and keeps recurring among the
# rest. With real-time reporting on it is a full event message and may answer a long poll.
#
#   lock     00 0F 9999     every transfer condition, 99.99 seconds
#   cancel   80 00 0000
#
# Only a machine that takes locks can raise it (machine.aft), so none of this runs against the
# software machine. Every case also needs a person to confirm play stops and resumes (D40).

test "16.33 a lock raises game locked until it goes" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @destructive
  requires decoder.lp74, machine.aft, machine.power_cycle, host.poll_sequence

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  # Locked: $6F, and again some five seconds later.
  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF
  host expect exception game_locked within 7s
  host expect exception game_locked within 7s

  # A power cycle ends it.
  egm power cycle
  host expect link up within 60s
  host forget exceptions within 5s
  host expect no exception game_locked within 12s
  host send lp 74 data FF000000
  host expect field game_lock_status == $FF

  # So does a cancel.
  host send lp 74 data 000F9999
  host expect exception game_locked within 7s
  host send lp 74 data 80000000
  host expect field game_lock_status == $FF
  host forget exceptions within 2s
  host expect no exception game_locked within 12s

  # And so does half a minute with nothing on the line.
  host send lp 74 data 000F9999
  host expect exception game_locked within 7s
  host poll sequence
  host wait 32s
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 5s
  host expect no exception game_locked within 12s
  host send lp 74 data FF000000
  host expect field game_lock_status == $FF

  note "At the machine: playable after the power cycle, after the cancel and after the link comes back."
}

test "16.34 game locked goes ahead of the queue" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @destructive
  requires decoder.lp74, machine.aft, machine.power_cycle, host.poll_sequence
  requires stimulus.door.main, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  # Exceptions queued with no general polls on the line.
  host poll sequence
  egm door main open
  egm door main close
  egm door main open
  egm door main close

  # Then the lock, and the first general poll draws $6F before any door.
  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF
  host send gp draws game_locked
  host send gp draws slot_door_opened

  note "Keep polling: $6F recurs about every five seconds among the queued door exceptions. A second lock request still answers $00 or $40 and the machine stays unplayable; a power cycle ends the $6F and makes it playable."
}

test "16.35 game locked as a real-time event" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @destructive
  requires decoder.lp74, decoder.lp0E, machine.aft, machine.power_cycle
  requires stimulus.real_time_events, host.poll_sequence

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  host send lp 0E data 01
  host send lp 74 data 000F9999
  host expect field game_lock_status != $FF
  host expect exception game_locked within 7s
  host expect exception game_locked within 7s

  egm power cycle
  host expect link up within 60s
  host forget exceptions within 5s
  host expect no exception game_locked within 12s

  note "The event is the address, $FF, $6F and a check sequence, and a long poll may be answered with it instead of its own reply - so the steps after the lock read $6F off general polls only. After the power cycle reporting is off; turn it on again and repeat the cancel and the half minute of silence of 16.33, each ending the events and making the machine playable."
}

test "16.36 game locked ahead of the queue as a real-time event" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp74, decoder.lp0E, machine.aft, host.poll_sequence
  requires stimulus.real_time_events, stimulus.door.main, stimulus.settle

  note "With no general polls on the line, turn reporting off, open and close the door a few times, ask for a lock and then turn reporting on. The first event a long poll draws is $6F, in the event format, and it recurs about every five seconds among the queued door events."
  note "The language can drive the line this way, but cannot yet assert which event answered a long poll: a long poll answered with an event fails its step (D43). That is why this one is a note."
}
