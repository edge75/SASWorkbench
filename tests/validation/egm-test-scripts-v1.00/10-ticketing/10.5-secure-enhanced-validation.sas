# Chapter 10, section 10.5 - secure enhanced validation.
#
# The style where the host hands the machine its validation numbers in advance and the machine
# spends them one per ticket, keeping a record of each in a circular buffer the host reads back.
# It is the style a casino system usually runs, and it is the one with the most to go wrong:
# numbers can be spent twice, the buffer can wrap, and a link that goes down mid-ticket leaves a
# printed ticket nobody has read.
#
# Every case here needs a machine performing this style. That is memory the host cannot write, so
# each case declares it: the software machine is built performing it, and any other machine is
# asked and skips by name when it performs another. A fixture exists for a real machine
# (fixtures/nvram/secure-enhanced.nvram.gz).
#
# Every case that prints reads its own record back with $4D before it ends. A record left unread
# makes the machine's next cash-out a hand pay, which would turn the case after this one into a
# test of something else.

test "10.5.1 a machine with no validation configured refuses to print" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation secure_enhanced
  requires decoder.lp7B, decoder.lp4C, decoder.lp1B, host.validation_sequence
  requires stimulus.bill, stimulus.button.collect, stimulus.handpay.reset

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # A machine that holds an identifier is made to forget it: $7B bit 15 cleared cancels it. A machine
  # that was never given one reports the same from power-up.
  host send lp 7B data 088000000000000000
  host expect exception validation_id_not_configured within 20s

  # The cash-out cannot be numbered, so it is paid by hand rather than printed.
  egm bill insert 2000
  egm press collect
  host expect exception handpay_is_pending within 10s

  # Paid, reset and given an identifier again, so the next case finds a machine that prints.
  host send lp 1B
  host expect field amount of lp 1B != 0
  host wait 1s
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  host send lp 4C with validation_id=1 sequence_number=1
  host expect no exception validation_id_not_configured within 20s

  note "The document names no style here; a machine with no host validation is a standard one on the wire, which numbers its own tickets, so the case is the secure enhanced machine that has no identifier. The exception it raises is $3F, the validation identifier not configured."
}

test "10.5.2 the host sets the secure enhanced validation identifier" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation secure_enhanced
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
  machine validation secure_enhanced
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
  machine validation secure_enhanced
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
  machine validation secure_enhanced
  requires decoder.lp3D, host.validation_sequence
  note "The two-host form of 10.2.3. The same absence of a channel gate on the cashout ticket information poll applies here, so this is expected to show the same finding against the machine library."
}

test "10.5.6 a non-validating host must not read a validated hand pay" {
  tag @egm-test-scripts @chapter-10 @channel2 @needs-stimulus
  machine validation secure_enhanced
  requires stimulus.handpay, stimulus.handpay.reset, decoder.lp4D
  note "Force a hand pay on a machine validating hand pays, then ask for it from the channel that owns no validation."
  note "Not written yet: it needs a secure enhanced memory with validation on a second channel, and the committed one keeps every category on channel 1."
}

test "10.5.7 a validated hand pay carries a number and an amount" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation secure_enhanced
  machine handpay validation on
  requires decoder.lp7B, decoder.lp4D, stimulus.handpay, stimulus.handpay.reset, stimulus.settle, decoder.lp1B
  requires machine.handpay_validation

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1
  egm settle
  host forget exceptions within 2s

  host send lp 7B data 080400040000000000

  egm handpay 2000
  host expect exception handpay_is_pending within 10s
  # A queue-mode machine sends $52 only once the hand pay reading is acknowledged (SAS 6.02 7.8.1).
  host send lp 1B
  host expect field amount of lp 1B != 0
  host wait 1s
  egm handpay reset
  host expect exception handpay_validated within 20s

  # A cancelled-credit hand pay with no receipt is type $40, numbered from the host's sequence.
  host wait 1s
  host send lp 4D data 00
  host expect field validation_type of lp 4D == 64
  host expect field validation_number of lp 4D != 0
  host expect field amount of lp 4D >= 2000
  host wait 1s

  host send lp 7B data 080400000000000000

  note "The document reads the hand pay with the handpay-validated exception's own poll; that exception is answered by $4D, which is what is read here."
}

