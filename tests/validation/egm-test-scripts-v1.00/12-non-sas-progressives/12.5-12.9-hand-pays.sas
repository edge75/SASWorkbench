# Chapter 12, sections 12.5 to 12.9 - a non-SAS progressive won as a hand pay.
#
# With the jackpot limit set low, a progressive win locks the machine up for an attendant like any
# other jackpot. It is reported the queued way: $51 jumps the exception queue, is raised again every
# fifteen seconds until the host has read the hand pay information, survives a power cycle, and is
# closed by $52 only once the reading was acknowledged and the attendant reset it. The reading
# gives group zero, because no SAS host owns the progressive, and an amount in cents that adds
# together everything the attendant pays - the progressive, any machine jackpot won with it, and
# any partial pay.
#
# None of this runs here: the software machine has no progressive (machine.progressives) and this
# build cannot make one hit (stimulus.progressive), so the win is a person's step. Each case also
# needs the jackpot limit set below the progressive's value and partial pay off.

test "12.5 the hand pay of a progressive win is reported first and repeated" {
  tag @egm-test-scripts @chapter-12 @needs-a-person @destructive
  requires machine.progressives, stimulus.progressive, machine.jackpot_limit, machine.power_cycle
  requires host.poll_sequence, stimulus.door.main, stimulus.handpay.reset, decoder.lp1B

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # Two door events queued before the win, with nobody polling.
  host poll sequence
  host send sync
  egm door main open
  egm door main close
  note "Play the progressive game until level 1 is won and the machine locks up for a hand pay."

  # The hand pay comes out ahead of the door events that were queued before it.
  host send gp draws handpay_is_pending
  host send gp draws slot_door_opened
  host send gp draws slot_door_closed

  host poll every 200ms
  host expect exception handpay_is_pending within 20s
  host expect exception handpay_is_pending within 20s

  # Still raised every fifteen seconds after a power cycle.
  egm power cycle
  host expect link up within 120s
  host expect exception handpay_is_pending within 20s
  host expect exception handpay_is_pending within 20s

  # Read and acknowledged, it stops; the reading after that is zeros.
  host send lp 1B
  host expect field progressive_group of lp 1B == 0
  host expect field amount of lp 1B > 0
  host expect no exception handpay_is_pending within 30s
  host send lp 1B
  host expect field amount of lp 1B == 0

  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "Before the run, configure the machine for a non-SAS progressive at level 1, a jackpot limit below the level's value and no partial pay, and select the progressive game."
  note "The document also stops polling for fifteen seconds and then sends a broadcast and three general polls: the first two draw $51 and the third draws $51 or nothing. A broadcast, a general poll, the hand pay reading and a general poll then give $51, the hand pay, and some other exception - the reading was acknowledged. This build sends no broadcast long poll, so those two sequences are a person's to read in the frames."
}

# A second progressive won while the first is still unread: the reset of the first is held back,
# and the readings come out oldest first.
test "12.6 two progressive hand pays are queued" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, machine.jackpot_limit, machine.handpay_queue
  requires stimulus.handpay.reset, host.poll_sequence, decoder.lp1B

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  note "Play the progressive game until level 1 is won as a hand pay, and note the amount."
  host expect exception handpay_is_pending within 20s
  host expect exception handpay_is_pending within 20s

  # Reset before anybody read it: no $52 yet.
  egm handpay reset
  host expect no exception handpay_was_reset within 20s

  note "Play until level 1 is won as a hand pay again, and note the amount."
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field progressive_group of lp 1B == 0
  host expect field amount of lp 1B > 0
  host expect exception handpay_was_reset within 20s
  host expect exception handpay_is_pending within 20s

  # The second reading, repeated while unacknowledged, then acknowledged by the next one.
  host send lp 1B
  host expect field amount of lp 1B > 0
  host send lp 1B
  host expect field amount of lp 1B == 0

  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "Before the run, configure the machine for a non-SAS progressive at level 1, a jackpot limit below the level's value and no partial pay, and select the progressive game."
  note "The first reading carries the first win's amount and the second the second's, each including any partial pay; compare them with the amounts noted."
  note "The document also stops the round and sends a broadcast, three readings and a broadcast: every reading carries the second win, because none of them was acknowledged by anything but a repeat. This build sends no broadcast long poll, so that sequence is a person's to read in the frames."
}

test "12.7 the hand pay reading for a progressive win" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, machine.jackpot_limit
  requires stimulus.handpay.reset, decoder.lp1B

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  note "Play the progressive game until level 1 is won as a hand pay, and note the amount."
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field progressive_group of lp 1B == 0
  host expect field amount of lp 1B > 0
  host expect field partial_pay of lp 1B == 0

  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "Before the run, configure the progressive game for a non-SAS progressive at level 1 and every other game for none, set a jackpot limit below the level's value, disable partial pay, and select the progressive game. The controller is connected as its manufacturer describes."
  note "The amount is the progressive win plus any partial pay, in cents, and must match the amount noted. The reset byte is 00 when only a standard hand pay is offered and 01 when the machine can also reset to the credit meter; the ten bytes after it are normally zero."
  note "The document expects level 00 in this reply. The machine library reports whichever level the game hands it, with group zero for a non-SAS level, so on a Quixant machine expect the level that was hit; whether 00 or the hit level is right for a non-SAS progressive is left to whoever reads this result."
}

test "12.8 a progressive and a machine jackpot paid as one hand pay" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, machine.jackpot_limit
  requires stimulus.handpay.reset, decoder.lp1B

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  note "Play until one game wins both progressive level 1 and a paytable jackpot above the limit, and note both amounts."
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field progressive_group of lp 1B == 0
  host expect field amount of lp 1B > 0

  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "Before the run, configure one game that can win a progressive and a jackpot at once - often a multi-line game - for a non-SAS progressive at level 1, every other game for none, and a jackpot limit low enough to lock up."
  note "The amount is the paytable jackpot plus the progressive plus any partial pay, in cents; the partial pay field is what the machine paid before the hand pay in its own credits, or zero."
}

test "12.9 two progressives paid as one hand pay" {
  tag @egm-test-scripts @chapter-12 @needs-a-person
  requires machine.progressives, stimulus.progressive, machine.jackpot_limit
  requires stimulus.handpay.reset, decoder.lp1B

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  note "Play until one game wins two progressives at once, and note both amounts."
  host expect exception handpay_is_pending within 20s
  host send lp 1B
  host expect field progressive_group of lp 1B == 0
  host expect field amount of lp 1B > 0

  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "Before the run, configure one game that can win two progressives at once - often a multi-line game - for a non-SAS progressive, every other game for none, and a jackpot limit low enough to lock up."
  note "The amount is the sum of both progressive wins plus any partial pay, in cents."
}
