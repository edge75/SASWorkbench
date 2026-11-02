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
  requires egm.validation == secure_enhanced
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
  requires decoder.lp7B, stimulus.handpay, printer.wallet
  note "Set the handpay-receipt bit, read it back, then force a hand pay and confirm a receipt is printed rather than the machine waiting on an attendant alone."
}

test "10.3.5 hand pays and their receipts are validated" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires decoder.lp7B, stimulus.handpay, decoder.lp3E
  note "Set the validate-handpays bit, read it back, force a hand pay and check that the handpay-validated poll carries a number and an amount rather than zeros."
}

test "10.3.6 restricted tickets may be printed" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires decoder.lp7B, machine.restricted_credits
  note "Set the restricted-ticket bit, read it back, then cash out restricted credits and confirm a restricted ticket is printed. Restricted credits have to be put on the machine first, which needs a transfer this build cannot make."
}

test "10.3.7 tickets may be printed for foreign restricted amounts" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires decoder.lp7B, machine.restricted_credits
  note "Set the foreign-restricted bit, read it back, and confirm the machine prints for a restricted amount it did not itself issue."
}

test "10.3.8 ticket redemption is turned on by its own bit" {
  tag @egm-test-scripts @chapter-10
  requires egm.validation == secure_enhanced
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
  requires egm.validation == secure_enhanced
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

  host poll sequence

  # A broadcast configures every machine on the line and none of them answers it, because several
  # answering at once would collide. So the check is that the line stays quiet and that the
  # machine is still there afterwards.
  host send sync
  host send gp draws nothing
}

test "10.3.11 extended ticket data is set" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires decoder.lp7C
  note "Send the extended ticket data poll carrying the location and address lines a ticket is to be printed with, then print one and read the paper. The text only appears on the ticket, so the check is at the printer and not on the link."
}

test "10.3.12 extended ticket data addressed to everybody" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires decoder.lp7C
  note "The same poll sent to address 00 reaches every machine and is answered by none. Confirm the text reaches the paper of a machine that never acknowledged the poll."
}

test "10.3.13 ticket data is set" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires decoder.lp7D
  note "Send the ticket data poll and print a ticket carrying what it set. Read the paper for the values."
}

test "10.3.14 ticket data addressed to everybody" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires decoder.lp7D
  note "The broadcast form, answered by nobody and acted on by everybody. Read the paper of a machine that never acknowledged it."
}
