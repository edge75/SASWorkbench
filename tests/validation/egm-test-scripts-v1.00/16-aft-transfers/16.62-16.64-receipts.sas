# Chapter 16, sections 16.62 to 16.64 - the receipt a transfer can print.
#
# A host can ask for a transaction receipt and supply some of what goes on it. The machine then
# reports the receipt's progress alongside the transfer, prints only what it was given plus what it
# knows itself, and refuses the transfer outright when it has too little to print a receipt or the
# kind of transfer does not allow one. Every refusal is a status code the host must be able to
# read, which this build cannot.
#
# Everything here needs the funds-transfer command decoded, a machine performing transfers and a
# receipt printer; the receipt itself is read by a person.

test "16.62 the receipt status while a receipt prints" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.aft_receipts

  note "With credits on the machine, ask for a full cashable transfer with a receipt and the patron's name, balance and account number, then read the status over and over until it is final and once more."
  note "The transfer is pending ($40) and $69 follows. Until the receipt is out its status is printing ($20) or pending ($40); once it is out, printed ($00)."
}

test "16.63 an empty transfer to a ticket is refused" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.aft_receipts, printer.wallet

  note "Ask for a full transfer to a ticket with every amount zero and a receipt wanted. It is accepted as pending and $69 follows, but no ticket is printed, and the status read afterwards shows a failure, most likely $82 or $92."
}

test "16.64 what a receipt carries depends on what the host sent" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.aft_receipts, decoder.lp73, decoder.lp4C, printer.wallet

  note "Each transfer is followed by a second without a receipt flag, which only adds credit and prints nothing. Under secure enhanced validation, set the validation identity first."
  note "House transfers into the machine: a transfer-target field alone is too little and is refused with $91. With an account number the receipt shows source, the machine's date and the account; with a date the receipt uses it instead of the machine's; with a name it shows the name; with name, account, balance, date and target together it shows them all and a balance less what was moved. Debit fields sent with a house transfer are ignored: only the date and the patron account are printed."
  note "Debit transfers, after registering the machine: a target alone is refused with $91; a debit account is printed masked to its last four digits, with a transaction fee and a total debit where given (total = amount plus fee unless a total is supplied). A name sent with a debit transfer is not printed."
  note "Transfers to the host: a name alone is refused with $91; name, account and balance print with the balance less the amount. Transfers to a ticket follow the same rules as house transfers into the machine. Debit transfers to a ticket with no debit account are refused with $91, and print masked account, fee and total otherwise."
  note "Bonus and bonus jackpot transfers with receipt data are refused with $92. Wins paid to the host follow the house rules, with a balance increased by the amount."
  note "A transfer the machine refuses reports receipt status $FF in the status read."
}
