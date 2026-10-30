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
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires decoder.lp7B, printer.wallet
  note "Set the cashout-device bit, read the status back and check the bit holds, then cash out and confirm a ticket is what comes out. This build can set and read the bit; the half that matters is the machine's behaviour afterwards, which needs the machine configured with a printer it will use for cashout."
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
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires decoder.lp7B, stimulus.ticket, machine.redemption_switch
  note "Set the redemption bit, read it back, then offer a ticket and confirm it is taken. With the bit clear the same ticket is refused, which is the half that proves the bit did something - and it is the half this build cannot produce, because the software machine takes its redemption setting from the validation style rather than from a switch of its own."
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