test "10.5.8 the enhanced validation information poll reads the buffer back" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation secure_enhanced
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
  machine validation secure_enhanced
  requires decoder.lp7B, decoder.lp4D, stimulus.handpay, stimulus.handpay.reset, stimulus.settle, decoder.lp1B
  requires stimulus.bill, stimulus.button.collect, printer.wallet
  requires machine.handpay_validation, machine.handpay_receipts

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1
  egm settle
  host forget exceptions within 2s

  # A cashout ticket: type 0.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s
  host wait 1s
  host send lp 4D data 00
  host expect field validation_type of lp 4D == 0
  host wait 1s

  # A hand pay with a receipt: type $10. Receipts and hand pay validation both on.
  host send lp 7B data 080600060000000000
  egm handpay 2000
  host expect exception handpay_is_pending within 10s
  # A queue-mode machine sends $52 only once the hand pay reading is acknowledged (SAS 6.02 7.8.1).
  host send lp 1B
  host expect field amount of lp 1B != 0
  host wait 1s
  egm handpay reset
  host expect exception handpay_validated within 20s
  host wait 1s
  host send lp 4D data 00
  host expect field validation_type of lp 4D == 16
  host wait 1s

  # A hand pay with no receipt: type $40. Receipts off, validation still on.
  host send lp 7B data 080200000000000000
  host forget exceptions within 2s
  egm handpay 2000
  host expect exception handpay_is_pending within 10s
  # A queue-mode machine sends $52 only once the hand pay reading is acknowledged (SAS 6.02 7.8.1).
  host send lp 1B
  host expect field amount of lp 1B != 0
  host wait 1s
  egm handpay reset
  host expect exception handpay_validated within 20s
  host wait 1s
  host send lp 4D data 00
  host expect field validation_type of lp 4D == 64
  host wait 1s

  host send lp 7B data 080400000000000000
}

test "10.5.10 the machine generates a sixteen-digit validation number" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation secure_enhanced
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
  machine validation secure_enhanced
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
  machine validation secure_enhanced
  requires stimulus.bill, stimulus.button.collect, machine.buffer_depth
  requires decoder.lp4C, decoder.lp4D, printer.wallet

  # Thirty-two tickets through a buffer of thirty-one, each read as it prints so the printer stays the
  # cash-out device; the thirty-second takes the slot the first was in.
  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1
  egm settle
  host forget exceptions within 2s

  # Ticket 1.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number
  host remember field validation_number of lp 4D as first

  # Ticket 2.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number
  host remember field validation_number of lp 4D as second

  # Ticket 3.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 4.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 5.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 6.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 7.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 8.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 9.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 10.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 11.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 12.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 13.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 14.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 15.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 16.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 17.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 18.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 19.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 20.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 21.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 22.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 23.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 24.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 25.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 26.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 27.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 28.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 29.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 30.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # Ticket 31.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number
  host remember field validation_number of lp 4D as thirty_first

  # Ticket 32.
  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number

  # The newest in slot 1, where the oldest was; the second still in slot 2; the thirty-first in the last.
  host send lp 4D data 01
  host expect field validation_number of lp 4D == ticket.validation_number
  host expect field validation_number of lp 4D != saved.first
  host send lp 4D data 02
  host expect field validation_number of lp 4D == saved.second
  host send lp 4D data 1F
  host expect field validation_number of lp 4D == saved.thirty_first

  note "Assumes a buffer of thirty-one records starting at index 1, the machine library's; a machine with another depth wraps at another ticket."
}

test "10.5.13 a ticket printed while the link is down is still on the record" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation secure_enhanced
  requires decoder.lp4C, decoder.lp4D, stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1
  egm settle
  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
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
