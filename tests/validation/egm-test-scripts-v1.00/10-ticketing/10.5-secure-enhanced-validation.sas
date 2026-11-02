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
#
# Every case that prints reads its own record back with $4D before it ends. A record left unread
# makes the machine's next cash-out a hand pay, which would turn the case after this one into a
# test of something else.

test "10.5.1 a machine with no validation configured refuses to print" {
  tag @egm-test-scripts @chapter-10
  requires egm.validation == none
  requires decoder.lp3F, host.validation_sequence
  note "With no validation style configured the machine cannot number a ticket, so a cashout raises the no-validation-information exception rather than producing paper. A machine that printed an unnumbered ticket would have produced money nobody can account for."
}

test "10.5.2 the host sets the secure enhanced validation identifier" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == secure_enhanced
  requires decoder.lp4C, decoder.lp4D, stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Identifier $100001, written in decimal because a dollar sign names one byte.
  host send lp 4C with validation_id=1048577 sequence_number=1
  host expect field validation_id of lp 4C == 1048577
  host expect field sequence_number of lp 4C == 1

  egm settle
  host forget exceptions within 2s

  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # A zero identifier asks without setting: one ticket spent one number.
  host send lp 4C with validation_id=0 sequence_number=0
  host expect field validation_id of lp 4C == 1048577
  host expect field sequence_number of lp 4C == 2

  # The same identifier and starting number again does not rewind a machine that has spent from
  # them; a host that resent its configuration would otherwise make the machine reuse numbers.
  host send lp 4C with validation_id=1048577 sequence_number=1
  host expect field sequence_number of lp 4C == 2

  # A new starting number is taken, and the last one there is rolls over to zero.
  host send lp 4C with validation_id=1048577 sequence_number=16777215
  host expect field sequence_number of lp 4C == 16777215

  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  host send lp 4C with validation_id=0 sequence_number=0
  host expect field sequence_number of lp 4C == 0

  note "The document also power-cycles the machine to show the identifier and number survive, and sends the poll with a broken check sequence to show it is refused. Neither is something this build can do to the machine."
}

test "10.5.3 the cashout ticket information poll answers under this style" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == secure_enhanced
  requires decoder.lp3D, decoder.lp4C, decoder.lp4D
  requires stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1
  egm settle
  host forget exceptions within 2s

  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s

  # The document lets the machine answer $3D or ignore it in this style. This machine ignores it,
  # as section 15.5 reads, and the assertion pins that choice.
  host expect no reply to lp 3D

  # What the document cares about: asking with $3D did not clear the record, so the machine is
  # still announcing it and $4D still has it.
  host expect exception cashout_ticket_printed within 20s
  host send lp 4D data 00
  host expect field validation_type of lp 4D == 0
  host expect field validation_number of lp 4D == ticket.validation_number
  host expect field amount of lp 4D == ticket.amount_cents

  note "The second half connects a host that owns no validation and expects a zero number from it. That needs a second host the machine treats as non-validating, which is 10.5.5's subject."
}

