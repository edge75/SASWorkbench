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
  tag @egm-test-scripts @chapter-3 @needs-stimulus
  requires host.poll_sequence, stimulus.handpay, stimulus.handpay.reset, stimulus.door.main
  requires stimulus.button.service, stimulus.settle
  requires decoder.lp1B, decoder.lp1F

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
  egm door main open
  egm door main close
  egm press service
  egm press service
  host send sync
  host send lp 1F

  # Step 1: twice the same general poll draws the same exception; the identification poll then
  # acknowledges it and the next general poll moves on.
  host send gp draws slot_door_opened
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F

  # Step 2: the same for the change lamp.
  host send gp draws change_lamp_on
  host send gp draws change_lamp_on
  host send lp 1F
  host send gp draws change_lamp_off
  host send lp 1F
  host send gp draws nothing

  # Step 3: the hand pay information poll in place of the general one.
  egm handpay 2000
  host wait 2s
  host send sync
  host send lp 1F
  host send gp draws handpay_is_pending
  host send lp 1F
  host send lp 1B
  host expect field amount of lp 1B != 0
  host send lp 1B
  host expect field amount of lp 1B != 0
  host send lp 1F
  host send lp 1B
  # All zeros is the hand pay queue rule (SAS 6.02 7.8.1); libsas answers it from v5.4.6.14 (quixantplc/libsas#258).
  host expect field amount of lp 1B == 0

  host poll every 200ms
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "The document makes the hand pay a jackpot; a cancelled-credit hand pay reads the same on 1B. It also repeats the sequence forty milliseconds apart, which this case does not time."
}

# Three the same is final. The third answer is optional - the machine may repeat it or stay quiet -
# and both are a pass, which is the only place in this chapter where two behaviours are allowed.
test "3.21 the same poll three times is final" {
  tag @egm-test-scripts @chapter-3 @needs-stimulus
  requires host.poll_sequence, stimulus.door.main, stimulus.button.service, stimulus.settle
  requires decoder.lp1F, decoder.lp7E, host.compare_replies

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

  # Steps 8 and 9 with the date and time poll: a repeat carries the same time as the first, and
  # after the identification poll acknowledges it the next one carries a fresh time.
  host send lp 7E
  host remember field day as day
  host remember field hour as hour
  host remember field minute as minute
  host remember field second as second
  host send lp 7E
  host expect field day == saved.day
  host expect field hour == saved.hour
  host expect field minute == saved.minute
  host expect field second == saved.second
  host send lp 1F
  # The pause is ours, not the document's: it lets the clock move on by more than its resolution.
  host wait 2s
  host send lp 7E
  host expect field second != saved.second

  note "The document sends the date and time poll a third time in a row, which may go unanswered. A long poll sent here must be answered, so that third one is left out and the identification poll follows the repeat directly."
}

# A power cycle does not clear the retry. The machine comes back still owing the same exception.
test "3.22 a power cycle does not clear a final refusal" {
  tag @egm-test-scripts @chapter-3 @needs-a-person @destructive
  requires host.poll_sequence, machine.power_cycle, stimulus.door.main, stimulus.button.service, stimulus.settle
  requires decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host forget exceptions within 2s

  # Step 1: slot door opened, slot door closed and change lamp on wait in the queue.
  host poll sequence
  host send sync
  egm door main open
  egm door main close
  egm press service
  host send sync
  host send lp 1F

  # Step 2: the first is drawn and acknowledged, the second is drawn and left unacknowledged.
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed

  # Step 3: after a power cycle, two general polls both carry the unacknowledged exception.
  egm power cycle
  # The machine chirps after booting, so the first sync may read a chirp as its reply; the second
  # is answered on a quiet line.
  host send sync
  host send sync
  host send gp draws slot_door_closed
  host send gp draws slot_door_closed

  # Step 4: three in a row; the third may repeat it or go unanswered, so it is sent, not asserted.
  egm power cycle
  # The machine chirps after booting, so the first sync may read a chirp as its reply; the second
  # is answered on a quiet line.
  host send sync
  host send sync
  host send gp draws slot_door_closed
  host send gp draws slot_door_closed
  host send gp

  # Step 5: the same with the identification poll on the end.
  egm power cycle
  # The machine chirps after booting, so the first sync may read a chirp as its reply; the second
  # is answered on a quiet line.
  host send sync
  host send sync
  host send gp draws slot_door_closed
  host send gp draws slot_door_closed
  host send gp
  host send lp 1F

  # Step 6: the identification poll in between is the acknowledgement the retries never had.
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws change_lamp_on

  # Leaves the change lamp off for the next case.
  egm press service
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
