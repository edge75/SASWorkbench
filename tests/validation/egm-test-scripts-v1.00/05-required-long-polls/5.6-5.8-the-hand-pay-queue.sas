# Chapter 5, sections 5.6 to 5.8 - several hand pays at once, and what survives a power cycle.
#
# A queueing machine holds every hand pay until the host has read it and acknowledged the reading,
# raising $51 every fifteen seconds while one is unread and $52 only after the reading is
# acknowledged. These cases fill the queue, overflow it, and cut the power in the middle, and they
# check that the readings come out in the order the hand pays happened. Each declares the five-cent
# accounting unit, so a twenty-dollar cash out reads 400 and a fifteen-hundred-dollar jackpot 30000.
#
# The software machine keeps the library's queue of sixteen on every channel, and is built in legacy
# reporting when a case declares 'machine handpay legacy'.
#
# @finding: 5.8.2 - the library announces a hand pay still awaiting its reading again at power up
# whichever reporting mode is in force (libsas_internal.cpp:887), so a legacy machine sends $51 again.

test "5.6.1 five queued hand pays are read in the order they happened" {
  tag @egm-test-scripts @chapter-5 @needs-stimulus
  requires machine.handpay_queue, stimulus.handpay, stimulus.handpay.jackpot, stimulus.handpay.reset, stimulus.settle
  requires decoder.lp1B
  machine accounting denomination 5c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # Jackpots and cash outs in turn, each reset at the machine before the host reads it but the last.
  egm handpay jackpot 100500
  host expect exception handpay_is_pending within 20s
  egm handpay reset
  egm handpay 500
  host expect exception handpay_is_pending within 20s
  egm handpay reset
  egm handpay jackpot 101500
  host expect exception handpay_is_pending within 20s
  egm handpay reset
  egm handpay 1000
  host expect exception handpay_is_pending within 20s
  egm handpay reset
  egm handpay jackpot 102500
  host expect exception handpay_is_pending within 20s

  # Each reading is the oldest unread, and its acknowledgement brings that one's $52, then the next one's $51.
  host send lp 1B
  host expect field amount of lp 1B == 20100
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 100
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 20300
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 200
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 20500
  host wait 1s
  host send lp 1B
  host expect field amount of lp 1B == 0

  # The last was read before its reset, so its $52 comes at once.
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "The amounts are in five-cent units: the jackpots and the cash outs as the prompts above made them."
  note "The document also reads between a $52 and the next $51 and expects zeros; the round polls in between here, so that reading is not isolated."
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
  tag @egm-test-scripts @chapter-5 @needs-stimulus
  requires machine.handpay_queue, machine.handpay_queue_size, stimulus.handpay, stimulus.handpay.jackpot, stimulus.handpay.reset
  requires stimulus.settle, decoder.lp1B
  machine accounting denomination 5c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # Seventeen hand pays, one more than the sixteen the queue holds, each reset before the host reads it.
  egm handpay jackpot 100500
  egm handpay reset
  egm handpay 500
  egm handpay reset
  egm handpay jackpot 101500
  egm handpay reset
  egm handpay 1000
  egm handpay reset
  egm handpay jackpot 102500
  egm handpay reset
  egm handpay 2000
  egm handpay reset
  egm handpay jackpot 103500
  egm handpay reset
  egm handpay 5000
  egm handpay reset
  egm handpay jackpot 104500
  egm handpay reset
  egm handpay 500
  egm handpay reset
  egm handpay jackpot 105500
  egm handpay reset
  egm handpay 1000
  egm handpay reset
  egm handpay jackpot 106500
  egm handpay reset
  egm handpay 2000
  egm handpay reset
  egm handpay jackpot 107500
  egm handpay reset
  egm handpay 5000
  egm handpay reset
  egm handpay jackpot 108500
  egm handpay reset
  host expect exception handpay_is_pending within 20s

  # The first reading is the second hand pay: the first was overwritten. Each acknowledged reading brings
  # its $52, then the next one's $51.
  host send lp 1B
  host expect field amount of lp 1B == 100
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 20300
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 200
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 20500
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 400
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 20700
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 1000
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 20900
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 100
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 21100
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 200
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 21300
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 400
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 21500
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 1000
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field amount of lp 1B == 21700
  host expect exception handpay_was_reset within 15s
  host wait 1s
  host send lp 1B
  host expect field amount of lp 1B == 0
  host forget exceptions within 2s
  host expect no exception handpay_is_pending within 20s

  note "The queue's size is the machine's own: sixteen on the software machine, as in the library it mirrors. A machine with another size needs a case of its own."
}

test "5.8.1 queued hand pays survive a power cycle" {
  tag @egm-test-scripts @chapter-5 @needs-stimulus @destructive
  requires machine.handpay_queue, machine.power_cycle, host.poll_sequence, stimulus.handpay, stimulus.handpay.jackpot, stimulus.handpay.reset
  requires stimulus.settle, decoder.lp1B
  machine accounting denomination 5c

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # A cash out reset before it is read, then a jackpot that stays locked up.
  egm handpay 2000
  host expect exception handpay_is_pending within 10s
  egm handpay reset
  egm handpay jackpot 150000
  host forget exceptions within 2s
  host expect exception handpay_is_pending within 20s

  # One reading, left unacknowledged: the line is held and the power cut straight after it.
  host poll sequence
  host send sync
  host send gp
  host send lp 1B
  host expect field level of lp 1B == $80
  host expect field amount of lp 1B == 400
  egm power cycle

  # The reading was never acknowledged, so after the restart it is owed again.
  host poll every 200ms
  host expect link up within 120s
  host expect exception handpay_is_pending within 20s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field level of lp 1B == $80
  host expect field amount of lp 1B == 400
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s

  # Once more across a power cycle, and then the jackpot.
  egm power cycle
  host expect link up within 120s
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field level of lp 1B == 0
  host expect field amount of lp 1B == 30000
  host forget exceptions within 2s
  host expect no exception handpay_is_pending within 30s
  egm handpay reset
  host expect exception handpay_was_reset within 15s
}

test "5.8.2 a legacy hand pay survives a power cycle without being announced again" {
  tag @egm-test-scripts @chapter-5 @needs-stimulus @destructive @finding
  requires machine.legacy_handpay, machine.power_cycle, stimulus.handpay, stimulus.handpay.jackpot, stimulus.handpay.reset
  requires stimulus.settle, decoder.lp1B
  machine accounting denomination 5c
  machine handpay legacy

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # A cash out paid by hand: one $51, not one every fifteen seconds.
  egm handpay 2000
  host expect exception handpay_is_pending within 10s
  host forget exceptions within 2s
  host expect no exception handpay_is_pending within 30s

  # Still locked up after a power cycle, and not announced again.
  egm power cycle
  host expect link up within 120s
  host expect no exception handpay_is_pending within 20s
  host send lp 1B
  host expect field level of lp 1B == $80
  host expect field amount of lp 1B == 400
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  # The same for a jackpot.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host forget exceptions within 2s
  host expect no exception handpay_is_pending within 30s
  egm power cycle
  host expect link up within 120s
  host expect no exception handpay_is_pending within 20s
  host send lp 1B
  host expect field level of lp 1B == 0
  host expect field amount of lp 1B == 30000
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "Configure legacy hand pay before the run; on a Quixant machine it can only be chosen straight after a memory reset."
}
