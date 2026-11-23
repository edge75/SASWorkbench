# Chapter 10, sections 10.1 and 10.2 - which validation style the machine performs, and the
# standard style in full.
#
# A machine performs exactly one validation style at a time, and which one it is decides almost
# everything else in this chapter: standard prints a ticket the machine numbered itself, secure
# enhanced has the host hand out the number, and system has the machine ask for one per ticket.
# The style lives in memory the host cannot write, so every case here that needs a style other
# than the one the machine is in declares it and skips when it is not.
#
# Two things about this chapter's numbering are worth stating once. These documents are revision
# 1.00, and the corpus under tests/conformance/ is the same suite at revision 1.2, where the
# sections moved: what 1.2 calls 10.1 is 10.2 here, and its 10.2 is 10.5 here. The path carries
# the revision for exactly that reason (D39).

# The style is chosen at the machine, and the machine remembers which one it was told.
test "10.1.1 the validation style is selected at the machine and remembered" {
  tag @egm-test-scripts @chapter-10 @needs-a-person
  requires machine.ticket_configuration

  note "At the machine's own ticket configuration panel, select each validation style the machine offers in turn, saving and leaving the panel between each, and check on re-entry that the panel shows the style that was chosen. A machine performs one style at a time, so this is the panel agreeing with itself rather than anything on the link."
  note "No poll can read this back and no poll can set it, which is why the case needs a person at the panel rather than a host."
}

# What the machine says it is, when it is doing secure enhanced validation.
test "10.1.2 the enabled features reply reports secure enhanced validation" {
  tag @egm-test-scripts @chapter-10
  machine validation secure_enhanced
  requires decoder.lpA0

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  host send lp A0 data 0000
  host expect field validation_style of lp A0 == 2
}

# ---------------------------------------------------------------------------------------------
# 10.2 - standard validation. The machine numbers its own tickets and tells the host what it
# printed; no host permission is asked for and none is given.

# With no redemption configured, a ticket offered to the machine is not taken.
test "10.2.1 a machine that redeems no tickets refuses one" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == standard
  requires stimulus.ticket, machine.redemption_disabled

  note "Configure the machine so that ticket redemption is off, then offer it a valid ticket. It does not take it, no credits are added and no transfer-complete exception is raised."
  note "This build's software machine has no switch for redemption on its own - the validation style decides it - so the configuration half is declared rather than performed."
}

# The poll itself, asked of a machine that has printed nothing. It runs ahead of 10.2.2 on purpose:
# the poll answers with the last ticket printed, so after 10.2.2 in the same run it would read that
# ticket back instead of zeros.
test "10.2.5 the cashout ticket information poll answers before anything is printed" {
  tag @egm-test-scripts @chapter-10
  requires egm.validation == standard
  requires decoder.lp3D

  note "The zeros hold only on a memory that has printed no ticket since it was cleared or restored. A machine with a printing history answers with its last ticket, and this case is then red for that reason."

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # Nothing has been printed, so the machine answers with a number of zero rather than refusing
  # the poll or staying quiet. Zero here means "no ticket", which is a statement and not an
  # absence - and it is the same reply shape the poll gives after a print.
  host send lp 3D
  host expect field validation_number of lp 3D == 0
  host expect field ticket_amount of lp 3D == 0
}

# The exception that says a ticket came out, and the poll that says what was on it.
test "10.2.2 a printed ticket raises its exception and can be read back" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus
  requires egm.validation == standard
  requires stimulus.bill, stimulus.button.collect, printer.wallet
  requires decoder.lp3D

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  host forget exceptions within 2s

  egm bill insert 2000
  host expect meter total_bill_in += 20 within 5s

  egm press collect
  egm expect ticket printed within 20s
  host expect exception cashout_ticket_printed within 5s

  # The number and the amount the machine put on the paper, read off the link and checked against
  # the ticket itself rather than against a constant.
  host send lp 3D
  host expect field validation_number of lp 3D != 0
  host expect field validation_number of lp 3D == ticket.validation_number
  host expect field ticket_amount of lp 3D == ticket.amount_cents
}

# The same ticket, asked for by a host that owns no validation. Left failing on purpose: this is
# the finding the conformance tree already carries at revision 1.2's numbering.
test "10.2.3 a non-validating host must not be able to read the ticket" {
  tag @egm-test-scripts @chapter-10 @needs-stimulus @channel2 @finding
  requires egm.validation == standard
  requires stimulus.bill, stimulus.button.collect, printer.wallet
  requires decoder.lp3D

  note "The same case is written out in full at tests/conformance/10.1/, against the same machine, where it is numbered 10.1.4 because that tree is revision 1.2 of this suite. It fails there on purpose and for a reason that is a property of the machine library rather than of either tree: the cashout ticket information poll carries no channel gate at all, so a host that owns no validation is answered with the real number and the real amount where this document asks to be ignored or answered with zeros."
  note "Not duplicated here. Two copies of one finding would have to be kept red together, and the day the library gates that poll somebody would find only one of them."
}

# Standard validation does not answer the enhanced-validation commands.
test "10.2.6 a standard machine does not perform enhanced validation" {
  tag @egm-test-scripts @chapter-10
  requires egm.validation == standard
  requires decoder.lp4D

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s

  # The enhanced validation information poll asks for a record this machine does not keep, so it
  # is not answered. Silence rather than a refusal: the machine has not understood the command in
  # the state it is in, and a refusal would say it understood and declined.
  host expect no reply to lp 4D data 00
}
