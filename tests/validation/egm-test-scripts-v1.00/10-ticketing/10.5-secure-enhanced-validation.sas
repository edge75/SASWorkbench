# Chapter 10, section 10.5 - secure enhanced validation.
#
# The style where the host hands the machine its validation numbers in advance and the machine
# spends them one per ticket, keeping a record of each in a circular buffer the host reads back.
# It is the style a casino system usually runs, and it is the one with the most to go wrong:
# numbers can be spent twice, the buffer can wrap, and a link that goes down mid-ticket leaves a
# printed ticket nobody has read.
#
# Every case here needs a machine performing this style. That is memory the host cannot write, so
# a machine in another style skips by name rather than being driven into a state it is not in.
# A fixture exists for a real machine (fixtures/nvram/secure-enhanced.nvram.gz); this build's
# software machine comes up performing standard validation and a script cannot change it, which
# is why the whole section skips against the loopback peer.

test "10.5.1 a machine with no validation configured refuses to print" {
  tag @egm-test-scripts @chapter-10
  requires egm.validation == none
  requires decoder.lp3F, host.validation_sequence
  note "With no validation style configured the machine cannot number a ticket, so a cashout raises the no-validation-information exception rather than producing paper. A machine that printed an unnumbered ticket would have produced money nobody can account for."
}

test "10.5.2 the host sets the secure enhanced validation identifier" {
  tag @egm-test-scripts @chapter-10
  requires egm.validation == secure_enhanced
  requires decoder.lp4C, host.validation_sequence
  note "Send the set-validation-identifier poll carrying the system identifier and the first sequence number, and check it is acknowledged. Everything else in this section depends on it having been accepted."
}

test "10.5.3 the cashout ticket information poll answers under this style" {
  tag @egm-test-scripts @chapter-10
  requires egm.validation == secure_enhanced
  requires decoder.lp3D, host.validation_sequence
  note "The same poll as 10.2.5 against a machine in this style: it carries the number the machine spent from the host's sequence rather than one it invented."
}

test "10.5.4 a printed ticket raises its exception under this style" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == secure_enhanced
  requires stimulus.bill, stimulus.button.collect, printer.wallet
  note "Money in, cashout pressed, ticket printed, exception raised - the same shape as 10.2.2, with the number coming from the host's sequence."
}

test "10.5.5 a non-validating host must not read the ticket under this style" {
  tag @egm-test-scripts @chapter-10 @channel2
  requires egm.validation == secure_enhanced
  requires decoder.lp3D, host.validation_sequence
  note "The two-host form of 10.2.3. The same absence of a channel gate on the cashout ticket information poll applies here, so this is expected to show the same finding against the machine library."
}

test "10.5.6 a non-validating host must not read a validated hand pay" {
  tag @egm-test-scripts @chapter-10 @channel2 @needs-stimulus
  requires egm.validation == secure_enhanced
  requires stimulus.handpay, decoder.lp3E
  note "Force a hand pay on a machine validating hand pays, then ask for it from the channel that owns no validation."
}

test "10.5.7 a validated hand pay carries a number and an amount" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == secure_enhanced
  requires stimulus.handpay, decoder.lp3E
  note "Force a hand pay and read the handpay-validated poll: it carries the number spent from the host's sequence and the amount paid."
}

test "10.5.8 the enhanced validation information poll reads the buffer back" {
  tag @egm-test-scripts @chapter-10
  requires egm.validation == secure_enhanced
  requires decoder.lp4D, host.validation_sequence
  note "Read the record of a ticket back out of the machine's circular buffer by its index, and check every field against the ticket that was printed."
}

test "10.5.9 the validation type field says what kind of transaction it was" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == secure_enhanced
  requires stimulus.handpay, decoder.lp4D
  note "Produce a cashout ticket, a hand pay receipt and a hand pay with no receipt, and check each buffer record names its own type. A machine that recorded them all alike would satisfy every other case in this section."
}

test "10.5.10 the machine generates a sixteen-digit validation number" {
  tag @egm-test-scripts @chapter-10
  requires egm.validation == secure_enhanced
  requires decoder.lp4D, host.validation_sequence
  note "Print several tickets and check the number on each is sixteen digits, is derived from the identifier and sequence the host set, and never repeats."
}

test "10.5.11 ticket numbers run in sequence" {
  tag @egm-test-scripts @chapter-10
  requires egm.validation == secure_enhanced
  requires decoder.lp4D, host.validation_sequence
  note "Print several tickets in a row and check the sequence number advances by one each time, with no gap and no repeat."
}

test "10.5.12 the buffer is circular and the oldest record is the one lost" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == secure_enhanced
  requires stimulus.bill, stimulus.button.collect, machine.buffer_depth
  note "Print more tickets than the buffer holds and read it back: the newest are all present and the oldest have gone, in that order. Needs the machine's own buffer depth, which no poll reports."
}

test "10.5.13 a ticket printed while the link is down is still on the record" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires egm.validation == secure_enhanced
  requires machine.link_interruption
  note "Take the link down, print a ticket, bring the link back and read the buffer: the ticket is there. A machine that only recorded what it managed to report would lose money whenever a cable was pulled."
}

test "10.5.14 hand pay cashout receipts are counted in cents and quantity" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires meter.handpay_cashout_receipt_cents, stimulus.handpay
  note "Meters 008C and 008D, moved by a hand pay that printed a receipt."
}

test "10.5.15 hand pay single wins with a receipt are counted in cents and quantity" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires meter.handpay_single_win_receipt_cents, stimulus.handpay
  note "Meters 008E and 008F, moved by a single-win hand pay that printed a receipt."
}

test "10.5.16 hand pay cashouts with no receipt are counted in cents and quantity" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires meter.handpay_cashout_no_receipt_cents, stimulus.handpay
  note "Meters 0090 and 0091, the same pair for a hand pay that printed nothing."
}

test "10.5.17 hand pay single wins with no receipt are counted in cents and quantity" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires meter.handpay_single_win_no_receipt_cents, stimulus.handpay
  note "Meters 0092 and 0093, completing the four-way split of hand pays by kind and by whether paper came out."
}