test "10.5.4 a printed ticket raises its exception under this style" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == secure_enhanced
  requires stimulus.bill, stimulus.button.collect, printer.wallet
  requires decoder.lp4C, decoder.lp4D

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1
  egm settle
  host forget exceptions within 2s

  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s

  # Raised again until the host reads the record: one missed general poll must not lose a ticket.
  host expect exception cashout_ticket_printed within 20s

  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number
  host expect field amount of lp 4D == ticket.amount_cents

  # Read and acknowledged, so it stops.
  host expect no exception cashout_ticket_printed within 20s

  note "The document also power-cycles the machine with the record unread, and repeats the check for a hand pay receipt and a jackpot. The first is beyond this build; the other two need a hand pay, which it cannot yet reset."
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
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == secure_enhanced
  requires decoder.lp4C, decoder.lp4D, stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1
  egm settle
  host forget exceptions within 2s

  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s

  # $FF looks at the current record without moving past it.
  host send lp 4D data FF
  host expect field validation_type of lp 4D == 0
  host expect field validation_number of lp 4D == ticket.validation_number
  host expect field amount of lp 4D == ticket.amount_cents
  host expect field ticket_number of lp 4D == ticket.ticket_number
  host expect field pool_id of lp 4D == 0

  # The round's polls come between, as they do between the document's one-key polls. On this
  # machine a $4D 00 that follows another long poll straight away is answered but not counted as
  # read, so the record would still be waiting after the case ends.
  host wait 1s

  # Zero reads the same record and, once acknowledged, moves past it.
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number
  host expect field amount of lp 4D == ticket.amount_cents

  # The round's next poll is what acknowledges the read. Sent again at once, the same $4D would be
  # a retransmission, and the machine would rightly answer it with the same record.
  host wait 1s

  # Nothing left to read, so every field is zero.
  host send lp 4D data 00
  host expect field validation_type of lp 4D == 0
  host expect field validation_number of lp 4D == 0
  host expect field amount of lp 4D == 0

  note "The document goes on with two restricted tickets and a jackpot hand pay, checking the type and that index and ticket number advance. Restricted credits are the third batch's subject, and the hand pay needs a reset this build cannot do."
}

test "10.5.9 the validation type field says what kind of transaction it was" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == secure_enhanced
  requires stimulus.handpay, decoder.lp4D
  note "Produce a cashout ticket, a hand pay receipt and a hand pay with no receipt, and check each buffer record names its own type. A machine that recorded them all alike would satisfy every other case in this section."
}

test "10.5.10 the machine generates a sixteen-digit validation number" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == secure_enhanced
  requires decoder.lp4C, decoder.lp4D, stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Through 99 first, so the start at 1 is a change the machine has to take whatever it held.
  host send lp 4C with validation_id=1048577 sequence_number=99
  host send lp 4C with validation_id=1048577 sequence_number=1
  host expect field sequence_number of lp 4C == 1
  egm settle
  host forget exceptions within 2s

  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s

  # Identifier $100001 at the first number after 1 makes this number, by the algorithm alone.
  host send lp 4D data 00
  host expect field validation_number of lp 4D == 0230633823748784
  host expect field validation_system_id of lp 4D == 0
  host expect field validation_number of lp 4D == ticket.validation_number
}

test "10.5.11 ticket numbers run in sequence" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == secure_enhanced
  requires decoder.lp4C, decoder.lp4D, stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1
  egm settle
  host forget exceptions within 2s

  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s
  host send lp 4D data 00
  host expect field ticket_number of lp 4D == ticket.ticket_number

  # A new validation sequence does not restart the ticket number: the two are different counters.
  host send lp 4C with validation_id=1 sequence_number=99

  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s
  host send lp 4D data 00
  host expect field ticket_number of lp 4D == ticket.ticket_number
  host expect field ticket_number of lp 4D != 1

  note "That each ticket number is one more than the last is read from the two records in the run record: a script cannot yet compare one ticket with the ticket before it."
}

test "10.5.12 the buffer is circular and the oldest record is the one lost" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == secure_enhanced
  requires stimulus.bill, stimulus.button.collect, machine.buffer_depth
  note "Print more tickets than the buffer holds and read it back: the newest are all present and the oldest have gone, in that order. Needs the machine's own buffer depth, which no poll reports."
}

test "10.5.13 a ticket printed while the link is down is still on the record" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == secure_enhanced
  requires decoder.lp4C, decoder.lp4D, stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1
  egm settle
  host forget exceptions within 2s

  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s

  # The record is left unread and the line goes quiet: nothing is sent until the case sends it,
  # so the machine loses its host with a ticket nobody has read.
  host poll sequence
  host wait 10s

  # The host comes back, and the ticket is still announced and still on the record.
  host poll every 200ms
  host expect exception cashout_ticket_printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number
  host expect field amount of lp 4D == ticket.amount_cents

  note "The document also cashes out again with records unread until the machine falls back to receipts. On this machine the next cash-out after an unread record is a hand pay, which this build cannot reset, so that half is not run. Measured on 2026-11-02: with the line quiet, this machine printed nothing for a cash-out at all."
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
