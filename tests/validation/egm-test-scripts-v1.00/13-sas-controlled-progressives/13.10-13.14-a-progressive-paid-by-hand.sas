# Chapter 13, sections 13.10 to 13.14 - a SAS progressive the machine cannot pay itself.
#
# A hit on a host-controlled level raises $56, and a hit the attendant must pay also raises $51.
# The document prefers $56 first but accepts either order. Both are reissued every fifteen seconds
# until the host has dealt with them: $51 until the hand pay information ($1B) has been read, $56
# until every queued win has been read with $85 (or $87). The hand pay information reports the
# group, the highest level that contributed, and an amount in cents that includes any partial pay.
#
# Winning a progressive is not something this build can make a machine do (stimulus.progressive),
# and a machine on a SAS progressive also expects the host to keep broadcasting every level while
# the case runs, which this build cannot do while it waits for an exception
# (host.progressive_broadcast). Where the win happens is marked in each case; the amount assumed is
# the $5.00 the case broadcasts to level 1 before it starts.

test "13.10 the hit and the hand pay are reported and reissued" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @destructive @needs-a-person
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires machine.jackpot_limit, machine.power_cycle, stimulus.door.main, stimulus.handpay.reset
  requires host.poll_sequence, decoder.lp80, decoder.lp1B, decoder.lp85

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 80 with progressive_group=1 level=1 amount=500
  host forget exceptions within 2s

  # Nothing is reported while no general poll goes out.
  host poll sequence
  host send sync
  egm door main open
  egm door main close
  # A person wins level 1 here, as a hand pay.

  # Polling resumes: $56 and $51 first, in either order, then the doors.
  host poll every 200ms
  host expect exception sas_progressive_level_hit within 10s
  host expect exception handpay_is_pending within 10s
  host expect exception slot_door_opened within 10s
  host expect exception slot_door_closed within 10s

  # Both are reissued every fifteen seconds, and still after a power cycle.
  host forget exceptions within 2s
  host expect exception handpay_is_pending within 20s
  host expect exception sas_progressive_level_hit within 20s
  egm power cycle
  host expect link up within 60s
  host forget exceptions within 2s
  host expect exception handpay_is_pending within 20s
  host expect exception sas_progressive_level_hit within 20s

  # Reading the hand pay ends $51; $56 carries on.
  host send lp 1B
  host expect field progressive_group of lp 1B == 1
  host expect field level of lp 1B == 1
  host forget exceptions within 2s
  host expect exception sas_progressive_level_hit within 20s
  host expect no exception handpay_is_pending within 30s

  # Fifteen seconds of silence, then the machine's own general poll three times.
  host poll sequence
  host wait 15s
  host send sync
  host send gp draws sas_progressive_level_hit
  host send gp draws sas_progressive_level_hit
  host send gp

  # A long poll between two general polls acknowledges the first.
  host send sync
  host send gp draws sas_progressive_level_hit
  host send lp 1B
  host send gp

  host poll every 200ms
  host forget exceptions within 2s
  host expect exception sas_progressive_level_hit within 20s
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  # Reading the win ends $56.
  host send lp 85
  host expect field progressive_group of lp 85 == 1
  host expect field level of lp 85 == 1
  host expect field amount of lp 85 == 500
  host forget exceptions within 2s
  host expect no exception sas_progressive_level_hit within 20s

  note "Before the run, enrol the machine in group 1 with level 1, set the jackpot limit low enough that the level is a hand pay, turn partial pay off, select the progressive game, and stop the host answering $51 with $1B on its own."
  note "The third general poll of the first sequence may be answered or ignored. The second general poll of the last sequence draws something other than $56, because the hand pay read in between acknowledged it; this build can only say what a poll drew, not that it differs."
}

