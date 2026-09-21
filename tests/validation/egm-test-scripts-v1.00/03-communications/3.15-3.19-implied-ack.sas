# Chapter 3, sections 3.15 to 3.19 - what counts as an acknowledgement.
#
# SAS has no acknowledgement byte. A machine learns that the host received its last reply from the
# next poll it sees: any poll that is not a repeat of the one just answered is an acknowledgement,
# and a repeat is a refusal. These five cases enumerate the four shapes that acknowledge - a
# general poll after a long one, a different long poll, a long poll after a general one, and a poll
# to somebody else or to everybody.
#
# All five need the host to put an exact sequence of named polls on the line and nothing else.
# This host runs a round on a timer and sends a synchronisation poll ahead of every message, and a
# synchronisation poll is itself a global broadcast - which 3.19 says is an acknowledgement. So
# the host cannot present the machine with the sequence any of these cases describe.

# A general poll to the machine's own address acknowledges the long poll it just answered.
test "3.15 a general poll acknowledges a long poll" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.poll_sequence, stimulus.service_button

  note "Create four exceptions in order - slot door opened, slot door closed, change lamp on, change lamp off. Establish communication with a poll to address 00 followed by the identification poll, then alternate a general poll to the machine's address with that identification poll, four times."
  note "Each general poll draws the next exception, in the order the four happened, and each identification poll is answered with the machine's information. Keep alternating: the general polls end with no activity."
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
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.poll_sequence, stimulus.service_button
  requires decoder.lp7E

  note "Create the same four exceptions. Establish communication, then alternate the send date and time poll with a general poll to the machine's address, four times: each date and time reply is current, and each general poll draws the next of the four exceptions."
  note "Send date and time, general poll, date and time once more. The second general poll reports no activity."
}

# A poll addressed to a different machine acknowledges as well, and must not be answered.
test "3.18 a poll to another machine acknowledges" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.poll_sequence, stimulus.handpay, stimulus.service_button
  requires decoder.lp1B, decoder.lp7E

  note "Configure the machine for a hand pay queue and create a jackpot hand pay, and create the four door and change lamp exceptions. Establish communication, then send a general poll to the machine's address, the hand pay information poll, a send date and time addressed to another machine, the hand pay poll again, and that foreign poll again."
  note "The general poll draws the hand-pay-pending exception, the first hand pay reading carries the amount, the foreign polls are not answered at all, and the second hand pay reading is all zeros - the foreign poll acknowledged the first."
  note "Then alternate a general poll to the machine's address with a general poll to another address, and with the foreign date and time poll, until the exceptions run out. Each of the machine's own general polls draws the next exception and none of the foreign polls is answered."
}

# A global broadcast - a poll to address 00 - acknowledges too, which is the case that makes this
# host's own synchronisation poll impossible to keep off the line.
test "3.19 a global broadcast acknowledges" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.poll_sequence, stimulus.handpay, stimulus.service_button
  requires decoder.lp1B, decoder.lp7F

  note "Configure the machine for a hand pay queue, create a jackpot hand pay, and create the change lamp on and change lamp off exceptions. Establish communication, then send a general poll to the machine's address, the hand pay information poll, a receive date and time addressed to 00, the hand pay poll again, and that broadcast again."
  note "The first hand pay reading carries the amount, the broadcast is not answered, and the second reading is all zeros. Then alternate the broadcast with a general poll to the machine's address until the exceptions run out: the general polls draw them and the broadcast is never answered."
}
