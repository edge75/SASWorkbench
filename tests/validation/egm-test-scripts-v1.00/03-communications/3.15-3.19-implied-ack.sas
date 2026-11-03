# Chapter 3, sections 3.15 to 3.19 - what counts as an acknowledgement.
#
# SAS has no acknowledgement byte. A machine learns that the host received its last reply from the
# next poll it sees: any poll that is not a repeat of the one just answered is an acknowledgement,
# and a repeat is a refusal. These five cases enumerate the four shapes that acknowledge - a
# general poll after a long one, a different long poll, a long poll after a general one, and a poll
# to somebody else or to everybody.
#
# They need the host to put an exact sequence of named polls on the line and nothing else, which
# `host poll sequence` gives them: the timer and its synchronisation polls stop, and the case
# sends each poll itself. The ones that can make their exceptions here - doors and the service
# button - run; the rest need a hand pay or a power cycle.

# A general poll to the machine's own address acknowledges the long poll it just answered.
test "3.15 a general poll acknowledges a long poll" {
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

  # Step 4: each identification poll is answered, and each general poll after it draws the next
  # of the four in the order they happened.
  host send gp draws slot_door_opened
  host send lp 1F
  host send gp draws slot_door_closed
  host send lp 1F
  host send gp draws change_lamp_on
  host send lp 1F
  host send gp draws change_lamp_off
  host send lp 1F

  # Step 5: and then no activity.
  host send gp draws nothing
  host send lp 1F
}

# A different long poll acknowledges the one before it, and the hand pay information proves it -
# read once it carries the amount, read again after the acknowledgement it comes back zeroed.
test "3.16 a different long poll acknowledges the one before it" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.poll_sequence, stimulus.handpay
  requires decoder.lp1B

  note "Create a jackpot hand pay. Establish communication, then send a general poll to the machine's address and the hand pay information poll: the general poll draws the hand-pay-pending exception and the hand pay poll carries the amount."
  note "Within five seconds send the identification poll and then the hand pay poll again. The identification poll acknowledged the hand pay reading, so this one comes back with its data all zeros."
}

# The mirror of 3.15: a long poll acknowledges the general poll that carried an exception.
test "3.17 a long poll acknowledges a general poll" {
  tag @egm-test-scripts @chapter-3 @needs-stimulus
  requires host.poll_sequence, stimulus.door.main, stimulus.button.service, stimulus.settle
  requires decoder.lp1F, decoder.lp7E

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

  # Step 4: the date and time poll between two general polls is what lets the second move on.
  host send lp 7E
  host send gp draws slot_door_opened
  host send lp 7E
  host send gp draws slot_door_closed
  host send lp 7E
  host send gp draws change_lamp_on
  host send lp 7E
  host send gp draws change_lamp_off

  # Step 5: date and time, no activity, date and time.
  host send lp 7E
  host send gp draws nothing
  host send lp 7E

  note "The document also has each date and time reply carry the current time; this build cannot compare a reply's time with the clock, so only that each was answered is asserted."
}

# A poll addressed to a different machine acknowledges as well, and must not be answered.
test "3.18 a poll to another machine acknowledges" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.poll_sequence, stimulus.handpay, stimulus.button.service
  requires decoder.lp1B, decoder.lp7E

  note "Configure the machine for a hand pay queue and create a jackpot hand pay, and create the four door and change lamp exceptions. Establish communication, then send a general poll to the machine's address, the hand pay information poll, a send date and time addressed to another machine, the hand pay poll again, and that foreign poll again."
  note "The general poll draws the hand-pay-pending exception, the first hand pay reading carries the amount, the foreign polls are not answered at all, and the second hand pay reading is all zeros - the foreign poll acknowledged the first."
  note "Then alternate a general poll to the machine's address with a general poll to another address, and with the foreign date and time poll, until the exceptions run out. Each of the machine's own general polls draws the next exception and none of the foreign polls is answered."
}

# A global broadcast - a poll to address 00 - acknowledges too, which is the case that makes this
# host's own synchronisation poll impossible to keep off the line.
test "3.19 a global broadcast acknowledges" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.poll_sequence, stimulus.handpay, stimulus.button.service
  requires decoder.lp1B, decoder.lp7F

  note "Configure the machine for a hand pay queue, create a jackpot hand pay, and create the change lamp on and change lamp off exceptions. Establish communication, then send a general poll to the machine's address, the hand pay information poll, a receive date and time addressed to 00, the hand pay poll again, and that broadcast again."
  note "The first hand pay reading carries the amount, the broadcast is not answered, and the second reading is all zeros. Then alternate the broadcast with a general poll to the machine's address until the exceptions run out: the general polls draw them and the broadcast is never answered."
}
