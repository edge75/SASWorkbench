# Chapter 16, sections 16.48 to 16.50 - every kind of transfer, the meters it moves, and a cancel.
#
# The broadest case in the chapter: each direction and each credit type is moved once, and the
# electronic transfer meters that belong to it must move by exactly that much - counts by one,
# credit meters in accounting units, cents meters in cents. The cancel case is the opposite end:
# a transfer withdrawn straight after it was asked for either stops or finishes, and the machine
# has to say which.
#
# Nothing here can run in this build. The funds-transfer command has no layout, so neither the
# request nor the status it returns can be read field by field, and the software machine performs
# no transfers at all.

test "16.48 each transfer type moves its own meters" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires decoder.lp73, decoder.lp6F, host.compare_replies, host.meter_arithmetic, stimulus.play, machine.restricted_credits, machine.credit_limit

  note "Before each transfer, remember the count, credit and cents meters of the kind being moved; after it, each count is up by one, each credit meter by the amount in accounting units, and each cents meter by the amount in cents."
  note "Into the machine from the house account: cashable, restricted and non-restricted alone and in every combination, each moving 0017 plus the type's own meters (002E/00A0/00A1, 002F/00A2/00A3, 0030/00A4/00A5)."
  note "To a ticket and from a debit account: restricted to ticket moves 0016, 0017, 00AA, 00AB, 002D and 002F; debit to the machine moves 0017, 0031, 00A6, 00A7; debit to ticket moves 0017, 0031, 00AC, 00AD. The debit kinds need the machine registered first, and the ticket kinds need a printer and secure enhanced or system validation."
  note "Out of the machine to the host: cashable, restricted and non-restricted alone and combined, moving 0018 plus 0032/00B8/00B9, 0033/00BA/00BB and 0034/00BC/00BD. The same again as a win paid to the host after a win over a low credit limit, where the manufacturer has to say how restricted and non-restricted wins are produced."
  note "Restricted credits put on the machine raise 001B; playing them lowers 001B and raises 0019 by the wager. Non-restricted credits played raise 001A by the wager."
}

test "16.49 the transfer meters" {
  tag @egm-test-scripts @chapter-16 @needs-stimulus @needs-a-person
  requires decoder.lp72, machine.aft
  requires decoder.lp6F, host.meter_arithmetic

  note "The document covers these meters through the transfers of 16.48 rather than with steps of its own; a pass of 16.48 is a pass here."
}

test "16.50 a transfer cancelled straight after it was asked for" {
  tag @egm-test-scripts @chapter-16
  requires decoder.lp72, machine.aft
  requires host.poll_sequence

  note "Ask for a full cashable transfer into the machine with a receipt wanted, and send the cancel form of the same command with nothing in between."
  note "If the machine stopped the transfer, its status says it was cancelled ($80); if it was too late, the status is the one the transfer had reached, complete ($00) or pending ($40). Either is a pass so long as it is the truth."
}
