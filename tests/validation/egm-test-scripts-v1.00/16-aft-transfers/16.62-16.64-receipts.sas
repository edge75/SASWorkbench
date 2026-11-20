# Chapter 16, sections 16.62 to 16.64 - the receipt a transfer can print.
#
# A host can ask for a transaction receipt and supply some of what goes on it. The machine then
# reports the receipt's progress alongside the transfer, prints only what it was given plus what it
# knows itself, and refuses the transfer outright when it has too little to print a receipt or the
# kind of transfer does not allow one. Every refusal is a status code the host reads back.
#
# Everything here needs a machine performing transfers that says it can print receipts, in bit 0 of
# its $74 status (machine.aft_receipts); the receipt itself is read by a person.

test "16.62 the receipt status while a receipt prints" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.aft_receipts, stimulus.asset_number, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm settle
  host forget exceptions within 2s

  # Name, account and balance as receipt data: pending with the receipt pending.
  host send lp 72 data 4E0000000000000500000000000000000000008039300000000000000000000000000000000000000000000001310000000000001810094A6F6520536D697468110434323432130500002395000000
  host expect field transfer_status == $40
  host expect field receipt_status == $40
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field receipt_status <= $20

  # Once the receipt is out it reads printed.
  host wait 3s
  host send lp 72 data 02FF00
  host expect field transfer_status == $00
  host expect field receipt_status == $00

  note "With credits on the machine, ask for a full cashable transfer with a receipt and the patron's name, balance and account number, then read the status over and over until it is final and once more."
  note "The transfer is pending ($40) and $69 follows. Until the receipt is out its status is printing ($20) or pending ($40); once it is out, printed ($00)."
}

# @finding: refused at once with $82 and no $69. The library refuses a transfer to a ticket of
# nothing outright (lp.cpp:5356), after refusing any transfer to a ticket on a machine that offers
# none (lp.cpp:5345); the document expects it pending first.
test "16.63 an empty transfer to a ticket is refused" {
  tag @egm-test-scripts @chapter-16 @needs-a-person @finding
  requires decoder.lp72, machine.aft
  requires machine.aft_receipts, printer.wallet, stimulus.asset_number, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  egm settle
  host forget exceptions within 2s

  host send lp 72 data 3C000020000000000000000000000000000000803930000000000000000000000000000000000000000000000131000000000000061104343234320000
  host expect field transfer_status == $40
  host expect exception aft_transfer_complete within 15s
  host send lp 72 data 02FF00
  host expect field transfer_status >= $80

  note "Ask for a full transfer to a ticket with every amount zero and a receipt wanted. It is accepted as pending and $69 follows, but no ticket is printed, and the status read afterwards shows a failure, most likely $82 or $92."
}

test "16.64 what a receipt carries depends on what the host sent" {
  tag @egm-test-scripts @chapter-16 @needs-a-person
  requires decoder.lp72, machine.aft
  requires machine.aft_receipts, decoder.lp73, decoder.lp4C, printer.wallet
  requires stimulus.asset_number, stimulus.settle

  host connect loopback address 01
  host poll every 200ms
  host expect link up within 30s
  egm set asset number 12345
  host send lp 4C with validation_id=1 sequence_number=1 when egm.validation == secure_enhanced
  egm settle
  host forget exceptions within 2s

  # The refusals the receipt data alone decides. A house transfer in with only a target: $91.
  host send lp 72 data 3C000000000000050000000000000000000000803930000000000000000000000000000000000000000000000131000000000000060004484F53540000
  host expect field transfer_status == $91
  host send lp 72 data 02FF00
  host expect field receipt_status == $FF

  # A transfer to the host with only a name: $91.
  host send lp 72 data 410000800000000500000000000000000000008039300000000000000000000000000000000000000000000001320000000000000B10094A6F6520536D6974680000
  host expect field transfer_status == $91

  # A bonus with receipt data: $92.
  host send lp 72 data 3C000010000000050000000000000000000000803930000000000000000000000000000000000000000000000133000000000000061104343234320000
  host expect field transfer_status == $92

  # Registered, a debit transfer with only a target: $91.
  host send lp 73 data 1D0039300000907856341200000000000000000000000000000045230100
  host send lp 73 data 1D0139300000214365870900000000000000000000000000000021430500
  host expect field registration_status == $01
  host send lp 72 data 3C000040000000050000000000000000000000803930000021436587090000000000000000000000000000000134000000000000060004484F53540000
  host expect field transfer_status == $91

  note "Each transfer is followed by a second without a receipt flag, which only adds credit and prints nothing. Under secure enhanced validation, set the validation identity first."
  note "House transfers into the machine: a transfer-target field alone is too little and is refused with $91. With an account number the receipt shows source, the machine's date and the account; with a date the receipt uses it instead of the machine's; with a name it shows the name; with name, account, balance, date and target together it shows them all and a balance less what was moved. Debit fields sent with a house transfer are ignored: only the date and the patron account are printed."
  note "Debit transfers, after registering the machine: a target alone is refused with $91; a debit account is printed masked to its last four digits, with a transaction fee and a total debit where given (total = amount plus fee unless a total is supplied). A name sent with a debit transfer is not printed."
  note "Transfers to the host: a name alone is refused with $91; name, account and balance print with the balance less the amount. Transfers to a ticket follow the same rules as house transfers into the machine. Debit transfers to a ticket with no debit account are refused with $91, and print masked account, fee and total otherwise."
  note "Bonus and bonus jackpot transfers with receipt data are refused with $92. Wins paid to the host follow the house rules, with a balance increased by the amount."
  note "A transfer the machine refuses reports receipt status $FF in the status read."
}