test "13.11 two progressive hand pays are queued in order" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus @needs-a-person
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires machine.handpay_queue, machine.jackpot_limit, stimulus.handpay.reset
  requires host.poll_sequence, decoder.lp80, decoder.lp1B, decoder.lp85

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 80 with progressive_group=1 level=1 amount=500
  host forget exceptions within 2s

  # A person wins level 1 here, as a hand pay.
  host expect exception sas_progressive_level_hit within 10s
  host expect exception handpay_is_pending within 10s
  host forget exceptions within 2s
  host expect exception handpay_is_pending within 20s
  host expect exception sas_progressive_level_hit within 20s

  # Keyed off before the host has read it: no reset is reported yet.
  egm handpay reset
  host expect no exception handpay_was_reset within 5s
  host send lp 80 with progressive_group=1 level=1 amount=700
  # A person wins level 1 again here, also a hand pay.

  # The first read reports the first win, and its reset follows.
  host send lp 1B
  host expect field progressive_group of lp 1B == 1
  host expect field level of lp 1B == 1
  host expect field amount of lp 1B == 500
  host expect exception handpay_was_reset within 15s
  host forget exceptions within 2s
  host expect exception handpay_is_pending within 20s

  # The second is read, and keyed off, before $52 can follow.
  host send lp 1B
  host expect field amount of lp 1B == 700
  host expect no exception handpay_was_reset within 5s
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  # Two wins in the progressive queue, read one at a time.
  host poll sequence
  host send sync
  host send lp 85
  host expect field amount of lp 85 == 500
  host send lp 85
  host poll every 200ms
  host forget exceptions within 2s
  host expect exception sas_progressive_level_hit within 20s
  host send lp 85
  host expect field amount of lp 85 == 700
  host forget exceptions within 2s
  host expect no exception sas_progressive_level_hit within 20s
  host send lp 85
  host expect field progressive_group of lp 85 == 0
  host expect field level of lp 85 == 0
  host expect field amount of lp 85 == 0

  note "Before the run, configure a hand pay queue, enrol the machine in group 1 with level 1, set the jackpot limit low, turn partial pay off, select the progressive game, and stop the host answering $51 with $1B on its own."
  note "The document also re-reads a hand pay with the line held - a long poll repeated back to back is the same request asked again, so the same amount comes back - and, once both hand pays are read, a third $1B answers zeros and $51 stops. Two back-to-back $85 are likewise the same request twice: the first two answers are the same win, and a third may be answered or ignored."
}

test "13.12 the hand pay information of a progressive win" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires machine.jackpot_limit, stimulus.handpay.reset
  requires decoder.lp80, decoder.lp1B, decoder.lp85

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 80 with progressive_group=1 level=1 amount=500
  host forget exceptions within 2s

  # A person wins level 1 here, as a hand pay.
  host expect exception sas_progressive_level_hit within 10s
  host expect exception handpay_is_pending within 10s

  # Group, the level, and the amount in cents.
  host send lp 1B
  host expect field progressive_group of lp 1B == 1
  host expect field level of lp 1B == 1
  host expect field amount of lp 1B == 500
  host expect field partial_pay of lp 1B == 0
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  host send lp 85
  host expect field progressive_group of lp 85 == 1
  host expect field level of lp 85 == 1

  note "Before the run, enrol the machine in group 1 with level 1, set the jackpot limit low, and select the progressive game. Partial pay is off here; a machine that offers it is run again with it on, and then the amount is the win plus what the machine paid first, while the partial pay field is that payment in credits."
  note "The reset byte is 00 for a standard hand pay only and 01 when the machine can also reset to its credit meter; read it in the frames. The bytes after it are unused."
}

test "13.13 a progressive and a machine jackpot in one hand pay" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires machine.jackpot_limit, stimulus.handpay.reset
  requires decoder.lp80, decoder.lp1B, decoder.lp85

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 80 with progressive_group=1 level=1 amount=500
  host forget exceptions within 2s

  # A person wins level 1 and a machine jackpot in the same game here.
  host expect exception sas_progressive_level_hit within 10s
  host expect exception handpay_is_pending within 10s

  # The progressive's group and level; the amount includes the jackpot.
  host send lp 1B
  host expect field progressive_group of lp 1B == 1
  host expect field level of lp 1B == 1
  host expect field amount of lp 1B > 500
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  host send lp 85
  host expect field level of lp 85 == 1
  host expect field amount of lp 85 == 500

  note "Before the run, enrol the machine in group 1 with level 1, set the jackpot limit low, and select the progressive game. The amount is exactly the progressive plus the machine's own jackpot plus any partial pay, in cents; the jackpot's value is the paytable's, so compare it there."
}

test "13.14 two progressives won in one hand pay" {
  tag @egm-test-scripts @chapter-13 @needs-stimulus
  requires machine.sas_progressives, stimulus.progressive, host.progressive_broadcast
  requires machine.jackpot_limit, stimulus.handpay.reset
  requires decoder.lp86, decoder.lp1B, decoder.lp85

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 86 data 0D01010000000500020000000300
  host forget exceptions within 2s

  # A person wins levels 1 and 2 in the same game here.
  host expect exception sas_progressive_level_hit within 10s
  host expect exception handpay_is_pending within 10s

  # One of the two levels, and both wins together.
  host send lp 1B
  host expect field progressive_group of lp 1B == 1
  host expect field level of lp 1B >= 1
  host expect field level of lp 1B <= 2
  host expect field amount of lp 1B == 800
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  host send lp 85
  host expect field progressive_group of lp 85 == 1

  note "Before the run, enrol the machine in group 1 with the levels the game can win together, set the jackpot limit low, turn partial pay off, and select the progressive game. The document configures level 1 only and asks for two progressive wins at once; it is run here with levels 1 and 2. The level byte names the highest of the levels that contributed, and which of 1 and 2 counts as highest is the machine's documentation to say, so this build only asserts it is one of them."
}
