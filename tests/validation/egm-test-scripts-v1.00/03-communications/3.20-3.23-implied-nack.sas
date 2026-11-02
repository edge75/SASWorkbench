# Chapter 3, sections 3.20 to 3.23 - what counts as a refusal.
#
# The other half of section 3.15's rule. A poll repeated once says the host did not get the answer,
# so the machine sends the same thing again; repeated a third time it is final, and the machine may
# answer once more or say nothing, but either way it moves on. Two things end the retry besides a
# third try: a power cycle, and five seconds of silence.
#
# They need the host to put an exact sequence of named polls on the line and nothing else, which
# `host poll sequence` gives them: the timer and its synchronisation polls stop, and the case
# sends each poll itself. The ones that can make their exceptions here - doors and the service
# button - run; the rest need a hand pay or a power cycle.

# Two the same is a refusal; three is final.
test "3.20 the same poll twice is a refusal" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.poll_sequence, stimulus.handpay, stimulus.button.service
  requires decoder.lp1B

  note "Create four exceptions in order - slot door opened, slot door closed, change lamp on, change lamp off. Establish communication, then send a general poll to the machine's address twice, the identification poll, the general poll, the identification poll."
  note "The first two general polls both draw slot door opened - the repeat is a refusal, so the machine sends it again - and the third draws slot door closed. Repeat the sequence for change lamp on and change lamp off."
  note "Then a jackpot hand pay, with the hand pay information poll standing in for the general one: the first two readings carry the same amount, the third comes back all zeros."
  note "The rule holds whatever the gap: the same three general polls forty milliseconds apart behave exactly as they do two hundred apart."
}

# Three the same is final. The third answer is optional - the machine may repeat it or stay quiet -
# and both are a pass, which is the only place in this chapter where two behaviours are allowed.
test "3.21 the same poll three times is final" {
  tag @egm-test-scripts @chapter-3 @needs-stimulus
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

  # Step 4.
  host send gp draws slot_door_opened
  host send lp 1F

  # Step 5: three in a row. The first two carry the same exception, and the third may carry it
  # again or go unanswered - both are allowed, so the third is sent and not asserted. The
  # identification poll after them is answered.
  host send gp draws slot_door_closed
  host send gp draws slot_door_closed
  host send gp
  host send lp 1F

  # Step 6: a final refusal acknowledges nothing, so the exception it refused comes round again
  # before the other two.
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws change_lamp_on
  host send lp 1F
  host send gp draws change_lamp_off
  host send lp 1F

  # Step 7.
  host send gp draws nothing
  host send lp 1F

  note "Steps 8 and 9 repeat the shape with the date and time poll: three in a row, where the first two carry the same time and the third repeats it or is unanswered, then date and time, identification, date and time, where the last carries a new time. This build asserts a long poll only by its answer and cannot compare one reply with another, so they are not run."
}

# A power cycle does not clear the retry. The machine comes back still owing the same exception.
test "3.22 a power cycle does not clear a final refusal" {
  tag @egm-test-scripts @chapter-3 @needs-a-person @destructive
  requires host.poll_sequence, machine.power_cycle, stimulus.button.service

  note "Create slot door opened, slot door closed and change lamp on. Establish communication, then a general poll to the machine's address and the identification poll: the general poll draws slot door opened. Send the general poll once more: it draws slot door closed."
  note "Power cycle the machine and let it boot. Establish communication and send the general poll twice: both draw slot door closed. Power cycle again and send it three times: the first two draw slot door closed and the third either repeats it or is unanswered. Power cycle again and repeat with the identification poll on the end."
  note "Then general poll, identification poll, general poll: the first draws slot door closed, and the second draws change lamp on - the identification poll in between was the acknowledgement the retries never had."
}

# Five seconds of silence is final too, and it is not the same as a power cycle: the machine chirps
# through it.
test "3.23 five seconds of silence is a final refusal" {
  tag @egm-test-scripts @chapter-3 @needs-stimulus
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

  # Step 4.
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed

  # Step 5: a refusal, then a final one that may go unanswered.
  host send gp draws slot_door_closed
  host send gp

  # Steps 6 to 8: the silence is final too, so after it the same exception comes back.
  host wait 6s
  host send sync
  host send lp 1F
  host send gp draws slot_door_closed
  host send gp draws slot_door_closed
  host send lp 1F

  # Step 9: the identification poll acknowledged it, so the next moves on, and is refused once.
  host send gp draws change_lamp_on
  host send gp draws change_lamp_on

  # Steps 10 and 11: silence again, then a poll to another machine is what synchronises it.
  host wait 6s
  host send gp draws silence to 02
  host send gp draws change_lamp_on
  host send gp draws silence to 02
  host send gp draws change_lamp_off

  # Step 12.
  host send lp 1F
  host send gp draws nothing

  note "During each silence the document has the machine chirp every 200 ms; this build does not assert the chirp. Its poll to another machine in step 11 is the identification long poll at address 2; a general poll to address 2 stands in for it, because this build asserts a long poll only by its answer."
}
