# Chapter 4, sections 4.21 to 4.25 - hand pay, and the two ways a machine can keep one.
#
# A machine either keeps a queue of hand pays or keeps the single legacy one, and the difference
# shows on the wire: a queue reissues $51 every fifteen seconds until the host reads the hand pay
# information and something acknowledges the reading, while the legacy form sends $51 once and
# sends $52 when the attendant resets it whether or not the host ever asked. Both are conforming
# and the cases are written to tell them apart rather than to prefer one.
#
# Nothing here can create a hand pay. It needs a jackpot win over the machine's limit, or credits
# over its hopper limit and a cash out, and this build has no stimulus for either.

test "4.21.1 a jackpot win raises hand pay pending" {
  tag @egm-test-scripts @chapter-4 @needs-stimulus
  requires stimulus.handpay.jackpot, stimulus.handpay.reset, stimulus.settle
  requires decoder.lp1B

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm settle
  host forget exceptions within 2s

  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host send lp 1B
  host expect field amount of lp 1B != 0
  host expect field level of lp 1B == 0

  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "A machine that decides its own outcomes cannot be asked for a jackpot, so this case runs only where the outcome is taken from outside."
}

test "4.21.2 a cash out over the hopper limit raises hand pay pending" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.handpay, stimulus.hopper
  requires decoder.lp1B

  note "Insert money until the credit meter is above the machine's hopper payout limit, then bring the link up and press cash out: one hand pay pending $51 on a general poll."
  note "Read the hand pay information and reset the hand pay when the case is done."
}

# The queue form, in full: reissued every fifteen seconds, surviving a power cycle, and closed by
# $52 only after the host has read it.
test "4.22 a queued hand pay is reissued every fifteen seconds" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires stimulus.handpay, stimulus.hopper, machine.power_cycle
  requires decoder.lp1B

  note "Configure the machine for a hand pay queue and a small hopper payout limit. Put credits above that limit on it, bring the link up and press cash out: $51 on the next general poll, and again every fifteen seconds for two minutes."
  note "Power cycle the machine. Once it answers polls again, $51 on the next general poll and every fifteen seconds after - the hand pay is in non-volatile memory."
  note "Read the hand pay information and reset the hand pay: one $52 on the next general poll, and no more $51 for the next two minutes."
}

test "4.23.1 one hand pay, read once" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires stimulus.handpay.jackpot, stimulus.handpay.reset, machine.power_cycle, stimulus.settle
  requires decoder.lp1B

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # A jackpot over the machine's limit: $51, and again fifteen seconds later.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  host expect exception handpay_is_pending within 20s

  # Reset at the machine before the host has read it: no $52, and $51 goes on.
  egm handpay reset
  host expect no exception handpay_was_reset within 5s
  host forget exceptions within 2s
  host expect exception handpay_is_pending within 20s

  # The hand pay is in non-volatile memory, so a power cycle does not end it.
  egm power cycle
  # The person confirms once the machine has booted, so only the link is awaited here.
  host expect link up within 120s
  host forget exceptions within 2s
  host expect exception handpay_is_pending within 20s

  # Read: the machine answers, and one $52 follows.
  host send lp 1B
  host expect field amount of lp 1B != 0
  host expect exception handpay_was_reset within 15s

  note "Before the run, configure the machine for a hand pay queue with a jackpot win limit below the amount the prompt asks for. The amount is in cents."
}

# Two hand pays, which is where the queue form earns its name: the second $51 must appear behind
# the first one's $52 rather than being merged with it.
test "4.23.2 two hand pays, in turn" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires stimulus.handpay.jackpot, stimulus.handpay.reset, machine.power_cycle, stimulus.settle
  requires decoder.lp1B

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # The first jackpot, reset at the machine before it is read: no $52, and $51 goes on.
  egm handpay jackpot 150000
  host expect exception handpay_is_pending within 10s
  egm handpay reset
  host expect no exception handpay_was_reset within 5s
  host forget exceptions within 2s
  host expect exception handpay_is_pending within 20s

  # Across a power cycle, still every fifteen seconds.
  egm power cycle
  # The person confirms once the machine has booted, so only the link is awaited here.
  host expect link up within 120s
  host forget exceptions within 2s
  host expect exception handpay_is_pending within 20s

  # The second jackpot. Reading the first brings its $52, then the second's $51.
  egm handpay jackpot 150000
  host send lp 1B
  host expect field amount of lp 1B != 0
  host expect exception handpay_was_reset within 15s
  host expect exception handpay_is_pending within 20s

  # Reading the second stops its $51; the attendant's reset brings one $52.
  host send lp 1B
  host expect field amount of lp 1B != 0
  host wait 1s
  host forget exceptions within 2s
  host expect no exception handpay_is_pending within 20s
  egm handpay reset
  host expect exception handpay_was_reset within 15s

  note "Before the run, configure the machine for a hand pay queue with a jackpot win limit below the amount the prompt asks for. The amount is in cents."
}

# The legacy form, which is the opposite shape: $51 once, $52 on the reset, and the host's reading
# has nothing to do with either.
test "4.24 a legacy hand pay is sent once and reset by the attendant" {
  tag @egm-test-scripts @chapter-4 @needs-a-person @destructive
  requires stimulus.handpay, stimulus.hopper, machine.power_cycle
  requires decoder.lp1B

  note "Configure the machine for legacy hand pay if it offers it, with small hopper and jackpot limits. Put credits above the hopper limit on it and press cash out: one $51, and no more for a minute. Power cycle: still no more."
  note "Reset the hand pay at the machine: one $52 on a general poll."
  note "Then play until a jackpot win occurs: one $51, no repeats for a minute, and one $52 when it is reset."
}

# A player who changes their mind. $55 follows $51 and is not sent when there was no cash out
# request to cancel.
test "4.25 a cancelled cash out" {
  tag @egm-test-scripts @chapter-4 @needs-a-person
  requires stimulus.handpay, stimulus.hopper

  note "Only for machines where the player can cancel a cash out. With a small hopper payout limit and credits above it, press cash out: one $51. Cancel the request: one $55."
  note "Press cash out again: one $51. Reset the hand pay at the machine: one $52. Try to cancel now: no $55 - there is nothing left to cancel."
}
