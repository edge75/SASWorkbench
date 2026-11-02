# Chapter 4, sections 4.8 to 4.13 - how deep the queue is and what comes out of it first.
#
# Three things are being established here. The queue holds at least twenty exceptions in
# non-volatile memory; when it fills, the oldest go and the machine says so once with $70; and
# some exceptions jump the queue - a hand pay pending is a person standing at a machine, and it
# does not wait behind a door that opened an hour ago.
#
# The overflow cases run, with the door making the exceptions; the others need a power cycle or a
# hand pay.

# Twenty is the floor, and they come out oldest first, twice - once with the line down and once
# across a power cycle.
test "4.8 the queue holds at least twenty, in order, across a power cycle" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.power_cycle

  note "With nothing polling, create twenty exceptions - doors, the audit menu, the service button. Start polling: all twenty come back one per general poll in the order they happened, then $00 on every poll after. No buffer overflow exception $70 appears, because twenty did not overflow anything."
  note "Do it again, but power cycle before polling. All twenty still come back oldest first, possibly with the machine's own power-down and power-up exceptions among them."
}

# When it does fill, the oldest are lost and the newest survive. The depth asserted is the gaming
# machine library's sixty-four.
test "4.9 a full queue loses the oldest" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires host.poll_sequence, stimulus.door.main, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s

  host poll sequence
  host send sync

  # Step 2: sixty-eight exceptions, four more than this machine's queue of sixty-four holds. The
  # door opens on the odd ones and closes on the even ones, and a poll to another machine after
  # each keeps the link up without drawing anything out.
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02

  # Step 3: the overflow comes first and is not one of the sixty-four; then the sixty-four that
  # fit, oldest first - the fifth to the sixty-eighth - and then no activity.
  host send sync
  host send gp draws exception_buffer_overflow
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws slot_door_opened
  host send gp draws silence to 02
  host send gp draws slot_door_closed
  host send gp draws silence to 02
  host send gp draws nothing

  host poll every 200ms

  note "The queue depth of sixty-four is the gaming machine library's; a machine with a different depth fails this case at the first exception that does not match, which says where its queue ends."
}

# One overflow exception, not one per lost event, and it is a priority exception so it arrives
# ahead of whatever is still queued.
test "4.10 the machine reports an overflow once" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires host.poll_sequence, stimulus.door.main, stimulus.settle
  requires decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s

  host poll sequence
  host send sync

  # Step 1: sixty-seven exceptions, three more than the queue holds, so the first three are lost
  # and the fourth, fifth and sixth are the oldest left. The door is left open by the last.
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02

  # Step 3.
  host send sync
  host send lp 1F

  # Step 4: three in a row; the overflow comes first, and the identification poll is answered.
  host send gp draws exception_buffer_overflow
  host send gp draws exception_buffer_overflow
  host send gp
  host send lp 1F

  # Step 5: nothing acknowledged it, so the overflow comes once more.
  host send gp draws exception_buffer_overflow
  host send lp 1F

  # Step 6: then the fourth, fifth and sixth.
  host send sync
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F

  # Step 7: four more, which overflows the queue a second time and loses the seventh.
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02
  egm door main close
  host send gp draws silence to 02
  egm door main open
  host send gp draws silence to 02

  # Step 8 to 10: the overflow again, then the rest, oldest first, then no activity.
  host send sync
  host send lp 1F
  host send gp draws exception_buffer_overflow
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws nothing
  host send lp 1F

  # Put the door back.
  host poll every 200ms
  egm door main close

  note "The document says the last three of step 10 are the last of step 1; with the four of step 7 added they are the last of step 7, which is what is asserted."
}

# A hand pay pending goes first even though four exceptions were queued before it.
test "4.11 a priority exception goes first" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.handpay, host.poll_sequence, stimulus.button.service
  requires decoder.lp1B

  note "Create slot door opened, slot door closed, change lamp on and change lamp off. Then put enough credits on the machine to exceed its cash out limit and press cash out, so it locks up in a cancelled-credit hand pay."
  note "With the host's resynchronisation off, establish communication and send the general poll three times then the identification poll: the first two carry hand pay pending $51, ahead of all four queued exceptions. Then the four arrive in the order they happened, then no activity."
  note "The same shape is owed by every priority exception, highest first: $57, $67, $68, $3F, $6A, $6B, $6F, $56, $3D, $3E, $69, $6C, $6D, $51, $52, $8F, $70."
  note "Reset the hand pay when the case is done."
}

# A priority exception that is never acknowledged is reissued on a timer rather than sent once.
test "4.12 a priority exception is reissued until it is acknowledged" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.handpay
  requires decoder.lp1B

  note "Put the machine into a cancelled-credit hand pay, then poll it. Hand pay pending $51 comes back on a general poll every fifteen seconds, with other exceptions on the general polls in between, and long polls answered normally throughout."
  note "Reset the hand pay when the case is done. The same is owed by every other priority exception."
}

# Priority against priority: the hand pay goes before the overflow, and both go before the queue.
test "4.13 priority exceptions come out in their own order" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.handpay, machine.exception_queue_depth, host.poll_sequence
  requires decoder.lp1B

  note "With nothing polling, overflow the queue, noting the last four exceptions. Then put the machine into a cancelled-credit hand pay."
  note "Establish communication and alternate the general poll with the identification poll five times: the first general poll carries hand pay pending $51, the second carries the overflow $70, and the rest carry the queued exceptions oldest first."
  note "Then poll normally until no activity comes back: the remainder arrive oldest first. Reset the hand pay when the case is done."
}
