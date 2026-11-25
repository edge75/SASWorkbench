# Chapter 10, section 10.3 - the extended validation status poll, and the eight things it turns on.
#
# Long poll $7B is how a host tells a machine what its printer is for and what kinds of ticket it
# may produce. Each bit is a separate case in this document because each one changes what the
# machine will do afterwards, and a machine that accepted the bit and ignored it would look
# identical on the wire at the moment it was set.
#
# All of them share one shape: set the bit, read the status back, and then do the thing the bit
# governs and see whether it happened. This build can do the first half against the software
# machine and the second half needs the peripheral the bit is about, which is why most of these
# carry the setting as an assertion and the consequence as a note.

test "10.3.1 the machine keeps up with a forty-millisecond poll" {
  tag @egm-test-scripts @chapter-10
  # The enhanced styles leave $3D unanswered, so the rate could not be told from the style.
  requires egm.validation == standard
  requires decoder.lp3D

  host connect loopback address 01
  host poll every 40ms
  host expect link up within 30s

  # Nothing about the answer changes with the rate; what is measured is that it still arrives.
  host send lp 3D
  host send lp 3D
  host send lp 3D
}

test "10.3.2 the extended validation status poll is answered" {
  tag @egm-test-scripts @chapter-10
  requires decoder.lp7B

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Read-only form: no bits asserted and no mask, so the machine reports what it is without being
  # changed. A case that set bits here could not then say whether the reply describes the machine
  # or describes what it was just told.
  host send lp 7B data 080000000000000000
}

test "10.3.3 the printer is declared a cashout device" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation secure_enhanced
  requires decoder.lp7B, decoder.lp4C, decoder.lp4D
  requires stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1
  egm settle
  host forget exceptions within 2s

  # The status is compared whole, so it carries the other bits the machine holds: $8000 because a
  # host set its validation identifier, $0020 for redemption, and $0001, the bit this case is about.
  host send lp 7B data 080100010000000000
  host expect field current_validation_status of lp 7B == 32801

  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s
  host send lp 4D data 00
  host expect field validation_number of lp 4D == ticket.validation_number
  host expect field amount of lp 4D != 0

  # Cleared, and only that bit moves.
  host send lp 7B data 080100000000000000
  host expect field current_validation_status of lp 7B == 32800

  # Given back, so the next case finds a machine that prints.
  host send lp 7B data 080100010000000000
  host expect field current_validation_status of lp 7B == 32801

  note "The document then cashes out with the bit clear and expects a hopper payout or an attendant hand pay. This machine has no hopper, and a hand pay is one this build cannot yet reset, so that half is not run."
}

test "10.3.4 the printer is declared a handpay receipt device" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  machine validation secure_enhanced
  requires decoder.lp7B, stimulus.handpay, stimulus.handpay.reset, stimulus.settle, printer.wallet, decoder.lp1B
  requires machine.handpay_receipts

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1
  egm settle
  host forget exceptions within 2s

  # Bit 1, "use printer as handpay receipt device", and only that bit moves.
  host send lp 7B data 080200020000000000
  host expect field current_validation_status of lp 7B == 32803

  # A hand pay, paid by the attendant: the printer prints its receipt.
  egm handpay 2000
  host expect exception handpay_is_pending within 10s
  # A queue-mode machine sends $52 only once the hand pay reading is acknowledged (SAS 6.02 7.8.1).
  host send lp 1B
  host expect field amount of lp 1B != 0
  host wait 1s
  egm handpay reset
  host expect exception handpay_was_reset within 15s
  egm expect ticket printed within 20s

  # Given back, so the next case finds the machine as it was.
  host send lp 7B data 080200000000000000
  host expect field current_validation_status of lp 7B == 32801

  note "Run on the secure enhanced configuration, where the status word without this bit reads 32801; the values asserted assume it."
  note "A machine keeps only the long poll 7B bits it declares it supports. The demo declares neither handpay receipts nor handpay validation, so on 2026-11-03 bits 1 and 2 stayed clear and this case needs a machine that offers them."
}

test "10.3.5 hand pays and their receipts are validated" {
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

  # Bit 2, "validate handpays and handpay receipts".
  host send lp 7B data 080400040000000000
  host expect field current_validation_status of lp 7B == 32805

  egm handpay 2000
  host expect exception handpay_is_pending within 10s
  # A queue-mode machine sends $52 only once the hand pay reading is acknowledged (SAS 6.02 7.8.1).
  host send lp 1B
  host expect field amount of lp 1B != 0
  host wait 1s
  egm handpay reset
  host expect exception handpay_validated within 20s

  # The record carries a number and an amount rather than zeros. Read once, so it is not left
  # unread for the next case's cash-out.
  host wait 1s
  host send lp 4D data 00
  host expect field validation_number of lp 4D != 0
  host expect field amount of lp 4D >= 2000
  host wait 1s

  host send lp 7B data 080400000000000000
  host expect field current_validation_status of lp 7B == 32801
}

test "10.3.6 restricted tickets may be printed" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires decoder.lp7B, machine.restricted_credits
  # The platform's restricted ticket option is off on the software machine, as it is by default (QSP SasNetwork.cpp:617).
  requires machine.restricted_tickets
  note "Set the restricted-ticket bit, read it back, then cash out restricted credits and confirm a restricted ticket is printed. Restricted credits have to be put on the machine first, which needs a transfer this build cannot make."
}

