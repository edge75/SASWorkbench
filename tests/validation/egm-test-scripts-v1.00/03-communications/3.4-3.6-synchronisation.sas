# Chapter 3, sections 3.4 to 3.6 - losing the link and getting it back.
#
# A machine that has heard nothing for five seconds declares the loop broken, throws away
# everything queued against its last reply, and chirps its own address every 200 ms until some
# poll arrives. It then waits for a synchronisation poll before it will act on anything addressed
# to it. The waiting is observable from a sequence; the chirp is not asserted by this build.

# The chirp itself, and what silences it. Three addresses are polled in turn - another machine's,
# this machine's, and the synchronisation address - and each stops the chirp, which is the point:
# the machine is listening to the line rather than to its own address.
test "3.4 loop break" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires host.chirp

  note "Bring the link up, stop polling, and wait more than five seconds. The machine sends one byte every 200 ms - its own address with the wakeup bit set. Then send a long poll to another machine's address: the chirp stops. Wait five seconds again and send one to this machine's address: it stops. Wait again and send one to address 00: it stops. Any poll on the line ends the chirp, whoever it is addressed to."
  note "Start polling again. After the first synchronisation poll the machine answers general polls with an exception byte and answers long polls."
}

# What a machine does with the polls that arrive before it has been synchronised. The GSA tool
# turns its own resynchronisation off to test this; here a sequence does the same.
test "3.5.1 the machine waits for a synchronisation poll" {
  tag @egm-test-scripts @chapter-3 @needs-stimulus
  requires host.poll_sequence, stimulus.door.main, stimulus.settle
  requires decoder.lp1F

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # Steps 1 and 2: one exception waiting, then the line goes quiet for longer than a loop break.
  host poll sequence
  host send sync
  egm door main open
  host wait 6s

  # Step 6: after the silence the machine ignores its own polls until it sees address 00.
  host expect no reply to lp 1F
  host send sync
  host send lp 1F

  # Steps 7 and 8: the same with general polls - only the one after the poll to 00 is answered.
  host wait 6s
  host send gp draws silence
  host send sync
  host send gp draws slot_door_opened

  # Step 9.
  host send sync
  host send lp 1F

  # Put the door back.
  host poll every 200ms
  egm door main close

  note "The document's poll to address 00 in steps 6 and 9 is the identification long poll sent to 00; this host's synchronisation poll, a general poll to 00, stands in for it. During each silence the document has the machine chirp every 200 ms; this build does not assert the chirp."
}

# A hand pay survives the loop break that discarded its acknowledgement. The machine answers the
# hand pay poll with the same information again after the link comes back, and only the second
# reading - the acknowledged one - comes back zeroed.
test "3.5.2 a hand pay survives an implied NACK" {
  tag @egm-test-scripts @chapter-3 @needs-a-person
  requires stimulus.handpay, host.poll_sequence
  requires decoder.lp1B

  note "Put enough credits on the machine to exceed its hopper limit and cash out, so it locks up in a cancelled-credit hand pay. Stop polling, ask for the hand pay information, and wait more than five seconds. The reply carries the amount in accounting credits."
  note "Bring the link back and ask again: the same amount. Ask a third time: the data is all zeros. A machine that lost the hand pay over the loop break, or that zeroed it on the first reading, fails - and it fails the same way whether it keeps a queue of hand pays or the single legacy one."
}

# The same waiting-for-synchronisation rule, established from a cold start rather than from a
# loop break, which is the harder half: nothing was queued and nothing was lost, and the machine
# must still refuse addressed polls until it has seen address 00.
test "3.6 synchronisation after a power cycle" {
  tag @egm-test-scripts @chapter-3 @needs-a-person @destructive
  requires machine.power_cycle, host.poll_sequence, host.chirp

  note "Bring the link up, cut the machine's power, stop polling, and power it back on. Once it has booted it chirps every 200 ms."
  note "With the host's resynchronisation turned off, poll it: it ignores everything. Power cycle again and poll again: still nothing. Stop polling and send its identification poll, a poll to address 00, and its identification poll again - only the third is answered."
  note "Power cycle once more, then send a general poll to its address, one to another address, and one to its address again. Only the third draws an exception byte."
}
