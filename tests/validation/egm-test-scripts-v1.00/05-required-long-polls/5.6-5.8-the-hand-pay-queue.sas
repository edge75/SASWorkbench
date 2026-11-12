# Chapter 5, sections 5.6 to 5.8 - several hand pays at once, and what survives a power cycle.
#
# A queueing machine holds every hand pay until the host has read it and acknowledged the reading,
# raising $51 every fifteen seconds while one is unread and $52 only after the reading is
# acknowledged. These cases fill the queue, refuse to acknowledge, overflow it, and cut the power
# in the middle, and they check that the readings come out in the order the hand pays happened.
#
# All but the legacy case need a machine that holds more than one hand pay. The software machine
# keeps one at a time and refuses a second, and no person-driven stimulus here can make one either,
# so they skip on 'machine.handpay_queue' until one of the two can.

test "5.6.1 five queued hand pays are read in the order they happened" {
  tag @egm-test-scripts @chapter-5 @needs-a-person
  requires machine.handpay_queue, stimulus.handpay, stimulus.handpay.jackpot, stimulus.handpay.reset
  requires decoder.lp1B

  note "Configure the machine for a hand pay queue with low hopper and jackpot limits and no partial pay. With the round running, alternate a cash-out hand pay and a jackpot hand pay, noting each amount and resetting each at the machine, until five are outstanding and the last is still locked up. $51 arrives every fifteen seconds throughout."
  note "Then poll a round that includes the hand pay information poll. The first reading after each $51 is the oldest unread hand pay, in the order they happened; once the reading is acknowledged $52 follows for each of the first four. Any reading between a $52 and the next $51 is all zeros."
  note "Reset the last hand pay at the machine: its $52 arrives once it has been read."
}

# The reading is resent until acknowledged: a repeat of the same poll is a refusal, and so is a
# synchronisation poll followed by the same read.
test "5.6.2 an unacknowledged reading is not consumed" {
  tag @egm-test-scripts @chapter-5 @needs-a-person
  requires machine.handpay_queue, host.poll_sequence, stimulus.handpay, stimulus.handpay.jackpot, stimulus.handpay.reset
  requires decoder.lp1B

  note "Configure a hand pay queue. Make a cash-out hand pay, reset it at the machine, then make a jackpot hand pay and leave it locked up. Stop the round and let the machine fall silent."
  note "Send a broadcast and three hand pay readings back to back: the first two carry the identical cash-out information, and the third repeats it or is not answered."
  note "Send a broadcast, two readings, a broadcast and a reading: the first two are the same cash-out again, and the last, after the broadcast acknowledged them, is all zeros."
  note "Alternate the broadcast with a general poll until $52 and then $51 arrive - not necessarily one straight after the other. A broadcast, a reading, a broadcast and a reading then give the jackpot and then zeros. Restart the round and reset the jackpot at the machine: $52."
}

# A full queue drops its oldest entry rather than refusing the newest.
test "5.7 a full queue loses its oldest hand pay" {
  tag @egm-test-scripts @chapter-5 @needs-a-person
  requires machine.handpay_queue, machine.handpay_queue_size, stimulus.handpay, stimulus.handpay.jackpot, stimulus.handpay.reset
  requires decoder.lp1B

  note "Ask the manufacturer how many hand pays the machine queues. Alternate jackpot and cash-out hand pays, noting each amount and resetting each at the machine, until one more than that number is outstanding, with $51 every fifteen seconds throughout."
  note "Read the hand pay information repeatedly until $51 stops. The first reading is the second-oldest hand pay - the oldest was overwritten - and each acknowledged reading brings $52 and then the next-newest, so exactly the queue's size of the latest hand pays come out."
}

test "5.8.1 queued hand pays survive a power cycle" {
  tag @egm-test-scripts @chapter-5 @needs-a-person @destructive
  requires machine.handpay_queue, machine.power_cycle, host.poll_sequence, stimulus.handpay, stimulus.handpay.jackpot, stimulus.handpay.reset
  requires decoder.lp1B

  note "Configure a hand pay queue. Make a cash-out hand pay and reset it at the machine without reading it, then make a jackpot hand pay. $51 every fifteen seconds throughout."
  note "Stop the round, then send a general poll and one hand pay reading, and leave that reading unacknowledged: it carries the cash out, shaped as in 5.4.1. Cut the power and wait for the machine to boot: $51 every fifteen seconds."
  note "Restart the round and read again: the cash out again, then $52 and $51. Power cycle once more: $51 every fifteen seconds. Read and wait thirty seconds: the jackpot, shaped as in 5.3.1. Reset it at the machine: $52."
}

test "5.8.2 a legacy hand pay survives a power cycle without being announced again" {
  tag @egm-test-scripts @chapter-5 @needs-a-person @destructive
  requires machine.legacy_handpay, machine.power_cycle, stimulus.handpay, stimulus.handpay.jackpot, stimulus.handpay.reset
  requires decoder.lp1B

  note "Configure legacy hand pay with low hopper and jackpot limits and no partial pay. Cash out over the hopper limit and wait thirty seconds: one $51. Power cycle: the machine is still locked up and does not send $51 again."
  note "Read the hand pay information: the cash out, shaped as in 5.4.1. Reset it at the machine: $52."
  note "Play to a jackpot hand pay and wait thirty seconds: one $51. Power cycle: still locked up, no further $51. Read: the jackpot. Reset: $52."
}