test "10.3.7 tickets may be printed for foreign restricted amounts" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires decoder.lp7B, machine.restricted_credits
  # The platform's restricted ticket option is off on the software machine, as it is by default (QSP SasNetwork.cpp:617).
  requires machine.restricted_tickets
  note "Set the foreign-restricted bit, read it back, and confirm the machine prints for a restricted amount it did not itself issue."
}

test "10.3.8 ticket redemption is turned on by its own bit" {
  tag @egm-test-scripts @chapter-10
  machine validation secure_enhanced
  requires decoder.lp7B, decoder.lp4C

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host send lp 4C with validation_id=1 sequence_number=1

  # $8000 because a host set the validation identifier, $0001 for the printer, and $0020, the bit
  # this case is about.
  host send lp 7B data 082000200000000000
  host expect field current_validation_status of lp 7B == 32801

  host send lp 7B data 082000000000000000
  host expect field current_validation_status of lp 7B == 32769

  # Given back, so the next case finds a machine that takes tickets.
  host send lp 7B data 082000200000000000
  host expect field current_validation_status of lp 7B == 32801

  note "Whether the bit changes what the machine does with a ticket is 10.7.1's subject."
}

test "10.3.9 secure enhanced validation is configured through the status poll" {
  tag @egm-test-scripts @chapter-10
  machine validation secure_enhanced
  requires decoder.lp7B



  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Read-only form again: the machine is already performing this style, and the poll reports what
  # it is. Which style a machine performs is set at the machine and not over the link, so a case
  # that tried to set it here would be asserting something the protocol cannot do.
  host send lp 7B data 080000000000000000
}

test "10.3.10 the status poll addressed to everybody is not answered" {
  tag @egm-test-scripts @chapter-10
  requires host.poll_sequence, decoder.lp7B

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # A machine that has just started is still reporting; only the first case of a run waits.
  host warm up 20s
  host poll sequence

  # A broadcast configures every machine on the line and none of them answers it, because several
  # answering at once would collide. So the check is that the line stays quiet and that the
  # machine is still there afterwards.
  host send sync
  host send gp draws nothing
}

# The ticket text polls. What they set only shows on the paper, so each case prints a ticket and reads the
# lines off it in the wallet. The text is ASCII in hexadecimal: each line is its data code, its length
# and its characters, and $7D takes a host identifier and an expiration before three lines in order.
test "10.3.11 extended ticket data is set" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == standard
  requires decoder.lp7C, stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  # TEST CASINO, 1 TEST ROAD and TESTVILLE as location and the two address lines: the reply says they were set.
  host send lp 7C data 25000B5445535420434153494E4F010B31205445535420524F414402095445535456494C4C45
  host expect field ticket_data_status of lp 7C == 1

  # Asked again with no lines, the flag the last reply carried is clear.
  host send lp 7C data 00
  host expect field ticket_data_status of lp 7C == 0

  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  egm expect ticket location == TEST CASINO
  egm expect ticket address1 == 1 TEST ROAD
  egm expect ticket address2 == TESTVILLE
  host expect exception cashout_ticket_printed within 5s

  note "The restricted and debit ticket titles ($10, $20) only appear on those tickets, which this case does not print."
}

test "10.3.12 extended ticket data addressed to everybody" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == standard
  requires decoder.lp7C, stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  # FLOOR WIDE, 9 SHARED WAY and EVERYTOWN to address 00: no machine answers, every machine sets them.
  host send lp 7C data 25000A464C4F4F522057494445010C39205348415245442057415902094556455259544F574E to 00

  # The machine that never answered says it was set when it is asked.
  host send lp 7C data 00
  host expect field ticket_data_status of lp 7C == 1

  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  egm expect ticket location == FLOOR WIDE
  egm expect ticket address1 == 9 SHARED WAY
  egm expect ticket address2 == EVERYTOWN
  host expect exception cashout_ticket_printed within 5s
}

test "10.3.13 ticket data is set" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == standard
  requires decoder.lp7D, stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  # Host 1, thirty days, then DATA CASINO, 2 DATA ROAD and DATAVILLE.
  host send lp 7D data 2501001E0B4441544120434153494E4F0B32204441544120524F4144094441544156494C4C45
  host expect field ticket_data_status of lp 7D == 1

  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  egm expect ticket location == DATA CASINO
  egm expect ticket address1 == 2 DATA ROAD
  egm expect ticket address2 == DATAVILLE
  host expect exception cashout_ticket_printed within 5s

  note "The expiration $7D carries is printed only where long poll 7B has set none; read it off the paper of a machine that prints one."
}

test "10.3.14 ticket data addressed to everybody" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == standard
  requires decoder.lp7D, stimulus.bill, stimulus.button.collect, printer.wallet

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  # Host 2, thirty days, then OLD CASINO, 3 OLD ROAD and OLDVILLE, to address 00.
  host send lp 7D data 2202001E0A4F4C4420434153494E4F0A33204F4C4420524F4144084F4C4456494C4C45 to 00

  egm bill insert 2000
  egm press collect
  egm expect ticket printed within 20s
  egm expect ticket location == OLD CASINO
  egm expect ticket address1 == 3 OLD ROAD
  egm expect ticket address2 == OLDVILLE
  host expect exception cashout_ticket_printed within 5s
}
