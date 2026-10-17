# Chapter 4, sections 4.8 to 4.13 - how deep the queue is and what comes out of it first.
#
# Three things are being established here. The queue holds at least twenty exceptions in
# non-volatile memory; when it fills, the oldest go and the machine says so once with $70; and
# some exceptions jump the queue - a hand pay pending is a person standing at a machine, and it
# does not wait behind a door that opened an hour ago.
#
# Every case needs something this environment does not have: a power cycle, the machine's own
# queue depth, or a hand pay.

# Twenty is the floor, and they come out oldest first, twice - once with the line down and once
# across a power cycle.
test "4.8 the queue holds at least twenty, in order, across a power cycle" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires machine.power_cycle

  note "With nothing polling, create twenty exceptions - doors, the audit menu, the service button. Start polling: all twenty come back one per general poll in the order they happened, then $00 on every poll after. No buffer overflow exception $70 appears, because twenty did not overflow anything."
  note "Do it again, but power cycle before polling. All twenty still come back oldest first, possibly with the machine's own power-down and power-up exceptions among them."
}

# When it does fill, the oldest are lost and the newest survive. Which needs the queue's depth,
# and only the machine knows that.
test "4.9 a full queue loses the oldest" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires machine.exception_queue_depth

  note "With nothing polling, create more exceptions than the machine can hold, noting the last four that fit. Start polling: the count of non-zero exceptions returned equals the queue's depth, and the last four - the overflow exception $70 aside - are the four noted. The overflow exception is not itself put in the queue."
  note "Establish the queue's depth first; the document expects it to be done alongside 4.10."
}

# One overflow exception, not one per lost event, and it is a priority exception so it arrives
# ahead of whatever is still queued.
test "4.10 the machine reports an overflow once" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires machine.exception_queue_depth, host.poll_sequence

  note "Overflow the queue by three, noting the fourth, fifth and sixth exceptions and the three that were pushed out. With the host's resynchronisation off, establish communication and send the general poll three times then the identification poll: the first two carry $70, the third carries it or is unanswered."
  note "Send the general poll again: $70 once more, because nothing had acknowledged it. Then alternate the general and identification polls: the next three exceptions are the fourth, fifth and sixth noted."
  note "Create four more exceptions and re-establish communication. The general poll carries $70 again - the queue overflowed a second time - and then the rest, ending in no activity, with the last three being the last created."
}

# A hand pay pending goes first even though four exceptions were queued before it.
test "4.11 a priority exception goes first" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.handpay, host.poll_sequence, stimulus.service_button
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
